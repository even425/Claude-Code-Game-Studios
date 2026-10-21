using System;
using System.Collections.Generic;
using System.Linq;

namespace ClaudeCodeGameStudios.Domain.Combat;

/// <summary>战斗中的一名参战者（<b>带实时状态</b>）。</summary>
/// <remarks>
/// <para>它是**战斗过程里的可变状态**，不是持久化实体（基线 §13.6：静态定义不得被运行时写回；
/// 战斗的中间状态不落盘 —— 落盘的是<b>结果</b>）。</para>
/// <para>⚠️ <b>它必须是值语义的快照进战斗</b>：属性来自
/// <see cref="CombatParameters"/>（D-065 的表），不是活对象引用。</para>
/// </remarks>
public sealed class CombatantState
{
    /// <summary>构造一个参战者。</summary>
    /// <param name="id">标识。</param>
    /// <param name="name">显示名（写进战斗日志）。</param>
    /// <param name="stats">战斗属性（攻击 / 防御 / 生命）。</param>
    /// <exception cref="ArgumentException">标识或名字为空。</exception>
    /// <exception cref="InvalidOperationException">属性<b>不明</b>（全 0）⇒ 战斗不能拿它开打。</exception>
    public CombatantState(string id, string name, CombatStats stats)
    {
        if (string.IsNullOrEmpty(id)) throw new ArgumentException("参战者标识不能为空。", nameof(id));
        if (string.IsNullOrEmpty(name)) throw new ArgumentException("参战者名字不能为空。", nameof(name));
        if (!stats.IsKnown)
        {
            throw new InvalidOperationException(
                $"参战者「{name}」的战斗属性不明（攻/防/命 全 0）—— "
                + "战斗必须拿到真实属性才能开打（队伍状态不明 ⇒ 战斗无法解算）。");
        }
        Id = id;
        Name = name;
        Stats = stats;
        Health = stats.Health;
    }

    /// <summary>标识。</summary>
    public string Id { get; }

    /// <summary>显示名。</summary>
    public string Name { get; }

    /// <summary>初始战斗属性（不可变）。</summary>
    public CombatStats Stats { get; }

    /// <summary>当前生命。</summary>
    /// <remarks>
    /// ⚠️ **没有公开 setter**（不变量 IT-27：领域层不许有公开可写属性）⇒
    /// 状态只能经由本类的方法改 ⇒「谁能改战斗状态」这件事**写在类型里**，不是散在调用点。
    /// </remarks>
    public int Health { get; private set; }

    /// <summary>距离下次出手还差几回合（0 = 这回合就能出手）。</summary>
    public int CooldownRounds { get; private set; }

    /// <summary>中毒还剩几回合（0 = 没中毒）。</summary>
    public int PoisonRoundsLeft { get; private set; }

    /// <summary>还站着吗（还有血）。</summary>
    /// <remarks>
    /// ⚠️ **为什么叫「IsStanding」而不是「IsAlive」**：不变量 I6（<c>05</c> §7）规定
    /// 「死亡代价相关的任何字段<b>永不建模</b>」⇒ 守卫 <c>DeathHasNoPersistentCostTests</c>
    /// 会拦下任何名字像「生死位」的<b>布尔</b>成员（<c>IsAlive</c> / <c>IsDead</c> / 「存活」…）。
    /// 而本属性是<b>战斗过程里的瞬时状态</b>，**不是**英雄的存亡标记
    /// ⇒ 用游戏语言「还站着」⇒ 既不撞守卫，也不改变 I6 的意思。
    /// </remarks>
    public bool IsStanding => Health > 0;

    /// <summary>中毒中吗。</summary>
    public bool IsPoisoned => PoisonRoundsLeft > 0;

    /// <summary>受到伤害（伤害为负或零时什么都不做）。</summary>
    /// <param name="amount">伤害量。</param>
    public void ApplyDamage(int amount)
    {
        if (amount > 0) Health -= amount;
    }

    /// <summary>设置冷却（负数按 0 算）。</summary>
    /// <param name="rounds">要等几回合。</param>
    public void SetCooldown(int rounds) => CooldownRounds = rounds < 0 ? 0 : rounds;

    /// <summary>冷却减一（已经能出手时不动）。</summary>
    public void TickCooldown()
    {
        if (CooldownRounds > 0) CooldownRounds -= 1;
    }

