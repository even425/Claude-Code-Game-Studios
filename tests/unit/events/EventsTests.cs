using System;
using System.Collections.Generic;
using System.Linq;
using GdUnit4;
using ClaudeCodeGameStudios.Domain.Events;

/// <summary>
/// 领域事件与杂谈的单元测试。
///
/// <para><b>本文档保护三条东西</b>：</para>
/// <list type="number">
/// <item><b>反装饰规则</b>（<c>03</c> §5.2 / 基线 §7.3）：事件只在状态确实变了时产生。
/// 声明为空的状态变化在<b>构造期</b>就被拒绝。</item>
/// <item><b>不变量 I5</b>：每条杂谈条目恰好对应一个<b>已存在</b>的事件 ID；同一事件 ID 至多一条。</item>
/// <item><b>断言 A-04（杂谈不说谎）</b>：不存在「有条目但来源不存在」或「有条目但状态未变」的条目。</item>
/// </list>
///
/// <para><b>这不是 C-01 的偶然修复，是它的常态化。</b>
/// <c>07</c> 的 C-01 是：拒工的英雄被记成「不但做完了」—— 同一条代码路径为相反的事实生成正面描述。</para>
/// </summary>
[TestSuite]
public class EventsTests
{
    private static readonly EventTypeId Started = new("HeroActionStarted");
    private static readonly EventTypeId Completed = new("HeroActionCompleted");

    private static readonly StateKey CurrentActivity = new("Hero.CurrentActivity");

    private static DomainEvent MakeEvent(string id, EventTypeId? type = null, params StateKey[] changed)
        => DomainEvent.Create(
            new EventId(id), type ?? Started, "阿黄", 10.0,
            changed.Length > 0 ? changed : new[] { CurrentActivity });

    private static ChronicleEntry Render(DomainEvent e)
        => new(e.Id, $"{e.Actor} 做了 {e.Type}", e.Actor);

    // ── 反装饰规则 ──────────────────────────────────────────────────────────

    /// <summary>
    /// 声明了「什么都没改」的事件必须在构造期被拒 ——
    /// 它就是装饰，而装饰会变成 <c>07</c> B7 那种「改了状态却什么都不影响」的条目。
    /// </summary>
    [TestCase]
    public void DomainEvent_NoStateChange_Throws_AtConstruction()
    {
        bool emptyThrew = false;
        // 直接调 Create，不用 MakeEvent —— 后者在收到空数组时会自己补上默认的
        // CurrentActivity，于是恰好把「空声明」这个被测情况屏蔽掉，测试变��空转。
        try
        {
            var unused = DomainEvent.Create(
                new EventId("e-empty"), Started, "阿黄", 10.0, Array.Empty<StateKey>());
        }
        catch (ArgumentException) { emptyThrew = true; }

        Assertions.AssertThat(emptyThrew).IsTrue();
    }

    /// <summary>重复声明同一个状态键会被去重，不影响「至少声明了一项」的要求。</summary>
    [TestCase]
    public void DomainEvent_DuplicateStateKeys_AreDeduplicated()
    {
        var e = MakeEvent("e-dup", Started, CurrentActivity, CurrentActivity, CurrentActivity);

        Assertions.AssertThat(e.ChangedStates.Count).IsEqual(1);
    }

    // ── 事件日志：幂等 ──────────────────────────────────────────────────────

    /// <summary>同一事件 ID 重复记录只生效一次 —— 基线 §13.7「不得重复应用」。</summary>
    [TestCase]
    public void EventLog_SameIdRecordedTwice_KeepsOnlyOne()
    {
        var log = new EventLog();
        var e = MakeEvent("dup");

        bool first = log.Record(e);
        bool second = log.Record(e);

        Assertions.AssertThat(first).IsTrue();
        Assertions.AssertThat(second).IsFalse();
        Assertions.AssertThat(log.Count).IsEqual(1);
    }

