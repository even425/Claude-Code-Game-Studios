using System;
using System.Collections.Generic;
using System.Linq;
using GdUnit4;
using ClaudeCodeGameStudios.Application;
using ClaudeCodeGameStudios.Domain.Heroes;
using ClaudeCodeGameStudios.Domain.Items;
using ClaudeCodeGameStudios.Domain.Village;
// ⚠️ 别名：这个测试的命名空间叫 `...Tests.Settlement`，
// 而领域类型也叫 `Settlement` ⇒ 不加别名的话 `Settlement` 指的是命名空间（CS0118）。
using DomainSettlement = ClaudeCodeGameStudios.Domain.Settlement.Settlement;
using DomainSettlementDelta = ClaudeCodeGameStudios.Domain.Settlement.SettlementDelta;
using DomainSettlementKey = ClaudeCodeGameStudios.Domain.Settlement.SettlementKey;

namespace ClaudeCodeGameStudios.Tests.Settlement;

/// <summary>
/// <b>IT-13 · I15 —— 结算要么完整应用，要么不应用</b>（L2 集成层）。
///
/// <para><b>它验的是什么</b>：一次结算会同时动<b>三个地方</b> ——
/// 村庄的资源、英雄的<b>背包</b>、英雄的<b>钱包</b>。
/// I15 要求：这三处的总增量<b>要么全为 0，要么等于结算结果给出的完整增量</b>，
/// <b>不存在部分入账</b>。</para>
///
/// <para><b>为什么单测证明不了它</b>：单测只能验 <c>Settlement</c> 拿到一个
/// <b>桩</b>写入入口时的行为 —— 而桩的原子性是「自己写给自己的」。
/// I15 的真实风险在<b>三个真服务各自的边界</b>：
/// 村庄资源写不进（扣成负数 / 溢出）、背包装不下、钱包装不下。
/// 任一处失败，三处都<b>不能</b>动。</para>
///
/// <para><b>注入点</b>（TP-001 问的就是这个）：
/// <b>失败发生在「应用」之前</b> —— 也就是各写入入口的<b>预检</b>那一侧。
/// 理由不是「这样最简单」，而是：<b>提交阶段按设计不会失败</b>
/// （预检已排除全部失败路径，提交要么整笔成功、要么根本不开始）。
/// 所以「应用前失败」<b>就是</b>唯一真实存在的失败形态 ——
/// 真去注入一个「提交到一半才炸」的故障，等于造一个<b>设计上不可能发生</b>的场景，
/// 测出来的不是生产代码的性质，是想象出来的性质。</para>
///
/// <para><b>断言写的是「性质」不是「数值」</b>：本文不预设任何设计数值
/// （材料该给多少属【待定】P-08，金币该给多少属【待定】P-14）——
/// 它只断言<b>三处要么一起动，要么一起不动</b>。</para>
///
/// <para><b>为什么要有这条</b>：I15 是验收计划里<b>最后一条</b>还标着「受阻」的不变量。
/// 它的阻塞点从来不是「语义没定」（语义在基线里<b>已确认</b>），
/// 而是「怎么在集成层证明它」。而村庄那一侧的写入原先是
/// <c>Main.ApplyWrites</c>（<b>私有方法</b>）⇒ 测试<b>够不着</b> ⇒ 只能验桩。
/// 现在它被抽成 <see cref="VillageResourceWriter"/>（与另两个写入入口同构），
/// 这条测试才第一次跑在<b>真服务</b>上。</para>
/// </summary>
[TestSuite]
public class SettlementAtomicityTests
{
    private static readonly ItemId Stone = new("石料");
    private static readonly ItemId Cloth = new("布匹");
    /// <summary>资源种类名（<c>SettlementDelta.AddResource</c> 收的是字符串）。</summary>
    private const string MaterialsName = "材料";
    private const string GoldName = "金币";
    /// <summary>同一个资源种类的强类型标识（<c>Village.Get</c> / <c>Wallet.Amount</c> 收它）。</summary>
    private static readonly ResourceId Materials = new(MaterialsName);
    private static readonly ResourceId Gold = new(GoldName);

    private static HeroId H(string id) => new(id);

    /// <summary>一次结算牵动的三个地方（村庄 + 两位英雄）。</summary>
    private sealed class Scene
    {
        public required Village Village;
        public required HeroAgent Alice;
        public required HeroAgent Bob;
        public required ItemGrantService Items;
        public required WalletGrantService Wallets;
        public required VillageResourceWriter Resources;
        public required DomainSettlement Settlement;

