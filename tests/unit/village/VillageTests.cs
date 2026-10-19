using System;
using GdUnit4;
using ClaudeCodeGameStudios.Domain.Village;

/// <summary>
/// 村庄与建筑的单元测试。
///
/// <para><b>本文档保护两条：</b></para>
/// <list type="number">
/// <item><b>不变量 I12</b>：资源不会变为负数 —— 不足时<b>拒绝扣减</b>，而不是扣成负数。</item>
/// <item><b>占位冲突</b>（基线 §13.7 明列「建筑坐标与占地冲突」）：重叠的放置必须被拒绝，且状态不变。</item>
/// </list>
///
/// <para><b>本文档同时守住两条「不要重犯」：</b></para>
/// <ul>
/// <li><b>没有 tier 阶梯</b> —— 0→5 直线层级模型已被 <c>07</c> 技术债 B2 判死（基线 §12.2）</li>
/// <li><b>没有预置材料种类</b> —— 六种材料的清单来自已作废的注册表（D-002），
/// 基线未确认资源种类，属【待定】</li>
/// </ul>
///
/// <para><b>本文档不规定任何游戏数值。</b> 格子坐标只是测试夹具，
/// 不代表村庄的实际尺寸或布局。【待定】属基线 §16 #12。</para>
/// </summary>
[TestSuite]
public class VillageTests
{
    private static readonly ResourceId Wood = new("木材");

    // ── I12 · 资源不会变为负 ────────────────────────────────────────────────

    /// <summary>从未持有过的种类返回 0，而不是抛异常或负数。</summary>
    [TestCase]
    public void Get_UnheldResource_ReturnsZero()
    {
        var village = new Village("v1");

        Assertions.AssertThat(village.Get(Wood)).IsEqual(0L);
    }

    /// <summary>不足时<b>拒绝扣减</b>，且余额<b>保持不变</b>（不变负数，也不变 0 以下）。</summary>
    [TestCase]
    public void TrySpend_InsufficientBalance_IsRejected_AndLeavesBalanceUnchanged()
    {
        var village = new Village("v1");
        village.Add(Wood, 5);

        bool spent = village.TrySpend(Wood, 10);

        Assertions.AssertThat(spent).IsFalse();
        Assertions.AssertThat(village.Get(Wood)).IsEqual(5L);
    }

    /// <summary>恰好相等时扣减成功并归零 —— 边界不得误判为不足。</summary>
    [TestCase]
    public void TrySpend_ExactlyEqual_Succeeds_AndLeavesZero()
    {
        var village = new Village("v1");
        village.Add(Wood, 5);

        bool spent = village.TrySpend(Wood, 5);

        Assertions.AssertThat(spent).IsTrue();
        Assertions.AssertThat(village.Get(Wood)).IsEqual(0L);
    }

    /// <summary>一连串扣减在任何时刻都不会让余额变负。</summary>
    [TestCase]
    public void TrySpend_NeverDrivesBalanceNegative_AcrossManyAttempts()
    {
        var village = new Village("v1");
        village.Add(Wood, 12);

        for (var i = 0; i < 50; i++) village.TrySpend(Wood, 1);

        Assertions.AssertThat(village.Get(Wood) >= 0).IsTrue();
    }

    /// <summary>零与负的增减都按编程错误拒绝，不静默接受。</summary>
    [TestCase]
    public void Add_Or_TrySpend_NonPositiveAmount_Throws()
    {
        var village = new Village("v1");

        bool addZeroThrew = false;
        bool spendZeroThrew = false;
        bool spendNegativeThrew = false;

        try { village.Add(Wood, 0); } catch (ArgumentOutOfRangeException) { addZeroThrew = true; }
        try { village.TrySpend(Wood, 0); } catch (ArgumentOutOfRangeException) { spendZeroThrew = true; }
        try { village.TrySpend(Wood, -1); } catch (ArgumentOutOfRangeException) { spendNegativeThrew = true; }

        Assertions.AssertThat(addZeroThrew).IsTrue();
        Assertions.AssertThat(spendZeroThrew).IsTrue();
        Assertions.AssertThat(spendNegativeThrew).IsTrue();
    }

    // ── 占位冲突 ────────────────────────────────────────────────────────────

    /// <summary>两栋建筑完全重叠时，后放的被拒绝。</summary>
    [TestCase]
    public void TryPlace_OverlappingFootprint_IsRejected_AndVillageUnchanged()
    {
        var village = new Village("v1");
        var first = new BuildingInstance(new BuildingId("土屋"), 2, 3, 1, 1);
        Assertions.AssertThat(village.TryPlace(first)).IsTrue();

        var clash = new BuildingInstance(new BuildingId("井"), 2, 3, 1, 1);
        bool placed = village.TryPlace(clash);

        Assertions.AssertThat(placed).IsFalse();
        Assertions.AssertThat(village.Buildings.Count).IsEqual(1);
    }

