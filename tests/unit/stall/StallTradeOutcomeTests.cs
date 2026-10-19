using System;
using System.Collections.Generic;
using GdUnit4;
using ClaudeCodeGameStudios.Application.Stall;
using ClaudeCodeGameStudios.Domain.Heroes;
using ClaudeCodeGameStudios.Domain.Items;
using ClaudeCodeGameStudios.Domain.Stall;
using DomainResourceId = ClaudeCodeGameStudios.Domain.Village.ResourceId;
using DomainVillage = ClaudeCodeGameStudios.Domain.Village.Village;

namespace ClaudeCodeGameStudios.Tests.Stall;

/// <summary>
/// 第四阶段 3A：手动购买的完整结果分类与幂等。
/// </summary>
/// <remarks>
/// <para><b>守什么</b>：成功购买时双方货币/背包/摊位库存/村库费用全部正确；
/// 同一业务请求键只成交一次；所有失败场景（余额、库存、无摊、非 Open、商品不符、自己买自己）
/// 都返回可区分原因且<b>状态一字不动</b>（I15）。</para>
/// <para><b>唯一结算入口</b>：`StallService.TryBuy`（内部完成扣款/发货/库存/费用）；
/// 不经过 `Settlement.ApplyTrade`（那是给英雄↔英雄交易预留的）。</para>
/// </remarks>
[TestSuite]
public class StallTradeOutcomeTests
{
    private static readonly HeroId Alice = new("h-alice");
    private static readonly HeroId Bob = new("h-bob");
    private static readonly ItemId Stone = new("石料");
    private static readonly ItemId Wood = new("木料");
    private static readonly DomainResourceId Gold = new("金币");

    /// <summary>Alice 持 10 石料 + 100 金；Bob 持 0 石料 + 100 金；村庄有影响区市集。</summary>
    private static (Dictionary<HeroId, HeroAgent> heroes, DomainVillage village) MakeScene()
    {
        var village = new DomainVillage("v1");
        var market = new ClaudeCodeGameStudios.Domain.Village.BuildingInstance(
            new ClaudeCodeGameStudios.Domain.Village.BuildingId("market"),
            gridX: -5, gridY: -5, footprintW: 1, footprintH: 1,
            constructionProgress: 1.0, affectsAreaW: 11, affectsAreaH: 11);
        village.TryPlace(market);

        var heroes = new Dictionary<HeroId, HeroAgent>();

        var alice = new HeroAgent { Name = "Alice" };
        alice.RestoreBag(new Inventory().TryAdd(Stone, 10)!);
        alice.RestoreWallet(new Wallet(new Dictionary<DomainResourceId, long> { [Gold] = 100L }));
        heroes[Alice] = alice;

        var bob = new HeroAgent { Name = "Bob" };
        bob.RestoreBag(new Inventory());
        bob.RestoreWallet(new Wallet(new Dictionary<DomainResourceId, long> { [Gold] = 100L }));
        heroes[Bob] = bob;

        return (heroes, village);
    }

    private static StallService Service(
        Dictionary<HeroId, HeroAgent> heroes, DomainVillage village)
        => new(village, PriceBook.Default, id => heroes.TryGetValue(id, out var h) ? h : null);

    private static long GoldOf(HeroAgent h) => h.Wallet.Amount(Gold);

    // ── 成功路径 ──────────────────────────────────────────────────────────

    /// <summary>成功购买：买方 -10 金 +2 石；卖方 +10 金；摊位 5→3；村费为 0（10×5% floor）。</summary>
    [TestCase]
    public void TryBuy_Success_UpdatesMoneyBagStockAndVillage()
    {
        var (heroes, village) = MakeScene();
        var svc = Service(heroes, village);
        var alice = heroes[Alice];
        var bob = heroes[Bob];
        svc.PlaceStall(Alice, Stone, 5, 10.0);
        long aliceGold0 = GoldOf(alice), bobGold0 = GoldOf(bob);

        var r = svc.TryBuy(Alice, Bob, Stone, 2, 20.0, "req-1");

        Assertions.AssertThat(r.Outcome).IsEqual(StallService.BuyOutcome.Bought);
        Assertions.AssertThat(r.Bought).IsTrue();
        Assertions.AssertThat(GoldOf(bob)).IsEqual(bobGold0 - 10);
        Assertions.AssertThat(GoldOf(alice)).IsEqual(aliceGold0 + 10);
        Assertions.AssertThat(bob.Bag.Count(Stone)).IsEqual(2L);
        Assertions.AssertThat(svc.GetStall(Alice)!.Stock).IsEqual(3L);
        Assertions.AssertThat(village.Get(Gold)).IsEqual(0L);
        Assertions.AssertThat(r.Result!.VillageCut).IsEqual(0L);
    }

