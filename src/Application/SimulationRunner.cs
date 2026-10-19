using System;
using System.Collections.Generic;
using ClaudeCodeGameStudios.Domain.Clock;

namespace ClaudeCodeGameStudios.Application;

/// <summary>
/// 一个模拟步内，某个模块要执行一次的上下文（03 §2.3 / §2.4）。
///
/// <para>传的是<b>模拟</b>时间增量，不是引擎帧间隔。规则层拿不到帧，
/// 因此它没有机会做出依赖帧率的行为 —— 帧率无关性（基线 §13.1，
/// 验收项 A-01）在这里就成立了一半，另一半由
/// <see cref="SimulationRunner"/> 保证步长不随帧变化。</para>
/// </summary>
public sealed class SimulationStepContext
{
    /// <summary>本次推进使用的时钟。只读使用，规则层不回写它。</summary>
    public SimClock Clock { get; }

    /// <summary>这一步覆盖的<b>模拟</b>时间长度（秒）。</summary>
    public double SimDeltaSeconds { get; }

    /// <summary>这一步的序号（对应 <see cref="SimClock.StepCount"/> 已推进后的值）。</summary>
    public long StepIndex { get; }

    /// <summary>构造一步的上下文。</summary>
    /// <param name="clock">时钟，非空。</param>
    /// <param name="simDeltaSeconds">这一步的模拟时间长度，必须非负。</param>
    /// <param name="stepIndex">这一步的序号。</param>
    /// <exception cref="ArgumentNullException"><paramref name="clock"/> 为 null。</exception>
    public SimulationStepContext(SimClock clock, double simDeltaSeconds, long stepIndex)
    {
        Clock = clock ?? throw new ArgumentNullException(nameof(clock));
        SimDeltaSeconds = simDeltaSeconds;
        StepIndex = stepIndex;
    }
}

/// <summary>
/// 一个模拟步内的模块执行体（03 §2.4 的步内顺序表里的一行）。
///
/// <para>模块<b>不决定</b>谁先谁后 —— 顺序由
/// <see cref="SimulationRunner"/> 按注入时的排列执行。模块只回答
/// 「轮到我时我做什么」。</para>
/// </summary>
public interface ISimulationStep
{
    /// <summary>这个执行体的名字，仅用于日志与排查，不参与执行顺序。</summary>
    string Name { get; }

    /// <summary>在当前模拟步内执行一次。</summary>
    /// <param name="context">本步上下文。</param>
    void Execute(SimulationStepContext context);
}

/// <summary>
/// 模拟驱动的契约 —— 表现层唯一被允许调用的东西。
///
/// <para>03 §2.2【已确认】：「表现层不得直接调用 <c>SimClock.Advance</c>
/// 或任何领域推进方法。它只能把引擎的 <c>dt</c> 作为<b>输入数据</b>
/// 交给应用层。」本接口就是那条通道：<c>dt</c> 进，模拟推进，
/// 表现层不碰领域。</para>
///
/// <para>技术债 <b>A4</b>（<c>07</c> §4.1）记录的正是这件事被违反的样子：
/// 原 <c>VillageView.cs:71</c> 直接 <c>h.Tick(delta)</c>，
/// 表现层成了模拟的时钟源。本接口是对它的纠正。</para>
/// </summary>
public interface ISimulationDriver
{
    /// <summary>
    /// 喂入一段现实时间，推进模拟。
    /// </summary>
    /// <param name="realDeltaSeconds">现实时间增量（秒），来自引擎帧间隔，必须非负。</param>
    /// <returns>本次实际执行的模拟步数量。</returns>
    int AdvanceFrame(double realDeltaSeconds);
}

/// <summary>
/// 模拟推进器（03 §2.2 推进链的应用层一环）。
///
/// <para><b>它拥有模拟步的推进：决定一个模拟步内各模块的执行顺序。</b>
/// 上游是表现层交来的帧 <c>dt</c>（只是数据），下游是按固定顺序执行的
/// 一串 <see cref="ISimulationStep"/>。这是 D-001 路线 B 迁移的第一步 ——
/// 把控制反转（A4）从表现层搬回应用层。</para>
///
/// <para><b>两种节奏，都由构造参数决定，本类不填任何数值</b>：
/// <list type="bullet">
/// <item><b>固定步长累加器模式</b>（03 §2.3）：现实时间先进累加器，攒够一个
/// 固定步长才推进一步，并受「单步追帧上限」约束。
/// <b>【待定】</b>步长属 <c>07</c> 的 <b>P-01</b>、追帧上限属 <b>T-001</b>，
/// 因此必须显式注入 —— 本类不提供默认步长，提供默认值就等于替开发者填了
/// T-001。</item>
/// <item><b>逐帧模式</b>：每帧推进一次，长度由注入的换算决定。
/// 这是当前原型 <c>WorldClock</c> 的节奏，保留它是为了让这一小步迁移
/// <b>不改变游戏现有的观感</b>；它是过渡态，不是目标形态。</item>
/// </list></para>
///
/// <para><b>依赖注入</b>：时钟与步序都由构造参数给出，
/// 没有静态单例、没有 <c>Instance</c>。测试因此可以在没有 Godot 运行时的
/// 情况下构造它（不变量 I9 · <c>07</c> §4.1 A7 的反面教材）。</para>
/// </summary>
public sealed class SimulationRunner : ISimulationDriver
{
    /// <summary>时钟。由时钟模块（唯一写者）持有，本类只读它、推进它。</summary>
    private readonly SimClock _clock;

