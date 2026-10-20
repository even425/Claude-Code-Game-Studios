using System;
using System.Collections.Generic;
using System.Linq;
using GdUnit4;
using ClaudeCodeGameStudios.Application.Stall;
using ClaudeCodeGameStudios.Domain.Heroes;
using ClaudeCodeGameStudios.Domain.Items;
using ClaudeCodeGameStudios.Domain.Village;
using DomainStall = ClaudeCodeGameStudios.Domain.Stall.Stall;
using DomainStallStatus = ClaudeCodeGameStudios.Domain.Stall.StallStatus;
using DomainVillage = ClaudeCodeGameStudios.Domain.Village.Village;
using DomainResourceId = ClaudeCodeGameStudios.Domain.Village.ResourceId;

namespace ClaudeCodeGameStudios.Tests.Stall;

/// <summary>
/// 第四阶段 M4（D-044）：摊位快照 / 读档恢复。
///
/// <para><b>本文档保护：</b></para>
/// <list type="bullet">
/// <item><see cref="StallService.Snapshot"/> 按摊主 Id 稳定排序，
/// 并与 <see cref="StallService.AppliedRequestIds"/> 一起构成完整读档输入。</item>
/// <item><see cref="StallService.Restore"/> 是<b>全有或全无</b>：
/// 任何一条非法输入都不留下半份摊位目录，旧幂等键也不会与新摊位错配。</item>
/// <item>恢复后同一请求键仍是重复请求；新键仍能正常成交。</item>
/// <item><see cref="DomainStall.Restore"/> 允许库存 0（售罄/已收摊），
/// 但拒绝负数 —— 公开构造只允许开新摊（库存 ≥ 1）。</item>
/// </list>
/// </summary>
[TestSuite]
public class StallSnapshotRestoreTests
{
    private static readonly HeroId Alice = new("h-alice");
    private static readonly HeroId Bob = new("h-bob");
    private static readonly ItemId Stone = new("石料");
    private static readonly ItemId Wood = new("木料");
    private static readonly DomainResourceId GoldResource = new("金币");

    /// <summary>建 Alice（100 金 + 10 石）+ Bob（100 金 + 0 石）+ 一个影响区覆盖 (0,0) 的市集。</summary>
    private static (Dictionary<HeroId, HeroAgent> heroes, DomainVillage village) MakeScene()
    {
        var village = new DomainVillage("v1");
        village.TryPlace(new BuildingInstance(
            new BuildingId("off_market"),
            gridX: -5, gridY: -5, footprintW: 1, footprintH: 1,
            constructionProgress: 1.0,
            affectsAreaW: 11, affectsAreaH: 11));

        var heroes = new Dictionary<HeroId, HeroAgent>();

        var alice = new HeroAgent { Name = "Alice" };
        alice.RestoreBag(new Inventory().TryAdd(Stone, 10)!);
        alice.RestoreWallet(new Wallet(new Dictionary<DomainResourceId, long>
        {
            { GoldResource, 100L },
        }));
        heroes[Alice] = alice;

        var bob = new HeroAgent { Name = "Bob" };
        bob.RestoreBag(new Inventory());
        bob.RestoreWallet(new Wallet(new Dictionary<DomainResourceId, long>
        {
            { GoldResource, 100L },
        }));
        heroes[Bob] = bob;

        return (heroes, village);
    }

    private static StallService MakeService(
        Dictionary<HeroId, HeroAgent> heroes, DomainVillage village)
        => new(village, TestPrices.Book, id => heroes.TryGetValue(id, out var h) ? h : null);

    // ── Snapshot ─────────────────────────────────────────────────────────

    [TestCase]
    public void Snapshot_ReturnsAllStallsSortedByOwner_AndKeysIntact()
    {
        var (heroes, village) = MakeScene();
        var svc = MakeService(heroes, village);

        // Bob 也持货，才能开第二个摊
        heroes[Bob].RestoreBag(new Inventory().TryAdd(Stone, 4)!);
        Assertions.AssertThat(svc.PlaceStall(Alice, Stone, 5, 10.0)).IsNotNull();
        Assertions.AssertThat(svc.PlaceStall(Bob, Stone, 4, 11.0)).IsNotNull();
        Assertions.AssertThat(svc.TryBuy(Alice, Bob, Stone, 2, 12.0, "k1").Bought).IsTrue();

        var snapshot = svc.Snapshot();

        Assertions.AssertThat(snapshot.Count).IsEqual(2);
        Assertions.AssertThat(snapshot[0].Owner).IsEqual(Alice);
        Assertions.AssertThat(snapshot[0].Stock).IsEqual(3L);
        Assertions.AssertThat(snapshot[1].Owner).IsEqual(Bob);
        Assertions.AssertThat(svc.AppliedRequestIds.Any(k => k == "k1")).IsTrue();
    }

    // ── Restore ──────────────────────────────────────────────────────────

