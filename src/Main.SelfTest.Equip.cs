using Godot;
using System.Collections.Generic;
using System.Linq;
using ClaudeCodeGameStudios.Domain.Heroes;
using ClaudeCodeGameStudios.Domain.Items;
using ClaudeCodeGameStudios.Domain.Village;

/// <summary>
/// <b>装备线的真机自检</b>（D-119）—— 走<b>真实游戏</b>的那条路。
/// </summary>
/// <remarks>
/// <para>⚠️ <b>为什么单测不够</b>：单元测试验的是「规则」
/// （加成 / 耐久 / 买卖 / 修理），而这里验的是<b>接线</b>：
/// 日切有没有真的调它、建造流程能不能真的盖出商店、
/// 存档里有没有真的存下装备（<c>--save</c> 那条路）。</para>
///
/// <para>⚠️ <b>为什么它必须自己造一个村庄</b>：标定用的采集器<b>不建商店</b>
/// （D-112 之后采集器只盖围栏 / 试炼塔 / 防护罩）
/// ⇒ 在标定世界里装备线<b>不会被触发</b> ⇒ 光跑采集看不出它有没有接上。
/// ⇒ 这里直接摆出两间店 + 一间铁匠铺，让它真的跑一轮。</para>
/// </remarks>
public partial class Main : Node2D
{
    /// <summary>是否已经跑过装备自检。</summary>
    private bool _equipTestDone;

    /// <summary>
    /// 装备线自检：建店 → 补货 → 英雄自主购买 → 磨损 → 修理。
    /// </summary>
    private void RunEquipSelfTest()
    {
        _equipTestDone = true;

        var shop = BuildableBuildings.WeaponShop.Id;
        var armorShop = BuildableBuildings.ArmorShop.Id;
        var blacksmith = TierBuildings.Blacksmith.Id;

        // ① 摆出三栋已建成的建筑（相当于玩家盖好了）。
        int x = 0;
        foreach (var id in new[] { shop, armorShop, blacksmith })
        {
            _villageModel.TryPlace(new BuildingInstance(id, x, 0, 1, 1,
                constructionProgress: 1.0, affectsAreaW: 0, affectsAreaH: 0));
            x += 2;
        }

        // ② 给钱：村库要有钱补货，英雄要有钱买。
        _villageModel.Add(ClaudeCodeGameStudios.Application.Stall.StallService.Gold, 500L);
        _villageModel.Add(MaterialsId, 100L);

        var hero = _heroes.FirstOrDefault();
        if (hero is null)
        {
            GD.Print("EQUIPTEST FAIL：村子里一个人都没有。");
            return;
        }

        hero.RestoreWallet(new ClaudeCodeGameStudios.Domain.Items.Wallet(
            new Dictionary<ClaudeCodeGameStudios.Domain.Village.ResourceId, long>
            {
                [ClaudeCodeGameStudios.Application.Stall.StallService.Gold] = 300L,
            }));

        // ③ 补货 + 自主购买。
        var restocked = _heroEquipment.RestockShops(_villageModel);
        var actions = _heroEquipment.RunDay(_heroes, _villageModel);

        bool bought = actions.Any(a => a.Action == "买武器" || a.Action == "买衣服");
        bool equipped = !hero.Equipment.HasNoWeapon || !hero.Equipment.HasNoArmor;

        GD.Print($"EQUIPTEST 补货 {restocked.Count} 件 · 自主行为 {actions.Count} 次 · "
                 + $"买了={bought} 穿上了={equipped}");

        // ④ 把装备磨坏 ⇒ 再跑一轮 ⇒ 应该**修理**（铁匠铺已建成）。
        var sword = ClaudeCodeGameStudios.Domain.Items.EquipmentCatalog.Default
            .Find(new ItemId("石剑"));
        if (sword is not null)
        {
            hero.Equipment = ClaudeCodeGameStudios.Application.EquipmentService.Equip(
                hero.Equipment, sword, ClaudeCodeGameStudios.Domain.Items.EquipmentCatalog.Default);
            hero.Equipment = hero.Equipment with { WeaponDurability = 0 };
        }

        var repairActions = _heroEquipment.RunDay(_heroes, _villageModel);
        bool repaired = repairActions.Any(a => a.Action == "修理");
        int durability = hero.Equipment.WeaponDurability;

        GD.Print($"EQUIPTEST 修理={repaired} 耐久={durability}");

        if (restocked.Count > 0 && bought && equipped && repaired && durability > 0)
        {
            GD.Print("EQUIPTEST PASS");
        }
        else
        {
            GD.Print("EQUIPTEST FAIL");
        }
    }
}
