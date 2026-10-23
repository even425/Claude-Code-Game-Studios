using System;
using System.Collections.Generic;
using System.Linq;
using GdUnit4;
using ClaudeCodeGameStudios.Application;
using ClaudeCodeGameStudios.Domain.Combat;
using ClaudeCodeGameStudios.Domain.WorldEvents;

namespace ClaudeCodeGameStudios.Tests.Application;

/// <summary>
/// <b>大事件的阶段机</b>（D-101）—— 「一天一步」这条规则。
/// </summary>
/// <remarks>
/// <para>🔴 <b>它守的是一个曾经让整个大事件系统作废的 bug</b>：
/// 「刚创建 ⇒ 这次不动」那个判据原先用的是 <c>Progress == 0</c>，
/// 而 <c>Progress</c> <b>只有 Advance 才会改</b> ⇒ <b>互相锁死</b>
/// ⇒ 实例永远停在「预兆」⇒ <b>兽潮 / 商队 / 盗匪来袭从来没有结算过</b>
/// （实测：真实游戏 200 天里 <b>0 次</b>大事件结算）。</para>
///
/// <para>⚠️ <b>为什么单测能抓到、之前却没抓到</b>：
/// 原先的单测是<b>直接调 <c>Advance</c></b> 的 ⇒ <b>绕开了</b>「一天一步」那道闸门
/// ⇒ 测的是「阶段能推进」，不是「<b>真实游戏里</b>阶段能推进」。
/// ⇒ 现在这条规则搬进了领域层（<c>WorldEventBook.AdvanceDaily</c>）⇒ 可以离线单测。</para>
/// </remarks>
[TestSuite]
public class WorldEventDailyAdvanceTests
{
    /// <summary>一个模拟日的模拟秒数（测试夹具，不是设计值）。</summary>
    private const double SimSecondsPerDay = 60.0;

    /// <summary>造一个只有「预兆 → 准备 → 进行中 → 结算 → 结束」五个阶段的簿。</summary>
    private static WorldEventBook NewBook()
    {
        var book = new WorldEventBook();
        book.Register(new WorldEventTemplate
        {
            Id = "test_event",
            Name = "测试事件",
            PeriodDays = 9,
            Phases = new[]
            {
                WorldEventPhase.Omen,
                WorldEventPhase.Preparation,
                WorldEventPhase.Ongoing,
                WorldEventPhase.Settlement,
                WorldEventPhase.Finished,
            },
            Settle = (instance, participants) => new WorldEventOutcome(
                true,
                new Dictionary<string, WorldEventEffect>(StringComparer.Ordinal)
                {
                    ["test_event/material"] = new WorldEventEffect(
                        WorldEventEffect.KindMaterial, 20L, "给了 20 材料"),
                }),
        });
        return book;
    }

    /// <summary>
    /// <b>核心</b>：逐日推进 ⇒ 实例必须<b>真的走到结算</b>。
    /// </summary>
    /// <remarks>
    /// ⚠️ <b>这一条在修之前是红的</b>：实例永远停在预兆 ⇒ 走不到结算
    /// ⇒ 「大事件从来没结算过」这件事，第一次被一条测试抓住。
    /// </remarks>
    [TestCase]
    public void DailyAdvance_ReachesTheSettlementPhase()
    {
        var book = NewBook();
        var instance = book.TriggerByPeriod(
            simDay: 9, now: 0.0, participants: new[] { "h-1" },
            (t, day) => $"inst-{t.Id}-{day}")!;

        double now = 0.0;
        int guard = 0;
        while (instance.Phase != WorldEventPhase.Settlement && guard++ < 20)
        {
            now += SimSecondsPerDay;
            book.AdvanceDaily(instance.Id, now, SimSecondsPerDay);
        }

        Assertions.AssertThat(instance.Phase).IsEqual(WorldEventPhase.Settlement);
    }

    /// <summary>
    /// <b>走到结算 ⇒ 后果真的被应用</b>（这一条守的是「结算了但没发东西」）。
    /// </summary>
    [TestCase]
    public void DailyAdvance_SettlesAndAppliesTheConsequences()
    {
        var book = NewBook();
        var instance = book.TriggerByPeriod(
            simDay: 9, now: 0.0, participants: new[] { "h-1" },
            (t, day) => $"inst-{t.Id}-{day}")!;

        double now = 0.0;
        int guard = 0;
        while (instance.Phase != WorldEventPhase.Settlement && guard++ < 20)
        {
            now += SimSecondsPerDay;
            book.AdvanceDaily(instance.Id, now, SimSecondsPerDay);
        }

        var applied = new List<WorldEventEffect>();
        var outcome = book.Settle(instance.Id, (key, effect) => applied.Add(effect));

        Assertions.AssertThat(outcome is not null).IsTrue();
        Assertions.AssertThat(applied.Count).IsEqual(1);
        Assertions.AssertThat(applied[0].Amount).IsEqual(20L);
    }

