using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using GdUnit4;
using ClaudeCodeGameStudios.Domain.Heroes;
using ClaudeCodeGameStudios.Domain.Quests;

namespace ClaudeCodeGameStudios.Tests;

/// <summary>
/// <b>IT-03（A-11 决策链无玩家输入）</b>：玩家<b>不能</b>指定某个英雄的「当前所在活动」。
///
/// <para><b>它守的是什么</b>：英雄做什么由<b>决策链</b>（<c>DecisionFlow</c> 的 S1–S6）
/// 决定，依据是性格 / 特质 / 当前状态。如果玩家有一条命令能直接写「让某某去采集」，
/// 那性格就<b>不是</b>二极管了 —— 基线 §4.2 整条建立在这个前提上：
/// 「喜欢打副本的英雄同样会休闲、会采集，只是效率慢点」。
/// 玩家能指定 ⇒ 这个前提被架空。</para>
///
/// <para><b>为什么写成「审计」而不是「执行后报错」</b>：<c>02</c> §4.3 的 N1–N8 写得很清楚 ——
/// 那些命令的含义是「<b>命令集中不存在</b>」，不是「执行后报错」。
/// 所以检查的对象是<b>能力面</b>：<b>有没有</b>一条能写英雄当前活动的路。</para>
///
/// <para><b>三道防线</b>（少一道就留一个洞）：
/// <list type="number">
/// <item><b>结构</b>：<c>CurrentActivity</c> 的 setter 必须是私有的 —— 外部代码<b>根本不能</b>直接赋值。</item>
/// <item><b>源码审计</b>：<c>BeginAction</c>（唯一的「开始一项行动」入口）的调用点
/// 只允许出现在三处：<b>S6 决策流</b>、<b>P9 读档归位</b>、<b>自检夹具</b>。
/// 多一处，就是多一条玩家能走的后门。</item>
/// <item><b>行为</b>：玩家命令 O4（编队）/ O5（派遣）跑完之后，
/// 参战英雄的「当前所在活动」必须<b>一字未改</b>。</item>
/// </list></para>
///
/// <para><b>本文件不发明任何玩法数值</b>。</para>
/// </summary>
[TestSuite]
public class PlayerCommandAuditTests
{
    // ── 防线一：结构 ──────────────────────────────────────────────────

    /// <summary>
    /// IT-03 防线①：<c>HeroState.CurrentActivity</c> 的 setter <b>不得公开</b>。
    /// <para>公开的 setter = 一条后门：任何拿到 <c>HeroState</c> 的代码都能直接改英雄在干嘛，
    /// 决策链的权重计算就全成了摆设。</para>
    /// </summary>
    [TestCase]
    public void IT03_CurrentActivity_HasNoPublicSetter()
    {
        var prop = typeof(HeroState).GetProperty("CurrentActivity");

        Assertions.AssertThat(prop).IsNotNull();
        Assertions.AssertThat(prop!.GetSetMethod() is null || !prop.GetSetMethod()!.IsPublic).IsTrue();
    }

    /// <summary>
    /// IT-03 防线①的延伸：<c>HeroState</c> 不得有<b>其它</b>公开方法能写当前活动。
    /// <para>（<c>CurrentTarget</c> / <c>RemainingActionSeconds</c> 同理 —— 它们和
    /// <c>CurrentActivity</c> 是一套「行动态」，被拆开写就等于漏了半个后门。）</para>
    /// </summary>
    [TestCase]
    public void IT03_ActionState_HasNoPublicSetter()
    {
        var names = new[] { "CurrentActivity", "CurrentTarget", "RemainingActionSeconds" };
        var offenders = new List<string>();

        foreach (var name in names)
        {
            var prop = typeof(HeroState).GetProperty(name);
            if (prop is null)
            {
                offenders.Add($"{name} 属性不存在");
                continue;
            }
            var setter = prop.GetSetMethod();
            if (setter is not null && setter.IsPublic)
            {
                offenders.Add($"{name} 有公开 setter");
            }
        }

        Assertions.AssertThat(string.Join(" | ", offenders)).IsEmpty();
    }

