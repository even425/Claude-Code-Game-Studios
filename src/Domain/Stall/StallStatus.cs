namespace ClaudeCodeGameStudios.Domain.Stall;

/// <summary>
/// 摊位状态机（单向迁移，I14）。
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><b>Open</b> —— 摆出来卖，能成交</item>
/// <item><b>SoldOut</b> —— 商品卖完了，不接受新订单（<b>不</b>自动 Close —— 等英雄主动 Close）</item>
/// <item><b>Closed</b> —— 英雄收摊。Closed 后摊主可开新摊</item>
/// </list>
///
/// <para><b>不可回退</b>：Open → SoldOut → Closed（一旦 Close 不再 Open）。
/// 与 <see cref="ClaudeCodeGameStudios.Domain.Trade.TradeStatus"/> 的单向迁移一致。</para>
/// </remarks>
public enum StallStatus
{
    /// <summary>摆出在卖，能成交。</summary>
    Open = 0,
    /// <summary>商品售罄，不接受新订单。<b>不</b>自动 Close。</summary>
    SoldOut = 1,
    /// <summary>英雄已收摊。Closed 后摊主可开新摊。</summary>
    Closed = 2,
}
