using System;
using System.Collections.Generic;
using System.Linq;
using GdUnit4;
using ClaudeCodeGameStudios.Domain.Items;
using ClaudeCodeGameStudios.Domain.Village;

namespace ClaudeCodeGameStudios.Tests.Items;

/// <summary>
/// <see cref="Wallet"/> 的单元测试。
///
/// <para><b>本文档保护：</b></para>
/// <list type="bullet">
/// <item>不可变：<see cref="Wallet.TryAdd"/> / <see cref="Wallet.TrySpend"/> 返回新对象，原对象<b>不变</b>。</item>
/// <item>负数防线（I12 一致）：<see cref="Wallet.TrySpend"/> 余额不足返回 null，不扣成负。</item>
/// <item>容量防线（P2-D06）：累计超容量返回 null。</item>
/// <item>全有或全无（I15）：<see cref="Wallet.Transfer"/> 任意一边失败就两边都返回 null。</item>
/// </list>
///
/// <para><b>本文档不规定数值</b>：资源名 / 初始金额 / 容量都来自夹具。</para>
/// </summary>
[TestSuite]
public class WalletTests
{
    private static readonly ResourceId Gold = new("金币");

    // ── 构造 ────────────────────────────────────────────────────────────────

    /// <summary>负容量必须被拒 —— 0 是合法的（=无限），但 -1 不可。</summary>
    [TestCase]
    public void Wallet_WithNegativeCapacity_IsRejected()
    {
        bool threw = false;
        try { new Wallet(capacity: -1); }
        catch (ArgumentOutOfRangeException) { threw = true; }
        Assertions.AssertThat(threw).IsTrue();
    }

    /// <summary>初始清单里出现负数 = 拒绝。</summary>
    [TestCase]
    public void Wallet_WithNegativeInitialAmount_IsRejected()
    {
        bool threw = false;
        try
        {
            new Wallet(new[] { new KeyValuePair<ResourceId, long>(Gold, -3) });
        }
        catch (ArgumentException) { threw = true; }
        Assertions.AssertThat(threw).IsTrue();
    }

    // ── TryAdd ──────────────────────────────────────────────────────────────

    /// <summary>正常加：返回的新包数量正确，旧包<b>不变</b>。</summary>
    [TestCase]
    public void TryAdd_NormalIncrease_ReturnsNewWallet_AndLeavesOriginal()
    {
        var before = new Wallet();
        var after = before.TryAdd(Gold, 100);

        Assertions.AssertThat(after).IsNotNull();
        Assertions.AssertThat(after!.Amount(Gold)).IsEqual(100L);
        // 不可变：原对象保持空
        Assertions.AssertThat(before.Amount(Gold)).IsEqual(0L);
    }

    /// <summary>空 ResourceId 必须被拒。</summary>
    [TestCase]
    public void TryAdd_WithEmptyId_ReturnsNull()
    {
        var w = new Wallet();
        Assertions.AssertThat(w.TryAdd(new ResourceId(""), 10)).IsNull();
    }

    /// <summary>加零或负 = 拒绝。</summary>
    [TestCase]
    public void TryAdd_WithNonPositiveDelta_ReturnsNull()
    {
        var w = new Wallet();
        Assertions.AssertThat(w.TryAdd(Gold, 0)).IsNull();
        Assertions.AssertThat(w.TryAdd(Gold, -5)).IsNull();
    }

    /// <summary>容量防线：超容量拒绝，旧对象不变。</summary>
    [TestCase]
    public void TryAdd_BeyondCapacity_ReturnsNull_AndLeavesOriginal()
    {
        var before = new Wallet(capacity: 50);
        var after = before.TryAdd(Gold, 30);   // 30 进去 = 30 满
        Assertions.AssertThat(after).IsNotNull();

        // 再加 25 = 累计 55 > 50 → 拒绝
        var over = after!.TryAdd(Gold, 25);
        Assertions.AssertThat(over).IsNull();

        // 不可变：第二个 30 进去的那个 after 还是 30
        Assertions.AssertThat(after.Amount(Gold)).IsEqual(30L);
        Assertions.AssertThat(before.Amount(Gold)).IsEqual(0L);
    }

    // ── TrySpend ───────────────────────────────────────────────────────────

    /// <summary>扣到 0 时<b>从 Snapshot 中消失</b>（不留 0 字段）。</summary>
    /// <remarks>
    /// 这是<b>外部可观察</b>的「不存 0 字段」不变量：
    /// <c>Snapshot</c> 计数应该只反映真正非零的资源。<br/>
    /// 之前我直接用 <c>Resources.ContainsKey</c> 断言，但<b>不可变版本
    /// 内部字典覆盖 0 时键还在</b>，那条断言过弱。
    /// </remarks>
    [TestCase]
    public void TrySpend_DecrementToZero_RemovesTheEntryFromSnapshot()
    {
        var inv = new Wallet().TryAdd(Gold, 50);
        var after = inv!.TrySpend(Gold, 50);

        Assertions.AssertThat(after).IsNotNull();
        Assertions.AssertThat(after!.Amount(Gold)).IsEqual(0L);
        // 真不变量：Snapshot 不含 0 字段
        Assertions.AssertThat(after.Snapshot().Count).IsEqual(0);
    }

