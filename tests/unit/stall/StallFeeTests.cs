using System;
using GdUnit4;
using ClaudeCodeGameStudios.Domain.Stall;

namespace ClaudeCodeGameStudios.Tests.Stall;

/// <summary>
/// <see cref="StallFee.Compute"/> 单元测试。
/// </summary>
/// <remarks>
/// <para><b>本文档保护：</b></para>
/// <list type="bullet">
/// <item>5% 向下取整（D-023）。</item>
/// <item>整百数无残差（5% 整除）。</item>
/// <item><c>totalAmount = 0</c> → 双方 0。</item>
/// <item>负数 → 抛异常（与 I12 防线一致）。</item>
/// <item><b>守恒律</b>：<c>sellerNet + villageCut == totalAmount</c> 永成立（不丢钱不生钱）。</item>
/// <item><b>余数累计</b>（D-052）：carry 累加、carry=0 时行为与旧版逐位相同。</item>
/// </list>
/// <para>
/// <b>D-052 修订说明</b>：「残差归卖家」已改成「残差<b>累计归村庄金库</b>」。
/// 本文件里**带 carry = 0 的断言值一个都没变** —— 单笔拆分在无历史余数时
/// 与旧行为逐位相同；变的只是「跨笔累计时零头最终归谁」。
/// 原来断言「残差归卖家」的用例已改名为「单笔无历史余数时 villageCut = floor」，
/// 并新增累计语义的用例。
/// </para>
/// </remarks>
[TestSuite]
public class StallFeeTests
{
    // ── 守恒律（所有金额的"硬约束"）────────────────────────────────

    /// <summary>100 金：5% = 5 整除，卖家 95、村 5，<b>无残差</b>。</summary>
    [TestCase]
    public void Compute_100Gold_FivePercent_NoRemainder()
    {
        var (seller, village, _) = StallFee.Compute(100);
        Assertions.AssertThat(seller).IsEqual(95L);
        Assertions.AssertThat(village).IsEqual(5L);
        Assertions.AssertThat(seller + village).IsEqual(100L);
    }

    /// <summary>200 金：5% = 10，卖家 190、村 10。</summary>
    [TestCase]
    public void Compute_200Gold_FivePercent_Ten()
    {
        var (seller, village, _) = StallFee.Compute(200);
        Assertions.AssertThat(seller).IsEqual(190L);
        Assertions.AssertThat(village).IsEqual(10L);
        Assertions.AssertThat(seller + village).IsEqual(200L);
    }

    // ── 单笔取整（carry = 0 时与旧版逐位相同）─────────────────────────

    /// <summary>
    /// 10 金：5% = 0.5 → 单笔 floor 0，卖家拿 10；<b>零头 50（1/100 金）留进 carry</b>。
    /// </summary>
    /// <remarks>
    /// D-052 之前这里断言「残差 10 全归卖家」；现在零头<b>不再归卖家</b>，
    /// 而是进 carry 等凑满 1 金归村库。**卖家这 10 金没变**（carry=0 时单笔行为相同）。
    /// </remarks>
    [TestCase]
    public void Compute_10Gold_SingleTransaction_FloorToZero_CarryKeepsRemainder()
    {
        var (seller, village, carry) = StallFee.Compute(10);
        Assertions.AssertThat(seller).IsEqual(10L);
        Assertions.AssertThat(village).IsEqual(0L);
        Assertions.AssertThat(seller + village).IsEqual(10L);
        Assertions.AssertThat(carry).IsEqual(50L);   // 10 × 5 = 50（1/100 金）
    }

    /// <summary>1 金：5% = 0.05 → 单笔 floor 0，零头 5 进 carry。</summary>
    [TestCase]
    public void Compute_1Gold_SingleTransaction_CarryKeepsRemainder()
    {
        var (seller, village, carry) = StallFee.Compute(1);
        Assertions.AssertThat(seller).IsEqual(1L);
        Assertions.AssertThat(village).IsEqual(0L);
        Assertions.AssertThat(carry).IsEqual(5L);
    }

    /// <summary>99 金：5% = 4.95 → 单笔 floor 4，卖家 95，零头 95 进 carry。</summary>
    [TestCase]
    public void Compute_99Gold_FloorRoundsDown()
    {
        var (seller, village, carry) = StallFee.Compute(99);
        Assertions.AssertThat(village).IsEqual(4L);
        Assertions.AssertThat(seller).IsEqual(95L);
        Assertions.AssertThat(seller + village).IsEqual(99L);
        Assertions.AssertThat(carry).IsEqual(95L);
    }

    /// <summary>101 金：5% = 5.05 → 单笔 floor 5，卖家 96，零头 5 进 carry。</summary>
    [TestCase]
    public void Compute_101Gold_FloorRoundsDown_CarryKeepsRemainder()
    {
        var (seller, village, carry) = StallFee.Compute(101);
        Assertions.AssertThat(village).IsEqual(5L);
        Assertions.AssertThat(seller).IsEqual(96L);
        Assertions.AssertThat(seller + village).IsEqual(101L);
        Assertions.AssertThat(carry).IsEqual(5L);
    }

    // ── 零元 ────────────────────────────────────────────────────────