    /// <summary>
    /// <b>创建当天不推进</b>（预兆要待满一天，玩家得能读到预告）。
    /// </summary>
    [TestCase]
    public void TheOmen_LastsAtLeastOneDay()
    {
        var book = NewBook();
        var instance = book.TriggerByPeriod(
            simDay: 9, now: 0.0, participants: new[] { "h-1" },
            (t, day) => $"inst-{t.Id}-{day}")!;

        // 同一天内再推一次（模拟「一天里被 tick 了很多次」）⇒ 阶段不变。
        book.AdvanceDaily(instance.Id, now: 0.0, SimSecondsPerDay);
        book.AdvanceDaily(instance.Id, now: SimSecondsPerDay * 0.5, SimSecondsPerDay);

        Assertions.AssertThat(instance.Phase).IsEqual(WorldEventPhase.Omen);
    }

    /// <summary>
    /// <b>预兆满一天之后</b> ⇒ 进入下一阶段。
    /// </summary>
    [TestCase]
    public void AfterTheOmenDay_TheEventMovesOn()
    {
        var book = NewBook();
        var instance = book.TriggerByPeriod(
            simDay: 9, now: 0.0, participants: new[] { "h-1" },
            (t, day) => $"inst-{t.Id}-{day}")!;

        book.AdvanceDaily(instance.Id, now: SimSecondsPerDay, SimSecondsPerDay);

        Assertions.AssertThat(instance.Phase).IsEqual(WorldEventPhase.Preparation);
    }

    /// <summary>
    /// <b>「一天一步」</b>：其余阶段一天只走一步（不是一天走完）。
    /// </summary>
    [TestCase]
    public void EachDay_AdvancesExactlyOnePhase()
    {
        var book = NewBook();
        var instance = book.TriggerByPeriod(
            simDay: 9, now: 0.0, participants: new[] { "h-1" },
            (t, day) => $"inst-{t.Id}-{day}")!;

        // 第一天：预兆 → 准备。
        book.AdvanceDaily(instance.Id, SimSecondsPerDay, SimSecondsPerDay);
        Assertions.AssertThat(instance.Phase).IsEqual(WorldEventPhase.Preparation);

        // 第二天：准备 → 进行中（**不是**直接到结算）。
        book.AdvanceDaily(instance.Id, SimSecondsPerDay * 2, SimSecondsPerDay);
        Assertions.AssertThat(instance.Phase).IsEqual(WorldEventPhase.Ongoing);
    }

    /// <summary>
    /// <b>「预兆是否待够」这条判据不能建立在 <c>Progress</c> 上</b>。
    /// </summary>
    /// <remarks>
    /// ⚠️ <b>这一条是「把 bug 写在测试里」</b>：它断言判据只依赖<b>时间</b>
    /// ⇒ 若有人改回用 <c>Progress</c> ⇒ 这条红。
    /// <para>⚠️ <b>为什么它不是同义反复</b>：它不是「断言某天数等于某数」，
    /// 而是断言「<b>创建当天即使被推多次，阶段也不变</b>」——
    /// 这正是死锁的那个形态（用 <c>Progress</c> 时，推多少次都停在预兆）。</para>
    /// </remarks>
    [TestCase]
    public void TheOmenDecision_DoesNotDependOnProgress()
    {
        var book = NewBook();
        var instance = book.TriggerByPeriod(
            simDay: 9, now: 0.0, participants: new[] { "h-1" },
            (t, day) => $"inst-{t.Id}-{day}")!;

        // 刚创建：Progress = 0，创建当天不该推进。
        Assertions.AssertThat(instance.Progress).IsEqual(0.0);
        Assertions.AssertThat(
            WorldEventBook.OmenIsOver(instance, now: 0.0, SimSecondsPerDay)).IsFalse();

        // 满一天：推进。
        Assertions.AssertThat(
            WorldEventBook.OmenIsOver(instance, now: SimSecondsPerDay, SimSecondsPerDay)).IsTrue();
    }
}
