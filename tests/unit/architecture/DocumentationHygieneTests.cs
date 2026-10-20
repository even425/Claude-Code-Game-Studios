using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GdUnit4;

namespace ClaudeCodeGameStudios.Tests;

/// <summary>
/// <b>文档卫生守卫</b>：规格与决策文档里不许出现控制字符。
///
/// <para><b>它守的是什么</b>：一次真实事故。往 <c>production/07</c> 写 Markdown 时用了
/// PowerShell 的<b>双引号 here-string</b>（<c>@"..."@</c>）—— 那是<b>可展开</b>字符串，
/// 里面的 <c>`</c> 仍然按转义处理：<c>`0</c> 变成 NUL、<c>`7</c> 变成 BEL。
/// 于是原文的 <c>`07`</c> 变成了「NUL + 7」，<b>整行被污染</b>，
/// 而肉眼看过去「好像也像那么回事」—— 只是少了一个字符、少了一个反引号。
/// 污染一共 14 处，散在 4 行里，<b>没有任何报错</b>。</para>
///
/// <para><b>为什么要写成测试</b>：那 14 处污染是<b>事后</b>靠「统计 <c>ord(c) &lt; 32</c>」
/// 发现的 —— 也就是说，<b>它能被机器发现，只是没人让机器去发现</b>。
/// 把它变成一条每次 <c>dotnet test</c> 都跑的断言，
/// 下次同样的事故会在<b>写完那一分钟</b>就红，而不是几小时之后。</para>
///
/// <para><b>判据</b>：除 <c>\r</c> / <c>\n</c> / <c>\t</c> 外，
/// Markdown 文件里不应出现任何 ASCII 控制字符。它们在 Markdown 里没有合法用途，
/// 却恰恰是「转义被吃掉」最典型的痕迹。</para>
/// </summary>
[TestSuite]
public class DocumentationHygieneTests
{
    private static string ProductionDir()
    {
        var dir = new DirectoryInfo(
            Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location)!);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "production");
            if (Directory.Exists(candidate)
                && File.Exists(Path.Combine(dir.FullName, "Claude-Code-Game-Studios.csproj")))
            {
                return candidate;
            }
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("找不到 production/ 目录");
    }

    /// <summary>
    /// 规格与决策文档里不许有控制字符（<c>\r</c> / <c>\n</c> / <c>\t</c> 除外），
    /// 也不许有 <b>U+FFFD 替换字符</b>。
    ///
    /// <para><b>为什么 U+FFFD 也要查</b>：U+FFFD 是「解码失败」的产物 ——
    /// 字节被吃掉或转义被展开后留下的残骸。它<b>不是控制字符</b>，
    /// 所以控制字符那条判据抓不到它 ⇒ 本守卫刚写出来时，
    /// <c>07</c> 里就躺着一处 <b>从第一个提交起</b>就有的污染
    /// （「但没有一<code>���</code>被游戏用到」），
    /// 它躺在文档里<b>没人看见</b>。
    /// <b>凡是「读出来是乱码」的地方，机器都能一眼看见 —— 只要有人让它去看。</b></para>
    /// </summary>
    [TestCase]
    public void ProductionDocs_ContainNoControlOrReplacementCharacters()
    {
        var offenders = new List<string>();

        foreach (var file in Directory.EnumerateFiles(ProductionDir(), "*.md", SearchOption.TopDirectoryOnly))
        {
            string text = File.ReadAllText(file);
            int lineNo = 0;
            foreach (var line in text.Replace("\r\n", "\n").Split('\n'))
            {
                lineNo++;
                var bad = line.Where(c => char.IsControl(c) && c != '\t').ToList();
                if (bad.Count > 0)
                {
                    offenders.Add($"{Path.GetFileName(file)}:{lineNo} → "
                                  + string.Join(",", bad.Select(c => $"U+{(int)c:X4}")));
                }

                int badChars = line.Count(c => c == '\uFFFD');
                if (badChars > 0)
                {
                    offenders.Add($"{Path.GetFileName(file)}:{lineNo} → "
                                  + $"{badChars} 个 U+FFFD（解码失败的残骸，"
                                  + $"位置 {line.IndexOf('\uFFFD')}）");
                }
            }
        }

        Assertions.AssertThat(string.Join(" | ", offenders)).IsEmpty();
    }

    /// <summary>
    /// <b>已判死的设计线</b>（2026-10-17 的 GDD，<c>07</c> D-002「完全作废」）的
    /// 审查报告留在 <c>production/</c>（现行文档目录）里 ⇒ 它们<b>必须</b>带
    /// 「审查对象已判死」的状态横幅。
    ///
    /// <para><b>为什么这是守卫而不是备注</b>：那批报告里列着 26 条交叉冲突、
    /// 一堆内容缺口与未决问题，读起来像**现行待办**；
    /// 而 D-002 明令<b>不得引用</b>那条线的公式、常数与实体定义。
    /// 没有横幅，下一个人可能照着已判死的公式去改代码 ——
    /// 那不是「多做了事」，是<b>按作废的规则实现</b>。</para>
    /// </summary>
    [TestCase]
    public void VoidedDesignLine_Audits_CarryTheStatusBanner()
    {
        const string marker = "【2026-10-21 补状态标记 · 审查对象已判死】";

        var expected = new[]
        {
            "cross-doc-consistency.md",
            "content-gap-audit.md",
            "experience-gap-audit.md",
            "design-review-findings.md",
            "open-questions-consolidated.md",
            "design-decisions.md",
            "terminology-sweep.md",
            "project-stage-report.md",
        };

        var missing = new List<string>();
        foreach (var name in expected)
        {
            var path = Path.Combine(ProductionDir(), name);
            if (!File.Exists(path)) { missing.Add($"{name}（文件不存在）"); continue; }
            if (!File.ReadAllText(path).Contains(marker)) missing.Add(name);
        }

        Assertions.AssertThat(string.Join(" | ", missing)).IsEmpty();
    }

    /// <summary>
    /// <b>任何</b>现行文档一旦提到 <c>design/gdd/</c>（那条已判死的设计线），
    /// 就必须<b>同时声明它已作废</b> —— 否则读者会以为它引用的东西仍然有效。
    ///
    /// <para><b>「声明作废」有两种合法写法</b>：
    /// ① 带完整状态横幅（那 8 份旧审查报告）；
    /// ② 就地写明它不构成基线的任何部分 / 不得引用
    /// （如 <c>05</c> §9、<c>07</c> D-002 的「效力」段）——
    /// 那种写法<b>正是我们想要的</b>，不该被守卫骂。</para>
    ///
    /// <para>所以本条只抓「<b>提了却没说它死</b>」的情形。</para>
    /// </summary>
    [TestCase]
    public void AnyProductionDoc_ReferencingTheVoidedGddLine_AlsoSaysItIsVoid()
    {
        const string marker = "【2026-10-21 补状态标记 · 审查对象已判死】";

        // 「就地声明作废」的几种说法
        string[] voidingPhrases =
        {
            "不构成当前基线的任何部分",
            "完全作废",
            "判死",
            "已归档",
            "已空",
            "不是现行待办",
        };

        var offenders = new List<string>();
        foreach (var file in Directory.EnumerateFiles(ProductionDir(), "*.md", SearchOption.TopDirectoryOnly))
        {
            string text = File.ReadAllText(file);
            if (!text.Contains("design/gdd/")) continue;
            if (text.Contains(marker)) continue;
            if (voidingPhrases.Any(p => text.Contains(p))) continue;
            offenders.Add(Path.GetFileName(file));
        }

        Assertions.AssertThat(string.Join(" | ", offenders)).IsEmpty();
    }
}
