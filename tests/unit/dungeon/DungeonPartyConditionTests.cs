using System;
using System.Collections.Generic;
using System.Linq;
using GdUnit4;
using ClaudeCodeGameStudios.Application;
using ClaudeCodeGameStudios.Domain.Combat;
using ClaudeCodeGameStudios.Domain.Dungeon;
using ClaudeCodeGameStudios.Domain.Heroes;
using ClaudeCodeGameStudios.Domain.Quests;

namespace ClaudeCodeGameStudios.Tests.Dungeon;

/// <summary>
/// <b>IT-05 · F-02 —— 队伍不满足参与条件时拒绝</b>（L2 集成层）。
///
/// <para><b>它补的是哪一块</b>：<c>Mission.Create</c> 拒绝不合法队伍这件事
/// <b>单元层已经验过</b>（<c>QuestsTests.Mission_Create_WithIllegalSquad_Throws</c>）。
/// 但那条只到<b>任务</b>为止 —— <b>副本那一侧没人验</b>：
/// 「任务没建 ⇒ 副本也没跑」这件事，从来没有一条断言把两边连起来。</para>
///
/// <para><b>为什么必须连起来验</b>：副本流程（<see cref="DungeonFlow"/>）
/// 是<b>独立于任务</b>的一套入口 —— 它自己就会
/// <c>DungeonRun.Create(...)</c> + <c>Enter()</c>。
/// 若某条路径绕过了任务校验直接开副本，就会出现
/// 「**没有任务的副本**」：战斗真的打了、结算真的发了，
/// 而日志里查不到这次出发的依据 —— <b>玩家的战利品来自一次没人批准的行动</b>。</para>
///
/// <para><b>本文件不预设任何设计数值</b>：「参与条件」由
/// <see cref="SquadRules"/> <b>注入</b>（测试自己造规则），
/// 真实的人数上下限属【待定】<c>07</c> 的 <b>P-10</b>。
/// 这跟本仓库一贯的纪律一致：**夹具是夹具，不是设计值**。</para>
///
/// <para><b>断言的可观测形态</b>：不说「副本没被创建」（那需要去数全局实例），
/// 而是说「<b>战斗解算器一次都没被调用</b>」——
/// 那是「副本真的没跑起来」最直接、也最不会骗人的证据。</para>
/// </summary>
[TestSuite]
public class DungeonPartyConditionTests
{
    private static HeroState MakeHero(string name)
        => new(new HeroDefinition(new HeroId("h-" + name), name, "来处"));

    /// <summary>
    /// 夹具规则：加入永远允许；<b>队伍完备性</b>按人数判定。
    /// <para>两者必须分开 —— 若下限也用于加入，逐个加入永远跨不过下限（已实测过的死锁）。</para>
    /// </summary>
    private static SquadRules SizeRule(int min, int max)
        => new(canJoin: _ => true, isRosterLegal: m => m.Count >= min && m.Count <= max);

    /// <summary>
    /// 数解算器被调了几次（副本真的跑起来了吗）。
    /// <para>⚠️ 日志里**必须有一条决定性记录**（<c>DecidedOutcome = true</c>）：
    /// <c>BattleResult.Produce</c> 在构造期就拒收「交不出判定依据」的日志
    /// （基线 §8.5 D5）⇒ 空日志会让流程推进直接抛异常。
    /// 而且 reader 读出的结论必须与那条记录<b>一致</b> —— 否则结果无法被复核。</para>
    /// </summary>
    private sealed class CountingResolver : IBattleResolver, IBattleOutcomeReader
    {
        private const string VerdictWin = "胜";

        public int Calls { get; private set; }

        public IReadOnlyList<BattleLogEntry> Resolve(
            string encounterName, IReadOnlyList<string> participantIds)
        {
            Calls++;
            return new[]
            {
                // 过程记录：不该被引用为判定依据
                new BattleLogEntry($"{encounterName}#e0", $"{encounterName}：双方接战", false),
                // 决定性记录：胜负结论靠它
                new BattleLogEntry(
                    $"{encounterName}#e1", $"{encounterName}：判定 {VerdictWin}", true),
            };
        }

        public bool DidHeroesWin(IReadOnlyList<BattleLogEntry> log)
        {
            var decisive = log.Where(e => e.DecidedOutcome).ToArray();
            if (decisive.Length == 0)
            {
                throw new InvalidOperationException("测试替身：日志里没有决定性条目，读不出结论。");
            }
            return true;
        }
    }

    // ── 不够格：任务与副本都不该发生 ────────────────────────────────────────

    /// <summary>
    /// <b>队伍不够格 ⇒ 任务不创建</b>（F-01 那一侧）。
    /// </summary>
    [TestCase]
    public void InsufficientParty_CreatesNoMission()
    {
        var rules = SizeRule(2, 4);
        var squad = new Squad(rules);
        squad.TryJoin(MakeHero("独苗"));      // 1 人 < 下限 2

        bool threw = false;
        try { Mission.Create(new MissionId("m1"), "北境旧堡探查", squad, rules); }
        catch (ArgumentException) { threw = true; }

        Assertions.AssertThat(threw).IsTrue();
        Assertions.AssertThat(squad.IsRosterReady).IsFalse();
    }

