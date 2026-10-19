using System;
using System.Collections.Generic;
using GdUnit4;
using ClaudeCodeGameStudios.Application.Stall;
using ClaudeCodeGameStudios.Domain.Heroes;
using ClaudeCodeGameStudios.Domain.Items;
using DomainStallStatus = ClaudeCodeGameStudios.Domain.Stall.StallStatus;
using DomainVillage = ClaudeCodeGameStudios.Domain.Village.Village;
using DomainResourceId = ClaudeCodeGameStudios.Domain.Village.ResourceId;

namespace ClaudeCodeGameStudios.Tests.Stall;

/// <summary>
/// <see cref="StallService"/> 单元测试。
///
/// <para><b>本文档保护：</b></para>
/// <list type="bullet">
/// <item>摆摊前置条件（AND 三项：不在场 / 忙 / 货不够 / 商品不卖 / 已开摊）。</item>
/// <item><b>I15</b>：PlaceStall / BuyFromStall 任何一步失败 → 状态一字不动。</item>
/// <item>5% 摊位费：floor + 残差归卖家（StallFee 内已单测，这里只断"两路值对 + 村金库 Add 调用对"）。</item>
/// <item>一人一摊：开新摊前不开旧摊 → 拒。</item>
/// <item>摊位状态机：SoldOut 后不能再 Buy；Closed 后可重开。</item>
/// </list>
///
/// <para><b>注意</b>：测试用真 <see cref="HeroAgent"/>，因为 M2 已立
/// <c>ApplyTradeResult(Wallet, Inventory)</c> 薄壳 + <c>Bag.TryRemove</c> /
/// <c>Wallet.TryAdd</c> 不可变。StallService 直接持 HeroAgent 引用，
/// <b>不</b> mock —— 不可变 + 真值守门更稳。</para>
/// </summary>
[TestSuite]
public class StallServiceTests
{
    private const string GoldKey = "金币";
    private static readonly HeroId Alice = new("h-alice");
    private static readonly HeroId Bob = new("h-bob");
    private static readonly HeroId Charlie = new("h-charlie");
    private static readonly ItemId Stone = new("石料");
    private static readonly ItemId Gold = new("金币");   // 物品化的金币（≠ 资源金币）
    private static readonly DomainResourceId GoldResource = new(GoldKey);

    /// <summary>建一个 Alice + Bob 的英雄组，Alice 持 100 金 + 10 石；Bob 持 100 金 + 0 石。</summary>
    private static (Dictionary<HeroId, HeroAgent> heroes, DomainVillage village)
        MakeScene()
    {
        var village = new DomainVillage("v1");
        // M5 / D-033：摆摊位置必须落在建筑影响区。
        // 旧 M3 测试默认 (gridX, gridY) = (0, 0) —— 给一个 footprint 在 (-5, -5) 的市集
        // 影响区 covers [-15..-5+10] = [-15..5]（X/Y 同），(0, 0) 在范围内且不重叠。
        var offMarket = new ClaudeCodeGameStudios.Domain.Village.BuildingInstance(
            new ClaudeCodeGameStudios.Domain.Village.BuildingId("off_market"),
            gridX: -5, gridY: -5, footprintW: 1, footprintH: 1,
            constructionProgress: 1.0,
            affectsAreaW: 11, affectsAreaH: 11);
        village.TryPlace(offMarket);
        var heroes = new Dictionary<HeroId, HeroAgent>();

        var aliceBag = new Inventory().TryAdd(Stone, 10);
        var aliceWallet = new Wallet(new Dictionary<DomainResourceId, long>
        {
            { GoldResource, 100L }
        });
        var alice = new HeroAgent { Name = "Alice" };
        alice.RestoreBag(aliceBag!);
        alice.RestoreWallet(aliceWallet);
        heroes[Alice] = alice;

        var bobWallet = new Wallet(new Dictionary<DomainResourceId, long>
        {
            { GoldResource, 100L }
        });
        var bob = new HeroAgent { Name = "Bob" };
        bob.RestoreBag(new Inventory());
        bob.RestoreWallet(bobWallet);
        heroes[Bob] = bob;

        return (heroes, village);
    }

    private static PriceBook MakeBook() => PriceBook.Default;

    // ── PlaceStall：前置条件 ─────────────────────────────────────────

    [TestCase]
    public void PlaceStall_NotInScene_ReturnsNull()
    {
        var (heroes, village) = MakeScene();
        var svc = new StallService(village, MakeBook(), id => heroes.TryGetValue(id, out var h) ? h : null);

        // 顺手改 Alice 状态为 Away
        heroes[Alice].Away = true;
        var r = svc.PlaceStall(Alice, Stone, 5, 10.0);
        Assertions.AssertThat(r).IsNull();
    }

