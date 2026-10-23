using System;
using System.Collections.Generic;
using System.Linq;
using ClaudeCodeGameStudios.Domain.Village;

namespace ClaudeCodeGameStudios.Domain.Village;

/// <summary>
/// <b>哪些建筑会放开「摆摊范围」</b>（D-098）。
/// </summary>
/// <remarks>
/// <para><b>为什么要有这一层</b>：摆摊位置守门（D-033）原先只问
/// 「这格落在<b>任何一个</b>已建成建筑的影响区里吗」⇒ 守门<b>不区分建筑种类</b>
/// ⇒ 防护罩（D-095 加的）也顺带放开了摆摊范围。</para>
///
/// <para>⚠️ <b>那与两条已确认的规则都冲突</b>：
/// <list type="bullet">
/// <item>基线 §12.4（已确认）：「摆摊位置受<b>防护罩</b>范围约束」⇒ 防护罩<b>该</b>管摆摊范围。</item>
/// <item>D-095（已确认）：「围栏 = <b>范围建筑</b>；防护罩 = <b>专职防御建筑</b> ⇒ 各降一级、各降各的」
/// ⇒ 防护罩<b>不该</b>管摆摊范围。</item>
/// </list></para>
///
/// <para>⇒ <b>开发者拍板（D-098）选 B</b>：摆摊只认<b>「范围类」建筑</b>
/// （围栏 + 开局的市集），<b>防护罩不参与摆摊范围</b>。
/// ⇒ 基线 §12.4 的执行方式随之明确为：<b>「防护罩 / 围栏这类范围建筑共用同一个『范围』原语，
/// 摆摊落在任一范围建筑内即可」</b>；而防护罩当前<b>只</b>做防御（兽潮规模 −1 级）。</para>
///
/// <para>⚠️ <b>判据是「效果」不是「名字」</b>：名字是显示、会被改；
/// 效果是规则、不会变（与 D-097 的画法映射同一原则）。</para>
///
/// <para>⚠️ <b>目录里查不到的建筑算「范围类」</b>：市集（开局就有）与
/// 一些历史建筑都<b>不在</b> <see cref="BuildableBuildings"/> 目录里（玩家不能建它们）
/// ⇒ 但它们<b>就是</b>范围建筑（整个村子的地盘靠市集）⇒ 单列一条。
/// <br>⇒ 反过来说：<b>只有目录里明确写着「不是范围」的那种</b>（防护罩 / 试炼塔）
/// 才<b>不</b>参与摆摊范围。</para>
/// </remarks>
public static class RangeBuildings
{
    /// <summary>
    /// 开局市集的建筑标识（D-033 开局就有的那一个）。
    /// </summary>
    /// <remarks>
    /// ⚠️ 它<b>不在</b> <see cref="BuildableBuildings"/> 目录里（玩家不能建它）
    /// ⇒ 但它是<b>范围类</b>（整个村子的地盘都靠它）⇒ 单列一条。
    /// </remarks>
    public const string MarketId = "village_square";

    /// <summary>
    /// 这栋建筑<b>是不是「范围类」</b>（会放开摆摊范围）。
    /// </summary>
    /// <param name="id">建筑标识。</param>
    /// <returns>是范围类为 true。</returns>
    /// <remarks>
    /// ⚠️ <b>查不到 ⇒ 算范围类</b>：市集与历史建筑不在可建目录里，
    /// 可它们**确实**有影响区、**确实**在圈地 ⇒ 不能因为「查不到」就把它们踢出去
    /// （那会让开局一个摊位都摆不了）。
    /// <br>⇒ 只有<b>目录里明确标着别的效果</b>（防护罩 = 防御、试炼塔 = 加生命）才排除。
    /// </remarks>
    public static bool IsRangeBuilding(BuildingId id)
    {
        // ⚠️ **层级附带建筑**（D-099：井等）**不是范围类**：
        // 它们是「升级送的地标」，不圈摆摊范围。
        if (TierBuildings.Find(id) is not null) return false;

        var definition = BuildableBuildings.Find(id);
        if (definition is null) return true;   // 目录外 ⇒ 范围类（市集 / 历史建筑）
        return definition.Value.Effect == BuildingEffect.Range;
    }

    /// <summary>
    /// 这个实例是不是「范围类」。
    /// </summary>
    /// <param name="building">建筑实例。</param>
    /// <returns>是范围类为 true。</returns>
    public static bool IsRangeBuilding(BuildingInstance building)
        => building is not null && IsRangeBuilding(building.Id);
}
