using System;
using System.Collections.Generic;
using System.Linq;

namespace ClaudeCodeGameStudios.Domain.WorldEvents;

/// <summary>
/// 大事件的<b>生命周期阶段</b>（基线 §11.2）。
/// </summary>
/// <remarks>
/// <para><b>七个阶段，顺序固定</b>：
/// 待触发 → 预兆 → 准备 → 进行中 → 结算 → 后续影响 → 结束。</para>
/// <para>⚠️ <b>不是每个事件都要走完七个阶段</b>（基线 §11.2：「某些事件可跳过不适用阶段」）
/// ⇒ <b>哪些阶段适用由模板声明</b>（<see cref="WorldEventTemplate.Phases"/>），
/// 框架只认模板说的那些 ⇒ 框架里**不写死**任何阶段的业务含义。</para>
/// </remarks>
public enum WorldEventPhase
{
    /// <summary>待触发（还没到）。</summary>
    Pending = 0,

    /// <summary>预兆 —— 玩家**能读到**「要来了」（基线 §11.6 V3）。</summary>
    Omen = 1,

    /// <summary>准备（集结、备货……）。</summary>
    Preparation = 2,

    /// <summary>进行中。</summary>
    Ongoing = 3,

    /// <summary>结算（算出结果）。</summary>
    Settlement = 4,

    /// <summary>后续影响（结果落到村庄/英雄身上的那一段）。</summary>
    Aftermath = 5,

    /// <summary>结束（终结态，不可再推进）。</summary>
    Finished = 6,
}

/// <summary>大事件的<b>模板</b>（基线 §11.3）—— 「这一类事件是什么样的」。</summary>
/// <remarks>
/// <para><b>模板与实例必须分离</b>（基线 §11.3）：模板是<b>规则</b>，
/// 实例是<b>这一次</b>。⇒ 新增一类事件只需<b>加一个模板</b>，
/// <b>不改核心世界循环</b>（基线 §11.1 的硬要求）。</para>
/// <para>⚠️ <b>模板不含任何具体数值</b>：它只声明「适用哪些阶段」「怎么结算」
/// （委托），数值由实例/参数带进来。</para>
/// </remarks>
public sealed class WorldEventTemplate
{
    /// <summary>模板标识（稳定，存档里用它指回模板）。</summary>
    public required string Id { get; init; }

    /// <summary>显示名。</summary>
    public required string Name { get; init; }

    /// <summary>
    /// 这个模板<b>适用</b>的阶段（按顺序）。
    /// </summary>
    /// <remarks>
    /// ⚠️ <b>基线 §11.2 的「可跳过不适用阶段」就落在这里</b>：
    /// 框架只按这个列表推进 ⇒ 不同类型的事件可以有**不同的阶段形状**
    /// ⇒ 这正是「框架是通用的、不是某个事件专属的」的可验证形态。
    /// </remarks>
    public required IReadOnlyList<WorldEventPhase> Phases { get; init; }

    /// <summary>
    /// 触发周期（<b>游戏内天数</b>）。
    /// </summary>
    /// <remarks>
    /// ⚠️ 基线 §11.1：「按**已定义的游戏内周期或触发规则**发生；
    /// <b>是否与现实时间绑定尚未确认</b>」⇒ 这里用**游戏内天数**，与现有时钟天然契合。
    /// </remarks>
    public int PeriodDays { get; init; }

    /// <summary>
    /// 结算委托：算出这次事件的结果。
    /// </summary>
    /// <param name="instance">本次实例（已推进到结算阶段）。</param>
    /// <param name="participants">参与者（英雄标识）。</param>
    /// <returns>结算结果（可为空 ⇒ 这次事件没有后果）。</returns>
    public Func<WorldEventInstance, IReadOnlyList<string>, WorldEventOutcome>? Settle { get; init; }
}

/// <summary>一次大事件的<b>实例</b>（基线 §11.3）—— 「这一次是什么样的」。</summary>
public sealed class WorldEventInstance
{
    /// <summary>实例标识（稳定，存档里用它做幂等键）。</summary>
    public required string Id { get; init; }

    /// <summary>用的是哪个模板。</summary>
    public required string TemplateId { get; init; }

    /// <summary>当前阶段（框架按模板的阶段列表推进）。</summary>
    public WorldEventPhase Phase { get; internal set; } = WorldEventPhase.Pending;

    /// <summary>开始时间（模拟秒）。</summary>
    public double StartedAt { get; internal set; }

    /// <summary>进度（0..1，框架推进用；具体含义由模板定）。</summary>
    public double Progress { get; internal set; }

    /// <summary>参与者（英雄标识）。</summary>
    public IReadOnlyList<string> Participants { get; internal set; } = Array.Empty<string>();

    /// <summary>结算结果（结算阶段算出来后落在这里）。</summary>
    public WorldEventOutcome? Outcome { get; internal set; }

    /// <summary>
    /// <b>已应用的后果</b>（幂等依据，基线 §11.4）。
    /// </summary>
    /// <remarks>
    /// ⚠️ **必须随存档走**：事件结算后存档、读档，再结算一次 ⇒
    /// 后果**不得**被应用第二次（基线 §11.6 V2）。
    /// ⇒ 靠的就是这个集合（与 <c>Settlement.AppliedKeys</c> 同一套路）。
    /// <para>⚠️ <b>只读视图</b>（不变量 IT-27：领域层不得把可变集合暴露成公开属性）
    /// ⇒ 外部只能读，写入走 <see cref="MarkApplied"/>。</para>
    /// </remarks>
    public IReadOnlyCollection<string> AppliedConsequences => _appliedConsequences;

