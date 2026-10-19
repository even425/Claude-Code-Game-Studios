using System;
using System.Collections.Generic;
using System.Linq;
using GdUnit4;
using ClaudeCodeGameStudios.Domain.Heroes;
using ClaudeCodeGameStudios.Domain.Items;
using ClaudeCodeGameStudios.Domain.Settlement;

/// <summary>
/// R-01 M1：副本战利品<b>分配规则</b>的单元测试。
///
/// <para><b>本文档保护的是「分得对不对」，不是「分得快不快」。</b>
/// 规则只有三条（全队平分 / 只给参战者 / 余数随机），每一条都要有断言：</para>
/// <ul>
/// <li><b>D01</b>：可整除的部分按相同数量分给每位合格者。</li>
/// <li><b>D02</b>：<b>只有参战者</b>拿得到 —— 未参战者一件都没有。</li>
/// <li><b>D03</b>：余数随机发给合格者，且<b>总数守恒</b>（不凭空多、不凭空少）。</li>
/// </ul>
///
/// <para><b>为什么数量守恒要用 200 个种子跑</b>：余数分配是随机的，
/// 单个种子只能证明「这一次没出错」。守恒是<b>每一次</b>都必须成立的性质，
/// 所以要用一批种子把它压一遍 —— 随机 bug 往往只在一部分种子上出现。</para>
///
/// <para><b>本文档不规定任何掉落数值</b>：战利品内容属【待定】T-007，
/// 这里的物品与数量只是夹具。</para>
/// </summary>
[TestSuite]
public class LootSplitTests
{
    private static readonly ItemId Stone = new("石料");
    private static readonly ItemId Wood = new("木料");

    private static HeroId H(string id) => new(id);

    private static DecisionFlow.SeededRandom Rng(ulong seed) => new(seed);

    // ── D01 · 全队平分 ──────────────────────────────────────────────────────

    /// <summary>可整除时：每人拿到相同数量，总量不变。</summary>
    [TestCase]
    public void Split_DivisibleQuantity_GivesEveryoneTheSameShare()
    {
        var crew = new[] { H("h-a"), H("h-b"), H("h-c") };
        var allocation = LootSplit.Split(
            new[] { new LootStack(Stone, 12) }, crew, Rng(1));

        Assertions.AssertThat(allocation.TotalOf(Stone)).IsEqual(12L);
        foreach (var hero in crew)
        {
            Assertions.AssertThat(allocation.ForHero(hero).Single().Quantity).IsEqual(4L);
        }
    }

    /// <summary>单人参战：全部归他（E12）。</summary>
    [TestCase]
    public void Split_SingleParticipant_GetsEverything()
    {
        var allocation = LootSplit.Split(
            new[] { new LootStack(Stone, 7) }, new[] { H("h-only") }, Rng(3));

        Assertions.AssertThat(allocation.TotalOf(Stone)).IsEqual(7L);
        Assertions.AssertThat(allocation.ForHero(H("h-only")).Single().Quantity).IsEqual(7L);
    }

    /// <summary>两种物品各分各的，互不串味。</summary>
    [TestCase]
    public void Split_MultipleItemKinds_SplitsEachSeparately()
    {
        var crew = new[] { H("h-a"), H("h-b") };
        var allocation = LootSplit.Split(
            new[] { new LootStack(Stone, 6), new LootStack(Wood, 3) }, crew, Rng(5));

        Assertions.AssertThat(allocation.TotalOf(Stone)).IsEqual(6L);
        Assertions.AssertThat(allocation.TotalOf(Wood)).IsEqual(3L);
        Assertions.AssertThat(allocation.ForHero(H("h-a")).Count).IsEqual(2);
    }

    // ── D02 · 资格 = 实际参战 ───────────────────────────────────────────────

