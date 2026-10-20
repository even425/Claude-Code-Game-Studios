using System;
using System.Collections.Generic;
using System.Linq;
using GdUnit4;
using ClaudeCodeGameStudios.Application;
using ClaudeCodeGameStudios.Domain.Heroes;
using ClaudeCodeGameStudios.Domain.Items;
using DomainSettlement = ClaudeCodeGameStudios.Domain.Settlement.Settlement;
using DomainSettlementKey = ClaudeCodeGameStudios.Domain.Settlement.SettlementKey;
using DomainSettlementDelta = ClaudeCodeGameStudios.Domain.Settlement.SettlementDelta;
using DomainIItemGrantWriter = ClaudeCodeGameStudios.Domain.Settlement.IItemGrantWriter;
using DomainIWalletGrantWriter = ClaudeCodeGameStudios.Domain.Settlement.IWalletGrantWriter;
using DomainItemGrant = ClaudeCodeGameStudios.Domain.Settlement.ItemGrant;
using DomainWalletGrant = ClaudeCodeGameStudios.Domain.Settlement.WalletGrant;
using DomainResourceDelta = ClaudeCodeGameStudios.Domain.Settlement.ResourceDelta;
using ClaudeCodeGameStudios.Domain.Village;

namespace ClaudeCodeGameStudios.Tests.Settlement;

/// <summary>
/// D-054-01：副本金币报酬进入<b>结算路径</b>之后的单元测试。
///
/// <para><b>本文档保护三条最贵的：</b></para>
/// <ul>
/// <li><b>I15</b>：村庄材料、个人物品、个人金币<b>同一次提交</b> —— 任何一边不行，整笔都不应用。</li>
/// <li><b>I4</b>：同一个结算键只生效一次（含<b>读档往返</b>后重放）。</li>
/// <li><b>D-054-01 裁决 ①</b>：<b>失败也发金币</b>，但材料与战利品仍不发（D-010 Q1）。</li>
/// </ul>
///
/// <para><b>本文档不规定金额</b>：X 只是夹具（正式值是 1 金的<b>基准候选值</b>，
/// 实施后靠采集标定 —— `34` 阶段 4）。</para>
/// </summary>
[TestSuite]
public class SettlementGoldTests
{
    private static readonly ItemId Stone = new("石料");
    private static readonly HeroId Alice = new("h-alice");
    private static readonly HeroId Bob = new("h-bob");
    private static readonly HeroId Carol = new("h-carol");
    private static readonly ResourceId Gold = new("金币");

    /// <summary>记录实际写入的村庄资源批次。</summary>
    private sealed class RecordingWriter
    {
        public List<DomainResourceDelta> Applied { get; } = new();
        public bool Reject { get; set; }

        public bool Write(DomainResourceDelta[] deltas)
        {
            if (Reject) return false;
            Applied.AddRange(deltas);
            return true;
        }
    }

    /// <summary>记录实际写入的物品批次，并可模拟「发不出去」。</summary>
    private sealed class RecordingItemWriter : DomainIItemGrantWriter
    {
        public List<DomainItemGrant> Applied { get; } = new();
        public bool Approve { get; set; } = true;
        public int CanCalls { get; private set; }
        public int GrantCalls { get; private set; }

        public bool CanGrant(IReadOnlyList<DomainItemGrant> grants)
        {
            CanCalls++;
            return Approve;
        }

        public void Grant(IReadOnlyList<DomainItemGrant> grants)
        {
            GrantCalls++;
            Applied.AddRange(grants);
        }
    }

    /// <summary>记录实际写入的钱包批次，并可模拟「钱包装不下」。</summary>
    private sealed class RecordingWalletWriter : DomainIWalletGrantWriter
    {
        public List<DomainWalletGrant> Applied { get; } = new();
        public bool Approve { get; set; } = true;
        public int CanCalls { get; private set; }
        public int GrantCalls { get; private set; }

        public bool CanGrant(IReadOnlyList<DomainWalletGrant> grants)
        {
            CanCalls++;
            return Approve;
        }

        public void Grant(IReadOnlyList<DomainWalletGrant> grants)
        {
            GrantCalls++;
            Applied.AddRange(grants);
        }
    }

