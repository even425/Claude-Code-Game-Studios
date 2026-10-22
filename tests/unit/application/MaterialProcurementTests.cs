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
/// <b>村庄向村民采购建材</b>（D-091 拍板：「材料有用途 —— 村庄建造/修理优先消耗村民背包里的材料，
/// 付钱给他们」）。
/// </summary>
/// <remarks>
/// <para><b>它守的是四件事</b>：
/// <list type="number">
/// <item>采购时<b>村民拿到钱</b>、材料<b>离袋</b>、村庄<b>出账</b>（钱从村庄资产/现金到村民口袋）。</item>
/// <item><b>总量守恒</b>：钱没有凭空多出来（村庄少多少 = 村民多多少）。</item>
/// <item><b>预算受限</b>：村庄的钱不够 ⇒ 只买得起那么多（不超发）。</item>
/// <item><b>回滚</b>：建造最终没成立时，货还回去、钱收回来（不白拿人家的货）。</item>
/// </list>
///
/// <para>⚠️ <b>为什么这条重要</b>：实测（中位 5~6 金）证明<b>闲人的支出没有出口</b> ——
/// 他们在市场里买走材料，而材料对他们毫无用途 ⇒ 钱白流出去。
/// ⇒ 这条规则给材料一个出口：<b>村民买来的材料变成了村庄的建材</b>。</para>
/// </remarks>
[TestSuite]
public class MaterialProcurementTests
{
    private static readonly HeroId Alice = new("h-alice");
    private static readonly HeroId Bob = new("h-bob");
    private static readonly ItemId Stone = new("石料");
    private static readonly DomainResourceId GoldResource = new("金币");
    private static readonly DomainResourceId MaterialResource = new("材料");
    private static readonly DomainResourceId AssetResource = VillageUpgradeService.Asset;

    /// <summary>造一个场景：村庄 + 两个英雄 + 采购服务 + 升级服务。</summary>
    private static (DomainVillage Village,
                    Dictionary<HeroId, HeroAgent> Heroes,
                    MaterialProcurement Procurement,
                    VillageUpgradeService Upgrades)
        MakeScene(long villageGold, long villageMaterial, long villageAsset,
                  long reserve, params (HeroId Id, long StoneCount)[] heroes)
    {
        var village = new DomainVillage("v1");
        if (villageGold > 0) village.Add(GoldResource, villageGold);
        if (villageMaterial > 0) village.Add(MaterialResource, villageMaterial);
        if (villageAsset > 0) village.Add(AssetResource, villageAsset);

        var map = new Dictionary<HeroId, HeroAgent>();
        foreach (var (id, stones) in heroes)
        {
            var bag = new Inventory();
            if (stones > 0) bag = bag.TryAdd(Stone, stones)!;

            var hero = new HeroAgent { Name = id.Value };
            hero.AssignId(id);
            hero.RestoreBag(bag);
            hero.RestoreWallet(new Wallet(new Dictionary<DomainResourceId, long> { { GoldResource, 0 } }));
            map[id] = hero;
        }

        var procurement = new MaterialProcurement(
            village, TestPrices.Book, GoldResource, () => reserve);
        var upgrades = new VillageUpgradeService(
            village, GoldResource, MaterialResource, procurement, () => map.Values.ToList());

        return (village, map, procurement, upgrades);
    }

    /// <summary>D-091 ①：采购 ⇒ <b>村民拿到钱</b>、材料离袋、村庄出账。</summary>
    [TestCase]
    public void Procurement_PaysTheVillager_AndTakesTheGoods()
    {
        // 村庄资产 100；夹具价 石料 = 5。
        var (village, heroes, procurement, _) =
            MakeScene(villageGold: 0, villageMaterial: 0, villageAsset: 100, reserve: 0, (Alice, 4));

        var plan = procurement.Plan(neededQty: 3, heroes.Values);
        bool ok = procurement.Commit(plan);

        Assertions.AssertThat(ok).IsTrue();
        Assertions.AssertThat(plan.Qty).IsEqual(3);
        Assertions.AssertThat(plan.Gold).IsEqual(15);
        // 钱：村庄资产 −15，村民 +15
        Assertions.AssertThat(village.Get(AssetResource)).IsEqual(85);
        Assertions.AssertThat(heroes[Alice].Wallet.Amount(GoldResource)).IsEqual(15);
        // 货：离袋
        Assertions.AssertThat(heroes[Alice].Bag.Count(Stone)).IsEqual(1);
    }

    /// <summary>⚠️ <b>总量守恒</b>：村庄少的钱 = 村民多的钱。</summary>
    [TestCase]
    public void Procurement_KeepsTotalGoldUnchanged()
    {
        var (village, heroes, procurement, _) =
            MakeScene(villageGold: 0, villageMaterial: 0, villageAsset: 100, reserve: 0, (Alice, 4));

        long before = village.Get(AssetResource) + heroes.Values.Sum(h => h.Wallet.Amount(GoldResource));

        var plan = procurement.Plan(neededQty: 3, heroes.Values);
        procurement.Commit(plan);

        long after = village.Get(AssetResource) + heroes.Values.Sum(h => h.Wallet.Amount(GoldResource));
        Assertions.AssertThat(after).IsEqual(before);
    }

