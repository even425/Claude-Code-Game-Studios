using Godot;
using System;
using System.Collections.Generic;
using ClaudeCodeGameStudios.Application;
using WorldMap = ClaudeCodeGameStudios.Domain.Village.WorldMap;

/// <summary>
/// 村庄视图 —— 45° 俯视的等距地图（2:1 dimetric）。
///
/// 坐标：世界用格子 (x, y)，z 是高度。投影到屏幕
///     sx = (x - y) * TW/2
///     sy = (x + y) * TH/2 - z
/// 也就是 +x 朝右下、+y 朝左下、+z 朝上 —— 标准 2:1 等距。
///
/// 绘制顺序按 (x + y) 深度排序，所以人会走到建筑前面去、被建筑挡住。
/// 全部 _Draw 手绘，没有美术资源。
/// </summary>
public partial class VillageView : Node2D
{
    public const int MaxTier = 5;

    public int Tier { get; private set; }
    public bool HasPeople { get; set; }

    /// <summary>正在盖的东西：叫什么、进度 0..1。没盖时为 -1。</summary>
    public float BuildProgress { get; set; } = -1f;
    public string BuildName { get; set; } = "";

    private float _t;
    private readonly List<HeroAgent> _heroes = new();
    private readonly List<Prop> _props = new();      // 静止装饰，按深度一起排序
    private readonly Random _rng = new();

    private sealed class Prop
    {
        public float X, Y, Key;
        public Action<Prop>? Draw;
    }

    // ---- 调色板 ----
    private static readonly Color SkyTop   = new("1d2c4a");
    private static readonly Color SkyLow   = new("a87a5a");
    private static readonly Color Grass    = new("4a6b3a");
    private static readonly Color GrassAlt = new("557a41");
    /// <summary>村外的草地（D-117：比村里的暗一点 ⇒ 界线一眼可见）。</summary>
    private static readonly Color WildGrass = new("3c5730");
    private static readonly Color Path     = new("9c8760");
    private static readonly Color Water    = new("35627a");
    private static readonly Color Roof     = new("a34a3a");
    private static readonly Color RoofLux  = new("8a3a30");
    private static readonly Color Wall     = new("b08a63");
    private static readonly Color WallDark = new("8a6a4a");
    private static readonly Color Wood     = new("6a5a44");
    private static readonly Color WoodDark = new("3f3729");
    private static readonly Color Stone    = new("8a837b");
    private static readonly Color StoneTop = new("a9a29a");
    private static readonly Color Forge    = new("ff8a3a");
    private static readonly Color Water_   = new("4d7d94");
    private static readonly Color Banner   = new("c04a4a");
    private static readonly Color Leaf     = new("3f6b3c");
    private static readonly Color LeafLux  = new("4f8046");
    private static readonly Color Window   = new("f2d184");

    // 工地固定在村中心偏下，谁都在那儿干活
    public const float SiteX = 6.2f, SiteY = 5.6f;

    private const float TW = 64f, TH = 32f;   // 2:1
    private const float WALL_H = 46f;          // 一个标准墙高（像素）

    public override void _Process(double delta)
    {
        _t += (float)delta;
        // 技术债 A4（07 §4.1）的纠正点。原先这里是
        //   foreach (var h in _heroes) h.Tick((float)delta);
        // —— 表现层直接驱动领域，控制反转。现在表现层只把引擎帧 dt
        // 当作**数据**交给应用层的模拟驱动器（03 §2.2【已确认】）：
        // 「表现层不得直接调用 SimClock.Advance 或任何领域推进方法。
        //   它只能把引擎的 dt 作为输入数据交给应用层。」
        // 步内谁先谁后由应用层决定，本文件一个字都不参与。
        SimulationDriver.AdvanceFrame(delta);
        QueueRedraw();
    }

    /// <summary>模拟驱动器（null 时按 <see cref="SimulationDriver"/> 的默认值构造）。</summary>
    private ISimulationDriver? _simulationDriver;

    /// <summary>
    /// 模拟驱动器 —— 本视图把帧 dt 交给它，其余一律不管。
    ///
    /// <para>默认是【临时】适配器：逐帧把 dt 喂给 <c>HeroAgent.Tick</c>，
    /// 与迁移前逐值相同，这样这一小步迁移不必改 <c>Main.cs</c>
    /// 也能让原型照常运行。<c>Main.cs</c> 按 A1 拆分时用
    /// <see cref="SetSimulationDriver"/> 换成真正的
    /// <c>SimulationRunner</c>，临时适配器随之删除。</para>
    /// </summary>
    private ISimulationDriver SimulationDriver
        => _simulationDriver ??= PrototypeSimulationDriverFactory.Create(_heroes);

    /// <summary>
    /// 注入模拟驱动器。供 <c>Main.cs</c> 拆分后替换掉默认的临时适配器。
    /// </summary>
    /// <param name="driver">驱动器，非空。</param>
    /// <exception cref="ArgumentNullException"><paramref name="driver"/> 为 null。</exception>
    public void SetSimulationDriver(ISimulationDriver driver)
        => _simulationDriver = driver ?? throw new ArgumentNullException(nameof(driver));

    public void SetTier(int t) { Tier = Math.Clamp(t, 0, MaxTier); }
    public void AddHero(HeroAgent h) { _heroes.Add(h); }

    /// <summary>
    /// 清空已登记的英雄。<b>只给读档用</b> —— 读档要按存档重建整份花名册，
    /// 若只在追加，读出来的村庄会同时画着存档里的英雄与开局的三个。
    /// </summary>
    /// <remarks>
    /// 召唤（<c>AddHero</c>）是纯追加，没有「替换」语义；
    /// 而读档<b>必须</b>是替换 —— 半份旧英雄加半份存档英雄，
    /// 那种状态没有任何规则能解释它。
    /// </remarks>
    public void ClearHeroes() => _heroes.Clear();

    // ---------------------------------------------------------------- 投影

    public static Vector2 P(float x, float y, float z = 0f)
        => new Vector2((x - y) * (TW / 2f), (x + y) * (TH / 2f) - z);

    // ---------------------------------------------------------------- 放置预览（D-093）

    /// <summary>放置预览的格位（int.MinValue = 没有预览）。</summary>
    private int _previewX = int.MinValue;
    private int _previewY;
    private bool _previewValid;
    private int _previewAffectsW;
    private int _previewAffectsH;

    /// <summary>设置放置预览（格位 + 是否合法 + 影响区半径）。</summary>
    /// <param name="gridX">格位 X。</param>
    /// <param name="gridY">格位 Y。</param>
    /// <param name="valid">这个落点合法吗。</param>
    /// <param name="affectsW">影响区 X 半径。</param>
    /// <param name="affectsH">影响区 Y 半径。</param>
    public void SetPlacementPreview(int gridX, int gridY, bool valid, int affectsW, int affectsH)
    {
        _previewX = gridX;
        _previewY = gridY;
        _previewValid = valid;
        _previewAffectsW = affectsW;
        _previewAffectsH = affectsH;
        QueueRedraw();
    }

    /// <summary>清掉放置预览。</summary>
    public void ClearPlacementPreview()
    {
        if (_previewX == int.MinValue) return;
        _previewX = int.MinValue;
        QueueRedraw();
    }

