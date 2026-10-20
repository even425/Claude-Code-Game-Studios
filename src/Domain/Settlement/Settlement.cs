using System;
using System.Collections.Generic;
using System.Linq;
using ClaudeCodeGameStudios.Domain.Heroes;
using ClaudeCodeGameStudios.Domain.Items;
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

/// <summary>
/// 一次结算对<b>某个英雄个人背包</b>的物品发放（R-01 D04：无公共奖励池，物品直接归个人）。
/// </summary>
/// <param name="Hero">接收者。</param>
/// <param name="Item">物品。</param>
/// <param name="Quantity">数量，必须为正。</param>
public readonly record struct ItemGrant(HeroId Hero, ItemId Item, long Quantity);

/// <summary>
/// 一次结算对<b>某个英雄个人钱包</b>的资金发放（D-054-01：副本金币报酬）。
/// </summary>
/// <param name="Hero">接收者。</param>
/// <remarks>
/// <b>这里不带金额</b>：报酬率是**规则**（D-054-01 定的「每人每次 X 金」），
/// 住在写入入口（<c>IWalletGrantWriter</c> 的实现）里；
/// 增量只说「<b>谁</b>有资格领一次报酬」。
/// <br>金额之所以不能放这里：X 可以是<b>分数</b>（金币的最小单位是 1/100 金），
/// 而增量里的数字若要参与「零头累计」，语义就会在两处重复定义 —— 那是最容易分叉的地方。
/// </remarks>
public readonly record struct WalletGrant(HeroId Hero);

/// <summary>一次结算对村庄的全部影响。计算得出，尚未应用。</summary>
public sealed class SettlementDelta
{
    private readonly List<ResourceDelta> _resources = new();
    private readonly List<ItemGrant> _itemGrants = new();
    private readonly List<WalletGrant> _walletGrants = new();

    /// <summary>村庄侧的资源增量。</summary>
    public IReadOnlyList<ResourceDelta> Resources => _resources;

    /// <summary>
    /// 个人物品发放（R-01）。
    /// </summary>
    /// <remarks>
    /// <b>为什么物品也进结算增量，而不是另开一条发放路径</b>：
    /// 副本成功时<b>村庄资源</b>与<b>英雄个人物品</b>是同一笔奖励的两半。
    /// 分两次发，中间任何一步失败都会留下「村庄拿到了、英雄没拿到」或反之的
    /// 半笔账 —— 那正是<b>不变量 I15</b>（要么完整应用，要么不应用）要禁止的状态。
    /// 放进同一个增量，<see cref="Settlement.Apply"/> 才能用<b>同一把幂等锁</b>、
    /// 在<b>同一次提交</b>里把两半一起落下去。
    /// </remarks>
    public IReadOnlyList<ItemGrant> ItemGrants => _itemGrants;

    /// <summary>
    /// 个人钱包发放（D-054-01）。
    /// </summary>
    /// <remarks>
    /// <b>为什么金币也进结算增量，而不是另发一次</b>：
    /// 副本的<b>村庄材料</b>、<b>英雄战利品</b>、<b>英雄金币</b>是同一笔奖励的三半
    /// （D-054 明确「失败也发金币」→ 三者<b>不总是同时发生</b>，但属于
    /// <b>同一次结算</b>）。分三次发，中间任何一步失败都会留下半笔账 ——
    /// 与 <see cref="ItemGrants"/> 同一把理由，<b>同一把幂等锁</b>、<b>同一次提交</b>。
    /// </remarks>
    public IReadOnlyList<WalletGrant> WalletGrants => _walletGrants;

    /// <summary>本次结算是否改变任何东西。</summary>
    /// <remarks>
    /// <b>零增量是合法的</b>：副本失败不给奖励、也不额外惩罚（<c>07</c> **D-010 Q1**），
    /// 那次结算就是零增量 —— 但它<b>仍然必须被结算一次</b>，
    /// 否则副本会永远停在「待结算」状态。
    /// <para>
    /// <b>物品发放也算「改变了东西」</b>：一次什么都没发的村庄资源结算，
    /// 只要带了个人物品，就不是零增量。
    /// </para>
    /// <para>
    /// <b>钱包发放同样算「改变了东西」</b>（D-054-01：失败也发金币）——
    /// 只有「什么都没有」的失败结算**才是**零增量。
    /// </para>
    /// </remarks>
    public bool IsEmpty =>
        (_resources.Count == 0 || _resources.All(r => r.Amount == 0))
        && _itemGrants.Count == 0
        && _walletGrants.Count == 0;

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

