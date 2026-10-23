using System;
using System.Collections.Generic;
using System.Linq;
using GdUnit4;
using ClaudeCodeGameStudios.Application;
using ClaudeCodeGameStudios.Domain.Combat;
using ClaudeCodeGameStudios.Domain.Village;
using ClaudeCodeGameStudios.Domain.WorldEvents;

namespace ClaudeCodeGameStudios.Tests.Application;

/// <summary>
/// <b>盗匪来袭</b>（D-096\uff1a<b>\u7b2c\u4e09\u4e2a\u5927\u4e8b\u4ef6\u5b9e\u4f8b</b>\uff09\u3002
/// </summary>
/// <remarks>
/// <para><b>\u5b83\u5b88\u7684\u662f\u4e94\u4ef6\u4e8b</b>\uff1a
/// <list type="number">
/// <item>\u6253\u8d62 \u21d2 <b>\u7f09\u83b7</b>\uff08\u6750\u6599 + \u91d1\u5e01\uff09\u3002</item>
/// <item>\u6253\u8d25 \u21d2 <b>\u88ab\u62a2</b>\uff08\u6750\u6599 \u2212\uff0c<b>\u91d1\u5e01\u4e0d\u52a8</b>\uff1a\u76d7\u56e2\u62a2\u7684\u662f\u8d27\uff0c\u4e0d\u662f\u94b1\uff09\u3002</item>
/// <item>\u6ca1\u4eba\u51fa\u51fb \u21d2 \u5fc5\u8d25\uff08\u4e00\u6837\u88ab\u62a2\uff09\u3002</item>
/// <item><b>\u5f62\u72b6\u4e0e\u517d\u6f6e\u4e0d\u540c</b>\uff1a\u9636\u6bb5\u5c11\u4e00\u4e2a\uff08\u6ca1\u6709\u300c\u540e\u7eed\u5f71\u54cd\u300d\uff09\u21d2 \u8bc1\u660e\u300c\u9636\u6bb5\u53ef\u8df3\u8fc7\u300d\u3002</item>
/// <item><b>\u4e0d\u88ab\u300c\u5b88\u4e0d\u4f4f\u5c31\u4e0d\u89e6\u53d1\u300d\u90a3\u9053\u95f8\u62e6\u4f4f</b>\uff08\u90a3\u662f\u517d\u6f6e\u7279\u6709\u7684\uff09\u3002</item>
/// </list>
/// <para>\u26a0\ufe0f <b>\u4e3a\u4ec0\u4e48\u91cd\u8981</b>\uff1a\u57fa\u7ebf \u00a711.5 \u539f\u8bdd\u300c<b>\u6846\u67b6\u7684\u53ef\u6269\u5c55\u6027\u9700\u8981\u81f3\u5c11\u4e24\u4e2a\u5b9e\u4f8b\u6765\u9a8c\u8bc1</b>\u300d
/// \u21d2 \u73b0\u5728\u6709\u4e09\u4e2a\uff08\u517d\u6f6e / \u5546\u961f / \u76d7\u56e2\uff09\u4e14\u5f62\u72b6\u5404\u4e0d\u76f8\u540c \u21d2 \u8fd9\u624d\u7b97\u300c\u901a\u7528\u300d\u3002</para>
/// </remarks>
[TestSuite]
public class BanditRaidTests
{
    private static readonly BanditTuning Tuning = new(MaterialReward: 20, GoldReward: 10, MaterialLoss: 10);

    /// <summary>\u9020\u4e00\u4e2a\u76d7\u56e2\u6a21\u677f\uff1a\u80dc\u8d1f\u7531\u300c\u82f1\u96c4\u5c5e\u6027 vs \u76d7\u56e2\u5c5e\u6027\u300d\u51b3\u5b9a\uff08\u6218\u6597\u662f\u786e\u5b9a\u7684\uff09\u3002</summary>
    private static WorldEventBook NewBook(CombatStats heroStats, CombatStats monsterStats)
    {
        var combat = new FormulaCombat(monsterStats: _ => monsterStats);
        var book = new WorldEventBook();
        book.Register(WorldEventTemplates.BanditRaid(
            periodDays: 9, combat, combat,
            heroStats: _ => heroStats,
            tuning: () => Tuning));
        return book;
    }

    /// <summary>\u63a8\u8fdb\u5230\u7ed3\u7b97\u5e76\u7ed3\u7b97\u4e00\u6b21\u3002</summary>
    private static WorldEventOutcome? SettleNow(WorldEventBook book, List<WorldEventEffect> applied,
        string[] participants = null)
    {
        var instance = book.TriggerByPeriod(
            simDay: 9, now: 0.0,
            participants: participants ?? new[] { "h-1" },
            (t, day) => $"inst-{t.Id}-{day}")!;

        int guard = 0;
        while (instance.Phase != WorldEventPhase.Settlement && guard++ < 20)
        {
            book.Advance(instance.Id, 0.5);
        }

        return book.Settle(instance.Id, (key, effect) => applied.Add(effect));
    }

