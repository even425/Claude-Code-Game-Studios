using System;
using System.Collections.Generic;
using System.Linq;
using GdUnit4;
using ClaudeCodeGameStudios.Application;
using ClaudeCodeGameStudios.Domain.Combat;
using ClaudeCodeGameStudios.Domain.Dungeon;

/// <summary>
/// 副本<b>流程驱动</b>（M5）的单元测试。
///
/// <para><b>本文档保护：</b></para>
/// <ul>
/// <li><b>流程五步</b>：创建+进入 → 逐阶段解算 → 产出结果 → 推进/判定 → 结算</li>
/// <li><b>I3</b>：一个副本至多关联一个战斗结果 —— 且是<b>决定成败的那一个</b></li>
/// <li><b>基线 §8.5 D5</b>：每一场产出的战斗结果都能被日志复核</li>
/// <li><b>D-010 Q1</b>：中途失败即终止，且阶段不再前进</li>
/// <li><b>建筑默认无影响</b>：修饰只被采集与记录，不改变战斗</li>
/// </ul>
///
/// <para><b>本文档不含任何战斗数值。</b>胜负一律由测试替身按剧本给出，
/// <b>绝不随机</b>。真实战斗公式属【待定】<c>07</c> 的 <b>P-07 / T-004</b>。</para>
///
/// <para><b>为什么替身同时实现两个接口。</b>
/// <see cref="IBattleResolver.Resolve"/> 只返回日志，签名里<b>没有胜负</b>，
/// 胜负结论由第二个注入点 <see cref="IBattleOutcomeReader"/> 提供。
/// 把两者放在同一个替身上，日志与结论就<b>必然一致</b> ——
/// 日志说「胜」而结论说「负」的替身会把这个测试文件教坏。
/// 这一对正是 T-004 落地后要整体替换掉的东西。</para>
/// </summary>
[TestSuite]
public class DungeonFlowTests
{
    private static readonly string[] Party = { "甲", "乙", "丙" };

    // ── 测试替身 ───────────────────────────────────────────────────────────

    /// <summary>
    /// 按剧本给出胜负的战斗模块替身，同时实现两个注入点。
    /// 日志里的决定性条目与它给出的结论<b>永远一致</b>。
    /// </summary>
    private sealed class ScriptedCombatModule : IBattleResolver, IBattleOutcomeReader
    {
        private const string VerdictWin = "胜";
        private const string VerdictLoss = "负";

        private readonly Queue<bool> _verdicts;
        private readonly bool _afterScript;

        /// <param name="afterScript">剧本用完之后给出的结论。</param>
        /// <param name="verdicts">逐场的结论剧本。</param>
        public ScriptedCombatModule(bool afterScript, params bool[] verdicts)
        {
            _afterScript = afterScript;
            _verdicts = new Queue<bool>(verdicts);
        }

        /// <summary>被解算的次数。</summary>
        public int Calls { get; private set; }

        /// <summary>被解算的遭遇名，按调用顺序。</summary>
        public List<string> ResolvedEncounters { get; } = new();

        /// <summary>每次解算看到的参与者名单快照。</summary>
        public List<IReadOnlyList<string>> SeenParticipants { get; } = new();

        /// <summary>被询问结论的次数 —— 应与 <see cref="Calls"/> 相等。</summary>
        public int Consulted { get; private set; }

        public IReadOnlyList<BattleLogEntry> Resolve(
            string encounterName, CombatPartySnapshot party, int stageIndex)
        {
            Calls++;
            ResolvedEncounters.Add(encounterName);
            SeenParticipants.Add(party.ParticipantIds.ToArray());

            bool won = _verdicts.Count > 0 ? _verdicts.Dequeue() : _afterScript;

            return new[]
            {
                // 过程记录：不改变判定，因此不该被引用为依据
                new BattleLogEntry($"{encounterName}#e0", $"{encounterName}：双方接战", false),
                // 决定性记录：胜负结论就靠它
                new BattleLogEntry(
                    $"{encounterName}#e1",
                    $"{encounterName}：判定 {(won ? VerdictWin : VerdictLoss)}",
                    true),
            };
        }

