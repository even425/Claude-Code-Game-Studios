using System;

namespace ClaudeCodeGameStudios.Domain.Items;

/// <summary>
/// 一个英雄<b>身上穿着什么</b>（D-119）—— 武器槽 + 衣服槽 + 各自的耐久。
/// </summary>
/// <remarks>
/// <para>⚠️ <b>为什么是「值对象」而不是存在背包里</b>：
/// 装备一旦穿上就<b>不再是一个可以交易的东西</b>（它跟着这个人）
/// ⇒ 若放在背包里，就会出现「背包里那把剑到底穿没穿」的第二个真相
/// ⇒ 槽位状态<b>只存在这一处</b>。</para>
///
/// <para>⚠️ <b>耐久为 0 = 坏了</b>：加成<b>不生效</b>（不是「加成减半」），
/// 要修理才能继续用（D-119 拍板：耐久损耗 + 铁匠铺修理）。</para>
/// </remarks>
/// <param name="Weapon">武器的物品 Id（null = 没穿武器）。</param>
/// <param name="WeaponDurability">武器的当前耐久（0 = 坏了）。</param>
/// <param name="Armor">衣服的物品 Id（null = 没穿衣服）。</param>
/// <param name="ArmorDurability">衣服的当前耐久（0 = 坏了）。</param>
public sealed record EquippedGear(
    ItemId? Weapon,
    int WeaponDurability,
    ItemId? Armor,
    int ArmorDurability)
{
    /// <summary>什么都没穿。</summary>
    public static readonly EquippedGear Empty = new(null, 0, null, 0);

    /// <summary>武器槽是不是空的。</summary>
    public bool HasNoWeapon => Weapon is null;

    /// <summary>衣服槽是不是空的。</summary>
    public bool HasNoArmor => Armor is null;

    /// <summary>身上是不是有<b>坏了</b>的装备（需要修理）。</summary>
    public bool HasBroken =>
        (Weapon is not null && WeaponDurability <= 0)
        || (Armor is not null && ArmorDurability <= 0);
}
