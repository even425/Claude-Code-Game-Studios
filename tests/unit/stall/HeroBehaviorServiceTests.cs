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
/// <item>钱不够 → 跳过；背包 ≥ 5 的摊在<b>抽样前</b>被剔除（D-053）。</item>
/// <item>单次买量 = min(库存, 余额÷单价, 背包余量)（D-053：买尽可能多，上限仍在）。</item>
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
    private static readonly HeroId Carol = new("h-carol");
    private static readonly ItemId Stone = new("石料");
    private static readonly ItemId Wood = new("木料");

    /// <summary>固定 random = 0.0（最小值） —— 必触发（任何 desire > 0 都满足 r &lt; desire）。</summary>
    private static readonly Func<double> AlwaysZero = () => 0.0;
    /// <summary>固定 random = 0.99（最大值） —— 必不触发（除非 desire > 0.99，即"肝"+"莽" = 1.5×1.2 = 1.8）。</summary>
    private static readonly Func<double> AlmostOne = () => 0.99;

    /// <summary>建一个 Alice + Bob 的英雄组，Bob 持 100 金，开摊卖石。</summary>
    private static (Dictionary<HeroId, HeroAgent> heroes, DomainVillage village, StallService svc)
        MakeScene(string alicePersonality = "莽", string aliceDiligence = "随手", long aliceGold = 100L)
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
        var aliceWallet = new Wallet(new Dictionary<DomainResourceId, long> { { GoldResource, aliceGold } });
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

    /// <summary>
    /// 加一个卖<b>木料</b>的摊主 Carol（摆 5 份）。
    /// </summary>
    /// <remarks>
    /// 为什么需要第三个英雄：R-05 三轮（D-053）之后，「背包已满的物品」会把对应摊
    /// 在<b>抽样前</b>过滤掉 —— 要验「他会转去买别的材料」，
    /// 村里就得住得下<b>另一个</b>摊子。
    /// </remarks>
    private static void AddCarol(Dictionary<HeroId, HeroAgent> heroes, StallService svc)
    {
        var carol = new HeroAgent { Name = "Carol" };
        carol.RestoreBag(new Inventory().TryAdd(Wood, 5)!);
        carol.RestoreWallet(new Wallet());
        heroes[Carol] = carol;
        svc.PlaceStall(Carol, Wood, 5, 10.0);
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
        Assertions.AssertThat((r.Detail ?? "").Contains("已经买过")).IsTrue();
        Assertions.AssertThat(r.SkipReason)
            .IsEqual(HeroBehaviorService.ShoppingSkipReason.AlreadyShopped);
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
        Assertions.AssertThat((r.Detail ?? "").Contains("不在场")).IsTrue();
        Assertions.AssertThat(r.SkipReason)
            .IsEqual(HeroBehaviorService.ShoppingSkipReason.NotPresent);
    }

    [TestCase]
    public void SimulateShopping_ToSite_Skipped()
    {
        var (heroes, _, svc) = MakeScene();
        heroes[Alice].ToSite = true;
        var hbs = new HeroBehaviorService(svc, id => heroes.TryGetValue(id, out var h) ? h : null, AlwaysZero);
        var r = hbs.SimulateShopping(Alice, today: 1, simTime: 20.0);
        Assertions.AssertThat(r.Outcome).IsEqual(HeroBehaviorService.ShoppingOutcome.Skipped_PrecheckFailed);
        Assertions.AssertThat((r.Detail ?? "").Contains("不在场")).IsTrue();
        Assertions.AssertThat(r.SkipReason)
            .IsEqual(HeroBehaviorService.ShoppingSkipReason.NotPresent);
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
        Assertions.AssertThat((r.Detail ?? "").Contains("随机")).IsTrue();
        Assertions.AssertThat(r.SkipReason)
            .IsEqual(HeroBehaviorService.ShoppingSkipReason.NoDesire);
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
        Assertions.AssertThat((r.Detail ?? "").Contains("摊位")).IsTrue();
        Assertions.AssertThat(r.SkipReason)
            .IsEqual(HeroBehaviorService.ShoppingSkipReason.NoOpenStall);
    }

    // ── 成功路径 ─────────────────────────────────────────────────────

    /// <summary>
    /// 成交时四方（买家钱包 / 买家背包 / 摊主钱包 / 村金库）都按 I15 更新。
    /// </summary>
    /// <remarks>
    /// <b>D-053 之后一次买多份</b>：Alice 100 金、摊上 5 份石料（单价 5）、
    /// 背包空（余量 5）→ 买量 = min(5, 100÷5=20, 5) = <b>5 份</b>。
    /// 村抽 = floor(25 × 5%) = 1（金），摊主净得 24 —— 与 D-052 的余数累计口径一致。
    /// </remarks>
    [TestCase]
    public void SimulateShopping_Success_AllFourSidesUpdated()
    {
        var (heroes, village, svc) = MakeScene();
        svc.PlaceStall(Bob, Stone, 5, 10.0);
        var hbs = new HeroBehaviorService(svc, id => heroes.TryGetValue(id, out var h) ? h : null, AlwaysZero);

        var r = hbs.SimulateShopping(Alice, today: 1, simTime: 20.0);
        Assertions.AssertThat(r.Outcome).IsEqual(HeroBehaviorService.ShoppingOutcome.Bought);
        Assertions.AssertThat((r.Detail ?? "").Contains("石料")).IsTrue();
        Assertions.AssertThat(r.Quantity).IsEqual(5L);
        // Alice：钱包 -25（5 份 × 5 金），背包 +5
        Assertions.AssertThat(heroes[Alice].Wallet.Amount(GoldResource)).IsEqual(75L);
        Assertions.AssertThat(heroes[Alice].Bag.Count(Stone)).IsEqual(5L);
        // Bob：净得 24（25 - 1 村抽）；村金库：+1
        Assertions.AssertThat(heroes[Bob].Wallet.Amount(GoldResource)).IsEqual(24L);
        Assertions.AssertThat(village.Get(GoldResource)).IsEqual(1L);
        // LastShoppingDay = 1
        Assertions.AssertThat(heroes[Alice].LastShoppingDay).IsEqual(1);
        // 摊位：5 → 0（一次买光）
        var s = svc.GetStall(Bob);
        Assertions.AssertThat(s!.Stock).IsEqual(0L);
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
        Assertions.AssertThat((r.Detail ?? "").Contains("金币不足")).IsTrue();
        Assertions.AssertThat(r.SkipReason)
            .IsEqual(HeroBehaviorService.ShoppingSkipReason.NotEnoughGold);
    }

    // ── D-053：背包上限判据移到抽样前 ────────────────────────────────

    /// <summary>
    /// 背包里该物品已满 → 那个摊在<b>抽样前</b>就被剔掉；
    /// 村里只有这一个摊 → 报「背包满」（不是「只有自己的摊」）。
    /// </summary>
    /// <remarks>
    /// 旧行为：抽到 → 才发现背满 → 整笔放弃（当天白过）。
    /// 新行为：抽样前就把它剔掉 —— 若剔完没摊了，原因仍然是 <c>BagFull</c>，
    /// 玩家看到的还是「想买没买成·背包满」，但**理由说对了**。
    /// </remarks>
    [TestCase]
    public void SimulateShopping_BagAlreadyHas5_StallFilteredBeforeSampling_ReportsBagFull()
    {
        var (heroes, _, svc) = MakeScene();
        // Alice 背包先塞 5 份石
        var aliceBag = new Inventory().TryAdd(Stone, 5)!;
        heroes[Alice].RestoreBag(aliceBag);
        svc.PlaceStall(Bob, Stone, 5, 10.0);
        var hbs = new HeroBehaviorService(svc, id => heroes.TryGetValue(id, out var h) ? h : null, AlwaysZero);

        var r = hbs.SimulateShopping(Alice, today: 1, simTime: 20.0);
        Assertions.AssertThat(r.Outcome).IsEqual(HeroBehaviorService.ShoppingOutcome.Skipped_PrecheckFailed);
        Assertions.AssertThat((r.Detail ?? "").Contains("背包")).IsTrue();
        Assertions.AssertThat((r.Detail ?? "").Contains("上限 5")).IsTrue();
        Assertions.AssertThat(r.SkipReason)
            .IsEqual(HeroBehaviorService.ShoppingSkipReason.BagFull);
        // 背包满时**不该**成交
        Assertions.AssertThat(heroes[Alice].Bag.Count(Stone)).IsEqual(5L);
    }

    /// <summary>
    /// 背包满了一种材料 → 那个摊被过滤 → 他去买<b>另一种</b>材料的摊。
    /// </summary>
    /// <remarks>
    /// 这正是 D-053 修法的目的：旧规则下英雄当天就白过了，
    /// 新规则下他会转去买别的材料。
    /// </remarks>
    [TestCase]
    public void SimulateShopping_BagFullOnItem_BuysFromAnotherItemsStall()
    {
        var (heroes, _, svc) = MakeScene();
        heroes[Alice].RestoreBag(new Inventory().TryAdd(Stone, 5)!);
        svc.PlaceStall(Bob, Stone, 5, 10.0);
        AddCarol(heroes, svc);   // Carol 卖木料
        var hbs = new HeroBehaviorService(svc, id => heroes.TryGetValue(id, out var h) ? h : null, AlwaysZero);

        var r = hbs.SimulateShopping(Alice, today: 1, simTime: 20.0);

        Assertions.AssertThat(r.Outcome).IsEqual(HeroBehaviorService.ShoppingOutcome.Bought);
        Assertions.AssertThat(r.ItemId).IsEqual(Wood);
        Assertions.AssertThat(r.Seller).IsEqual(Carol);
        // 石料那一摊**没被动过**（它是被过滤掉的，不是被买空）
        Assertions.AssertThat(svc.GetStall(Bob)!.Stock).IsEqual(5L);
    }

    // ── D-053：一次买尽可能多 ─────────────────────────────────────────

    /// <summary>买量 = min(库存, 余额÷单价, 背包余量) —— 三个约束取小。</summary>
    [TestCase]
    public void SimulateShopping_BuysAsMuchAsPossible_LimitedByBagRoom()
    {
        var (heroes, _, svc) = MakeScene();
        // 背包已有 3 份石料 → 余量 2 → 最多再买 2 份
        heroes[Alice].RestoreBag(new Inventory().TryAdd(Stone, 3)!);
        svc.PlaceStall(Bob, Stone, 5, 10.0);
        var hbs = new HeroBehaviorService(svc, id => heroes.TryGetValue(id, out var h) ? h : null, AlwaysZero);

        var r = hbs.SimulateShopping(Alice, today: 1, simTime: 20.0);

        Assertions.AssertThat(r.Outcome).IsEqual(HeroBehaviorService.ShoppingOutcome.Bought);
        Assertions.AssertThat(r.Quantity).IsEqual(2L);
        Assertions.AssertThat(heroes[Alice].Bag.Count(Stone)).IsEqual(5L);   // 正好到上限
        Assertions.AssertThat(heroes[Alice].Wallet.Amount(GoldResource)).IsEqual(90L);
    }

    /// <summary>买量也受<b>余额</b>限制（钱不够就少买，不是不买）。</summary>
    [TestCase]
    public void SimulateShopping_BuysAsMuchAsPossible_LimitedByGold()
    {
        var (heroes, _, svc) = MakeScene(aliceGold: 12L);   // 石料单价 5 → 买得起 2 份
        svc.PlaceStall(Bob, Stone, 5, 10.0);
        var hbs = new HeroBehaviorService(svc, id => heroes.TryGetValue(id, out var h) ? h : null, AlwaysZero);

        var r = hbs.SimulateShopping(Alice, today: 1, simTime: 20.0);

        Assertions.AssertThat(r.Outcome).IsEqual(HeroBehaviorService.ShoppingOutcome.Bought);
        Assertions.AssertThat(r.Quantity).IsEqual(2L);
        Assertions.AssertThat(heroes[Alice].Wallet.Amount(GoldResource)).IsEqual(2L);
        Assertions.AssertThat(svc.GetStall(Bob)!.Stock).IsEqual(3L);
    }

    /// <summary>买量也受<b>摊位库存</b>限制。</summary>
    [TestCase]
    public void SimulateShopping_BuysAsMuchAsPossible_LimitedByStock()
    {
        var (heroes, _, svc) = MakeScene();
        svc.PlaceStall(Bob, Stone, 2, 10.0);   // 只有 2 份
        var hbs = new HeroBehaviorService(svc, id => heroes.TryGetValue(id, out var h) ? h : null, AlwaysZero);

        var r = hbs.SimulateShopping(Alice, today: 1, simTime: 20.0);

        Assertions.AssertThat(r.Outcome).IsEqual(HeroBehaviorService.ShoppingOutcome.Bought);
        Assertions.AssertThat(r.Quantity).IsEqual(2L);
        Assertions.AssertThat(svc.GetStall(Bob)!.Stock).IsEqual(0L);
    }

    /// <summary>
    /// <b>上限没被取消</b>：买完之后背包正好到上限，<b>一份都不许超</b>。
    /// </summary>
    /// <remarks>
    /// 这条是 D-053 的**边界守卫**：修法是「把判据挪到抽样前」，
    /// 不是「取消上限」。若哪天有人把 <c>BagItemLimit</c> 改成 0（= 不限），
    /// 这条会红。
    /// </remarks>
    [TestCase]
    public void SimulateShopping_NeverExceedsBagItemLimit()
    {
        var (heroes, _, svc) = MakeScene();
        heroes[Alice].RestoreBag(new Inventory().TryAdd(Stone, 4)!);   // 余量 1
        svc.PlaceStall(Bob, Stone, 5, 10.0);
        var hbs = new HeroBehaviorService(svc, id => heroes.TryGetValue(id, out var h) ? h : null, AlwaysZero);

        var r = hbs.SimulateShopping(Alice, today: 1, simTime: 20.0);

        Assertions.AssertThat(r.Quantity).IsEqual(1L);
        Assertions.AssertThat(heroes[Alice].Bag.Count(Stone))
            .IsEqual(HeroBehaviorService.BagItemLimit);
    }

    // ── R-03：抽样时排除自己的摊位 ────────────────────────────────────

    /// <summary>
    /// R-03：抽到自己开的摊**不再浪费当天的机会** —— 抽样时就把它剔除，
    /// 英雄去买别人的摊。
    /// </summary>
    /// <remarks>
    /// 旧规则是「随机抽，抽到自己的就跳过」—— 那一抽必然被
    /// <c>SameHero</c> 拒绝，当天什么也没买到，还白占了购物机会。
    /// </remarks>
    [TestCase]
    public void SimulateShopping_OwnStallIsExcluded_BuysFromAnotherStall()
    {
        var (heroes, _, svc) = MakeScene();
        // Alice 自己也有一个摊（她的 5 份石料全摆上去了）
        heroes[Alice].RestoreBag(new Inventory().TryAdd(Stone, 5)!);
        svc.PlaceStall(Alice, Stone, 5, 10.0);
        svc.PlaceStall(Bob, Stone, 5, 10.0);

        var hbs = new HeroBehaviorService(svc, id => heroes.TryGetValue(id, out var h) ? h : null, AlwaysZero);
        var r = hbs.SimulateShopping(Alice, today: 1, simTime: 20.0);

        Assertions.AssertThat(r.Outcome).IsEqual(HeroBehaviorService.ShoppingOutcome.Bought);
        Assertions.AssertThat(r.Seller).IsEqual(Bob);   // 买的是**别人**的摊
    }

    /// <summary>
    /// 村里只有自己开着的摊 —— 买不成，但**原因要说清楚**（R-03）。
    /// </summary>
    /// <remarks>
    /// 这条不能被误报成「背包满 / 钱不够」—— 那会让玩家以为经济系统坏了。
    /// </remarks>
    [TestCase]
    public void SimulateShopping_OnlyOwnStall_ReportsSelfTrade()
    {
        var (heroes, _, svc) = MakeScene();
        heroes[Alice].RestoreBag(new Inventory().TryAdd(Stone, 5)!);
        svc.PlaceStall(Alice, Stone, 5, 10.0);

        var hbs = new HeroBehaviorService(svc, id => heroes.TryGetValue(id, out var h) ? h : null, AlwaysZero);
        var r = hbs.SimulateShopping(Alice, today: 1, simTime: 20.0);

        Assertions.AssertThat(r.Outcome).IsEqual(HeroBehaviorService.ShoppingOutcome.Skipped_PrecheckFailed);
        Assertions.AssertThat((r.Detail ?? "").Contains("只有自己开着的摊")).IsTrue();
        Assertions.AssertThat(r.SkipReason)
            .IsEqual(HeroBehaviorService.ShoppingSkipReason.OnlyOwnStall);
    }
}
