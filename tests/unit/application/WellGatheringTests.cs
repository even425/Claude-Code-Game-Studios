using System.Collections.Generic;
using GdUnit4;
using ClaudeCodeGameStudios.Application;
using ClaudeCodeGameStudios.Domain.Heroes;
using ClaudeCodeGameStudios.Domain.Items;
using ClaudeCodeGameStudios.Domain.Village;
using ClaudeCodeGameStudios.Tests.Items;
using DomainVillage = ClaudeCodeGameStudios.Domain.Village.Village;
using DomainResourceId = ClaudeCodeGameStudios.Domain.Village.ResourceId;

namespace ClaudeCodeGameStudios.Tests.Application;

/// <summary>
/// <b>井 = 采集点</b>（D-105，开发者拍板「井是采集点」）。
/// </summary>
/// <remarks>
/// <para><b>它守的是五件事</b>：
/// <list type="number">
/// <item>没有采集点 ⇒ 采集产量<b>不变</b>（1 份/天，既有规则 D-086）。</item>
/// <item>有采集点（已建成的井）⇒ 采集产量 <b>+1 份/天</b>。</item>
/// <item><b>施工中</b>的井<b>不算</b>（还没打井呢，采集不该变快）。</item>
/// <item>背包装不下 ⇒ <b>只采装得下的那部分</b>（不凭空多出材料）。</item>
/// <item>升到第 2 级 ⇒ 井自动出现 ⇒ 加成<b>自动生效</b>（端到端接线）。</item>
/// </list>
/// <para>⚠️ <b>为什么这条值得守</b>：它给「有井的村子」这一级<b>真正的玩法意义</b>
/// ⇒ 村庄层级阶梯从此不是只有名字上的差别。</para>
/// </remarks>
[TestSuite]
public class WellGatheringTests
{
    private static readonly HeroId Alice = new("h-alice");
    private static readonly ItemId Stone = new("石料");
    private static readonly DomainResourceId GoldResource = new("金币");

    /// <summary>建一个场景：村庄（市集）+ Alice + 按需放一口井。</summary>
    private static (DomainVillage Village, Dictionary<HeroId, HeroAgent> Heroes, GatherService Service)
        MakeScene(bool withWell = false, bool wellBuilt = true, long bagStone = 0)
    {
        var village = new DomainVillage("v1");
        village.TryPlace(new ClaudeCodeGameStudios.Domain.Village.BuildingInstance(
            new ClaudeCodeGameStudios.Domain.Village.BuildingId("village_square"),
            gridX: -5, gridY: -5, footprintW: 1, footprintH: 1,
            constructionProgress: 1.0,
            affectsAreaW: 11, affectsAreaH: 11));

        if (withWell)
        {
            var def = TierBuildings.Well;
            village.TryPlace(new ClaudeCodeGameStudios.Domain.Village.BuildingInstance(
                def.Id, def.GridX, def.GridY, def.FootprintW, def.FootprintH,
                constructionProgress: wellBuilt ? 1.0 : 0.5,
                affectsAreaW: def.AffectsAreaW, affectsAreaH: def.AffectsAreaH));
        }

        var heroes = new Dictionary<HeroId, HeroAgent>();
        var bag = new Inventory();
        if (bagStone > 0) bag = bag.TryAdd(Stone, bagStone)!;

        var alice = new HeroAgent { Name = "Alice" };
        alice.AssignId(Alice);
        alice.RestoreBag(bag);
        alice.RestoreWallet(new Wallet(new Dictionary<DomainResourceId, long> { { GoldResource, 100 } }));
        heroes[Alice] = alice;

        var stalls = new ClaudeCodeGameStudios.Application.Stall.StallService(
            village, TestPrices.Book, id => heroes.TryGetValue(id, out var h) ? h : null);

        // ⚠️ 加成用**委托**注入 ⇒ 生产代码与守卫走**同一份**规则
        // （`GatheringPoints.BonusFor`）。
        var service = new GatherService(
            stalls, TestPrices.Book,
            () => ClaudeCodeGameStudios.Domain.Village.GatheringPoints.BonusFor(village));

        return (village, heroes, service);
    }

    /// <summary>① 没有采集点 ⇒ 产量不变（D-086 的 1 份/天）。</summary>
    [TestCase]
    public void WithoutWell_GatherAmountIsUnchanged()
    {
        var (_, heroes, service) = MakeScene();

        long got = service.Gather(heroes[Alice]);

        Assertions.AssertThat(got).IsEqual(GatherService.GatheredAmountPerDay);
        Assertions.AssertThat(heroes[Alice].Bag.Count(Stone)).IsEqual(1);
    }

    /// <summary>② 有采集点 ⇒ 每天多采 1 份。</summary>
    [TestCase]
    public void WithWell_GatherAmountIsOneMore()
    {
        var (_, heroes, service) = MakeScene(withWell: true);

        long got = service.Gather(heroes[Alice]);

        Assertions.AssertThat(got).IsEqual(GatherService.GatheredAmountPerDay + 1);
        Assertions.AssertThat(heroes[Alice].Bag.Count(Stone)).IsEqual(2);
    }

    /// <summary>
    /// ③ <b>施工中</b>的井<b>不算</b>采集点（还没打井呢）。
    /// </summary>
    /// <remarks>
    /// ⚠️ 这条守的是「<b>没建好就不生效</b>」——
    /// 否则玩家刚点下「挖井」，采集立刻变快（而井还在施工）。
    /// </remarks>
    [TestCase]
    public void UnderConstructionWell_DoesNotCount()
    {
        var (_, heroes, service) = MakeScene(withWell: true, wellBuilt: false);

        long got = service.Gather(heroes[Alice]);

        Assertions.AssertThat(got).IsEqual(GatherService.GatheredAmountPerDay);
    }

