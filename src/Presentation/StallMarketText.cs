using System;
using ClaudeCodeGameStudios.Application.Stall;
using DomainStall = ClaudeCodeGameStudios.Domain.Stall.Stall;

namespace ClaudeCodeGameStudios.Presentation;

/// <summary>
/// 摊位面板的<b>全部文案</b>（R-02）—— 纯函数，可逐字测。
/// </summary>
/// <remarks>
/// <para><b>为什么文案要抽出来</b>：面板上的每一行都带着"看得懂才能用"的约定
/// （哪个是摊主、库存剩多少、单价多少、这个人最多能买几份）。
/// 这些约定一旦散在 Node 构造代码里，就<b>没有任何东西能验证它们</b> ——
/// 和 <see cref="EconomySummary"/> / <see cref="TradeFeedback"/> 同一个理由。</para>
///
/// <para><b>宽度纪律</b>：面板宽 380 像素，一行要放得下"名字 · 物品 ×数量 · 单价 N"。
/// 所以分隔符固定用 <c>·</c>，数量用 <c>×N</c>，单价写成"单价 N"而不是"单价:N" ——
/// 少一个字符，37 个英雄名列出来时就不会被裁。</para>
/// </remarks>
public static class StallMarketText
{
    /// <summary>行内分隔符。</summary>
    public const string Separator = " · ";

    /// <summary>数量前缀（<c>石料×3</c>）。</summary>
    public const string QuantityMark = "×";

    /// <summary>单价的前缀。</summary>
    public const string UnitPricePrefix = "单价 ";

    /// <summary>背包还能装几份时，候选行的前缀。</summary>
    public const string CanBuyPrefix = "可买 ";

    /// <summary>售罄后缀。</summary>
    public const string SoldOutSuffix = "（已售罄）";

    /// <summary>
    /// 一行摊位，例如 <c>瓦德 · 石料 ×3 · 单价 5</c>。
    /// </summary>
    /// <param name="sellerName">摊主显示名。</param>
    /// <param name="stall">摊位。</param>
    /// <returns>摊位行文本；<paramref name="stall"/> 为 null 时返回空字符串。</returns>
    /// <remarks>
    /// <b>售罄也要显示</b>：玩家该知道"这个摊卖完了"，而不是发现它凭空消失。
    /// 售罄的摊位<b>不</b>作为候选（<c>StallMarket.Candidates</c> 只对 Open 摊位有意义），
    /// 但它在列表里占一行，让人看得见。
    /// </remarks>
    public static string StallLine(string sellerName, DomainStall stall)
    {
        if (stall is null) return string.Empty;

        string head = string.IsNullOrEmpty(sellerName) ? stall.Owner.Value : sellerName;
        string item = $"{stall.ItemId.Value} {QuantityMark}{stall.Stock}";

        if (stall.Status != ClaudeCodeGameStudios.Domain.Stall.StallStatus.Open)
        {
            return head + Separator + item + Separator + SoldOutSuffix;
        }

        return head + Separator + item + Separator + UnitPricePrefix + stall.UnitPrice;
    }

    /// <summary>
    /// 一行候选买家，例如 <c>灰七 · 金95 · 可买 19 份</c>。
    /// </summary>
    /// <param name="offer">候选买家。</param>
    /// <returns>买家行文本。</returns>
    /// <remarks>
    /// <b>把"可买几份"写出来</b>：玩家点了才知道会花多少钱、拿多少货。
    /// 只写"灰七 · 金95"的话，玩家还得在脑子里算一遍除法。
    /// </remarks>
    public static string BuyerLine(BuyerOffer offer)
    {
        return offer.Name
             + Separator + "金" + offer.Gold
             + Separator + CanBuyPrefix + offer.MaxQty + " 份";
    }

    /// <summary>
    /// 购买失败的原因（面板内回执，<b>不进杂谈</b>）。
    /// </summary>
    /// <param name="outcome">结果分类。</param>
    /// <returns>中文原因；未知枚举值说"未知结果"而不是静默吞掉。</returns>
    /// <remarks>
    /// <b>为什么失败不进杂谈</b>：杂谈是"村里发生了什么"，不是"玩家的每次点击的回执"。
    /// 面板里当场显示就够了 —— 玩家看得到、也不打扰别人。
    /// <para>
    /// <b>为什么 <c>Bought</c> 也在表里</b>：这个方法是<b>枚举全覆盖</b>的文案表。
    /// 摆摊那一侧的 <c>ShoppingFeedback</c> 也是这么做的 ——
    /// 「每个枚举值都必须有一句人话」，是为了让任何一次调用都不会掉进
    /// 什么都不说的分支。成交不会走到这里，但表里有它，才不会留下一个空洞。
    /// </para>
    /// </remarks>
    public static string Rejected(StallService.BuyOutcome outcome) => outcome switch
    {
        StallService.BuyOutcome.Bought => "买成了。",
        StallService.BuyOutcome.DuplicateRequest => "这笔已经结过了。",
        StallService.BuyOutcome.InvalidRequest => "这一单不成立（人或数不对）。",
        StallService.BuyOutcome.SameHero => "不能买自己的摊。",
        StallService.BuyOutcome.NoStall => "这个摊已经收了。",
        StallService.BuyOutcome.StallNotOpen => "这个摊现在没在开。",
        StallService.BuyOutcome.WrongItem => "摊上的货和想买的不一样。",
        StallService.BuyOutcome.InsufficientStock => "货不多了。",
        StallService.BuyOutcome.CannotAfford => "钱不够。",
        StallService.BuyOutcome.BagRejected => "背包装不下了。",
        StallService.BuyOutcome.WalletRejected => "摊主那边收不了钱。",
        _ => "没买成（未知结果）。",
    };

    /// <summary>
    /// 成交后的回执，例如 <c>灰七 买走 石料×19，付 95 金币。</c>。
    /// </summary>
    /// <param name="buyerName">买方显示名。</param>
    /// <param name="item">物品名。</param>
    /// <param name="qty">买了几份。</param>
    /// <param name="amount">实付金币。</param>
    /// <returns>回执文本。</returns>
    public static string Bought(string buyerName, string item, long qty, long amount)
    {
        return $"{buyerName} 买走 {item}{QuantityMark}{qty}，付 {amount} 金币。";
    }
}
