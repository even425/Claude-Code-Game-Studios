using System;
using System.Collections.Generic;
using GdUnit4;
using ClaudeCodeGameStudios.Application;
using ClaudeCodeGameStudios.Domain.Village;
using ClaudeCodeGameStudios.Domain.WorldEvents;

namespace ClaudeCodeGameStudios.Tests.Application;

/// <summary>
/// <b>大事件的「外部收入 / 外部支出」</b>（D-101）—— 守恒律的新科目。
/// </summary>
/// <remarks>
/// <para><b>为什么需要它</b>：大事件发的钱是<b>从外面进来的</b>（打完怪捡到的战利品）
/// ⇒ 它<b>不在</b>英雄钱包里、也<b>不是</b>「累计发放」（那一项只记「结算发给英雄的钱」）
/// ⇒ 守恒律的等式<b>右边必须加上它</b>，否则<b>每结算一次大事件就破一次</b>。</para>
///
/// <para>🔴 <b>为什么这条一直没人发现</b>：D-101 之前<b>大事件从未真的结算过</b>
/// （阶段机死锁）⇒ 这一项<b>永远是 0</b> ⇒ 守恒律看着一直是对的
/// ⇒ <b>一条从未被触发的规则，看起来是「通过」的</b>。</para>
/// </remarks>
[TestSuite]
public class EventGoldAccountingTests
{
    /// <summary>金币资源。</summary>
    private static readonly ResourceId Gold = new("金币");

    /// <summary>材料资源（外部收入 / 支出<b>只对金币</b>记）。</summary>
    private static readonly ResourceId Material = new("材料");

    /// <summary>造一条「给 N 金币」的后果。</summary>
    private static WorldEventEffect Gain(long amount)
        => new(WorldEventEffect.KindGold, amount, "给钱");

    /// <summary>造一条「扣 N 金币」的后果（⚠️ 金额是<b>负数</b>）。</summary>
    private static WorldEventEffect Loss(long amount)
        => new(WorldEventEffect.KindGold, -amount, "扣钱");

    /// <summary>恢复到「干净」状态（静态计数器 ⇒ 每个用例都要先归零）。</summary>
    private static void Reset() => WorldEventResourceApplier.RestoreTotals(0, 0);

    /// <summary>
    /// 大事件<b>给</b>村库金币 ⇒ 记进「外部收入」。
    /// </summary>
    [TestCase]
    public void EventGold_Given_IsCountedAsExternalIncome()
    {
        Reset();
        var village = new Village("v1");
        village.Add(Gold, 100);

        WorldEventResourceApplier.Apply(village, Gold, Gain(20), "金币");

        Assertions.AssertThat(village.Get(Gold)).IsEqual(120L);
        Assertions.AssertThat(WorldEventResourceApplier.TotalEventGoldIn).IsEqual(20L);
        Assertions.AssertThat(WorldEventResourceApplier.TotalEventGoldOut).IsEqual(0L);
    }

    /// <summary>
    /// 大事件<b>抢走</b>村库金币 ⇒ 记进「外部支出」（与收入<b>分开</b>）。
    /// </summary>
    /// <remarks>
    /// ⚠️ **分开记的理由**：「进来多少」与「出去多少」是<b>两件事</b>
    /// ⇒ 混成一个净值就看不出大事件到底是净收入还是净损失。
    /// </remarks>
    [TestCase]
    public void EventGold_Taken_IsCountedAsExternalOutflow()
    {
        Reset();
        var village = new Village("v1");
        village.Add(Gold, 100);

        WorldEventResourceApplier.Apply(village, Gold, Loss(30), "金币");

        Assertions.AssertThat(village.Get(Gold)).IsEqual(70L);
        Assertions.AssertThat(WorldEventResourceApplier.TotalEventGoldOut).IsEqual(30L);
        Assertions.AssertThat(WorldEventResourceApplier.TotalEventGoldIn).IsEqual(0L);
    }

