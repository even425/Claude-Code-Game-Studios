using System;
using System.Collections.Generic;
using ClaudeCodeGameStudios.Domain.Heroes;

namespace ClaudeCodeGameStudios.Domain.Relationships;

/// <summary>关系快照的一条记录（存档 / 恢复用）。</summary>
/// <param name="Key">有向关系键。</param>
/// <param name="Value">关系值。</param>
public readonly record struct RelationshipRecord(RelationshipKey Key, int Value);

/// <summary>
/// 一次关系变化的结果。
/// </summary>
/// <param name="Applied">
/// 变化是否被<b>接受</b>。同一 <c>interactionKey</c> 的第二次调用为 false（幂等）。
/// </param>
/// <param name="Changed">
/// 关系值是否真的变了。已到边界（±100）时可能 <see cref="Applied"/> 为 true 而本项为 false ——
/// 此时<b>不应</b>产生「关系变化」事件（<c>05</c> §5.1 反装饰规则）。
/// </param>
/// <param name="Before">变化前。</param>
/// <param name="After">变化后。</param>
public readonly record struct RelationshipChange(
    bool Applied, bool Changed, Relationship Before, Relationship After)
{
    /// <summary>这次变化是否跨过了展示阶段（杂谈只对跨档发消息，P3-D09）。</summary>
    public bool StageChanged =>
        RelationshipRules.StageOf(Before.Value) != RelationshipRules.StageOf(After.Value);
}

/// <summary>
/// 全部英雄关系的<b>唯一写者</b>（<c>05</c> 不变量 I10）。
/// </summary>
/// <remarks>
/// <para><b>它持有什么</b>：有向关系 <c>(from, to) → int</c>；以及一份
/// <c>interactionKey</c> 已应用集合，用来保证「同一业务结果重复处理不会重复加关系」
/// （<c>07</c> D-036 / P3-D08）。</para>
/// <para><b>它不管什么</b>：不知道英雄名单（那是应用层/存档校验的事）、
/// 不产生事件（事件由应用层根据 <see cref="RelationshipChange"/> 构造）、
/// 不依赖 Godot（I9）。</para>
/// <para><b>原子性</b>：<see cref="Restore"/> 先完整校验再替换；任何一条非法输入
/// 都不会留下「一半新一半旧」的关系表（与 I15 同一纪律）。</para>
/// </remarks>
public sealed class RelationshipBook
{
    private readonly Dictionary<RelationshipKey, Relationship> _values = new();
    private readonly HashSet<string> _appliedKeys = new(StringComparer.Ordinal);

    /// <summary>当前存在的关系条数（不包含「值为 0 且从未变化」的隐含关系）。</summary>
    public int Count => _values.Count;

    /// <summary>已应用的业务键集合（幂等依据；必须随存档落盘）。</summary>
    public IReadOnlyCollection<string> AppliedKeys => _appliedKeys;

    /// <summary>
    /// 查询一段关系。没有记录 = 初始值 0。
    /// </summary>
    /// <param name="key">有向关系键。</param>
    /// <returns>关系值。</returns>
    /// <exception cref="ArgumentException">关系键不完整（空 ID / 自关联）。</exception>
    public Relationship Get(RelationshipKey key)
    {
        RequireKey(key);
        return _values.TryGetValue(key, out var value) ? value : Relationship.Initial;
    }

    /// <summary>查询 <paramref name="from"/> 对 <paramref name="to"/> 的关系值。</summary>
    /// <param name="from">一方英雄。</param>
    /// <param name="to">另一方英雄。</param>
    /// <returns>关系值（无记录为 0）。</returns>
    public int ValueOf(HeroId from, HeroId to) => Get(new RelationshipKey(from, to)).Value;

    /// <summary>查询一段关系当前所处的展示阶段。</summary>
    /// <param name="key">有向关系键。</param>
    /// <returns>展示阶段。</returns>
    public RelationshipStage StageOf(RelationshipKey key)
        => RelationshipRules.StageOf(Get(key).Value);

    /// <summary>某个业务键是否已经应用过。</summary>
    /// <param name="interactionKey">业务键。</param>
    /// <returns>已应用为 true。</returns>
    public bool HasApplied(string interactionKey) => _appliedKeys.Contains(interactionKey);

