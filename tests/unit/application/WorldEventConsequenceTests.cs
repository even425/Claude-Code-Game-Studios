using System.Collections.Generic;
using GdUnit4;
using ClaudeCodeGameStudios.Application;
using ClaudeCodeGameStudios.Domain.Combat;
using ClaudeCodeGameStudios.Domain.Village;
using ClaudeCodeGameStudios.Domain.WorldEvents;

namespace ClaudeCodeGameStudios.Tests.Application;

/// <summary>
/// <b>大事件的后果</b>（D-080）：兽潮的胜负要**真的落到村庄资源上**。
/// </summary>
/// <remarks>
/// <para><b>它守的是开发者拍板的四条</b>：
/// <list type="number">
/// <item>兽潮打输 ⇒ <b>只损资源</b>（材料与金币），<b>人畜无损</b>。</item>
/// <item>兽潮打赢 ⇒ <b>材料 + 金币</b>。</item>
/// <item>兽潮的<b>规模随村庄层级递增</b>。</item>
/// <item>后果是<b>结构化</b>的（种类 + 数量）⇒ 落地层不解析中文。</item>
/// </list>
/// <para>⚠️ <b>为什么断言「结构化」而不只是断言「有发钱」</b>：
/// 后果若只给一句中文（「材料 +40」），落地层就只能解析中文 ⇒ 改个措辞就悄悄失效，
/// 而且<b>没有任何守卫能发现</b>。⇒ 这里断言 <c>Kind</c> 与 <c>Amount</c> 本身。</para>
/// </remarks>
[TestSuite]
public class WorldEventConsequenceTests
{
    private static readonly ResourceId Materials = new("材料");
    private static readonly ResourceId Gold = new("金币");

    private static readonly BeastTideTuning Tuning = new()
    {
        MaterialReward = 40,
        GoldReward = 20,
        MaterialLoss = 30,
        GoldLoss = 15,
    };

    /// <summary>造一个兽潮模板：胜负由「英雄属性 vs 怪物属性」决定（战斗是确定的）。</summary>
    private static WorldEventBook NewBook(
        CombatStats heroStats, CombatStats monsterStats, out List<WorldEventEffect> applied)
    {
        var effects = new List<WorldEventEffect>();
        applied = effects;

        var combat = new FormulaCombat(monsterStats: _ => monsterStats);
        var book = new WorldEventBook();
        book.Register(WorldEventTemplates.BeastTide(
            periodDays: 7, combat, combat,
            heroStats: _ => heroStats,
            tuning: () => Tuning));
        return book;
    }

    /// <summary>推进到结算并结算一次，返回结算结果。</summary>
    private static WorldEventOutcome? SettleNow(WorldEventBook book, List<WorldEventEffect> applied)
    {
        var instance = book.TriggerByPeriod(
            simDay: 7, now: 0.0, participants: new[] { "h-1" },
            (t, day) => $"inst-{t.Id}-{day}")!;

        int guard = 0;
        while (instance.Phase != WorldEventPhase.Settlement && guard++ < 20)
        {
            book.Advance(instance.Id, 0.5);
        }

        return book.Settle(instance.Id, (key, effect) => applied.Add(effect));
    }

    /// <summary>D-080 ②：打赢 ⇒ 材料与金币都是<b>正的</b>（结构化的「给」）。</summary>
    [TestCase]
    public void BeastTide_Win_GivesMaterialAndGold()
    {
        // ⚠️ **不要用「一拳秒杀」的属性**：那样战斗会在第 0 回合结束 ⇒
        // `BattleLogReader.RoundsUsed` 读到 0 回合会抛（回合数必须 ≥ 1）
        // ⇒ 测的是「战斗系统不接受 0 回合」，不是「兽潮打赢发钱」。
        var book = NewBook(
            heroStats: new CombatStats(60, 10, 500),
            monsterStats: new CombatStats(5, 2, 120),
            out var applied);

        var outcome = SettleNow(book, applied);

        Assertions.AssertThat(outcome).IsNotNull();
        Assertions.AssertThat(outcome!.Succeeded).IsTrue();

        var material = outcome.Consequences["beast_tide/material"];
        var gold = outcome.Consequences["beast_tide/gold"];

        Assertions.AssertThat(material.Kind).IsEqual(WorldEventEffect.KindMaterial);
        Assertions.AssertThat(material.Amount).IsEqual(Tuning.MaterialReward);
        Assertions.AssertThat(gold.Kind).IsEqual(WorldEventEffect.KindGold);
        Assertions.AssertThat(gold.Amount).IsEqual(Tuning.GoldReward);
    }

