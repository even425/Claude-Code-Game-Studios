using System;
using GdUnit4;
using ClaudeCodeGameStudios.Application;
using ClaudeCodeGameStudios.Domain.Village;

namespace ClaudeCodeGameStudios.Tests.Application;

/// <summary>
/// <b>召唤的消耗</b>（D-077 / 基线 §10.8 的 S1 / S4 / S5）。
/// </summary>
/// <remarks>
/// <para><b>它守的是四件事</b>：
/// <list type="number">
/// <item><b>钱不够 ⇒ 拒绝</b>，且<b>一文钱都不扣</b>（S5 + 不变量 I12）。</item>
/// <item><b>扣费准确</b>：召唤后村金库恰好少 100（S4：「消耗来自游戏内资源」）。</item>
/// <item><b>资源不会变成负数</b>（S5）。</item>
/// <item><b>能退钱</b>：召唤没成时退款 ⇒ 不会留下「扣了钱没招到人」的半笔账。</item>
/// </list>
/// <para>⚠️ <b>这里只测「钱的规则」</b>：造英雄要写花名册与领域状态，
/// 那是表现层的活（<c>Main.SummonHero</c>）⇒ 由真机自检覆盖。
/// 本类测的是<b>规则层</b>，那部分必须能离线验。</para>
/// </remarks>
[TestSuite]
public class SummonServiceTests
{
    private static readonly ResourceId Gold = new("金币");

    private static (SummonService Service, Village Village) NewService(long gold)
    {
        var village = new Village("test");
        village.Add(Gold, gold);
        return (new SummonService(village, Gold), village);
    }

    /// <summary>召唤费是 100 金（D-077 拍板）。</summary>
    [TestCase]
    public void CostIsOneHundredGold()
    {
        Assertions.AssertThat(SummonService.GoldCost).IsEqual(100L);
    }

    /// <summary>钱够 ⇒ 扣掉 100，其余不动（S4）。</summary>
    [TestCase]
    public void TryPayCost_DeductsExactlyTheCost()
    {
        var (service, village) = NewService(250);

        bool paid = service.TryPayCost();

        Assertions.AssertThat(paid).IsTrue();
        Assertions.AssertThat(village.Get(Gold)).IsEqual(250 - SummonService.GoldCost);
    }

    /// <summary>钱不够 ⇒ <b>拒绝且一文不扣</b>（S5 + I12）。</summary>
    [TestCase]
    public void TryPayCost_WithoutEnoughGold_RefusesAndDeductsNothing()
    {
        var (service, village) = NewService(SummonService.GoldCost - 1);

        bool paid = service.TryPayCost();

        Assertions.AssertThat(paid).IsFalse();
        Assertions.AssertThat(village.Get(Gold)).IsEqual(SummonService.GoldCost - 1);
    }

    /// <summary>连续召唤到钱不够 ⇒ 余额<b>恰好归零</b>，<b>不会变成负数</b>（S5）。</summary>
    [TestCase]
    public void SummonUntilBroke_NeverGoesNegative()
    {
        var (service, village) = NewService(250);

        int summoned = 0;
        while (service.TryPayCost()) summoned++;

        Assertions.AssertThat(summoned).IsEqual(2);          // 250 ⇒ 招 2 个，剩 50
        Assertions.AssertThat(village.Get(Gold)).IsEqual(50);
        Assertions.AssertThat(village.Get(Gold)).IsGreaterEqual(0L);
    }

    /// <summary>恰好够一次 ⇒ 招完余额归零，且还能看到「不能再招」。</summary>
    [TestCase]
    public void ExactlyEnough_CanSummonOnceThenNot()
    {
        var (service, village) = NewService(SummonService.GoldCost);

        Assertions.AssertThat(service.CanSummon).IsTrue();
        Assertions.AssertThat(service.TryPayCost()).IsTrue();
        Assertions.AssertThat(service.CanSummon).IsFalse();
        Assertions.AssertThat(village.Get(Gold)).IsEqual(0);
    }

    /// <summary>退款 ⇒ 钱回来（召唤没成时不留半笔账）。</summary>
    [TestCase]
    public void Refund_ReturnsTheMoney()
    {
        var (service, village) = NewService(200);

        service.TryPayCost();
        service.Refund(SummonService.GoldCost);

        Assertions.AssertThat(village.Get(Gold)).IsEqual(200);
    }

    /// <summary>退款金额非正 ⇒ 拒绝（不往金库里塞 0 或负数）。</summary>
    [TestCase]
    public void Refund_WithNonPositiveAmount_Throws()
    {
        var (service, _) = NewService(200);

        bool threw = false;
        try { service.Refund(0); }
        catch (ArgumentOutOfRangeException) { threw = true; }

        Assertions.AssertThat(threw).IsTrue();
    }
}
