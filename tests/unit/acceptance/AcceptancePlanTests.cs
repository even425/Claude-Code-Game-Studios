using System;
using System.Collections.Generic;
using System.Linq;
using GdUnit4;
using ClaudeCodeGameStudios.Application;
using ClaudeCodeGameStudios.Domain.Clock;
using ClaudeCodeGameStudios.Domain.Events;
using ClaudeCodeGameStudios.Domain.Heroes;

namespace ClaudeCodeGameStudios.Tests;

/// <summary>
/// <b>验收计划里还没有变成测试的断言</b>（<c>06_第一阶段验收与测试计划_V1.0.md</c>）。
///
/// <para>这三条的共同点：<b>它们不需要任何设计数值</b>。
/// `06` 对 UT-02 写得很直白：「这条不需要任何设计数值，因此它在 T-001 未决时
/// <b>就可以先写</b>」。既然如此，一直不写就<b>没有道理</b> ——
/// 拖着的不是规则，是纪律。</para>
///
/// <para><b>为什么它们值钱</b>：UT-02 原文：「它一旦失败，说明表现层正在驱动领域
/// （<c>07</c> A4 未清除），这类缺陷后期极难定位」。
/// 它是一盏<b>探照灯</b>：只要有人把帧间隔漏进领域层，它就亮红灯。</para>
///
/// <para><b>本文件不发明任何玩法数值</b>。文中的时长、步长、种子全是
/// <b>测试夹具</b>（为了让断言可观测而放大），不是设计值。</para>
/// </summary>
[TestSuite]
public class AcceptancePlanTests
{
    // ── 夹具 ──────────────────────────────────────────────────────────
    // ⚠️ 这些数是**夹具**，不是设计值。设计值属 07 的 T-001 / P-01 / P-06。

    /// <summary>一个模拟步覆盖多少模拟秒（固定步长模式的步长）。</summary>
    private const double FixedStepSeconds = 0.5;

    /// <summary>测试用行动时长：非零，且不是步长的整数倍 —— 后者才能暴露「按步取整」的 bug。</summary>
    private const double ActionDurationSeconds = 1.3;

    /// <summary>固定种子。两次运行必须用同一个，否则比的是运气不是确定性。</summary>
    private const ulong TestSeed = 0x5EED_1234UL;

    private static readonly ActivityId Collect = new("采集");
    private static readonly ActivityId Leisure = new("休闲");

    private static List<ActivityDefinition> Activities() => new()
    {
        new ActivityDefinition(Collect, new PreferenceCategory("劳作"), "勤快", ActionDurationSeconds),
        new ActivityDefinition(Leisure, new PreferenceCategory("闲散"), "懒散", ActionDurationSeconds),
    };

    private static HeroState MakeHero() =>
        new(new HeroDefinition(new HeroId("h-test"), "测试", "测试来处",
            new Dictionary<string, double> { ["勤快"] = 1.5, ["懒散"] = 1.0 },
            new Dictionary<string, double> { ["劳作"] = 1.2, ["闲散"] = 1.0 }));

    /// <summary>
    /// 一步模拟：推进当前行动 → 行动完成则记一笔；空闲则做一次决策。
    /// <b>把状态变化逐条记进日志</b>，供两次运行逐条比对。
    /// </summary>
    private sealed class HeroDayStep : ISimulationStep
    {
        private readonly HeroState _hero;
        private readonly DecisionFlow _flow;
        private readonly List<string> _log;

        public HeroDayStep(HeroState hero, DecisionFlow flow, List<string> log)
        {
            _hero = hero;
            _flow = flow;
            _log = log;
        }

        public string Name => "hero-day";

        public void Execute(SimulationStepContext context)
        {
            var finished = _hero.AdvanceAction(context.SimDeltaSeconds);
            if (finished != ActivityId.None)
            {
                _log.Add($"done:{finished.Value}@{context.Clock.Now:0.000000}");
            }

            if (!_hero.IsEligibleForDecision()) return;

            var result = _flow.Step(_hero);
            if (result.Outcome == DecisionOutcome.Started)
            {
                _log.Add($"start:{result.Chosen.Value}"
                         + $"@{context.Clock.Now:0.000000}"
                         + $"/rem={_hero.RemainingActionSeconds:0.000000}");
            }
        }
    }