    /// <summary>地图上已建成建筑的标记（D-093：放下之后要看得见）。</summary>
    private readonly List<VillageBuildingMark> _buildingMarks = new();

    /// <summary>一个已建成建筑在地图上的标记。</summary>
    /// <param name="GridX">格位 X。</param>
    /// <param name="GridY">格位 Y。</param>
    /// <param name="AffectsW">影响区 X 半径。</param>
    /// <param name="AffectsH">影响区 Y 半径。</param>
    /// <param name="Label">名字（画在标记上）。</param>
    /// <param name="Done">已建成（影响区已生效）。</param>
    /// <param name="Kind">
    /// 建筑种类（D-097：<b>要画成各自的形状</b>）。
    /// ⚠️ 之前只有「一块色块」⇒ 围栏 / 试炼塔 / 防护罩在地图上<b>长得一模一样</b>，
    /// 玩家根本看不出自己盖了什么。
    /// </param>
    /// <param name="Progress">建造进度（0..1）；未建完时按工地画。</param>
    /// <param name="IsRange">
    /// 这栋建筑是不是<b>「范围类」</b>（D-098：影响区 = 摊子能摆到哪儿）。
    /// ⚠️ 用来区分影响区的**颜色**：绿色 = 能摆摊，蓝色 = 罩住了但摆摊不算。
    /// </param>
    public readonly record struct VillageBuildingMark(
        int GridX, int GridY, int AffectsW, int AffectsH, string Label, bool Done = true,
        string Kind = "", double Progress = 1.0, bool IsRange = true);

    /// <summary>设置「地图上已建成的建筑」标记。</summary>
    /// <param name="marks">标记列表。</param>
    public void SetBuildingMarks(IReadOnlyList<VillageBuildingMark> marks)
    {
        _buildingMarks.Clear();
        if (marks is not null)
        {
            foreach (var m in marks) _buildingMarks.Add(m);
        }
        QueueRedraw();
    }

    /// <summary>村外的野兽（D-117）—— 视图只负责「照着画」。</summary>
    private readonly List<ClaudeCodeGameStudios.Domain.Wildlife.WildBeast> _beasts = new();

    /// <summary>设置「村外的野兽」（D-117）。</summary>
    /// <param name="beasts">野兽列表。</param>
    public void SetWildlife(IReadOnlyList<ClaudeCodeGameStudios.Domain.Wildlife.WildBeast> beasts)
    {
        _beasts.Clear();
        if (beasts is not null)
        {
            foreach (var b in beasts) _beasts.Add(b);
        }
        QueueRedraw();
    }

    /// <summary>
    /// 把本节点的局部坐标换回格位（D-093）。
    /// </summary>
    /// <param name="local">本节点局部坐标。</param>
    /// <param name="gridX">格位 X。</param>
    /// <param name="gridY">格位 Y。</param>
    /// <returns>转换成功为 true。</returns>
    /// <remarks>
    /// <para>⚠️ <b>逆投影</b>：正投影是 <c>sx = (x - y) * TW/2</c>、<c>sy = (x + y) * TH/2</c>
    /// ⇒ 反解：<code>x - y = sx / (TW/2)</code>、<code>x + y = sy / (TH/2)</code> ⇒ 两式相加相减再除 2。</para>
    /// <para>⚠️ <b>只算地面（z = 0）</b>：建筑落在地上、不在空中 ⇒ 高度不影响落点。</para>
    /// </remarks>
    public static bool TryGridFromLocal(Vector2 local, out int gridX, out int gridY)
    {
        float a = local.X / (TW / 2f);      // x - y
        float b = local.Y / (TH / 2f);      // x + y
        float gx = (a + b) / 2f;
        float gy = (b - a) / 2f;
        if (!float.IsFinite(gx) || !float.IsFinite(gy))
        {
            gridX = 0;
            gridY = 0;
            return false;
        }

        int gxi = (int)MathF.Round(gx);
        int gyi = (int)MathF.Round(gy);

        // ⚠️ **落点只能在可见的地图上**（D-093）。
        // 地板是 MapOrigin..MapOrigin+WorldMap.Width × MapOrigin..MapOrigin+WorldMap.Height（⚠️ D-117：地板有偏移）；
        // 村庄的影响区（市集的）较大，连到地板之外
        // ⇒ 若不限，玩家会在「空里」点一下就把楼房放了 ⇒ 地图上出现一个悬在半空的标记。
        if (gxi < WorldMap.OriginX || gxi >= WorldMap.OriginX + WorldMap.Width
            || gyi < WorldMap.OriginY || gyi >= WorldMap.OriginY + WorldMap.Height)
        {
            gridX = gxi;
            gridY = gyi;
            return false;   // 不在地图上 ⇒ 没有落点
        }

        gridX = gxi;
        gridY = gyi;
        return true;
    }

    /// <summary>
    /// 画「影响区」（**裁到可见地图内**）。
    /// </summary>
    /// <param name="gridX">中心格位 X。</param>
    /// <param name="gridY">中心格位 Y。</param>
    /// <param name="affectsW">影响区 X 半径。</param>
    /// <param name="affectsH">影响区 Y 半径。</param>
    /// <param name="color">颜色（含 alpha）。</param>
    /// <remarks>
    /// ⚠️ **为什么要裁**：市集的影响区很大（11×11，中心在 (-5,-5)）
    /// ⇒ 不裁的话，半透明区会覆盖到地图之外的「天空」里（看着像一层模糊的幕布）。
    /// </remarks>
    private void DrawAffectsArea(int gridX, int gridY, int affectsW, int affectsH, Color color)
    {
        if (affectsW <= 0 || affectsH <= 0) return;

        int minX = Math.Max(WorldMap.OriginX, gridX - (affectsW - 1));
        int maxX = Math.Min(WorldMap.OriginX + WorldMap.Width, gridX + affectsW);
        int minY = Math.Max(WorldMap.OriginY, gridY - (affectsH - 1));
        int maxY = Math.Min(WorldMap.OriginY + WorldMap.Height, gridY + affectsH);
        if (minX >= maxX || minY >= maxY) return;

        var area = new[]
        {
            P(minX, minY), P(maxX, minY), P(maxX, maxY), P(minX, maxY),
        };
        DrawPolygon(area, new[] { color });
    }

