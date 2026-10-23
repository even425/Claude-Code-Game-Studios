using System;
using System.Collections.Generic;
using System.Linq;

namespace ClaudeCodeGameStudios.Domain.WorldEvents;

/// <summary>
/// 大事件的<b>唯一写者</b>（基线 §11.4）—— 模板注册表 + 实例簿 + 阶段推进 + 幂等结算。
/// </summary>
/// <remarks>
/// <para><b>它做什么</b>：按周期触发事件模板 ⇒ 推进实例的阶段 ⇒ 在结算阶段算出结果
/// ⇒ <b>把后果应用出去，且保证同一个后果只应用一次</b>。</para>
///
/// <para><b>它不做什么</b>（基线 §11.4 的系统边界）：
/// 它<b>不直接改</b>村庄资源、英雄属性、关系值 —— 那些由对应系统应用。
/// ⇒ 后果的应用走<b>委托</b>，本类只负责「什么时候应用、应用过一次没有」。</para>
///
/// <para><b>为什么框架里不写死「兽潮」</b>（基线 §11.5 的硬要求）：
/// 「用一个实例抽象出的框架，<b>无法区分「通用」与「该实例专属」</b>」
/// ⇒ 本类只认 <see cref="WorldEventTemplate"/>，具体事件全靠注册表注入。</para>
/// </remarks>
public sealed class WorldEventBook
{
    private readonly Dictionary<string, WorldEventTemplate> _templates =
        new(StringComparer.Ordinal);
    private readonly Dictionary<string, WorldEventInstance> _instances =
        new(StringComparer.Ordinal);
    private readonly List<string> _order = new();

    /// <summary>已注册的模板（按注册顺序）。</summary>
    public IReadOnlyList<WorldEventTemplate> Templates =>
        _templates.Values.ToList();

    /// <summary>当前的实例（按创建顺序）。</summary>
    public IReadOnlyList<WorldEventInstance> Instances =>
        _order.Where(_instances.ContainsKey).Select(id => _instances[id]).ToList();

    /// <summary>还没结束的实例。</summary>
    public IReadOnlyList<WorldEventInstance> ActiveInstances =>
        Instances.Where(i => i.Phase != WorldEventPhase.Finished).ToList();

    /// <summary>
    /// 注册一个模板。
    /// </summary>
    /// <param name="template">模板。</param>
    /// <exception cref="ArgumentNullException"><paramref name="template"/> 为 null。</exception>
    /// <exception cref="ArgumentException">
    /// 标识为空、阶段列表为空、周期非正，或<b>同一标识注册了两次</b>。
    /// </exception>
    public void Register(WorldEventTemplate template)
    {
        if (template is null) throw new ArgumentNullException(nameof(template));
        if (string.IsNullOrEmpty(template.Id))
        {
            throw new ArgumentException("模板标识不能为空。", nameof(template));
        }
        if (template.Phases is null || template.Phases.Count == 0)
        {
            throw new ArgumentException($"模板「{template.Id}」至少要有一个适用阶段。", nameof(template));
        }
        if (template.PeriodDays <= 0)
        {
            throw new ArgumentException(
                $"模板「{template.Id}」的触发周期必须为正（游戏内天数）。", nameof(template));
        }
        if (_templates.ContainsKey(template.Id))
        {
            throw new ArgumentException(
                $"模板「{template.Id}」已注册 —— 同一标识只能有一个模板。", nameof(template));
        }

        _templates[template.Id] = template;
    }

    /// <summary>查一个模板。</summary>
    /// <param name="templateId">模板标识。</param>
    /// <returns>找不到时返回 null。</returns>
    public WorldEventTemplate? Template(string templateId)
        => _templates.TryGetValue(templateId, out var t) ? t : null;

