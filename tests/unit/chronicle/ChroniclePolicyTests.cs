using System;
using System.Collections.Generic;
using GdUnit4;
using ClaudeCodeGameStudios.Domain.Events;
using ClaudeCodeGameStudios.Presentation;

/// <summary>
/// 日常劳动（采集 / 建屋）不进村庄杂谈 —— 守住 2026-10-19 的开发者决定（<c>07</c> D-034）。
/// </summary>
/// <remarks>
/// <para><b>这不是「不产生事件」</b>：基线 §7.2 要求杂谈条目能追溯到事件，
/// §7.4 的 C1/C2 与存档、状态追溯都依赖事件日志。要少的是<b>玩家读到的条数</b>，
/// 所以过滤发生在 <c>ChronicleFeed.Publish</c> 的渲染出口（返回 null 不产生条目），
/// 事件本身仍然写进日志。</para>
/// <para><b>为什么在这里合成一条管道</b>：真正的接线在 <c>Main.PublishEvent</c>，
/// 它需要整棵场景树，单测起不来。本测试用与接线相同的顺序
/// （<c>Record → Publish → render</c>）验证契约：隐藏项不产条目但留在日志，
/// 可见项照常产条目。接线层再由真机运行验证（材料照涨、杂谈干净）。</para>
/// </remarks>
[TestSuite]
public class ChroniclePolicyTests
{
    private static readonly EventTypeId Completed = new("HeroActionCompleted");
    private static readonly StateKey CurrentActivity = new("Hero.CurrentActivity");

    /// <summary>造一条「某英雄做完了某项活动」的事件，形状与 DailyLife 发的一致。</summary>
    private static DomainEvent CompletedEvent(string activity, string actor = "阿黄")
        => DomainEvent.Create(
            new EventId($"HeroActionCompleted:{actor}:{activity}"),
            Completed, actor, 0.0,
            new[] { CurrentActivity },
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["speaker"] = actor,
                ["activity"] = activity,
            });

    /// <summary>与 <c>Main.PublishEvent</c> 相同的展示判定与渲染（返回 null = 不产条目）。</summary>
    private static ChronicleEntry RenderIfWorthShowing(DomainEvent e)
    {
        if (!e.Payload.TryGetValue("activity", out var activity)) return null;
        if (!ChroniclePolicy.IsWorthShowing(activity)) return null;

        return new ChronicleEntry(e.Id, $"做完了{activity}。", e.Actor);
    }

    // ── 取舍规则本身 ──────────────────────────────────────────────────────

    /// <summary>采集与建屋 = 日常劳动，不该逐条进杂谈。</summary>
    [TestCase]
    public void RoutineLabor_IsNotWorthShowing()
    {
        Assertions.AssertThat(ChroniclePolicy.IsWorthShowing("采集")).IsFalse();
        Assertions.AssertThat(ChroniclePolicy.IsWorthShowing("建屋")).IsFalse();
    }

    /// <summary>
    /// 黑名单语义：没登记过的活动默认<b>显示</b>。
    /// 白名单会让「忘了登记」变成静默消失 —— 比噪音更难查。
    /// </summary>
    [TestCase]
    public void UnknownActivity_DefaultsToWorthShowing()
    {
        Assertions.AssertThat(ChroniclePolicy.IsWorthShowing("摆摊")).IsTrue();
        Assertions.AssertThat(ChroniclePolicy.IsWorthShowing("社交")).IsTrue();
        Assertions.AssertThat(ChroniclePolicy.IsWorthShowing("采集2")).IsTrue();   // 不做前缀匹配
    }

    // ── 管道契约 ──────────────────────────────────────────────────────────

    /// <summary>
    /// 核心断言：日常劳动<b>仍进事件日志</b>（世界照样发生），但<b>不产生杂谈条目</b>。
    /// </summary>
    [TestCase]
    public void RoutineLabor_IsLoggedButProducesNoChronicleEntry()
    {
        var log = new EventLog();
        var feed = new ChronicleFeed();
        var e = CompletedEvent("采集");
        log.Record(e);

        bool published = feed.Publish(e, log, RenderIfWorthShowing);

        Assertions.AssertThat(log.Count).IsEqual(1);            // 事件仍被记录
        Assertions.AssertThat(log.Contains(e.Id)).IsTrue();     // 仍可追溯
        Assertions.AssertThat(published).IsFalse();             // 不产条目
        Assertions.AssertThat(feed.Entries.Count).IsEqual(0);
    }

    /// <summary>对照组：同一条管道上，值得显示的活动照常产生可溯源的条目。</summary>
    [TestCase]
    public void WorthShowingActivity_StillProducesATraceableEntry()
    {
        var log = new EventLog();
        var feed = new ChronicleFeed();
        var e = CompletedEvent("摆摊");
        log.Record(e);

        bool published = feed.Publish(e, log, RenderIfWorthShowing);

        Assertions.AssertThat(published).IsTrue();
        Assertions.AssertThat(feed.Entries.Count).IsEqual(1);
        Assertions.AssertThat(feed.Entries[0].SourceEventId).IsEqual(e.Id);
    }

    /// <summary>
    /// 批量场景（37 个人一天几十条劳作）：日志条数照涨，杂谈条目数保持 0。
    /// 这正是玩家截图里「杂谈被劳作流水账刷屏」的反面。
    /// </summary>
    [TestCase]
    public void ManyRoutineLaborEvents_FloodTheLog_ButNotTheChronicle()
    {
        var log = new EventLog();
        var feed = new ChronicleFeed();

        for (int i = 0; i < 40; i++)
        {
            var e = CompletedEvent(i % 2 == 0 ? "采集" : "建屋", $"英雄{i:00}");
            log.Record(e);
            feed.Publish(e, log, RenderIfWorthShowing);
        }

        Assertions.AssertThat(log.Count).IsEqual(40);
        Assertions.AssertThat(feed.Entries.Count).IsEqual(0);
    }
}