    /// <summary>画「已建成的建筑」：影响区（半透明）+ 一个带名字的标记。</summary>
    /// <remarks>
    /// ⚠️ <b>为什么要画</b>：玩家把围栏放下之后，影响区（摆摊能摆到哪儿）就是这次放置的价值
    /// ⇒ 看不见就等于没放。只画「标记」而不画「房子」：房子是手绘装饰、不跟格位走。
    /// </remarks>
/// <summary>画「已建成的建筑」的<b>影响区</b>（半透明，铺在地面上）。</summary>
    /// <remarks>
    /// ⚠️ <b>为什么要画</b>：玩家把围栏放下之后，影响区（摆摊能摆到哪儿）就是这次放置的价值
    /// ⇒ 看不见就等于没放。
    /// <para>⚠️ <b>只画影响区，不画「村庄那种房子」</b>：村庄的房子是手绘装饰、不跟格位走
    /// ⇒ 若把落点画成那种房子，玩家会以为看到的房子的位置就是真实格位 ⇒ 那是个谎。
    /// <b>但建筑本身（围栏 / 试炼塔 / 防护罩）必须画成它自己的形状</b>（D-097）——
    /// 它们<b>确实</b>在那个格位上 ⇒ 画它们不是撒谎。</para>
    /// </remarks>
    private void DrawBuildingAffectsAreas()
    {
        foreach (var m in _buildingMarks)
        {
            if (m.AffectsW <= 0 || m.AffectsH <= 0) continue;

            int minX = m.GridX - (m.AffectsW - 1);
            int maxX = m.GridX + (m.AffectsW - 1);
            int minY = m.GridY - (m.AffectsH - 1);
            int maxY = m.GridY + (m.AffectsH - 1);
            var area = new[]
            {
                P(minX, minY), P(maxX, minY), P(maxX + 1, maxY + 1), P(minX, maxY + 1),
            };
            // ⚠️ **颜色按「范围类 / 非范围类」分**（D-098）：
            // 绿色 = 「**摊子能摆在这儿**」（范围类建筑的影响区）；
            // 蓝色 = 「这是防护罩罩住的地方，但**摆摊不算**」。
            // ⇒ 若两种都画成绿色，玩家会以为防护罩里也能摆摊 ⇒ 那是个谎。
            var color = m.IsRange
                ? new Color(0.45f, 0.85f, 0.55f, 0.10f)
                : new Color(0.45f, 0.72f, 0.98f, 0.10f);
            DrawPolygon(area, new[] { color });
        }
    }

    /// <summary>画一栋建筑本身（按种类画成各自的形状）。</summary>
    /// <remarks>
    /// ⚠️ <b>为什么要区分种类</b>（D-097）：之前三种建筑都画成<b>同一块色块</b>
    /// ⇒ 玩家在地图上看不出自己盖的是围栏还是试炼塔 ⇒ 「看得见自己盖了什么」这件事是假的。
    /// <para>⚠️ <b>没建完 ⇒ 画脚手架</b>，建成 ⇒ 画成品。</para>
    /// </remarks>
    /// <param name="m">建筑标记。</param>
    private void DrawBuildingProp(VillageBuildingMark m)
    {
        float x = m.GridX;
        float y = m.GridY;

        if (!m.Done)
        {
            DrawScaffold(x, y, (float)Math.Clamp(m.Progress, 0.0, 1.0));
            return;
        }

        switch (m.Kind)
        {
            case ClaudeCodeGameStudios.Presentation.BuildingVisual.Fence:
                DrawFenceBuilding(x, y);
                break;
            case ClaudeCodeGameStudios.Presentation.BuildingVisual.TrialTower:
                DrawTowerBuilding(x, y);
                break;
            case ClaudeCodeGameStudios.Presentation.BuildingVisual.Shield:
                DrawShieldBuilding(x, y);
                break;
            case ClaudeCodeGameStudios.Presentation.BuildingVisual.Well:
                DrawWellBuilding(x, y);
                break;
            case ClaudeCodeGameStudios.Presentation.BuildingVisual.Blacksmith:
                DrawBlacksmithBuilding(x, y);
                break;
            default:
                DrawGenericBuilding(x, y);
                break;
        }
    }

    /// <summary>围栏：一圈木栅栏（它圈的是地，不是房子）。</summary>
    /// <remarks>
    /// ⚠️ **要看得见**：栅栏只有几根细木棍 ⇒ 在等距视角里几乎看不见
    /// ⇒ 玩家盖了围栏却看不出那儿有东西 ⇒ 画成**带横梁的实心矮墙**。
    /// </remarks>
    private void DrawFenceBuilding(float x, float y)
    {
        // 四条边：矮墙（上表面 + 两个侧面）。
        DrawFenceSide(x, y, 1f, 0f);        // 沿 x 方向的那条
        DrawFenceSide(x, y + 1f, 1f, 0f);
        DrawFenceSide(x, y, 0f, 1f);        // 沿 y 方向的那条
        DrawFenceSide(x + 1f, y, 0f, 1f);
        // 四角立柱（高一点，看得出是围起来的）。
        Box(x, y, 0.2f, 0.2f, 30f, WoodDark);
        Box(x + 0.8f, y, 0.2f, 0.2f, 30f, WoodDark);
        Box(x, y + 0.8f, 0.2f, 0.2f, 30f, WoodDark);
        Box(x + 0.8f, y + 0.8f, 0.2f, 0.2f, 30f, WoodDark);
    }

    /// <summary>围栏的一段（沿 dx / dy 方向的矮墙）。</summary>
    private void DrawFenceSide(float x, float y, float dx, float dy)
    {
        const float h = 18f;
        var col = new Color("8a6a4a");
        if (dx > 0f)
        {
            Box(x + 0.06f, y + 0.74f, 0.88f, 0.2f, h, col);
        }
        else
        {
            Box(x + 0.06f, y + 0.06f, 0.2f, 0.88f, h, col);
        }
    }

    /// <summary>试炼塔：一座石塔，顶部有旗。</summary>
    private void DrawTowerBuilding(float x, float y)
    {
        Box(x + 0.15f, y + 0.15f, 0.7f, 0.7f, 62f, Stone);
        Box(x + 0.25f, y + 0.25f, 0.5f, 0.5f, 68f, StoneTop);
        var apex = P(x + 0.5f, y + 0.5f, 92f);
        DrawPolygon(new[] { P(x, y, 68f), P(x + 1f, y, 68f), apex },
                    new[] { RoofLux, RoofLux, Darken(RoofLux, 0.1f) });
        DrawPolygon(new[] { P(x + 1f, y, 68f), P(x + 1f, y + 1f, 68f), apex },
                    new[] { Darken(RoofLux, 0.2f), Darken(RoofLux, 0.2f), Darken(RoofLux, 0.1f) });
        DrawPolygon(new[] { P(x + 1f, y + 1f, 68f), P(x, y + 1f, 68f), apex },
                    new[] { Darken(RoofLux, 0.3f), Darken(RoofLux, 0.3f), Darken(RoofLux, 0.1f) });
        DrawPolygon(new[] { P(x, y + 1f, 68f), P(x, y, 68f), apex },
                    new[] { Darken(RoofLux, 0.24f), Darken(RoofLux, 0.24f), Darken(RoofLux, 0.1f) });
        // 旗
        DrawLine(P(x + 0.5f, y + 0.5f, 92f), P(x + 0.5f, y + 0.5f, 118f), WoodDark, 3);
        DrawPolygon(new[] { P(x + 0.5f, y + 0.5f, 118f), P(x + 0.5f, y + 0.9f, 110f),
                            P(x + 0.5f, y + 0.5f, 104f) },
                    new[] { Banner, Banner, Banner });
    }

