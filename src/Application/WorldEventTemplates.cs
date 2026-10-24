using System;
using System.Collections.Generic;
using System.Linq;
using ClaudeCodeGameStudios.Domain.Combat;
using ClaudeCodeGameStudios.Domain.Dungeon;
using ClaudeCodeGameStudios.Domain.WorldEvents;

namespace ClaudeCodeGameStudios.Application;

/// <summary>
/// <b>兽潮的数值</b>（D-080）—— 打赢给什么、打输赔什么。
/// </summary>
/// <remarks>
/// <para>⚠️ <b>这里的数是「基准值」，不是最终值</b>：真正落地的是
/// <see cref="ScaledToTier"/> 按<b>村庄层级</b>放大后的值
/// （D-080 拍板：<b>兽潮的规模随村庄层级递增</b>）。</para>
/// <para>⚠️ <b>具体数值由开发者拍板</b>；代码里这些是**占位基准**，
/// 由标定实测给出建议值后再定（见 `07` D-080）。</para>
/// </remarks>
public sealed record BeastTideTuning
{
    /// <summary>打赢时给的<b>材料</b>（基准）。</summary>
    public long MaterialReward { get; init; }

    /// <summary>打赢时给的<b>金币</b>（基准）。</summary>
    public long GoldReward { get; init; }

    /// <summary>打输时赔的<b>材料</b>（基准）。</summary>
    public long MaterialLoss { get; init; }

    /// <summary>打输时赔的<b>金币</b>（基准）。</summary>
    public long GoldLoss { get; init; }

    /// <summary>
    /// 按村庄层级放大：层级 <c>t</c> ⇒ 四项都乘以 <c>(1 + t)</c>。
    /// </summary>
    /// <remarks>
    /// <para>⚠️ <b>为什么胜负都放大</b>：只放大奖励 ⇒ 村庄越强，打赢的收益越大
    /// （白捡）；只放大损失 ⇒ 村庄越强，一次失手越致命（劝退）。
    /// 两边同倍 ⇒ <b>风险与回报同步增长</b>，兽潮始终是个「值得紧张」的事件。</para>
    /// <para>层级为负（不可能发生）当作 0 处理。</para>
    /// </remarks>
    /// <param name="tier">村庄层级（0 起）。</param>
    /// <returns>放大后的数值。</returns>
    public BeastTideTuning ScaledToTier(int tier)
    {
        if (tier <= 0) return this;
        long factor = 1 + tier;
        return this with
        {
            MaterialReward = MaterialReward * factor,
            GoldReward = GoldReward * factor,
            MaterialLoss = MaterialLoss * factor,
            GoldLoss = GoldLoss * factor,
        };
    }
}

