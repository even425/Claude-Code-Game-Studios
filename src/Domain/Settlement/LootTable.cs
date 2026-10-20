using System;
using System.Collections.Generic;
using System.Linq;
using ClaudeCodeGameStudios.Domain.Items;

namespace ClaudeCodeGameStudios.Domain.Settlement;

/// <summary>掉落表里的一条：<b>什么东西、多大概率、多少个</b>（R-05）。</summary>
/// <param name="Item">物品标识（必须已在 <c>ItemCatalog</c> 登记）。</param>
/// <param name="Weight">
/// 权重。<b>任意正数</b> —— 表内权重<b>归一化</b>，不要求总和是 100 或 1。
/// </param>
/// <param name="MinQuantity">数量下限（含）。</param>
/// <param name="MaxQuantity">数量上限（含）。</param>
public readonly record struct LootEntry(
    ItemId Item, double Weight, long MinQuantity, long MaxQuantity)
{
    /// <summary>本条本身是否合法（权重为正、数量为正且区间不倒）。</summary>
    public bool IsWellFormed =>
        !string.IsNullOrEmpty(Item.Value)
        && double.IsFinite(Weight) && Weight > 0.0
        && MinQuantity >= 1
        && MaxQuantity >= MinQuantity;
}

/// <summary>
/// 一个副本的<b>掉落表</b>（R-05）。
/// </summary>
/// <remarks>
/// <para><b>它是一份配置，不是规则</b>：本类只声明「这个副本抽几次、池子里有什么」，
/// 抽不抽、抽中什么，由 <see cref="LootRoller"/> 按权重算；
/// 配置本身对不对，由 <see cref="LootTableValidator"/> 判。
/// 两者分开，是为了让「配错了」和「抽到了」是两件能分别测的事。</para>
///
/// <para><b>构造期不抛</b>：本类<b>刻意不做构造期防线</b>（与
/// <see cref="Stall"/> / <see cref="PriceBook"/> 的做法相反）。
/// 理由：掉落表是<b>策划写出来的配置</b>，配错了应该由
/// <b>验证器</b>报一条明确的错，让开发者看见 —— 而不是让游戏在启动时崩掉。
/// 代价是本类允许存在「权重为 0」「数量为 -1」这种坏数据，
/// 所以<b>用掉落表之前必须先过验证器</b>。</para>
///
/// <para><b>权重归一化</b>：表内权重之和可以是任意正数，抽取时按比例折算。
/// 策划写「石料 60、木料 30、布匹 10」和写「石料 6、木料 3、布匹 1」效果一样。</para>
/// </remarks>
public sealed class LootTable
{
    private readonly List<LootEntry> _entries;

    /// <summary>构造一张掉落表。</summary>
    /// <param name="dungeonName">副本名称（与 <c>ILootProvider.LootFor</c> 的入参对应）。</param>
    /// <param name="draws">抽取次数；0 = 这个副本不掉落任何东西。</param>
    /// <param name="entries">掉落池条目。</param>
    /// <exception cref="ArgumentException"><paramref name="dungeonName"/> 为空。</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="draws"/> 为负。</exception>
    public LootTable(string dungeonName, int draws, IEnumerable<LootEntry> entries)
    {
        if (string.IsNullOrEmpty(dungeonName))
        {
            throw new ArgumentException("副本名称不得为空。", nameof(dungeonName));
        }

        if (draws < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(draws), draws, "抽取次数不得为负。");
        }

        DungeonName = dungeonName;
        Draws = draws;
        _entries = (entries ?? Enumerable.Empty<LootEntry>()).ToList();
    }

    /// <summary>这份表属于哪个副本。</summary>
    public string DungeonName { get; }

    /// <summary>抽取次数。</summary>
    public int Draws { get; }

    /// <summary>掉落池条目（只读快照）。</summary>
    public IReadOnlyList<LootEntry> Entries => _entries;

    /// <summary>这份表里所有条目的权重之和（用于诊断与展示）。</summary>
    public double TotalWeight => _entries.Sum(e => double.IsFinite(e.Weight) ? e.Weight : 0.0);
}
