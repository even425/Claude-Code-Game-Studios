using System;
using System.Collections.Generic;
using System.Linq;
using GdUnit4;
using ClaudeCodeGameStudios.Domain.Combat;
using ClaudeCodeGameStudios.Domain.Dungeon;

/// <summary>
/// 副本与战斗的单元测试。
///
/// <para><b>本文档保护：</b></para>
/// <ul>
/// <li><b>I3</b>：一个副本实例至多关联一个战斗结果</li>
/// <li><b>I4</b>：已结算的副本不能重复结算</li>
/// <li><b>基线 §8.2</b>：副本须有明确入口、多个阶段、明确最终目标、结算、奖励</li>
/// <li><b>基线 §8.5 D5</b>：战斗日志能复现胜负判定所引用的每一个关键事件</li>
/// <li><b>D-010</b>：失败不给奖励也不额外惩罚 · 第一版 3 阶段 · 每副本一个战斗结果 · 结算整体事务化</li>
/// </ul>
///
/// <para><b>本文档不规定任何战斗数值。</b>伤害/命中/冷却/目标选择全部属【待定】
/// <c>07</c> 的 <b>P-07 / T-004</b>；本模块只测<b>结构</b>与<b>可复核性</b>。</para>
/// </summary>
[TestSuite]
public class DungeonCombatTests
{
    private static readonly string[] Party = { "甲", "乙", "丙" };

    private static DungeonRun FreshRun(string id = "r1")
    {
        var run = DungeonRun.Create(new DungeonRunId(id), "北境旧堡", Party);
        run.Enter();
        return run;
    }

    private static BattleResult Battle(
        string id, bool won, params BattleLogEntry[] log)
        => BattleResult.Produce(
            new BattleResultId(id), "遭遇", won, Party,
            log.Length > 0
                ? log
                : new[] { new BattleLogEntry("e0", "双方接战", true) });

    // ── I3 · 一个副本至多一个战斗结果 ───────────────────────────────────────

    /// <summary>关联第二个战斗结果必须被拒（I3），且原有关联不变。</summary>
    [TestCase]
    public void AttachBattleResult_SecondTime_IsRejected_AndOriginalKept()
    {
        var run = FreshRun();
        run.AttachBattleResult("br1");

        bool threw = false;
        try { run.AttachBattleResult("br2"); }
        catch (InvalidOperationException) { threw = true; }

        Assertions.AssertThat(threw).IsTrue();
        Assertions.AssertThat(run.BattleResultId).IsEqual("br1");
        Assertions.AssertThat(run.HasBattleResult).IsTrue();
    }

    // ── 基线 §8.2 · 三阶段结构（D-010 Q2）──────────────────────────────────

    /// <summary>
    /// 完整走通：入口 → 遭遇一 → 遭遇二 → **最终目标** → 达成。
    /// 断言阶段严格按 EncounterOne → EncounterTwo → FinalGoal 推进。
    /// </summary>
    [TestCase]
    public void AdvanceStage_WalksTheThreeDeclaredStages_ReachingTheFinalGoal()
    {
        var run = FreshRun();
        Assertions.AssertThat(run.Stage).IsEqual(DungeonStage.EncounterOne);

        bool atFinalAfterFirst = run.AdvanceStage(succeeded: true);
        Assertions.AssertThat(run.Stage).IsEqual(DungeonStage.EncounterTwo);
        Assertions.AssertThat(atFinalAfterFirst).IsFalse();

        bool atFinalAfterSecond = run.AdvanceStage(succeeded: true);
        Assertions.AssertThat(run.Stage).IsEqual(DungeonStage.FinalGoal);
        // 这一条是关键：2 阶段会让「最终目标」与「阶段」塌缩成同一个东西，
        // 验证不到基线 §8.2 要求的区分（D-010 Q2）。
        Assertions.AssertThat(atFinalAfterSecond).IsTrue();
        Assertions.AssertThat(run.IsAtFinalGoal).IsTrue();
    }