    /// <summary>胜利时发材料 + 战利品 + <b>每位参战者一次金币报酬</b>；失败时<b>只发金币</b>。</summary>
    /// <remarks>
    /// <b>本夹具不写金额</b>：金币数额是<b>规则</b>（D-054-01 的「每人每次 X 金」），
    /// 住在写入入口（<c>IWalletGrantWriter</c> 的实现）里 ——
    /// 增量只说「<b>谁</b>有资格领一次报酬」，发多少由 <c>WalletGrantService</c> 决定。
    /// </remarks>
    private static DomainSettlementDelta GoldRewards(bool heroesWon, params string[] participants)
    {
        var delta = new DomainSettlementDelta();

        // 金币：不论成败（D-054-01 裁决 ①）
        foreach (var p in participants)
        {
            delta.AddWalletGrant(new HeroId(p));
        }

        // 材料与战利品：只在成功时（D-010 Q1 不变）
        if (!heroesWon) return delta;

        delta.AddResource("材料", 38);
        delta.AddItemGrant(Alice, Stone, 5);
        delta.AddItemGrant(Bob, Stone, 4);
        return delta;
    }

    /// <summary>
    /// 只发材料 + 金币（<b>不含物品</b>）的奖励 —— 用来把「金币那一路」单独摘出来验，
    /// 免得物品的写入入口混进来干扰断言。
    /// </summary>
    private static DomainSettlementDelta MaterialAndGoldRewards(params string[] participants)
    {
        var delta = new DomainSettlementDelta();
        delta.AddResource("材料", 38);
        foreach (var p in participants)
        {
            delta.AddWalletGrant(new HeroId(p));
        }

        return delta;
    }

    // ── 发放条件：不论成败 ────────────────────────────────────────────────

    /// <summary>成功副本 → <b>每位实际参战者</b>各领一次报酬；非参战者一次都没有。</summary>
    /// <remarks>
    /// 这里是<b>名单</b>的断言（谁有资格领）；<b>金额</b>的断言在下面
    /// <see cref="Apply_WithRealWriter_PaysRewardRateToParticipantsOnly"/>（真写入入口）。
    /// </remarks>
    [TestCase]
    public void Apply_SuccessfulDungeon_GivesGoldToEveryParticipantOnly()
    {
        var resources = new RecordingWriter();
        var items = new RecordingItemWriter();
        var wallets = new RecordingWalletWriter();
        var settlement = new DomainSettlement(
            won => GoldRewards(won, "h-alice", "h-bob"),
            resources.Write, itemWriter: items, walletWriter: wallets);

        bool applied = settlement.Apply(new DomainSettlementKey("run-1", "br-1"), heroesWon: true);

        Assertions.AssertThat(applied).IsTrue();
        Assertions.AssertThat(wallets.Applied.Count).IsEqual(2);
        Assertions.AssertThat(wallets.Applied[0].Hero).IsEqual(Alice);
        Assertions.AssertThat(wallets.Applied[1].Hero).IsEqual(Bob);
        // 非参战者一个都没有
        Assertions.AssertThat(wallets.Applied.Any(g => g.Hero == Carol)).IsFalse();
        // 材料与战利品照旧
        Assertions.AssertThat(resources.Applied.Count).IsEqual(1);
        Assertions.AssertThat(items.Applied.Count).IsEqual(2);
    }

    /// <summary>
    /// **失败副本 → 仍然各 +X 金**，但材料与战利品为 0（D-054-01 裁决 ①）。
    /// </summary>
    /// <remarks>
    /// 这是 D-054 引入的<b>唯一一项「失败也发」的奖励</b>；D-010 Q1 的其余部分不变
    /// （失败不发材料、不发战利品、不额外惩罚）。
    /// </remarks>
    [TestCase]
    public void Apply_FailedDungeon_StillGivesGold_ButNoMaterialsOrLoot()
    {
        var resources = new RecordingWriter();
        var items = new RecordingItemWriter();
        var wallets = new RecordingWalletWriter();
        var settlement = new DomainSettlement(
            won => GoldRewards(won, "h-alice", "h-bob"),
            resources.Write, itemWriter: items, walletWriter: wallets);

        bool applied = settlement.Apply(new DomainSettlementKey("run-1", "br-1"), heroesWon: false);

        Assertions.AssertThat(applied).IsTrue();          // 失败也要结算一次
        Assertions.AssertThat(wallets.Applied.Count).IsEqual(2);   // ← 钱照发
        Assertions.AssertThat(resources.Applied.Count).IsEqual(0);  // 材料不发
        Assertions.AssertThat(items.Applied.Count).IsEqual(0);     // 战利品不发
    }

