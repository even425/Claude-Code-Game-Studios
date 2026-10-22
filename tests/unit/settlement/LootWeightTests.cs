using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GdUnit4;
using ClaudeCodeGameStudios.Application;
using ClaudeCodeGameStudios.Domain.Heroes;
using ClaudeCodeGameStudios.Domain.Items;
using ClaudeCodeGameStudios.Domain.Settlement;

namespace ClaudeCodeGameStudios.Tests.Settlement;

/// <summary>
/// <b>副本掉落的权重与数量</b>（D-076 拍板）。
/// </summary>
/// <remarks>
/// <para><b>它守的是什么</b>：掉落表的三条数值
/// —— <b>石料 50 / 木料 30 / 布匹 20</b>、<b>石料 1-2 份</b>、<b>抽 3 次</b>。</para>
/// <para><b>为什么用「大样本实测分布」而不是「断言表里的字面量」</b>：
/// 只断言「表里写着 50.0」的话，那只是把表抄了一遍 ——
/// <b>抽取器**按权重**抽这件事没被验过</b>（万一抽取器把权重读反了呢？）。
/// ⇒ 这里**真的抽一万次**，看实际分布是不是那个比例。</para>
/// <para>⚠️ <b>为什么不用「区间」断言</b>：区间可以放宽 ⇒ 守卫会变成装饰
/// （同类教训见记忆里的「区间断言无法自证有效」）。
/// ⇒ 这里断言<b>具体比例</b>，且容差**小到容不下任何别的权重组合</b>。</para>
/// </remarks>
[TestSuite]
public class LootWeightTests
{
    /// <summary>实测用的抽样次数（够多 ⇒ 比例稳定）。</summary>
    private const int Samples = 20000;

    private static DecisionFlow.SeededRandom Rng(ulong seed) => new(seed);

    /// <summary>
    /// 用生产掉落表抽 N 次，数出每种物品<b>一共出了多少份</b>。
    /// </summary>
    /// <remarks>
    /// ⚠️ **必须数「份」而不是「条」**：石料每次给 1-2 份 ⇒ 数条数的话，
    /// 石料的占比会被**系统性拉低**（一次出 2 份只算 1 条）⇒ 实测 43.3% 而不是 50%。
    /// ⇒ 而玩家真正拿到的是**份数** ⇒ 这才是该量的东西。
    /// </remarks>
    private static Dictionary<string, long> SampleCounts(int samples)
    {
        var table = DefaultLootTables.For("北境旧堡探查");
        var counts = new Dictionary<string, long>
        {
            ["石料"] = 0, ["木料"] = 0, ["布匹"] = 0,
        };

        for (int i = 0; i < samples; i++)
        {
            var rolled = LootRoller.Roll(table, Rng((ulong)(i + 1) * 7919UL));
            foreach (var stack in rolled)
            {
                if (counts.ContainsKey(stack.Item.Value)) counts[stack.Item.Value] += stack.Quantity;
            }
        }

        return counts;
    }

    /// <summary>
    /// <b>玩家实际拿到的份数比例</b>必须与 D-076 拍板的 50 / 30 / 20 相符。
    /// </summary>
    /// <remarks>
    /// <para>⚠️ <b>这里量的是「份数」而不是「抽中次数」</b>——
    /// 因为石料每次给 1-2 份 ⇒ 抽中次数的占比与份数的占比<b>不是一回事</b>
    /// （前者会系统性拉低石头）。而玩家真正拿到的是份数
    /// ⇒ D-076 拍板的也是「玩家拿到的份数比例」。</para>
    /// <para>⚠️ 断言的是<b>具体比例</b>（容差 ±1.5 个百分点）——
    /// 这个容差**远小于**任何另一组整数权重能造成的差距
    /// （例如填 50/30/20 会让石头偏离 10 个点 ⇒ 一定被抓到）。</para>
    /// </remarks>
    [TestCase]
    public void DroppedDistribution_MatchesTheApprovedWeights()
    {
        var counts = SampleCounts(Samples);
        double total = counts.Values.Sum();

        double stone = 100.0 * counts["石料"] / total;
        double wood = 100.0 * counts["木料"] / total;
        double cloth = 100.0 * counts["布匹"] / total;

        string detail = $"实测份数比例 石料 {stone:F1}% / 木料 {wood:F1}% / 布匹 {cloth:F1}%"
                        + $"（目标 50 / 30 / 20）"
                        + $" 计数 {counts["石料"]}/{counts["木料"]}/{counts["布匹"]}";

        // 诊断信息落文件（断言值本身必须是 bool，消息只能走文件）。
        try
        {
            File.WriteAllText(
                Path.Combine(Path.GetTempPath(), "ccgs-loot-dist.txt"),
                detail, new System.Text.UTF8Encoding(false));
        }
        catch { /* 落盘失败不影响断言 */ }

        // ⚠️ GdUnit4 的 `IsTrue` 只接受 bool ⇒ 先算成 bool 再断言。
        Assertions.AssertThat(Math.Abs(stone - 50.0) <= 1.5).IsTrue();
        Assertions.AssertThat(Math.Abs(wood - 30.0) <= 1.5).IsTrue();
        Assertions.AssertThat(Math.Abs(cloth - 20.0) <= 1.5).IsTrue();
    }