    /// <summary>
    /// 按周期<b>触发</b>：到了该模板的周期就创建一个新实例。
    /// </summary>
    /// <param name="simDay">当前的游戏日（0 起）。</param>
    /// <param name="now">当前模拟时间（模拟秒）。</param>
    /// <param name="participants">参与这次事件的英雄标识。</param>
    /// <param name="instanceIdFactory">
    /// 生成实例标识的委托（<b>由调用方给</b> ⇒ 本类不引入随机源
    /// ⇒ 可复现、可离线验算）。
    /// </param>
    /// <param name="shouldTrigger">
    /// <b>触发过滤器</b>（可空 = 全部触发）：返回 false ⇒ 这个模板<b>这次不来</b>。
    /// </param>
    /// <returns>这次新建的实例；没到周期则返回 null。</returns>
    /// <remarks>
    /// ⚠️ <b>「同一个周期不重复触发」</b>：按 <c>simDay % PeriodDays == 0</c> 判定
    /// ⇒ 同一天多次调用只会创建一次（实例簿里有活跃实例时不再建）。
    /// <para>⚠️ <b>过滤器是「这次不来」的正规入口</b>（D-082）：
    /// 兽潮「守不住就不触发」用的就是它 ⇒ 框架不需要知道「守不守得住」这种业务规则
    /// ⇒ 规则留在调用方（应用层），框架只管「该不该来」。
    /// ⚠️ **过滤掉的那一次不算触发失败**：调用方要自己给玩家一条说明
    /// （否则玩家会以为游戏出问题了）。</para>
    /// </remarks>
    public WorldEventInstance? TriggerByPeriod(
        long simDay,
        double now,
        IReadOnlyList<string> participants,
        Func<WorldEventTemplate, long, string> instanceIdFactory,
        Func<WorldEventTemplate, bool>? shouldTrigger = null)
    {
        if (instanceIdFactory is null) throw new ArgumentNullException(nameof(instanceIdFactory));
        participants ??= Array.Empty<string>();

        foreach (var template in _templates.Values)
        {
            if (simDay % template.PeriodDays != 0) continue;

            // 这个模板此刻已有一个活跃实例 ⇒ 不重复触发。
            bool alreadyActive = ActiveInstances
                .Any(i => i.TemplateId == template.Id);
            if (alreadyActive) continue;

            // ⚠️ 调用方说「这次不来」⇒ 不建实例（D-082：守不住就别触发兽潮）。
            if (shouldTrigger is not null && !shouldTrigger(template)) continue;

            var instance = new WorldEventInstance
            {
                Id = instanceIdFactory(template, simDay),
                TemplateId = template.Id,
                StartedAt = now,
                Participants = participants.ToArray(),
            };
            // 起点是模板的第一个适用阶段。
            instance.Phase = template.Phases[0];
            _instances[instance.Id] = instance;
            _order.Add(instance.Id);
            return instance;
        }

        return null;
    }

    /// <summary>
    /// 把一个实例推进到<b>下一个</b>适用阶段。
    /// </summary>
    /// <param name="instanceId">实例标识。</param>
    /// <param name="progress">推进后的进度（0..1）。</param>
    /// <returns>推进后的实例；已结束或找不到时返回 null。</returns>
    /// <exception cref="InvalidOperationException">
    /// 实例的阶段<b>不在模板的适用阶段列表里</b>（状态被改坏了）。
    /// </exception>
    public WorldEventInstance? Advance(string instanceId, double progress)
    {
        if (!_instances.TryGetValue(instanceId, out var instance)) return null;
        var template = Template(instance.TemplateId)
                        ?? throw new InvalidOperationException(
                            $"实例「{instanceId}」的模板「{instance.TemplateId}」没注册 —— 状态被改坏了。");

        if (instance.Phase == WorldEventPhase.Finished) return instance;

        int index = template.Phases.ToList().IndexOf(instance.Phase);
        if (index < 0)
        {
            throw new InvalidOperationException(
                $"实例「{instanceId}」当前阶段「{instance.Phase}」不在模板「{template.Id}」的适用阶段里。");
        }

        instance.Progress = progress;
        if (index + 1 < template.Phases.Count)
        {
            instance.Phase = template.Phases[index + 1];
        }
        else
        {
            instance.Phase = WorldEventPhase.Finished;
        }

        return instance;
    }

    /// <summary>
    /// <b>一天一步</b>地推进一个实例（D-101）。
    /// </summary>
    /// <param name="instanceId">实例标识。</param>
    /// <param name="now">当前模拟时刻（秒）。</param>
    /// <param name="simSecondsPerDay">一个模拟日的模拟秒数。</param>
    /// <returns>
    /// 推进后的实例；<b>本次没有推进</b>时也返回当前实例（阶段没变）
    /// ⇒ 调用方靠「阶段变没变」判断今天是不是推了一步。
    /// </returns>
    /// <remarks>
    /// ⚠️ <b>为什么这条规则住在领域层</b>（D-101 的教训）：
    /// 它原先写在 <c>Main</c>（表现层）里 ⇒ <b>单测碰不到</b>
    /// ⇒ 改坏它时守卫照样绿 ⇒ 而它<b>坏掉时整个大事件系统就废了</b>
    /// （实例卡在预兆、从不变现）。
    /// ⇒ 现在它是<b>一条可离线单测的规则</b>（<c>WorldEventTests</c> 盯着它）。
    ///
    /// <para>⚠️ <b>「预兆」要多待几天</b>（玩家要能读到预告）：
    /// 用<b>「实例创建以来过了几天」</b>判断，<b>不用 <c>Progress</c></b>。
    /// ⇒ 原因：<b><c>Progress</c> 只有 <see cref="Advance"/> 会改</b>
    /// ⇒ 若用 <c>Progress == 0</c> 当「刚创建」的判据，
    /// 而「刚创建 ⇒ 不推进」又阻断了 <see cref="Advance"/> ⇒ <b>死锁</b>
    /// ⇒ 实例永远停在预兆 ⇒ <b>大事件从来不会结算</b>（实测：真实游戏 200 天里 0 次结算）。
    /// ⇒ 这条判据必须建立在<b>不会被推进动作改变</b>的量上（时间）。</para>
    /// </remarks>
    public WorldEventInstance? AdvanceDaily(
        string instanceId, double now, double simSecondsPerDay)
    {
        if (!_instances.TryGetValue(instanceId, out var instance)) return null;
        if (instance.Phase == WorldEventPhase.Finished) return instance;

        // 「预兆」至少待满一天（创建当天不动）⇒ 玩家能读到预告。
        if (instance.Phase == WorldEventPhase.Omen && !OmenIsOver(instance, now, simSecondsPerDay))
        {
            return instance;
        }

        return Advance(instanceId, progress: 0.5);
    }

