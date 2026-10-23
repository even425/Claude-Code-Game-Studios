using System;
using System.Collections.Generic;
using System.Linq;

namespace ClaudeCodeGameStudios.Domain.Village;

/// <summary>
/// 一栋<b>层级附带建筑</b>的定义 —— 升到某个村庄层级时<b>自动出现</b>的那类建筑（D-099）。
/// </summary>
/// <remarks>
/// <para><b>为什么有它</b>：基线 §12.1 的建筑名单里，<b>井</b>（「然后有井」）
/// 与村庄层级阶梯（破落户 → 有人来往的村子 → <b>有井的村子</b> → 有围墙的村子 →
/// 铁匠铺 → 王国第一的城）是<b>同一套东西</b> ⇒ 「井」天然是<b>层级附带</b>的，
/// 不是玩家单独去建的一栋。</para>
///
/// <para>⚠️ <b>本类只管「定义」</b>（叫什么、哪个层级、放在哪、影响区多大），
/// <b>不持有状态</b>（村里到底有没有那口井，是 <see cref="Village"/> 的事）。
/// ⇒ 与 <see cref="BuildableBuilding"/> 同款分层：定义在领域层，状态在村庄里。</para>
///
/// <para>⚠️ <b>与「可建建筑」的区别</b>：
/// <list type="bullet">
/// <item><b>可建建筑</b>（<see cref="BuildableBuildings"/>）：玩家<b>花钱自己建</b>，
/// 有成本、有工期、能选落点。</item>
/// <item><b>层级附带建筑</b>（本类）：村庄<b>升级</b>时<b>自动出现</b>，
/// <b>不要钱、不要工期、落点固定</b> ⇒ 玩家不能拒绝、也不能挑地方。</item>
/// </list>
/// ⇒ 两者的**成本模型完全不同** ⇒ 不许混进同一个目录（混了就会出现「玩家盖井要钱、
/// 而升级又送一口井」这种自相矛盾）。</para>
/// </remarks>
/// <param name="Id">建筑标识。</param>
/// <param name="Name">显示名。</param>
/// <param name="Tier">升到第几级时出现（村庄层级 0 起）。</param>
/// <param name="GridX">落点的格位 X（固定）。</param>
/// <param name="GridY">落点的格位 Y（固定）。</param>
/// <param name="FootprintW">占地格宽。</param>
/// <param name="FootprintH">占地格高。</param>
/// <param name="AffectsAreaW">影响区 X 方向半径（0 = 不影响任何地方）。</param>
/// <param name="AffectsAreaH">影响区 Y 方向半径（0 = 不影响任何地方）。</param>
/// <param name="Effect">这栋建筑的<b>效果</b>（D-105 起：井 = 采集点）。</param>
public readonly record struct TierBuilding(
    BuildingId Id,
    string Name,
    int Tier,
    int GridX,
    int GridY,
    int FootprintW,
    int FootprintH,
    int AffectsAreaW,
    int AffectsAreaH,
    BuildingEffect Effect);

/// <summary>
/// <b>层级附带建筑</b>的目录（D-099）。
/// </summary>
/// <remarks>
/// <para>⚠️ <b>它为什么是「单一真源」</b>：村庄升级时<b>谁</b>该冒出来、<b>冒在哪</b>、
/// <b>叫什么</b> ⇒ 全在这里 ⇒ 领域层、表现层、测试读的是<b>同一份</b>。</para>
///
/// <para>⚠️ <b>不要在这里写「效果」</b>：这一版的井<b>还没有玩法效果</b>
/// （基线 §12.3：「不能仅凭建筑名称自行指定功能」⇒ 功能得开发者拍板）
/// ⇒ 现在它<b>只</b>是「这一级该有的那口井」：真的存在、真的入档、真的看得见。</para>
/// </remarks>
public static class TierBuildings
{
    /// <summary>
    /// <b>井</b>（D-099）：升到<b>第 2 级</b>时出现。
    /// </summary>
    /// <remarks>
    /// ⚠️ <b>为什么是第 2 级</b>：村庄层级阶梯里有一级就叫「<b>有井的村子</b>」
    /// （破落户 → 有人来往的村子 → <b>有井的村子</b> → …），
    /// 而升级到第 2 级那次施工的名字本来就叫「<b>挖井</b>」
    /// ⇒ 「井 = 第 2 级附带」<b>不是实现者发明的</b>，是基线自己的说法。
    /// <para>⚠️ <b>落点 (5, 0)</b>：必须在<b>可见地图</b>上（0..15 × 0..10，D-093 的规矩），
    /// 且不与别的建筑重叠。⚠️ 它<b>不在</b>市集的影响区里（市集在 (-5,-5)）⇒
    /// 所以它<b>不</b>属于「范围类」建筑（见 <see cref="RangeBuildings"/>）。</para>
    /// </remarks>
    public static readonly TierBuilding Well = new(
        new BuildingId("well"),
        "井",
        Tier: 2,
        GridX: 5,
        GridY: 0,
        FootprintW: 1,
        FootprintH: 1,
        AffectsAreaW: 0,
        AffectsAreaH: 0,
        // D-105：开发者拍板「井 = 采集点」⇒ 它给采集加产量。
        Effect: BuildingEffect.Gathering);

    /// <summary>全部层级附带建筑。</summary>
    public static IReadOnlyList<TierBuilding> All { get; } = new[] { Well };

    /// <summary>
    /// 升到某一层级时，<b>应该出现</b>的层级附带建筑。
    /// </summary>
    /// <param name="tier">村庄层级（0 起）。</param>
    /// <returns>该层级附带的所有建筑（可能为空）。</returns>
    /// <remarks>
    /// ⚠️ <b>「累积」的</b>：升到第 3 级时，第 2 级那口井<b>也在</b>（它没消失）
    /// ⇒ 这里返回的是「<b>到这一级为止应有的全部</b>」。
    /// </remarks>
    public static IReadOnlyList<TierBuilding> UpToTier(int tier)
        => All.Where(b => b.Tier <= tier).ToArray();

    /// <summary>按标识查一栋层级附带建筑。</summary>
    /// <param name="id">建筑标识。</param>
    /// <returns>找不到时返回 null。</returns>
    public static TierBuilding? Find(BuildingId id)
    {
        foreach (var b in All)
        {
            if (b.Id.Value == id.Value) return b;
        }

        return null;
    }
}
