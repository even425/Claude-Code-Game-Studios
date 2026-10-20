using System;
using System.Collections.Generic;
using GdUnit4;
using ClaudeCodeGameStudios.Tests;
using ClaudeCodeGameStudios.Application;
using ClaudeCodeGameStudios.Domain.Heroes;
using ClaudeCodeGameStudios.Domain.Items;
using ClaudeCodeGameStudios.Domain.Settlement;

/// <summary>
/// R-01 M2：<b>批量物品发放</b>的单元测试。
///
/// <para><b>本文档保护的是「要么全发要么全不发」</b>：战利品要发给好几个人，
/// 其中任何一个人装不下，整笔就该不发 —— 绝不能出现「甲拿到了、乙没拿到」
/// 这种半笔账（I15）。</para>
///
/// <para><b>为什么走同一个受控入口</b>：单笔发放（<c>--grant=</c> 调试用）与
/// 批量发放（副本战利品）共用同一套合法性判定 ——
/// 物品必须在价格表登记为可售、英雄必须存在、数量必须为正。
/// 两套判定迟早会漂移，届时「测试配给能发、副本战利品发不出去」会极难查。</para>
///
/// <para><b>背包容量在这里是能测的</b>，因为测试程序集被显式授权访问
/// <c>internal</c> 成员（见主工程 <c>InternalsVisibleTo</c>），
/// 可以给英雄换一个有容量上限的背包。</para>
/// </summary>
[TestSuite]
public class ItemGrantBatchTests
{
    private static readonly ItemId Stone = new("石料");
    private static readonly ItemId Wood = new("木料");

    private static HeroId H(string id) => new(id);

