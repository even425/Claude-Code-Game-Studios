using System.Collections.Generic;
using GdUnit4;
using ClaudeCodeGameStudios.Domain.Items;
using ClaudeCodeGameStudios.Domain.Village;
using ClaudeCodeGameStudios.Presentation;

namespace ClaudeCodeGameStudios.Tests.UI;

/// <summary>
/// 花名册行尾经济摘要的契约（第四阶段 M5）。
/// </summary>
/// <remarks>
/// <para><b>为什么测它</b>：这是玩家核对「买卖双方货币和背包变化」的唯一界面。
/// 它必须稳定（同一状态每次同样的字），且不能因为背包种类多就把整行撑到被裁掉。</para>
/// <para><b>宽度纪律</b>：左栏 248 像素；背包最多列两种，其余只报种数。</para>
/// </remarks>
[TestSuite]
public class EconomySummaryTests
{
    private static readonly ResourceId Gold = new("金币");

    /// <summary>造一个指定金币与背包的英雄。</summary>
    private static HeroAgent Hero(long gold, params (string name, long count)[] items)
    {
        var hero = new HeroAgent { Name = "甲" };
        hero.RestoreWallet(new Wallet(new Dictionary<ResourceId, long> { { Gold, gold } }));

        var bag = new Inventory();
        foreach (var (name, count) in items)
        {
            bag = bag.TryAdd(new ItemId(name), count)!;
        }

        hero.RestoreBag(bag);
        return hero;
    }

    [TestCase]
    public void GoldOnly_RendersWithoutTrailingSpace()
    {
        var hero = Hero(100);

        Assertions.AssertThat(EconomySummary.For(hero, Gold)).IsEqual("金100");
        Assertions.AssertThat(EconomySummary.BagText(hero.Bag)).IsEqual(string.Empty);
    }

    [TestCase]
    public void GoldAndOneItem_ShowsBoth()
    {
        var hero = Hero(95, ("石料", 1));

        Assertions.AssertThat(EconomySummary.For(hero, Gold)).IsEqual("金95 石料×1");
    }

    [TestCase]
    public void ZeroGold_IsStillShown()
    {
        var hero = Hero(0);

        Assertions.AssertThat(EconomySummary.For(hero, Gold)).IsEqual("金0");
    }

    /// <summary>
    /// 物品按 Id 排序（Inventory.Snapshot 的稳定顺序），最多列两个，其余只报种数。
    /// 「布匹」&lt;「木料」&lt;「石料」（Ordinal），所以前两个是布匹、木料。
    /// </summary>
    [TestCase]
    public void MoreThanTwoBagKinds_CappedWithCount()
    {
        var hero = Hero(12, ("石料", 2), ("木料", 1), ("布匹", 3));

        Assertions.AssertThat(EconomySummary.For(hero, Gold))
            .IsEqual("金12 布匹×3 木料×1 +1");
    }

    [TestCase]
    public void MaxKindsBelowOne_IsClampedToOne()
    {
        var hero = Hero(0, ("石料", 2), ("木料", 1), ("布匹", 3));

        Assertions.AssertThat(EconomySummary.For(hero, Gold, maxBagKinds: 0))
            .IsEqual("金0 布匹×3 +2");
    }
}
