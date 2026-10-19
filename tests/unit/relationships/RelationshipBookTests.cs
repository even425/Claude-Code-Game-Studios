using System;
using System.Collections.Generic;
using GdUnit4;
using ClaudeCodeGameStudios.Domain.Heroes;
using ClaudeCodeGameStudios.Domain.Relationships;

/// <summary>
/// 关系领域模型 —— M1 的规则契约。
/// </summary>
/// <remarks>
/// <para><b>依据：</b>基线 §9.3、<c>05</c> 不变量 I8（有向）、<c>07</c> D-036
/// （单值 ±100、社交 +2、副本 +3、阶段阈值、目标权重）。</para>
/// <para><b>这些测试守什么：</b>方向性、范围、幂等、原子恢复、阶段派生 ——
/// 它们都是后续 M2–M6 的地基；地基一旦松，上层每个里程碑都会漂。</para>
/// </remarks>
[TestSuite]
public class RelationshipBookTests
{
    private static HeroId H(string id) => new(id);

    private static RelationshipKey Key(string from, string to) => new(H(from), H(to));

    // ── 创建与方向性 ──────────────────────────────────────────────────────

    /// <summary>一次社交变化会建立 A→B 的有向关系。</summary>
    [TestCase]
    public void Apply_CreatesDirectionalRelationship()
    {
        var book = new RelationshipBook();

        var change = book.Apply("social:h-0->h-1@10", Key("h-0", "h-1"), RelationshipRules.SocialDelta);

        Assertions.AssertThat(change.Applied).IsTrue();
        Assertions.AssertThat(change.Changed).IsTrue();
        Assertions.AssertThat(change.After.Value).IsEqual(2);
        Assertions.AssertThat(book.Count).IsEqual(1);
        Assertions.AssertThat(book.ValueOf(H("h-0"), H("h-1"))).IsEqual(2);
    }

    /// <summary>
    /// 【I8】**有向**：A 对 B 的关系变化不得影响 B 对 A。
    /// </summary>
    [TestCase]
    public void Apply_IsDirectional_AsymmetryIsExpressible()
    {
        var book = new RelationshipBook();

        book.Apply("social:h-0->h-1@10", Key("h-0", "h-1"), 5);

        Assertions.AssertThat(book.ValueOf(H("h-0"), H("h-1"))).IsEqual(5);
        Assertions.AssertThat(book.ValueOf(H("h-1"), H("h-0"))).IsEqual(0);
    }

    // ── 非法输入 ──────────────────────────────────────────────────────────

    /// <summary>空英雄 ID 在键构造期即被拒绝。</summary>
    [TestCase]
    public void Key_RejectsEmptyHeroIds()
    {
        bool threw = false;
        try { _ = Key("", "h-1"); }
        catch (ArgumentException) { threw = true; }

        Assertions.AssertThat(threw).IsTrue();
    }

    /// <summary>自己与自己不能建立关系。</summary>
    [TestCase]
    public void Key_RejectsSelfRelation()
    {
        bool threw = false;
        try { _ = new RelationshipKey(H("h-0"), H("h-0")); }
        catch (ArgumentException) { threw = true; }

        Assertions.AssertThat(threw).IsTrue();
    }

    /// <summary>
    /// <c>default(RelationshipKey)</c> 绕过了构造函数 —— 唯一写者必须再挡一次，
    /// 且失败后不能留下任何改动。
    /// </summary>
    [TestCase]
    public void Apply_RejectsDefaultKey_WithoutMutatingState()
    {
        var book = new RelationshipBook();

        bool threw = false;
        try { book.Apply("k", default, 2); }
        catch (ArgumentException) { threw = true; }

        Assertions.AssertThat(threw).IsTrue();
        Assertions.AssertThat(book.Count).IsEqual(0);
        Assertions.AssertThat(book.AppliedKeys.Count).IsEqual(0);
    }

    // ── 幂等 ──────────────────────────────────────────────────────────────

