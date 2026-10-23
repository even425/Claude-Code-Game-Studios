using System.Collections.Generic;
using System.Linq;

namespace ClaudeCodeGameStudios.Domain.Village;

/// <summary>
/// <b>哪些建筑是「采集点」</b>，以及它们给采集加多少（D-105）。
/// </summary>
/// <remarks>
/// <para><b>为什么要有这一层</b>：D-105 开发者拍板「<b>井 = 采集点</b>」
/// ⇒ 「有井的村子采得到更多」这条规则必须<b>只有一份</b>
/// ⇒ 领域层、采集服务、守卫读的是<b>同一份</b>。</para>
///
/// <para>⚠️ <b>判据是「效果」不是「名字」</b>：名字是显示、会被改；
/// 效果是规则、不会变（与 D-097 的画法映射、D-098 的范围类判定同一原则）。</para>
///
/// <para>⚠️ <b>与 <see cref="RangeBuildings"/> 的「查不到就算范围类」正好相反</b>：
/// 那一条是<b>放开</b>（少一个圈地的建筑 ⇒ 开局摆不了摊 ⇒ 直接玩不下去），
/// 所以宁可多算；这一条是<b>加产量</b>（多算 = 白送材料 ⇒ 守恒律与经济标定都会漂）
/// ⇒ <b>宁可漏算</b>：只有<b>目录里明确写着「采集点」</b>的建筑才算数。
/// <br>⇒ 两条规则<b>方向相反是有原因的</b>，不是笔误。</para>
/// </remarks>
public static class GatheringPoints
{
    /// <summary>
    /// 一个采集点给采集加多少份（D-105 <b>建议值</b>：+1 份/天）。
    /// </summary>
    /// <remarks>
    /// ⚠️ <b>这是建议值</b>：基线 §12.3 说功能与数值都由开发者拍板
    /// ⇒ 开发者拍的是「<b>井 = 采集点</b>」这个<b>方向</b>，
    /// 数值「<b>+1 份/天</b>」是我先跑一版、实测后请他改的。
    /// <br>⇒ 它登记在参数台账里（改它必须同步台账 + 重跑标定）。
    /// </remarks>
    public const long BonusPerPoint = 1L;

    /// <summary>
    /// 这栋建筑是不是「采集点」。
    /// </summary>
    /// <param name="id">建筑标识。</param>
    /// <returns>是采集点为 true。</returns>
    public static bool IsGatheringPoint(BuildingId id)
        => IsGatheringPoint(TierBuildings.Find(id));

    /// <summary>
    /// 这栋<b>层级附带建筑</b>是不是「采集点」—— <b>按效果判</b>。
    /// </summary>
    /// <param name="definition">层级附带建筑的定义（可以是 null）。</param>
    /// <returns>是采集点为 true。</returns>
    /// <remarks>
    /// ⚠️ <b>为什么单独给一个「吃定义」的入口</b>：守卫要能验
    /// 「<b>按效果判</b>，而不是按名字判」⇒ 它必须能喂进
    /// <b>名字与效果互相矛盾</b>的定义（名字叫别的、效果是采集点）
    /// ⇒ 只给「按标识查」的那一个入口，守卫就<b>分不出</b>这两种写法
    /// （查出来的永远是同一栋井 ⇒ 两种实现都绿 ⇒ 守卫成了装饰品）。
    /// </remarks>
    public static bool IsGatheringPoint(TierBuilding? definition)
        => definition is not null && definition.Value.Effect == BuildingEffect.Gathering;

    /// <summary>
    /// 这个实例是不是「采集点」。
    /// </summary>
    /// <param name="building">建筑实例。</param>
    /// <returns>是采集点为 true。</returns>
    public static bool IsGatheringPoint(BuildingInstance building)
        => building is not null && IsGatheringPoint(building.Id);

    /// <summary>
    /// 这个村庄<b>一共有几个采集点</b>。
    /// </summary>
    /// <param name="village">村庄。</param>
    /// <returns>采集点数量。</returns>
    public static int CountFor(Village village)
    {
        if (village is null) return 0;
        return village.Buildings.Count(IsGatheringPoint);
    }

    /// <summary>
    /// 这个村庄给采集加多少份（每个采集点 +<see cref="BonusPerPoint"/>）。
    /// </summary>
    /// <param name="village">村庄。</param>
    /// <returns>每天多采到的份数（没有采集点就是 0）。</returns>
    /// <remarks>
    /// ⚠️ <b>只数「已建成」的</b>（见 <see cref="BuiltFor"/>）：施工中的井还不算数。
    /// </remarks>
    public static long BonusFor(Village village)
        => BuiltFor(village).Count * BonusPerPoint;

    /// <summary>
    /// 村庄里<b>已建成</b>的采集点（未建成的不算）。
    /// </summary>
    /// <param name="village">村庄。</param>
    /// <returns>已建成的采集点实例。</returns>
    /// <remarks>
    /// ⚠️ <b>为什么要筛「已建成」</b>：施工中的建筑还没生效
    /// ⇒ 不筛的话，玩家刚点下「挖井」，采集立刻就变快了（还没打井呢）。
    /// </remarks>
    public static IReadOnlyList<BuildingInstance> BuiltFor(Village village)
    {
        if (village is null) return System.Array.Empty<BuildingInstance>();
        return village.Buildings
            .Where(b => IsGatheringPoint(b) && b.ConstructionProgress >= 1.0)
            .ToArray();
    }
}