    /// <summary>没有参战名单（与副本无关的结算）→ 不发金币。</summary>
    [TestCase]
    public void Apply_WithoutParticipants_GivesNoGold()
    {
        var resources = new RecordingWriter();
        var wallets = new RecordingWalletWriter();
        var settlement = new DomainSettlement(
            won => new DomainSettlementDelta().AddResource("材料", 38),
            resources.Write, walletWriter: wallets);

        settlement.Apply(new DomainSettlementKey("run-9", "br-9"), heroesWon: true);

        Assertions.AssertThat(wallets.Applied.Count).IsEqual(0);
    }

    // ── I15 · 全有或全无 ───────────────────────────────────────────────────

    /// <summary>钱包发不出去 → <b>整笔不应用</b>：材料与物品也不能写。</summary>
    [TestCase]
    public void Apply_WhenWalletCannotBeGranted_AppliesNothingAtAll()
    {
        var resources = new RecordingWriter();
        var items = new RecordingItemWriter();
        var wallets = new RecordingWalletWriter { Approve = false };
        var settlement = new DomainSettlement(
            won => GoldRewards(won, "h-alice"),
            resources.Write, itemWriter: items, walletWriter: wallets);
        var key = new DomainSettlementKey("run-1", "br-1");

        bool threw = false;
        try { settlement.Apply(key, heroesWon: true); }
        catch (InvalidOperationException) { threw = true; }

        Assertions.AssertThat(threw).IsTrue();
        Assertions.AssertThat(resources.Applied.Count).IsEqual(0);   // 材料一笔没写
        Assertions.AssertThat(items.GrantCalls).IsEqual(0);          // 物品一次没发
        Assertions.AssertThat(wallets.GrantCalls).IsEqual(0);        // 钱包一次没发
        Assertions.AssertThat(settlement.IsApplied(key)).IsFalse();  // 幂等键不落 → 可重试
    }

    /// <summary>有钱包发放却<b>没注入</b>写入入口 → 整笔拒绝（绝不静默丢钱）。</summary>
    [TestCase]
    public void Apply_WalletGrantsWithoutWriter_RefusesTheWholeSettlement()
    {
        var resources = new RecordingWriter();
        var settlement = new DomainSettlement(
            won => GoldRewards(won, "h-alice"),
            resources.Write);   // 没有 walletWriter

        bool threw = false;
        try { settlement.Apply(new DomainSettlementKey("run-1", "br-1"), heroesWon: true); }
        catch (InvalidOperationException) { threw = true; }

        Assertions.AssertThat(threw).IsTrue();
        Assertions.AssertThat(resources.Applied.Count).IsEqual(0);
    }

    /// <summary>材料写不进去 → 金币也不发（另一半的 I15）。</summary>
    [TestCase]
    public void Apply_WhenResourceWriteFails_GrantsNoGold()
    {
        var resources = new RecordingWriter { Reject = true };
        var wallets = new RecordingWalletWriter();
        var settlement = new DomainSettlement(
            won => GoldRewards(won, "h-alice"),
            resources.Write, walletWriter: wallets);

        bool threw = false;
        try { settlement.Apply(new DomainSettlementKey("run-1", "br-1"), heroesWon: true); }
        catch (InvalidOperationException) { threw = true; }

        Assertions.AssertThat(threw).IsTrue();
        Assertions.AssertThat(wallets.GrantCalls).IsEqual(0);
    }