    /// <summary>D-080 ①：打输 ⇒ 材料与金币都是<b>负的</b>（结构化的「扣」）。</summary>
    [TestCase]
    public void BeastTide_Loss_TakesMaterialAndGold()
    {
        var book = NewBook(
            heroStats: new CombatStats(5, 2, 120),
            monsterStats: new CombatStats(60, 10, 500),
            out var applied);

        var outcome = SettleNow(book, applied);

        Assertions.AssertThat(outcome).IsNotNull();
        Assertions.AssertThat(outcome!.Succeeded).IsFalse();

        var material = outcome.Consequences["beast_tide/material"];
        var gold = outcome.Consequences["beast_tide/gold"];

        Assertions.AssertThat(material.Kind).IsEqual(WorldEventEffect.KindMaterial);
        Assertions.AssertThat(material.Amount).IsEqual(-Tuning.MaterialLoss);
        Assertions.AssertThat(gold.Kind).IsEqual(WorldEventEffect.KindGold);
        Assertions.AssertThat(gold.Amount).IsEqual(-Tuning.GoldLoss);
    }

    /// <summary>
    /// D-080 ①：<b>没人守</b> ⇒ 照样赔（不是「没人守就没事」）。
    /// </summary>
    [TestCase]
    public void BeastTide_NoDefenders_StillLoses()
    {
        var combat = new FormulaCombat();
        var book = new WorldEventBook();
        book.Register(WorldEventTemplates.BeastTide(
            periodDays: 7, combat, combat,
            heroStats: _ => new CombatStats(6, 2, 60),
            tuning: () => Tuning));

        var instance = book.TriggerByPeriod(
            simDay: 7, now: 0.0, participants: System.Array.Empty<string>(),
            (t, day) => $"inst-{t.Id}-{day}")!;

        int guard = 0;
        while (instance.Phase != WorldEventPhase.Settlement && guard++ < 20)
        {
            book.Advance(instance.Id, 0.5);
        }

        var applied = new List<WorldEventEffect>();
        var outcome = book.Settle(instance.Id, (key, effect) => applied.Add(effect));

        Assertions.AssertThat(outcome).IsNotNull();
        Assertions.AssertThat(outcome!.Succeeded).IsFalse();
        Assertions.AssertThat(outcome.Consequences["beast_tide/material"].Amount)
            .IsEqual(-Tuning.MaterialLoss);
    }

    /// <summary>
    /// D-080 ③：兽潮的<b>规模随村庄层级递增</b> —— 层级越高，怪越强。
    /// </summary>
    /// <remarks>
    /// ⚠️ 断言的是<b>具体的强多少</b>（每级 +15% ⇒ 2 级 = ×1.30），
    /// 不是「比上一层强」这种可被放宽的区间断言。
    /// </remarks>
    [TestCase]
    public void BeastTide_StrongerAtHigherTier()
    {
        var baseStats = new CombatStats(6, 2, 60);

        var tier0 = BeastTideScale.ScaleMonster(baseStats, tier: 0, percentPerTier: 15);
        var tier2 = BeastTideScale.ScaleMonster(baseStats, tier: 2, percentPerTier: 15);
        var tier5 = BeastTideScale.ScaleMonster(baseStats, tier: 5, percentPerTier: 15);

        // 层级 0 = 原样。
        Assertions.AssertThat(tier0.Attack).IsEqual(6);
        Assertions.AssertThat(tier0.Health).IsEqual(60);

        // 2 级 = ×1.30（6 → 7.8 → 7；60 → 78）。
        Assertions.AssertThat(tier2.Attack).IsEqual(7);
        Assertions.AssertThat(tier2.Health).IsEqual(78);

        // 5 级 = ×1.75（6 → 10.5 → 10；60 → 105）。
        Assertions.AssertThat(tier5.Attack).IsEqual(10);
        Assertions.AssertThat(tier5.Health).IsEqual(105);

        // 层级越高越强（单调）。
        Assertions.AssertThat(tier5.Attack > tier2.Attack).IsTrue();
        Assertions.AssertThat(tier2.Attack > tier0.Attack).IsTrue();
    }

    /// <summary>
    /// D-080 ③：回报与损失都<b>随层级放大</b>（风险与回报同步增长）。
    /// </summary>
    [TestCase]
    public void BeastTide_RewardsAndLosses_ScaleWithTier()
    {
        var tier0 = Tuning.ScaledToTier(0);
        var tier3 = Tuning.ScaledToTier(3);

        Assertions.AssertThat(tier0.MaterialReward).IsEqual(Tuning.MaterialReward);
        Assertions.AssertThat(tier3.MaterialReward).IsEqual(Tuning.MaterialReward * 4);
        Assertions.AssertThat(tier3.GoldReward).IsEqual(Tuning.GoldReward * 4);
        Assertions.AssertThat(tier3.MaterialLoss).IsEqual(Tuning.MaterialLoss * 4);
        Assertions.AssertThat(tier3.GoldLoss).IsEqual(Tuning.GoldLoss * 4);
    }

