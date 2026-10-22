using System;
using System.Collections.Generic;
using System.Linq;
using GdUnit4;
using ClaudeCodeGameStudios.Application;
using ClaudeCodeGameStudios.Domain.Combat;
using ClaudeCodeGameStudios.Domain.WorldEvents;

namespace ClaudeCodeGameStudios.Tests.Application;

/// <summary>
/// <b>大事件框架</b>（基线 §11.6 的 V1–V4）。
/// </summary>
/// <remarks>
/// <para><b>它守的是四条验收</b>：
/// <list type="number">
/// <item><b>V1</b> 事件按模板走完生命周期（合法阶段迁移）。</item>
/// <item><b>V2</b> <b>后果不被重复应用</b>（结算后存档、读档，再结算也不重发）。</item>
/// <item><b>V3</b> 事件可预告（预兆阶段在进行中之前，且能读到）。</item>
/// <item><b>V4</b> 事件不越权改数据（后果由对应系统应用，框架不自己写）。</item>
/// </list>
/// <para>⚠️ <b>为什么「两个实例」是这些测试的前提</b>（基线 §11.5）：
/// 「用一个实例抽象出的框架，无法区分「通用」与「该实例专属」」
/// ⇒ 这里刻意用<b>形状不同</b>的兽潮与商队：
/// 商队<b>没有战斗、阶段更少</b> ⇒ 若框架把「进行中 ⇒ 打一场」写死了，它跑不起来。</para>
/// </remarks>
[TestSuite]
public class WorldEventTests
{
    private static (WorldEventBook Book, List<string> Applied) NewBook()
    {
        var book = new WorldEventBook();
        var applied = new List<string>();
        book.Register(WorldEventTemplates.BeastTide(
            periodDays: 7, new FormulaCombat(), new FormulaCombat()));
        book.Register(WorldEventTemplates.Caravan(periodDays: 11));
        return (book, applied);
    }

    private static void Apply(List<string> applied, string key, string description)
        => applied.Add($"{key}:{description}");

    /// <summary>
    /// 把实例推进到「结算」阶段。
    /// </summary>
    /// <remarks>
    /// ⚠️ **这个循环必须有护栏**（实测踩过）：没有护栏时，一旦阶段推进被改坏
    /// （例如变异成「一步跳到结束」），实例的阶段就<b>永远等不到 Settlement</b>
    /// ⇒ 这个循环<b>挂死</b>而不是<b>跑红</b>
    /// ⇒ 变异矩阵那一档变成「超时」，不是「看红」⇒ 等于<b>没验</b>。
    /// ⇒ 护栏 + 断言 = 改坏了会**明确失败**，而不是把整轮验证拖死。
    /// </remarks>
    private static void AdvanceToSettlement(WorldEventBook book, WorldEventInstance instance)
    {
        int guard = 0;
        while (instance.Phase != WorldEventPhase.Settlement && guard++ < 20)
        {
            book.Advance(instance.Id, 0.5);
        }

        Assertions.AssertThat(instance.Phase).IsEqual(WorldEventPhase.Settlement);
    }

    // ── V1：生命周期 ──────────────────────────────────────────────────────

    /// <summary>V1：实例按<b>模板声明的阶段</b>推进，且最后停在「结束」。</summary>
    [TestCase]
    public void Instance_AdvancesThroughTheTemplatePhases()
    {
        var (book, _) = NewBook();
        var instance = book.TriggerByPeriod(
            simDay: 7, now: 0.0, participants: new[] { "h-1" },
            (t, day) => $"inst-{t.Id}-{day}");
        Assertions.AssertThat(instance).IsNotNull();

        // 一路推进到结束。
        WorldEventInstance? cur = instance;
        int guard = 0;
        while (cur!.Phase != WorldEventPhase.Finished && guard++ < 20)
        {
            cur = book.Advance(cur.Id, progress: 0.5);
        }

        Assertions.AssertThat(cur!.Phase).IsEqual(WorldEventPhase.Finished);
        // 已经结束 ⇒ 再推进不动（终结态）。
        Assertions.AssertThat(book.Advance(cur.Id, 1.0)!.Phase)
            .IsEqual(WorldEventPhase.Finished);
    }

