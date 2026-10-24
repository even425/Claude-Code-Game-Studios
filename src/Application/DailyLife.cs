using System;
using System.Collections.Generic;
using ClaudeCodeGameStudios.Domain.Events;
using ClaudeCodeGameStudios.Domain.Heroes;

namespace ClaudeCodeGameStudios.Application;

/// <summary>
/// 村庄的日常循环 —— 用 <see cref="DecisionFlow"/> 驱动英雄的自主行为。
/// </summary>
/// <remarks>
/// <para>
/// <b>它取代了什么</b>：旧的 <c>VillageLife.cs</c>（159 行）有八类每日事件，
/// 而它们<b>全部</b>建立在已作废的状态字段上（<c>AbsentDays</c> / <c>SlackDays</c> /
/// <c>RestlessDays</c> / <c>GrudgeWith</c>），故整体删除。见 <c>07</c> 的 <b>D-012</b>。
/// </para>
///
/// <para><b>反装饰规则（本类最重要的约束）</b>：每项活动
/// <b>都必须改变一项真实状态</b>。基线 §7.3 与 <c>07</c> 技术债 B7 都明写
/// 「不记录无意义的随机文本」「事件必须改状态，不能只是写一句话」——
/// 一个什么都不影响的活动只会重建那笔债。</para>
///
/// <para><b>为什么效果是注入的</b>：本类不持有材料或工地进度，
/// 它只声明「这项活动做完后，谁去改什么」。这样等 <c>Main</c> 把材料迁到
/// <c>Domain/Village</c> 时，本类一行都不用改。</para>
///
/// <para><b>【待定】</b>本类的两个数值全部是<b>临时占位</b>：
/// 行动耗时取自 <c>07</c> 的 <b>P-04</b>，取值区间取自 <b>P-06</b>。
/// 两者都尚未拍板，开发者拍板后由本类统一校准（见 <c>07</c> §2.1 使用纪律）。</para>
/// </remarks>
public sealed class DailyLife
{
    /// <summary>一项活动做完之后要改的东西。</summary>
    /// <param name="ActivityId">刚完成的活动。</param>
    /// <param name="Actor">做这件事的英雄标识。</param>
    /// <param name="Target">
    /// 目标英雄（第三阶段 M2：社交需要对象）；非目标活动为 <see cref="HeroId.None"/>。
    /// </param>
    public delegate void ActivityEffect(ActivityId ActivityId, string Actor, HeroId Target);

    /// <summary>
    /// 行动耗时占一日的比例 —— <b>临时占位</b>（<c>07</c> 的 P-04，【待定】）。
    /// </summary>
    /// <remarks>用具名常量而非裸数字：<c>07</c> §2.1 第 1 条纪律要求占位值只出现一次。</remarks>
    public const double PlaceholderActionFractionOfDay = 0.25;

    /// <summary>
    /// 候选活动修正因子的取值下端 —— <b>临时占位</b>（<c>07</c> 的 P-06，【待定】）。
    /// </summary>
    /// <remarks>
    /// **这个下端不得取 0。** CR-1 要求 <c>W &gt; 0</c> 恒成立；
    /// 一旦下端触到 0，「性格不是二极管」就退化成了「性格 = 允许做什么」的开关。
    /// </remarks>
    public const double PlaceholderModifierFloor = 0.10;

    /// <summary>同一活动的第二次取值上端（<c>07</c> 的 P-06，【待定】）。</summary>
    public const double PlaceholderModifierCeiling = 10.0;

    private readonly DecisionFlow _flow;
    private readonly IReadOnlyList<ActivityDefinition> _catalog;
    private readonly Dictionary<ActivityId, ActivityEffect> _effects;
    private readonly EventIdGenerator _ids = new();
    private readonly List<DomainEvent> _emitted = new();

