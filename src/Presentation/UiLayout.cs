using Godot;

namespace ClaudeCodeGameStudios.Presentation;

/// <summary>
/// 界面布局的<b>纯几何</b>：只算矩形，不碰任何 Node。
/// </summary>
/// <remarks>
/// <para><b>为什么这些数字住在独立文件里</b>：2026-10-19 之前它们是
/// <c>Main.BuildScene</c> 里的一串局部 <c>const</c>，于是没有任何东西能验证它们，
/// 左栏被写成 985 像素宽、横跨整张地图、花名册与杂谈被 0 高的滚动区裁没时，
/// 编译通过、自检通过、331 个测试全绿。<b>把它们收进一个可被测试引用的类，
/// 是那次事故真正缺的那一环。</b></para>
/// <para><b>三栏约束</b>：左栏（花名册 + 队伍 + 按钮）、中间的地图条带、右栏（杂谈）
/// 三者互不相交，且地图条带恰好是留给地图的。</para>
/// </remarks>
public static class UiLayout
{
    // ── 视口 ─────────────────────────────────────────────────────────────
    // 项目固定 1280×720（project.godot 里没开 stretch），所以直接用绝对坐标。
    public const float ViewportW = 1280f;
    public const float ViewportH = 720f;

    // ── 上下通栏 ─────────────────────────────────────────────────────────
    public const float PanelTopY = 48f;          // 顶部状态栏之下
    public const float PanelBottomY = 704f;      // 底部提示之上
    public const float PanelHeight = PanelBottomY - PanelTopY;   // 656

    // ── 左右两栏（两条窄沟，不再横跨地图） ────────────────────────────────
    public const float LeftX = 8f;
    public const float LeftW = 248f;
    public const float RightX = 992f;
    public const float RightW = 280f;

    /// <summary>栏与栏之间的留白。</summary>
    public const float Gutter = 8f;

    // ── 留给地图的横向条带 ───────────────────────────────────────────────
    public const float MapX = LeftX + LeftW + Gutter;              // 264
    public const float MapW = RightX - MapX - Gutter;             // 720

    // ── 地图的本地包围盒 ─────────────────────────────────────────────────
    // 由 VillageView 的投影 sx=(x-y)*TW/2、sy=(x+y)*TH/2 推出：
    //   P(0,MapH).x = -320   P(MapW,0).x = 480
    //   P(0,MapH).y =  160   P(MapW,MapH).y = 400
    // 上方多留 60：墙与树是从地面往上画的，最高会探出地面投影。
    public const float MapLocalMinX = -320f;
    public const float MapLocalMaxX = 480f;
    public const float MapLocalMinY = -60f;
    public const float MapLocalMaxY = 400f;
    public const float MapLocalWidth = MapLocalMaxX - MapLocalMinX;   // 800
    public const float MapLocalHeight = MapLocalMaxY - MapLocalMinY;  // 460

    // ── 面板内部的尺寸 ───────────────────────────────────────────────────
    public const float HeaderHeight = 18f;
    public const float SquadLineHeight = 17f;
    public const float VBoxSeparation = 5f;

    /// <summary>花名册滚动区的<b>最小</b>高度；实际还会吃掉左栏的富余空间。</summary>
    public const float RosterMinHeight = 300f;

    /// <summary>杂谈滚动区的<b>最小</b>高度。</summary>
    /// <remarks>
    /// <b>必须大于 0</b>。ScrollContainer 的最小尺寸恒为 (0,0) —— 它<b>不</b>把子节点的
    /// 最小尺寸往外传，所以在 VBoxContainer 里写 <c>Size</c> 会被容器的排版过程覆盖回 0，
    /// 里面的 ItemList / Label 就全被裁掉了。真正生效的是 <c>CustomMinimumSize</c>。
    /// 2026-10-19 的「花名册没有名字」「杂谈空白」就是这一条。
    /// </remarks>
    public const float ChronicleMinHeight = 400f;

    /// <summary>
    /// 右栏「关系」摘要区的最小高度（第三阶段 M6）：4 行有向关系。
    /// </summary>
    /// <remarks>
    /// 左栏已无富余（见 <see cref="LeftContentMinHeight"/>），关系展示只能放右栏；
    /// 杂谈区仍是主体，关系只占底部 4 行。
    /// </remarks>
    public const float RelationsMinHeight = 4f * SquadLineHeight;   // 68

    /// <summary>右栏内容的最小高度：标题 + 杂谈 + 标题 + 关系摘要 + 间隔。</summary>
    public const float RightContentMinHeight =
        HeaderHeight + ChronicleMinHeight + HeaderHeight + RelationsMinHeight
        + 3f * VBoxSeparation;

    public const float ButtonMinHeight = 31f;

    /// <summary>
    /// 左栏按钮个数。R-02 增加了「看摊位」，6 → 7。
    /// </summary>
    /// <remarks>
    /// <b>这个数字要跟着按钮一起改</b>：<see cref="LeftContentMinHeight"/> 用它算
    /// 左栏内容的高度，加了按钮不改这里，内容就会溢出面板、底部按钮被挤出屏幕。
    /// <c>UiLayoutTests.LeftPanel_ContentFitsItsHeight</c> 守着这条。
    /// </remarks>
    public const int ButtonCount = 7;

    /// <summary>
    /// 左栏内容的最小高度。两个标题 + 花名册 + 队伍文字 + 6 个按钮 + 9 个间隔。
    /// </summary>
    public const float LeftContentMinHeight =
        HeaderHeight + RosterMinHeight + HeaderHeight + SquadLineHeight
        + ButtonCount * ButtonMinHeight
        + (ButtonCount + 3) * VBoxSeparation;

    // ── 矩形 ─────────────────────────────────────────────────────────────

    /// <summary>左栏：花名册 + 队伍 + 按钮。</summary>
    public static Rect2 LeftPanel => new(LeftX, PanelTopY, LeftW, PanelHeight);

    /// <summary>右栏：村庄杂谈。</summary>
    public static Rect2 RightPanel => new(RightX, PanelTopY, RightW, PanelHeight);

    /// <summary>中间专门留给地图的条带。</summary>
    public static Rect2 MapArea => new(MapX, PanelTopY, MapW, PanelHeight);

    /// <summary>地图的缩放：横向刚好占满条带，纵向还有富余（条带更高）。</summary>
    public static float MapScale => MapW / MapLocalWidth;

    /// <summary>地图左下角之外的锚点（<c>VillageView.Position</c>）。</summary>
    public static Vector2 MapOrigin
    {
        get
        {
            float s = MapScale;
            var area = MapArea;
            float localCenterX = (MapLocalMinX + MapLocalMaxX) / 2f;
            float localCenterY = (MapLocalMinY + MapLocalMaxY) / 2f;
            return new Vector2(
                area.Position.X + area.Size.X / 2f - localCenterX * s,
                area.Position.Y + area.Size.Y / 2f - localCenterY * s);
        }
    }

    /// <summary>地图缩放后在屏幕上的矩形。</summary>
    public static Rect2 MapBounds
    {
        get
        {
            float s = MapScale;
            var o = MapOrigin;
            return new Rect2(
                o.X + MapLocalMinX * s,
                o.Y + MapLocalMinY * s,
                MapLocalWidth * s,
                MapLocalHeight * s);
        }
    }

    /// <summary>底部提示行的位置。</summary>
    public static Vector2 HintPosition => new(MapX, PanelBottomY - 28f);
}