    [TestCase]
    public void Restore_RoundTripsStallsAndIdempotencyKeys()
    {
        var (heroes, village) = MakeScene();
        var source = MakeService(heroes, village);
        Assertions.AssertThat(source.PlaceStall(Alice, Stone, 5, 10.0)).IsNotNull();
        Assertions.AssertThat(source.TryBuy(Alice, Bob, Stone, 2, 12.0, "k1").Bought).IsTrue();

        var target = MakeService(heroes, village);
        target.Restore(source.Snapshot(), source.AppliedRequestIds);

        var restored = target.GetStall(Alice);
        Assertions.AssertThat(restored).IsNotNull();
        Assertions.AssertThat(restored!.Stock).IsEqual(3L);
        Assertions.AssertThat(restored.Status).IsEqual(DomainStallStatus.Open);
        Assertions.AssertThat(target.AppliedRequestIds.Any(k => k == "k1")).IsTrue();

        // 读档后重放同一请求键：必须仍被判重，库存不变
        var replay = target.TryBuy(Alice, Bob, Stone, 2, 13.0, "k1");
        Assertions.AssertThat(replay.Outcome).IsEqual(StallService.BuyOutcome.DuplicateRequest);
        Assertions.AssertThat(target.GetStall(Alice)!.Stock).IsEqual(3L);

        // 新请求键仍能成交 —— 恢复不能把购买路径锁死
        var fresh = target.TryBuy(Alice, Bob, Stone, 1, 13.0, "k2");
        Assertions.AssertThat(fresh.Outcome).IsEqual(StallService.BuyOutcome.Bought);
        Assertions.AssertThat(target.GetStall(Alice)!.Stock).IsEqual(2L);
    }

    /// <summary>恢复期发现同一摊主两个摊位 → 抛异常，且旧目录一字不动。</summary>
    [TestCase]
    public void Restore_IsAllOrNothing_OnDuplicateOwner()
    {
        var (heroes, village) = MakeScene();
        var svc = MakeService(heroes, village);
        Assertions.AssertThat(svc.PlaceStall(Alice, Stone, 5, 10.0)).IsNotNull();

        var bad = new List<DomainStall>
        {
            new(Alice, Stone, 5, 5, DomainStallStatus.Open, 1.0),
            new(Alice, Wood, 2, 3, DomainStallStatus.Open, 2.0),
        };

        bool threw = false;
        try { svc.Restore(bad, Array.Empty<string>()); }
        catch (ArgumentException) { threw = true; }

        Assertions.AssertThat(threw).IsTrue();
        Assertions.AssertThat(svc.Stalls.Count).IsEqual(1);
        Assertions.AssertThat(svc.GetStall(Alice)!.Stock).IsEqual(5L);
    }

    /// <summary>恢复期出现空幂等键 → 抛异常，且旧目录/旧键一字不动。</summary>
    [TestCase]
    public void Restore_IsAllOrNothing_OnEmptyRequestKey()
    {
        var (heroes, village) = MakeScene();
        var svc = MakeService(heroes, village);
        Assertions.AssertThat(svc.PlaceStall(Alice, Stone, 5, 10.0)).IsNotNull();
        Assertions.AssertThat(svc.TryBuy(Alice, Bob, Stone, 1, 11.0, "keep").Bought).IsTrue();

        var validStall = new List<DomainStall>
        {
            new(Alice, Stone, 1, 5, DomainStallStatus.Open, 1.0),
        };

        bool threw = false;
        try { svc.Restore(validStall, new[] { "  " }); }
        catch (ArgumentException) { threw = true; }

        Assertions.AssertThat(threw).IsTrue();
        Assertions.AssertThat(svc.GetStall(Alice)!.Stock).IsEqual(4L);
        Assertions.AssertThat(svc.AppliedRequestIds.Any(k => k == "keep")).IsTrue();
    }

    [TestCase]
    public void Restore_WithEmptySnapshot_ClearsEverything()
    {
        var (heroes, village) = MakeScene();
        var svc = MakeService(heroes, village);
        Assertions.AssertThat(svc.PlaceStall(Alice, Stone, 5, 10.0)).IsNotNull();
        Assertions.AssertThat(svc.TryBuy(Alice, Bob, Stone, 1, 11.0, "k1").Bought).IsTrue();

        svc.Restore(Array.Empty<DomainStall>(), Array.Empty<string>());

        Assertions.AssertThat(svc.Stalls.Count).IsEqual(0);
        Assertions.AssertThat(svc.AppliedRequestIds.Count).IsEqual(0);
    }

    // ── Stall.Restore（读档入口）────────────────────────────────────────

    /// <summary>售罄摊位库存 0 必须能重建 —— 公开构造拒绝 0，读档不能跟着拒。</summary>
    [TestCase]
    public void StallRestore_AllowsZeroStock_SoldOut()
    {
        var stall = DomainStall.Restore(
            Alice, Stone, 0, 5, DomainStallStatus.SoldOut, 0.0);

        Assertions.AssertThat(stall.Owner).IsEqual(Alice);
        Assertions.AssertThat(stall.Stock).IsEqual(0L);
        Assertions.AssertThat(stall.Status).IsEqual(DomainStallStatus.SoldOut);
    }

    [TestCase]
    public void StallRestore_RejectsNegativeStock()
    {
        bool threw = false;
        try
        {
            DomainStall.Restore(Alice, Stone, -1, 5, DomainStallStatus.Closed, 0.0);
        }
        catch (ArgumentOutOfRangeException) { threw = true; }

        Assertions.AssertThat(threw).IsTrue();
    }

    [TestCase]
    public void StallRestore_RejectsEmptyOwnerOrItem()
    {
        bool ownerThrew = false;
        try
        {
            DomainStall.Restore(new HeroId(""), Stone, 1, 5, DomainStallStatus.Open, 0.0);
        }
        catch (ArgumentException) { ownerThrew = true; }

        bool itemThrew = false;
        try
        {
            DomainStall.Restore(Alice, new ItemId(""), 1, 5, DomainStallStatus.Open, 0.0);
        }
        catch (ArgumentException) { itemThrew = true; }

        Assertions.AssertThat(ownerThrew).IsTrue();
        Assertions.AssertThat(itemThrew).IsTrue();
    }
}