    /// <summary>步内执行序。<b>注入时的排列就是执行顺序</b>，本类不排序。</summary>
    private readonly IReadOnlyList<ISimulationStep> _steps;

    /// <summary>
    /// 单步追帧上限 —— <b>临时占位</b>（<c>07</c> 的 <b>P-12 / T-001</b>【待定】）。
    /// </summary>
    /// <remarks>
    /// <para><b>这个上限必须存在</b>（<c>03</c> §2.3【建议】）：现实卡顿（加载、断点、
    /// 窗口失焦）会积累大量待补步；若无上限，一次涌入几十个模拟步会让英雄
    /// 「瞬移式」完成大量行动 —— 那不是省事，是把 bug 藏进画面里。</para>
    /// <para><b>为什么它是必填而不是默认值</b>：原先这个参数默认 <c>0</c>，
    /// 而 <c>0</c> 在下方判断里意味着「<b>关闭守卫</b>」。也就是说，
    /// <b>不写这个参数就等于悄悄去掉了一道防线</b> —— 正好是
    /// <c>07</c> §2.1 第 4 条纪律禁止的那种兜底默认值。</para>
    /// <para>取值本身属【待定】；开发者拍板后由本常量统一替换。</para>
    /// </remarks>
    public const int RequiredCatchUpLimit = 8;

    /// <summary>固定步长模式下的步长（模拟秒）；逐帧模式下为 <c>null</c>。</summary>
    private readonly double? _fixedStepSeconds;

    /// <summary>现实卡顿后一次最多补多少个模拟步（03 §2.3 单步追帧上限）。</summary>
    private readonly int _maxCatchUpSteps;

    /// <summary>现实时间累加器，固定步长模式下使用。</summary>
    private double _accumulator;

    /// <summary>构造一个<b>逐帧</b>节奏的推进器（原型当前的节奏）。
    /// </summary>
    /// <param name="clock">时钟，非空。</param>
    /// <param name="steps">步内执行序，按数组顺序执行。</param>
    /// <exception cref="ArgumentNullException"><paramref name="clock"/> 或 <paramref name="steps"/> 为 null。</exception>
    public SimulationRunner(SimClock clock, IEnumerable<ISimulationStep> steps)
        : this(clock, steps, fixedStepSeconds: null,
               maxCatchUpSteps: RequiredCatchUpLimit)
    {
    }

    /// <summary>构造一个<b>固定步长</b>节奏的推进器。
    /// </summary>
    /// <param name="clock">时钟，非空。</param>
    /// <param name="steps">步内执行序，按数组顺序执行。</param>
    /// <param name="fixedStepSeconds">
    /// 一个模拟步覆盖多少模拟秒。<b>【待定】</b>属 <c>07</c> 的 <b>P-01 / T-001</b>，
    /// 必须由调用方显式给出，本类不提供默认值。必须为正。
    /// </param>
    /// <param name="maxCatchUpSteps">
    /// 一次调用最多补多少个模拟步。<b>【待定】</b>属 <c>07</c> 的 <b>T-001</b>。
    /// 必须非负；0 表示不补（只走累加器里已攒够的步）。
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="clock"/> 或 <paramref name="steps"/> 为 null。</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="fixedStepSeconds"/> 非正数，或 <paramref name="maxCatchUpSteps"/> 为负数。
    /// </exception>
    public SimulationRunner(
        SimClock clock,
        IEnumerable<ISimulationStep> steps,
        double fixedStepSeconds,
        int maxCatchUpSteps)
        : this(clock, steps, (double?)fixedStepSeconds, maxCatchUpSteps)
    {
    }

