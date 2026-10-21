using System;
using System.Collections.Generic;
using System.Linq;

namespace ClaudeCodeGameStudios.Domain.Village;

/// <summary>一栋<b>可建建筑</b>的定义 —— 建造流程的规则来源（D-074）。</summary>
/// <remarks>
/// <para><b>它是什么</b>：建筑「叫什么 / 要花多少 / 占地多大 / 盖好之后有什么用」
/// 这份<b>静态规则</b>。它<b>不持有状态</b>（建到哪儿了是 <see cref="BuildingInstance"/> 的事）。</para>
/// <para><b>为什么需要它</b>：D-066 裁决「七栋不做、两栋落地」（围栏 = 范围、
/// 试炼塔 = 属性加成）⇒ 两栋的规则得有个<b>单一真源</b>，
/// 否则 UI 显示的、扣的钱、占的地、给的效果会各说各话。</para>
/// <para><b>不变量 I7</b>：静态定义<b>不可</b>被运行时写回
/// ⇒ 这些都是只读属性，运行期只能读。</para>
/// </remarks>
/// <param name="Id">建筑标识。</param>
/// <param name="Name">显示名。</param>
/// <param name="GoldCost">建造成本（金币）。</param>
/// <param name="MaterialCost">建造成本（材料）。</param>
/// <param name="BaseBuildHours">
/// <b>一个人盖要多久</b>（游戏内小时）。工期 = 本值 ÷ 参与施工的人数（D-074）。
/// </param>
/// <param name="FootprintW">占地格宽。</param>
/// <param name="FootprintH">占地格高。</param>
/// <param name="AffectsAreaW">影响区 X 方向半径（0 = 不影响任何地方）。</param>
/// <param name="AffectsAreaH">影响区 Y 方向半径（0 = 不影响任何地方）。</param>
/// <param name="Effect">建好之后的效果。</param>
public readonly record struct BuildableBuilding(
    BuildingId Id,
    string Name,
    long GoldCost,
    long MaterialCost,
    double BaseBuildHours,
    int FootprintW,
    int FootprintH,
    int AffectsAreaW,
    int AffectsAreaH,
    BuildingEffect Effect)
{
    /// <summary>占地不与已有建筑重叠时用的默认格位（村庄中心偏北）。</summary>
    /// <remarks>
    /// ⚠️ **这是一个占位落点**：第一版不做「玩家选位置」
    /// （基线 §12.3 说建筑与村庄的关系【待定】，而选位置会牵出一整套放置规则）。
    /// ⇒ 两栋建筑都落在**固定格位**，且与市集错开。
    /// </remarks>
    public int GridX { get; init; } = -3;

    /// <summary>默认落点的 Y（见 <see cref="GridX"/> 的说明）。</summary>
    public int GridY { get; init; } = -3;
}

/// <summary>建筑建成后的效果（D-074）。</summary>
public enum BuildingEffect
{
    /// <summary>无特殊效果（只是个范围载体）。</summary>
    None = 0,

    /// <summary>
    /// <b>范围</b>：圈出一片可用范围（摆摊位置必须落在其中）。
    /// </summary>
    /// <remarks>
    /// D-066 / D-074：这是**已存在的概念**（基线 §12.4 + 摆摊位置守门）
    /// ⇒ 不新造机制，只是把「市集的范围」扩到「围栏的范围」。
    /// </remarks>
    Range = 1,

    /// <summary>
    /// <b>试炼塔</b>：进去打一场，赢了给英雄<b>永久</b>加生命（D-074）。
    /// </summary>
    /// <remarks>
    /// ⚠️ **只加生命**（D-074）：英雄**没有经验 / 等级**
    /// （`Village` 刻意不含 tier）⇒「升级」只能是属性变强；
    /// 而加攻防会直接改战斗胜率（首领档 86.7% 得重测）⇒ 先只动生命。
    /// </remarks>
    TrialTower = 2,
}

/// <summary>
/// 可建建筑的<b>目录</b> —— D-074 的单一真源。
/// </summary>
/// <remarks>
/// <para><b>为什么只有两栋</b>：D-066 已裁决九栋里的七栋<b>暂不做</b>
/// （人口 / 采集点 / 装备三个大概念挡在门外）⇒ 只剩围栏与试炼塔可建。
/// <para>⚠️ <b>基线 §12.3 原文</b>：「不能仅凭建筑名称自行指定功能」
/// ⇒ 本目录里的每一栋的<b>功能都来自开发者拍板</b>（D-066 / D-074），
/// 不是实现者按名字推的。</para>
/// <para><b>为什么在领域层</b>：它是<b>规则</b>（要花多少、盖多久、有什么用），
/// 与 Godot 无关 ⇒ 可离线单测。</para>
/// </remarks>
public static class BuildableBuildings
{
    /// <summary>围栏：圈出摆摊可用范围（D-066 B4 / D-074）。</summary>
    public static readonly BuildableBuilding Fence = new(
        new BuildingId("fence"),
        "围栏",
        GoldCost: 80L,
        MaterialCost: 40L,
        BaseBuildHours: 8.0,
        FootprintW: 1,
        FootprintH: 1,
        AffectsAreaW: 9,
        AffectsAreaH: 9,
        Effect: BuildingEffect.Range)
    {
        GridX = -3,
        GridY = -3,
    };

    /// <summary>试炼塔：进去打一场，赢了永久加生命（D-066 B8 / D-074）。</summary>
    public static readonly BuildableBuilding TrialTower = new(
        new BuildingId("trial_tower"),
        "试炼塔",
        GoldCost: 150L,
        MaterialCost: 70L,
        BaseBuildHours: 12.0,
        FootprintW: 1,
        FootprintH: 1,
        AffectsAreaW: 0,
        AffectsAreaH: 0,
        Effect: BuildingEffect.TrialTower)
    {
        GridX = -1,
        GridY = -3,
    };

    /// <summary>全部可建建筑。</summary>
    public static IReadOnlyList<BuildableBuilding> All { get; } =
        new[] { Fence, TrialTower };

    /// <summary>按标识查一栋可建建筑。</summary>
    /// <param name="id">建筑标识。</param>
    /// <returns>找不到时返回 null（不是默认实例 —— 默认实例的 Id 是空的，会被误当成真建筑）。</returns>
    public static BuildableBuilding? Find(BuildingId id)
    {
        foreach (var b in All)
        {
            if (b.Id.Value == id.Value) return b;
        }

        return null;
    }
}
