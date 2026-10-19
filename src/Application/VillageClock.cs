using System;
using ClaudeCodeGameStudios.Domain.Clock;

namespace ClaudeCodeGameStudios.Application;

/// <summary>
/// 村庄时钟 —— 把领域层的 <see cref="SimClock"/> 包装成界面与日常事件需要的形状。
/// </summary>
/// <remarks>
/// <para>
/// <b>它为什么存在</b>：领域时钟刻意<b>不</b>提供三样东西 ——
/// <list type="bullet">
/// <item><b>显示文本</b>（<c>Clock</c> / <c>Phase</c>）—— 那是表现层的事，领域层不该产出字符串</item>
/// <item><b>跨天粘合</b>（旧 <c>WorldClock.Rolled</c> / <c>ConsumeRoll()</c>）——
/// 那是「调用方需要在跨过日界时做一次事情」的机制，属于应用层</item>
/// </list>
/// 所以两者都放这里，而不是塞回领域层。</para>
///
/// <para><b>它取代了 <c>WorldClock</c></b>（已作废：<c>07</c> D-001）。
/// 旧时钟的 <c>DeepNight</c> 与「人下班后停工」整套前提都已作废，不再提供。</para>
///
/// <para><b>【待定】</b>一现实秒等于多少模拟秒属 <c>07</c> 的 <b>T-001 / P-01 / P-02</b>。
/// 本类只提供换算的<b>注入点</b>，默认恒等（1 现实秒 = 1 模拟秒），**不发明任何数值</b>。</para>
/// </remarks>
public sealed class VillageClock
{
    private readonly SimClock _clock;
    private long _lastDaySeq;

    /// <param name="simSecondsPerDay">一个模拟日的模拟秒数。【待定】T-001 / P-01。</param>
    /// <param name="realToSimSeconds">现实秒 → 模拟秒的换算。【待定】T-001 / P-02。</param>
    public VillageClock(double simSecondsPerDay, Func<double, double>? realToSimSeconds = null,
                         double startSimTime = 0.0)
    {
        _clock = new SimClock(simSecondsPerDay, realToSimSeconds ?? (r => r), startSimTime);
        _lastDaySeq = _clock.DaySeq;
    }

    /// <summary>
    /// 从命令行参数解析日长，覆盖默认值。<b>【待定】</b>默认值属 P-01。
    /// </summary>
    /// <param name="args">命令行用户参数。</param>
    /// <param name="fallback">未指定时使用的日长。</param>
    /// <returns>日长（模拟秒/日）。</returns>
    public static double ResolveDayLength(string[] args, double fallback)
    {
        foreach (var a in args ?? Array.Empty<string>())
        {
            if (a.StartsWith("--daylen=", StringComparison.Ordinal)
                && double.TryParse(a[9..], out var parsed) && parsed > 0.0)
            {
                return parsed;
            }
        }

        return fallback;
    }

    /// <summary>一个模拟日的模拟秒数。【待定】T-001 / P-01。</summary>
    public double SimSecondsPerDay => _clock.SimSecondsPerDay;

    /// <summary>当前模拟时间（模拟秒）。</summary>
    public double Now => _clock.Now;

    /// <summary>今天是第几天，从 1 起。</summary>
    public long Day => _clock.DaySeq;

    /// <summary>当前日内时刻，<c>[0,24)</c>。</summary>
    public double Hour => _clock.DayProgress * 24.0;

    /// <summary>自开局以来的绝对小时数（跨天单调递增）。</summary>
    /// <remarks>由 <see cref="Now"/> 直接换算：<c>Now / (一日秒数 / 24)</c>。</remarks>
    public double AbsoluteHour => _clock.Now / (_clock.SimSecondsPerDay / 24.0);

    /// <summary>时钟是否刚刚跨过日界（供「每日事件跑一次」使用）。</summary>
    public bool Rolled { get; private set; }

    /// <summary>确认已处理本次跨天。</summary>
    public void ConsumeRoll() => Rolled = false;

    /// <summary>显示用时刻串，如 <c>10:09 上午</c>。</summary>
    public string Clock => $"{Hour:00.0}:00 {Phase}";

    /// <summary>显示用时段。【已确认】仅用于显示，不参与任何规则。</summary>
    public string Phase => Hour < 5 ? "深夜" : Hour < 11 ? "清晨" : Hour < 14 ? "上午"
                          : Hour < 18 ? "下午" : Hour < 22 ? "傍晚" : "夜里";

    /// <summary>
    /// 造一个村庄专用的时钟：读 <c>--daylen=</c> 覆盖日长，并从<b>上午</b>开局。
    /// </summary>
    /// <param name="args">命令行用户参数。</param>
    /// <returns>时钟。</returns>
    /// <remarks>
    /// <b>开局时刻 10:00 是【建议】</b>，沿用原型一直以来的表现，让玩家一进游戏
    /// 就看到一个正在过日子的村庄，而不是从午夜开始等。
    /// 它<b>不参与任何规则</b>——作废的时区作息已整体移除（<c>07</c> D-001），
    /// 这里只是起始画面。
    /// </remarks>
    public static VillageClock NewVillageClock(string[] args)
    {
        const double dayLength = 75.0;                 // 【待定】T-001 / P-01
        const double startHourOfDay = 10.0;            // 【建议】仅为起始画面
        var resolved = ResolveDayLength(args, dayLength);
        var startSimTime = resolved * (startHourOfDay / 24.0);
        return new VillageClock(resolved, r => r, startSimTime);
    }

    /// <summary>推进。</summary>
    /// <param name="realDeltaSeconds">现实秒增量。</param>
    public void Tick(double realDeltaSeconds)
    {
        _clock.Advance(realDeltaSeconds);

        // 跨天信号由 DaySeq 派生，而不是另设一个计数器 ——
        // 那样两个数字可能不一致。
        if (_clock.DaySeq != _lastDaySeq)
        {
            _lastDaySeq = _clock.DaySeq;
            Rolled = true;
        }
    }

    /// <summary>
    /// 把模拟时间<b>直接置位</b>到 <paramref name="simTime"/>。<b>只给读档用。</b>
    /// </summary>
    /// <remarks>
    /// <para><b>这里原本还有一个 <c>AdvanceTo(target)</c>（只向前推进），
    /// 已删除。</b>接线时先用了它，结果读档后模拟时间停在 531 秒而存档是 31 秒 ——
    /// 因为存档比当时更早，而它对「目标在更早」是<b>静默无操作</b>的。
    /// 留着两个语义相近、时间方向相反的 API，就是下一次同类 bug 的配方。
    /// 移动时钟<b>只有这一个入口</b>。</para>
    /// </remarks>
    /// <param name="simTime">目标模拟时刻（秒），必须非负且有限。</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="simTime"/> 为负或非有限数。</exception>
    public void SetSimTime(double simTime) => _clock.SetSimTime(simTime);

    /// <summary>时间倍率。【待定】允许的档位属 P-03 / T-001。</summary>
    public double TimeScale
    {
        get => _clock.TimeScale;
        set => _clock.TimeScale = value;
    }
}
