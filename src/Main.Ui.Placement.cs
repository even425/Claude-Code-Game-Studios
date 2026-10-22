using Godot;
using System;
using System.Linq;
using ClaudeCodeGameStudios.Domain.Village;

public partial class Main : Node2D
{
    /// <summary>
    /// <b>放置模式</b>（D-093）：玩家点了「建围栏 / 建试炼塔」之后，
    /// 下一次点地图上的格子 = 把建筑盖在那儿。
    /// </summary>
    /// <remarks>
    /// <para>⚠️ **为什么要有它**：围栏的<b>位置是有意义的</b> —— 它的「影响区」决定
    /// 摊位能摆到哪儿去 ⇒ 玩家当然该自己选（而不是固定在 (-3,-3)）。
    /// 基线 §12.3 说「建筑与村庄的关系【待定】」，但<b>「落在哪、不能跟什么重叠」这两条
    /// 早就存在</b>（§13.7 的占地冲突 + 摆摊用的影响区尺子）⇒ 这一版只把
    /// <b>「落点由玩家定」</b>这件事补上，不新造任何机制。</para>
    ///
    /// <para>⚠️ **画面上是「标记」而不是「房子」**：村庄的房子是手绘装饰、
    /// <b>不跟建筑实例走</b> ⇒ 若把落点画成房子，玩家会以为看到的房子的位置
    /// 就是真实格位 ⇒ 那是个谎。所以这里画一个<b>落在格位上的半透明标记</b>
    /// （合法 = 绿，非法 = 红）+ 围栏的<b>影响区预览</b>。</para>
    ///
    /// <para>⚠️ **一次只放一样**：放置中再点「建围栏」= 取消放置。</para>
    /// </remarks>
    private BuildableBuilding? _pendingPlacement;

    /// <summary>正在等玩家选落点的建筑（null = 不在放置中）。</summary>
    public BuildableBuilding? PendingPlacement => _pendingPlacement;

    /// <summary>
    /// 进入放置模式（点「建围栏 / 建试炼塔」时调用）。
    /// </summary>
    /// <param name="definition">要建什么。</param>
    /// <returns>进入了放置模式为 true；<b>已经在放置中</b>（这次点击 = 取消）为 false。</returns>
    private bool BeginPlacement(BuildableBuilding definition)
    {
        // 已经在放了 ⇒ 这次点击 = 取消（而不是换一样）。
        if (_pendingPlacement is not null)
        {
            CancelPlacement();
            return false;
        }

        _pendingPlacement = definition;
        GD.Print($"BUILD 选个地方放下{definition.Name}（左键放下 / 右键或 Esc 取消）。");
        Refresh();
        return true;
    }

    /// <summary>取消放置（什么都没发生）。</summary>
    private void CancelPlacement()
    {
        if (_pendingPlacement is null) return;
        GD.Print($"BUILD 取消放下{_pendingPlacement.Value.Name}。");
        _pendingPlacement = null;
        _village.ClearPlacementPreview();
        Refresh();
    }

    /// <summary>
    /// 在指定格位放下正在放置的建筑。
    /// </summary>
    /// <param name="gridX">格位 X。</param>
    /// <param name="gridY">格位 Y。</param>
    private void PlacePendingAt(int gridX, int gridY)
    {
        var pending = _pendingPlacement;
        if (pending is null) return;
        var definition = pending.Value;

        if (!_buildingService.CanPlaceAt(definition, gridX, gridY))
        {
            GD.Print($"BUILD 这儿放不下{definition.Name}（跟现有的建筑重叠，或不在村子的范围里）。");
            return;
        }

        // ⚠️ **一次只盖一样**（D-074）：施工队是同一批人 ⇒
        // 村庄升级在施工时，独立建筑拿不到人（停工）⇒ 两条路互斥。
        if (_buildTarget >= 0)
        {
            GD.Print($"BUILD {definition.Name} 开不了工（村庄正在升级，工地上有人）。");
            _pendingPlacement = null;
            _village.ClearPlacementPreview();
            Refresh();
            return;
        }

        int available = _heroes.Count(h => !h.Away && !h.ToSite && IsPresent(h));
        int workers = available / (_buildingService.ActiveConstructions.Count + 1);

        _pendingPlacement = null;
        _village.ClearPlacementPreview();

        if (!_buildingService.TryStartAt(definition, gridX, gridY, workers))
        {
            GD.Print($"BUILD {definition.Name} 开工失败（资源或落点不满足）。");
            Refresh();
            return;
        }

        GD.Print($"BUILD {definition.Name} 开工于 ({gridX}, {gridY})：{definition.GoldCost}金 {definition.MaterialCost}材料，"
                 + $"{workers} 人施工（一个人要 {definition.BaseBuildHours:0.#} 小时）。");
        Refresh();
    }

    /// <summary>
    /// 每帧更新放置预览（跟随鼠标）。
    /// </summary>
    /// <remarks>
    /// ⚠️ **为什么用「每帧轮询」而不是「鼠标移动事件」**：那要多接一个输入事件类型，
    /// 而这里要的是「鼠标在哪 ⇒ 预览在哪」⇒ 轮询更简单也更不容易漏状态。
    /// </remarks>
    private void UpdatePlacementPreview()
    {
        if (_pendingPlacement is null)
        {
            _village.ClearPlacementPreview();
            return;
        }

        if (!VillageView.TryGridFromLocal(_village.ToLocal(GetGlobalMousePosition()),
                                          out int gridX, out int gridY))
        {
            _village.ClearPlacementPreview();
            return;
        }

        bool valid = _buildingService.CanPlaceAt(_pendingPlacement.Value, gridX, gridY);
        _village.SetPlacementPreview(
            gridX, gridY, valid,
            _pendingPlacement.Value.AffectsAreaW, _pendingPlacement.Value.AffectsAreaH);
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        // ⚠️ **D-093：放置模式的鼠标操作**（左键放下 / 右键取消）。
        // ⚠️ **放在按键之前**：放置中按 Esc 应当**取消放置**，而不是触发别的快捷键。
        if (@event is InputEventMouseButton { Pressed: true } mb && PendingPlacement is not null)
        {
            if (mb.ButtonIndex == MouseButton.Right)
            {
                CancelPlacement();
                return;
            }

            if (mb.ButtonIndex == MouseButton.Left)
            {
                if (VillageView.TryGridFromLocal(_village.ToLocal(GetGlobalMousePosition()),
                                                out int gridX, out int gridY))
                {
                    PlacePendingAt(gridX, gridY);
                }
                return;
            }
        }

        if (@event is InputEventKey { Pressed: true } k)
        {
            if (k.Keycode == Key.Escape && PendingPlacement is not null)
            {
                CancelPlacement();
                return;
            }

            switch (k.Keycode)
            {
                case Key.F: OnSummonPaid(); break;
                case Key.R: OnTask(); break;
                case Key.Q: OnUpgrade(); break;
            }
        }
    }
}