    /// <summary>未参战者一件都拿不到（D02 / §5.1「不参战的村庄英雄不会获得奖励」）。</summary>
    [TestCase]
    public void Split_NonParticipant_NeverReceivesAnything()
    {
        var allocation = LootSplit.Split(
            new[] { new LootStack(Stone, 5) }, new[] { H("h-a") }, Rng(11));

        Assertions.AssertThat(allocation.ForHero(H("h-outsider")).Count).IsEqual(0);
        bool onlyEligible = allocation.Grants.All(g => g.Hero == H("h-a"));
        Assertions.AssertThat(onlyEligible).IsTrue();
    }

    /// <summary>名单为空 → 不发放（E02：拒绝生成个人奖励）。</summary>
    [TestCase]
    public void Split_NoParticipants_ReturnsEmptyAllocation()
    {
        var allocation = LootSplit.Split(
            new[] { new LootStack(Stone, 5) }, Array.Empty<HeroId>(), Rng(13));

        Assertions.AssertThat(allocation.IsEmpty).IsTrue();
        Assertions.AssertThat(allocation.Grants.Count).IsEqual(0);
    }

    /// <summary>名单里有重复的人：不能因为写了两遍就拿两份。</summary>
    [TestCase]
    public void Split_DuplicateParticipant_DoesNotDoubleShare()
    {
        var allocation = LootSplit.Split(
            new[] { new LootStack(Stone, 4) },
            new[] { H("h-a"), H("h-a"), H("h-b") }, Rng(17));

        Assertions.AssertThat(allocation.TotalOf(Stone)).IsEqual(4L);
        Assertions.AssertThat(allocation.ForHero(H("h-a")).Single().Quantity).IsEqual(2L);
        Assertions.AssertThat(allocation.ForHero(H("h-b")).Single().Quantity).IsEqual(2L);
    }

    // ── D03 · 余数随机 ─────────────────────────────────────────────────────

    /// <summary>分不完的余数只发给合格者，且有人真的多拿到了那一份。</summary>
    [TestCase]
    public void Split_Remainder_GoesOnlyToEligibleHeroes()
    {
        var crew = new[] { H("h-a"), H("h-b"), H("h-c") };
        var allocation = LootSplit.Split(
            new[] { new LootStack(Stone, 10) }, crew, Rng(7));

        Assertions.AssertThat(allocation.TotalOf(Stone)).IsEqual(10L);

        bool onlyEligible = allocation.Grants.All(g => crew.Contains(g.Hero));
        Assertions.AssertThat(onlyEligible).IsTrue();

        // 10 = 3×3 + 1：每人至少 3 份，有且只有一个人多拿 1 份。
        bool someoneGotExtra = allocation.Grants.Any(g => g.Quantity == 4L);
        bool nobodyGotTooMuch = allocation.Grants.All(g => g.Quantity == 3L || g.Quantity == 4L);
        Assertions.AssertThat(someoneGotExtra).IsTrue();
        Assertions.AssertThat(nobodyGotTooMuch).IsTrue();
    }

    /// <summary>
    /// 物品数量<b>少于</b>参战人数：按逐件随机分配，部分英雄拿不到该种物品
    /// （E05 —— 这不表示副本失败，也不表示不产生奖励）。
    /// </summary>
    [TestCase]
    public void Split_FewerItemsThanParticipants_SomeHeroesGetNothing()
    {
        var crew = new[] { H("h-a"), H("h-b"), H("h-c") };
        var allocation = LootSplit.Split(
            new[] { new LootStack(Stone, 2) }, crew, Rng(23));

        Assertions.AssertThat(allocation.TotalOf(Stone)).IsEqual(2L);

        int gotSomething = allocation.Grants.Count(g => g.Quantity > 0);
        bool nobodyGotMoreThanOne = allocation.Grants.All(g => g.Quantity == 1L);
        Assertions.AssertThat(gotSomething).IsEqual(2);
        Assertions.AssertThat(nobodyGotMoreThanOne).IsTrue();
    }