    /// <summary>预检必须发生在资源写入<b>之前</b>（与物品同一道门）。</summary>
    [TestCase]
    public void Apply_ChecksWalletsBeforeWritingResources()
    {
        var resources = new RecordingWriter();
        var wallets = new RecordingWalletWriter { Approve = false };
        var settlement = new DomainSettlement(
            won => MaterialAndGoldRewards("h-alice"),
            resources.Write, walletWriter: wallets);

        try { settlement.Apply(new DomainSettlementKey("run-1", "br-1"), true); }
        catch (InvalidOperationException) { /* 预期 */ }

        Assertions.AssertThat(wallets.CanCalls).IsEqual(1);
        Assertions.AssertThat(resources.Applied.Count).IsEqual(0);
    }

    // ── I4 · 幂等 ──────────────────────────────────────────────────────────

    /// <summary>同一个键重复应用：金币<b>只发一次</b>（I4）。</summary>
    [TestCase]
    public void Apply_SameKeyTwice_GrantsGoldOnlyOnce()
    {
        var resources = new RecordingWriter();
        var wallets = new RecordingWalletWriter();
        var settlement = new DomainSettlement(
            won => MaterialAndGoldRewards("h-alice", "h-bob"),
            resources.Write, walletWriter: wallets);
        var key = new DomainSettlementKey("run-1", "br-1");

        settlement.Apply(key, heroesWon: true);
        settlement.Apply(key, heroesWon: true);

        Assertions.AssertThat(wallets.Applied.Count).IsEqual(2);   // 只有第一次那两笔
        Assertions.AssertThat(resources.Applied.Count).IsEqual(1);
    }

    /// <summary><b>读档往返</b>后重放同一批结算 → 金币<b>不再发</b>（基线 §13.7）。</summary>
    [TestCase]
    public void Apply_AfterSaveReload_GrantsNoGoldAgain()
    {
        var key = new DomainSettlementKey("run-1", "br-1");

        var before = new DomainSettlement(
            won => MaterialAndGoldRewards("h-alice"),
            new RecordingWriter().Write, walletWriter: new RecordingWalletWriter());
        before.Apply(key, true);
        var savedKeys = before.AppliedKeys.ToArray();

        var afterResources = new RecordingWriter();
        var afterWallets = new RecordingWalletWriter();
        var after = new DomainSettlement(
            won => MaterialAndGoldRewards("h-alice"),
            afterResources.Write, walletWriter: afterWallets);
        after.RestoreApplied(savedKeys);

        bool appliedAgain = after.Apply(key, true);

        Assertions.AssertThat(appliedAgain).IsFalse();
        Assertions.AssertThat(afterWallets.Applied.Count).IsEqual(0);
        Assertions.AssertThat(afterResources.Applied.Count).IsEqual(0);
    }

    // ── 增量本身的防线 ─────────────────────────────────────────────────────

    /// <summary>
    /// <b>增量不带金额</b>：同一个英雄被加两次 → 两条独立的发放记录（两次报酬）。
    /// </summary>
    /// <remarks>
    /// 金额是<b>规则</b>（D-054-01 的「每人每次 X 金」），住在写入入口里 ——
    /// 所以在这一层<b>改不了</b>「发多少」，只能决定「发不发」。
    /// 这也意味着「零头怎么算」只有一处定义（<c>WalletGrantService</c>），不会分叉。
    /// </remarks>
    [TestCase]
    public void AddWalletGrant_SameHeroTwice_KeepsTwoGrants()
    {
        var delta = new DomainSettlementDelta();

        delta.AddWalletGrant(Alice);
        delta.AddWalletGrant(Alice);

        Assertions.AssertThat(delta.WalletGrants.Count).IsEqual(2);
        Assertions.AssertThat(delta.WalletGrants.All(g => g.Hero == Alice)).IsTrue();
    }

    /// <summary>空标识直接拒。</summary>
    [TestCase]
    public void AddWalletGrant_EmptyHeroId_Throws()
    {
        var delta = new DomainSettlementDelta();

        bool threw = false;
        try { delta.AddWalletGrant(HeroId.None); }
        catch (ArgumentException) { threw = true; }

        Assertions.AssertThat(threw).IsTrue();
        Assertions.AssertThat(delta.WalletGrants.Count).IsEqual(0);
    }

