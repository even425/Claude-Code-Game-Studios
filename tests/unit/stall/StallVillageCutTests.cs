using System;
using System.Collections.Generic;
using GdUnit4;
using ClaudeCodeGameStudios.Application.Stall;
using ClaudeCodeGameStudios.Domain.Heroes;
using ClaudeCodeGameStudios.Domain.Items;
using ClaudeCodeGameStudios.Domain.Stall;
using DomainVillage = ClaudeCodeGameStudios.Domain.Village.Village;
using DomainResourceId = ClaudeCodeGameStudios.Domain.Village.ResourceId;

namespace ClaudeCodeGameStudios.Tests.Stall;

/// <summary>
/// 村庄税额余数在<b>摊位服务这一层</b>的契约（R-05 第二轮 / D-052）。
/// </summary>
/// <remarks>
/// <para><b>本文档保护：</b></para>
/// <list type="bullet">
/// <item>余数随成交<b>累计</b>，跨笔不清零。</item>
/// <item><b>幂等请求被拒 → 余数不变</b>（不能因为「拒绝了」也把零头记进去）。</item>
/// <item><b>结算失败 → 余数不变</b>，且三方状态一字不动（I15）。</item>
/// <item><b>金额溢出 → 判为入参非法</b>，不是让异常冒到游戏循环里。</item>
/// <item>余数是<b>摊位服务的状态</b>，读档走 <c>RestoreVillageCutCarry</c>。</item>
/// </list>
/// <para>
/// <b>为什么这些必须测</b>：余数是**跨交易的累积状态**。
/// 它一旦在失败路径上被多记或少记，账就会慢慢偏 —— 而这种偏是<b>静默</b>的。
/// </para>
/// </remarks>
[TestSuite]
public class StallVillageCutTests
{
    private const string GoldKey = "金币";
    private static readonly HeroId Alice = new("h-alice");
    private static readonly HeroId Bob = new("h-bob");
    private static readonly ItemId Stone = new("石料");
    private static readonly DomainResourceId GoldResource = new(GoldKey);

    /// <summary>Alice 持 10 石料 + 100 金；Bob 持 100 金。</summary>
    private static (Dictionary<HeroId, HeroAgent> heroes, DomainVillage village)
        MakeScene(long aliceGold = 100L, long bobGold = 100L, long aliceStone = 10L)
    {
        var village = new DomainVillage("v1");
        var offMarket = new ClaudeCodeGameStudios.Domain.Village.BuildingInstance(
            new ClaudeCodeGameStudios.Domain.Village.BuildingId("off_market"),
            gridX: -5, gridY: -5, footprintW: 1, footprintH: 1,
            constructionProgress: 1.0,
            affectsAreaW: 11, affectsAreaH: 11);
        village.TryPlace(offMarket);

        var heroes = new Dictionary<HeroId, HeroAgent>();

        var alice = new HeroAgent { Name = "Alice" };
        alice.RestoreBag(new Inventory().TryAdd(Stone, aliceStone)!);
        alice.RestoreWallet(new Wallet(new Dictionary<DomainResourceId, long> { { GoldResource, aliceGold } }));
        heroes[Alice] = alice;

        var bob = new HeroAgent { Name = "Bob" };
        bob.RestoreBag(new Inventory());
        bob.RestoreWallet(new Wallet(new Dictionary<DomainResourceId, long> { { GoldResource, bobGold } }));
        heroes[Bob] = bob;

        return (heroes, village);
    }

    private static StallService MakeService(
        Dictionary<HeroId, HeroAgent> heroes, DomainVillage village)
        => new(village, TestPrices.Book, id => heroes.TryGetValue(id, out var h) ? h : null);

    // ── 累计 ────────────────────────────────────────────────────────

    /// <summary>多笔成交后余数按累计口径增长，村库收到的钱与累计口径一致。</summary>
    [TestCase]
    public void Buy_MultipleTransactions_AccumulatesRemainder()
    {
        var (heroes, village) = MakeScene();
        var svc = MakeService(heroes, village);
        svc.PlaceStall(Alice, Stone, 10, 10.0);

        // 4 笔 5 金（石料单价 5）：零头 25 × 4 = 100 → 第 4 笔入账 1 金
        for (int i = 0; i < 4; i++)
        {
            var r = svc.TryBuy(Alice, Bob, Stone, 1, 20.0, $"cut-{i}");
            Assertions.AssertThat(r.Bought).IsTrue();
        }

        Assertions.AssertThat(village.Get(GoldResource)).IsEqual(1L);
        Assertions.AssertThat(svc.VillageCutCarry).IsEqual(0L);
    }