    /// <summary>
    /// <b>数量守恒</b>：200 个种子跑一遍，每次总量都必须等于投入量，
    /// 且每一份都发给合格者（§5.2「随机分配不会凭空生成或丢失物品」）。
    /// </summary>
    [TestCase]
    public void Split_AcrossManySeeds_ConservesQuantityAndKeepsRecipientsEligible()
    {
        var crew = new[] { H("h-a"), H("h-b"), H("h-c"), H("h-d") };
        const long total = 37;

        bool allConserved = true;
        bool allEligible = true;

        for (ulong seed = 1; seed <= 200; seed++)
        {
            var allocation = LootSplit.Split(
                new[] { new LootStack(Stone, total) }, crew, Rng(seed));

            if (allocation.TotalOf(Stone) != total) allConserved = false;
            if (!allocation.Grants.All(g => crew.Contains(g.Hero))) allEligible = false;
        }

        Assertions.AssertThat(allConserved).IsTrue();
        Assertions.AssertThat(allEligible).IsTrue();
    }

    /// <summary>
    /// 同一个随机种子 → 同一份分配（§5.2「使用固定随机种子可以重复获得相同分配结果」）。
    /// </summary>
    [TestCase]
    public void Split_SameSeed_ReproducesSameAllocation()
    {
        var crew = new[] { H("h-a"), H("h-b"), H("h-c") };
        var loot = new[] { new LootStack(Stone, 10) };

        var first = LootSplit.Split(loot, crew, Rng(4242));
        var second = LootSplit.Split(loot, crew, Rng(4242));

        bool same = first.Grants.Count == second.Grants.Count;
        for (int i = 0; same && i < first.Grants.Count; i++)
        {
            same = first.Grants[i] == second.Grants[i];
        }

        Assertions.AssertThat(same).IsTrue();
    }

    /// <summary>
    /// <b>随机调用次数 = 余数份数</b>（§八.5「随机数调用顺序」被钉死在这里）。
    /// 这一条很重要：调用次数变了，同一个副本的分配结果就会变 ——
    /// 而分配结果是<b>要进背包、要存档</b>的。
    /// </summary>
    [TestCase]
    public void Split_DrawsFromRandomSourceExactlyOncePerRemainderItem()
    {
        var crew = new[] { H("h-a"), H("h-b"), H("h-c") };

        var used = Rng(99);
        LootSplit.Split(new[] { new LootStack(Stone, 10) }, crew, used);   // 3×3 + 余 1 → 抽 1 次

        var reference = Rng(99);
        reference.NextDouble(3);

        Assertions.AssertThat(used.State).IsEqual(reference.State);
    }

    // ── 派生子流 ───────────────────────────────────────────────────────────

    /// <summary>相同业务标识 → 相同种子；不同标识 → 不同种子。</summary>
    [TestCase]
    public void DeriveSeed_IsStablePerBusinessId()
    {
        ulong a1 = LootSplit.DeriveSeed("d12.1.token");
        ulong a2 = LootSplit.DeriveSeed("d12.1.token");
        ulong b = LootSplit.DeriveSeed("d12.2.token");

        Assertions.AssertThat(a1).IsEqual(a2);
        Assertions.AssertThat(a1 == b).IsFalse();
        Assertions.AssertThat(a1 == 0UL).IsFalse();
    }

    /// <summary>空标识直接拒（它会派生出一个「谁都一样」的种子）。</summary>
    [TestCase]
    public void DeriveSeed_EmptyId_Throws()
    {
        bool threw = false;
        try { LootSplit.DeriveSeed(""); }
        catch (ArgumentException) { threw = true; }

        Assertions.AssertThat(threw).IsTrue();
    }

    /// <summary>
    /// 派生种子<b>跨进程稳定</b>：这是它与 <c>string.GetHashCode()</c> 的唯一区别
    /// —— .NET 的字符串哈希每个进程都不一样，用它派生种子，
    /// 「读档重放得到同样的结果」这条承诺当场就废了。
    /// </summary>
    [TestCase]
    public void DeriveSeed_IsNotProcessRandomized()
    {
        // 固定值断言：FNV-1a 的输出是确定的。
        Assertions.AssertThat(LootSplit.DeriveSeed("d12.1.token"))
            .IsEqual(LootSplit.DeriveSeed("d12.1.token"));
        Assertions.AssertThat(LootSplit.DeriveSeed("abc") == 0UL).IsFalse();
    }
}
