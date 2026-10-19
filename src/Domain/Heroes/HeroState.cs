using System;

namespace ClaudeCodeGameStudios.Domain.Heroes;

/// <summary>
/// 英雄的<b>运行时状态</b> —— 他「此刻能不能」「此刻在做什么」。
/// </summary>
/// <remarks>
/// <para>
/// 依据基线 §4.1 四层建模，本类型承载<b>当前状态</b>这一层，以及 03 §3.1 定义的<b>在场性</b>。
/// 人物特质与玩法偏好在 <see cref="HeroDefinition"/>；人际关系第一阶段不建模。
/// </para>
/// <para>
/// <b>唯一写者：<c>Heroes</c> 模块</b>（<c>05</c> §2.3 的不变量 I10）。
/// 别的模块只能读，不能改。
/// </para>
/// </remarks>
public sealed class HeroState
{
    /// <summary>在场性。第一阶段<b>只有两种取值</b>，没有第三种。</summary>
    /// <remarks>
    /// 依据 03 §3.1【已确认】：不存在「离线」「缺席」等语义——
    /// 地球时区作息与离线判定已由 <c>07</c> D-001 作废。
    /// 随队远征中的英雄不是「缺席」，是<b>在队伍任务中</b>（<c>07</c> D-006）。
    /// </remarks>
    public Presence Presence { get; private set; } = Presence.InVillage;

    /// <summary>当前所在活动。<see cref="ActivityId.None"/> 表示空闲。</summary>
    public ActivityId CurrentActivity { get; private set; } = ActivityId.None;

    /// <summary>
    /// 当前行动的<b>目标英雄</b>（第三阶段 M2：社交需要对象）。
    /// </summary>
    /// <remarks>
    /// <see cref="HeroId.None"/> 表示不是目标型活动（采集 / 建屋等）。
    /// 目标在 <see cref="BeginAction"/> 时确定，完成时由应用层读取并释放 ——
    /// 它<b>不</b>属于关系数据本身；关系存放在 <c>Domain/Relationships</c>。
    /// </remarks>
    public HeroId CurrentTarget { get; private set; } = HeroId.None;

    /// <summary>
    /// 当前行动实例的<b>剩余时长</b>（模拟秒）。
    /// </summary>
    /// <remarks>
    /// 依据 <c>05</c> §3.2.1（<c>07</c> D-009 R-1）：不补这个字段，
    /// 「行动进行中存档」会导致行动凭空消失或被重算。
    /// 取<b>直接持有</b>而非存行动实例 ID 再解析 —— 存档往返只需一次字段比较。
    /// <para>
    /// 【待定】行动耗时本身属 <c>07</c> 的 <b>P-04 / T-002</b>。
    /// 第一阶段允许耗时为 0（瞬时行动），此时本值恒为 0。
    /// </para>
    /// </remarks>
    public double RemainingActionSeconds { get; private set; }

    /// <summary>首次完成副本的标记。基线 §7.2 点名它是杂谈的触发条件之一。</summary>
    public bool HasCompletedFirstDungeon { get; private set; }

    /// <summary>创建状态。构造函数之外一律通过本类型的方法改动（唯一写者约束）。</summary>
    /// <param name="definition">该英雄的静态定义。</param>
    /// <exception cref="ArgumentException">
    /// <paramref name="definition"/> 的 Id 为空 —— 实体标识不允许为空。
    /// </exception>
    public HeroState(HeroDefinition definition)
    {
        Definition = definition ?? throw new ArgumentNullException(nameof(definition));
        if (string.IsNullOrEmpty(definition.Id.Value))
        {
            throw new ArgumentException("英雄定义的标识不能为空。", nameof(definition));
        }
    }

    /// <summary>该英雄的静态定义。只读。</summary>
    public HeroDefinition Definition { get; }

    /// <summary>是否空闲 —— 03 §3.1 S1 触发条件的第一个分量。</summary>
    public bool IsIdle => CurrentActivity.Value == ActivityId.None.Value;

    /// <summary>是否在村 —— 03 §3.1 S1 触发条件的第二个分量。</summary>
    public bool IsInVillage => Presence == Presence.InVillage;

    /// <summary>
    /// 03 §3.1 S1：此刻是否轮到这名英雄做决定。
    /// </summary>
    /// <remarks>
    /// 这是一个<b>纯粹的集合表达式</b>，不需要任何数值：
    /// <c>O(t) = { h | h.当前所在活动 = 空闲 ∧ h.在场性 = 在村 }</c>。
    /// </remarks>
    /// <returns>空闲且在村时为 true。</returns>
    public bool IsEligibleForDecision() => IsIdle && IsInVillage;

