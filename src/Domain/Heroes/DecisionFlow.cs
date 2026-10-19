using System;
using System.Collections.Generic;
using System.Linq;

namespace ClaudeCodeGameStudios.Domain.Heroes;

/// <summary>随机源。注入而非全局 —— 这是技术债 A7（全局未播种 <c>Random</c>）的解法方向。</summary>
/// <remarks>
/// 固定种子即可让整条决策链复现（06 计划的「固定种子 + 重复运行」）。
/// </remarks>
public interface IRandomSource
{
    /// <summary>返回 <c>[0, bound)</c> 内的一个整数。</summary>
    /// <param name="bound">上界，必须为正。</param>
    double NextDouble(double bound);
}

/// <summary>
/// 一项活动定义。
/// </summary>
/// <remarks>
/// 【待定】第一阶段的具体活动集合见 <c>03</c> §6；各项的耗时属 <c>07</c> 的 <b>P-04 / T-002</b>。
/// </remarks>
public sealed class ActivityDefinition
{
    /// <summary>活动标识。</summary>
    public ActivityId Id { get; }

    /// <summary>该活动属于哪一类玩法偏好 —— 玩法偏好权重表的键。</summary>
    public PreferenceCategory Category { get; }

    /// <summary>
    /// 该活动读取哪一条人物特质修正。
    /// </summary>
    /// <remarks>
    /// 该键在 <see cref="HeroDefinition.TraitModifiers"/> 中不存在时，修正取 <b>1</b>
    /// （恒等的「无修正」），<b>不是 0</b> —— 0 会把这项活动变成不可能，正是 CR-1 禁止的。
    /// </remarks>
    public string TraitModifierKey { get; }

    /// <summary>
    /// 该行动的耗时（模拟秒）。
    /// </summary>
    /// <remarks>【待定】属 <c>07</c> 的 <b>P-04 / T-002</b>。允许为 0（瞬时行动）。</remarks>
    public double DurationSeconds { get; }

    /// <param name="id">活动标识。</param>
    /// <param name="category">玩法偏好类别。</param>
    /// <param name="traitModifierKey">人物特质修正的键。</param>
    /// <param name="durationSeconds">耗时。【待定】P-04 / T-002。</param>
    public ActivityDefinition(
        ActivityId id, PreferenceCategory category, string traitModifierKey, double durationSeconds)
    {
        if (!double.IsFinite(durationSeconds) || durationSeconds < 0.0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(durationSeconds), durationSeconds,
                "行动耗时必须是非负有限数。【待定】真实取值见 07 / P-04。");
        }

        Id = id;
        Category = category;
        TraitModifierKey = traitModifierKey ?? string.Empty;
        DurationSeconds = durationSeconds;
    }
}

/// <summary>决策一步的结果。</summary>
public enum DecisionOutcome
{
    /// <summary>本步不参与决策（S1 未通过：非空闲，或不在村）。</summary>
    NotEligible = 0,

    /// <summary>候选集为空，本步合法空转，不产生行动也不产生事件（03 §3.5）。</summary>
    NoCandidates = 1,

    /// <summary>已选定并开始一项行动（S6 完成）。</summary>
    Started = 2,
}

/// <summary>决策一步的产出。</summary>
/// <param name="Outcome">结果分类。</param>
/// <param name="Chosen">被选中的活动；非 <see cref="DecisionOutcome.Started"/> 时为 <see cref="ActivityId.None"/>。</param>
/// <param name="Target">
/// 目标英雄（第三阶段 M2：社交需要对象）。非目标活动或非 Started 时为 <see cref="HeroId.None"/>。
/// </param>
public readonly record struct DecisionResult(
    DecisionOutcome Outcome, ActivityId Chosen, HeroId Target = default);

