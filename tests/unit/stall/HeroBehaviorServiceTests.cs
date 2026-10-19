using System;
using System.Collections.Generic;
using GdUnit4;
using ClaudeCodeGameStudios.Application.Stall;
using ClaudeCodeGameStudios.Domain.Heroes;
using ClaudeCodeGameStudios.Domain.Items;
using DomainVillage = ClaudeCodeGameStudios.Domain.Village.Village;
using DomainResourceId = ClaudeCodeGameStudios.Domain.Village.ResourceId;

namespace ClaudeCodeGameStudios.Tests.Stall;

/// <summary>
/// <see cref="HeroBehaviorService"/> 单元测试（M4 / D-025）。
///
/// <para><b>本文档保护：</b></para>
/// <list type="bullet">
/// <item>频率上限：<c>LastShoppingDay == today</c> → 跳过。</item>
/// <item>不在场 / 忙 → 跳过（与 PlaceStall 镜像）。</item>
/// <item>性格权重：肝/莽/傲/挂机 四类边界。</item>
/// <item>触发：<c>random &lt; desire</c> 才买。</item>
/// <item>找不到 Open 摊位 → 跳过。</item>
/// <item>钱不够 / 背包 ≥ 5 → 跳过。</item>
/// <item>真买：英雄钱包 / Bag / 摊位 / 村金库 四方都按 I15 更新；<c>LastShoppingDay</c> 标今天。</item>
/// </list>
///
/// <para><b>为什么用 <c>Func&lt;double&gt;</c> 注入随机源</b>：测试纪律要求"看红过"，
/// 全局 <c>Random</c> 不可控；注入可固定种子级精度。</para>
/// </summary>
[TestSuite]
public class HeroBehaviorServiceTests
{
    private const string GoldKey = "金币";
    private static readonly DomainResourceId GoldResource = new(GoldKey);
    private static readonly HeroId Alice = new("h-alice");
    private static readonly HeroId Bob = new("h-bob");
    private static readonly ItemId Stone = new("石料");

    /// <summary>固定 random = 0.0（最小值） —— 必触发（任何 desire > 0 都满足 r &lt; desire）。</summary>
    private static readonly Func<double> AlwaysZero = () => 0.0;
    /// <summary>固定 random = 0.99（最大值） —— 必不触发（除非 desire > 0.99，即"肝"+"莽" = 1.5×1.2 = 1.8）。</summary>
    private static readonly Func<double> AlmostOne = () => 0.99;

    /// <summary>建一个 Alice + Bob 的英雄组，Bob 持 100 金，开摊卖石。</summary>
    private static (Dictionary<HeroId, HeroAgent> heroes, DomainVillage village, StallService svc)
        MakeScene(string alicePersonality = "莽", string aliceDiligence = "随手")
    {
        var village = new DomainVillage("v1");
        // M5：摆摊位置必须在建筑影响区。给 (-5,-5) 一个市集，影响区覆盖 (0,0)
        var offMarket = new ClaudeCodeGameStudios.Domain.Village.BuildingInstance(
            new ClaudeCodeGameStudios.Domain.Village.BuildingId("off_market"),
            gridX: -5, gridY: -5, footprintW: 1, footprintH: 1,
            constructionProgress: 1.0,
            affectsAreaW: 11, affectsAreaH: 11);
        village.TryPlace(offMarket);
        var heroes = new Dictionary<HeroId, HeroAgent>();

        var aliceBag = new Inventory();
        var aliceWallet = new Wallet(new Dictionary<DomainResourceId, long> { { GoldResource, 100L } });
        var alice = new HeroAgent
        {
            Name = "Alice",
            Personality = alicePersonality,
            Diligence = aliceDiligence,
        };
        alice.RestoreBag(aliceBag);
        alice.RestoreWallet(aliceWallet);
        heroes[Alice] = alice;

        var bobBag = new Inventory().TryAdd(Stone, 5)!;
        var bobWallet = new Wallet();
        var bob = new HeroAgent { Name = "Bob" };
        bob.RestoreBag(bobBag);
        bob.RestoreWallet(bobWallet);
        heroes[Bob] = bob;

        var svc = new StallService(village, PriceBook.Default, id => heroes.TryGetValue(id, out var h) ? h : null);
        return (heroes, village, svc);
    }

