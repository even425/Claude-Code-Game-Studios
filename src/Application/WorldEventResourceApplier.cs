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
/// </remarks>
public static class WorldEventResourceApplier
{
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
            return new ResourceChangeResult(effect.Amount, $"{label} +{effect.Amount}");
        }

        if (effect.Amount < 0)
        {
            long wanted = -effect.Amount;
            long actual = Math.Min(wanted, village.Get(resource));
            if (actual > 0) village.TrySpend(resource, actual);

            // ⚠️ **扣不动就只扣有的**（不欠账）：村库里没有「负债」这个状态 ⇒
            // 下一次结算才不会凭空扣掉负数，村库也永远不会被扣成负数。
            return actual < wanted
                ? new ResourceChangeResult(-actual, $"{label} -{actual}（库存不足，只扣了这些）")
                : new ResourceChangeResult(-actual, $"{label} -{actual}");
        }

        return new ResourceChangeResult(0, effect.Description);
    }
}