/// <summary>
/// 英雄自主决策流程 —— <c>03</c> §3.1 的 S1–S6。
/// </summary>
/// <remarks>
/// <para>
/// <b>这是本项目最核心的一段设计</b>，因为它是基线 §4.2「性格不是二极管」的机械落点：
/// 人物特质与玩法偏好<b>只在 S3 改权重</b>，在 S2（能否）与 S5（前置校验）中<b>一律不参与</b>。
/// </para>
/// <para>
/// <b>CR-1：</b>权重 <c>W = P · M · B · S</c> 的四个因子全部恒为正有限数，
/// 因此 <c>W &gt; 0</c> 恒成立 —— <b>不存在任何一项活动能被特质或偏好压到 0</b>。
/// 这不是靠事后检查，是靠「构造期校验 + 四因子皆正」的结构本身。
/// </para>
/// <para>
/// <b>唯一写者：</b>本类型属于 <c>Heroes</c> 模块（不变量 I10）。
/// </para>
/// </remarks>
public sealed class DecisionFlow
{
    private readonly IReadOnlyList<ActivityDefinition> _activities;
    private readonly Func<HeroState, ActivityDefinition, bool> _precondition;
    private readonly Func<HeroState, double> _stateWeightFactor;
    private readonly Func<HeroState, ActivityDefinition, IReadOnlyList<HeroId>?>? _targetCandidates;
    private readonly Func<HeroState, HeroId, double>? _targetWeight;

    /// <param name="activities">活动注册表。第一阶段应只含 <c>03</c> §6 已确认的活动。</param>
    /// <param name="random">注入的随机源。固定种子可复现整条决策链。</param>
    /// <param name="precondition">
    /// S5 前置校验：环境能否支持这项活动开始。传 null 表示恒真。
    /// <b>注意</b>：人物特质与玩法偏好<b>不得</b>出现在这个委托里 —— 它们只改权重（S3）。
    /// </param>
    /// <param name="stateWeightFactor">
    /// S3 中「当前状态」那一层的权重修正。传 null 表示恒 1。
    /// 须返回<b>正有限数</b>；返回 0 或负数会被拒绝，因为它会把活动变成不可做。
    /// </param>
    /// <param name="targetCandidates">
    /// <b>第三阶段 M2：目标候选</b>。返回 null 表示「这项活动不需要目标」；
    /// 返回列表表示「需要目标，这些是可选对象」（空列表 = 本步没有合法目标，按 S5 失败剔除）。
    /// 传 null（默认）时全部活动都不需要目标 —— 既有行为完全不变。
    /// </param>
    /// <param name="targetWeight">
    /// 目标的权重（第三阶段 M2/M4）：关系越好越可能被选中。传 null 表示等概率。
    /// 须返回<b>正有限数</b> —— 关系只改概率、不改合法性（P3-D06）。
    /// </param>
    /// <exception cref="ArgumentException">
    /// 活动注册表为空，或活动 ID 重复。
    /// </exception>
    public DecisionFlow(
        IReadOnlyList<ActivityDefinition> activities,
        IRandomSource random,
        Func<HeroState, ActivityDefinition, bool>? precondition = null,
        Func<HeroState, double>? stateWeightFactor = null,
        Func<HeroState, ActivityDefinition, IReadOnlyList<HeroId>?>? targetCandidates = null,
        Func<HeroState, HeroId, double>? targetWeight = null)
    {
        if (activities is null) throw new ArgumentNullException(nameof(activities));
        _random = random ?? throw new ArgumentNullException(nameof(random));

        if (activities.Count == 0)
        {
            throw new ArgumentException("活动注册表为空，候选集将永远为空。", nameof(activities));
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var a in activities)
        {
            if (!seen.Add(a.Id.Value))
            {
                throw new ArgumentException($"活动 ID 重复：{a.Id}", nameof(activities));
            }
        }

        _activities = activities;
        _precondition = precondition ?? ((_, _) => true);
        _stateWeightFactor = stateWeightFactor ?? (_ => 1.0);
        _targetCandidates = targetCandidates;
        _targetWeight = targetWeight;
    }

    private readonly IRandomSource _random;