    /// <summary>
    /// V1 的另一半：<b>商队的阶段形状与兽潮不同</b>（少两个阶段）⇒
    /// 框架是「按模板推进」的，不是「按某个事件写死」。
    /// </summary>
    [TestCase]
    public void DifferentTemplates_HaveDifferentPhaseShapes()
    {
        var (book, _) = NewBook();
        var beast = book.Template(WorldEventTemplates.BeastTideId)!;
        var caravan = book.Template(WorldEventTemplates.CaravanId)!;

        Assertions.AssertThat(caravan.Phases.Count < beast.Phases.Count).IsTrue();
        // 商队**没有战斗** ⇒ 它的阶段里压根没有「准备」这一段。
        Assertions.AssertThat(caravan.Phases.Contains(WorldEventPhase.Preparation)).IsFalse();
    }

    /// <summary>未到周期不触发。</summary>
    [TestCase]
    public void Trigger_OnlyFiresOnThePeriod()
    {
        var (book, _) = NewBook();

        var instance = book.TriggerByPeriod(
            simDay: 3, now: 0.0, participants: new[] { "h-1" },
            (t, day) => $"inst-{t.Id}-{day}");

        Assertions.AssertThat(instance).IsNull();
        Assertions.AssertThat(book.Instances.Count).IsEqual(0);
    }

    // ── V2：幂等 ─────────────────────────────────────────────────────────

    /// <summary>
    /// V2：<b>同一个后果不会被应用两次</b>（结算后读档再结算，也不重发）。
    /// </summary>
    [TestCase]
    public void Consequences_AreAppliedOnlyOnce()
    {
        var (book, applied) = NewBook();
        var instance = book.TriggerByPeriod(
            simDay: 7, now: 0.0, participants: new[] { "h-1" },
            (t, day) => $"inst-{t.Id}-{day}")!;

        // 推进到「结算」阶段。
        AdvanceToSettlement(book, instance);

        var outcome = book.Settle(instance.Id, (k, d) => Apply(applied, k, d));
        Assertions.AssertThat(outcome).IsNotNull();
        int afterFirst = applied.Count;
        Assertions.AssertThat(afterFirst > 0).IsTrue();

        // **再结算一次**（模拟读档后重放）⇒ 后果**不得**再应用。
        var again = book.Settle(instance.Id, (k, d) => Apply(applied, k, d));

        Assertions.AssertThat(again).IsNotNull();          // 结果仍能算出来
        Assertions.AssertThat(applied.Count).IsEqual(afterFirst);  // 但后果没有重发
    }

    /// <summary>
    /// V2 的**存档往返**版本：把实例簿导出再导入 ⇒ 已应用的后果**跟着走** ⇒ 仍不重发。
    /// </summary>
    [TestCase]
    public void Consequences_SurviveSaveAndLoadWithoutDuplicating()
    {
        var (book, applied) = NewBook();
        var instance = book.TriggerByPeriod(
            simDay: 7, now: 0.0, participants: new[] { "h-1" },
            (t, day) => $"inst-{t.Id}-{day}")!;
        AdvanceToSettlement(book, instance);
        book.Settle(instance.Id, (k, d) => Apply(applied, k, d));
        int afterFirst = applied.Count;

        // 存档 → 读档（换一个簿）。
        var snapshot = book.Snapshot();
        var (book2, applied2) = NewBook();
        book2.Restore(snapshot);

        // 读档后再结算一次 ⇒ 后果不得重发。
        book2.Settle(instance.Id, (k, d) => Apply(applied2, k, d));

        Assertions.AssertThat(applied2.Count).IsEqual(0);   // 一个都没再应用
        Assertions.AssertThat(applied.Count).IsEqual(afterFirst);
    }

    // ── V3：可预告 ───────────────────────────────────────────────────────