    /// <summary>D-091 ③：<b>预算受限</b> ⇒ 只买得起那么多（不超发）。</summary>
    [TestCase]
    public void Procurement_StaysWithinTheVillageBudget()
    {
        // 资产只有 12 ⇒ 夹具价 5 ⇒ 买得起 2 份（10 金）。
        var (village, heroes, procurement, _) =
            MakeScene(villageGold: 0, villageMaterial: 0, villageAsset: 12, reserve: 0, (Alice, 10));

        var plan = procurement.Plan(neededQty: 10, heroes.Values);
        procurement.Commit(plan);

        Assertions.AssertThat(plan.Qty).IsEqual(2);
        Assertions.AssertThat(heroes[Alice].Wallet.Amount(GoldResource)).IsEqual(10);
        Assertions.AssertThat(village.Get(AssetResource)).IsEqual(2);
    }

    /// <summary>⚠️ <b>升级的保留金不能动</b>（村庄发展不被抽空）。</summary>
    [TestCase]
    public void Procurement_NeverTouchesTheReservedCash()
    {
        // 村库现金 60、保留金 50 ⇒ 现金只能动用 10；资产 0。
        var (village, heroes, procurement, _) =
            MakeScene(villageGold: 60, villageMaterial: 0, villageAsset: 0, reserve: 50, (Alice, 10));

        var plan = procurement.Plan(neededQty: 10, heroes.Values);
        procurement.Commit(plan);

        Assertions.AssertThat(plan.Qty).IsEqual(2);            // 10 ÷ 5
        Assertions.AssertThat(village.Get(GoldResource)).IsEqual(50);
    }

    /// <summary>
    /// D-091 ④：<b>升级时优先用村民的材料</b> —— 村库材料不够，但村民有 ⇒ 仍然升得了，
    /// 而且<b>村民拿到了钱</b>。
    /// </summary>
    [TestCase]
    public void Upgrade_UsesVillagersMaterialsFirst_AndPaysThem()
    {
        // 升级第 0 级要 50 金 + 30 材料；村库只有 10 份材料，但 Alice 背包里有 30 份石料。
        var (village, heroes, _, upgrades) =
            MakeScene(villageGold: 200, villageMaterial: 10, villageAsset: 500, reserve: 0, (Alice, 30));

        bool ok = upgrades.TryUpgrade(0);

        Assertions.AssertThat(ok).IsTrue();
        // 村民的钱：30 份 × 夹具价 5 = 150 金
        Assertions.AssertThat(heroes[Alice].Wallet.Amount(GoldResource)).IsEqual(150);
        // 材料：Alice 的石料被拿走 30 份（材料变成了建材）
        Assertions.AssertThat(heroes[Alice].Bag.Count(Stone)).IsEqual(0);
        // 村库材料池只被扣了剩下的 0 份（全部由村民提供）
        Assertions.AssertThat(village.Get(MaterialResource)).IsEqual(10);
        // 金币：升级照付 50
        Assertions.AssertThat(village.Get(GoldResource)).IsEqual(150);
    }

    /// <summary>⚠️ <b>回滚</b>：货还回去、钱收回来（不白拿人家的货）。</summary>
    [TestCase]
    public void Procurement_RollbackReturnsGoodsAndTakesMoneyBack()
    {
        var (village, heroes, procurement, _) =
            MakeScene(villageGold: 0, villageMaterial: 0, villageAsset: 100, reserve: 0, (Alice, 4));

        var plan = procurement.Plan(neededQty: 3, heroes.Values);
        procurement.Commit(plan);
        procurement.Rollback(plan);

        Assertions.AssertThat(heroes[Alice].Bag.Count(Stone)).IsEqual(4);
        Assertions.AssertThat(heroes[Alice].Wallet.Amount(GoldResource)).IsEqual(0);
        Assertions.AssertThat(village.Get(AssetResource)).IsEqual(100);
    }

    /// <summary>
    /// 🔴 <b>同一个英雄出现在采购单里多次</b>（他背包里有好几种东西）⇒
    /// 第二笔必须<b>接着第一笔的新钱包</b>算 ⇒ 钱不能被覆盖丢失。
    /// </summary>
    /// <remarks>
    /// ⚠️ **这里曾经丢过钱**：早先的实现每一笔都拿「<b>原始钱包</b>」算
    /// ⇒ 第二笔的钱包把第一笔的钱<b>覆盖</b>掉了 ⇒ 钱凭空消失
    /// （实测：一次升级采购 145 金，英雄只到账 <b>44</b> 金 ⇒ 守恒律当天就破了）。
    /// ⇒ 这条守卫盯着「多笔 + 同一人」。
    /// </remarks>
    [TestCase]
    public void Procurement_PaysEverySinglePick_EvenForTheSameVillager()
    {
        var village = new DomainVillage("v1");
        village.Add(AssetResource, 500);

        var hero = new HeroAgent { Name = "Alice" };
        hero.AssignId(Alice);
        var bag = new Inventory();
        bag = bag.TryAdd(Stone, 3)!;      // 石料 = 5 金
        bag = bag.TryAdd(new ItemId("木料"), 2)!;   // 木料 = 3 金
        hero.RestoreBag(bag);
        hero.RestoreWallet(new Wallet(new Dictionary<DomainResourceId, long> { { GoldResource, 0 } }));

        var procurement = new MaterialProcurement(village, TestPrices.Book, GoldResource, () => 0L);

        var plan = procurement.Plan(neededQty: 5, new[] { hero });
        bool ok = procurement.Commit(plan);

        Assertions.AssertThat(ok).IsTrue();
        Assertions.AssertThat(plan.Picks.Count).IsEqual(2);        // 两种物品 ⇒ 两笔
        Assertions.AssertThat(plan.Qty).IsEqual(5);
        // 3 × 5 + 2 × 3 = 21 金 ⇒ **两笔都要到账**
        Assertions.AssertThat(hero.Wallet.Amount(GoldResource)).IsEqual(21);
        Assertions.AssertThat(village.Get(AssetResource)).IsEqual(500 - 21);
    }
}
