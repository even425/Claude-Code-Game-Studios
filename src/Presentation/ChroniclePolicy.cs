using System;
using System.Collections.Generic;

namespace ClaudeCodeGameStudios.Presentation;

/// <summary>
/// 村庄杂谈的取舍规则 —— <b>哪些已发生的事件值得占用玩家一行注意力</b>。
/// </summary>
/// <remarks>
/// <para><b>2026-10-19 开发者拍板</b>：像「采集」「建屋」这种整天重复发生的日常劳动，
/// 不必逐条显示在村庄杂谈里。它们是真实发生的事 —— 事件照常记入事件日志、
/// 照常改变材料与工地进度，只是<b>不该刷屏</b>。</para>
/// <para><b>为什么在呈现层过滤，而不是不产生事件</b>：基线 §7.2 要求杂谈可追溯到事件、
/// §7.4 的 C1/C2 依赖事件日志；存档与状态追溯也要它。真正该少的是
/// <b>玩家读到的条数</b>，不是<b>世界里发生的事</b>。所以过滤放在
/// <c>ChronicleFeed.Publish</c> 的渲染出口（渲染委托返回 null 即不产生条目），
/// 事件日志一行不少。</para>
/// <para><b>为什么是黑名单而不是白名单</b>：新增的活动默认进杂谈 ——
/// 先让玩家看见，再决定要不要藏。白名单会让「忘记登记」变成静默消失，
/// 那比噪音更难查。</para>
/// </remarks>
public static class ChroniclePolicy
{
    /// <summary>日常劳作的活动名 —— 高频、低信息量，只改材料 / 工地进度。</summary>
    /// <remarks>
    /// 取值对应 <c>DailyLife</c> 的活动集：<c>采集</c> 加材料、<c>建屋</c> 推进工地。
    /// <b>不要改写成别的字符串</b>，否则过滤会静默失效 ——
    /// <c>ChroniclePolicyTests</c> 用同样的字面量守住这条契约。
    /// </remarks>
    private static readonly HashSet<string> RoutineLabor =
        new(StringComparer.Ordinal) { "采集", "建屋" };

    /// <summary>这项活动做完之后，值得在村庄杂谈里说一句吗？</summary>
    /// <param name="activity">活动名（<c>ActivityId.Value</c>）。</param>
    /// <returns>日常劳动（<c>采集</c> / <c>建屋</c>）为 false；其余活动为 true。</returns>
    public static bool IsWorthShowing(string activity)
        => !RoutineLabor.Contains(activity);
}