    /// <param name="random">可注入的随机源；传 null 用固定种子的占位源（【待定】P-01 之后应注入真实源）。</param>
    /// <param name="effects">活动 → 效果的映射。<b>每一项都必须有效果</b>，否则是装饰。</param>
    /// <param name="activityDurationSeconds">每项活动的耗时（模拟秒）。【待定】P-04。</param>
    /// <param name="precondition">
    /// 可选前置校验（交给 <see cref="DecisionFlow"/> 的 S5）。传 null 表示恒真。
    /// </param>
    /// <param name="targetCandidates">
    /// 可选目标候选（第三阶段 M2）：null = 该活动不需要目标；
    /// 返回列表 = 需要目标；空列表 = 本步没有合法目标（按 S5 失败处理）。
    /// </param>
    /// <param name="targetWeight">可选目标权重（关系越好越可能被选中）。</param>
    /// <param name="activities">
    /// 可选：<b>只用这几项活动</b>（传 null = 用默认活动集）。
    /// ⚠️ **为什么要有它**：活动集里加了「狩猎」（D-118）之后，
    /// 「测社交」的用例就**不再确定**了（英雄可能去狩猎）⇒
    /// ⇒ 用例要能**只留它关心的那项活动** ⇒ 才有确定性的断言。
    /// </param>
    /// <exception cref="ArgumentException">存在没有对应效果的活动。</exception>
    public DailyLife(
        ClaudeCodeGameStudios.Domain.Heroes.IRandomSource? random,
        IReadOnlyDictionary<ActivityId, ActivityEffect> effects,
        double activityDurationSeconds,
        Func<HeroState, ActivityDefinition, bool>? precondition = null,
        Func<HeroState, ActivityDefinition, IReadOnlyList<HeroId>?>? targetCandidates = null,
        Func<HeroState, HeroId, double>? targetWeight = null,
        IReadOnlyList<ActivityDefinition>? activities = null)
    {
        _effects = new Dictionary<ActivityId, ActivityEffect>(effects);
        foreach (var (activityId, _) in _effects)
        {
            if (_effects[activityId] is null)
            {
                throw new ArgumentException(
                    $"活动 {activityId} 没有对应的效果。基线 §7.3：不改变任何状态的行为"
                    + "不得产生事件、也不得写入杂谈 —— 那正是被删掉的装饰债（B7）。",
                    nameof(effects));
            }
        }

        // 活动集里的每一项都必须有效果 —— 包括新增的「社交」。
        // 漏注册会在构造期炸掉，而不是等到第一次完成时才无声跑过。
        _catalog = activities ?? BuildCatalog(activityDurationSeconds);
        var known = new HashSet<string>(StringComparer.Ordinal);
        foreach (var activity in _catalog) known.Add(activity.Id.Value);

        foreach (var (activityId, _) in _effects)
        {
            if (!known.Contains(activityId.Value))
            {
                throw new ArgumentException(
                    $"效果表里的活动 {activityId} 不在活动集里 —— 这份效果永远不会被执行。",
                    nameof(effects));
            }
        }

        foreach (var activity in _catalog)
        {
            if (!_effects.ContainsKey(activity.Id))
            {
                throw new ArgumentException(
                    $"活动 {activity.Id} 没有对应的效果。基线 §7.3：不改变任何状态的行为"
                    + "不得产生事件、也不得写入杂谈 —— 那正是被删掉的装饰债（B7）。",
                    nameof(effects));
            }
        }

        _flow = new DecisionFlow(_catalog,
            random ?? new DecisionFlow.SeededRandom(1),
            precondition, targetCandidates: targetCandidates, targetWeight: targetWeight);
    }

    /// <summary>本循环用到的活动集。</summary>
    public IReadOnlyList<ActivityDefinition> Activities => _flow.Activities;

