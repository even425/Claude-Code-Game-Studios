using System;
using System.Collections.Generic;
using System.Linq;
using GdUnit4;
using ClaudeCodeGameStudios.Application.Stall;
using ClaudeCodeGameStudios.Domain.Heroes;
using ClaudeCodeGameStudios.Domain.Items;
using DomainStall = ClaudeCodeGameStudios.Domain.Stall.Stall;
using DomainStallStatus = ClaudeCodeGameStudios.Domain.Stall.StallStatus;

namespace ClaudeCodeGameStudios.Tests.Stall;

/// <summary>
/// 摊位市场的纯逻辑契约（R-02）。
/// </summary>
/// <remarks>
/// <para><b>为什么测它</b>：「尽可能多」的上限是钱 ∧ 库存 ∧ 背包余量三者取小，
/// 其中<b>单价为 0 的白送摊</b>会让「余额 ÷ 单价」除以 0，
/// <b>自己买自己的摊</b>则必须被滤掉 —— 这两处都是「算错了会亏钱」的地方。
/// 纯函数让它们能被逐条钉死，不需要界面、不需要真机。</para>
/// </remarks>
[TestSuite]
public class StallMarketTests
{
    private static readonly ItemId Stone = new("石料");

    private static DomainStall Stall(
        string owner = "h-0", long stock = 10, long unitPrice = 5,
        DomainStallStatus status = DomainStallStatus.Open)
        => new(new HeroId(owner), Stone, stock, unitPrice, status, simTime: 0);

    private static BuyerFacts Facts(
        string id, string name, long gold, long room = StallMarket.Unlimited)
        => new(new HeroId(id), name, gold, room);

    // ── MaxAffordable ───────────────────────────────────────────────────

    [TestCase]
    public void MaxAffordable_StockIsTheLimit_WhenMoneyAndRoomArePlenty()
    {
        long qty = StallMarket.MaxAffordable(
            stock: 3, unitPrice: 5, gold: 100, bagRoom: StallMarket.Unlimited);

        Assertions.AssertThat(qty).IsEqual(3L);
    }

    [TestCase]
    public void MaxAffordable_GoldIsTheLimit_WhenStockIsPlenty()
    {
        // 17 金 / 单价 5 = 3 份（余 2 买不起第 4 份）
        long qty = StallMarket.MaxAffordable(
            stock: 100, unitPrice: 5, gold: 17, bagRoom: StallMarket.Unlimited);

        Assertions.AssertThat(qty).IsEqual(3L);
    }

    [TestCase]
    public void MaxAffordable_BagRoomIsTheLimit_WhenMoneyIsPlenty()
    {
        long qty = StallMarket.MaxAffordable(
            stock: 100, unitPrice: 5, gold: 100, bagRoom: 7);

        Assertions.AssertThat(qty).IsEqual(7L);
    }

    [TestCase]
    public void MaxAffordable_FreeStall_IgnoresGold()
    {
        // 单价 0 = 白送（合法）。这里若拿 gold / unitPrice 就会除以 0。
        long qty = StallMarket.MaxAffordable(
            stock: 4, unitPrice: 0, gold: 0, bagRoom: StallMarket.Unlimited);

        Assertions.AssertThat(qty).IsEqual(4L);
    }

    [TestCase]
    public void MaxAffordable_EmptyStock_IsZero()
    {
        long qty = StallMarket.MaxAffordable(
            stock: 0, unitPrice: 5, gold: 100, bagRoom: StallMarket.Unlimited);

        Assertions.AssertThat(qty).IsEqual(0L);
    }

    [TestCase]
    public void MaxAffordable_NoMoney_IsZero()
    {
        long qty = StallMarket.MaxAffordable(
            stock: 10, unitPrice: 5, gold: 4, bagRoom: StallMarket.Unlimited);

        Assertions.AssertThat(qty).IsEqual(0L);
    }

    [TestCase]
    public void MaxAffordable_NegativeBagRoom_IsZero()
    {
        long qty = StallMarket.MaxAffordable(
            stock: 10, unitPrice: 5, gold: 100, bagRoom: -1);

        Assertions.AssertThat(qty).IsEqual(0L);
    }

    // ── BagRoom ─────────────────────────────────────────────────────────