    // ── 防线二：源码审计 ──────────────────────────────────────────────

    /// <summary>
    /// IT-03 防线②：<c>BeginAction</c> 的调用点<b>只允许</b>出现在三处 ——
    /// <list type="bullet">
    /// <item><b>S6 决策流</b>（<c>Domain/Heroes/DecisionFlow.cs</c>）—— 唯一合法的「开始一项行动」。</item>
    /// <item><b>P9 读档归位</b>（<c>Main.Save.cs</c>）—— 读档时恢复「存档那一刻他正在干嘛」，
    /// 这是<b>还原</b>不是<b>指定</b>。</item>
    /// <item><b>自检夹具</b>（<c>Main.SelfTest.cs</c>）—— 测试自己摆场景。</item>
    /// </list>
    /// <para><b>为什么这三处之外一处都不能有</b>：多一处，就多一条玩家能走的后门。
    /// 这条断言把「命令集中不存在 N1–N8」从一句设计约定变成了<b>会红的测试</b>。</para>
    /// </summary>
    [TestCase]
    public void IT03_BeginAction_IsOnlyCalledByTheDecisionFlowAndSaveRestore()
    {
        var allowed = new HashSet<string>(StringComparer.Ordinal)
        {
            "DecisionFlow.cs",     // S6：决策流开始行动
            "Main.Save.cs",        // P9：读档归位
            "Main.SelfTest.cs",    // 自检夹具
        };

        var offenders = new List<string>();
        foreach (var file in CsFiles(SrcDir()))
        {
            string name = Path.GetFileName(file);
            if (name == "HeroState.cs") continue;      // 它自己定义这个方法
            if (allowed.Contains(name)) continue;

            int lineNo = 0;
            foreach (var line in File.ReadAllLines(file))
            {
                lineNo++;
                if (!line.Contains(".BeginAction(")) continue;
                var trimmed = line.Trim();
                if (trimmed.StartsWith("//") || trimmed.StartsWith("///") || trimmed.StartsWith("*"))
                {
                    continue;   // 注释里提到不算调用
                }
                offenders.Add($"{name}:{lineNo}: {trimmed}");
            }
        }

        Assertions.AssertThat(string.Join(" | ", offenders)).IsEmpty();
    }

    /// <summary>
    /// IT-03 防线②的延伸：玩家命令入口（<c>Main.Ui.cs</c> 里的 <c>On*</c> 处理器）
    /// <b>不得</b>碰英雄的行动态。
    /// <para>这一条比上一条更贴近「玩家命令」这个概念：上一个守的是「谁能调
    /// <c>BeginAction</c>」，这一个守的是「界面层不许直接写行动态」——
    /// 万一将来加了个 <c>HeroState.SetActivity()</c> 之类的后门，界面层一用就红。</para>
    /// <para><b>为什么还要盯 <c>ClearTarget()</c></b>：setter 是私有的，
    /// 所以「直接赋值」<b>编译不过</b>（防线①的效果）；但行动态仍有<b>公开方法</b>
    /// 可以写 —— <c>ClearTarget()</c> 就是其中之一（应用层读完社交目标后释放它）。
    /// 界面层调它 = 玩家命令伸手进了英雄的「当前在跟谁做」——
    /// 那也是行动态的一部分。</para>
    /// </summary>
    [TestCase]
    public void IT03_PlayerCommandHandlers_DoNotTouchHeroActionState()
    {
        var path = Path.Combine(SrcDir(), "Main.Ui.cs");
        Assertions.AssertThat(File.Exists(path)).IsTrue();

        var forbidden = new[]
        {
            ".BeginAction(",
            ".CurrentActivity =",
            ".RemainingActionSeconds =",
            ".ClearTarget(",
        };
        var offenders = new List<string>();

        int lineNo = 0;
        foreach (var line in File.ReadAllLines(path))
        {
            lineNo++;
            var trimmed = line.Trim();
            if (trimmed.StartsWith("//") || trimmed.StartsWith("///") || trimmed.StartsWith("*"))
            {
                continue;
            }
            foreach (var f in forbidden)
            {
                if (line.Contains(f)) offenders.Add($"Main.Ui.cs:{lineNo}: {trimmed}");
            }
        }

        Assertions.AssertThat(string.Join(" | ", offenders)).IsEmpty();
    }

