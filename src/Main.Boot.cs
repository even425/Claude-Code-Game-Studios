using Godot;
using System;
using System.Globalization;
using System.Linq;

/// <summary>
/// 主场景的<b>启动参数解析</b>部分（D-119 拆分）。
///
/// <para>⚠️ <b>为什么拆出来</b>：<c>Main.cs</c> 已经顶到 UT-01 的 1200 行上限
/// ⇒ 参数解析（组合根的职责）单独住一个文件 ⇒ 两边都不越线。</para>
///
/// <para>⚠️ <b>规则只有一份</b>：报酬率 X 怎么解析、非法时怎么中止，
/// 都住在这里（<c>ParseGoldRewardUnits</c>）⇒ 采集器与真机自检走的是同一个入口。</para>
/// </summary>
public partial class Main : Node2D
{
    /// <summary>
    /// 解析 <c>--goldreward=</c>（报酬率 X，单位 1/100 金）。
    /// </summary>
    /// <returns>报酬率（单位 1/100 金）；参数非法时返回 <c>null</c>。</returns>
    /// <remarks>
    /// ⚠️ **参数非法必须明确报错并中止采集** —— 静默取整会产出
    /// 「看着像 X=1 其实不是」的样本，那比没有数据更坏（2026-10-21 实测踩过：
    /// <c>--goldreward=0.5</c> 被 <c>long</c> 解析静默丢弃，两轮采集都跑的 X=1）。
    /// </remarks>
    private static long? ParseGoldRewardUnits()
    {
        var arg = OS.GetCmdlineUserArgs()
            .FirstOrDefault(a => a.StartsWith("--goldreward=", StringComparison.Ordinal));
        if (arg is null) return DefaultGoldRewardUnits;

        var raw = arg.Substring("--goldreward=".Length);
        if (!decimal.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            || value < 0m || value > 1000m)
        {
            GD.Print($"GOLDREWARD 参数无效：「{raw}」。"
                     + "X 必须是 0 ~ 1000 之间的非负数（金），且**可以是分数**（如 0.5）。"
                     + "本次采集中止 —— 不产出数据，免得留下「看着像另一个 X」的误导样本。");
            return null;
        }

        return (long)Math.Round(value * 100m, MidpointRounding.AwayFromZero);
    }
}
