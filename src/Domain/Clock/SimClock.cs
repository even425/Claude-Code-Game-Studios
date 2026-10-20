using System;

namespace ClaudeCodeGameStudios.Domain.Clock;

/// <summary>
/// 模拟时钟 —— 模拟/领域层里「时间是谁在推进、推进到哪」的**唯一权威**
/// （05 §3.9 · 03 §2.1 · D-001 路线 B 的第一个领域文件）。
///
/// <para><b>纯 C#，不引用任何 Godot 类型。</b>这是 <c>src/Domain/README.md</c>
/// 写死的硬规则，由 <c>tests/unit/architecture/DomainLayerPurityTests.cs</c>
/// 在每次 <c>dotnet test</c> 时扫描强制执行。本文件必须保持可以在没有
/// Godot 运行时的环境里编译与测试（不变量 I9）。</para>
///
/// <para><b>它拥有什么</b>（05 §3.9 持有状态，逐条对应）：
/// <list type="bullet">
/// <item>当前模拟时间 <see cref="Now"/></item>
/// <item>日序号 <see cref="DaySeq"/> 与日内进度 <see cref="DayProgress"/>
///       （D-009 R-2 批准回填的两个字段）</item>
/// <item>已处理的模拟步序号 <see cref="StepCount"/></item>
/// <item>时间倍率 <see cref="TimeScale"/></item>
/// </list></para>
///
/// <para><b>两条不可违反的约束</b>：
/// <list type="number">
/// <item><b>日序号与日内进度必须互为函数</b>。两者都是
///       <see cref="Now"/> 的<b>纯函数</b>，由同一次除法算出，不各自演进。
///       两者之间的反算 <see cref="GetSimTime"/> 同样提供，
///       并且断言「正算反算结果一致」就是测试
///       <c>DayPosition_IsAFunctionOfSimTime_InBothDirections</c>
///       （05 §3.9.1）。</item>
/// <item><b>时间单调不回退</b>。不变量 I13：<see cref="Now"/> 的取值序列
///       非递减。<see cref="Advance"/> 对负的输入现实时间直接拒绝，
///       因此不存在回退路径。</item>
/// </list></para>
///
/// <para><b>【待定】——本文件不填任何数值</b>：模拟时间与现实时间的关系、
/// 模拟步长、时间倍率的取值档位，全部属 <c>07</c> 的
/// <b>T-001</b>（临时参数 <b>P-01 / P-02 / P-03</b>）。
/// 因此本类只提供<b>结构与注入点</b>：换算由构造时注入的
/// <see cref="Func{Double,Double}"/> 提供，一日多长由构造参数
/// <c>simSecondsPerDay</c> 提供。本文件<b>不给出换算公式</b>，
/// 也不给出任何默认的「现实秒 ↔ 模拟秒」比例。测试注入的是自己写的换算，
/// 不是被写死在这里的换算。</para>
///
/// <para><b>它不决定任何英雄做什么</b>。03 §2.4：步内的模块执行顺序、
/// 谁先谁后，属于「模拟时钟与调度模块」；谁做什么属于英雄模块。
/// 本类只提供时间，不含任何玩法规则。</para>
///
/// <para><b>模拟与帧率分离</b>（基线 §13.1 / 03 §2.2）：本类没有
/// <c>_Process</c>、没有 <c>SceneTreeTimer</c>、没有
/// <c>Engine.GetTicksMsec()</c>。它只能被<b>外部喂</b>时间，
/// 喂多少取决于谁调用 <see cref="Advance"/>。一个只能从引擎读时间的
/// 时钟是不可测试的 —— 所以没有。</para>
/// </summary>
public class SimClock
{
    /// <summary>模拟时间，单位为「模拟秒」。恒非负，单调不减（不变量 I13）。</summary>
    private double _now;

    /// <summary>已处理的模拟步序号，每推进一次加一（05 §3.9 持有状态）。</summary>
    private long _stepCount;

    /// <summary>时间倍率。恒为正数；1.0 是<b>恒等</b>的结构默认，不是被拍定的档位值。</summary>
    private double _timeScale = 1.0;