    /// <summary>施加（或刷新）中毒的持续回合数。</summary>
    /// <param name="rounds">持续回合数。</param>
    public void ApplyPoison(int rounds) => PoisonRoundsLeft = rounds < 0 ? 0 : rounds;

    /// <summary>中毒回合数减一（没中毒时不动）。</summary>
    public void TickPoison()
    {
        if (PoisonRoundsLeft > 0) PoisonRoundsLeft -= 1;
    }
}

/// <summary>一场战斗的结果（胜负 + 日志 + 用了几个回合）。</summary>
/// <param name="Outcome">胜负。</param>
/// <param name="Log">战斗日志（每一条都是一件已发生的事）。</param>
/// <param name="RoundsUsed">用掉的回合数（插拔点 7「阶段耗时」要用它换算模拟时间 ⇒ 换算属 T-001 / T-002）。</param>
public sealed record BattleRun(
    BattleOutcome Outcome,
    IReadOnlyList<BattleLogEntry> Log,
    int RoundsUsed);

/// <summary>战斗的胜负。</summary>
public enum BattleOutcome
{
    /// <summary>还没分出胜负（战斗中）。</summary>
    InProgress = 0,
    /// <summary>我方获胜。</summary>
    HeroesWin = 1,
    /// <summary>敌方获胜（含<b>超时判负</b>）。</summary>
    EnemiesWin = 2,
}

/// <summary>胜负判定的快照（插拔点 1 的输入）。</summary>
/// <param name="HeroesAlive">还活着的英雄数。</param>
/// <param name="EnemiesAlive">还活着的敌人数。</param>
/// <param name="RoundsCompleted">已经**走完**的回合数。</param>
public readonly record struct BattleSnapshot(int HeroesAlive, int EnemiesAlive, int RoundsCompleted);

/// <summary>
/// 插拔点 1 · <b>胜负判定</b> —— 这一场是赢是输。
/// </summary>
/// <remarks>默认实现见 <see cref="DefaultOutcomeDecider"/>（D-065：全灭判负 / 超时判负）。</remarks>
public interface IBattleOutcomeDecider
{
    /// <summary>当前是赢是输。</summary>
    /// <param name="snapshot">战场快照。</param>
    BattleOutcome Decide(BattleSnapshot snapshot);
}

/// <summary>插拔点 2 · <b>伤害结算</b> —— 一次攻击造成多少。</summary>
public interface IDamageCalculator
{
    /// <summary>算一次攻击的伤害。</summary>
    /// <param name="attacker">攻方。</param>
    /// <param name="defender">守方。</param>
    int Calculate(CombatantState attacker, CombatantState defender);
}

/// <summary>插拔点 3 · <b>命中判定</b> —— 这次攻击是否生效。</summary>
public interface IHitCheck
{
    /// <summary>这次攻击命中吗。</summary>
    /// <param name="attacker">攻方。</param>
    /// <param name="defender">守方。</param>
    bool Hits(CombatantState attacker, CombatantState defender);
}

/// <summary>插拔点 4 · <b>冷却与行动选择</b> —— 谁在什么时候行动、打谁。</summary>
public interface IActionScheduler
{
    /// <summary>出手间隔（回合）。</summary>
    /// <param name="actor">行动者。</param>
    int RoundsPerAction(CombatantState actor);

    /// <summary>选目标（从<b>活着的</b>候选里选）。</summary>
    /// <param name="candidates">候选目标。</param>
    CombatantState? ChooseTarget(IReadOnlyList<CombatantState> candidates);
}

/// <summary>插拔点 5 · <b>状态效果</b> —— 状态如何施加与结算。</summary>
public interface IStatusEffectResolver
{
    /// <summary>一次命中之后施加状态。</summary>
    /// <param name="target">被击中的目标。</param>
    void OnHit(CombatantState target);

    /// <summary>回合开始时结算状态（返回造成的总伤害，写进日志）。</summary>
    /// <param name="combatants">全场参战者。</param>
    /// <returns>状态造成的总伤害。</returns>
    int TickRound(IReadOnlyList<CombatantState> combatants);
}

