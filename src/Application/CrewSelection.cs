using System;
using System.Collections.Generic;
using System.Linq;

namespace ClaudeCodeGameStudios.Application;

/// <summary>
/// <b>副本选人</b>（D-122）—— 优先派<b>有装备</b>的人。
/// </summary>
/// <remarks>
/// <para>⚠️ <b>为什么要有它</b>：开发者拍板「副本优先派有装备的人」
/// ⇒ 装备才真的成了<b>成长线</b>（强的人去打更难的仗），而不是摆在背包里的装饰。</para>
///
/// <para>⚠️ <b>两轮扫描</b>：
/// <list type="number">
/// <item>第一轮：只收<b>有装备且没坏</b>的（坏掉的装备没有加成 ⇒ 不算「有装备」）。</item>
/// <item>第二轮：不够的人数用<b>其余可用的人</b>补齐（不能因为没装备就不派人）。</item>
/// </list>
/// ⇒ 两轮都按<b>轮换起点</b>顺序走 ⇒ 队伍仍然轮换，不会总是同样两个人。</para>
///
/// <para>⚠️ <b>规则住在应用层（纯函数）</b> ⇒ 可离线单测；
/// 采集器与未来的自动组队都走它。</para>
/// </remarks>
public static class CrewSelection
{
    /// <summary>这个人是不是「有装备（而且没坏）」。</summary>
    /// <param name="hero">英雄。</param>
    /// <returns>有可用装备为 true。</returns>
    /// <remarks>
    /// ⚠️ <b>坏掉的装备不算</b>：耐久为 0 ⇒ 加成不生效 ⇒ 穿了等于没穿。
    /// </remarks>
    public static bool IsEquipped(HeroAgent hero)
    {
        if (hero is null) return false;

        var gear = hero.Equipment;
        bool weaponOk = gear.Weapon is not null && gear.WeaponDurability > 0;
        bool armorOk = gear.Armor is not null && gear.ArmorDurability > 0;
        return weaponOk || armorOk;
    }

    /// <summary>
    /// 选一支队伍：<b>优先有装备的人</b>。
    /// </summary>
    /// <param name="heroes">候选英雄（全体）。</param>
    /// <param name="startIndex">轮换起点（从第几个人开始看）。</param>
    /// <param name="count">要几个人。</param>
    /// <param name="isAvailable">这个人现在能不能派（在不在场 / 在不在别的任务里）。</param>
    /// <returns>选出来的人（按「先有装备、后补齐」的顺序）。</returns>
    public static IReadOnlyList<HeroAgent> Select(
        IReadOnlyList<HeroAgent> heroes,
        int startIndex,
        int count,
        Func<HeroAgent, bool> isAvailable)
    {
        if (heroes is null) throw new ArgumentNullException(nameof(heroes));
        if (isAvailable is null) throw new ArgumentNullException(nameof(isAvailable));
        if (count <= 0 || heroes.Count == 0) return Array.Empty<HeroAgent>();

        var picked = new List<HeroAgent>();
        var used = new HashSet<string>(StringComparer.Ordinal);

        int n = heroes.Count;
        int start = n > 0 ? ((startIndex % n) + n) % n : 0;

        // ① 第一轮：只收有装备的。
        for (int k = 0; k < n && picked.Count < count; k++)
        {
            var h = heroes[(start + k) % n];
            if (h is null) continue;
            if (!isAvailable(h)) continue;
            if (!IsEquipped(h)) continue;
            if (!used.Add(h.Id.Value)) continue;

            picked.Add(h);
        }

        // ② 第二轮：不够的人数用其余可用的人补齐。
        for (int k = 0; k < n && picked.Count < count; k++)
        {
            var h = heroes[(start + k) % n];
            if (h is null) continue;
            if (!isAvailable(h)) continue;
            if (!used.Add(h.Id.Value)) continue;

            picked.Add(h);
        }

        return picked;
    }
}
