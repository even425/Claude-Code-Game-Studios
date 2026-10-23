using System;
using ClaudeCodeGameStudios.Data;

namespace ClaudeCodeGameStudios.Application;

/// <summary>
/// D-104：试炼塔给的<b>永久生命加成</b>的存 / 取（D-074 定的「永久」语义）。
/// </summary>
/// <remarks>
/// <para><b>为什么单独抽出来</b>：这一对转换原本住在 <c>Main.Save.cs</c> 里
/// （<c>BuildSave</c> 写、<c>ApplySave</c> 读）。而 <c>Main</c> 是 Godot 侧的大管家
/// ⇒ <b>单测碰不到</b> ⇒ 那一对转换<b>从来没被验过</b>——
/// 于是「永久加成读档后归零」这种缺陷可以静静躺在仓库里。
/// 抽成纯函数后，<b>生产代码与守卫调的是同一份</b>。</para>
/// <para>⚠️ <b>不要在这里复制规则</b>：本类只做<b>搬运</b>，
/// 「加成只增不减、不得为负」那条规则住在 <c>SaveValidator</c>（存档层），
/// 「怎么加的」住在 <c>BuildingCoordinator</c>（应用层规则）。
/// 这里只负责<b>把一个 int 原样搬过去</b>。</para>
/// </remarks>
public static class TrialTowerBonusPersistence
{
    /// <summary>写盘方向：把这个英雄当前的永久生命加成写进存档条目。</summary>
    /// <param name="agent">英雄（表现侧状态）。</param>
    /// <returns>永久生命加成（≥ 0）。</returns>
    public static int Export(HeroAgent agent)
    {
        if (agent is null) throw new ArgumentNullException(nameof(agent));
        return agent.TrialTowerHealthBonus;
    }

    /// <summary>读盘方向：把存档里的永久生命加成还回给英雄。</summary>
    /// <param name="save">存档条目（已过 <c>SaveValidator</c>）。</param>
    /// <param name="agent">要恢复的英雄。</param>
    /// <remarks>
    /// 调用方应<b>先过 <c>SaveValidator.Validate</c></b>：负数在那一层已被整份拒绝，
    /// 这里不再重复守门（权威判定只有一处）。
    /// </remarks>
    public static void Import(HeroSave save, HeroAgent agent)
    {
        if (save is null) throw new ArgumentNullException(nameof(save));
        if (agent is null) throw new ArgumentNullException(nameof(agent));
        agent.TrialTowerHealthBonus = save.TrialTowerHealthBonus;
    }
}
