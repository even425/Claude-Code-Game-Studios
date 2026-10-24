using System;
using System.Collections.Generic;
using ClaudeCodeGameStudios.Domain.Items;

namespace ClaudeCodeGameStudios.Application;

/// <summary>
/// <b>装备的规则</b>（D-119）：穿上、磨损、修理。
/// </summary>
/// <remarks>
/// <para>⚠️ <b>全是纯函数</b>：穿戴 / 磨损 / 修理的规则住在这里，
/// 表现层与存档只「照着算」⇒ 可离线单测（D-119 的守卫直接调它）。</para>
///
/// <para>⚠️ <b>「战斗一次扣 1 点」是这里唯一的口径</b>：
/// 副本 / 试炼塔 / 狩猎 / 兽潮四场战斗<b>都</b>走它
/// ⇒ 不会出现「副本扣 3、狩猎扣 1」这种各扣各的。</para>
/// </remarks>
public static class EquipmentService
{
    /// <summary>每打一场战斗，装备掉几点耐久（**建议值**，允许调）。</summary>
    public const int WearDownPerBattle = 1;

    /// <summary>
    /// 磨损：每件装备的耐久 -1（<b>不会低于 0</b>）。
    /// </summary>
    /// <param name="gear">当前身上的装备。</param>
    /// <returns>磨损后的装备。</returns>
    public static EquippedGear WearDown(EquippedGear gear)
    {
        if (gear is null) throw new ArgumentNullException(nameof(gear));

        int weapon = gear.WeaponDurability - WearDownPerBattle;
        if (weapon < 0) weapon = 0;

        int armor = gear.ArmorDurability - WearDownPerBattle;
        if (armor < 0) armor = 0;

        return gear with { WeaponDurability = weapon, ArmorDurability = armor };
    }

    /// <summary>
    /// 穿上某件装备（<b>替换掉原来那件</b>）。
    /// </summary>
    /// <param name="gear">当前身上的装备。</param>
    /// <param name="definition">要穿上的装备（<b>必须在注册表里</b>）。</param>
    /// <param name="catalog">装备注册表。</param>
    /// <returns>穿上之后的装备。</returns>
    /// <exception cref="ArgumentNullException">参数为 null。</exception>
    /// <exception cref="InvalidOperationException">装备不在注册表里。</exception>
    /// <remarks>
    /// ⚠️ <b>被替换下来的旧装备直接报废</b>（不进背包）：
    /// 它的耐久已经损耗过，且「换下来的装备怎么处置」在 D-119 里<b>没有拍板</b>
    /// ⇒ 第一版不猜 ⇒ 明确报废（写进台账的「范围外」一栏）。
    /// </remarks>
    public static EquippedGear Equip(
        EquippedGear gear,
        EquipmentDefinition definition,
        EquipmentCatalog catalog)
    {
        if (gear is null) throw new ArgumentNullException(nameof(gear));
        if (definition is null) throw new ArgumentNullException(nameof(definition));
        if (catalog is null) throw new ArgumentNullException(nameof(catalog));

        if (catalog.Find(definition.Id) is null)
        {
            throw new InvalidOperationException(
                $"装备 {definition.Id.Value} 不在注册表里 ⇒ 不能穿（注册表是「什么是装备」的唯一真源）。");
        }

        return definition.Slot switch
        {
            EquipmentSlot.Weapon => gear with
            {
                Weapon = definition.Id,
                WeaponDurability = definition.DurabilityMax,
            },
            EquipmentSlot.Armor => gear with
            {
                Armor = definition.Id,
                ArmorDurability = definition.DurabilityMax,
            },
            _ => throw new InvalidOperationException(
                $"装备 {definition.Id.Value} 的槽位是「{definition.Slot}」⇒ 没有槽位可穿。"),
        };
    }

    /// <summary>
    /// 修理：把身上<b>所有坏了的装备</b>修满（好的不动）。
    /// </summary>
    /// <param name="gear">当前身上的装备。</param>
    /// <param name="catalog">装备注册表。</param>
    /// <returns>修好之后的装备。</returns>
    public static EquippedGear Repair(EquippedGear gear, EquipmentCatalog catalog)
    {
        if (gear is null) throw new ArgumentNullException(nameof(gear));
        if (catalog is null) throw new ArgumentNullException(nameof(catalog));

        int weapon = gear.WeaponDurability;
        if (gear.Weapon is { } w && weapon <= 0)
        {
            var def = catalog.Find(w);
            if (def is not null) weapon = def.DurabilityMax;
        }

        int armor = gear.ArmorDurability;
        if (gear.Armor is { } a && armor <= 0)
        {
            var def = catalog.Find(a);
            if (def is not null) armor = def.DurabilityMax;
        }

        return gear with { WeaponDurability = weapon, ArmorDurability = armor };
    }

    /// <summary>
    /// 修理费：身上所有<b>坏了</b>的装备的修理费之和（**建议值**，允许调）。
    /// </summary>
    /// <param name="gear">当前身上的装备。</param>
    /// <param name="catalog">装备注册表。</param>
    /// <returns>修理费（没有坏装备 ⇒ 0）。</returns>
    public static long RepairCostOf(EquippedGear gear, EquipmentCatalog catalog)
    {
        if (gear is null) throw new ArgumentNullException(nameof(gear));
        if (catalog is null) throw new ArgumentNullException(nameof(catalog));

        long cost = 0;

        if (gear.Weapon is { } w && gear.WeaponDurability <= 0)
        {
            var def = catalog.Find(w);
            if (def is not null) cost += def.RepairCost;
        }

        if (gear.Armor is { } a && gear.ArmorDurability <= 0)
        {
            var def = catalog.Find(a);
            if (def is not null) cost += def.RepairCost;
        }

        return cost;
    }
}
