using System;
using System.Collections.Generic;
using System.Linq;
using GdUnit4;
using ClaudeCodeGameStudios.Application;
using ClaudeCodeGameStudios.Domain.Heroes;
using ClaudeCodeGameStudios.Domain.Items;
using ClaudeCodeGameStudios.Domain.Settlement;

namespace ClaudeCodeGameStudios.Tests.Settlement;

/// <summary>
/// 「抽取 → 分配」整条管线的契约（R-05 与 R-01 的接缝）。
/// </summary>
/// <remarks>
/// <para><b>为什么要有这一组测试</b>：R-01 的测试守住「<b>分配</b>可复现」，
/// R-05 的测试守住「<b>抽取</b>可复现」—— 但两者<b>共用同一条随机子流</b>，
/// 顺序固定为「先抽后分」。那个接缝处<b>没有测试</b>：
/// 哪天有人把两段代码调换一下，抽取仍可复现、分配仍可复现，
/// <b>但整个副本的战利品会悄悄变成另一份</b>，而且不会以任何形式报错。</para>
///
/// <para><b>这些测试调的是 <see cref="LootPipeline"/> —— 与 <c>Main</c> 完全同一段代码</b>。
/// 若在测试里复制一份管线来测，就<b>盯不住 Main 的接线</b>：
/// Main 里那两行调换了，测试照样全绿。抽成函数后，改动必然被测出来。</para>
///
/// <para><b>用测试自带的表，不用游戏的默认表</b>：机制测试不该被数据调整误伤 ——
/// 二轮调掉落数值时，这些测试必须<b>照样绿</b>；
/// 而机制真变了（顺序、种子派生、随机消耗），它们必须<b>红</b>。</para>
/// </remarks>
[TestSuite]
public class LootPipelineTests
{
    private const string RunId = "d10.1.04a878d6f57f2546";
    private const string Dungeon = "北境旧堡";

    private static readonly HeroId[] Crew =
    {
        new("h-0"), new("h-1"), new("h-2"),
    };

    /// <summary>
    /// 测试自带的掉落表：<b>数量固定为 1、抽 2 次</b>。
    /// </summary>
    /// <remarks>
    /// <b>为什么数量固定</b>：数量区间若大于 1，抽取会额外消耗随机，测试就同时在测两件事。
    /// 固定成 1 让「抽取消耗几次随机」清清楚楚。
    /// <para>
    /// <b>为什么 2 次抽 / 3 个参战者</b>：总数 2 除不尽 3 → <b>必有余数</b> →
    /// 分配侧一定会消耗随机 → <b>接缝一定被踩到</b>。
    /// （第一版用 3 次抽，恰好整除，分配侧一次随机都没消耗 ——
    /// 那个测试因此是<b>假绿</b>：它红不是因为接缝坏，而是因为运气。）
    /// </para>
    /// </remarks>
    private static LootTable Table(int draws = 2) => new(
        Dungeon,
        draws,
        new[]
        {
            new LootEntry(new ItemId("石料"), 60.0, 1, 1),
            new LootEntry(new ItemId("木料"), 30.0, 1, 1),
            new LootEntry(new ItemId("布匹"), 10.0, 1, 1),
        });

    private static DungeonLootProvider Provider(int draws = 2)
        => new(new[] { Table(draws) }, ItemCatalog.Default, PriceBook.Default);

    /// <summary>把分配结果压成可比较的字符串（英雄|物品|数量，按发放顺序）。</summary>
    private static string Describe(LootAllocation allocation)
        => string.Join("; ", allocation.Grants.Select(g => $"{g.Hero.Value}|{g.Item.Value}|{g.Quantity}"));

    /// <summary>同一副本标识 → 两次跑出完全一样的结果（管线是纯函数）。</summary>
    [TestCase]
    public void Pipeline_SameRunId_ProducesIdenticalResult()
    {
        string first = Describe(LootPipeline.Run(Provider(), Dungeon, Crew, RunId));
        string second = Describe(LootPipeline.Run(Provider(), Dungeon, Crew, RunId));

        Assertions.AssertThat(first).IsEqual(second);
    }

