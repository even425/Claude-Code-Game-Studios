using System.Linq;
using GdUnit4;
using ClaudeCodeGameStudios.Application;
using ClaudeCodeGameStudios.Domain.Village;

namespace ClaudeCodeGameStudios.Tests.Application;

/// <summary>
/// <b>层级附带建筑</b>（D-099）—— 村庄升级时，那一级该有的建筑<b>自动出现</b>。
/// </summary>
/// <remarks>
/// <para><b>它守的是六件事</b>：
/// <list type="number">
/// <item><b>井在第 2 级出现</b>（层级阶梯里那一级就叫「有井的村子」，升级名就叫「挖井」）。</item>
/// <item><b>第 1 级没有井</b>（不许提前出现）。</item>
/// <item><b>升级后它真的在村里</b>（不是地图上的装饰）。</item>
/// <item><b>幂等</b>：升级可能重入、读档后也会再调一次 ⇒ <b>不能出现两口井</b>。</item>
/// <item><b>它不放开摆摊范围</b>（D-098：只有「范围类」建筑才放开）。</item>
/// <item><b>它不是「可建建筑」</b>（玩家不能花钱自己盖一口井 ⇒ 不能有两个来源）。</item>
/// </list></para>
///
/// <para>⚠️ <b>为什么这条重要</b>：之前那口井**只是地图上的手绘装饰**
/// ⇒ 村庄里其实**没有**这栋建筑 ⇒ 「有井的村子」这句话<b>当时是假的</b>
/// （与 D-097 修的是同一族问题）。</para>
/// </remarks>
[TestSuite]
public class TierBuildingTests
{
    private static readonly ResourceId GoldResource = new("金币");
    private static readonly ResourceId MaterialResource = new("材料");

    /// <summary>造一个空村庄（没有市集 —— 这里只验层级附带建筑本身）。</summary>
    private static Village NewVillage() => new("v1");

    /// <summary>第 2 级 ⇒ 井出现。</summary>
    [TestCase]
    public void Tier2_TheWellAppears()
    {
        var village = NewVillage();

        var placed = TierBuildingService.EnsureForTier(village, tier: 2);

        Assertions.AssertThat(placed.Count).IsEqual(1);
        Assertions.AssertThat(placed[0].Id.Value).IsEqual(TierBuildings.Well.Id.Value);
        Assertions.AssertThat(village.Buildings.Any(b => b.Id.Value == "well")).IsTrue();
    }

    /// <summary>第 1 级 ⇒ <b>没有</b>井（不许提前出现）。</summary>
    [TestCase]
    public void Tier1_ThereIsNoWellYet()
    {
        var village = NewVillage();

        TierBuildingService.EnsureForTier(village, tier: 1);

        Assertions.AssertThat(village.Buildings.Any(b => b.Id.Value == "well")).IsFalse();
        Assertions.AssertThat(TierBuildingService.MissingForTier(village, tier: 1).Count).IsEqual(0);
    }

    /// <summary>第 3 级 ⇒ 井<b>还在</b>（升级不会让它消失），且不会多出第二口。</summary>
    [TestCase]
    public void Tier3_TheWellIsStillThere_AndThereIsOnlyOne()
    {
        var village = NewVillage();
        TierBuildingService.EnsureForTier(village, tier: 2);

        TierBuildingService.EnsureForTier(village, tier: 3);

        int wells = village.Buildings.Count(b => b.Id.Value == "well");
        Assertions.AssertThat(wells).IsEqual(1);
    }

    /// <summary>
    /// <b>幂等</b>：连续调两次 ⇒ 仍然只有一口井。
    /// </summary>
    /// <remarks>
    /// ⚠️ 这是**真的会发生**的：升级可能重入、读档后也会再调一次
    /// ⇒ 不幂等就会**两口井**（玩家会以为出了 bug）。
    /// </remarks>
    [TestCase]
    public void EnsuringTwice_DoesNotDuplicateTheWell()
    {
        var village = NewVillage();

        TierBuildingService.EnsureForTier(village, tier: 2);
        var second = TierBuildingService.EnsureForTier(village, tier: 2);

        Assertions.AssertThat(second.Count).IsEqual(0);
        Assertions.AssertThat(village.Buildings.Count(b => b.Id.Value == "well")).IsEqual(1);
    }

    /// <summary>
    /// D-098 配套：井<b>不是范围类</b> ⇒ 它<b>不</b>放开摆摊范围。
    /// </summary>
    [TestCase]
    public void TheWell_IsNotARangeBuilding()
    {
        Assertions.AssertThat(RangeBuildings.IsRangeBuilding(new BuildingId("well"))).IsFalse();
    }