    // ── 后果落地：村库资源 ───────────────────────────────────────────────

    /// <summary>正数后果 ⇒ 村库真的多了（材料桶与金币桶是<b>两个</b>池子）。</summary>
    [TestCase]
    public void PositiveEffect_AddsToVillage()
    {
        var village = new Village("v1");

        var result = WorldEventResourceApplier.Apply(
            village, Materials,
            new WorldEventEffect(WorldEventEffect.KindMaterial, 40, "材料 +40"), "材料");

        Assertions.AssertThat(village.Get(Materials)).IsEqual(40);
        Assertions.AssertThat(village.Get(Gold)).IsEqual(0);
        Assertions.AssertThat(result.Amount).IsEqual(40);
    }

    /// <summary>负数后果 ⇒ 村库真的少了。</summary>
    [TestCase]
    public void NegativeEffect_TakesFromVillage()
    {
        var village = new Village("v1");
        village.Add(Materials, 100);

        var result = WorldEventResourceApplier.Apply(
            village, Materials,
            new WorldEventEffect(WorldEventEffect.KindMaterial, -30, "材料 -30"), "材料");

        Assertions.AssertThat(village.Get(Materials)).IsEqual(70);
        Assertions.AssertThat(result.Amount).IsEqual(-30);
    }

    /// <summary>
    /// ⚠️ <b>扣不动就只扣有的</b>：村库不够时**扣到 0 为止**，<b>不欠账</b>。
    /// </summary>
    /// <remarks>
    /// 若允许「扣成负数」，村库会出现「负债」这个状态 ⇒ 下一次结算会凭空扣掉负数，
    /// 守恒律也会对不上。
    /// </remarks>
    [TestCase]
    public void NegativeEffect_ClampsToAvailableBalance()
    {
        var village = new Village("v1");
        village.Add(Materials, 12);

        var result = WorldEventResourceApplier.Apply(
            village, Materials,
            new WorldEventEffect(WorldEventEffect.KindMaterial, -30, "材料 -30"), "材料");

        Assertions.AssertThat(village.Get(Materials)).IsEqual(0);
        Assertions.AssertThat(result.Amount).IsEqual(-12);
    }

    /// <summary>空村库遇上扣款 ⇒ 不变负、不报错。</summary>
    [TestCase]
    public void NegativeEffect_OnEmptyVillage_ChangesNothing()
    {
        var village = new Village("v1");

        var result = WorldEventResourceApplier.Apply(
            village, Gold,
            new WorldEventEffect(WorldEventEffect.KindGold, -15, "金币 -15"), "金币");

        Assertions.AssertThat(village.Get(Gold)).IsEqual(0);
        Assertions.AssertThat(result.Amount).IsEqual(0);
    }

    /// <summary>
    /// 「只说明」类后果 ⇒ <b>不动任何资源</b>（否则「没守住」这种描述也会扣钱）。
    /// </summary>
    [TestCase]
    public void NoneEffect_ChangesNothing()
    {
        var village = new Village("v1");
        village.Add(Gold, 50);

        var result = WorldEventResourceApplier.Apply(
            village, Gold, WorldEventEffect.None("守住了"), "金币");

        Assertions.AssertThat(village.Get(Gold)).IsEqual(50);
        Assertions.AssertThat(result.Amount).IsEqual(0);
    }

    // ── 商队报价（D-080：低于市价 + 限量）────────────────────────────────

    /// <summary>折扣必须落在 [1, 100]：越界 ⇒ 大声失败（不静默夹取）。</summary>
    [TestCase]
    public void CaravanOffer_RejectsInvalidDiscount()
    {
        bool threw = false;
        try { _ = new CaravanOffer("石料", 0, 10); }
        catch (System.ArgumentOutOfRangeException) { threw = true; }

        Assertions.AssertThat(threw).IsTrue();

        bool threwTooBig = false;
        try { _ = new CaravanOffer("石料", 101, 10); }
        catch (System.ArgumentOutOfRangeException) { threwTooBig = true; }

        Assertions.AssertThat(threwTooBig).IsTrue();
    }

    /// <summary>库存必须 ≥ 1（没货的商队不用来访）。</summary>
    [TestCase]
    public void CaravanOffer_RejectsEmptyStock()
    {
        bool threw = false;
        try { _ = new CaravanOffer("石料", 80, 0); }
        catch (System.ArgumentOutOfRangeException) { threw = true; }

        Assertions.AssertThat(threw).IsTrue();
    }
}