    [TestCase]
    public void PlaceStall_ToSite_ReturnsNull()
    {
        var (heroes, village) = MakeScene();
        var svc = new StallService(village, MakeBook(), id => heroes.TryGetValue(id, out var h) ? h : null);

        heroes[Alice].ToSite = true;
        var r = svc.PlaceStall(Alice, Stone, 5, 10.0);
        Assertions.AssertThat(r).IsNull();
    }

    [TestCase]
    public void PlaceStall_NotEnoughStock_ReturnsNull()
    {
        var (heroes, village) = MakeScene();
        var svc = new StallService(village, MakeBook(), id => heroes.TryGetValue(id, out var h) ? h : null);

        // Alice 只有 10 石料，要挪 11 → 拒
        var r = svc.PlaceStall(Alice, Stone, 11, 10.0);
        Assertions.AssertThat(r).IsNull();
        // 货一字不动
        Assertions.AssertThat(heroes[Alice].Bag.Count(Stone)).IsEqual(10L);
    }

    [TestCase]
    public void PlaceStall_ItemNotInPriceBook_ReturnsNull()
    {
        var (heroes, village) = MakeScene();
        var svc = new StallService(village, MakeBook(), id => heroes.TryGetValue(id, out var h) ? h : null);

        var mysteryItem = new ItemId("谜之物品");
        var r = svc.PlaceStall(Alice, mysteryItem, 1, 10.0);
        Assertions.AssertThat(r).IsNull();
    }

    // ── PlaceStall：成功 ──────────────────────────────────────────────

    [TestCase]
    public void PlaceStall_Success_MovesStockFromBagToStall()
    {
        var (heroes, village) = MakeScene();
        var svc = new StallService(village, MakeBook(), id => heroes.TryGetValue(id, out var h) ? h : null);

        var r = svc.PlaceStall(Alice, Stone, 5, 10.0);
        Assertions.AssertThat(r).IsNotNull();
        // Bag: 10 - 5 = 5
        Assertions.AssertThat(r!.NewBag.Count(Stone)).IsEqual(5L);
        // 摊位：5 份，Open，单价 5（PriceBook.Default 石料 = 5）
        Assertions.AssertThat(r.Stall.Stock).IsEqual(5L);
        Assertions.AssertThat(r.Stall.Status).IsEqual(DomainStallStatus.Open);
        Assertions.AssertThat(r.Stall.UnitPrice).IsEqual(5L);
    }

    [TestCase]
    public void PlaceStall_OneHeroOneStall_SecondAttemptReturnsNull()
    {
        var (heroes, village) = MakeScene();
        var svc = new StallService(village, MakeBook(), id => heroes.TryGetValue(id, out var h) ? h : null);

        // 第一次：成功（挪 5，留 5）
        var r1 = svc.PlaceStall(Alice, Stone, 5, 10.0);
        Assertions.AssertThat(r1).IsNotNull();

        // 第二次：失败（一人一摊），状态保持第一次
        var r2 = svc.PlaceStall(Alice, Stone, 3, 11.0);
        Assertions.AssertThat(r2).IsNull();
        // Bag 不变
        Assertions.AssertThat(heroes[Alice].Bag.Count(Stone)).IsEqual(5L);
    }

    [TestCase]
    public void PlaceStall_AfterClose_CanPlaceAgain()
    {
        var (heroes, village) = MakeScene();
        var svc = new StallService(village, MakeBook(), id => heroes.TryGetValue(id, out var h) ? h : null);

        var r1 = svc.PlaceStall(Alice, Stone, 5, 10.0);
        svc.CloseStall(Alice);
        var r2 = svc.PlaceStall(Alice, Stone, 3, 11.0);
        Assertions.AssertThat(r2).IsNotNull();   // Closed 后可重开
    }

    // ── BuyFromStall：5% 入村金库 ────────────────────────────────────

