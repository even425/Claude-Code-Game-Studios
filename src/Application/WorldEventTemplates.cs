using System;
using System.Collections.Generic;
using System.Linq;
using ClaudeCodeGameStudios.Domain.Combat;
using ClaudeCodeGameStudios.Domain.Dungeon;
using ClaudeCodeGameStudios.Domain.WorldEvents;

namespace ClaudeCodeGameStudios.Application;

/// <summary>
/// 大事件的<b>两个实例</b>（D-079）：<b>兽潮</b>与<b>商队来访</b>。
/// </summary>
/// <remarks>
/// <para><b>为什么要有两个</b>（基线 §11.5 原话）：
/// 「框架的可扩展性需要至少两个实例来验证 —— <b>用一个实例抽象出的框架，
/// 无法区分「通用」与「该实例专属」</b>。」
/// ⇒ 这两个实例刻意<b>形状不同</b>：
/// <list type="bullet">
/// <item><b>兽潮</b>：有战斗、有参与者、会死伤 ⇒ 阶段多（预兆→准备→进行中→结算→后续影响→结束）。</item>
/// <item><b>商队来访</b>：<b>没有战斗</b>，只带来货物与机会 ⇒ 阶段少（预兆→进行中→结算→结束）
///       ⇒ 它证明<b>框架没有把「战斗」写死</b>。</item>
/// </list></para>
///
/// <para>⚠️ <b>本类只「描述」后果，不直接改状态</b>（基线 §11.4）：
/// 兽潮的战斗由 <see cref="FormulaCombat"/> 跑（战斗系统的事），
/// 后果的应用由调用方（应用层）按各自的系统入口做。</para>
/// </remarks>
public static class WorldEventTemplates
{
    /// <summary>兽潮的模板标识。</summary>
    public const string BeastTideId = "beast_tide";

    /// <summary>商队来访的模板标识。</summary>
    public const string CaravanId = "caravan_visit";

    /// <summary>
    /// 兽潮：<b>周期刷怪，村庄要防守</b>（D-079）。
    /// </summary>
    /// <param name="periodDays">触发周期（游戏内天数）。</param>
    /// <param name="resolver">战斗解算器（兽潮的战斗由它跑）。</param>
    /// <param name="outcomeReader">胜负读者（与解算器成对）。</param>
    /// <returns>模板。</returns>
    /// <remarks>
    /// ⚠️ <b>它没有「专用字段」</b>：兽潮的「刷几只怪」之类的参数
    /// <b>不写在模板里</b>（模板是规则，数值走参数/实例）⇒
    /// 换第二个「刷怪」事件时<b>不需要改框架</b>。
    /// </remarks>
    public static WorldEventTemplate BeastTide(
        int periodDays,
        IBattleResolver resolver,
        IBattleOutcomeReader outcomeReader)
    {
        if (resolver is null) throw new ArgumentNullException(nameof(resolver));
        if (outcomeReader is null) throw new ArgumentNullException(nameof(outcomeReader));

        return new WorldEventTemplate
        {
            Id = BeastTideId,
            Name = "兽潮",
            PeriodDays = periodDays,
            Phases = new[]
            {
                WorldEventPhase.Omen,          // 预兆：村民先看到远处的动静
                WorldEventPhase.Preparation,   // 准备：集结、备货
                WorldEventPhase.Ongoing,       // 进行中：守村
                WorldEventPhase.Settlement,    // 结算：算这一波的结果
                WorldEventPhase.Aftermath,     // 后续影响：伤者与损失落到村庄
                WorldEventPhase.Finished,      // 结束
            },
            Settle = (instance, participants) =>
            {
                // 兽潮的战斗：**一个参与者都没有 ⇒ 守住了**；
                // 有参与者 ⇒ 他们打了一场。
                bool defended = participants.Count > 0;
                var consequences = new Dictionary<string, string>(StringComparer.Ordinal);

                if (defended && participants.Count > 0)
                {
                    // 真打一场：用战斗系统解算（战斗是确定性的 ⇒ 同结果）。
                    var party = new CombatPartySnapshot(participants
                        .Select((id, i) => CombatantSnapshot.Create(
                            id, id, stats: new CombatStats(6, 2, 60)))
                        .ToArray());

                    var flow = DungeonFlow.Begin(
                        new DungeonRunId($"beasttide:{instance.Id}"),
                        "兽潮", participants, resolver, outcomeReader, party: party);
                    var outcome = flow.RunToCompletion();
                    bool won = outcome.Succeeded;

                    consequences["beast_tide/defended"] = won ? "守住了" : "没守住";
                    consequences["beast_tide/participants"] = string.Join("、", participants);
                    return new WorldEventOutcome(won, consequences);
                }

                consequences["beast_tide/defended"] = "没守住";
                consequences["beast_tide/participants"] = "（无人防守）";
                return new WorldEventOutcome(false, consequences);
            },
        };
    }

    /// <summary>
    /// 商队来访：<b>没有战斗</b>，外来的商队来村里做买卖（D-079）。
    /// </summary>
    /// <param name="periodDays">触发周期（游戏内天数）。</param>
    /// <returns>模板。</returns>
    /// <remarks>
    /// ⚠️ <b>它为什么重要</b>：它的阶段形状与兽潮<b>不同</b>（少两个阶段），
    /// 而且<b>完全不涉及战斗</b> ⇒ 它是「框架是通用的」这件事的<b>可验证证据</b>：
    /// 若框架把「进行中 ⇒ 打一场」写死了，这个实例根本跑不起来。
    /// </remarks>
    public static WorldEventTemplate Caravan(int periodDays)
    {
        return new WorldEventTemplate
        {
            Id = CaravanId,
            Name = "商队来访",
            PeriodDays = periodDays,
            Phases = new[]
            {
                WorldEventPhase.Omen,       // 预兆：听说有商队要来
                WorldEventPhase.Ongoing,    // 进行中：商队到了，摆摊买卖
                WorldEventPhase.Settlement, // 结算：算这次买卖的结果
                WorldEventPhase.Finished,   // 结束
            },
            Settle = (instance, participants) =>
            {
                var consequences = new Dictionary<string, string>(StringComparer.Ordinal);
                consequences["caravan/goods"] = "外地来的货物";
                consequences["caravan/participants"] = string.Join("、", participants);
                // 商队**没有战斗** ⇒ 「守住」这种概念在这里根本不出现。
                return new WorldEventOutcome(Succeeded: true, consequences);
            },
        };
    }
}