    private readonly HashSet<string> _appliedConsequences = new(StringComparer.Ordinal);

    /// <summary>
    /// <b>实例状态</b>（字符串键 → 字符串值）—— 这一次的「可变部分」（D-081）。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么需要它</b>：模板给的是<b>规则</b>，而有些规则需要「这一次还剩多少」
    /// 才能算：商队带来 30 份货、卖掉 7 份 ⇒ 剩 23 份 ⇒ 这个「23」<b>必须随存档走</b>
    /// （否则读档后又是 30 份 ⇒ 玩家能靠读档无限进货 ⇒ <b>限量就白限量了</b>）。</para>
    /// <para>⚠️ <b>只读视图</b>（不变量 IT-27：领域层不得把可变集合暴露成公开属性）⇒
    /// 外部只能读，写入走 <see cref="SetState"/>（唯一写入口）。</para>
    /// </remarks>
    public IReadOnlyDictionary<string, string> State => _state;

    private readonly Dictionary<string, string> _state = new(StringComparer.Ordinal);

    /// <summary>
    /// 写入一个实例状态（<b>唯一的写入口</b>）。
    /// </summary>
    /// <param name="key">状态键（不能为空）。</param>
    /// <param name="value">状态值（不能为空）。</param>
    /// <exception cref="ArgumentException">键或值为空。</exception>
    public void SetState(string key, string value)
    {
        if (string.IsNullOrEmpty(key))
        {
            throw new ArgumentException("实例状态键不能为空。", nameof(key));
        }

        if (string.IsNullOrEmpty(value))
        {
            throw new ArgumentException("实例状态值不能为空。", nameof(value));
        }

        _state[key] = value;
    }

    /// <summary>
    /// 记下一个后果<b>已被应用</b>。
    /// </summary>
    /// <param name="key">后果键。</param>
    /// <returns><b>true</b> = 这是第一次应用（调用方**应当**去应用它）；
    /// <b>false</b> = 之前已经应用过（调用方**不得**再应用）。</returns>
    /// <remarks>
    /// ⚠️ **幂等的关键就在这个返回值**：「已应用过 ⇒ 不再应用」。
    /// </remarks>
    public bool MarkApplied(string key)
    {
        if (string.IsNullOrEmpty(key))
        {
            throw new ArgumentException("后果键不能为空。", nameof(key));
        }

        return _appliedConsequences.Add(key);
    }
}

/// <summary>
/// 一条后果的<b>结构化</b>形态（D-080）。
/// </summary>
/// <remarks>
/// <para>⚠️ <b>为什么不能只留一句中文</b>：后果要**真的落到村庄的资源上**
/// （兽潮打赢发材料与金币、打输扣材料与金币）⇒ 落地的那一层必须能读出
/// <b>种类</b>与<b>数量</b>。若只给「材料 +120」这样的一句话，
/// 落地层就只能<b>解析中文</b> ⇒ 改个措辞就悄悄失效，而且没有任何守卫能发现。</para>
/// <para>⇒ 后果 = <b>结构化的种类与数量</b> + <b>给人看的说明</b>。
/// 说明只负责显示，<b>不参与</b>任何判定。</para>
/// <para>⚠️ <b>它不越权</b>（基线 §11.4）：它只<b>描述</b>要发生什么，
/// 真正写状态的是应用层（各有各的系统入口）。</para>
/// </remarks>
/// <param name="Kind">
/// 后果种类（机器读）：<c>material</c>（村庄材料）/ <c>gold</c>（村金库）/
/// <c>none</c>（只说明、没有可落地的资源变化）。
/// </param>
/// <param name="Amount">
/// 数量：<b>正 = 给</b>，<b>负 = 扣</b>。单位与种类对应（材料 = 份，金币 = 单位）。
/// </param>
/// <param name="Description">给人看的说明（只用于显示）。</param>
public sealed record WorldEventEffect(
    string Kind,
    long Amount,
    string Description)
{
    /// <summary>村庄材料。</summary>
    public const string KindMaterial = "material";

    /// <summary>村金库（金币）。</summary>
    public const string KindGold = "gold";

    /// <summary>没有可落地的资源变化（只有说明）。</summary>
    public const string KindNone = "none";

    /// <summary>这是一个「只说明」的后果。</summary>
    public static WorldEventEffect None(string description) => new(KindNone, 0L, description);
}

/// <summary>一次事件结算的结果。</summary>
/// <remarks>
/// <para>⚠️ <b>它只「描述」后果，不直接改状态</b>（基线 §11.4 的系统边界：
/// 事件系统负责触发/生命周期/协调，<b>不直接承担所有业务细节</b>）。
/// ⇒ 后果由各系统自己应用（战斗 / 建筑 / 经济 / 关系）。</para>
/// </remarks>
/// <param name="Succeeded">这次事件算成成功还是失败。</param>
/// <param name="Consequences">
/// 后果：键 → <see cref="WorldEventEffect"/>。<b>键</b>用于幂等（同一后果只应用一次）；
/// <b>值</b>是结构化的（种类 + 数量 + 说明）⇒ 落地层不解析中文。
/// </param>
public sealed record WorldEventOutcome(
    bool Succeeded,
    IReadOnlyDictionary<string, WorldEventEffect> Consequences);