    /// <summary>\u6253\u8d62 \u21d2 <b>\u7f09\u83b7</b>\uff1a\u6750\u6599 + \u91d1\u5e01\u3002</summary>
    [TestCase]
    public void Win_GrantsMaterialAndGold()
    {
        var applied = new List<WorldEventEffect>();
        var book = NewBook(
            heroStats: new CombatStats(Attack: 40, Defense: 40, Health: 400),
            monsterStats: new CombatStats(Attack: 1, Defense: 1, Health: 200));

        var outcome = SettleNow(book, applied);

        Assertions.AssertThat(outcome!.Succeeded).IsTrue();
        Assertions.AssertThat(outcome.Consequences["bandit_raid/material"].Amount).IsEqual(20);
        Assertions.AssertThat(outcome.Consequences["bandit_raid/material"].Kind)
            .IsEqual(WorldEventEffect.KindMaterial);
        Assertions.AssertThat(outcome.Consequences["bandit_raid/gold"].Amount).IsEqual(10);
    }

    /// <summary>\u6253\u8d25 \u21d2 <b>\u88ab\u62a2\u7269\u8d44</b>\uff1a\u6750\u6599 \u2212\uff0c<b>\u91d1\u5e01\u4e0d\u52a8</b>\u3002</summary>
    [TestCase]
    public void Loss_LosesMaterial_ButNotGold()
    {
        var applied = new List<WorldEventEffect>();
        var book = NewBook(
            heroStats: new CombatStats(Attack: 1, Defense: 1, Health: 400),
            monsterStats: new CombatStats(Attack: 40, Defense: 40, Health: 4000));

        var outcome = SettleNow(book, applied);

        Assertions.AssertThat(outcome!.Succeeded).IsFalse();
        Assertions.AssertThat(outcome.Consequences["bandit_raid/material"].Amount).IsEqual(-10);
        Assertions.AssertThat(outcome.Consequences["bandit_raid/gold"].Amount).IsEqual(0);
    }

    /// <summary>\u6ca1\u4eba\u51fa\u51fb \u21d2 \u5fc5\u8d25\uff08\u4e00\u6837\u88ab\u62a2\uff09\u3002</summary>
    [TestCase]
    public void NoParticipants_LosesLikeItWouldAnyway()
    {
        var applied = new List<WorldEventEffect>();
        var book = NewBook(
            heroStats: new CombatStats(Attack: 40, Defense: 40, Health: 400),
            monsterStats: new CombatStats(Attack: 1, Defense: 1, Health: 200));

        var outcome = SettleNow(book, applied, participants: Array.Empty<string>());

        Assertions.AssertThat(outcome!.Succeeded).IsFalse();
        Assertions.AssertThat(outcome.Consequences["bandit_raid/material"].Amount).IsEqual(-10);
    }

    /// <summary>\u26a0\ufe0f <b>\u9636\u6bb5\u5c11\u4e00\u4e2a</b>\uff08\u6ca1\u6709\u300c\u540e\u7eed\u5f71\u54cd\u300d\uff09\u21d2 \u8bc1\u660e\u300c\u9636\u6bb5\u53ef\u8df3\u8fc7\u300d\u3002</summary>
    [TestCase]
    public void Shape_IsDifferentFromTheBeastTide()
    {
        var combat = new FormulaCombat(monsterStats: _ => new CombatStats(1, 1, 1));
        var book = new WorldEventBook();
        book.Register(WorldEventTemplates.BanditRaid(
            periodDays: 9, combat, combat,
            heroStats: _ => new CombatStats(1, 1, 1),
            tuning: () => Tuning));
        book.Register(WorldEventTemplates.BeastTide(
            periodDays: 7, combat, combat,
            heroStats: _ => new CombatStats(1, 1, 1),
            tuning: () => new BeastTideTuning
            {
                MaterialReward = 40, GoldReward = 20, MaterialLoss = 15, GoldLoss = 8,
            }));

        var bandit = book.Template(WorldEventTemplates.BanditRaidId)!;
        var beast = book.Template(WorldEventTemplates.BeastTideId)!;

        Assertions.AssertThat(bandit.Phases.Count < beast.Phases.Count).IsTrue();
        Assertions.AssertThat(bandit.Phases.Contains(WorldEventPhase.Aftermath)).IsFalse();
        Assertions.AssertThat(bandit.PeriodDays).IsEqual(9);
    }

    /// <summary>\u76d7\u56e2<b>\u4e0d\u53d7\u300c\u5b88\u4e0d\u4f4f\u5c31\u4e0d\u89e6\u53d1\u300d\u90a3\u9053\u95f8\u7ea6\u675f</b>\uff08D-082 \u90a3\u9053\u95f8\u662f\u517d\u6f6e\u7279\u6709\u7684\uff09\u3002</summary>
    [TestCase]
    public void Bandits_AreNotGatedByTheBeastTideFilter()
    {
        // \u82e5\u88ab\u90a3\u9053\u95f8\u62e6\u4f4f\uff0c\u76d7\u56e2\u5c31\u4e0d\u4f1a\u6765 \u21d2 \u6ca1\u6709\u5b9e\u4f8b\u53ef\u8bc1\u660e\u300c\u4e0d\u540c\u89c4\u5219\u300d\u3002
        var combat = new FormulaCombat(monsterStats: _ => new CombatStats(1, 1, 1));
        var book = new WorldEventBook();
        book.Register(WorldEventTemplates.BanditRaid(
            periodDays: 9, combat, combat,
            heroStats: _ => new CombatStats(1, 1, 1),
            tuning: () => Tuning));

        var instance = book.TriggerByPeriod(
            simDay: 9, now: 0.0, participants: new[] { "h-1" },
            (t, day) => $"inst-{t.Id}-{day}");

        Assertions.AssertThat(instance).IsNotNull();
    }
}