    /// <summary>防护罩：一层罩在格位上的光罩（它罩的是「外面来的东西」）。</summary>
    /// <remarks>
    /// ⚠️ **要看得见**：光罩是半透明的 ⇒ 在草地上几乎看不出来
    /// ⇒ 加一圈**发光的底座环** + 更亮的罩体 ⇒ 一眼能认出「这儿有个罩子」。
    /// </remarks>
    private void DrawShieldBuilding(float x, float y)
    {
        // 发光底座（比罩子大 ⇒ 一眼能看见「这儿有东西」）。
        var baseC = P(x + 0.5f, y + 0.5f, 0f);
        DrawCircle(baseC + new Vector2(0, -4), 30f, new Color(0.5f, 0.85f, 1.0f, 0.45f));
        DrawCircle(baseC + new Vector2(0, -4), 22f, new Color(0.75f, 0.95f, 1.0f, 0.55f));
        // 石台
        Box(x + 0.15f, y + 0.15f, 0.7f, 0.7f, 18f, Stone);
        // 罩子：两层半透明球（内层更亮）。
        var c = P(x + 0.5f, y + 0.5f, 30f);
        DrawCircle(c, 27f, new Color(0.55f, 0.82f, 0.98f, 0.38f));
        DrawCircle(c, 18f, new Color(0.7f, 0.92f, 1.0f, 0.40f));
        // 四角支架
        DrawLine(P(x + 0.1f, y + 0.1f, 18f), P(x + 0.1f, y + 0.1f, 54f), StoneTop, 3);
        DrawLine(P(x + 0.9f, y + 0.1f, 18f), P(x + 0.9f, y + 0.1f, 54f), StoneTop, 3);
        DrawLine(P(x + 0.1f, y + 0.9f, 18f), P(x + 0.1f, y + 0.9f, 54f), StoneTop, 3);
        DrawLine(P(x + 0.9f, y + 0.9f, 18f), P(x + 0.9f, y + 0.9f, 54f), StoneTop, 3);
    }

    /// <summary>不认识种类时的兜底：一块矮石台。</summary>
    private void DrawGenericBuilding(float x, float y)
    {
        Box(x + 0.15f, y + 0.15f, 0.7f, 0.7f, 30f, Stone);
    }

    /// <summary>井（D-099）：一口带辘轳的石井 —— <b>层级 2 就有它</b>。</summary>
    /// <remarks>
    /// ⚠️ 之前村里有这口井**只是手绘装饰**（<c>WellProp</c>，不跟格位走）
    /// ⇒ 现在它是<b>真的建筑实例</b> ⇒ 画在它自己的格位上。
    /// </remarks>
    private void DrawWellBuilding(float x, float y)
    {
        // 井台
        Box(x + 0.15f, y + 0.15f, 0.7f, 0.7f, 22f, Stone);
        // 井口（水面）
        var w = P(x + 0.5f, y + 0.5f, 23f);
        DrawPolygon(new[] { w + new Vector2(-13, -6), w + new Vector2(13, -6),
                            w + new Vector2(13, 6), w + new Vector2(-13, 6) },
                    new[] { Water_, Water_, Water_, Water_ });
        // 辘轳（两根柱 + 一根轴）
        DrawLine(P(x + 0.2f, y + 0.2f, 22f), P(x + 0.2f, y + 0.2f, 56f), Wood, 4);
        DrawLine(P(x + 0.8f, y + 0.8f, 22f), P(x + 0.8f, y + 0.8f, 56f), Wood, 4);
        DrawLine(P(x + 0.2f, y + 0.2f, 52f), P(x + 0.8f, y + 0.8f, 52f), WoodDark, 4);
        // 顶棚
        var apex = P(x + 0.5f, y + 0.5f, 70f);
        DrawPolygon(new[] { P(x - 0.05f, y - 0.05f, 56f), P(x + 1.05f, y + 1.05f, 56f), apex },
                    new[] { Roof, Roof, Roof });
        DrawPolygon(new[] { P(x + 1.05f, y - 0.05f, 56f), P(x - 0.05f, y + 1.05f, 56f), apex },
                    new[] { Darken(Roof, 0.2f), Darken(Roof, 0.2f), Darken(Roof, 0.1f) });
    }

    /// <summary>
    /// 铁匠铺（D-107，层级附带建筑）：一间带炉火的石屋 —— <b>第 4 级就有它</b>。
    /// </summary>
    /// <remarks>
    /// ⚠️ <b>要能一眼认出来</b>（D-097 的规矩）：石屋 + **跳动的炉火** + 屋前的**铁砧**
    /// ⇒ 玩家在地图上看到那团橙色的火光，就知道「这儿是铁匠铺」
    /// ⇒ 而不是一个「和别的建筑长得一样」的石台。
    /// </remarks>
    private void DrawBlacksmithBuilding(float x, float y)
    {
        // 石屋（比井高一点点，但明显矮于试炼塔 ⇒ 一眼分得清）。
        Box(x + 0.12f, y + 0.12f, 0.76f, 0.76f, 40f, Stone);
        Box(x + 0.22f, y + 0.22f, 0.56f, 0.56f, 44f, StoneTop);
        // 屋顶（两坡）。
        var apex = P(x + 0.5f, y + 0.5f, 66f);
        DrawPolygon(new[] { P(x + 0.05f, y + 0.05f, 44f), P(x + 0.95f, y + 0.95f, 44f), apex },
                    new[] { Roof, Roof, Roof });
        DrawPolygon(new[] { P(x + 0.95f, y + 0.05f, 44f), P(x + 0.05f, y + 0.95f, 44f), apex },
                    new[] { Darken(Roof, 0.2f), Darken(Roof, 0.2f), Darken(Roof, 0.1f) });
        // 炉火：门口一团橙红的光（D-097：认得出比画得细更要紧）。
        var fire = P(x + 0.5f, y + 0.5f, 42f);
        DrawCircle(fire + new Vector2(0, -3), 9f, new Color(1.0f, 0.55f, 0.15f, 0.85f));
        DrawCircle(fire + new Vector2(0, -5), 5f, new Color(1.0f, 0.88f, 0.45f, 0.95f));
        // 铁砧（屋前一块矮墩 + 上面的砧面）。
        var anvil = P(x + 0.5f, y + 0.5f, 16f);
        DrawCircle(anvil + new Vector2(0, -2), 7f, new Color(0.28f, 0.29f, 0.33f, 0.95f));
        DrawLine(P(x + 0.34f, y + 0.34f, 20f), P(x + 0.66f, y + 0.66f, 20f), WoodDark, 4);
    }

