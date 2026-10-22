using System;
using System.Collections.Generic;
using System.Linq;
using GdUnit4;
using ClaudeCodeGameStudios.Application;
using ClaudeCodeGameStudios.Domain.Village;

namespace ClaudeCodeGameStudios.Tests.Application;

/// <summary>
/// <b>多栋同时施工</b>（D-082 拍板）。
/// </summary>
/// <remarks>
/// <para><b>它守的是三件事</b>：
/// <list type="number">
/// <item><b>人力只算一次</b>：两栋同时盖时，<b>同一批人不能被算两遍</b>。</item>
/// <item><b>分配是确定的</b>：同样的输入 ⇒ 同样的分配（可复现、可离线验算）。</item>
/// <item><b>两栋都能推进</b>：不是「先到先得」的排队。</item>
/// </list>
/// <para>⚠️ <b>为什么第一条重要</b>：原来每栋在建建筑都拿到<b>全村</b>的人力
/// ⇒ 围栏与试炼塔同时施工时人力**凭空翻倍** ⇒ 两栋一起盖比一栋快一倍。</para>
/// </remarks>
[TestSuite]
public class ParallelConstructionTests
{
    private static readonly ResourceId Gold = new("金币");
    private static readonly ResourceId Material = new("材料");

    private static BuildingService NewService(Village village) => new(village, Gold, Material);

    private static Village NewVillage(long gold, long material)
    {
        var v = new Village("v1");
        v.Add(Gold, gold);
        v.Add(Material, material);
        // 「市集」（真实游戏开局就有，影响区 11×11）。
        // ⚠️ D-093：落点必须在村子的地盘里 ⇒ 夹具里没有市集就没有地盘。
        v.TryPlace(new ClaudeCodeGameStudios.Domain.Village.BuildingInstance(
            new ClaudeCodeGameStudios.Domain.Village.BuildingId("village_square"),
            gridX: -5, gridY: -5, footprintW: 1, footprintH: 1,
            constructionProgress: 1.0,
            affectsAreaW: 11, affectsAreaH: 11));
        return v;
    }

    /// <summary>人力分配：均分 + 余数给最早开工的。</summary>
    [TestCase]
    public void AssignWorkers_SplitsEvenly_AndRemainderToTheEarliest()
    {
        var village = NewVillage(1000, 1000);
        var service = NewService(village);

        Assertions.AssertThat(service.TryStart(BuildableBuildings.Fence, 0)).IsTrue();
        Assertions.AssertThat(service.TryStart(BuildableBuildings.TrialTower, 0)).IsTrue();

        var assigned = service.AssignWorkers(availableWorkers: 5);

        Assertions.AssertThat(assigned.Count).IsEqual(2);
        // 5 人分给 2 栋：3 + 2 ⇒ 余数给最早开工的围栏。
        Assertions.AssertThat(assigned[0].Workers).IsEqual(3);
        Assertions.AssertThat(assigned[1].Workers).IsEqual(2);
        // ⚠️ **总人数不许超过可用人力**（人力只算一次）。
        Assertions.AssertThat(assigned.Sum(a => a.Workers)).IsEqual(5);
    }

    /// <summary>人不够时：后开工的那栋分到 0 ⇒ 停工（而不是把人力算两遍）。</summary>
    [TestCase]
    public void AssignWorkers_WhenShortOnPeople_TheLaterOneGetsNone()
    {
        var village = NewVillage(1000, 1000);
        var service = NewService(village);

        service.TryStart(BuildableBuildings.Fence, 0);
        service.TryStart(BuildableBuildings.TrialTower, 0);

        var assigned = service.AssignWorkers(availableWorkers: 1);

        Assertions.AssertThat(assigned[0].Workers).IsEqual(1);
        Assertions.AssertThat(assigned[1].Workers).IsEqual(0);
        Assertions.AssertThat(assigned.Sum(a => a.Workers)).IsEqual(1);
    }

