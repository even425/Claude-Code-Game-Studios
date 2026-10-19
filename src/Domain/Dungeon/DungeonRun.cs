using System;
using System.Collections.Generic;
using System.Linq;

namespace ClaudeCodeGameStudios.Domain.Dungeon;

/// <summary>副本实例标识。</summary>
/// <param name="Value">标识值。</param>
public readonly record struct DungeonRunId(string Value)
{
    /// <inheritdoc />
    public override string ToString() => string.IsNullOrEmpty(Value) ? "(无)" : Value;
}

/// <summary>副本实例的阶段位置。</summary>
/// <remarks>
/// 【已确认】基线 §8.2：第一阶段副本必须具备<b>明确入口、多个阶段、明确最终目标、结算、奖励</b>。
/// 【已确认】<c>07</c> **D-010 Q2**：第一版做 <b>3 个阶段</b>（2 场遭遇 + 1 个最终目标战）。
/// <para>为什么是 3 而不是下限 2：两阶段会让「最终目标」与「阶段」在实现上塌缩成同一个东西，
/// 验证不到基线要求的两者区分。</para>
/// </remarks>
public enum DungeonStage
{
    /// <summary>尚未进入副本。</summary>
    NotEntered = 0,

    /// <summary>第 1 阶段：遭遇。</summary>
    EncounterOne = 1,

    /// <summary>第 2 阶段：遭遇。</summary>
    EncounterTwo = 2,

    /// <summary>第 3 阶段：最终目标。</summary>
    FinalGoal = 3,
}

/// <summary>副本实例的状态。</summary>
public enum DungeonRunStatus
{
    /// <summary>进行中。</summary>
    InProgress = 0,

    /// <summary>已成功。</summary>
    Succeeded = 1,

    /// <summary>已失败。</summary>
    Failed = 2,

    /// <summary>已结算（成功或失败都走一次幂等结算）。</summary>
    Settled = 3,
}

/// <summary>
/// 一次副本实例。
/// </summary>
/// <remarks>
/// <para>
/// <b>唯一写者：<c>Dungeon</c> 模块</b>。它负责「阶段推进与流转、完成条件判定」，
/// <b>不负责自动战斗规则</b>（那是 <c>Combat</c>）、
/// <b>不负责奖励内容</b>（那是 <c>Settlement</c>，且属【待定】T-004）。
/// </para>
/// <para><b>不变量 I3：一个副本实例至多关联一个战斗结果。</b>由 <see cref="HasBattleResult"/> 保证。</para>
/// </remarks>
public sealed class DungeonRun
{
    private DungeonRun(DungeonRunId id, string name, IReadOnlyList<string> participants)
    {
        Id = id;
        Name = name;
        Participants = participants;
        Stage = DungeonStage.NotEntered;
        Status = DungeonRunStatus.InProgress;
    }

    /// <summary>实例标识。</summary>
    public DungeonRunId Id { get; }

    /// <summary>副本名称。</summary>
    public string Name { get; }

    /// <summary>
    /// 参与者的<b>快照</b>，不是活引用。
    /// </summary>
    /// <remarks>
    /// 结算必须对应「谁去了」，而不是「读档瞬间队伍里还有谁」。
    /// 依据 <c>05</c> §4.2 的引用表。
    /// </remarks>
    public IReadOnlyList<string> Participants { get; }

    /// <summary>当前阶段。</summary>
    public DungeonStage Stage { get; private set; }

    /// <summary>当前状态。</summary>
    public DungeonRunStatus Status { get; private set; }

    /// <summary>已通过的战斗结果标识；<b>至多一个</b>（不变量 I3）。</summary>
    public string? BattleResultId { get; private set; }

    /// <summary>是否已关联战斗结果。</summary>
    public bool HasBattleResult => BattleResultId is not null;

    /// <summary>是否已走到最终目标。</summary>
    public bool IsAtFinalGoal => Stage == DungeonStage.FinalGoal;

    /// <summary>创建一次副本实例。</summary>
    /// <param name="id">实例标识。</param>
    /// <param name="name">副本名称。</param>
    /// <param name="participants">参与者标识列表（快照）。</param>
    /// <returns>状态为 InProgress、阶段为 NotEntered 的实例。</returns>
    /// <exception cref="ArgumentException">标识为空，或参与者为空。</exception>
    public static DungeonRun Create(DungeonRunId id, string name, IReadOnlyList<string> participants)
    {
        if (string.IsNullOrEmpty(id.Value))
        {
            throw new ArgumentException("副本实例标识不能为空。", nameof(id));
        }

        if (participants is null || participants.Count == 0)
        {
            throw new ArgumentException("副本必须至少有参与者。", nameof(participants));
        }

        return new DungeonRun(id, name ?? string.Empty, participants.ToArray());
    }

