using System;
using ClaudeCodeGameStudios.Domain.Village;
using ClaudeCodeGameStudios.Domain.WorldEvents;

namespace ClaudeCodeGameStudios.Application;

/// <summary>一次资源变更的<b>实际落地结果</b>。</summary>
/// <param name="Amount">实际加/扣了多少（正 = 加，负 = 扣；0 = 没动）。</param>
/// <param name="Description">给人看的一句话（数字是<b>实际发生的</b>）。</param>
public readonly record struct ResourceChangeResult(long Amount, string Description);

/// <summary>
/// 把大事件后果落到<b>村庄资源</b>上（D-080）。
/// </summary>
/// <remarks>
/// <para>⚠️ <b>为什么是独立的一层</b>：落地的规则（「扣不动就只扣有的」、
/// 「绝不把村库扣成负数」）是<b>规则</b> ⇒ 写在表现层里就<b>没法被守卫直接验</b>。
/// ⇒ 这里是纯函数 + 领域层的 <c>Village</c> 入口 ⇒ 守卫能直接调。</para>
///
/// <para>⚠️ <b>只写村庄的资源池</b>（材料 / 金币）—— D-080 拍板：兽潮
/// <b>只损资源，人畜无损</b>（不扣英雄、不损建筑）。</para>
///
/// <para>⚠️ <b>「外部收入 / 外部支出」两个累计（D-101）</b>：
/// 大事件发的钱是<b>从外面进来的</b>（打完怪捡到的战利品）
/// ⇒ 它不在英雄钱包里、也不在「累计发放」里（那一项只记「结算发给英雄的钱」）
/// ⇒ 守恒律的等式<b>右边必须加上它</b>，否则<b>每结算一次大事件就破一次</b>。
/// ⇒ ⚠️ **D-101 之前这一项一直是 0**：大事件<b>从未真的结算过</b>（阶段机死锁）
/// ⇒ 这条规则<b>从来没被触发过</b>。</para>
/// </remarks>
public static class WorldEventResourceApplier
{
    /// <summary>
    /// 大事件带来的<b>外部金币</b>累计（进来的）。
    /// </summary>
    /// <remarks>
    /// ⚠️ **为什么需要它**（D-101）：大事件发的钱是<b>从外面进来的</b>
    /// ⇒ 它不在英雄钱包里、也不在「累计发放」里
    /// ⇒ 守恒律的等式<b>右边必须加上它</b>。
    /// </remarks>
    public static long TotalEventGoldIn { get; private set; }

    /// <summary>
    /// 大事件带走的<b>金币</b>累计（出去的：打输被抢的）。
    /// </summary>
    /// <remarks>
    /// ⚠️ 与 <see cref="TotalEventGoldIn"/> <b>分开记</b>（不是负数）：
    /// 「进来多少」与「出去多少」是<b>两件事</b> ⇒ 混成一个净值就看不出
    /// 大事件到底是净收入还是净损失。
    /// </remarks>
    public static long TotalEventGoldOut { get; private set; }

    /// <summary>
    /// 恢复这两个累计（<b>仅供读档</b>）。
    /// </summary>
    /// <param name="goldIn">累计进来的金币。</param>
    /// <param name="goldOut">累计带走的金币。</param>
    /// <exception cref="ArgumentOutOfRangeException">任一项为负。</exception>
    public static void RestoreTotals(long goldIn, long goldOut)
    {
        if (goldIn < 0) throw new ArgumentOutOfRangeException(nameof(goldIn), goldIn, "外部收入不能为负。");
        if (goldOut < 0) throw new ArgumentOutOfRangeException(nameof(goldOut), goldOut, "外部支出不能为负。");
        TotalEventGoldIn = goldIn;
        TotalEventGoldOut = goldOut;
    }

    /// <summary>这是不是<b>金币</b>资源（外部收入 / 支出只对金币记）。</summary>
    /// <param name="resource">资源种类。</param>
    /// <returns>是金币为 true。</returns>
    private static bool IsGold(ResourceId resource)
        => resource.Value == "金币";

    /// <summary>
    /// 把一条资源类后果应用到村庄的某个资源池。
    /// </summary>
    /// <param name="village">村庄（资源的唯一持有者）。</param>
    /// <param name="resource">资源身份（材料 / 金币）。</param>
    /// <param name="effect">结构化后果。</param>
    /// <param name="label">显示用的名字（「材料」/「金币」）。</param>
    /// <returns>实际落地的结果。</returns>
    /// <exception cref="ArgumentNullException"><paramref name="village"/> 为 null。</exception>
    public static ResourceChangeResult Apply(
        Village village, ResourceId resource, WorldEventEffect effect, string label)
    {
        if (village is null) throw new ArgumentNullException(nameof(village));
        if (effect is null) throw new ArgumentNullException(nameof(effect));

        if (effect.Amount > 0)
        {
            village.Add(resource, effect.Amount);
            // ⚠️ **D-101**：金币是「从外面进来的」⇒ 记进守恒律的「外部收入」科目
            // （否则等式右边少这一项 ⇒ 每结算一次大事件就破一次）。
            if (IsGold(resource)) TotalEventGoldIn += effect.Amount;
            return new ResourceChangeResult(effect.Amount, $"{label} +{effect.Amount}");
        }

        if (effect.Amount < 0)
        {
            long wanted = -effect.Amount;
            long actual = Math.Min(wanted, village.Get(resource));
            if (actual > 0) village.TrySpend(resource, actual);

            // ⚠️ **D-101**：被抢走的钱是「流出系统的」⇒ 记进「外部支出」科目
            // （与进来的分开记 ⇒ 净收支才看得出来）。
            if (IsGold(resource) && actual > 0) TotalEventGoldOut += actual;

            // ⚠️ **扣不动就只扣有的**（不欠账）：村库里没有「负债」这个状态 ⇒
            // 下一次结算才不会凭空扣掉负数，村库也永远不会被扣成负数。
            return actual < wanted
                ? new ResourceChangeResult(-actual, $"{label} -{actual}（库存不足，只扣了这些）")
                : new ResourceChangeResult(-actual, $"{label} -{actual}");
        }

        return new ResourceChangeResult(0, effect.Description);
    }
}
