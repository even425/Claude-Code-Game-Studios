using System;
using System.Collections.Generic;
using GdUnit4;
using ClaudeCodeGameStudios.Application;
using ClaudeCodeGameStudios.Domain.Heroes;
using ClaudeCodeGameStudios.Domain.Items;
using ClaudeCodeGameStudios.Domain.Settlement;
using ClaudeCodeGameStudios.Domain.Village;

namespace ClaudeCodeGameStudios.Tests.Application;

/// <summary>
/// <see cref="WalletGrantService"/> 的单元测试（D-054-01：副本金币报酬 + 零头累计）。
///
/// <para><b>本文档保护三条：</b></para>
/// <ul>
/// <li><b>I15</b>：钱发给<b>多个</b>英雄时，任何一个人装不下 → <b>一个都不发</b>。</li>
/// <li><b>预检与提交分离</b>：<c>CanGrant</c> 只回答「行不行」，
/// <c>Grant</c> 在已知可行的前提下一次性发完（钱包不可变）。</li>
/// <li><b>零头按英雄分别累计</b>（D-054 阶段 4 拍板）：X 可以是分数，
/// 零头攒在<b>各自名下</b>，攒够 1 金才发。</li>
/// </ul>
///
/// <para><b>本文档不规定金额</b>：X 只是夹具。</para>
/// </summary>
[TestSuite]
public class WalletGrantServiceTests
{
    private static readonly HeroId Alice = new("h-alice");
    private static readonly HeroId Bob = new("h-bob");
    private static readonly ResourceId Gold = new("金币");

    /// <summary>1 金 = 100 个 1/100 金单位。</summary>
    private const long OneGold = 100L;

    private static HeroAgent NewHero(long gold = 0L)
    {
        var hero = new HeroAgent { Name = "H" };
        hero.RestoreWallet(new Wallet(
            new Dictionary<ResourceId, long> { { Gold, gold } }));
        return hero;
    }

    private static WalletGrantService Make(
        Dictionary<HeroId, HeroAgent> heroes, long rewardUnits = OneGold)
        => new WalletGrantService(
            id => heroes.TryGetValue(id, out var h) ? h : null, Gold, rewardUnits);

    [TestCase]
    public void CanGrant_ForValidGrants_ReturnsTrue()
    {
        var alice = NewHero(10);
        var svc = Make(new Dictionary<HeroId, HeroAgent> { [Alice] = alice });

        bool ok = svc.CanGrant(new[] { new WalletGrant(Alice) });

        Assertions.AssertThat(ok).IsTrue();
        // 预检**不改状态**
        Assertions.AssertThat(alice.Wallet.Amount(Gold)).IsEqual(10L);
        Assertions.AssertThat(svc.TotalPending).IsEqual(0L);
    }

    [TestCase]
    public void Grant_WholeGoldReward_PaysExactlyThatAmount()
    {
        var alice = NewHero(10);
        var svc = Make(new Dictionary<HeroId, HeroAgent> { [Alice] = alice });

        svc.Grant(new[] { new WalletGrant(Alice) });

        Assertions.AssertThat(alice.Wallet.Amount(Gold)).IsEqual(11L);
        Assertions.AssertThat(svc.TotalGranted).IsEqual(1L);
        Assertions.AssertThat(svc.TotalPending).IsEqual(0L);
    }

    [TestCase]
    public void Grant_ToSameHeroTwice_Accumulates()
    {
        var alice = NewHero(0);
        var svc = Make(new Dictionary<HeroId, HeroAgent> { [Alice] = alice });

        svc.Grant(new[] { new WalletGrant(Alice) });
        svc.Grant(new[] { new WalletGrant(Alice) });

        Assertions.AssertThat(alice.Wallet.Amount(Gold)).IsEqual(2L);
        Assertions.AssertThat(svc.TotalGranted).IsEqual(2L);
    }

    // ── 零头累计（D-054 阶段 4 拍板）───────────────────────────────────

