using System;
using System.Collections.Generic;
using System.Linq;
using GdUnit4;
using ClaudeCodeGameStudios.Domain.Settlement;

/// <summary>
/// 结算的单元测试。
///
/// <para><b>本文档保护两条最贵的：</b></para>
/// <ul>
/// <li><b>I4</b>：同一结算键只生效一次（含<b>读档往返</b>后重放）</li>
/// <li><b>I15</b>：要么完整应用，要么不应用 —— <b>绝不部分入账</b></li>
/// </ul>
///
/// <para><b>不要重犯：</b>原型里 <c>shield_wear</c> 曾用单个 <c>bool raidResolved</c>
/// 标记结算，而一次兽潮有 <b>3 次破口</b> —— 崩在第 2 次之后就会漏发或翻倍。
/// 因此本模块的幂等键是<b>复合键</b>（副本 × 战斗结果），不是布尔。</para>
///
/// <para><b>本文档不规定任何奖励数值。</b>奖励内容属【待定】<c>07</c> 的
/// <b>P-08 / T-004</b>；测试里的资源种类与数量只是夹具。</para>
/// </summary>
[TestSuite]
public class SettlementTests
{
    /// <summary>记录实际写入的批次，用来验证「全有或全无」。</summary>
    private sealed class RecordingWriter
    {
        public List<ResourceDelta> Applied { get; } = new();
        public bool Reject { get; set; }

        public bool Write(ResourceDelta[] deltas)
        {
            if (Reject) return false;                 // 模拟余额不足 / 写入失败
            Applied.AddRange(deltas);
            return true;
        }
    }

    private static SettlementDelta NonEmpty(bool heroesWon)
    {
        var d = new SettlementDelta();
        d.AddResource("木材", heroesWon ? 10 : 0);
        return d;
    }

    // ── I4 · 幂等 ──────────────────────────────────────────────────────────

    /// <summary>同一结算键重复应用只生效一次。</summary>
    [TestCase]
    public void Apply_SameKeyTwice_AppliesOnlyOnce()
    {
        var writer = new RecordingWriter();
        var settlement = new Settlement(NonEmpty, writer.Write);
        var key = new SettlementKey("r1", "br1");

        bool first = settlement.Apply(key, heroesWon: true);
        bool second = settlement.Apply(key, heroesWon: true);

        Assertions.AssertThat(first).IsTrue();
        Assertions.AssertThat(second).IsFalse();
        Assertions.AssertThat(writer.Applied.Count).IsEqual(1);
    }

    /// <summary>
    /// <b>读档往返</b>：恢复幂等标志后重放同一批结算，全部被拒且不重复写入。
    /// 这正是基线 §13.7「已完成事件不会重复应用」要防的场景。
    /// </summary>
    [TestCase]
    public void Apply_AfterSaveReload_AppliesNothing()
    {
        var key1 = new SettlementKey("r1", "br1");
        var key2 = new SettlementKey("r2", "br1");

        // 存档前：结算了两次
        var beforeWriter = new RecordingWriter();
        var before = new Settlement(NonEmpty, beforeWriter.Write);
        before.Apply(key1, true);
        before.Apply(key2, true);
        var savedKeys = before.AppliedKeys.ToArray();

        // 读档后：恢复标志，重放同一批
        var afterWriter = new RecordingWriter();
        var after = new Settlement(NonEmpty, afterWriter.Write);
        after.RestoreApplied(savedKeys);
        after.Apply(key1, true);
        after.Apply(key2, true);

        Assertions.AssertThat(afterWriter.Applied.Count).IsEqual(0);
    }

    /// <summary>
    /// <b>复合键的意义</b>：同一个副本的不同战斗结果是<b>不同的</b>结算。
    /// <para>若用单个 bool 标记，一个副本就只能结算一次，第二次会被误判为重复。</para>
    /// </summary>
    [TestCase]
    public void Apply_DifferentBattleResultsInTheSameRun_AreBothApplied()
    {
        var writer = new RecordingWriter();
        var settlement = new Settlement(NonEmpty, writer.Write);

        bool a = settlement.Apply(new SettlementKey("r1", "br1"), true);
        bool b = settlement.Apply(new SettlementKey("r1", "br2"), true);

        Assertions.AssertThat(a).IsTrue();
        Assertions.AssertThat(b).IsTrue();
        Assertions.AssertThat(writer.Applied.Count).IsEqual(2);
    }

