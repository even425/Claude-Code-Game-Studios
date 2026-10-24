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

    /// <summary>
    /// <b>防护罩</b>：削弱来扰的实际攻击（D-095）。
    /// </summary>
    /// <remarks>
    /// ⚠️ <b>基线原话</b>：「还有防护罩，作用是保护村庄不被外面攻击」（§12.1）。
    /// ⚠️ <b>为什么不是「大屏防护」</b>：当前游戏里没有「被攻击的东西」可以被罩住
    /// （兽潮输赢损的是资源，人畜无损）⇒ <b>第一版采用「最小可测版」</b>：
    /// 建成后 <b>兽潮规模降一级</b>（与围栏可叠加）。
    /// ⇒ 「罩住谁」留到下一版（当村庄有了更多可被攻击的东西再定）。
    /// </remarks>
    Shield = 3,

    /// <summary>
    /// <b>采集点</b>：让采集的人多采到材料（D-105，开发者拍板「井 = 采集点」）。
    /// </summary>
    /// <remarks>
    /// ⚠️ <b>基线原话</b>：井是「然后有井」那一级自然带出来的建筑（§12.1），
    /// 而开发者拍板它<b>是采集点</b> ⇒ 「打井 ⇒ 村里有水 ⇒ 采得到更多」。
    /// <br>⚠️ <b>为什么加在枚举里而不是另开一个字符串</b>：效果是<b>规则</b>，
    /// 而规则要能被一处判、几处读（D-097 的画法映射、D-098 的范围类判定都是这个道理）。
    /// </remarks>
    Gathering = 4,

    /// <summary>
    /// <b>武器店</b>：卖武器给英雄（D-119，开发者拍板「武器店 + 服装店」）。
    /// </summary>
    /// <remarks>
    /// ⚠️ **玩家可建**（开发者拍板「花钱 + 材料自己盖」）⇒ 它在
    /// <see cref="BuildableBuildings"/> 目录里，与围栏 / 试炼塔 / 防护罩同款。
    /// </remarks>
    WeaponShop = 5,

    /// <summary>
    /// <b>服装店</b>：卖衣服给英雄（D-119，开发者拍板「武器店 + 服装店」）。
    /// </summary>
    ArmorShop = 6,

    /// <summary>
    /// <b>修理</b>：把坏了的装备修好（D-119，开发者拍板「铁匠铺 = 修理」）。
    /// </summary>
    /// <remarks>
    /// ⚠️ 铁匠铺<b>已经</b>是采集点（D-107）⇒ 一栋建筑要同时有两种效果
    /// ⇒ 层级附带建筑用<b>第二个效果字段</b>承载（<see cref="TierBuilding.SecondaryEffect"/>），
    /// 可建建筑仍是一效果一栋。
    /// </remarks>
    Repair = 7,
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

    /// <summary>
    /// 防护罩：削弱来扰的实际攻击（D-095）。
    /// </summary>
    /// <remarks>
    /// ⚠️ <b>数值是「建议值」</b>（开发者已拍板「最小可测版」，数值允许调）：
    /// 120 金 / 60 材料 / 10 工时。
    /// ⚠️ 影响区 9×9：与围栏同档 ⇒ 地图上能看懂「它罩到哪儿」（D-093 的标记机制）。
    /// </remarks>
    public static readonly BuildableBuilding Shield = new(
        new BuildingId("shield"),
        "防护罩",
        GoldCost: 120L,
        MaterialCost: 60L,
        BaseBuildHours: 10.0,
        FootprintW: 1,
        FootprintH: 1,
        AffectsAreaW: 9,
        AffectsAreaH: 9,
        Effect: BuildingEffect.Shield);

    /// <summary>
    /// <b>武器店</b>（D-119，开发者拍板「玩家可建：花钱 + 材料自己盖」）。
    /// </summary>
    /// <remarks>
    /// ⚠️ <b>数值是建议值</b>（允许调）：100 金 / 50 材料 / 10 工时。
    /// ⚠️ 影响区 0×0：商店<b>不</b>扩大村子的地盘（它不是范围类建筑 ⇒ D-098 同款）。
    /// </remarks>
    public static readonly BuildableBuilding WeaponShop = new(
        new BuildingId("weapon_shop"),
        "武器店",
        GoldCost: 100L,
        MaterialCost: 50L,
        BaseBuildHours: 10.0,
        FootprintW: 1,
        FootprintH: 1,
        AffectsAreaW: 0,
        AffectsAreaH: 0,
        Effect: BuildingEffect.WeaponShop)
    {
        // ⚠️ **D-122：默认落点必须在市集的影响区里**（市集 (-5,-5)、11×11 ⇒ 覆盖 [-10,0]×[-10,0]）
        // ⇒ 否则采集器（不选点，直接用默认落点）与玩家「不选点直接放」都会被落点规则拒。
        GridX = 0,
        GridY = -4,
    };

    /// <summary>
    /// <b>服装店</b>（D-119，开发者拍板「玩家可建」）。
    /// </summary>
    /// <remarks>
    /// ⚠️ <b>数值是建议值</b>（允许调）：80 金 / 40 材料 / 8 工时。
    /// </remarks>
    public static readonly BuildableBuilding ArmorShop = new(
        new BuildingId("armor_shop"),
        "服装店",
        GoldCost: 80L,
        MaterialCost: 40L,
        BaseBuildHours: 8.0,
        FootprintW: 1,
        FootprintH: 1,
        AffectsAreaW: 0,
        AffectsAreaH: 0,
        Effect: BuildingEffect.ArmorShop)
    {
        // ⚠️ **D-122：同武器店**（在市集影响区内，且不与围栏 (-3,-3) / 试炼塔 (-1,-3) 重叠）。
        GridX = -2,
        GridY = -5,
    };

    /// <summary>
    /// 全部可建建筑。
    /// </summary>
    /// <remarks>
    /// ⚠️ **必须声明在各个建筑之后**（D-119 踩过的坑）：
    /// 静态字段按**声明顺序**初始化 ⇒ 若 <c>All</c> 写在 <c>WeaponShop</c> / <c>ArmorShop</c>
    /// <b>前面</b>，它拿到的就是两个<b>还没初始化的空实例</b>（Id 为空）
    /// ⇒ 目录里凭空多出两栋「没有名字的建筑」⇒ 守卫（每栋都要有画法）当场红。
    /// </remarks>
    public static IReadOnlyList<BuildableBuilding> All { get; } =
        new[] { Fence, TrialTower, Shield, WeaponShop, ArmorShop };

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
