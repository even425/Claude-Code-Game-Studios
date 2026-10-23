using System;
using System.Collections.Generic;
using System.Linq;
using ClaudeCodeGameStudios.Domain.Village;

namespace ClaudeCodeGameStudios.Application;

/// <summary>采集器的一次<b>盖房计划</b>（D-108）。</summary>
/// <param name="Definition">要盖什么。</param>
/// <param name="GridX">落点 X。</param>
/// <param name="GridY">落点 Y。</param>
public readonly record struct CollectorBuildPlan(BuildableBuilding Definition, int GridX, int GridY);

/// <summary>
/// <b>采集器今天要不要盖房子、盖哪栋、盖在哪</b>（D-108）。
/// </summary>
/// <remarks>
/// <para><b>为什么要有它</b>：采集器此前**只升级村庄**（5 级、750 金），
/// 而**从不盖房子、从不召唤** ⇒ 村库的钱<b>只进不出</b>
/// ⇒ 标定出来的「钱在涨」<b>有很大一部分是采集器的失真</b>，不是游戏的病
/// （见 <c>07</c> 的 D-106）。</para>
///
/// <para>⚠️ <b>为什么规则住在应用层</b>：这段决策原先会写在 <c>Main</c>（表现层）
/// ⇒ 单测碰不到 ⇒ 改坏了照样绿 ⇒ 搬到这里，<b>生产代码与守卫调同一份</b>
/// （与 D-099 的 <c>TierBuildingService</c>、D-083 的 <c>BuildingPersistence</c> 同款理由）。</para>
///
/// <para><b>三条规则</b>：
/// <list type="number">
/// <item><b>升级优先</b>：村庄升级在施工时<b>不盖房子</b>（两条路互斥 —— 施工队是同一批人，
/// D-074 的老规矩）。</item>
/// <item><b>盖完还得买得起下一级升级</b>：否则房子会把村庄的钱吃光、
/// 村庄<b>永远升不上去</b> ⇒ 那比「钱囤着」更糟（层级附带建筑也就不会出现）。</item>
/// <item><b>围栏与试炼塔轮着盖</b>（谁盖得少盖谁）：两栋都有用 ——
/// 围栏扩大摆摊范围，试炼塔是英雄变强的入口 ⇒ 只盖一种会偏科。</item>
/// </list></para>
/// </remarks>
public static class CollectorBuildingPlan
{
    /// <summary>
    /// 今天的盖房计划（<b>没有</b> ⇒ null）。
    /// </summary>
    /// <param name="villageGold">村库当前金币。</param>
    /// <param name="nextUpgradeGoldCost">下一级村庄升级要多少金币（0 = 已满级，不再需要留钱）。</param>
    /// <param name="candidates">候选建筑（按「谁盖得少盖谁」排序，不需要调用方排好）。</param>
    /// <param name="existingCounts">每种建筑已经盖了多少栋（按建筑标识）。</param>
    /// <param name="canAfford">钱与材料够不够（规则住在 <c>BuildingService</c>）。</param>
    /// <param name="findSpot">找一个合法落点（规则住在 <c>BuildingService.CanPlaceAt</c>）。</param>
    /// <returns>计划；不该盖 / 盖不了时返回 null。</returns>
    public static CollectorBuildPlan? Plan(
        long villageGold,
        long nextUpgradeGoldCost,
        IReadOnlyList<BuildableBuilding> candidates,
        IReadOnlyDictionary<string, int> existingCounts,
        Func<BuildableBuilding, bool> canAfford,
        Func<BuildableBuilding, (int X, int Y)?> findSpot)
    {
        if (candidates is null || candidates.Count == 0) return null;

        // ⚠️ **围栏与试炼塔轮着盖**：谁盖得少盖谁（一样多 ⇒ 按候选表顺序）。
        var ordered = candidates
            .OrderBy(c => existingCounts.TryGetValue(c.Id.Value, out int n) ? n : 0)
            .ToArray();

        foreach (var definition in ordered)
        {
            if (!canAfford(definition)) continue;

            // ⚠️ **盖完还得买得起下一级升级** ⇒ 否则村庄会被房子拖住、永远升不上去。
            if (villageGold - definition.GoldCost < nextUpgradeGoldCost) continue;

            var spot = findSpot(definition);
            if (spot is null) continue;      // 没地方盖（地图满了 / 全被占）

            return new CollectorBuildPlan(definition, spot.Value.X, spot.Value.Y);
        }

        return null;
    }
}
