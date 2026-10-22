using System;
using System.IO;
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
    public void LeftColumn_Scrolls_SoBottomButtonsStayReachable()
    {
        // ⚠️ **这条替代了旧的「内容不得高过面板」**（D-081 修的界面缺陷）：
        // 左栏内容的高度**不是常量** ——「队伍」区会随村庄层级长行（升级选项 + 建筑选项），
        // 按钮也从一个长到十一个 ⇒ 任何固定高度都会把底部的按钮挤出屏幕。
        // 实测：11 个按钮里**只有 3 个能看见**，「发布任务 / 升级村庄 / 建围栏 /
        // 建试炼塔 / 进试炼塔 / 存盘 / 读盘」全都点不到。
        // ⇒ 左栏必须**可滚动** ⇒ 内容多高都能滚到。
        Assertions.AssertThat(UiLayout.LeftColumnScrolls).IsTrue();
    }

    [TestCase]
    public void ButtonCount_MatchesTheButtonsActuallyInTheCode()
    {
        // ⚠️ **以前这条是装饰性的**：`ButtonCount` 写着 7，而代码里早就有 10 个按钮
        // ⇒ 「内容装得下」那条守卫一直绿着，而屏幕上的按钮**早被挤出去了**。
        // ⇒ 现在直接**数代码里的 MakeButton( 调用数** ⇒ 这个数字从此漂移不了。
        int actual = CountMakeButtonCalls();
        Assertions.AssertThat(actual).IsEqual(UiLayout.ButtonCount);
    }

    /// <summary>数 <c>src/Main*.cs</c> 里真的建了多少个左栏按钮。</summary>
    /// <returns>调用次数。</returns>
    private static int CountMakeButtonCalls()
    {
        var srcDir = Path.Combine(RepoRoot(), "src");
        int count = 0;
        foreach (var file in Directory.EnumerateFiles(srcDir, "Main*.cs"))
        {
            foreach (var line in File.ReadAllLines(file))
            {
                string trimmed = line.TrimStart();
                if (trimmed.StartsWith("//") || trimmed.StartsWith("///")) continue;
                // ⚠️ **别把 MakeButton 的定义本身算进去**（它也含 "MakeButton("）。
                if (trimmed.Contains("static Button MakeButton(", StringComparison.Ordinal)) continue;
                if (trimmed.Contains("MakeButton(", StringComparison.Ordinal)) count++;
            }
        }

        return count;
    }

    /// <summary>仓库根目录（测试程序集在 <c>tests/bin/...</c> 下）。</summary>
    /// <returns>根目录路径。</returns>
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(
            Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location)!);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Claude-Code-Game-Studios.csproj")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("找不到仓库根目录");
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