    /// <summary>活动注册表。只读。</summary>
    public IReadOnlyList<ActivityDefinition> Activities => _activities;

    /// <summary>
    /// 对一名英雄执行一步决策（S1–S6）。
    /// </summary>
    /// <param name="hero">英雄状态。</param>
    /// <param name="stateWeightOverride">
    /// 测试或调参用的状态层权重覆盖。传 null 使用构造时注入的那个。
    /// </param>
    /// <returns>本步的产出。</returns>
    /// <exception cref="ArgumentNullException"><paramref name="hero"/> 为 null。</exception>
    /// <exception cref="InvalidOperationException">
    /// 状态层权重因子返回了非正有限数 —— 那是定义错误，不是运行期状况。
    /// </exception>
    public DecisionResult Step(HeroState hero, Func<HeroState, double>? stateWeightOverride = null)
    {
        if (hero is null) throw new ArgumentNullException(nameof(hero));

        // ── S1 触发 ───────────────────────────────────────────────────────
        if (!hero.IsEligibleForDecision())
        {
            return new DecisionResult(DecisionOutcome.NotEligible, ActivityId.None);
        }

        var factor = stateWeightOverride ?? _stateWeightFactor;
        var stateFactor = factor(hero);
        if (!double.IsFinite(stateFactor) || stateFactor <= 0.0)
        {
            throw new InvalidOperationException(
                $"状态层权重因子对英雄 {hero.Id} 返回了 {stateFactor}。"
                + "它必须是正有限数：返回 0 会把活动变成不可做，违反 CR-1"
                + "（性格不是二极管，基线 §4.2）。");
        }

        var rejectedThisStep = new HashSet<ActivityId>();
        ActivityDefinition? chosen = null;
        HeroId chosenTarget = HeroId.None;

        // S2 + S5 合流成一个循环：剔除 → 权重 → 抽样 → 前置校验 → 失败则剔除重来
        while (chosen is null)
        {
            // ── S2 候选集：唯一的剔除环节，剔除权只给「当前状态」与「已定义的活动」──
            // 人物特质与玩法偏好**不参与**这一步。
            var candidates = _activities
                .Where(a => !rejectedThisStep.Contains(a.Id))
                .ToList();

            if (candidates.Count == 0)
            {
                // 03 §3.5：候选集为空**不回退到伪活动**，该英雄本步不行动，
                // 且不生成事件（合法空转）。
                return new DecisionResult(DecisionOutcome.NoCandidates, ActivityId.None);
            }

            // ── S3 权重合成：W = P · M · B · S ────────────────────────────
            var weights = new List<double>(candidates.Count);
            foreach (var a in candidates)
            {
                weights.Add(ComputeWeight(hero, a, stateFactor));
            }

            // ── S4 抽样 ───────────────────────────────────────────────────
            var picked = WeightedPick(candidates, weights);

            // ── S5 前置校验：失败则本项本步标记不可用，回 S2 ────────────────
            if (!_precondition(hero, picked))
            {
                rejectedThisStep.Add(picked.Id);
                continue;
            }

            // ── S5.5 目标解析（第三阶段 M2）：需要目标的活动必须选到合法对象 ──
            // 目标只影响「和谁做」，不改变活动合法性；没有候选 = 本步做不了，
            // 按 S5 失败处理（03 §3.5：不能退化成伪活动）。
            if (_targetCandidates is not null)
            {
                var targets = _targetCandidates(hero, picked);
                if (targets is not null)
                {
                    var target = WeightedPickTarget(hero, targets);
                    if (target.Value.Length == 0)
                    {
                        rejectedThisStep.Add(picked.Id);
                        continue;
                    }

                    chosenTarget = target;
                }
            }

            chosen = picked;
        }

        // ── S6 占用与开始 ───────────────────────────────────────────────────
        hero.BeginAction(chosen.Id, chosen.DurationSeconds, chosenTarget);
        return new DecisionResult(DecisionOutcome.Started, chosen.Id, chosenTarget);
    }

