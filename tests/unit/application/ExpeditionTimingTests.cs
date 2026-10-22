using System;
using System.Linq;
using GdUnit4;
using ClaudeCodeGameStudios.Application;
using ClaudeCodeGameStudios.Domain.Combat;

namespace ClaudeCodeGameStudios.Tests.Application;

/// <summary>
/// <b>副本在日历上占多久</b>（D-075）：行程 = 路程 + 战斗。
/// </summary>
/// <remarks>
/// <para><b>它守的是三件事</b>：
/// <list type="number">
/// <item><b>战斗耗时被加进总时长</b> —— 不是只有路程。</item>
/// <item><b>战斗耗时是真的</b>（由真实战斗的回合数算出来，不是编的数）。</item>
/// <item><b>非副本任务没有战斗</b>（采集任务的时长就是任务表那个数）。</item>
/// </list>
/// <para><b>为什么要「用真实战斗」来验</b>：若测试自己编一个战斗时长（比如直接传 5.0），
/// 那它测的是**测试的算术**，不是「战斗耗时真的会被加进去」这件事
/// ⇒ 战斗那一半即使断了，测试也照样绿。</para>
/// </remarks>
[TestSuite]
public class ExpeditionTimingTests
{
    /// <summary>
    /// 真打一场三阶段副本，取它的<b>真实</b>战斗时长。
    /// </summary>
    /// <param name="personality">参战者的性格。</param>
    /// <param name="diligence">参战者的投入度。</param>
    /// <returns>战斗占的游戏内小时。</returns>
    private static double RealBattleHours(string personality, string diligence)
    {
        var combat = new FormulaCombat();
        var stats = CombatParameters.ForHero(personality, diligence);
        var party = new CombatPartySnapshot(new[]
        {
            CombatantSnapshot.Create("h-1", "甲", stats: stats),
            CombatantSnapshot.Create("h-2", "乙", stats: stats),
        });

        var flow = DungeonFlow.Begin(
            new ClaudeCodeGameStudios.Domain.Dungeon.DungeonRunId("timing-run-1"),
            "北境旧堡探查",
            new[] { "h-1", "h-2" },
            combat, combat, party: party);

        var outcome = flow.RunToCompletion();
        return outcome.TotalGameHours;
    }

    /// <summary>
    /// <b>战斗耗时被加进总时长</b> —— 总时长必须**严格大于**路程。
    /// </summary>
    [TestCase]
    public void TotalHours_IncludesTheBattleTime()
    {
        double battle = RealBattleHours("莽", "肝");
        double total = ExpeditionTiming.TotalHours(travelHours: 11.0, battleHours: battle);

        Assertions.AssertThat(battle).IsGreaterEqual(0.000001);   // 战斗确实花了时间
        Assertions.AssertThat(total - 11.0).IsEqual(battle);
    }

    /// <summary>
    /// <b>战斗耗时与真实回合数对得上</b>（D-073 的换算 + D-075 的合并）。
    /// </summary>
    /// <remarks>
    /// <para>⚠️ 这是<b>端到端</b>的一环：真实战斗 ⇒ 真实回合数 ⇒ 真实小时数
    /// ⇒ 被加进总时长。任一环断了都会红。</para>
    /// <para>⚠️ <b>为什么期望值写死 0.25 而不引用 <c>StageDuration.GameHoursPerRound</c></b>：
    /// 引用它 ⇒ 改了那个常量，期望值<b>跟着一起变</b> ⇒ 断言成了<b>同义反复</b>
    /// （变异时「每回合小时数被改」那一档<b>照样绿</b>）。
    /// ⇒ 0.25 是 **D-073 拍板的值**，它<b>不该</b>随实现漂移 ⇒ 写死在这里，
    /// 让「实现与拍板值分叉」立刻暴露。</para>
    /// </remarks>
    [TestCase]
    public void BattleHours_MatchTheRealRoundsAndFlowIntoTheTotal()
    {
        var combat = new FormulaCombat();
        var stats = CombatParameters.ForHero("莽", "肝");
        var party = new CombatPartySnapshot(new[]
        {
            CombatantSnapshot.Create("h-1", "甲", stats: stats),
            CombatantSnapshot.Create("h-2", "乙", stats: stats),
        });

        var flow = DungeonFlow.Begin(
            new ClaudeCodeGameStudios.Domain.Dungeon.DungeonRunId("timing-run-2"),
            "北境旧堡探查",
            new[] { "h-1", "h-2" },
            combat, combat, party: party);

        var outcome = flow.RunToCompletion();

        // 战斗小时数 = 总回合数 × 每回合小时（D-073 拍板：0.25）。
        // ⚠️ 期望值**写死** 0.25 —— 引用常量会变成同义反复（见上面的说明）。
        const double approvedHoursPerRound = 0.25;
        double expected = outcome.TotalRoundsUsed * approvedHoursPerRound;
        Assertions.AssertThat(outcome.TotalGameHours).IsEqual(expected);

        // 且它真的进了总时长。
        double total = ExpeditionTiming.TotalHours(11.0, outcome.TotalGameHours);
        Assertions.AssertThat(total - 11.0).IsEqual(outcome.TotalGameHours);
    }

    /// <summary>非副本任务（采集）**没有战斗** ⇒ 时长就是路程。</summary>
    [TestCase]
    public void TotalHours_WithoutABattle_IsJustTheTravel()
    {
        double total = ExpeditionTiming.TotalHours(travelHours: 3.0, battleHours: 0.0);

        Assertions.AssertThat(total).IsEqual(3.0);
    }

    /// <summary>负的时长要被拒（不是悄悄当成 0）。</summary>
    [TestCase]
    public void TotalHours_WithNegativeInput_Throws()
    {
        bool threw = false;
        try { ExpeditionTiming.TotalHours(-1.0, 0.0); }
        catch (ArgumentOutOfRangeException) { threw = true; }

        Assertions.AssertThat(threw).IsTrue();
    }
}
