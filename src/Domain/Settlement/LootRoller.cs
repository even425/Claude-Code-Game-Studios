using System;
using System.Collections.Generic;
using System.Linq;
using ClaudeCodeGameStudios.Domain.Heroes;
using ClaudeCodeGameStudios.Domain.Items;

namespace ClaudeCodeGameStudios.Domain.Settlement;

/// <summary>
/// 掉落表的<b>权重抽取</b>（R-05）。纯函数，无副作用。
/// </summary>
/// <remarks>
/// <para><b>为什么是「抽 K 次」而不是「每条独立判定」</b>：
/// 独立判定<b>控不住总量</b> —— 三个条目各 30% 概率，可能一次都不出，也可能一次出三样。
/// 抽 K 次则<b>产出数量固定</b>，稀有度分布完全由权重决定。</para>
///
/// <para><b>同一条可以被抽中多次</b>：抽中的份数累加到同一物品上，
/// 最终合成一个 <see cref="LootStack"/>。这让「一次出 3 个石料」是自然结果，
/// 而不是需要配置里写「石料×3」。</para>
///
/// <para><b>累积法</b>：与 <c>DecisionFlow.WeightedPick</c> 同一路数 ——
/// 抽一个 [0, 总权重) 的随机数，按累加值落在哪一段决定命中谁。
/// 不重复扫表，复杂度 O(条目数)。</para>
///
/// <para><b>坏数据不产出垃圾</b>：权重非正 / 数量非法的条目被跳过；
/// 一条都不可用时返回空列表。<b>绝不</b>产出 0 份或负数份的物品 ——
/// 那会让「凭空多出/凭空消失」这类 bug 混进背包。</para>
/// </remarks>
public static class LootRoller
{
    /// <summary>
    /// 按权重抽 <see cref="LootTable.Draws"/> 次，产出战利品。
    /// </summary>
    /// <param name="table">掉落表。</param>
    /// <param name="rng">随机源。<b>不得为 null</b>。</param>
    /// <returns>
    /// 战利品清单（按物品 Id 排序，稳定顺序）。表为空、抽取次数为 0
    /// 或没有一条可用条目时返回空列表。
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="rng"/> 为 null。</exception>
    public static IReadOnlyList<LootStack> Roll(LootTable table, IRandomSource rng)
    {
        if (table is null) throw new ArgumentNullException(nameof(table));
        if (rng is null) throw new ArgumentNullException(nameof(rng));

        if (table.Draws <= 0) return Array.Empty<LootStack>();

        // 只取本身合法的条目 —— 坏配置不产出垃圾，也不抛。
        var usable = new List<LootEntry>();
        foreach (var e in table.Entries)
        {
            if (e.IsWellFormed) usable.Add(e);
        }

        if (usable.Count == 0) return Array.Empty<LootStack>();

        double total = 0.0;
        foreach (var e in usable) total += e.Weight;
        if (total <= 0.0) return Array.Empty<LootStack>();

        var totals = new Dictionary<ItemId, long>(ItemIdComparer.Comparer);

        for (int draw = 0; draw < table.Draws; draw++)
        {
            var picked = Pick(usable, total, rng);
            if (picked is null) continue;

            var entry = picked.Value;
            long qty = QuantityFor(entry, rng);
            totals.TryGetValue(entry.Item, out long current);
            totals[entry.Item] = current + qty;
        }

        return totals
            .Select(kv => new LootStack(kv.Key, kv.Value))
            .OrderBy(s => s.Item.Value, StringComparer.Ordinal)
            .ToArray();
    }

    /// <summary>按权重抽一条。</summary>
    private static LootEntry? Pick(List<LootEntry> usable, double total, IRandomSource rng)
    {
        double roll = rng.NextDouble(total);
        double acc = 0.0;

        foreach (var e in usable)
        {
            acc += e.Weight;
            if (roll < acc) return e;
        }

        // 浮点累加的兜底：roll 恰好等于总和时（极少见）落到最后一条。
        return usable[^1];
    }

    /// <summary>在条目的数量区间里取一个数。</summary>
    private static long QuantityFor(LootEntry entry, IRandomSource rng)
    {
        long span = entry.MaxQuantity - entry.MinQuantity + 1;
        if (span <= 1) return entry.MinQuantity;

        // NextDouble(bound) 返回 [0, bound) 的 **double**（不是整数）——
        // 取整即落在 [0, span-1]。
        long offset = (long)rng.NextDouble((double)span);
        return entry.MinQuantity + offset;
    }
}
