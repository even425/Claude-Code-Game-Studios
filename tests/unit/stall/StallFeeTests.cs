using System;
using GdUnit4;
using ClaudeCodeGameStudios.Domain.Stall;

namespace ClaudeCodeGameStudios.Tests.Stall;

/// <summary>
/// <see cref="StallFee.Compute"/> 单元测试。
///
/// <para><b>本文档保护：</b></para>
/// <list type="bullet">
/// <item>5% 向下取整（D-023）。</item>
/// <item>残差归卖家（D-023 保护玩家）。</item>
/// <item>整百数无残差（5% 整除）。</item>
/// <item><c>totalAmount = 0</c> → 双方 0。</item>
/// <item>负数 → 抛异常（与 I12 防线一致）。</item>
/// <item><b>守恒律</b>：<c>sellerNet + villageCut == totalAmount</c> 永成立（不丢钱不生钱）。</item>
/// </list>
/// </summary>
[TestSuite]
public class StallFeeTests
{
    // ── 守恒律（所有金额的"硬约束"）────────────────────────────────

    /// <summary>100 金：5% = 5 整除，卖家 95、村 5，<b>无残差</b>。</summary>
    [TestCase]
    public void Compute_100Gold_FivePercent_NoRemainder()
    {
        var (seller, village) = StallFee.Compute(100);
        Assertions.AssertThat(seller).IsEqual(95L);
        Assertions.AssertThat(village).IsEqual(5L);
        Assertions.AssertThat(seller + village).IsEqual(100L);
    }

    /// <summary>200 金：5% = 10，卖家 190、村 10。</summary>
    [TestCase]
    public void Compute_200Gold_FivePercent_Ten()
    {
        var (seller, village) = StallFee.Compute(200);
        Assertions.AssertThat(seller).IsEqual(190L);
        Assertions.AssertThat(village).IsEqual(10L);
        Assertions.AssertThat(seller + village).IsEqual(200L);
    }

    // ── 残差：floor + 残差归卖家 ────────────────────────────────────

    /// <summary>10 金：5% = 0.5 → floor 0，<b>残差 10 全归卖家</b>。</summary>
    [TestCase]
    public void Compute_10Gold_FloorRoundsToZero_SellerGetsAll()
    {
        var (seller, village) = StallFee.Compute(10);
        Assertions.AssertThat(seller).IsEqual(10L);
        Assertions.AssertThat(village).IsEqual(0L);
        Assertions.AssertThat(seller + village).IsEqual(10L);
    }

    /// <summary>1 金：5% = 0.05 → floor 0，残差归卖家。</summary>
    [TestCase]
    public void Compute_1Gold_SellerGetsAll_Reminder()
    {
        var (seller, village) = StallFee.Compute(1);
        Assertions.AssertThat(seller).IsEqual(1L);
        Assertions.AssertThat(village).IsEqual(0L);
    }

    /// <summary>99 金：5% = 4.95 → floor 4，卖家 95（4+95=99 无残差）。</summary>
    [TestCase]
    public void Compute_99Gold_FloorRoundsDown()
    {
        var (seller, village) = StallFee.Compute(99);
        Assertions.AssertThat(village).IsEqual(4L);
        Assertions.AssertThat(seller).IsEqual(95L);
        Assertions.AssertThat(seller + village).IsEqual(99L);
    }

    /// <summary>101 金：5% = 5.05 → floor 5，残差 1 归卖家（= 卖家 96）。</summary>
    [TestCase]
    public void Compute_101Gold_FloorRoundsDown_ReminderToSeller()
    {
        var (seller, village) = StallFee.Compute(101);
        Assertions.AssertThat(village).IsEqual(5L);
        Assertions.AssertThat(seller).IsEqual(96L);
        Assertions.AssertThat(seller + village).IsEqual(101L);
    }

    // ── 零元 ────────────────────────────────────────────────────────

    /// <summary>0 金：双方 0，<b>不</b>抛异常。</summary>
    [TestCase]
    public void Compute_0Gold_BothZero()
    {
        var (seller, village) = StallFee.Compute(0);
        Assertions.AssertThat(seller).IsEqual(0L);
        Assertions.AssertThat(village).IsEqual(0L);
    }

    // ── 防线 ────────────────────────────────────────────────────────

    /// <summary>负数 → 抛异常（I12 防线在摊位费上的体现）。</summary>
    [TestCase]
    public void Compute_NegativeAmount_Throws()
    {
        bool threw = false;
        try { StallFee.Compute(-1); }
        catch (ArgumentOutOfRangeException) { threw = true; }
        Assertions.AssertThat(threw).IsTrue();
    }
}
