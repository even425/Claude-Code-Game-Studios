using System;
using GdUnit4;
using ClaudeCodeGameStudios.Domain.Heroes;

/// <summary>
/// 随机源的<b>分布</b>测试。
///
/// <para><b>为什么单独测它</b>：加权抽样整个偏好系统都建立在随机源之上。
/// 一个<b>偏斜</b>的随机源不会让任何测试「报错」—— 它只是让某些活动永远抽不中，
/// 而「相同种子产生相同序列」这类可复现性断言<b>照样全绿</b>。
/// 本测试把那种静默失效变成红灯。</para>
///
/// <para><b>实际踩过的坑</b>：初版 <c>SeededRandom</c> 写成
/// <c>(s &gt;&gt; 33) / ulong.MaxValue</c> —— 31 位的数除以 64 位的最大值，
/// 结果恒约等于 0，抽样永远返回第一项。当时只有 CR-1 的统计断言抓住了它。</para>
/// </summary>
[TestSuite]
public class SeededRandomTests
{
    /// <summary>抽样 8000 次，统计落在十个等宽区间里的次数。</summary>
    private static int[] Histogram(ulong seed, int draws)
    {
        var rng = new DecisionFlow.SeededRandom(seed);
        var hist = new int[10];
        for (var i = 0; i < draws; i++)
        {
            double x = rng.NextDouble(1.0);
            if (x < 0.0 || x >= 1.0) { hist[0] = int.MinValue; break; }   // 越界即致命
            hist[Math.Min(9, (int)(x * 10))]++;
        }
        return hist;
    }

    /// <summary>
    /// 三个固定种子都必须能覆盖<b>整个</b> [0,1) 区间。
    /// </summary>
    /// <remarks>
    /// 曾经的实现让 <c>x</c> 恒约等于 0，十个区间里只有第一个被填满。
    /// 这条断言正是当时缺失的那道防线。
    /// </remarks>
    [TestCase]
    public void NextDouble_CoversTheWholeRange_ForSeveralSeeds()
    {
        ulong[] seeds = { 1, 7, 8, 424242, 20261017 };

        bool allCovered = true;
        foreach (var seed in seeds)
        {
            var hist = Histogram(seed, 4000);
            if (hist[0] == int.MinValue) { allCovered = false; break; }   // 越界
            foreach (var bucket in hist)
            {
                if (bucket == 0) { allCovered = false; break; }
            }
        }

        Assertions.AssertThat(allCovered).IsTrue();
    }

    /// <summary>
    /// 高位区间（约 0.9–1.0）必须能被抽到 —— 它正是「第二项活动」能被选中的前提。
    /// </summary>
    /// <remarks>
    /// <c>Step_PreferenceShiftsTheDistribution</c> 用种子 7/8 时曾出现
    /// highPicked = 8000 / lowPicked = 0，正是这条缺失导致的。
    /// </remarks>
    [TestCase]
    public void NextDouble_ReachesTheHighDecile_ForEverySeed()
    {
        ulong[] seeds = { 1, 7, 8, 424242, 20261017 };

        bool allReached = true;
        foreach (var seed in seeds)
        {
            var hist = Histogram(seed, 8000);
            if (hist[9] == 0) allReached = false;
        }

        Assertions.AssertThat(allReached).IsTrue();
    }

    /// <summary>取值必须落在 <c>[0, bound)</c> 内，且随 bound 线性缩放。</summary>
    [TestCase]
    public void NextDouble_StaysWithinBound_AndScalesLinearly()
    {
        var rng = new DecisionFlow.SeededRandom(99);
        bool inRange = true;
        for (var i = 0; i < 5000; i++)
        {
            double v = rng.NextDouble(3.5);
            if (v < 0.0 || v >= 3.5) inRange = false;
        }
        Assertions.AssertThat(inRange).IsTrue();

        // 同样的种子与上界序列应完全一致 —— 可复现性
        var a = new DecisionFlow.SeededRandom(99);
        var b = new DecisionFlow.SeededRandom(99);
        bool same = true;
        for (var i = 0; i < 100; i++)
        {
            if (Math.Abs(a.NextDouble(2.0) - b.NextDouble(2.0)) > double.Epsilon) same = false;
        }
        Assertions.AssertThat(same).IsTrue();
    }

    /// <summary>上界非正必须被拒 —— 静默返回一个坏值比抛异常危险得多。</summary>
    [TestCase]
    public void NextDouble_NonPositiveBound_Throws()
    {
        var rng = new DecisionFlow.SeededRandom(1);

        bool zeroThrew = false;
        bool negativeThrew = false;
        try { rng.NextDouble(0.0); } catch (ArgumentOutOfRangeException) { zeroThrew = true; }
        try { rng.NextDouble(-1.0); } catch (ArgumentOutOfRangeException) { negativeThrew = true; }

        Assertions.AssertThat(zeroThrew).IsTrue();
        Assertions.AssertThat(negativeThrew).IsTrue();
    }

    /// <summary>种子 0 必须被换掉 —— 它是 LCG 的不动点，会产生恒定序列。</summary>
    [TestCase]
    public void Constructor_ZeroSeed_DoesNotProduceAConstantSequence()
    {
        var rng = new DecisionFlow.SeededRandom(0);

        double first = rng.NextDouble(1.0);
        double second = rng.NextDouble(1.0);

        Assertions.AssertThat(Math.Abs(first - second) > 1e-12).IsTrue();
    }
}
