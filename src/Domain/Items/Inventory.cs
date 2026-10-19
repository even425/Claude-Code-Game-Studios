using System;
using System.Collections.Generic;
using System.Linq;

namespace ClaudeCodeGameStudios.Domain.Items;

/// <summary>
/// 一份库存：<b>持有者</b> + 一组 <c>ItemId → 数量</c>。
/// </summary>
/// <remarks>
/// <para><b>不可变</b>。任何修改 = 返回新对象，原对象不动。
/// 这一点和 <c>Village</c>（<c>Add</c> 原地变更）不一样 ——
/// 库存的"持有者"是英雄或村庄，库存变更要进入事件溯源（D-020），
/// 不可变让"事件携带哪个 Inventory"成为可被快照的事实，
/// 而"快照之后再被悄悄改"是危险的。</para>
///
/// <para><b>每件物品有上限</b>（P2-D06 建议）。容量是构造时给的；后续可扩展为"按物品类型
/// 设容量"，但本阶段统一一个值（M1 默认无限大 = 容忍零碎输入）。</para>
///
/// <para><b>转移 = 同一次操作内两边都改</b>（沿用基线 I15 全有或全无）：
/// 「先从 A 减、再给 B 加」中间任何错误都会留下半笔账。
/// 改用一个 <see cref="Transfer"/> 方法把两边打包成一次预检+提交。</para>
/// </remarks>
public sealed class Inventory
{
    private readonly Dictionary<ItemId, long> _items;

    /// <summary>
    /// 构造一份空库存。
    /// </summary>
    /// <param name="capacity">每件物品的容量上限。<b>0 表示无限</b>（不设上限）。</param>
    public Inventory(long capacity = 0) : this(new Dictionary<ItemId, long>(ItemIdComparer.Comparer), capacity) { }

