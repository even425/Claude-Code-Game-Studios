using System;

namespace ClaudeCodeGameStudios.Domain.Stall;

/// <summary>
/// 5% 摊位费的<b>纯函数</b>拆分（D-023 拍定；D-052 修订余数归属）。
/// </summary>
/// <remarks>
/// <para><b>公式</b>（D-023 / P2-D07-摊位费）：</para>
/// <list type="bullet">
/// <item><c>taxUnits = totalAmount × 5</c> —— 单位是 <b>1/100 金</b></item>
/// <item><c>accumulated = carry + taxUnits</c> —— 把上一笔留下的零头接上</item>
/// <item><c>villageCut = accumulated / 100</c> —— 本次计入村庄金库的<b>整金</b></item>
/// <item><c>newCarry = accumulated % 100</c> —— 0..99，留到下一笔</item>
/// <item><c>sellerNet = totalAmount - villageCut</c></item>
/// </list>
///
/// <para><b>余数归谁（D-052 修订）</b>：D-023 原本拍「残差归卖家（保护玩家）」。
/// 修订后<b>残差累计归村庄金库</b>，凑够 1 金时入账。**税率比例仍是 5%**，
/// 变的只是「零头最终归谁」。</para>
///
/// <para><b>为什么要改</b>：原实现逐笔 <c>floor</c>，单价 3/5/8 时每笔都 floor 到 0 ——
/// R-08 实测 5088 金成交额抽出 <b>0 金</b>，D-023 的「5%」是一条<b>死规则</b>。
/// 累计之后，任何单价都最终能收齐 5%。</para>
///
/// <para><b>整数算术保证</b>：全程 <c>long</c>，无浮点。
/// 每笔满足 <c>sellerNet + villageCut == totalAmount</c>（<b>钱不生不灭</b>）；
/// 累计满足 <c>sum(villageCut) + carry == floor(5% × 总成交额)</c>。</para>
///
/// <para><b>为什么用「不溢出的分解式」而不是裸乘法</b>：
/// <c>totalAmount × 5</c> 在 <c>totalAmount &gt; long.MaxValue / 5</c> 时会溢出。
/// 分解成 <c>(totalAmount / 100) × 500 + (totalAmount % 100) × 5</c> 后，
/// 前半 ≤ <c>totalAmount / 20</c>、后半 &lt; 500，<b>都不会溢出</b>。</para>
///
/// <para><b>为什么是纯函数</b>：<c>carry</c> 是<b>入参</b>，状态由调用方（<c>StallService</c>）持有。
/// 单元测试可以直接断两路 + 断累计，无任何依赖。</para>
/// </remarks>
public static class StallFee
{
    /// <summary>5% 摊位费率（D-023 拍定）。</summary>
    public const int RatePer100 = 5;

    /// <summary>
    /// 百分之一金的换算基数：<c>1 金 = 100 个 taxUnits</c>。
    /// </summary>
    public const int UnitsPerGold = 100;

    /// <summary>
    /// 算一笔摊位的费用拆分（<b>含余数累计</b>）。
    /// </summary>
    /// <param name="totalAmount">本笔交易总金额（钱）。<b>必须 ≥ 0</b>。</param>
    /// <param name="carry">
    /// 上笔留下的税额余数（单位：1/100 金，<b>0..99</b>）。
    /// <b>0 = 无历史余数</b>；此时结果与「逐笔 floor、残差归卖家」的旧行为<b>逐位相同</b>。
    /// </param>
    /// <returns>
    /// (卖家实收, 村金库入账, 新余数)。前两项均 ≥ 0 且相加 = <paramref name="totalAmount"/>；
    /// 第三项 ∈ [0, 99]。
    /// </returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="totalAmount"/> 为负，或 <paramref name="carry"/> 不在 [0, 99]。
    /// </exception>
    /// <remarks>
    /// <b>边界行为（需知晓）</b>：当 <paramref name="carry"/> 接近 99 而本笔金额很小，
    /// 本次 <c>villageCut</c> 可能<b>超过</b>该笔的 5%（极端例：carry=99、本笔 1 金
    /// → 村库入 1 金、卖家拿 0）。这是「延迟入账」方案的固有性质：
    /// 单笔不再严格等于 5%，但<b>累计口径严格等于 5%</b>，且每笔总额仍然守恒。
    /// </remarks>
    public static (long sellerNet, long villageCut, long newCarry) Compute(
        long totalAmount, long carry = 0)
    {
        if (totalAmount < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(totalAmount), totalAmount, "总金额不得为负。");
        }

        if (carry < 0 || carry >= UnitsPerGold)
        {
            throw new ArgumentOutOfRangeException(
                nameof(carry), carry, $"余数必须在 [0, {UnitsPerGold - 1}] 之间。");
        }

        // taxUnits = totalAmount × 5，用不溢出的分解式。
        long taxUnits = (totalAmount / UnitsPerGold) * (RatePer100 * UnitsPerGold)
                      + (totalAmount % UnitsPerGold) * RatePer100;

        long accumulated = carry + taxUnits;   // carry < 100，taxUnits ≤ totalAmount/20 → 不溢出
        long villageCut = accumulated / UnitsPerGold;
        long newCarry = accumulated % UnitsPerGold;
        long sellerNet = totalAmount - villageCut;

        return (sellerNet, villageCut, newCarry);
    }

    /// <summary>
    /// 只算<b>单笔</b>拆分，不涉及余数（<paramref name="carry"/> 视为 0）。
    /// </summary>
    /// <param name="totalAmount">本笔交易总金额（钱）。<b>必须 ≥ 0</b>。</param>
    /// <returns>(卖家实收, 村金库入账)。</returns>
    /// <remarks>
    /// <b>给只需要「这一笔怎么分」的调用方用</b>（不持有余数状态）。
    /// 需要累计余数的路径请用 <see cref="Compute(long, long)"/>。
    /// </remarks>
    public static (long sellerNet, long villageCut) ComputeSplit(long totalAmount)
    {
        var (sellerNet, villageCut, _) = Compute(totalAmount);
        return (sellerNet, villageCut);
    }
}
