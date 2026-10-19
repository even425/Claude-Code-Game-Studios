using System;
using GdUnit4;
using ClaudeCodeGameStudios.Application.Stall;

namespace ClaudeCodeGameStudios.Tests.Stall;

/// <summary>
/// 自主购物结果 → 调试文本的契约（第四阶段 M5）。
/// </summary>
/// <remarks>
/// <para><b>为什么需要它</b>：<c>ShoppingOutcome</c> 的
/// <c>Skipped_PrecheckFailed</c> 同时代表「不在场 / 钱不够 / 背包满 / 结算失败」。
/// 只打枚举名等于把四种原因糊成一句「跳过了」—— 与设计文档要求的
/// 「成功、跳过或失败的原因能够被调试验证」不符。</para>
/// </remarks>
[TestSuite]
public class ShoppingFeedbackTests
{
    private static HeroBehaviorService.ShoppingResult Result(
        HeroBehaviorService.ShoppingOutcome outcome, string? detail = null)
        => new(outcome, null, null, null, null, null, null) { Detail = detail };

    /// <summary>五个枚举值都必须有可读短名，不能落到「未知结果」。</summary>
    [TestCase]
    public void EveryOutcome_HasReadableLabel()
    {
        foreach (HeroBehaviorService.ShoppingOutcome outcome in
                 Enum.GetValues(typeof(HeroBehaviorService.ShoppingOutcome)))
        {
            string label = ShoppingFeedback.Label(outcome);
            Assertions.AssertThat(string.IsNullOrWhiteSpace(label)).IsFalse();
            Assertions.AssertThat(label).IsNotEqual("未知结果");
        }
    }

    [TestCase]
    public void Describe_WithoutDetail_IsLabelOnly()
    {
        var result = Result(HeroBehaviorService.ShoppingOutcome.Skipped_NoOpenStall);

        Assertions.AssertThat(ShoppingFeedback.Describe(result))
            .IsEqual("跳过·没有开着的摊位");
    }

    [TestCase]
    public void Describe_WithDetail_KeepsReason()
    {
        var result = Result(
            HeroBehaviorService.ShoppingOutcome.Skipped_PrecheckFailed,
            "金币不足：有 3，需 5");

        Assertions.AssertThat(ShoppingFeedback.Describe(result))
            .IsEqual("跳过·前置条件不满足（金币不足：有 3，需 5）");
    }
}