    /// <summary>
    /// 现实秒 → 模拟秒的换算。<b>注入点</b>，不是本层写死的公式。
    /// 【待定】它的实际换算关系属 <c>07</c> 的 <b>T-001 / P-02</b>。
    /// </summary>
    private readonly Func<double, double> _realToSimSeconds;

    /// <summary>
    /// 一个「模拟日」有多少模拟秒。【待定】取值属 <c>07</c> 的 <b>T-001</b>；
    /// 本类只把它当作换算点使用，不对它的取值做任何假设。
    /// </summary>
    private readonly double _simSecondsPerDay;

    /// <summary>
    /// 构造一个模拟时钟。
    /// </summary>
    /// <param name="simSecondsPerDay">
    /// 一个模拟日包含多少模拟秒。<b>【待定】</b>取值属 <c>07</c> 的 <b>T-001</b>，
    /// 调用方必须显式传入 —— 本类不提供默认日长，因为提供一个默认值就等于
    /// 替开发者把 T-001 填了。必须大于 0。
    /// </param>
    /// <param name="realToSimSeconds">
    /// 把「现实时间增量」换算成「模拟时间增量」的委托。<b>注入点</b>：
    /// 换算关系本身属【待定】<b>T-001 / P-02</b>，本类不发明它。
    /// 传入 <c>null</c> 是编程错误，不提供「默认换算」。
    /// </param>
    /// <param name="startSimTime">
    /// 起始模拟时间，默认 0.0。默认 0 表示<b>第一天开始的那一刻</b>；
    /// 它是一个结构起点，不是被拍定的「开局时刻」。
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="realToSimSeconds"/> 为 null。</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="simSecondsPerDay"/> 不是正数，或
    /// <paramref name="startSimTime"/> 为负数。
    /// </exception>
    public SimClock(
        double simSecondsPerDay,
        Func<double, double> realToSimSeconds,
        double startSimTime = 0.0)
    {
        if (realToSimSeconds is null)
        {
            throw new ArgumentNullException(
                nameof(realToSimSeconds),
                "现实时间到模拟时间的换算是【待定】(07 / T-001 / P-02)，"
                + "SimClock 不提供默认换算 —— 必须由调用方注入。");
        }

        if (!(simSecondsPerDay > 0.0) || double.IsNaN(simSecondsPerDay)
            || double.IsInfinity(simSecondsPerDay))
        {
            throw new ArgumentOutOfRangeException(
                nameof(simSecondsPerDay),
                simSecondsPerDay,
                "一个模拟日的模拟秒数必须为正有限数。");
        }

        if (startSimTime < 0.0 || double.IsNaN(startSimTime))
        {
            throw new ArgumentOutOfRangeException(
                nameof(startSimTime),
                startSimTime,
                "起始模拟时间不能为负 —— 时间不回退（不变量 I13）。");
        }

        _simSecondsPerDay = simSecondsPerDay;
        _realToSimSeconds = realToSimSeconds;
        _now = startSimTime;
    }

    // ── 只读状态（05 §3.9「谁读它」：全部领域模块 + 表现层显示时间）────────

    /// <summary>当前模拟时间（模拟秒）。单调不减 —— 不变量 I13。</summary>
    public double Now => _now;

    /// <summary>
    /// 当前是第几天，从 1 开始。<b>是 <see cref="Now"/> 的函数</b>，
    /// 不是独立演进的计数器。
    /// </summary>
    public long DaySeq => GetDayPosition(_now, _simSecondsPerDay).DaySeq;

    /// <summary>
    /// 日内进度，半开区间 <c>[0, 1)</c>：0 表示刚过日界，逼近 1 表示下一个日界将至。
    /// <b>是 <see cref="Now"/> 的函数</b>，与 <see cref="DaySeq"/> 同源算出。
    /// </summary>
    public double DayProgress => GetDayPosition(_now, _simSecondsPerDay).DayProgress;