    /// <summary>20 金成交：5% floor = 1 进村库，卖家净收 19。</summary>
    [TestCase]
    public void TryBuy_VillageCutIsApplied()
    {
        var (heroes, village) = MakeScene();
        var svc = Service(heroes, village);
        var alice = heroes[Alice];
        svc.PlaceStall(Alice, Stone, 5, 10.0);

        var r = svc.TryBuy(Alice, Bob, Stone, 4, 20.0, "req-cut");

        Assertions.AssertThat(r.Bought).IsTrue();
        Assertions.AssertThat(r.Result!.TotalAmount).IsEqual(20L);
        Assertions.AssertThat(r.Result.VillageCut).IsEqual(1L);
        Assertions.AssertThat(r.Result.SellerNet).IsEqual(19L);
        Assertions.AssertThat(village.Get(Gold)).IsEqual(1L);
        Assertions.AssertThat(GoldOf(heroes[Bob])).IsEqual(80L);
        Assertions.AssertThat(GoldOf(alice)).IsEqual(119L);
    }

    // ── 幂等 ──────────────────────────────────────────────────────────────

    /// <summary>同一业务请求键第二次：拒绝为 DuplicateRequest，状态不变。</summary>
    [TestCase]
    public void TryBuy_DuplicateRequestKey_NoStateChange()
    {
        var (heroes, village) = MakeScene();
        var svc = Service(heroes, village);
        svc.PlaceStall(Alice, Stone, 5, 10.0);

        svc.TryBuy(Alice, Bob, Stone, 2, 20.0, "same");
        long goldA = GoldOf(heroes[Alice]);
        long goldB = GoldOf(heroes[Bob]);

        var dup = svc.TryBuy(Alice, Bob, Stone, 2, 20.0, "same");

        Assertions.AssertThat(dup.Outcome).IsEqual(StallService.BuyOutcome.DuplicateRequest);
        Assertions.AssertThat(dup.Result).IsNull();
        Assertions.AssertThat(GoldOf(heroes[Alice])).IsEqual(goldA);
        Assertions.AssertThat(GoldOf(heroes[Bob])).IsEqual(goldB);
        Assertions.AssertThat(heroes[Bob].Bag.Count(Stone)).IsEqual(2L);
        Assertions.AssertThat(svc.GetStall(Alice)!.Stock).IsEqual(3L);
    }

    /// <summary>空请求键 = 不做幂等（旧行为）：两次购买都成交。</summary>
    [TestCase]
    public void TryBuy_EmptyRequestKey_IsNotDeduplicated()
    {
        var (heroes, village) = MakeScene();
        var svc = Service(heroes, village);
        svc.PlaceStall(Alice, Stone, 5, 10.0);

        var first = svc.TryBuy(Alice, Bob, Stone, 1, 20.0);
        var second = svc.TryBuy(Alice, Bob, Stone, 1, 21.0);

        Assertions.AssertThat(first.Bought).IsTrue();
        Assertions.AssertThat(second.Bought).IsTrue();
        Assertions.AssertThat(heroes[Bob].Bag.Count(Stone)).IsEqual(2L);
    }

    /// <summary>失败不占用请求键：同一个键在失败后仍可重试成功。</summary>
    [TestCase]
    public void FailedBuy_DoesNotConsumeRequestKey()
    {
        var (heroes, village) = MakeScene();
        var svc = Service(heroes, village);
        svc.PlaceStall(Alice, Stone, 5, 10.0);

        var fail = svc.TryBuy(Alice, Bob, Stone, 999, 20.0, "retry-key");
        var ok = svc.TryBuy(Alice, Bob, Stone, 1, 21.0, "retry-key");

        Assertions.AssertThat(fail.Outcome).IsEqual(StallService.BuyOutcome.InsufficientStock);
        Assertions.AssertThat(ok.Bought).IsTrue();
        Assertions.AssertThat(heroes[Bob].Bag.Count(Stone)).IsEqual(1L);
    }

    // ── 失败路径：状态不变 ────────────────────────────────────────────────

    [TestCase]
    public void TryBuy_InsufficientStock_NoStateChange()
    {
        var (heroes, village) = MakeScene();
        var svc = Service(heroes, village);
        svc.PlaceStall(Alice, Stone, 2, 10.0);
        long goldA = GoldOf(heroes[Alice]);
        long goldB = GoldOf(heroes[Bob]);

        var r = svc.TryBuy(Alice, Bob, Stone, 3, 20.0, "stock");

        Assertions.AssertThat(r.Outcome).IsEqual(StallService.BuyOutcome.InsufficientStock);
        Assertions.AssertThat(GoldOf(heroes[Alice])).IsEqual(goldA);
        Assertions.AssertThat(GoldOf(heroes[Bob])).IsEqual(goldB);
        Assertions.AssertThat(heroes[Bob].Bag.Count(Stone)).IsEqual(0L);
        Assertions.AssertThat(svc.GetStall(Alice)!.Stock).IsEqual(2L);
    }

