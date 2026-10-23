using System.Collections.Generic;
using GdUnit4;
using ClaudeCodeGameStudios.Application;
using ClaudeCodeGameStudios.Domain.Village;

namespace ClaudeCodeGameStudios.Tests.Application;

/// <summary>
/// <b>防护罩</b>（D-095 拍板：<b>最小可测版</b> —— 一条能写进验收的规则 + 看得见）。
/// </summary>
/// <remarks>
/// <para><b>它守的是四件事</b>：
/// <list type="number">
/// <item>防护罩<b>建成</b> ⇒ 兽潮规模等级 −1（与围栏可叠加 ⇒ 最多 −2）。</item>
/// <item><b>在建</b>的防护罩不算（罩子没搭好挡不住）。</item>
/// <item>它与围栏<b>职责不同</b>：围栏 = 范围（决定摊位能摆到哪儿），
/// 防护罩 = 专职防御 ⇒ 两者各降一级。</item>
/// <item>它<b>在建造目录里</b>、能像围栏/试炼塔那样被玩家建造（落点自选，D-093）。</item>
/// </list>
///
/// <para>⚠️ <b>为什么第一版是「兽潮 −1 级」</b>：基线原话是「保护村庄不被外面攻击」，
/// 而当前游戏里<b>没有被攻击的东西</b>可罩（兽潮输赢损的是资源，人畜无损）
/// ⇒ 「最小可测版」先取一条<b>真的能写进验收</b>的规则；「罩住谁」留到下一版。</para>
/// </remarks>
[TestSuite]
public class ShieldDefenseTests
{
    /// <summary>造一个村庄：放一座防护罩（可选建成/在建）。</summary>
    private static Village NewVillage(bool shieldCompleted)
    {
        var village = new Village("v1");
        village.TryPlace(new BuildingInstance(
            new BuildingId("shield"),
            gridX: 2, gridY: 2, footprintW: 1, footprintH: 1,
            constructionProgress: shieldCompleted ? 1.0 : 0.4,
            affectsAreaW: 9, affectsAreaH: 9));
        return village;
    }

    /// <summary>防护罩建成 ⇒ 兽潮规模等级 −1。</summary>
    [TestCase]
    public void CompletedShield_LowersTheTideTierByOne()
    {
        var village = NewVillage(shieldCompleted: true);

        int shields = BeastTideScale.CompletedShieldCount(village);
        int tier = BeastTideScale.TierWithDefenses(villageTier: 3, completedFences: 0, completedShields: shields);

        Assertions.AssertThat(shields).IsEqual(1);
        Assertions.AssertThat(tier).IsEqual(2);
    }

    /// <summary><b>在建</b>的防护罩不算（半成品罩子挡不住）。</summary>
    [TestCase]
    public void UnfinishedShield_DoesNotCount()
    {
        var village = NewVillage(shieldCompleted: false);

        int shields = BeastTideScale.CompletedShieldCount(village);

        Assertions.AssertThat(shields).IsEqual(0);
        Assertions.AssertThat(BeastTideScale.TierWithDefenses(3, 0, shields)).IsEqual(3);
    }

    /// <summary>
    /// 围栏与防护罩<b>可叠加</b>（职责不同：范围 vs 防御）⇒ 一座围栏 + 一座防护罩 = −2 级。
    /// </summary>
    [TestCase]
    public void FenceAndShield_StackUp()
    {
        var village = NewVillage(shieldCompleted: true);
        village.TryPlace(new BuildingInstance(
            new BuildingId("fence"),
            gridX: 1, gridY: 1, footprintW: 1, footprintH: 1,
            constructionProgress: 1.0,
            affectsAreaW: 9, affectsAreaH: 9));

        int tier = BeastTideScale.TierWithDefenses(
            villageTier: 3,
            completedFences: BeastTideScale.CompletedFenceCount(village),
            completedShields: BeastTideScale.CompletedShieldCount(village));

        Assertions.AssertThat(tier).IsEqual(1);
    }

    /// <summary>最低仍是 0（防御设施不能把兽潮减成负数）。</summary>
    [TestCase]
    public void ManyShields_NeverGoBelowZero()
    {
        int tier = BeastTideScale.TierWithDefenses(villageTier: 1, completedFences: 2, completedShields: 3);

        Assertions.AssertThat(tier).IsEqual(0);
    }

    /// <summary>防护罩<b>在建造目录里</b>（玩家能建），且与围栏/试炼塔并列。</summary>
    [TestCase]
    public void Shield_IsInTheBuildableCatalog()
    {
        var shield = BuildableBuildings.Shield;

        Assertions.AssertThat(shield.Name).IsEqual("防护罩");
        Assertions.AssertThat(BuildableBuildings.All.Count).IsEqual(3);
        Assertions.AssertThat(BuildableBuildings.Find(shield.Id)).IsNotNull();
        Assertions.AssertThat(shield.Effect).IsEqual(BuildingEffect.Shield);
        // ⚠️ 成本与工时必须是**正数**（否则「建得起」永远是假的）。
        Assertions.AssertThat(shield.GoldCost > 0).IsTrue();
        Assertions.AssertThat(shield.MaterialCost > 0).IsTrue();
        Assertions.AssertThat(shield.BaseBuildHours > 0).IsTrue();
    }
}
