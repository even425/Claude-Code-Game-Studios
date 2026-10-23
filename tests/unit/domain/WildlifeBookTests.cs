using System;
using System.Linq;
using GdUnit4;
using ClaudeCodeGameStudios.Domain.Village;
using ClaudeCodeGameStudios.Domain.Wildlife;

namespace ClaudeCodeGameStudios.Tests.Domain;

/// <summary>
/// <b>世界地图与村外的野兽</b>（D-117）。
/// </summary>
/// <remarks>
/// <para><b>它守的是六件事</b>：
/// <list type="number">
/// <item>「哪里是村外」判据正确（村内 0..14 × 0..10 之外就是野外）。</item>
/// <item>地图的边界判定用<b>偏移后的原点</b>（格位可以是负数 ⇒ 不能写 <c>x &gt;= 0</c>）。</item>
/// <item>野兽<b>只生成在村外</b>。</item>
/// <item>野兽<b>走不进村子</b>。</item>
/// <item>野兽<b>走不出地图</b>。</item>
/// <item>每天走一步：要么原地不动，要么落到<b>仍在村外</b>的地方。</item>
/// </list></para>
/// <para>⚠️ <b>为什么这些要单测</b>：「野兽不进村」这条规则如果只在表现层判，
/// 将来有人改渲染就会把它改没 ⇒ 规则住在 <c>Domain</c>（可离线单测），
/// 表现层只负责「照着画」。</para>
/// </remarks>
[TestSuite]
public class WildlifeBookTests
{
    /// <summary>一个可复现的随机数（0..1 循环）。</summary>
    private static Func<float> Constant(float v) => () => v;

    /// <summary>
    /// 按给定顺序返回随机数（用完以后重复最后一个）。
    /// </summary>
    /// <remarks>
    /// ⚠️ **为什么要「先给一个落在村里的数」**：生成野兽时若第一下就落在村里，
    /// 会**重试**（换个位置再取）⇒ 只有序列里**先出现一个村里的点**，
    /// 「重试」那条路才真的被走到 ⇒ 否则「生成时不保证在村外」这个变异抓不到。
    /// </remarks>
    private static Func<float> Roll(params float[] values)
    {
        int i = 0;
        return () =>
        {
            float v = values[Math.Min(i, values.Length - 1)];
            i++;
            return v;
        };
    }

    // ── 地图判据 ────────────────────────────────────────────────────────

    /// <summary>村子地盘内 ⇒ 不是野外。</summary>
    [TestCase]
    public void InsideTheVillage_IsNotWilderness()
    {
        Assertions.AssertThat(WorldMap.IsWilderness(0, 0)).IsFalse();
        Assertions.AssertThat(WorldMap.IsWilderness(7, 5)).IsFalse();
        Assertions.AssertThat(WorldMap.IsWilderness(14, 9)).IsFalse();
    }

    /// <summary>村子地盘外 ⇒ 是野外。</summary>
    [TestCase]
    public void OutsideTheVillage_IsWilderness()
    {
        Assertions.AssertThat(WorldMap.IsWilderness(-1, 0)).IsTrue();
        Assertions.AssertThat(WorldMap.IsWilderness(15, 0)).IsTrue();
        Assertions.AssertThat(WorldMap.IsWilderness(0, 10)).IsTrue();
        Assertions.AssertThat(WorldMap.IsWilderness(20, 15)).IsTrue();
    }

    /// <summary>地图边界判定用<b>偏移后的原点</b>（负数格位也在地图上）。</summary>
    [TestCase]
    public void InBounds_UsesTheOffsetOrigin()
    {
        Assertions.AssertThat(WorldMap.InBounds(WorldMap.OriginX, WorldMap.OriginY)).IsTrue();
        Assertions.AssertThat(WorldMap.InBounds(WorldMap.OriginX - 1, 0)).IsFalse();
        Assertions.AssertThat(WorldMap.InBounds(
            WorldMap.OriginX + WorldMap.Width - 1, 0)).IsTrue();
        Assertions.AssertThat(WorldMap.InBounds(
            WorldMap.OriginX + WorldMap.Width, 0)).IsFalse();
    }