    /// <summary>没有在建 ⇒ 不分（也不该抛）。</summary>
    [TestCase]
    public void AssignWorkers_WithNothingUnderConstruction_IsNoop()
    {
        var village = NewVillage(1000, 1000);
        var service = NewService(village);

        var assigned = service.AssignWorkers(availableWorkers: 5);

        Assertions.AssertThat(assigned.Count).IsEqual(0);
    }

    /// <summary>
    /// 两栋同时施工 ⇒ <b>两栋都在推进</b>，而进度**按分到的人算**。
    /// </summary>
    /// <remarks>
    /// 4 个人分给 2 栋 ⇒ 各 2 人 ⇒ 干 2 小时：
    /// 围栏（8 工时）= 2×2/8 = **0.5**；试炼塔（12 工时）= 2×2/12 = **0.333**。
    /// <para>⚠️ **如果人力被算两遍**（每栋都拿 4 人）⇒ 围栏 4×2/8 = 1.0（盖完）、
    /// 试炼塔 4×2/12 = 0.667 ⇒ **两个数都不一样** ⇒ 这条断言抓得住。</para>
    /// <para>⚠️ **为什么用「4 人 × 2 小时」而不是「1 人 × 4 小时」**：两种组合的
    /// 总人时一样（4 人时）⇒ 算出来的进度也一样 ⇒ 但**只有前者**能区分
    /// 「进度里乘了人数」和「没乘」⇒ 用后者会让那条变异<b>抓不到</b>。</para>
    /// </remarks>
    [TestCase]
    public void TwoConstructions_ShareTheSamePoolOfWorkers()
    {
        var village = NewVillage(1000, 1000);
        var service = NewService(village);

        service.TryStart(BuildableBuildings.Fence, 0);
        service.TryStart(BuildableBuildings.TrialTower, 0);
        service.AssignWorkers(availableWorkers: 4);

        service.AdvanceConstruction(workedHours: 2.0);

        var constructions = service.ActiveConstructions;
        var fence = constructions.First(c => c.Definition.Id.Value == BuildableBuildings.Fence.Id.Value);
        var tower = constructions.First(c => c.Definition.Id.Value == BuildableBuildings.TrialTower.Id.Value);

        // 两栋都还在建（人力没被算两遍 ⇒ 没这么快）。
        Assertions.AssertThat(constructions.Count).IsEqual(2);
        Assertions.AssertThat(Math.Abs(fence.Instance.ConstructionProgress - 0.5) < 0.001f).IsTrue();
        Assertions.AssertThat(Math.Abs(tower.Instance.ConstructionProgress - 0.3333333) < 0.001f).IsTrue();
    }

    /// <summary>
    /// ⚠️ **人力只算一次**的行为断言：人不够分时，<b>后开工的那栋拿不到人</b>（进度为 0）。
    /// </summary>
    /// <remarks>
    /// 1 个人分给 2 栋 ⇒ 围栏 1 人、试炼塔 0 人 ⇒ 干 8 小时：
    /// 围栏 1×8/8 = 1.0（盖完），试炼塔 **0**（没人 ⇒ 停工）。
    /// <para>⚠️ **这正是「人力只算一次」的样子**：若每栋都拿全村人力，
    /// 试炼塔会推进 8/12 = 0.67 ⇒ 断言抓得住。</para>
    /// </remarks>
    [TestCase]
    public void WorkersAreNotCountedTwiceAcrossConstructions()
    {
        var village = NewVillage(1000, 1000);
        var service = NewService(village);

        service.TryStart(BuildableBuildings.Fence, 0);
        service.TryStart(BuildableBuildings.TrialTower, 0);
        service.AssignWorkers(availableWorkers: 1);

        service.AdvanceConstruction(workedHours: 8.0);

        // 围栏盖完（从在施工列表里移除）⇒ 只剩试炼塔，而它一分人也没拿到。
        var constructions = service.ActiveConstructions;
        Assertions.AssertThat(constructions.Count).IsEqual(1);

        var tower = constructions.First(c => c.Definition.Id.Value == BuildableBuildings.TrialTower.Id.Value);
        Assertions.AssertThat(tower.Instance.ConstructionProgress).IsEqual(0.0);
        Assertions.AssertThat(tower.Workers).IsEqual(0);
    }
}
