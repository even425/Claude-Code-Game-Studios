using System;
using System.Collections.Generic;
using System.Linq;
using ClaudeCodeGameStudios.Domain.Combat;
using ClaudeCodeGameStudios.Domain.Dungeon;

namespace ClaudeCodeGameStudios.Application;

/// <summary>一次兽潮战斗的结果。</summary>
/// <param name="Won">守住了吗。</param>
/// <param name="Rounds">打了几回合（三阶段合计）。</param>
/// <param name="Participants">参战的英雄。</param>
public readonly record struct BeastTideBattleResult(
    bool Won,
    int Rounds,
    IReadOnlyList<string> Participants);

/// <summary>
/// <b>兽潮的战斗判定</b>（D-082）：<b>触发前先问一次「守得住吗」</b>。
/// </summary>
/// <remarks>
/// <para><b>为什么要有这一步</b>：实测发现，<b>弱阵容（挂机）在任何缩放下都必输</b>
/// （30 回合超时）⇒ 兽潮对弱村子就是一笔「<b>必输的税</b>」——
/// 每 7 天扣一次材料与金币，而村子<b>无论如何也守不住</b>。
/// ⇒ D-082 拍板：「<b>守不住就别触发兽潮</b>」⇒ 弱村子不被惩罚，
/// 而守得住的村子才真的面对风险。</para>
///
/// <para>⚠️ <b>战斗是纯函数</b>（D-062：不设命中判定 ⇒ 没有随机数）
/// ⇒ 「先打一遍看看守不守得住」<b>不产生任何副作用</b>，
/// 且<b>与真正的结算走同一段代码</b> ⇒ 预演与实战不可能不一致。</para>
///
/// <para>⚠️ <b>判定发生在「触发那一刻」</b>：那一刻守得住才会来。
/// ⇒ 变强（练英雄、试炼塔加生命）要在<b>下一次</b>兽潮之前生效。</para>
/// </remarks>
public static class BeastTideBattle
{
    /// <summary>
    /// 打一场兽潮（<b>纯计算，不改任何状态</b>）。
    /// </summary>
    /// <param name="participants">参战英雄（空 ⇒ 没人守）。</param>
    /// <param name="heroStats">英雄战斗属性查询。</param>
    /// <param name="resolver">战斗解算器。</param>
    /// <param name="outcomeReader">胜负读者（与解算器成对）。</param>
    /// <param name="runId">
    /// 这次战斗的标识。<b>要稳定</b>：战斗本身没有随机数（D-062），
    /// 但标识会进战报与幂等键 ⇒ 用随机数会让「同一份输入得到同一场战斗」这句话不成立。
    /// </param>
    /// <returns>战斗结果。</returns>
    public static BeastTideBattleResult Resolve(
        IReadOnlyList<string> participants,
        Func<string, CombatStats> heroStats,
        IBattleResolver resolver,
        IBattleOutcomeReader outcomeReader,
        string runId = "beasttide:probe")
    {
        if (participants is null) throw new ArgumentNullException(nameof(participants));
        if (heroStats is null) throw new ArgumentNullException(nameof(heroStats));
        if (resolver is null) throw new ArgumentNullException(nameof(resolver));
        if (outcomeReader is null) throw new ArgumentNullException(nameof(outcomeReader));

        // 一个参与者都没有 ⇒ 没人守 ⇒ 必败（不需要真的打一场）。
        if (participants.Count == 0)
        {
            return new BeastTideBattleResult(false, 0, Array.Empty<string>());
        }

        // ⚠️ **用英雄自己的属性**（D-080）：写死的属性会让「谁去守村」毫无意义。
        var party = new CombatPartySnapshot(participants
            .Select(id => CombatantSnapshot.Create(id, id, stats: heroStats(id)))
            .ToArray());

        var flow = DungeonFlow.Begin(
            new DungeonRunId(runId),
            "兽潮", participants, resolver, outcomeReader, party: party);
        var outcome = flow.RunToCompletion();

        return new BeastTideBattleResult(outcome.Succeeded, outcome.TotalRoundsUsed, participants.ToArray());
    }

    /// <summary>
    /// 守得住吗（<b>触发前的判定</b>；纯计算，不改状态）。
    /// </summary>
    /// <param name="participants">参战英雄。</param>
    /// <param name="heroStats">英雄战斗属性查询。</param>
    /// <param name="resolver">战斗解算器。</param>
    /// <param name="outcomeReader">胜负读者。</param>
    /// <returns>守得住为 true。</returns>
    public static bool CanHold(
        IReadOnlyList<string> participants,
        Func<string, CombatStats> heroStats,
        IBattleResolver resolver,
        IBattleOutcomeReader outcomeReader)
        => Resolve(participants, heroStats, resolver, outcomeReader).Won;
}