    /// <summary>同一业务键第二次应用：不接受、不改变、不重复计数。</summary>
    [TestCase]
    public void Apply_SameInteractionKey_IsIdempotent()
    {
        var book = new RelationshipBook();
        var key = Key("h-0", "h-1");

        book.Apply("social:h-0->h-1@10", key, RelationshipRules.SocialDelta);
        var second = book.Apply("social:h-0->h-1@10", key, RelationshipRules.SocialDelta);

        Assertions.AssertThat(second.Applied).IsFalse();
        Assertions.AssertThat(second.Changed).IsFalse();
        Assertions.AssertThat(book.ValueOf(H("h-0"), H("h-1"))).IsEqual(2);
    }

    /// <summary>空业务键拒绝 —— 没有键就没有幂等依据。</summary>
    [TestCase]
    public void Apply_RejectsEmptyInteractionKey()
    {
        var book = new RelationshipBook();

        bool threw = false;
        try { book.Apply("  ", Key("h-0", "h-1"), 2); }
        catch (ArgumentException) { threw = true; }

        Assertions.AssertThat(threw).IsTrue();
        Assertions.AssertThat(book.Count).IsEqual(0);
    }

    // ── 范围 ──────────────────────────────────────────────────────────────

    /// <summary>超出上界时停在 +100，并报告「接受但未改变」。</summary>
    [TestCase]
    public void Apply_ClampsAtUpperBound_AndReportsNoChange()
    {
        var book = new RelationshipBook();
        var key = Key("h-0", "h-1");

        book.Apply("big", key, 1000);
        var atCap = book.Apply("again", key, 2);

        Assertions.AssertThat(book.ValueOf(H("h-0"), H("h-1"))).IsEqual(Relationship.MaxValue);
        Assertions.AssertThat(atCap.Applied).IsTrue();
        Assertions.AssertThat(atCap.Changed).IsFalse();
    }

    /// <summary>下界同理：停在 -100。</summary>
    [TestCase]
    public void Apply_ClampsAtLowerBound()
    {
        var book = new RelationshipBook();

        book.Apply("big", Key("h-0", "h-1"), -1000);

        Assertions.AssertThat(book.ValueOf(H("h-0"), H("h-1"))).IsEqual(Relationship.MinValue);
    }

    /// <summary>
    /// <c>WithDelta</c> 用 <c>long</c> 求和：<c>int.MaxValue</c> 也必须 clamp，
    /// 不能溢出成一个负数。
    /// </summary>
    [TestCase]
    public void WithDelta_DoesNotOverflow()
    {
        Assertions.AssertThat(new Relationship(100).WithDelta(int.MaxValue).Value).IsEqual(100);
        Assertions.AssertThat(new Relationship(-100).WithDelta(int.MinValue).Value).IsEqual(-100);
    }

    /// <summary>构造期越界值直接拒绝（损坏存档不能静默变成合法值）。</summary>
    [TestCase]
    public void Relationship_Constructor_RejectsOutOfRange()
    {
        bool threw = false;
        try { _ = new Relationship(101); }
        catch (ArgumentOutOfRangeException) { threw = true; }

        Assertions.AssertThat(threw).IsTrue();
    }

    // ── 快照 / 恢复 ───────────────────────────────────────────────────────

    /// <summary>快照 → 恢复：关系值、方向与业务键全部一致。</summary>
    [TestCase]
    public void Snapshot_Restore_RoundTripsExactly()
    {
        var book = new RelationshipBook();
        book.Apply("social:h-0->h-1@1", Key("h-0", "h-1"), 2);
        book.Apply("social:h-1->h-0@2", Key("h-1", "h-0"), -3);

        var restored = new RelationshipBook();
        restored.Restore(book.Snapshot(), book.AppliedKeys);

        Assertions.AssertThat(restored.Count).IsEqual(2);
        Assertions.AssertThat(restored.ValueOf(H("h-0"), H("h-1"))).IsEqual(2);
        Assertions.AssertThat(restored.ValueOf(H("h-1"), H("h-0"))).IsEqual(-3);
        Assertions.AssertThat(restored.AppliedKeys.Count).IsEqual(2);
    }

