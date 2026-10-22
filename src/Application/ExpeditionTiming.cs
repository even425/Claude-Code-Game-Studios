using System;

namespace ClaudeCodeGameStudios.Application;

/// <summary>
/// 一次出征<b>占多少时间</b>（D-075）。
/// </summary>
/// <remarks>
/// <para><b>它是什么</b>：副本任务的总时长 = <b>路程 + 战斗</b>。
/// 战斗那部分由 <c>StageDuration</c>（回合 × 每回合小时）算出，
/// 路程那部分来自任务表（<c>Job.Hours</c>）。</para>
/// <para><b>为什么要有这个类型</b>：「副本在日历上占多久」这条规则
/// <b>只应该有一处实现</b>。若它散落在按钮处理里，有人只写「路程」那一半
/// （战斗耗时被悄悄丢掉）⇒ 副本在日历上就<b>不占时间</b>了
/// ⇒ 而那<b>看起来完全正常</b>（队伍出发、回来，只是回来得早了）。</para>
/// <para>⚠️ <b>它不含任何随机</b>：同样的输入永远得到同样的时长。</para>
/// </remarks>
public static class ExpeditionTiming
{
    /// <summary>
    /// 一次出征占的总时长。
    /// </summary>
    /// <param name="travelHours">路程占多少游戏内小时（任务表给的数）。</param>
    /// <param name="battleHours">
    /// 战斗占多少游戏内小时。<b>非副本任务传 0</b>（没有战斗）。
    /// </param>
    /// <returns>总时长（游戏内小时）。</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// 路程为负，或战斗时长为负。
    /// </exception>
    public static double TotalHours(double travelHours, double battleHours)
    {
        if (!double.IsFinite(travelHours) || travelHours < 0.0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(travelHours), travelHours, "路程时长必须是非负有限数。");
        }

        if (!double.IsFinite(battleHours) || battleHours < 0.0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(battleHours), battleHours, "战斗时长必须是非负有限数。");
        }

        return travelHours + battleHours;
    }
}
