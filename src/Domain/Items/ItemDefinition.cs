using System;

namespace ClaudeCodeGameStudios.Domain.Items;

/// <summary>
/// 物品的<b>定义</b>：不可变的种类信息（id / 名称 / 描述）。
/// </summary>
/// <remarks>
/// <para><b>定义 ≠ 库存</b>（基线 §5 / 计划 §M1 第 4 条）。</para>
/// <list type="bullet">
/// <item><b>定义</b>：「石料」这个种类，名字叫什么、一份多少、看不出什么词缀。
/// ——<see cref="ItemDefinition"/>,在游戏启动时定好,整局不变。</item>
/// <item><b>库存</b>：「张麻子持有 3 份石料、村里 17 份」——按持有者划开的数量。
/// ——<see cref="Inventory"/>,随交易 / 采集 / 摆摊变化。</item>
/// </list>
///
/// <para><b>本阶段只放基线 §5 列出的最小字段</b>：</para>
/// <list type="bullet">
/// <item>Id：机器名（库存/交易按它引用）</item>
/// <item>DisplayName：杂谈里用的"石料"等显示文本</item>
/// <item>Rarity：稀有度（R-05 补；见 <see cref="ItemRarity"/>）</item>
/// </list>
/// <para>词缀 / 耐久度 / 制作配方——<b>不做</b>（计划 §1.3）。
/// 「是否可交易 / 是否可堆叠」也<b>不做</b>：前者要动交易链路，
/// 后者要扩 <see cref="Inventory"/>（不可堆叠物品 = R-01 的 E06 另开任务）。</para>
/// </remarks>
public sealed class ItemDefinition
{
    /// <summary>机器名。必须非空。</summary>
    public ItemId Id { get; }

    /// <summary>杂谈里显示给玩家看的名字。允许为空字符串（无显示名时 Id 即显示名）。</summary>
    public string DisplayName { get; }

    /// <summary>稀有度（R-05）。<b>必填</b> —— 没有默认值。</summary>
    /// <remarks>
    /// <b>为什么不给默认值</b>：默认值会让「忘了标稀有度」这件事<b>静默发生</b> ——
    /// 物品悄悄成了"普通"，直到某天掉落表按稀有度配权重时出了怪事。
    /// 宁可编译期逼着写，也不让它默认。
    /// </remarks>
    public ItemRarity Rarity { get; }

    /// <summary>
    /// 构造一个物品定义。
    /// </summary>
    /// <param name="id">机器名；<b>不得为空</b>。</param>
    /// <param name="displayName">显示名（可空字符串）。</param>
    /// <param name="rarity">稀有度。</param>
    /// <exception cref="ArgumentException"><paramref name="id"/> 为空。</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="rarity"/> 不是已定义的稀有度。</exception>
    public ItemDefinition(ItemId id, string displayName, ItemRarity rarity)
    {
        if (string.IsNullOrEmpty(id.Value))
        {
            throw new ArgumentException("物品 Id 不得为空。", nameof(id));
        }

        if (!System.Enum.IsDefined(typeof(ItemRarity), rarity))
        {
            throw new ArgumentOutOfRangeException(
                nameof(rarity), rarity, "未定义的稀有度。");
        }

        Id = id;
        DisplayName = displayName ?? string.Empty;
        Rarity = rarity;
    }

    /// <summary>
    /// 构造一个<b>普通</b>稀有度的物品定义。
    /// </summary>
    /// <param name="id">机器名；<b>不得为空</b>。</param>
    /// <param name="displayName">显示名（可空字符串）。</param>
    /// <remarks>
    /// <b>这个重载是给「确实就是普通」的场景用的</b>（比如临时夹具）。
    /// 正式登记物品请用三参构造，把稀有度写明。
    /// </remarks>
    /// <exception cref="ArgumentException"><paramref name="id"/> 为空。</exception>
    public ItemDefinition(ItemId id, string displayName)
        : this(id, displayName, ItemRarity.Common) { }
}