        /// <summary>村庄资源的快照（用于「逐项相同」比对）。</summary>
        public string VillageSnapshot() =>
            string.Join(",", Village.Snapshot().OrderBy(kv => kv.Key, StringComparer.Ordinal)
                .Select(kv => $"{kv.Key}={kv.Value}"));

        /// <summary>英雄侧的快照：背包 + 钱包。</summary>
        public string HeroSnapshot() =>
            string.Join("|", new[] { Alice, Bob }.Select(h =>
                $"{h.Id.Value}:bag[{h.Bag.Count(Stone)},{h.Bag.Count(Cloth)}]" +
                $":gold[{h.Wallet.Amount(Gold)}]"));
    }

    /// <summary>
    /// 造一个真实的三方场景：村庄用真 <see cref="Village"/>，
    /// 物品/钱包/资源三个写入入口都用<b>真服务</b>。
    /// </summary>
    private static Scene MakeScene()
    {
        var village = new Village("村庄");
        var alice = new HeroAgent { Name = "Alice" };
        var bob = new HeroAgent { Name = "Bob" };
        // Id 是只读的（只有创建方能设）⇒ 走它自己的入口。
        alice.AssignId(H("h-alice"));
        bob.AssignId(H("h-bob"));
        var heroes = new Dictionary<HeroId, HeroAgent>
        {
            [alice.Id] = alice,
            [bob.Id] = bob,
        };

        var items = new ItemGrantService(id => heroes.TryGetValue(id, out var h) ? h : null,
            TestPrices.Book);
        var wallets = new WalletGrantService(
            id => heroes.TryGetValue(id, out var h) ? h : null, Gold, 100);
        var resources = new VillageResourceWriter(village);

        // 奖励内容：村庄 + 材料，两名英雄各一份石料 + 一次金币报酬。
        var settlement = new DomainSettlement(
            won => new DomainSettlementDelta()
                .AddResource(MaterialsName, 7)
                .AddItemGrant(alice.Id, Stone, 3)
                .AddItemGrant(bob.Id, Stone, 2)
                .AddWalletGrant(alice.Id)
                .AddWalletGrant(bob.Id),
            resources.Write,
            itemWriter: items,
            walletWriter: wallets);

        return new Scene
        {
            Village = village,
            Alice = alice,
            Bob = bob,
            Items = items,
            Wallets = wallets,
            Resources = resources,
            Settlement = settlement,
        };
    }

    private static readonly DomainSettlementKey Key = new("run-1", "br-1");

    // ── 成功路径 ────────────────────────────────────────────────────────────

    /// <summary>
    /// 一切可行 ⇒ 三处<b>都</b>到位（这是「要么等于完整增量」的那一半）。
    /// </summary>
    [TestCase]
    public void Apply_AllFeasible_TouchesAllThreePlaces()
    {
        var s = MakeScene();

        bool applied = s.Settlement.Apply(Key, heroesWon: true);

        Assertions.AssertThat(applied).IsTrue();
        Assertions.AssertThat(s.Village.Get(Materials)).IsEqual(7L);
        Assertions.AssertThat(s.Alice.Bag.Count(Stone)).IsEqual(3L);
        Assertions.AssertThat(s.Bob.Bag.Count(Stone)).IsEqual(2L);
        Assertions.AssertThat(s.Alice.Wallet.Amount(Gold)).IsEqual(1L);
        Assertions.AssertThat(s.Bob.Wallet.Amount(Gold)).IsEqual(1L);
    }

    // ── 失败注入：村庄资源那一侧 ────────────────────────────────────────────

    /// <summary>
    /// <b>村庄资源扣不动</b>（余额不足 ⇒ 预检拒绝）⇒
    /// <b>村庄不动、背包不动、钱包不动</b> —— 一处都不能动。
    /// </summary>
    [TestCase]
    public void Apply_VillageResourceInfeasible_TouchesNobody()
    {
        var s = MakeScene();
        var villageBefore = s.VillageSnapshot();
        var heroesBefore = s.HeroSnapshot();

        // 村庄只有 3 材料，结算要扣 7 ⇒ 预检拒绝。
        var settlement = new DomainSettlement(
            won => new DomainSettlementDelta()
                .AddResource(MaterialsName, -7)
                .AddItemGrant(s.Alice.Id, Stone, 3)
                .AddWalletGrant(s.Alice.Id),
            s.Resources.Write,
            itemWriter: s.Items,
            walletWriter: s.Wallets);

        bool threw = false;
        try { settlement.Apply(Key, heroesWon: true); }
        catch (InvalidOperationException) { threw = true; }

        Assertions.AssertThat(threw).IsTrue();
        Assertions.AssertThat(s.VillageSnapshot()).IsEqual(villageBefore);
        Assertions.AssertThat(s.HeroSnapshot()).IsEqual(heroesBefore);
        Assertions.AssertThat(settlement.IsApplied(Key)).IsFalse();
    }

