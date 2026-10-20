using System.Collections.Generic;
using GdUnit4;
using ClaudeCodeGameStudios.Application.Stall;
using ClaudeCodeGameStudios.Domain.Heroes;
using ClaudeCodeGameStudios.Domain.Items;
using DomainStall = ClaudeCodeGameStudios.Domain.Stall.Stall;
using DomainStallStatus = ClaudeCodeGameStudios.Domain.Stall.StallStatus;
using DomainVillage = ClaudeCodeGameStudios.Domain.Village.Village;
using DomainBuilding = ClaudeCodeGameStudios.Domain.Village.BuildingInstance;
using DomainBuildingId = ClaudeCodeGameStudios.Domain.Village.BuildingId;
using DomainResourceId = ClaudeCodeGameStudios.Domain.Village.ResourceId;
using DomainBuildingStatus = ClaudeCodeGameStudios.Domain.Village.BuildingStatus;

namespace ClaudeCodeGameStudios.Tests.Stall;

/// <summary>
/// M5 / D-033：摆摊位置守门（占用 + 影响区）单元测试。
///
/// <para><b>本文档保护：</b></para>
/// <list type="bullet">
/// <item>摊位位置与建筑占用格重叠 → 拒。</item>
/// <item>摊位位置<b>不</b>在任一已建成建筑的影响区 → 拒（M5 规则）。</item>
/// <item>摊位位置在影响区 + 不重叠 → 通过，<c>Stall.GridX/Y</c> 记录。</item>
/// <item>未建成（<c>UnderConstruction</c>）的建筑影响区<b>不</b>算。</item>
/// <item><c>AffectsAreaW=0</c> 的"老建筑"兼容 —— 它影响区=0 永远不覆盖任何格子。</item>
/// </list>
/// </summary>
[TestSuite]
public class PlaceStallPositionTests
{
    private static readonly HeroId Alice = new("h-alice");
    private static readonly ItemId Stone = new("石料");
    private static readonly DomainResourceId Gold = new("金币");

    private static (Dictionary<HeroId, HeroAgent> heroes, DomainVillage village, StallService svc)
        MakeSceneWithMarket()
    {
        var village = new DomainVillage("v1");
        var heroes = new Dictionary<HeroId, HeroAgent>();

        // 一个市集：3×3 footprint 在 (5,5)，affects 5×5（覆盖 (3,3)-(9,9)）
        var market = new DomainBuilding(
            new DomainBuildingId("market"),
            gridX: 5, gridY: 5, footprintW: 3, footprintH: 3,
            constructionProgress: 1.0,
            affectsAreaW: 5, affectsAreaH: 5);
        village.TryPlace(market);

        var aliceBag = new Inventory().TryAdd(Stone, 10);
        var aliceWallet = new Wallet(new Dictionary<DomainResourceId, long> { { Gold, 100L } });
        var alice = new HeroAgent { Name = "Alice" };
        alice.AssignId(Alice);
        alice.RestoreBag(aliceBag!);
        alice.RestoreWallet(aliceWallet);
        heroes[Alice] = alice;

        var svc = new StallService(village, TestPrices.Book, id => heroes.TryGetValue(id, out var h) ? h : null);
        return (heroes, village, svc);
    }

    // ── 成功路径 ─────────────────────────────────────────────────────

    [TestCase]
    public void PlaceStall_InsideAffectsArea_RecordsGridXY()
    {
        var (heroes, _, svc) = MakeSceneWithMarket();
        // (7,8) 不在 footprint (5..7×5..7) 内，但在影响区 (1..11×1..11) 内
        var r = svc.PlaceStall(Alice, Stone, 1, 10.0, gridX: 7, gridY: 8);
        Assertions.AssertThat(r).IsNotNull();
        Assertions.AssertThat(r!.Stall.GridX).IsEqual(7);
        Assertions.AssertThat(r.Stall.GridY).IsEqual(8);
    }

    [TestCase]
    public void PlaceStall_OutsideFootprintButInsideAffects_Succeeds()
    {
        var (_, _, svc) = MakeSceneWithMarket();
        // (9,9) 在影响区 (1..11×1..11) 内，但 footprint 是 (5..7×5..7) → 不占
        var r = svc.PlaceStall(Alice, Stone, 1, 10.0, 9, 9);
        Assertions.AssertThat(r).IsNotNull();
    }

