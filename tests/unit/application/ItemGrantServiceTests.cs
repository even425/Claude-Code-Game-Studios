using System;
using System.Collections.Generic;
using GdUnit4;
using ClaudeCodeGameStudios.Tests;
using ClaudeCodeGameStudios.Application;
using ClaudeCodeGameStudios.Domain.Heroes;
using ClaudeCodeGameStudios.Domain.Items;

/// <summary>
/// 第四阶段 M1：受控物品发放入口。
/// </summary>
/// <remarks>
/// <para><b>守什么</b>：合法发放进入背包；未知英雄/未知物品/非正数量/空标识一律拒绝，
/// 且**不改变背包**（全有或全无）；发放只影响目标英雄。</para>
/// <para><b>不守什么</b>：价格、掉落概率、奖励归属 —— 那些属于尚未确认的游戏规则。</para>
/// </remarks>
[TestSuite]
public class ItemGrantServiceTests
{
    private static readonly ItemId Stone = new("石料");
    private static readonly ItemId Wood = new("木料");

    private static HeroId H(string id) => new(id);

    private sealed class Scene
    {
        public required HeroAgent Alice;
        public required HeroAgent Bob;
        public required Dictionary<HeroId, HeroAgent> Heroes;
        public required ItemGrantService Service;
    }

    private static Scene MakeScene(PriceBook? catalog = null)
    {
        var alice = new HeroAgent { Name = "Alice" };
        var bob = new HeroAgent { Name = "Bob" };
        var heroes = new Dictionary<HeroId, HeroAgent>
        {
            [H("h-alice")] = alice,
            [H("h-bob")] = bob,
        };

        return new Scene
        {
            Alice = alice,
            Bob = bob,
            Heroes = heroes,
            Service = new ItemGrantService(
                id => heroes.TryGetValue(id, out var h) ? h : null,
                catalog ?? TestPrices.Book),
        };
    }

    // ── 成功发放 ──────────────────────────────────────────────────────────

    [TestCase]
    public void Grant_ValidItem_EntersBagAndReportsNewCount()
    {
        var s = MakeScene();

        var r = s.Service.Grant(H("h-alice"), Stone, 5);

        Assertions.AssertThat(r.Result).IsEqual(ItemGrantService.Outcome.Granted);
        Assertions.AssertThat(r.Granted).IsTrue();
        Assertions.AssertThat(r.NewCount).IsEqual(5L);
        Assertions.AssertThat(s.Alice.Bag.Count(Stone)).IsEqual(5L);
    }

    [TestCase]
    public void Grant_Twice_Accumulates()
    {
        var s = MakeScene();

        s.Service.Grant(H("h-alice"), Stone, 5);
        var r = s.Service.Grant(H("h-alice"), Stone, 3);

        Assertions.AssertThat(r.NewCount).IsEqual(8L);
        Assertions.AssertThat(s.Alice.Bag.Count(Stone)).IsEqual(8L);
    }

    [TestCase]
    public void Grant_DoesNotTouchOtherHeroes()
    {
        var s = MakeScene();

        s.Service.Grant(H("h-alice"), Stone, 5);

        Assertions.AssertThat(s.Alice.Bag.Count(Stone)).IsEqual(5L);
        Assertions.AssertThat(s.Bob.Bag.Count(Stone)).IsEqual(0L);
    }

    // ── 非法输入：拒绝且不改状态 ──────────────────────────────────────────

    [TestCase]
    public void Grant_UnknownHero_Rejected_NoChange()
    {
        var s = MakeScene();

        var r = s.Service.Grant(H("h-ghost"), Stone, 5);

        Assertions.AssertThat(r.Result).IsEqual(ItemGrantService.Outcome.UnknownHero);
        Assertions.AssertThat(s.Alice.Bag.Count(Stone)).IsEqual(0L);
        Assertions.AssertThat(s.Bob.Bag.Count(Stone)).IsEqual(0L);
    }

    [TestCase]
    public void Grant_UnknownItem_Rejected_NoChange()
    {
        var s = MakeScene();

        var r = s.Service.Grant(H("h-alice"), new ItemId("神秘物品"), 1);

        Assertions.AssertThat(r.Result).IsEqual(ItemGrantService.Outcome.UnknownItem);
        Assertions.AssertThat(s.Alice.Bag.Items.Count).IsEqual(0);
    }

    /// <summary>价格 0 = “不卖”，不属于可发放的合法物品。</summary>
    [TestCase]
    public void Grant_ZeroPriceItem_Rejected()
    {
        var catalog = new PriceBook(new[]
        {
            new KeyValuePair<ItemId, long>(Stone, 0L),
            new KeyValuePair<ItemId, long>(Wood, 3L),
        });
        var s = MakeScene(catalog);

        var zero = s.Service.Grant(H("h-alice"), Stone, 1);
        var normal = s.Service.Grant(H("h-alice"), Wood, 1);

        Assertions.AssertThat(zero.Result).IsEqual(ItemGrantService.Outcome.UnknownItem);
        Assertions.AssertThat(normal.Result).IsEqual(ItemGrantService.Outcome.Granted);
    }

    [TestCase]
    public void Grant_NonPositiveQuantity_Rejected_NoChange()
    {
        var s = MakeScene();

        var zero = s.Service.Grant(H("h-alice"), Stone, 0);
        var negative = s.Service.Grant(H("h-alice"), Stone, -3);

        Assertions.AssertThat(zero.Result).IsEqual(ItemGrantService.Outcome.InvalidQuantity);
        Assertions.AssertThat(negative.Result).IsEqual(ItemGrantService.Outcome.InvalidQuantity);
        Assertions.AssertThat(s.Alice.Bag.Count(Stone)).IsEqual(0L);
    }

    [TestCase]
    public void Grant_EmptyIds_Rejected()
    {
        var s = MakeScene();

        var emptyHero = s.Service.Grant(new HeroId(""), Stone, 1);
        var emptyItem = s.Service.Grant(H("h-alice"), new ItemId(""), 1);

        Assertions.AssertThat(emptyHero.Result).IsEqual(ItemGrantService.Outcome.UnknownHero);
        Assertions.AssertThat(emptyItem.Result).IsEqual(ItemGrantService.Outcome.UnknownItem);
        Assertions.AssertThat(s.Alice.Bag.Items.Count).IsEqual(0);
    }

    /// <summary>先成功发放、再混入非法调用：之前发出的物品与数量不能被破坏。</summary>
    [TestCase]
    public void InvalidCalls_DoNotCorruptExistingInventory()
    {
        var s = MakeScene();
        s.Service.Grant(H("h-alice"), Stone, 5);

        s.Service.Grant(H("h-alice"), new ItemId("神秘物品"), 1);
        s.Service.Grant(H("h-alice"), Stone, 0);
        s.Service.Grant(H("h-ghost"), Stone, 99);

        Assertions.AssertThat(s.Alice.Bag.Count(Stone)).IsEqual(5L);
        Assertions.AssertThat(s.Alice.Bag.Items.Count).IsEqual(1);
    }
}
