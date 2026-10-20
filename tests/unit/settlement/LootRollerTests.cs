using System;
using System.Collections.Generic;
using System.Linq;
using GdUnit4;
using ClaudeCodeGameStudios.Domain.Heroes;
using ClaudeCodeGameStudios.Domain.Items;
using ClaudeCodeGameStudios.Domain.Settlement;

namespace ClaudeCodeGameStudios.Tests.Settlement;

/// <summary>
/// 掉落表权重抽取的契约（R-05）。
/// </summary>
/// <remarks>
/// <para><b>为什么测它</b>：抽掉落是「凭空造物」的那一步 ——
/// 抽多了会凭空多出物品，抽少了会凭空消失。
/// 数量守恒与可复现是这里最贵的两条。</para>
/// </remarks>
[TestSuite]
public class LootRollerTests
{
    private static readonly ItemId Stone = new("石料");
    private static readonly ItemId Cloth = new("布匹");

    private static DecisionFlow.SeededRandom Rng(ulong seed) => new(seed);

    private static LootTable Table(int draws, params LootEntry[] entries)
        => new("北境旧堡", draws, entries);

    [TestCase]
    public void Roll_FixedSeed_IsReproducible()
    {
        var table = Table(5, new LootEntry(Stone, 60.0, 1, 3), new LootEntry(Cloth, 40.0, 1, 2));

        var a = LootRoller.Roll(table, Rng(12345));
        var b = LootRoller.Roll(table, Rng(12345));

        Assertions.AssertThat(a.Count).IsEqual(b.Count);
        for (int i = 0; i < a.Count; i++)
        {
            Assertions.AssertThat(a[i].Item).IsEqual(b[i].Item);
            Assertions.AssertThat(a[i].Quantity).IsEqual(b[i].Quantity);
        }
    }

    [TestCase]
    public void Roll_DifferentSeeds_CanDiffer()
    {
        var table = Table(5, new LootEntry(Stone, 60.0, 1, 3), new LootEntry(Cloth, 40.0, 1, 2));

        var a = LootRoller.Roll(table, Rng(1));
        var b = LootRoller.Roll(table, Rng(999));

        // 不是断言"一定不同"（那可能偶然相同），而是断言两者都合法。
        Assertions.AssertThat(a.All(s => s.Quantity >= 1)).IsTrue();
        Assertions.AssertThat(b.All(s => s.Quantity >= 1)).IsTrue();
    }

    [TestCase]
    public void Roll_OnlyProducesItemsFromPool()
    {
        var table = Table(20, new LootEntry(Stone, 60.0, 1, 2), new LootEntry(Cloth, 40.0, 1, 1));

        var result = LootRoller.Roll(table, Rng(7));

        foreach (var stack in result)
        {
            Assertions.AssertThat(stack.Item == Stone || stack.Item == Cloth).IsTrue();
        }
    }

    [TestCase]
    public void Roll_NoDraws_ProducesNothing()
    {
        var table = Table(0, new LootEntry(Stone, 60.0, 1, 2));

        var result = LootRoller.Roll(table, Rng(3));

        Assertions.AssertThat(result.Count).IsEqual(0);
    }

    [TestCase]
    public void Roll_EmptyPool_ProducesNothing()
    {
        var result = LootRoller.Roll(Table(3), Rng(3));

        Assertions.AssertThat(result.Count).IsEqual(0);
    }

    /// <summary>坏条目（权重 0 / 数量非法）被跳过，<b>不产出垃圾</b>。</summary>
    [TestCase]
    public void Roll_SkipsMalformedEntries()
    {
        var table = Table(5,
            new LootEntry(Stone, 0.0, 1, 1),      // 权重 0
            new LootEntry(Cloth, 10.0, 0, 1),     // 数量 0
            new LootEntry(Stone, 10.0, 1, 1));     // 唯一可用

        var result = LootRoller.Roll(table, Rng(11));

        Assertions.AssertThat(result.Count).IsEqual(1);
        Assertions.AssertThat(result[0].Item).IsEqual(Stone);
        Assertions.AssertThat(result[0].Quantity).IsEqual(5L);
    }

    /// <summary>只有一条时，每次都该抽中它 —— 数量守恒可精确断言。</summary>
    [TestCase]
    public void Roll_SingleEntry_AlwaysPicksIt()
    {
        var table = Table(4, new LootEntry(Stone, 10.0, 1, 1));

        var result = LootRoller.Roll(table, Rng(5));

        Assertions.AssertThat(result.Count).IsEqual(1);
        Assertions.AssertThat(result[0].Quantity).IsEqual(4L);
    }

    [TestCase]
    public void Roll_QuantityStaysInRange()
    {
        var table = Table(50, new LootEntry(Stone, 50.0, 2, 4), new LootEntry(Cloth, 50.0, 1, 1));

        var result = LootRoller.Roll(table, Rng(2024));

        foreach (var stack in result)
        {
            if (stack.Item == Stone) Assertions.AssertThat(stack.Quantity).IsGreaterEqual(2L);
            if (stack.Item == Cloth) Assertions.AssertThat(stack.Quantity).IsGreaterEqual(1L);
        }
    }

    /// <summary>同一物品被抽中多次 → <b>合并成一条</b>，总份数守恒。</summary>
    [TestCase]
    public void Roll_MergesSameItem_AndConservesQuantity()
    {
        var table = Table(8, new LootEntry(Stone, 100.0, 1, 1));

        var result = LootRoller.Roll(table, Rng(42));

        Assertions.AssertThat(result.Count).IsEqual(1);
        Assertions.AssertThat(result[0].Quantity).IsEqual(8L);
    }

    /// <summary>
    /// <b>统计断言</b>：权重高的条目必须明显更常被抽中。
    /// </summary>
    /// <remarks>
    /// <b>为什么不能只测「同种子同结果」</b>：抽样恒返回第一项时，
    /// 可复现性测试<b>照样通过</b> —— 而游戏里的掉落已经永远固定成那一样东西。
    /// 这条断言抓的是「权重到底有没有起作用」。
    /// </remarks>
    [TestCase]
    public void Roll_HeavierWeight_IsPickedMoreOften()
    {
        var table = Table(1, new LootEntry(Stone, 90.0, 1, 1), new LootEntry(Cloth, 10.0, 1, 1));

        int heavy = 0;
        const int rounds = 400;

        for (int seed = 1; seed <= rounds; seed++)
        {
            var result = LootRoller.Roll(table, Rng((ulong)seed));
            if (result.Count > 0 && result[0].Item == Stone) heavy++;
        }

        // 90% 权重：400 次里至少 300 次命中重物（留足余量，不卡边界）。
        Assertions.AssertThat(heavy).IsGreaterEqual(300);
    }
}
