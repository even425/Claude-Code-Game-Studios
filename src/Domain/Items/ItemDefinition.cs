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
/// </list>
/// <para>稀有度 / 词缀 / 耐久度 / 制作配方——**不做**（计划 §1.3）。</para>
/// </remarks>
public sealed class ItemDefinition
{
    /// <summary>机器名。必须非空。</summary>
    public ItemId Id { get; }

    /// <summary>杂谈里显示给玩家看的名字。允许为空字符串（无显示名时 Id 即显示名）。</summary>
    public string DisplayName { get; }

    /// <summary>
    /// 构造一个物品定义。
    /// </summary>
    /// <param name="id">机器名；<b>不得为空</b>。</param>
    /// <param name="displayName">显示名（可空字符串）。</param>
    /// <exception cref="ArgumentException"><paramref name="id"/> 为空。</exception>
    public ItemDefinition(ItemId id, string displayName)
    {
        if (string.IsNullOrEmpty(id.Value))
        {
            throw new ArgumentException("物品 Id 不得为空。", nameof(id));
        }

        Id = id;
        DisplayName = displayName ?? string.Empty;
    }
}
