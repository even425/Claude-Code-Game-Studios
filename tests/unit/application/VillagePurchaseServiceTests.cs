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
/// <b>村庄收购站</b>（D-087 拍板：「村庄每天按市价收购在村英雄的采集品，钱回流给闲人」）。
/// </summary>
/// <remarks>
/// <para><b>它守的是五件事</b>：
/// <list type="number">
/// <item><b>钱从村库到英雄</b>：村库少多少，英雄就多多少 ⇒ <b>总量不变</b>（守恒律）。</item>
/// <item><b>预算 = 村库余额 − 下一级村庄升级所需金币</b> ⇒ 村庄发展不被抽空（D-087 拍板）。</item>
/// <item><b>只收「采集品」</b>：副本掉落的石料<b>不被收走</b>（否则摊位市场被挤掉、摊位税消失）。</item>
/// <item><b>不在村的英雄跳过</b>（副本能带回一堆东西，但那是掉落不是采集）。</item>
/// <item><b>一天只收一次</b>（同日第二轮不再花钱）。</item>
/// </list>
///
/// <para>⚠️ <b>为什么这些断言要写「事实」而不是「用常量算出的公式」</b>：
/// 「预算 = 余额 − 升级成本」这条规则如果只写成「断言预算等于某个常量算出来的数」，
/// 那它守的是<b>公式</b>不是<b>行为</b> —— 变异把扣款顺序改了，它照样绿。
/// ⇒ 这里断言的是<b>钱与货真的动了多少</b>（村库余额、英雄钱包、背包、材料池）。</para>
/// </remarks>
[TestSuite]
public class VillagePurchaseServiceTests
{
    private static readonly HeroId Alice = new("h-alice");
    private static readonly HeroId Bob = new("h-bob");
    private static readonly ItemId Stone = new("石料");
    private static readonly DomainResourceId GoldResource = new("金币");
    private static readonly DomainResourceId MaterialResource = new("材料");

    /// <summary>
    /// 造一个场景：村庄 + 若干英雄 + 收购站。
    /// </summary>
    /// <param name="villageGold">村库初始金币。</param>
    /// <param name="tier">村庄层级（决定「下一级升级要留多少」）。</param>
    /// <param name="heroes">英雄与（背包石料数、钱包金币）的对应。</param>
    private static (DomainVillage Village,
                    Dictionary<HeroId, HeroAgent> Heroes,
                    VillagePurchaseService Service,
                    VillageUpgradeService Upgrades)
        MakeScene(long villageGold, int tier,
                  params (HeroId Id, long BagStone, long Gold)[] heroes)
    {
        var village = new DomainVillage("v1");
        village.Add(GoldResource, villageGold);

        var map = new Dictionary<HeroId, HeroAgent>();
        foreach (var (id, bagStone, gold) in heroes)
        {
            var bag = new Inventory();
            if (bagStone > 0) bag = bag.TryAdd(Stone, bagStone)!;

            var hero = new HeroAgent { Name = id.Value };
            hero.AssignId(id);
            hero.RestoreBag(bag);
            hero.RestoreWallet(new Wallet(new Dictionary<DomainResourceId, long> { { GoldResource, gold } }));
            map[id] = hero;
        }

        var upgrades = new VillageUpgradeService(village, GoldResource, MaterialResource);
        var service = new VillagePurchaseService(
            village, TestPrices.Book, GoldResource, MaterialResource, upgrades, () => tier);

        return (village, map, service, upgrades);
    }

    /// <summary>D-087 ①：收购后<b>村库的钱进了英雄的钱包</b>，材料进村库材料池。</summary>
    [TestCase]
    public void Purchase_MovesMoneyFromVillageToHero_AndMaterialToVillagePool()
    {
        var (village, heroes, service, _) = MakeScene(villageGold: 500, tier: 0, (Alice, 3, 0));

        service.NoteGathered(Alice, 3);
        var records = service.RunRound(day: 5, heroes.Values);

        Assertions.AssertThat(records.Count).IsEqual(1);
        Assertions.AssertThat(records[0].Qty).IsEqual(3);
        // 收购价 = 市价（夹具 5）⇒ 3 份 = 15 金
        Assertions.AssertThat(records[0].Amount).IsEqual(3 * TestPrices.Stone);

        // 钱：村库 −15，英雄 +15
        Assertions.AssertThat(village.Get(GoldResource)).IsEqual(500 - 15);
        Assertions.AssertThat(heroes[Alice].Wallet.Amount(GoldResource)).IsEqual(15);
        // 货：背包里没了，村库材料池 +3
        Assertions.AssertThat(heroes[Alice].Bag.Count(Stone)).IsEqual(0);
        Assertions.AssertThat(village.Get(MaterialResource)).IsEqual(3);
    }