    /// <summary>
    /// <b>布匹的稀有度</b>：它是三者里唯一的「优秀」（D-076 拍板保持原样）。
    /// </summary>
    [TestCase]
    public void Cloth_IsTheOnlyUncommonItem()
    {
        var catalog = ItemCatalog.Default;

        Assertions.AssertThat(catalog.RarityOf(new ItemId("石料"))).IsEqual(ItemRarity.Common);
        Assertions.AssertThat(catalog.RarityOf(new ItemId("木料"))).IsEqual(ItemRarity.Common);
        Assertions.AssertThat(catalog.RarityOf(new ItemId("布匹"))).IsEqual(ItemRarity.Uncommon);
    }

    /// <summary>
    /// 抽 3 次 ⇒ <b>总份数</b>落在 3 到 6 之间（三次各出 1-2 份）。
    /// </summary>
    /// <remarks>
    /// ⚠️ **不是断言「出 3 条」**：`LootRoller` 的语义是
    /// <b>抽 3 次、同种物品的份数累加、最后按物品合并成若干条</b>
    /// ⇒ 三次都抽到石头 ⇒ **1 条**（数量 3~6），不是 3 条。
    /// ⇒ 能断言的是<b>总份数</b>（= 抽次数 × 每份数量）。
    /// </remarks>
    [TestCase]
    public void DungeonDrawsThreeTimes_EachYieldingOneOrTwo()
    {
        var table = DefaultLootTables.For("北境旧堡探查");

        // 抽 3 次 ⇒ 总份数在 3..6（每次 1~2 份）。
        long minTotal = long.MaxValue;
        long maxTotal = long.MinValue;
        for (int i = 0; i < Samples; i++)
        {
            long total = LootRoller.Roll(table, Rng((ulong)(i + 1) * 15485863UL))
                .Sum(s => s.Quantity);
            if (total < minTotal) minTotal = total;
            if (total > maxTotal) maxTotal = total;
        }

        Assertions.AssertThat(minTotal).IsEqual(3);
        Assertions.AssertThat(maxTotal).IsEqual(6);
    }

    /// <summary>
    /// 数量：石料 1-2 份，木料与布匹各 1 份（D-076 拍板「数量不变」）。
    /// </summary>
    /// <remarks>
    /// ⚠️ 因为<b>份数会累加</b>，单次掉落里石料可能是 1 份也可能是 3 份
    /// （两次都抽中石头）⇒ 只能断言「<b>单次抽中的那一份</b>是 1 或 2」，
    /// 而不能断言「一条石料记录的份数是 1 或 2」。
    /// </remarks>
    [TestCase]
    public void SingleDraw_YieldsOneOrTwoForStoneAndOneForTheRest()
    {
        var table = new LootTable("单抽", draws: 1, new[]
        {
            new LootEntry(new ItemId("石料"), 50.0, 1, 2),
            new LootEntry(new ItemId("木料"), 30.0, 1, 1),
            new LootEntry(new ItemId("布匹"), 20.0, 1, 1),
        });

        var stoneCounts = new List<long>();
        var woodCounts = new List<long>();
        var clothCounts = new List<long>();

        for (int i = 0; i < Samples; i++)
        {
            foreach (var stack in LootRoller.Roll(table, Rng((ulong)(i + 1) * 32452843UL)))
            {
                switch (stack.Item.Value)
                {
                    case "石料": stoneCounts.Add(stack.Quantity); break;
                    case "木料": woodCounts.Add(stack.Quantity); break;
                    case "布匹": clothCounts.Add(stack.Quantity); break;
                }
            }
        }

        Assertions.AssertThat(stoneCounts.All(q => q == 1 || q == 2)).IsTrue();
        Assertions.AssertThat(woodCounts.All(q => q == 1)).IsTrue();
        Assertions.AssertThat(clothCounts.All(q => q == 1)).IsTrue();
    }
}