    /// <summary>追加一条个人物品发放（R-01）。</summary>
    /// <param name="hero">接收者。</param>
    /// <param name="item">物品。</param>
    /// <param name="quantity">数量，必须为正。</param>
    /// <exception cref="ArgumentException">英雄标识或物品标识为空。</exception>
    /// <exception cref="ArgumentOutOfRangeException">数量不是正数。</exception>
    public SettlementDelta AddItemGrant(HeroId hero, ItemId item, long quantity)
    {
        if (string.IsNullOrEmpty(hero.Value))
        {
            throw new ArgumentException("接收者标识不能为空。", nameof(hero));
        }

        if (string.IsNullOrEmpty(item.Value))
        {
            throw new ArgumentException("物品标识不能为空。", nameof(item));
        }

        if (quantity <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(quantity), quantity, "发放数量必须为正数。");
        }

        _itemGrants.Add(new ItemGrant(hero, item, quantity));
        return this;
    }

    /// <summary>追加一条个人钱包发放（D-054-01）。</summary>
    /// <param name="hero">接收者。</param>
    /// <exception cref="ArgumentException">英雄标识为空。</exception>
    public SettlementDelta AddWalletGrant(HeroId hero)
    {
        if (string.IsNullOrEmpty(hero.Value))
        {
            throw new ArgumentException("接收者标识不能为空。", nameof(hero));
        }

        _walletGrants.Add(new WalletGrant(hero));
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
    /// <param name="tradeSettler">交易结算写入委托（可选）。</param>
    /// <param name="itemWriter">
    /// 个人物品发放的写入入口（可选，R-01）。
    /// <b>没有它就发不了物品</b>：结算增量里若含物品发放而本项为 null，
    /// <see cref="Apply"/> 会<b>整笔拒绝</b>并抛异常 ——
    /// 绝不把物品悄悄丢掉，那会让「奖励发出去了」成为一句谎话。
    /// </param>
    /// <param name="walletWriter">
    /// 个人钱包发放的写入入口（可选，D-054-01）。
    /// <b>没有它就发不了金币</b>：增量里若含钱包发放而本项为 null，
    /// <see cref="Apply"/> 会<b>整笔拒绝</b>并抛异常 —— 同上，绝不静默丢弃。
    /// </param>
    public Settlement(
        Func<bool, SettlementDelta> rewards,
        Func<ResourceDelta[], bool> resourceWriter,
        ITradeSettler? tradeSettler = null,
        IItemGrantWriter? itemWriter = null,
        IWalletGrantWriter? walletWriter = null)
    {
        _rewards = rewards ?? throw new ArgumentNullException(nameof(rewards));
        _resourceWriter = resourceWriter ?? throw new ArgumentNullException(nameof(resourceWriter));
        _tradeSettler = tradeSettler;   // 可选：M6 引入；null = 旧行为
        _itemWriter = itemWriter;       // 可选：R-01 引入；null = 不发物品
        _walletWriter = walletWriter;   // 可选：D-054-01 引入；null = 不发金币
    }

    private readonly Func<bool, SettlementDelta> _rewards;
    private readonly Func<ResourceDelta[], bool> _resourceWriter;
    private readonly ITradeSettler? _tradeSettler;
    private readonly IItemGrantWriter? _itemWriter;
    private readonly IWalletGrantWriter? _walletWriter;

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
    /// <param name="rewardsOverride">
    /// 本次结算专用的奖励内容提供者。<b>null</b> 时用构造时注入的那个。
    /// <para>
    /// <b>为什么需要它</b>：副本奖励的「村庄资源」与「个人物品」必须<b>属于同一次结算</b>。
    /// 若把战利品放进某个会话级的共享状态里由奖励委托去读，
    /// 任何一次<b>与副本无关</b>的结算都可能把它当成自己的奖励再发一遍。
    /// 让调用方把本次的战利品<b>随调用带进来</b>，这条路径就不存在了。
    /// </para>
    /// </param>
    /// <returns>
    /// <c>true</c> 表示本次已应用；<c>false</c> 表示该键<b>之前已结算过</b>，本次被忽略。
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// 写入被拒（余额不足、背包装不下等）—— 结算<b>整体未应用</b>，状态保持不变（I15）。
    /// </exception>
    /// <remarks>
    /// <para><b>步骤，顺序不能换</b>：</para>
    /// <list type="number">
    /// <item><b>先算全量</b>（资源增量 + 个人物品发放 + 个人钱包发放）。失败在这里抛出 —— 此时什么都没写。</item>
    /// <item><b>再预检个人发放</b>：背包装不下 / 钱包装不下 → <b>整笔拒绝</b>。
    /// 这一步必须在资源写入<b>之前</b>，否则会出现「村庄拿到了、英雄没拿到」的半笔账。</item>
    /// <item><b>最后一次性提交</b>：先写资源，再写物品，再写钱包；全部成功才记幂等标志。</item>
    /// </list>
    /// <para>
    /// <b>为什么个人发放的预检与提交是分开的两个调用</b>：<see cref="IItemGrantWriter"/> 与
    /// <see cref="IWalletGrantWriter"/> 要能<b>先算清楚每个英雄的新背包 / 新钱包再动手</b>——
    /// 背包与钱包都是不可变的，所以「算出全部新值 → 一次性换上」天然就是原子的。
    /// 预检通过后提交不再有失败路径，因此提交阶段不会产生半笔账。
    /// </para>
    /// </remarks>
    public bool Apply(
        SettlementKey key,
        bool heroesWon,
        Func<bool, SettlementDelta>? rewardsOverride = null)
    {
        var id = key.ToString();
        if (_applied.Contains(id)) return false;

        // 1. 先算全量。失败在这里抛出 —— 此时什么都没写。
        var delta = (rewardsOverride ?? _rewards)(heroesWon) ?? new SettlementDelta();
        var writes = delta.Resources.Where(r => r.Amount != 0).ToArray();
        var grants = delta.ItemGrants.ToArray();
        var walletGrants = delta.WalletGrants.ToArray();

        // 2. 个人物品：没有写入能力就整笔拒绝（绝不静默丢弃）。
        if (grants.Length > 0 && _itemWriter is null)
        {
            throw new InvalidOperationException(
                $"结算 {id} 含 {grants.Length} 条个人物品发放，但未注入 IItemGrantWriter —— "
                + "本次结算**整体未应用**，状态保持不变。");
        }

        // 3. 预检物品：背包装不下 → 整笔不应用（I15）。幂等键不落，可重试。
        if (grants.Length > 0 && !_itemWriter!.CanGrant(grants))
        {
            throw new InvalidOperationException(
                $"结算 {id} 的个人物品发放不可行（背包容量不足、英雄不存在或物品未登记），"
                + "本次结算**整体未应用** —— 状态保持不变。幂等键未记录，可重试。");
        }

        // 3b. 个人金币（D-054-01）：与物品同款的两道门 ——
        //     「没有写入能力就整笔拒绝」+「预检不可行就整笔不应用」。
        if (walletGrants.Length > 0 && _walletWriter is null)
        {
            throw new InvalidOperationException(
                $"结算 {id} 含 {walletGrants.Length} 条个人钱包发放，但未注入 IWalletGrantWriter —— "
                + "本次结算**整体未应用**，状态保持不变。");
        }

        if (walletGrants.Length > 0 && !_walletWriter!.CanGrant(walletGrants))
        {
            throw new InvalidOperationException(
                $"结算 {id} 的个人钱包发放不可行（钱包容量不足、英雄不存在或金额溢出），"
                + "本次结算**整体未应用** —— 状态保持不变。幂等键未记录，可重试。");
        }

        // 4. 一次性提交。写入函数自身必须保证全成功或全不成功（不变量 I15）。
        if (writes.Length > 0 && !_resourceWriter(writes))
        {
            throw new InvalidOperationException(
                $"结算 {id} 的写入被拒，本次结算**整体未应用** —— 状态保持不变。"
                + "不变量 I15：要么完整应用，要么不应用，绝不部分入账。");
        }

        // 5. 物品提交。预检已通过，实现必须保证这一步不再失败。
        if (grants.Length > 0) _itemWriter!.Grant(grants);

        // 6. 金币提交。预检已通过，实现必须保证这一步不再失败。
        if (walletGrants.Length > 0) _walletWriter!.Grant(walletGrants);

        // 7. 只有提交成功才记幂等标志。顺序反了会导致「记了但没写」。
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

/// <summary>
/// 个人物品发放的写入入口（R-01）。
/// </summary>
/// <remarks>
/// <para><b>为什么是「预检 + 提交」两步，而不是一个 <c>bool Apply(...)</c></b>：
/// 结算的原子性要求是「<b>要么全都发出去，要么一个都不发</b>」。
/// 物品要发给<b>多个</b>英雄，其中任何一个人装不下，整笔就该不发。
/// 而「能不能装下」必须在动手之前就知道 —— 于是拆成两步：
/// <see cref="CanGrant"/> 只回答「行不行」，
/// <see cref="Grant"/> 在<b>已知可行</b>的前提下把东西一次性发完。</para>
///
/// <para><b>实现方必须保证</b>：<see cref="Grant"/> 在 <see cref="CanGrant"/> 返回 true 之后
/// <b>不会失败、不会抛</b>。背包（<c>Inventory</c>）是不可变的，
/// 因此「先算出每个英雄的新背包，再一次性换上」天然满足这一条。</para>
/// </remarks>
public interface IItemGrantWriter
{
    /// <summary>
    /// 预检：这批发放是否<b>全部</b>可行。<b>不改变任何状态。</b>
    /// </summary>
    /// <param name="grants">发放明细。</param>
    /// <returns>全部可行返回 true；任何一条不可行返回 false（此时整笔结算都不该应用）。</returns>
    bool CanGrant(IReadOnlyList<ItemGrant> grants);

    /// <summary>
    /// 提交：把东西发出去。<b>调用前必须已通过 <see cref="CanGrant"/>。</b>
    /// </summary>
    /// <param name="grants">发放明细。</param>
    void Grant(IReadOnlyList<ItemGrant> grants);
}

/// <summary>
/// 个人<b>钱包</b>发放的写入入口（D-054-01：副本金币报酬）。
/// </summary>
/// <remarks>
/// <para><b>形态与 <see cref="IItemGrantWriter"/> 完全同构</b>：预检 + 提交两步。
/// 理由一样 —— 钱要发给<b>多个</b>英雄，任何一个人装不下（钱包容量 / 溢出），
/// 整笔就该不发；而「能不能装下」必须在动手之前就知道。</para>
///
/// <para><b>实现方必须保证</b>：<see cref="Grant"/> 在 <see cref="CanGrant"/> 返回 true 之后
/// <b>不会失败、不会抛</b>。钱包（<c>Wallet</c>）是不可变的，
/// 因此「先算出每个英雄的新钱包，再一次性换上」天然满足这一条。</para>
/// </remarks>
public interface IWalletGrantWriter
{
    /// <summary>
    /// 预检：这批发放是否<b>全部</b>可行。<b>不改变任何状态。</b>
    /// </summary>
    /// <param name="grants">发放明细。</param>
    /// <returns>全部可行返回 true；任何一条不可行返回 false（此时整笔结算都不该应用）。</returns>
    bool CanGrant(IReadOnlyList<WalletGrant> grants);

    /// <summary>
    /// 提交：把钱发出去。<b>调用前必须已通过 <see cref="CanGrant"/>。</b>
    /// </summary>
    /// <param name="grants">发放明细。</param>
    void Grant(IReadOnlyList<WalletGrant> grants);
}