    /// <summary>单价 5 的单笔成交：村库当笔仍入 0，零头 25 进 carry。</summary>
    [TestCase]
    public void Buy_SmallAmount_DefersCutIntoCarry()
    {
        var (heroes, village) = MakeScene();
        var svc = MakeService(heroes, village);
        svc.PlaceStall(Alice, Stone, 10, 10.0);

        var r = svc.TryBuy(Alice, Bob, Stone, 1, 20.0, "cut-1");

        Assertions.AssertThat(r.Bought).IsTrue();
        Assertions.AssertThat(village.Get(GoldResource)).IsEqual(0L);
        Assertions.AssertThat(svc.VillageCutCarry).IsEqual(25L);
    }

    // ── 幂等：被拒时余数不变 ────────────────────────────────────────

    /// <summary>同一 requestId 第二次调用被拒 → <b>余数不变</b>。</summary>
    [TestCase]
    public void Buy_DuplicateRequest_DoesNotAccumulateTwice()
    {
        var (heroes, village) = MakeScene();
        var svc = MakeService(heroes, village);
        svc.PlaceStall(Alice, Stone, 10, 10.0);

        var first = svc.TryBuy(Alice, Bob, Stone, 1, 20.0, "same-key");
        long carryAfterFirst = svc.VillageCutCarry;
        long villageAfterFirst = village.Get(GoldResource);

        var second = svc.TryBuy(Alice, Bob, Stone, 1, 20.0, "same-key");

        Assertions.AssertThat(first.Bought).IsTrue();
        Assertions.AssertThat(second.Outcome).IsEqual(StallService.BuyOutcome.DuplicateRequest);
        Assertions.AssertThat(svc.VillageCutCarry).IsEqual(carryAfterFirst);
        Assertions.AssertThat(village.Get(GoldResource)).IsEqual(villageAfterFirst);
    }

    /// <summary>被拒的请求（钱不够）→ <b>余数不变</b>。</summary>
    [TestCase]
    public void Buy_RejectedByPrecheck_DoesNotTouchCarry()
    {
        var (heroes, village) = MakeScene(bobGold: 0L);
        var svc = MakeService(heroes, village);
        svc.PlaceStall(Alice, Stone, 10, 10.0);

        var r = svc.TryBuy(Alice, Bob, Stone, 1, 20.0, "poor");

        Assertions.AssertThat(r.Outcome).IsEqual(StallService.BuyOutcome.CannotAfford);
        Assertions.AssertThat(svc.VillageCutCarry).IsEqual(0L);
        Assertions.AssertThat(village.Get(GoldResource)).IsEqual(0L);
    }

    // ── 失败路径不留半笔账 ──────────────────────────────────────────

    /// <summary>村金库写入抛异常（余额溢出）→ <b>余数与三方状态全不变</b>。</summary>
    /// <remarks>
    /// 要触发村库溢出，交易必须**真的往金库里入钱** ——
    /// 单价 5 的单笔成交 `villageCut = 0`，压根不会调 `Add`。
    /// 所以这里用单价 100（taxUnits = 500 → 本次入账 5 金）。
    /// </remarks>
    [TestCase]
    public void Buy_VillageOverflow_LeavesNoPartialState()
    {
        var (heroes, village) = MakeScene();
        var book = new PriceBook(new[]
        {
            new KeyValuePair<ItemId, long>(Stone, 100L),
        });
        var svc = new StallService(village, book, id => heroes.TryGetValue(id, out var h) ? h : null);
        svc.PlaceStall(Alice, Stone, 10, 10.0);

        // 把村金库塞到 long.MaxValue - 1：再入 5 金就溢出。
        village.Add(GoldResource, long.MaxValue - 1);

        long carryBefore = svc.VillageCutCarry;
        long aliceGoldBefore = heroes[Alice].Wallet.Amount(GoldResource);
        long bobGoldBefore = heroes[Bob].Wallet.Amount(GoldResource);
        long bobStoneBefore = heroes[Bob].Bag.Count(Stone);
        long stockBefore = svc.GetStall(Alice)?.Stock ?? -1;

        bool threw = false;
        try
        {
            svc.TryBuy(Alice, Bob, Stone, 1, 20.0, "overflow");
        }
        catch (OverflowException)
        {
            threw = true;
        }

        Assertions.AssertThat(threw).IsTrue();
        // 什么都没写：余数、双方钱包、买方背包、摊位库存全在原处
        Assertions.AssertThat(svc.VillageCutCarry).IsEqual(carryBefore);
        Assertions.AssertThat(heroes[Alice].Wallet.Amount(GoldResource)).IsEqual(aliceGoldBefore);
        Assertions.AssertThat(heroes[Bob].Wallet.Amount(GoldResource)).IsEqual(bobGoldBefore);
        Assertions.AssertThat(heroes[Bob].Bag.Count(Stone)).IsEqual(bobStoneBefore);
        Assertions.AssertThat(svc.GetStall(Alice)?.Stock ?? -1).IsEqual(stockBefore);
    }

