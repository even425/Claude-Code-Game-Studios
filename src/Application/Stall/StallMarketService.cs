using System;
using System.Collections.Generic;
using ClaudeCodeGameStudios.Domain.Heroes;
using ClaudeCodeGameStudios.Domain.Village;
using DomainStall = ClaudeCodeGameStudios.Domain.Stall.Stall;

namespace ClaudeCodeGameStudios.Application.Stall;

/// <summary>
/// 摊位市场：<b>谁买得到</b>、<b>买多少</b>、<b>成交</b> 三件事收在一处（R-02）。
/// </summary>
/// <remarks>
/// <para><b>为什么要有它</b>：面板（<c>StallMarketPanel</c>）与真机自检（<c>--market</c>）
/// 需要的是<b>同一件事</b> —— 「这个摊，谁买得到、最多买几份、买下来」。
/// 两边各写一份，就会出现「界面说能买 19 份、点下去只买到 12 份」这种对不上的局面。
/// 所以规则只在这里一份，界面与自检都调它。</para>
///
/// <para><b>结算仍然只有一条路</b>：本类最终调
/// <see cref="StallService.TryBuy"/>（唯一结算入口，I15 四步提交），
/// 不自己算钱、不自己改背包。</para>
///
/// <para><b>「在场」是市场的门槛</b>：随队外出、在工地的人买不了 ——
/// 这与自主购物的判据一致（<c>HeroBehaviorService</c> 的「不在场或正在忙」），
/// 两边口径不一样会让玩家看到「他能买你买不了」的怪事。</para>
/// </remarks>
public sealed class StallMarketService
{
    private readonly StallService _stalls;
    private readonly ResourceId _gold;
    private readonly Func<HeroId, HeroAgent?> _heroLookup;

    /// <param name="stalls">摊位服务（唯一结算入口）。</param>
    /// <param name="gold">金币资源 Id。</param>
    /// <param name="heroLookup">按 HeroId 查英雄。</param>
    public StallMarketService(
        StallService stalls, ResourceId gold, Func<HeroId, HeroAgent?> heroLookup)
    {
        _stalls = stalls ?? throw new ArgumentNullException(nameof(stalls));
        _gold = gold;
        _heroLookup = heroLookup ?? throw new ArgumentNullException(nameof(heroLookup));
    }

    /// <summary>当前所有摊位（顺序确定：按摊主 Id 排序）。</summary>
    public IReadOnlyList<DomainStall> Stalls => _stalls.Snapshot();

    /// <summary>
    /// 某个摊位的<b>候选买家</b>：在场 ∧ 空闲 ∧ 买得起 1 份。
    /// </summary>
    /// <param name="stall">摊位。</param>
    /// <param name="heroes">候选英雄池（调用方给全村英雄即可，筛选在这里做）。</param>
    /// <returns>候选买家，按显示名排序。</returns>
    public IReadOnlyList<BuyerOffer> Offers(DomainStall stall, IReadOnlyList<HeroAgent> heroes)
    {
        if (stall is null) throw new ArgumentNullException(nameof(stall));
        if (heroes is null || heroes.Count == 0) return Array.Empty<BuyerOffer>();

        var pool = new List<BuyerFacts>();
        foreach (var h in heroes)
        {
            if (h is null) continue;
            if (h.Away || h.ToSite) continue;   // 不在场或正在忙的人买不了

            pool.Add(new BuyerFacts(
                h.Id, h.Name,
                h.Wallet.Amount(_gold),
                StallMarket.BagRoom(h.Bag, stall.ItemId)));
        }

        return StallMarket.Candidates(stall, pool);
    }

    /// <summary>
    /// 买「尽可能多」：<c>min(库存, 余额 ÷ 单价, 背包余量)</c>。
    /// </summary>
    /// <param name="stall">摊位。</param>
    /// <param name="buyer">买方英雄。</param>
    /// <param name="simTime">成交时间（模拟秒）。</param>
    /// <returns>购买尝试的结果；失败时状态不变。</returns>
    /// <remarks>
    /// <b>数量在这里重算</b>，不采信界面上的旧数字 ——
    /// 列表建好之后，钱和库存都可能变了（别处的成交、英雄自己开摊）。
    /// 用旧数字会买出一个失败的单。
    /// </remarks>
    public StallService.BuyAttempt BuyAsMuchAsPossible(DomainStall stall, HeroId buyer, double simTime)
    {
        if (stall is null) throw new ArgumentNullException(nameof(stall));

        var hero = _heroLookup(buyer);
        if (hero is null)
        {
            return new StallService.BuyAttempt(StallService.BuyOutcome.InvalidRequest, null);
        }

        long gold = hero.Wallet.Amount(_gold);
        long room = StallMarket.BagRoom(hero.Bag, stall.ItemId);
        long qty = StallMarket.MaxAffordable(stall.Stock, stall.UnitPrice, gold, room);

        if (qty < 1)
        {
            // 说清楚是哪一样不够 —— 三种原因对应的处置完全不同。
            if (stall.UnitPrice > 0 && gold < stall.UnitPrice)
            {
                return new StallService.BuyAttempt(StallService.BuyOutcome.CannotAfford, null);
            }
            if (room < 1)
            {
                return new StallService.BuyAttempt(StallService.BuyOutcome.BagRejected, null);
            }
            return new StallService.BuyAttempt(StallService.BuyOutcome.InsufficientStock, null);
        }

        // 幂等键留空：玩家每点一次就是一次独立交易，不存在"重放"要挡。
        return _stalls.TryBuy(stall.Owner, buyer, stall.ItemId, qty, simTime, requestId: "");
    }
}
