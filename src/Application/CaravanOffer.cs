using System;

namespace ClaudeCodeGameStudios.Application;

/// <summary>
/// <b>商队的报价</b>（D-080）：商队来村里卖什么、卖多便宜、有多少货。
/// </summary>
/// <remarks>
/// <para><b>为什么是「折扣」而不是「固定价」</b>：商队要<b>低于市价</b>（D-080 拍板）⇒
/// 它就该<b>跟着市价走</b>（市价涨了，商队的价也涨）⇒ 规则只说「打折几折」，
/// 具体单价在<b>开摊那一刻</b>由价格表算出来并<b>锁定</b>（与普通摊位同款：
/// 开摊时锁定单价，售出过程中不变价）。</para>
///
/// <para>⚠️ <b>库存必须有限</b>：无限制的低价货 = 玩家「低买高卖」的**无限套利**
/// （买入价 8 折、摆摊按市价卖 ⇒ 每份白赚，货无限 ⇒ 金币无限）。
/// ⇒ 库存是这条规则<b>能成立</b>的前提，不是可选项。</para>
/// </remarks>
/// <param name="ItemId">卖什么（物品标识）。</param>
/// <param name="DiscountPercent">
/// 折扣（<b>百分之多少</b>）：80 = 八折 = 付市价的 80%。
/// 必须在 [1, 100] 内：0 或负 = 白送（另一种规则），&gt;100 = 比市价还贵
/// （那就不是「低于市价」了）。越界 ⇒ <b>大声失败</b>，不静默夹取。
/// </param>
/// <param name="Stock">货有多少（份）。必须 ≥ 1（没货的商队不用来访）。</param>
public sealed record CaravanOffer(string ItemId, int DiscountPercent, long Stock)
{
    /// <inheritdoc cref="ItemId"/>
    public string ItemId { get; init; } = string.IsNullOrEmpty(ItemId)
        ? throw new ArgumentException("商队商品标识不得为空。", nameof(ItemId))
        : ItemId;

    /// <inheritdoc cref="DiscountPercent"/>
    public int DiscountPercent { get; init; } = DiscountPercent is < 1 or > 100
        ? throw new ArgumentOutOfRangeException(
            nameof(DiscountPercent), DiscountPercent,
            "折扣必须落在 [1, 100]（100 = 市价，1 = 一折）。")
        : DiscountPercent;

    /// <inheritdoc cref="Stock"/>
    public long Stock { get; init; } = Stock < 1
        ? throw new ArgumentOutOfRangeException(
            nameof(Stock), Stock, "商队库存必须 ≥ 1（没货的商队不用来访）。")
        : Stock;
}
