using System;
using System.Collections.Generic;
using System.Linq;
using DomainTrade = ClaudeCodeGameStudios.Domain.Trade.Trade;
using DomainTradeId = ClaudeCodeGameStudios.Domain.Trade.TradeId;

namespace ClaudeCodeGameStudios.Domain.Settlement;

/// <summary>结算键 —— 一次「副本 × 战斗结果」的组合。</summary>
/// <remarks>
/// <b>为什么不能只用 bool</b>：一个副本可能产生多次应用（例如按阶段分次结算）。
/// 单个布尔标志分不清「哪一次已经应用过」，重复读档就会漏发或重发。
/// 用复合键，重复应用会被精确定位并拒绝（不变量 I4）。
/// </remarks>
/// <param name="RunId">副本实例标识。</param>
/// <param name="BattleResultId">战斗结果标识。</param>
public readonly record struct SettlementKey(string RunId, string BattleResultId)
{
    /// <inheritdoc />
    public override string ToString() => $"{RunId}#{BattleResultId}";
}

/// <summary>一次结算产生的资源增量。</summary>
/// <param name="ResourceId">资源种类。</param>
/// <param name="Amount">数量；为负表示扣减。</param>
public readonly record struct ResourceDelta(string ResourceId, long Amount);

/// <summary>一次结算对村庄的全部影响。计算得出，尚未应用。</summary>
public sealed class SettlementDelta
{
    private readonly List<ResourceDelta> _resources = new();

    /// <summary>村庄侧的资源增量。</summary>
    public IReadOnlyList<ResourceDelta> Resources => _resources;

    /// <summary>本次结算是否改变任何东西。</summary>
    /// <remarks>
    /// <b>零增量是合法的</b>：副本失败不给奖励、也不额外惩罚（<c>07</c> **D-010 Q1**），
    /// 那次结算就是零增量 —— 但它<b>仍然必须被结算一次</b>，
    /// 否则副本会永远停在「待结算」状态。
    /// </remarks>
    public bool IsEmpty => _resources.Count == 0 || _resources.All(r => r.Amount == 0);

    /// <summary>追加一条资源增量。</summary>
    /// <param name="resourceId">资源种类。</param>
    /// <param name="amount">数量。</param>
    /// <exception cref="ArgumentException">资源种类为空。</exception>
    public SettlementDelta AddResource(string resourceId, long amount)
    {
        if (string.IsNullOrEmpty(resourceId))
        {
            throw new ArgumentException("资源种类不能为空。", nameof(resourceId));
        }

        _resources.Add(new ResourceDelta(resourceId, amount));
        return this;
    }
}

/// <summary>
/// 结算 —— 把一次战斗结果变成村庄的实际变化，<b>且只做一次</b>。
/// </summary>
/// <remarks>
/// <para>
/// <b>唯一写者：<c>Settlement</c> 模块</c>（<c>05</c> §2.3）。
/// 它<b>不定义奖励内容</b>——那属【待定】<c>07</c> 的 <b>P-08 / T-004</b>，
/// 由注入的 <see cref="ISettlementRewards"/> 提供。
/// </para>
/// <para>
/// <b>不变量 I15：要么完整应用，要么不应用。</b>
/// 流程固定为<b>先算全量 → 校验 → 一次性提交</b>，任何一步失败都不允许留下部分入账。
/// </para>
/// <para>
/// <b>不变量 I10：只经由拥有数据的模块写入。</b>本类不直接持有村庄或英雄，
/// 它只调用注入的写入委托；调用方必须把委托接到各自模块的写入口上。
/// </para>
/// </remarks>
public sealed class Settlement
{
    private readonly HashSet<string> _applied = new(StringComparer.Ordinal);

    /// <param name="rewards">奖励内容提供者。【待定】P-08 / T-004。</param>
    /// <param name="resourceWriter">
    /// 资源写入委托，必须<b>接到村庄模块的写入口</b>（不变量 I10）。
    /// 返回 <c>false</c> 表示余额不足、写入被拒。
    /// </param>
    public Settlement(
        Func<bool, SettlementDelta> rewards,
        Func<ResourceDelta[], bool> resourceWriter,
        ITradeSettler? tradeSettler = null)
    {
        _rewards = rewards ?? throw new ArgumentNullException(nameof(rewards));
        _resourceWriter = resourceWriter ?? throw new ArgumentNullException(nameof(resourceWriter));
        _tradeSettler = tradeSettler;   // 可选：M6 引入；null = 旧行为
    }

    private readonly Func<bool, SettlementDelta> _rewards;
    private readonly Func<ResourceDelta[], bool> _resourceWriter;
    private readonly ITradeSettler? _tradeSettler;

    /// <summary>已应用的结算键。</summary>
    public IReadOnlyCollection<string> AppliedKeys => _applied;

    /// <summary>该 <see cref="SettlementKey"/> 是否已结算过。</summary>
    /// <param name="key">结算键。</param>
    public bool IsApplied(SettlementKey key) => _applied.Contains(key.ToString());

