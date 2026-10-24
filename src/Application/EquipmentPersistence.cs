using System;
using System.Collections.Generic;
using System.Linq;
using ClaudeCodeGameStudios.Data;
using ClaudeCodeGameStudios.Domain.Items;

namespace ClaudeCodeGameStudios.Application;

/// <summary>
/// D-119：<b>装备与商店库存</b>的存 / 取。
/// </summary>
/// <remarks>
/// <para><b>为什么单独抽出来</b>：同 <c>TrialTowerBonusPersistence</c> 的理由 ——
/// 转换住在 <c>Main.Save.cs</c>（Godot 侧）⇒ <b>单测碰不到</b>
/// ⇒ 「读档后装备凭空消失」这类缺陷可以静静躺在仓库里。
/// 抽成纯函数后，<b>生产代码与守卫调的是同一份</b>。</para>
///
/// <para>⚠️ <b>不要在这里复制规则</b>：本类只做<b>搬运</b>。
/// 「装备 Id 必须在注册表里、耐久 0..上限」那条规则住在 <c>SaveValidator</c>；
/// 「穿上 / 磨损 / 修理」住在 <c>EquipmentService</c>。</para>
/// </remarks>
public static class EquipmentPersistence
{
    /// <summary>写盘方向：把这个英雄身上的装备写进存档条目。</summary>
    /// <param name="agent">英雄（表现侧状态）。</param>
    /// <returns>要写进存档的四个值（装备 Id 为 null 时写空串）。</returns>
    public static (string Weapon, int WeaponDurability, string Armor, int ArmorDurability) Export(
        HeroAgent agent)
    {
        if (agent is null) throw new ArgumentNullException(nameof(agent));

        var gear = agent.Equipment;
        return (
            gear.Weapon?.Value ?? "",
            gear.WeaponDurability,
            gear.Armor?.Value ?? "",
            gear.ArmorDurability);
    }

    /// <summary>读盘方向：把存档里的装备还回给英雄。</summary>
    /// <param name="save">存档条目（已过 <c>SaveValidator</c>）。</param>
    /// <param name="agent">要恢复的英雄。</param>
    /// <remarks>
    /// 调用方应<b>先过 <c>SaveValidator.Validate</c></b>：
    /// 未登记的装备 Id、越界的耐久在那���层已被整份拒绝，这里不再重复守门。
    /// </remarks>
    public static void Import(HeroSave save, HeroAgent agent)
    {
        if (save is null) throw new ArgumentNullException(nameof(save));
        if (agent is null) throw new ArgumentNullException(nameof(agent));

        agent.Equipment = new EquippedGear(
            string.IsNullOrEmpty(save.EquippedWeaponId)
                ? null
                : new ItemId(save.EquippedWeaponId),
            save.WeaponDurability,
            string.IsNullOrEmpty(save.EquippedArmorId)
                ? null
                : new ItemId(save.EquippedArmorId),
            save.ArmorDurability);
    }

    /// <summary>写盘方向：把商店库存写进存档。</summary>
    /// <param name="shops">商店服务。</param>
    /// <param name="shopIds">要存的店铺（通常是已建成的两间店）。</param>
    /// <returns>库存条目清单。</returns>
    public static List<ShopStockSave> ExportStock(
        EquipmentShopService shops,
        IEnumerable<Domain.Village.BuildingId> shopIds)
    {
        if (shops is null) throw new ArgumentNullException(nameof(shops));
        if (shopIds is null) throw new ArgumentNullException(nameof(shopIds));

        var result = new List<ShopStockSave>();

        foreach (var shopId in shopIds)
        {
            var items = shops.StockOf(shopId)
                .Where(kv => kv.Value > 0)
                .OrderBy(kv => kv.Key.Value, StringComparer.Ordinal)
                .Select(kv => new ShopItemSave { ItemId = kv.Key.Value, Count = kv.Value })
                .ToList();

            if (items.Count == 0) continue;

            result.Add(new ShopStockSave { ShopId = shopId.Value, Items = items });
        }

        return result;
    }

    /// <summary>读盘方向：把存档里的库存还回商店。</summary>
    /// <param name="saves">存档条目清单。</param>
    /// <param name="shops">商店服务。</param>
    public static void ImportStock(IEnumerable<ShopStockSave> saves, EquipmentShopService shops)
    {
        if (saves is null) throw new ArgumentNullException(nameof(saves));
        if (shops is null) throw new ArgumentNullException(nameof(shops));

        foreach (var save in saves)
        {
            if (save is null) continue;
            if (string.IsNullOrEmpty(save.ShopId)) continue;

            var shopId = new Domain.Village.BuildingId(save.ShopId);
            shops.ClearStock(shopId);

            foreach (var item in save.Items)
            {
                if (item is null) continue;
                if (item.Count <= 0) continue;
                if (string.IsNullOrEmpty(item.ItemId)) continue;

                shops.AddStock(shopId, new ItemId(item.ItemId), item.Count);
            }
        }
    }
}