    /// <summary>唯一的构造路径。<paramref name="fixedStepSeconds"/> 为 null 即逐帧模式。</summary>
    private SimulationRunner(
        SimClock clock,
        IEnumerable<ISimulationStep> steps,
        double? fixedStepSeconds,
        int maxCatchUpSteps)
    {
        if (clock is null)
        {
            throw new ArgumentNullException(nameof(clock));
        }

        if (steps is null)
        {
            throw new ArgumentNullException(nameof(steps));
        }

        if (fixedStepSeconds is not null
            && (!(fixedStepSeconds.Value > 0.0) || double.IsNaN(fixedStepSeconds.Value)
                || double.IsInfinity(fixedStepSeconds.Value)))
        {
            throw new ArgumentOutOfRangeException(
                nameof(fixedStepSeconds),
                fixedStepSeconds,
                "模拟步长必须为正有限数。【待定】取值见 07 / P-01 / T-001，本类不提供默认值。");
        }

        if (maxCatchUpSteps < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxCatchUpSteps), maxCatchUpSteps, "单步追帧上限不能为负。");
        }

        _clock = clock;
        _steps = new List<ISimulationStep>(steps).AsReadOnly();
        _fixedStepSeconds = fixedStepSeconds;
        _maxCatchUpSteps = maxCatchUpSteps;
    }

    /// <summary>本推进器使用的时钟。只读。</summary>
    public SimClock Clock => _clock;

    /// <summary>步内执行序（注入时的排列）。只读。</summary>
    public IReadOnlyList<ISimulationStep> Steps => _steps;

    /// <summary>
    /// 喂入一段现实时间并推进模拟（03 §2.2 推进链的入口）。
    ///
    /// <para>表现层<b>只能</b>调用本方法，传入的是引擎的帧 <c>dt</c>，
    /// 且那只是数据。本方法内部把现实时间换算成模拟时间、
    /// 按固定步长切分模拟步、再按注入顺序执行各模块 ——
    /// 表现层无从得知，也无从干预。</para>
    /// </summary>
    /// <param name="realDeltaSeconds">现实时间增量（秒），必须非负且有限。</param>
    /// <returns>本次实际执行的模拟步数量。</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="realDeltaSeconds"/> 为负数或非有限数。</exception>
    public int AdvanceFrame(double realDeltaSeconds)
    {
        if (realDeltaSeconds < 0.0 || double.IsNaN(realDeltaSeconds)
            || double.IsInfinity(realDeltaSeconds))
        {
            throw new ArgumentOutOfRangeException(
                nameof(realDeltaSeconds), realDeltaSeconds, "现实时间增量必须非负且有限。");
        }

        if (_fixedStepSeconds is null)
        {
            // 逐帧模式：一帧 = 一个模拟步。步长由注入的换算决定（【待定】T-001）。
            double simDelta = _clock.Advance(realDeltaSeconds);
            RunSteps(simDelta);
            return _steps.Count > 0 ? 1 : 0;
        }

        // 固定步长模式（03 §2.3）：现实时间先换算成模拟时间进累加器，
        // 攒够一个固定步长才推进一步。换算走 SimClock 的注入点，
        // 本类不自己算「现实秒 → 模拟秒」，也不发明步长。
        double simDeltaThisFrame = _clock.ConvertToSimSeconds(realDeltaSeconds);
        _accumulator += simDeltaThisFrame;

        double stepLength = _fixedStepSeconds.Value;
        int executed = 0;

        while (_accumulator >= stepLength)
        {
            if (_maxCatchUpSteps > 0 && executed >= _maxCatchUpSteps)
            {
                // 单步追帧上限：现实卡顿后不能一次性涌入几十步，
                // 否则英雄会「瞬移式」完成大量行动（03 §2.3）。
                // 余下的积压直接丢弃，而不是留到下一帧继续涌。
                // 【待定】上限的值属 T-001，这里只保证它存在。
                _accumulator = 0.0;
                break;
            }

            _accumulator -= stepLength;
            _clock.AdvanceSimSeconds(stepLength);
            RunSteps(stepLength);
            executed++;
        }

        return executed;
    }

    /// <summary>按注入顺序执行一遍步内模块。</summary>
    private void RunSteps(double simDeltaSeconds)
    {
        var context = new SimulationStepContext(_clock, simDeltaSeconds, _clock.StepCount);

        foreach (ISimulationStep step in _steps)
        {
            step.Execute(context);
        }
    }
}

/// <summary>
/// 【临时】把现有的 <c>HeroAgent.Tick(dt)</c> 包成一步模拟。
///
/// <para><b>这是一个过渡适配器，不是目标形态。</b>它的存在只有一个理由：
/// 让控制反转（A4）的纠正<b>不需要改动 <c>Main.cs</c></b>（本轮不改它），
/// 同时原型仍然照常运行。真正的英雄行为属于 <c>src/Domain/Heroes/</c>，
/// 那要等 <c>HeroAgent</c> 按 D-001 路线 B 重写之后。</para>
///
/// <para>它收到的是<b>模拟</b>时间增量（见 <see cref="SimulationStepContext"/>），
/// 而不是帧间隔 —— 换算关系由注入的 <c>SimClock</c> 决定。
/// 在当前逐帧模式、换算为恒等、倍率为 1 的情况下，
/// 它与迁移前 <c>VillageView.cs:71</c> 的行为<b>逐值相同</b>。</para>
///
/// <para><b>删除条件</b>：<c>Main.cs</c> 按 A1 拆分、
/// <c>HeroAgent</c> 按 D-001 重写之后，本类应随 <c>HeroAgent.Tick</c> 一起消失。</para>
/// </summary>
public sealed class LegacyHeroSimulationStep : ISimulationStep
{
    /// <summary>被驱动的英雄集合。由表现层在构造时交给应用层引用（引用，不是所有权）。</summary>
    private readonly IReadOnlyList<HeroAgent> _heroes;

