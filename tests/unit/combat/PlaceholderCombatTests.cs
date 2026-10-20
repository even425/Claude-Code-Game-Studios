using System;
using System.Collections.Generic;
using System.Linq;
using GdUnit4;
using ClaudeCodeGameStudios.Application;
using ClaudeCodeGameStudios.Domain.Combat;
using ClaudeCodeGameStudios.Domain.Heroes;

namespace ClaudeCodeGameStudios.Tests.Combat;

/// <summary>
/// 【待定 T-004】占位战斗模块的测试。
///
/// <para><b>本文档保护三件事</b>，前两件是结构性的，第三件是对历史上那类
/// 「随机源坏了但没人发现」缺陷的防御：</para>
/// <list type="number">
/// <item><b>两个接缝自洽</b> —— <c>Resolve</c> 写进日志的胜负，
///       <c>DidHeroesWin</c> 必须能原样读回来。<c>DungeonFlow</c> 依赖这一点：
///       若两者矛盾，产出的 <c>BattleResult</c> 会「日志说赢、结论说输」，
///       而 <c>BattleResult</c> 只校验可复核性、<b>不校验真假</b>。</item>
/// <item><b>读不出就大声失败</b> —— 不回落成默认胜负
///       （<c>07</c> §2.1 第 4 条纪律）。</item>
/// <item><b>随机源没有偏</b> —— 见下方那条统计断言的说明。</item>
/// </list>
///
/// <para><b>本文档不规定任何战斗数值。</b>胜率属【待定】T-004；
/// 唯一的数是 <c>PlaceholderHeroWinRate</c>，它本身就是个占位符。</para>
/// </summary>
[TestSuite]
public class PlaceholderCombatTests
{
    private static readonly string[] Crew = { "甲", "乙", "丙" };

    private static PlaceholderCombat Combat(ulong seed) => new(new DecisionFlow.SeededRandom(seed));

    // ── 1 · 日志形状 ───────────────────────────────────────────────────────

    /// <summary>每场战斗都恰好产出一条决定性记录 —— 否则战斗不可复核（D5）。</summary>
    [TestCase]
    public void Resolve_ProducesExactlyOneDecidingEntry_SoTheBattleIsVerifiable()
    {
        // Arrange / Act —— 跨越多个种子，避免只测到一种走向
        for (ulong seed = 1; seed <= 64; seed++)
        {
            var combat = Combat(seed);

            // Act
            var log = combat.Resolve("遭遇一", Crew);

            // Assert
            var deciding = log.Where(e => e.DecidedOutcome).ToArray();
            if (deciding.Length != 1)
            {
                Assertions.AssertThat(
                        $"种子 {seed}：决定性记录 {deciding.Length} 条，应为 1 条。")
                    .IsEqual(string.Empty);
                return;
            }
        }

        Assertions.AssertThat(true).IsTrue();
    }

    /// <summary>条目标识按遭遇名派生，因此不同遭遇之间不会撞 ID。</summary>
    [TestCase]
    public void Resolve_DerivesEntryIdsFromTheEncounterName_SoStagesDoNotCollide()
    {
        // Arrange
        var combat = Combat(7);

        // Act
        var first = combat.Resolve("遭遇一", Crew);
        var second = combat.Resolve("遭遇二", Crew);

        // Assert —— 两次的 ID 集合不相交
        var a = first.Select(e => e.EntryId).ToArray();
        var b = second.Select(e => e.EntryId).ToArray();
        Assertions.AssertThat(a.Intersect(b).Any()).IsFalse();
    }

    // ── 2 · 两个接缝自洽 ───────────────────────────────────────────────────

