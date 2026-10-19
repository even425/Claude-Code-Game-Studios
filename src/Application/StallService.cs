using System;
using System.Collections.Generic;
using ClaudeCodeGameStudios.Application;
using ClaudeCodeGameStudios.Domain.Heroes;
using ClaudeCodeGameStudios.Domain.Items;
using ClaudeCodeGameStudios.Domain.Village;
using DomainStall = ClaudeCodeGameStudios.Domain.Stall.Stall;
using DomainStallStatus = ClaudeCodeGameStudios.Domain.Stall.StallStatus;
using DomainStallFee = ClaudeCodeGameStudios.Domain.Stall.StallFee;
using DomainTrade = ClaudeCodeGameStudios.Domain.Trade.Trade;
using DomainTradeId = ClaudeCodeGameStudios.Domain.Trade.TradeId;
using DomainTradeStatus = ClaudeCodeGameStudios.Domain.Trade.TradeStatus;
using DomainVillage = ClaudeCodeGameStudios.Domain.Village.Village;

namespace ClaudeCodeGameStudios.Application.Stall;

/// <summary>
/// 摆摊 / 收摊 / 买摊 三件事的<b>应用层门面</b>。
/// </summary>
/// <remarks>
/// <para><b>职责</b>：把"摆摊 = 挪货 + 注册"、"买摊 = Trade.Apply + 5% 入村金库 + stall 库存递减"
/// 三步按 I15 守门，不让任何一步失败留下半摊子。</para>
///
/// <para><b>不</b>做什么：</para>
/// <list type="bullet">
/// <item>不画摊位（应用层不管渲染）</item>
/// <item>不决策英雄该摆什么（M4 决策流的工作）</item>
/// <item>不挂 Settlement 写村庄——M3 走的是 <c>Village.Add</c> 直写，
///       保留 Settlement 路径给"需要幂等"的战斗奖励流</item>
/// </list>
///
/// <para><b>一人一摊</b>（D-023 / P2-D04）：目录 <c>Dictionary&lt;HeroId, Stall&gt;</c>，
/// <c>PlaceStall</c> 时若该 Hero 已有 <c>Status != Closed</c> 的摊位 → 拒。
/// <c>Closed</c> 摊位 <b>不</b>占位（让英雄开新摊）。</para>
///
/// <para><b>5% 入村金库</b>：D-023 拍定"floor + 残差归卖家"。
/// <c>BuyFromStall</c> 内部 <c>StallFee.Compute(totalAmount)</c> 算两路（卖家净收 + 村入账），
/// 村入账 = 0 时<b>不</b>调 <c>Village.Add</c>（<c>Add</c> 拒 0）。</para>
///
/// <para><b>摆摊前置条件</b>（D-023 / P2-D04，AND 三项）：</para>
/// <list type="bullet">
/// <item>英雄<b>在场</b>（<c>Away == false</c>）</item>
/// <item>英雄<b>空闲</b>（<c>ToSite == false</c>）</item>
/// <item>英雄<b>持可售物品 ≥ qty</b>（<c>Bag.Count(itemId) >= qty</c> 且
///       <c>PriceBook.IsSellable(itemId)</c>）</item>
/// </list>
/// </remarks>
public sealed class StallService
{
    private readonly Dictionary<HeroId, DomainStall> _stalls = new();
    private readonly DomainVillage _village;
    private readonly PriceBook _book;
    private readonly Func<HeroId, HeroAgent?> _heroLookup;

    /// <summary>摊位目录（只读）。</summary>
    public IReadOnlyDictionary<HeroId, DomainStall> Stalls => _stalls;

    /// <summary>村庄金库的 ResourceId（D-021 "金币"）。</summary>
    public static readonly ResourceId Gold = new("金币");

    /// <param name="village">村庄（写"金币"金库）。</param>
    /// <param name="book">统一价格表（验"可售"+ 取单价）。</param>
    /// <param name="heroLookup">按 HeroId 找英雄（含 Bag/Wallet/Away/ToSite）。</param>
    public StallService(DomainVillage village, PriceBook book, Func<HeroId, HeroAgent?> heroLookup)
    {
        _village = village ?? throw new ArgumentNullException(nameof(village));
        _book = book ?? throw new ArgumentNullException(nameof(book));
        _heroLookup = heroLookup ?? throw new ArgumentNullException(nameof(heroLookup));
    }