    [TestCase]
    public void TryBuy_CannotAfford_NoStateChange()
    {
        var (heroes, village) = MakeScene();
        var svc = Service(heroes, village);
        svc.PlaceStall(Alice, Stone, 5, 10.0);
        heroes[Bob].RestoreWallet(new Wallet(new Dictionary<DomainResourceId, long> { [Gold] = 3L }));

        var r = svc.TryBuy(Alice, Bob, Stone, 2, 20.0, "poor");

        Assertions.AssertThat(r.Outcome).IsEqual(StallService.BuyOutcome.CannotAfford);
        Assertions.AssertThat(GoldOf(heroes[Bob])).IsEqual(3L);
        Assertions.AssertThat(heroes[Bob].Bag.Count(Stone)).IsEqual(0L);
        Assertions.AssertThat(svc.GetStall(Alice)!.Stock).IsEqual(5L);
    }

    [TestCase]
    public void TryBuy_NoStall_Rejected()
    {
        var (heroes, village) = MakeScene();
        var svc = Service(heroes, village);

        var r = svc.TryBuy(Alice, Bob, Stone, 1, 20.0, "nostall");

        Assertions.AssertThat(r.Outcome).IsEqual(StallService.BuyOutcome.NoStall);
    }

    [TestCase]
    public void TryBuy_ClosedStall_Rejected()
    {
        var (heroes, village) = MakeScene();
        var svc = Service(heroes, village);
        svc.PlaceStall(Alice, Stone, 5, 10.0);
        svc.CloseStall(Alice);

        var r = svc.TryBuy(Alice, Bob, Stone, 1, 20.0, "closed");

        Assertions.AssertThat(r.Outcome).IsEqual(StallService.BuyOutcome.StallNotOpen);
        Assertions.AssertThat(heroes[Bob].Bag.Count(Stone)).IsEqual(0L);
    }

    [TestCase]
    public void TryBuy_WrongItem_Rejected()
    {
        var (heroes, village) = MakeScene();
        var svc = Service(heroes, village);
        svc.PlaceStall(Alice, Stone, 5, 10.0);

        var r = svc.TryBuy(Alice, Bob, Wood, 1, 20.0, "wrongitem");

        Assertions.AssertThat(r.Outcome).IsEqual(StallService.BuyOutcome.WrongItem);
    }

    [TestCase]
    public void TryBuy_SameHero_Rejected()
    {
        var (heroes, village) = MakeScene();
        var svc = Service(heroes, village);
        svc.PlaceStall(Alice, Stone, 5, 10.0);

        var r = svc.TryBuy(Alice, Alice, Stone, 1, 20.0, "self");

        Assertions.AssertThat(r.Outcome).IsEqual(StallService.BuyOutcome.SameHero);
        Assertions.AssertThat(svc.GetStall(Alice)!.Stock).IsEqual(5L);
    }

    [TestCase]
    public void TryBuy_InvalidRequest_Rejected()
    {
        var (heroes, village) = MakeScene();
        var svc = Service(heroes, village);
        svc.PlaceStall(Alice, Stone, 5, 10.0);

        Assertions.AssertThat(svc.TryBuy(Alice, Bob, Stone, 0, 20.0).Outcome)
            .IsEqual(StallService.BuyOutcome.InvalidRequest);
        Assertions.AssertThat(svc.TryBuy(Alice, Bob, Stone, 1, -1.0).Outcome)
            .IsEqual(StallService.BuyOutcome.InvalidRequest);
        Assertions.AssertThat(svc.TryBuy(new HeroId(""), Bob, Stone, 1, 20.0).Outcome)
            .IsEqual(StallService.BuyOutcome.InvalidRequest);
    }

    /// <summary>旧入口 <c>BuyFromStall</c> 行为不变：成功返结果、失败返 null。</summary>
    [TestCase]
    public void BuyFromStall_LegacyWrapper_StillReturnsNullOnFailure()
    {
        var (heroes, village) = MakeScene();
        var svc = Service(heroes, village);
        svc.PlaceStall(Alice, Stone, 5, 10.0);

        Assertions.AssertThat(svc.BuyFromStall(Alice, Bob, Stone, 1, 20.0)).IsNotNull();
        Assertions.AssertThat(svc.BuyFromStall(Alice, Bob, Stone, 999, 21.0)).IsNull();
    }
}