    /// <summary>⚠️ <b>钱不能凭空多出来</b>（守恒律）：村库少的 == 英雄多的。</summary>
    [TestCase]
    public void Purchase_KeepsTotalGoldUnchanged()
    {
        var (village, heroes, service, _) = MakeScene(villageGold: 500, tier: 0, (Alice, 2, 7));

        long before = village.Get(GoldResource) + heroes[Alice].Wallet.Amount(GoldResource);

        service.NoteGathered(Alice, 2);
        service.RunRound(day: 5, heroes.Values);

        long after = village.Get(GoldResource) + heroes[Alice].Wallet.Amount(GoldResource);
        Assertions.AssertThat(after).IsEqual(before);
    }

    /// <summary>
    /// D-087 ②：<b>预算 = 村库余额 − 下一级村庄升级所需金币</b> ⇒
    /// 收完之后村库**仍然攒得够升级**。
    /// </summary>
    [TestCase]
    public void Purchase_NeverSpendsTheMoneyReservedForTheNextUpgrade()
    {
        // tier 0 ⇒ 下一级要 50 金。村库只有 60 ⇒ 可收购的预算只有 10 ⇒ 2 份（夹具价 5）。
        var (village, heroes, service, upgrades) = MakeScene(villageGold: 60, tier: 0, (Alice, 5, 0));

        service.NoteGathered(Alice, 5);
        var records = service.RunRound(day: 5, heroes.Values);

        Assertions.AssertThat(records.Count).IsEqual(1);
        Assertions.AssertThat(records[0].Qty).IsEqual(2);
        Assertions.AssertThat(village.Get(GoldResource)).IsEqual(50);
        // 保留金没被动 ⇒ 村庄还升得起
        Assertions.AssertThat(village.Get(GoldResource) >= upgrades.CostFor(0).Gold).IsTrue();
    }

    /// <summary>村库<b>还没攒够升级的钱</b> ⇒ 预算 0 ⇒ 一份都不收（先攒钱，再收购）。</summary>
    [TestCase]
    public void Purchase_WithNothingLeftAfterReserve_BuysNothing()
    {
        var (village, heroes, service, _) = MakeScene(villageGold: 10, tier: 0, (Alice, 3, 0));

        service.NoteGathered(Alice, 3);
        var records = service.RunRound(day: 5, heroes.Values);

        Assertions.AssertThat(records.Count).IsEqual(0);
        Assertions.AssertThat(village.Get(GoldResource)).IsEqual(10);
        Assertions.AssertThat(heroes[Alice].Wallet.Amount(GoldResource)).IsEqual(0);
        Assertions.AssertThat(heroes[Alice].Bag.Count(Stone)).IsEqual(3);
    }

    /// <summary>已到最高级 ⇒ 没有「下一级」⇒ 保留金 0 ⇒ 村库的钱可以全用来收购。</summary>
    [TestCase]
    public void Purchase_AtMaxTier_HasNoReserve()
    {
        var (village, heroes, service, _) =
            MakeScene(villageGold: 20, tier: VillageUpgradeService.MaxTier, (Alice, 4, 0));

        service.NoteGathered(Alice, 4);
        var records = service.RunRound(day: 5, heroes.Values);

        Assertions.AssertThat(records.Count).IsEqual(1);
        Assertions.AssertThat(records[0].Qty).IsEqual(4);   // 20 金 ÷ 5 金 = 4 份
        Assertions.AssertThat(village.Get(GoldResource)).IsEqual(0);
    }

    /// <summary>
    /// D-087 ③：<b>只收「采集品」</b> —— 背包里的石料若<b>没有采集记录</b>（= 副本掉的），
    /// 村庄<b>不收</b>（否则摊位市场被挤掉 ⇒ 5% 摊位税消失 ⇒ 村库更没钱）。
    /// </summary>
    [TestCase]
    public void Purchase_IgnoresItemsThatWereNotGathered()
    {
        var (village, heroes, service, _) = MakeScene(villageGold: 500, tier: 0, (Alice, 3, 0));

        // ⚠️ **不**调 NoteGathered —— 这 3 份是「副本掉的」，不是采集品。
        var records = service.RunRound(day: 5, heroes.Values);

        Assertions.AssertThat(records.Count).IsEqual(0);
        Assertions.AssertThat(village.Get(GoldResource)).IsEqual(500);
        Assertions.AssertThat(heroes[Alice].Bag.Count(Stone)).IsEqual(3);
    }