    /// <summary>
    /// 已处理的模拟步序号。<b>【待定】</b>「一个模拟步代表多少模拟时间」
    /// 属 <c>07</c> 的 <b>P-01 / T-001</b>，本类只计数，不决定步长。
    /// </summary>
    public long StepCount => _stepCount;

    /// <summary>一个模拟日包含多少模拟秒（构造时注入；取值本身【待定】T-001）。</summary>
    public double SimSecondsPerDay => _simSecondsPerDay;

    /// <summary>
    /// 时间倍率。<b>【待定】</b>取值档位属 <c>07</c> 的 <b>P-03 / T-001</b>；
    /// 本类只做「必须是正数」这一结构性校验，不规定它能取哪些档位。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么是「只读属性 + 写方法」而不是公开 setter</b>（2026-10-21）：
    /// 公开的 setter 是一条<b>任何模块都能用的直达写入口</b> ——
    /// 而不变量 I10 要求「跨模块状态改变只经由拥有该数据的模块」。
    /// 收成写方法之后，<b>改倍率这件事本身</b>仍然是一个明确的动作
    /// （可以被记账、被守卫），而不是随手一个属性赋值。
    /// 守卫：<c>DomainWritePointAuditTests</c>（领域层不得有公开可写属性）。</para>
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">被设为非正数或非有限数。</exception>
    public double TimeScale => _timeScale;

    /// <summary>
    /// 设置时间倍率。<b>唯一写入口</b>（不变量 I10：跨模块状态改变只经由拥有该数据的模块）。
    /// </summary>
    /// <param name="value">新的时间倍率，必须为正有限数。</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="value"/> 非正数或非有限数。</exception>
    public void SetTimeScale(double value)
    {
        if (!(value > 0.0) || double.IsNaN(value) || double.IsInfinity(value))
        {
            throw new ArgumentOutOfRangeException(
                nameof(value), value,
                "时间倍率必须为正有限数。【待定】允许的档位见 07 / P-03。");
        }

        _timeScale = value;
    }

    // ── 推进 ────────────────────────────────────────────────────────────────

    /// <summary>
    /// 喂入一段现实时间，返回这次推进增加了多少<b>模拟</b>时间；同时步序号加一。
    /// </summary>
    /// <param name="realDeltaSeconds">
    /// 现实时间增量（秒），例如引擎的帧间隔。<b>必须非负</b> ——
    /// 负的输入不是一个「回退」请求，而是一个编程错误，所以这里直接拒绝，
    /// 而不是悄悄 clamp。不变量 I13 靠这一条保证，而不是靠调用方自觉。
    /// </param>
    /// <returns>本次增加的模拟时间（秒），已乘以 <see cref="TimeScale"/>。</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="realDeltaSeconds"/> 为负数或非有限数。
    /// </exception>
    public double Advance(double realDeltaSeconds)
    {
        if (realDeltaSeconds < 0.0 || double.IsNaN(realDeltaSeconds)
            || double.IsInfinity(realDeltaSeconds))
        {
            throw new ArgumentOutOfRangeException(
                nameof(realDeltaSeconds),
                realDeltaSeconds,
                "现实时间增量必须非负：模拟时间不回退（不变量 I13）。");
        }

        // 换算委托返回负数，等价于时间回退，同样拒绝。
        double simDelta = ConvertToSimSeconds(realDeltaSeconds);

        _now += simDelta;
        _stepCount++;
        return simDelta;
    }

