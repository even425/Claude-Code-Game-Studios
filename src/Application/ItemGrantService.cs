using System;
using ClaudeCodeGameStudios.Domain.Heroes;
using ClaudeCodeGameStudios.Domain.Items;

namespace ClaudeCodeGameStudios.Application;

/// <summary>
/// 物品发放的<b>受控应用入口</b>（第四阶段 M1）。
/// </summary>
/// <remarks>
/// <para><b>为什么需要它</b>：当前英雄背包没有任何物品来源，导致摆摊/购买整条链不可达。
/// 本类提供一个**统一的发放入口**，供测试场景验证背包、摆摊与交易；
/// 正式玩法（副本战利品）以后也走同一条入口，只是调用方不同。</para>
/// <para><b>它守什么</b>：英雄存在、物品在价格表登记为可售（价格 &gt; 0）、数量为正；
/// 任何一项不合法都**不修改背包**（<c>Inventory.TryAdd</c> 本身全有或全无）。</para>
/// <para><b>它不做什么</b>：不产生事件（测试配给不应污染正式事件流）、不决定价格/掉落规则、
/// 不直接改 <c>Bag</c> 内部字典 —— 只调 <see cref="HeroAgent.TryAddItem"/>。</para>
/// <para><b>测试/正式边界</b>：当前只有 <c>Main</c> 的 <c>--grant=</c> 调试入口调用它；
/// 它<b>不</b>接入正常游戏流程，也不接入副本结算。正式掉落接入时需先确认奖励归属规则。</para>
/// </remarks>
public sealed class ItemGrantService
{
    /// <summary>一次发放的结果分类。</summary>
    public enum Outcome
    {
        /// <summary>已发放，背包已更新。</summary>
        Granted = 0,

        /// <summary>英雄不存在或标识为空。</summary>
        UnknownHero = 1,

        /// <summary>物品未登记为可售（或标识为空）。</summary>
        UnknownItem = 2,

        /// <summary>数量不是正数。</summary>
        InvalidQuantity = 3,

        /// <summary>背包拒绝（容量超限等）；背包未变。</summary>
        InventoryRejected = 4,
    }

    /// <summary>一次发放的结果。</summary>
    /// <param name="Result">结果分类。</param>
    /// <param name="Item">物品标识。</param>
    /// <param name="Quantity">请求数量。</param>
    /// <param name="NewCount">发放后该物品在背包中的数量（失败时为发放前数量）。</param>
    public readonly record struct GrantResult(
        Outcome Result, ItemId Item, long Quantity, long NewCount)
    {
        /// <summary>是否真的发放成功。</summary>
        public bool Granted => Result == Outcome.Granted;
    }

    private readonly Func<HeroId, HeroAgent?> _heroLookup;
    private readonly PriceBook _catalog;

    /// <param name="heroLookup">按 HeroId 查英雄。</param>
    /// <param name="catalog">
    /// 物品登记表。当前用 <see cref="PriceBook"/>：只有登记且价格 &gt; 0 的物品可发放 ——
    /// 与“可摆摊”保持同一套合法性，不另建平行物品系统。
    /// </param>
    /// <exception cref="ArgumentNullException">任一参数为 null。</exception>
    public ItemGrantService(Func<HeroId, HeroAgent?> heroLookup, PriceBook catalog)
    {
        _heroLookup = heroLookup ?? throw new ArgumentNullException(nameof(heroLookup));
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
    }

    /// <summary>
    /// 向指定英雄发放物品。
    /// </summary>
    /// <param name="heroId">目标英雄。</param>
    /// <param name="item">物品。</param>
    /// <param name="quantity">数量，必须 &gt; 0。</param>
    /// <returns>结果；失败时背包不变。</returns>
    public GrantResult Grant(HeroId heroId, ItemId item, long quantity)
    {
        if (string.IsNullOrEmpty(heroId.Value)) return new GrantResult(Outcome.UnknownHero, item, quantity, 0);
        if (string.IsNullOrEmpty(item.Value) || !_catalog.IsSellable(item))
        {
            long current = 0;
            var existing = _heroLookup(heroId);
            if (existing is not null) current = existing.Bag.Count(item);
            return new GrantResult(Outcome.UnknownItem, item, quantity, current);
        }

        if (quantity <= 0)
        {
            var existing = _heroLookup(heroId);
            long current = existing?.Bag.Count(item) ?? 0;
            return new GrantResult(Outcome.InvalidQuantity, item, quantity, current);
        }

        var hero = _heroLookup(heroId);
        if (hero is null) return new GrantResult(Outcome.UnknownHero, item, quantity, 0);

        long before = hero.Bag.Count(item);
        if (!hero.TryAddItem(item, quantity))
        {
            // TryAddItem 失败时背包不变（Inventory.TryAdd 全有或全无）
            return new GrantResult(Outcome.InventoryRejected, item, quantity, before);
        }

        return new GrantResult(Outcome.Granted, item, quantity, hero.Bag.Count(item));
    }
}