    /// <summary>余额不足 = 失败（I12：不扣成负）。</summary>
    [TestCase]
    public void TrySpend_InsufficientBalance_ReturnsNull_AndLeavesOriginal()
    {
        var before = new Wallet().TryAdd(Gold, 20);
        var after = before!.TrySpend(Gold, 100);
        Assertions.AssertThat(after).IsNull();
        // 原对象不动
        Assertions.AssertThat(before.Amount(Gold)).IsEqual(20L);
    }

    /// <summary>扣零或负 = 拒绝。</summary>
    [TestCase]
    public void TrySpend_WithNonPositiveDelta_ReturnsNull()
    {
        var w = new Wallet().TryAdd(Gold, 50);
        Assertions.AssertThat(w!.TrySpend(Gold, 0)).IsNull();
        Assertions.AssertThat(w.TrySpend(Gold, -1)).IsNull();
    }

    // ── Transfer（I15）─────────────────────────────────────────────────

    /// <summary>两边都够 = 双方都变；新对象，不破坏原对象。</summary>
    [TestCase]
    public void Transfer_BothSidesValid_ReturnsBothNewWallets()
    {
        var from = new Wallet().TryAdd(Gold, 100);
        var to = new Wallet();

        var (newFrom, newTo) = Wallet.Transfer(from!, to, Gold, 30);

        Assertions.AssertThat(newFrom).IsNotNull();
        Assertions.AssertThat(newTo).IsNotNull();
        Assertions.AssertThat(newFrom!.Amount(Gold)).IsEqual(70L);
        Assertions.AssertThat(newTo!.Amount(Gold)).IsEqual(30L);
        // 不可变
        Assertions.AssertThat(from!.Amount(Gold)).IsEqual(100L);
        Assertions.AssertThat(to.Amount(Gold)).IsEqual(0L);
    }

    /// <summary>源余额不足 = 失败，<b>两边都返回 null</b>（I15）。</summary>
    [TestCase]
    public void Transfer_SourceInsufficient_BothSidesUnchanged()
    {
        var from = new Wallet().TryAdd(Gold, 20);
        var to = new Wallet();
        var toBeforeGold = to.Amount(Gold);

        var (newFrom, newTo) = Wallet.Transfer(from!, to, Gold, 100);

        Assertions.AssertThat(newFrom).IsNull();
        Assertions.AssertThat(newTo).IsNull();
        Assertions.AssertThat(from!.Amount(Gold)).IsEqual(20L);
        Assertions.AssertThat(to.Amount(Gold)).IsEqual(toBeforeGold);
    }

    /// <summary>目标容量超 = 失败，<b>源不扣</b>（I15）。</summary>
    [TestCase]
    public void Transfer_TargetCapacityExceeded_BothSidesUnchanged()
    {
        var from = new Wallet().TryAdd(Gold, 200);
        var to = new Wallet(capacity: 50);

        var (newFrom, newTo) = Wallet.Transfer(from!, to, Gold, 100);

        Assertions.AssertThat(newFrom).IsNull();
        Assertions.AssertThat(newTo).IsNull();
        // 源 200 没动
        Assertions.AssertThat(from!.Amount(Gold)).IsEqual(200L);
    }

    // ── Snapshot 稳定性 ─────────────────────────────────────────────────

    /// <summary>
    /// Snapshot 返回的<b>每条</b>与原包对应，<b>没有重复、没有遗漏</b>。
    /// </summary>
    /// <remarks>
    /// <b>不测排序</b>：Ordinal 排序对汉字的实现规则不是直觉的，
    /// 也可能因运行框架不同而不同（mono vs coreclr）。
    /// 单测应断言<b>内容</b>，把"按 Ordinal 排序"当<b>实现细节</b>：换
    /// 排序策略（除了<b>稳定</b>这一点）不应当破坏单测。
    /// </remarks>
    [TestCase]
    public void Snapshot_ContainsEveryResourceExactlyOnce()
    {
        var before = new Dictionary<ResourceId, long>
        {
            { new ResourceId("木材"), 1 },
            { new ResourceId("石料"), 2 },
            { Gold, 3 },
        };
        var w = new Wallet(before);

        var snap = w.Snapshot();
        Assertions.AssertThat(snap.Count).IsEqual(3);
        foreach (var (id, amount) in before)
        {
            var hit = snap.SingleOrDefault(kv => kv.Key == id);
            Assertions.AssertThat(hit.Value).IsEqual(amount);
        }
    }
}
