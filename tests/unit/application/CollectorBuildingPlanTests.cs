using System.Collections.Generic;
using GdUnit4;
using ClaudeCodeGameStudios.Application;
using ClaudeCodeGameStudios.Domain.Village;

namespace ClaudeCodeGameStudios.Tests.Application;

/// <summary>
/// <b>采集器的盖房决策</b>（D-108）—— 标定器要有<b>花钱的出口</b>。
/// </summary>
/// <remarks>
/// <para><b>它守的是五件事</b>：
/// <list type="number">
/// <item>钱不够 ⇒ <b>不盖</b>（不能凭空盖房）。</item>
/// <item><b>盖完还买得起下一级升级</b> ⇒ 才盖（否则村庄永远升不上去）。</item>
/// <item>围栏与试炼塔<b>轮着盖</b>（谁盖得少盖谁）⇒ 不偏科。</item>
/// <item>没有合法落点 ⇒ <b>不盖</b>（不硬塞）。</item>
/// <item>已满级（下一级升级成本 = 0）⇒ <b>不用留钱</b> ⇒ 有余钱就盖。</item>
/// </list></para>
/// <para>⚠️ <b>为什么这条值得守</b>：D-106 查出「钱在涨」有很大一部分是
/// <b>采集器从不花钱</b>造成的 ⇒ 采集器的行为必须与真实玩家<b>同款</b>
/// ⇒ 而「会不会盖、盖哪栋、盖在哪」正是玩家每天都在做的决定。</para>
/// </remarks>
[TestSuite]
public class CollectorBuildingPlanTests
{
    private static readonly BuildableBuilding Fence = BuildableBuildings.Fence;
    private static readonly BuildableBuilding Tower = BuildableBuildings.TrialTower;

    private static readonly List<BuildableBuilding> Candidates = new() { Fence, Tower };

    private static CollectorBuildPlan? Plan(
        long gold,
        long nextUpgradeCost,
        int fences = 0,
        int towers = 0,
        bool affordable = true,
        bool hasSpot = true)
    {
        var counts = new Dictionary<string, int>
        {
            [Fence.Id.Value] = fences,
            [Tower.Id.Value] = towers,
        };

        return CollectorBuildingPlan.Plan(
            gold, nextUpgradeCost, Candidates, counts,
            _ => affordable,
            _ => hasSpot ? (1, 2) : ((int, int)?)null);
    }

    /// <summary>钱不够 ⇒ 不盖。</summary>
    [TestCase]
    public void NotAffordable_DoesNotBuild()
    {
        var plan = Plan(gold: 1000, nextUpgradeCost: 0, affordable: false);

        Assertions.AssertThat(plan).IsNull();
    }

    /// <summary>
    /// <b>盖完还买得起下一级升级</b> ⇒ 才盖。
    /// </summary>
    /// <remarks>
    /// ⚠️ 这一条守的是「<b>房子不能把村庄的钱吃光</b>」——
    /// 否则村庄永远升不上去 ⇒ 层级附带建筑（井 / 铁匠铺）也就永远不出现
    /// ⇒ 那比「钱囤着」更糟。
    /// </remarks>
    [TestCase]
    public void BuildWouldBreakTheNextUpgrade_DoesNotBuild()
    {
        // 围栏 80 金：村库 200 金、下一级升级要 150 金 ⇒ 盖完只剩 120 < 150 ⇒ 不盖。
        var plan = Plan(gold: 200, nextUpgradeCost: 150, fences: 0, towers: 0);

        Assertions.AssertThat(plan).IsNull();
    }

    /// <summary>盖完还剩得下升级的钱 ⇒ 盖。</summary>
    [TestCase]
    public void AffordableAndKeepsTheUpgradeMoney_Builds()
    {
        var plan = Plan(gold: 400, nextUpgradeCost: 150, fences: 0, towers: 0);

        Assertions.AssertThat(plan).IsNotNull();
        Assertions.AssertThat(plan!.Value.Definition.Id.Value).IsEqual(Fence.Id.Value);
        Assertions.AssertThat(plan.Value.GridX).IsEqual(1);
        Assertions.AssertThat(plan.Value.GridY).IsEqual(2);
    }

    /// <summary>
    /// <b>围栏与试炼塔轮着盖</b>：谁盖得少盖谁。
    /// </summary>
    /// <remarks>
    /// ⚠️ 只盖一种会偏科：只盖围栏 ⇒ 市场范围膨胀而英雄不变强；
    /// 只盖试炼塔 ⇒ 市场不变而村里堆着一堆用不上的塔。
    /// </remarks>
    [TestCase]
    public void TheLessBuiltOneIsBuiltNext()
    {
        // 围栏已经 2 栋、试炼塔 0 栋 ⇒ 这轮该盖试炼塔。
        var withFences = Plan(gold: 1000, nextUpgradeCost: 0, fences: 2, towers: 0);
        Assertions.AssertThat(withFences!.Value.Definition.Id.Value).IsEqual(Tower.Id.Value);

        // 反过来 ⇒ 盖围栏。
        var withTowers = Plan(gold: 1000, nextUpgradeCost: 0, fences: 0, towers: 2);
        Assertions.AssertThat(withTowers!.Value.Definition.Id.Value).IsEqual(Fence.Id.Value);
    }

    /// <summary>一样多 ⇒ 按候选表顺序（围栏先）。</summary>
    [TestCase]
    public void WhenEqual_KeepsTheCandidateOrder()
    {
        var plan = Plan(gold: 1000, nextUpgradeCost: 0, fences: 1, towers: 1);

        Assertions.AssertThat(plan!.Value.Definition.Id.Value).IsEqual(Fence.Id.Value);
    }

    /// <summary>没有合法落点 ⇒ 不盖（不硬塞）。</summary>
    [TestCase]
    public void NoLegalSpot_DoesNotBuild()
    {
        var plan = Plan(gold: 1000, nextUpgradeCost: 0, hasSpot: false);

        Assertions.AssertThat(plan).IsNull();
    }

    /// <summary>
    /// <b>已满级</b>（下一级升级成本 = 0）⇒ 不用留钱 ⇒ 有余钱就盖。
    /// </summary>
    [TestCase]
    public void AtMaxTier_NoNeedToReserveMoney()
    {
        var plan = Plan(gold: 80, nextUpgradeCost: 0, fences: 0, towers: 0);

        Assertions.AssertThat(plan).IsNotNull();
        Assertions.AssertThat(plan!.Value.Definition.Id.Value).IsEqual(Fence.Id.Value);
    }
}