/// <summary>
/// 大事件的<b>两个实例</b>（D-079）：<b>兽潮</b>与<b>商队来访</b>。
/// </summary>
/// <remarks>
/// <para><b>为什么要有两个</b>（基线 §11.5 原话）：
/// 「框架的可扩展性需要至少两个实例来验证 —— <b>用一个实例抽象出的框架，
/// 无法区分「通用」与「该实例专属」</b>。」
/// ⇒ 这两个实例刻意<b>形状不同</b>：
/// <list type="bullet">
/// <item><b>兽潮</b>：有战斗、有参与者、有人畜无损但**村库要赔** ⇒ 阶段多
///       （预兆→准备→进行中→结算→后续影响→结束）。</item>
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

    /// <summary>盗匪来袭的模板标识（D-096）。</summary>
    public const string BanditRaidId = "bandit_raid";

    /// <summary>
    /// 兽潮：<b>周期刷怪，村庄要防守</b>（D-079 / D-080）。
    /// </summary>
    /// <param name="periodDays">触发周期（游戏内天数）。</param>
    /// <param name="resolver">战斗解算器（兽潮的战斗由它跑）。</param>
    /// <param name="outcomeReader">胜负读者（与解算器成对）。</param>
    /// <param name="heroStats">
    /// 英雄战斗属性查询（按英雄标识）。<b>用英雄自己的属性</b>，不是写死的数 ——
    /// 否则「谁去守村」这件事对结果没有任何影响。
    /// </param>
    /// <param name="tuning">
    /// 兽潮数值（按<b>当前</b>村庄层级取值）。<b>结算那一刻</b>才取 ⇒
    /// 事件跨日进行时村庄升级了，结算按新层级算。
    /// </param>
    /// <returns>模板。</returns>
    /// <remarks>
    /// ⚠️ <b>它没有「专用字段」</b>：兽潮的「刷几只怪」之类的参数
    /// <b>不写在模板里</b>（模板是规则，数值走参数/实例）⇒
    /// 换第二个「刷怪」事件时<b>不需要改框架</b>。
    /// <para>⚠️ <b>敌方强度不在这里</b>：怪物属性由<b>战斗解算器</b>决定
    /// （规模随层级递增是战斗系统的事）⇒ 本模板只负责「让解算器去打」。</para>
    /// </remarks>
    public static WorldEventTemplate BeastTide(
        int periodDays,
        IBattleResolver resolver,
        IBattleOutcomeReader outcomeReader,
        Func<string, CombatStats> heroStats,
        Func<BeastTideTuning> tuning)
    {
        if (resolver is null) throw new ArgumentNullException(nameof(resolver));
        if (outcomeReader is null) throw new ArgumentNullException(nameof(outcomeReader));
        if (heroStats is null) throw new ArgumentNullException(nameof(heroStats));
        if (tuning is null) throw new ArgumentNullException(nameof(tuning));

        return new WorldEventTemplate
        {
            Id = BeastTideId,
            Name = "兽潮",
            PeriodDays = periodDays,
            // ⚠️ **D-121：这是战斗型事件** ⇒ 参战者的装备会磨损（耐久 -1）。
            IsCombat = true,
            Phases = new[]
            {
                WorldEventPhase.Omen,          // 预兆：村民先看到远处的动静
                WorldEventPhase.Preparation,   // 准备：集结、备货
                WorldEventPhase.Ongoing,       // 进行中：守村
                WorldEventPhase.Settlement,    // 结算：算这一波的结果
                WorldEventPhase.Aftermath,     // 后续影响：损失落到村庄
                WorldEventPhase.Finished,      // 结束
            },
            Settle = (instance, participants) =>
            {
                var consequences = new Dictionary<string, WorldEventEffect>(StringComparer.Ordinal);
                var values = tuning();

                // 兽潮的战斗：**一个参与者都没有 ⇒ 守住了**；
                // 有参与者 ⇒ 他们打了一场。
                bool defended = participants.Count > 0;

                if (defended)
                {
                    // ⚠️ **走共享的那份判定**（`BeastTideBattle.Resolve`）——
                    // 触发前问「守得住吗」用的也是它 ⇒ 预演与实战**不可能不一致**。
                    var battle = BeastTideBattle.Resolve(
                        participants, heroStats, resolver, outcomeReader,
                        runId: $"beasttide:{instance.Id}");

                    consequences["beast_tide/defended"] = new WorldEventEffect(
                        WorldEventEffect.KindNone, 0L, battle.Won ? "守住了" : "没守住");
                    consequences["beast_tide/participants"] = WorldEventEffect.None(
                        string.Join("、", participants));

                    // ── 后果落到村庄的资源上（D-080 拍板）──
                    if (battle.Won)
                    {
                        consequences["beast_tide/material"] = new WorldEventEffect(
                            WorldEventEffect.KindMaterial, values.MaterialReward,
                            $"兽潮守住：材料 +{values.MaterialReward}");
                        consequences["beast_tide/gold"] = new WorldEventEffect(
                            WorldEventEffect.KindGold, values.GoldReward,
                            $"兽潮守住：金币 +{values.GoldReward}");
                    }
                    else
                    {
                        // ⚠️ **只损资源，人畜无损**（D-080 拍板）：不扣英雄、不损建筑。
                        consequences["beast_tide/material"] = new WorldEventEffect(
                            WorldEventEffect.KindMaterial, -values.MaterialLoss,
                            $"兽潮没守住：材料 -{values.MaterialLoss}");
                        consequences["beast_tide/gold"] = new WorldEventEffect(
                            WorldEventEffect.KindGold, -values.GoldLoss,
                            $"兽潮没守住：金币 -{values.GoldLoss}");
                    }

                    return new WorldEventOutcome(battle.Won, consequences);
                }

                // 没人守 ⇒ 必然没守住 ⇒ 照样赔（村库那部分照扣）。
                consequences["beast_tide/defended"] = new WorldEventEffect(
                    WorldEventEffect.KindNone, 0L, "没守住");
                consequences["beast_tide/participants"] = WorldEventEffect.None("（无人防守）");
                consequences["beast_tide/material"] = new WorldEventEffect(
                    WorldEventEffect.KindMaterial, -values.MaterialLoss,
                    $"兽潮没守住：材料 -{values.MaterialLoss}");
                consequences["beast_tide/gold"] = new WorldEventEffect(
                    WorldEventEffect.KindGold, -values.GoldLoss,
                    $"兽潮没守住：金币 -{values.GoldLoss}");

                return new WorldEventOutcome(false, consequences);
            },
        };
    }

    /// <summary>
    /// 商队来访：<b>没有战斗</b>，外来的商队来村里做买卖（D-079 / D-080）。
    /// </summary>
    /// <param name="periodDays">触发周期（游戏内天数）。</param>
    /// <param name="offer">
    /// 商队的报价（物品 / 单价 / 库存 / 窗口天数）。<b>模板给规则，实例存状态</b>。
    /// </param>
    /// <returns>模板。</returns>
    /// <remarks>
    /// ⚠️ <b>它为什么重要</b>：它的阶段形状与兽潮<b>不同</b>（少两个阶段），
    /// 而且<b>完全不涉及战斗</b> ⇒ 它是「框架是通用的」这件事的<b>可验证证据</b>：
    /// 若框架把「进行中 ⇒ 打一场」写死了，这个实例根本跑不起来。
    /// </remarks>
    public static WorldEventTemplate Caravan(int periodDays, CaravanOffer offer)
    {
        if (offer is null) throw new ArgumentNullException(nameof(offer));

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
                var consequences = new Dictionary<string, WorldEventEffect>(StringComparer.Ordinal);
                consequences["caravan/goods"] = WorldEventEffect.None(
                    $"外地来的货物：{offer.ItemId} × {offer.Stock}");
                consequences["caravan/participants"] = WorldEventEffect.None(
                    string.Join("、", participants));
                // 商队**没有战斗** ⇒ 「守住」这种概念在这里根本不出现。
                return new WorldEventOutcome(Succeeded: true, consequences);
            },
        };
    }

    /// <summary>
    /// 盗匪来袭：<b>三个大事件实例之一</b>（D-096）。
    /// </summary>
    /// <param name="periodDays">触发周期（游戏内天数）。</param>
    /// <param name="resolver">战斗解算器（盗匪的战斗由它跑）。</param>
    /// <param name="outcomeReader">胜负读者（与解算器成对）。</param>
    /// <param name="heroStats">英雄战斗属性查询（按英雄标识）。</param>
    /// <param name="tuning">盗匪数值（建议值，允许调）。</param>
    /// <returns>模板。</returns>
    /// <remarks>
    /// <para>⚠️ <b>它与兽潮的形状不同</b>（这正是基线 §11.5 要的「通用性证据」）：
    /// <list type="bullet">
    /// <item><b>强度不随村庄层级增长</b>（盗匪不会因为你村子变大而变强）⇒ 解算器用固定档位。</item>
    /// <item><b>人才是「出击」而不是「守村」</b>：打赢→缓获（村库 + 材料 + 金币），打败→被抢（村库 − 材料）。</item>
    /// <item><b>阶段少一个</b>（没有「后续影响」）⇒ 证明「阶段可跳过」。</item>
    /// </list></para>
    /// <para>⚠️ <b>不设「守不住就不触发」</b>（那是兽潮特有的，D-082）⇒ 盗匪来了就要应对。</para>
    /// </remarks>
    public static WorldEventTemplate BanditRaid(
        int periodDays,
        IBattleResolver resolver,
        IBattleOutcomeReader outcomeReader,
        Func<string, CombatStats> heroStats,
        Func<BanditTuning> tuning)
    {
        if (resolver is null) throw new ArgumentNullException(nameof(resolver));
        if (outcomeReader is null) throw new ArgumentNullException(nameof(outcomeReader));
        if (heroStats is null) throw new ArgumentNullException(nameof(heroStats));
        if (tuning is null) throw new ArgumentNullException(nameof(tuning));

        return new WorldEventTemplate
        {
            Id = BanditRaidId,
            Name = "盗匪来袭",
            PeriodDays = periodDays,
            // ⚠️ **D-121：这是战斗型事件** ⇒ 出击者的装备会磨损（耐久 -1）。
            IsCombat = true,
            Phases = new[]
            {
                WorldEventPhase.Omen,          // 预兆：有人报说路上有人
                WorldEventPhase.Preparation,   // 准备：集结去追
                WorldEventPhase.Ongoing,       // 进行中：与盗匪交手
                WorldEventPhase.Settlement,    // 结算：缉获或被抢
                WorldEventPhase.Finished,      // 结束（没有「后续影响」阶段）
            },
            Settle = (instance, participants) =>
            {
                var consequences = new Dictionary<string, WorldEventEffect>(StringComparer.Ordinal);
                var values = tuning();

                // 没人出击 ⇒ 没人来抢 ⇒ 必败（不需要真的打一场）。
                bool fought = participants.Count > 0;
                bool won = false;

                if (fought)
                {
                    var battle = BeastTideBattle.Resolve(
                        participants, heroStats, resolver, outcomeReader,
                        runId: $"banditraid:{instance.Id}", placeName: "盗匪来袭");
                    won = battle.Won;
                    consequences["bandit_raid/defended"] = new WorldEventEffect(
                        WorldEventEffect.KindNone, 0L, won ? "缴获" : "被抢");
                    consequences["bandit_raid/participants"] = WorldEventEffect.None(
                        string.Join("、", participants));
                }
                else
                {
                    consequences["bandit_raid/defended"] = new WorldEventEffect(
                        WorldEventEffect.KindNone, 0L, "被抢");
                    consequences["bandit_raid/participants"] = WorldEventEffect.None("（无人出击）");
                }

                if (won)
                {
                    consequences["bandit_raid/material"] = new WorldEventEffect(
                        WorldEventEffect.KindMaterial, values.MaterialReward,
                        $"缴获盗匪：材料 +{values.MaterialReward}");
                    consequences["bandit_raid/gold"] = new WorldEventEffect(
                        WorldEventEffect.KindGold, values.GoldReward,
                        $"缴获盗匪：金币 +{values.GoldReward}");
                }
                else
                {
                    // ⚠️ **被抢的是物资**（金币不动）：盗匪来抢的是货，不是钱。
                    consequences["bandit_raid/material"] = new WorldEventEffect(
                        WorldEventEffect.KindMaterial, -values.MaterialLoss,
                        $"盗匪抢走了材料：材料 -{values.MaterialLoss}");
                    consequences["bandit_raid/gold"] = new WorldEventEffect(
                        WorldEventEffect.KindGold, 0L, "金币没被抢走");
                }

                return new WorldEventOutcome(won, consequences);
            },
        };
    }
}

/// <summary>
/// <b>盗匪来袭的数值</b>（D-096：<b>建议值</b>，允许调）。
/// </summary>
/// <param name="MaterialReward">缴获时拿到的材料。</param>
/// <param name="GoldReward">缴获时拿到的金币。</param>
/// <param name="MaterialLoss">被抢走的材料。</param>
public readonly record struct BanditTuning(long MaterialReward, long GoldReward, long MaterialLoss);
