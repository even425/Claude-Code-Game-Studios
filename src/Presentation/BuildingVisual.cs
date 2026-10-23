using System;
using System.Collections.Generic;
using ClaudeCodeGameStudios.Domain.Village;

namespace ClaudeCodeGameStudios.Presentation;

/// <summary>
/// 建筑在地图上的<b>画法</b>（D-097）—— 一栋建筑长什么样。
/// </summary>
/// <remarks>
/// <para><b>为什么要有它</b>：D-093 让玩家自己选落点，地图上有了「标记」；
/// 但那三种建筑<b>都画成同一块色块</b> ⇒ 玩家在地图上看不出自己盖的是围栏还是试炼塔
/// ⇒ 「看得见自己盖了什么」这件事<b>是假的</b>。</para>
///
/// <para>⚠️ <b>这个映射放在纯 C# 里（不在 Godot 节点里）</b>：
/// 这样「**每一栋建筑都必须有对应的画法**」才能被<b>离线单测</b>守住
/// ⇒ 将来加一栋新建筑却忘了给它画法 ⇒ 立刻红（而不是安静地画成一块石台）。</para>
///
/// <para>⚠️ <b>「通用石台」是兜底，不是默认</b>：它只在<b>真的不认识</b>时用，
/// 而「不认识」本身就该让守卫红。</para>
///
/// <para>⚠️ <b>D-099：两类建筑共用这张表</b>——<b>可建建筑</b>（玩家花钱盖的）
/// 与<b>层级附带建筑</b>（升级送）都从这里取画法 ⇒ 新增建筑只改这一处。</para>
/// </remarks>
public static class BuildingVisual
{
    /// <summary>围栏：一圈木栅栏（圈的是地）。</summary>
    public const string Fence = "fence";

    /// <summary>试炼塔：一座石塔（顶上挂旗）。</summary>
    public const string TrialTower = "trial_tower";

    /// <summary>防护罩：一层光罩。</summary>
    public const string Shield = "shield";

    /// <summary>井（D-099：层级附带建筑）：一口带辘轳的石井。</summary>
    public const string Well = "well";

    /// <summary>铁匠铺（D-107：层级附带建筑）：一间带炉火的石屋。</summary>
    public const string Blacksmith = "blacksmith";

    /// <summary>兜底：一块矮石台（**只在不认识时用** ⇒ 守卫会红）。</summary>
    public const string Generic = "generic";

    /// <summary>全部已知的画法。</summary>
    public static readonly IReadOnlyList<string> All =
        new[] { Fence, TrialTower, Shield, Well, Blacksmith, Generic };

    /// <summary>
    /// 这栋建筑该画成什么样。
    /// </summary>
    /// <param name="building">可建建筑的定义。</param>
    /// <returns>画法标识（<see cref="All"/> 里的一个）。</returns>
    /// <remarks>
    /// ⚠️ **按「效果」而不是按名字判**：名字可能被改，效果不会
    /// （效果是规则，名字是显示）。
    /// </remarks>
    public static string ShapeFor(BuildableBuilding building)
        => building.Effect switch
        {
            BuildingEffect.Range => Fence,
            BuildingEffect.TrialTower => TrialTower,
            BuildingEffect.Shield => Shield,
            _ => Generic,
        };

    /// <summary>
    /// 按<b>建筑标识</b>取画法 —— <b>两类建筑都走这里</b>（D-099）。
    /// </summary>
    /// <param name="id">建筑标识。</param>
    /// <returns>画法标识（<see cref="All"/> 里的一个）。</returns>
    /// <remarks>
    /// ⚠️ <b>层级附带建筑</b>（井等）不在可建目录里 ⇒ 单独查它那份目录。
    /// </remarks>
    public static string ShapeForId(BuildingId id)
    {
        var tierBuilding = TierBuildings.Find(id);
        if (tierBuilding is not null)
        {
            // ⚠️ 层级附带建筑**各有各的画法**（D-097 的规矩：长得不一样才认得出）。
            if (tierBuilding.Value.Id.Value == TierBuildings.Well.Id.Value) return Well;
            if (tierBuilding.Value.Id.Value == TierBuildings.Blacksmith.Id.Value) return Blacksmith;
            return Generic;
        }

        var buildable = BuildableBuildings.Find(id);
        return buildable is null ? Generic : ShapeFor(buildable.Value);
    }

    /// <summary>
    /// 这个画法是不是「真的认识」（<b>不是兜底</b>）。
    /// </summary>
    /// <param name="shape">画法标识。</param>
    /// <returns>认识为 true；落到兜底为 false。</returns>
    public static bool IsKnown(string shape)
        => shape == Fence || shape == TrialTower || shape == Shield || shape == Well
           || shape == Blacksmith;
}
