using System;
using System.Collections.Generic;
using GdUnit4;
using ClaudeCodeGameStudios.Domain.Combat;

/// <summary>
/// <see cref="BattleResult"/> 的单元测试：<b>可复核性</b>。
///
/// <para><b>本文档保护：</b></para>
/// <ul>
/// <li><b>基线 §8.5 验收项 D5</b>：战斗日志必须能复现胜负判定所引用的每一个关键事件</li>
/// <li>引用必须<b>存在</b>、<b>互不重复</b>，且<b>整体替换</b>而非叠加</li>
/// <li>构造期就拒绝「日志里没有任何关键事件」的战斗 —— 不把 bug 留到运行期</li>
/// </ul>
///
/// <para>
/// <b>本文档不规定任何战斗数值。</b>伤害 / 命中 / 冷却 / 状态效果 / 目标选择
/// 全部属【待定】<c>07</c> 的 <b>P-07 / T-004</b>；这里只测<b>结构</b>与<b>可复核性</b>，
/// 一个数值都不定。
/// </para>
///
/// <para>
/// 命名遵循 <c>.claude/rules/test-standards.md</c>：<c>[Scenario]_[Expected]</c>。
/// API 面读自 GdUnit4Api 5.1.0-rc5：<c>[TestSuite]</c> 在类上、<c>[TestCase]</c> 在方法上、
/// <c>Assertions.AssertThat(...)</c>，无裸 <c>Assert</c>、无 <c>AddMessage</c> 重载。
/// </para>
/// </summary>
[TestSuite]
public class BattleResultTests
{
    /// <summary>参与者只是标识，不参与任何判定。</summary>
    private static readonly string[] Party = { "甲", "乙", "丙" };

    /// <summary>一份日志：一条关键事件 + 一条过程记录。</summary>
    private static BattleResult VerifiableLogBattle(string id = "br1")
        => BattleResult.Produce(
            new BattleResultId(id), "遭遇", heroesWon: true, Party,
            new[]
            {
                new BattleLogEntry("e1", "阿黄击破守卫", DecidedOutcome: true),
                new BattleLogEntry("e2", "队伍整理阵型", DecidedOutcome: false),
            });

    // ── 正常路径 · D5 ──────────────────────────────────────────────────────

    /// <summary>
    /// 引用合法条目 → 结果可复核（D5 的正向形态）。
    /// </summary>
    [TestCase]
    public void CiteDecidingEntries_WithLegalEntries_MakesTheResultVerifiable()
    {
        // Arrange
        var result = VerifiableLogBattle();

        // Act
        result.CiteDecidingEntries(new[] { "e1" });

        // Assert
        Assertions.AssertThat(result.IsVerifiableAgainstLog()).IsTrue();
        Assertions.AssertThat(result.DecidingEntryIds).IsNotEmpty();
        Assertions.AssertThat(result.DecidingEntryIds.Count).IsEqual(1);
    }

    /// <summary>
    /// 只引用「没有改变判定」的条目 → 不可复核：引用了不等于复现了判定。
    /// </summary>
    [TestCase]
    public void IsVerifiable_CitingOnlyNonDecidingEntries_ReturnsFalse()
    {
        // Arrange
        var result = VerifiableLogBattle();

        // Act
        result.CiteDecidingEntries(new[] { "e2" });

        // Assert
        Assertions.AssertThat(result.IsVerifiableAgainstLog()).IsFalse();
    }

    /// <summary>
    /// 一条都不引用 → 不可复核。战斗结果必须交代自己凭什么赢的。
    /// </summary>
    [TestCase]
    public void IsVerifiable_WithoutAnyCitedEntry_ReturnsFalse()
    {
        // Arrange —— 产出一份日志合法、但从未声明判定依据的结果
        var result = VerifiableLogBattle();

        // Act / Assert
        Assertions.AssertThat(result.IsVerifiableAgainstLog()).IsFalse();
        Assertions.AssertThat(result.DecidingEntryIds.Count).IsEqual(0);
    }

    // ── 防线 1 · 引用不存在的条目 ──────────────────────────────────────────

    /// <summary>
    /// 引用一件没记录的事，玩家无法复核 → 拒绝。
    /// </summary>
    [TestCase]
    public void CiteDecidingEntries_ReferencingAMissingEntry_IsRejected()
    {
        // Arrange
        var result = VerifiableLogBattle();

        // Act
        bool threw = false;
        try { result.CiteDecidingEntries(new[] { "e1", "根本没有这一条" }); }
        catch (ArgumentException) { threw = true; }

        // Assert
        Assertions.AssertThat(threw).IsTrue();
    }

    // ── 防线 2 · 同一条目被引用两次 ────────────────────────────────────────

