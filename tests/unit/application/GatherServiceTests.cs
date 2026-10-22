using System.Collections.Generic;
using System.Linq;
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
/// <b>采集与自动卖货</b>（D-086 拍板：「自主采集：每天 1 份材料」）。
/// </summary>
/// <remarks>
/// <para><b>它守的是四件事</b>：
/// <list type="number">
/// <item>采集的材料进<b>英雄自己的背包</b>（⇒ 能卖 ⇒ 才是个人收入），<b>不进</b>村庄材料池。</item>
/// <item><b>背包满了 ⇒ 白采</b>（采回来也放不下 ⇒ 状态不变）。</item>
/// <item>有货就<b>自动开摊</b>（按价格表定价 ⇒ 不新造经济）。</item>
/// <item><b>一人一摊</b>（D-023）：已有未收的摊就不再开。</item>
/// </list>
/// <para>⚠️ <b>为什么这条重要</b>：标定实测发现钱高度集中在 8 个人身上
/// （英雄金币中位只有 6 金、639 次自主购物失败全是「真没钱」）
/// ⇒ 根因是<b>收入只来自副本</b>，每场只有 2 人参与 ⇒ 35 个人永远没收入。</para>
/// </remarks>
[TestSuite]
public class GatherServiceTests
{
    private static readonly HeroId Alice = new("h-alice");
    private static readonly ItemId Stone = new("石料");
    private static readonly DomainResourceId GoldResource = new("金币");

    /// <summary>建一个场景：村庄（市集，影响区够大）+ Alice。</summary>
    private static (DomainVillage Village, Dictionary<HeroId, HeroAgent> Heroes, GatherService Service, ClaudeCodeGameStudios.Application.Stall.StallService Stalls) MakeScene(long bagStone = 0, long gold = 100)
    {
        var village = new DomainVillage("v1");
        village.TryPlace(new ClaudeCodeGameStudios.Domain.Village.BuildingInstance(
            new ClaudeCodeGameStudios.Domain.Village.BuildingId("village_square"),
            gridX: -5, gridY: -5, footprintW: 1, footprintH: 1,
            constructionProgress: 1.0,
            affectsAreaW: 11, affectsAreaH: 11));

        var heroes = new Dictionary<HeroId, HeroAgent>();
        var bag = new Inventory();
        if (bagStone > 0) bag = bag.TryAdd(Stone, bagStone)!;

        var alice = new HeroAgent { Name = "Alice" };
        alice.AssignId(Alice);
        alice.RestoreBag(bag);
        alice.RestoreWallet(new Wallet(new Dictionary<DomainResourceId, long> { { GoldResource, gold } }));
        heroes[Alice] = alice;

        var stalls = new ClaudeCodeGameStudios.Application.Stall.StallService(
            village, TestPrices.Book, id => heroes.TryGetValue(id, out var h) ? h : null);
        var service = new GatherService(stalls, TestPrices.Book);

        return (village, heroes, service, stalls);
    }

    /// <summary>D-086 ①：采集的材料进<b>英雄的背包</b>。</summary>
    [TestCase]
    public void Gather_PutsMaterialIntoTheGatherersBag()
    {
        var (_, heroes, service, _) = MakeScene();

        long got = service.Gather(heroes[Alice]);

        Assertions.AssertThat(got).IsEqual(GatherService.GatheredAmountPerDay);
        Assertions.AssertThat(heroes[Alice].Bag.Count(Stone)).IsEqual(1);
    }

    /// <summary>⚠️ <b>背包满了 ⇒ 白采</b>（状态不变）。</summary>
    [TestCase]
    public void Gather_WhenBagIsFull_ChangesNothing()
    {
        // 背包容量是 5 份/种 ⇒ 先塞满。
        var (_, heroes, service, _) = MakeScene(bagStone: 5);

        long got = service.Gather(heroes[Alice]);

        Assertions.AssertThat(got).IsEqual(0L);
        Assertions.AssertThat(heroes[Alice].Bag.Count(Stone)).IsEqual(5);
    }

    /// <summary>D-086 ③：背包里有可售物 ⇒ <b>自动开摊</b>。</summary>
    [TestCase]
    public void OpenStallFor_WithSellableGoods_OpensAStall()
    {
        var (_, heroes, service, _) = MakeScene(bagStone: 4);

        bool opened = service.OpenStallFor(heroes[Alice], day: 3, heroIndex: 0, simTime: 10.0);

        Assertions.AssertThat(opened).IsTrue();
    }

    /// <summary>没有可售物 ⇒ <b>不开摊</b>（不能白占一个位置）。</summary>
    [TestCase]
    public void OpenStallFor_WithoutSellableGoods_DoesNothing()
    {
        var (_, heroes, service, _) = MakeScene();

        bool opened = service.OpenStallFor(heroes[Alice], day: 3, heroIndex: 0, simTime: 10.0);

        Assertions.AssertThat(opened).IsFalse();
    }

