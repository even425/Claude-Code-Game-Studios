using Godot;
using System;
using System.Linq;
using ClaudeCodeGameStudios.Domain.Village;

public partial class Main : Node2D
{
    /// <summary>
    /// <c>--build</c>：D-093 真机自检 —— <b>玩家自己选落点</b>这条链。
    /// </summary>
    /// <remarks>
    /// <para><b>验的是三件事</b>：
    /// <list type="number">
    /// <item>点「建围栏」⇒ <b>进入放置模式</b>（不再直接开工）。</item>
    /// <item>放到<b>合法</b>的格位 ⇒ 建筑真的盖在那里（<b>格位就是玩家点的那一格</b>）。</item>
    /// <item>放到<b>非法</b>的格位（跟已有建筑重叠 / 不在村子的范围里）⇒ <b>什么都不发生</b>。</item>
    /// </list></para>
    ///
    /// <para>⚠️ **为什么单测验不了**：这条链的「接线」在表现层（<c>Main</c>）
    /// ⇒ 单测只能验规则（<c>BuildingService.CanPlaceAt</c>）⇒
    /// <b>接线由本自检拉下来</b>（与 D-092 的战利品同款理由）。</para>
    /// </remarks>
    private void RunBuildSelfTest()
    {
        _buildTestDone = true;

        var fence = BuildableBuildings.Fence;

        // ⚠️ **夹具：先给村库备料备钱** —— 开局村库是 0 金 40 材料
        // ⇒ 「建围栏」会被 CanStart 挡掉（钱不够）⇒ 根本进不了放置模式。
        var goldResource = ClaudeCodeGameStudios.Application.Stall.StallService.Gold;
        _villageModel.Add(goldResource, 500);
        _villageModel.Add(new ResourceId("材料"), 500);

        // ① 点「建围栏」⇒ 进入放置模式（**不是直接开工**）。
        OnBuildFence();
        bool inPlacement = PendingPlacement is not null
            && PendingPlacement.Value.Id.Value == fence.Id.Value;
        bool nothingStarted = _buildingService.ActiveConstructions.Count == 0;
        GD.Print($"BUILDTEST 点「建围栏」后：放置模式={inPlacement}，还没开工={nothingStarted}");

        // ② 放到**合法**的格位：市集的影响区里、且没被占的地方。
        //    市集在 (-5,-5)、影响区 11×11 ⇒ (2,2) 合法、没被占，
        //    **而且在可见地图上**（0..14 × 0..9）⇒ 标记会画在地图里，看得见。
        int gx = 4;
        int gy = 1;
        bool legal = _buildingService.CanPlaceAt(fence, gx, gy);
        PlacePendingAt(gx, gy);

        var placed = _villageModel.Buildings
            .FirstOrDefault(b => b.Id.Value == fence.Id.Value);
        bool atChosenCell = placed is not null && placed.GridX == gx && placed.GridY == gy;
        bool nowBuilding = _buildingService.ActiveConstructions.Count == 1;

        GD.Print($"BUILDTEST 放到 ({gx}, {gy})：合法={legal}，建筑在那儿={atChosenCell}，在建={nowBuilding}"
                 + $"（放置模式已退出={PendingPlacement is null}）");

        // ③ 放到**非法**的格位 ⇒ 什么都不发生（没有新的建筑）。
        int buildingsBefore = _villageModel.Buildings.Count;
        bool illegal = !_buildingService.CanPlaceAt(fence, -5, -5);   // 市集自己那一格
        PlacePendingAt(-5, -5);
        bool nothingHappened = _villageModel.Buildings.Count == buildingsBefore;

        GD.Print($"BUILDTEST 放到 (-5, -5)（市集自己那格）：非法={illegal}，什么都没发生={nothingHappened}");

        // ④ 防护罩（D-095）：也能建，且**落点同样由玩家定**。
        var shield = ClaudeCodeGameStudios.Domain.Village.BuildableBuildings.Shield;
        OnBuildShield();
        bool shieldPlacing = PendingPlacement is not null
            && PendingPlacement.Value.Id.Value == shield.Id.Value;
        PlacePendingAt(5, 5);
        var placedShield = _villageModel.Buildings
            .FirstOrDefault(b => b.Id.Value == shield.Id.Value);
        bool shieldAtChosenCell = placedShield is not null
            && placedShield.GridX == 5 && placedShield.GridY == 5;
        GD.Print($"BUILDTEST 防护罩：进入放置模式={shieldPlacing}，建在 (5, 5)={shieldAtChosenCell}");

        // ④b 试炼塔：第三栋（三种建筑各盖一栋，地图上才看得出「长得不一样」）。
        var tower = ClaudeCodeGameStudios.Domain.Village.BuildableBuildings.TrialTower;
        OnBuildTower();
        PlacePendingAt(1, 5);
        var placedTower = _villageModel.Buildings
            .FirstOrDefault(b => b.Id.Value == tower.Id.Value);
        bool towerAtChosenCell = placedTower is not null
            && placedTower.GridX == 1 && placedTower.GridY == 5;
        GD.Print($"BUILDTEST 试炼塔：建在 (1, 5)={towerAtChosenCell}");

        // ④c **盖完**（D-097：要看的是「建成后的样子」，不是脚手架）。
        // ⚠️ 直接推进施工（同一个生产代码入口 `AdvanceConstruction`）——
        // 免得为了截图去跑几十个游戏日。
        _buildingService.AdvanceConstruction(workedHours: 100.0);
        int doneCount = _villageModel.Buildings.Count(b => b.Status ==
            ClaudeCodeGameStudios.Domain.Village.BuildingStatus.Completed);
        GD.Print($"BUILDTEST 盖完之后：已建成 {doneCount} 栋"
                 + $"（围栏/防护罩/试炼塔各一栋 ⇒ 地图上三样都得看得见）");

        bool ok = inPlacement && nothingStarted && legal && atChosenCell && nowBuilding
                  && illegal && nothingHappened && shieldPlacing && shieldAtChosenCell
                  && towerAtChosenCell && doneCount >= 3;
        GD.Print($"BUILDTEST 落点由玩家定 ⇒ {(ok ? "PASS" : "FAIL")}");

        // ⑤ D-097：地图上**三种建筑长得不一样**（接线层，单测验不到画法本身）。
        //    验的是「喂给视图的标记里，每栋建筑的画法都认识，且互不相同」。
        UpdateBuildingMarks();
        var shapes = _villageModel.Buildings
            .Where(b => b.Id.Value != "village_square")
            .Select(b => ClaudeCodeGameStudios.Presentation.BuildingVisual.ShapeFor(
                ClaudeCodeGameStudios.Domain.Village.BuildableBuildings.Find(b.Id)
                    ?? default))
            .ToArray();
        bool allKnown = shapes.All(ClaudeCodeGameStudios.Presentation.BuildingVisual.IsKnown);
        bool allDistinct = shapes.Distinct().Count() == shapes.Length;
        GD.Print($"BUILDTEST 建筑画法：{string.Join("/", shapes)} ⇒ 都有画法={allKnown} 互不相同={allDistinct}");
        GD.Print($"BUILDTEST 建筑长得不一样 ⇒ {(allKnown && allDistinct ? "PASS" : "FAIL")}");

        // ⑥ **D-113：试炼塔每天限一次**（接线层，单测验不到界面那条路）。
        //    ⚠️ **走的是界面的那个实现**（`RunTrialTower`）⇒ 这样「界面忘了传日号」
        //    这类接线错误也**抓得到**（直接调协调器会绕开它）。
        bool trialLimitOk = false;
        if (_heroes.Count > 0)
        {
            var challenger = _heroes[0];
            int today = (int)_clock.Day;

            // 第一次：界面那条路（它自己挑「第一个在场且空闲的英雄」）。
            var first = RunTrialTower();
            int bonusAfterFirst = challenger.TrialTowerHealthBonus;

            // 第二次（同一天）⇒ 规则必须拦下（**结果**必须是 SkippedToday）。
            var second = RunTrialTower();

            bool skipped = second == TrialTowerUiResult.SkippedToday;
            bool noNewBonus = challenger.TrialTowerHealthBonus == bonusAfterFirst;
            bool dayRecorded = challenger.LastTrialDay == today;

            trialLimitOk = skipped && noNewBonus && dayRecorded;
            GD.Print($"BUILDTEST D-113 试炼塔每天限一次：第一次={first} 第二次={second}（应为 SkippedToday）"
                     + $" 加成未变={noNewBonus}({bonusAfterFirst}→{challenger.TrialTowerHealthBonus})"
                     + $" 记下了日号={dayRecorded}({challenger.LastTrialDay})");
            GD.Print($"BUILDTEST D-113 试炼塔每天限一次 ⇒ {(trialLimitOk ? "PASS" : "FAIL")}");
        }
        else
        {
            GD.Print("BUILDTEST D-113 试炼塔每天限一次：村口没人 ⇒ 跳过（不参与判定）");
        }
    }
}
