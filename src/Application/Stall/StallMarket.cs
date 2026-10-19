using System;
using System.Collections.Generic;
using System.Linq;
using ClaudeCodeGameStudios.Domain.Heroes;
using ClaudeCodeGameStudios.Domain.Items;
using DomainStall = ClaudeCodeGameStudios.Domain.Stall.Stall;

namespace ClaudeCodeGameStudios.Application.Stall;

/// <summary>候选买家的<b>原始事实</b>（谁、有多少钱、背包还能再装几份）。</summary>
/// <param name="Id">英雄标识。</param>
/// <param name="Name">显示名。</param>
/// <param name="Gold">当前金币。</param>
/// <param name="BagRoom">背包还能再装该物品几份（<see cref="StallMarket.Unlimited"/> = 无限）。</param>
public readonly record struct BuyerFacts(HeroId Id, string Name, long Gold, long BagRoom);

/// <summary>一个候选买家，外加<b>本次最多能买几份</b>。</summary>
/// <param name="Id">英雄标识。</param>
/// <param name="Name">显示名。</param>
/// <param name="Gold">当前金币。</param>
/// <param name="MaxQty">本次最多能买几份（钱 ∧ 库存 ∧ 背包容量三者取小）。</param>
public readonly record struct BuyerOffer(HeroId Id, string Name, long Gold, long MaxQty);

/// <summary>
/// 摊位市场的<b>纯逻辑</b>：谁能买某个摊、最多能买几份（R-02）。
/// </summary>
/// <remarks>
/// <para><b>为什么是纯函数</b>：候选筛选与「尽可能多」的上限计算是这一轮最容易算错的地方
/// （单价为 0 的白送摊、钱不够、背包满、买自己的摊），
/// 而它们<b>都不需要界面</b>。放进纯函数，单测就能把每个边界钉死 ——
/// 和 <c>LootSplit</c>（R-01）、<c>TradeFeedback</c>（M5）同一个理由。</para>
///
/// <para><b>它不做什么</b>：不碰 <c>StallService</c>（那是结算的唯一入口）、
/// 不碰背包与钱包、不知道界面的存在。调用方把事实喂进来，它把答案算出来。</para>
///
/// <para><b>「尽可能多」的口径</b>：<c>min(摊位库存, 余额 ÷ 单价, 背包余量)</c>。
/// 三者取小 —— 少一样就会出现「钱花光了但没买够」或「买爆背包」这种半截结果。</para>
/// </remarks>
public static class StallMarket
{
    /// <summary>「装得下多少份」无上限时的哨兵值。</summary>
    public const long Unlimited = long.MaxValue;

    /// <summary>
    /// 背包还能再装几份该物品。
    /// </summary>
    /// <param name="bag">背包。<b>不得为 null</b>。</param>
    /// <param name="item">物品。</param>
    /// <returns>
    /// 还能再装几份；容量为 0（无限）时返回 <see cref="Unlimited"/>。
    /// 已经装满（且有容量上限）时返回 0。
    /// </returns>
    /// <remarks>
    /// <b>为什么不用 <c>TryAdd</c> 试算</b>：<c>Inventory</c> 是不可变的，
    /// <c>TryAdd</c> 要么返回新对象要么返回 null —— 拿它当"还能装几份"的尺子，
    /// 只能试出"装不装得下 1 份"，试不出"还能装 7 份"。
    /// </remarks>
    public static long BagRoom(Inventory bag, ItemId item)
    {
        if (bag is null) throw new ArgumentNullException(nameof(bag));

        if (bag.Capacity <= 0) return Unlimited;

        long room = bag.Capacity - bag.Count(item);
        return room < 0 ? 0 : room;
    }

    /// <summary>
    /// 「尽可能多」能买几份：钱 ∧ 库存 ∧ 背包余量，三者取小。
    /// </summary>
    /// <param name="stock">摊位库存。</param>
    /// <param name="unitPrice">单价。<b>0 = 白送</b>（合法，见 <c>Stall</c> 构造期防线）。</param>
    /// <param name="gold">买家当前金币。</param>
    /// <param name="bagRoom">买家背包余量（<see cref="Unlimited"/> = 无限）。</param>
    /// <returns>能买的份数；任一项为 0 或不足时返回 0。</returns>
    /// <remarks>
    /// <b>单价为 0 时不看钱</b>：白送摊（单价 0 是合法值）下，
    /// 「余额 ÷ 单价」会除以 0 —— 所以只在单价 &gt; 0 时才拿钱设上限。
    /// </remarks>
    public static long MaxAffordable(long stock, long unitPrice, long gold, long bagRoom)
    {
        if (stock <= 0) return 0;

        long qty = stock;
        if (bagRoom < qty) qty = bagRoom < 0 ? 0 : bagRoom;

        if (unitPrice > 0)
        {
            long byGold = gold / unitPrice;   // 整数除法：向下取整，够买几份就是几份
            if (byGold < qty) qty = byGold;
        }

        return qty < 0 ? 0 : qty;
    }

    /// <summary>
    /// 算出某个摊位的<b>候选买家</b>（够买得起 1 份的人）。
    /// </summary>
    /// <param name="stall">摊位。<b>不得为 null</b>。</param>
    /// <param name="pool">
    /// 候选人池 —— 调用方已经筛过「在场 / 有货」的人。
    /// 「在场」属于场景事实（谁在村里），本类不关心，所以由调用方给。
    /// </param>
    /// <returns>候选买家，按显示名排序（稳定，便于测试与玩家扫读）。</returns>
    /// <remarks>
    /// <para><b>自己不能买自己的摊</b>：<c>StallService.TryBuy</c> 会以
    /// <c>SameHero</c> 拒绝，但那是<b>成交阶段</b>的拒绝 —— 面板里摆一个
    /// 点了必然失败的选项，等于骗玩家。所以这里<b>先滤掉</b>摊主本人。</para>
    ///
    /// <para><b>买得起 0 份的不列</b>：与其把「这个人买不起」列出来让玩家点一下再失败，
    /// 不如一开始就不列 —— 候选名单里出现的每一个人都是<b>点了就能买成</b>的。</para>
    /// </remarks>
    public static IReadOnlyList<BuyerOffer> Candidates(
        DomainStall stall, IReadOnlyList<BuyerFacts> pool)
    {
        if (stall is null) throw new ArgumentNullException(nameof(stall));
        if (pool is null || pool.Count == 0) return Array.Empty<BuyerOffer>();

        var offers = new List<BuyerOffer>();
        foreach (var f in pool)
        {
            if (f.Id == stall.Owner) continue;   // 同一个人不能自买自卖

            long qty = MaxAffordable(stall.Stock, stall.UnitPrice, f.Gold, f.BagRoom);
            if (qty < 1) continue;              // 买得起 0 份 = 不算候选

            offers.Add(new BuyerOffer(f.Id, f.Name, f.Gold, qty));
        }

        return offers
            .OrderBy(o => o.Name, StringComparer.Ordinal)
            .ToArray();
    }
}
