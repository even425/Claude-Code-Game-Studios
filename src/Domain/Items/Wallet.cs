using System;
using System.Collections.Generic;
using System.Linq;
using ClaudeCodeGameStudios.Domain.Village;

namespace ClaudeCodeGameStudios.Domain.Items;

/// <summary>
/// 一份<b>资金/材料</b>钱包：<see cref="ResourceId"/> → 数量。
/// </summary>
/// <remarks>
/// <para><b>为什么不复用 <c>Inventory</c></b>：<see cref="Inventory"/> 卖的是<b>物品</b>
/// （<see cref="ItemId"/> → 数量），<see cref="Wallet"/> 装的是<b>资金 / 材料</b>
/// （<see cref="ResourceId"/> → 数量）。两者的<b>语义</b>不一样：</para>
/// <list type="bullet">
/// <item>卖出 3 份石料 → 买方钱包加 3 钱 + 卖方背包减 3 份石料</item>
/// <item>卖出 1 件「神秘道具」→ 买方背包加 1 件，<b>不是钱</b></item>
/// </list>
/// <para>结构同源（都是 immutable dict），但 <c>Wallet</c> 走 <c>ResourceId</c>，
/// 数量语义是「钱 / 一堆」；<c>Inventory</c> 走 <c>ItemId</c>，数量语义是「几件」。</para>
///
/// <para><b>不可变 + 容量 + I15</b> 全部沿用 <see cref="Inventory"/> 的同款做法 ——
/// 任何修改 = 返回新对象；加超容量返回 null；<see cref="Transfer"/> 任意一边失败
/// 就两边都返回 null。</para>
///
/// <para><b>本类的 key 是 <see cref="ResourceId"/></b>（与 <see cref="Inventory"/> 不同）。
/// 第二阶段把 <c>Wallet</c> 挂在 <c>HeroState</c> 上，<c>Village.Resources</c>
/// 也叫 <c>Dictionary&lt;ResourceId,long&gt;</c> —— 两个是<b>同样的 key 类型</b>，
/// 但<b>持有者</b>不同，<b>转移</b>必须走 <see cref="Transfer"/>。</para>
/// </remarks>
public sealed class Wallet
{
    private readonly Dictionary<ResourceId, long> _resources;

    /// <summary>构造一份空钱包。</summary>
    /// <param name="capacity">每种资源的上限；0 表示无限。</param>
    public Wallet(long capacity = 0)
        : this(Array.Empty<KeyValuePair<ResourceId, long>>(), capacity) { }

    /// <summary>构造一份有初始余额的包。</summary>
    /// <param name="initial">初始资源清单。键不存在 = 0。</param>
    /// <param name="capacity">每种资源的上限；0 表示无限。</param>
    /// <exception cref="ArgumentException">初始清单中任意资源 <b>负数</b>。</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="capacity"/> 为负。</exception>
    public Wallet(IEnumerable<KeyValuePair<ResourceId, long>> initial, long capacity = 0)
    {
        if (capacity < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity), capacity,
                "钱包容量不得为负。0 表示无限。");
        }

        _resources = new Dictionary<ResourceId, long>(ResourceIdComparer.Comparer);
        if (initial is not null)
        {
            foreach (var (id, amount) in initial)
            {
                if (amount < 0)
                {
                    throw new ArgumentException(
                        $"初始钱包含负数：{id} = {amount}。", nameof(initial));
                }

                if (amount > 0) _resources[id] = amount;
            }
        }

        Capacity = capacity;
    }

    /// <summary>每种资源的上限；0 表示无限。</summary>
    public long Capacity { get; }

    /// <summary>所有资源与数量（只读快照）。</summary>
    public IReadOnlyDictionary<ResourceId, long> Resources => _resources;

    /// <summary>拿到指定资源的当前余额。键不存在 = 0。</summary>
    public long Amount(ResourceId id)
        => _resources.TryGetValue(id, out var n) ? n : 0L;

    /// <summary>返回「加了 <paramref name="delta"/>」之后的新包。</summary>
    /// <remarks>
    /// 走 <see cref="Inventory.TryAdd"/> 的同款防线：空 id / 非正 delta / 超容量 = null。
    /// </remarks>
    public Wallet? TryAdd(ResourceId id, long delta)
    {
        if (string.IsNullOrEmpty(id.Value)) return null;
        if (delta <= 0) return null;
        long next = Amount(id) + delta;
        if (Capacity > 0 && next > Capacity) return null;

        var cloned = new Dictionary<ResourceId, long>(ResourceIdComparer.Comparer);
        foreach (var kv in _resources) cloned[kv.Key] = kv.Value;
        cloned[id] = next;
        return new Wallet(cloned, Capacity);
    }

    /// <summary>返回「扣了 <paramref name="delta"/>」之后的新包。</summary>
    public Wallet? TrySpend(ResourceId id, long delta)
    {
        if (string.IsNullOrEmpty(id.Value)) return null;
        if (delta <= 0) return null;
        if (Amount(id) < delta) return null;   // I12：不扣成负

        var cloned = new Dictionary<ResourceId, long>(ResourceIdComparer.Comparer);
        foreach (var kv in _resources) cloned[kv.Key] = kv.Value;
        long next = Amount(id) - delta;
        if (next == 0) cloned.Remove(id);
        else cloned[id] = next;
        return new Wallet(cloned, Capacity);
    }

    /// <summary>把某种资源从 from 转到 to，<b>全有或全无</b>。</summary>
    /// <remarks>
    /// <para>使用示例：买方向卖方支付 30 钱 → <c>Wallet.Transfer(buyerWallet, sellerWallet, "金币", 30)</c>。</para>
    /// <para><b>谁付钱、谁收钱，由调用方决定</b> —— <see cref="Transfer"/> 不区分买卖。</para>
    /// </remarks>
    public static (Wallet? fromAfter, Wallet? toAfter)
        Transfer(Wallet from, Wallet to, ResourceId id, long delta)
    {
        if (string.IsNullOrEmpty(id.Value)) return (null, null);
        if (delta <= 0) return (null, null);

        var newFrom = from.TrySpend(id, delta);
        if (newFrom is null) return (null, null);

        var newTo = to.TryAdd(id, delta);
        if (newTo is null) return (null, null);

        return (newFrom, newTo);
    }

    /// <summary>
    /// 整份包按 <c>ResourceId.Value</c> Ordinal 升序的快照（用于单测断言稳定顺序）。
    /// </summary>
    public IReadOnlyList<KeyValuePair<ResourceId, long>> Snapshot()
        => _resources
           .OrderBy(kv => kv.Key.Value, StringComparer.Ordinal)
           .ToArray();
}

/// <summary>
/// <see cref="ResourceId"/> 的稳定比较器：Ordinal 排序与相等。
/// </summary>
public static class ResourceIdComparer
{
    public static IEqualityComparer<ResourceId> Comparer { get; } = new Equality();

    private sealed class Equality : IEqualityComparer<ResourceId>
    {
        public bool Equals(ResourceId x, ResourceId y)
            => string.Equals(x.Value, y.Value, StringComparison.Ordinal);

        public int GetHashCode(ResourceId obj)
            => obj.Value is null ? 0 : obj.Value.GetHashCode();
    }
}