    /// <summary>0 金：双方 0，<b>不</b>抛异常。</summary>
    [TestCase]
    public void Compute_0Gold_BothZero()
    {
        var (seller, village, _) = StallFee.Compute(0);
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

    /// <summary>负余数 → 抛异常（carry 必须落在 [0, 99]）。</summary>
    [TestCase]
    public void Compute_NegativeCarry_Throws()
    {
        bool threw = false;
        try { StallFee.Compute(10, -1); }
        catch (ArgumentOutOfRangeException) { threw = true; }
        Assertions.AssertThat(threw).IsTrue();
    }

    /// <summary>余数 ≥ 100 → 抛异常（那已经该入账了，不是余数）。</summary>
    [TestCase]
    public void Compute_CarryOutOfRange_Throws()
    {
        bool threw = false;
        try { StallFee.Compute(10, 100); }
        catch (ArgumentOutOfRangeException) { threw = true; }
        Assertions.AssertThat(threw).IsTrue();
    }

    // ── 余数累计（D-052）────────────────────────────────────────────

    /// <summary>
    /// 4 笔 5 金：零头 25 × 4 = 100 → 第 4 笔入账 1 金，累计口径正好 5%。
    /// </summary>
    /// <remarks>
    /// 这正是「原来村抽永远是 0」的修复：
    /// 逐笔 floor 时每笔都 floor 到 0，20 金成交额村库一毛不收；
    /// 累计后 20 金收 1 金 = **正好 5%**。
    /// </remarks>
    [TestCase]
    public void Compute_FourFiveGoldTransactions_AccumulatesToExactlyFivePercent()
    {
        long carry = 0;
        long totalVillage = 0;

        for (int i = 0; i < 4; i++)
        {
            var (seller, village, newCarry) = StallFee.Compute(5, carry);
            carry = newCarry;
            totalVillage += village;
            Assertions.AssertThat(seller + village).IsEqual(5L);   // 每笔仍守恒
        }

        Assertions.AssertThat(totalVillage).IsEqual(1L);   // 20 金 → 1 金 = 5%
        Assertions.AssertThat(carry).IsEqual(0L);          // 凑满了，余数归零
    }

    /// <summary>carry 非 0 时，单笔的 villageCut 会把上一笔的零头一起算进去。</summary>
    [TestCase]
    public void Compute_WithCarry_CarriesPreviousRemainder()
    {
        // 第一笔 10 金 → 零头 50
        var (_, _, carry1) = StallFee.Compute(10);
        Assertions.AssertThat(carry1).IsEqual(50L);

        // 第二笔 10 金：零头 50 + 50 = 100 → 入账 1 金，余数归零
        var (seller, village, carry2) = StallFee.Compute(10, carry1);
        Assertions.AssertThat(village).IsEqual(1L);
        Assertions.AssertThat(carry2).IsEqual(0L);
        Assertions.AssertThat(seller).IsEqual(9L);
        Assertions.AssertThat(seller + village).IsEqual(10L);
    }

    /// <summary>累计守恒：N 笔后，<c>sum(villageCut) + carry == floor(5% × 总成交额)</c>。</summary>
    [TestCase]
    public void Compute_AccumulatedConservesAgainstFivePercentOfTotal()
    {
        long[] amounts = { 1, 3, 5, 8, 13, 21, 34, 55 };
        long carry = 0;
        long totalVillage = 0;
        long totalAmount = 0;

        foreach (long amount in amounts)
        {
            var (seller, village, newCarry) = StallFee.Compute(amount, carry);
            carry = newCarry;
            totalVillage += village;
            totalAmount += amount;
            Assertions.AssertThat(seller + village).IsEqual(amount);   // 每笔守恒
        }

        long expected = totalAmount * 5 / 100;   // 整数口径的 5%
        Assertions.AssertThat(totalVillage + carry).IsEqual(expected);
    }

    /// <summary>跨日不清零：余数是**累计**的，不随日期重置。</summary>
    [TestCase]
    public void Compute_CarryIsNotResetByDay()
    {
        var (_, _, carry) = StallFee.Compute(3);     // 3 × 5 = 15
        Assertions.AssertThat(carry).IsEqual(15L);

        // 「第二天」的第一笔：carry 照旧接着走
        var (_, village, carry2) = StallFee.Compute(3, carry);
        Assertions.AssertThat(village).IsEqual(0L);
        Assertions.AssertThat(carry2).IsEqual(30L);
    }

    /// <summary>
    /// 边界：carry 接近 99 而本笔金额很小时，单笔 villageCut 可能超过 5%。
    /// </summary>
    /// <remarks>
    /// 这是「延迟入账」方案的**固有性质**，不是 bug：
    /// 单笔不再严格等于 5%，但累计口径严格等于 5%，且每笔总额仍然守恒。
    /// 这条测试把这个行为**钉住**，免得将来有人以为是 bug 去"修"。
    /// </remarks>
    [TestCase]
    public void Compute_LargeCarryWithTinyTransaction_MayExceedFivePercentOfThatTransaction()
    {
        var (seller, village, carry) = StallFee.Compute(1, 99);

        Assertions.AssertThat(village).IsEqual(1L);     // 1 金的交易，村库入 1 金
        Assertions.AssertThat(seller).IsEqual(0L);     // 卖家这 1 金全归了村库
        Assertions.AssertThat(carry).IsEqual(4L);
        Assertions.AssertThat(seller + village).IsEqual(1L);   // 仍然守恒
    }
}