    /// <summary>
    /// <b>材料不算</b>外部收入 / 支出（只有金币进守恒律那条等式）。
    /// </summary>
    [TestCase]
    public void EventMaterial_IsNotCountedInTheGoldAccounting()
    {
        Reset();
        var village = new Village("v1");

        WorldEventResourceApplier.Apply(village, Material, Gain(50), "材料");
        WorldEventResourceApplier.Apply(village, Material, Loss(20), "材料");

        Assertions.AssertThat(WorldEventResourceApplier.TotalEventGoldIn).IsEqual(0L);
        Assertions.AssertThat(WorldEventResourceApplier.TotalEventGoldOut).IsEqual(0L);
    }

    /// <summary>
    /// <b>守恒律</b>：外部收入 / 支出补上之后，等式成立。
    /// </summary>
    /// <remarks>
    /// ⚠️ <b>这条是本组的命门</b>：它把「钱从外面进来」这件事
    /// <b>接到守恒律那条等式上</b> ⇒ 没有它，前面三条只是「记了个数」，
    /// 而「记了数但等式里没有」⇒ 守恒律照样破。
    /// </remarks>
    [TestCase]
    public void TheConservationLaw_Holds_WithTheEventTerms()
    {
        Reset();
        var village = new Village("v1");
        village.Add(Gold, 100);

        // 大事件给 20 金 ⇒ 村库 120。
        WorldEventResourceApplier.Apply(village, Gold, Gain(20), "金币");

        long heroGold = 0;
        long villageGold = village.Get(Gold);
        long asset = 0;
        long destroyed = 0;

        long lhs = heroGold + villageGold + asset + destroyed
                 + WorldEventResourceApplier.TotalEventGoldOut;
        long rhs = 100 /* 初始 */ + 0 /* 累计发放 */
                 + WorldEventResourceApplier.TotalEventGoldIn;

        Assertions.AssertThat(lhs).IsEqual(rhs);
    }

    /// <summary>
    /// <b>累计</b>：多结算几次 ⇒ 两个累计都要能累加。
    /// </summary>
    [TestCase]
    public void TheTotals_Accumulate()
    {
        Reset();
        var village = new Village("v1");
        village.Add(Gold, 1000);

        WorldEventResourceApplier.Apply(village, Gold, Gain(20), "金币");
        WorldEventResourceApplier.Apply(village, Gold, Gain(30), "金币");
        WorldEventResourceApplier.Apply(village, Gold, Loss(10), "金币");

        Assertions.AssertThat(WorldEventResourceApplier.TotalEventGoldIn).IsEqual(50L);
        Assertions.AssertThat(WorldEventResourceApplier.TotalEventGoldOut).IsEqual(10L);
    }

    /// <summary>
    /// <b>读档恢复</b>：两个累计要能恢复回来（否则读档后守恒律立刻误报）。
    /// </summary>
    [TestCase]
    public void RestoreTotals_BringsTheCountersBack()
    {
        Reset();
        var village = new Village("v1");
        village.Add(Gold, 100);
        WorldEventResourceApplier.Apply(village, Gold, Gain(20), "金币");
        WorldEventResourceApplier.Apply(village, Gold, Loss(5), "金币");

        WorldEventResourceApplier.RestoreTotals(7, 3);

        Assertions.AssertThat(WorldEventResourceApplier.TotalEventGoldIn).IsEqual(7L);
        Assertions.AssertThat(WorldEventResourceApplier.TotalEventGoldOut).IsEqual(3L);
    }

    /// <summary>负数累计 ⇒ 拒绝（那是账目坏了，不是「抵消了」）。</summary>
    [TestCase]
    public void RestoreTotals_RejectsNegativeValues()
    {
        bool threw = false;
        try { WorldEventResourceApplier.RestoreTotals(-1, 0); }
        catch (ArgumentOutOfRangeException) { threw = true; }

        Assertions.AssertThat(threw).IsTrue();
        Reset();
    }
}
