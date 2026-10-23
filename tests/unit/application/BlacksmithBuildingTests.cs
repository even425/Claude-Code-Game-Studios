using System.Linq;
using GdUnit4;
using ClaudeCodeGameStudios.Application;
using ClaudeCodeGameStudios.Domain.Village;
using ClaudeCodeGameStudios.Presentation;

namespace ClaudeCodeGameStudios.Tests.Application;

/// <summary>
/// <b>铁匠铺</b>（D-107，开发者拍板「采集 +1（工具好，采得快）」）——
/// 层级第 4 级的<b>层级附带建筑</b>。
/// </summary>
/// <remarks>
/// <para><b>它守的是七件事</b>：
/// <list type="number">
/// <item>第 4 级 ⇒ 铁匠铺出现（层级阶梯里那一级就叫「铁匠铺」，升级名就叫「起铁匠铺」）。</item>
/// <item>第 3 级 ⇒ <b>没有</b>铁匠铺（不许提前出现）。</item>
/// <item>它<b>是建好的</b>（升级送的 ⇒ 一出现就生效）。</item>
/// <item>它<b>是采集点</b>（效果 = Gathering）⇒ 与井<b>叠加</b>（第 4 级 = +2 份/天）。</item>
/// <item>它<b>不是范围类</b>（D-098）⇒ 不放开摆摊范围。</item>
/// <item>它<b>不是可建建筑</b>（不能有两个来源）。</item>
/// <item>它<b>有自己的画法</b>（D-097：长得不一样才认得出）。</item>
/// </list></para>
/// <para>⚠️ <b>为什么这条重要</b>：村庄层级阶梯的第 4 级此前<b>只是个名字</b>
/// ⇒ 升上去除了「升级费」之外<b>什么都没变</b> ⇒ 而「铁匠铺 = 采集 +1」
/// 让那一级<b>第一次真的给了东西</b>。</para>
/// </remarks>
[TestSuite]
public class BlacksmithBuildingTests
{
    private static Village NewVillage() => new("v1");

    /// <summary>第 4 级 ⇒ 铁匠铺出现。</summary>
    [TestCase]
    public void Tier4_TheBlacksmithAppears()
    {
        var village = NewVillage();

        var placed = TierBuildingService.EnsureForTier(village, tier: 4);

        Assertions.AssertThat(placed.Any(b => b.Id.Value == "blacksmith")).IsTrue();
        Assertions.AssertThat(village.Buildings.Any(b => b.Id.Value == "blacksmith")).IsTrue();
    }

    /// <summary>第 3 级 ⇒ <b>没有</b>铁匠铺（不许提前出现）。</summary>
    [TestCase]
    public void Tier3_ThereIsNoBlacksmithYet()
    {
        var village = NewVillage();

        TierBuildingService.EnsureForTier(village, tier: 3);

        Assertions.AssertThat(village.Buildings.Any(b => b.Id.Value == "blacksmith")).IsFalse();
    }

    /// <summary>它<b>是建好的</b>（升级送的，不是施工的）⇒ 一出现就生效。</summary>
    [TestCase]
    public void TheBlacksmith_IsAlreadyBuilt()
    {
        var village = NewVillage();

        var placed = TierBuildingService.EnsureForTier(village, tier: 4);
        var blacksmith = placed.First(b => b.Id.Value == "blacksmith");

        Assertions.AssertThat(blacksmith.ConstructionProgress).IsEqual(1.0);
        Assertions.AssertThat(blacksmith.Status).IsEqual(BuildingStatus.Completed);
    }

    /// <summary>
    /// 它<b>是采集点</b> ⇒ 与井<b>叠加</b>（第 4 级村庄的采集加成 = +2）。
    /// </summary>
    /// <remarks>
    /// ⚠️ <b>叠加是开发者拍板的后果</b>：井 = 采集点（D-105）+ 铁匠铺 = 采集 +1（D-107）
    /// ⇒ 两栋都是采集点 ⇒ 第 4 级村庄<b>每天多采 2 份</b>。
    /// </remarks>
    [TestCase]
    public void TheBlacksmith_IsAGatheringPoint_AndStacksWithTheWell()
    {
        var village = NewVillage();

        TierBuildingService.EnsureForTier(village, tier: 2);
        Assertions.AssertThat(
            ClaudeCodeGameStudios.Domain.Village.GatheringPoints.BonusFor(village)).IsEqual(1L);

        TierBuildingService.EnsureForTier(village, tier: 4);
        Assertions.AssertThat(
            ClaudeCodeGameStudios.Domain.Village.GatheringPoints.BonusFor(village)).IsEqual(2L);
    }

    /// <summary>它<b>不是范围类</b>（D-098）⇒ 不放开摆摊范围。</summary>
    [TestCase]
    public void TheBlacksmith_IsNotARangeBuilding()
    {
        Assertions.AssertThat(RangeBuildings.IsRangeBuilding(new BuildingId("blacksmith"))).IsFalse();
    }

    /// <summary>它<b>不是可建建筑</b>（玩家不能花钱自己盖一间铁匠铺）。</summary>
    [TestCase]
    public void TheBlacksmith_IsNotPlayerBuildable()
    {
        Assertions.AssertThat(BuildableBuildings.Find(TierBuildings.Blacksmith.Id)).IsNull();
        Assertions.AssertThat(BuildableBuildings.All.Any(b => b.Id.Value == "blacksmith")).IsFalse();
    }

    /// <summary>
    /// 它<b>有自己的画法</b>（D-097）⇒ 地图上一眼认得出。
    /// </summary>
    /// <remarks>
    /// ⚠️ 判据是「<b>不是兜底</b>」（<c>IsKnown</c> 为 false）⇒
    /// 忘了给它画法就会红（而不是安静地画成一块石台）。
    /// </remarks>
    [TestCase]
    public void TheBlacksmith_HasItsOwnShape()
    {
        string shape = BuildingVisual.ShapeForId(TierBuildings.Blacksmith.Id);

        Assertions.AssertThat(shape).IsEqual(BuildingVisual.Blacksmith);
        Assertions.AssertThat(BuildingVisual.IsKnown(shape)).IsTrue();
    }

    /// <summary>
    /// 它的落点在<b>可见地图</b>上（D-093 的规矩），且<b>不与井重叠</b>。
    /// </summary>
    /// <remarks>
    /// ⚠️ 「不重叠」这条不能省：两栋都是层级附带建筑、落点都是写死的
    /// ⇒ 一旦写重了，<b>后放的那栋就放不下</b>（<c>TryPlace</c> 失败 ⇒ 静默少一栋）。
    /// </remarks>
    [TestCase]
    public void TheBlacksmith_IsOnTheVisibleMap_AndDoesNotOverlapTheWell()
    {
        var shop = TierBuildings.Blacksmith;
        var well = TierBuildings.Well;

        bool onMap = shop.GridX >= 0 && shop.GridX < 15
                  && shop.GridY >= 0 && shop.GridY < 10;
        bool overlaps = shop.GridX == well.GridX && shop.GridY == well.GridY;

        Assertions.AssertThat(onMap).IsTrue();
        Assertions.AssertThat(overlaps).IsFalse();

        // 真放一次：两栋都必须放得下（放不下 ⇒ 静默少一栋）。
        var village = NewVillage();
        TierBuildingService.EnsureForTier(village, tier: 4);

        Assertions.AssertThat(village.Buildings.Count(b => b.Id.Value == "blacksmith")).IsEqual(1);
        Assertions.AssertThat(village.Buildings.Count(b => b.Id.Value == "well")).IsEqual(1);
    }
}