    /// <summary>
    /// 幂等在<b>读档往返</b>后必须仍然成立：重新记录同一批事件不应让日志变长。
    /// 这是 §13.7 那条「已结算事件不会重复」的真实场景。
    /// </summary>
    [TestCase]
    public void EventLog_ReplayingTheSameEvents_AddsNothing()
    {
        var ids = new EventIdGenerator();
        var log = new EventLog();

        var firstPass = new List<DomainEvent>();
        for (var i = 0; i < 5; i++)
        {
            var e = DomainEvent.Create(ids.Next(Started, "阿黄"), Started, "阿黄", i, new[] { CurrentActivity });
            log.Record(e);
            firstPass.Add(e);
        }
        int afterFirst = log.Count;

        // 读档后重放同一批事件
        foreach (var e in firstPass) log.Record(e);

        Assertions.AssertThat(afterFirst).IsEqual(5);
        Assertions.AssertThat(log.Count).IsEqual(5);
    }

    // ── I5 · 杂谈与事件的对应关系 ──────────────────────────────────────────

    /// <summary>
    /// 【I5】每条杂谈条目都能在日志里找到它的来源事件；且来源事件 ID 不重复。
    /// </summary>
    [TestCase]
    public void ChronicleFeed_EveryEntry_TracesToAnExistingEvent_AndSourcesAreUnique()
    {
        var log = new EventLog();
        var chronicle = new ChronicleFeed();
        var events = new[] { MakeEvent("e1"), MakeEvent("e2"), MakeEvent("e3") };
        foreach (var e in events) log.Record(e);
        foreach (var e in events) chronicle.Publish(e, log, Render);

        bool allTraceable = true;
        foreach (var entry in chronicle.Entries)
        {
            if (!log.Contains(entry.SourceEventId)) allTraceable = false;
        }
        bool unique = chronicle.PublishedSourceIds.Count == chronicle.Entries.Count;

        Assertions.AssertThat(allTraceable).IsTrue();
        Assertions.AssertThat(unique).IsTrue();
    }

    /// <summary>同一事件重复发布只产生一条条目。</summary>
    [TestCase]
    public void Chronicle_SameEventPublishedTwice_ProducesOneEntry()
    {
        var log = new EventLog();
        var chronicle = new ChronicleFeed();
        var e = MakeEvent("same");
        log.Record(e);

        chronicle.Publish(e, log, Render);
        bool second = chronicle.Publish(e, log, Render);

        Assertions.AssertThat(second).IsFalse();
        Assertions.AssertThat(chronicle.Entries.Count).IsEqual(1);
    }

    /// <summary>
    /// 拒绝为<b>未记录</b>的事件生成条目 —— 凭空生成杂谈就是编造（基线 §7.2）。
    /// </summary>
    [TestCase]
    public void Chronicle_RefusesToPublish_AnEventThatIsNotInTheLog()
    {
        var log = new EventLog();
        var chronicle = new ChronicleFeed();
        var orphan = MakeEvent("orphan");

        bool threw = false;
        try { chronicle.Publish(orphan, log, Render); }
        catch (InvalidOperationException) { threw = true; }

        Assertions.AssertThat(threw).IsTrue();
        Assertions.AssertThat(chronicle.Entries.Count).IsEqual(0);
    }

    /// <summary>渲染委托无权更换来源 ID —— 否则条目与事件脱钩，I5 就失效了。</summary>
    [TestCase]
    public void Chronicle_RejectsRender_ThatSwapsTheSourceEventId()
    {
        var log = new EventLog();
        var chronicle = new ChronicleFeed();
        var e = MakeEvent("real");
        log.Record(e);

        bool threw = false;
        try
        {
            chronicle.Publish(e, log, _ => new ChronicleEntry(new EventId("fake"), "编的"));
        }
        catch (InvalidOperationException) { threw = true; }

        Assertions.AssertThat(threw).IsTrue();
    }

    /// <summary>渲染委托返回 null 时不产生条目 —— 「有些事件不写入杂谈」的出口。</summary>
    [TestCase]
    public void Chronicle_RenderReturningNull_ProducesNoEntry()
    {
        var log = new EventLog();
        var chronicle = new ChronicleFeed();
        var e = MakeEvent("skipme");
        log.Record(e);

        bool published = chronicle.Publish(e, log, _ => null);

        Assertions.AssertThat(published).IsFalse();
        Assertions.AssertThat(chronicle.Entries.Count).IsEqual(0);
    }

