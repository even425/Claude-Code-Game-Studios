using System;
using System.Collections.Generic;
using GdUnit4;
using ClaudeCodeGameStudios.Application.Stall;
using ClaudeCodeGameStudios.Domain.Heroes;
using ClaudeCodeGameStudios.Domain.Items;
using DomainResourceId = ClaudeCodeGameStudios.Domain.Village.ResourceId;
using DomainVillage = ClaudeCodeGameStudios.Domain.Village.Village;

/// <summary>
/// 第四阶段 3B：每日自主购物的调度闸门。
/// </summary>
/// <remarks>
/// <para><b>守什么</b>：同一游戏日只跑一次（无论结果成功/跳过）；跨天会再次尝试；
/// 只有成交才写 <c>LastShoppingDay</c>；同日重复调用不产生新状态。</para>
/// <para><b>不守什么</b>：随机分布与性格权重（属 <see cref="HeroBehaviorServiceTests"/>）。</para>
/// </remarks>
[TestSuite]
public class DailyShoppingServiceTests
{
    private static readonly HeroId Alice = new("h-alice");
    private static readonly HeroId Bob = new("h-bob");
    private static readonly HeroId Carol = new("h-carol");
    private static readonly ItemId Stone = new("石料");
    private static readonly ItemId Wood = new("木料");
    private static readonly DomainResourceId Gold = new("金币");

    private sealed class Scene
    {
        public required Dictionary<HeroId, HeroAgent> Heroes;
        public required StallService Stall;
        public required DailyShoppingService Daily;
    }

    /// <summary>Alice 有 10 石料，Bob 有 100 金；市场在影响区。</summary>
    /// <remarks>
    /// <b>为什么还要一个 Carol（卖木料）</b>：D-053 之后自主购物「一次买尽可能多」，
    /// Bob 第 1 天会把石料<b>买满 5 份</b>（背包余量上限）→
    /// 第 2 天石料摊在<b>抽样前</b>被剔除。他还能不能买，取决于村里还有没有别的摊 ——
    /// 这正是「抽样前过滤」要守的行为，夹具必须具备这个条件才测得到。
    /// </remarks>
    private static Scene MakeScene(bool openStall = true)
    {
        var village = new DomainVillage("v1");
        var market = new ClaudeCodeGameStudios.Domain.Village.BuildingInstance(
            new ClaudeCodeGameStudios.Domain.Village.BuildingId("market"),
            gridX: -5, gridY: -5, footprintW: 1, footprintH: 1,
            constructionProgress: 1.0, affectsAreaW: 11, affectsAreaH: 11);
        village.TryPlace(market);

        var alice = new HeroAgent { Name = "Alice" };
        alice.RestoreBag(new Inventory().TryAdd(Stone, 10)!);
        alice.RestoreWallet(new Wallet(new Dictionary<DomainResourceId, long> { [Gold] = 100L }));

        var bob = new HeroAgent { Name = "Bob" };
        bob.RestoreBag(new Inventory());
        bob.RestoreWallet(new Wallet(new Dictionary<DomainResourceId, long> { [Gold] = 100L }));

        var carol = new HeroAgent { Name = "Carol" };
        carol.RestoreBag(new Inventory().TryAdd(Wood, 10)!);
        carol.RestoreWallet(new Wallet());

        var heroes = new Dictionary<HeroId, HeroAgent>
        {
            [Alice] = alice,
            [Bob] = bob,
            [Carol] = carol,
        };

        var stall = new StallService(village, PriceBook.Default,
            id => heroes.TryGetValue(id, out var h) ? h : null);
        if (openStall) stall.PlaceStall(Alice, Stone, 10, 10.0);
        if (openStall) stall.PlaceStall(Carol, Wood, 10, 10.0);   // 跟 Alice 同一开关

        var behavior = new HeroBehaviorService(
            stall,
            id => heroes.TryGetValue(id, out var h) ? h : null,
            () => 0.0);   // 永远通过 desire 检查，夹具只测调度

        return new Scene
        {
            Heroes = heroes,
            Stall = stall,
            Daily = new DailyShoppingService(behavior),
        };
    }

    [TestCase]
    public void RunDay_BuysOnceAndMarksLastShoppingDay()
    {
        var s = MakeScene();

        var results = s.Daily.RunDay(1, new[] { Bob }, 20.0);

        Assertions.AssertThat(results.Count).IsEqual(1);
        Assertions.AssertThat(results[0].Outcome)
            .IsEqual(HeroBehaviorService.ShoppingOutcome.Bought);
        Assertions.AssertThat(s.Heroes[Bob].LastShoppingDay).IsEqual(1);
        // D-053：一次买尽可能多 —— 背包余量 5 → 买 5 份
        Assertions.AssertThat(s.Heroes[Bob].Bag.Count(Stone)).IsEqual(5L);
        Assertions.AssertThat(s.Stall.GetStall(Alice)!.Stock).IsEqual(5L);
    }