    // ── 野兽 ────────────────────────────────────────────────────────────

    /// <summary>野兽<b>只生成在村外</b>。</summary>
    /// <remarks>
    /// ⚠️ 随机序列**故意先给两个落在村里的数**（0.5 ⇒ 大约 (6,4) ⇒ 村里）
    /// ⇒ 这样「落在村里就重试」那条路**真的被走到** ⇒
    /// 变异「生成时不检查是不是村外」才抓得到。
    /// ⚠️ 注意：`Seed` 每次尝试取**两个**随机数（先 x 后 y）⇒ 序列要成对给。
    /// </remarks>
    [TestCase]
    public void Seed_PlacesEveryBeastInTheWilderness()
    {
        var book = new WildlifeBook();

        book.Seed(8, Roll(0.5f, 0.5f, 0.9f, 0.9f), i => (BeastKind)(i % 3));

        Assertions.AssertThat(book.Beasts.Count).IsEqual(8);
        foreach (var beast in book.Beasts)
        {
            Assertions.AssertThat(WorldMap.IsWilderness(beast.X, beast.Y)).IsTrue();
        }
    }

    /// <summary>野兽<b>走不进村子</b>。</summary>
    [TestCase]
    public void TryMoveTo_RefusesToEnterTheVillage()
    {
        var beast = new WildBeast("b1", BeastKind.Boar, 20, 0);
        int beforeX = beast.X, beforeY = beast.Y;

        bool moved = beast.TryMoveTo(5, 5);   // 村子正中间

        Assertions.AssertThat(moved).IsFalse();
        Assertions.AssertThat(beast.X).IsEqual(beforeX);
        Assertions.AssertThat(beast.Y).IsEqual(beforeY);
    }

    /// <summary>野兽<b>走不出地图</b>。</summary>
    [TestCase]
    public void TryMoveTo_RefusesToLeaveTheMap()
    {
        var beast = new WildBeast("b1", BeastKind.Boar, 20, 0);
        int beforeX = beast.X, beforeY = beast.Y;

        bool moved = beast.TryMoveTo(WorldMap.OriginX - 5, 0);

        Assertions.AssertThat(moved).IsFalse();
        Assertions.AssertThat(beast.X).IsEqual(beforeX);
        Assertions.AssertThat(beast.Y).IsEqual(beforeY);
    }

    /// <summary>每天走一步：要么原地不动，要么**仍在村外**。</summary>
    [TestCase]
    public void WanderOneStep_NeverLeavesTheWilderness()
    {
        var book = new WildlifeBook();
        book.Seed(8, Constant(0.95f), i => (BeastKind)(i % 3));

        for (int day = 0; day < 30; day++)
        {
            book.WanderOneStep(Constant(0.5f));
            foreach (var beast in book.Beasts)
            {
                Assertions.AssertThat(WorldMap.IsWilderness(beast.X, beast.Y)).IsTrue();
                Assertions.AssertThat(WorldMap.InBounds(beast.X, beast.Y)).IsTrue();
            }
        }
    }

    /// <summary>种类分布：按传入的规则来（不替开发者做主）。</summary>
    [TestCase]
    public void CountOf_CountsByKind()
    {
        var book = new WildlifeBook();
        book.Seed(6, Constant(0.9f), i => (BeastKind)(i % 3));

        Assertions.AssertThat(book.CountOf(BeastKind.Boar)).IsEqual(2);
        Assertions.AssertThat(book.CountOf(BeastKind.Wolf)).IsEqual(2);
        Assertions.AssertThat(book.CountOf(BeastKind.Deer)).IsEqual(2);
    }
}
