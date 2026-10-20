using System.Collections.Generic;
using ClaudeCodeGameStudios.Domain.Heroes;

namespace ClaudeCodeGameStudios.Domain.Settlement;

/// <summary>
/// 副本战利品<b>内容的来源</b>（注入点，R-05 改）。
/// </summary>
/// <remarks>
/// <para><b>为什么把它单独立一个接口</b>：奖励<b>怎么分</b>是已定的规则（R-01 D01–D03，
/// 见 <see cref="LootSplit"/>），奖励<b>是什么</b>是另一套规则（R-05 的掉落表）。
/// 把两者分开，改掉落表时分配规则一行都不用改。</para>
///
/// <para><b>它不负责</b>：不判断副本成败、不发物品、不碰背包。
/// 它只回答「这次副本产出了什么」。</para>
///
/// <para><b>随机源是入参，不是字段</b>：抽取要可复现，就必须由调用方
/// <b>把随机流递进来</b>。本接口<b>不自己 new 一个随机源</b> ——
/// 那样每次调用都从同一起点开始，重复结算会得到同样的掉落（听起来像好事，
/// 实际会让「同一次副本抽到不同结果」这种语义消失，而且读档重放无从谈起）。
/// 调用方用<b>由副本实例标识派生的独立子流</b>（见 <see cref="LootSplit.DeriveSeed"/>），
/// 同一副本永远得到同一份掉落，且掉落<b>不插队</b>进战斗与日常共用的全局随机流。</para>
/// </remarks>
public interface ILootProvider
{
    /// <summary>
    /// 生成本次副本的战利品。
    /// </summary>
    /// <param name="dungeonName">副本名称。</param>
    /// <param name="rng">随机源（由调用方按副本实例派生）。<b>不得为 null</b>。</param>
    /// <returns>战利品清单。空清单表示这次副本没有物品产出（合法）。</returns>
    IReadOnlyList<LootStack> LootFor(string dungeonName, IRandomSource rng);
}