    /// <summary>
    /// 构造一份有初始物品的库存。
    /// </summary>
    /// <param name="initial">初始物品清单。键不存在 = 0。</param>
    /// <param name="capacity">每件物品的容量上限；0 表示无限。</param>
    /// <exception cref="ArgumentException">初始清单中任意物品 <b>负数</b>。</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="capacity"/> 为负。</exception>
    public Inventory(IEnumerable<KeyValuePair<ItemId, long>> initial, long capacity = 0)
    {
        if (capacity < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity), capacity,
                "库存容量不得为负。0 表示无限。");
        }

        _items = new Dictionary<ItemId, long>(ItemIdComparer.Comparer);
        if (initial is not null)
        {
            foreach (var (id, count) in initial)
            {
                if (count < 0)
                {
                    throw new ArgumentException(
                        $"初始库存含负数：{id} = {count}。", nameof(initial));
                }

                if (count > 0) _items[id] = count;
            }
        }

        Capacity = capacity;
    }

    /// <summary>每件物品的容量上限。0 表示无限。</summary>
    public long Capacity { get; }

    /// <summary>所有物品与数量（只读快照）。</summary>
    public IReadOnlyDictionary<ItemId, long> Items => _items;

    /// <summary>
    /// 拿到指定物品的当前数量。键不存在 = 0。
    /// </summary>
    public long Count(ItemId id) => _items.TryGetValue(id, out var n) ? n : 0L;

    /// <summary>
    /// 返回「加了 <paramref name="delta"/> 份」之后的新库存。
    /// </summary>
    /// <remarks>
    /// <para><b>不会修改原对象</b>。调用方拿到的始终是返回的新对象。
    /// 这不是性能优化路径 —— 走事件溯源 + 存档路径，<b>可读性</b>优先。</para>
    ///
    /// <para><b>容量防线（P2-D06）</b>：加了之后不能超过 <see cref="Capacity"/>。
    /// 容量 = 0 时不检查。</para>
    ///
    /// <para><b>非法输入</b>：<paramref name="delta"/> ≤ 0 → 抛 <see cref="ArgumentOutOfRangeException"/>。
    /// （扣减走 <see cref="Remove"/>；混合"既加又减"会让人误以为负值合法。）</para>
    /// </remarks>
    /// <param name="id">物品 Id。<b>不得为空</b>。</param>
    /// <param name="delta">要加的正数。</param>
    /// <returns>新库存。失败时（容量超出 / Id 为空）<b>原样返回 null</b>，不抛。</returns>
    public Inventory? TryAdd(ItemId id, long delta)
    {
        if (string.IsNullOrEmpty(id.Value)) return null;
        if (delta <= 0) return null;
        long current = Count(id);
        long next = current + delta;

        if (Capacity > 0 && next > Capacity)
        {
            return null;   // 容量超 —— 整笔拒绝，调用方按"未变化"处理
        }

        // 全有或全无：到这里已经预检过了，剩下只是克隆字典
        var cloned = new Dictionary<ItemId, long>(ItemIdComparer.Comparer);
        foreach (var kv in _items) cloned[kv.Key] = kv.Value;
        cloned[id] = next;
        return new Inventory(cloned, Capacity);
    }

    /// <summary>
    /// 返回「扣了 <paramref name="delta"/> 份」之后的新库存。
    /// </summary>
    /// <remarks>
    /// <b>不抛</b>：扣到负数 / 数量不足时返回 null，调用方按"未变化"处理。
    /// （沿用 <c>Village.TrySpend</c> 的 I12 风格 —— 「失败时<b>不</b>抛」
    /// 是因为失败通常意味着"用户输错了"，抛错会让 caller 一路 try-catch，
    /// 静默回退能让决策层写得清爽。）</para>
    /// </remarks>
    public Inventory? TryRemove(ItemId id, long delta)
    {
        if (string.IsNullOrEmpty(id.Value)) return null;
        if (delta <= 0) return null;
        long current = Count(id);
        if (current < delta) return null;   // 不足

        var cloned = new Dictionary<ItemId, long>(ItemIdComparer.Comparer);
        foreach (var kv in _items) cloned[kv.Key] = kv.Value;
        long next = current - delta;
        if (next == 0) cloned.Remove(id);
        else cloned[id] = next;
        return new Inventory(cloned, Capacity);
    }

    /// <summary>
    /// <b>全有或全无</b>地把一批物品从一个库存移到另一个。
    /// </summary>
    /// <remarks>
    /// <para><b>调用方应先确认 <paramref name="from"/> 是 <paramref name="to"/> 的合法来源</b> ——
    /// 例如：同一英雄的两个库存之间互转是合法的；
    /// 跨英雄之间需要走"交易"语义，而交易<b>不只是</b>物品转移（还要换钱），
    /// 应当用 <see cref="Domain.Trade.Trade"/> 而不是 <see cref="Transfer"/>。</para>
    ///
    /// <para><b>两边都变</b>：先预检 from 够不够、to 装不装得下，任意一边不行就<b>都不动</b>。</para>
    /// </remarks>
    /// <param name="from">源库存（会被扣减）。</param>
    /// <param name="to">目标库存（会被增加）。</param>
    /// <param name="id">物品 Id。</param>
    /// <param name="delta">要转移的正数。</param>
    /// <returns>成功时返回「新的 from / 新的 to」；失败时（任意一边）返回 (null, null)。</returns>
    public static (Inventory? fromAfter, Inventory? toAfter)
        Transfer(Inventory from, Inventory to, ItemId id, long delta)
    {
        if (string.IsNullOrEmpty(id.Value)) return (null, null);
        if (delta <= 0) return (null, null);

        var newFrom = from.TryRemove(id, delta);
        if (newFrom is null) return (null, null);

        var newTo = to.TryAdd(id, delta);
        if (newTo is null) return (null, null);

        return (newFrom, newTo);
    }

    /// <summary>
    /// 「<b>用于断言</b>：把整份库存转成一个 (id, count) 列表，按 Id 排序。
    /// 稳定顺序 —— 单测可拿它和预期列表逐项比较，不必担心字典迭代顺序。</summary>
    public IReadOnlyList<KeyValuePair<ItemId, long>> Snapshot()
        => _items.OrderBy(kv => kv.Key.Value, StringComparer.Ordinal).ToArray();
}

/// <summary>
/// <see cref="ItemId"/> 的稳定比较器：把"机器名"按 Ordinal 排序。
/// </summary>
/// <remarks>
/// <b>不可变 record struct 的 Comparer 不能挂在它自己身上</b>（C# 限制），
/// 所以单独放一个静态类里，供 <c>Inventory</c>、<c>Stall</c>、<c>Trade</c> 等共享。
/// </remarks>
public static class ItemIdComparer
{
    public static IEqualityComparer<ItemId> Comparer { get; } = new Equality();
    public static IComparer<ItemId> OrdinalOrder { get; } = new Order();

    private sealed class Equality : IEqualityComparer<ItemId>
    {
        public bool Equals(ItemId x, ItemId y)
            => string.Equals(x.Value, y.Value, StringComparison.Ordinal);
        public int GetHashCode(ItemId obj) => obj.Value?.GetHashCode() ?? 0;
    }

    private sealed class Order : IComparer<ItemId>
    {
        public int Compare(ItemId x, ItemId y)
            => string.CompareOrdinal(x.Value, y.Value);
    }
}