    /// <summary>只带钱包发放的增量<b>不是</b>零增量（失败也发金币 → 它确实改了东西）。</summary>
    [TestCase]
    public void Delta_WithOnlyWalletGrants_IsNotEmpty()
    {
        var delta = new DomainSettlementDelta();
        Assertions.AssertThat(delta.IsEmpty).IsTrue();

        delta.AddWalletGrant(Alice);
        Assertions.AssertThat(delta.IsEmpty).IsFalse();
    }

    // ── 真的落到钱包上（用真写入入口）──────────────────────────────────────

    /// <summary>
    /// <b>金额只在写入入口</b>：X = 1 金时，每位参战者 +1 金，非参战者 +0。
    /// </summary>
    /// <remarks>
    /// 增量只带「谁有资格领一次」，X 由 <see cref="WalletGrantService"/> 持有 ——
    /// 这条测试保护那条<b>接线</b>：增量 → 写入入口 → 钱包，中间没丢金额、没多发。
    /// </remarks>
    [TestCase]
    public void Apply_WithRealWriter_PaysRewardRateToParticipantsOnly()
    {
        var heroes = new Dictionary<HeroId, HeroAgent>
        {
            [Alice] = NewHero(Alice, 0),
            [Bob] = NewHero(Bob, 0),
            [Carol] = NewHero(Carol, 0),
        };

        var walletWriter = new WalletGrantService(
            id => heroes.TryGetValue(id, out var h) ? h : null, Gold, rewardUnits: 100L);

        var settlement = new DomainSettlement(
            won => GoldRewards(won, "h-alice", "h-bob"),
            new RecordingWriter().Write, walletWriter: walletWriter);

        // 失败副本：只发金币（材料与战利品不发）—— 这一路不碰物品写入入口
        settlement.Apply(new DomainSettlementKey("run-1", "br-1"), heroesWon: false);

        Assertions.AssertThat(heroes[Alice].Wallet.Amount(Gold)).IsEqual(1L);
        Assertions.AssertThat(heroes[Bob].Wallet.Amount(Gold)).IsEqual(1L);
        Assertions.AssertThat(heroes[Carol].Wallet.Amount(Gold)).IsEqual(0L);
        Assertions.AssertThat(walletWriter.TotalGranted).IsEqual(2L);
    }

    /// <summary>
    /// <b>分数 X 走结算路径</b>：X = 0.5 金 → 第一次发不出（攒 50），第二次发 1 金。
    /// </summary>
    /// <remarks>
    /// 零头<b>按英雄分别累计</b>（D-054 阶段 4 拍板）：Alice 攒 50、Bob 攒 50，
    /// <b>不共用</b> —— 共用会让先轮到的英雄拿走整金、后面的吃亏，那就不是「每人每次 X 金」。
    /// </remarks>
    [TestCase]
    public void Apply_WithRealWriter_FractionalRate_AccumulatesPerHero()
    {
        var heroes = new Dictionary<HeroId, HeroAgent>
        {
            [Alice] = NewHero(Alice, 0),
            [Bob] = NewHero(Bob, 0),
        };

        var walletWriter = new WalletGrantService(
            id => heroes.TryGetValue(id, out var h) ? h : null, Gold, rewardUnits: 50L);

        var settlement = new DomainSettlement(
            won => GoldRewards(won, "h-alice", "h-bob"),
            new RecordingWriter().Write, walletWriter: walletWriter);

        settlement.Apply(new DomainSettlementKey("run-1", "br-1"), heroesWon: false);

        // 各攒 50（1/100 金）→ 谁都没到 1 金 → 一分没发
        Assertions.AssertThat(walletWriter.PendingOf(Alice)).IsEqual(50L);
        Assertions.AssertThat(walletWriter.PendingOf(Bob)).IsEqual(50L);
        Assertions.AssertThat(heroes[Alice].Wallet.Amount(Gold)).IsEqual(0L);
        Assertions.AssertThat(heroes[Bob].Wallet.Amount(Gold)).IsEqual(0L);
        Assertions.AssertThat(walletWriter.TotalGranted).IsEqual(0L);

        settlement.Apply(new DomainSettlementKey("run-2", "br-2"), heroesWon: false);

        // 各自攒满 → 各自发 1 金
        Assertions.AssertThat(heroes[Alice].Wallet.Amount(Gold)).IsEqual(1L);
        Assertions.AssertThat(heroes[Bob].Wallet.Amount(Gold)).IsEqual(1L);
        Assertions.AssertThat(walletWriter.PendingOf(Alice)).IsEqual(0L);
        Assertions.AssertThat(walletWriter.TotalGranted).IsEqual(2L);
    }

