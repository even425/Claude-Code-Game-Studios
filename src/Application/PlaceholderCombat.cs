using System;
using System.Collections.Generic;
using System.Linq;
using ClaudeCodeGameStudios.Domain.Combat;
using ClaudeCodeGameStudios.Domain.Heroes;

namespace ClaudeCodeGameStudios.Application;

/// <summary>
/// 【待定 T-004】战斗的<b>占位实现</b> —— 一个 <see cref="IBattleResolver"/>
/// 加一个 <see cref="IBattleOutcomeReader"/>。
///
/// <remarks>
/// <para><b>本类不实现任何战斗规则。</b>没有伤害、没有命中、没有冷却、
/// 没有状态效果、没有 AI 选目标。全部属【待定】（<c>07</c> 的
/// <b>T-004</b>、基线 §16 #13）。规则拍板后<b>整类替换</b>即可，
/// <see cref="DungeonFlow"/> 的流程与调度顺序一行都不用改。</para>
///
/// <para><b>唯一的数是 <see cref="PlaceholderHeroWinRate"/>：</b>
/// </para>
/// <list type="bullet">
/// <item><b>0.65</b>（D-052 确认的实施参数）。</item>
/// <item>它<b>仍然只是占位</b> —— 战斗公式尚未存在（T-004），
/// 它不代表最终的战斗平衡，只决定「副本要连赢几场才拿得到东西」这个经济节流阀。</item>
/// <item>它<b>集中在一个具名常量</b>上，不是散在代码里的字面量
/// （<c>07</c> §2.1 第 2 条纪律）；改它必须是<b>一次有记录的改动</b>。</item>
/// </list>
///
/// <para><b>两个接缝必须自洽，所以判定只写一次。</b>
/// <see cref="Resolve"/> 把胜负写进日志的决定性条目里，
/// <see cref="DidHeroesWin"/> 再从<b>同一条</b>里读回来。
/// 若让两者各自掷一次随机数或各算一次公式，它们就可能互相矛盾 ——
/// 那会产生一条「日志说赢、结论说输」的战斗结果，
/// 而 <see cref="BattleResult"/> 不会察觉（它只校验可复核性，不校验真假）。</para>
///
/// <para><b>读不出胜负时大声失败</b>，不回落成「算赢吧」
/// （<c>07</c> §2.1 第 4 条纪律：可以响亮地失败，不要被兜底盖住）。</para>
///
/// <para><b>决定性条目的文字里明写「占位」</b>，这样即使它出现在
/// 任何面向玩家的文本里，也不会被当成一次真实的战斗判定。</para>
/// </remarks>
/// </summary>
public sealed class PlaceholderCombat : IBattleResolver, IBattleOutcomeReader
{
    /// <summary>写进日志、表示我方胜的标记。</summary>
    private const string WinnerMarker = "【我方胜】";

    /// <summary>写进日志、表示敌方胜的标记。</summary>
    private const string LoserMarker = "【敌方胜】";

    /// <summary>
    /// 【待定 T-004】占位胜率。<b>0.65</b>（`07` **D-052** 确认的实施参数）。
    /// </summary>
    /// <remarks>
    /// <para><b>它不是设计值</b> —— 战斗公式尚未存在，它只是让
    /// <see cref="DungeonFlow"/> 能跑通、并给经济一个节流阀。</para>
    /// <para>
    /// <b>为什么是 0.65</b>：副本要<b>连赢 3 场</b>（`DungeonFlow.DeclaredStageCount = 3`），
    /// 所以副本成功率 = p³。原先 p=0.5 → 12.5%，实测 1000 模拟天才出 136 份掉落样本 ——
    /// 产出量被压得太低，掉落数值无从谈起。p=0.65 → **0.65³ = 0.274625 ≈ 27.46%**。
    /// </para>
    /// <para>
    /// <b>⚠️ 共享随机流</b>：本类用的是 <c>Main._rng</c>，与日常活动
    /// <b>共用同一条随机流</b>。改这个数会<b>改变后续所有随机调用序列</b>，
    /// 不只是战斗结果 —— 回归说明里必须写清这一点。
    /// </para>
    /// <para>
    /// <b>删除条件</b>：T-004 拍板、真正的 <see cref="IBattleResolver"/> 落地之后，
    /// 本常量连同整类一起删除。
    /// </para>
    /// </remarks>
    public const double PlaceholderHeroWinRate = 0.65;

    private readonly IRandomSource _random;

    /// <summary>构造一个占位战斗模块。</summary>
    /// <param name="random">随机源，非空。测试注入定种实现即可复现整场战斗。</param>
    /// <exception cref="ArgumentNullException"><paramref name="random"/> 为 null。</exception>
    public PlaceholderCombat(IRandomSource random)
    {
        _random = random ?? throw new ArgumentNullException(nameof(random));
    }

