using System.Collections.Generic;

namespace ClaudeCodeGameStudios.Domain.Settlement;

/// <summary>
/// 副本战利品<b>内容的来源</b>（注入点）。
/// </summary>
/// <remarks>
/// <para><b>为什么把它单独立一个接口</b>：奖励<b>怎么分</b>是已定的规则（R-01 D01–D03，
/// 见 <see cref="LootSplit"/>），而奖励<b>是什么</b>是尚未拍板的内容规则
/// （掉落表、稀有度、掉落权重 —— 属 <c>07</c> 的 <b>T-007</b> / <c>R-05</c>）。
/// 把两者分开，T-007 拍板时只需换掉本接口的实现，分配规则一行都不用改。</para>
///
/// <para><b>它不负责</b>：不判断副本成败、不发物品、不碰背包。
/// 它只回答「这次副本产出了什么」。</para>
/// </remarks>
public interface ILootProvider
{
    /// <summary>
    /// 生成本次副本的战利品。
    /// </summary>
    /// <param name="dungeonName">副本名称。</param>
    /// <param name="amount">
    /// 本次副本的奖励规模。沿用任务表现有的份数口径，
    /// <b>不是</b>掉落概率也不是权重。
    /// </param>
    /// <returns>战利品清单。空清单表示这次副本没有物品产出（合法）。</returns>
    IReadOnlyList<LootStack> LootFor(string dungeonName, long amount);
}