    /// <summary>
    /// 只要<b>有一个格子</b>重叠就应被拒绝 —— 不能只比较左下角坐标。
    /// </summary>
    /// <TestCase]
    public void TryPlace_PartialOverlap_IsRejected()
    {
        var village = new Village("v1");
        // 一栋 3×2 的建筑占 (0,0)(1,0)(2,0)(0,1)(1,1)(2,1)
        Assertions.AssertThat(
            village.TryPlace(new BuildingInstance(new BuildingId("铁匠铺"), 0, 0, 3, 2))).IsTrue();

        // 只与右上角那一格重叠
        bool placed = village.TryPlace(new BuildingInstance(new BuildingId("井"), 2, 1, 1, 1));

        Assertions.AssertThat(placed).IsFalse();
    }

    /// <summary>不重叠时放置成功，且占位被正确记录。</summary>
    [TestCase]
    public void TryPlace_NonOverlapping_Succeeds_AndOccupiesItsCells()
    {
        var village = new Village("v1");
        var placed = village.TryPlace(new BuildingInstance(new BuildingId("围栏"), 0, 0, 4, 1));

        Assertions.AssertThat(placed).IsTrue();
        Assertions.AssertThat(village.IsOccupied(0, 0)).IsTrue();
        Assertions.AssertThat(village.IsOccupied(3, 0)).IsTrue();
        Assertions.AssertThat(village.IsOccupied(4, 0)).IsFalse();
    }

    // ── 不要重犯：没有 tier 阶梯 ─────────────────────────────────────────────

    /// <summary>
    /// <c>BuildingStatus</c> 只有「在建 / 已建成」两态，<b>不存在等级</b>。
    /// <para>0→5 的直线层级模型已被 <c>07</c> 技术债 B2 判死。本测试把这个事实钉住：
    /// 若将来有人重新引入 <c>Tier</c> 之类的字段，本测试会编译不过。</para>
    /// </summary>
    [TestCase]
    public void Building_HasNoTier_OnlyConstructionProgress()
    {
        var hut = new BuildingInstance(new BuildingId("土屋"), 0, 0, 1, 1);

        Assertions.AssertThat(hut.Status).IsEqual(BuildingStatus.UnderConstruction);
        Assertions.AssertThat(hut.ConstructionProgress).IsEqual(0.0);

        hut.AdvanceConstruction(1.0);

        Assertions.AssertThat(hut.Status).IsEqual(BuildingStatus.Completed);
        Assertions.AssertThat(hut.ConstructionProgress).IsEqual(1.0);
    }

    /// <summary>建造进度只增不减，且不会超过 1。</summary>
    [TestCase]
    public void AdvanceConstruction_IsMonotonic_AndClampedToOne()
    {
        var hut = new BuildingInstance(new BuildingId("土屋"), 0, 0, 1, 1);

        hut.AdvanceConstruction(0.4);
        hut.AdvanceConstruction(0.4);
        hut.AdvanceConstruction(5.0);

        Assertions.AssertThat(hut.ConstructionProgress).IsEqual(1.0);
    }

    /// <summary>负的进度增量按编程错误拒绝，而不是悄悄回退。</summary>
    [TestCase]
    public void AdvanceConstruction_NegativeDelta_Throws()
    {
        var hut = new BuildingInstance(new BuildingId("土屋"), 0, 0, 1, 1);
        hut.AdvanceConstruction(0.5);

        bool threw = false;
        try { hut.AdvanceConstruction(-0.1); }
        catch (ArgumentOutOfRangeException) { threw = true; }

        Assertions.AssertThat(threw).IsTrue();
        Assertions.AssertThat(hut.ConstructionProgress).IsEqual(0.5);
    }

    /// <summary>占地非正必须被拒 —— 零或负的占地会让占位冲突检测失效。</summary>
    [TestCase]
    public void Constructor_NonPositiveFootprint_Throws()
    {
        bool zeroW = false;
        bool negativeH = false;

        try { var a = new BuildingInstance(new BuildingId("x"), 0, 0, 0, 1); }
        catch (ArgumentOutOfRangeException) { zeroW = true; }

        try { var b = new BuildingInstance(new BuildingId("x"), 0, 0, 1, -1); }
        catch (ArgumentOutOfRangeException) { negativeH = true; }

        Assertions.AssertThat(zeroW).IsTrue();
        Assertions.AssertThat(negativeH).IsTrue();
    }
}
