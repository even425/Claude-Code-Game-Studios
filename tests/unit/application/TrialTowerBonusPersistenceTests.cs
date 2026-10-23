using GdUnit4;
using ClaudeCodeGameStudios.Application;
using ClaudeCodeGameStudios.Data;

namespace ClaudeCodeGameStudios.Tests.Application;

/// <summary>
/// D-104：试炼塔的<b>永久生命加成</b>的存 / 取。
/// </summary>
/// <remarks>
/// <para><b>这条守卫守的是什么</b>：这个加成的定义是「<b>永久</b>」（D-074）⇒
/// 它必须跟着人走完「写盘 → 读盘」的整圈。</para>
/// <para><b>为什么值得单独守</b>：这一对转换原本住在 <c>Main.Save.cs</c> 里
/// ⇒ 单测碰不到 ⇒ <b>从来没被验过</b> ⇒
/// 「读档后永久加成归零」才会静静躺在仓库里（而且游戏<b>不报任何错</b>，
/// 只是「这个人好像变弱了」）。抽成纯函数后，生产代码与守卫调的是<b>同一份</b>。</para>
/// </remarks>
[TestSuite]
public class TrialTowerBonusPersistenceTests
{
    /// <summary>写盘方向：把英雄当前的永久加成取出来。</summary>
    [TestCase]
    public void Export_ReturnsAgentsCurrentBonus()
    {
        var agent = new HeroAgent { TrialTowerHealthBonus = 20 };

        int exported = TrialTowerBonusPersistence.Export(agent);

        Assertions.AssertThat(exported).IsEqual(20);
    }

    /// <summary>读盘方向：把存档里的永久加成还回英雄。</summary>
    [TestCase]
    public void Import_SetsAgentsBonus()
    {
        var agent = new HeroAgent();
        var save = new HeroSave { TrialTowerHealthBonus = 35 };

        TrialTowerBonusPersistence.Import(save, agent);

        Assertions.AssertThat(agent.TrialTowerHealthBonus).IsEqual(35);
    }

    /// <summary>
    /// <b>整圈往返</b>：英雄身上 20 → 存档 → 另一个英雄 → 还是 20。
    /// </summary>
    /// <remarks>
    /// 这一条才真正对应「读档后加成还在不在」——
    /// 单测「取」与「还」各自都对，中间断一节照样全绿。
    /// </remarks>
    [TestCase]
    public void RoundTrip_BonusSurvives()
    {
        var before = new HeroAgent { TrialTowerHealthBonus = 20 };

        var save = new HeroSave
        {
            TrialTowerHealthBonus = TrialTowerBonusPersistence.Export(before),
        };

        var after = new HeroAgent();
        TrialTowerBonusPersistence.Import(save, after);

        Assertions.AssertThat(after.TrialTowerHealthBonus).IsEqual(20);
    }

    /// <summary>
    /// <b>加成是 0 时必须真的写成 0</b>（不能留着上一个英雄的值）。
    /// </summary>
    /// <remarks>
    /// 「没打��试炼塔」与「打输了」在存档里都表现为 0 ⇒
    /// 导入 0 不能变成「保持原样」，否则会带着上一份世界的加成。
    /// </remarks>
    [TestCase]
    public void Import_Zero_OverwritesExistingBonus()
    {
        var agent = new HeroAgent { TrialTowerHealthBonus = 40 };
        var save = new HeroSave { TrialTowerHealthBonus = 0 };

        TrialTowerBonusPersistence.Import(save, agent);

        Assertions.AssertThat(agent.TrialTowerHealthBonus).IsEqual(0);
    }
}
