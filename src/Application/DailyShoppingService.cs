using System;
using System.Collections.Generic;
using ClaudeCodeGameStudios.Domain.Heroes;

namespace ClaudeCodeGameStudios.Application.Stall;

/// <summary>
/// 每日自主购物的<b>调度</b>（第四阶段 3B）：保证「同一游戏日只尝试一次」。
/// </summary>
/// <remarks>
/// <para><b>为什么单独一层</b>：<see cref="HeroBehaviorService"/> 只负责“一名英雄今天买不买”，
/// 它不记录“今天是否已经跑过”。如果把“跑没跑过”的判断写在渲染帧循环里，
/// 要么每帧重复尝试（失败/跳过会一直重试），要么需要散落各处的标志位。
/// 本类持有一个<b>按游戏日</b>的闸门：同一天第二次调用直接返回空结果。</para>
/// <para><b>与 <c>LastShoppingDay</c> 的分工</b>：<c>LastShoppingDay</c> 是“这名英雄今天成交过”
/// （只有成交才写）；本类只负责“今天整体是否已经尝试过”。跳过/失败<b>不</b>消耗
/// <c>LastShoppingDay</c>，但也不会在同一游戏日被重复尝试。</para>
/// <para><b>随机</b>：不引入新随机源；全部随机都在 <see cref="HeroBehaviorService"/> 内部，
/// 由构造时注入的 <c>Func&lt;double&gt;</c>（生产中是 <c>Main._rng</c>）决定。</para>
/// </remarks>
public sealed class DailyShoppingService
{
    private readonly HeroBehaviorService _behavior;

    /// <summary>上一次已尝试的游戏日；<see cref="int.MinValue"/> = 还没跑过。</summary>
    private int _lastRunDay = int.MinValue;

    /// <param name="behavior">单英雄购物决策。</param>
    /// <exception cref="ArgumentNullException"><paramref name="behavior"/> 为 null。</exception>
    public DailyShoppingService(HeroBehaviorService behavior)
    {
        _behavior = behavior ?? throw new ArgumentNullException(nameof(behavior));
    }

    /// <summary>上一次已尝试的游戏日。</summary>
    public int LastRunDay => _lastRunDay;

    /// <summary>
    /// 恢复「上一次已尝试的游戏日」——<b>仅供读档</b>。
    /// </summary>
    /// <remarks>
    /// 不恢复它，读档后同一天会再跑一遍「每人最多买一次」的尝试：
    /// 已经成交的人会被 <c>LastShoppingDay</c> 挡住，但当天还没成交的人会多一次机会，
    /// 读档前后世界不一致。走本方法而不是公开 setter，让“只有一个入口”保持可见。
    /// </remarks>
    /// <param name="day">游戏日号（第 1 天起）；<see cref="int.MinValue"/> = 从未运行。</param>
    /// <exception cref="ArgumentOutOfRangeException">日号既不是 int.MinValue，也不是 ≥ 1 的合法日号。</exception>
    public void RestoreLastRunDay(int day)
    {
        if (day != int.MinValue && day < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(day), day, "日号必须 ≥ 1（第一天），或 int.MinValue 表示从未运行。");
        }

        _lastRunDay = day;
    }

    /// <summary>
    /// 跑一个游戏日的自主购物；<b>同一游戏日第二次调用返回空列表</b>。
    /// </summary>
    /// <param name="today">游戏日序号（<c>VillageClock.Day</c>）。</param>
    /// <param name="heroIds">本日参与尝试的英雄（顺序无关）。</param>
    /// <param name="simTime">模拟时间（传给成交记录）。</param>
    /// <returns>每名英雄一条结果（含跳过/失败）；同日重复调用为空。</returns>
    public IReadOnlyList<HeroBehaviorService.ShoppingResult> RunDay(
        int today, IReadOnlyList<HeroId> heroIds, double simTime)
    {
        if (today == _lastRunDay) return Array.Empty<HeroBehaviorService.ShoppingResult>();

        _lastRunDay = today;

        var results = new List<HeroBehaviorService.ShoppingResult>();
        if (heroIds is null) return results;

        foreach (var id in heroIds)
        {
            results.Add(_behavior.SimulateShopping(id, today, simTime));
        }

        return results;
    }
}
