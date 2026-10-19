using System;
using GdUnit4;
using ClaudeCodeGameStudios.Domain.Heroes;
using ClaudeCodeGameStudios.Domain.Items;
using DomainStall = ClaudeCodeGameStudios.Domain.Stall.Stall;
using DomainStallStatus = ClaudeCodeGameStudios.Domain.Stall.StallStatus;

namespace ClaudeCodeGameStudios.Tests.Stall;

/// <summary>
/// <see cref="DomainStall"/> 单元测试。
///
/// <para><b>本文档保护：</b></para>
/// <list type="bullet">
/// <item>构造期防线（Owner/ItemId 空、Stock&lt;1、UnitPrice 负、SimTime 越界）。</item>
/// <item>不可变：<see cref="DomainStall.AfterSale"/> 返<b>新</b>实例；原对象一字不动。</item>
/// <item>状态机：AfterSale 售罄自动转 <see cref="DomainStallStatus.SoldOut"/>。</item>
/// <item>收摊：<see cref="DomainStall.Closed"/> 设状态 = Closed；<b>已 Closed 再 Closed</b> = 幂等返同状态。</item>
/// </list>
///
/// <para><b>namespace 冲突</b>：<c>namespace ClaudeCodeGameStudios.Tests.Stall</c> 与
/// <c>ClaudeCodeGameStudios.Domain.Stall.Stall</c> 在裸名 <c>Stall</c> 上冲突。
/// 同上轮 <c>Trade</c> 的 fix：用 <c>DomainStall</c> alias 让裸名解析走类型。</para>
/// </summary>
[TestSuite]
public class StallTests
{
    private static readonly HeroId Alice = new("h-alice");
    private static readonly ItemId Stone = new("石料");

    // record struct 默认值不能用 null — 用哨兵 null 检查触发 default 分支
    private static DomainStall MakeStall(
        HeroId owner = default, ItemId itemId = default,
        long stock = 10, long unitPrice = 5,
        DomainStallStatus status = DomainStallStatus.Open, double simTime = 100.0)
    {
        if (owner.Value == null) owner = Alice;
        if (itemId.Value == null) itemId = Stone;
        return new DomainStall(owner, itemId, stock, unitPrice, status, simTime);
    }

    // ── 构造期防线 ─────────────────────────────────────────────────────

    [TestCase]
    public void Stall_WithEmptyOwnerId_Throws()
    {
        bool threw = false;
        try { new DomainStall(new HeroId(""), Stone, 10, 5, DomainStallStatus.Open, 0); }
        catch (ArgumentException) { threw = true; }
        Assertions.AssertThat(threw).IsTrue();
    }

    [TestCase]
    public void Stall_WithEmptyItemId_Throws()
    {
        bool threw = false;
        try { new DomainStall(Alice, new ItemId(""), 10, 5, DomainStallStatus.Open, 0); }
        catch (ArgumentException) { threw = true; }
        Assertions.AssertThat(threw).IsTrue();
    }

    [TestCase]
    public void Stall_WithZeroStock_Throws()
    {
        bool threw = false;
        try { MakeStall(stock: 0); }
        catch (ArgumentOutOfRangeException) { threw = true; }
        Assertions.AssertThat(threw).IsTrue();
    }

    [TestCase]
    public void Stall_WithNegativeUnitPrice_Throws()
    {
        bool threw = false;
        try { MakeStall(unitPrice: -1); }
        catch (ArgumentOutOfRangeException) { threw = true; }
        Assertions.AssertThat(threw).IsTrue();
    }

    [TestCase]
    public void Stall_WithZeroUnitPrice_IsAllowed_Freebie()
    {
        // 0 = 白送（D-023 与 Trade 一致：0 合法，负数拒）
        var s = MakeStall(unitPrice: 0);
        Assertions.AssertThat(s.UnitPrice).IsEqual(0L);
    }

    [TestCase]
    public void Stall_WithBadSimTime_Throws()
    {
        bool threw = false;
        try { MakeStall(simTime: -1.0); }
        catch (ArgumentOutOfRangeException) { threw = true; }
        Assertions.AssertThat(threw).IsTrue();
    }

    // ── AfterSale（不可变 + 售罄转 SoldOut）──────────────────────

    [TestCase]
    public void AfterSale_PartialSale_KeepsOpen_AndReturnsNewInstance()
    {
        var original = MakeStall(stock: 10);
        var after = original.AfterSale(3);

        // 新对象：库存 -3，状态 Open
        Assertions.AssertThat(after.Stock).IsEqual(7L);
        Assertions.AssertThat(after.Status).IsEqual(DomainStallStatus.Open);
        // 原对象一字不动（不可变）
        Assertions.AssertThat(original.Stock).IsEqual(10L);
        Assertions.AssertThat(original.Status).IsEqual(DomainStallStatus.Open);
        // 是<b>新</b>引用
        Assertions.AssertThat(ReferenceEquals(original, after)).IsFalse();
    }

    [TestCase]
    public void AfterSale_ExactStock_TransitionsToSoldOut()
    {
        var original = MakeStall(stock: 5);
        var after = original.AfterSale(5);

        Assertions.AssertThat(after.Stock).IsEqual(0L);
        Assertions.AssertThat(after.Status).IsEqual(DomainStallStatus.SoldOut);
        // 原不变
        Assertions.AssertThat(original.Stock).IsEqual(5L);
        Assertions.AssertThat(original.Status).IsEqual(DomainStallStatus.Open);
    }

    [TestCase]
    public void AfterSale_ExceedingStock_Throws()
    {
        var s = MakeStall(stock: 2);
        bool threw = false;
        try { s.AfterSale(5); }
        catch (ArgumentOutOfRangeException) { threw = true; }
        Assertions.AssertThat(threw).IsTrue();
    }

    [TestCase]
    public void AfterSale_ZeroQty_Throws()
    {
        var s = MakeStall();
        bool threw = false;
        try { s.AfterSale(0); }
        catch (ArgumentOutOfRangeException) { threw = true; }
        Assertions.AssertThat(threw).IsTrue();
    }

    // ── Closed（收摊幂等）────────────────────────────────────────

    [TestCase]
    public void Closed_FromOpen_TransitionsToClosed()
    {
        var s = MakeStall(status: DomainStallStatus.Open);
        var c = s.Closed();
        Assertions.AssertThat(c.Status).IsEqual(DomainStallStatus.Closed);
        Assertions.AssertThat(s.Status).IsEqual(DomainStallStatus.Open);
    }

    [TestCase]
    public void Closed_FromSoldOut_AlsoTransitionsToClosed()
    {
        var s = MakeStall(stock: 5, status: DomainStallStatus.Open).AfterSale(5);
        Assertions.AssertThat(s.Status).IsEqual(DomainStallStatus.SoldOut);
        var c = s.Closed();
        Assertions.AssertThat(c.Status).IsEqual(DomainStallStatus.Closed);
    }

    [TestCase]
    public void Closed_Idempotent_WhenAlreadyClosed()
    {
        var s = MakeStall(status: DomainStallStatus.Open).Closed();
        var c2 = s.Closed();
        Assertions.AssertThat(c2.Status).IsEqual(DomainStallStatus.Closed);
    }
}
