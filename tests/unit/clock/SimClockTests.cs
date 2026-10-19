using System;
using GdUnit4;
using ClaudeCodeGameStudios.Domain.Clock;

/// <summary>
/// 模拟时钟的单元测试。
///
/// <para><b>为什么这里能在没有 Godot 运行时的环境下测</b>：<c>SimClock</c> 位于
/// <c>src/Domain/</c>，是纯 C#，且换算函数<b>必须由调用方注入</b>，
/// 因此模拟时间可以在测试里被完全确定地驱动。这正是技术债 A7
/// （全局未播种 <c>Random</c>、规则层无法单测）要解决的方向。</para>
///
/// <para><b>API 约定（不要猜）</b>：<c>[TestSuite]</c> 在类上、<c>[TestCase]</c> 在方法上，
/// 断言走 <c>Assertions.AssertThat(...)</c> —— 没有裸 <c>Assert</c> 符号，
/// 也没有 <c>GdUnit4.Test</c> 命名空间。</para>
///
/// <para><b>本文档不规定任何游戏数值。</b> 用到的「一日多少模拟秒」是
/// <b>测试夹具</b>，不是设计值；真实取值属 <c>07</c> 的 <b>T-001 / P-01 / P-02</b>，
/// 此处【待定】。</para>
/// </summary>
[TestSuite]
public class SimClockTests
{
    /// <summary>
    /// 测试夹具用的日长。<b>这是测试夹具，不是设计值</b> —— 真实取值见 07 / T-001。
    /// 刻意取 60，使「一天」与「60 个整步」在二进制浮点下都可精确表示，
    /// 从而帧率无关性断言不会被舍入误差污染。
    /// </summary>
    private const double TestSimSecondsPerDay = 60.0;

    /// <summary>恒等换算：1 现实秒 = 1 模拟秒。真实换算属【待定】T-001 / P-02。</summary>
    private static double Identity(double realSeconds) => realSeconds;

    // ── 不变量 I13：时间单调不回退 ───────────────────────────────────────────

    /// <summary>
    /// 【不变量 I13】反复推进后，模拟时间序列非递减。
    /// 含 dt = 0 的边界：静止的一帧不应让时间倒退，也不应推进。
    /// </summary>
    [TestCase]
    public void Advance_MovesTimeForwardOrHolds_NeverBackwards()
    {
        var clock = new SimClock(TestSimSecondsPerDay, Identity);
        double previous = clock.Now;
        bool monotonic = true;

        double[] deltas = { 0.0, 0.25, 0.0, 1.0, 0.5, 0.0, 2.0, 0.125 };
        foreach (var d in deltas)
        {
            clock.Advance(d);
            if (clock.Now < previous) monotonic = false;
            previous = clock.Now;
        }

        Assertions.AssertThat(monotonic).IsTrue();
    }

    /// <summary>
    /// 负的 dt 是编程错误，不是「回退」请求 —— 必须**拒绝**，不能悄悄 clamp。
    /// 这是 I13 的第二道防线：不靠调用方自觉。
    /// </summary>
    [TestCase]
    public void Advance_NegativeDelta_Throws_InsteadOfSilentlyClamping()
    {
        var clock = new SimClock(TestSimSecondsPerDay, Identity);
        bool threw = false;

        try
        {
            clock.Advance(-1.0);
        }
        catch (ArgumentOutOfRangeException)
        {
            threw = true;
        }

        Assertions.AssertThat(threw).IsTrue();
        Assertions.AssertThat(clock.Now).IsEqual(0.0);
    }

    /// <summary>零 dt 不推进时间，但步序号照常加一 —— 「被 tick 了」与「时间前进了」是两件事。</summary>
    [TestCase]
    public void Advance_ZeroDelta_CountsAStep_WithoutMovingTime()
    {
        var clock = new SimClock(TestSimSecondsPerDay, Identity);

        clock.Advance(0.0);

        Assertions.AssertThat(clock.Now).IsEqual(0.0);
        Assertions.AssertThat(clock.StepCount).IsEqual(1);
    }

    // ── 05 §3.9.1：日序号与日内进度必须互为函数 ──────────────────────────────

    /// <summary>
    /// 【05 §3.9.1】「日序号与日内进度必须保持互为函数」。
    /// 正算 <c>GetDayPosition</c> 再反算 <c>GetSimTime</c>，必须回到原处。
    /// 这条断言正是让该约束**可被机械验证**的机制 —— 只在文档里写「互为函数」是不够的。
    /// </summary>
    [TestCase]
    public void DayPosition_RoundTrips_BackToTheSameSimTime()
    {
        // 覆盖：起始时刻、精确日界、日界前一刻、跨多日、半日、接近日界
        double[] probes =
        {
            0.0,
            60.0,          // 恰好一个整日界
            59.999,        // 日界前一瞬
            60.001,        // 刚过日界
            1799.0,        // 30 日界的整数倍
            30.0,          // 半日
            59.999999,     // 逼近日界的浮点尾巴
        };

        bool allRoundTrip = true;
        foreach (var t in probes)
        {
            var pos = SimClock.GetDayPosition(t, TestSimSecondsPerDay);
            double back = SimClock.GetSimTime(pos.DaySeq, pos.DayProgress, TestSimSecondsPerDay);
            // 容差覆盖浮点除法在整日界上的尾巴
            if (System.Math.Abs(back - t) > 1e-6) allRoundTrip = false;
        }

        Assertions.AssertThat(allRoundTrip).IsTrue();
    }