    /// <summary>V3：<b>预兆阶段在进行中之前</b>，且玩家能读到。</summary>
    [TestCase]
    public void Omen_ComesBeforeOngoing_AndIsReadable()
    {
        var (book, _) = NewBook();
        var instance = book.TriggerByPeriod(
            simDay: 7, now: 0.0, participants: new[] { "h-1" },
            (t, day) => $"inst-{t.Id}-{day}")!;

        // 触发后第一阶段就是「预兆」⇒ 一开始就能读到「要来了」。
        Assertions.AssertThat(instance.Phase).IsEqual(WorldEventPhase.Omen);

        // 预兆一定排在「进行中」之前（两个模板都要成立）。
        foreach (var template in book.Templates)
        {
            int omen = template.Phases.ToList().IndexOf(WorldEventPhase.Omen);
            int ongoing = template.Phases.ToList().IndexOf(WorldEventPhase.Ongoing);
            if (omen < 0 || ongoing < 0) continue;
            Assertions.AssertThat(omen < ongoing).IsTrue();
        }
    }

    // ── V4：不越权 ───────────────────────────────────────────────────────

    /// <summary>
    /// V4：<b>框架不自己改状态</b> —— 后果**只**通过注入的委托出去。
    /// </summary>
    /// <remarks>
    /// ⚠️ 这条怎么验：框架里<b>没有任何</b>村庄/钱包/关系的写入口
    /// ⇒ 它**只能**调用注入的委托 ⇒ 后果的应用必然经由对应系统。
    /// ⇒ 这里断言「委托被调用的次数 == 后果条数」——
    /// 若框架绕过委托自己改了状态，计数就对不上。
    /// </remarks>
    [TestCase]
    public void Consequences_GoOnlyThroughTheInjectedApplier()
    {
        var (book, applied) = NewBook();
        var instance = book.TriggerByPeriod(
            simDay: 11, now: 0.0, participants: new[] { "h-1", "h-2" },
            (t, day) => $"inst-{t.Id}-{day}")!;

        // 商队（11 天周期）。
        AdvanceToSettlement(book, instance);
        var outcome = book.Settle(instance.Id, (k, d) => Apply(applied, k, d))!;

        Assertions.AssertThat(applied.Count).IsEqual(outcome.Consequences.Count);
    }

    // ── 边界 ─────────────────────────────────────────────────────────────

    /// <summary>
    /// <b>结算只发生在「结算阶段」</b>（基线 §11.2 的阶段语义）。
    /// </summary>
    /// <remarks>
    /// ⚠️ 这条守的是「阶段不是装饰」：若结算不看阶段，
    /// 事件在**预兆**阶段就算完了 ⇒ 后果会在事件还没开始时落地。
    /// </remarks>
    [TestCase]
    public void Settle_OnlyHappensInTheSettlementPhase()
    {
        var (book, applied) = NewBook();
        var instance = book.TriggerByPeriod(
            simDay: 7, now: 0.0, participants: new[] { "h-1" },
            (t, day) => $"inst-{t.Id}-{day}")!;

        // 刚触发（预兆阶段）⇒ 结算**不发生**。
        var tooEarly = book.Settle(instance.Id, (k, d) => Apply(applied, k, d));

        Assertions.AssertThat(tooEarly).IsNull();
        Assertions.AssertThat(applied.Count).IsEqual(0);
    }

    /// <summary>同一个模板不能注册两次（否则「按模板」就不唯一了）。</summary>
    [TestCase]
    public void Register_DuplicateTemplate_Throws()
    {
        var book = new WorldEventBook();
        book.Register(WorldEventTemplates.Caravan(10));

        bool threw = false;
        try { book.Register(WorldEventTemplates.Caravan(10)); }
        catch (ArgumentException) { threw = true; }

        Assertions.AssertThat(threw).IsTrue();
    }

    /// <summary>周期非正的模板要被拒（否则它会**每天都触发**）。</summary>
    [TestCase]
    public void Register_WithNonPositivePeriod_Throws()
    {
        var book = new WorldEventBook();

        bool threw = false;
        try { book.Register(WorldEventTemplates.Caravan(0)); }
        catch (ArgumentException) { threw = true; }

        Assertions.AssertThat(threw).IsTrue();
    }
}
