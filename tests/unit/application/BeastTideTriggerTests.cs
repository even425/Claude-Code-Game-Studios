using System;
using System.Collections.Generic;
using System.Linq;
using GdUnit4;
using ClaudeCodeGameStudios.Application;
using ClaudeCodeGameStudios.Domain.Combat;
using ClaudeCodeGameStudios.Domain.WorldEvents;

namespace ClaudeCodeGameStudios.Tests.Application;

/// <summary>
/// <b>守不住就别触发兽潮</b>（D-082 拍板）。
/// </summary>
/// <remarks>
/// <para><b>它守的是三件事</b>：
/// <list type="number">
/// <item><b>守不住 ⇒ 这次兽潮不来</b>（弱村子不再被罚一笔「必输的税」）。</item>
/// <item><b>预演与实战一致</b>：触发前问的那一次，和真正结算那一次，结论必须一样。</item>
/// <item><b>预演没有副作用</b>：问一次不改变任何状态（战斗没有随机数 ⇒ 纯计算）。</item>
/// </list>
/// <para>⚠️ <b>为什么这条重要</b>：实测发现<b>弱阵容（挂机）在任何缩放下都必输</b>
/// ⇒ 兽潮对弱村子就是一笔<b>必输的税</b>（每 7 天扣一次材料与金币），
/// 而村子<b>无论如何也守不住</b>。</para>
/// </remarks>
[TestSuite]
public class BeastTideTriggerTests
{
    private static readonly CombatStats Strong = new(60, 10, 500);
    private static readonly CombatStats Weak = new(5, 4, 60);

    /// <summary>造一个只含兽潮的簿（商队不打仗，没有「守不守得住」这回事）。</summary>
    private static WorldEventBook NewBook(CombatStats heroStats, CombatStats monsterStats)
    {
        var combat = new FormulaCombat(monsterStats: _ => monsterStats);
        var book = new WorldEventBook();
        book.Register(WorldEventTemplates.BeastTide(
            periodDays: 7, combat, combat,
            heroStats: _ => heroStats,
            tuning: () => new BeastTideTuning
            {
                MaterialReward = 40,
                GoldReward = 20,
                MaterialLoss = 15,
                GoldLoss = 8,
            }));
        return book;
    }

    /// <summary>「守得住吗」的判定（与 Main 用的同一份代码）。</summary>
    private static bool CanHold(WorldEventBook book, CombatStats heroStats, CombatStats monsterStats)
    {
        var combat = new FormulaCombat(monsterStats: _ => monsterStats);
        return BeastTideBattle.CanHold(
            new[] { "h-1" },
            _ => heroStats,
            combat,
            combat);
    }

    /// <summary>
    /// D-082 ①：<b>守得住 ⇒ 兽潮照常触发</b>。
    /// </summary>
    [TestCase]
    public void StrongVillage_TheTideStillComes()
    {
        var book = NewBook(Strong, Strong);

        var instance = book.TriggerByPeriod(
            simDay: 7, now: 0.0, participants: new[] { "h-1" },
            (t, day) => $"inst-{t.Id}-{day}");

        Assertions.AssertThat(instance).IsNotNull();
        Assertions.AssertThat(book.ActiveInstances.Count).IsEqual(1);
    }

    /// <summary>
    /// D-082 ①：<b>守不住 ⇒ 这次兽潮不来</b>（过滤掉 ⇒ 连实例都没有）。
    /// </summary>
    /// <remarks>
    /// ⚠️ 断言的是「**没有实例**」，不是「实例被标记成失败」——
    /// 没触发就**不该有实例**（否则它照样会走完生命周期、照样会结算）。
    /// </remarks>
    [TestCase]
    public void WeakVillage_TheTideDoesNotCome()
    {
        var book = NewBook(Weak, Strong);

        var instance = book.TriggerByPeriod(
            simDay: 7, now: 0.0, participants: new[] { "h-1" },
            (t, day) => $"inst-{t.Id}-{day}",
            shouldTrigger: t => CanHold(book, Weak, Strong));

        Assertions.AssertThat(instance).IsNull();
        Assertions.AssertThat(book.ActiveInstances.Count).IsEqual(0);
        Assertions.AssertThat(book.Instances.Count).IsEqual(0);
    }

    /// <summary>
    /// D-082 ②：<b>预演与实战一致</b> —— 触发前问的结论，就是结算出来的结论。
    /// </summary>
    /// <remarks>
    /// ⚠️ 这一条是「预演」这条路的命门：若预演用一套算法、结算用另一套，
    /// 就会出现「预演说守得住，真打却输了」⇒ 玩家完全无法理解。
    /// </remarks>
    [TestCase]
    public void ProbeAndRealBattle_Agree()
    {
        foreach (var (hero, monster) in new[]
        {
            (Strong, Strong),
            (Weak, Strong),
            (Strong, Weak),
        })
        {
            var combat = new FormulaCombat(monsterStats: _ => monster);
            bool canHold = BeastTideBattle.CanHold(
                new[] { "h-1" }, _ => hero, combat, combat);

            var book = new WorldEventBook();
            book.Register(WorldEventTemplates.BeastTide(
                periodDays: 7, combat, combat,
                heroStats: _ => hero,
                tuning: () => new BeastTideTuning
                {
                    MaterialReward = 40,
                    GoldReward = 20,
                    MaterialLoss = 15,
                    GoldLoss = 8,
                }));

            var instance = book.TriggerByPeriod(
                simDay: 7, now: 0.0, participants: new[] { "h-1" },
                (t, day) => $"inst-{t.Id}-{day}")!;

            int guard = 0;
            while (instance.Phase != WorldEventPhase.Settlement && guard++ < 20)
            {
                book.Advance(instance.Id, 0.5);
            }

            var outcome = book.Settle(instance.Id, (key, effect) => { })!;

            Assertions.AssertThat(outcome.Succeeded).IsEqual(canHold);
        }
    }

    /// <summary>
    /// D-082 ③：<b>预演没有副作用</b> —— 问一次与问两次，结论一样。
    /// </summary>
    /// <remarks>
    /// 战斗没有随机数（D-062）⇒ 预演是<b>纯计算</b> ⇒ 问几次都一样。
    /// 若哪天给战斗加回了随机数，这条会立刻红 ⇒ 那时「预演」就不可信了。
    /// </remarks>
    [TestCase]
    public void Probe_IsSideEffectFree()
    {
        var combat = new FormulaCombat(monsterStats: _ => Strong);

        bool first = BeastTideBattle.CanHold(new[] { "h-1" }, _ => Strong, combat, combat);
        bool second = BeastTideBattle.CanHold(new[] { "h-1" }, _ => Strong, combat, combat);

        Assertions.AssertThat(first).IsTrue();
        Assertions.AssertThat(second).IsEqual(first);
    }

    /// <summary>没人守 ⇒ 守不住（连一只怪都打不过去）。</summary>
    [TestCase]
    public void NoDefenders_CannotHold()
    {
        var combat = new FormulaCombat();

        bool canHold = BeastTideBattle.CanHold(
            Array.Empty<string>(), _ => Strong, combat, combat);

        Assertions.AssertThat(canHold).IsFalse();
    }
}
