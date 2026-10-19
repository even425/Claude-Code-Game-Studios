using System;
using System.Collections.Generic;
using GdUnit4;
using ClaudeCodeGameStudios.Domain.Heroes;
using ClaudeCodeGameStudios.Domain.Relationships;

/// <summary>
/// 第三阶段 M4：关系驱动行为的最小闭环证据。
/// </summary>
/// <remarks>
/// <para><b>验收要求（第三阶段设计 §9.4）：</b>「在其他条件受控时，关系变化能够改变
/// 目标行为的选择结果或可用性」；「如果关系只影响权重，不应保证某个行为必然发生」。</para>
/// <para><b>本测试的做法：</b>固定同一个随机落点、同一组候选，只改关系值，
/// 断言被选中的社交对象改变。关系权重通过 <see cref="RelationshipRules.TargetWeight"/>
/// 计算 —— 与 <c>Main</c> 注入 <c>DecisionFlow</c> 的委托是同一函数，不是复制品。</para>
/// </remarks>
[TestSuite]
public class SocialTargetTests
{
    private sealed class FractionRandom : IRandomSource
    {
        private readonly double _fraction;

        public FractionRandom(double fraction) { _fraction = fraction; }

        public double NextDouble(double bound) => Math.Min(_fraction * bound, bound * 0.999999);
    }

    private static readonly HeroId Actor = new("h-0");
    private static readonly HeroId Alice = new("h-1");
    private static readonly HeroId Bob = new("h-2");

    private static HeroState Hero(string id = "h-0")
        => new(new HeroDefinition(new HeroId(id), id, "测试"));

    private static ActivityDefinition Social()
        => new(new ActivityId("社交"), new PreferenceCategory("社交"), "", 1.0);

    /// <summary>用关系书构造与 Main 相同的目标权重委托。</summary>
    private static Func<HeroState, HeroId, double> WeightsFrom(RelationshipBook book)
        => (hero, targetId) => RelationshipRules.TargetWeight(book.ValueOf(hero.Id, targetId));

    // ── 核心闭环 ──────────────────────────────────────────────────────────

    /// <summary>
    /// 【M4 核心断言】同一随机落点、同一候选：关系改变 → 选中对象改变。
    /// </summary>
    [TestCase]
    public void RelationshipChange_ChangesWhichTargetIsChosen()
    {
        // 未建立关系时，权重都接近 1；0.5 落点会落到第二个人（Bob）。
        var noRelation = new RelationshipBook();
        var baseline = PickTarget(noRelation);

        // 让 Actor 非常喜欢 Alice：Alice 权重 3，Bob 权重 1；同一落点应改选 Alice。
        var liked = new RelationshipBook();
        liked.Apply("seed:like", new RelationshipKey(Actor, Alice), 100);
        var after = PickTarget(liked);

        Assertions.AssertThat(baseline).IsEqual(Bob);
        Assertions.AssertThat(after).IsEqual(Alice);
    }

    /// <summary>
    /// 反向：关系变差只降低概率，不把目标从候选里删掉（关系不改合法性）。
    /// </summary>
    [TestCase]
    public void NegativeRelationship_DoesNotRemoveTarget()
    {
        var book = new RelationshipBook();
        book.Apply("seed:dislike", new RelationshipKey(Actor, Alice), -100);

        // 只剩 Alice 时她依然可选：权重 0.1 > 0，不是「不可选」。
        var result = Step(book, new[] { Alice }, 0.0);

        Assertions.AssertThat(result.Outcome).IsEqual(DecisionOutcome.Started);
        Assertions.AssertThat(result.Target).IsEqual(Alice);
    }

    /// <summary>
    /// 同一份关系数据、同一个随机源跑两次，结果必须一致 ——
    /// 关系影响可复现，不引入不可预测的全局随机（第三阶段设计 §3.3）。
    /// </summary>
    [TestCase]
    public void RelationshipWeightedPick_IsReproducible()
    {
        var book = new RelationshipBook();
        book.Apply("seed:like", new RelationshipKey(Actor, Bob), 50);

        var first = PickTarget(book, 0.4);
        var second = PickTarget(book, 0.4);

        Assertions.AssertThat(first).IsEqual(second);
    }

    // ── 夹具 ──────────────────────────────────────────────────────────────

    private static HeroId PickTarget(RelationshipBook book, double fraction = 0.5)
        => Step(book, new[] { Alice, Bob }, fraction).Target;

    private static DecisionResult Step(
        RelationshipBook book, IReadOnlyList<HeroId> targets, double fraction)
    {
        var flow = new DecisionFlow(
            new[] { Social() }, new FractionRandom(fraction),
            targetCandidates: (_, _) => targets,
            targetWeight: WeightsFrom(book));

        return flow.Step(Hero());
    }
}