    /// <summary>
    /// 「预兆」阶段是否已经待够了（<b>创建满一天</b>）。
    /// </summary>
    /// <param name="instance">实例。</param>
    /// <param name="now">当前模拟时刻（秒）。</param>
    /// <param name="simSecondsPerDay">一个模拟日的模拟秒数。</param>
    /// <returns>可以推进了为 true。</returns>
    /// <remarks>
    /// ⚠️ **用时间，不用 <c>Progress</c>**（D-101）：
    /// <c>Progress</c> 只由 <see cref="Advance"/> 改 ⇒ 用它当判据会与推进动作互相锁死。
    /// </remarks>
    public static bool OmenIsOver(
        WorldEventInstance instance, double now, double simSecondsPerDay)
    {
        if (instance is null) throw new ArgumentNullException(nameof(instance));
        if (!(simSecondsPerDay > 0.0))
        {
            throw new ArgumentOutOfRangeException(
                nameof(simSecondsPerDay), simSecondsPerDay, "一个模拟日的模拟秒数必须为正。");
        }

        double daysSinceStart = (now - instance.StartedAt) / simSecondsPerDay;
        return daysSinceStart >= 1.0;
    }

    /// <summary>
    /// 结算一个实例：算结果，并把后果<b>应用</b>出去（每条后果只应用一次）。
    /// </summary>
    /// <param name="instanceId">实例标识。</param>
    /// <param name="applyConsequence">
    /// 应用一条后果的委托（<b>由对应系统提供</b> ⇒ 事件簿不越权改数据，基线 §11.4）。
    /// </param>
    /// <returns>结算结果；没结算成（阶段不对 / 已结算过）返回 null。</returns>
    /// <remarks>
    /// ⚠️ <b>幂等（基线 §11.6 V2）</b>：
    /// 结算后存档、读档、再结算一次 ⇒ 后果**不得**被应用第二次。
    /// ⇒ 靠 <see cref="WorldEventInstance.AppliedConsequences"/>（随存档走）。
    /// </remarks>
    public WorldEventOutcome? Settle(
        string instanceId,
        Action<string, WorldEventEffect> applyConsequence)
    {
        if (applyConsequence is null) throw new ArgumentNullException(nameof(applyConsequence));
        if (!_instances.TryGetValue(instanceId, out var instance)) return null;

        var template = Template(instance.TemplateId)
                        ?? throw new InvalidOperationException(
                            $"实例「{instanceId}」的模板「{instance.TemplateId}」没注册 —— 状态被改坏了。");

        // 结算只在「结算阶段」发生（基线 §11.2 的阶段语义）。
        if (instance.Phase != WorldEventPhase.Settlement) return null;
        if (template.Settle is null) return null;

        var outcome = template.Settle(instance, instance.Participants);
        instance.Outcome = outcome;

        foreach (var (key, effect) in outcome.Consequences)
        {
            // ⚠️ **幂等的关键**：`MarkApplied` 返回 false ⇒ 之前已应用过 ⇒ 不再应用。
            if (!instance.MarkApplied(key)) continue;
            applyConsequence(key, effect);
        }

        return outcome;
    }

    /// <summary>导出实例（存档用，按创建顺序）。</summary>
    public IReadOnlyList<WorldEventInstance> Snapshot() => Instances;

    /// <summary>
    /// 用存档整份替换实例簿（读档用）。
    /// </summary>
    /// <param name="instances">存档里的实例。</param>
    /// <exception cref="ArgumentNullException"><paramref name="instances"/> 为 null。</exception>
    public void Restore(IEnumerable<WorldEventInstance> instances)
    {
        if (instances is null) throw new ArgumentNullException(nameof(instances));

        _instances.Clear();
        _order.Clear();
        foreach (var instance in instances)
        {
            if (instance is null) continue;
            _instances[instance.Id] = instance;
            _order.Add(instance.Id);
        }
    }
}
