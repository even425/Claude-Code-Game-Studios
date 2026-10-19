using System;
using System.Collections.Generic;
using System.Linq;
using GdUnit4;
using ClaudeCodeGameStudios.Domain.Heroes;
using ClaudeCodeGameStudios.Domain.Items;
using ClaudeCodeGameStudios.Domain.Settlement;

/// <summary>
/// R-01 M2/M3：副本战利品进入<b>结算路径</b>之后的单元测试。
///
/// <para><b>本文档保护两条最贵的：</b></para>
/// <ul>
/// <li><b>I15</b>：村庄资源与个人物品<b>同一次提交</b> —— 任何一边不行，整笔都不应用。</li>
/// <li><b>I4</b>：同一个结算键只生效一次（含<b>读档往返</b>后重放）。</li>
/// </ul>
///
/// <para><b>为什么物品必须进结算增量，而不是另发一次</b>：
/// 分两次发，中间任何一步失败都会留下「村庄拿到了、英雄没拿到」的半笔账。
/// 把它塞进同一个增量，两半才能共用<b>同一把幂等锁</b>。</para>
///
/// <para><b>本文档不规定任何奖励数值</b>：物品与数量只是夹具。</para>
/// </summary>
[TestSuite]
public class SettlementLootTests
{
    private static readonly ItemId Stone = new("石料");
    private static readonly HeroId Alice = new("h-alice");
    private static readonly HeroId Bob = new("h-bob");

    /// <summary>记录实际写入的资源批次。</summary>
    private sealed class RecordingWriter
    {
        public List<ResourceDelta> Applied { get; } = new();
        public bool Reject { get; set; }

        public bool Write(ResourceDelta[] deltas)
        {
            if (Reject) return false;
            Applied.AddRange(deltas);
            return true;
        }
    }

    /// <summary>
    /// 记录实际写入的物品批次，并可模拟「发不出去」。
    /// </summary>
    private sealed class RecordingItemWriter : IItemGrantWriter
    {
        public List<ItemGrant> Applied { get; } = new();
        public bool Approve { get; set; } = true;
        public int CanCalls { get; private set; }
        public int GrantCalls { get; private set; }

        public bool CanGrant(IReadOnlyList<ItemGrant> grants)
        {
            CanCalls++;
            return Approve;
        }

        public void Grant(IReadOnlyList<ItemGrant> grants)
        {
            GrantCalls++;
            Applied.AddRange(grants);
        }
    }

    /// <summary>胜利时给一份资源和两份额外物品；失败时什么都不给。</summary>
    private static SettlementDelta LootyRewards(bool heroesWon)
    {
        var delta = new SettlementDelta();
        if (!heroesWon) return delta;

        delta.AddResource("材料", 38);
        delta.AddItemGrant(Alice, Stone, 5);
        delta.AddItemGrant(Bob, Stone, 4);
        return delta;
    }

    // ── 同一次提交 ─────────────────────────────────────────────────────────

    /// <summary>成功时：资源与物品一起落下去，幂等键也记上。</summary>
    [TestCase]
    public void Apply_WithItemGrants_WritesResourcesAndItemsTogether()
    {
        var resources = new RecordingWriter();
        var items = new RecordingItemWriter();
        var settlement = new Settlement(LootyRewards, resources.Write, itemWriter: items);
        var key = new SettlementKey("run-1", "br-1");

        bool applied = settlement.Apply(key, heroesWon: true);

        Assertions.AssertThat(applied).IsTrue();
        Assertions.AssertThat(resources.Applied.Count).IsEqual(1);
        Assertions.AssertThat(items.Applied.Count).IsEqual(2);
        Assertions.AssertThat(settlement.IsApplied(key)).IsTrue();
    }

    /// <summary>失败时：什么都不发（D-010 Q1 / R01-D05「副本失败不发放战利品」）。</summary>
    [TestCase]
    public void Apply_FailedDungeon_GrantsNoItems()
    {
        var resources = new RecordingWriter();
        var items = new RecordingItemWriter();
        var settlement = new Settlement(LootyRewards, resources.Write, itemWriter: items);

        bool applied = settlement.Apply(new SettlementKey("run-1", "br-1"), heroesWon: false);

        Assertions.AssertThat(applied).IsTrue();      // 失败也要结算一次（幂等键要防的就是这条路）
        Assertions.AssertThat(resources.Applied.Count).IsEqual(0);
        Assertions.AssertThat(items.Applied.Count).IsEqual(0);
    }