    // ── 防线三：行为 ──────────────────────────────────────────────────

    /// <summary>
    /// IT-03 防线③：玩家命令 <b>O4（编队）</b> 与 <b>O5（派遣）</b> 跑完之后，
    /// 参战英雄的「当前所在活动」必须<b>没被指定成任何新活动</b>。
    ///
    /// <para><b>这里有一处必须说清的区别</b>（第一版断言写成「一字未改」，结果被数据打脸）：
    /// 派遣会让英雄<b>释放</b>手上的活 —— <c>Mission.Start</c> → <c>SetOnMission()</c> →
    /// <c>ReleaseAction()</c>，把 <c>CurrentActivity</c> 置成 <c>None</c>。
    /// 这是<b>释放</b>，不是<b>指定</b>：英雄离队远征了，手上的活干不下去，
    /// 自然作废。IT-03 禁的是「玩家能<b>指定</b>英雄做什么」，
    /// 不是「英雄的活会被中止」。</para>
    ///
    /// <para>所以这里断言的是：<b>行动态要么保持原样、要么被干净地释放，
    /// 绝不会被改成另一个具体活动</b>。这才是「决策链无玩家输入」的准确形式 ——
    /// 否则这条断言会把「派他去做副本，结果他手上的采集作废了」这种
    /// 正常行为误判成违规。</para>
    /// </summary>
    [TestCase]
    public void IT03_SquadAndMissionCommands_DoNotSpecifyWhatHeroesAreDoing()
    {
        var hero = new HeroState(
            new HeroDefinition(new HeroId("h-1"), "甲", "来处",
                new Dictionary<string, double>(), new Dictionary<string, double>()));

        // 他本来在干活
        hero.BeginAction(new ActivityId("采集"), 10.0);
        string activityBefore = hero.CurrentActivity.Value;

        // ── O4：编队 ──────────────────────────────────────────────────
        var rules = new SquadRules(
            canJoin: _ => true,
            isRosterLegal: members => members.Count <= 4);
        var squad = new Squad(rules);
        bool joined = squad.TryJoin(hero);

        // ── O5：派遣 ──────────────────────────────────────────────────
        var mission = Mission.Create(new MissionId("m-1"), "一次副本", squad, rules);
        mission.Start(_ => false);   // 没人「在忙」⇒ 顺利出发

        Assertions.AssertThat(joined).IsTrue();
        Assertions.AssertThat(mission.Status).IsEqual(MissionStatus.Active);

        // 行动态：要么原样，要么被**干净地释放** —— 但绝不能是「另一个具体活动」
        string activityAfter = hero.CurrentActivity.Value;
        bool keptOriginal = activityAfter == activityBefore;
        bool released = activityAfter == ActivityId.None.Value;

        Assertions.AssertThat(keptOriginal || released).IsTrue();
        if (released)
        {
            // 释放要干净：不能留下「没有活动但还剩 10 秒」这种半截状态
            Assertions.AssertThat(hero.RemainingActionSeconds).IsEqual(0.0);
        }
        else
        {
            Assertions.AssertThat(hero.RemainingActionSeconds).IsEqual(10.0);
        }
    }

    // ── 工具 ──────────────────────────────────────────────────────────

    private static string SrcDir()
    {
        var dir = new DirectoryInfo(
            Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location)!);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "src");
            if (Directory.Exists(candidate)
                && File.Exists(Path.Combine(dir.FullName, "Claude-Code-Game-Studios.csproj")))
            {
                return candidate;
            }
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("找不到 src/ 目录");
    }

    private static IEnumerable<string> CsFiles(string dir) =>
        Directory.EnumerateFiles(dir, "*.cs", SearchOption.AllDirectories);
}
