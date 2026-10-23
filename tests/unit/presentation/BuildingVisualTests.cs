using System.Linq;
using GdUnit4;
using ClaudeCodeGameStudios.Domain.Village;
using ClaudeCodeGameStudios.Presentation;

namespace ClaudeCodeGameStudios.Tests.Presentation;

/// <summary>
/// <b>建筑在地图上要长得不一样</b>（D-097）。
/// </summary>
/// <remarks>
/// <para><b>它守的是四件事</b>：
/// <list type="number">
/// <item><b>每一栋可建建筑都有专属画法</b>（不能落到兜底）—— 否则玩家在地图上看不出自己盖了什么。</item>
/// <item><b>三种建筑三种画法</b>（不许撞车）—— 撞了等于没区分。</item>
/// <item><b>画法按「效果」判</b>，不是按名字（名字会被改，效果是规则）。</item>
/// <item><b>兜底不是默认</b>：只有「真的不认识」时才用它。</item>
/// </list></para>
/// <para>⚠️ <b>为什么这条重要</b>：D-093 落地时，地图上画的是<b>一块色块</b>，
/// 三种建筑<b>长得一模一样</b> ⇒ 玩家在地图上看不出自己盖了什么
/// ⇒ 「看得见自己盖了什么」这句话<b>当时是假的</b>。</para>
/// </remarks>
[TestSuite]
public class BuildingVisualTests
{
    /// <summary>每一栋可建建筑都<b>必须有</b>专属画法（不许落到兜底）。</summary>
    /// <remarks>
    /// ⚠️ 这是本组守卫的<b>核心</b>：将来加一栋新建筑却忘了给它画法，
    /// 这里立刻红 ⇒ 不会出现「默默画成一块石台」。
    /// </remarks>
    [TestCase]
    public void EveryBuildableBuilding_HasItsOwnShape()
    {
        foreach (var b in BuildableBuildings.All)
        {
            string shape = BuildingVisual.ShapeFor(b);

            Assertions.AssertThat(BuildingVisual.IsKnown(shape))
                .IsTrue();
        }
    }

    /// <summary>三种建筑<b>三种画法</b>（不许撞车）。</summary>
    /// <remarks>
    /// ⚠️ 断言的是「**互不相同**」而不是「某几个具体值」⇒
    /// 将来改掉某个画法（只要仍互不相同）也不会红 —— 守的是<b>性质</b>，不是实现。
    /// </remarks>
    [TestCase]
    public void TheThreeBuildings_LookDifferentFromEachOther()
    {
        var shapes = BuildableBuildings.All
            .Select(b => BuildingVisual.ShapeFor(b))
            .ToArray();

        Assertions.AssertThat(shapes.Distinct().Count()).IsEqual(shapes.Length);
    }

    /// <summary>画法按<b>效果</b>判（围栏 = 范围 ⇒ 画成栅栏）。</summary>
    /// <remarks>
    /// ⚠️ <b>必须用「改过名字」的副本再验一次</b>：只拿现成的三栋去比，
    /// 「按名字判」和「按效果判」的结果<b>完全一样</b> ⇒ 那条守卫是<b>装饰品</b>
    /// （改坏实现它照样绿）。⇒ 把名字换掉再比，才能真的区分开。
    /// </remarks>
    [TestCase]
    public void ShapeFollowsTheEffect_NotTheName()
    {
        Assertions.AssertThat(BuildingVisual.ShapeFor(BuildableBuildings.Fence))
            .IsEqual(BuildingVisual.Fence);
        Assertions.AssertThat(BuildingVisual.ShapeFor(BuildableBuildings.TrialTower))
            .IsEqual(BuildingVisual.TrialTower);
        Assertions.AssertThat(BuildingVisual.ShapeFor(BuildableBuildings.Shield))
            .IsEqual(BuildingVisual.Shield);

        // ⚠️ **改名之后，画法不许变**（效果没变 ⇒ 规则没变 ⇒ 画面也不该变）。
        var renamed = BuildableBuildings.Shield with { Name = "罩子（改名了）" };
        Assertions.AssertThat(BuildingVisual.ShapeFor(renamed))
            .IsEqual(BuildingVisual.Shield);
    }

    /// <summary>兜底是「不认识」才用的，<b>不是默认</b>。</summary>
    [TestCase]
    public void TheFallback_IsNotADefault()
    {
        Assertions.AssertThat(BuildingVisual.IsKnown(BuildingVisual.Generic)).IsFalse();
        Assertions.AssertThat(BuildingVisual.IsKnown(BuildingVisual.Fence)).IsTrue();
        Assertions.AssertThat(BuildingVisual.IsKnown("")).IsFalse();
    }

    /// <summary>
    /// <b>D-099</b>：<b>层级附带建筑</b>（井）也有专属画法
    /// ⇒ 地图上不会又出现一个「不认识 ⇒ 石台」的井。
    /// </summary>
    /// <remarks>
    /// ⚠️ 这条守的是「<b>两类建筑共用一张画法表</b>」：
    /// 可建建筑按效果查、层级附带建筑按标识查 ⇒ 两边都不能掉进兜底。
    /// </remarks>
    [TestCase]
    public void TierBuildings_AlsoHaveTheirOwnShape()
    {
        string shape = BuildingVisual.ShapeForId(TierBuildings.Well.Id);

        Assertions.AssertThat(shape).IsEqual(BuildingVisual.Well);
        Assertions.AssertThat(BuildingVisual.IsKnown(shape)).IsTrue();
    }

    /// <summary>
    /// <b>D-099</b>：<b>按标识查</b>与<b>按定义查</b>对同一栋建筑必须一致
    /// ⇒ 两处不许分叉（分叉了就有一处画错）。
    /// </summary>
    [TestCase]
    public void LookupById_MatchesLookupByDefinition()
    {
        foreach (var b in BuildableBuildings.All)
        {
            Assertions.AssertThat(BuildingVisual.ShapeForId(b.Id)).IsEqual(BuildingVisual.ShapeFor(b));
        }
    }

    /// <summary>
    /// <b>D-099</b>：<b>不认识</b>的标识 ⇒ 兜底（且<b>守卫会红</b>，因为兜底不算「认识」）。
    /// </summary>
    [TestCase]
    public void AnUnknownBuilding_FallsBackToTheGenericShape()
    {
        string shape = BuildingVisual.ShapeForId(new BuildingId("根本没这栋建筑"));

        Assertions.AssertThat(shape).IsEqual(BuildingVisual.Generic);
        Assertions.AssertThat(BuildingVisual.IsKnown(shape)).IsFalse();
    }
}