    private sealed class Scene
    {
        public required HeroAgent Alice;
        public required HeroAgent Bob;
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
            Service = new ItemGrantService(
                id => heroes.TryGetValue(id, out var h) ? h : null,
                catalog ?? TestPrices.Book),
        };
    }

    // ── 预检 ───────────────────────────────────────────────────────────────

    /// <summary>都装得下 → 可行。</summary>
    [TestCase]
    public void CanGrant_AllRecipientsCanHold_ReturnsTrue()
    {
        var s = MakeScene();
        var grants = new[]
        {
            new ItemGrant(H("h-alice"), Stone, 5),
            new ItemGrant(H("h-bob"), Stone, 3),
        };

        Assertions.AssertThat(s.Service.CanGrant(grants)).IsTrue();
    }

    /// <summary>物品未登记为可售 → 不可行（与单笔发放同一套判定）。</summary>
    [TestCase]
    public void CanGrant_UnregisteredItem_ReturnsFalse()
    {
        var s = MakeScene();
        var grants = new[] { new ItemGrant(H("h-alice"), new ItemId("没登记的东西"), 1) };

        Assertions.AssertThat(s.Service.CanGrant(grants)).IsFalse();
    }

    /// <summary>英雄不存在 → 不可行（E04：不得静默吞掉奖励）。</summary>
    [TestCase]
    public void CanGrant_UnknownHero_ReturnsFalse()
    {
        var s = MakeScene();
        var grants = new[] { new ItemGrant(H("h-ghost"), Stone, 1) };

        Assertions.AssertThat(s.Service.CanGrant(grants)).IsFalse();
    }

    /// <summary>数量非正 → 不可行。</summary>
    [TestCase]
    public void CanGrant_NonPositiveQuantity_ReturnsFalse()
    {
        var s = MakeScene();
        var grants = new[] { new ItemGrant(H("h-alice"), Stone, 0) };

        Assertions.AssertThat(s.Service.CanGrant(grants)).IsFalse();
    }

    /// <summary>
    /// <b>背包装不下 → 不可行</b>（E09）。这条今天在正式游戏里不会触发
    /// （正式背包容量是「无限」），但规则必须钉死，
    /// 否则将来给背包加上限时会发现没人处理过这条路径。
    /// </summary>
    [TestCase]
    public void CanGrant_BagCannotHold_ReturnsFalse()
    {
        var s = MakeScene();
        s.Alice.RestoreBag(new Inventory(capacity: 3));

        var grants = new[] { new ItemGrant(H("h-alice"), Stone, 5) };

        Assertions.AssertThat(s.Service.CanGrant(grants)).IsFalse();
    }

    // ── 提交 ───────────────────────────────────────────────────────────────

    /// <summary>提交后：每位收件人的背包都真的多了东西。</summary>
    [TestCase]
    public void Grant_ValidBatch_FillsEveryRecipientBag()
    {
        var s = MakeScene();
        var grants = new[]
        {
            new ItemGrant(H("h-alice"), Stone, 5),
            new ItemGrant(H("h-bob"), Wood, 2),
        };

        s.Service.Grant(grants);

        Assertions.AssertThat(s.Alice.Bag.Count(Stone)).IsEqual(5L);
        Assertions.AssertThat(s.Bob.Bag.Count(Wood)).IsEqual(2L);
        Assertions.AssertThat(s.Bob.Bag.Count(Stone)).IsEqual(0L);
    }

    /// <summary>
    /// <b>一人装不下 → 谁都拿不到</b>（E09「整次回滚」）。
    /// 这是本任务最贵的一条：半发出去比不发更难查 ——
    /// 玩家只会发现「我明明参战了，怎么没有」。
    /// </summary>
    [TestCase]
    public void Grant_OneRecipientCannotHold_ChangesNobodyBag()
    {
        var s = MakeScene();
        s.Bob.RestoreBag(new Inventory(capacity: 3));

        var grants = new[]
        {
            new ItemGrant(H("h-alice"), Stone, 5),
            new ItemGrant(H("h-bob"), Stone, 5),
        };

        bool threw = false;
        try { s.Service.Grant(grants); }
        catch (InvalidOperationException) { threw = true; }

        Assertions.AssertThat(threw).IsTrue();
        Assertions.AssertThat(s.Alice.Bag.Count(Stone)).IsEqual(0L);
        Assertions.AssertThat(s.Bob.Bag.Count(Stone)).IsEqual(0L);
    }

    /// <summary>
    /// 预检通过后提交<b>不会失败</b>：背包不可变，
    /// 「先算出新背包再一次性换上」是原子的。
    /// </summary>
    [TestCase]
    public void Grant_AfterSuccessfulPrecheck_NeverThrows()
    {
        var s = MakeScene();
        var grants = new[]
        {
            new ItemGrant(H("h-alice"), Stone, 5),
            new ItemGrant(H("h-bob"), Stone, 5),
        };

        bool can = s.Service.CanGrant(grants);
        bool threw = false;
        try { s.Service.Grant(grants); }
        catch (Exception) { threw = true; }

        Assertions.AssertThat(can).IsTrue();
        Assertions.AssertThat(threw).IsFalse();
        Assertions.AssertThat(s.Alice.Bag.Count(Stone)).IsEqual(5L);
        Assertions.AssertThat(s.Bob.Bag.Count(Stone)).IsEqual(5L);
    }

    /// <summary>空批次：什么都不做，也不该抛。</summary>
    [TestCase]
    public void Grant_EmptyBatch_DoesNothing()
    {
        var s = MakeScene();

        bool threw = false;
        try { s.Service.Grant(Array.Empty<ItemGrant>()); }
        catch (Exception) { threw = true; }

        Assertions.AssertThat(threw).IsFalse();
        Assertions.AssertThat(s.Alice.Bag.Count(Stone)).IsEqual(0L);
    }

    /// <summary>
    /// 同一个英雄拿两件不同的物品：两件都要在（批量不是「只算第一件」）。
    /// </summary>
    [TestCase]
    public void Grant_SameRecipientTwoItems_BothLandInBag()
    {
        var s = MakeScene();
        var grants = new[]
        {
            new ItemGrant(H("h-alice"), Stone, 5),
            new ItemGrant(H("h-alice"), Wood, 2),
        };

        s.Service.Grant(grants);

        Assertions.AssertThat(s.Alice.Bag.Count(Stone)).IsEqual(5L);
        Assertions.AssertThat(s.Alice.Bag.Count(Wood)).IsEqual(2L);
    }
}