    /// <summary>构造这一步。</summary>
    /// <param name="heroes">要推进的英雄列表；持有引用，不接管所有权。</param>
    /// <exception cref="ArgumentNullException"><paramref name="heroes"/> 为 null。</exception>
    public LegacyHeroSimulationStep(IReadOnlyList<HeroAgent> heroes)
    {
        _heroes = heroes ?? throw new ArgumentNullException(nameof(heroes));
    }

    /// <inheritdoc/>
    public string Name => "legacy-hero";

    /// <inheritdoc/>
    public void Execute(SimulationStepContext context)
    {
        float dt = (float)context.SimDeltaSeconds;
        foreach (HeroAgent hero in _heroes)
        {
            hero.Tick(dt);
        }
    }
}

/// <summary>
/// 【临时】构造原型当前使用的默认驱动器。
///
/// <para>存在的唯一理由：让 A4 的纠正不必改 <c>Main.cs</c>（本轮不改它）
/// 就生效。它产出的驱动器在行为上与迁移前 <c>VillageView.cs:71</c> 的
/// <c>h.Tick(delta)</c> <b>逐值相同</b>：换算是恒等、倍率是 1、每帧一步。</para>
///
/// <para><b>关于这里的两个常数</b>：日长与换算比例都是【待定】
/// （<c>07</c> 的 <b>T-001 / P-01 / P-02 / P-03</b>），因此它们
/// <b>不是</b>设计值，而是「让临时适配器能构造出一个时钟」的占位符：
/// <list type="bullet">
/// <item><see cref="PlaceholderSimSecondsPerDay"/> 只影响
///       <c>SimClock.DaySeq</c> / <c>DayProgress</c>，而本适配器
///       <b>没有任何规则读这两个字段</b>（日界是玩法语义，见 03 §2.6）。</item>
/// <item><see cref="IdentitySeconds"/> 让 1 现实秒 = 1 模拟秒，
///       使迁移前后英雄移动速度逐值不变。</item>
/// </list>
/// 两者都在 <c>Main.cs</c> 按 A1 拆分、真实取值由 T-001 拍定时被替换掉。</para>
///
/// <para><b>删除条件</b>：与 <see cref="LegacyHeroSimulationStep"/> 相同 ——
/// <c>HeroAgent</c> 按 D-001 路线 B 重写之后。</para>
/// </summary>
public static class PrototypeSimulationDriverFactory
{
    /// <summary>
    /// 【待定 T-001 / P-01】占位日长。模拟器当前**不消费任何日界语义**，因此此值不影响任何行为。
    /// <para>
    /// **刻意不取 75。** 原型曾有一个 <c>day_length_seconds: 75</c>，
    /// 但它来自 <c>design/registry/entities.yaml</c>，已由 **D-002** 判死（与基线 §4.4 冲突：
    /// 把时区作息与疲劳当成了已定机制）。沿用同一个数字会让作废的值以「占位」之名复活并被误当成定案。
    /// 此处取一个无出处的数，使它一眼可辨为占位符而非遗留值。
    /// </para>
    /// <para>删除条件：<c>Main.cs</c> 按 A1 拆分、真实取值由 T-001 拍定时被替换。</para>
    /// </summary>
    public const double PlaceholderSimSecondsPerDay = 100.0;

    /// <summary>【待定 T-001 / P-02】占位换算：1 现实秒 = 1 模拟秒。</summary>
    public static double IdentitySeconds(double realSeconds) => realSeconds;

    /// <summary>为原型当前的英雄集合造一个驱动器。</summary>
    /// <param name="heroes">要推进的英雄列表；持有引用，不接管所有权。</param>
    /// <exception cref="ArgumentNullException"><paramref name="heroes"/> 为 null。</exception>
    public static ISimulationDriver Create(IReadOnlyList<HeroAgent> heroes)
    {
        if (heroes is null)
        {
            throw new ArgumentNullException(nameof(heroes));
        }

        var clock = new SimClock(PlaceholderSimSecondsPerDay, IdentitySeconds);
        return new SimulationRunner(clock, new ISimulationStep[] { new LegacyHeroSimulationStep(heroes) });
    }
}
