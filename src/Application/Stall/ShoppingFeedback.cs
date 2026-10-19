namespace ClaudeCodeGameStudios.Application.Stall;

/// <summary>
/// 自主购物结果的<b>可读原因</b> —— 第四阶段 M5 的「自主行为结果」反馈。
/// </summary>
/// <remarks>
/// <para><b>为什么要有它</b>：<c>ShoppingOutcome</c> 只有五个枚举值，其中
/// <c>Skipped_PrecheckFailed</c> 吞掉了「不在场 / 钱不够 / 背包满 / 结算失败」四种完全不同的原因。
/// 玩家和调试者只看到「跳过了」并不能知道<b>为什么</b>。本类把
/// <see cref="HeroBehaviorService.ShoppingResult.Detail"/> 拼进一句话，
/// 输出到 <c>--econlog</c> / <c>--shop</c> 的调试行 —— 不写进杂谈（那会刷屏）。</para>
/// <para><b>未知枚举值</b>：枚举由世界内规则产生，理论上不会出现未知值；
/// 但调试输出宁可说「未知结果」也不能静默吞掉 —— 静默是这类问题的第二层。</para>
/// </remarks>
public static class ShoppingFeedback
{
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
}