    /// <summary>
    /// 第一阶段的活动集。<b>每一项都改变真实状态</b>。
    /// </summary>
    /// <remarks>
    /// 【建议】集合。摆摊（基线 §5 的核心玩法）<b>尚未纳入</b>——
    /// 它的定价与摊位费属 <c>07</c> 的 <b>T-006</b>，未拍板前纳入就是凭空发明经济。
    /// <para><b>第三阶段 M2</b> 纳入「社交」：它是<b>目标型活动</b>（需要另一个英雄），
    /// 目标由 <c>DecisionFlow</c> 的可选 target 委托解析；偏好类别「社交」的权重
    /// 由英雄定义提供（P3-D05），未覆盖时取 1（恒等，不把社交变成不可做）。</para>
    /// </remarks>
    private static List<ActivityDefinition> BuildCatalog(double durationSeconds)
    {
        const string collect = "采集";
        const string build = "建屋";
        const string social = "社交";
        const string hunt = "狩猎";

        return new List<ActivityDefinition>
        {
            // 分类 → 玩法偏好权重表的键；特质键 → 人物特质修正表的键
            new(new ActivityId(collect), new PreferenceCategory("劳作"), "勤快", durationSeconds),
            new(new ActivityId(build), new PreferenceCategory("营造"), "勤快", durationSeconds),
            // 社交读「社交」偏好类别（P3-D05）；特质键留空 = 恒等修正。
            new(new ActivityId(social), new PreferenceCategory("社交"), "", durationSeconds),
            // ⚠️ **D-118：狩猎**（出村打野兽）——
            //   分类 = 「狩猎」：**没被任何英雄显式偏好覆盖 ⇒ 权重取默认 1**
            //   ⇒ 它和「社交」一样是**人人都会考虑**的一项（不是某个性格的专利）。
            //   时长 = 采集的 **2 倍**（要出村 + 打一场 ⇒ 不能和采一把时间当成同一件事）。
            new(new ActivityId(hunt), new PreferenceCategory("狩猎"), "", durationSeconds * 2),
        };
    }

    /// <summary>
    /// 推进一次模拟步：让空闲的英雄做决定，并完成到期��行动。
    /// </summary>
    /// <param name="states">全部英雄状态。</param>
    /// <param name="simDeltaSeconds">本次推进的模拟秒。</param>
    /// <returns>本次新产生的事件。</returns>
    public IReadOnlyList<DomainEvent> Step(IReadOnlyList<HeroState> states, double simDeltaSeconds)
    {
        _emitted.Clear();

        foreach (var state in states)
        {
            // 1. 先把进行中的行动往前推；到期就结算并产生事件
            var finished = state.AdvanceAction(simDeltaSeconds);
            if (finished.Value.Length > 0)
            {
                CompleteAction(state, finished);
                continue;
            }

            // 2. 空闲且在场的才做决定（03 §3.1 S1）
            if (!state.IsEligibleForDecision()) continue;

            var result = _flow.Step(state);
            if (result.Outcome != DecisionOutcome.Started) continue;   // 合法空转，不产生事件

            _emitted.Add(MakeEvent(
                // §7.2（内部）—— 动作的开始与结束是内部信号，不进杂谈
                new EventTypeId("HeroActionStarted"), state.Id.Value, "Hero.CurrentActivity",
                state.Definition.Name, result.Chosen.Value, result.Target));
        }

        return _emitted;
    }

    /// <summary>完成一项行动：施加效果并产生「做完了」事件。</summary>
    private void CompleteAction(HeroState state, ActivityId activity)
    {
        var actor = state.Id.Value;
        var target = state.CurrentTarget;

        if (_effects.TryGetValue(activity, out var effect))
        {
            effect(activity, actor, target);
            _emitted.Add(MakeEvent(
                // §7.2（内部）
                new EventTypeId("HeroActionCompleted"), actor, "Hero.CurrentActivity",
                state.Definition.Name, activity.Value, target));
            state.ClearTarget();
        }
        else
        {
            // 到这里说明调用方漏注册了效果 —— 宁可炸掉，也不要让活动无声完成。
            throw new InvalidOperationException(
                $"活动 {activity} 没有注册效果，英雄 {actor} 做完了却什么都没发生。"
                + "这会让杂谈记录一件不存在的事（基线 §7.2）。");
        }
    }

