using System;
using System.Collections.Generic;
using System.Linq;

namespace ClaudeCodeGameStudios.Domain.Events;

/// <summary>
/// 领域事件日志 —— 追加写，按 ID 去重。
/// </summary>
/// <remarks>
/// <para>
/// <b>唯一写者：<c>Events</c> 模块</b>（<c>05</c> §2.3 的不变量 I10）。
/// 别的模块产生事件后交给它记录，但不得自己改日志里的内容。
/// </para>
/// <para>
/// <b>去重是硬保证，不是建议。</b> 基线 §13.7 / §11.4 都要求「同一后果不得因重复加载或重复结算
/// 而被应用多次」；杂谈是它的下游出口，一个没有幂等键的出口会在读档后把同一件事说两遍。
/// </para>
/// </remarks>
public sealed class EventLog
{
    private readonly Dictionary<string, DomainEvent> _byId = new(StringComparer.Ordinal);
    private readonly List<DomainEvent> _ordered = new();

    /// <summary>已记录的事件，按记录顺序。</summary>
    public IReadOnlyList<DomainEvent> Events => _ordered;

    /// <summary>事件总数。</summary>
    public int Count => _ordered.Count;

    /// <summary>
    /// 记录一个事件。<b>同一 <see cref="DomainEvent.Id"/> 只生效一次</b>。
    /// </summary>
    /// <param name="e">事件。</param>
    /// <returns>
    /// <c>true</c> 表示这次是新事件；<c>false</c> 表示该 ID 之前已记录过，本次被忽略。
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="e"/> 为 null。</exception>
    /// <exception cref="ArgumentException">事件 ID 为空。</exception>
    public bool Record(DomainEvent e)
    {
        if (e is null) throw new ArgumentNullException(nameof(e));
        if (string.IsNullOrEmpty(e.Id.Value))
        {
            throw new ArgumentException("事件 ID 不能为空 —— 它是杂谈与幂等的唯一依据。", nameof(e));
        }

        if (_byId.ContainsKey(e.Id.Value)) return false;

        _byId[e.Id.Value] = e;
        _ordered.Add(e);
        return true;
    }

    /// <summary>该 ID 是否已记录。</summary>
    /// <param name="id">事件标识。</param>
    /// <returns>已记录时为 true。</returns>
    public bool Contains(EventId id) => id.Value.Length > 0 && _byId.ContainsKey(id.Value);

    /// <summary>按 ID 取回事件。</summary>
    /// <param name="id">事件标识。</param>
    /// <returns>已记录的事件；否则为 null。</returns>
    public DomainEvent? Find(EventId id)
        => id.Value.Length > 0 && _byId.TryGetValue(id.Value, out var e) ? e : null;
}

/// <summary>
/// 杂谈条目流 —— 把已发生的事件变成玩家能读到的文本。
/// </summary>
/// <remarks>
/// <para>
/// <b>唯一写者：<c>Events</c> 模块</c>。
/// 本类<b>不判断事件值不值得记</b>（那是 <c>05</c> §2.3 划给别处的边界）；
/// 它只负责「已记录的事件 → 可溯源的条目」，并保证<b>一条事件至多一条条目</b>。
/// </para>
/// <para>
/// <b>渲染必须由注入的委托完成。</b> 本类<b>不含任何模板文本</b> ——
/// 模板内容属杂谈内容设计，是 <c>07</c> 技术债 B6/B6a 的范围，不该和幂等逻辑混在一个类里。
/// </para>
/// </remarks>
public sealed class ChronicleFeed
{
    private readonly HashSet<string> _published = new(StringComparer.Ordinal);
    private readonly List<ChronicleEntry> _entries = new();

    /// <summary>已发布的条目。</summary>
    public IReadOnlyList<ChronicleEntry> Entries => _entries;

    /// <summary>条目的来源事件标识集合（不变量 I5 的可查询形式）。</summary>
    public IReadOnlyCollection<string> PublishedSourceIds => _published;

    /// <summary>
    /// 把一个事件转成一条杂谈并发布。
    /// </summary>
    /// <param name="e">已发生的事件。</param>
    /// <param name="log">事件日志 —— 条目必须能在其中找到它的来源事件。</param>
    /// <param name="render">
    /// 渲染委托：把事件变成文本。返回 null 表示本事件<b>不产生条目</b>。
    /// <para>【建议】第一阶段不应有返回 null 的情形；保留该出口是为了将来「有些事件不写入杂谈」。</para>
    /// </param>
    /// <returns>
    /// <c>true</c> 表示新发布了一条；<c>false</c> 表示被去重、或渲染委托返回 null。
    /// </returns>
    /// <exception cref="ArgumentNullException">参数为 null。</exception>
    /// <exception cref="InvalidOperationException">
    /// 该事件<b>不在日志里</b> —— 凭空生成杂谈条目就是编造（基线 §7.2）。
    /// </exception>
    public bool Publish(
        DomainEvent e, EventLog log, Func<DomainEvent, ChronicleEntry?> render)
    {
        if (e is null) throw new ArgumentNullException(nameof(e));
        if (log is null) throw new ArgumentNullException(nameof(log));
        if (render is null) throw new ArgumentNullException(nameof(render));

        // 先溯源，再渲染：没有来源事件就没有条目。
        if (!log.Contains(e.Id))
        {
            throw new InvalidOperationException(
                $"事件 {e.Id}（{e.Type}）不在日志里，拒绝为它生成杂谈条目。"
                + "杂谈必须对应一件已发生的事（基线 §7.2），凭空生成的条目就是编造。");
        }

        // 同一事件 ID 至多一条（不变量 I5）。
        if (!_published.Add(e.Id.Value)) return false;

        var rendered = render(e);
        if (rendered is null) return false;

        // 渲染者不得换掉来源 ID —— 否则条目就与事件脱钩了。
        if (rendered.SourceEventId != e.Id)
        {
            throw new InvalidOperationException(
                $"渲染委托把来源事件 ID 从 {e.Id} 换成了 {rendered.SourceEventId}。"
                + "来源 ID 是杂谈可被验证的唯一依据，渲染无权更改它。");
        }

        _entries.Add(rendered);
        return true;
    }
}

/// <summary>
/// 领域事件的确定性标识生成器。
/// </summary>
/// <remarks>
/// <para>
/// ID 由「类型 + 参与者 + 序号」拼成，因而<b>可复现</b> ——
/// 同一串事件重放得到同一串 ID，幂等判断才有意义。
/// </para>
/// <para>
/// 【待定】ID 的具体形态不属设计决定；此处只保证<b>唯一且可复现</b>。
/// </para>
/// </remarks>
public sealed class EventIdGenerator
{
    private readonly Dictionary<string, long> _counters = new(StringComparer.Ordinal);

    /// <summary>
    /// 为「类型 + 参与者」取下一个标识。
    /// </summary>
    /// <param name="type">事件类型。</param>
    /// <param name="actor">参与者。</param>
    /// <returns>确定性标识。</returns>
    public EventId Next(EventTypeId type, string actor)
    {
        var key = type.Value + "|" + (actor ?? string.Empty);
        _counters.TryGetValue(key, out var n);
        _counters[key] = n + 1;
        return new EventId($"{type.Value}:{actor}:{n}");
    }

    /// <summary>把计数器恢复到某个状态（读档 / 重放）。</summary>
    /// <param name="type">事件类型。</param>
    /// <param name="actor">参与者。</param>
    /// <param name="nextValue">下一个应返回的序号。</param>
    public void Restore(EventTypeId type, string actor, long nextValue)
        => _counters[type.Value + "|" + (actor ?? string.Empty)] = nextValue;
}
