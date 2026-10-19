using System;
using System.Collections.Generic;
using System.Linq;
using ClaudeCodeGameStudios.Domain.Heroes;
using ClaudeCodeGameStudios.Domain.Items;
using ClaudeCodeGameStudios.Domain.Settlement;

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
/// <para><b>测试/正式边界</b>：单笔发放当前只被 <c>Main</c> 的 <c>--grant=</c> 调试入口调用；
/// <b>批量发放</b>（<see cref="Grant(IReadOnlyList{ItemGrant})"/>）则由副本结算调用 ——
/// 那是 R-01 接上的正式路径，走的还是<b>同一个受控入口</b>，
/// 不另建一套平行的物品系统。</para>
/// </remarks>
public sealed class ItemGrantService : IItemGrantWriter
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

    // ── 批量发放（R-01）─────────────────────────────────────────────────

    /// <summary>
    /// 预检一批发放是否<b>全部</b>可行。<b>不改变任何状态。</b>
    /// </summary>
    /// <param name="grants">发放明细。</param>
    /// <returns>全部可行返回 true；任何一条不可行返回 false。</returns>
    /// <remarks>
    /// <b>判据</b>：英雄存在、物品已登记为可售、数量为正、且<b>所有</b>英雄的背包
    /// 都装得下（<c>Inventory.TryAdd</c> 不会因容量超限而返回 null）。
    /// </remarks>
    public bool CanGrant(IReadOnlyList<ItemGrant> grants)
    {
        if (grants is null || grants.Count == 0) return true;
        return BuildFinalBags(grants) is not null;
    }

    /// <summary>
    /// 提交一批发放。<b>调用前必须已通过 <see cref="CanGrant"/>。</b>
    /// </summary>
    /// <param name="grants">发放明细。</param>
    /// <exception cref="InvalidOperationException">
    /// 这批发放不可行 —— 说明调用方跳过了预检，那是编程错误。
    /// </exception>
    /// <remarks>
    /// <para><b>为什么能保证「要么全发要么全不发」</b>：背包（<c>Inventory</c>）是
    /// <b>不可变</b>的，<c>TryAdd</c> 返回的是<b>新对象</b>。
    /// 所以本方法先在<b>本地</b>把每个英雄的新背包算出来（算不出来就整批放弃），
    /// 全部算完再一次性换上 —— 中间没有任何一步会改到真实状态。</para>
    /// </remarks>
    public void Grant(IReadOnlyList<ItemGrant> grants)
    {
        if (grants is null || grants.Count == 0) return;

        var final = BuildFinalBags(grants);
        if (final is null)
        {
            throw new InvalidOperationException(
                "这批物品发放不可行（英雄不存在、物品未登记、数量非正或背包装不下）。"
                + "Grant 必须在 CanGrant 通过之后调用 —— 跳过了预检就不能保证「全有或全无」。");
        }

        // 全部换上。到这一步已经没有任何会失败的路径了。
        foreach (var (hero, bag) in final)
        {
            hero.RestoreBag(bag);
        }
    }

    /// <summary>
    /// 算出这批发放之后每个英雄的新背包。算不出来（任何一条不可行）返回 <c>null</c>。
    /// </summary>
    /// <param name="grants">发放明细。</param>
    private List<(HeroAgent Hero, Inventory Bag)>? BuildFinalBags(IReadOnlyList<ItemGrant> grants)
    {
        var order = new List<HeroId>();
        var bags = new Dictionary<HeroId, Inventory>();

        foreach (var g in grants)
        {
            if (g.Quantity <= 0) return null;
            if (string.IsNullOrEmpty(g.Hero.Value)) return null;
            if (string.IsNullOrEmpty(g.Item.Value) || !_catalog.IsSellable(g.Item)) return null;

            var hero = _heroLookup(g.Hero);
            if (hero is null) return null;

            if (!bags.TryGetValue(g.Hero, out var bag))
            {
                bag = hero.Bag;
                bags[g.Hero] = bag;
                order.Add(g.Hero);
            }

            // 不可变背包：TryAdd 返回新对象，装不下就返回 null —— 整批放弃。
            var next = bag.TryAdd(g.Item, g.Quantity);
            if (next is null) return null;

            bags[g.Hero] = next;
        }

        var result = new List<(HeroAgent, Inventory)>(order.Count);
        foreach (var id in order)
        {
            var hero = _heroLookup(id);
            if (hero is null) return null;   // 名单在计算过程中失效：整批放弃
            result.Add((hero, bags[id]));
        }

        return result;
    }
}
