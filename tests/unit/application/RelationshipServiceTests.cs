using System;
using System.Collections.Generic;
using GdUnit4;
using ClaudeCodeGameStudios.Application;
using ClaudeCodeGameStudios.Domain.Heroes;
using ClaudeCodeGameStudios.Domain.Relationships;

/// <summary>
/// 第三阶段 M3：关系变化的应用入口 —— 双向规则与幂等键。
/// </summary>
/// <remarks>
/// <para><b>依据：</b><c>07</c> D-036（P3-D04/D08）。社交双向各 +2；
/// 同队副本每个有序对 +3；重复处理同一业务键不得二次加关系。</para>
/// <para><b>为什么测应用层而不是 Main</b>：键格式与双向规则是业务规则，
/// 放在这里可以脱离场景树验证；<c>Main</c> 只负责把「行为发生了」翻译成调用。</para>
/// </remarks>
[TestSuite]
public class RelationshipServiceTests
{
    private static HeroId H(string id) => new(id);

    private static RelationshipBook NewBook(out RelationshipService service)
    {
        var book = new RelationshipBook();
        service = new RelationshipService(book);
        return book;
    }

    // ── 社交 ──────────────────────────────────────────────────────────────

    /// <summary>一次社交：A→B 与 B→A 各自 +2（有向、独立记录）。</summary>
    [TestCase]
    public void Socialize_AppliesBothDirections()
    {
        var book = NewBook(out var service);

        var touched = service.Socialize(H("h-0"), H("h-1"), simTime: 10.0);

        Assertions.AssertThat(touched.Changed).IsTrue();
        Assertions.AssertThat(book.ValueOf(H("h-0"), H("h-1")))
            .IsEqual(RelationshipRules.SocialDelta);
        Assertions.AssertThat(book.ValueOf(H("h-1"), H("h-0")))
            .IsEqual(RelationshipRules.SocialDelta);
        Assertions.AssertThat(book.Count).IsEqual(2);
    }

    /// <summary>同一时刻同一对再次调用 = 重复处理 → 不再加。</summary>
    [TestCase]
    public void Socialize_SameSimTime_IsIdempotent()
    {
        var book = NewBook(out var service);

        service.Socialize(H("h-0"), H("h-1"), simTime: 10.0);
        var second = service.Socialize(H("h-0"), H("h-1"), simTime: 10.0);

        Assertions.AssertThat(second.Changed).IsFalse();
        Assertions.AssertThat(book.ValueOf(H("h-0"), H("h-1"))).IsEqual(2);
    }

    /// <summary>不同时刻是两次真实社交 —— 应该继续加。</summary>
    [TestCase]
    public void Socialize_DifferentSimTimes_AppliesAgain()
    {
        var book = NewBook(out var service);

        service.Socialize(H("h-0"), H("h-1"), 10.0);
        service.Socialize(H("h-0"), H("h-1"), 20.0);

        Assertions.AssertThat(book.ValueOf(H("h-0"), H("h-1"))).IsEqual(4);
    }

    // ── 同队副本合作 ──────────────────────────────────────────────────────

    /// <summary>3 人同队：3 对 × 双向 = 6 条有向记录，各 +3。</summary>
    [TestCase]
    public void Cooperate_AppliesEveryPairBothDirections()
    {
        var book = NewBook(out var service);
        var members = new[] { H("h-0"), H("h-1"), H("h-2") };

        var touched = service.Cooperate("run-1", members);

        Assertions.AssertThat(touched.PairsChanged).IsEqual(3);
        Assertions.AssertThat(book.Count).IsEqual(6);
        Assertions.AssertThat(book.ValueOf(H("h-0"), H("h-2")))
            .IsEqual(RelationshipRules.CooperationDelta);
        Assertions.AssertThat(book.ValueOf(H("h-2"), H("h-0")))
            .IsEqual(RelationshipRules.CooperationDelta);
    }

    /// <summary>同一副本重复结算 → 关系一分不加（I4 / P3-D08）。</summary>
    [TestCase]
    public void Cooperate_SameRunId_IsIdempotent()
    {
        var book = NewBook(out var service);
        var members = new[] { H("h-0"), H("h-1") };

        service.Cooperate("run-1", members);
        var again = service.Cooperate("run-1", members);

        Assertions.AssertThat(again.Changed).IsFalse();
        Assertions.AssertThat(book.ValueOf(H("h-0"), H("h-1"))).IsEqual(3);
    }

    /// <summary>单人队伍没有「对」，不产生任何变化（也就不产生事件）。</summary>
    [TestCase]
    public void Cooperate_OneMember_ChangesNothing()
    {
        var book = NewBook(out var service);

        var touched = service.Cooperate("run-1", new[] { H("h-0") });

        Assertions.AssertThat(touched.Changed).IsFalse();
        Assertions.AssertThat(book.Count).IsEqual(0);
    }

    /// <summary>没有 runId 就没有幂等依据 —— 构造错误必须炸。</summary>
    [TestCase]
    public void Cooperate_EmptyRunId_Throws()
    {
        NewBook(out var service);

        bool threw = false;
        try { service.Cooperate(" ", new[] { H("h-0"), H("h-1") }); }
        catch (ArgumentException) { threw = true; }

        Assertions.AssertThat(threw).IsTrue();
    }

    /// <summary>空英雄 ID 混在成员里被跳过，而不是把关系写到「无」上。</summary>
    [TestCase]
    public void Cooperate_SkipsInvalidMemberIds()
    {
        var book = NewBook(out var service);

        var touched = service.Cooperate("run-1", new[] { H("h-0"), new HeroId("") });

        Assertions.AssertThat(touched.Changed).IsFalse();
        Assertions.AssertThat(book.Count).IsEqual(0);
    }
}