    // ── I15 · 全有或全无 ───────────────────────────────────────────────────

    /// <summary>
    /// 物品发不出去 → <b>整笔不应用</b>：村庄资源也不能写。
    /// 这正是「背包满就整次回滚」那条规则的落点。
    /// </summary>
    [TestCase]
    public void Apply_WhenItemsCannotBeGranted_AppliesNothingAtAll()
    {
        var resources = new RecordingWriter();
        var items = new RecordingItemWriter { Approve = false };
        var settlement = new Settlement(LootyRewards, resources.Write, itemWriter: items);
        var key = new SettlementKey("run-1", "br-1");

        bool threw = false;
        try { settlement.Apply(key, heroesWon: true); }
        catch (InvalidOperationException) { threw = true; }

        Assertions.AssertThat(threw).IsTrue();
        Assertions.AssertThat(resources.Applied.Count).IsEqual(0);   // 资源一笔都没写
        Assertions.AssertThat(items.GrantCalls).IsEqual(0);          // 物品一次都没发
        Assertions.AssertThat(settlement.IsApplied(key)).IsFalse();  // 幂等键不落 → 可重试
    }

    /// <summary>资源写不进去 → 物品也不发（另一半的 I15）。</summary>
    [TestCase]
    public void Apply_WhenResourceWriteFails_GrantsNoItems()
    {
        var resources = new RecordingWriter { Reject = true };
        var items = new RecordingItemWriter();
        var settlement = new Settlement(LootyRewards, resources.Write, itemWriter: items);

        bool threw = false;
        try { settlement.Apply(new SettlementKey("run-1", "br-1"), heroesWon: true); }
        catch (InvalidOperationException) { threw = true; }

        Assertions.AssertThat(threw).IsTrue();
        Assertions.AssertThat(items.Applied.Count).IsEqual(0);
    }

    /// <summary>
    /// 结算增量里有物品，却<b>没注入</b>写入入口 → 整笔拒绝。
    /// 绝不把物品悄悄丢掉：那会让「奖励发出去了」成为一句谎话。
    /// </summary>
    [TestCase]
    public void Apply_ItemGrantsWithoutWriter_RefusesTheWholeSettlement()
    {
        var resources = new RecordingWriter();
        var settlement = new Settlement(LootyRewards, resources.Write);   // 没有 itemWriter

        bool threw = false;
        try { settlement.Apply(new SettlementKey("run-1", "br-1"), heroesWon: true); }
        catch (InvalidOperationException) { threw = true; }

        Assertions.AssertThat(threw).IsTrue();
        Assertions.AssertThat(resources.Applied.Count).IsEqual(0);
    }

    // ── I4 · 幂等 ──────────────────────────────────────────────────────────

    /// <summary>同一个键重复应用：物品只发一次（I4）。</summary>
    [TestCase]
    public void Apply_SameKeyTwice_GrantsItemsOnlyOnce()
    {
        var resources = new RecordingWriter();
        var items = new RecordingItemWriter();
        var settlement = new Settlement(LootyRewards, resources.Write, itemWriter: items);
        var key = new SettlementKey("run-1", "br-1");

        settlement.Apply(key, heroesWon: true);
        settlement.Apply(key, heroesWon: true);

        Assertions.AssertThat(items.Applied.Count).IsEqual(2);   // 只有第一次那两笔
        Assertions.AssertThat(resources.Applied.Count).IsEqual(1);
    }

    /// <summary>
    /// <b>读档往返</b>：恢复幂等标志后重放同一批结算，物品<b>不再发</b>。
    /// 这正是基线 §13.7「已完成事件不会重复应用」要防的场景（E10）。
    /// </summary>
    [TestCase]
    public void Apply_AfterSaveReload_GrantsNoItemsAgain()
    {
        var key = new SettlementKey("run-1", "br-1");

        var before = new Settlement(LootyRewards, new RecordingWriter().Write, itemWriter: new RecordingItemWriter());
        before.Apply(key, true);
        var savedKeys = before.AppliedKeys.ToArray();

        var afterResources = new RecordingWriter();
        var afterItems = new RecordingItemWriter();
        var after = new Settlement(LootyRewards, afterResources.Write, itemWriter: afterItems);
        after.RestoreApplied(savedKeys);

        bool appliedAgain = after.Apply(key, true);

        Assertions.AssertThat(appliedAgain).IsFalse();
        Assertions.AssertThat(afterItems.Applied.Count).IsEqual(0);
        Assertions.AssertThat(afterResources.Applied.Count).IsEqual(0);
    }

