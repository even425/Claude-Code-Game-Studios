using System;
using System.Collections.Generic;
using System.Linq;

namespace ClaudeCodeGameStudios.Domain.Events;

/// <summary>领域事件类型标识。可扩展注册表 —— 新增类型不得要求重写决策流程或调度顺序。</summary>
/// <param name="Value">类型标识。</param>
public readonly record struct EventTypeId(string Value)
{
    /// <inheritdoc />
    public override string ToString() => string.IsNullOrEmpty(Value) ? "(无)" : Value;
}

/// <summary>领域事件标识。</summary>
/// <param name="Value">标识值。</param>
public readonly record struct EventId(string Value)
{
    /// <inheritdoc />
    public override string ToString() => string.IsNullOrEmpty(Value) ? "(无)" : Value;
}

/// <summary>已登记的状态键 —— 用来声明「这件事改了什么」。</summary>
/// <param name="Value">状态键，如 <c>Hero.CurrentActivity</c>。</param>
public readonly record struct StateKey(string Value)
{
    /// <inheritdoc />
    public override string ToString() => string.IsNullOrEmpty(Value) ? "(无)" : Value;
}

/// <summary>
/// 一个**已发生**的领域事件。不可变。
/// </summary>
/// <remarks>
/// <para>
/// <b>为什么必须声明 <see cref="ChangedStates"/></b>：这是反装饰规则（<c>03</c> §5.2）的机械落点。
/// 一个既不改变任何运行时状态、也不构成已定义事件类型的行为，<b>不产生事件，也不产生杂谈</b>。
/// 事件若不声明它改了什么，就无法验证「它真的改了什么」，杂谈就会退化成装饰 ——
/// 正是 <c>07</c> 技术债 B7（<c>DayIdle</c> / <c>DayStranger</c>）的成因。
/// </para>
/// <para>
/// <b>构造时若 <see cref="ChangedStates"/> 为空则直接拒绝</b>，
/// 因为「不改变任何状态的事件」按定义就是装饰。
/// </para>
/// </remarks>
public sealed class DomainEvent
{
    private DomainEvent(
        EventId id, EventTypeId type, string actor, double occurredAtSimSeconds,
        StateKey[] changedStates, IReadOnlyDictionary<string, string> payload)
    {
        Id = id;
        Type = type;
        Actor = actor;
        OccurredAtSimSeconds = occurredAtSimSeconds;
        ChangedStates = changedStates;
        Payload = payload;
    }

    /// <summary>唯一标识。杂谈条目靠它溯源（不变量 I5）。</summary>
    public EventId Id { get; }

    /// <summary>事件类型。</summary>
    public EventTypeId Type { get; }

    /// <summary>参与者。空串表示无特定参与者（如世界级事件）。</summary>
    public string Actor { get; }

    /// <summary>发生时的模拟时间（模拟秒）。</summary>
    public double OccurredAtSimSeconds { get; }

    /// <summary>
    /// 这件事<b>确实改变</b>的状态键。<b>永不为空</b> —— 见类型说明的反装饰规则。
    /// </summary>
    public IReadOnlyList<StateKey> ChangedStates { get; }

    /// <summary>供杂谈渲染使用的只读载荷。只读，不参与状态。</summary>
    public IReadOnlyDictionary<string, string> Payload { get; }

    /// <summary>
    /// 构造一个领域事件。
    /// </summary>
    /// <param name="id">唯一标识。</param>
    /// <param name="type">事件类型。</param>
    /// <param name="actor">参与者。</param>
    /// <param name="occurredAtSimSeconds">发生时的模拟时间。</param>
    /// <param name="changedStates">这件事改掉的状态键。<b>不得为空</b>。</param>
    /// <param name="payload">只读载荷。</param>
    /// <exception cref="ArgumentException">
    /// <paramref name="changedStates"/> 为空 —— 那是装饰事件，按反装饰规则必须拒绝。
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">发生时间为负或非有限数。</exception>
    public static DomainEvent Create(
        EventId id,
        EventTypeId type,
        string actor,
        double occurredAtSimSeconds,
        IEnumerable<StateKey> changedStates,
        IReadOnlyDictionary<string, string>? payload = null)
    {
        if (!double.IsFinite(occurredAtSimSeconds) || occurredAtSimSeconds < 0.0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(occurredAtSimSeconds), occurredAtSimSeconds, "发生时间必须是非负有限数。");
        }

        var changed = (changedStates ?? Enumerable.Empty<StateKey>()).Distinct().ToArray();
        if (changed.Length == 0)
        {
            throw new ArgumentException(
                $"事件 {type}（参与者 {actor}）声明的状态变化为空。"
                + "按反装饰规则（03 §5.2 / 基线 §7.3），不改变任何状态的事件不产生事件、也不产生杂谈。"
                + "若它确实是装饰，请连同事件类型一起删掉，而不是记录下来。",
                nameof(changedStates));
        }

        return new DomainEvent(
            id, type, actor ?? string.Empty, occurredAtSimSeconds,
            changed, payload ?? new Dictionary<string, string>(StringComparer.Ordinal));
    }
}

/// <summary>一条杂谈条目。</summary>
public sealed class ChronicleEntry
{
    /// <summary>构造。</summary>
    /// <param name="sourceEventId">
    /// 来源事件标识。<b>必填且不可为空</b>（<c>05</c> §3.8）——
    /// 「来源事件 ID」是杂谈可被验证的唯一依据。
    /// </param>
    /// <param name="text">渲染后的文本。</param>
    /// <param name="speaker">说话人；空串表示无。</param>
    /// <exception cref="ArgumentException">来源事件标识为空。</exception>
    public ChronicleEntry(EventId sourceEventId, string text, string speaker = "")
    {
        if (string.IsNullOrEmpty(sourceEventId.Value))
        {
            throw new ArgumentException(
                "杂谈条目必须有来源事件 ID —— 它是「这条话对应一件已发生的事」的唯一凭证。"
                + "没有来源的条目就是编造（基线 §7.2：杂谈根据已发生的事件生成消息）。",
                nameof(sourceEventId));
        }

        SourceEventId = sourceEventId;
        Text = text ?? throw new ArgumentNullException(nameof(text));
        Speaker = speaker ?? string.Empty;
    }

    /// <summary>来源事件标识。</summary>
    public EventId SourceEventId { get; }

    /// <summary>文本。</summary>
    public string Text { get; }

    /// <summary>说话人。</summary>
    public string Speaker { get; }
}