    /// <summary>采集来的份数<b>不超过背包里实际有的</b>（背包是权威）。</summary>
    [TestCase]
    public void Purchase_NeverTakesMoreThanTheBagHolds()
    {
        var (village, heroes, service, _) = MakeScene(villageGold: 500, tier: 0, (Alice, 1, 0));

        service.NoteGathered(Alice, 3);      // 记了 3 份，但袋里只有 1 份
        var records = service.RunRound(day: 5, heroes.Values);

        Assertions.AssertThat(records.Count).IsEqual(1);
        Assertions.AssertThat(records[0].Qty).IsEqual(1);
        Assertions.AssertThat(heroes[Alice].Bag.Count(Stone)).IsEqual(0);
    }

    /// <summary>D-087 ④：<b>不在村的英雄跳过</b>（副本能带回东西，但那是掉落）。</summary>
    [TestCase]
    public void Purchase_SkipsHeroesWhoAreAway()
    {
        var (village, heroes, service, _) = MakeScene(villageGold: 500, tier: 0, (Alice, 3, 0));
        heroes[Alice].Away = true;

        service.NoteGathered(Alice, 3);
        var records = service.RunRound(day: 5, heroes.Values);

        Assertions.AssertThat(records.Count).IsEqual(0);
        Assertions.AssertThat(village.Get(GoldResource)).IsEqual(500);
        Assertions.AssertThat(heroes[Alice].Bag.Count(Stone)).IsEqual(3);
        // ⚠️ 不在村的**不清账** ⇒ 他回来那天还能被收（采集品不会凭空消失）
        Assertions.AssertThat(service.PendingFor(Alice)).IsEqual(3);
    }

    /// <summary>D-087 ⑤：<b>一天只收一次</b>（同一天第二轮不再花钱）。</summary>
    /// <remarks>
    /// ⚠️ **第二次调用前必须再记一笔「待售」**：否则第一轮已经把待售账清空了，
    /// 第二轮**本来就没东西可收** ⇒ 断言照样绿 ⇒ **这条守卫就成了装饰品**
    /// （变异「去掉日闸门」抓不到它 —— 实测就是这么发现的）。
    /// ⇒ 这里模拟「同一天又采到一份」（日闸门该挡住的就是这种重复）。
    /// </remarks>
    [TestCase]
    public void Purchase_RunsOnlyOncePerDay()
    {
        var (village, heroes, service, _) = MakeScene(villageGold: 500, tier: 0, (Alice, 3, 0));

        service.NoteGathered(Alice, 3);
        var first = service.RunRound(day: 5, heroes.Values);
        long goldAfterFirst = village.Get(GoldResource);

        // 同一天又采到一份（同一天第二轮不该再花钱）。
        // ⚠️ **要真的把货放进背包**：待售账只是「他采到了」的记录，
        // 背包才是货在的地方（收购以背包为准）。
        heroes[Alice].TryAddItem(Stone, 1);
        service.NoteGathered(Alice, 1);
        var second = service.RunRound(day: 5, heroes.Values);

        Assertions.AssertThat(first.Count).IsEqual(1);
        Assertions.AssertThat(second.Count).IsEqual(0);
        Assertions.AssertThat(village.Get(GoldResource)).IsEqual(goldAfterFirst);
        // 那一份还在他背包里（明天才会被收）。
        Assertions.AssertThat(heroes[Alice].Bag.Count(Stone)).IsEqual(1);
    }

    /// <summary>预算在<b>整轮内共享</b>：村库的钱是<b>一个池子</b>，不会超发。</summary>
    [TestCase]
    public void Purchase_SharesOneBudgetAcrossAllHeroes()
    {
        // tier 0 ⇒ 保留 50；村库 70 ⇒ 预算 20 ⇒ 4 份（夹具价 5）。
        var (village, heroes, service, _) =
            MakeScene(villageGold: 70, tier: 0, (Alice, 3, 0), (Bob, 3, 0));

        service.NoteGathered(Alice, 3);
        service.NoteGathered(Bob, 3);
        var records = service.RunRound(day: 5, heroes.Values);

        long bought = records.Sum(r => r.Qty);
        Assertions.AssertThat(bought).IsEqual(4);
        Assertions.AssertThat(village.Get(GoldResource)).IsEqual(50);
    }
}