    /// <summary>
    /// 预检必须发生在资源写入<b>之前</b> —— 否则会出现「村庄拿到了、英雄没拿到」。
    /// </summary>
    [TestCase]
    public void Apply_ChecksItemsBeforeWritingResources()
    {
        var resources = new RecordingWriter();
        var items = new RecordingItemWriter { Approve = false };
        var settlement = new Settlement(LootyRewards, resources.Write, itemWriter: items);

        try { settlement.Apply(new SettlementKey("run-1", "br-1"), true); }
        catch (InvalidOperationException) { /* 预期 */ }

        Assertions.AssertThat(items.CanCalls).IsEqual(1);
        Assertions.AssertThat(resources.Applied.Count).IsEqual(0);
    }

    // ── 增量本身的防线 ─────────────────────────────────────────────────────

    /// <summary>数量非正的物品发放直接拒（它一旦进了增量就会写进背包）。</summary>
    [TestCase]
    public void AddItemGrant_NonPositiveQuantity_Throws()
    {
        var delta = new SettlementDelta();

        bool threw = false;
        try { delta.AddItemGrant(Alice, Stone, 0); }
        catch (ArgumentOutOfRangeException) { threw = true; }

        Assertions.AssertThat(threw).IsTrue();
        Assertions.AssertThat(delta.ItemGrants.Count).IsEqual(0);
    }

    /// <summary>空标识直接拒。</summary>
    [TestCase]
    public void AddItemGrant_EmptyIdentifiers_Throw()
    {
        var delta = new SettlementDelta();

        bool emptyHero = false;
        try { delta.AddItemGrant(HeroId.None, Stone, 1); }
        catch (ArgumentException) { emptyHero = true; }

        bool emptyItem = false;
        try { delta.AddItemGrant(Alice, ItemId.None, 1); }
        catch (ArgumentException) { emptyItem = true; }

        Assertions.AssertThat(emptyHero).IsTrue();
        Assertions.AssertThat(emptyItem).IsTrue();
        Assertions.AssertThat(delta.ItemGrants.Count).IsEqual(0);
    }

    /// <summary>带物品的增量不是零增量。</summary>
    [TestCase]
    public void Delta_WithOnlyItemGrants_IsNotEmpty()
    {
        var delta = new SettlementDelta();
        Assertions.AssertThat(delta.IsEmpty).IsTrue();

        delta.AddItemGrant(Alice, Stone, 1);
        Assertions.AssertThat(delta.IsEmpty).IsFalse();
    }

    // ── 随调用带进来的奖励 ─────────────────────────────────────────────────

    /// <summary>
    /// <b>本次结算专用的奖励内容</b>：只对这一次结算生效。
    /// 结算器构造时注入的那个默认奖励<b>不</b>含物品 ——
    /// 与副本无关的结算没有战利品可发。
    /// </summary>
    [TestCase]
    public void Apply_WithRewardsOverride_UsesTheOverrideForThisCallOnly()
    {
        var resources = new RecordingWriter();
        var items = new RecordingItemWriter();
        var settlement = new Settlement(LootyRewards, resources.Write, itemWriter: items);

        // 走默认奖励（构造时注入的那个）：它带物品。
        settlement.Apply(new SettlementKey("run-1", "br-1"), heroesWon: true);
        Assertions.AssertThat(items.Applied.Count).IsEqual(2);

        // 换一个键，走「本次专属」的奖励：只发村庄资源，不发物品。
        var overrideResources = new RecordingWriter();
        var overrideItems = new RecordingItemWriter();
        var overrideSettlement = new Settlement(
            LootyRewards, overrideResources.Write, itemWriter: overrideItems);

        overrideSettlement.Apply(
            new SettlementKey("run-2", "br-2"), heroesWon: true,
            rewardsOverride: won => new SettlementDelta().AddResource("材料", 38));

        Assertions.AssertThat(overrideItems.Applied.Count).IsEqual(0);
        Assertions.AssertThat(overrideResources.Applied.Count).IsEqual(1);
    }
}
