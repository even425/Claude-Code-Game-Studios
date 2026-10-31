using System;
using ClaudeCodeGameStudios.Domain.Village;

namespace ClaudeCodeGameStudios.Application;

/// <summary>
/// <b>村庄日常开销</b>（D-125）—— 每天从村库现金里扣一点，<b>只扣闲钱</b>。
/// </summary>
/// <remarks>
/// <para><b>为什么需要它</b>：村库的收入里有一大块是<b>从外面进来的</b>
/// （兽潮打赢 +18 金、盗匪 +10 金 —— D-101/D-096）⇒ 而支出侧只有
/// 「村庄升级」（5 级共 750 金，**一次性**）⇒ 钱必然越囤越多
/// ⇒ D-109 的第二条判据「<b>村庄花得出去</b>（可花未花的钱不一路囤）」**一直不绿**。</para>
///
/// <para>⚠️ <b>「只扣闲钱」是关键</b>：扣款<b>不许动升级储备</b>
/// （<c>下一级村庄升级要留的钱</c>）⇒ 否则村庄会**永远升不上去**
/// ⇒ 那比「钱囤着」更糟（层级附带建筑井 / 铁匠铺也就永远不出现）。</para>
///
/// <para>⚠️ <b>扣不动就不扣</b>（不欠账）⇒ 村庄不会变成负数；
/// 且「村库现金 &lt; 维护费」时只扣到 0 为止。</para>
///
/// <para>⚠️ <b>数值是待拍板的建议值</b>（默认 <c>0</c> = 不扣 ⇒ 现状不变）；
/// 见参数台账与 <c>07</c> 的 D-125。</para>
/// </remarks>
public static class VillageUpkeepService
{
    /// <summary>
    /// 每天的村庄日常开销（<b>建议值，默认 0 = 不扣</b> ⇒ 现状不变；待开发者拍板）。
    /// </summary>
    /// <remarks>
    /// ⚠️ **为什么默认 0**：这条机制是**新设计**（D-125 待拍板）⇒ 在拍板之前
    /// **不能**悄悄改变玩家的钱 ⇒ 默认值必须等于「什么都不做」。
    /// </remarks>
    public const long DefaultUpkeepPerDay = 8L;

    /// <summary>
    /// 村庄每升一级，<b>日常开销多多少</b>（D-130 建议值：2 金/级）。
    /// </summary>
    /// <remarks>
    /// ⚠️ **为什么要有它**（D-130 的实测依据）：村庄**长大 ⇒ 收入变大**
    /// （井 + 铁匠铺 ⇒ 采集每天多 2 份 ⇒ 副本产出 ↑ ⇒ 村库毛收入 ≈ 12.6 金/天）
    /// ⇒ 而维护费若是**固定** 8 金 ⇒ **抵不住** ⇒ 又开始囤（判据②退回 ⚠️）。
    /// ⇒ 语义上正确的做法：**村庄越大，维护越贵**。
    /// <br>⚠️ **数值是建议值**（D-126 拍板的 8 金 = 0 级村庄的开销）⇒ 可改（改这一个常量即可）。
    /// </remarks>
    public const long UpkeepStepPerTier = 0L;

    /// <summary>
    /// 某个层级的村庄每天要付多少日常开销。
    /// </summary>
    /// <param name="tier">村庄层级（0 起）。</param>
    /// <returns>每天的开销（金）。</returns>
    /// <remarks>
    /// 公式：<c>0 级 8 金，每升一级 +增量</c>。
    /// ⚠️ **D-130 实测：增量设为 2 反而让判据①变红**（把村库抽干 ⇒ 收购站没预算
    /// ⇒ 村民卖不掉货 ⇒ 中位只剩 9 金、最富 441 ⇒ 6 倍集中）⇒ **默认回退为 0（不递增）**，
    /// 机制保留（要开就改这一个常量）。
    /// </remarks>
    public static long UpkeepForTier(int tier)
    {
        if (tier <= 0) return DefaultUpkeepPerDay;
        return DefaultUpkeepPerDay + UpkeepStepPerTier * tier;
    }

    /// <summary>
    /// 累计已收的日常开销（金）—— <b>那笔钱流出村庄</b> ⇒ 属于守恒律的「已销毁」。
    /// </summary>
    /// <remarks>
    /// ⚠️ **为什么单独记**：快照的「已销毁」原本只含**商队销毁**（D-090 之后升级那部分
    /// 改记「村庄资产」）⇒ 维护费不并进去 ⇒ 守恒律左边少一笔 ⇒ **钱凭空消失**（实测 0/200）。
    /// <br>⚠️ **读档要重置**（<see cref="ResetTotal"/>）—— 否则旧档会带着上一局的数。
    /// </remarks>
    public static long TotalUpkeepSpent { get; private set; }

    /// <summary>记一笔已收的开销（累加进 <see cref="TotalUpkeepSpent"/>）。</summary>
    /// <param name="amount">刚收的金额（≤ 0 = 不记）。</param>
    public static void RecordSpent(long amount)
    {
        if (amount <= 0) return;
        TotalUpkeepSpent += amount;
    }

    /// <summary>读档时把累计清零（存档里没有这一项 ⇒ 它从 0 开始）。</summary>
    public static void ResetTotal() => TotalUpkeepSpent = 0L;

    /// <summary>读档时恢复累计（存档 0.13 起有这一项）。</summary>
    /// <param name="total">存档里的累计值（&lt; 0 视为 0）。</param>
    public static void RestoreTotal(long total) => TotalUpkeepSpent = total < 0 ? 0L : total;

    /// <summary>
    /// 收一天的日常开销。
    /// </summary>
    /// <param name="village">村庄（金库现金的写者）。</param>
    /// <param name="gold">金币的资源种类。</param>
    /// <param name="reserveGold">必须留着的钱（下一级村庄升级要多少；0 = 不用留）。</param>
    /// <param name="amountPerDay">每天要扣多少（≤ 0 = 不扣）。</param>
    /// <returns>实际扣了多少（0 = 没扣）。</returns>
    /// <remarks>
    /// ⚠️ **只有一处扣款判定**（权威）⇒ 采集器与真实游戏都调它 ⇒ 两边账一致。
    /// </remarks>
    public static long Charge(
        Village village,
        ResourceId gold,
        long reserveGold,
        long amountPerDay)
    {
        if (village is null) throw new ArgumentNullException(nameof(village));
        if (amountPerDay <= 0) return 0L;

        long cash = village.Get(gold);
        if (cash <= 0) return 0L;

        // ⚠️ **闲钱 = 现金 − 升级储备 × 两级**（D-128 的修正）。
        // ⚠️ **为什么是两级**：原口径「现金 − 一级储备」会把村库**钉死在储备线上**
        // ⇒ 每天的收入几乎全被维护费吃掉 ⇒ **村庄永远攒不到第二级**
        // ⇒ 实测 1000 天只升 1 级 ⇒ 村庄被锁死。
        // ⇒ 留两级 ⇒ 村库既能积累（能升级），又不会无限囤（上限 = 两级储备）。
        long spare = cash - reserveGold * 2;
        if (spare <= 0) return 0L;

        long charge = amountPerDay < spare ? amountPerDay : spare;
        if (!village.TrySpend(gold, charge)) return 0L;

        // ⚠️ **记账在服务内部做**（不是调用方）—— 调用方可能忘
        // ⇒ 那笔钱就「凭空消失」（实测守恒律 0/200）⇒ 权威判定只有这一处。
        RecordSpent(charge);

        return charge;
    }
}