    /// <summary>
    /// <c>W(a, h, t) = P(h,a) · M(h,a) · B(h,a,t) · S(h,t)</c>（03 §3.6.1）。
    /// </summary>
    /// <remarks>
    /// 四个因子全部恒为正有限数，因此 <c>W &gt; 0</c> 恒成立 ——
    /// <b>CR-1 的结构落点</b>。B（人际关系）第一阶段恒等于 1：位置先留、内容后填。
    /// </remarks>
    private static double ComputeWeight(HeroState hero, ActivityDefinition a, double stateFactor)
    {
        // P：玩法偏好基础权重。未覆盖的类别取 1（恒等），不是 0。
        double p = hero.Definition.PreferenceWeights.TryGetValue(a.Category.Value, out var pref)
            ? pref : 1.0;

        // M：人物特质修正。未覆盖的键取 1（恒等），不是 0。
        double m = !string.IsNullOrEmpty(a.TraitModifierKey)
                   && hero.Definition.TraitModifiers.TryGetValue(a.TraitModifierKey, out var trait)
            ? trait : 1.0;

        // B：人际关系。第一阶段恒等于 1（05 不变量 I8 只存方向性，不建触发规则）。
        const double b = 1.0;

        double w = p * m * b * stateFactor;

        if (!double.IsFinite(w) || w <= 0.0)
        {
            // 走到这里说明有人绕过了构造期校验。宁可炸掉，也不要悄悄让某项活动不可做。
            throw new InvalidOperationException(
                $"活动 {a.Id} 对英雄 {hero.Id} 的权重算成了 {w}（P={p} · M={m} · S={stateFactor}）。"
                + "权重必须恒为正 —— 偏好与特质只能改变权重，不能把某项活动变成不可做"
                + "（CR-1，基线 §4.3）。");
        }

        return w;
    }

    /// <summary>按权重抽样。累积法；固定种子即可复现。</summary>
    private ActivityDefinition WeightedPick(List<ActivityDefinition> candidates, List<double> weights)
    {
        double total = weights.Sum();
        double roll = _random.NextDouble(total);

        double acc = 0.0;
        for (var i = 0; i < candidates.Count; i++)
        {
            acc += weights[i];
            if (roll < acc) return candidates[i];
        }

        // 浮点残差兜底：取最后一项，而不是抛异常。
        return candidates[^1];
    }

    /// <summary>
    /// 社交目标按权重抽样（第三阶段 M2/M4）。
    /// </summary>
    /// <remarks>
    /// <para><b>关系只改概率</b>：<c>_targetWeight</c> 的返回值必须是正有限数，
    /// 因此「关系很差」只会降低被选中的概率，<b>不会</b>让目标变成不可选
    /// （CR-1 / P3-D06）。</para>
    /// <para><b>非法候选是编程错误，不是运行期状况</b>：空 ID、自己、重复目标、
    /// 非正权重一律炸掉 —— 悄悄跳过会让「社交对象」在无人察觉时变成别人。</para>
    /// </remarks>
    private HeroId WeightedPickTarget(HeroState hero, IReadOnlyList<HeroId> targets)
    {
        if (targets.Count == 0) return HeroId.None;

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var weights = new List<double>(targets.Count);
        double total = 0.0;

        foreach (var target in targets)
        {
            if (string.IsNullOrEmpty(target.Value))
            {
                throw new InvalidOperationException(
                    $"英雄 {hero.Id} 的目标候选里出现了空标识 —— 目标解析器提供了非法对象。");
            }

            if (target == hero.Id)
            {
                throw new InvalidOperationException(
                    $"英雄 {hero.Id} 的目标候选里包含自己 —— 目标解析器必须排除自己。");
            }

            if (!seen.Add(target.Value))
            {
                throw new InvalidOperationException(
                    $"英雄 {hero.Id} 的目标候选里「{target.Value}」重复 —— 会让同一个人被抽中两次。");
            }

            double w = _targetWeight is null ? 1.0 : _targetWeight(hero, target);
            if (!double.IsFinite(w) || w <= 0.0)
            {
                throw new InvalidOperationException(
                    $"英雄 {hero.Id} 对目标「{target.Value}」的权重是 {w}。"
                    + "它必须是正有限数：返回 0 会把目标变成不可选，违反 CR-1（P3-D06）。");
            }

            weights.Add(w);
            total += w;
        }

        double roll = _random.NextDouble(total);
        double acc = 0.0;
        for (var i = 0; i < targets.Count; i++)
        {
            acc += weights[i];
            if (roll < acc) return targets[i];
        }

        return targets[^1];
    }

