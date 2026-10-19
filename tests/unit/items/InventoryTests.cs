using System;
using System.Collections.Generic;
using System.Linq;
using GdUnit4;
using ClaudeCodeGameStudios.Domain.Items;

namespace ClaudeCodeGameStudios.Tests.Items;

/// <summary>
/// 第二阶段 M1 的库存单元测试。
///
/// <para><b>本文档保护：</b></para>
/// <list type="bullet">
/// <item>不可变：<see cref="Inventory.TryAdd"/> / <see cref="Inventory.TryRemove"/> 返回新对象，原对象<b>不变</b>。</item>
/// <item>负数防线（P2-D06）：<see cref="Inventory.TryRemove"/> 数量不足返回 null，不抛。</item>
/// <item>容量防线（P2-D06）：加到容量上限以上返回 null，不抛。</item>
/// <item>全有或全无（I15）：<see cref="Inventory.Transfer"/> 一边失败就两边都不动。</item>
/// <item>反装饰：空初始清单合法、空 delta 不合法（抛 ArgumentOutOfRangeException 而非静默接受）。</item>
/// </list>
///
/// <para><b>本文档不规定行为数值</b>：容量 / 物品名都来自夹具，测试不依赖任何"游戏默认值"。</para>
/// </summary>
[TestSuite]
public class InventoryTests
{
    // ── 构造 ────────────────────────────────────────────────────────────────

    /// <summary>空 Id 必须被拒 —— Inventory 不允许"哪种东西都行"这种事。</summary>
    [TestCase]
    public void AddItemDefinition_WithEmptyId_IsRejected()
    {
        bool threw = false;
        try { new ItemDefinition(ItemId.None, "无名"); }
        catch (ArgumentException) { threw = true; }
        Assertions.AssertThat(threw).IsTrue();
    }

    /// <summary>负容量必须被拒 —— 0 是合法的（=无限），但 -1 不可。</summary>
    [TestCase]
    public void Inventory_WithNegativeCapacity_IsRejected()
    {
        bool threw = false;
        try { new Inventory(capacity: -1); }
        catch (ArgumentOutOfRangeException) { threw = true; }
        Assertions.AssertThat(threw).IsTrue();
    }

    /// <summary>初始清单里出现负数 = 拒绝 —— 不让"持有 -3 份石料"这种状态从构造期漏出去。</summary>
    [TestCase]
    public void Inventory_WithNegativeInitialCount_IsRejected()
    {
        bool threw = false;
        try
        {
            new Inventory(new[] { new KeyValuePair<ItemId, long>(new ItemId("石料"), -3) });
        }
        catch (ArgumentException) { threw = true; }
        Assertions.AssertThat(threw).IsTrue();
    }

    // ── TryAdd ──────────────────────────────────────────────────────────────

    /// <summary>正常加：返回的新库存数量正确，旧库存<b>不变</b>。</summary>
    [TestCase]
    public void TryAdd_NormalIncrease_ReturnsNewInventoryAndLeavesOriginal()
    {
        var before = new Inventory();
        var after = before.TryAdd(new ItemId("石料"), 5);

        Assertions.AssertThat(after).IsNotNull();
        Assertions.AssertThat(after!.Count(new ItemId("石料"))).IsEqual(5L);
        // 不可变：原对象保持空
        Assertions.AssertThat(before.Count(new ItemId("石料"))).IsEqual(0L);
        Assertions.AssertThat(before.Items.Count).IsEqual(0);
    }

    /// <summary>加零或负 = 拒绝 —— "扣"请走 Remove，混用会让人误以为负值合法。</summary>
    [TestCase]
    public void TryAdd_WithNonPositiveDelta_ReturnsNull()
    {
        var inv = new Inventory();
        Assertions.AssertThat(inv.TryAdd(new ItemId("石料"), 0)).IsNull();
        Assertions.AssertThat(inv.TryAdd(new ItemId("石料"), -3)).IsNull();
    }

    /// <summary>空 Id 必须被拒 —— Inventory 守门。</summary>
    [TestCase]
    public void TryAdd_WithEmptyId_ReturnsNull()
    {
        var inv = new Inventory();
        Assertions.AssertThat(inv.TryAdd(ItemId.None, 5)).IsNull();
    }

    /// <summary>
    /// 容量防线（P2-D06）：累计超容量 = 拒绝，原对象不变。
    /// </summary>
    /// <remarks>
    /// <b>为什么这条必须存在</b>：没有它，"英雄能塞 99999 件石料"就是真的能发生 ——
    /// 而《基线 V1.2 §5》明确要"英雄库存与村庄库存有边界"。</remarks>
    [TestCase]
    public void TryAdd_BeyondCapacity_ReturnsNull_AndLeavesOriginalUntouched()
    {
        var inv = new Inventory(capacity: 10);
        var filled = inv.TryAdd(new ItemId("石料"), 7);
        Assertions.AssertThat(filled).IsNotNull();

        // 再加 5 = 累计 12 > 10 → 拒绝
        var overfilled = filled!.TryAdd(new ItemId("石料"), 5);
        Assertions.AssertThat(overfilled).IsNull();

        // 原对象仍是 7，没被悄悄改成 12
        Assertions.AssertThat(filled.Count(new ItemId("石料"))).IsEqual(7L);
    }

    /// <summary>容量 = 0 = 无限大 —— P2-D06 默认形态。</summary>
    [TestCase]
    public void TryAdd_WithZeroCapacity_AllowsUnboundedAmount()
    {
        var inv = new Inventory(capacity: 0);
        var after = inv.TryAdd(new ItemId("石料"), 1_000_000);
        Assertions.AssertThat(after).IsNotNull();
        Assertions.AssertThat(after!.Count(new ItemId("石料"))).IsEqual(1_000_000L);
    }

