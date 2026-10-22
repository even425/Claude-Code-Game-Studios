using System.Collections.Generic;
using System.Linq;
using GdUnit4;
using ClaudeCodeGameStudios.Domain.Combat;

namespace ClaudeCodeGameStudios.Tests.Combat;

/// <summary>
/// <b>战斗参数表必须覆盖游戏会创建的所有性格</b>（D-084）。
/// </summary>
/// <remarks>
/// <para><b>它守的是什么</b>：游戏会创建三种性格（莽 / 傲 / 贪 —— 见
/// <c>HeroFactory</c> 的世界表），而战斗参数表原先<b>只有两种</b>。
/// ⇒ 任何<b>贪</b>的英雄去算战斗属性都会<b>抛异常</b>，后果有两处：
/// <list type="number">
/// <item>兽潮的「守得住吗」预演每 7 天抛一次 ⇒ <b>那天的世界事件整段被跳过</b>；</item>
/// <item>采集里凡是队伍里有贪英雄的副本 ⇒ 被 <c>catch</c> <b>静默丢弃</b>
/// ⇒ 实测 1958 场副本结算里，约三分之一的英雄<b>从没参过战</b> ⇒ <b>标定数据一直是偏的</b>。</item>
/// </list></para>
/// <para>⚠️ <b>为什么这条要有</b>：性格表与战斗表是<b>两份</b>数据，
/// 而战斗表<b>只查「有没有」</b>、不查「是不是全的」⇒ 缺一个不会有人发现，
/// 只会在**跑到它的时候**抛异常 ⇒ 而那通常发生在**采集/压测**里（没人看的地方）。</para>
/// </remarks>
[TestSuite]
public class CombatPersonalityCoverageTests
{
    /// <summary>
    /// 世界表里出现的**每一种性格**，战斗参数表里都必须有。
    /// </summary>
    /// <remarks>
    /// ⚠️ 用 <c>HeroFactory.World.All</c>（当前生效的那份：外部配置优先，坏则退回内置表）
    /// ⇒ 这样<b>配置文件里新增的性格</b>也会被这条守卫盯住。
    /// </remarks>
    [TestCase]
    public void EveryPersonalityTheGameCanCreate_HasCombatStats()
    {
        var personalities = new HashSet<string>();
        foreach (var world in HeroFactory.World.All)
        {
            foreach (var p in world.Personalities) personalities.Add(p);
        }

        Assertions.AssertThat(personalities.Count).IsGreater(0);

        var missing = new List<string>();
        foreach (var p in personalities)
        {
            bool known = CombatParameters.PersonalityStats.Any(
                a => a.Name == p)
                || CombatParameters.DiligenceHealth.Any(a => a.Name == p)
                || CombatParameters.IdleAttackDefense.Any(a => a.Name == p);

            if (!known) missing.Add(p);
        }

        Assertions.AssertThat(string.Join("、", missing)).IsEmpty();
    }

    /// <summary>
    /// ⚠️ **每一种性格都必须能算出战斗属性**（不只是「表里有」）——
    /// 「投入度为挂机」这条优先规则也不能把结果算成「全 0」（那叫状态不明，战斗会大声失败）。
    /// </summary>
    [TestCase]
    public void EveryPersonality_CanComputeNonZeroStats()
    {
        foreach (var world in HeroFactory.World.All)
        {
            foreach (var personality in world.Personalities)
            {
                foreach (var diligence in new[] { "肝", "随手", "挂机" })
                {
                    var stats = CombatParameters.ForHero(personality, diligence);

                    Assertions.AssertThat(stats.Attack).IsGreater(0);
                    Assertions.AssertThat(stats.Defense).IsGreater(0);
                    Assertions.AssertThat(stats.Health).IsGreater(0);
                }
            }
        }
    }

    /// <summary>「贪」必须有战斗属性（D-084 拍板：攻 10 / 防 5）。</summary>
    [TestCase]
    public void Greedy_HasItsDecidedStats()
    {
        var stats = CombatParameters.ForHero("贪", "肝");

        Assertions.AssertThat(stats.Attack).IsEqual(10);
        Assertions.AssertThat(stats.Defense).IsEqual(5);
        Assertions.AssertThat(stats.Health).IsEqual(120);   // 生命仍由投入度定
    }
}
