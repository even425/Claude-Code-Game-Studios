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
        //    市集在 (-5,-5)、影响区 11×11 ⇒ (-3,-3) 合法且没被占。
        int gx = -3;
        int gy = -3;
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

        bool ok = inPlacement && nothingStarted && legal && atChosenCell && nowBuilding
                  && illegal && nothingHappened;
        GD.Print($"BUILDTEST 落点由玩家定 ⇒ {(ok ? "PASS" : "FAIL")}");
    }
}