    /// <summary>
    /// <b>黄金值</b>：固定副本标识 + 固定表 = 固定的掉落与分配。
    /// </summary>
    /// <remarks>
    /// 这条把「种子怎么派生、抽几次、抽完分配从流的哪一段开始」<b>全部钉死</b>：
    /// 调换顺序、多抽一次、抽法变了、种子派生变了 —— 任何一处都会改掉这个值。
    /// <para>
    /// 当前值 = 2 次抽中「布匹」「石料」，分给 3 人时**余数 2 随机**落在 h-0 / h-1，
    /// h-2 一件没得 —— 与 R-01 的 E05「少于参战人数时部分英雄什么都拿不到」一致。
    /// </para>
    /// </remarks>
    [TestCase]
    public void Pipeline_FixedRunId_HasGoldenResult()
    {
        Assertions.AssertThat(Describe(LootPipeline.Run(Provider(), Dungeon, Crew, RunId)))
            .IsEqual("h-0|布匹|1; h-1|石料|1");
    }

    /// <summary>
    /// <b>接缝敏感性的证明</b>：分配若改用一条全新的随机流，结果会变。
    /// </summary>
    /// <remarks>
    /// 证明上一条的黄金值<b>真的对顺序敏感</b>，不是恰好相同。
    /// 若哪天有人把管线改成「抽取一条流、分配另起一条」，分配结果就会与黄金值不同 ——
    /// 上一条会红。
    /// </remarks>
    [TestCase]
    public void Pipeline_FreshStreamForSplit_ChangesResult()
    {
        string shared = Describe(LootPipeline.Run(Provider(), Dungeon, Crew, RunId));

        // 错序：抽取用一条流，分配另起一条新的。
        var rng = new DecisionFlow.SeededRandom(LootSplit.DeriveSeed(RunId));
        var loot = Provider().LootFor(Dungeon, rng);
        string fresh = Describe(LootSplit.Split(loot, Crew, new DecisionFlow.SeededRandom(1)));

        Assertions.AssertThat(fresh).IsNotEqual(shared);
    }

    /// <summary>
    /// 抽取侧的消耗变化会<b>连锁</b>到分配侧 —— 这正是「共用一条流」的代价，
    /// 也是它必须被测试盯住的原因。
    /// </summary>
    [TestCase]
    public void Pipeline_ExtraDraw_ShiftsAllocation()
    {
        string twoDraws = Describe(LootPipeline.Run(Provider(2), Dungeon, Crew, RunId));
        string threeDraws = Describe(LootPipeline.Run(Provider(3), Dungeon, Crew, RunId));

        Assertions.AssertThat(threeDraws).IsNotEqual(twoDraws);
    }

    /// <summary>不同副本标识 → 不同结果（不是所有副本掉一样的东西）。</summary>
    [TestCase]
    public void Pipeline_DifferentRunIds_CanDiffer()
    {
        var a = LootPipeline.Run(Provider(), Dungeon, Crew, "d10.1.tokenA");
        var b = LootPipeline.Run(Provider(), Dungeon, Crew, "d10.2.tokenB");

        // 不强求一定不同（同种子偶然相同是可能的），但两者都必须合法。
        Assertions.AssertThat(a.Grants.All(g => g.Quantity > 0)).IsTrue();
        Assertions.AssertThat(b.Grants.All(g => g.Quantity > 0)).IsTrue();
    }

    /// <summary>无掉落 → 空分配（不发放）。</summary>
    [TestCase]
    public void Pipeline_NoLoot_ProducesEmptyAllocation()
    {
        var provider = new DungeonLootProvider(
            new[] { new LootTable(Dungeon, 0, Array.Empty<LootEntry>()) },
            ItemCatalog.Default, PriceBook.Default);

        var result = LootPipeline.Run(provider, Dungeon, Crew, RunId);

        Assertions.AssertThat(result.Grants.Count).IsEqual(0);
    }

    /// <summary>参战名单为空 → 空分配（E02：绝不退化成「全村都有份」）。</summary>
    [TestCase]
    public void Pipeline_NoParticipants_ProducesEmptyAllocation()
    {
        var result = LootPipeline.Run(Provider(), Dungeon, Array.Empty<HeroId>(), RunId);

        Assertions.AssertThat(result.Grants.Count).IsEqual(0);
    }
}
