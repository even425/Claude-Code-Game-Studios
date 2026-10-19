using System;
using GdUnit4;
using ClaudeCodeGameStudios.Domain.Items;
using ClaudeCodeGameStudios.Domain.Village;

namespace ClaudeCodeGameStudios.Tests.Items;

/// <summary>
/// 第二阶段 M1：<see cref="ClaudeCodeGameStudios.HeroAgent"/> 的经济 / 物品助手方法。
///
/// <para><b>本文档保护：</b>钱包/背包助手方法（<c>TryAddResource</c> / <c>TrySpendResource</c> /
/// <c>TryAddItem</c> / <c>TryRemoveItem</c>）的薄封装行为 —— <b>引用替换</b>
/// 必须走新的不可变实例，<b>不允许</b>原地修改。</para>
///
/// <para><b>为什么不测防线本身</b>：<see cref="Wallet"/> / <see cref="Inventory"/>
/// 的 I12 / 容量 / I15 防线在它们各自的测试里盯过。本测试只盯"薄封装"
/// 这层 —— <b>新对象引用替换</b> + <b>失败返 false</b> + <b>Restore 入口</b>。</para>
///
/// <para><b>本文档不规定数值</b>：资源名 / 物品名都来自夹具。</para>
/// </summary>
[TestSuite]
public class HeroAgentEconomyTests
{
    private static readonly ResourceId Gold = new("金币");
    private static readonly ResourceId Wood = new("木材");
    private static readonly ItemId Stone = new("石料");

    // ── 钱包助手 ─────────────────────────────────────────────────────

    /// <summary>初始钱包是空的、初始背包是空的。</summary>
    [TestCase]
    public void Constructor_StartsWithEmptyWalletAndBag()
    {
        var h = new HeroAgent();

        Assertions.AssertThat(h.Wallet.Resources.Count).IsEqual(0);
        Assertions.AssertThat(h.Bag.Items.Count).IsEqual(0);
    }

    /// <summary>
    /// 成功加资源：返回 true，<c>Wallet</c> 引用换新（不可变），余额正确。
    /// </summary>
    [TestCase]
    public void TryAddResource_OnSuccess_ReturnsTrue_AndReplacesWallet()
    {
        var h = new HeroAgent();
        var before = h.Wallet;

        bool ok = h.TryAddResource(Gold, 100);

        Assertions.AssertThat(ok).IsTrue();
        Assertions.AssertThat(h.Wallet.Amount(Gold)).IsEqual(100L);
        // 不可变：原 Wallet 引用保持 0
        Assertions.AssertThat(before.Amount(Gold)).IsEqual(0L);
        // 引用也换了
        Assertions.AssertThat(ReferenceEquals(h.Wallet, before)).IsFalse();
    }

    /// <summary>
    /// 容量超限 → 返 false，<b>不</b>替换 Wallet（保持旧引用）。
    /// </summary>
    /// <remarks>
    /// <b>这条是真防线</b>：<see cref="Wallet.TryAdd"/> 容量超时返 null；
    /// <see cref="ClaudeCodeGameStudios.HeroAgent.TryAddResource"/> 必须把
    /// 「null = 失败」传达为「false = 失败」，并<b>不动 Wallet</b>。
    /// 如果助手方法把 null 替换给 Wallet（**原地修改**），就会绕过
    /// <see cref="Inventory"/> / <see cref="Wallet"/> 自身的不可变不变量。
    /// </remarks>
    [TestCase]
    public void TryAddResource_WhenCapacityExceeded_ReturnsFalse_AndLeavesWalletUntouched()
    {
        var h = new HeroAgent();
        // 先让 Wallet 容量 = 10，再加 20 超容量
        h.RestoreWallet(new Wallet(capacity: 10));
        h.TryAddResource(Gold, 5);   // 进去 = 5
        var afterFill = h.Wallet;

        bool ok = h.TryAddResource(Gold, 20);   // 5 + 20 = 25 > 10

        Assertions.AssertThat(ok).IsFalse();
        // 失败：Wallet 引用**不变**（仍是 5 的那个）
        Assertions.AssertThat(ReferenceEquals(h.Wallet, afterFill)).IsTrue();
        Assertions.AssertThat(h.Wallet.Amount(Gold)).IsEqual(5L);
    }

    /// <summary>扣资源：成功 → 新 Wallet；失败 → 旧 Wallet 保留。</summary>
    [TestCase]
    public void TrySpendResource_SuccessReplaces_FailureLeaves()
    {
        var h = new HeroAgent();
        h.RestoreWallet(new Wallet(new System.Collections.Generic.Dictionary<ResourceId, long>
        {
            { Gold, 100 },
        }));
        var before = h.Wallet;

        bool ok = h.TrySpendResource(Gold, 30);

        Assertions.AssertThat(ok).IsTrue();
        Assertions.AssertThat(h.Wallet.Amount(Gold)).IsEqual(70L);
        Assertions.AssertThat(ReferenceEquals(h.Wallet, before)).IsFalse();

        // 失败：余额不足
        var before2 = h.Wallet;
        bool ok2 = h.TrySpendResource(Gold, 1000);
        Assertions.AssertThat(ok2).IsFalse();
        Assertions.AssertThat(ReferenceEquals(h.Wallet, before2)).IsTrue();
    }

    /// <summary>加物品 + 扣物品：同样引用替换模式。</summary>
    [TestCase]
    public void TryAddItem_AndTryRemoveItem_ReplaceTheBag()
    {
        var h = new HeroAgent();
        var before = h.Bag;

        bool addOk = h.TryAddItem(Stone, 5);
        Assertions.AssertThat(addOk).IsTrue();
        Assertions.AssertThat(h.Bag.Count(Stone)).IsEqual(5L);
        Assertions.AssertThat(ReferenceEquals(h.Bag, before)).IsFalse();

        // 失败：扣超容量
        var before2 = h.Bag;
        bool removeOk = h.TryRemoveItem(Stone, 100);
        Assertions.AssertThat(removeOk).IsFalse();
        Assertions.AssertThat(ReferenceEquals(h.Bag, before2)).IsTrue();
    }

    // ── Restore（仅供读档）─────────────────────────────────────────

    /// <summary>RestoreWallet 把 Wallet 引用整个换掉；传入 null 必须被拒。</summary>
    [TestCase]
    public void RestoreWallet_ReplacesAndRejectsNull()
    {
        var h = new HeroAgent();
        var newW = new Wallet(new System.Collections.Generic.Dictionary<ResourceId, long>
        {
            { Gold, 7 },
        });
        h.RestoreWallet(newW);

        Assertions.AssertThat(ReferenceEquals(h.Wallet, newW)).IsTrue();
        Assertions.AssertThat(h.Wallet.Amount(Gold)).IsEqual(7L);

        // null 必须被拒
        bool threw = false;
        try { h.RestoreWallet(null); }
        catch (ArgumentNullException) { threw = true; }
        Assertions.AssertThat(threw).IsTrue();
    }
}
