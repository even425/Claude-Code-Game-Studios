using Godot;
using System.Collections.Generic;
using System.Linq;
using ClaudeCodeGameStudios.Domain.Items;
using ClaudeCodeGameStudios.Domain.Village;

/// <summary>
/// <b>装备店面板的真机自检</b>（D-127）—— 玩家能打开商店、给英雄买装备。
/// </summary>
/// <remarks>
/// <para>⚠️ <b>为什么要有</b>：装备是「英雄自主购买」⇒ 玩家建了店**只能看不能买**
/// ⇒ 这里补一条**玩家能参与**的路 ⇒ 但面板是表现层 ⇒ 单测碰不到
/// ⇒ 必须有真机自检（与 `--market` 摊位面板同款理由）。</para>
///
/// <para>⚠️ <b>验的是三件事</b>：面板**列得出货**、列得出英雄、**买得成**
/// （钱从英雄钱包扣 ⇒ 货少一件 ⇒ 村库多一笔）。</para>
/// </remarks>
public partial class Main : Node2D
{
    /// <summary>是否已经跑过装备店自检。</summary>
    private bool _equipShopTestDone;

    /// <summary>装备店面板自检。</summary>
    private void RunEquipShopSelfTest()
    {
        _equipShopTestDone = true;

        // ① 摆出一间已建成的武器店（自检绕过建造流程）。
        _villageModel.TryPlace(new BuildingInstance(
            BuildableBuildings.WeaponShop.Id, 0, -3, 1, 1,
            constructionProgress: 1.0, affectsAreaW: 0, affectsAreaH: 0));

        // ② 给它两件货，并给第一个英雄 100 金。
        _equipmentShops.AddStock(BuildableBuildings.WeaponShop.Id, new ItemId("石剑"), 2);

        var hero = _heroes.FirstOrDefault();
        if (hero is null)
        {
            GD.Print("EQUIPSHOP FAIL：村子里一个人都没有。");
            return;
        }

        var gold = ClaudeCodeGameStudios.Application.Stall.StallService.Gold;
        hero.RestoreWallet(new ClaudeCodeGameStudios.Domain.Items.Wallet(
            new Dictionary<ClaudeCodeGameStudios.Domain.Village.ResourceId, long> { [gold] = 100L }));

        // ③ 打开面板。
        InitEquipmentPanel();
        _equipPanel?.Open();

        int stockRows = _equipPanel?.StockRowCount ?? 0;
        int heroRows = _equipPanel?.HeroRowCount ?? 0;

        long villageGoldBefore = _villageModel.Get(gold);
        long heroGoldBefore = hero.Wallet.Amount(gold);
        long stockBefore = _equipmentShops.CountOf(BuildableBuildings.WeaponShop.Id, new ItemId("石剑"));

        bool bought = _equipPanel?.BuySelected() ?? false;

        long heroGoldAfter = hero.Wallet.Amount(gold);
        long villageGoldAfter = _villageModel.Get(gold);
        long stockAfter = _equipmentShops.CountOf(BuildableBuildings.WeaponShop.Id, new ItemId("石剑"));

        GD.Print($"EQUIPSHOP 货架 {stockRows} 行 · 英雄 {heroRows} 行 · 买成={bought}");
        GD.Print($"EQUIPSHOP 英雄金 {heroGoldBefore} → {heroGoldAfter}；村库金 {villageGoldBefore} → {villageGoldAfter}；"
                 + $"库存 {stockBefore} → {stockAfter}；穿上={hero.Equipment.Weapon?.Value ?? "无"}");

        bool ok = bought
                  && stockRows > 0
                  && heroRows > 0
                  && heroGoldAfter < heroGoldBefore
                  && villageGoldAfter > villageGoldBefore
                  && stockAfter == stockBefore - 1
                  && hero.Equipment.Weapon is not null;

        GD.Print(ok ? "EQUIPSHOP PASS" : "EQUIPSHOP FAIL");
    }
}