    /// <summary>
    /// <b>分数 X</b>：X = 0.5 金 → 第一次**发不出**（攒 50），第二次发 1 金。
    /// </summary>
    /// <remarks>
    /// 零头**攒在这个英雄名下**，不是全局 —— 全局攒会让先轮到的英雄拿走整金、
    /// 后面的吃亏，那就不是「每人每次 X 金」了。
    /// </remarks>
    [TestCase]
    public void Grant_FractionalReward_AccumulatesPerHeroAndPaysWhenFull()
    {
        var alice = NewHero(0);
        var svc = Make(new Dictionary<HeroId, HeroAgent> { [Alice] = alice }, rewardUnits: 50);

        svc.Grant(new[] { new WalletGrant(Alice) });

        // 攒了 50（1/100 金）→ 还没到 1 金 → 一分没发
        Assertions.AssertThat(alice.Wallet.Amount(Gold)).IsEqual(0L);
        Assertions.AssertThat(svc.PendingOf(Alice)).IsEqual(50L);
        Assertions.AssertThat(svc.TotalPending).IsEqual(50L);
        Assertions.AssertThat(svc.TotalGranted).IsEqual(0L);

        svc.Grant(new[] { new WalletGrant(Alice) });

        // 50 + 50 = 100 → 发 1 金，零头归零
        Assertions.AssertThat(alice.Wallet.Amount(Gold)).IsEqual(1L);
        Assertions.AssertThat(svc.PendingOf(Alice)).IsEqual(0L);
        Assertions.AssertThat(svc.TotalGranted).IsEqual(1L);
    }

    /// <summary>两个英雄的零头<b>各攒各的</b>，不互相占用。</summary>
    [TestCase]
    public void Grant_FractionalReward_DoesNotMixHeroesCarries()
    {
        var alice = NewHero(0);
        var bob = NewHero(0);
        var svc = Make(new Dictionary<HeroId, HeroAgent> { [Alice] = alice, [Bob] = bob },
            rewardUnits: 50);

        svc.Grant(new[] { new WalletGrant(Alice), new WalletGrant(Bob) });

        Assertions.AssertThat(svc.PendingOf(Alice)).IsEqual(50L);
        Assertions.AssertThat(svc.PendingOf(Bob)).IsEqual(50L);
        Assertions.AssertThat(alice.Wallet.Amount(Gold)).IsEqual(0L);
        Assertions.AssertThat(bob.Wallet.Amount(Gold)).IsEqual(0L);
    }

    /// <summary>零头<b>能入档</b>：读档后它还在（否则那部分钱凭空消失）。</summary>
    [TestCase]
    public void RestorePending_KeepsTheFractionAfterReload()
    {
        var alice = NewHero(0);
        var svc = Make(new Dictionary<HeroId, HeroAgent> { [Alice] = alice }, rewardUnits: 50);
        svc.Grant(new[] { new WalletGrant(Alice) });

        // 模拟「读档 → 恢复」
        var reloaded = Make(new Dictionary<HeroId, HeroAgent> { [Alice] = alice }, rewardUnits: 50);
        reloaded.RestorePending(Alice, svc.PendingOf(Alice));

        Assertions.AssertThat(reloaded.PendingOf(Alice)).IsEqual(50L);
        // 恢复后再发一次 → 正好凑满 1 金
        reloaded.Grant(new[] { new WalletGrant(Alice) });
        Assertions.AssertThat(alice.Wallet.Amount(Gold)).IsEqual(1L);
    }

    /// <summary>零头超出合法范围 → 拒（那是损坏的存档，不是合法状态）。</summary>
    [TestCase]
    public void RestorePending_OutOfRange_Throws()
    {
        var alice = NewHero(0);
        var svc = Make(new Dictionary<HeroId, HeroAgent> { [Alice] = alice });

        bool threw = false;
        try { svc.RestorePending(Alice, 100); }
        catch (ArgumentOutOfRangeException) { threw = true; }

        Assertions.AssertThat(threw).IsTrue();
    }

    // ── 防线 ───────────────────────────────────────────────────────────────

    /// <summary>英雄不存在 → 预检不过 → 整批放弃。</summary>
    [TestCase]
    public void CanGrant_WhenHeroMissing_ReturnsFalse()
    {
        var alice = NewHero(10);
        var svc = Make(new Dictionary<HeroId, HeroAgent> { [Alice] = alice });

        bool ok = svc.CanGrant(new[] { new WalletGrant(Alice), new WalletGrant(Bob) });

        Assertions.AssertThat(ok).IsFalse();
    }

    /// <summary>钱包<b>容量上限</b>（有上限的钱包）→ 装不下就拒。</summary>
    /// <remarks>
    /// 容量语义是「<b>上限值</b>」：余额 10 + 上限 11 = 11 装得下，
    /// 10 + 2 = 12 超过 11 → 拒。（正好等于上限是**合法**的。）
    /// </remarks>
    [TestCase]
    public void CanGrant_WhenWalletCapacityExceeded_ReturnsFalse()
    {
        var alice = new HeroAgent { Name = "A" };
        alice.RestoreWallet(new Wallet(
            new Dictionary<ResourceId, long> { { Gold, 10L } }, capacity: 11));

        var svc = Make(new Dictionary<HeroId, HeroAgent> { [Alice] = alice });

        Assertions.AssertThat(svc.CanGrant(new[] { new WalletGrant(Alice) })).IsTrue();
        Assertions.AssertThat(svc.CanGrant(new[] { new WalletGrant(Alice), new WalletGrant(Alice) }))
            .IsFalse();   // 一次发 2 金 → 12 > 上限 11
    }