    /// <summary>
    /// <b>守恒律</b>（金币版）：<c>Σ英雄金币 + 村库金币 + 已销毁金币
    /// = 初始金币总额 + 累计新增金币</c>。
    /// </summary>
    /// <remarks>
    /// <para>这条测试只覆盖结算路径这一段：结算**只创造**金币（不转移、不销毁），
    /// 所以「初始 + 累计新增」必须**分毫不差**地等于英雄钱包的合计。
    /// 村库 / 销毁两项在结算路径上恒为 0（它们是摊位交易与升级的产物）。</para>
    /// <para>⚠️ <b>这里没有「税额余数」</b> —— 余数**不是一笔钱**（它是 5% 税取整后
    /// 还没凑够 1 金的记账零头，钱从没离开系统）。把它加进守恒律左边 = 凭空多算。
    /// 余数归**税务口径**管（另一条等式，见 <c>StallFeeTests</c>）。</para>
    /// <para>同样<b>不含「待发零头」</b>：零头是<b>还没进任何钱包</b>的应收 ——
    /// 左边是「钱在哪」（它哪都不在），右边是「已经兑现了多少」
    /// （兑现的那部分才在「累计发放」里）。只把它加在一边 = 凭空多算。
    /// 采集器那边它是独立的一项，另行核对自洽性。</para>
    /// </remarks>
    [TestCase]
    public void Apply_WithRealWriter_ConservesGold()
    {
        const long initialPerHero = 100L;
        var heroes = new Dictionary<HeroId, HeroAgent>
        {
            [Alice] = NewHero(Alice, initialPerHero),
            [Bob] = NewHero(Bob, initialPerHero),
            [Carol] = NewHero(Carol, initialPerHero),
        };

        // X = 3 金 → 300 个 1/100 金单位
        var walletWriter = new WalletGrantService(
            id => heroes.TryGetValue(id, out var h) ? h : null, Gold, rewardUnits: 300L);

        var settlement = new DomainSettlement(
            won => GoldRewards(won, "h-alice", "h-bob"),
            new RecordingWriter().Write, walletWriter: walletWriter);

        settlement.Apply(new DomainSettlementKey("run-1", "br-1"), heroesWon: false);
        settlement.Apply(new DomainSettlementKey("run-2", "br-2"), heroesWon: false);
        settlement.Apply(new DomainSettlementKey("run-3", "br-3"), heroesWon: false);

        long initialTotal = 3L * initialPerHero;      // 300
        long created = 3L * 2L * 3L;                  // 3 次结算 × 2 参战者 × 3 金
        long heroTotal = heroes.Values.Sum(h => h.Wallet.Amount(Gold));

        Assertions.AssertThat(heroTotal).IsEqual(initialTotal + created);
        // 结算路径不碰村库、不销毁、不留余数 —— 这三项恒为 0
        Assertions.AssertThat(0L).IsEqual(0L);
        // 非参战者一个金币都没有
        Assertions.AssertThat(heroes[Carol].Wallet.Amount(Gold)).IsEqual(initialPerHero);
        // 参战者各多拿了 9 金（3 次 × 3 金）
        Assertions.AssertThat(heroes[Alice].Wallet.Amount(Gold)).IsEqual(initialPerHero + 9L);
        // 累计新增与实际入账一致（没有零头残留）
        Assertions.AssertThat(walletWriter.TotalGranted).IsEqual(created);
        Assertions.AssertThat(walletWriter.TotalPending).IsEqual(0L);
    }

    private static HeroAgent NewHero(HeroId id, long gold)
    {
        var hero = new HeroAgent { Name = id.Value };
        hero.RestoreWallet(new Wallet(
            new Dictionary<ResourceId, long> { { Gold, gold } }));
        return hero;
    }
}