    /// <summary>
    /// 只做换算，<b>不推进时间</b>：把现实时间增量换算成模拟时间增量
    /// （含 <see cref="TimeScale"/>）。
    /// <para>
    /// 固定步长累加器（03 §2.3）需要先知道「这一帧相当于多少模拟时间」
    /// 才能决定能跑几步，但时钟<b>不应该</b>因此就按整帧跳一次 ——
    /// 那正是帧率依赖的来源。所以换算与推进是分开的两件事：
    /// 先 <see cref="ConvertToSimSeconds"/>，再按步长逐次
    /// <see cref="AdvanceSimSeconds"/>。
    /// </para>
    /// </summary>
    /// <param name="realDeltaSeconds">现实时间增量（秒），必须非负且有限。</param>
    /// <returns>对应的模拟时间增量（秒），已含时间倍率。</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="realDeltaSeconds"/> 为负数或非有限数。</exception>
    /// <exception cref="InvalidOperationException">注入的换算返回了负数或非有限数。</exception>
    public double ConvertToSimSeconds(double realDeltaSeconds)
    {
        if (realDeltaSeconds < 0.0 || double.IsNaN(realDeltaSeconds)
            || double.IsInfinity(realDeltaSeconds))
        {
            throw new ArgumentOutOfRangeException(
                nameof(realDeltaSeconds),
                realDeltaSeconds,
                "现实时间增量必须非负：模拟时间不回退（不变量 I13）。");
        }

        double simDelta = _realToSimSeconds(realDeltaSeconds) * _timeScale;
        if (simDelta < 0.0 || double.IsNaN(simDelta) || double.IsInfinity(simDelta))
        {
            throw new InvalidOperationException(
                "注入的换算委托返回了非法的模拟时间增量 —— 模拟时间不回退（不变量 I13）。"
                + "换算关系属【待定】07 / T-001 / P-02，由调用方负责。");
        }

        return simDelta;
    }

    /// <summary>
    /// 直接推进指定长度的<b>模拟</b>时间（已含倍率的换算留给调用方）。
    /// 供已经算好模拟时间的驱动方使用（例如固定步长累加器）。
    /// 步序号同样加一。
    /// </summary>
    /// <param name="simDeltaSeconds">模拟时间增量（秒），必须非负。</param>
    /// <returns>本次增加的模拟时间（秒）。</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="simDeltaSeconds"/> 为负数或非有限数。
    /// </exception>
    public double AdvanceSimSeconds(double simDeltaSeconds)
    {
        if (simDeltaSeconds < 0.0 || double.IsNaN(simDeltaSeconds)
            || double.IsInfinity(simDeltaSeconds))
        {
            throw new ArgumentOutOfRangeException(
                nameof(simDeltaSeconds),
                simDeltaSeconds,
                "模拟时间增量必须非负。要把时间**拨回去**请用 SetSimTime —— "
                + "那是读档需要的操作，与本方法的入参约束是两件事。");
        }

        _now += simDeltaSeconds;
        _stepCount++;
        return simDeltaSeconds;
    }

    /// <summary>
    /// <b>直接置位</b>模拟时间。<b>只给读档用。</b>
    /// </summary>
    /// <remarks>
    /// <para><b>为什么必须有它</b>：<see cref="AdvanceSimSeconds"/> 只接受非负增量，
    /// 于是<b>无法把时间拨回去</b>。而读档经常要拨回去 ——
    /// 存档可能来自当前时刻之前。
    /// <b>逐帧「赶时间」也不是办法</b>：一次读档要补几千个模拟步，
    /// 会被「单步追帧上限」截断，落点仍然是错的 ——
    /// 症状是读档后时钟停在一个没人到过的时刻，且不会报错。</para>
    /// <para><b>它与 I13 的关系</b>：I13 是「日序号与日内进度互为函数」。
    /// 本方法写完 <c>_now</c> 之后，<see cref="DaySeq"/> 与 <see cref="DayProgress"/>
    /// 仍然由同一个 <see cref="GetDayPosition"/> 算出，因此<b>不破坏 I13</b>。</para>
    /// <para><b>不要在正常运行中调用它</b>：它绕过步数计数，
    /// 传入的时间若与已推进的步数对不上，<see cref="StepCount"/> 就与时间脱节了。</para>
    /// </remarks>
    /// <param name="simTime">目标模拟时刻（秒），必须非负且有限。</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="simTime"/> 为负或非有限数。</exception>
    public void SetSimTime(double simTime)
    {
        if (simTime < 0.0 || double.IsNaN(simTime) || double.IsInfinity(simTime))
        {
            throw new ArgumentOutOfRangeException(
                nameof(simTime), simTime, "模拟时刻必须非负且有限。");
        }

        _now = simTime;
    }