    /// <summary>
    /// <b>村庄资源会溢出</b>（加上后越界 ⇒ 预检拒绝）⇒ 三处都不动。
    /// </summary>
    /// <remarks>
    /// ⚠️ **溢出那一档要「余额非零」才撞得到**：村庄材料余额是 0 时，
    /// 加上 <c>long.MaxValue</c> 恰好**不溢出**（0 + MaxValue = MaxValue，合法）——
    /// 于是预检放行、整笔落地。必须先给村庄放 1 材料，
    /// 余额 1 + MaxValue 才会绕成负数 ⇒ 被预检拦下。
    /// </remarks>
    [TestCase]
    public void Apply_VillageResourceOverflows_TouchesNobody()
    {
        var s = MakeScene();
        // 先让村庄有 1 材料 —— 余额为 0 时加 long.MaxValue **不溢出**。
        s.Village.Add(Materials, 1);
        var villageBefore = s.VillageSnapshot();
        var heroesBefore = s.HeroSnapshot();

        var settlement = new DomainSettlement(
            won => new DomainSettlementDelta()
                .AddResource(MaterialsName, long.MaxValue)
                .AddItemGrant(s.Alice.Id, Stone, 3)
                .AddWalletGrant(s.Alice.Id),
            s.Resources.Write,
            itemWriter: s.Items,
            walletWriter: s.Wallets);

        bool threw = false;
        try { settlement.Apply(Key, heroesWon: true); }
        catch (InvalidOperationException) { threw = true; }

        Assertions.AssertThat(threw).IsTrue();
        Assertions.AssertThat(s.VillageSnapshot()).IsEqual(villageBefore);
        Assertions.AssertThat(s.HeroSnapshot()).IsEqual(heroesBefore);
    }

    // ── 失败注入：英雄背包那一侧 ────────────────────────────────────────────

    /// <summary>
    /// <b>背包装不下</b>（预检拒绝）⇒ <b>村庄不动</b>、别的英雄的背包也不动。
    /// <para>这条最值钱：<b>村庄资源与英雄物品在同一次结算里</b> ——
    /// 若提交顺序反了（先写村庄再发物品），村庄会拿到材料而英雄两手空空，
    /// 那正是「半笔账」，而它<b>在界面上完全看不出来</b>。</para>
    /// </summary>
    [TestCase]
    public void Apply_BagCannotHold_TouchesNobody()
    {
        var s = MakeScene();
        // Alice 的背包容量 3，要装 3 件石料 —— 装得下。
        // 把容量改成 2 ⇒ 装不下 ⇒ 预检拒绝。
        s.Alice.RestoreBag(new Inventory(capacity: 2));

        var villageBefore = s.VillageSnapshot();
        var heroesBefore = s.HeroSnapshot();

        bool threw = false;
        try { s.Settlement.Apply(Key, heroesWon: true); }
        catch (InvalidOperationException) { threw = true; }

        Assertions.AssertThat(threw).IsTrue();
        Assertions.AssertThat(s.VillageSnapshot()).IsEqual(villageBefore);
        Assertions.AssertThat(s.HeroSnapshot()).IsEqual(heroesBefore);
        Assertions.AssertThat(s.Settlement.IsApplied(Key)).IsFalse();
    }

    // ── 失败注入：英雄钱包那一侧 ────────────────────────────────────────────