    /// <summary>
    /// 写进日志的胜负，必须能被读回 —— 一次不落。
    /// </summary>
    /// <remarks>
    /// 这条是本测试类<b>最要紧的一条</b>。若哪天有人图省事，让
    /// <c>DidHeroesWin</c> 自己也掷一次随机数或重算一次公式，
    /// 两者就会各自决定胜负 —— 而 <c>BattleResult</c> 不会发现，
    /// 因为它只检查「引用了真实存在的条目」，不检查「结论和日志是否一致」。
    /// </remarks>
    [TestCase]
    public void DidHeroesWin_ReadsBackExactlyWhatResolveWrote_EveryTime()
    {
        // Arrange
        var problems = new System.Text.StringBuilder();
        int wins = 0;
        int losses = 0;

        // Act / Assert
        for (ulong seed = 1; seed <= 200; seed++)
        {
            var combat = Combat(seed);
            var log = combat.Resolve("遭遇一", Crew);

            bool reported = combat.DidHeroesWin(log);
            string deciding = log.First(e => e.DecidedOutcome).Summary;

            bool logSaysWin = deciding.Contains("【我方胜】", StringComparison.Ordinal);

            if (reported != logSaysWin)
            {
                problems.AppendLine(
                    $"种子 {seed}：日志说 {(logSaysWin ? "赢" : "输")}，读者报 {(reported ? "赢" : "输")}");
            }

            if (reported) wins++; else losses++;
        }

        // 顺带确认两种走向都出现过 —— 全赢或全输说明读回逻辑写死了
        if (wins == 0 || losses == 0)
        {
            problems.AppendLine($"只出现了 {(wins == 0 ? "败" : "胜")}：读回逻辑可能写死了一边。");
        }

        Assertions.AssertThat(problems.ToString()).IsEqual(string.Empty);
    }

    /// <summary>赢标记读成赢、败标记读成败。</summary>
    [TestCase]
    public void DidHeroesWin_MapsEachMarkerToItsOwnAnswer()
    {
        // Arrange
        var combat = Combat(1);

        var winLog = new[] { new BattleLogEntry("e1", "遭遇一：【我方胜】（占位判定）", true) };
        var lossLog = new[] { new BattleLogEntry("e1", "遭遇一：【敌方胜】（占位判定）", true) };

        // Act / Assert
        Assertions.AssertThat(combat.DidHeroesWin(winLog)).IsTrue();
        Assertions.AssertThat(combat.DidHeroesWin(lossLog)).IsFalse();
    }

    // ── 3 · 读不出就大声失败 ───────────────────────────────────────────────

    /// <summary>没有决定性记录 → 抛错，<b>不</b>默认算赢。</summary>
    [TestCase]
    public void DidHeroesWin_WithNoDecidingEntry_ThrowsRatherThanGuessing()
    {
        // Arrange
        var combat = Combat(1);
        var log = new[] { new BattleLogEntry("e0", "遭遇一：双方接战。", false) };

        // Act
        bool threw = false;
        try { combat.DidHeroesWin(log); }
        catch (InvalidOperationException) { threw = true; }

        // Assert
        Assertions.AssertThat(threw).IsTrue();
    }

    /// <summary>两条决定性记录 → 抛错（说明解算器与读者不是成对的）。</summary>
    [TestCase]
    public void DidHeroesWin_WithTwoDecidingEntries_Throws()
    {
        // Arrange
        var combat = Combat(1);
        var log = new[]
        {
            new BattleLogEntry("e1", "遭遇一：【我方胜】", true),
            new BattleLogEntry("e2", "遭遇一：【敌方胜】", true),
        };

        // Act
        bool threw = false;
        try { combat.DidHeroesWin(log); }
        catch (InvalidOperationException) { threw = true; }

        // Assert
        Assertions.AssertThat(threw).IsTrue();
    }

    /// <summary>日志里没有任何胜负标记（来自别的解算器）→ 抛错。</summary>
    [TestCase]
    public void DidHeroesWin_WithAForeignLog_ThrowsRatherThanGuessing()
    {
        // Arrange
        var combat = Combat(1);
        var log = new[] { new BattleLogEntry("e1", "阿黄击破守卫。", true) };

        // Act
        bool threw = false;
        try { combat.DidHeroesWin(log); }
        catch (InvalidOperationException) { threw = true; }

        // Assert
        Assertions.AssertThat(threw).IsTrue();
    }

    /// <summary>两个标记同时出现（日志被改坏）→ 抛错。</summary>
    [TestCase]
    public void DidHeroesWin_WithBothMarkersInOneEntry_Throws()
    {
        // Arrange
        var combat = Combat(1);
        var log = new[] { new BattleLogEntry("e1", "遭遇一：【我方胜】【敌方胜】", true) };

        // Act
        bool threw = false;
        try { combat.DidHeroesWin(log); }
        catch (InvalidOperationException) { threw = true; }

        // Assert
        Assertions.AssertThat(threw).IsTrue();
    }

    // ── 4 · 随机源没有偏 ───────────────────────────────────────────────────