    // ── 日界换算（唯一一处日序号与日内进度的算法）────────────────────────────

    /// <summary>
    /// 把一个模拟时间拆成「日序号 + 日内进度」。
    /// <para>
    /// 这是全类<b>唯一</b>计算日界的入口：<see cref="DaySeq"/> 与
    /// <see cref="DayProgress"/> 两个属性都调用它，
    /// 所以两者按构造就是同一个函数的两路输出，不可能各自演进
    /// （05 §3.9.1）。
    /// </para>
    /// <para><b>【待定】</b>日界落在模拟时间的哪一点、一日多长，
    /// 由 <c>07</c> 的 <b>T-001</b> 决定。本方法只实现「长度由
    /// <paramref name="simSecondsPerDay"/> 注入」的<b>结构</b>：
    /// 日界 = 模拟时间 0、<c>L</c>、<c>2L</c>…；日序号从 1 起。</para>
    /// </summary>
    /// <param name="simTime">模拟时间（秒），必须非负。</param>
    /// <param name="simSecondsPerDay">一个模拟日的模拟秒数，必须为正。</param>
    /// <returns>日序号（从 1 开始）与日内进度（<c>[0, 1)</c>）。</returns>
    public static (long DaySeq, double DayProgress) GetDayPosition(
        double simTime, double simSecondsPerDay)
    {
        if (simTime < 0.0 || double.IsNaN(simTime))
        {
            throw new ArgumentOutOfRangeException(
                nameof(simTime), simTime, "模拟时间不能为负（不变量 I13）。");
        }

        if (!(simSecondsPerDay > 0.0))
        {
            throw new ArgumentOutOfRangeException(
                nameof(simSecondsPerDay), simSecondsPerDay, "一个模拟日的模拟秒数必须为正。");
        }

        // elapsed / L 落在 [0, N)：N 为已过去的整日数，尾数就是日内进度。
        double elapsedDays = simTime / simSecondsPerDay;
        long wholeDays = (long)Math.Floor(elapsedDays);

        // 浮点除法在整日界上可能给出 0.9999999999 这种尾巴。
        // 它是合法进度，不越界；但若修正到 1.0，必须同时进位一天，
        // 否则进度会短暂等于 1（越过半开区间的上界）。
        double progress = elapsedDays - wholeDays;
        if (progress >= 1.0)
        {
            wholeDays += 1;
            progress = 0.0;
        }

        return (wholeDays + 1, progress);
    }

    /// <summary>
    /// 反算：由「日序号 + 日内进度」得到模拟时间。
    /// <para>
    /// 与 <see cref="GetDayPosition"/> 互逆。存在的意义就是让
    /// 「日序号与日内进度互为函数」这条约束<b>可被机械断言</b>：
    /// 正算再反算必须回到原处，两条路径不一致时测试立刻变红。
    /// </para>
    /// </summary>
    /// <param name="daySeq">日序号，从 1 开始。</param>
    /// <param name="dayProgress">日内进度，须落在 <c>[0, 1)</c>。</param>
    /// <param name="simSecondsPerDay">一个模拟日的模拟秒数，必须为正。</param>
    /// <returns>对应的模拟时间（秒）。</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="daySeq"/> 小于 1，或 <paramref name="dayProgress"/>
    /// 不在 <c>[0, 1)</c> 内。
    /// </exception>
    public static double GetSimTime(long daySeq, double dayProgress, double simSecondsPerDay)
    {
        if (daySeq < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(daySeq), daySeq, "日序号从 1 开始。");
        }

        if (double.IsNaN(dayProgress) || dayProgress < 0.0 || dayProgress >= 1.0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(dayProgress), dayProgress,
                "日内进度必须落在半开区间 [0, 1)。");
        }

        if (!(simSecondsPerDay > 0.0))
        {
            throw new ArgumentOutOfRangeException(
                nameof(simSecondsPerDay), simSecondsPerDay, "一个模拟日的模拟秒数必须为正。");
        }

        return (daySeq - 1 + dayProgress) * simSecondsPerDay;
    }
}