    /// <summary>
    /// <b>钱包装不下</b>（预检拒绝）⇒ 村庄、背包、钱包<b>全都不动</b>。
    /// </summary>
    /// <remarks>
    /// ⚠️ **怎么让钱包「装不下」**：正式钱包是<b>无限容量</b>（capacity = 0），
    /// 此时唯一的拒收条件是<b>溢出</b>。所以这一档给英雄换一只
    /// <b>容量 1 的钱包</b>，再发 2 金 ⇒ 超容量 ⇒ 预检拒绝。
    /// <br>⚠️ 报酬率的单位是 <b>1/100 金</b>（D-054-01）⇒ 100 单位 = 1 金。
    /// 我第一版写「报酬率 10 ⇒ 钱包里 10 金」—— 那会断言成
    /// 「钱包里有 10 **单位**」⇒ 实际金币仍是 0 ⇒ 断言数错了单位。
    /// </remarks>
    [TestCase]
    public void Apply_WalletCannotHold_TouchesNobody()
    {
        var s = MakeScene();
        // 容量 1 的钱包：发 2 金就装不下。
        s.Alice.RestoreWallet(new Wallet(capacity: 1));

        var villageBefore = s.VillageSnapshot();
        var heroesBefore = s.HeroSnapshot();

        // 报酬率 200 单位 = 2 金 ⇒ 超出容量 1 ⇒ 预检拒绝。
        var wallets = new WalletGrantService(
            id => id == s.Alice.Id ? s.Alice : (id == s.Bob.Id ? s.Bob : null),
            Gold, 200);
        var settlement = new DomainSettlement(
            won => new DomainSettlementDelta()
                .AddResource(MaterialsName, 7)
                .AddItemGrant(s.Alice.Id, Stone, 3)
                .AddWalletGrant(s.Alice.Id),
            s.Resources.Write,
            itemWriter: s.Items,
            walletWriter: wallets);

        bool threw = false;
        try { settlement.Apply(Key, heroesWon: true); }
        catch (InvalidOperationException) { threw = true; }

        Assertions.AssertThat(threw).IsTrue();
        Assertions.AssertThat(s.VillageSnapshot()).IsEqual(villageBefore);
        Assertions.AssertThat(s.HeroSnapshot()).IsEqual(heroesBefore);
        Assertions.AssertThat(settlement.IsApplied(Key)).IsFalse();
    }

    // ── 失败之后：可以重试 ─────────────────────────────────────────────────

    /// <summary>
    /// 失败之后<b>幂等键没有落下</b> ⇒ 同一笔结算<b>可以重试</b>，
    /// 重试成功时三处**一起**到位。
    /// <para>为什么这一条不能少：幂等键若在失败时也被记下，
    /// 这次失败就变成了<b>永久丢失</b>（再也不会补发）——
    /// 而玩家只会发现「我明明参战了，怎么没有」。</para>
    /// </summary>
    [TestCase]
    public void Apply_AfterRejection_CanBeRetriedAndThenAppliesEverything()
    {
        var s = MakeScene();
        var villageBefore = s.VillageSnapshot();
        var heroesBefore = s.HeroSnapshot();

        // 第一笔：村庄扣 7（只有 3）⇒ 失败。
        var failing = new DomainSettlement(
            won => new DomainSettlementDelta()
                .AddResource(MaterialsName, -7)
                .AddItemGrant(s.Alice.Id, Stone, 3)
                .AddWalletGrant(s.Alice.Id),
            s.Resources.Write,
            itemWriter: s.Items,
            walletWriter: s.Wallets);

        bool threw = false;
        try { failing.Apply(Key, heroesWon: true); }
        catch (InvalidOperationException) { threw = true; }

        Assertions.AssertThat(threw).IsTrue();
        Assertions.AssertThat(failing.IsApplied(Key)).IsFalse();

        // 第二笔：同一个键，村庄侧改成一笔可行的 ⇒ 应当整体落地。
        var retry = new DomainSettlement(
            won => new DomainSettlementDelta()
                .AddResource(MaterialsName, 7)
                .AddItemGrant(s.Alice.Id, Stone, 3)
                .AddWalletGrant(s.Alice.Id),
            s.Resources.Write,
            itemWriter: s.Items,
            walletWriter: s.Wallets);

        bool applied = retry.Apply(Key, heroesWon: true);

        Assertions.AssertThat(applied).IsTrue();
        Assertions.AssertThat(s.Village.Get(Materials)).IsEqual(7L);
        Assertions.AssertThat(s.Alice.Bag.Count(Stone)).IsEqual(3L);
        Assertions.AssertThat(s.Alice.Wallet.Amount(Gold)).IsEqual(1L);
        Assertions.AssertThat(villageBefore).IsNotEqual(s.VillageSnapshot());
        Assertions.AssertThat(heroesBefore).IsNotEqual(s.HeroSnapshot());
    }

    // ── 失败之后：幂等键真的没落下（直接看集合） ──────────────────────────

    /// <summary>
    /// 失败后 <c>AppliedKeys</c> 里<b>没有</b>这个键（不只看 <c>IsApplied</c>）。
    /// </summary>
    [TestCase]
    public void Apply_AfterRejection_TheKeyIsNotInAppliedKeys()
    {
        var s = MakeScene();
        var settlement = new DomainSettlement(
            won => new DomainSettlementDelta().AddResource(MaterialsName, -7),
            s.Resources.Write);

        bool threw = false;
        try { settlement.Apply(Key, heroesWon: true); }
        catch (InvalidOperationException) { threw = true; }

        Assertions.AssertThat(threw).IsTrue();
        Assertions.AssertThat(settlement.AppliedKeys.Contains(Key.ToString())).IsFalse();
    }
}