    [TestCase]
    public void BuyFromStall_Success_5PercentGoesToVillage_95ToSeller()
    {
        var (heroes, village) = MakeScene();
        var svc = new StallService(village, MakeBook(), id => heroes.TryGetValue(id, out var h) ? h : null);

        // Alice 开摊卖 10 份石料 (单价 5)
        svc.PlaceStall(Alice, Stone, 10, 10.0);
        // Bob 买 1 份 → 总 5 金
        // 5% = 0（5×5/100=0.25 → floor 0），残差归卖家 → 卖家 5 + 村 0
        var r = svc.BuyFromStall(Alice, Bob, Stone, 1, 20.0);

        Assertions.AssertThat(r).IsNotNull();
        Assertions.AssertThat(r!.TotalAmount).IsEqual(5L);
        Assertions.AssertThat(r.SellerNet).IsEqual(5L);
        Assertions.AssertThat(r.VillageCut).IsEqual(0L);
        // 村金库：0（villageCut=0 时不调 Add）
        Assertions.AssertThat(village.Get(GoldResource)).IsEqual(0L);
        // 摊主 Alice：钱包加 5 → 100 + 5 = 105
        Assertions.AssertThat(heroes[Alice].Wallet.Amount(GoldResource)).IsEqual(105L);
        // Alice 的 Bag：10 份全挪去摆摊 → 0；买走 1 份不动（货从摊位走，不是从 Alice Bag）
        Assertions.AssertThat(heroes[Alice].Bag.Count(Stone)).IsEqual(0L);
        // Bob：钱包 100 - 5 = 95；Bag：1 份石料
        Assertions.AssertThat(heroes[Bob].Wallet.Amount(GoldResource)).IsEqual(95L);
        Assertions.AssertThat(heroes[Bob].Bag.Count(Stone)).IsEqual(1L);
        // 摊位库存：10 - 1 = 9，仍 Open
        Assertions.AssertThat(r.NewStall.Stock).IsEqual(9L);
        Assertions.AssertThat(r.NewStall.Status).IsEqual(DomainStallStatus.Open);
    }

    [TestCase]
    public void BuyFromStall_100Gold_5Percent_NoRemainder()
    {
        var (heroes, village) = MakeScene();
        var svc = new StallService(village, MakeBook(), id => heroes.TryGetValue(id, out var h) ? h : null);

        // Alice 钱包 + Bag 都加大，挪 100 份石料摆摊
        var aliceBiggerWallet = new Wallet(new Dictionary<DomainResourceId, long>
        {
            { GoldResource, 10_000L }
        });
        var aliceBiggerBag = new Inventory().TryAdd(Stone, 100);
        heroes[Alice].RestoreWallet(aliceBiggerWallet);
        heroes[Alice].RestoreBag(aliceBiggerBag!);
        svc.PlaceStall(Alice, Stone, 100, 10.0);

        // Bob 钱包加大到 200
        var bobBiggerWallet = new Wallet(new Dictionary<DomainResourceId, long>
        {
            { GoldResource, 200L }
        });
        heroes[Bob].RestoreWallet(bobBiggerWallet);
        // Bob 买 20 份 → 总 100 金
        // 5% = 5 整除 → 卖家 95 + 村 5
        var r = svc.BuyFromStall(Alice, Bob, Stone, 20, 20.0);

        Assertions.AssertThat(r).IsNotNull();
        Assertions.AssertThat(r!.TotalAmount).IsEqual(100L);
        Assertions.AssertThat(r.SellerNet).IsEqual(95L);
        Assertions.AssertThat(r.VillageCut).IsEqual(5L);
        // 村金库：5
        Assertions.AssertThat(village.Get(GoldResource)).IsEqual(5L);
        // 摊主钱包：10_000 + 95 = 10_095
        Assertions.AssertThat(heroes[Alice].Wallet.Amount(GoldResource)).IsEqual(10_095L);
        // Bob 钱包：200 - 100 = 100
        Assertions.AssertThat(heroes[Bob].Wallet.Amount(GoldResource)).IsEqual(100L);
    }

    // ── BuyFromStall：I15 防线 ────────────────────────────────────────

    [TestCase]
    public void BuyFromStall_BuyerCannotAfford_ReturnsNull_NoStateChange()
    {
        var (heroes, village) = MakeScene();
        var svc = new StallService(village, MakeBook(), id => heroes.TryGetValue(id, out var h) ? h : null);

        svc.PlaceStall(Alice, Stone, 5, 10.0);
        // Bob 钱包：100 金；买 100 份 → 500 金 → 钱不够
        var r = svc.BuyFromStall(Alice, Bob, Stone, 100, 20.0);
        Assertions.AssertThat(r).IsNull();
        // 状态一字不动
        Assertions.AssertThat(heroes[Bob].Wallet.Amount(GoldResource)).IsEqual(100L);
        Assertions.AssertThat(heroes[Alice].Bag.Count(Stone)).IsEqual(5L);
        Assertions.AssertThat(heroes[Alice].Wallet.Amount(GoldResource)).IsEqual(100L);
        Assertions.AssertThat(village.Get(GoldResource)).IsEqual(0L);
        // 摊位库存：5
        var existing = svc.GetStall(Alice);
        Assertions.AssertThat(existing).IsNotNull();
        Assertions.AssertThat(existing!.Stock).IsEqual(5L);
    }

