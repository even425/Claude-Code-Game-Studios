using Godot;
using System;
using System.Collections.Generic;
using ClaudeCodeGameStudios.Application;

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
    public const int MapW = 15;
    public const int MapH = 10;

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
    public readonly record struct VillageBuildingMark(
        int GridX, int GridY, int AffectsW, int AffectsH, string Label, bool Done = true);

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
        // 地板是 0..MapW × 0..MapH；村庄的影响区（市集的）较大，连到地板之外
        // ⇒ 若不限，玩家会在「空里」点一下就把楼房放了 ⇒ 地图上出现一个悬在半空的标记。
        if (gxi < 0 || gxi >= MapW || gyi < 0 || gyi >= MapH)
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

        int minX = Math.Max(0, gridX - (affectsW - 1));
        int maxX = Math.Min(MapW, gridX + affectsW);
        int minY = Math.Max(0, gridY - (affectsH - 1));
        int maxY = Math.Min(MapH, gridY + affectsH);
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
    private void DrawBuildingMarks()
    {
        foreach (var m in _buildingMarks)
        {
            if (m.AffectsW > 0 && m.AffectsH > 0)
            {
                int minX = m.GridX - (m.AffectsW - 1);
                int maxX = m.GridX + (m.AffectsW - 1);
                int minY = m.GridY - (m.AffectsH - 1);
                int maxY = m.GridY + (m.AffectsH - 1);
                var area = new[]
                {
                    P(minX, minY), P(maxX + 1, minY), P(maxX + 1, maxY + 1), P(minX, maxY + 1),
                };
                DrawPolygon(area, new[] { new Color(0.45f, 0.85f, 0.55f, 0.10f) });
            }

            var cell = new[]
            {
                P(m.GridX, m.GridY), P(m.GridX + 1, m.GridY),
                P(m.GridX + 1, m.GridY + 1), P(m.GridX, m.GridY + 1),
            };
            // 已建成 = 蓝色；施工中 = 琥现色（「还没能用」）。
            var mark = m.Done ? new Color(0.55f, 0.8f, 0.95f, 0.45f) : new Color(0.95f, 0.72f, 0.35f, 0.45f);
            DrawPolygon(cell, new[] { mark });
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

        items.Sort((a, b) => a.key.CompareTo(b.key));
        foreach (var it in items) it.draw();

        // 已建成的建筑标记（影响区）先画。
        DrawBuildingMarks();

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
        // 整块地：一个菱形
        var quad = new[] { P(0, 0), P(MapW, 0), P(MapW, MapH), P(0, MapH) };
        DrawPolygon(quad, new[] { Grass, Grass, Grass, Grass });

        // 地面格子（隔一个画一条深色缝，地块感）
        for (int x = 0; x < MapW; x++)
            for (int y = 0; y < MapH; y++)
                if ((x + y) % 2 == 0)
                {
                    var c = new[] { P(x, y), P(x + 1, y), P(x + 1, y + 1), P(x, y + 1) };
                    DrawPolygon(c, new[] { GrassAlt, GrassAlt, GrassAlt, GrassAlt });
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
        // 注意：随机值必须在【建布局时】取好并捕获；放进 lambda 里就是绘制时求值，
        // 那样树会每帧变一次大小。
        for (int i = 0; i < 30; i++)
        {
            float x = _rng.Next(0, MapW - 1), y = _rng.Next(0, MapH - 1);
            if (occupied(x, y)) continue;
            float s = 0.75f + _rng.NextSingle() * 0.7f;
            Add(x, y, v => v.Tree(x, y, s));
        }
        for (int i = 0; i < 13; i++)
        {
            float x = _rng.Next(0, MapW - 1), y = _rng.Next(0, MapH - 1);
            if (occupied(x, y)) continue;
            float s = 0.8f + _rng.NextSingle() * 0.6f;
            Add(x, y, v => v.Bush(x, y, s));
        }
        for (int i = 0; i < 6; i++)
        {
            float x = _rng.Next(0, MapW - 1), y = _rng.Next(0, MapH - 1);
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
        if (Tier >= 2) Add(6, 0.5f, v => v.WellProp(6, 0.5f));
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