    // ── 性格权重（纯函数） ───────────────────────────────────────────

    [TestCase]
    public void DesireMultiplier_HepMang_1_8()
    {
        // 肝 1.5 × 莽 1.2 = 1.8（IEEE 754 浮点精度：1.7999...9998）
        var v = HeroBehaviorService.DesireMultiplier("莽", "肝");
        Assertions.AssertThat(Math.Abs(v - 1.8)).IsLess(1e-9);
    }

    [TestCase]
    public void DesireMultiplier_ProudLaze_0_1()
    {
        // 傲 0.5 × 挂机 0.2 = 0.1
        var v = HeroBehaviorService.DesireMultiplier("傲", "挂机");
        Assertions.AssertThat(Math.Abs(v - 0.1)).IsLess(1e-9);
    }

    [TestCase]
    public void DesireMultiplier_DefaultCasual_1_0()
    {
        // 随手 + 默认 Personality = 1.0
        Assertions.AssertThat(HeroBehaviorService.DesireMultiplier("未知", "随手")).IsEqual(1.0);
    }

    // ── 频率上限 ──────────────────────────────────────────────────────

    [TestCase]
    public void SimulateShopping_AlreadyShoppedToday_Skipped()
    {
        var (heroes, _, svc) = MakeScene();
        heroes[Alice].LastShoppingDay = 1;
        var hbs = new HeroBehaviorService(svc, id => heroes.TryGetValue(id, out var h) ? h : null, AlwaysZero);

        var r = hbs.SimulateShopping(Alice, today: 1, simTime: 20.0);
        Assertions.AssertThat(r.Outcome).IsEqual(HeroBehaviorService.ShoppingOutcome.Skipped_AlreadyShoppedToday);
    }

    // ── 在场 / 忙 镜像 ───────────────────────────────────────────────

    [TestCase]
    public void SimulateShopping_Away_Skipped()
    {
        var (heroes, _, svc) = MakeScene();
        heroes[Alice].Away = true;
        var hbs = new HeroBehaviorService(svc, id => heroes.TryGetValue(id, out var h) ? h : null, AlwaysZero);
        var r = hbs.SimulateShopping(Alice, today: 1, simTime: 20.0);
        Assertions.AssertThat(r.Outcome).IsEqual(HeroBehaviorService.ShoppingOutcome.Skipped_PrecheckFailed);
    }

    [TestCase]
    public void SimulateShopping_ToSite_Skipped()
    {
        var (heroes, _, svc) = MakeScene();
        heroes[Alice].ToSite = true;
        var hbs = new HeroBehaviorService(svc, id => heroes.TryGetValue(id, out var h) ? h : null, AlwaysZero);
        var r = hbs.SimulateShopping(Alice, today: 1, simTime: 20.0);
        Assertions.AssertThat(r.Outcome).IsEqual(HeroBehaviorService.ShoppingOutcome.Skipped_PrecheckFailed);
    }

    // ── 性格 + 随机 ───────────────────────────────────────────────────

    [TestCase]
    public void SimulateShopping_ProudLaze_AlmostOneRandom_StillSkipped_NoDesire()
    {
        // 傲+挂机 = desire 0.1，r=0.99 ≥ 0.1 → 必不触发 → Skipped_NoDesire
        var (heroes, _, svc) = MakeScene("傲", "挂机");
        svc.PlaceStall(Bob, Stone, 5, 10.0);
        var hbs = new HeroBehaviorService(svc, id => heroes.TryGetValue(id, out var h) ? h : null, AlmostOne);
        var r = hbs.SimulateShopping(Alice, today: 1, simTime: 20.0);
        Assertions.AssertThat(r.Outcome).IsEqual(HeroBehaviorService.ShoppingOutcome.Skipped_NoDesire);
    }