    // ── 占用检查（与建筑 footprint 重叠 → 拒） ──────────────────────

    [TestCase]
    public void PlaceStall_OnBuildingFootprintCell_Rejected()
    {
        // (6,7) 在市集 footprint (5..7 × 5..7) 内 → 占用
        var (heroes, _, svc) = MakeSceneWithMarket();
        var r = svc.PlaceStall(Alice, Stone, 1, 10.0, 6, 7);
        Assertions.AssertThat(r).IsNull();
        // 货不动
        Assertions.AssertThat(heroes[Alice].Bag.Count(Stone)).IsEqual(10L);
    }

    // ── 影响区检查（出影响区 → 拒） ────────────────────────────────

    [TestCase]
    public void PlaceStall_OutsideAffectsArea_Rejected()
    {
        // 市集影响区 (3,3)-(9,9)；(0,0) 远在影响区外
        var (heroes, _, svc) = MakeSceneWithMarket();
        var r = svc.PlaceStall(Alice, Stone, 1, 10.0, 0, 0);
        Assertions.AssertThat(r).IsNull();
        Assertions.AssertThat(heroes[Alice].Bag.Count(Stone)).IsEqual(10L);
    }

    // ── 未建成建筑的影响区不算 ────────────────────────────────────────

    [TestCase]
    public void PlaceStall_UnderConstructionBuilding_DoesNotCount()
    {
        var (heroes, _, svc) = MakeSceneWithMarket();
        // 把市集改成未建成
        var underConstruct = new DomainBuilding(
            new DomainBuildingId("market"),
            gridX: 5, gridY: 5, footprintW: 3, footprintH: 3,
            constructionProgress: 0.5,         // < 1 = 未建成
            affectsAreaW: 5, affectsAreaH: 5);
        // 重建 village
        var village2 = new DomainVillage("v1");
        village2.TryPlace(underConstruct);
        heroes[Alice] = new HeroAgent { Name = "Alice" };
        heroes[Alice].AssignId(Alice);
        heroes[Alice].RestoreBag(new Inventory().TryAdd(Stone, 10)!);
        heroes[Alice].RestoreWallet(new Wallet(new Dictionary<DomainResourceId, long> { { Gold, 100L } }));
        var svc2 = new StallService(village2, TestPrices.Book, id => heroes.TryGetValue(id, out var h) ? h : null);

        var r = svc2.PlaceStall(Alice, Stone, 1, 10.0, 6, 6);
        Assertions.AssertThat(r).IsNull();   // 未建成 → 影响区不算
    }

    // ── AffectsAreaW=0 老建筑兼容 ────────────────────────────────────

    [TestCase]
    public void PlaceStall_OldBuildingWithZeroAffects_StallFails()
    {
        // 老建筑：affects 0（默认）→ 影响区空 → 摊位无任何影响区可入
        var village = new DomainVillage("v1");
        var heroes = new Dictionary<HeroId, HeroAgent>();
        var alice = new HeroAgent { Name = "Alice" };
        alice.AssignId(Alice);
        alice.RestoreBag(new Inventory().TryAdd(Stone, 5)!);
        alice.RestoreWallet(new Wallet(new Dictionary<DomainResourceId, long> { { Gold, 100L } }));
        heroes[Alice] = alice;

        var oldBuilding = new DomainBuilding(
            new DomainBuildingId("house"), gridX: 3, gridY: 3, footprintW: 1, footprintH: 1);
        // 默认 affectsAreaW/H = 0
        village.TryPlace(oldBuilding);
        var svc = new StallService(village, TestPrices.Book, id => heroes.TryGetValue(id, out var h) ? h : null);

        // (3,3) 在 house footprint 内 → 占用 → 拒
        var r1 = svc.PlaceStall(Alice, Stone, 1, 10.0, 3, 3);
        Assertions.AssertThat(r1).IsNull();
        // (5,5) 远在影响区外（0）→ 拒
        var r2 = svc.PlaceStall(Alice, Stone, 1, 10.0, 5, 5);
        Assertions.AssertThat(r2).IsNull();
    }
}
