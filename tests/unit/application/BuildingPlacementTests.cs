using System.Collections.Generic;
using System.Linq;
using GdUnit4;
using ClaudeCodeGameStudios.Application;
using ClaudeCodeGameStudios.Domain.Village;

namespace ClaudeCodeGameStudios.Tests.Application;

/// <summary>
/// <b>玩家自选落点</b>（D-093）：围栏与试炼塔的落点由玩家点地图决定。
/// </summary>
/// <remarks>
/// <para><b>它守的是三件事</b>：
/// <list type="number">
/// <item><b>落点合法</b>：不与已有建筑重叠，且落在村子的地盘（某个已建成建筑的影响区）里。</item>
/// <item><b>落点被记住</b>：盖出来的建筑就在玩家选的那一格（不是默认落点）。</item>
/// <item><b>非法落点不消耗任何东西</b>（钱、材料都还在）。</item>
/// </list>
///
/// <para>⚠️ <b>为什么要有这条</b>：围栏的<b>位置有意义</b> —— 它的影响区决定
/// 摊位能摆到哪儿 ⇒ 玩家当然该自己选。
/// 而「落在哪、不能跟什么重叠」这两条规则<b>本来就存在</b>（§13.7 的占地冲突 +
/// 摆摊用的影响区尺子）⇒ 这一版只是把它们<b>交给玩家</b>。</para>
/// </remarks>
[TestSuite]
public class BuildingPlacementTests
{
    private static readonly ResourceId Gold = new("金币");
    private static readonly ResourceId Material = new("材料");

    /// <summary>造一个场景：村库有钱有料，开局带一个「市集」（影响区 11×11，落在 (-5,-5)）。</summary>
    private static (BuildingService Service, Village Village) NewScene(
        long gold = 1000, long material = 1000)
    {
        var village = new Village("test");
        village.Add(Gold, gold);
        village.Add(Material, material);
        village.TryPlace(new ClaudeCodeGameStudios.Domain.Village.BuildingInstance(
            new ClaudeCodeGameStudios.Domain.Village.BuildingId("village_square"),
            gridX: -5, gridY: -5, footprintW: 1, footprintH: 1,
            constructionProgress: 1.0,
            affectsAreaW: 11, affectsAreaH: 11));
        return (new BuildingService(village, Gold, Material), village);
    }

    /// <summary>落在市集的影响区里、且没被占 ⇒ <b>合法</b>。</summary>
    [TestCase]
    public void CanPlaceAt_InsideTheVillageAndFree_SaysYes()
    {
        var (service, _) = NewScene();

        bool legal = service.CanPlaceAt(BuildableBuildings.Fence, -3, -3);

        Assertions.AssertThat(legal).IsTrue();
    }

    /// <summary>跟已有建筑<b>重叠</b> ⇒ <b>不合法</b>（§13.7 的占地冲突）。</summary>
    [TestCase]
    public void CanPlaceAt_OnAnExistingBuilding_SaysNo()
    {
        var (service, _) = NewScene();

        bool legal = service.CanPlaceAt(BuildableBuildings.Fence, -5, -5);   // 市集自己那格

        Assertions.AssertThat(legal).IsFalse();
    }

    /// <summary>落在<b>村子地盘之外</b> ⇒ <b>不合法</b>（与摆摊用同一把尺子）。</summary>
    [TestCase]
    public void CanPlaceAt_OutsideTheVillageGround_SaysNo()
    {
        var (service, _) = NewScene();

        bool legal = service.CanPlaceAt(BuildableBuildings.Fence, 40, 40);

        Assertions.AssertThat(legal).IsFalse();
    }

    /// <summary><b>落点被记住</b>：盖出来的建筑就在玩家选的那一格。</summary>
    [TestCase]
    public void TryStartAt_PlacesTheBuildingAtTheChosenCell()
    {
        var (service, village) = NewScene();
        var fence = BuildableBuildings.Fence;

        bool started = service.TryStartAt(fence, -2, -4, workers: 2);

        Assertions.AssertThat(started).IsTrue();
        var building = village.Buildings.First(b => b.Id.Value == fence.Id.Value);
        Assertions.AssertThat(building.GridX).IsEqual(-2);
        Assertions.AssertThat(building.GridY).IsEqual(-4);
        Assertions.AssertThat(building.Status).IsEqual(BuildingStatus.UnderConstruction);
    }

    /// <summary>非法落点 ⇒ <b>什么都不扣</b>（钱、材料都还在，也没有建筑）。</summary>
    [TestCase]
    public void TryStartAt_IllegalCell_DeductsNothing()
    {
        var (service, village) = NewScene(gold: 500, material: 400);
        var fence = BuildableBuildings.Fence;

        bool started = service.TryStartAt(fence, 40, 40, workers: 2);

        Assertions.AssertThat(started).IsFalse();
        Assertions.AssertThat(village.Get(Gold)).IsEqual(500);
        Assertions.AssertThat(village.Get(Material)).IsEqual(400);
        Assertions.AssertThat(village.Buildings.Count).IsEqual(1);   // 只有市集
    }

    /// <summary>旧的入口（<c>TryStart</c>）仍然落在<b>默认落点</b>上（既有行为不变）。</summary>
    [TestCase]
    public void TryStart_StillUsesTheDefaultCell()
    {
        var (service, village) = NewScene();
        var fence = BuildableBuildings.Fence;

        bool started = service.TryStart(fence, workers: 2);

        Assertions.AssertThat(started).IsTrue();
        var building = village.Buildings.First(b => b.Id.Value == fence.Id.Value);
        Assertions.AssertThat(building.GridX).IsEqual(fence.GridX);
        Assertions.AssertThat(building.GridY).IsEqual(fence.GridY);
    }
}
