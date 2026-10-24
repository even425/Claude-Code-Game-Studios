using System;
using ClaudeCodeGameStudios.Domain.Combat;
using ClaudeCodeGameStudios.Domain.Items;

namespace ClaudeCodeGameStudios.Application;

/// <summary>
/// <b>一个英雄此刻的战斗属性</b>（D-119）—— <b>唯一一处</b>算它的地方。
/// </summary>
/// <remarks>
/// <para>⚠️ <b>为什么要有这个类</b>：算「这个人的属性」原来有<b>两处</b>：
/// <c>Main.Expedition.CombatStatsFor</c>（副本 / 狩猎走它）与
/// <c>BuildingCoordinator.TryTrial</c>（试炼塔走它）——
/// 两处<b>各抄了一遍</b>「性格 × 投入度 + 试炼塔的永久生命加成」。
/// ⇒ 装备线再加一条「+装备加成」就会变成<b>第三处</b>，
/// 而三处一旦不同步，症状是「这个人打副本很强、试炼塔却像没穿装备」
/// —— <b>游戏不报错，只是数字对不上</b>。</para>
/// <para>⇒ 现在<b>只有这一个函数</b>知道属性是怎么算出来的，两处调用点都调它。</para>
///
/// <para>⚠️ <b>加成的顺序</b>：基础属性 → 试炼塔的永久生命加成 → 装备加成。
/// ⇒ 换算表（静态）与加成（这个人后来得到的）<b>分开算再相加</b>，
/// 静态表不会被写回（不变量 I7）。</para>
/// </remarks>
public static class HeroCombatStats
{
    /// <summary>
    /// 算某个英雄现在的战斗属性。
    /// </summary>
    /// <param name="agent">英雄个体。</param>
    /// <param name="catalog">装备注册表（身上穿的东西要靠它换算加成）。</param>
    /// <returns>战斗属性（攻 / 防 / 命）。</returns>
    /// <exception cref="ArgumentNullException">参数为 null。</exception>
    public static CombatStats For(HeroAgent agent, EquipmentCatalog catalog)
    {
        if (agent is null) throw new ArgumentNullException(nameof(agent));
        if (catalog is null) throw new ArgumentNullException(nameof(catalog));

        // ① 基础属性（静态表：性格 × 投入度）
        var stats = CombatParameters.ForHero(agent.Personality, agent.Diligence);

        // ② 试炼塔给的**永久**生命加成（D-074：只加生命）
        int bonus = agent.TrialTowerHealthBonus;
        if (bonus != 0) stats = stats with { Health = stats.Health + bonus };

        // ③ 装备加成（D-119：耐久为 0 的装备**不生效**）
        return EquipmentStats.Resolve(stats, agent.Equipment, catalog);
    }
}
