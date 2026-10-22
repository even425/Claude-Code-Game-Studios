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
