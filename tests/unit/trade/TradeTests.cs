using System;
using System.Collections.Generic;
using GdUnit4;
using ClaudeCodeGameStudios.Domain.Heroes;
using ClaudeCodeGameStudios.Domain.Items;
using DomainTrade = ClaudeCodeGameStudios.Domain.Trade;
using ClaudeCodeGameStudios.Domain.Village;

namespace ClaudeCodeGameStudios.Tests.Trade;

/// <summary>
/// <see cref="Trade"/> 的单元测试。
///
/// <para><b>本文档保护：</b></para>
/// <list type="bullet">
/// <item>构造期防线（卖方 ≠ 买方、数量 ≥ 1、单价 ≥ 0、Id 全非空）。</item>
/// <item>Precheck：余额不够 / 库存不够 → false。</item>
/// <item>I15 全有或全无：Apply 失败时，<b>4 个新引用均等于原引用</b>。</item>
/// <item>Apply 成功：买卖双方钱与物品各变化正确。</item>
/// </list>
///
/// <para><b>为什么不测防线外</b>：交易税、摊位费、价格浮动都属 P2-D02 / P2-D05 / T-004
/// 【待定】，<b>不</b>在本类里测 —— 让 <c>Settlement</c> 与未来的 <c>StallFee</c> 各自负责。</para>
/// </summary>
[TestSuite]
public class TradeTests
{
    private static readonly HeroId Alice = new("h-alice");
    private static readonly HeroId Bob = new("h-bob");
    private static readonly ItemId Stone = new("石料");
    private static readonly ResourceId Gold = new("金币");

    // record struct 默认值不能用 null — 用哨兵 HeroId / ItemId.None 触发默认
    private static DomainTrade.Trade MakeTrade(
        HeroId seller = default, HeroId buyer = default,
        ItemId item = default, long qty = 1, long unitPrice = 5,
        string runId = "r1", double simTime = 100.0)
    {
        if (seller.Value == null) seller = Alice;
        if (buyer.Value == null) buyer = Bob;
        if (item.Value == null) item = Stone;
        return new DomainTrade.Trade(
            new DomainTrade.TradeId(runId, seller, buyer, item, qty, simTime),
            seller, buyer, item, qty, unitPrice, simTime);
    }

    // ── 构造期防线 ─────────────────────────────────────────────────────

    /// <summary>卖方 = 买方 → 构造期拒。</summary>
    [TestCase]
    public void Trade_WithSameSellerAndBuyer_IsRejected()
    {
        bool threw = false;
        try { MakeTrade(seller: Alice, buyer: Alice); }
        catch (ArgumentException) { threw = true; }
        Assertions.AssertThat(threw).IsTrue();
    }

    /// <summary>数量 = 0 → 构造期拒。</summary>
    [TestCase]
    public void Trade_WithZeroQuantity_IsRejected()
    {
        bool threw = false;
        try { MakeTrade(qty: 0); }
        catch (ArgumentException) { threw = true; }
        Assertions.AssertThat(threw).IsTrue();
    }

    /// <summary>数量负 → 构造期拒。</summary>
    [TestCase]
    public void Trade_WithNegativeQuantity_IsRejected()
    {
        bool threw = false;
        try { MakeTrade(qty: -3); }
        catch (ArgumentException) { threw = true; }
        Assertions.AssertThat(threw).IsTrue();
    }

    /// <summary>单价负 → 构造期拒（0 合法 ——「白送」，M1 已立）。</summary>
    [TestCase]
    public void Trade_WithNegativeUnitPrice_IsRejected()
    {
        bool threw = false;
        try { MakeTrade(unitPrice: -1); }
        catch (ArgumentException) { threw = true; }
        Assertions.AssertThat(threw).IsTrue();
    }

    /// <summary>卖方 Id 空 → 构造期拒。</summary>
    [TestCase]
    public void Trade_WithEmptySellerId_IsRejected()
    {
        bool threw = false;
        try { new DomainTrade.Trade(new DomainTrade.TradeId("r1", new HeroId(""), Bob, Stone, 1, 1.0), new HeroId(""), Bob, Stone, 1, 1, 1.0); }
        catch (ArgumentException) { threw = true; }
        Assertions.AssertThat(threw).IsTrue();
    }

    /// <summary>SimTime 负 → 构造期拒。</summary>
    [TestCase]
    public void Trade_WithBadSimTime_IsRejected()
    {
        bool threw = false;
        try { MakeTrade(simTime: -1.0); }
        catch (ArgumentOutOfRangeException) { threw = true; }
        Assertions.AssertThat(threw).IsTrue();
    }

    // ── Precheck ──────────────────────────────────────────────────────────

