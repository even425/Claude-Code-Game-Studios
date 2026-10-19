using System;

namespace ClaudeCodeGameStudios.Domain.Stall;

/// <summary>
/// 5% 摊位费的<b>纯函数</b>拆分（D-023 拍定）。
/// </summary>
/// <remarks>
/// <para><b>公式</b>（D-023 / P2-D07-摊位费）：</para>
/// <list type="bullet">
/// <item><c>villageCut = totalAmount × 5 / 100</c>（整数算术，<b>向下取整</b>）</item>
/// <item><c>sellerNet = totalAmount - villageCut</c></item>
/// <item><b>残差归卖家</b>（保护玩家 —— D-023 拍板）：
///   <c>100 金 → 卖家 95 + 村 5</c>；
///   <c>99 金 → villageCut = 4（99×5/100 = 4.95 → floor 4）→ 卖家 95（= 99 - 4）</c>，
///   <b>残差 0</b> 因为 99 = 4 + 95；
///   真正有残差是 <c>totalAmount × 5 % 100 ≠ 0</c> 时：
///   <c>10 金 → villageCut = 0（10×5/100 = 0.5 → floor 0）→ 卖家 10</c>（残差 10 全归卖家）。
/// </list>
///
/// <para><b>整数算术保证</b>：输入 <c>totalAmount ≥ 0</c>，输出 <c>villageCut ≥ 0</c>，
/// <c>sellerNet + villageCut == totalAmount</c>，<b>永不丢钱、永不生钱</b>。
/// 这条是 I12 防线在摊位费上的体现 —— 也是 <see cref="Compute"/> 第一个红测试要验的。</para>
///
/// <para><b>为什么 floor 而不是 ceil</b>：D-023 拍定"残差归卖家"。
/// 卖 10 金的货，卖家拿 10 金、村 0 金（不收 1 分钱）—— 避免"卖 1 金的小生意交 1 金摊位费"的荒唐。
/// 这是<b>保护玩家</b>的取舍。整百整数（如 100 金）时 5/100 整除无残差，结果与 ceil 一致。</para>
///
/// <para><b>为什么是纯函数</b>：单元测试直接断两路（卖家收的 + 村收的），无任何依赖。
/// <see cref="StallService.BuyFromStall"/> 在 I15 路径里调它，<b>只</b>把两路值塞进 <c>Settlement</c>。</para>
/// </remarks>
public static class StallFee
{
    /// <summary>5% 摊位费率（D-023 拍定）。</summary>
    public const int RatePer100 = 5;

    /// <summary>
    /// 算一笔摊位的费用拆分。
    /// </summary>
    /// <param name="totalAmount">本笔交易总金额（钱）。<b>必须 ≥ 0</b>。</param>
    /// <returns>(卖家实收, 村金库入账)。两者均 ≥ 0 且相加 = <paramref name="totalAmount"/>。</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="totalAmount"/> 为负。</exception>
    public static (long sellerNet, long villageCut) Compute(long totalAmount)
    {
        if (totalAmount < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(totalAmount), totalAmount, "总金额不得为负。");
        }
        if (totalAmount == 0)
        {
            return (0L, 0L);
        }
        // 整数算术：totalAmount × 5 / 100 向下取整
        long villageCut = (totalAmount * RatePer100) / 100L;
        long sellerNet = totalAmount - villageCut;
        return (sellerNet, villageCut);
    }
}