    /// <summary>
    /// 画一只<b>村外的野兽</b>（D-117）。
    /// </summary>
    /// <remarks>
    /// ⚠️ **只按「种类」画**：不画血条、不画名字 ——
    /// 它是**野生世界的一部分**，不是 UI 元素 ⇒ 画面上只该有「那边有活物」的感觉。
    /// <br>⚠️ <b>相位让它在轻轻动</b>（走路时的上下起伏）⇒ 静止的野兽看着像贴图。
    /// </remarks>
    private void DrawBeast(ClaudeCodeGameStudios.Domain.Wildlife.WildBeast beast)
    {
        Color body = beast.Kind switch
        {
            ClaudeCodeGameStudios.Domain.Wildlife.BeastKind.Boar => new Color("6b4f35"),
            ClaudeCodeGameStudios.Domain.Wildlife.BeastKind.Wolf => new Color("7a7f86"),
            _ => new Color("b08a63"),
        };

        // 走路的起伏（相位 0..1 ⇒ 一个来回）。
        float bob = (float)Math.Sin(beast.Phase * MathF.PI * 2f) * 2.5f;
        float x = beast.X + 0.5f;
        float y = beast.Y + 0.5f;

        // 身子：一个小菱形（等距投影的地面块）。
        var c00 = P(x - 0.34f, y - 0.24f, 12f + bob);
        var c10 = P(x + 0.18f, y - 0.34f, 12f + bob);
        var c11 = P(x + 0.34f, y + 0.18f, 12f + bob);
        var c01 = P(x - 0.18f, y + 0.34f, 12f + bob);
        DrawPolygon(new[] { c00, c10, c11, c01 },
                    new[] { Lighten(body, 0.18f), Lighten(body, 0.18f), body, body });

        // 头：往前探一点（+x 方向）。
        var head = P(x + 0.30f, y + 0.06f, 16f + bob);
        DrawCircle(head, 7f, body);

        // 四条腿：细线，站着不动 ⇒ 靠 bob 做出「在走」的感觉。
        Color leg = Darken(body, 0.35f);
        DrawLine(P(x - 0.18f, y - 0.10f, 12f + bob), P(x - 0.18f, y - 0.10f, 0f), leg, 2);
        DrawLine(P(x + 0.10f, y - 0.10f, 12f + bob), P(x + 0.10f, y - 0.10f, 0f), leg, 2);
        DrawLine(P(x - 0.10f, y + 0.18f, 12f + bob), P(x - 0.10f, y + 0.18f, 0f), leg, 2);
        DrawLine(P(x + 0.16f, y + 0.18f, 12f + bob), P(x + 0.16f, y + 0.18f, 0f), leg, 2);

        // 鹿有角（一眼能认出是「能抓的那只」）。
        if (beast.Kind == ClaudeCodeGameStudios.Domain.Wildlife.BeastKind.Deer)
        {
            DrawLine(P(x + 0.34f, y + 0.02f, 22f + bob), P(x + 0.44f, y - 0.06f, 30f + bob), leg, 2);
            DrawLine(P(x + 0.34f, y + 0.10f, 22f + bob), P(x + 0.44f, y + 0.02f, 30f + bob), leg, 2);
        }
    }

    /// <summary>施工中的建筑：脚手架（进度越高墙越完整）。</summary>
    private void DrawScaffold(float x, float y, float progress)
    {
        float post = 8f + progress * 30f;
        var postCol = new Color("9c8560");
        Box(x, y, 0.2f, 0.2f, post, postCol);
        Box(x + 0.8f, y, 0.2f, 0.2f, post, postCol);
        Box(x, y + 0.8f, 0.2f, 0.2f, post, postCol);
        Box(x + 0.8f, y + 0.8f, 0.2f, 0.2f, post, postCol);
        if (progress > 0.35f)
        {
            float wallH = (progress - 0.35f) / 0.65f * 26f;
            Box(x + 0.2f, y, 0.6f, 0.18f, wallH, new Color("b0a07c"));
            Box(x + 0.2f, y + 0.82f, 0.6f, 0.18f, wallH, new Color("b0a07c"));
        }
    }

    /// <summary>画放置预览（合法 = 绿，非法 = 红）+ 影响区。</summary>
    private void DrawPlacementPreview()
    {
        if (_previewX == int.MinValue) return;

        // 影响区预览（半透明的大圆——围栏的作用就是「围墙能围到哪儿」）。
        DrawAffectsArea(_previewX, _previewY, _previewAffectsW, _previewAffectsH,
            new Color(0.45f, 0.85f, 0.55f, 0.14f));

        var cell = new[]
        {
            P(_previewX, _previewY), P(_previewX + 1, _previewY),
            P(_previewX + 1, _previewY + 1), P(_previewX, _previewY + 1),
        };
        var color = _previewValid ? new Color(0.45f, 0.9f, 0.55f, 0.5f) : new Color(0.9f, 0.35f, 0.3f, 0.5f);
        DrawPolygon(cell, new[] { color });
    }

    // ---------------------------------------------------------------- 主绘制

    public override void _Draw()
    {
        DrawSky();
        DrawGroundPlate();

        EnsureLayout();

        // 深度排序：x+y 越大越靠前
        var items = new List<(float key, Action draw)>();
        if (BuildProgress >= 0f)
        {
            float bp = BuildProgress;
            items.Add((SiteX + SiteY, () => BuildSite(SiteX, SiteY, bp)));
        }
        foreach (var b in _village) items.Add((b.X + b.Y, () => b.Draw!(this)));
        foreach (var h in _heroes) if (h.Present) items.Add((h.MapX + h.MapY, () => ClaudeCodeGameStudios.Presentation.HeroRenderer.Draw(this, h)));
        // ⚠️ **玩家自己盖的建筑也要参与深度排序**（D-097）：
        // 否则它永远画在最上层 ⇒ 站在它「后面」的英雄会被它盖住。
        foreach (var m in _buildingMarks) items.Add((m.GridX + m.GridY, () => DrawBuildingProp(m)));
        // ⚠️ **D-117：村外的野兽也参与深度排序** —— 否则它永远在最后面 ⇒
        // 走到树后面的野兽会被树挡住（而它本来该在树前面/后面按位置算）。
        foreach (var b in _beasts) items.Add((b.X + b.Y, () => DrawBeast(b)));

        items.Sort((a, b) => a.key.CompareTo(b.key));
        foreach (var it in items) it.draw();

        // 已建成建筑的<b>影响区</b>先画（它是铺在地面上的半透明层）。
        DrawBuildingAffectsAreas();

        // 放置预览画在最上层（否则会被地面的楼房遮住）。
        DrawPlacementPreview();
    }

    private sealed class Item { public float X, Y; public Action<VillageView>? Draw; }
    private readonly List<Item> _village = new();
    private int _builtForTier = -1;

    /// <summary>布局只建一次 —— 每帧重建会让树的位置乱跳。</summary>
    private int _lastBuildState = -1;

    private void EnsureLayout()
    {
        int st = BuildProgress >= 0f ? 1 : 0;
        if (_builtForTier == Tier && _lastBuildState == st && _village.Count > 0) return;
        _builtForTier = Tier;
        _lastBuildState = st;
        BuildVillage();
    }

    // ---------------------------------------------------------------- 背景

    private void DrawSky()
    {
        // 傍晚的天：顶上一片深蓝，往下转暖
        const int steps = 56;
        for (int i = 0; i < steps; i++)
        {
            float t = i / (float)(steps - 1);
            var c = SkyTop.Lerp(SkyLow, t * t * t);
            DrawRect(new Rect2(-600, -600 + i * 26, 2600, 27), c);
        }
    }