    [TestCase]
    public void BuyFromStall_NoStall_ReturnsNull()
    {
        var (heroes, village) = MakeScene();
        var svc = new StallService(village, MakeBook(), id => heroes.TryGetValue(id, out var h) ? h : null);

        // Charlie 不在 heroes；用 Bob 试买 Charlie 的摊（不存在）
        var r = svc.BuyFromStall(Charlie, Bob, Stone, 1, 20.0);
        Assertions.AssertThat(r).IsNull();
    }

    [TestCase]
    public void BuyFromStall_SameHero_ReturnsNull()
    {
        var (heroes, village) = MakeScene();
        var svc = new StallService(village, MakeBook(), id => heroes.TryGetValue(id, out var h) ? h : null);

        svc.PlaceStall(Alice, Stone, 5, 10.0);
        // Alice 自买 = 卖方 = 买方 → 拒
        var r = svc.BuyFromStall(Alice, Alice, Stone, 1, 20.0);
        Assertions.AssertThat(r).IsNull();
    }

    [TestCase]
    public void BuyFromStall_StockRunsOut_NextBuyReturnsNull_StallSoldOut()
    {
        var (heroes, village) = MakeScene();
        var svc = new StallService(village, MakeBook(), id => heroes.TryGetValue(id, out var h) ? h : null);

        svc.PlaceStall(Alice, Stone, 2, 10.0);
        // 第一次：买 2 份
        var r1 = svc.BuyFromStall(Alice, Bob, Stone, 2, 20.0);
        Assertions.AssertThat(r1).IsNotNull();
        Assertions.AssertThat(r1!.NewStall.Status).IsEqual(DomainStallStatus.SoldOut);
        // 第二次：SoldOut → 拒
        var r2 = svc.BuyFromStall(Alice, Bob, Stone, 1, 21.0);
        Assertions.AssertThat(r2).IsNull();
    }

    [TestCase]
    public void BuyFromStall_ExceedingStock_ReturnsNull()
    {
        var (heroes, village) = MakeScene();
        var svc = new StallService(village, MakeBook(), id => heroes.TryGetValue(id, out var h) ? h : null);

        // Alice 摆 2 份石料；Bob 想一次买 3 份
        svc.PlaceStall(Alice, Stone, 2, 10.0);
        var r = svc.BuyFromStall(Alice, Bob, Stone, 3, 20.0);
        // 摊位库存 < qty → 拒
        Assertions.AssertThat(r).IsNull();
        // 状态一字不动：摊位仍是 2 份
        var s = svc.GetStall(Alice);
        Assertions.AssertThat(s!.Stock).IsEqual(2L);
    }

    [TestCase]
    public void BuyFromStall_BuyerBagCapacityExceeded_ReturnsNull()
    {
        var (heroes, village) = MakeScene();
        var svc = new StallService(village, MakeBook(), id => heroes.TryGetValue(id, out var h) ? h : null);

        // Alice 只有 10 份石料，挪 10 份摆摊
        svc.PlaceStall(Alice, Stone, 10, 10.0);

        // Bob 背包容量 = 5
        var bobSmallBag = new Inventory(capacity: 5);
        heroes[Bob].RestoreBag(bobSmallBag);
        // 买 6 份 → 超出 Bob 背包容量 → Trade.Apply 内部拒
        var r = svc.BuyFromStall(Alice, Bob, Stone, 6, 20.0);
        Assertions.AssertThat(r).IsNull();
        // Bob 钱包不动
        Assertions.AssertThat(heroes[Bob].Wallet.Amount(GoldResource)).IsEqual(100L);
        // 摊位库存不动（仍是 10）
        var existing = svc.GetStall(Alice);
        Assertions.AssertThat(existing).IsNotNull();
        Assertions.AssertThat(existing!.Stock).IsEqual(10L);
    }

    // ── CloseStall ─────────────────────────────────────────────────────

    [TestCase]
    public void CloseStall_FromOpen_TransitionsToClosed()
    {
        var (heroes, village) = MakeScene();
        var svc = new StallService(village, MakeBook(), id => heroes.TryGetValue(id, out var h) ? h : null);

        svc.PlaceStall(Alice, Stone, 5, 10.0);
        var ok = svc.CloseStall(Alice);
        Assertions.AssertThat(ok).IsTrue();
        var s = svc.GetStall(Alice);
        Assertions.AssertThat(s!.Status).IsEqual(DomainStallStatus.Closed);
    }

    [TestCase]
    public void CloseStall_NoStall_ReturnsFalse()
    {
        var (heroes, village) = MakeScene();
        var svc = new StallService(village, MakeBook(), id => heroes.TryGetValue(id, out var h) ? h : null);
        var ok = svc.CloseStall(Alice);
        Assertions.AssertThat(ok).IsFalse();
    }
}