    // ── TryRemove ───────────────────────────────────────────────────────────

    /// <summary>数量足够 = 扣减成功；减到 0 自动从字典移除（不留下 0 字段）。</summary>
    [TestCase]
    public void TryRemove_DecrementToZero_RemovesTheEntry()
    {
        var inv = new Inventory().TryAdd(new ItemId("木料"), 3);
        var after = inv!.TryRemove(new ItemId("木料"), 3);

        Assertions.AssertThat(after).IsNotNull();
        Assertions.AssertThat(after!.Count(new ItemId("木料"))).IsEqual(0L);
        Assertions.AssertThat(after.Items.ContainsKey(new ItemId("木料"))).IsFalse();
    }

    /// <summary>数量不足 = 失败（I12 一致：不扣成负）。</summary>
    [TestCase]
    public void TryRemove_InsufficientStock_ReturnsNull_AndLeavesOriginal()
    {
        var inv = new Inventory().TryAdd(new ItemId("木料"), 2);
        var after = inv!.TryRemove(new ItemId("木料"), 5);
        Assertions.AssertThat(after).IsNull();
        // 原对象仍是 2
        Assertions.AssertThat(inv.Count(new ItemId("木料"))).IsEqual(2L);
    }

    /// <summary>扣零或负 = 拒绝（与 TryAdd 对称）。</summary>
    [TestCase]
    public void TryRemove_WithNonPositiveDelta_ReturnsNull()
    {
        var inv = new Inventory().TryAdd(new ItemId("木料"), 5);
        Assertions.AssertThat(inv!.TryRemove(new ItemId("木料"), 0)).IsNull();
        Assertions.AssertThat(inv.TryRemove(new ItemId("木料"), -1)).IsNull();
        // 原对象不动
        Assertions.AssertThat(inv.Count(new ItemId("木料"))).IsEqual(5L);
    }

    // ── Transfer（I15 全有或全无）───────────────────────────────────────

    /// <summary>两边都够 = 双方都变；<b>两边都返回新对象</b>。</summary>
    [TestCase]
    public void Transfer_BothSidesValid_ReturnsBothNewInventories()
    {
        var from = new Inventory().TryAdd(new ItemId("石料"), 5);
        var to = new Inventory();

        var (newFrom, newTo) = Inventory.Transfer(from!, to, new ItemId("石料"), 3);

        Assertions.AssertThat(newFrom).IsNotNull();
        Assertions.AssertThat(newTo).IsNotNull();
        Assertions.AssertThat(newFrom!.Count(new ItemId("石料"))).IsEqual(2L);
        Assertions.AssertThat(newTo!.Count(new ItemId("石料"))).IsEqual(3L);
        // 不可变：原对象不动
        Assertions.AssertThat(from!.Count(new ItemId("石料"))).IsEqual(5L);
        Assertions.AssertThat(to.Count(new ItemId("石料"))).IsEqual(0L);
    }

    /// <summary>
    /// 源不够 = 失败，<b>两边都返回 null</b>（I15）。这条最关键 ——
    /// 没有它，"from 扣了但 to 没加"就是真会发生的 bug。
    /// </summary>
    [TestCase]
    public void Transfer_SourceInsufficient_BothSidesUnchanged()
    {
        var from = new Inventory().TryAdd(new ItemId("石料"), 2);
        var to = new Inventory();
        var toBefore = to.Count(new ItemId("石料"));

        var (newFrom, newTo) = Inventory.Transfer(from!, to, new ItemId("石料"), 5);

        Assertions.AssertThat(newFrom).IsNull();
        Assertions.AssertThat(newTo).IsNull();
        // 源不增不减、目标不动 —— 整笔"未发生"
        Assertions.AssertThat(from!.Count(new ItemId("石料"))).IsEqual(2L);
        Assertions.AssertThat(to.Count(new ItemId("石料"))).IsEqual(toBefore);
    }

    /// <summary>目标装不下 = 失败，<b>源不扣</b>（I15）。</summary>
    [TestCase]
    public void Transfer_TargetCapacityExceeded_BothSidesUnchanged()
    {
        var from = new Inventory().TryAdd(new ItemId("石料"), 10);
        var to = new Inventory(capacity: 2);

        var (newFrom, newTo) = Inventory.Transfer(from!, to, new ItemId("石料"), 5);

        Assertions.AssertThat(newFrom).IsNull();
        Assertions.AssertThat(newTo).IsNull();
        // 源 10 一份没动 —— 不会"扣了但没收到"
        Assertions.AssertThat(from!.Count(new ItemId("石料"))).IsEqual(10L);
    }

    // ── Snapshot 稳定性 ─────────────────────────────────────────────────

    /// <summary>
    /// Snapshot 返回的<b>每条</b>与原包对应，<b>没有重复、没有遗漏</b>。
    /// </summary>
    /// <remarks>
    /// <b>不测排序</b>：Ordinal 对汉字的规则与运行框架有关（mono vs coreclr），
    /// 测实现不测假设。把"按 Ordinal 排序"当<b>实现细节</b>，未来换排序策略不应当破坏这条测试。
    /// </remarks>
    [TestCase]
    public void Snapshot_ContainsEveryItemExactlyOnce()
    {
        var before = new Dictionary<ItemId, long>
        {
            { new ItemId("木料"), 1 },
            { new ItemId("石料"), 2 },
            { new ItemId("布匹"), 3 },
        };
        var inv = new Inventory(before);

        var snap = inv.Snapshot();
        Assertions.AssertThat(snap.Count).IsEqual(3);
        foreach (var (id, count) in before)
        {
            var hit = snap.SingleOrDefault(kv => kv.Key == id);
            Assertions.AssertThat(hit.Value).IsEqual(count);
        }
    }
}