    private void DrawGroundPlate()
    {
        // 整块地：一个菱形（⚠️ D-117：地板有**原点偏移**，村子在中间、四周是野外）
        var quad = new[]
        {
            P(WorldMap.OriginX, WorldMap.OriginY),
            P(WorldMap.OriginX + WorldMap.Width, WorldMap.OriginY),
            P(WorldMap.OriginX + WorldMap.Width, WorldMap.OriginY + WorldMap.Height),
            P(WorldMap.OriginX, WorldMap.OriginY + WorldMap.Height),
        };
        DrawPolygon(quad, new[] { Grass, Grass, Grass, Grass });

        // 地面格子（隔一个画一条深色缝，地块感）
        // ⚠️ **D-117：村外用另一种底色** —— 玩家一眼能看出「村子」与「野外」的界线。
        for (int x = WorldMap.OriginX; x < WorldMap.OriginX + WorldMap.Width; x++)
            for (int y = WorldMap.OriginY; y < WorldMap.OriginY + WorldMap.Height; y++)
            {
                bool wild = WorldMap.IsWilderness(x, y);
                if (!wild && (x + y) % 2 != 0) continue;   // 村里照旧：隔一个画一条
                var c = new[] { P(x, y), P(x + 1, y), P(x + 1, y + 1), P(x, y + 1) };
                var col = wild ? WildGrass : GrassAlt;
                DrawPolygon(c, new[] { col, col, col, col });
            }

        // 土路：从村口斜穿过去
        DrawIsoPath(new[] { new Vector2(1, 8), new Vector2(5, 6), new Vector2(9, 4), new Vector2(14, 1) });
        DrawIsoPath(new[] { new Vector2(7.5f, 6), new Vector2(8, 2) });

        // 边沿：让地图有厚度感
        DrawThickEdges(quad);
    }

    private void DrawIsoPath(System.Collections.Generic.IReadOnlyList<Vector2> pts)
    {
        for (int i = 0; i < pts.Count - 1; i++)
        {
            var a = pts[i]; var b = pts[i + 1];
            var steps = 8;
            for (int s = 0; s <= steps; s++)
            {
                float t = s / (float)steps;
                float px = Mathf.Lerp(a.X, b.X, t), py = Mathf.Lerp(a.Y, b.Y, t);
                var c = new[] { P(px - 0.42f, py - 0.42f), P(px + 0.42f, py - 0.42f),
                                P(px + 0.42f, py + 0.42f), P(px - 0.42f, py + 0.42f) };
                DrawPolygon(c, new[] { Path, Path, Path, Path });
            }
        }
    }

    private void DrawThickEdges(Vector2[] q)
    {
        // 左右两条边向下压，做出"地块有厚度"
        for (int i = 0; i < 2; i++)
        {
            var a = q[0]; var b = q[1];
            if (i == 1) { a = q[3]; b = q[2]; }
            var c = new[] { a, b, b + new Vector2(0, 14), a + new Vector2(0, 14) };
            DrawPolygon(c, new[] { new Color("2a3a24"), new Color("2a3a24"), new Color("1c2618"), new Color("1c2618") });
        }
    }

    // ---------------------------------------------------------------- 立体块

    /// <summary>画一个 2:1 等距的方块：顶面 + 两个侧面，带受光。</summary>
    public void Box(float x, float y, float w, float d, float h, Color col)
    {
        var c00 = P(x, y, h);
        var c10 = P(x + w, y, h);
        var c11 = P(x + w, y + d, h);
        var c01 = P(x, y + d, h);
        // 顶面
        DrawPolygon(new[] { c00, c10, c11, c01 }, new[] { Lighten(col, 0.26f), Lighten(col, 0.26f), Lighten(col, 0.26f), Lighten(col, 0.26f) });
        // 右前面（x+w 那一面）
        var b0 = P(x + w, y, 0); var b1 = P(x + w, y + d, 0);
        DrawPolygon(new[] { c10, c11, b1, b0 }, new[] { Lighten(col, 0.04f), Lighten(col, 0.04f), Darken(col, 0.34f), Darken(col, 0.34f) });
        // 左前面（y+d 那一面）
        var d0 = P(x, y + d, 0);
        DrawPolygon(new[] { c01, c11, b1, d0 }, new[] { col, col, Darken(col, 0.16f), Darken(col, 0.16f) });
    }

    private static Color Lighten(Color c, float t) => new Color(
        Mathf.Min(1f, c.R + t), Mathf.Min(1f, c.G + t), Mathf.Min(1f, c.B + t), c.A);
    private static Color Darken(Color c, float t) => new Color(c.R * (1 - t), c.G * (1 - t), c.B * (1 - t), c.A);

    /// <summary>一个带斜屋顶的小屋。屋脊沿 x 方向，在 y + 0.75 处。</summary>
    private void Hut(float x, float y, bool upgraded)
    {
        const float w = 1.9f, d = 1.5f;
        Box(x, y, w, d, WALL_H, upgraded ? Wall : WallDark);

        float ridge = WALL_H + 30f;
        float ov = 0.24f;                       // 屋檐外挑
        float x0 = x - ov, x1 = x + w + ov;
        float yA = y, yR = y + d / 2f, yB = y + d;
        var roof = Tier >= 4 ? RoofLux : Roof;

        // 迎向观众的两片坡：+y 那片（前）和 -y 那片（后）
        DrawPolygon(new[] { P(x0, yR, ridge), P(x1, yR, ridge),
                            P(x1, yB, WALL_H), P(x0, yB, WALL_H) },
                    new[] { roof, roof, Darken(roof, 0.22f), Darken(roof, 0.22f) });
        DrawPolygon(new[] { P(x0, yR, ridge), P(x1, yR, ridge),
                            P(x1, yA, WALL_H), P(x0, yA, WALL_H) },
                    new[] { Lighten(roof, 0.14f), Lighten(roof, 0.14f), roof, roof });
        // 两面山墙
        DrawPolygon(new[] { P(x0, yA, WALL_H), P(x0, yB, WALL_H), P(x0, yR, ridge) },
                    new[] { Lighten(Wall, 0.10f), Lighten(Wall, 0.10f), Lighten(roof, 0.10f) });
        DrawPolygon(new[] { P(x1, yA, WALL_H), P(x1, yB, WALL_H), P(x1, yR, ridge) },
                    new[] { Darken(Wall, 0.26f), Darken(Wall, 0.26f), Darken(roof, 0.12f) });

        // 门：贴在 +y 那一面
        var door = P(x + w / 2f, yB, 0);
        DrawPolygon(new[] { door + new Vector2(-8, -24), door + new Vector2(8, -24),
                            door + new Vector2(8, 0), door + new Vector2(-8, 0) },
                    new[] { WoodDark, WoodDark, WoodDark, WoodDark });
        if (Tier >= 2)
        {
            var win = P(x + 0.45f, yB, 26);
            DrawPolygon(new[] { win + new Vector2(-6, -6), win + new Vector2(6, -6),
                                win + new Vector2(6, 6), win + new Vector2(-6, 6) },
                        new[] { Window, Window, Window, Window });
        }
    }

    private void Tree(float x, float y, float s)
    {
        Box(x + 0.35f, y + 0.35f, 0.3f, 0.3f, 14f * s, WoodDark);
        var c = P(x + 0.5f, y + 0.5f, 14f * s + 16f * s);
        DrawCircle(c, 15f * s, Leaf);
        DrawCircle(c + new Vector2(-7, 5), 10f * s, Darken(Leaf, 0.22f));
        DrawCircle(c + new Vector2(8, 3), 9f * s, Lighten(Leaf, 0.08f));
    }

