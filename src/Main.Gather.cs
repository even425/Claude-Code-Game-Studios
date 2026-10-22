using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

public partial class Main : Node2D
{
    /// <summary>采集与自动卖货（D-086）。</summary>
    private ClaudeCodeGameStudios.Application.GatherService _gather = null!;

    /// <summary>
    /// 某个英雄采集了一次：材料进<b>他自己的背包</b>（D-086）。
    /// </summary>
    /// <param name="heroId">采集者的英雄标识。</param>
    /// <param name="amount">份数。</param>
    /// <remarks>
    /// ⚠️ **为什么不再进村庄材料池**：村庄材料已经过剩（实测 200 天堆到 7.5 万份），
    /// 而**没参与副本的人一点收入都没有**（英雄金币中位只有 6 金）
    /// ⇒ 材料要能**被卖掉**才成为收入 ⇒ 归个人背包。
    /// <para>⚠️ **背包满了就是白采**（既有规则：背包 5 份/种）
    /// ⇒ 这正是「卖掉才能再采」的压力来源。</para>
    /// </remarks>
    private void GatherFor(string heroId, int amount)
    {
        var agent = _heroes.FirstOrDefault(h => h.Id.Value == heroId);
        if (agent is null) return;

        long got = _gather.Gather(agent);
        if (got <= 0)
        {
            // 背包满了 ⇒ 这一天的劳动白费（**不静默**：杂谈里说得出来）。
            GD.Print($"GATHER {agent.Name} 背包满了，采回来的东西放不下。");
        }
    }

    /// <summary>
    /// <b>有货就自动开摊</b>（D-086）：让背包里的材料能变现。
    /// </summary>
    /// <remarks>
    /// ⚠️ **这是游戏规则，不是采集器的取巧**：采集器原来自己开摊（还带一条
    /// 「今天卖哪一样」的轮换）⇒ 那个轮换原本是**压测专用**的做法，
    /// 却决定了整个市场的商品分布 ⇒ 现在<b>升为游戏规则</b>，
    /// 游戏与采集器<b>共用同一份实现</b>（一份规则 ⇒ 测到的就是玩到的）。
    /// <para>⇒ 收入链条因此闭合：**采集（材料进背包）→ 自动开摊 → 别的英雄买 → 卖方有钱**。</para>
    /// </remarks>
    private void OpenStallsForIdleHeroes()
    {
        int day = (int)_clock.Day;
        for (int i = 0; i < _heroes.Count; i++)
        {
            _gather.OpenStallFor(_heroes[i], day, i, _clock.Now);
        }
    }
}