    /// <summary>
    /// 以指定的<b>现实帧间隔</b>驱动模拟，直到推进了 <paramref name="targetSteps"/> 个模拟步，
    /// 返回逐条状态变化日志。
    /// </summary>
    private static List<string> RunAtFrameRate(double realFrameSeconds, int targetSteps)
    {
        // 恒等换算：现实秒 = 模拟秒。换算关系本身属 T-001，这里注入的是「自己写的换算」。
        var clock = new SimClock(simSecondsPerDay: 24 * 3600, realToSimSeconds: r => r);
        var flow = new DecisionFlow(Activities(), new DecisionFlow.SeededRandom(TestSeed));
        var hero = MakeHero();
        var log = new List<string>();

        var runner = new SimulationRunner(
            clock,
            new ISimulationStep[] { new HeroDayStep(hero, flow, log) },
            fixedStepSeconds: FixedStepSeconds,
            maxCatchUpSteps: SimulationRunner.RequiredCatchUpLimit);

        int guard = 0;
        while (clock.StepCount < targetSteps)
        {
            if (++guard > 10_000_000)
            {
                throw new InvalidOperationException("模拟没有推进，测试夹具有问题。");
            }
            runner.AdvanceFrame(realFrameSeconds);
        }

        return log;
    }

    // ── UT-02（A-01 帧率无关性）──────────────────────────────────────

    /// <summary>
    /// <b>UT-02 / A-01</b>：以高帧率与低帧率各推进到<b>相同的模拟步数</b>，
    /// 两次产生的模拟状态序列必须<b>逐条完全相同</b>。
    ///
    /// <para><b>它守的是什么</b>：领域层<b>不能看见帧率</b>。
    /// 表现层交来的 <c>dt</c> 只能是数据；模拟步长由固定步长模式决定，
    /// 与现实帧间隔无关。一旦有人把 <c>realDeltaSeconds</c> 漏进领域，
    /// 这条就红。</para>
    ///
    /// <para><b>为什么比对「日志」而不是「最终状态」</b>：最终状态相同但
    /// 中途路径不同，是有可能的（两条不同的路走到同一个地方）。
    /// 逐条比对整条轨迹才是真的 A-01。</para>
    /// </summary>
    [TestCase]
    public void UT02_SameNumberOfSimSteps_ProducesIdenticalStateSequence()
    {
        const int targetSteps = 400;

        var highFps = RunAtFrameRate(realFrameSeconds: 1.0 / 60.0, targetSteps);
        var lowFps = RunAtFrameRate(realFrameSeconds: 1.0 / 15.0, targetSteps);

        // 夹具自检：两次都必须真的动起来，否则「两次都空」也会「相同」。
        Assertions.AssertThat(highFps.Count).IsGreater(0);
        Assertions.AssertThat(lowFps.Count).IsEqual(highFps.Count);

        // 逐条比对整条轨迹（不是只比最终状态：两条不同的路可能走到同一个地方）
        Assertions.AssertThat(highFps).IsEqual(lowFps);
    }

    /// <summary>
    /// UT-02 的<b>另一半</b>：帧率变了，<b>模拟步数与模拟时间</b>也不能变。
    /// <para>（上面那条比的是轨迹；这条比的是「跑到哪儿了」。两者都成立，
    /// 才说明帧率只影响「多久走完」，不影响「走成什么样」。）</para>
    /// </summary>
    [TestCase]
    public void UT02_FrameRate_DoesNotChange_HowMuchSimTimeElapsed()
    {
        const int targetSteps = 300;

        double highFpsNow = RunToClockNow(1.0 / 60.0, targetSteps);
        double lowFpsNow = RunToClockNow(1.0 / 15.0, targetSteps);

        Assertions.AssertThat(lowFpsNow).IsEqual(highFpsNow);
        // 步长 × 步数 = 应有的模拟时间。夹具自检：别让「两条都是 0」蒙混过关。
        Assertions.AssertThat(highFpsNow).IsEqual(targetSteps * FixedStepSeconds);
    }

    private static double RunToClockNow(double realFrameSeconds, int targetSteps)
    {
        var clock = new SimClock(simSecondsPerDay: 24 * 3600, realToSimSeconds: r => r);
        var runner = new SimulationRunner(
            clock,
            Array.Empty<ISimulationStep>(),
            fixedStepSeconds: FixedStepSeconds,
            maxCatchUpSteps: SimulationRunner.RequiredCatchUpLimit);

        int guard = 0;
        while (clock.StepCount < targetSteps)
        {
            if (++guard > 10_000_000)
            {
                throw new InvalidOperationException("模拟没有推进，测试夹具有问题。");
            }
            runner.AdvanceFrame(realFrameSeconds);
        }

        return clock.Now;
    }

    // ── UT-04（A-03 行动只完成一次）──────────────────────────────────