    /// <summary>溢出也拒收（余额接近 long.MaxValue 时再加会绕成负数）。</summary>
    [TestCase]
    public void CanGrant_WhenAmountWouldOverflow_ReturnsFalse()
    {
        var alice = NewHero(long.MaxValue);
        var svc = Make(new Dictionary<HeroId, HeroAgent> { [Alice] = alice });

        bool ok = svc.CanGrant(new[] { new WalletGrant(Alice) });

        Assertions.AssertThat(ok).IsFalse();
        Assertions.AssertThat(alice.Wallet.Amount(Gold)).IsEqual(long.MaxValue);
    }

    /// <summary>跳过预检就提交 → 抛（那是编程错误，不能默默发一半）。</summary>
    [TestCase]
    public void Grant_WithoutPrecheck_Throws()
    {
        var alice = NewHero(10);
        var svc = Make(new Dictionary<HeroId, HeroAgent> { [Alice] = alice });

        bool threw = false;
        try { svc.Grant(new[] { new WalletGrant(new HeroId("h-nobody")) }); }
        catch (InvalidOperationException) { threw = true; }

        Assertions.AssertThat(threw).IsTrue();
    }

    /// <summary>
    /// <b>全有或全无</b>：一个人装不下 → <b>另一个人的钱包也不能动</b>。
    /// </summary>
    [TestCase]
    public void Grant_WhenOneHeroCannotTake_ChangesNoWallet()
    {
        var alice = NewHero(10);
        var bob = NewHero(0);
        var svc = Make(new Dictionary<HeroId, HeroAgent> { [Alice] = alice, [Bob] = bob });

        var grants = new[] { new WalletGrant(Alice), new WalletGrant(new HeroId("h-nobody")) };

        bool threw = false;
        try { svc.Grant(grants); }
        catch (InvalidOperationException) { threw = true; }

        Assertions.AssertThat(threw).IsTrue();
        Assertions.AssertThat(alice.Wallet.Amount(Gold)).IsEqual(10L);   // 一分没多
        Assertions.AssertThat(bob.Wallet.Amount(Gold)).IsEqual(0L);
        Assertions.AssertThat(svc.TotalGranted).IsEqual(0L);
    }

    /// <summary>空批次：什么都不做，也不抛。</summary>
    [TestCase]
    public void Grant_EmptyBatch_DoesNothing()
    {
        var alice = NewHero(10);
        var svc = Make(new Dictionary<HeroId, HeroAgent> { [Alice] = alice });

        svc.Grant(Array.Empty<WalletGrant>());

        Assertions.AssertThat(alice.Wallet.Amount(Gold)).IsEqual(10L);
    }

    /// <summary>
    /// <b>累计发放</b>要如实累加 —— 守恒律的「累计新增」这一项就取自它。
    /// </summary>
    [TestCase]
    public void Grant_AccumulatesTotalGranted()
    {
        var alice = NewHero(0);
        var bob = NewHero(0);
        var svc = Make(new Dictionary<HeroId, HeroAgent> { [Alice] = alice, [Bob] = bob });

        svc.Grant(new[] { new WalletGrant(Alice) });
        svc.Grant(new[] { new WalletGrant(Alice), new WalletGrant(Bob) });

        Assertions.AssertThat(svc.TotalGranted).IsEqual(3L);
        Assertions.AssertThat(alice.Wallet.Amount(Gold)).IsEqual(2L);
        Assertions.AssertThat(bob.Wallet.Amount(Gold)).IsEqual(1L);
    }

    /// <summary>发放失败（跳过预检）→ <b>不计入</b>累计（钱没发出去就不算「新增」）。</summary>
    [TestCase]
    public void Grant_WhenItFails_DoesNotCountAsGranted()
    {
        var alice = NewHero(0);
        var svc = Make(new Dictionary<HeroId, HeroAgent> { [Alice] = alice });

        bool threw = false;
        try { svc.Grant(new[] { new WalletGrant(new HeroId("h-nobody")) }); }
        catch (InvalidOperationException) { threw = true; }

        Assertions.AssertThat(threw).IsTrue();
        Assertions.AssertThat(svc.TotalGranted).IsEqual(0L);
    }
}