    // ── I15 · 全有或全无 ───────────────────────────────────────────────────

    /// <summary>写入被拒时<b>抛异常且幂等标志不被记录</b> —— 否则会出现「记了但没写」。</summary>
    [TestCase]
    public void Apply_WhenWriterRejects_Throws_AndKeyIsNotMarkedApplied()
    {
        var writer = new RecordingWriter { Reject = true };
        var settlement = new Settlement(NonEmpty, writer.Write);
        var key = new SettlementKey("r1", "br1");

        bool threw = false;
        try { settlement.Apply(key, true); }
        catch (InvalidOperationException) { threw = true; }

        Assertions.AssertThat(threw).IsTrue();
        Assertions.AssertThat(writer.Applied.Count).IsEqual(0);
        Assertions.AssertThat(settlement.IsApplied(key)).IsFalse();

        // 修好写入条件后可以重试 —— 因为上一次根本没生效
        writer.Reject = false;
        bool retried = settlement.Apply(key, true);
        Assertions.AssertThat(retried).IsTrue();
    }

    /// <summary>奖励计算本身抛异常时，同样什么都没写、也没记幂等。</summary>
    [TestCase]
    public void Apply_WhenRewardComputationThrows_NothingIsWritten()
    {
        var writer = new RecordingWriter();
        var settlement = new Settlement(_ => throw new InvalidOperationException("规则未定"), writer.Write);

        bool threw = false;
        try { settlement.Apply(new SettlementKey("r1", "br1"), true); }
        catch (InvalidOperationException) { threw = true; }

        Assertions.AssertThat(threw).IsTrue();
        Assertions.AssertThat(writer.Applied.Count).IsEqual(0);
    }

    // ── D-010 Q1 · 失败不给奖励，但不额外惩罚 ──────────────────────────────

    /// <summary>
    /// 失败的那次结算是<b>零增量</b>，但它<b>仍然被结算一次</b>。
    /// <para>零增量 ≠ 不结算：不结算的话副本会永远停在「待结算」状态。</para>
    /// </summary>
    [TestCase]
    public void Apply_OnFailure_IsAZeroDelta_ButStillCountsAsApplied()
    {
        var writer = new RecordingWriter();
        var settlement = new Settlement(NonEmpty, writer.Write);
        var key = new SettlementKey("r1", "br1");

        bool applied = settlement.Apply(key, heroesWon: false);

        // 写入的是 0，被 writer 过滤掉 → 实际写入为空
        Assertions.AssertThat(applied).IsTrue();
        Assertions.AssertThat(settlement.IsApplied(key)).IsTrue();
        // 再调一次会被幂等拦下 —— 证明它确实被结算过了
        bool again = settlement.Apply(key, heroesWon: false);
        Assertions.AssertThat(again).IsFalse();
    }

    /// <summary>非零的负增量（扣减）走同一条提交路径。</summary>
    [TestCase]
    public void Apply_NegativeDelta_IsWrittenThroughTheSameAtomicPath()
    {
        var writer = new RecordingWriter();
        var settlement = new Settlement(
            _ =>
            {
                var d = new SettlementDelta();
                d.AddResource("木材", 20);
                d.AddResource("布料", -5);
                return d;
            },
            writer.Write);

        settlement.Apply(new SettlementKey("r1", "br1"), true);

        Assertions.AssertThat(writer.Applied.Count).IsEqual(2);
        Assertions.AssertThat(writer.Applied.Any(d => d.Amount == -5)).IsTrue();
    }

    // ── 幂等标志的落盘点（M7 存档的前提）─────────────────────────────────

    /// <summary>
    /// 已应用的键必须<b>可以被完整取回</b>，否则 M7 无法把它写进存档。
    /// </summary>
    [TestCase]
    public void AppliedKeys_AreRetrievable_ForSavefileRoundTrip()
    {
        var settlement = new Settlement(NonEmpty, new RecordingWriter().Write);
        settlement.Apply(new SettlementKey("rA", "br1"), true);
        settlement.Apply(new SettlementKey("rB", "br2"), true);

        var keys = settlement.AppliedKeys.ToList();

        Assertions.AssertThat(keys.Count).IsEqual(2);
        Assertions.AssertThat(keys.Contains("rA#br1")).IsTrue();
        Assertions.AssertThat(keys.Contains("rB#br2")).IsTrue();
    }
}