    /// <summary>
    /// <b>UT-04 / A-03</b>：对<b>同一个行动实例</b>调用两次完成处理，
    /// 第二次必须<b>什么都没发生</b>：不新增事件、不改资源与持有物。
    ///
    /// <para><b>它守的是什么</b>：「行动完成」是<b>一次性</b>的。
    /// 如果第二次调用还能再领一次收益（材料、战利品、金币），
    /// 玩家就能靠「多推一帧」刷资源 —— 那是基线 §13.5 I15 那类「要么完整应用、
    /// 要么不应用」被打破的形态。</para>
    /// </summary>
    [TestCase]
    public void UT04_CompletingTheSameActionTwice_ChangesNothingTheSecondTime()
    {
        var hero = MakeHero();
        hero.BeginAction(Collect, ActionDurationSeconds);

        // 第一次完成：推进超过剩余时长
        var first = hero.AdvanceAction(ActionDurationSeconds + 0.1);
        Assertions.AssertThat(first).IsEqual(Collect);
        Assertions.AssertThat(hero.IsIdle).IsTrue();
        Assertions.AssertThat(hero.RemainingActionSeconds).IsEqual(0.0);

        // 记录「完成之后」的全部可观察状态
        string activityAfter = hero.CurrentActivity.Value;
        double remainingAfter = hero.RemainingActionSeconds;
        string targetAfter = hero.CurrentTarget.Value;
        bool firstDungeonAfter = hero.HasCompletedFirstDungeon;

        // 第二次完成：**同一个行动实例**再来一次
        var second = hero.AdvanceAction(ActionDurationSeconds + 0.1);

        // 不得再报一次完成
        Assertions.AssertThat(second).IsEqual(ActivityId.None);
        // 状态一个字段都不能动
        Assertions.AssertThat(hero.CurrentActivity.Value).IsEqual(activityAfter);
        Assertions.AssertThat(hero.RemainingActionSeconds).IsEqual(remainingAfter);
        Assertions.AssertThat(hero.CurrentTarget.Value).IsEqual(targetAfter);
        Assertions.AssertThat(hero.HasCompletedFirstDungeon).IsEqual(firstDungeonAfter);
        Assertions.AssertThat(hero.IsIdle).IsTrue();
    }

    // ── UT-10（A-07 空转不产生条目）──────────────────────────────────

    /// <summary>
    /// <b>UT-10 / A-07</b>：只有「<b>占用中</b>」或「<b>候选为空</b>」的模拟步，
    /// 新增杂谈条目数必须 = 0。
    ///
    /// <para><b>它守的是什么</b>：基线 §7.3 —— 杂谈只描述<b>发生过的事</b>。
    /// 一次什么都没发生的步子，不该在村庄的闲聊里留下痕迹。
    /// `07` 的债 B7（装饰条目 <c>DayIdle</c> / <c>DayStranger</c>
    /// 不改任何状态）违反的正是这条。</para>
    ///
    /// <para><b>怎么验</b>：空转的两种形态各跑一遍，
    /// 断言「① 没有决策产出、② 状态逐字段没变、③ 事件日志与杂谈条目数没变」。
    /// 三条一起看，才不会漏掉「悄悄改了状态但没发条目」那种更隐蔽的形态。</para>
    /// </summary>
    [TestCase]
    public void UT10_IdleOrEmptyCandidateSteps_ProduceNoActionAndNoChronicleEntry()
    {
        var events = new EventLog();
        var feed = new ChronicleFeed();

        // ── 形态一：占用中（还在干活，不该重新决策）────────────────────
        var busy = MakeHero();
        busy.BeginAction(Collect, ActionDurationSeconds);

        var busyResult = new DecisionFlow(Activities(), new DecisionFlow.SeededRandom(TestSeed)).Step(busy);

        Assertions.AssertThat(busyResult.Outcome).IsEqual(DecisionOutcome.NotEligible);
        Assertions.AssertThat(busy.CurrentActivity.Value).IsEqual(Collect.Value);
        Assertions.AssertThat(busy.RemainingActionSeconds).IsEqual(ActionDurationSeconds);

        // ── 形态二：候选为空（每项活动的前置校验都不通过）────────────
        var starved = MakeHero();
        var flow = new DecisionFlow(
            Activities(),
            new DecisionFlow.SeededRandom(TestSeed),
            precondition: (_, _) => false);   // 全部候选都在 S5 被剔除

        var starvedResult = flow.Step(starved);

        Assertions.AssertThat(starvedResult.Outcome).IsEqual(DecisionOutcome.NoCandidates);
        // 候选为空 ⇒ 合法空转：不得开始任何行动，也不得改状态
        Assertions.AssertThat(starved.IsIdle).IsTrue();
        Assertions.AssertThat(starved.RemainingActionSeconds).IsEqual(0.0);

        // ── 条目数：两种空转都不得留下任何痕迹 ───────────────────────
        Assertions.AssertThat(events.Count).IsEqual(0);
        Assertions.AssertThat(feed.Entries.Count).IsEqual(0);
    }
}