    /// <summary>
    /// <b>幂等的真话</b>：村里<b>已经有同标识的井</b>（但位置不同）
    /// ⇒ 不得<b>再放一口</b>。
    /// </summary>
    /// <remarks>
    /// ⚠️ <b>为什么这条不能省</b>：上一条（连续调两次）其实<b>有两层检查</b>在挡
    /// —— ①「已有同标识就跳过」；②「落点被占 ⇒ 放不下」。
    /// ⇒ 只去掉①，②仍然挡着 ⇒ <b>那条守卫是装饰品</b>（改坏①它照样绿）。
    /// <para>⚠️ 而②是<b>巧合</b>：两处检查靠「同一格」才重合。
    /// 若井的位置变了（老档、以后调整落点）⇒ ②不再挡 ⇒ 就会真的出现两口井。
    /// ⇒ 这条把位置<b>故意错开</b>，让②挡不住 ⇒ 只剩①能抓。</para>
    /// </remarks>
    [TestCase]
    public void EnsuringWithTheSameIdAtAnotherSpot_DoesNotAddASecondWell()
    {
        var village = NewVillage();
        // ⚠️ **故意放在别的位置** ⇒ 落点不冲突 ⇒ 「放不下」那条检查挡不住。
        village.TryPlace(new BuildingInstance(
            new BuildingId("well"), gridX: 1, gridY: 1,
            footprintW: 1, footprintH: 1,
            constructionProgress: 1.0, affectsAreaW: 0, affectsAreaH: 0));

        var placed = TierBuildingService.EnsureForTier(village, tier: 2);

        Assertions.AssertThat(placed.Count).IsEqual(0);
        Assertions.AssertThat(village.Buildings.Count(b => b.Id.Value == "well")).IsEqual(1);
    }

    /// <summary>
    /// 井<b>不是可建建筑</b>（玩家不能花钱自己盖一口井）。
    /// </summary>
    /// <remarks>
    /// ⚠️ 这一条守的是「<b>不能有两个来源</b>」：若它既在可建目录里、
    /// 又是升级送的 ⇒ 玩家花 150 金盖一口井、而升到第 2 级又白送一口 ⇒ 自相矛盾。
    /// </remarks>
    [TestCase]
    public void TheWell_IsNotPlayerBuildable()
    {
        Assertions.AssertThat(BuildableBuildings.Find(TierBuildings.Well.Id)).IsNull();
        Assertions.AssertThat(BuildableBuildings.All.Any(b => b.Id.Value == "well")).IsFalse();
    }

    /// <summary>
    /// 井<b>是建好的</b>（升级送的，不是施工的）⇒ 它一出现就生效。
    /// </summary>
    [TestCase]
    public void TheWell_IsAlreadyBuilt()
    {
        var village = NewVillage();

        var placed = TierBuildingService.EnsureForTier(village, tier: 2);

        Assertions.AssertThat(placed[0].ConstructionProgress).IsEqual(1.0);
        Assertions.AssertThat(placed[0].Status).IsEqual(BuildingStatus.Completed);
    }

    /// <summary>
    /// 井的<b>落点在可见地图上</b>（D-093 的规矩：玩家看得见才算数）。
    /// </summary>
    [TestCase]
    public void TheWell_IsOnTheVisibleMap()
    {
        var well = TierBuildings.Well;

        bool onMap = well.GridX >= 0 && well.GridX < 15
                  && well.GridY >= 0 && well.GridY < 10;

        Assertions.AssertThat(onMap).IsTrue();
    }

    /// <summary>
    /// <b>升级完成那个入口</b>必须真的补齐（D-099）。
    /// </summary>
    /// <remarks>
    /// ⚠️ <b>为什么要有这条</b>：规则搬到了
    /// <c>VillageUpgradeService.CompleteUpgrade</c>（可离线单测），
    /// <c>Main</c> 只负责<b>调用</b>它 ⇒ 若这个入口本身不补齐，
    /// 「升级 ⇒ 井出现」就<b>不成立</b>，而单测若只测
    /// <c>TierBuildingService</c> ⇒ **抓不到**（实测：那两档变异当时是绿的）。
    /// </remarks>
    [TestCase]
    public void CompletingTheUpgrade_AddsTheTierBuildings()
    {
        var village = NewVillage();
        var service = new ClaudeCodeGameStudios.Application.VillageUpgradeService(
            village, GoldResource, MaterialResource);

        var placed = service.CompleteUpgrade(tier: 2);

        Assertions.AssertThat(placed.Count).IsEqual(1);
        Assertions.AssertThat(placed[0].Id.Value).IsEqual(TierBuildings.Well.Id.Value);
        Assertions.AssertThat(village.Buildings.Any(b => b.Id.Value == "well")).IsTrue();
    }

    /// <summary>
    /// <b>升级完成的入口用错层级</b> ⇒ 该出现的建筑没出现 ⇒ 必须红。
    /// </summary>
    /// <remarks>
    /// ⚠️ 这一条与「入口不补齐」那一条**不是同一件事**：
    /// 前者验「入口什么都没做」，后者验「入口做了但差一级」
    /// ⇒ 两种坏法都得被抓到。
    /// </remarks>
    [TestCase]
    public void CompletingTheUpgrade_UsesTheTierItWasGiven()
    {
        var village = NewVillage();
        var service = new ClaudeCodeGameStudios.Application.VillageUpgradeService(
            village, GoldResource, MaterialResource);

        // 升到第 1 级 ⇒ 不该有井。
        service.CompleteUpgrade(tier: 1);
        Assertions.AssertThat(village.Buildings.Any(b => b.Id.Value == "well")).IsFalse();

        // 升到第 2 级 ⇒ 该有井。
        service.CompleteUpgrade(tier: 2);
        Assertions.AssertThat(village.Buildings.Any(b => b.Id.Value == "well")).IsTrue();
    }

}
