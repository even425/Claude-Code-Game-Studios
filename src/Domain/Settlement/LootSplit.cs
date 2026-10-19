using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using ClaudeCodeGameStudios.Domain.Heroes;
using ClaudeCodeGameStudios.Domain.Items;

namespace ClaudeCodeGameStudios.Domain.Settlement;

/// <summary>
/// 一堆战利品：某种物品的若干份。
/// </summary>
/// <param name="Item">物品。</param>
/// <param name="Quantity">份数。</param>
public readonly record struct LootStack(ItemId Item, long Quantity)
{
    /// <inheritdoc />
    public override string ToString() => $"{Item.Value}×{Quantity}";
}

/// <summary>
/// 一次战利品分配的<b>结果</b>：谁拿到什么。
/// </summary>
/// <remarks>
/// <para><b>它是算出来的，不是攒出来的</b>：分配完成后不可再改，
/// 因为它要作为结算增量的一部分被提交（一次提交、同一把幂等锁）。</para>
/// </remarks>
public sealed class LootAllocation
{
    private readonly List<ItemGrant> _grants;

    /// <summary>构造一份分配结果。</summary>
    /// <param name="grants">发放明细。</param>
    public LootAllocation(IEnumerable<ItemGrant> grants)
    {
        _grants = (grants ?? Enumerable.Empty<ItemGrant>()).ToList();
    }

    /// <summary>空分配（没有任何东西要发）。</summary>
    public static LootAllocation Empty { get; } = new(Array.Empty<ItemGrant>());

    /// <summary>发放明细。顺序稳定：按英雄标识、物品标识排序。</summary>
    public IReadOnlyList<ItemGrant> Grants => _grants;

    /// <summary>本次分配是否什么都没发。</summary>
    public bool IsEmpty => _grants.Count == 0;

    /// <summary>某件物品一共发出了多少份（用于守恒检查）。</summary>
    /// <param name="item">物品。</param>
    public long TotalOf(ItemId item)
        => _grants.Where(g => g.Item == item).Sum(g => g.Quantity);

    /// <summary>某位英雄拿到了什么。</summary>
    /// <param name="hero">英雄。</param>
    public IReadOnlyList<ItemGrant> ForHero(HeroId hero)
        => _grants.Where(g => g.Hero == hero).ToArray();

    /// <summary>本次分配涉及到的英雄（去重，保持顺序）。</summary>
    public IReadOnlyList<HeroId> Heroes
        => _grants.Select(g => g.Hero).Distinct().ToArray();
}

