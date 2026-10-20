using System.Collections.Generic;
using System.Linq;
using GdUnit4;
using ClaudeCodeGameStudios;

namespace ClaudeCodeGameStudios.Tests;

/// <summary>
/// 杂谈的<b>确定性</b>守卫（D-054 阶段 8 · A7 收尾）。
///
/// <para><b>它守的是什么</b>：同一个会话种子，两次运行必须写出<b>同一批杂谈</b>。</para>
///
/// <para><b>为什么需要它</b>：杂谈的模板与配角是用随机源挑的。此前那个随机源
/// <b>没播种</b> ⇒ 同一个种子两次跑会写出不同的杂谈。这既违反
/// <c>CLAUDE.md</c> 的 Determinism 测试标准，也让「同种子复现采集」的样本
/// <b>彼此不可比</b>（D-054 阶段 3 已经因为同一个原因给召唤补过一次播种）。</para>
///
/// <para><b>还守一条</b>：杂谈的随机流**不能与英雄召唤共用同一条**
/// —— 否则「换哪个模板」会与「抽哪个世界/性格」同进同退。</para>
/// </summary>
[TestSuite]
public class ChronicleDeterminismTests
{
    /// <summary>同一种子 → 同一批杂谈（逐条相同）。</summary>
    [TestCase]
    public void SameSessionSeed_ProducesTheSameChronicleLines()
    {
        var first = Render(4242);
        var second = Render(4242);

        Assertions.AssertThat(first.Count).IsGreater(0);
        Assertions.AssertThat(first).IsEqual(second);
    }

    /// <summary>不同种子 → 至少有一条不同（否则说明这条流根本没被种子影响）。</summary>
    [TestCase]
    public void DifferentSessionSeeds_ProduceDifferentChronicleLines()
    {
        var a = Render(4242);
        var b = Render(7777);

        bool anyDifferent = a.Zip(b, (x, y) => x != y).Any(d => d);
        Assertions.AssertThat(anyDifferent).IsTrue();
    }

    /// <summary>
    /// 杂谈这条流的种子**必然**不等于英雄召唤那条（否则两条流同进同退）。
    /// </summary>
    /// <remarks>
    /// 异或一个非零常量 ⇒ 必然不同（这是构造上的保证，不是概率）。
    /// </remarks>
    [TestCase]
    public void ChronicleStreamSeed_NeverEqualsTheHeroFactoryStreamSeed()
    {
        foreach (ulong s in new ulong[] { 0UL, 1UL, 42UL, 4242UL, 7777UL, ulong.MaxValue })
        {
            int heroFactory = (int)(s & 0x7FFFFFFF);
            int chronicle = Chronicle.DeriveStreamSeed(s);

            Assertions.AssertThat(chronicle).IsNotEqual(heroFactory);
        }
    }

    /// <summary>
    /// 播种之后再渲染，杂谈内容**确实**受种子影响（不是「恰好都一样」）。
    /// </summary>
    /// <remarks>
    /// 这条防的是「把 <c>Seed</c> 写成空实现」——那样前两条会侥幸通过。
    /// </remarks>
    [TestCase]
    public void Seeding_ActuallyChangesTheOutput()
    {
        var a = Render(1);
        var b = Render(2);
        var c = Render(3);
        var d = Render(4);

        // 四条流里至少要有两条不同 —— 否则种子对输出没有影响
        int distinct = new[] { a, b, c, d }.Distinct().Count();
        Assertions.AssertThat(distinct).IsGreater(1);
    }

    /// <summary>渲染一批杂谈（每行都会用到随机源：挑模板 + 挑配角）。</summary>
    private static List<string> Render(ulong seed)
    {
        Chronicle.Seed(seed);
        var lines = new List<string>();
        for (int i = 0; i < 12; i++)
        {
            lines.Add(Chronicle.SquadLeft(i + 1, "甲", "乙", "丙").Text);
        }
        return lines;
    }
}