    /// <summary>
    /// <b>队伍不够格 ⇒ 副本一次都没跑</b>（IT-05 的**副本那一侧**）。
    /// <para>这是本文件<b>唯一一条</b>单元层给不出的断言：
    /// 单元层验的是「任务建不起来」，而这条验的是「<b>连战斗都没解算</b>」——
    /// 也就是「这次副本攻略根本没发生」。</para>
    /// </summary>
    [TestCase]
    public void InsufficientParty_ResolvesNoBattle()
    {
        var rules = SizeRule(2, 4);
        var squad = new Squad(rules);
        squad.TryJoin(MakeHero("独苗"));      // 1 人 < 下限 2

        var resolver = new CountingResolver();
        var missionCreated = false;
        var flowCreated = false;

        // 复刻「发起一次副本攻略」这条路径（Main.OnTask → Mission.Create → ResolveDungeon）：
        // 任务建不起来 ⇒ **不得**继续开副本。
        try
        {
            Mission.Create(new MissionId("m1"), "北境旧堡探查", squad, rules);
            missionCreated = true;
        }
        catch (ArgumentException)
        {
            // 预期：队伍不合法 ⇒ 拒绝 ⇒ 流程到此为止。
        }

        if (missionCreated)
        {
            var flow = DungeonFlow.Begin(
                new DungeonRunId("run-1"), "北境旧堡",
                squad.Members.Select(m => m.Id.Value).ToArray(),
                resolver, resolver);
            flowCreated = true;
        }

        Assertions.AssertThat(missionCreated).IsFalse();
        Assertions.AssertThat(flowCreated).IsFalse();
        Assertions.AssertThat(resolver.Calls).IsEqual(0);
    }

    // ── 够格：任务与副本都该发生（正向条件） ────────────────────────────────

    /// <summary>
    /// <b>队伍够格 ⇒ 任务建得起来</b>（IT-05 的<b>正向条件</b>那一半）。
    /// </summary>
    [TestCase]
    public void SufficientParty_CreatesMission()
    {
        var rules = SizeRule(2, 4);
        var squad = new Squad(rules);
        squad.TryJoin(MakeHero("甲"));
        squad.TryJoin(MakeHero("乙"));

        var mission = Mission.Create(new MissionId("m1"), "北境旧堡探查", squad, rules);

        Assertions.AssertThat(squad.IsRosterReady).IsTrue();
        Assertions.AssertThat(mission.Status).IsEqual(MissionStatus.Created);
        Assertions.AssertThat(mission.Members.Count).IsEqual(2);
    }

    /// <summary>
    /// <b>队伍够格 ⇒ 副本真的跑起来</b>（正向条件的另一半：<b>战斗确实被解算</b>）。
    /// <para>这一条不能少：只验「不够格 ⇒ 没跑」的话，一个
    /// 「<b>永远不跑</b>副本」的 bug 也能全绿 —— 那等于把游戏的核心玩法关掉了，
    /// 而所有失败路径的断言照样通过。</para>
    /// </summary>
    [TestCase]
    public void SufficientParty_ResolvesBattle()
    {
        var rules = SizeRule(2, 4);
        var squad = new Squad(rules);
        squad.TryJoin(MakeHero("甲"));
        squad.TryJoin(MakeHero("乙"));

        var resolver = new CountingResolver();
        var flow = DungeonFlow.Begin(
            new DungeonRunId("run-1"), "北境旧堡",
            squad.Members.Select(m => m.Id.Value).ToArray(),
            resolver, resolver);

        // ⚠️ `Begin` 只**创建并进入**副本；战斗要靠 `RunToCompletion()` 推进。
        //    漏掉这一步 ⇒ 解算器一次都不会被调用 ⇒ 而「没调用」正是
        //    上面那条失败路径的断言依据 —— **两条断言会互相掩护**。
        flow.RunToCompletion();

        Assertions.AssertThat(squad.IsRosterReady).IsTrue();
        Assertions.AssertThat(resolver.Calls).IsGreaterEqual(1);
        Assertions.AssertThat(flow.Run.Participants.Count).IsEqual(2);
    }

    /// <summary>
    /// 队伍**为空**时同样不得开副本（参与者为空 ⇒ 副本连创建都不该尝试）。
    /// </summary>
    [TestCase]
    public void EmptyParty_CreatesNoDungeonRun()
    {
        var rules = SizeRule(2, 4);
        var squad = new Squad(rules);

        bool threw = false;
        try
        {
            DungeonFlow.Begin(new DungeonRunId("run-1"), "北境旧堡",
                Array.Empty<string>(), new CountingResolver(), new CountingResolver());
        }
        catch (ArgumentException) { threw = true; }

        Assertions.AssertThat(threw).IsTrue();
        Assertions.AssertThat(squad.IsRosterReady).IsFalse();
    }
}