    /// <summary>
    /// 胜率大致落在占位值附近 —— <b>随机源没有系统性偏差</b>。
    /// </summary>
    /// <remarks>
    /// <b>这条为什么必须存在</b>：本项目出过一次
    /// <c>(s &gt;&gt; 33) / (double)ulong.MaxValue</c> —— 一个 31 位的值除以
    /// 64 位的最大值，结果恒等于约 0，于是<b>每一次加权抽取都选中第一个候选</b>，
    /// 整套偏好系统静默失效。当时<b>确定性测试是全过的</b>（同样的种子确实给出同样的序列），
    /// 只有这条统计断言把它逮住。
    /// <para>
    /// 确定性测不出「恒定」，只有分布能测。所以它留在这里。
    /// </para>
    /// </remarks>
    [TestCase]
    public void Resolve_WinRateIsNotSystematicallySkewed()
    {
        // Arrange
        const int Trials = 400;
        int wins = 0;

        // Act
        for (ulong seed = 1; seed <= Trials; seed++)
        {
            var combat = Combat(seed);
            if (combat.DidHeroesWin(combat.Resolve("遭遇一", Crew))) wins++;
        }

        // Assert —— 占位胜率 0.65（D-052）；400 次抽样下 ±0.12 是极宽松的界
        // （期望 260 胜，σ ≈ 9.5，±0.12 相当于 ±3.2σ，不会偶发）
        double rate = (double)wins / Trials;
        Assertions.AssertThat(rate > 0.53 && rate < 0.77).IsTrue();
    }

    /// <summary>占位胜率必须是 D-052 确认的那个值，而不是一个看起来调过的数。</summary>
    /// <remarks>
    /// 这条测的是<b>纪律</b>而不是行为：<c>07</c> §2.1 第 1 条说占位符
    /// 「不得伪装成已定」。改这个数必须是<b>一次有记录的改动</b>（D-052 那样），
    /// 而不是谁为了让它好看就动一下。
    /// <para>
    /// <b>它仍然是占位</b>：战斗公式属 T-004，尚未拍板；
    /// 0.65 决定的是「副本要连赢几场才拿得到东西」这个经济节流阀，
    /// <b>不代表最终的战斗平衡</b>。
    /// </para>
    /// </remarks>
    [TestCase]
    public void PlaceholderWinRate_IsTheConfirmedValue_AndChangedDeliberately()
    {
        // Assert
        Assertions.AssertThat(PlaceholderCombat.PlaceholderHeroWinRate).IsEqual(0.65);
    }

    // ── 5 · 随机源适配器 ───────────────────────────────────────────────────

    /// <summary>返回值必须落在半开区间 <c>[0, bound)</c> 里。</summary>
    [TestCase]
    public void SystemRandomSource_NextDouble_StaysInsideTheHalfOpenInterval()
    {
        // Arrange
        var rng = new Random(20261018);
        var source = new SystemRandomSource(rng);

        // Act / Assert
        for (int i = 0; i < 2000; i++)
        {
            double v = source.NextDouble(3.5);
            if (v < 0.0 || v >= 3.5)
            {
                Assertions.AssertThat($"第 {i} 次返回 {v}，应落在 [0, 3.5)。").IsEqual(string.Empty);
                return;
            }
        }

        Assertions.AssertThat(true).IsTrue();
    }

    /// <summary>非正或非有限的上界必须被拒。</summary>
    [TestCase]
    public void SystemRandomSource_WithANonPositiveBound_Throws()
    {
        // Arrange
        var source = new SystemRandomSource(new Random(1));

        // Act / Assert
        foreach (double bound in new[] { 0.0, -1.0, double.NaN, double.PositiveInfinity })
        {
            bool threw = false;
            try { source.NextDouble(bound); }
            catch (ArgumentOutOfRangeException) { threw = true; }

            if (!threw)
            {
                Assertions.AssertThat($"上界 {bound} 未被拒。").IsEqual(string.Empty);
                return;
            }
        }

        Assertions.AssertThat(true).IsTrue();
    }

    /// <summary>随机源为 null 必须在构造期就被拒。</summary>
    [TestCase]
    public void Constructor_WithNullRandom_Throws()
    {
        // Act
        bool threw = false;
        try { new PlaceholderCombat(null); }
        catch (ArgumentNullException) { threw = true; }

        // Assert
        Assertions.AssertThat(threw).IsTrue();
    }
}