    [TestCase]
    public void BagRoom_UnlimitedCapacity_IsUnlimited()
    {
        var bag = new Inventory();   // capacity = 0 = 不设上限
        bag = bag.TryAdd(Stone, 3)!;

        Assertions.AssertThat(StallMarket.BagRoom(bag, Stone)).IsEqual(StallMarket.Unlimited);
    }

    [TestCase]
    public void BagRoom_CountsAgainstWhatIsAlreadyThere()
    {
        var bag = new Inventory(capacity: 10);
        bag = bag.TryAdd(Stone, 4)!;

        Assertions.AssertThat(StallMarket.BagRoom(bag, Stone)).IsEqual(6L);
    }

    [TestCase]
    public void BagRoom_FullBag_IsZero()
    {
        var bag = new Inventory(capacity: 10);
        bag = bag.TryAdd(Stone, 10)!;

        Assertions.AssertThat(StallMarket.BagRoom(bag, Stone)).IsEqual(0L);
    }

    [TestCase]
    public void BagRoom_OtherItemDoesNotConsumeRoom()
    {
        var bag = new Inventory(capacity: 10);
        bag = bag.TryAdd(new ItemId("木料"), 9)!;

        Assertions.AssertThat(StallMarket.BagRoom(bag, Stone)).IsEqual(10L);
    }

    // ── Candidates ──────────────────────────────────────────────────────

    [TestCase]
    public void Candidates_ExcludesTheSeller()
    {
        var stall = Stall(owner: "h-0");
        var pool = new[] { Facts("h-0", "摊主", 100), Facts("h-1", "买家", 100) };

        var offers = StallMarket.Candidates(stall, pool);

        Assertions.AssertThat(offers.Count).IsEqual(1);
        Assertions.AssertThat(offers[0].Id).IsEqual(new HeroId("h-1"));
    }

    [TestCase]
    public void Candidates_ExcludesThoseWhoCannotAffordOne()
    {
        var stall = Stall(unitPrice: 5);
        var pool = new[] { Facts("h-1", "有钱", 100), Facts("h-2", "没钱", 4) };

        var offers = StallMarket.Candidates(stall, pool);

        Assertions.AssertThat(offers.Count).IsEqual(1);
        Assertions.AssertThat(offers[0].Name).IsEqual("有钱");
    }

    [TestCase]
    public void Candidates_ExcludesThoseWhoseBagIsFull()
    {
        var stall = Stall(unitPrice: 5);
        var pool = new[] { Facts("h-1", "装不下", 100, room: 0) };

        var offers = StallMarket.Candidates(stall, pool);

        Assertions.AssertThat(offers.Count).IsEqual(0);
    }

    [TestCase]
    public void Candidates_SortedByName()
    {
        var stall = Stall();
        var pool = new[]
        {
            Facts("h-1", "丙", 100),
            Facts("h-2", "甲", 100),
            Facts("h-3", "乙", 100),
        };

        var offers = StallMarket.Candidates(stall, pool);

        // Ordinal 比较的是码位：丙 U+4E19 < 乙 U+4E59 < 甲 U+7532。
        Assertions.AssertThat(offers.Select(o => o.Name).ToArray())
            .IsEqual(new[] { "丙", "乙", "甲" });
    }

    [TestCase]
    public void Candidates_ReportsMaxQty()
    {
        var stall = Stall(stock: 3, unitPrice: 5);
        var pool = new[] { Facts("h-1", "买家", 100) };

        var offers = StallMarket.Candidates(stall, pool);

        Assertions.AssertThat(offers.Count).IsEqual(1);
        Assertions.AssertThat(offers[0].MaxQty).IsEqual(3L);
        Assertions.AssertThat(offers[0].Gold).IsEqual(100L);
    }

    [TestCase]
    public void Candidates_EmptyPool_IsEmpty()
    {
        var offers = StallMarket.Candidates(Stall(), Array.Empty<BuyerFacts>());

        Assertions.AssertThat(offers.Count).IsEqual(0);
    }

    [TestCase]
    public void Candidates_KeepsEveryoneWhoCanAffordOne()
    {
        var stall = Stall(unitPrice: 5);
        var pool = new List<BuyerFacts>
        {
            Facts("h-1", "甲", 5),
            Facts("h-2", "乙", 6),
        };

        var offers = StallMarket.Candidates(stall, pool);

        Assertions.AssertThat(offers.Count).IsEqual(2);
    }
}