        public bool DidHeroesWin(IReadOnlyList<BattleLogEntry> log)
        {
            Consulted++;

            var decisive = log.Where(entry => entry.DecidedOutcome).ToArray();
            if (decisive.Length == 0)
            {
                throw new InvalidOperationException("测试替身：日志里没有决定性条目，读不出结论。");
            }

            return decisive[decisive.Length - 1].Summary.EndsWith(VerdictWin, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// 战斗模块替身的**变体**：打出的日志里<b>没有一条改变判定</b>。
    /// 用来验证 D5 的强制点 —— 交不出判定依据的战斗必须被流程当场拒绝。
    /// </summary>
    private sealed class UndecidableCombatModule : IBattleResolver, IBattleOutcomeReader
    {
        public int Calls { get; private set; }

        public IReadOnlyList<BattleLogEntry> Resolve(
            string encounterName, CombatPartySnapshot party, int stageIndex)
        {
            Calls++;
            return new[]
            {
                new BattleLogEntry($"{encounterName}#e0", $"{encounterName}：双方接战", false),
                new BattleLogEntry($"{encounterName}#e1", $"{encounterName}：天色渐晚", false),
            };
        }

        public bool DidHeroesWin(IReadOnlyList<BattleLogEntry> log) => true;
    }

    /// <summary>
    /// 战斗模块替身的另一个变体：<b>交回 null 日志</b>。
    /// 用来验证流程在「根本没有日志」时也拒绝，且不留下痕迹。
    /// </summary>
    /// <remarks>
    /// 这与 <see cref="UndecidableCombatModule"/> 是<b>两个不同的洞</b>：
    /// 后者日志有内容只是没有关键事件，前者连日志都没有。分别钉测试，
    /// 免得其中一条被另一条捎带覆盖。
    /// </remarks>
    private sealed class NullLogCombatModule : IBattleResolver, IBattleOutcomeReader
    {
        /// <summary>被解算的次数。</summary>
        public int Calls { get; private set; }

        /// <summary>被读结论的次数。</summary>
        public int Consulted { get; private set; }

        public IReadOnlyList<BattleLogEntry> Resolve(
            string encounterName, CombatPartySnapshot party, int stageIndex)
        {
            Calls++;
            return null;
        }

        public bool DidHeroesWin(IReadOnlyList<BattleLogEntry> log)
        {
            Consulted++;
            return true;
        }
    }

    /// <summary>总是声称某座建筑提供了修饰的测试替身。</summary>
    private sealed class AlwaysModifier : IDungeonModifier
    {
        public DungeonModifier[] Evaluate(string dungeonName, IReadOnlyList<string> participantIds)
            => new[] { new DungeonModifier("test-modifier", "test-building") };
    }

    // ── Arrange ────────────────────────────────────────────────────────────

    /// <summary>每场都赢的战斗模块替身。</summary>
    private static ScriptedCombatModule WinsEveryEncounter()
        => new(afterScript: true, true, true, true);

    /// <summary>第一场赢、第二场输。</summary>
    private static ScriptedCombatModule WinsThenLoses()
        => new(afterScript: false, true, false);

    /// <summary>用给定战斗模块起一次副本。</summary>
    private static DungeonFlow BeginFlow(
        IBattleResolver resolver,
        IBattleOutcomeReader reader,
        string runId = "r1",
        IDungeonModifier? modifiers = null,
        Func<DungeonStage, string>? encounterNaming = null)
        => DungeonFlow.Begin(
            new DungeonRunId(runId), "北境旧堡", Party, resolver, reader, modifiers, encounterNaming);

    /// <summary>起一个全程获胜的副本。</summary>
    private static DungeonFlow AllWins(string runId = "r1")
    {
        var module = WinsEveryEncounter();
        return BeginFlow(module, module, runId);
    }

    // ── 完整流程：进入 → 遭遇一 → 遭遇二 → 最终目标 → 达成 ──────────────────

    /// <summary>
    /// 完整走通三阶段，断言阶段严格按 EncounterOne → EncounterTwo → FinalGoal 推进，
    /// 且最终判成功。
    /// </summary>
    [TestCase]
    public void Advance_WalksThreeStages_ReachingTheFinalGoal_AndSucceeds()
    {
        // Arrange —— Begin 已进入副本，尚未开打
        var flow = AllWins();

        Assertions.AssertThat(flow.Run.Stage).IsEqual(DungeonStage.EncounterOne);
        Assertions.AssertThat(flow.IsFinished).IsEqual(false);
        Assertions.AssertThat(flow.Records.Count).IsEqual(0);

        // Act / Assert —— 遭遇一
        var one = flow.Advance();
        Assertions.AssertThat(one.Stage).IsEqual(DungeonStage.EncounterOne);
        Assertions.AssertThat(one.EncounterName).IsEqual("遭遇一");
        Assertions.AssertThat(flow.Run.Stage).IsEqual(DungeonStage.EncounterTwo);
        Assertions.AssertThat(flow.Run.Status).IsEqual(DungeonRunStatus.InProgress);

        // 遭遇二
        var two = flow.Advance();
        Assertions.AssertThat(two.Stage).IsEqual(DungeonStage.EncounterTwo);
        Assertions.AssertThat(two.EncounterName).IsEqual("遭遇二");
        Assertions.AssertThat(flow.Run.Stage).IsEqual(DungeonStage.FinalGoal);
        Assertions.AssertThat(flow.Run.IsAtFinalGoal).IsEqual(true);

        // 最终目标
        var three = flow.Advance();
        Assertions.AssertThat(three.Stage).IsEqual(DungeonStage.FinalGoal);
        Assertions.AssertThat(three.EncounterName).IsEqual("最终目标");
        Assertions.AssertThat(flow.Run.Status).IsEqual(DungeonRunStatus.Succeeded);
        Assertions.AssertThat(flow.IsFinished).IsEqual(true);

        Assertions.AssertThat(flow.Records.Count).IsEqual(DungeonFlow.DeclaredStageCount);
    }

    /// <summary>三场战斗各打一次，不多不少；结算后状态为 Settled。</summary>
    [TestCase]
    public void RunToCompletion_WithAllWins_ResolvesOncePerStage_ThenSettles()
    {
        // Arrange
        var module = WinsEveryEncounter();
        var flow = BeginFlow(module, module);

        // Act
        var outcome = flow.RunToCompletion();
        flow.Settle();

        // Assert —— 恰好三场，且每场都被问过一次结论
        Assertions.AssertThat(module.Calls).IsEqual(3);
        Assertions.AssertThat(module.Consulted).IsEqual(3);
        Assertions.AssertThat(outcome.Succeeded).IsEqual(true);
        Assertions.AssertThat(flow.Run.Status).IsEqual(DungeonRunStatus.Settled);
        Assertions.AssertThat(outcome.Records.Count).IsEqual(3);

        // 遭遇名依次为 遭遇一 / 遭遇二 / 最终目标
        Assertions.AssertThat(outcome.Records[0].EncounterName).IsEqual("遭遇一");
        Assertions.AssertThat(outcome.Records[1].EncounterName).IsEqual("遭遇二");
        Assertions.AssertThat(outcome.Records[2].EncounterName).IsEqual("最终目标");
    }

    // ── I3 · 全程只关联一个 BattleResult，且是决定成败的那一个 ───────────────

    /// <summary>
    /// 每一阶段都产出战斗结果，但<b>整个副本只关联一个</b> —— 决定成败的那一场。
    /// </summary>
    [TestCase]
    public void RunToCompletion_LinksExactlyOneBattleResult_TheDecidingOne()
    {
        // Arrange
        var flow = AllWins();

        // Act
        var outcome = flow.RunToCompletion();

        // Assert —— 三场都产出了结果（不是漏打）
        Assertions.AssertThat(outcome.Records.Count).IsEqual(3);

        // 但只有一场进了实例（I3）
        var attached = outcome.Records.Where(record => record.AttachedToRun).ToArray();
        Assertions.AssertThat(attached.Length).IsEqual(1);
        Assertions.AssertThat(flow.Run.HasBattleResult).IsEqual(true);

        // 关联的必须是**最终目标**那一场 —— 它才是决定副本成败的那场
        Assertions.AssertThat(attached[0].Stage).IsEqual(DungeonStage.FinalGoal);
        Assertions.AssertThat(flow.Run.BattleResultId).IsEqual("r1#FinalGoal");
        Assertions.AssertThat(outcome.DecidingBattle.Id.Value).IsEqual("r1#FinalGoal");

        // 中间两场打赢了，但副本没打完，所以不在实例里
        Assertions.AssertThat(outcome.Records[0].AttachedToRun).IsEqual(false);
        Assertions.AssertThat(outcome.Records[1].AttachedToRun).IsEqual(false);
    }

    /// <summary>
    /// 挂在最终目标那一场，而不是第一场 —— 一个倒在最终目标的副本
    /// 绝不能挂上一个「英雄获胜」的战斗结果。
    /// </summary>
    [TestCase]
    public void RunToCompletion_LosingAtTheFinalGoal_LinksTheLoss_NotAnEarlierWin()
    {
        // Arrange —— 前两场赢、最终目标输
        var module = new ScriptedCombatModule(afterScript: false, true, true, false);
        var flow = BeginFlow(module, module, "r9");

        // Act
        var outcome = flow.RunToCompletion();

        // Assert
        Assertions.AssertThat(outcome.Succeeded).IsEqual(false);
        Assertions.AssertThat(flow.Run.Status).IsEqual(DungeonRunStatus.Failed);
        Assertions.AssertThat(flow.Run.BattleResultId).IsEqual("r9#FinalGoal");
        Assertions.AssertThat(outcome.DecidingBattle.HeroesWon).IsEqual(false);
        Assertions.AssertThat(outcome.Records.Count(record => record.AttachedToRun)).IsEqual(1);
    }

    /// <summary>战斗结果标识由副本标识与阶段推导 —— 不含随机，读档重放得到同样的标识。</summary>
    [TestCase]
    public void Advance_DerivesBattleResultIds_FromRunIdAndStage()
    {
        // Arrange
        var flow = AllWins();

        // Act
        var outcome = flow.RunToCompletion();

        // Assert
        Assertions.AssertThat(outcome.Records[0].Result.Id.Value).IsEqual("r1#EncounterOne");
        Assertions.AssertThat(outcome.Records[1].Result.Id.Value).IsEqual("r1#EncounterTwo");
        Assertions.AssertThat(outcome.Records[2].Result.Id.Value).IsEqual("r1#FinalGoal");
    }

    // ── D5 · 每次产出的结果都能被日志复核 ───────────────────────────────────

    /// <summary>
    /// 每一场产出的战斗结果都必须通过 <c>IsVerifiableAgainstLog()</c>：
    /// 判定引用的条目全部在日志里，且至少一条真的改变了判定（基线 §8.5 D5）。
    /// </summary>
    [TestCase]
    public void Advance_EveryProducedResult_IsVerifiableAgainstLog()
    {
        // Arrange
        var flow = AllWins();

        // Act
        var outcome = flow.RunToCompletion();

        // Assert
        Assertions.AssertThat(outcome.Records.Count).IsEqual(DungeonFlow.DeclaredStageCount);
        foreach (var record in outcome.Records)
        {
            var result = record.Result;
            Assertions.AssertThat(result.IsVerifiableAgainstLog()).IsEqual(true);

            // 判定引用的必须是那条真正改变判定的条目，而不是过程记录
            Assertions.AssertThat(result.DecidingEntryIds.Count).IsEqual(1);
            Assertions.AssertThat(result.DecidingEntryIds[0]).IsEqual($"{record.EncounterName}#e1");
        }
    }

    /// <summary>战斗模块交回 null 日志时必须被拒，且副本实例不得留下任何痕迹。</summary>
    /// <remarks>
    /// 这与下一条是<b>不同的两个洞</b>：下一条是「日志有内容但没有关键事件」，
    /// 本条是「根本没有日志」。两者的拒绝点不同（后者更早，在读结论之前），
    /// 因此各自钉一个测试 —— 合并成一个会让其中一条失去覆盖。
    /// </remarks>
    [TestCase]
    public void Advance_WithANullLog_IsRejected_AndLeavesTheRunUntouched()
    {
        // Arrange —— 替身交回 null
        var module = new NullLogCombatModule();
        var flow = BeginFlow(module, module, "r5");

        // Act
        bool threw = false;
        try { flow.Advance(); }
        catch (InvalidOperationException) { threw = true; }

        // Assert
        Assertions.AssertThat(threw).IsEqual(true);
        Assertions.AssertThat(module.Calls).IsEqual(1);

        // 「交不出依据的战斗不许进副本」—— 这里连胜负结论都没读出来就停了
        Assertions.AssertThat(module.Consulted).IsEqual(0);
        Assertions.AssertThat(flow.Run.HasBattleResult).IsEqual(false);
        Assertions.AssertThat(flow.Records.Count).IsEqual(0);
        Assertions.AssertThat(flow.Run.Status).IsEqual(DungeonRunStatus.InProgress);
        Assertions.AssertThat(flow.Run.Stage).IsEqual(DungeonStage.EncounterOne);
    }

    /// <summary>
    /// 交不出判定依据的战斗必须被当场拒绝，且<b>不许</b>写进副本实例。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 拒绝点如今在 <b><c>BattleResult.Produce</c> 的构造期</b>（抛
    /// <see cref="ArgumentException"/>），而不是流程事后的一次复核。
    /// 这比原先<b>更早也更强</b>：结果还没被构造出来，就无从被写进副本实例。
    /// </para>
    /// <para>
    /// <b>所以这里断言的是 <see cref="ArgumentException"/>，不是
    /// <see cref="InvalidOperationException"/>。</b>
    /// </para>
    /// </remarks>
    [TestCase]
    public void Advance_WithAnUndecidableLog_IsRejectedAtConstruction_AndLeavesTheRunUntouched()
    {
        // Arrange —— 替身打出的日志里没有一条改变判定
        var module = new UndecidableCombatModule();
        var flow = BeginFlow(module, module, "r2");

        // Act
        bool threw = false;
        try { flow.Advance(); }
        catch (ArgumentException) { threw = true; }

        // Assert —— 拒绝了，且没有留下任何痕迹
        Assertions.AssertThat(threw).IsEqual(true);

        // 关键：解算器**确实被调用过**。没有这一条，上面那个 catch 会连
        // 「流程在解算之前就因为别的原因抛出 ArgumentException」也一起放过，
        // 那样的绿灯什么都没证明。这一条把失败点钉死在 Produce 上。
        Assertions.AssertThat(module.Calls).IsEqual(1);

        Assertions.AssertThat(flow.Run.HasBattleResult).IsEqual(false);
        Assertions.AssertThat(flow.Records.Count).IsEqual(0);
        Assertions.AssertThat(flow.Run.Status).IsEqual(DungeonRunStatus.InProgress);
        Assertions.AssertThat(flow.Run.Stage).IsEqual(DungeonStage.EncounterOne);
    }

    // ── D-010 Q1 · 中途失败即终止 ───────────────────────────────────────────

    /// <summary>
    /// 遭遇二失败 → 终止、判失败、<b>阶段不再前进</b>，之后不再推进。
    /// </summary>
    [TestCase]
    public void Advance_FailingAtEncounterTwo_TerminatesWithoutAdvancing_AndRefusesFurtherSteps()
    {
        // Arrange
        var module = WinsThenLoses();
        var flow = BeginFlow(module, module, "r3");
        flow.Advance();   // 遭遇一赢 → EncounterTwo

        // Act
        var lost = flow.Advance();

        // Assert —— 终止在遭遇二，阶段没有前进
        Assertions.AssertThat(lost.Stage).IsEqual(DungeonStage.EncounterTwo);
        Assertions.AssertThat(lost.AttachedToRun).IsEqual(true);
        Assertions.AssertThat(flow.Run.Stage).IsEqual(DungeonStage.EncounterTwo);
        Assertions.AssertThat(flow.Run.Status).IsEqual(DungeonRunStatus.Failed);
        Assertions.AssertThat(flow.IsFinished).IsEqual(true);

        // 不再推进：再调 Advance 必须被拒，且**不产生新的战斗**
        int callsBefore = module.Calls;
        bool threw = false;
        try { flow.Advance(); }
        catch (InvalidOperationException) { threw = true; }

        Assertions.AssertThat(threw).IsEqual(true);
        Assertions.AssertThat(module.Calls).IsEqual(callsBefore);
        Assertions.AssertThat(flow.Records.Count).IsEqual(2);

        // 仍可结算一次（I4 之外的正常路径）
        flow.Settle();
        Assertions.AssertThat(flow.Run.Status).IsEqual(DungeonRunStatus.Settled);
    }

    /// <summary>第一场就输 → 立刻终止，最终目标那场永远不打。</summary>
    [TestCase]
    public void RunToCompletion_LosingAtEncounterOne_StopsImmediately()
    {
        // Arrange —— 剧本是「输、输、输」
        var module = new ScriptedCombatModule(afterScript: false, false);
        var flow = BeginFlow(module, module, "r4");

        // Act
        var outcome = flow.RunToCompletion();

        // Assert —— 只打了一场
        Assertions.AssertThat(module.Calls).IsEqual(1);
        Assertions.AssertThat(outcome.Records.Count).IsEqual(1);
        Assertions.AssertThat(flow.Run.Stage).IsEqual(DungeonStage.EncounterOne);
        Assertions.AssertThat(flow.Run.Status).IsEqual(DungeonRunStatus.Failed);
        Assertions.AssertThat(outcome.Succeeded).IsEqual(false);

        // 关联的是**输了的那一场**
        Assertions.AssertThat(flow.Run.BattleResultId).IsEqual("r4#EncounterOne");
        Assertions.AssertThat(outcome.DecidingBattle.HeroesWon).IsEqual(false);
    }

    /// <summary>
    /// 失败**不额外惩罚**（D-010 Q1）：副本输掉之后，
    /// 除状态之外本类没有别的东西可改 —— 参与者名单仍是进入时的快照。
    /// </summary>
    [TestCase]
    public void RunToCompletion_AfterALoss_KeepsParticipantsIntact()
    {
        // Arrange
        var module = new ScriptedCombatModule(afterScript: false, false);
        var flow = BeginFlow(module, module, "r5");

        // Act
        flow.RunToCompletion();

        // Assert
        Assertions.AssertThat(flow.Run.Participants.Count).IsEqual(3);
        Assertions.AssertThat(flow.Run.Participants[0]).IsEqual("甲");
    }

    // ── 结算 ───────────────────────────────────────────────────────────────

    /// <summary>跑完 → 结算 → 状态 Settled。</summary>
    [TestCase]
    public void Settle_AfterCompletion_MarksTheRunSettled()
    {
        // Arrange
        var flow = AllWins();
        flow.RunToCompletion();

        // Act
        flow.Settle();

        // Assert
        Assertions.AssertThat(flow.Run.Status).IsEqual(DungeonRunStatus.Settled);
    }

    /// <summary>重复结算被拒（不变量 I4）—— 本类不吞异常，让调用方知道自己没结算成。</summary>
    [TestCase]
    public void Settle_Twice_IsRejected()
    {
        // Arrange
        var flow = AllWins();
        flow.RunToCompletion();
        flow.Settle();

        // Act
        bool threw = false;
        try { flow.Settle(); }
        catch (InvalidOperationException) { threw = true; }

        // Assert
        Assertions.AssertThat(threw).IsEqual(true);
        Assertions.AssertThat(flow.Run.Status).IsEqual(DungeonRunStatus.Settled);
    }

    /// <summary>仍在进行中不能结算。</summary>
    [TestCase]
    public void Settle_WhileInProgress_IsRejected()
    {
        // Arrange
        var flow = AllWins();

        // Act
        bool threw = false;
        try { flow.Settle(); }
        catch (InvalidOperationException) { threw = true; }

        // Assert
        Assertions.AssertThat(threw).IsEqual(true);
        Assertions.AssertThat(flow.Run.Status).IsEqual(DungeonRunStatus.InProgress);
    }

    // ── 建筑默认无影响 ─────────────────────────────────────────────────────

    /// <summary>不注入建筑修饰 → 每场都记到零条修饰（默认无影响）。</summary>
    [TestCase]
    public void Advance_WithoutBuildings_RecordsNoModifiers()
    {
        // Arrange
        var flow = AllWins();

        // Act
        var outcome = flow.RunToCompletion();

        // Assert
        foreach (var record in outcome.Records)
        {
            Assertions.AssertThat(record.Modifiers.Count).IsEqual(0);
        }
    }

    /// <summary>
    /// 注入建筑修饰 → <b>被采集并记录</b>，但本阶段<b>不改变战斗</b>
    /// （建筑功能属【待定】T-004，战斗公式也未定）。
    /// </summary>
    [TestCase]
    public void Advance_WithBuildings_RecordsThem_WithoutChangingTheOutcome()
    {
        // Arrange
        var module = WinsEveryEncounter();
        var flow = BeginFlow(module, module, "r6", modifiers: new AlwaysModifier());

        // Act
        var outcome = flow.RunToCompletion();

        // Assert —— 每场都记下了那条修饰
        foreach (var record in outcome.Records)
        {
            Assertions.AssertThat(record.Modifiers.Count).IsEqual(1);
            Assertions.AssertThat(record.Modifiers[0].ModifierId).IsEqual("test-modifier");
        }

        // 但战斗结果分毫未变
        Assertions.AssertThat(outcome.Succeeded).IsEqual(true);
        Assertions.AssertThat(outcome.Records[2].Result.HeroesWon).IsEqual(true);
        Assertions.AssertThat(module.Calls).IsEqual(3);
    }

    // ── 副本期间不影响村庄日常 ─────────────────────────────────────────────

    /// <summary>
    /// 参与者是<b>快照</b>：进入之后调用方改动原列表，
    /// 不影响已创建的副本，也不改变后续战斗看到的名单。
    /// </summary>
    [TestCase]
    public void Begin_SnapshotsParticipants_SoTheCallersListCannotChangeTheRun()
    {
        // Arrange
        var roster = new List<string>(Party);
        var module = WinsEveryEncounter();
        var flow = DungeonFlow.Begin(
            new DungeonRunId("r7"), "北境旧堡", roster, module, module);

        // Act —— 模拟「读档瞬间队伍变了」
        roster.Clear();
        flow.Advance();

        // Assert —— 副本与战斗看到的都还是原来那三个人
        Assertions.AssertThat(flow.Run.Participants.Count).IsEqual(3);
        Assertions.AssertThat(module.SeenParticipants[0].Count).IsEqual(3);
    }

    // ── 依赖注入与前置条件 ─────────────────────────────────────────────────

    /// <summary>resolver / 胜负读者 / 实例必须注入 —— 缺一个都不许构造流程。</summary>
    [TestCase]
    public void Constructor_WithMissingCollaborators_IsRejected()
    {
        // Arrange / Act
        bool noResolver = false;
        try
        {
            _ = new DungeonFlow(
                DungeonRun.Create(new DungeonRunId("r8"), "堡", Party),
                null, new ScriptedCombatModule(true));
        }
        catch (ArgumentNullException) { noResolver = true; }

        bool noReader = false;
        try
        {
            _ = new DungeonFlow(
                DungeonRun.Create(new DungeonRunId("r8"), "堡", Party),
                new ScriptedCombatModule(true), null);
        }
        catch (ArgumentNullException) { noReader = true; }

        bool noRun = false;
        try
        {
            _ = new DungeonFlow(
                null, new ScriptedCombatModule(true), new ScriptedCombatModule(true));
        }
        catch (ArgumentNullException) { noRun = true; }

        // Assert
        Assertions.AssertThat(noResolver).IsEqual(true);
        Assertions.AssertThat(noReader).IsEqual(true);
        Assertions.AssertThat(noRun).IsEqual(true);
    }

    /// <summary>未进入的副本不能推进 —— 用不自动 Enter 的构造函数走这条路。</summary>
    [TestCase]
    public void Advance_WithoutEntering_IsRejected()
    {
        // Arrange —— 构造函数不负责 Enter
        var module = WinsEveryEncounter();
        var run = DungeonRun.Create(new DungeonRunId("r10"), "堡", Party);
        var flow = new DungeonFlow(run, module, module);

        // Act
        bool threw = false;
        try { flow.Advance(); }
        catch (InvalidOperationException) { threw = true; }

        // Assert —— 拒绝，且一次战斗都没打
        Assertions.AssertThat(threw).IsEqual(true);
        Assertions.AssertThat(module.Calls).IsEqual(0);
        Assertions.AssertThat(flow.Records.Count).IsEqual(0);
    }

    /// <summary>构造函数不进入，<c>Begin</c> 进入 —— 两条路径的差别是显式的。</summary>
    [TestCase]
    public void Constructor_DoesNotEnter_ButBegin_Does()
    {
        // Arrange
        var module = WinsEveryEncounter();
        var manual = new DungeonFlow(
            DungeonRun.Create(new DungeonRunId("r11"), "堡", Party), module, module);
        var begun = AllWins();

        // Act / Assert
        Assertions.AssertThat(manual.Run.Stage).IsEqual(DungeonStage.NotEntered);
        Assertions.AssertThat(begun.Run.Stage).IsEqual(DungeonStage.EncounterOne);
    }

    /// <summary>没有判定的阶段名不存在 —— 遭遇名映射对未进入阶段拒绝作答。</summary>
    [TestCase]
    public void DefaultEncounterName_ForAnUnenteredStage_IsRejected()
    {
        // Act
        bool threw = false;
        try { DungeonFlow.DefaultEncounterName(DungeonStage.NotEntered); }
        catch (ArgumentOutOfRangeException) { threw = true; }

        // Assert
        Assertions.AssertThat(threw).IsEqual(true);
    }

    /// <summary>遭遇名可以注入 —— 不必改流程代码就能换一套命名。</summary>
    [TestCase]
    public void Advance_WithInjectedEncounterNaming_UsesIt()
    {
        // Arrange
        var module = WinsEveryEncounter();
        var flow = BeginFlow(
            module, module, "r12", encounterNaming: stage => $"stage-{stage}");

        // Act
        var one = flow.Advance();

        // Assert
        Assertions.AssertThat(one.EncounterName).IsEqual("stage-EncounterOne");
        Assertions.AssertThat(one.Result.EncounterName).IsEqual("stage-EncounterOne");
    }

    /// <summary>
    /// 总结必须基于本流程真的打过的战斗：拿一个<b>在本流程之前就已结束</b>的实例
    /// 来总结，<c>RunToCompletion</c> 必须报出「一场都没打过」，
    /// 而不是把空记录包装成一次成功。
    /// </summary>
    /// <remarks>
    /// <b>本测试覆盖不到的那道护栏</b>：<c>RunToCompletion</c> 里
    /// 「推进步数超过 <c>DeclaredStageCount</c>」的死循环护栏，
    /// 需要一个「永远打不完」的领域行为才能触发，而那要改 <c>DungeonRun</c> ——
    /// 本轮不改任何现有文件，所以那道护栏目前<b>未经测试覆盖</b>。
    /// 这是有意留下的缺口，不是通过。
    /// </remarks>
    [TestCase]
    public void RunToCompletion_OnAnAlreadyFinishedRun_ReportsThatNothingWasFought()
    {
        // Arrange —— 先把一个副本跑到成功（它已终止）
        var module = WinsEveryEncounter();
        var finished = BeginFlow(module, module, "r13");
        finished.RunToCompletion();

        // Act —— 再拿这个已终止的实例起一个流程：循环不会转（IsFinished 为真），
        //       但总结时必须报出「一场都没打过」，而不是谎报结果。
        var reopened = new DungeonFlow(finished.Run, module, module);

        bool threw = false;
        try { reopened.RunToCompletion(); }
        catch (InvalidOperationException) { threw = true; }

        // Assert
        Assertions.AssertThat(threw).IsEqual(true);
        Assertions.AssertThat(reopened.Records.Count).IsEqual(0);
    }
}
