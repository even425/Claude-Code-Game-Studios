using System;
using System.Collections.Generic;
using System.Linq;
using GdUnit4;
using ClaudeCodeGameStudios.Application;
using ClaudeCodeGameStudios.Data;
using ClaudeCodeGameStudios.Domain.Village;

namespace ClaudeCodeGameStudios.Tests.Data;

/// <summary>
/// <b>建筑入档</b>（D-083）。
/// </summary>
/// <remarks>
/// <para><b>它守的是三件事</b>：
/// <list type="number">
/// <item>存档 → 读档 ⇒ <b>已建成的建筑还在</b>（围栏/试炼塔不能凭空消失）。</item>
/// <item>存档 → 读档 ⇒ <b>影响区还在</b>（围栏没了摆摊范围 = 白盖了）。</item>
/// <item>存档 → 读档 ⇒ <b>在建的建筑会继续推进</b>（不能永远停在 40%）。</item>
/// </list>
/// <para>⚠️ <b>为什么这三条都要有</b>：建筑是玩家花<b>钱 + 材料 + 工期</b>换来的
/// ⇒ 读档后它们消失，等于把玩家的投入<b>白拿</b>。</para>
/// </remarks>
[TestSuite]
public class BuildingSaveTests
{
    private static readonly ResourceId Gold = new("金币");
    private static readonly ResourceId Material = new("材料");

    /// <summary>造一个村庄 + 建造服务，并放好开局市集。</summary>
    private static (Village Village, BuildingService Service) NewScene(long gold = 1000, long material = 1000)
    {
        var village = new Village("v1");
        village.Add(Gold, gold);
        village.Add(Material, material);

        // 开局市集（与 Main.BuildInitialVillage 同款）。
        var market = new BuildingInstance(
            new BuildingId("village_square"),
            gridX: -5, gridY: -5, footprintW: 1, footprintH: 1,
            constructionProgress: 1.0,
            affectsAreaW: 11, affectsAreaH: 11);
        village.TryPlace(market);

        return (village, new BuildingService(village, Gold, Material));
    }

    /// <summary>
    /// 把村庄里的建筑导出成存档里的形状。
    /// </summary>
    /// <remarks>
    /// ⚠️ **调的是生产代码**（`BuildingPersistence.Export`），**不是自己复制一遍** ——
    /// 复制品会让守卫变成装饰品（实测：五档变异全绿，因为改生产代码根本不影响测试）。
    /// </remarks>
    /// <param name="village">村庄。</param>
    /// <returns>建筑存档列表。</returns>
    private static List<BuildingSave> SaveBuildings(Village village)
        => BuildingPersistence.Export(village);

    /// <summary>
    /// 按存档把建筑放回村庄（<b>同样调生产代码</b>：<c>BuildingPersistence.Import</c>）。
    /// </summary>
    /// <param name="village">村庄。</param>
    /// <param name="saves">存档里的建筑。</param>
    /// <returns>成功放下的建筑实例。</returns>
    private static IReadOnlyList<BuildingInstance> RestoreBuildings(
        Village village, List<BuildingSave> saves)
        => BuildingPersistence.Import(village, saves);

    /// <summary>D-083 ①：<b>已建成的围栏与试炼塔，读档后还在</b>。</summary>
    [TestCase]
    public void CompletedBuildings_SurviveSaveAndLoad()
    {
        var (village, service) = NewScene();

        Assertions.AssertThat(service.TryStart(BuildableBuildings.Fence, 1)).IsTrue();
        Assertions.AssertThat(service.TryStart(BuildableBuildings.TrialTower, 1)).IsTrue();
        // 盖完。
        service.AdvanceConstruction(workedHours: 100.0);

        var saved = SaveBuildings(village);
        Assertions.AssertThat(saved.Count).IsEqual(3);   // 市集 + 围栏 + 试炼塔

        // ── 读档：一个全新的村庄 ──
        var (village2, _) = NewScene();
        RestoreBuildings(village2, saved);

        var ids = village2.Buildings.Select(b => b.Id.Value).ToList();
        Assertions.AssertThat(ids).Contains("fence");
        Assertions.AssertThat(ids).Contains("trial_tower");
    }

    /// <summary>D-083 ②：<b>影响区跟着走</b>（围栏没了摆摊范围 = 白盖了）。</summary>
    [TestCase]
    public void AffectsArea_SurvivesSaveAndLoad()
    {
        var (village, service) = NewScene();
        service.TryStart(BuildableBuildings.Fence, 1);
        service.AdvanceConstruction(workedHours: 100.0);

        var saved = SaveBuildings(village);

        var (village2, _) = NewScene();
        RestoreBuildings(village2, saved);

        var fence = village2.Buildings.First(b => b.Id.Value == "fence");
        Assertions.AssertThat(fence.AffectsAreaW).IsEqual(BuildableBuildings.Fence.AffectsAreaW);
        Assertions.AssertThat(fence.AffectsAreaH).IsEqual(BuildableBuildings.Fence.AffectsAreaH);
        // ⚠️ **真的能摆摊**：影响区覆盖默认摆摊位置。
        Assertions.AssertThat(fence.IsCellInAffectsArea(0, 0)).IsTrue();
    }

