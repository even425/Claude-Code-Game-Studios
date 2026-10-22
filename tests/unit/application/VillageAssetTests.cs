using System.Collections.Generic;
using System.Linq;
using GdUnit4;
using ClaudeCodeGameStudios.Application;
using ClaudeCodeGameStudios.Data;
using ClaudeCodeGameStudios.Domain.Heroes;
using ClaudeCodeGameStudios.Domain.Items;
using ClaudeCodeGameStudios.Domain.Village;
using ClaudeCodeGameStudios.Tests.Items;
using DomainVillage = ClaudeCodeGameStudios.Domain.Village.Village;
using DomainResourceId = ClaudeCodeGameStudios.Domain.Village.ResourceId;

namespace ClaudeCodeGameStudios.Tests.Application;

/// <summary>
/// <b>村庄资产</b>（D-090 拍板：「升级村庄花掉的金币不销毁，改记『村庄资产累计』」）。
/// </summary>
/// <remarks>
/// <para><b>它守的是四件事</b>：
/// <list type="number">
/// <item>升级时<b>钱没消失</b>：村库金币 −成本，村庄资产 +成本（钱变成了建筑）。</item>
/// <item>收购时<b>资产变少、英雄的钱变多</b>（资产是收购站的燃料）。</item>
/// <item><b>守恒律</b>：Σ英雄钱包 + 村库金币 + 村庄资产 + 累计销毁 = 初始 + 累计发放。</item>
/// <item><b>老档兼容</b>：没有「村庄资产」这一项的旧档，读档时补记（否则钱凭空消失）。</item>
/// </list>
///
/// <para>⚠️ <b>为什么这条重要</b>：D-087 的收购站实测<b>几乎没开张</b>
/// （200 天 2~5 笔）—— 根因就是「升级与收购抢同一笔钱」：
/// 村库的钱恰好在攒下一级升级 ⇒ 预算恒为 0。
/// ⇒ D-090 把升级花掉的钱变成<b>可再花的资产</b> ⇒ 收购站才有燃料。</para>
/// </remarks>
[TestSuite]
public class VillageAssetTests
{
    private static readonly HeroId Alice = new("h-alice");
    private static readonly ItemId Stone = new("石料");
    private static readonly DomainResourceId GoldResource = new("金币");
    private static readonly DomainResourceId MaterialResource = new("材料");
    private static readonly DomainResourceId AssetResource = VillageUpgradeService.Asset;

    /// <summary>造一个场景：村庄 + Alice + 升级服务 + 收购站。</summary>
    private static (DomainVillage Village,
                    Dictionary<HeroId, HeroAgent> Heroes,
                    VillageUpgradeService Upgrades,
                    VillagePurchaseService Purchase)
        MakeScene(long villageGold, long villageMaterial, int tier, long bagStone = 0)
    {
        var village = new DomainVillage("v1");
        if (villageGold > 0) village.Add(GoldResource, villageGold);
        if (villageMaterial > 0) village.Add(MaterialResource, villageMaterial);

        var map = new Dictionary<HeroId, HeroAgent>();
        var bag = new Inventory();
        if (bagStone > 0) bag = bag.TryAdd(Stone, bagStone)!;

        var hero = new HeroAgent { Name = "Alice" };
        hero.AssignId(Alice);
        hero.RestoreBag(bag);
        hero.RestoreWallet(new Wallet(new Dictionary<DomainResourceId, long> { { GoldResource, 0 } }));
        map[Alice] = hero;

        var upgrades = new VillageUpgradeService(village, GoldResource, MaterialResource);
        var purchase = new VillagePurchaseService(
            village, TestPrices.Book, GoldResource, MaterialResource, upgrades, () => tier);

        return (village, map, upgrades, purchase);
    }

    /// <summary>D-090 ①：升级 ⇒ <b>钱没消失</b>（金币变资产）。</summary>
    [TestCase]
    public void Upgrade_MovesGoldFromTreasuryToVillageAssets()
    {
        var (village, _, upgrades, _) = MakeScene(villageGold: 200, villageMaterial: 100, tier: 0);

        bool ok = upgrades.TryUpgrade(0);

        Assertions.AssertThat(ok).IsTrue();
        Assertions.AssertThat(village.Get(GoldResource)).IsEqual(200 - 50);
        Assertions.AssertThat(village.Get(AssetResource)).IsEqual(50);
    }

    /// <summary>升级 ⇒ <b>总量守恒</b>（村库 + 资产没变）。</summary>
    [TestCase]
    public void Upgrade_KeepsVillageWealthUnchanged()
    {
        var (village, _, upgrades, _) = MakeScene(villageGold: 200, villageMaterial: 100, tier: 0);

        long before = village.Get(GoldResource) + village.Get(AssetResource);
        upgrades.TryUpgrade(0);
        long after = village.Get(GoldResource) + village.Get(AssetResource);

        Assertions.AssertThat(after).IsEqual(before);
    }

