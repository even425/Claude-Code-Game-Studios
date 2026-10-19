using System;
using System.Collections.Generic;
using GdUnit4;
using ClaudeCodeGameStudios.Domain.Heroes;
using ClaudeCodeGameStudios.Domain.Relationships;

/// <summary>
/// 第三阶段 M2：社交作为「目标型活动」接入决策流程。
/// </summary>
/// <remarks>
/// <para><b>依据：</b><c>07</c> D-036（P3-D05/D06/D07）。社交与采集/建屋共用
/// S1–S6；目标必须在<b>决策时</b>确定（<c>HeroState.BeginAction</c> 在 Step 内执行），
/// 因此扩展点放在 <c>DecisionFlow</c>，而不是完成时的回调。</para>
/// <para><b>这些测试守什么：</b>默认行为不变（无 target 委托时既有活动零目标）、
/// 没有合法目标时活动不可开始、关系只改目标概率不改合法性。</para>
/// </remarks>
[TestSuite]
public class SocialDecisionTests
{
    /// <summary>固定比例的随机源：<c>NextDouble(bound) = fraction * bound</c>。</summary>
    private sealed class FractionRandom : IRandomSource
    {
        private readonly double _fraction;

        public FractionRandom(double fraction) { _fraction = fraction; }

        public double NextDouble(double bound) => Math.Min(_fraction * bound, bound * 0.999999);
    }

    private static readonly HeroId H0 = new("h-0");
    private static readonly HeroId H1 = new("h-1");
    private static readonly HeroId H2 = new("h-2");

    private static ActivityDefinition Social()
        => new(new ActivityId("社交"), new PreferenceCategory("社交"), "", 1.0);

    private static HeroState Hero(string id = "h-0")
        => new(new HeroDefinition(new HeroId(id), id, "测试"));

    // ── 目标解析 ──────────────────────────────────────────────────────────

    /// <summary>目标候选被解析出来，并写进 HeroState（决策时确定，不是完成时）。</summary>
    [TestCase]
    public void Step_TargetActivity_PassesTargetIntoHeroState()
    {
        var flow = new DecisionFlow(
            new[] { Social() }, new FractionRandom(0.0),
            targetCandidates: (_, _) => new[] { H1 });

        var hero = Hero();

        var result = flow.Step(hero);

        Assertions.AssertThat(result.Outcome).IsEqual(DecisionOutcome.Started);
        Assertions.AssertThat(result.Target).IsEqual(H1);
        Assertions.AssertThat(hero.CurrentTarget).IsEqual(H1);
        Assertions.AssertThat(hero.CurrentActivity.Value).IsEqual("社交");
    }

    /// <summary>没有目标委托时，既有活动（采集/建屋）的目标必须是 None —— 行为零变化。</summary>
    [TestCase]
    public void Step_WithoutTargetDelegate_ProducesNoTarget()
    {
        var flow = new DecisionFlow(
            new[] { new ActivityDefinition(new ActivityId("采集"), new PreferenceCategory("劳作"), "勤快", 1.0) },
            new FractionRandom(0.0));

        var result = flow.Step(Hero());

        Assertions.AssertThat(result.Outcome).IsEqual(DecisionOutcome.Started);
        Assertions.AssertThat(result.Target.Value).IsEqual("");
    }

    /// <summary>
    /// 目标型活动没有合法目标 → 按 S5 失败剔除，本步不开始行动、不产生事件。
    /// 这正是 P3-D07「至少一名其他在村英雄」的机械落点。
    /// </summary>
    [TestCase]
    public void Step_TargetActivityWithNoCandidates_IsRejected()
    {
        var flow = new DecisionFlow(
            new[] { Social() }, new FractionRandom(0.0),
            targetCandidates: (_, _) => Array.Empty<HeroId>());

        var hero = Hero();
        var result = flow.Step(hero);

        Assertions.AssertThat(result.Outcome).IsEqual(DecisionOutcome.NoCandidates);
        Assertions.AssertThat(hero.CurrentActivity.Value).IsEqual("");
        Assertions.AssertThat(hero.CurrentTarget.Value).IsEqual("");
    }