    /// <summary>
    /// ④ 背包只剩 1 格 ⇒ <b>只采 1 份</b>（不凭空多出材料）。
    /// </summary>
    /// <remarks>
    /// ⚠️ 这条守的是「<b>装不下的就是没采到</b>」——
    /// 不是「采到了然后弄丢」。两者对玩家是**不同的体验**。
    /// </remarks>
    [TestCase]
    public void WithWell_WhenBagIsNearlyFull_GathersOnlyWhatFits()
    {
        // 背包 5 份/种 ⇒ 先放 4 份 ⇒ 只剩 1 格。
        var (_, heroes, service) = MakeScene(withWell: true, bagStone: 4);

        long got = service.Gather(heroes[Alice]);

        Assertions.AssertThat(got).IsEqual(1L);
        Assertions.AssertThat(heroes[Alice].Bag.Count(Stone)).IsEqual(5);
    }

    /// <summary>
    /// ⑤ 升到第 2 级 ⇒ 井自动出现 ⇒ 加成<b>自动生效</b>（端到端接线）。
    /// </summary>
    /// <remarks>
    /// ⚠️ 这条把「升级 ⇒ 井出现」与「井出现 ⇒ 采集变快」<b>串起来</b>：
    /// 只验其中一半的话，另一半可以整块死掉而全部照样绿。
    /// </remarks>
    [TestCase]
    public void UpgradingToTier2_MakesGatheringFaster()
    {
        var (village, heroes, service) = MakeScene();

        long before = service.Gather(heroes[Alice]);
        Assertions.AssertThat(before).IsEqual(GatherService.GatheredAmountPerDay);

        // 升级到第 2 级（「有井的村子」）⇒ 井自动补齐。
        TierBuildingService.EnsureForTier(village, 2);

        long after = service.Gather(heroes[Alice]);
        Assertions.AssertThat(after).IsEqual(GatherService.GatheredAmountPerDay + 1);
    }

    /// <summary>
    /// ⑤' 升到第 4 级 ⇒ 井 + 铁匠铺<b>两个采集点叠加</b> ⇒ 每天多采 2 份（D-107）。
    /// </summary>
    /// <remarks>
    /// ⚠️ 这条守的是「<b>两个采集点要叠加，不是二选一</b>」：
    /// 开发者把两栋都定成了采集点（井 = 采集点、铁匠铺 = 采集 +1）
    /// ⇒ 第 4 级村庄的采集加成是 <b>+2</b>，不是 +1。
    /// </remarks>
    [TestCase]
    public void UpgradingToTier4_StacksTheWellAndTheBlacksmith()
    {
        var (village, heroes, service) = MakeScene();

        TierBuildingService.EnsureForTier(village, 4);

        long got = service.Gather(heroes[Alice]);

        Assertions.AssertThat(got).IsEqual(GatherService.GatheredAmountPerDay + 2);
    }

    /// <summary>
    /// ⑥ 判定依据是<b>效果</b>而不是名字（井改名也不该失效）。
    /// </summary>
    /// <remarks>
    /// ⚠️ 与 D-097（画法映射）/ D-098（范围类判定）同一原则：
    /// <b>名字是显示、会被改；效果是规则、不会变</b>。
    /// <br>⚠️ <b>这一条必须喂「名字与效果互相矛盾」的定义</b>：
    /// 否则守卫分不出「按名字判」与「按效果判」（查出来的永远是同一栋井
    /// ⇒ 两种实现都绿 ⇒ 守卫成了装饰品）。
    /// </remarks>
    [TestCase]
    public void WellIsRecognisedByEffect_NotByName()
    {
        Assertions.AssertThat(TierBuildings.Well.Effect).IsEqual(BuildingEffect.Gathering);
        Assertions.AssertThat(
            ClaudeCodeGameStudios.Domain.Village.GatheringPoints.IsGatheringPoint(TierBuildings.Well.Id))
            .IsTrue();

        // 名字改了、效果没变 ⇒ **仍然是采集点**（按效果判）。
        var renamed = TierBuildings.Well with { Name = "水井" };
        Assertions.AssertThat(
            ClaudeCodeGameStudios.Domain.Village.GatheringPoints.IsGatheringPoint(renamed))
            .IsTrue();

        // 名字还是「井」、效果没了 ⇒ **不是采集点**（按效果判）。
        var effectless = TierBuildings.Well with { Effect = BuildingEffect.None };
        Assertions.AssertThat(
            ClaudeCodeGameStudios.Domain.Village.GatheringPoints.IsGatheringPoint(effectless))
            .IsFalse();
    }

    /// <summary>
    /// ⑦ <b>不是采集点</b>的建筑<b>不许</b>加产量。
    /// </summary>
    /// <remarks>
    /// ⚠️ 这条与 D-098 的「查不到就算范围类」<b>正好相反</b>：
    /// 加产量这件事<b>宁可漏算</b>（少算 = 不给白送材料 ⇒ 经济不漂）。
    /// </remarks>
    [TestCase]
    public void OtherBuildings_DoNotAddGathering()
    {
        Assertions.AssertThat(
            ClaudeCodeGameStudios.Domain.Village.GatheringPoints.IsGatheringPoint(
                BuildableBuildings.Fence.Id)).IsFalse();
        Assertions.AssertThat(
            ClaudeCodeGameStudios.Domain.Village.GatheringPoints.IsGatheringPoint(
                BuildableBuildings.TrialTower.Id)).IsFalse();
        Assertions.AssertThat(
            ClaudeCodeGameStudios.Domain.Village.GatheringPoints.IsGatheringPoint(
                new ClaudeCodeGameStudios.Domain.Village.BuildingId("village_square"))).IsFalse();
    }
}