/// <summary>
/// 副本战利品的<b>分配规则</b>（R-01 · D01–D03）。
/// </summary>
/// <remarks>
/// <para><b>规则只有三条，全部写在这里，不散落在别处</b>：</para>
/// <list type="number">
/// <item><b>全队平分</b>（D01）：可整除的部分按相同数量分给每一位<b>符合资格</b>的英雄。</item>
/// <item><b>资格 = 实际参战</b>（D02）：分配的分母是<b>参战名单</b>，
/// 不是「当前队伍」也不是「村里的全部英雄」。名单由调用方给出，
/// 本类<b>只管分，不管认人</b>。</item>
/// <item><b>余数随机</b>（D03）：分不完的剩余份数逐件随机发给合格者。</item>
/// </list>
///
/// <para><b>它不做什么</b>：不生成战利品内容（那是 <see cref="ILootProvider"/>）、
/// 不碰背包（那是 <c>ItemGrantService</c>）、不决定成败（那是副本结算）。
/// 本类是<b>纯函数</b>：同样的输入 + 同样的随机序列 = 同样的结果，
/// 因此可以被单测逐条断言，也可以被存档重放。</para>
///
/// <para><b>为什么余数是「逐件独立抽样」而不是「轮流发」</b>：
/// D03 明确要的是<b>随机</b>分配。轮流发（谁先谁后）确定性强、可预测，
/// 那不是随机分配。独立抽样时同一位英雄<b>可能</b>连拿多件余数 ——
/// 期望上仍然公平（每人拿到余数的概率相同），且实现最简单。
/// 这一点是<b>规则细化</b>，不是新规则：换掉它只影响余数，不影响均分。</para>
///
/// <para><b>为什么数量守恒是硬要求</b>：分配前后物品总数必须相等。
/// 「凭空多出」与「凭空消失」都是同一个 bug 的两面 ——
/// 物品凭空消失比多分出去更难查（玩家只会发现背包里少东西）。</para>
/// </remarks>
public static class LootSplit
{
    /// <summary>
    /// 按规则分配战利品。
    /// </summary>
    /// <param name="loot">本次副本产出的战利品。</param>
    /// <param name="participants">
    /// 符合资格的英雄（<b>本次副本的实际参战名单</b>）。
    /// 重复的标识会被去重 —— 一个人不能因为名单写了两遍而拿两份。
    /// </param>
    /// <param name="rng">随机源。余数分配用它；<b>不得为 null</b>。</param>
    /// <returns>分配结果。名单为空时返回空分配（由调用方记录异常）。</returns>
    /// <exception cref="ArgumentNullException"><paramref name="rng"/> 为 null。</exception>
    /// <exception cref="ArgumentException">战利品里有物品标识为空的条目。</exception>
    public static LootAllocation Split(
        IReadOnlyList<LootStack> loot,
        IReadOnlyList<HeroId> participants,
        IRandomSource rng)
    {
        if (rng is null) throw new ArgumentNullException(nameof(rng));

        if (loot is null || loot.Count == 0) return LootAllocation.Empty;
        if (participants is null || participants.Count == 0) return LootAllocation.Empty;

        // 去重：名单里同一个人出现两次，不能让他拿两份。
        var eligible = participants.Distinct().ToArray();
        int n = eligible.Length;
        if (n == 0) return LootAllocation.Empty;

        var totals = new Dictionary<(HeroId Hero, ItemId Item), long>();

        foreach (var stack in loot)
        {
            if (string.IsNullOrEmpty(stack.Item.Value))
            {
                throw new ArgumentException(
                    $"战利品里有物品标识为空的条目（数量 {stack.Quantity}）。", nameof(loot));
            }

            if (stack.Quantity <= 0) continue;   // 零份的堆没有东西可分

            long baseEach = stack.Quantity / n;          // 可整除的部分
            long remainder = stack.Quantity % n;         // 分不完的余数

            // 1. 均分：每人 baseEach 份。
            for (int i = 0; i < n; i++)
            {
                if (baseEach > 0) Add(totals, eligible[i], stack.Item, baseEach);
            }

            // 2. 余数：逐件独立随机，**只发给合格者**。
            for (long r = 0; r < remainder; r++)
            {
                // NextDouble(bound) 返回 [0, bound) 的 double，取整即落在 [0, n-1]。
                int index = (int)rng.NextDouble(n);
                Add(totals, eligible[index], stack.Item, 1);
            }
        }

        // 稳定顺序：按英雄、再按物品排序。同一份分配在不同次运行里顺序一致，
        // 便于测试断言，也便于存档/日志对照。
        var grants = totals
            .Select(kv => new ItemGrant(kv.Key.Hero, kv.Key.Item, kv.Value))
            .OrderBy(g => g.Hero.Value, StringComparer.Ordinal)
            .ThenBy(g => g.Item.Value, StringComparer.Ordinal)
            .ToArray();

        return new LootAllocation(grants);
    }

    /// <summary>
    /// 由<b>业务标识</b>派生一个稳定的随机种子。
    /// </summary>
    /// <param name="businessId">业务标识（如副本实例标识）。</param>
    /// <returns>种子。相同标识永远得到相同种子。</returns>
    /// <exception cref="ArgumentException"><paramref name="businessId"/> 为空。</exception>
    /// <remarks>
    /// <para><b>为什么要派生子流，而不是直接用全局随机源</b>：
    /// 全局随机源（<c>Main._rng</c>）是战斗与日常共用的那一条流。
    /// 掉落若从它身上抽，会<b>插队</b> —— 一次副本掉落会改变此后所有战斗与日常的结果。
    /// 而掉落结果是<b>要被存档的</b>（它进了背包），所以它需要的是
    /// 「<b>同一个副本永远得到同一份分配</b>」，而不是「接着全局序列往下抽」。</para>
    ///
    /// <para><b>为什么不用 <c>string.GetHashCode()</c></b>：.NET 的字符串哈希
    /// <b>每个进程都不一样</b>（为防哈希碰撞攻击而随机化种子）。
    /// 用它派生种子，同一个副本在两次运行里会拿到<b>不同</b>的分配 ——
    /// 正是「读档重放得到同样的结果」这条承诺要防的事。
    /// 这里用 <b>FNV-1a</b>：跨进程、跨平台、跨版本都稳定。</para>
    /// </remarks>
    public static ulong DeriveSeed(string businessId)
    {
        if (string.IsNullOrEmpty(businessId))
        {
            throw new ArgumentException("业务标识不能为空。", nameof(businessId));
        }

        const ulong offsetBasis = 14695981039346656037UL;
        const ulong prime = 1099511628211UL;

        ulong hash = offsetBasis;
        foreach (byte b in Encoding.UTF8.GetBytes(businessId))
        {
            hash ^= b;
            hash *= prime;
        }

        // 0 会被 SeededRandom 纠正成 1；这里直接给出 1，保持「种子非零」的语义明确。
        return hash == 0 ? 1UL : hash;
    }

    /// <summary>累加某个 (英雄, 物品) 的份数。</summary>
    private static void Add(
        Dictionary<(HeroId Hero, ItemId Item), long> totals,
        HeroId hero, ItemId item, long quantity)
    {
        if (quantity <= 0) return;
        var key = (hero, item);
        totals.TryGetValue(key, out long current);
        totals[key] = current + quantity;
    }
}