    /// <summary>
    /// 按一个业务键应用一次关系变化。<b>同一业务键只生效一次。</b>
    /// </summary>
    /// <param name="interactionKey">
    /// 幂等键，如 <c>social:h-0->h-1@123.5</c>、<c>dungeon:run-1:h-0->h-1</c>。
    /// </param>
    /// <param name="key">有向关系键。</param>
    /// <param name="delta">变化量；正值变好、负值变差。</param>
    /// <returns>本次结果（是否接受 / 是否真的改变 / 前后值）。</returns>
    /// <exception cref="ArgumentException">业务键为空，或关系键不完整。</exception>
    public RelationshipChange Apply(string interactionKey, RelationshipKey key, int delta)
    {
        if (string.IsNullOrWhiteSpace(interactionKey))
        {
            throw new ArgumentException("关系变化的业务键不能为空 —— 它是幂等的唯一依据。", nameof(interactionKey));
        }

        RequireKey(key);

        // 先看是否重复，再写：重复调用连「边界 clamp 结果」都不该改变。
        var before = Get(key);
        if (!_appliedKeys.Add(interactionKey))
        {
            return new RelationshipChange(false, false, before, before);
        }

        var after = before.WithDelta(delta);
        if (after == before)
        {
            // 已到边界：接受这次业务（键已记录），但关系值没有变化。
            return new RelationshipChange(true, false, before, before);
        }

        _values[key] = after;
        return new RelationshipChange(true, true, before, after);
    }

    /// <summary>
    /// 导出全部关系（顺序确定，便于存档与测试比对）。
    /// </summary>
    /// <returns>按 <c>From</c>、<c>To</c> 排序的快照。</returns>
    public IReadOnlyList<RelationshipRecord> Snapshot()
    {
        var list = new List<RelationshipRecord>(_values.Count);
        foreach (var (key, value) in _values)
        {
            list.Add(new RelationshipRecord(key, value.Value));
        }

        list.Sort(static (a, b) =>
        {
            int byFrom = string.CompareOrdinal(a.Key.From.Value, b.Key.From.Value);
            return byFrom != 0 ? byFrom : string.CompareOrdinal(a.Key.To.Value, b.Key.To.Value);
        });

        return list;
    }

    /// <summary>
    /// 用快照整份替换现有关系 —— <b>全有或全无</b>（读档用）。
    /// </summary>
    /// <param name="records">关系记录。</param>
    /// <param name="appliedKeys">已应用业务键。</param>
    /// <exception cref="ArgumentException">存在重复关系、越界值、空业务键或非法键。</exception>
    public void Restore(
        IReadOnlyList<RelationshipRecord> records,
        IEnumerable<string> appliedKeys)
    {
        if (records is null) throw new ArgumentNullException(nameof(records));
        if (appliedKeys is null) throw new ArgumentNullException(nameof(appliedKeys));

        // 先全部校验到临时表，再一次性替换 —— 中途失败不会留下半份关系表。
        var values = new Dictionary<RelationshipKey, Relationship>(records.Count);
        foreach (var record in records)
        {
            RequireKey(record.Key);
            var value = new Relationship(record.Value);   // 越界即抛
            if (!values.TryAdd(record.Key, value))
            {
                throw new ArgumentException($"关系重复：{record.Key}。", nameof(records));
            }
        }

        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var key in appliedKeys)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                throw new ArgumentException("已应用业务键不能为空。", nameof(appliedKeys));
            }

            keys.Add(key);
        }

        _values.Clear();
        foreach (var (key, value) in values) _values[key] = value;

        _appliedKeys.Clear();
        foreach (var key in keys) _appliedKeys.Add(key);
    }

    /// <summary>
    /// 关系键必须完整：两端非空且不是自己。
    /// <c>default(RelationshipKey)</c> 能绕过构造函数，所以入口再验一次。
    /// </summary>
    private static void RequireKey(RelationshipKey key)
    {
        if (string.IsNullOrEmpty(key.From.Value) || string.IsNullOrEmpty(key.To.Value))
        {
            throw new ArgumentException(
                $"关系键不完整（{key}）。两端都必须是有效英雄标识。", nameof(key));
        }

        if (key.From == key.To)
        {
            throw new ArgumentException(
                $"英雄「{key.From.Value}」不能与自己建立关系。", nameof(key));
        }
    }
}
