using System.Collections.Generic;
using GdUnit4;
using ClaudeCodeGameStudios.Application;
using ClaudeCodeGameStudios.Domain.Village;

namespace ClaudeCodeGameStudios.Tests.Application;

/// <summary>
/// <b>防御设施能守村</b>（D-085 拍板：<b>围栏 ⇒ 兽潮规模 −1 级</b>）。
/// </summary>
/// <remarks>
/// <para><b>它守的是三件事</b>：
/// <list type="number">
/// <item>围栏建成 ⇒ 兽潮的<b>实际规模等级</b>降一级（最低 0）。</item>
/// <item><b>在建</b>的围栏不算（半成品墙挡不住兽潮）。</item>
/// <item>预演与真正结算用<b>同一份</b>当前值（不会「预演说守得住、实战却按满级打」）。</item>
/// </list>
/// <para>⚠️ <b>为什么这一条重要</b>：它把基线 §14.3 那条验收重新激活了 ——
/// 「防御设施通过明确的游戏规则改变了至少一项结算指标」现在**有指标可看了**
/// （守不守得住 / 战利品多少），而 D-082 当时只能记成「暂不适用」。</para>
/// </remarks>
[TestSuite]
public class BeastTideDefenseTests
{
    /// <summary>造一个村庄：放一座围栏（可选建成/在建）+ 若干别的建筑。</summary>
    private static Village NewVillage(bool fenceCompleted, int extraFences = 0)
    {
        var village = new Village("v1");

        int placed = 0;
        void PlaceFence(double progress)
        {
            // ⚠️ 每座围栏要占**不同的格**（同一格放不下第二座 ⇒ TryPlace 失败）。
            village.TryPlace(new BuildingInstance(
                new BuildingId("fence"),
                gridX: -3 - placed, gridY: -3, footprintW: 1, footprintH: 1,
                constructionProgress: progress,
                affectsAreaW: 9, affectsAreaH: 9));
            placed++;
        }

        if (fenceCompleted) PlaceFence(1.0); else PlaceFence(0.4);
        for (int i = 0; i < extraFences; i++) PlaceFence(1.0);

        return village;
    }

    /// <summary>围栏建成 ⇒ 规模等级 −1。</summary>
    [TestCase]
    public void CompletedFence_LowersTheTideTierByOne()
    {
        var withFence = NewVillage(fenceCompleted: true);

        int fences = BeastTideScale.CompletedFenceCount(withFence);
        int tier = BeastTideScale.TierWithDefenses(villageTier: 3, completedFences: fences);

        Assertions.AssertThat(fences).IsEqual(1);
        Assertions.AssertThat(tier).IsEqual(2);
    }

    /// <summary><b>在建</b>的围栏不算（半成品墙挡不住兽潮）。</summary>
    [TestCase]
    public void UnfinishedFence_DoesNotCount()
    {
        var building = NewVillage(fenceCompleted: false);

        int fences = BeastTideScale.CompletedFenceCount(building);

        Assertions.AssertThat(fences).IsEqual(0);
        Assertions.AssertThat(BeastTideScale.TierWithDefenses(3, fences)).IsEqual(3);
    }

    /// <summary>多座围栏 ⇒ 多降几级；<b>但最低 0</b>（不能减成负数）。</summary>
    [TestCase]
    public void MultipleFences_LowerFurther_ButNeverBelowZero()
    {
        var village = NewVillage(fenceCompleted: true, extraFences: 2);

        int fences = BeastTideScale.CompletedFenceCount(village);
        Assertions.AssertThat(fences).IsEqual(3);

        Assertions.AssertThat(BeastTideScale.TierWithDefenses(5, fences)).IsEqual(2);
        Assertions.AssertThat(BeastTideScale.TierWithDefenses(1, fences)).IsEqual(0);
        Assertions.AssertThat(BeastTideScale.TierWithDefenses(0, fences)).IsEqual(0);
    }

    /// <summary>没围栏 ⇒ 规模等级就是村庄层级（保持原行为）。</summary>
    [TestCase]
    public void NoFence_TierIsUnchanged()
    {
        var village = new Village("v1");
        // ⚠️ **村里有别的建筑**（市集）：只有**围栏**才守村 ⇒ 别把别的建筑算进去。
        village.TryPlace(new BuildingInstance(
            new BuildingId("village_square"),
            gridX: -5, gridY: -5, footprintW: 1, footprintH: 1,
            constructionProgress: 1.0,
            affectsAreaW: 11, affectsAreaH: 11));

        Assertions.AssertThat(BeastTideScale.CompletedFenceCount(village)).IsEqual(0);
        Assertions.AssertThat(BeastTideScale.TierWithDefenses(4, 0)).IsEqual(4);
    }
}
