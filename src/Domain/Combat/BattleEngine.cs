using System;
using System.Collections.Generic;
using System.Linq;

namespace ClaudeCodeGameStudios.Domain.Combat;

/// <summary>战斗输入（插拔点装配的输入）。</summary>
/// <param name="EncounterName">遭遇名（写进日志）。</param>
/// <param name="Heroes">我方参战者（带战斗属性）。</param>
/// <param name="Enemies">敌方参战者（怪物）。</param>
public sealed record BattleInput(
    string EncounterName,
    IReadOnlyList<CombatantState> Heroes,
    IReadOnlyList<CombatantState> Enemies);

/// <summary>战斗结果里的**胜负标记**（写进决定性条目，胜负读者靠它读回结论）。</summary>
/// <remarks>
/// ⚠️ **标记必须与「过程记录」分得开**：读者按标记判断结论，
/// 而日志的其余文字是人读的（D5：玩家要能看懂谁为什么赢）。
/// </remarks>
public static class BattleOutcomeMarkers
{
    /// <summary>我方获胜的标记。</summary>
    public const string HeroesWin = "【我方胜】";

    /// <summary>敌方获胜的标记（含超时判负）。</summary>
    public const string EnemiesWin = "【敌方胜】";
}

/// <summary>
/// 战斗引擎 —— 按 <c>04</c> §5.4 的**七个插拔点**跑一场遭遇。
/// </summary>
/// <remarks>
/// <para><b>它做什么</b>：回合循环 ⇒ 状态结算 ⇒ 胜负判定 ⇒ 出手（命中 / 伤害 / 状态附加）
/// ⇒ 产出**一条条日志**与一个胜负结论。</para>
///
/// <para><b>它不做什么</b>（三条硬边界）：
/// <list type="number">
/// <item><b>不发放奖励</b>（基线 §8.4 / `05` §2.3）：产出由结算模块处理（插拔点 6 = 既有的
/// <c>ILootProvider</c>）⇒ 战斗引擎<b>不碰</b>村庄资源与英雄持有物。</item>
/// <item><b>不决定副本的成败</b>：它只判「这一场谁赢」；副本的成败由
/// <c>DungeonRun</c> 按阶段流转判定（<c>04</c> §3.1 L4）。</item>
/// <item><b>不落盘</b>：战斗的中间状态是**过程**，落盘的是结果（基线 §13.6）。</item>
/// </list></para>
///
/// <para><b>为什么没有随机数</b>：D-062 决定<b>不设命中判定</b> ⇒ 战斗里**没有随机** ⇒
/// 同一份输入永远得到同一场战斗（基线 §14.2「涉及随机性的系统使用固定种子」在这里
/// 变成「**根本不涉及随机性**」）⇒ 这也是 IT-31 那种对照实验能写出来的前提。</para>
///
/// <para><b>回合的计法</b>：战斗内用<b>回合计数</b>（回合 ↔ 模拟时间的换算属 T-001 / T-002）
/// ⇒ <see cref="BattleRun.RoundsUsed"/> 报出用了几回合，转换留给调用方。</para>
/// </remarks>
public sealed class BattleEngine
{
    private readonly BattleRules _rules;

    /// <summary>构造一个引擎。</summary>
    /// <param name="rules">插拔点的组合；<c>null</c> 用默认那一套（D-065）。</param>
    public BattleEngine(BattleRules? rules = null)
    {
        _rules = rules ?? new BattleRules();
    }

