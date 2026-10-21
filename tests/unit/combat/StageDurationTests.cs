using System;
using System.Collections.Generic;
using System.Linq;
using GdUnit4;
using ClaudeCodeGameStudios.Domain.Combat;

namespace ClaudeCodeGameStudios.Tests.Combat;

/// <summary>
/// <b>阶段耗时</b>（D-073）：战斗回合 ↔ 游戏内时间的换算，与「回合数读回」。
/// </summary>
/// <remarks>
/// <para><b>它守的是三件事</b>：
/// <list type="number">
/// <item><b>回合数读得回来</b> —— 引擎写进决定性条目的标记，
///       <see cref="BattleLogReader"/> 能原样读出（<b>读不出就失败</b>）。</item>
/// <item><b>换算是纯函数且可预期</b> —— 回合数 × 每回合小时。</item>
/// <item><b>副本总耗时 = 各阶段之和</b>（不重不漏）。</item>
/// </list>
/// <para><b>为什么必须有它</b>：关系变化要能追溯到「一段具体经历」（基线 §9.5 R4），
/// 而那段经历必须有<b>时间坐标</b> ⇒ 回合数是那条坐标链的原始数据。
/// 没有这条守卫，引擎一旦不再写标记，耗时会静默变成 0 小时 ——
/// <b>而且外面看不到任何异常</b>（这正是「读不出就抛」要防的）。</para>
/// </remarks>
[TestSuite]
public class StageDurationTests
{
    /// <summary>一条带回合标记的决定性条目。</summary>
    private static BattleLogEntry Deciding(int rounds) =>
        new("遭遇一#e1", $"遭遇一：战斗结束{BattleOutcomeMarkers.RoundsMarker(rounds)} ⇒ 【我方胜】", true);

    // ── 读回 ──────────────────────────────────────────────────────────────

    /// <summary>引擎写的回合标记，读回来是同一个数。</summary>
    [TestCase]
    public void RoundsUsed_ReadsBackWhatTheEngineWrote()
    {
        var log = new[]
        {
            new BattleLogEntry("遭遇一#e0", "遭遇一：双方接战", false),
            Deciding(17),
        };

        Assertions.AssertThat(BattleLogReader.RoundsUsed(log)).IsEqual(17);
    }

    /// <summary>标记在决定性条目<b>中间</b>也能读到（不被前后文干扰）。</summary>
    [TestCase]
    public void RoundsUsed_ReadsTheMarkerWhereverItSits()
    {
        var log = new[]
        {
            new BattleLogEntry(
                "遭遇一#e1",
                $"遭遇一：甲 击倒了对手{BattleOutcomeMarkers.RoundsMarker(9)} ⇒ 【我方胜】",
                true),
        };

        Assertions.AssertThat(BattleLogReader.RoundsUsed(log)).IsEqual(9);
    }

    /// <summary>
    /// <b>读不出就失败</b> —— 绝不悄悄当成 0 回合。
    /// </summary>
    /// <remarks>
    /// ⚠️ 这是本类<b>最要紧的一条</b>：若读不出时返回 0，
    /// 副本耗时会算成 0 小时 ⇒ 关系变化的时间坐标整个塌掉，
    /// 而<b>没有任何异常</b>提示（`07` §2.1 第 4 条纪律）。
    /// </remarks>
    [TestCase]
    public void RoundsUsed_WithoutTheMarker_ThrowsRatherThanReportingZero()
    {
        var log = new[]
        {
            new BattleLogEntry("遭遇一#e1", "遭遇一：战斗结束 ⇒ 【我方胜】", true),
        };

        bool threw = false;
        try { BattleLogReader.RoundsUsed(log); }
        catch (InvalidOperationException) { threw = true; }

        Assertions.AssertThat(threw).IsTrue();
    }

    /// <summary>没有决定性条目 ⇒ 也失败（读不出就失败，不是读出 0）。</summary>
    [TestCase]
    public void RoundsUsed_WithoutADecidingEntry_Throws()
    {
        var log = new[]
        {
            new BattleLogEntry("遭遇一#e0", "遭遇一：双方接战", false),
        };

        bool threw = false;
        try { BattleLogReader.RoundsUsed(log); }
        catch (InvalidOperationException) { threw = true; }

        Assertions.AssertThat(threw).IsTrue();
    }

    // ── 换算 ──────────────────────────────────────────────────────────────

    /// <summary>一场战斗的耗时 = 回合数 × 每回合小时。</summary>
    [TestCase]
    public void GameHoursFor_MultipliesRoundsByThePerRoundHours()
    {
        Assertions.AssertThat(StageDuration.GameHoursFor(1))
            .IsEqual(StageDuration.GameHoursPerRound);
        Assertions.AssertThat(StageDuration.GameHoursFor(12))
            .IsEqual(12 * StageDuration.GameHoursPerRound);
    }

    /// <summary>回合数必须 ≥ 1 —— 0 回合的战斗不存在。</summary>
    [TestCase]
    public void GameHoursFor_WithNoRounds_Throws()
    {
        bool threw = false;
        try { StageDuration.GameHoursFor(0); }
        catch (ArgumentOutOfRangeException) { threw = true; }

        Assertions.AssertThat(threw).IsTrue();
    }

    /// <summary>副本总耗时 = 各阶段耗时之和（不重不漏）。</summary>
    [TestCase]
    public void GameHoursForStages_SumsEveryStage()
    {
        double total = StageDuration.GameHoursForStages(new[] { 10, 20, 30 });

        Assertions.AssertThat(total)
            .IsEqual(StageDuration.GameHoursFor(10)
                    + StageDuration.GameHoursFor(20)
                    + StageDuration.GameHoursFor(30));
    }

    /// <summary>
    /// <b>量级对齐</b>：三阶段满回合（各 30）≈ 一天 —— 与副本「跑一整天」的量级相符。
    /// </summary>
    /// <remarks>
    /// <b>为什么这条要写死</b>：每回合小时数是 D-073 拍板的数，
    /// 而它<b>唯一的理由</b>就是「一个副本约一天」⇒ 换算若被改大改小，
    /// 这个理由就不成立了 ⇒ 必须有一条断言盯着量级。
    /// </remarks>
    [TestCase]
    public void AFullRoundsDungeon_TakesAboutADay()
    {
        double hours = StageDuration.GameHoursForStages(
            Enumerable.Repeat(CombatParameters.RoundLimit, DungeonStageCount).ToArray());

        // 22.5 ≤ 24（一天）且 ≥ 12（半天）⇒ 量级正确。
        Assertions.AssertThat(hours >= 12.0).IsTrue();
        Assertions.AssertThat(hours <= 24.0).IsTrue();
    }

    /// <summary>副本的阶段数（本类用来说明「几段副本 = 几天」）。</summary>
    private const int DungeonStageCount = 3;
}