    /// <summary>
    /// 同一条目引用两次必须被拒：否则「引用的条目数」与「实际影响判定的条目数」对不上，
    /// 复核时计数错位 —— 这正是「计数错位」得以发生的入口。
    /// </summary>
    [TestCase]
    public void CiteDecidingEntries_CitingTheSameEntryTwice_IsRejected()
    {
        // Arrange —— 日志里只有一条 e1，引用列表里它出现两次
        var result = VerifiableLogBattle();

        // Act
        bool threw = false;
        try { result.CiteDecidingEntries(new[] { "e1", "e1" }); }
        catch (ArgumentException) { threw = true; }

        // Assert
        Assertions.AssertThat(threw).IsTrue();
    }

    /// <summary>
    /// 三次引用里重复两次，同样拒绝 —— 重复检测不依赖重复恰好是相邻的。
    /// </summary>
    [TestCase]
    public void CiteDecidingEntries_CitingADuplicateAmongDistinctEntries_IsRejected()
    {
        // Arrange
        var result = BattleResult.Produce(
            new BattleResultId("br1"), "遭遇", true, Party,
            new[]
            {
                new BattleLogEntry("e1", "阿黄击破守卫", true),
                new BattleLogEntry("e2", "乙救出同伴", true),
            });

        // Act
        bool threw = false;
        try { result.CiteDecidingEntries(new[] { "e1", "e2", "e1" }); }
        catch (ArgumentException) { threw = true; }

        // Assert
        Assertions.AssertThat(threw).IsTrue();
    }

    /// <summary>
    /// 被拒的引用<b>不留下半成品</b>：前一次合法的引用保持不变，
    /// 而不是被写坏或写成空。
    /// </summary>
    [TestCase]
    public void CiteDecidingEntries_WhenRejected_LeavesThePreviousCitationsIntact()
    {
        // Arrange —— 先立一份合法引用
        var result = VerifiableLogBattle();
        result.CiteDecidingEntries(new[] { "e1" });

        // Act —— 一次重复引用，必须被拒
        bool threw = false;
        try { result.CiteDecidingEntries(new[] { "e1", "e1" }); }
        catch (ArgumentException) { threw = true; }

        // Assert —— 原引用完好，结果依旧可复核
        Assertions.AssertThat(threw).IsTrue();
        Assertions.AssertThat(result.DecidingEntryIds.Count).IsEqual(1);
        Assertions.AssertThat(result.IsVerifiableAgainstLog()).IsTrue();
    }

    // ── 整体替换，不是叠加 ────────────────────────────────────────────────

    /// <summary>
    /// 二次调用是<b>整体替换</b>：第二次只引用 e2，最终就只有 e2，
    /// 而不是 <c>["e1", "e2"]</c>。
    /// </summary>
    [TestCase]
    public void CiteDecidingEntries_SecondCall_RejectsTheEarlierCitationsInsteadOfAppending()
    {
        // Arrange —— 日志里两条关键事件，先引用其中一条
        var result = BattleResult.Produce(
            new BattleResultId("br1"), "遭遇", true, Party,
            new[]
            {
                new BattleLogEntry("e1", "阿黄击破守卫", true),
                new BattleLogEntry("e2", "乙救出同伴", true),
            });
        result.CiteDecidingEntries(new[] { "e1" });

        // Act —— 改判依据
        result.CiteDecidingEntries(new[] { "e2" });

        // Assert
        Assertions.AssertThat(result.DecidingEntryIds.Count).IsEqual(1);
        Assertions.AssertThat(result.DecidingEntryIds[0]).IsEqual("e2");
        Assertions.AssertThat(result.IsVerifiableAgainstLog()).IsTrue();
    }

    /// <summary>
    /// 先引用两条、再引用一条 → 结果是那一条（窄化），不是并集。
    /// </summary>
    [TestCase]
    public void CiteDecidingEntries_SecondCall_NarrowsToItsOwnEntries()
    {
        // Arrange
        var result = BattleResult.Produce(
            new BattleResultId("br1"), "遭遇", true, Party,
            new[]
            {
                new BattleLogEntry("e1", "阿黄击破守卫", true),
                new BattleLogEntry("e2", "乙救出同伴", true),
            });
        result.CiteDecidingEntries(new[] { "e1", "e2" });
        Assertions.AssertThat(result.DecidingEntryIds.Count).IsEqual(2);

        // Act
        result.CiteDecidingEntries(new[] { "e1" });

        // Assert
        Assertions.AssertThat(result.DecidingEntryIds.Count).IsEqual(1);
        Assertions.AssertThat(result.DecidingEntryIds[0]).IsEqual("e1");
    }

