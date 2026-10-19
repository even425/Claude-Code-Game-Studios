namespace ClaudeCodeGameStudios.Presentation;

/// <summary>
/// 成交事件的一句话 —— 第四阶段 M5 的「交易事件」反馈。
/// </summary>
/// <remarks>
/// <para><b>为什么单独一个纯函数</b>：这句话是玩家核对经济变化的入口
/// （谁买了什么、付了多少、摊主得多少、村金库抽多少）。它以前直接写在
/// <c>Main.EmitTradeCompleted</c> 的渲染委托里，无法被单测直接断言；
/// 抽出来之后，「有村抽 / 无村抽」两种文案可以逐字守住。</para>
/// <para><b>它不产生事件</b>：事件由 <c>Emit</c> 构造并记入 <c>EventLog</c>，
/// 本类只把一笔<b>已经成交</b>的结果变成一句话。反装饰规则不变 ——
/// 没有成交就不会调用它。</para>
/// </remarks>
public static class TradeFeedback
{
    /// <summary>
    /// 生成成交条目。例：<c>零散从纸伞的摊上买走了石料×1，付5金币（纸伞得5）。</c>
    /// </summary>
    /// <param name="buyer">买家显示名。</param>
    /// <param name="seller">摊主显示名。</param>
    /// <param name="item">物品显示名（<c>ItemId.Value</c>）。</param>
    /// <param name="qty">数量。</param>
    /// <param name="amount">买家实付（= 摊主净得 + 村抽）。</param>
    /// <param name="sellerNet">摊主净得。</param>
    /// <param name="villageCut">村金库抽成。</param>
    /// <returns>一句可进杂谈的文本。</returns>
    public static string Line(
        string buyer, string seller, string item, long qty,
        long amount, long sellerNet, long villageCut)
    {
        string head = $"{buyer}从{seller}的摊上买走了{item}×{qty}，付{amount}金币";

        return villageCut > 0
            ? $"{head}（{seller}得{sellerNet}，村抽{villageCut}）。"
            : $"{head}（{seller}得{sellerNet}）。";
    }
}