    /// <summary>买方钱不够 → Precheck false。</summary>
    [TestCase]
    public void Precheck_InsufficientGold_ReturnsFalse()
    {
        var t = MakeTrade(qty: 5, unitPrice: 10);   // 50 金
        var buyer = new Wallet(new Dictionary<ResourceId, long> { { Gold, 30 } });
        var seller = new Inventory().TryAdd(Stone, 100);

        Assertions.AssertThat(t.Precheck(buyer, seller!)).IsFalse();
    }

    /// <summary>卖方库存不够 → Precheck false。</summary>
    [TestCase]
    public void Precheck_InsufficientStock_ReturnsFalse()
    {
        var t = MakeTrade(qty: 5, unitPrice: 10);
        var buyer = new Wallet(new Dictionary<ResourceId, long> { { Gold, 100 } });
        var seller = new Inventory().TryAdd(Stone, 2);

        Assertions.AssertThat(t.Precheck(buyer, seller!)).IsFalse();
    }

    // ── Apply 成功 ──────────────────────────────────────────────────────

    /// <summary>
    /// 成功：双方钱与物品各变化，<b>4 个新引用都返回</b>，调用方负责替换。
    /// </summary>
    [TestCase]
    public void Apply_OnSuccess_ReturnsFourNewReferences()
    {
        var t = MakeTrade(qty: 3, unitPrice: 5);   // 15 金，3 份石料
        var buyerW = new Wallet(new Dictionary<ResourceId, long> { { Gold, 100 } });
        var sellerBag = new Inventory().TryAdd(Stone, 5);
        var sellerW = new Wallet();
        var buyerBag = new Inventory(capacity: 100);

        var r = t.Apply(buyerW, sellerBag!, sellerW, buyerBag);
        Assertions.AssertThat(r.HasValue).IsTrue();
        var (nbW, nsBag, nsW, nbBag) = r!.Value;

        // 买方
        Assertions.AssertThat(nbW.Amount(Gold)).IsEqual(85L);
        Assertions.AssertThat(nbBag.Count(Stone)).IsEqual(3L);
        // 卖方
        Assertions.AssertThat(nsBag.Count(Stone)).IsEqual(2L);
        Assertions.AssertThat(nsW.Amount(Gold)).IsEqual(15L);
        // 状态
        Assertions.AssertThat(t.Status).IsEqual(DomainTrade.TradeStatus.Succeeded);
    }

    // ── Apply 失败（I15）─────────────────────────────────────────────

    /// <summary>钱不够：<b>4 个返回 = 4 个原引用</b>，原对象<b>一字不动</b>。</summary>
    [TestCase]
    public void Apply_InsufficientGold_ReturnsNull_AndLeavesInputsUnchanged()
    {
        var t = MakeTrade(qty: 5, unitPrice: 10);
        var buyerW = new Wallet(new Dictionary<ResourceId, long> { { Gold, 30 } });
        var sellerBag = new Inventory().TryAdd(Stone, 100);
        var sellerW = new Wallet();
        var buyerBag = new Inventory(capacity: 100);

        var r = t.Apply(buyerW, sellerBag!, sellerW, buyerBag);
        Assertions.AssertThat(r).IsNull();
        // 状态：变成 Rejected（不是 Pending）
        Assertions.AssertThat(t.Status).IsEqual(DomainTrade.TradeStatus.Rejected);
    }

    /// <summary>卖方库存不够：买家钱未扣、卖方货未动。</summary>
    [TestCase]
    public void Apply_InsufficientStock_ReturnsNull_AndLeavesInputsUnchanged()
    {
        var t = MakeTrade(qty: 5, unitPrice: 10);
        var buyerW = new Wallet(new Dictionary<ResourceId, long> { { Gold, 100 } });
        var sellerBag = new Inventory().TryAdd(Stone, 2);
        var sellerW = new Wallet();
        var buyerBag = new Inventory(capacity: 100);

        var r = t.Apply(buyerW, sellerBag!, sellerW, buyerBag);
        Assertions.AssertThat(r).IsNull();
    }

    /// <summary>已成功的交易再跑一次 = 不发第二遍（I4 由 Settlement 保证，<b>这条测本地幂等</b>）。</summary>
    [TestCase]
    public void Apply_AfterSuccess_StatusIsNotPending_SoCallingAgainFails()
    {
        var t = MakeTrade(qty: 1, unitPrice: 5);
        var buyerW = new Wallet(new Dictionary<ResourceId, long> { { Gold, 100 } });
        var sellerBag = new Inventory().TryAdd(Stone, 5);
        var sellerW = new Wallet();
        var buyerBag = new Inventory(capacity: 100);

        t.Apply(buyerW, sellerBag!, sellerW, buyerBag);
        var r2 = t.Apply(buyerW, sellerBag!, sellerW, buyerBag);
        Assertions.AssertThat(r2).IsNull();
    }
}