    /// <summary>
    /// D-086 ④：<b>一人一摊</b>（D-023）—— 摊上**还有货**时不再开；
    /// 但<b>卖光了的要收掉重开</b>（否则市场直接停摆 —— 实测成交从 4867 笔掉到 37 笔）。
    /// </summary>
    [TestCase]
    public void OpenStallFor_ReopensAfterTheStallIsSoldOut()
    {
        var (village, heroes, service, stalls) = MakeScene(bagStone: 6);

        // 第一次开摊（摊上 3 份，袋里还剩 3 份）。
        Assertions.AssertThat(service.OpenStallFor(heroes[Alice], 3, 0, 10.0)).IsTrue();

        // 摊上还有货 ⇒ 不再开（一人一摊）。
        Assertions.AssertThat(service.OpenStallFor(heroes[Alice], 4, 0, 20.0)).IsFalse();

        // 买光 ⇒ 摊子变 SoldOut ⇒ 下一轮要能**收掉重开**
        //（否则市场饿死：实测成交从 4867 笔掉到 37 笔）。
        var bob = new HeroAgent { Name = "Bob" };
        var bobId = new HeroId("h-bob");
        bob.AssignId(bobId);
        bob.RestoreBag(new Inventory());
        bob.RestoreWallet(new Wallet(new Dictionary<DomainResourceId, long> { { GoldResource, 100L } }));
        heroes[bobId] = bob;

        Assertions.AssertThat(stalls.TryBuy(Alice, bobId, Stone, 3, 12.0).Outcome)
            .IsEqual(ClaudeCodeGameStudios.Application.Stall.StallService.BuyOutcome.Bought);

        Assertions.AssertThat(service.OpenStallFor(heroes[Alice], 5, 0, 30.0)).IsTrue();
    }

    /// <summary>
    /// ⚠️ <b>「今天卖哪一样」要轮换</b>：不然谁的背包里先有布匹谁就一直卖布匹
    /// ⇒ <b>整个市场只剩一种商品</b>（实测：成交额一度有 87–91% 集中在布匹）。
    /// </summary>
    [TestCase]
    public void OpenStallFor_RotatesWhichItemIsSoldByDay()
    {
        var (_, heroes, service, stalls) = MakeScene();

        // Alice 背包里两种可售物（**各 5 份** ⇒ 第一次开摊搬走 3 份后**两种都还在**
        // ⇒ 「今天卖哪一样」只由**日序**决定 ⇒ 才测得到轮换本身）。
        var alice = heroes[Alice];
        var bag = alice.Bag.TryAdd(Stone, 5)!;
        bag = bag.TryAdd(new ItemId("木料"), 5)!;
        alice.RestoreBag(bag);

        // 第一天开摊。
        Assertions.AssertThat(service.OpenStallFor(alice, day: 3, heroIndex: 0, simTime: 10.0)).IsTrue();
        var first = stalls.Stalls[Alice].ItemId;

        // 关摊 ⇒ 换一天 ⇒ 应当换一样卖。
        stalls.CloseStall(Alice);
        Assertions.AssertThat(service.OpenStallFor(alice, day: 4, heroIndex: 0, simTime: 20.0)).IsTrue();
        var second = stalls.Stalls[Alice].ItemId;

        Assertions.AssertThat(first.Value).IsNotEqual(second.Value);
    }

    /// <summary>
    /// ⚠️ <b>收入链条闭合</b>：采集 → 自动开摊 → 别的英雄买 ⇒ <b>卖方真的拿到钱</b>。
    /// </summary>
    /// <remarks>
    /// 这一条守的是「自主采集真的有收入」—— 只验「开摊了」不够，
    /// 还得验「有人买、卖方收到钱、村库抽成」。
    /// </remarks>
    [TestCase]
    public void GatheredGoods_ActuallyBecomeIncome()
    {
        var (village, heroes, service, stalls) = MakeScene();

        // ① 采集 ⇒ 材料进背包。
        service.Gather(heroes[Alice]);
        Assertions.AssertThat(heroes[Alice].Bag.Count(Stone)).IsEqual(1);

        // ② 自动开摊。
        Assertions.AssertThat(service.OpenStallFor(heroes[Alice], 3, 0, 10.0)).IsTrue();

        // ③ 另一个英雄来买。
        var bob = new HeroAgent { Name = "Bob" };
        var bobId = new HeroId("h-bob");
        bob.AssignId(bobId);
        bob.RestoreBag(new Inventory());
        bob.RestoreWallet(new Wallet(new Dictionary<DomainResourceId, long> { { GoldResource, 100L } }));
        heroes[bobId] = bob;

        long goldBefore = bob.Wallet.Amount(GoldResource);
        long villageGoldBefore = village.Get(ClaudeCodeGameStudios.Application.Stall.StallService.Gold);

        var attempt = stalls.TryBuy(Alice, bobId, Stone, 1, 12.0);

        Assertions.AssertThat(attempt.Outcome)
            .IsEqual(ClaudeCodeGameStudios.Application.Stall.StallService.BuyOutcome.Bought);
        // 卖方真的拿到钱（**夹具价**：TestPrices.Book 的石料 = 5 ⇒ 5% 取整为 0 ⇒ 卖方拿全额 5）。
        Assertions.AssertThat(heroes[Alice].Wallet.Amount(GoldResource)).IsEqual(105L);
        Assertions.AssertThat(bob.Wallet.Amount(GoldResource)).IsLess(goldBefore);
        // ⚠️ **摊位费是 5% 且取整**（D-052）：5 金的 5% = 0.25 ⇒ 这一笔村库拿 0，
        // 余下 0.25 **累计**在税额余数里 ⇒ 等攒够 1 金才入账。
        Assertions.AssertThat(village.Get(ClaudeCodeGameStudios.Application.Stall.StallService.Gold))
            .IsEqual(villageGoldBefore);
        Assertions.AssertThat(stalls.VillageCutCarry).IsEqual(25L);
    }
}