    /// <summary>摆摊结果（成功三件套）。</summary>
    public sealed class PlaceStallResult
    {
        public required HeroId Owner { get; init; }
        public required DomainStall Stall { get; init; }
        public required Inventory NewBag { get; init; }   // 摆摊后英雄 Bag（已扣 qty 份）
    }

    /// <summary>开摊。</summary>
    /// <param name="owner">摊主。</param>
    /// <param name="itemId">商品。</param>
    /// <param name="qty">挪入摊位库存的数量（≥ 1）。</param>
    /// <param name="simTime">开摊时间（模拟秒）。</param>
    /// <returns>成功 = 三个新引用；失败 = null。</returns>
    public PlaceStallResult? PlaceStall(
        HeroId owner, ItemId itemId, long qty, double simTime,
        int gridX = 0, int gridY = 0)
    {
        // 入参防线
        if (string.IsNullOrEmpty(owner.Value)) return null;
        if (string.IsNullOrEmpty(itemId.Value)) return null;
        if (qty < 1) return null;
        if (simTime < 0.0 || double.IsNaN(simTime) || double.IsInfinity(simTime)) return null;

        // 英雄存在
        var hero = _heroLookup(owner);
        if (hero is null) return null;

        // D-023 / P2-D04：AND 三项
        if (hero.Away) return null;                  // 不在场
        if (hero.ToSite) return null;                // 忙
        if (hero.Bag.Count(itemId) < qty) return null;   // 货不够
        if (!_book.IsSellable(itemId)) return null;  // 商品不卖

        // 一人一摊：已有 Open / SoldOut → 拒
        if (_stalls.TryGetValue(owner, out var existing)
            && existing.Status != DomainStallStatus.Closed)
        {
            return null;
        }

        // M5 / D-033 位置校验：
        // (1) (gridX, gridY) 不能与建筑占用格重叠
        // (2) (gridX, gridY) 必须在**至少一个**已建成建筑的影响区内
        if (_village.IsOccupied(gridX, gridY)) return null;   // 占建筑 → 拒
        bool inAnyAffects = false;
        foreach (var b in _village.Buildings)
        {
            if (b.IsCellInAffectsArea(gridX, gridY)) { inAnyAffects = true; break; }
        }
        if (!inAnyAffects) return null;   // 0 建筑影响 → 拒（v1 简化）

        long unitPrice = _book.PriceOf(itemId);
        if (unitPrice < 0) return null;   // 不应发生（IsSellable 守过）—— 守门

        // I15 提交
        var newBag = hero.Bag.TryRemove(itemId, qty);
        if (newBag is null) return null;
        var newStall = new DomainStall(owner, itemId, qty, unitPrice, DomainStallStatus.Open, simTime,
            gridX, gridY);

        hero.ApplyTradeResult(hero.Wallet, newBag);   // 只换 Bag —— Wallet 复用
        _stalls[owner] = newStall;
        return new PlaceStallResult { Owner = owner, Stall = newStall, NewBag = newBag };
    }

    /// <summary>买摊结果（成功三件套）。</summary>
    public sealed class BuyStallResult
    {
        public required HeroId Seller { get; init; }
        public required HeroId Buyer { get; init; }
        public required DomainStall NewStall { get; init; }   // 库存递减
        public required long TotalAmount { get; init; }
        public required long SellerNet { get; init; }
        public required long VillageCut { get; init; }
    }