    /// <summary>D-090 ②：收购 ⇒ <b>资产变少、英雄的钱变多</b>（资产是燃料）。</summary>
    [TestCase]
    public void Purchase_SpendsVillageAssets_AndPaysTheHero()
    {
        // tier 0 ⇒ 保留金 50；村库现金 60 ⇒ 现金可动用 10；资产 40 ⇒ 预算共 50。
        var (village, heroes, upgrades, purchase) = MakeScene(villageGold: 60, villageMaterial: 0, tier: 0, bagStone: 5);
        village.Add(AssetResource, 40);

        purchase.NoteGathered(Alice, 5);
        var records = purchase.RunRound(day: 5, heroes.Values);

        // 夹具价 5 ⇒ 预算 50 ⇒ 买 10 份？⇒ 背包只有 5 份 ⇒ 买 5 份 = 25 金。
        Assertions.AssertThat(records.Count).IsEqual(1);
        Assertions.AssertThat(records[0].Qty).IsEqual(5);
        Assertions.AssertThat(records[0].Amount).IsEqual(25);

        Assertions.AssertThat(heroes[Alice].Wallet.Amount(GoldResource)).IsEqual(25);
        // 现金只被扣了「超出保留金」的那 10 ⇒ 剩下的 15 来自资产。
        Assertions.AssertThat(village.Get(AssetResource)).IsEqual(40 - 15);
        Assertions.AssertThat(village.Get(GoldResource)).IsEqual(50);
    }

    /// <summary>⚠️ <b>现金里的升级钱不能被花掉</b>（村庄发展不被抽空）。</summary>
    [TestCase]
    public void Purchase_NeverTouchesTheGoldReservedForTheNextUpgrade()
    {
        // tier 0 ⇒ 保留金 50。村库 60 ⇒ 只有 10 可动用；资产 0 ⇒ 预算 10。
        var (village, heroes, _, purchase) = MakeScene(villageGold: 60, villageMaterial: 0, tier: 0, bagStone: 5);

        purchase.NoteGathered(Alice, 5);
        var records = purchase.RunRound(day: 5, heroes.Values);

        Assertions.AssertThat(records.Count).IsEqual(1);
        Assertions.AssertThat(records[0].Qty).IsEqual(2);        // 10 ÷ 5 = 2 份
        Assertions.AssertThat(village.Get(GoldResource)).IsEqual(50);   // 保留金没被动
    }

    /// <summary>D-090 ③：<b>守恒律</b>（升级 + 收购之后仍然成立）。</summary>
    [TestCase]
    public void Conservation_HoldsAcrossUpgradeAndPurchase()
    {
        var (village, heroes, upgrades, purchase) = MakeScene(villageGold: 200, villageMaterial: 100, tier: 0, bagStone: 3);

        long before = heroes.Values.Sum(h => h.Wallet.Amount(GoldResource))
                    + village.Get(GoldResource) + village.Get(AssetResource) + 0;   // 无商队销毁

        upgrades.TryUpgrade(0);                                   // 升级：钱变成资产
        purchase.NoteGathered(Alice, 3);
        purchase.RunRound(day: 5, heroes.Values);                // 收购：资产变成英雄的钱

        long after = heroes.Values.Sum(h => h.Wallet.Amount(GoldResource))
                   + village.Get(GoldResource) + village.Get(AssetResource) + 0;

        Assertions.AssertThat(after).IsEqual(before);
        Assertions.AssertThat(after).IsEqual(200);   // 总量没变（村库 200 起手）
    }

    /// <summary>D-090 ④：<b>老档兼容</b> —— 旧档没有「村庄资产」⇒ 读档时补记。</summary>
    [TestCase]
    public void LegacySave_GetsVillageAssetRestored()
    {
        var save = new SaveGame
        {
            GoldSpentTotal = 150,      // 老档里「累计销毁（升级花费）」= 150
            Village = new VillageSave
            {
                Id = "v1",
                Resources = new Dictionary<string, long> { { "金币", 20 } },
            },
        };

        long restore = SaveGame.VillageAssetToRestore(save);

        Assertions.AssertThat(restore).IsEqual(150);
    }

    /// <summary>⚠️ <b>新档不能补</b>（否则会把「已被收购花掉的部分」又加回来）。</summary>
    [TestCase]
    public void NewSave_DoesNotGetVillageAssetRestored()
    {
        var save = new SaveGame
        {
            GoldSpentTotal = 150,
            Village = new VillageSave
            {
                Id = "v1",
                Resources = new Dictionary<string, long>
                {
                    { "金币", 20 },
                    { AssetResource.Value, 40 },     // 新档：资产余额已经在了（被收购花掉过）
                },
            },
        };

        long restore = SaveGame.VillageAssetToRestore(save);

        Assertions.AssertThat(restore).IsEqual(0);
    }

    /// <summary>没升级过的老档 ⇒ 没什么可补（资产 0）。</summary>
    [TestCase]
    public void LegacySave_WithoutUpgrades_RestoresNothing()
    {
        var save = new SaveGame
        {
            GoldSpentTotal = 0,
            Village = new VillageSave { Id = "v1", Resources = new Dictionary<string, long>() },
        };

        Assertions.AssertThat(SaveGame.VillageAssetToRestore(save)).IsEqual(0);
    }
}