    /// <summary>D-083 ③：<b>在建的建筑会继续推进</b>（不能永远停在 40%）。</summary>
    [TestCase]
    public void UnderConstruction_ContinuesAfterLoad()
    {
        var (village, service) = NewScene();
        service.TryStart(BuildableBuildings.Fence, 1);
        service.AdvanceConstruction(workedHours: 2.0);   // 围栏：1×2/8 = 0.25

        var saved = SaveBuildings(village);
        var savedFence = saved.First(b => b.Id == "fence");
        Assertions.AssertThat(Math.Abs(savedFence.Progress - 0.25) < 0.001f).IsTrue();

        // ── 读档 ──
        var (village2, service2) = NewScene();
        var restored = RestoreBuildings(village2, saved);

        // ⚠️ **恢复施工状态**：只恢复进度而不恢复「谁在施工」⇒
        // 那栋楼会永远停在 25%（没人推进它）⇒ 玩家的工期白等了。
        service2.RestoreConstructions(restored.Where(i => i.ConstructionProgress < 1.0));
        Assertions.AssertThat(service2.ActiveConstructions.Count).IsEqual(1);

        service2.AssignWorkers(availableWorkers: 1);
        service2.AdvanceConstruction(workedHours: 6.0);   // 1×6/8 = 0.75 ⇒ 共 1.0

        Assertions.AssertThat(service2.ActiveConstructions.Count).IsEqual(0);
        var done = village2.Buildings.First(b => b.Id.Value == "fence");
        Assertions.AssertThat(done.ConstructionProgress).IsEqual(1.0);
    }

    /// <summary>校验器：<b>建筑字段坏了 ⇒ 拒读</b>（别造出一个「解释不了」的村庄）。</summary>
    [TestCase]
    public void Validator_RejectsBrokenBuildings()
    {
        var save = new SaveGame
        {
            Version = SaveVersion.Current,
            Village = new VillageSave { Id = "v1" },
            Heroes = new List<HeroSave>
            {
                new()
                {
                    Id = "h-alice", Name = "Alice", Personality = "莽", Diligence = "随手",
                    Power = 1, Speed = 1, Grit = 1,
                },
            },
        };

        // 进度越界。
        var badProgress = new SaveGame
        {
            Version = SaveVersion.Current,
            Village = new VillageSave
            {
                Id = "v1",
                Buildings = new List<BuildingSave>
                {
                    new() { Id = "fence", X = 1, Y = 1, W = 1, H = 1, Progress = 1.5 },
                },
            },
            Heroes = save.Heroes,
        };
        // ⚠️ **必须断言「是哪一条检查拒的」**：只断言「整体非法」的话，
        // 存档因为**别的原因**非法也会绿 ⇒ 守卫会变成装饰品
        // （实测踩过：关掉「进度」那条检查，测试照样绿 —— 因为它是被**另一处**检查拒的）。
        var progressErrors = SaveValidator.Validate(badProgress);
        Assertions.AssertThat(progressErrors.IsValid).IsFalse();
        Assertions.AssertThat(progressErrors.Errors.Any(e => e.Path.EndsWith(".progress"))).IsTrue();

        // 占地为 0。
        var badSize = new SaveGame
        {
            Version = SaveVersion.Current,
            Village = new VillageSave
            {
                Id = "v1",
                Buildings = new List<BuildingSave>
                {
                    new() { Id = "fence", X = 1, Y = 1, W = 0, H = 1, Progress = 0.5 },
                },
            },
            Heroes = save.Heroes,
        };
        var sizeErrors = SaveValidator.Validate(badSize);
        Assertions.AssertThat(sizeErrors.IsValid).IsFalse();
        Assertions.AssertThat(sizeErrors.Errors.Any(e => e.Path.EndsWith(".size"))).IsTrue();

        // 标识重复。
        var duplicate = new SaveGame
        {
            Version = SaveVersion.Current,
            Village = new VillageSave
            {
                Id = "v1",
                Buildings = new List<BuildingSave>
                {
                    new() { Id = "fence", X = 1, Y = 1, W = 1, H = 1, Progress = 1.0 },
                    new() { Id = "fence", X = 3, Y = 3, W = 1, H = 1, Progress = 1.0 },
                },
            },
            Heroes = save.Heroes,
        };
        var duplicateErrors = SaveValidator.Validate(duplicate);
        Assertions.AssertThat(duplicateErrors.IsValid).IsFalse();
        Assertions.AssertThat(duplicateErrors.Errors.Any(e => e.Path.EndsWith(".id"))).IsTrue();

        // 合法的一份 ⇒ 通过。
        var good = new SaveGame
        {
            Version = SaveVersion.Current,
            Village = new VillageSave
            {
                Id = "v1",
                Buildings = new List<BuildingSave>
                {
                    new() { Id = "fence", X = 1, Y = 1, W = 1, H = 1, Progress = 1.0, AffectsAreaW = 9, AffectsAreaH = 9 },
                },
            },
            Heroes = save.Heroes,
        };
        Assertions.AssertThat(SaveValidator.Validate(good).IsValid).IsTrue();
    }
}