    /// <summary>买摊（I15：预检 → 算 → 一次性提交）。</summary>
    /// <remarks>
    /// <para><b>不</b>走 <see cref="DomainTrade.Trade.Apply"/>：<c>Trade.Apply</c> 假设
    /// 卖方库存<b>在 hero.Bag</b>，但摆摊后货已挪入 <see cref="DomainStall.Stock"/>，
    /// hero.Bag <b>不含</b>摊位上的货。强行传 <c>sellerHero.Bag</c> 会让
    /// <c>Precheck</c> 的"卖方库存够"假拒。摊位的"卖方库存"是 <c>stall.Stock</c>，
    /// 单独走 I15 守门。</para>
    ///
    /// <para><b>四步提交</b>：</para>
    /// <list type="number">
    /// <item>卖方收钱：<c>Wallet.TryAdd(金币, sellerNet)</c></item>
    /// <item>买方付钱：<c>Wallet.TrySpend(金币, totalAmount)</c></item>
    /// <item>买方收货：<c>Bag.TryAdd(itemId, qty)</c></item>
    /// <item>村金库入账：<c>Village.Add(金币, villageCut)</c>（villageCut &gt; 0 才调）</item>
    /// </list>
    /// <para>任一 null → 全部不变 + 返 null。</para>
    /// </remarks>
    /// <param name="seller">摊主。</param>
    /// <param name="buyer">买方英雄。</param>
    /// <param name="itemId">商品（与摊位一致）。</param>
    /// <param name="qty">买几份（≥ 1）。</param>
    /// <param name="simTime">成交时间（模拟秒）。<b>不再需要 runId</b>（不走 TradeId 幂等）。</param>
    public BuyStallResult? BuyFromStall(
        HeroId seller, HeroId buyer, ItemId itemId, long qty, double simTime)
    {
        // 入参防线
        if (string.IsNullOrEmpty(seller.Value)) return null;
        if (string.IsNullOrEmpty(buyer.Value)) return null;
        if (seller == buyer) return null;
        if (string.IsNullOrEmpty(itemId.Value)) return null;
        if (qty < 1) return null;
        if (simTime < 0.0 || double.IsNaN(simTime) || double.IsInfinity(simTime)) return null;

        // 摊位 + 状态
        if (!_stalls.TryGetValue(seller, out var stall)) return null;
        if (stall.Status != DomainStallStatus.Open) return null;
        if (stall.ItemId != itemId) return null;
        if (stall.Stock < qty) return null;

        // 双方英雄
        var sellerHero = _heroLookup(seller);
        var buyerHero = _heroLookup(buyer);
        if (sellerHero is null || buyerHero is null) return null;

        // 算金额 + 拆 5%
        long totalAmount = stall.UnitPrice * qty;
        var (sellerNet, villageCut) = DomainStallFee.Compute(totalAmount);

        // ── I15 预检：所有 TryAdd/TrySpend/TryAdd 都能成？─────────────────
        // 1. 卖方收钱（加钱不可能失败，但跑一手守门）
        var newSellerWallet = sellerHero.Wallet.TryAdd(Gold, sellerNet);
        if (newSellerWallet is null) return null;
        // 2. 买方付钱
        var newBuyerWallet = buyerHero.Wallet.TrySpend(Gold, totalAmount);
        if (newBuyerWallet is null) return null;
        // 3. 买方收货
        var newBuyerBag = buyerHero.Bag.TryAdd(itemId, qty);
        if (newBuyerBag is null) return null;
        // 4. 摊位库存递减
        var newStall = stall.AfterSale(qty);

        // ── 全有或全无：四个都拿到了，提交 ──
        sellerHero.ApplyTradeResult(newSellerWallet, sellerHero.Bag);   // 只换 Wallet
        buyerHero.ApplyTradeResult(newBuyerWallet, newBuyerBag);          // 双换
        _stalls[seller] = newStall;
        if (villageCut > 0)
        {
            _village.Add(Gold, villageCut);
        }

        return new BuyStallResult
        {
            Seller = seller,
            Buyer = buyer,
            NewStall = newStall,
            TotalAmount = totalAmount,
            SellerNet = sellerNet,
            VillageCut = villageCut,
        };
    }

    /// <summary>收摊：状态变 Closed。<b>不</b>动英雄背包（库存归 0 时也是 Closed 而非"丢货"）。</summary>
    /// <returns>成功 = true；该英雄本就没摊 = false（不是错误）。</returns>
    public bool CloseStall(HeroId owner)
    {
        if (string.IsNullOrEmpty(owner.Value)) return false;
        if (!_stalls.TryGetValue(owner, out var stall)) return false;
        if (stall.Status == DomainStallStatus.Closed) return false;   // 已收
        _stalls[owner] = stall.Closed();
        return true;
    }

    /// <summary>查某英雄的摊位（无摊 = null）。</summary>
    public DomainStall? GetStall(HeroId owner)
    {
        if (string.IsNullOrEmpty(owner.Value)) return null;
        return _stalls.TryGetValue(owner, out var s) ? s : null;
    }
}