    /// <summary>传空集合即「清空引用」，也是整体替换的一种 —— 清空后不可复核。</summary>
    [TestCase]
    public void CiteDecidingEntries_WithAnEmptySequence_ClearsTheCitations()
    {
        // Arrange
        var result = VerifiableLogBattle();
        result.CiteDecidingEntries(new[] { "e1" });

        // Act
        result.CiteDecidingEntries(Array.Empty<string>());

        // Assert
        Assertions.AssertThat(result.DecidingEntryIds.Count).IsEqual(0);
        Assertions.AssertThat(result.IsVerifiableAgainstLog()).IsFalse();
    }

    // ── 防线 3 · 日志里没有任何关键事件 ────────────────────────────────────

    /// <summary>
    /// 日志非空，但每一条 <c>DecidedOutcome</c> 都是 <c>false</c>：
    /// 没有任何一条可被引用为判定依据 → <b>构造期</b>就拒绝，
    /// 不留到运行期变成「复核面板永远是空的」。
    /// </summary>
    [TestCase]
    public void Produce_WithNoDecidingEntryInTheLog_IsRejected()
    {
        // Arrange —— 全是过程记录
        var log = new[]
        {
            new BattleLogEntry("e1", "双方接战", false),
            new BattleLogEntry("e2", "队伍整理阵型", false),
        };

        // Act
        bool threw = false;
        try { BattleResult.Produce(new BattleResultId("br1"), "遭遇", true, Party, log); }
        catch (ArgumentException) { threw = true; }

        // Assert
        Assertions.AssertThat(threw).IsTrue();
    }

    /// <summary>
    /// 一条关键事件 + 若干过程记录可以通过 —— 这条防线只要求
    /// <b>至少一条</b>，不要求「全部都是」（后者会让正常日志无法构造）。
    /// </summary>
    [TestCase]
    public void Produce_WithASingleDecidingEntryAmongProcessRecords_IsAccepted()
    {
        // Arrange / Act
        var result = BattleResult.Produce(
            new BattleResultId("br1"), "遭遇", true, Party,
            new[]
            {
                new BattleLogEntry("e1", "双方接战", false),
                new BattleLogEntry("e2", "阿黄击破守卫", true),
                new BattleLogEntry("e3", "天色渐晚", false),
            });

        // Assert
        Assertions.AssertThat(result.Log).IsNotEmpty();
        result.CiteDecidingEntries(new[] { "e2" });
        Assertions.AssertThat(result.IsVerifiableAgainstLog()).IsTrue();
    }

    // ── 既有的两条构造期防线（保留并显式回归） ───────────────────────────

    /// <summary>没有日志的战斗无法被玩家理解，也无法被测试复核。</summary>
    [TestCase]
    public void Produce_WithAnEmptyLog_IsRejected()
    {
        // Act
        bool threw = false;
        try
        {
            BattleResult.Produce(new BattleResultId("br1"), "遭遇", true, Party,
                Array.Empty<BattleLogEntry>());
        }
        catch (ArgumentException) { threw = true; }

        // Assert
        Assertions.AssertThat(threw).IsTrue();
    }

    /// <summary>没有参与者的战斗不成立。</summary>
    [TestCase]
    public void Produce_WithNoParticipants_IsRejected()
    {
        // Act
        bool threw = false;
        try
        {
            BattleResult.Produce(new BattleResultId("br1"), "遭遇", true,
                Array.Empty<string>(), new[] { new BattleLogEntry("e1", "打完了", true) });
        }
        catch (ArgumentException) { threw = true; }

        // Assert
        Assertions.AssertThat(threw).IsTrue();
    }

    /// <summary>没有标识的战斗结果无法被指认，也无法被引用。</summary>
    [TestCase]
    public void Produce_WithAnEmptyId_IsRejected()
    {
        // Act
        bool threw = false;
        try
        {
            BattleResult.Produce(new BattleResultId(string.Empty), "遭遇", true, Party,
                new[] { new BattleLogEntry("e1", "打完了", true) });
        }
        catch (ArgumentException) { threw = true; }

        // Assert
        Assertions.AssertThat(threw).IsTrue();
    }

    // ── 快照语义 · 与判定依据无关，但同样属于「结果不可变」 ───────────────

    /// <summary>
    /// 日志与参与者都是<b>快照</b>：产出之后改动源列表，不影响已产出的结果
    /// —— 否则判定依据可以在事后被改掉，D5 的复核就失去意义。
    /// </summary>
    [TestCase]
    public void Produce_SnapshotsTheLog_NotLiveReferences()
    {
        // Arrange
        var log = new List<BattleLogEntry> { new BattleLogEntry("e1", "打完了", true) };
        var participants = new List<string>(Party);

        // Act
        var result = BattleResult.Produce(new BattleResultId("br1"), "遭遇", true, participants, log);
        log.Clear();
        participants.Clear();

        // Assert
        Assertions.AssertThat(result.Log.Count).IsEqual(1);
        Assertions.AssertThat(result.Participants.Count).IsEqual(3);
    }
}
