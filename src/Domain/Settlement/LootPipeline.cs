using System.Collections.Generic;
using ClaudeCodeGameStudios.Domain.Heroes;

namespace ClaudeCodeGameStudios.Domain.Settlement;

/// <summary>
/// 副本战利品的<b>整条管线</b>：派生子流 → 抽取 → 分配（R-05）。
/// </summary>
/// <remarks>
/// <para><b>为什么把管线抽成一个函数</b>：抽取（R-05）与分配（R-01）<b>共用同一条随机子流</b>，
/// 顺序固定为「先抽后分」。这个接缝是整条链里<b>唯一一处</b>
/// 「改了一处、另一处跟着变」的地方：
/// 抽取多抽一次、调换顺序、权重变了 —— 都会<b>连锁</b>改变分配结果。</para>
///
/// <para><b>不抽出来会怎样</b>：Main 里那几行代码就成了「没人能测的地方」。
/// 测试若自己复制一份管线来测，<b>盯不住 Main 的接线</b> ——
/// 哪天有人把 Main 里那两行调换，测试照样全绿，而整个副本的战利品已经悄悄变了。
/// 抽成函数之后，测试与 Main <b>调的是同一段代码</b>，改动必然被测出来。</para>
///
/// <para><b>可复现性从哪来</b>：种子由<b>副本实例标识</b>用 FNV-1a 派生
/// （<see cref="LootSplit.DeriveSeed"/>）——
/// 同一副本永远得到同一份掉落与分配，读档重放一致；
/// 且走<b>独立子流</b>，掉落<b>不插队</b>进战斗与日常共用的全局随机流。</para>
/// </remarks>
public static class LootPipeline
{
    /// <summary>
    /// 跑一次完整的战利品管线。
    /// </summary>
    /// <param name="provider">战利品内容来源（注入点；决定抽什么）。</param>
    /// <param name="dungeonName">副本名称。</param>
    /// <param name="participants">实际参战名单（R-01 的资格依据）。</param>
    /// <param name="runId">副本实例标识（种子由它派生）。</param>
    /// <returns>战利品分配结果；无掉落或无参战者时为空分配。</returns>
    /// <remarks>
    /// <b>顺序不能换</b>：先抽取、后分配。抽取消耗掉的随机次数决定了分配从流的哪一段开始 ——
    /// 换了顺序，分配结果就变了（而这不会以任何形式报错）。
    /// </remarks>
    public static LootAllocation Run(
        ILootProvider provider,
        string dungeonName,
        IReadOnlyList<HeroId> participants,
        string runId)
    {
        var rng = new DecisionFlow.SeededRandom(LootSplit.DeriveSeed(runId));

        // 先抽：消耗 draws 次（外加数量区间的随机）。
        var loot = provider.LootFor(dungeonName, rng);
        if (loot is null || loot.Count == 0) return LootAllocation.Empty;

        // 后分：接着同一条流走 —— 余数分配用的正是抽取剩下的那段随机。
        return LootSplit.Split(loot, participants, rng);
    }
}