    /// <summary>
    /// 一个可复现、<b>状态可存档</b>的随机源实现（线性同余）。
    /// </summary>
    /// <remarks>
    /// <para><b>它同时是测试夹具与游戏的随机占位实现。</b>
    /// 游戏的随机策略属 <c>07</c> 的 <b>T-007</b>【待定】—— 本类是一个
    /// <b>占位</b>，不是对 T-007 的回答。</para>
    /// <para><b>为什么游戏也用它（而不是 <c>System.Random</c>）</b>：
    /// 存档要求读档重放得到同样的结果，而 <c>System.Random</c>
    /// <b>不暴露内部状态</b>，存档里就没法存它。没有可存档的随机源，
    /// <c>SaveGame.RandomState</c> 只能填一个假字符串 ——
    /// 那会让读档后的战斗结果<b>看起来可复现、实际不可复现</b>。</para>
    /// <para><b>替换成本</b>：T-007 拍板后换实现即可，只要新实现同样提供
    /// <see cref="State"/> 与 <see cref="RestoreState"/>，存档层与战斗层一行都不用改。</para>
    /// </remarks>
    public sealed class SeededRandom : IRandomSource
    {
        private ulong _state;

        /// <param name="seed">种子。相同种子产生相同序列。</param>
        public SeededRandom(ulong seed)
        {
            _state = seed == 0 ? 1UL : seed;
        }

        /// <summary>
        /// 内部状态 —— <b>存档用</b>。读档时用它恢复，读档重放才会得到同样的序列。
        /// </summary>
        /// <remarks>
        /// <b>没有它，存档就只是半个。</b>随机状态一旦丢失，读档后的战斗结果
        /// 就与存档前不可复现，而 <c>DungeonFlow</c> 那句
        /// 「读档后重放得到同样的结果」正是靠它成立的
        /// （战斗结果标识由副本标识与阶段<b>推导</b>，但<b>判定内容</b>来自随机）。
        /// </remarks>
        public ulong State => _state;

        /// <summary>恢复到指定状态（读档用）。</summary>
        /// <param name="state">存档里的状态值。0 会被纠正为 1，与构造时一致。</param>
        public void RestoreState(ulong state) => _state = state == 0 ? 1UL : state;

        /// <inheritdoc />
        public double NextDouble(double bound)
        {
            if (!double.IsFinite(bound) || bound <= 0.0)
            {
                throw new ArgumentOutOfRangeException(nameof(bound), bound, "上界必须为正有限数。");
            }

            // 数值配方常数（PCG 的 multiplier / increment），与游戏数值无关。
            _state = _state * 6364136223846793005UL + 1442695040888963407UL;

            // 取高 31 位 → x ∈ [0, 2^31)。
            // ⚠️ 除数必须是 x 的**量纲上界**，不能写成 ulong.MaxValue。
            //    那样算出来恒约等于 0，抽样永远返回第一项 —— 整个偏好系统静默失效，
            //    而「相同种子产生相同序列」这类测试**照样会通过**。
            //    这个错误由 CR-1 的统计断言抓住，不由可复现性断言抓住。
            var x = _state >> 33;
            return (x / (double)(1UL << 31)) * bound;
        }
    }
}
