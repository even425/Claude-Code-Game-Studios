using GdUnit4;
using Godot;
using ClaudeCodeGameStudios.Presentation;

/// <summary>
/// 界面布局的<b>几何契约</b> —— 守住 2026-10-19 那次修复。
/// </summary>
/// <remarks>
/// <para><b>这里为什么要有测试</b>：三个 UI 症状（花名册没有名字、杂谈空白、
/// 按钮盖住地图）看起来是三件事，实际是同一次回归里的两件事：
/// ① <c>ScrollContainer</c> 在 <c>VBoxContainer</c> 里被压成 0 高
/// （<c>ScrollContainer.get_minimum_size()</c> 恒为 (0,0)，不传播子节点最小尺寸），
/// 花名册与杂谈因此被裁成零高；② 左右两栏被写成 985 像素宽，正好横跨整张地图。</para>
/// <para><b>为什么断言「矩形不相交」</b>：布局常量是纯计算，不需要启动引擎就能验。
/// 一旦有人再把左栏宽度写成 985，这条会立刻变红 —— 这正是 2026-10-19 事故的形状。</para>
/// </remarks>
[TestSuite]
public class UiLayoutTests
{
    /// <summary>两个矩形是否有面积重叠。相切不算重叠（贴边是允许的）。</summary>
    private static bool Overlaps(Rect2 a, Rect2 b)
    {
        float ax0 = a.Position.X, ax1 = a.Position.X + a.Size.X;
        float ay0 = a.Position.Y, ay1 = a.Position.Y + a.Size.Y;
        float bx0 = b.Position.X, bx1 = b.Position.X + b.Size.X;
        float by0 = b.Position.Y, by1 = b.Position.Y + b.Size.Y;
        return ax0 < bx1 && bx0 < ax1 && ay0 < by1 && by0 < ay1;
    }

    [TestCase]
    public void LeftPanel_DoesNotCoverTheMap()
        => Assertions.AssertThat(Overlaps(UiLayout.LeftPanel, UiLayout.MapBounds)).IsFalse();

    [TestCase]
    public void RightPanel_DoesNotCoverTheMap()
        => Assertions.AssertThat(Overlaps(UiLayout.RightPanel, UiLayout.MapBounds)).IsFalse();

    [TestCase]
    public void MapBounds_StayInsideTheViewport()
    {
        var vp = new Rect2(0, 0, UiLayout.ViewportW, UiLayout.ViewportH);
        Assertions.AssertThat(UiLayout.MapBounds.End.X).IsLessEqual(vp.Size.X);
        Assertions.AssertThat(UiLayout.MapBounds.End.Y).IsLessEqual(vp.Size.Y);
        Assertions.AssertThat(UiLayout.MapBounds.Position.X).IsGreaterEqual(0f);
        Assertions.AssertThat(UiLayout.MapBounds.Position.Y).IsGreaterEqual(0f);
    }

    [TestCase]
    public void MapBounds_FillTheReservedMapArea()
    {
        // 地图必须撑满留给它的那块，否则「缩到 0.9 倍」就白缩了 ——
        // 留白一大片，UI 看着就又宽回去了。
        float widthRatio = UiLayout.MapBounds.Size.X / UiLayout.MapArea.Size.X;
        float heightRatio = UiLayout.MapBounds.Size.Y / UiLayout.MapArea.Size.Y;
        Assertions.AssertThat(widthRatio).IsGreaterEqual(0.99f);
        Assertions.AssertThat(heightRatio).IsGreaterEqual(0.55f);
    }

    [TestCase]
    public void SidePanels_DoNotOverlapEachOther()
        => Assertions.AssertThat(Overlaps(UiLayout.LeftPanel, UiLayout.RightPanel)).IsFalse();

    [TestCase]
    public void ScrollAreas_HaveNonZeroMinimumHeight()
    {
        // 这一条直接对应「花名册没有名字 / 杂谈空白」：
        // 0 高的滚动区会把里面的 ItemList / Label 全部裁没。
        // （花名册现在是 ItemList 自己滚动，最小高度同样是它的生存线。）
        Assertions.AssertThat(UiLayout.RosterMinHeight).IsGreater(0f);
        Assertions.AssertThat(UiLayout.ChronicleMinHeight).IsGreater(0f);
    }

    [TestCase]
    public void LeftPanel_FitsInsideTheViewport()
    {
        Assertions.AssertThat(UiLayout.LeftPanel.End.X).IsLessEqual(UiLayout.ViewportW);
        Assertions.AssertThat(UiLayout.RightPanel.End.X).IsLessEqual(UiLayout.ViewportW);
        Assertions.AssertThat(UiLayout.LeftPanel.End.Y).IsLessEqual(UiLayout.ViewportH);
        Assertions.AssertThat(UiLayout.RightPanel.End.Y).IsLessEqual(UiLayout.ViewportH);
    }

    [TestCase]
    public void LeftColumn_StaysANarrowGutter()
    {
        // 对应「各个按钮太过宽了挡住场景」：左栏是一条窄沟，不是一条横跨屏幕的板子；
        // 按钮宽度由文字决定（没有宽度下限），所以按钮不可能比窄沟更宽。
        Assertions.AssertThat(UiLayout.LeftW).IsLess(300f);
        Assertions.AssertThat(UiLayout.LeftW).IsLess(UiLayout.MapArea.Size.X / 2f);
        Assertions.AssertThat(UiLayout.ButtonMinHeight).IsGreater(0f);
    }

    [TestCase]
    public void LeftPanel_ContentFitsItsHeight()
    {
        // 左栏内容（标题 + 花名册 + 队伍标题 + 队伍文字 + 6 个按钮 + 间距）
        // 不得高过面板本身，否则底部按钮会被挤出屏幕。
        float content = UiLayout.LeftContentMinHeight;
        Assertions.AssertThat(content).IsLessEqual(UiLayout.PanelHeight);
    }

    [TestCase]
    public void RightPanel_ContentFitsItsHeight()
    {
        // 第三阶段 M6：右栏在杂谈下方加了 4 行关系摘要。
        // 标题 + 杂谈 + 标题 + 关系 + 间隔 不得高过面板，否则关系区被挤出屏幕。
        Assertions.AssertThat(UiLayout.RightContentMinHeight).IsLessEqual(UiLayout.PanelHeight);
        Assertions.AssertThat(UiLayout.RelationsMinHeight).IsGreater(0f);
    }
}