    /// <summary>
    /// 杂谈条目自身也必须有来源 ID —— 没有来源的条目在构造期就该被拒。
    /// </summary>
    [TestCase]
    public void ChronicleEntry_WithoutSourceEventId_Throws()
    {
        bool threw = false;
        try { var unused = new ChronicleEntry(new EventId(""), "没有来源的一句话"); }
        catch (ArgumentException) { threw = true; }

        Assertions.AssertThat(threw).IsTrue();
    }

    // ── A-04 · 杂谈不说谎 ──────────────────────────────────────────────────

    /// <summary>
    /// 【A-04】**每一条**杂谈条目，其来源事件都必须声明了至少一项状态变化。
    /// <para>反装饰规则已在构造期保证单条事件，这里对<b>整批条目</b>再验一次 ——
    /// 防止将来某条渲染路径绕过 <see cref="DomainEvent.Create"/>。</para>
    /// </summary>
    [TestCase]
    public void Chronicle_NoEntryExists_WhoseSourceEventDeclaredNoStateChange()
    {
        var log = new EventLog();
        var chronicle = new ChronicleFeed();
        var events = new[] { MakeEvent("a"), MakeEvent("b"), MakeEvent("c") };
        foreach (var e in events) log.Record(e);
        foreach (var e in events) chronicle.Publish(e, log, Render);

        bool allNonEmpty = chronicle.Entries.All(entry =>
        {
            var source = log.Find(entry.SourceEventId);
            return source is not null && source.ChangedStates.Count > 0;
        });

        Assertions.AssertThat(allNonEmpty).IsTrue();
        Assertions.AssertThat(chronicle.Entries.Count).IsEqual(3);
    }

    // ── 事件 ID 的确定性 ────────────────────────────────────────────────────

    /// <summary>
    /// 相同的事件序列产生相同的 ID —— 幂等判断只有在 ID 可复现时才有意义。
    /// </summary>
    [TestCase]
    public void EventIdGenerator_IsDeterministic_AcrossRuns()
    {
        List<string> Run()
        {
            var gen = new EventIdGenerator();
            var outp = new List<string>();
            outp.Add(gen.Next(Started, "阿黄").Value);
            outp.Add(gen.Next(Started, "阿黄").Value);
            outp.Add(gen.Next(Completed, "阿黄").Value);
            outp.Add(gen.Next(Started, "夸九").Value);
            return outp;
        }

        Assertions.AssertThat(string.Join("|", Run())).IsEqual(string.Join("|", Run()));
    }

    /// <summary>不同参与者 / 不同类型的计数器彼此独立。</summary>
    [TestCase]
    public void EventIdGenerator_CountersAreIndependentPerTypeAndActor()
    {
        var gen = new EventIdGenerator();
        var a1 = gen.Next(Started, "阿黄");
        var b1 = gen.Next(Started, "夸九");
        var c1 = gen.Next(Completed, "阿黄");
        var a2 = gen.Next(Started, "阿黄");

        var ids = new[] { a1.Value, b1.Value, c1.Value, a2.Value };
        Assertions.AssertThat(ids.Distinct().Count()).IsEqual(4);
        // 同类型同参与者第二次必须带序号 1
        Assertions.AssertThat(a2.Value).IsEqual("HeroActionStarted:阿黄:1");
    }

    /// <summary>Restore 之后 ID 序列继续 —— 读档后重放不会与存档内的 ID 撞车。</summary>
    [TestCase]
    public void EventIdGenerator_Restore_ResumesTheSequence()
    {
        var gen = new EventIdGenerator();
        gen.Next(Started, "阿黄");
        gen.Next(Started, "阿黄");

        gen.Restore(Started, "阿黄", 100);
        var afterRestore = gen.Next(Started, "阿黄");

        Assertions.AssertThat(afterRestore.Value).IsEqual("HeroActionStarted:阿黄:100");
    }
}