    private void WellProp(float x, float y)
    {
        Box(x + 0.2f, y + 0.2f, 0.9f, 0.9f, 20f, Stone);
        var w = P(x + 0.65f, y + 0.65f, 21f);
        DrawPolygon(new[] { w + new Vector2(-11, -5), w + new Vector2(11, -5),
                            w + new Vector2(11, 5), w + new Vector2(-11, 5) },
                    new[] { Water_, Water_, Water_, Water_ });
        // 立柱与顶
        DrawLine(P(x + 0.2f, y + 0.2f, 20), P(x + 0.2f, y + 0.2f, 58), Wood, 4);
        DrawLine(P(x + 1.1f, y + 1.1f, 20), P(x + 1.1f, y + 1.1f, 58), Wood, 4);
        var apex = P(x + 0.65f, y + 0.65f, 74);
        DrawPolygon(new[] { P(x - 0.1f, y - 0.1f, 56), P(x + 1.4f, y + 1.4f, 56), apex },
                    new[] { Roof, Roof, Roof });
        DrawPolygon(new[] { P(x + 1.4f, y - 0.1f, 56), P(x - 0.1f, y + 1.4f, 56), apex },
                    new[] { Darken(Roof, 0.2f), Darken(Roof, 0.2f), Darken(Roof, 0.1f) });
    }

    private void SmithyProp(float x, float y)
    {
        Box(x, y, 2.2f, 1.8f, 56f, new Color("6a5348"));
        var ridge = 56f + 24f;
        DrawPolygon(new[] { P(x - 0.2f, y, 56), P(x + 2.2f, y + 0.9f, 56), P(x + 1.0f, y + 0.9f, ridge), P(x - 0.2f, y + 0.9f, ridge) },
                    new[] { new Color("463830"), new Color("463830"), new Color("463830"), new Color("463830") });
        DrawPolygon(new[] { P(x - 0.2f, y + 0.9f, 56), P(x + 2.2f, y + 0.9f, 56), P(x + 2.4f, y + 1.8f, 56), P(x - 0.2f, y + 1.8f, 56) },
                    new[] { new Color("3a2e28"), new Color("3a2e28"), new Color("3a2e28"), new Color("3a2e28") });
        DrawPolygon(new[] { P(x - 0.2f, y + 0.9f, 56), P(x - 0.2f, y + 1.8f, 56), P(x + 1.0f, y + 0.9f, ridge) },
                    new[] { new Color("55443a"), new Color("55443a"), new Color("6a564a") });
        // 炉火
        float pulse = 0.7f + 0.3f * MathF.Sin(_t * 7.3f);
        var f = P(x + 1.0f, y + 1.8f, 14);
        DrawCircle(f, 15f * pulse, new Color(Forge.R, Forge.G, Forge.B, 0.35f));
        DrawCircle(f, 8f * pulse, Forge);
        DrawCircle(f, 4f, new Color("ffeec0"));
    }

    private void TavernProp(float x, float y)
    {
        Box(x, y, 2.4f, 1.8f, 62f, Wall);
        var ridge = 62f + 28f;
        DrawPolygon(new[] { P(x - 0.2f, y - 0.2f, 62), P(x + 2.6f, y + 2.0f, 62),
                            P(x + 1.2f, y + 0.9f, ridge), P(x + 0.0f, y - 0.2f, ridge) },
                    new[] { Roof, Roof, Roof, Roof });
        // 招牌
        DrawLine(P(x + 2.4f, y + 1.8f, 34), P(x + 2.4f, y + 1.8f, 58), WoodDark, 3);
        DrawPolygon(new[] { P(x + 2.4f, y + 1.8f, 58), P(x + 2.4f, y + 2.3f, 58),
                            P(x + 2.4f, y + 2.3f, 50), P(x + 2.4f, y + 1.8f, 50) },
                    new[] { Banner, Banner, Banner, Banner });
    }

    private void BannerProp(float x, float y, float h)
    {
        var baseP = P(x, y, 0);
        DrawLine(baseP, P(x, y, h), Wood, 4);
        var flap = MathF.Sin(_t * 2.2f + x) * 3;
        DrawPolygon(new[] { P(x, y, h), P(x, y, h - 16), P(x, y + 0.7f, h - 8 + flap), P(x, y + 0.7f, h - 6) },
                    new[] { Banner, Banner, Banner, Banner });
    }

    private void FenceProp(float x, float y)
    {
        var a = P(x, y, 0); var b = P(x + 0.9f, y, 0);
        DrawLine(a, a + new Vector2(0, -18), Wood, 3);
        DrawLine(b, b + new Vector2(0, -18), Wood, 3);
        DrawLine(a + new Vector2(0, -14), b + new Vector2(0, -14), Wood, 2);
        DrawLine(a + new Vector2(0, -7), b + new Vector2(0, -7), Wood, 2);
    }

    /// <summary>建到一半的工地。进度越高架子越高，墙越完整。</summary>
    private void BuildSite(float x, float y, float progress)
    {
        // 地基
        var baseQ = new[] { P(x, y), P(x + 1.8f, y), P(x + 1.8f, y + 1.6f), P(x, y + 1.6f) };
        DrawPolygon(baseQ, new[] { new Color("5a4a38"), new Color("5a4a38"), new Color("5a4a38"), new Color("5a4a38") });

        // 四角立柱，高度随进度长
        float post = 8f + progress * 34f;
        var postCol = new Color("9c8560");
        Box(x, y, 0.22f, 0.22f, post, postCol);
        Box(x + 1.58f, y, 0.22f, 0.22f, post, postCol);
        Box(x, y + 1.38f, 0.22f, 0.22f, post, postCol);
        Box(x + 1.58f, y + 1.38f, 0.22f, 0.22f, post, postCol);

        // 墙：进度到 1/3 才起，到 2/3 才上梁
        if (progress > 0.3f)
        {
            float wallH = (progress - 0.3f) / 0.4f * 30f;
            Box(x + 0.22f, y, 1.36f, 0.2f, wallH, new Color("b0a07c"));
            Box(x + 0.22f, y + 1.38f, 1.36f, 0.2f, wallH, new Color("b0a07c"));
        }
        // 梁
        if (progress > 0.7f)
        {
            float beamH = (progress - 0.7f) / 0.3f * 26f;
            Box(x - 0.1f, y - 0.1f, 2.0f, 1.8f, beamH, new Color("8a6a4a"));
        }
        // 料堆
        var pile = P(x + 0.4f, y + 0.5f, 0);
        DrawCircle(pile, 7f, new Color("7a6a52"));
        var pile2 = P(x + 0.5f, y + 0.6f, 4f);
        DrawCircle(pile2, 5f, new Color("8a7a5f"));
    }

    // ---------------------------------------------------------------- 装饰

    private void Bush(float x, float y, float s)
    {
        var c = P(x + 0.5f, y + 0.5f, 8f * s);
        DrawCircle(c + new Vector2(-6, 0), 8f * s, Darken(Leaf, 0.18f));
        DrawCircle(c + new Vector2(6, 1), 7f * s, Leaf);
        DrawCircle(c + new Vector2(0, -4), 7f * s, Lighten(Leaf, 0.10f));
    }

    private void Rock(float x, float y, float s)
    {
        var c = P(x + 0.5f, y + 0.5f, 5f * s);
        var col = new Color("7a746c");
        DrawCircle(c, 9f * s, col);
        DrawCircle(c + new Vector2(-3, 3), 6f * s, Darken(col, 0.28f));
        DrawCircle(c + new Vector2(3, -2), 4f * s, Lighten(col, 0.20f));
    }