    // ── 溢出：金额乘不得把异常冒到游戏循环 ───────────────────────────

    /// <summary>单价 × 数量溢出 → 判为入参非法，<b>不抛异常</b>，状态不变。</summary>
    /// <remarks>
    /// 这条尤其重要：自主购物的调用链（<c>HeroBehaviorService</c> →
    /// <c>DailyShoppingService</c> → 游戏循环）**没有接住异常** ——
    /// 异常一路冒到 <c>_Process</c> 会把游戏打断。
    /// </remarks>
    [TestCase]
    public void Buy_AmountOverflow_ReturnsInvalidRequest_WithoutThrowing()
    {
        var (heroes, village) = MakeScene(aliceGold: long.MaxValue, bobGold: long.MaxValue);

        // 单价极高：单价 long.MaxValue / 2，买 10 份 → 金额溢出
        var book = new PriceBook(new[]
        {
            new KeyValuePair<ItemId, long>(Stone, long.MaxValue / 2),
        });
        var svc = new StallService(village, book, id => heroes.TryGetValue(id, out var h) ? h : null);
        svc.PlaceStall(Alice, Stone, 10, 10.0);

        var r = svc.TryBuy(Alice, Bob, Stone, 10, 20.0, "huge");

        Assertions.AssertThat(r.Outcome).IsEqual(StallService.BuyOutcome.InvalidRequest);
        Assertions.AssertThat(svc.VillageCutCarry).IsEqual(0L);
        Assertions.AssertThat(heroes[Bob].Bag.Count(Stone)).IsEqual(0L);
    }

    // ── 读档入口 ────────────────────────────────────────────────────

    /// <summary>余数可被恢复（读档用），且范围外直接拒。</summary>
    [TestCase]
    public void RestoreVillageCutCarry_AcceptsRange_RejectsOutOfRange()
    {
        var (heroes, village) = MakeScene();
        var svc = MakeService(heroes, village);

        svc.RestoreVillageCutCarry(42);
        Assertions.AssertThat(svc.VillageCutCarry).IsEqual(42L);

        svc.RestoreVillageCutCarry(99);
        Assertions.AssertThat(svc.VillageCutCarry).IsEqual(99L);

        bool threwNeg = false;
        try { svc.RestoreVillageCutCarry(-1); }
        catch (ArgumentOutOfRangeException) { threwNeg = true; }
        Assertions.AssertThat(threwNeg).IsTrue();

        bool threwBig = false;
        try { svc.RestoreVillageCutCarry(100); }
        catch (ArgumentOutOfRangeException) { threwBig = true; }
        Assertions.AssertThat(threwBig).IsTrue();
    }

    /// <summary>余数跨日不清零（它是累计的，不是每日期的账）。</summary>
    [TestCase]
    public void Buy_CarryIsNotResetByDay()
    {
        var (heroes, village) = MakeScene();
        var svc = MakeService(heroes, village);
        svc.PlaceStall(Alice, Stone, 10, 10.0);

        svc.TryBuy(Alice, Bob, Stone, 1, 20.0, "day1");
        long carryAfterDay1 = svc.VillageCutCarry;

        // 「第二天」：模拟时间往前一天，同一个服务实例（余数不清零）
        svc.TryBuy(Alice, Bob, Stone, 1, 20.0 + 24 * 75, "day2");

        Assertions.AssertThat(svc.VillageCutCarry).IsEqual(carryAfterDay1 * 2);
    }
}