    [TestCase]
    public void SimulateShopping_HepMang_AlmostOne_StillBought()
    {
        // 肝+莽 = desire 1.8，r=0.99 < 1.8 → 必买
        var (heroes, _, svc) = MakeScene("莽", "肝");
        svc.PlaceStall(Bob, Stone, 5, 10.0);
        var hbs = new HeroBehaviorService(svc, id => heroes.TryGetValue(id, out var h) ? h : null, AlmostOne);
        var r = hbs.SimulateShopping(Alice, today: 1, simTime: 20.0);
        Assertions.AssertThat(r.Outcome).IsEqual(HeroBehaviorService.ShoppingOutcome.Bought);
    }

    // ── 没摊位 ─────────────────────────────────────────────────────────

    [TestCase]
    public void SimulateShopping_NoOpenStall_Skipped()
    {
        var (heroes, _, svc) = MakeScene();
        // 不摆摊 → 没人卖
        var hbs = new HeroBehaviorService(svc, id => heroes.TryGetValue(id, out var h) ? h : null, AlwaysZero);
        var r = hbs.SimulateShopping(Alice, today: 1, simTime: 20.0);
        Assertions.AssertThat(r.Outcome).IsEqual(HeroBehaviorService.ShoppingOutcome.Skipped_NoOpenStall);
    }

    // ── 成功路径 ─────────────────────────────────────────────────────

    [TestCase]
    public void SimulateShopping_Success_AllFourSidesUpdated()
    {
        var (heroes, village, svc) = MakeScene();
        svc.PlaceStall(Bob, Stone, 5, 10.0);
        var hbs = new HeroBehaviorService(svc, id => heroes.TryGetValue(id, out var h) ? h : null, AlwaysZero);

        var r = hbs.SimulateShopping(Alice, today: 1, simTime: 20.0);
        Assertions.AssertThat(r.Outcome).IsEqual(HeroBehaviorService.ShoppingOutcome.Bought);
        // Alice：钱包 -5，B：钱包 +5（残差归卖方），摊位 -1，村 +0（5% floor = 0）
        Assertions.AssertThat(heroes[Alice].Wallet.Amount(GoldResource)).IsEqual(95L);
        Assertions.AssertThat(heroes[Alice].Bag.Count(Stone)).IsEqual(1L);
        Assertions.AssertThat(heroes[Bob].Wallet.Amount(GoldResource)).IsEqual(5L);
        Assertions.AssertThat(village.Get(GoldResource)).IsEqual(0L);
        // LastShoppingDay = 1
        Assertions.AssertThat(heroes[Alice].LastShoppingDay).IsEqual(1);
        // 摊位：5 → 4
        var s = svc.GetStall(Bob);
        Assertions.AssertThat(s!.Stock).IsEqual(4L);
    }

    // ── 钱不够 ─────────────────────────────────────────────────────────

    [TestCase]
    public void SimulateShopping_BuyerBroke_Skipped()
    {
        var (heroes, _, svc) = MakeScene();
        // Alice 钱包 100 → 改 0
        heroes[Alice].RestoreWallet(new Wallet());
        svc.PlaceStall(Bob, Stone, 5, 10.0);
        var hbs = new HeroBehaviorService(svc, id => heroes.TryGetValue(id, out var h) ? h : null, AlwaysZero);

        var r = hbs.SimulateShopping(Alice, today: 1, simTime: 20.0);
        Assertions.AssertThat(r.Outcome).IsEqual(HeroBehaviorService.ShoppingOutcome.Skipped_PrecheckFailed);
    }

    // ── 背包够 5 → 不买 ──────────────────────────────────────────────

    [TestCase]
    public void SimulateShopping_BagAlreadyHas5_Skipped()
    {
        var (heroes, _, svc) = MakeScene();
        // Alice 背包先塞 5 份石
        var aliceBag = new Inventory().TryAdd(Stone, 5)!;
        heroes[Alice].RestoreBag(aliceBag);
        svc.PlaceStall(Bob, Stone, 5, 10.0);
        var hbs = new HeroBehaviorService(svc, id => heroes.TryGetValue(id, out var h) ? h : null, AlwaysZero);

        var r = hbs.SimulateShopping(Alice, today: 1, simTime: 20.0);
        Assertions.AssertThat(r.Outcome).IsEqual(HeroBehaviorService.ShoppingOutcome.Skipped_PrecheckFailed);
    }
}
