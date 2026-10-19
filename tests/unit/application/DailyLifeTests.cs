using System;
using System.Collections.Generic;
using GdUnit4;
using ClaudeCodeGameStudios.Application;
using ClaudeCodeGameStudios.Domain.Events;
using ClaudeCodeGameStudios.Domain.Heroes;

/// <summary>
/// 第三阶段 M2：<c>DailyLife</c> 接入「社交」活动的首个单元测试。
/// </summary>
/// <remarks>
/// <para><b>为什么现在才有</b>：<c>DailyLife</c> 此前<b>没有任何单测</b>（M0 审计 R-13），
/// 只靠 <c>Main --events</c> 的运行自检。第三阶段改它（加社交、加 target、换随机源），
/// 没有单测就等于改了没人知道。</para>
/// <para><b>这些测试守什么：</b>活动集与效果表必须一一对应（漏一个构造期就炸）、
/// 完成时效果拿得到目标、事件 payload 带目标。</para>
/// </remarks>
[TestSuite]
public class DailyLifeTests
{
    /// <summary>固定比例随机源（0.99 会抽到活动表最后一项「社交」）。</summary>
    private sealed class FractionRandom : IRandomSource
    {
        private readonly double _fraction;

        public FractionRandom(double fraction) { _fraction = fraction; }

        public double NextDouble(double bound) => Math.Min(_fraction * bound, bound * 0.999999);
    }

    private static HeroState Hero(string id = "h-0")
        => new(new HeroDefinition(new HeroId(id), id, "测试"));

    /// <summary>一份覆盖全部活动的最小效果表；社交效果被记录下来供断言。</summary>
    private static Dictionary<ActivityId, DailyLife.ActivityEffect> Effects(
        List<(string Actor, HeroId Target)> socialCalls,
        List<ActivityId> completed)
    {
        return new Dictionary<ActivityId, DailyLife.ActivityEffect>
        {
            [new ActivityId("采集")] = (activity, _, target) => completed.Add(activity),
            [new ActivityId("建屋")] = (activity, _, target) => completed.Add(activity),
            [new ActivityId("社交")] = (activity, actor, target) =>
            {
                completed.Add(activity);
                socialCalls.Add((actor, target));
            },
        };
    }

    // ── 构造期：活动集与效果表必须对齐 ────────────────────────────────────

    /// <summary>漏注册「社交」效果 → 构造期拒绝，而不是等第一个英雄完成社交时炸。</summary>
    [TestCase]
    public void Ctor_RejectsEffectsMissingSocialActivity()
    {
        var effects = new Dictionary<ActivityId, DailyLife.ActivityEffect>
        {
            [new ActivityId("采集")] = (_, _, _) => { },
            [new ActivityId("建屋")] = (_, _, _) => { },
        };

        bool threw = false;
        try { _ = new DailyLife(null, effects, 1.0); }
        catch (ArgumentException) { threw = true; }

        Assertions.AssertThat(threw).IsTrue();
    }

    /// <summary>效果表里出现活动集之外的活动 → 构造期拒绝（那项效果永远不会被执行）。</summary>
    [TestCase]
    public void Ctor_RejectsUnknownEffectActivity()
    {
        var effects = Effects(new List<(string, HeroId)>(), new List<ActivityId>());
        effects[new ActivityId("不存在的活动")] = (_, _, _) => { };

        bool threw = false;
        try { _ = new DailyLife(null, effects, 1.0); }
        catch (ArgumentException) { threw = true; }

        Assertions.AssertThat(threw).IsTrue();
    }

    // ── 社交完成：目标一路传到效果 ────────────────────────────────────────

    /// <summary>
    /// 社交被自主选中 → 完成后效果收到<b>目标英雄</b>，且完成事件 payload 带 target。
    /// </summary>
    [TestCase]
    public void Step_SocialCompletion_PassesTargetToEffectAndEvent()
    {
        var socialCalls = new List<(string Actor, HeroId Target)>();
        var completed = new List<ActivityId>();
        var targetId = new HeroId("h-1");
        var life = new DailyLife(
            new FractionRandom(0.99), Effects(socialCalls, completed), activityDurationSeconds: 1.0,
            targetCandidates: (_, _) => new[] { targetId });

        var hero = Hero("h-0");
        var started = life.Step(new[] { hero }, 0.0);
        Assertions.AssertThat(started.Count).IsEqual(1);
        Assertions.AssertThat(started[0].Payload["activity"]).IsEqual("社交");
        Assertions.AssertThat(started[0].Payload["target"]).IsEqual("h-1");

        var finished = life.Step(new[] { hero }, 1.0);   // 行动完成

        Assertions.AssertThat(socialCalls.Count).IsEqual(1);
        Assertions.AssertThat(socialCalls[0].Actor).IsEqual("h-0");
        Assertions.AssertThat(socialCalls[0].Target).IsEqual(targetId);
        Assertions.AssertThat(finished.Count).IsEqual(1);
        Assertions.AssertThat(finished[0].Payload["activity"]).IsEqual("社交");
        Assertions.AssertThat(finished[0].Payload["target"]).IsEqual("h-1");
        Assertions.AssertThat(hero.CurrentActivity.Value).IsEqual("");
        Assertions.AssertThat(hero.CurrentTarget.Value).IsEqual("");
    }

    /// <summary>非目标活动（采集）完成时，效果拿到的目标必须是 None。</summary>
    [TestCase]
    public void Step_UntargetedCompletion_EffectReceivesNoTarget()
    {
        var socialCalls = new List<(string Actor, HeroId Target)>();
        var completed = new List<ActivityId>();
        var life = new DailyLife(
            new FractionRandom(0.0), Effects(socialCalls, completed), activityDurationSeconds: 1.0);

        var hero = Hero("h-0");
        life.Step(new[] { hero }, 0.0);       // 抽到「采集」
        life.Step(new[] { hero }, 1.0);       // 完成

        Assertions.AssertThat(completed.Count).IsEqual(1);
        Assertions.AssertThat(completed[0].Value).IsEqual("采集");
        Assertions.AssertThat(socialCalls.Count).IsEqual(0);
        Assertions.AssertThat(hero.CurrentTarget.Value).IsEqual("");
    }

    /// <summary>没有合法目标时，社交不会被选中，本步也不产生事件（合法空转）。</summary>
    [TestCase]
    public void Step_SocialWithoutTargets_DoesNotStart()
    {
        var socialCalls = new List<(string Actor, HeroId Target)>();
        var completed = new List<ActivityId>();
        var life = new DailyLife(
            new FractionRandom(0.99), Effects(socialCalls, completed), activityDurationSeconds: 1.0,
            targetCandidates: (_, _) => Array.Empty<HeroId>());

        var hero = Hero("h-0");
        var events = life.Step(new[] { hero }, 0.0);

        Assertions.AssertThat(events.Count).IsEqual(0);
        Assertions.AssertThat(hero.CurrentActivity.Value).IsEqual("");
    }
}