    /// <summary>跑一场遭遇。</summary>
    /// <param name="input">战斗输入。</param>
    /// <returns>战斗结果（胜负 + 日志 + 用掉的回合数）。</returns>
    /// <exception cref="ArgumentException">双方有一方是空的。</exception>
    public BattleRun Run(BattleInput input)
    {
        if (input is null) throw new ArgumentNullException(nameof(input));
        if (input.Heroes is null || input.Heroes.Count == 0)
            throw new ArgumentException("战斗至少要有一名英雄。", nameof(input));
        if (input.Enemies is null || input.Enemies.Count == 0)
            throw new ArgumentException("战斗至少要有一个敌人。", nameof(input));

        var heroes = input.Heroes.ToList();
        var enemies = input.Enemies.ToList();
        var log = new List<BattleLogEntry>();
        int seq = 0;
        int roundsCompleted = 0;

        log.Add(Entry(ref seq, input.EncounterName, $"{input.EncounterName}：{Describe(heroes)} 对 {Describe(enemies)}。", false));

        BattleOutcome outcome = BattleOutcome.InProgress;
        while (outcome == BattleOutcome.InProgress)
        {
            // ── 1. 回合开始：状态结算（插拔点 5）────────────────────────
            //    记下结算前的血量 ⇒ 结算后**新倒下**的那些（中毒致死）才打一条记录
            //    （否则每回合都会把已经倒下的人再报一次）。
            var healthBefore = All(heroes, enemies).ToDictionary(c => c, c => c.Health);
            int poisonDamage = _rules.Status.TickRound(All(heroes, enemies));
            if (poisonDamage > 0)
            {
                log.Add(Entry(ref seq, input.EncounterName, $"{input.EncounterName}：中毒结算，全场共受 {poisonDamage} 点伤害。", false));
            }
            foreach (var c in All(heroes, enemies))
            {
                if (healthBefore[c] > 0 && c.Health <= 0)
                {
                    log.Add(Entry(ref seq, input.EncounterName, $"{c.Name} 中毒倒下了。", false));
                }
            }

            // ── 2. 胜负判定（插拔点 1）──────────────────────────────────
            outcome = _rules.Outcome.Decide(Snapshot(heroes, enemies, roundsCompleted));
            if (outcome != BattleOutcome.InProgress)
            {
                log.Add(Entry(ref seq, input.EncounterName, DecisiveSummary(input.EncounterName, outcome, roundsCompleted, null), true));
                break;
            }

            // ── 3. 出手：英雄方先，然后敌方 ────────────────────────────
            foreach (var side in new[] { heroes, enemies })
            {
                var opponents = ReferenceEquals(side, heroes) ? enemies : heroes;

                foreach (var actor in side)
                {
                    if (!actor.IsStanding) continue;

                    if (actor.CooldownRounds > 0)
                    {
                        actor.TickCooldown();   // 这一回合不出手
                        continue;
                    }

                    var target = _rules.Action.ChooseTarget(opponents.Where(c => c.IsStanding).ToList());
                    if (target is null) continue;   // 对面已经没人了（判定会接手）

                    if (_rules.Hit.Hits(actor, target))
                    {
                        int damage = _rules.Damage.Calculate(actor, target);
                        target.ApplyDamage(damage);
                        log.Add(Entry(ref seq, input.EncounterName,
                            $"{input.EncounterName}：{actor.Name} 攻击 {target.Name}，造成 {damage} 点伤害。",
                            false));
                        _rules.Status.OnHit(target);

                        if (!target.IsStanding)
                        {
                            // 这一击是不是**结束了整场战斗**？⇒ 是的话它就是决定性条目
                            var after = _rules.Outcome.Decide(Snapshot(heroes, enemies, roundsCompleted));
                            if (after != BattleOutcome.InProgress)
                            {
                                outcome = after;
                                log.Add(Entry(ref seq, input.EncounterName,
                                    DecisiveSummary(input.EncounterName, outcome, roundsCompleted, actor),
                                    true));
                                break;
                            }
                        }
                    }

                    actor.SetCooldown(_rules.Action.RoundsPerAction(actor) - 1);
                }

                if (outcome != BattleOutcome.InProgress) break;
            }

            if (outcome != BattleOutcome.InProgress) break;

            // ── 4. 回合结束：数回合数，再判一次（含超时判负）──────────
            roundsCompleted += 1;
            outcome = _rules.Outcome.Decide(Snapshot(heroes, enemies, roundsCompleted));
            if (outcome != BattleOutcome.InProgress)
            {
                log.Add(Entry(ref seq, input.EncounterName, DecisiveSummary(input.EncounterName, outcome, roundsCompleted, null), true));
                break;
            }
        }

        return new BattleRun(outcome, log, roundsCompleted);
    }

    private static BattleSnapshot Snapshot(
        List<CombatantState> heroes, List<CombatantState> enemies, int roundsCompleted)
        => new(heroes.Count(c => c.IsStanding), enemies.Count(c => c.IsStanding), roundsCompleted);

    private static List<CombatantState> All(
        List<CombatantState> heroes, List<CombatantState> enemies)
        => heroes.Concat(enemies).ToList();

    private static string Describe(IReadOnlyList<CombatantState> side)
        => string.Join("、", side.Select(c => $"{c.Name}（攻 {c.Stats.Attack} / 防 {c.Stats.Defense} / 命 {c.Stats.Health}）"));

    private static string DecisiveSummary(
        string encounter, BattleOutcome outcome, int roundsCompleted, CombatantState? killer)
    {
        string marker = outcome == BattleOutcome.HeroesWin
            ? BattleOutcomeMarkers.HeroesWin
            : BattleOutcomeMarkers.EnemiesWin;

        if (killer is not null)
        {
            return $"{encounter}：{killer.Name} 击倒了对手 ⇒ {marker}";
        }
        if (outcome == BattleOutcome.EnemiesWin)
        {
            return $"{encounter}：{roundsCompleted} 回合用尽，判定失败 ⇒ {marker}";
        }
        return $"{encounter}：战斗结束 ⇒ {marker}";
    }

    /// <summary>
    /// 造一条日志条目。
    /// <para>⚠️ <b>条目标识带遭遇名</b> ⇒ 两次不同遭遇的日志**不会撞 ID**
    /// （否则把两份日志并排放时，「001」指哪一条就说不清了）。</para>
    /// </summary>
    private static BattleLogEntry Entry(ref int seq, string encounter, string summary, bool decisive)
        => new($"{encounter}#{seq++:D2}", summary, decisive);}