    /// <summary>
    /// 03 §3.1 S6：占用当前行动并开始计时。
    /// </summary>
    /// <param name="activity">要开始的活动。</param>
    /// <param name="durationSeconds">该行动的耗时。【待定】属 P-04 / T-002。</param>
    /// <param name="target">
    /// 目标英雄（第三阶段 M2）。默认 <see cref="HeroId.None"/> = 非目标活动。
    /// </param>
    /// <exception cref="InvalidOperationException">该英雄已有进行中的行动。</exception>
    /// <exception cref="ArgumentOutOfRangeException">耗时为负或非有限数。</exception>
    public void BeginAction(ActivityId activity, double durationSeconds, HeroId target = default)
    {
        if (!IsIdle)
        {
            throw new InvalidOperationException(
                $"英雄 {Id} 已有进行中的行动 {CurrentActivity}，不能再开始 {activity}。"
                + "不变量 I2 / I16：同一时刻至多处于一个行动。");
        }

        if (!double.IsFinite(durationSeconds) || durationSeconds < 0.0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(durationSeconds), durationSeconds, "行动耗时必须是非负有限数。");
        }

        if (!string.IsNullOrEmpty(target.Value) && target == Id)
        {
            throw new ArgumentException("行动目标不能是自己。", nameof(target));
        }

        CurrentActivity = activity;
        CurrentTarget = target;
        RemainingActionSeconds = durationSeconds;
    }

    /// <summary>释放当前行动的目标（完成时由应用层调用）。</summary>
    public void ClearTarget() => CurrentTarget = HeroId.None;

    /// <summary>
    /// 推进计时；耗时走完则释放占用并返回<b>刚刚完成</b>的活动。
    /// </summary>
    /// <remarks>
    /// <b>完成时保留 <see cref="CurrentTarget"/></b>：应用层要读它来结算「和谁」的社交；
    /// 读完由 <see cref="ClearTarget"/> 释放。下一次 <see cref="BeginAction"/>
    /// 会整体覆盖目标，因此不存在跨行动串味。
    /// </remarks>
    /// <param name="simDeltaSeconds">本次推进的模拟秒数。</param>
    /// <returns>
    /// 若行动在此刻完成，返回其 <see cref="ActivityId"/>；否则返回 <see cref="ActivityId.None"/>。
    /// </returns>
    /// <exception cref="ArgumentOutOfRangeException">传入的增量不是非负有限数。</exception>
    public ActivityId AdvanceAction(double simDeltaSeconds)
    {
        if (!double.IsFinite(simDeltaSeconds) || simDeltaSeconds < 0.0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(simDeltaSeconds), simDeltaSeconds,
                "模拟时间不回退（不变量 I13），推进量必须是非负有限数。");
        }

        if (IsIdle) return ActivityId.None;

        RemainingActionSeconds -= simDeltaSeconds;

        // 用一个很小的容差吸收浮点减法的残差，避免「本该完成却差一点」的滞留。
        if (RemainingActionSeconds > 1e-9) return ActivityId.None;

        var finished = CurrentActivity;
        CurrentActivity = ActivityId.None;
        RemainingActionSeconds = 0.0;
        return finished;
    }

    /// <summary>置为在队伍任务中。<c>07</c> D-006：随队者不参与日常决策。</summary>
    public void SetOnMission()
    {
        Presence = Presence.OnMission;
        ReleaseAction();
    }

    /// <summary>回到在村。</summary>
    public void SetInVillage() => Presence = Presence.InVillage;

    /// <summary>标记首次完成副本。<c>05</c> §3.2 点名它是杂谈触发条件。</summary>
    public void MarkFirstDungeonCompleted() => HasCompletedFirstDungeon = true;

    /// <summary>强制释放当前行动（无产出、无损失）。用于回滚与读档。</summary>
    private void ReleaseAction()
    {
        CurrentActivity = ActivityId.None;
        CurrentTarget = HeroId.None;
        RemainingActionSeconds = 0.0;
    }

    /// <summary>该英雄的标识。</summary>
    public HeroId Id => Definition.Id;
}

/// <summary>在场性。第一阶段只有两种取值。</summary>
public enum Presence
{
    /// <summary>在村。可参与日常决策。</summary>
    InVillage = 0,

    /// <summary>在队伍任务中。不参与日常决策（<c>07</c> D-006）。</summary>
    OnMission = 1,
}