    /// <summary>
    /// 恢复是<b>全有或全无</b>：有一条非法记录时，原关系表保持原样。
    /// </summary>
    [TestCase]
    public void Restore_IsAllOrNothing_OnInvalidRecord()
    {
        var book = new RelationshipBook();
        book.Apply("keep", Key("h-0", "h-1"), 2);

        var bad = new List<RelationshipRecord>
        {
            new(Key("h-2", "h-3"), 5),
            new(Key("h-4", "h-5"), 999),   // 越界
        };

        bool threw = false;
        try { book.Restore(bad, Array.Empty<string>()); }
        catch (ArgumentOutOfRangeException) { threw = true; }

        Assertions.AssertThat(threw).IsTrue();
        Assertions.AssertThat(book.Count).IsEqual(1);
        Assertions.AssertThat(book.ValueOf(H("h-0"), H("h-1"))).IsEqual(2);
    }

    /// <summary>重复关系记录在恢复期拒绝（枚举同一段关系两次 = 数据损坏）。</summary>
    [TestCase]
    public void Restore_RejectsDuplicateRelationships()
    {
        var book = new RelationshipBook();
        var records = new List<RelationshipRecord>
        {
            new(Key("h-0", "h-1"), 1),
            new(Key("h-0", "h-1"), 2),
        };

        bool threw = false;
        try { book.Restore(records, Array.Empty<string>()); }
        catch (ArgumentException) { threw = true; }

        Assertions.AssertThat(threw).IsTrue();
        Assertions.AssertThat(book.Count).IsEqual(0);
    }

    // ── 阶段派生 ──────────────────────────────────────────────────────────

    /// <summary>阶段只由关系值派生，边界值与 P3-D09 一致。</summary>
    [TestCase]
    public void StageOf_MapsBoundaries()
    {
        Assertions.AssertThat(RelationshipRules.StageOf(-20)).IsEqual(RelationshipStage.Hostile);
        Assertions.AssertThat(RelationshipRules.StageOf(-1)).IsEqual(RelationshipStage.Cold);
        Assertions.AssertThat(RelationshipRules.StageOf(0)).IsEqual(RelationshipStage.Neutral);
        Assertions.AssertThat(RelationshipRules.StageOf(1)).IsEqual(RelationshipStage.Acquainted);
        Assertions.AssertThat(RelationshipRules.StageOf(19)).IsEqual(RelationshipStage.Acquainted);
        Assertions.AssertThat(RelationshipRules.StageOf(20)).IsEqual(RelationshipStage.Close);
    }

    /// <summary>跨档的精确点：普通(0) → 熟识(1) 必须报告 StageChanged，0 → 0 不报。</summary>
    [TestCase]
    public void StageChanged_OnlyWhenCrossingABand()
    {
        var book = new RelationshipBook();
        var key = Key("h-0", "h-1");

        var first = book.Apply("s1", key, 1);     // 0 → 1：普通 → 熟识
        var second = book.Apply("s2", key, 1);    // 1 → 2：同档

        Assertions.AssertThat(first.StageChanged).IsTrue();
        Assertions.AssertThat(second.StageChanged).IsFalse();
    }

    // ── 关系 → 行为权重 ──────────────────────────────────────────────────

    /// <summary>
    /// 目标权重恒正：再差的关系也只是接近 0，永不等于 0
    /// （关系只改概率、不改合法性，P3-D06 / CR-1）。
    /// </summary>
    [TestCase]
    public void TargetWeight_IsAlwaysPositive()
    {
        Assertions.AssertThat(RelationshipRules.TargetWeight(0)).IsEqual(1.0);
        Assertions.AssertThat(RelationshipRules.TargetWeight(100)).IsEqual(3.0);
        Assertions.AssertThat(RelationshipRules.TargetWeight(-100)).IsEqual(0.1);
        Assertions.AssertThat(RelationshipRules.TargetWeight(-1000)).IsGreater(0.0);
    }

    /// <summary>关系越好权重越高：单调、可解释、可测试。</summary>
    [TestCase]
    public void TargetWeight_IsMonotonic()
    {
        double disliked = RelationshipRules.TargetWeight(-50);
        double neutral = RelationshipRules.TargetWeight(0);
        double liked = RelationshipRules.TargetWeight(50);

        Assertions.AssertThat(disliked).IsLess(neutral);
        Assertions.AssertThat(neutral).IsLess(liked);
    }
}