    /// <summary>进入副本。</summary>
    /// <exception cref="InvalidOperationException">已进入过。</exception>
    public void Enter()
    {
        if (Stage != DungeonStage.NotEntered)
        {
            throw new InvalidOperationException(
                $"副本 {Id} 已在阶段 {Stage}，不能重复进入。阶段只前进不回退。");
        }

        Stage = DungeonStage.EncounterOne;
    }

    /// <summary>
    /// 关联一次战斗结果。<b>至多一次</b>（不变量 I3）。
    /// </summary>
    /// <param name="battleResultId">战斗结果标识。</param>
    /// <exception cref="InvalidOperationException">
    /// 已关联过（I3），或实例不在进行中。
    /// </exception>
    public void AttachBattleResult(string battleResultId)
    {
        if (string.IsNullOrEmpty(battleResultId))
        {
            throw new ArgumentException("战斗结果标识不能为空。", nameof(battleResultId));
        }

        if (Status != DungeonRunStatus.InProgress)
        {
            throw new InvalidOperationException(
                $"副本 {Id} 处于 {Status}，不能关联战斗结果。");
        }

        if (HasBattleResult)
        {
            throw new InvalidOperationException(
                $"副本 {Id} 已关联战斗结果 {BattleResultId}，不能关联第二个（不变量 I3）。");
        }

        BattleResultId = battleResultId;
    }

    /// <summary>
    /// 推进到下一阶段。走到最终目标后返回 <c>true</c>，表示<b>最终目标已达成</b>。
    /// </summary>
    /// <param name="succeeded">本阶段战斗是否成功；失败时阶段不推进。</param>
    /// <returns>本次推进后是否已处于最终目标。</returns>
    /// <exception cref="InvalidOperationException">实例不在进行中，或尚未进入。</exception>
    public bool AdvanceStage(bool succeeded)
    {
        if (Status != DungeonRunStatus.InProgress)
        {
            throw new InvalidOperationException($"副本 {Id} 处于 {Status}，不能推进阶段。");
        }

        if (Stage == DungeonStage.NotEntered)
        {
            throw new InvalidOperationException($"副本 {Id} 尚未进入，不能推进阶段。");
        }

        if (!succeeded)
        {
            // 失败即终止。**失败不给奖励，但也不额外惩罚**（07 D-010 Q1）。
            Status = DungeonRunStatus.Failed;
            return false;
        }

        Stage = Stage switch
        {
            DungeonStage.EncounterOne => DungeonStage.EncounterTwo,
            DungeonStage.EncounterTwo => DungeonStage.FinalGoal,
            DungeonStage.FinalGoal => DungeonStage.FinalGoal,   // 已是终点，停在原地
            _ => throw new InvalidOperationException("非法阶段"),
        };

        return Stage == DungeonStage.FinalGoal;
    }

    /// <summary>判定最终目标是否完成，并把实例标为成功。</summary>
    /// <param name="finalGoalAchieved">最终目标是否达成。</param>
    /// <exception cref="InvalidOperationException">实例不在进行中，或尚未走到最终目标。</exception>
    public void CompleteFinalGoal(bool finalGoalAchieved)
    {
        if (Status != DungeonRunStatus.InProgress)
        {
            throw new InvalidOperationException($"副本 {Id} 处于 {Status}，不能完成最终目标。");
        }

        if (Stage != DungeonStage.FinalGoal)
        {
            throw new InvalidOperationException(
                $"副本 {Id} 仍在阶段 {Stage}，不能完成最终目标（基线 §8.2 要求有明确的最终目标）。");
        }

        Status = finalGoalAchieved ? DungeonRunStatus.Succeeded : DungeonRunStatus.Failed;
    }

    /// <summary>
    /// 标记已结算。<b>至多一次</b> —— 不变量 I4：已结算的不能重复结算。
    /// </summary>
    /// <exception cref="InvalidOperationException">已结算过。</exception>
    public void MarkSettled()
    {
        if (Status == DungeonRunStatus.Settled)
        {
            throw new InvalidOperationException(
                $"副本 {Id} 已结算，不能重复结算（不变量 I4 / 基线 §13.7）。");
        }

        if (Status == DungeonRunStatus.InProgress)
        {
            throw new InvalidOperationException($"副本 {Id} 仍在进行中，不能结算。");
        }

        Status = DungeonRunStatus.Settled;
    }
}
