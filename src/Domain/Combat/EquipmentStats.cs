using System;
using ClaudeCodeGameStudios.Domain.Items;

namespace ClaudeCodeGameStudios.Domain.Combat;

/// <summary>
/// <b>装备加成的换算</b>（D-119）—— 「穿上的东西给这个人加多少」。
/// </summary>
/// <remarks>
/// <para>⚠️ <b>为什么必须是纯函数</b>：战斗必须能<b>离线复现</b>（基线 §8.5 D5）
/// ⇒ 「这个人的属性是多少」不能读场景树、不能读时间
/// ⇒ 纯函数：基础属性 + 身上的东西 ⇒ 新属性。</para>
///
/// <para>⚠️ <b>耐久为 0 ⇒ 加成不生效</b>（不是减半、不是照常生效）：
/// 装备坏了就是没有防护 ⇒ 这条规则只有这一处实现，
/// 存档校验（<c>SaveValidator</c>）与战斗属性都读它。</para>
/// </remarks>
public static class EquipmentStats
{
    /// <summary>
    /// 把装备的加成加到基础属性上。
    /// </summary>
    /// <param name="baseStats">基础属性（性格 × 投入度 + 试炼塔加成）。</param>
    /// <param name="gear">身上穿的东西。</param>
    /// <param name="catalog">装备注册表。</param>
    /// <returns>加完装备后的属性。</returns>
    /// <exception cref="ArgumentNullException"><paramref name="catalog"/> 为 null。</exception>
    public static CombatStats Resolve(CombatStats baseStats, EquippedGear gear, EquipmentCatalog catalog)
    {
        if (catalog is null) throw new ArgumentNullException(nameof(catalog));
        if (gear is null) return baseStats;

        int attack = 0;
        int defense = 0;

        // ⚠️ **D-124：按「装备自己的定义」加自己的加成**（不再按槽位分派）。
        // ⚠️ **为什么改**：原实现是「武器槽只加攻、衣服槽只加防」
        // ⇒ D-124 把衣服改成**加攻**之后，衣服的加成**整个加不进去**
        // ⇒ 买了装备却毫无变化（守卫 `Equipment_NeverLowersTheStats` 当场抓到）。
        // ⇒ 现在：**每一件装备都按自己的 AttackBonus / DefenseBonus 加**
        // ⇒ 将来战斗公式一改、防御重新有效 ⇒ 填回 DefenseBonus 就自动生效。
        if (gear.Weapon is { } weaponId && gear.WeaponDurability > 0)
        {
            var def = catalog.Find(weaponId);
            if (def is not null)
            {
                attack += def.AttackBonus;
                defense += def.DefenseBonus;
            }
        }

        if (gear.Armor is { } armorId && gear.ArmorDurability > 0)
        {
            var def = catalog.Find(armorId);
            if (def is not null)
            {
                attack += def.AttackBonus;
                defense += def.DefenseBonus;
            }
        }

        if (attack == 0 && defense == 0) return baseStats;

        return baseStats with
        {
            Attack = baseStats.Attack + attack,
            Defense = baseStats.Defense + defense,
        };
    }
}