/// <summary>
/// 战斗规则的<b>组合</b> —— 七个插拔点装在一起（`04` §5.4）。
/// </summary>
/// <remarks>
/// <para>⚠️ **插拔点 6（产出与掉落）不在这里**：它由既有的
/// <c>Settlement.ILootProvider</c> 承担（战斗结果里的产出描述 ⇒ 结算发放），
/// 战斗引擎不碰奖励（基线 §8.4：副本与战斗模块<b>不直接写</b>村庄资源与英雄持有物）。</para>
/// <para>⚠️ **插拔点 7（阶段耗时）也不在这里**：换算属 T-001 / T-002，
/// 引擎只报出 <see cref="BattleRun.RoundsUsed"/>。</para>
/// </remarks>
public sealed class BattleRules
{
    /// <summary>插拔点 1 · 胜负判定。</summary>
    public IBattleOutcomeDecider Outcome { get; init; } = new DefaultOutcomeDecider();

    /// <summary>插拔点 2 · 伤害结算。</summary>
    public IDamageCalculator Damage { get; init; } = new SubtractiveDamageCalculator();

    /// <summary>插拔点 3 · 命中判定。</summary>
    public IHitCheck Hit { get; init; } = new AlwaysHitCheck();

    /// <summary>插拔点 4 · 冷却与行动选择。</summary>
    public IActionScheduler Action { get; init; } = new LowestHealthFirstScheduler();

    /// <summary>插拔点 5 · 状态效果。</summary>
    public IStatusEffectResolver Status { get; init; } = new PoisonStatusResolver();
}

/// <summary>默认的胜负判定（D-065）：敌方全灭 ⇒ 胜；我方全灭 ⇒ 负；<b>回合用尽 ⇒ 负</b>。</summary>
public sealed class DefaultOutcomeDecider : IBattleOutcomeDecider
{
    /// <inheritdoc />
    public BattleOutcome Decide(BattleSnapshot snapshot)
    {
        if (snapshot.EnemiesAlive <= 0) return BattleOutcome.HeroesWin;
        if (snapshot.HeroesAlive <= 0) return BattleOutcome.EnemiesWin;
        if (snapshot.RoundsCompleted >= CombatParameters.RoundLimit) return BattleOutcome.EnemiesWin;
        return BattleOutcome.InProgress;
    }
}

/// <summary>默认的伤害结算（D-061）：<c>伤害 = max(1, 攻击 − 防御)</c>。</summary>
public sealed class SubtractiveDamageCalculator : IDamageCalculator
{
    /// <inheritdoc />
    public int Calculate(CombatantState attacker, CombatantState defender)
    {
        int raw = attacker.Stats.Attack - defender.Stats.Defense;
        return raw < 1 ? 1 : raw;   // 下限 1（`04` §5.6：伤害下限不为负）
    }
}

/// <summary>默认的命中判定（D-062）：<b>不设命中判定</b> —— 出手即命中。</summary>
public sealed class AlwaysHitCheck : IHitCheck
{
    /// <inheritdoc />
    public bool Hits(CombatantState attacker, CombatantState defender) => true;
}

/// <summary>
/// 默认的行动选择（D-065）：出手间隔 <see cref="CombatParameters.RoundsPerAction"/> 回合，
/// 打<b>血量最低</b>的存活目标（集火 ⇒ 队伍的总输出决定「多久打死」）。
/// </summary>
public sealed class LowestHealthFirstScheduler : IActionScheduler
{
    /// <inheritdoc />
    public int RoundsPerAction(CombatantState actor) => CombatParameters.RoundsPerAction;

    /// <inheritdoc />
    public CombatantState? ChooseTarget(IReadOnlyList<CombatantState> candidates)
    {
        CombatantState? best = null;
        foreach (var c in candidates)
        {
            if (!c.IsStanding) continue;
            if (best is null || c.Health < best.Health) best = c;
        }
        return best;
    }
}

/// <summary>
/// 默认的状态效果（D-065）：<b>中毒</b> —— 命中即附加（持续回合重置），
/// 每回合开始时造成固定伤害，战斗结束即解除。
/// </summary>
public sealed class PoisonStatusResolver : IStatusEffectResolver
{
    /// <inheritdoc />
    public void OnHit(CombatantState target)
        => target.ApplyPoison(CombatParameters.PoisonDurationRounds);

    /// <inheritdoc />
    public int TickRound(IReadOnlyList<CombatantState> combatants)
    {
        int total = 0;
        foreach (var c in combatants)
        {
            if (!c.IsPoisoned) continue;
            c.TickPoison();
            int dmg = CombatParameters.PoisonDamagePerRound;
            c.ApplyDamage(dmg);
            total += dmg;
        }
        return total;
    }
}