    /// <summary>一小块地。招来人之后才有地可种。</summary>
    private void FarmPlot(float x, float y, float w, float d)
    {
        var quad = new[] { P(x, y), P(x + w, y), P(x + w, y + d), P(x, y + d) };
        DrawPolygon(quad, new[] { new Color("6b533a"), new Color("6b533a"), new Color("6b533a"), new Color("6b533a") });
        // 垄
        for (float i = 0; i < d; i += 0.5f)
        {
            var a = P(x, y + i); var b = P(x + w, y + i);
            DrawLine(a, b, new Color("4f3d29"), 2f);
        }
    }

    private void Pond(float x, float y, float w, float d)
    {
        var quad = new[] { P(x, y), P(x + w, y), P(x + w, y + d), P(x, y + d) };
        DrawPolygon(quad, new[] { new Color("3f7286"), new Color("3f7286"), new Color("3f7286"), new Color("3f7286") });
        var mid = P(x + w / 2f, y + d / 2f, 0);
        DrawCircle(mid, 7f, new Color(0.75f, 0.88f, 0.95f, 0.35f));
    }

    /// <summary>建了一半的工地 —— 盖到一半的墙，能看出是"人盖的"。</summary>
    private void Site(float x, float y)
    {
        var r = new Random((int)(x * 31 + y * 17));
        for (int i = 0; i < 4; i++)
        {
            float bx = x + 0.2f + i * 0.45f, by = y + 0.2f + r.NextSingle() * 0.4f;
            Box(bx, by, 0.35f, 0.35f, 8f + r.NextSingle() * 26f, new Color("9c8560"));
        }
    }

    // ---------------------------------------------------------------- 村庄布局

    private void BuildVillage()
    {
        _village.Clear();
        void Add(float x, float y, Action<VillageView> d) => _village.Add(new Item { X = x, Y = y, Draw = d });

        bool occupied(float x, float y) =>
            (x > 1.5f && x < 3.5f && y < 2.5f) || (x > 7.5f && x < 9.5f && y > 2.5f && y < 4.5f) ||
            (x > 11.5f && x < 13.5f && y > 5.5f && y < 7.5f) || (x > 0 && x < 2.5f && y > 4.5f && y < 6.5f) ||
            (x > 2.5f && x < 4.5f && y > 5.5f && y < 7.5f) || (x > 5.5f && x < 7f && y < 1.5f) ||
            (x > 9.5f && x < 11.5f && y < 2.5f) || (x > 3 && x < 7 && y > 5.5f && y < 8.5f) ||
            (x > 0 && x < 3 && y > 8 && y < 10);

        // 树 —— 铺满全图，别都挤在边上。
        // ⚠️ **D-117：村子之外（野外）也要有树** —— 玩家要能一眼看出「那边是野外」。
        // 注意：随机值必须在【建布局时】取好并捕获；放进 lambda 里就是绘制时求值，
        // 那样树会每帧变一次大小。
        for (int i = 0; i < 60; i++)
        {
            float x = WorldMap.OriginX + _rng.Next(0, WorldMap.Width - 1), y = WorldMap.OriginY + _rng.Next(0, WorldMap.Height - 1);
            if (occupied(x, y)) continue;
            float s = 0.75f + _rng.NextSingle() * 0.7f;
            Add(x, y, v => v.Tree(x, y, s));
        }
        for (int i = 0; i < 26; i++)
        {
            float x = WorldMap.OriginX + _rng.Next(0, WorldMap.Width - 1), y = WorldMap.OriginY + _rng.Next(0, WorldMap.Height - 1);
            if (occupied(x, y)) continue;
            float s = 0.8f + _rng.NextSingle() * 0.6f;
            Add(x, y, v => v.Bush(x, y, s));
        }
        for (int i = 0; i < 12; i++)
        {
            float x = WorldMap.OriginX + _rng.Next(0, WorldMap.Width - 1), y = WorldMap.OriginY + _rng.Next(0, WorldMap.Height - 1);
            if (occupied(x, y)) continue;
            float s = 0.7f + _rng.NextSingle() * 0.5f;
            Add(x, y, v => v.Rock(x, y, s));
        }

        Add(12.6f, 8.4f, v => v.Pond(12.6f, 8.4f, 2.2f, 1.4f));
        if (Tier >= 2) Add(3.2f, 6.0f, v => v.FarmPlot(3.2f, 6.0f, 3.6f, 2.6f));

        // 建筑摊开：沿两条轴都铺开，否则等距投影下会全挤到屏幕同一角
        Add(2, 1, v => v.Hut(2, 1, false));
        Add(8, 3, v => v.Hut(8, 3, false));
        Add(12, 6, v => v.Hut(12, 6, false));
        if (Tier >= 2) Add(3, 6, v => v.Hut(3, 6, true));
        if (Tier >= 5) Add(10, 1, v => v.Hut(10, 1, true));

        if (HasPeople || Tier >= 1) Add(8.6f, 3.6f, v => v.Chimney(8.6f, 3.6f));

        if (Tier >= 1) Add(0.5f, 4.5f, v => v.TavernProp(0.5f, 4.5f));
        // ⚠️ **不再画装饰的井**（D-099）：现在井是**真的建筑实例**（层级 2 自动出现）
        // ⇒ 再画一个装饰的 ⇒ 地图上有两口井 ⇒ 玩家会以为那口装饰的也是能用的建筑。
        // ⇒ 真井由 `_buildingMarks` 那条路画（按格位、带自己的形状）。
        if (Tier >= 3)
        {
            for (int i = 0; i < 15; i++) Add(0.3f + i, 9.0f, v => v.FenceProp(0.3f + i, 9.0f));
            for (int i = 0; i < 10; i++) Add(0.3f, 6.5f + i, v => v.FenceProp(0.3f, 6.5f + i));
        }
        if (Tier >= 4)
        {
            Add(1, 8.5f, v => v.SmithyProp(1, 8.5f));
            Add(4.5f, 8.0f, v => v.Site(4.5f, 8.0f));   // 旁边还有一块在盖
        }
        if (Tier >= 5)
        {
            Add(6, 7.5f, v => v.BannerProp(6, 7.5f, 70));
            Add(9, 8.5f, v => v.BannerProp(9, 8.5f, 70));
        }
    }

    private void Chimney(float x, float y)
    {
        Box(x, y, 0.45f, 0.45f, 30f, Stone);
        var top = P(x + 0.22f, y + 0.22f, 30f);
        DrawPolygon(new[] { top + new Vector2(-7, -4), top + new Vector2(7, -4),
                            top + new Vector2(7, 4), top + new Vector2(-7, 4) },
                    new[] { StoneTop, StoneTop, StoneTop, StoneTop });
        if (!HasPeople) return;
        for (int i = 0; i < 8; i++)
        {
            float ph = (_t * 0.3f + i * 0.125f) % 1f;
            var p = top + new Vector2(MathF.Sin(ph * 5.5f + i) * (6 + ph * 20), -6 - ph * 70);
            DrawCircle(p, 4f + ph * 12f, new Color(0.8f, 0.77f, 0.74f, 0.38f * (1f - ph)));
        }
    }
}
