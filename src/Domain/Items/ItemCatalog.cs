using System;
using System.Collections.Generic;
using System.Linq;

namespace ClaudeCodeGameStudios.Domain.Items;

/// <summary>
/// 物品注册表：<see cref="ItemId"/> → <see cref="ItemDefinition"/>（含稀有度）。
/// </summary>
/// <remarks>
/// <para><b>为什么要有它</b>：<see cref="ItemDefinition"/> 此前<b>全仓无实例化</b> ——
/// 类在、没人用（R-05 审计的第一条发现）。稀有度挂在它上面而不给它一个容器，
/// 等于把规则挂在一个没人引用的类上。</para>
///
/// <para><b>本类就是那个容器</b>：掉落表引用的是 <see cref="ItemId"/>，
/// 而「这个 Id 是不是合法物品、它是什么稀有度」要在这里查。
/// 掉落表的<b>验证器</b>（<c>LootTableValidator</c>）就是靠它把
/// 「引用了不存在的物品」变成一条明确的报错，而不是等到发奖时
/// 被 <c>ItemGrantService</c> 判成 <c>UnknownItem</c>（R-01 踩过的坑）。</para>
///
/// <para><b>不可变</b>：与 <see cref="PriceBook"/> / <see cref="Inventory"/> 同款规矩 ——
/// 修改 = 返回新对象。</para>
///
/// <para><b>与 <see cref="PriceBook"/> 的分工</b>：注册表管「是什么、什么稀有度」，
/// 价格表管「多少钱」。两处都要登记 —— 那是刻意的（属性与价格本就是两件事），
/// 但**漏登记会被验证器抓出来**，不靠人记。</para>
/// </remarks>
public sealed class ItemCatalog
{
    private readonly Dictionary<ItemId, ItemDefinition> _definitions;

    /// <summary>构造一张注册表。</summary>
    /// <param name="definitions">物品定义清单。<b>Id 不得重复</b>。</param>
    /// <exception cref="ArgumentNullException"><paramref name="definitions"/> 为 null。</exception>
    /// <exception cref="ArgumentException">某个定义的 Id 为空。</exception>
    /// <exception cref="InvalidOperationException">两个定义用了同一个 Id。</exception>
    public ItemCatalog(IEnumerable<ItemDefinition> definitions)
    {
        if (definitions is null) throw new ArgumentNullException(nameof(definitions));

        _definitions = new Dictionary<ItemId, ItemDefinition>(ItemIdComparer.Comparer);
        foreach (var def in definitions)
        {
            if (def is null) continue;
            if (string.IsNullOrEmpty(def.Id.Value))
            {
                throw new ArgumentException("物品定义的 Id 不得为空。", nameof(definitions));
            }

            if (_definitions.ContainsKey(def.Id))
            {
                throw new InvalidOperationException(
                    $"物品 {def.Id.Value} 重复登记：同一个 Id 只能有一条定义。");
            }

            _definitions[def.Id] = def;
        }
    }

    /// <summary>空注册表（没有登记任何物品）。</summary>
    public static ItemCatalog Empty { get; } = new(Array.Empty<ItemDefinition>());

    /// <summary>
    /// 默认注册表（<b>【占位】</b>，与 <see cref="PriceBook.Default"/> 对齐）。
    /// </summary>
    /// <remarks>
    /// <para>登记的物品与 <see cref="PriceBook.Default"/> 的三个占位价<b>一一对应</b> ——
    /// 少登记一个，掉落表验证器就会报错（那个物品"没有稀有度、也没登记"）。</para>
    ///
    /// <para><b>稀有度是占位</b>：石料/木料是基础材料（普通），
    /// 布匹贵一些（优秀）。二轮定掉落与价格时会一并复核。</para>
    /// </remarks>
    public static ItemCatalog Default { get; } = new(new[]
    {
        new ItemDefinition(new ItemId("石料"), "石料", ItemRarity.Common),
        new ItemDefinition(new ItemId("木料"), "木料", ItemRarity.Common),
        new ItemDefinition(new ItemId("布匹"), "布匹", ItemRarity.Uncommon),
        // ⚠️ **D-118：狩猎的战利品**（与 `PriceBook.Default` 一一对应）
        new ItemDefinition(new ItemId("兽皮"), "兽皮", ItemRarity.Common),
        new ItemDefinition(new ItemId("兽肉"), "兽肉", ItemRarity.Common),
    });

    /// <summary>全部定义（按 Id 排序，稳定顺序便于比对）。</summary>
    public IReadOnlyList<ItemDefinition> All =>
        _definitions.Values
            .OrderBy(d => d.Id.Value, StringComparer.Ordinal)
            .ToArray();

    /// <summary>登记了多少件物品。</summary>
    public int Count => _definitions.Count;

    /// <summary>查某个物品的定义；没登记返回 null。</summary>
    /// <param name="id">物品标识。</param>
    /// <returns>定义；未登记时为 null。</returns>
    public ItemDefinition? Find(ItemId id)
    {
        if (string.IsNullOrEmpty(id.Value)) return null;
        return _definitions.TryGetValue(id, out var def) ? def : null;
    }

    /// <summary>某个物品是否已登记。</summary>
    /// <param name="id">物品标识。</param>
    /// <returns>已登记为 true。</returns>
    public bool Contains(ItemId id) => Find(id) is not null;

    /// <summary>查某个物品的稀有度；没登记返回 null。</summary>
    /// <param name="id">物品标识。</param>
    /// <returns>稀有度；未登记时为 null。</returns>
    public ItemRarity? RarityOf(ItemId id) => Find(id)?.Rarity;
}