    /// <summary>阶段只前进不回退；已到终点后不再前进。</summary>
    [TestCase]
    public void AdvanceStage_NeverGoesBackwards_AndStopsAtTheEnd()
    {
        var run = FreshRun();
        run.AdvanceStage(true);
        run.AdvanceStage(true);
        run.AdvanceStage(true);   // 已在 FinalGoal，再推进应停在原地

        Assertions.AssertThat(run.Stage).IsEqual(DungeonStage.FinalGoal);
    }

    /// <summary>未进入不能推进；进入两次也拒绝。</summary>
    [TestCase]
    public void Enter_And_Advance_HaveCorrectPreconditions()
    {
        var notEntered = DungeonRun.Create(new DungeonRunId("r0"), "堡", Party);
        bool advanceThrew = false;
        try { notEntered.AdvanceStage(true); }
        catch (InvalidOperationException) { advanceThrew = true; }
        Assertions.AssertThat(advanceThrew).IsTrue();

        var run = FreshRun();
        bool reEnterThrew = false;
        try { run.Enter(); }
        catch (InvalidOperationException) { reEnterThrew = true; }
        Assertions.AssertThat(reEnterThrew).IsTrue();
    }

    /// <summary>
    /// <b>失败不给奖励，但也不额外惩罚</b>（D-010 Q1）：失败只是终止并结算，
    /// 不扣资源、不降属性、不改关系 —— 本模块<b>根本没有</b>执行这些的能力。
    /// </summary>
    [TestCase]
    public void AdvanceStage_WithFailure_TerminatesWithoutSideEffects()
    {
        var run = FreshRun();
        run.AttachBattleResult("br1");

        run.AdvanceStage(succeeded: false);

        Assertions.AssertThat(run.Status).IsEqual(DungeonRunStatus.Failed);
        // 阶段**没有**前进 —— 失败不推进
        Assertions.AssertThat(run.Stage).IsEqual(DungeonStage.EncounterOne);
    }

    /// <summary>未到最终目标不能完成最终目标。</summary>
    [TestCase]
    public void CompleteFinalGoal_BeforeReachingIt_IsRejected()
    {
        var run = FreshRun();
        run.AdvanceStage(true);   // 只到 EncounterTwo

        bool threw = false;
        try { run.CompleteFinalGoal(true); }
        catch (InvalidOperationException) { threw = true; }

        Assertions.AssertThat(threw).IsTrue();
    }

    /// <summary>最终目标未达成 → 判失败而非成功。</summary>
    [TestCase]
    public void CompleteFinalGoal_NotAchieved_EndsAsFailed()
    {
        var run = FreshRun();
        run.AdvanceStage(true);
        run.AdvanceStage(true);

        run.CompleteFinalGoal(finalGoalAchieved: false);

        Assertions.AssertThat(run.Status).IsEqual(DungeonRunStatus.Failed);
    }

    // ── I4 · 结算幂等 ─────────────────────────────────────────────────────

    /// <summary>重复结算必须被拒（I4 / 基线 §13.7「已完成事件不会重复应用」）。</summary>
    [TestCase]
    public void MarkSettled_Twice_IsRejected()
    {
        var run = FreshRun();
        run.AdvanceStage(false);      // 失败 → 可结算
        run.MarkSettled();

        bool threw = false;
        try { run.MarkSettled(); }
        catch (InvalidOperationException) { threw = true; }

        Assertions.AssertThat(threw).IsTrue();
        Assertions.AssertThat(run.Status).IsEqual(DungeonRunStatus.Settled);
    }

    /// <summary>仍在进行中的副本不能被结算。</summary>
    [TestCase]
    public void MarkSettled_WhileInProgress_IsRejected()
    {
        var run = FreshRun();

        bool threw = false;
        try { run.MarkSettled(); }
        catch (InvalidOperationException) { threw = true; }

        Assertions.AssertThat(threw).IsTrue();
    }

    // ── D5 · 战斗日志能复现胜负判定 ────────────────────────────────────────

