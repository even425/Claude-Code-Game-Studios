using System;
using System.Collections.Generic;
using System.Linq;
using ClaudeCodeGameStudios.Domain.Village;

namespace ClaudeCodeGameStudios.Application;

/// <summary>
/// <b>层级附带建筑</b>的落地（D-099）—— 村庄升级时，那一级该有的建筑<b>自动出现</b>。
/// </summary>
/// <remarks>
/// <para><b>为什么有它</b>：基线 §12.1 的建筑名单里，<b>井</b>（「然后有井」）
/// 与村庄层级阶梯（… → <b>有井的村子</b> → …）是<b>同一套东西</b>
/// ⇒ 井天然是<b>层级附带</b>的：玩家升到那一级，它就在那儿了。</para>
///
/// <para>⚠️ <b>为什么不并进 <c>BuildableBuilding</c></b>：两者的<b>成本模型完全不同</b>
/// —— 可建建筑要钱、要工期、能选落点；层级附带建筑<b>不要钱、不要工期、落点固定</b>
/// ⇒ 混进同一个目录就会出现「玩家盖井要花钱，而升级又送一口井」这种自相矛盾。</para>
///
/// <para>⚠️ <b>幂等</b>：升级可能重入（读档后再升级）、也可能重复调用
/// ⇒ <b>已经有的就跳过</b> ⇒ 绝不出现两口井。</para>
/// </remarks>
public static class TierBuildingService
{
    /// <summary>
    /// 让村庄<b>补齐</b>到某一层级应有的层级附带建筑。
    /// </summary>
    /// <param name="village">村庄（会被写入）。</param>
    /// <param name="tier">村庄层级（0 起）。</param>
    /// <returns>这次**新放下的**建筑（已存在的不会重复报）。</returns>
    /// <remarks>
    /// ⚠️ <b>「补齐」而不是「放本级那一栋」</b>：升到第 3 级时，第 2 级那口井
    /// <b>还在</b> ⇒ 这里返回「到这一级为止应有的全部」里<b>缺的那些</b>。
    /// </remarks>
    public static IReadOnlyList<BuildingInstance> EnsureForTier(Village village, int tier)
    {
        if (village is null) throw new ArgumentNullException(nameof(village));

        var placed = new List<BuildingInstance>();
        foreach (var definition in TierBuildings.UpToTier(tier))
        {
            // ⚠️ **已经有的就跳过**（幂等）—— 升级可能重入、读档后也可能再调一次。
            if (village.Buildings.Any(b => b.Id.Value == definition.Id.Value)) continue;

            var instance = new BuildingInstance(
                definition.Id,
                definition.GridX, definition.GridY,
                definition.FootprintW, definition.FootprintH,
                constructionProgress: 1.0,      // ⚠️ **直接是建好的**（升级送的，不是施工的）
                affectsAreaW: definition.AffectsAreaW,
                affectsAreaH: definition.AffectsAreaH);

            if (!village.TryPlace(instance))
            {
                // ⚠️ **不静默**：放不下就是有问题（落点被别的建筑占了）。
                // 这里**不抛异常** —— 升级本身不该因此失败 ⇒ 由调用方打日志。
                continue;
            }

            placed.Add(instance);
        }

        return placed;
    }

    /// <summary>
    /// 这个村庄<b>缺</b>哪些层级附带建筑（只报缺的那些）。
    /// </summary>
    /// <param name="village">村庄。</param>
    /// <param name="tier">村庄层级。</param>
    /// <returns>缺失的层级附带建筑定义。</returns>
    /// <remarks>
    /// ⚠️ <b>为什么单独给一个查询</b>：守卫要能问「该有的都有吗」
    /// ⇒ 而生产代码与守卫必须问<b>同一份</b>（否则又是「测的是复制品」）。
    /// </remarks>
    public static IReadOnlyList<TierBuilding> MissingForTier(Village village, int tier)
    {
        if (village is null) throw new ArgumentNullException(nameof(village));

        return TierBuildings.UpToTier(tier)
            .Where(d => !village.Buildings.Any(b => b.Id.Value == d.Id.Value))
            .ToArray();
    }
}
