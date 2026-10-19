using System;
using System.Collections.Generic;
using GdUnit4;
using ClaudeCodeGameStudios.Domain.Heroes;
using ClaudeCodeGameStudios.Domain.Items;
using DomainSettlement = ClaudeCodeGameStudios.Domain.Settlement.Settlement;
using DomainSettlementKey = ClaudeCodeGameStudios.Domain.Settlement.SettlementKey;
using DomainSettlementDelta = ClaudeCodeGameStudios.Domain.Settlement.SettlementDelta;
using DomainITradeSettler = ClaudeCodeGameStudios.Domain.Settlement.ITradeSettler;
using DomainTradeId = ClaudeCodeGameStudios.Domain.Trade.TradeId;
using DomainTrade = ClaudeCodeGameStudios.Domain.Trade.Trade;
using DomainTradeStatus = ClaudeCodeGameStudios.Domain.Trade.TradeStatus;

namespace ClaudeCodeGameStudios.Tests.Settlement;

/// <summary>
/// <see cref="DomainSettlement.ApplyTrade"/> 单元测试（M6 / D-027）。
///
/// <para><b>本文档保护：</b></para>
/// <list type="bullet">
/// <item>同 <see cref="DomainTradeId"/> 第二次 ApplyTrade 返 <c>false</c>（I4 全局幂等）。</item>
/// <item>同 <c>_applied</c> 集合里 <see cref="DomainSettlementKey"/> 与 <see cref="DomainTradeId"/>
///       用 <c>ToString()</c> 区分，<b>不</b>冲突（防"用 trade key 重复战斗奖励"或反之）。</item>
/// <item><see cref="DomainSettlement.ApplyTrade"/> 调 <see cref="DomainITradeSettler.Settle"/> 一次成功调用。</item>
/// <item>没注入 <see cref="DomainITradeSettler"/> 时 <see cref="DomainSettlement.ApplyTrade"/> 抛异常。</item>
/// </list>
///
/// <para><b>注意</b>：测试类在 <c>namespace ClaudeCodeGameStudios.Tests.Settlement</c>，与
/// <c>DomainSettlement</c>（<c>ClaudeCodeGameStudios.Domain.Settlement.Settlement</c>）同名，
/// 用 alias 解决（与上轮 <c>Trade</c> / <c>Stall</c> 同款 fix）。</para>
/// </summary>
[TestSuite]
public class SettlementApplyTradeTests
{
    private static readonly HeroId Alice = new("h-alice");
    private static readonly HeroId Bob = new("h-bob");
    private static readonly ItemId Stone = new("石料");

    /// <summary>记录被 Settle 调用过的 TradeId。</summary>
    private sealed class RecordingSettler : DomainITradeSettler
    {
        public List<DomainTradeId> Called { get; } = new();
        public void Settle(DomainTradeId tradeId) => Called.Add(tradeId);
    }

    private static DomainTradeId MakeTradeId(
        string runId = "r1", double simTime = 100.0) =>
        new DomainTradeId(runId, Alice, Bob, Stone, Quantity: 1, simTime);

    // ── 全局幂等 ───────────────────────────────────────────────────────

    [TestCase]
    public void ApplyTrade_FirstCall_ReturnsTrue_AndRecordsKey()
    {
        var settler = new RecordingSettler();
        var settlement = new DomainSettlement(
            rewards: _ => new DomainSettlementDelta(),
            resourceWriter: _ => true,
            tradeSettler: settler);

        var id = MakeTradeId();
        var r = settlement.ApplyTrade(id);
        Assertions.AssertThat(r).IsTrue();
        Assertions.AssertThat(settler.Called.Count).IsEqual(1);
        Assertions.AssertThat(settler.Called[0].ToString()).IsEqual(id.ToString());
        Assertions.AssertThat(settlement.IsAppliedTrade(id)).IsTrue();
    }

    [TestCase]
    public void ApplyTrade_SameTradeIdTwice_FirstTrueThenFalse()
    {
        var settler = new RecordingSettler();
        var settlement = new DomainSettlement(
            rewards: _ => new DomainSettlementDelta(),
            resourceWriter: _ => true,
            tradeSettler: settler);
        var id = MakeTradeId();

        var r1 = settlement.ApplyTrade(id);
        var r2 = settlement.ApplyTrade(id);
        Assertions.AssertThat(r1).IsTrue();
        Assertions.AssertThat(r2).IsFalse();   // I4 拒重
        Assertions.AssertThat(settler.Called.Count).IsEqual(1);   // Settle 只调一次
    }

    [TestCase]
    public void ApplyTrade_DifferentTradeIds_BothApply()
    {
        var settler = new RecordingSettler();
        var settlement = new DomainSettlement(
            rewards: _ => new DomainSettlementDelta(),
            resourceWriter: _ => true,
            tradeSettler: settler);
        var id1 = MakeTradeId(runId: "r1", simTime: 100.0);
        var id2 = MakeTradeId(runId: "r1", simTime: 200.0);

        var r1 = settlement.ApplyTrade(id1);
        var r2 = settlement.ApplyTrade(id2);
        Assertions.AssertThat(r1).IsTrue();
        Assertions.AssertThat(r2).IsTrue();
        Assertions.AssertThat(settler.Called.Count).IsEqual(2);
    }

    // ── 与 SettlementKey 键空间隔离 ────────────────────────────────────

    [TestCase]
    public void ApplyTrade_DoesNotInterfereWithSettlementKey()
    {
        var settler = new RecordingSettler();
        var settlement = new DomainSettlement(
            rewards: _ => new DomainSettlementDelta(),
            resourceWriter: _ => true,
            tradeSettler: settler);

        // 先走战斗路径
        var sKey = new DomainSettlementKey("rA", "br1");
        settlement.Apply(sKey, heroesWon: true);
        // 再走交易路径
        var tId = MakeTradeId();
        var r = settlement.ApplyTrade(tId);

        Assertions.AssertThat(r).IsTrue();
        Assertions.AssertThat(settlement.IsApplied(sKey)).IsTrue();
        Assertions.AssertThat(settlement.IsAppliedTrade(tId)).IsTrue();
        Assertions.AssertThat(settlement.AppliedKeys.Count).IsEqual(2);
    }

    // ── 没注入 ITradeSettler → 抛 ─────────────────────────────────────

    [TestCase]
    public void ApplyTrade_NoSettlerInjected_Throws()
    {
        var settlement = new DomainSettlement(
            rewards: _ => new DomainSettlementDelta(),
            resourceWriter: _ => true);   // 不传 ITradeSettler

        var id = MakeTradeId();
        bool threw = false;
        try { settlement.ApplyTrade(id); }
        catch (InvalidOperationException) { threw = true; }
        Assertions.AssertThat(threw).IsTrue();
    }

    // ── 旧路径不破坏 ───────────────────────────────────────────────────

    [TestCase]
    public void Apply_BattleKey_StillWorks_NoSettler()
    {
        // M6 之前构造的 Settlement 没传 ITradeSettler —— 战斗路径照常
        var settlement = new DomainSettlement(
            rewards: _ => new DomainSettlementDelta().AddResource("金币", 10),
            resourceWriter: _ => true);
        var sKey = new DomainSettlementKey("r1", "br1");
        var r = settlement.Apply(sKey, heroesWon: true);
        Assertions.AssertThat(r).IsTrue();
        Assertions.AssertThat(settlement.IsApplied(sKey)).IsTrue();
    }
}