    /// <summary>该 <see cref="DomainTradeId"/> 是否已应用过（全局幂等）。</summary>
    /// <remarks>
    /// <b>M6 / D-027</b>：摊位费 / 英雄间交易走 Settlement 时，
    /// 用 <c>TradeId.ToString()</c> 当幂等键。与 <see cref="SettlementKey"/>
    /// 共用 <c>_applied</c> 集合，键空间以 <c>ToString()</c> 区分（不冲突）。
    /// </remarks>
    public bool IsAppliedTrade(DomainTradeId tradeId) => _applied.Contains(tradeId.ToString());

    /// <summary>
    /// 应用一次 <see cref="DomainTradeId"/> 结算（M6 / D-027）。
    /// </summary>
    /// <remarks>
    /// <b>不</b>做"预检 + 一次性提交"那一套 —— <see cref="DomainTrade.Trade.Apply"/>
    /// 已经是 I15 纯函数，<b>调用方</b>算好 4 个新引用后，自己写回。
    /// 本方法只负责：<b>(1)</b> 查 <c>_applied</c> 幂等；<b>(2)</b> 把幂等键写进 <c>_applied</c>。
    /// </remarks>
    /// <param name="tradeId">交易标识（复合幂等键）。</param>
    /// <returns>
    /// <c>true</c> = 本次已应用；<c>false</c> = 同 TradeId 之前已应用过，本次被忽略。
    /// </returns>
    /// <exception cref="InvalidOperationException">未注入 <see cref="ITradeSettler"/>。</exception>
    public bool ApplyTrade(DomainTradeId tradeId)
    {
        if (_tradeSettler is null)
        {
            throw new InvalidOperationException(
                "Settlement.ApplyTrade 需注入 ITradeSettler；当前 Settlement 是 M6 之前构造的，"
                + "请用接受 (Func<bool, SettlementDelta>, Func<ResourceDelta[], bool>, ITradeSettler) 的新构造。");
        }
        var id = tradeId.ToString();
        if (_applied.Contains(id)) return false;
        _tradeSettler.Settle(tradeId);
        _applied.Add(id);
        return true;
    }

    /// <summary>
    /// 执行一次结算。<b>同一键只生效一次</b>（不变量 I4）。
    /// </summary>
    /// <param name="key">结算键。</param>
    /// <param name="heroesWon">战斗是否胜利。</param>
    /// <returns>
    /// <c>true</c> 表示本次已应用；<c>false</c> 表示该键<b>之前已结算过</b>，本次被忽略。
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// 写入被拒（余额不足等）—— 结算<b>整体未应用</b>，状态保持不变（I15）。
    /// </exception>
    public bool Apply(SettlementKey key, bool heroesWon)
    {
        var id = key.ToString();
        if (_applied.Contains(id)) return false;

        // 1. 先算全量。失败在这里抛出 —— 此时什么都没写。
        var delta = _rewards(heroesWon) ?? new SettlementDelta();
        var writes = delta.Resources.Where(r => r.Amount != 0).ToArray();

        // 2. 一次性提交。写入函数自身必须保证全成功或全不成功（不变量 I15）。
        if (writes.Length > 0 && !_resourceWriter(writes))
        {
            throw new InvalidOperationException(
                $"结算 {id} 的写入被拒，本次结算**整体未应用** —— 状态保持不变。"
                + "不变量 I15：要么完整应用，要么不应用，绝不部分入账。");
        }

        // 3. 只有提交成功才记幂等标志。顺序反了会导致「记了但没写」。
        _applied.Add(id);
        return true;
    }

    /// <summary>
    /// 把已应用的幂等标志恢复到某个集合（读档）。
    /// </summary>
    /// <param name="keys">存档中记录的已结算键。</param>
    /// <remarks>
    /// 恢复后读档重放同一批结算会被拒绝 —— 这正是
    /// <c>02</c> F-07 与基线 §13.7「已完成事件不会重复」要防的情况。
    /// </remarks>
    public void RestoreApplied(IEnumerable<string> keys)
    {
        foreach (var k in keys ?? Enumerable.Empty<string>()) _applied.Add(k);
    }
}

/// <summary>
/// 交易结算写入委托（M6 / D-027）。
/// </summary>
/// <remarks>
/// <para><b>职责</b>：<see cref="Settlement.ApplyTrade(DomainTradeId)"/> 幂等通过后，
/// 调本接口的 <see cref="Settle"/> 真正应用一次交易。
/// <see cref="DomainTrade.Trade.Apply"/> 是纯函数 —— 它已算好 4 个新引用。
/// <b>本接口的实现</b>负责把新引用写回 <c>HeroAgent</c> 等持有者，<b>不</b>重新算。</para>
///
/// <para><b>为什么独立接口</b>：M3 摆摊/买摊走 <c>StallService.BuyFromStall</c>（自带 I15），
/// <b>不</b>走 Settlement。Settlement 的 <see cref="Settle"/> 是给"英雄 ↔ 英雄"路径
/// 预留的全局幂等入口 —— 调用方在 Settlement 之后写 4 个新引用，再调 <c>ApplyTrade</c>。</para>
/// </remarks>
public interface ITradeSettler
{
    /// <summary>应用一次交易（写回 4 个新引用）。<b>幂等性</b>由 <see cref="Settlement"/> 守门，本方法可假定"不重复"。</summary>
    void Settle(DomainTradeId tradeId);
}