    /// <summary>判定引用的条目必须<b>全部存在于日志</b>。</summary>
    [TestCase]
    public void CiteDecidingEntries_ReferencingAMissingEntry_IsRejected()
    {
        var result = Battle("br", won: true,
            new BattleLogEntry("e1", "阿黄击破守卫", true),
            new BattleLogEntry("e2", "队伍整理阵型", false));

        bool threw = false;
        try { result.CiteDecidingEntries(new[] { "e1", "根本没有这一条" }); }
        catch (ArgumentException) { threw = true; }

        Assertions.AssertThat(threw).IsTrue();
    }

    /// <summary>只引用「没有改变判定」的条目，等于判定不可复核。</summary>
    [TestCase]
    public void IsVerifiable_CitingOnlyNonDecidingEntries_ReturnsFalse()
    {
        // 追加一条 DecidedOutcome == true 的条目：Produce 的构造期防线要求日志里
        // 至少有一条关键事件，否则这场战斗根本无法复核（D5）。
        // 本用例要测的是「**引用了一条非判定条目**时不可复核」，不是「日志里没有关键事件」——
        // 后者已由 Produce 直接拒绝，那是另一条防线。故此处补足关键事件，
        // 让本用例仍然打在被测的那一个行为上。
        var result = Battle("br", won: true,
            new BattleLogEntry("e0", "阿黄击破守卫", true),
            new BattleLogEntry("e1", "队伍整理阵型", false),
            new BattleLogEntry("e2", "天色渐晚", false));

        result.CiteDecidingEntries(new[] { "e1" });

        Assertions.AssertThat(result.IsVerifiableAgainstLog()).IsFalse();
    }

    /// <summary>引用了真正改变判定的条目 → 可复核。</summary>
    [TestCase]
    public void IsVerifiable_CitingDecidingEntries_ReturnsTrue()
    {
        var result = Battle("br", won: true,
            new BattleLogEntry("e1", "阿黄击破守卫", true),
            new BattleLogEntry("e2", "队伍整理阵型", false));

        result.CiteDecidingEntries(new[] { "e1" });

        Assertions.AssertThat(result.IsVerifiableAgainstLog()).IsTrue();
    }

    /// <summary>从未声明判定依据时不可复核 —— 战斗结果必须交代自己凭什么赢的。</summary>
    [TestCase]
    public void IsVerifiable_WithoutCitingAnything_ReturnsFalse()
    {
        var result = Battle("br", won: true, new BattleLogEntry("e1", "打完了", true));

        Assertions.AssertThat(result.IsVerifiableAgainstLog()).IsFalse();
    }

    /// <summary>没有日志的战斗无法被玩家理解，也无法被测试复核。</summary>
    [TestCase]
    public void Produce_WithEmptyLog_IsRejected()
    {
        bool threw = false;
        try
        {
            BattleResult.Produce(new BattleResultId("br"), "遭遇", true, Party,
                Array.Empty<BattleLogEntry>());
        }
        catch (ArgumentException) { threw = true; }

        Assertions.AssertThat(threw).IsTrue();
    }

    /// <summary>没有参与者的战斗不成立。</summary>
    [TestCase]
    public void Produce_WithNoParticipants_IsRejected()
    {
        bool threw = false;
        try
        {
            BattleResult.Produce(new BattleResultId("br"), "遭遇", true,
                Array.Empty<string>(), new[] { new BattleLogEntry("e1", "x", true) });
        }
        catch (ArgumentException) { threw = true; }

        Assertions.AssertThat(threw).IsTrue();
    }

    /// <summary>
    /// 战斗结果是<b>快照</b>：副本结束后成员变动不影响已产出的结果。
    /// </summary>
    [TestCase]
    public void Produce_SnapshotsParticipants_NotLiveReferences()
    {
        var participants = new List<string>(Party);
        var result = BattleResult.Produce(new BattleResultId("br"), "遭遇", true, participants,
            new[] { new BattleLogEntry("e1", "打完了", true) });

        participants.Clear();   // 模拟「读档瞬间队伍变了」

        Assertions.AssertThat(result.Participants.Count).IsEqual(3);
    }
}