    /// <summary>造一个领域事件。<b>创建后不可变</b>（基线 §13.6）。</summary>
    /// <param name="type">事件类型。</param>
    /// <param name="actor">参与者标识。</param>
    /// <param name="changedState">这件事改掉了哪个状态键（反装饰规则的依据）。</param>
    /// <param name="speaker">说话人（杂谈显示用）。</param>
    /// <param name="activity">活动标识；进 payload，让杂谈能说清「做完了什么」。</param>
    /// <param name="target">目标英雄（第三阶段 M2）；非空时进 payload。</param>
    private DomainEvent MakeEvent(
        EventTypeId type, string actor, string changedState, string speaker, string activity,
        HeroId target = default)
    {
        var payload = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["speaker"] = speaker,
            ["activity"] = activity,
        };
        if (!string.IsNullOrEmpty(target.Value)) payload["target"] = target.Value;

        return DomainEvent.Create(_ids.Next(type, actor), type, actor, 0.0,
            new[] { new StateKey(changedState) }, payload);
    }

    /// <summary>构造默认效果表：采集加材料、建屋推工地、社交交给调用方。</summary>
    /// <param name="addMaterials">
    /// 加材料。<b>参数 = (采集者的英雄标识, 加的份数)</b>。
    /// ⚠️ **为什么带英雄标识**（D-086）：材料要进<b>采集者自己的背包</b>（⇒ 才能卖 ⇒ 才是收入），
    /// 而「是谁采的」只有英雄标识说得准（名字可能重复）。
    /// </param>
    /// <param name="advanceBuild">推进工地进度。参数为推进的比例。</param>
    /// <param name="onSocial">社交完成时的处理（写关系 + 发事件）；参数为发起者与目标。</param>
    /// <param name="onHunt">狩猎完成时的处理（打一场 + 发战利品，D-118）；参数为猎人的英雄标识。</param>
    /// <returns>活动标识 → 效果。</returns>
    /// <remarks>
    /// 【待定】两者的<b>数量</b>属 <c>07</c> 的 <b>P-08</b>（产出）与 T-002（耗时）。
    /// 本方法只声明<b>做什么</b>，不决定<b>多少</b>——份数由调用方决定。
    /// <para><b>社交是必填的</b>：活动集里已有「社交」，漏给效果会在构造期被拒绝 ——
    /// 让它编译期就暴露，而不是等到某个英雄第一次社交时炸掉。</para>
    /// <para>⚠️ <b>狩猎（D-118）也是必填的</b>：活动集里已有「狩猎」，
    /// 漏给效果 ⇒ 英雄打完猎**什么也没发生**（连战利品都没有）⇒
    /// 「出村狩猎」看起来像在演哑剧。</para>
    /// </remarks>
    public static Dictionary<ActivityId, ActivityEffect> DefaultEffects(
        Action<string, int> addMaterials, Action<double> advanceBuild,
        Action<string, HeroId> onSocial,
        Action<string> onHunt)
    {
        if (onSocial is null) throw new ArgumentNullException(nameof(onSocial));
        if (onHunt is null) throw new ArgumentNullException(nameof(onHunt));

        return new Dictionary<ActivityId, ActivityEffect>
        {
            [new ActivityId("采集")] = (_, actor, _) => addMaterials(actor, 1),
            [new ActivityId("建屋")] = (_, _, _) => advanceBuild(PlaceholderBuildProgressPerAction),
            [new ActivityId("社交")] = (_, actor, target) => onSocial(actor, target),
            [new ActivityId("狩猎")] = (_, actor, _) => onHunt(actor),
        };
    }

    /// <summary>一次建屋推进多少工地进度 —— <b>临时占位</b>（<c>07</c> 的 P-08，【待定】）。</summary>
    public const double PlaceholderBuildProgressPerAction = 0.25;
}