    /// <summary>同一游戏日第二次调用：直接返回空，不再购物。</summary>
    [TestCase]
    public void RunDay_SameDayAgain_ReturnsEmptyAndDoesNotBuyAgain()
    {
        var s = MakeScene();

        s.Daily.RunDay(1, new[] { Bob }, 20.0);
        long goldAfterFirst = s.Heroes[Bob].Wallet.Amount(Gold);
        long stockAfterFirst = s.Stall.GetStall(Alice)!.Stock;

        var second = s.Daily.RunDay(1, new[] { Bob }, 30.0);

        Assertions.AssertThat(second.Count).IsEqual(0);
        Assertions.AssertThat(s.Heroes[Bob].Wallet.Amount(Gold)).IsEqual(goldAfterFirst);
        Assertions.AssertThat(s.Stall.GetStall(Alice)!.Stock).IsEqual(stockAfterFirst);
        Assertions.AssertThat(s.Heroes[Bob].Bag.Count(Stone)).IsEqual(5L);
    }

    /// <summary>
    /// 跨天后会再次尝试（不是“一天买过就永久不买”）。
    /// </summary>
    /// <remarks>
    /// <b>D-053 之后的细节</b>：第 1 天 Bob 把石料<b>买满 5 份</b> →
    /// 第 2 天石料摊在<b>抽样前</b>被剔除 → 他转去买 Carol 的木料。
    /// 这条用例现在**同时**守住了「跨天会再试」与「背包满会换材料」两件事。
    /// </remarks>
    [TestCase]
    public void RunDay_NextDay_BuysAgain()
    {
        var s = MakeScene();

        s.Daily.RunDay(1, new[] { Bob }, 20.0);
        var day2 = s.Daily.RunDay(2, new[] { Bob }, 30.0);

        Assertions.AssertThat(day2.Count).IsEqual(1);
        Assertions.AssertThat(day2[0].Outcome)
            .IsEqual(HeroBehaviorService.ShoppingOutcome.Bought);
        Assertions.AssertThat(s.Heroes[Bob].LastShoppingDay).IsEqual(2);
        // 第 1 天的 5 份石料还在（背满）；第 2 天买的是**木料**
        Assertions.AssertThat(s.Heroes[Bob].Bag.Count(Stone)).IsEqual(5L);
        Assertions.AssertThat(s.Heroes[Bob].Bag.Count(Wood)).IsEqual(5L);
    }

    /// <summary>没有开放摊位：结果为 Skipped_NoOpenStall；不写 LastShoppingDay。</summary>
    [TestCase]
    public void RunDay_NoOpenStall_SkippedWithoutConsumingDay()
    {
        var s = MakeScene(openStall: false);

        var results = s.Daily.RunDay(1, new[] { Bob }, 20.0);

        Assertions.AssertThat(results[0].Outcome)
            .IsEqual(HeroBehaviorService.ShoppingOutcome.Skipped_NoOpenStall);
        Assertions.AssertThat(s.Heroes[Bob].LastShoppingDay).IsEqual(0);
        Assertions.AssertThat(s.Heroes[Bob].Bag.Count(Stone)).IsEqual(0L);
    }

    /// <summary>英雄今天已经成交过：同日再次尝试（新服务实例）应返回已购跳过。</summary>
    [TestCase]
    public void RunDay_AlreadyShoppedToday_Skipped()
    {
        var s = MakeScene();
        s.Heroes[Bob].LastShoppingDay = 1;

        var results = s.Daily.RunDay(1, new[] { Bob }, 20.0);

        Assertions.AssertThat(results[0].Outcome)
            .IsEqual(HeroBehaviorService.ShoppingOutcome.Skipped_AlreadyShoppedToday);
        Assertions.AssertThat(s.Heroes[Bob].Bag.Count(Stone)).IsEqual(0L);
    }

    /// <summary>一次 RunDay 对名单里每个英雄各产出恰好一条结果。</summary>
    [TestCase]
    public void RunDay_ReturnsOneResultPerHero()
    {
        var s = MakeScene();

        var results = s.Daily.RunDay(1, new[] { Alice, Bob }, 20.0);

        Assertions.AssertThat(results.Count).IsEqual(2);
    }
}
