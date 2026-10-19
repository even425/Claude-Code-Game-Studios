using System;
using System.Collections.Generic;
using System.Linq;

namespace ClaudeCodeGameStudios.Application.Stall;

/// <summary>
/// 自主购物结果的<b>可读原因</b> —— 第四阶段 M5 的「自主行为结果」反馈，
/// 以及 R-07 追加的<b>玩家可见</b>短文案。
/// </summary>
/// <remarks>
/// <para><b>为什么要有它</b>：<c>ShoppingOutcome</c> 只有五个枚举值，其中
/// <c>Skipped_PrecheckFailed</c> 吞掉了「不在场 / 钱不够 / 背包满 / 结算失败」四种完全不同的原因。
/// 玩家和调试者只看到「跳过了」并不能知道<b>为什么</b>。本类把
/// <see cref="HeroBehaviorService.ShoppingResult.Detail"/> 拼进一句话，
/// 输出到 <c>--econlog</c> / <c>--shop</c> 的调试行 —— 不写进杂谈（那会刷屏）。</para>
/// <para><b>未知枚举值</b>：枚举由世界内规则产生，理论上不会出现未知值；
/// 但调试输出宁可说「未知结果」也不能静默吞掉 —— 静默是这类问题的第二层。</para>
///
/// <para><b>R-07 追加的两件事</b>：</para>
/// <list type="bullet">
/// <item><b>花名册行尾</b>（<see cref="RosterNote"/>）：标出「<b>想买却没买成</b>」的人。</item>
/// <item><b>每日汇总</b>（<see cref="MissedSummary"/>）：一条，统计今天有多少人想买没买成、
/// 分别卡在哪儿。</item>
/// </list>
/// <para>
/// <b>两者的分工</b>：花名册答「<b>谁</b>没买成」，汇总答「<b>整体</b>怎么样」。
/// 汇总<b>不列名字</b> —— 37 个名字进杂谈就是刷屏；而名字本来就在花名册上。</para>
/// </remarks>
public static class ShoppingFeedback
{
    /// <summary>行尾标记的前缀。</summary>
    public const string NotePrefix = "想买未成·";

    /// <summary>结果类别的短名（用于表格 / 日志前缀）。</summary>
    /// <param name="outcome">结果枚举。</param>
    /// <returns>非空中文短名。</returns>
    public static string Label(HeroBehaviorService.ShoppingOutcome outcome) => outcome switch
    {
        HeroBehaviorService.ShoppingOutcome.Bought => "成交",
        HeroBehaviorService.ShoppingOutcome.Skipped_AlreadyShoppedToday => "跳过·今天已买过",
        HeroBehaviorService.ShoppingOutcome.Skipped_NoDesire => "跳过·没有购买意愿",
        HeroBehaviorService.ShoppingOutcome.Skipped_NoOpenStall => "跳过·没有开着的摊位",
        HeroBehaviorService.ShoppingOutcome.Skipped_PrecheckFailed => "跳过·前置条件不满足",
        _ => "未知结果",
    };

    /// <summary>
    /// 一条完整结果：类别 + 具体原因（有 <c>Detail</c> 时）。
    /// </summary>
    /// <param name="result">购物结果。</param>
    /// <returns>例如 <c>跳过·前置条件不满足（金币不足：有 3，需 5）</c>。</returns>
    public static string Describe(HeroBehaviorService.ShoppingResult result)
    {
        string label = Label(result.Outcome);
        return string.IsNullOrEmpty(result.Detail) ? label : $"{label}（{result.Detail}）";
    }

    /// <summary>
    /// 花名册行尾的短标记 —— <b>只有「想买没买成」才非空</b>。
    /// </summary>
    /// <param name="result">购物结果。</param>
    /// <returns>例如 <c>想买未成·钱不够</c>；不值得标时返回空串。</returns>
    /// <remarks>
    /// <b>哪些不算「想买没买成」</b>：今天不想买、在远征、已经买过、村里没摊 ——
    /// 这四种都不是「他想买却没买成」，标出来只会制造噪音，
    /// 也会让真正卡住的那几条淹没在日常里。
    /// </remarks>
    public static string RosterNote(HeroBehaviorService.ShoppingResult result)
    {
        string label = ShortLabel(result.SkipReason);
        return string.IsNullOrEmpty(label) ? string.Empty : NotePrefix + label;
    }

    /// <summary>
    /// 当天的「想买没买成」汇总；<b>没有人没买成时返回 null</b>（不产生条目）。
    /// </summary>
    /// <param name="results">当天的逐人结果。</param>
    /// <returns>例如 <c>今天有 3 人想买没买成（钱不够 2 人、背包满 1 人）。</c></returns>
    /// <remarks>
    /// <b>为什么只统计不点名</b>：37 个名字进杂谈就是刷屏；
    /// 而名字本来就在花名册上（<see cref="RosterNote"/>）。
    /// 汇总只回答「今天整体怎么样、卡在哪儿」。
    /// </remarks>
    public static string? MissedSummary(IReadOnlyList<HeroBehaviorService.ShoppingResult> results)
    {
        if (results is null || results.Count == 0) return null;

        var missed = results
            .Where(r => !string.IsNullOrEmpty(ShortLabel(r.SkipReason)))
            .ToArray();

        if (missed.Length == 0) return null;

        var parts = missed
            .GroupBy(r => r.SkipReason)
            .OrderBy(g => Array.IndexOf(VisibleReasons, g.Key))
            .Select(g => $"{ShortLabel(g.Key)} {g.Count()} 人")
            .ToArray();

        return $"今天有 {missed.Length} 人想买没买成（{string.Join("、", parts)}）。";
    }

    /// <summary>
    /// 原因 → 短标签。<b>空串 = 这个原因不值得给玩家看</b>。
    /// </summary>
    /// <param name="reason">结构化原因。</param>
    /// <returns>短标签；不值得显示时返回空串。</returns>
    /// <remarks>
    /// <b>未知枚举值</b>：宁可说「未知原因」也不静默吞掉 ——
    /// 但这里<b>不能</b>把它标给玩家看（那会让一行凭空多出废话），
    /// 所以未知值返回空串：不出现在界面上，但会落在调试输出里。
    /// </remarks>
    public static string ShortLabel(HeroBehaviorService.ShoppingSkipReason reason) => reason switch
    {
        HeroBehaviorService.ShoppingSkipReason.NotEnoughGold => "钱不够",
        HeroBehaviorService.ShoppingSkipReason.BagFull => "背包满",
        HeroBehaviorService.ShoppingSkipReason.SettlementFailed => "没成交",
        HeroBehaviorService.ShoppingSkipReason.OnlyOwnStall => "只有自己的摊",
        _ => string.Empty,
    };

    /// <summary>值得给玩家看的原因（顺序即汇总里的出现顺序）。</summary>
    private static readonly HeroBehaviorService.ShoppingSkipReason[] VisibleReasons =
    {
        HeroBehaviorService.ShoppingSkipReason.NotEnoughGold,
        HeroBehaviorService.ShoppingSkipReason.BagFull,
        HeroBehaviorService.ShoppingSkipReason.SettlementFailed,
        HeroBehaviorService.ShoppingSkipReason.OnlyOwnStall,
    };
}