    /// <summary>
    /// 解算一次遭遇，产出一条过程记录 + <b>一条</b>决定性记录。
    /// </summary>
    /// <param name="encounterName">遭遇名称。</param>
    /// <param name="party">
    /// 参战队伍的<b>状态快照</b>（谁在场、当时什么情况）。
    /// <para>⚠️ <b>本占位实现不读它</b> —— 战斗公式属【待定】T-004，
    /// 而「不发明数值」是硬约束。但它<b>拿得到</b>了：
    /// T-004 落地时，战斗规则要用的队伍配置<b>已经送到手边</b>，
    /// 不必再改接口（改了接口就要改所有调用方 + 存档的读档路径）。</para>
    /// </param>
    /// <returns>战斗日志；恰有一条 <c>DecidedOutcome == true</c>。</returns>
    public IReadOnlyList<BattleLogEntry> Resolve(
        string encounterName, CombatPartySnapshot party)
    {
        bool won = _random.NextDouble(1.0) < PlaceholderHeroWinRate;

        return new[]
        {
            // 过程记录：不改变判定，因此不会被引用为依据
            new BattleLogEntry(
                $"{encounterName}#e0", $"{encounterName}：双方接战。", false),

            // 决定性记录：胜负结论只靠它。文字里明写「占位」，
            // 以免它出现在任何玩家可见的地方时被当成真实判定。
            new BattleLogEntry(
                $"{encounterName}#e1",
                $"{encounterName}：{(won ? WinnerMarker : LoserMarker)}（占位判定，T-004 未定）",
                true),
        };
    }

    /// <summary>
    /// 从战斗日志里<b>读回</b>胜负。
    /// </summary>
    /// <remarks>
    /// <b>不掷随机数、不重算公式</b> —— 判定只由 <see cref="Resolve"/> 写一次，
    /// 这里只负责把它读出来。两者因此不可能互相矛盾。
    /// </remarks>
    /// <param name="log">本次刚产出的日志。</param>
    /// <returns>我方获胜为 <c>true</c>。</returns>
    /// <exception cref="InvalidOperationException">
    /// 日志里没有<b>恰好一条</b>决定性记录，或那条记录里读不出胜负标记 ——
    /// 说明注入的解算器不是本类，或日志在中途被改过。
    /// </exception>
    public bool DidHeroesWin(IReadOnlyList<BattleLogEntry> log)
    {
        var deciding = (log ?? Array.Empty<BattleLogEntry>())
                       .Where(entry => entry.DecidedOutcome)
                       .ToArray();

        if (deciding.Length != 1)
        {
            throw new InvalidOperationException(
                $"占位战斗模块期望日志里恰好一条决定性记录，实际有 {deciding.Length} 条。"
                + "解算器与结论读者必须成对使用 —— 若分别注入，两者可能互相矛盾。");
        }

        string summary = deciding[0].Summary ?? string.Empty;

        bool sawWin = summary.Contains(WinnerMarker, StringComparison.Ordinal);
        bool sawLoss = summary.Contains(LoserMarker, StringComparison.Ordinal);

        if (sawWin == sawLoss)
        {
            // 两个标记都没有 → 日志不是本类产的；两个都有 → 日志被改坏了。
            // 两种情况都大声失败，不回落成任何默认胜负。
            throw new InvalidOperationException(
                $"无法从决定性条目读出胜负：\"{summary}\"。"
                + "占位战斗模块写不出这种日志 —— 请检查注入的实现是否成对。");
        }

        return sawWin;
    }
}

/// <summary>把 <see cref="Random"/> 适配成 <see cref="IRandomSource"/>。</summary>
/// <remarks>
/// 让占位战斗模块不必知道表现层用什么随机源，也让它可以在测试里被
/// 换成 <see cref="DecisionFlow.SeededRandom"/> 从而复现整场战斗。
/// </remarks>
public sealed class SystemRandomSource : IRandomSource
{
    private readonly Random _random;

    /// <summary>构造一个适配器。</summary>
    /// <param name="random">随机源，非空。</param>
    /// <exception cref="ArgumentNullException"><paramref name="random"/> 为 null。</exception>
    public SystemRandomSource(Random random)
    {
        _random = random ?? throw new ArgumentNullException(nameof(random));
    }

    /// <inheritdoc />
    /// <remarks>
    /// <see cref="Random.NextDouble"/> 返回 <c>[0, 1)</c>，乘以正的上界后仍在
    /// <c>[0, bound)</c> 内 —— 契约要求的半开区间得以保持。
    /// </remarks>
    public double NextDouble(double bound)
    {
        if (!(bound > 0.0) || double.IsNaN(bound) || double.IsInfinity(bound))
        {
            throw new ArgumentOutOfRangeException(nameof(bound), bound, "上界必须为正有限数。");
        }

        return _random.NextDouble() * bound;
    }
}