    /// <summary>日内进度恒落在半开区间 [0, 1) —— 越界就意味着日界算错了。</summary>
    [TestCase]
    public void DayProgress_AlwaysStaysInHalfOpenInterval()
    {
        double[] probes = { 0.0, 0.5, 59.999999, 60.0, 120.0, 1799.9, 1800.0 };
        bool inRange = true;

        foreach (var t in probes)
        {
            var pos = SimClock.GetDayPosition(t, TestSimSecondsPerDay);
            if (pos.DayProgress < 0.0 || pos.DayProgress >= 1.0) inRange = false;
        }

        Assertions.AssertThat(inRange).IsTrue();
    }

    /// <summary>
    /// 跨日界时：时间刚过日界，日序号恰好 +1，日内进度回到接近 0。
    /// 这是「日序号由 Now 派生」的可见证据 —— 若它是独立计数器，这条会红。
    /// </summary>
    [TestCase]
    public void CrossingDayBoundary_IncrementsDaySeq_AndResetsProgress()
    {
        var clock = new SimClock(TestSimSecondsPerDay, Identity);
        clock.AdvanceSimSeconds(59.0);

        long before = clock.DaySeq;
        double progressBefore = clock.DayProgress;

        clock.AdvanceSimSeconds(1.0);   // 正好跨过 60.0

        Assertions.AssertThat(clock.DaySeq).IsEqual(before + 1);
        Assertions.AssertThat(clock.DayProgress).IsEqual(0.0);
        // 跨日前确实就在日界上（进度逼近 1），否则这条断言就没有区分力
        Assertions.AssertThat(progressBefore > 0.9).IsTrue();
    }

    // ── 06 计划 UT-02 / A-01：与帧率无关 ────────────────────────────────────

    /// <summary>
    /// 【A-01 帧率无关性】以不同步长推进**同样的现实时间**，结果必须一致。
    /// <para>
    /// 模拟绝不能接在渲染帧率上（基线 §13.1）。渲染可能跑 30fps，也可能跑 144fps；
    /// 村庄的一天不能因为玩家显卡好就变短。
    /// </para>
    /// <para>步长取 0.25 / 0.5 / 1.0，在二进制浮点下均可精确表示，避免舍入误差污染断言。</para>
    /// </summary>
    [TestCase]
    public void Advance_SameRealElapsedTime_GivesSameResult_AtAnyStepSize()
    {
        const double targetRealSeconds = 4.0;
        double[] stepSizes = { 0.25, 0.5, 1.0, 2.0 };

        double reference = -1.0;
        bool allEqual = true;

        foreach (var step in stepSizes)
        {
            var clock = new SimClock(TestSimSecondsPerDay, Identity);
            int steps = (int)(targetRealSeconds / step);
            for (var i = 0; i < steps; i++) clock.Advance(step);

            if (reference < 0.0) reference = clock.Now;
            else if (System.Math.Abs(clock.Now - reference) > 1e-9) allEqual = false;
        }

        Assertions.AssertThat(allEqual).IsTrue();
        // 恒等换算下，模拟时间应当等于走过的现实时间
        Assertions.AssertThat(reference).IsEqual(targetRealSeconds);
    }

    // ── 构造契约：换算必须注入 ───────────────────────────────────────────────

    /// <summary>
    /// 换算函数是【待定】(T-001 / P-02)，所以 <c>SimClock</c> **不提供默认换算** ——
    /// 提供一个隐式默认值，就等于悄悄替开发者把 T-001 定了。
    /// </summary>
    [TestCase]
    public void Constructor_NullConverter_Throws()
    {
        bool threw = false;

        try
        {
            var unused = new SimClock(TestSimSecondsPerDay, null);
        }
        catch (ArgumentNullException)
        {
            threw = true;
        }

        Assertions.AssertThat(threw).IsTrue();
    }

    /// <summary>日长必须为正 —— 0 或负数会让日界退化，破坏「日序号由 Now 派生」。</summary>
    [TestCase]
    public void Constructor_NonPositiveDayLength_Throws()
    {
        bool threwOnZero = false;
        bool threwOnNegative = false;

        try { var a = new SimClock(0.0, Identity); } catch (ArgumentOutOfRangeException) { threwOnZero = true; }
        try { var b = new SimClock(-1.0, Identity); } catch (ArgumentOutOfRangeException) { threwOnNegative = true; }

        Assertions.AssertThat(threwOnZero).IsTrue();
        Assertions.AssertThat(threwOnNegative).IsTrue();
    }
}
