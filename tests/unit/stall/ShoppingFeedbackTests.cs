using System;
using System.Collections.Generic;
using GdUnit4;
using ClaudeCodeGameStudios.Application.Stall;

namespace ClaudeCodeGameStudios.Tests.Stall;

/// <summary>
/// 自主购物结果 → 调试文本 / 玩家可见短文案的契约（第四阶段 M5 + R-07）。
/// </summary>
/// <remarks>
/// <para><b>为什么需要它</b>：<c>ShoppingOutcome</c> 的
/// <c>Skipped_PrecheckFailed</c> 同时代表「不在场 / 钱不够 / 背包满 / 结算失败」。
/// 只打枚举名等于把四种原因糊成一句「跳过了」—— 与设计文档要求的
/// 「成功、跳过或失败的原因能够被调试验证」不符。</para>
/// <para><b>R-07 追加</b>：花名册行尾的短标记 + 每日汇总。
/// 两者都靠 <c>ShoppingSkipReason</c> 这个结构化原因，
/// 不靠解析 <c>Detail</c> 那种自由文本。</para>
/// </remarks>
[TestSuite]
public class ShoppingFeedbackTests
{
    private static HeroBehaviorService.ShoppingResult Result(
        HeroBehaviorService.ShoppingOutcome outcome,
        HeroBehaviorService.ShoppingSkipReason reason = HeroBehaviorService.ShoppingSkipReason.None,
        string? detail = null)
        => new(outcome, null, null, null, null, null, null) { Detail = detail, SkipReason = reason };

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
            HeroBehaviorService.ShoppingSkipReason.NotEnoughGold,
            "金币不足：有 3，需 5");

        Assertions.AssertThat(ShoppingFeedback.Describe(result))
            .IsEqual("跳过·前置条件不满足（金币不足：有 3，需 5）");
    }

    // ── R-07：花名册行尾 ───────────────────────────────────────────────

    [TestCase]
    public void RosterNote_ForWorthShowingReason_ReturnsMarker()
    {
        var result = Result(
            HeroBehaviorService.ShoppingOutcome.Skipped_PrecheckFailed,
            HeroBehaviorService.ShoppingSkipReason.NotEnoughGold);

        Assertions.AssertThat(ShoppingFeedback.RosterNote(result)).IsEqual("想买未成·钱不够");
    }

    [TestCase]
    public void RosterNote_ForEachWorthShowingReason_IsNotEmpty()
    {
        var worth = new[]
        {
            HeroBehaviorService.ShoppingSkipReason.NotEnoughGold,
            HeroBehaviorService.ShoppingSkipReason.BagFull,
            HeroBehaviorService.ShoppingSkipReason.SettlementFailed,
            HeroBehaviorService.ShoppingSkipReason.OnlyOwnStall,
        };

        foreach (var reason in worth)
        {
            var result = Result(HeroBehaviorService.ShoppingOutcome.Skipped_PrecheckFailed, reason);
            Assertions.AssertThat(ShoppingFeedback.RosterNote(result)).IsNotEmpty();
        }
    }

    /// <summary>「今天不想买」「在远征」「已经买过」「村里没摊」都不是"想买没买成"。</summary>
    [TestCase]
    public void RosterNote_ForDailyNoiseReasons_IsEmpty()
    {
        var noise = new[]
        {
            HeroBehaviorService.ShoppingSkipReason.NoDesire,
            HeroBehaviorService.ShoppingSkipReason.NotPresent,
            HeroBehaviorService.ShoppingSkipReason.AlreadyShopped,
            HeroBehaviorService.ShoppingSkipReason.NoOpenStall,
        };

        foreach (var reason in noise)
        {
            var result = Result(HeroBehaviorService.ShoppingOutcome.Skipped_PrecheckFailed, reason);
            Assertions.AssertThat(ShoppingFeedback.RosterNote(result)).IsEmpty();
        }
    }

    [TestCase]
    public void RosterNote_ForBought_IsEmpty()
    {
        var result = Result(HeroBehaviorService.ShoppingOutcome.Bought);

        Assertions.AssertThat(ShoppingFeedback.RosterNote(result)).IsEmpty();
    }

    // ── R-07：每日汇总 ─────────────────────────────────────────────────

    [TestCase]
    public void MissedSummary_NobodyMissed_ReturnsNull()
    {
        var results = new[]
        {
            Result(HeroBehaviorService.ShoppingOutcome.Bought),
            Result(HeroBehaviorService.ShoppingOutcome.Skipped_NoDesire,
                   HeroBehaviorService.ShoppingSkipReason.NoDesire),
        };

        Assertions.AssertThat(ShoppingFeedback.MissedSummary(results)).IsNull();
    }

    [TestCase]
    public void MissedSummary_CountsByReason()
    {
        var results = new[]
        {
            Result(HeroBehaviorService.ShoppingOutcome.Skipped_PrecheckFailed,
                   HeroBehaviorService.ShoppingSkipReason.NotEnoughGold),
            Result(HeroBehaviorService.ShoppingOutcome.Skipped_PrecheckFailed,
                   HeroBehaviorService.ShoppingSkipReason.NotEnoughGold),
            Result(HeroBehaviorService.ShoppingOutcome.Skipped_PrecheckFailed,
                   HeroBehaviorService.ShoppingSkipReason.BagFull),
        };

        Assertions.AssertThat(ShoppingFeedback.MissedSummary(results))
            .IsEqual("今天有 3 人想买没买成（钱不够 2 人、背包满 1 人）。");
    }

    /// <summary>汇总**不点名** —— 37 个名字进杂谈就是刷屏。</summary>
    [TestCase]
    public void MissedSummary_DoesNotNameAnyone()
    {
        var results = new[]
        {
            Result(HeroBehaviorService.ShoppingOutcome.Skipped_PrecheckFailed,
                   HeroBehaviorService.ShoppingSkipReason.NotEnoughGold),
        };

        string? summary = ShoppingFeedback.MissedSummary(results);

        Assertions.AssertThat(summary).IsNotNull();
        Assertions.AssertThat(summary!.Contains("人想买没买成")).IsTrue();
    }

    [TestCase]
    public void MissedSummary_EmptyResults_ReturnsNull()
    {
        Assertions.AssertThat(
            ShoppingFeedback.MissedSummary(Array.Empty<HeroBehaviorService.ShoppingResult>())).IsNull();
    }

    // ── D-053：未成口径只有一份 ───────────────────────────────────────

    /// <summary>
    /// 「值得给玩家看」与「值得落进经济流水」<b>必须是同一份规则</b>。
    /// </summary>
    /// <remarks>
    /// <b>为什么这条重要</b>：R-07 用「哪些原因值得显示」决定花名册行尾与每日汇总，
    /// R-05 三轮（D-053）用同一口径决定「哪些未成落进流水」。
    /// 两边若各写一份，早晚对不上 —— 于是流水里的分母和玩家看到的汇总对不上，
    /// 「背包满占未成原因多少」这个结论就废了。
    /// </remarks>
    [TestCase]
    public void IsWorthShowing_MatchesShortLabelVisibility_ForEveryReason()
    {
        foreach (var reason in Enum.GetValues<HeroBehaviorService.ShoppingSkipReason>())
        {
            bool worthShowing = ShoppingFeedback.IsWorthShowing(reason);
            bool hasShortLabel = !string.IsNullOrEmpty(ShoppingFeedback.ShortLabel(reason));

            Assertions.AssertThat(worthShowing).IsEqual(hasShortLabel);
        }
    }

    /// <summary>四种「想买未成」的原因都算未成。</summary>
    [TestCase]
    public void IsWorthShowing_ForUnfilledReasons_IsTrue()
    {
        var worth = new[]
        {
            HeroBehaviorService.ShoppingSkipReason.NotEnoughGold,
            HeroBehaviorService.ShoppingSkipReason.BagFull,
            HeroBehaviorService.ShoppingSkipReason.SettlementFailed,
            HeroBehaviorService.ShoppingSkipReason.OnlyOwnStall,
        };

        foreach (var reason in worth)
        {
            Assertions.AssertThat(ShoppingFeedback.IsWorthShowing(reason)).IsTrue();
        }
    }

    /// <summary>四种「噪音」原因都不算未成（记进去会污染分母）。</summary>
    [TestCase]
    public void IsWorthShowing_ForNoiseReasons_IsFalse()
    {
        var noise = new[]
        {
            HeroBehaviorService.ShoppingSkipReason.None,
            HeroBehaviorService.ShoppingSkipReason.NotPresent,
            HeroBehaviorService.ShoppingSkipReason.NoDesire,
            HeroBehaviorService.ShoppingSkipReason.AlreadyShopped,
            HeroBehaviorService.ShoppingSkipReason.NoOpenStall,
        };

        foreach (var reason in noise)
        {
            Assertions.AssertThat(ShoppingFeedback.IsWorthShowing(reason)).IsFalse();
        }
    }
}