    /// <summary>
    /// 混合候选：社交没有对象时，其他活动仍然可选 —— 不会因为「没人可聊」而整步空转。
    /// </summary>
    [TestCase]
    public void Step_TargetActivityRejected_OtherActivitiesStillAvailable()
    {
        var activities = new[]
        {
            new ActivityDefinition(new ActivityId("采集"), new PreferenceCategory("劳作"), "勤快", 1.0),
            Social(),
        };
        // 0.0 → 先抽到「采集」（社交在最后）；社交无目标会被剔除，
        // 而采集不是目标活动（delegate 返回 null），不能受牵连。
        var flow = new DecisionFlow(
            activities, new FractionRandom(0.0),
            targetCandidates: (_, activity) =>
                activity.Id.Value == "社交" ? Array.Empty<HeroId>() : null);

        var result = flow.Step(Hero());

        Assertions.AssertThat(result.Outcome).IsEqual(DecisionOutcome.Started);
        Assertions.AssertThat(result.Chosen.Value).IsEqual("采集");
    }

    // ── 关系只改概率、不改合法性 ──────────────────────────────────────────

    /// <summary>
    /// 固定随机下，关系权重能决定抽中谁：h-1 权重 1、h-2 权重 3，
    /// 0.5 的落点应该落在 h-2 上。
    /// </summary>
    [TestCase]
    public void Step_TargetWeight_DeterminesPick_WithFixedRandom()
    {
        var flow = new DecisionFlow(
            new[] { Social() }, new FractionRandom(0.5),
            targetCandidates: (_, _) => new[] { H1, H2 },
            targetWeight: (_, target) => target == H2 ? 3.0 : 1.0);

        var result = flow.Step(Hero());

        Assertions.AssertThat(result.Target).IsEqual(H2);
    }

    /// <summary>同一个随机落点下，把权重反过来，抽中的人也应该反过来。</summary>
    [TestCase]
    public void Step_TargetWeight_FavoursTheLikedTarget()
    {
        var flow = new DecisionFlow(
            new[] { Social() }, new FractionRandom(0.2),
            targetCandidates: (_, _) => new[] { H1, H2 },
            targetWeight: (_, target) => target == H2 ? 3.0 : 1.0);

        var result = flow.Step(Hero());

        Assertions.AssertThat(result.Target).IsEqual(H1);
    }

    /// <summary>权重为 0 / 负数 / NaN 是定义错误，必须炸掉（关系只改概率）。</summary>
    [TestCase]
    public void Step_NonPositiveTargetWeight_Throws()
    {
        var flow = new DecisionFlow(
            new[] { Social() }, new FractionRandom(0.0),
            targetCandidates: (_, _) => new[] { H1 },
            targetWeight: (_, _) => 0.0);

        bool threw = false;
        try { flow.Step(Hero()); }
        catch (InvalidOperationException) { threw = true; }

        Assertions.AssertThat(threw).IsTrue();
    }

    /// <summary>
    /// 关系极差（权重 0.1）仍然可选 —— 只是概率低。
    /// 断言的是「不抛异常且能抽中」，不是「必然不抽中」。
    /// </summary>
    [TestCase]
    public void Step_DislikedTarget_RemainsSelectable()
    {
        var flow = new DecisionFlow(
            new[] { Social() }, new FractionRandom(0.0),
            targetCandidates: (_, _) => new[] { H1 },
            targetWeight: (_, _) => RelationshipRules.TargetWeight(-100));

        var result = flow.Step(Hero());

        Assertions.AssertThat(result.Outcome).IsEqual(DecisionOutcome.Started);
        Assertions.AssertThat(result.Target).IsEqual(H1);
    }

    /// <summary>目标候选里出现自己 / 空 ID / 重复，一律是编程错误。</summary>
    [TestCase]
    public void Step_InvalidTargetCandidates_Throw()
    {
        bool selfThrew = false, emptyThrew = false, dupThrew = false;

        try
        {
            new DecisionFlow(new[] { Social() }, new FractionRandom(0.0),
                    targetCandidates: (_, _) => new[] { H0 })
                .Step(Hero("h-0"));
        }
        catch (InvalidOperationException) { selfThrew = true; }

        try
        {
            new DecisionFlow(new[] { Social() }, new FractionRandom(0.0),
                    targetCandidates: (_, _) => new[] { new HeroId("") })
                .Step(Hero());
        }
        catch (InvalidOperationException) { emptyThrew = true; }

        try
        {
            new DecisionFlow(new[] { Social() }, new FractionRandom(0.0),
                    targetCandidates: (_, _) => new[] { H1, H1 })
                .Step(Hero());
        }
        catch (InvalidOperationException) { dupThrew = true; }

        Assertions.AssertThat(selfThrew).IsTrue();
        Assertions.AssertThat(emptyThrew).IsTrue();
        Assertions.AssertThat(dupThrew).IsTrue();
    }
}
