using System;
using System.Collections.Generic;
using ClaudeCodeGameStudios.Domain.Items;

namespace ClaudeCodeGameStudios.Application;

/// <summary>
/// <b>战斗后的装备磨损</b>（D-121）—— 谁上了战场，谁的装备就掉耐久。
/// </summary>
/// <remarks>
/// <para>⚠️ <b>为什么要单独一个类</b>：战斗有<b>五处</b>（副本 / 试炼塔 / 狩猎 / 兽潮 / 盗匪），
/// 而「磨损多少」这条规则<b>只有一处</b>（<see cref="EquipmentService.WearDownPerBattle"/>）
/// ⇒ 磨损的<b>接线</b>散在五处是没办法的，但<b>规则</b>不许散。</para>
///
/// <para>⚠️ <b>「参与了事件」≠「打了仗」</b>：商队来访也带参与者，可他们没打任何一场
/// ⇒ 是不是战斗型由<b>模板自己声明</b>（<c>WorldEventTemplate.IsCombat</c>），
/// 本类只负责「把名单里的人磨损掉」。</para>
/// </remarks>
public static class EquipmentWearDown
{
    /// <summary>
    /// 让名单里的英雄磨损装备（<b>每人 -1 点耐久</b>）。
    /// </summary>
    /// <param name="heroes">候选英雄（全体）。</param>
    /// <param name="participantIds">参战者的英雄标识。</param>
    /// <returns>实际磨损了几个人（找不到的人不算）。</returns>
    /// <remarks>
    /// ⚠️ <b>找不到就跳过</b>：英雄可能已不在村里（读档、召唤上限等）
    /// ⇒ 找不到**不是错误**，但也**不能算磨损了**（否则会虚报）。
    /// </remarks>
    public static int Apply(IEnumerable<HeroAgent> heroes, IReadOnlyList<string> participantIds)
    {
        if (heroes is null) throw new ArgumentNullException(nameof(heroes));
        if (participantIds is null) throw new ArgumentNullException(nameof(participantIds));
        if (participantIds.Count == 0) return 0;

        int worn = 0;
        foreach (var id in participantIds)
        {
            if (string.IsNullOrEmpty(id)) continue;

            foreach (var hero in heroes)
            {
                if (hero is null) continue;
                if (hero.Id.Value != id) continue;

                hero.Equipment = EquipmentService.WearDown(hero.Equipment);
                worn++;
                break;
            }
        }

        return worn;
    }
}
