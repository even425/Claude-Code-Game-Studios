using System;
using System.Collections.Generic;
using System.Linq;
using GdUnit4;
using ClaudeCodeGameStudios.Domain.Heroes;

namespace ClaudeCodeGameStudios.Tests.Heroes;

/// <summary>
/// <b>召唤池</b>：世界按<b>权重</b>抽（D-078）。
/// </summary>
/// <remarks>
/// <para><b>它守的是两件事</b>：
/// <list type="number">
/// <item><b>实际抽到的世界分布</b>与权重相符（不是「每个人都能抽到每个世界」）。</item>
/// <item><b>召唤池不分区</b>：六个世界一起抽（D-078 拍板）⇒ 没有任何一个世界被排除。</item>
/// </list>
/// <para>⚠️ <b>为什么必须实测</b>：权重写在表里，但「抽取器按权重抽」这件事
/// <b>没有任何守卫验过</b> ⇒ 若抽取器忽略了权重（每个世界等概率），
/// 表里的权重就成了一句没人看的话。</para>
/// <para>⚠️ <b>为什么不用「区间」断言</b>：区间可被放宽 ⇒ 守卫会变装饰
/// ⇒ 这里断言<b>具体比例</b>，容差远小于任何别的权重组合能造成的差距。</para>
/// </remarks>
[TestSuite]
public class SummonPoolTests
{
    /// <summary>实测用的召唤次数（够多 ⇒ 比例稳定）。</summary>
    private const int Samples = 20000;

    /// <summary>用固定种子抽 N 次，数出每个世界出现了多少次。</summary>
    private static Dictionary<string, int> SampleWorlds(int samples, ulong seed)
    {
        HeroFactory.Seed(seed);
        var counts = new Dictionary<string, int>();
        for (int i = 0; i < samples; i++)
        {
            var s = HeroFactory.Summon();
            counts[s.WorldName] = counts.TryGetValue(s.WorldName, out var n) ? n + 1 : 1;
        }
        return counts;
    }

    /// <summary>
    /// <b>实际分布与权重相符</b> —— 权重真的在起作用。
    /// </summary>
    [TestCase]
    public void WorldDistribution_MatchesTheConfiguredWeights()
    {
        var worlds = HeroFactory.World.All;
        var counts = SampleWorlds(Samples, seed: 20261022UL);

        var problems = new List<string>();
        foreach (var w in worlds)
        {
            int total = worlds.Sum(x => x.Weight);
            double expected = 100.0 * w.Weight / total;
            int actual = counts.TryGetValue(w.Name, out var n) ? n : 0;
            double rate = 100.0 * actual / Samples;

            // 容差 ±1.2 个百分点：远小于任何另一组整数权重能造成的差距
            // （权重总和约 22 ⇒ 改 1 个权重单位就是 4.5 个点）。
            if (Math.Abs(rate - expected) > 1.2)
            {
                problems.Add($"{w.Name}：期望 {expected:F1}%（权重 {w.Weight}），实测 {rate:F1}%");
            }
        }

        Assertions.AssertThat(string.Join(" | ", problems)).IsEqual(string.Empty);
    }

    /// <summary>
    /// <b>召唤池不分区</b>：六个世界<b>都</b>能被抽到（D-078）。
    /// </summary>
    /// <remarks>
    /// ⚠️ 为什么要单独断言：若将来有人把召唤池按村庄进度分区
    /// （基线 §10.5 建议过「经营 → 英雄」的联动），
    /// 那就是<b>规则变了</b> ⇒ 必须有人拍板 ⇒ 守卫先红。
    /// </remarks>
    [TestCase]
    public void EveryWorld_CanBeSummoned()
    {
        var counts = SampleWorlds(Samples, seed: 777777UL);

        var missing = HeroFactory.World.All
            .Where(w => !counts.ContainsKey(w.Name))
            .Select(w => w.Name)
            .ToArray();

        Assertions.AssertThat(string.Join(" | ", missing)).IsEqual(string.Empty);
    }

    /// <summary>
    /// <b>同一种子 ⇒ 同样的召唤序列</b>（可复现，基线 §14.2）。
    /// </summary>
    /// <remarks>
    /// ⚠️ 这条守的是「召唤用<b>另一个</b>随机源」（<c>HeroFactory</c> 自己的），
    /// 与游戏主随机流无关 ⇒ 否则「固定种子」会漏掉英雄本身的变化
    /// （曾经踩过：两次跑的英雄不同 ⇒ 采集数据依然不可复现）。
    /// </remarks>
    [TestCase]
    public void SameSeed_ProducesTheSameSequence()
    {
        var a = SampleWorlds(50, seed: 4242UL);
        var b = SampleWorlds(50, seed: 4242UL);

        Assertions.AssertThat(string.Join(",", a.OrderBy(k => k.Key).Select(k => $"{k.Key}={k.Value}")))
            .IsEqual(string.Join(",", b.OrderBy(k => k.Key).Select(k => $"{k.Key}={k.Value}")));
    }
}
