using System;
using System.Collections.Generic;

namespace ClaudeCodeGameStudios.Domain.Heroes;

/// <summary>
/// 英雄的<b>静态定义</b> —— 他「是什么人」「喜欢做什么」。
/// </summary>
/// <remarks>
/// <para>
/// 依据基线 §4.1 的四层建模，本类型只承载其中两层：
/// <b>人物特质</b>与<b>玩法偏好</b>。
/// <b>当前状态</b>在 <see cref="HeroState"/>，<b>人际关系</b>第一阶段不建模
/// （<c>05</c> 不变量 I8：只存方向性，不建触发规则）。
/// </para>
/// <para>
/// <b>不可变</b>。运行中任何变化都不得写回定义 —— 那是 <c>05</c> 的不变量 I7。
/// </para>
/// </remarks>
public sealed class HeroDefinition
{
    /// <summary>稳定标识。跨实体引用一律用 ID，不用对象引用（<c>05</c> §3.1）。</summary>
    public HeroId Id { get; }

    /// <summary>显示名。【已确认】每名英雄独一无二（基线 §2.2）。</summary>
    public string Name { get; }

    /// <summary>来处（哪个世界的召唤物）。用于杂谈展示。【建议】</summary>
    public string Origin { get; }

    /// <summary>
    /// 人物特质的<b>方向性修正</b>：特质 ID → 某类活动的修正系数。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 取值范围 <b>(0, +∞)</b> —— 见 <see cref="DecisionFlow"/> 的 CR-1。
    /// 【待定】具体特质条目与取值为 <c>07</c> 的 P-06 / 基线 §16 #3。
    /// </para>
    /// <para>
    /// <b>方向性</b>：特质只让某些活动更想做、某些更不想做，
    /// <b>永远不能让任何活动变成不可做</b>。可做不可做由
    /// <see cref="HeroState"/> 决定。
    /// </para>
    /// </remarks>
    public IReadOnlyDictionary<string, double> TraitModifiers { get; }

    /// <summary>
    /// 玩法偏好的<b>基础权重</b>：偏好类别 ID → 该类别的权重。
    /// </summary>
    /// <remarks>
    /// 取值范围 <b>(0, +∞)</b>。
    /// 【待定】具体偏好品类与权重表为 <c>07</c> 的 P-06 / 基线 §16 #4。
    /// </remarks>
    public IReadOnlyDictionary<string, double> PreferenceWeights { get; }

    /// <param name="id">稳定标识。</param>
    /// <param name="name">显示名。</param>
    /// <param name="origin">来处。</param>
    /// <param name="traitModifiers">人物特质修正；传 null 表示无修正。</param>
    /// <param name="preferenceWeights">玩法偏好权重；传 null 表示未覆盖的类别用 1。</param>
    /// <exception cref="ArgumentException">
    /// 任一修正或权重不是正有限数 —— 违反 CR-1，构造期即失败，不留到运行期。
    /// </exception>
    public HeroDefinition(
        HeroId id,
        string name,
        string origin,
        IReadOnlyDictionary<string, double>? traitModifiers = null,
        IReadOnlyDictionary<string, double>? preferenceWeights = null)
    {
        Id = id;
        Name = name ?? throw new ArgumentNullException(nameof(name));
        Origin = origin ?? throw new ArgumentNullException(nameof(origin));
        TraitModifiers = Freeze(traitModifiers, nameof(traitModifiers));
        PreferenceWeights = Freeze(preferenceWeights, nameof(preferenceWeights));
    }

    /// <summary>
    /// 校验并拷贝一份只读字典。所有值必须是<b>正有限数</b>。
    /// </summary>
    private static IReadOnlyDictionary<string, double> Freeze(
        IReadOnlyDictionary<string, double>? source, string paramName)
    {
        var copy = new Dictionary<string, double>(StringComparer.Ordinal);
        if (source is null) return copy;

        foreach (var (key, value) in source)
        {
            if (!double.IsFinite(value) || value <= 0.0)
            {
                throw new ArgumentException(
                    $"「{paramName}[{key}]」= {value}。人物特质与玩法偏好的系数必须为正有限数。"
                    + "这不是运行期状况而是定义期错误：偏好与特质只能改变权重，不能把某项活动变成不可做"
                    + "（CR-1，基线 §4.3「偏好只改变权重与倾向，不改变可行性」）。",
                    paramName);
            }

            copy[key] = value;
        }

        return copy;
    }
}

/// <summary>英雄的稳定标识。值类型，便于比较与做字典键。</summary>
/// <param name="Value">标识值。</param>
public readonly record struct HeroId(string Value)
{
    /// <summary>空标识。仅用于「还没有分配」的场景。</summary>
    public static HeroId None => new(string.Empty);

    /// <inheritdoc />
    public override string ToString() => string.IsNullOrEmpty(Value) ? "(无)" : Value;
}

/// <summary>活动标识。</summary>
/// <param name="Value">标识值。</param>
public readonly record struct ActivityId(string Value)
{
    /// <summary>「无活动」—— 英雄空闲时的当前活动。</summary>
    public static ActivityId None => new(string.Empty);

    /// <inheritdoc />
    public override string ToString() => string.IsNullOrEmpty(Value) ? "(无)" : Value;
}

/// <summary>活动偏好类别标识 —— 玩法偏好权重表的键。</summary>
/// <param name="Value">标识值。</param>
public readonly record struct PreferenceCategory(string Value)
{
    /// <inheritdoc />
    public override string ToString() => string.IsNullOrEmpty(Value) ? "(无)" : Value;
}
