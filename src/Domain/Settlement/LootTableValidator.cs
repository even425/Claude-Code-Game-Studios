using System;
using System.Collections.Generic;
using ClaudeCodeGameStudios.Domain.Items;

namespace ClaudeCodeGameStudios.Domain.Settlement;

/// <summary>
/// 掉落表的<b>配置验证器</b>（R-05）。纯函数：判对错，不改数据。
/// </summary>
/// <remarks>
/// <para><b>为什么要有它</b>：掉落表引用的是 <see cref="ItemId"/>，
/// 而「这个 Id 是不是合法物品、它卖不卖得出去」要拿别处的表才能查。
/// 没有验证器时，一个拼错的 Id 会一路走到发奖那一步，
/// 被 <c>ItemGrantService</c> 判成 <c>UnknownItem</c> 拒掉 ——
/// **错误发生在离原因最远的地方**（R-01 踩过这个坑：战利品物品未登记被拒）。
/// 验证器把错误拉回到<b>配置那一刻</b>。</para>
///
/// <para><b>验什么</b>：</para>
/// <list type="number">
/// <item>表本身合法（权重为正、数量为正且区间不倒、抽取次数非负）</item>
/// <item>每条引用的物品<b>已在物品注册表登记</b>（且有稀有度）</item>
/// <item>每条引用的物品<b>在价格表登记为可售</b>（价格 &gt; 0）</item>
/// </list>
///
/// <para><b>不验什么</b>：概率之和、稀有度分布是否"好看" ——
/// 那是<b>数值平衡</b>，不是配置错误。策划故意让史诗物品只占 0.2% 权重是合法的。</para>
///
/// <para><b>错误怎么报告</b>：返回<b>错误清单</b>（不是抛异常）——
/// 配置有问题时游戏仍该能起来，只是那个副本不掉落东西。
/// 这与 <c>SaveValidator</c> 的做法一致（它也返回错误清单而不是抛）。</para>
/// </remarks>
public static class LootTableValidator
{
    /// <summary>
    /// 验证一张掉落表。
    /// </summary>
    /// <param name="table">掉落表。</param>
    /// <param name="catalog">物品注册表（查稀有度与是否登记）。</param>
    /// <param name="prices">价格表（查是否可售）。</param>
    /// <returns>错误清单；<b>空清单 = 配置合法</b>。</returns>
    /// <exception cref="ArgumentNullException">任一参数为 null。</exception>
    public static IReadOnlyList<string> Validate(
        LootTable table, ItemCatalog catalog, PriceBook prices)
    {
        if (table is null) throw new ArgumentNullException(nameof(table));
        if (catalog is null) throw new ArgumentNullException(nameof(catalog));
        if (prices is null) throw new ArgumentNullException(nameof(prices));

        var errors = new List<string>();

        if (table.Draws < 0)
        {
            errors.Add($"掉落表「{table.DungeonName}」：抽取次数为负（{table.Draws}）。");
        }

        if (table.Entries.Count == 0)
        {
            errors.Add($"掉落表「{table.DungeonName}」：池子是空的 —— 这个副本不会产出任何东西。");
            return errors;
        }

        double weightSum = 0.0;

        foreach (var entry in table.Entries)
        {
            string where = $"掉落表「{table.DungeonName}」条目「{entry.Item.Value}」";

            if (string.IsNullOrEmpty(entry.Item.Value))
            {
                errors.Add($"{where}：物品标识为空。");
                continue;
            }

            if (!entry.IsWellFormed)
            {
                errors.Add($"{where}：配置非法（权重 {entry.Weight}，数量 {entry.MinQuantity}~{entry.MaxQuantity}）"
                           + " —— 权重必须为正、数量必须 ≥ 1 且区间不倒。");
                continue;
            }

            weightSum += entry.Weight;

            if (!catalog.Contains(entry.Item))
            {
                errors.Add($"{where}：物品没有在物品注册表里登记"
                           + $"（连稀有度都没有）—— 引用了不存在的物品。");
                continue;
            }

            if (!prices.IsSellable(entry.Item))
            {
                errors.Add($"{where}：物品在价格表里不可售（价格 0 或未登记）"
                           + " —— 它发到背包之后没人能买、也摆不了摊。");
            }
        }

        if (weightSum <= 0.0)
        {
            errors.Add($"掉落表「{table.DungeonName}」：所有权重都是 0 —— 无法抽取。");
        }

        return errors;
    }

    /// <summary>验证是否通过。</summary>
    /// <param name="table">掉落表。</param>
    /// <param name="catalog">物品注册表。</param>
    /// <param name="prices">价格表。</param>
    /// <returns>无错误为 true。</returns>
    public static bool IsValid(LootTable table, ItemCatalog catalog, PriceBook prices)
        => Validate(table, catalog, prices).Count == 0;
}
