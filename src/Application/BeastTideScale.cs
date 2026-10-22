using System;
using ClaudeCodeGameStudios.Domain.Combat;

namespace ClaudeCodeGameStudios.Application;

/// <summary>
/// <b>兽潮的规模缩放</b>（D-080 拍板：<b>兽潮随村庄层级递增</b>）。
/// </summary>
/// <remarks>
/// <para>⚠️ <b>为什么放在应用层而不是 Main 里</b>：缩放是<b>规则</b>，
/// 而 Main 是表现层 ⇒ 规则写在表现层里就<b>没法被守卫直接验</b>。
/// ⇒ 这里放成纯函数，Main 只负责把「当前层级」和「每级百分比」传进来。</para>
///
/// <para>⚠️ <b>只缩放属性，不改怪的档位</b>（普通 / 精英 / 首领）⇒
/// 兽潮的「形状」不变，只是更强 ⇒ 与「规模递增」而不是「换一种怪」相符。</para>
/// </remarks>
public static class BeastTideScale
{
    /// <summary>
    /// 兽潮的<b>实际规模等级</b>：村庄层级 − 围栏数（D-085 拍板「围栏 ⇒ 兽潮规模 −1 级」）。
    /// </summary>
    /// <param name="villageTier">村庄层级（0 起）。</param>
    /// <param name="completedFences">已建成的围栏数。</param>
    /// <returns>实际规模等级（<b>最低 0</b> —— 围栏再多也不会让兽潮变成「负级」）。</returns>
    /// <remarks>
    /// ⚠️ **为什么围栏能挡兽潮**（D-085 拍板）：兽潮的规模随村庄层级增长，
    /// 而**围栏是村庄唯一的防御建筑** ⇒ 「筑墙 ⇒ 兽潮弱一级」把防御设施变成了
    /// 一条**可经营的策略**，也让基线 §14.3 那条验收重新有了可观察的指标
    /// （防御设施改变了结算结果）。
    /// <para>⚠️ <b>最低 0</b>：围栏不能把兽潮「减成负数」⇒ 层级 0 的村庄
    /// 有一座围栏也只是**保持 0 级**，不是「更弱的兽潮」。</para>
    /// </remarks>
    public static int TierWithDefenses(int villageTier, int completedFences)
    {
        if (completedFences < 0) throw new ArgumentOutOfRangeException(nameof(completedFences));
        int tier = villageTier - completedFences;
        return tier < 0 ? 0 : tier;
    }

    /// <summary>
    /// 村里有几座<b>已建成</b>的围栏（在建的不算 —— 半成品墙挡不住兽潮）。
    /// </summary>
    /// <param name="village">村庄。</param>
    /// <returns>围栏数。</returns>
    public static int CompletedFenceCount(
        ClaudeCodeGameStudios.Domain.Village.Village village)
    {
        if (village is null) throw new ArgumentNullException(nameof(village));

        string fenceId = ClaudeCodeGameStudios.Domain.Village.BuildableBuildings.Fence.Id.Value;
        int count = 0;
        foreach (var b in village.Buildings)
        {
            if (b.Id.Value != fenceId) continue;
            // 建造进度到 1 才算建成（D-074：建成后才生效）。
            if (b.ConstructionProgress >= 1.0) count++;
        }

        return count;
    }

    /// <summary>
    /// 按村庄层级把某阶段的怪物属性放大。
    /// </summary>
    /// <param name="baseStats">该阶段的基础属性（档位表里的值）。</param>
    /// <param name="tier">村庄层级（0 起；≤ 0 = 不放大）。</param>
    /// <param name="percentPerTier">每级放大多少个百分点（15 = 每级 +15%）。</param>
    /// <returns>放大后的属性。</returns>
    /// <remarks>
    /// ⚠️ **用整数百分比算，不用浮点**：浮点会算出 6.000000000000001 这种属性，
    /// 而战斗属性是 int ⇒ 转换时的舍入会让「放大 15%」在不同档位上表现不一致。
    /// </remarks>
    public static CombatStats ScaleMonster(CombatStats baseStats, int tier, int percentPerTier)
    {
        if (tier <= 0) return baseStats;

        long percent = 100L + (long)tier * percentPerTier;
        long attack = baseStats.Attack * percent / 100;
        long defense = baseStats.Defense * percent / 100;
        long health = baseStats.Health * percent / 100;

        // ⚠️ 至少 1：属性为 0 = 「状态不明」⇒ 战斗引擎会大声失败。
        return new CombatStats(
            (int)Math.Max(1, attack),
            (int)Math.Max(1, defense),
            (int)Math.Max(1, health));
    }
}
