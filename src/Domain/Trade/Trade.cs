using System;
using System.Collections.Generic;
using ClaudeCodeGameStudios.Domain.Heroes;
using ClaudeCodeGameStudios.Domain.Items;
using ClaudeCodeGameStudios.Domain.Village;

namespace ClaudeCodeGameStudios.Domain.Trade;

/// <summary>
/// 一次交易的<b>标识</b>。复合键（D-018 修过的"复合幂等键"模式）。
/// </summary>
/// <remarks>
/// <para><b>字段全部在同一时刻定下</b>（构造期），所以"同一次交易"在不同位置
/// 算出来的 Id 永远一致 ——
/// 这正是 <c>Settlement.AppliedKeys</c> 幂等判定的依据。
/// 任何字段在交易成立后<b>不</b>可变。</para>
///
/// <para><b>为什么是这 6 个</b>：</para>
/// <list type="bullet">
/// <item><c>RunId</c> —— 同一局游戏内可能有多笔交易，区分</item>
/// <item><c>Seller</c> + <c>Buyer</c> —— 同一对英雄同种物品同次也不能算两笔（按时间）</item>
/// <item><c>ItemId</c> + <c>Quantity</c> —— 同卖方同买家在同种商品上金额不同</item>
/// <item><c>SimTime</c> —— 同一秒内同一对英雄同种商品同数量 = 几乎不可能（<c>SimClock</c> 在该秒会推进状态）</item>
/// </list>
///
/// <para><b>留意</b>：<see cref="Quantity"/> 与 <see cref="UnitPrice"/> 必须 <b>同存</b> ——
/// 只看 Quantity 不能重现交易（不知道卖了多少的石料还是多少的木料），只
/// 看 UnitPrice 也不能（不知道 1 份 vs 10 份）。两者的乘积才是钱数。</para>
/// </remarks>
public readonly record struct TradeId(string RunId, HeroId Seller, HeroId Buyer, ItemId ItemId, long Quantity, double SimTime)
{
    public override string ToString()
        => $"{RunId}#{Seller.Value}->{Buyer.Value}#{ItemId.Value}x{Quantity}@{SimTime:0.###}";
}

/// <summary>交易状态。单向迁移，不可回退（与 I14 一致）。</summary>
public enum TradeStatus
{
    /// <summary>已创建但尚未执行。</summary>
    Pending = 0,
    /// <summary>已成功结算：付款、发货、收钱都完成。</summary>
    Succeeded = 1,
    /// <summary>被拒绝：余额不够、商品未登记、买卖方相同等。</summary>
    Rejected = 2,
}

/// <summary>
/// 一次交易：<b>声明</b>一次买卖意图 + 一次<b>应用</b>结果。
/// </summary>
/// <remarks>
/// <para><b>不变</b>：构造之后 Id / Seller / Buyer / ItemId / Quantity / UnitPrice / SimTime
/// 都不变。状态字段（<see cref="Status"/>）按 I14 规则单向迁移（Pending → Succeeded/Rejected）。</para>
///
/// <para><b>复合幂等</b>：<see cref="TradeId"/> 是复合键。
/// 同一组（同一秒、同一对买卖方、同种商品、同数量）只能被 <see cref="Apply"/>
/// 成功一次 —— 第二次同名交易在 <see cref="ClaudeCodeGameStudios.Domain.Settlement.Settlement"/>
/// 的 <c>AppliedKeys</c> 看到同 Id 就会跳过。
/// 这条幂等性<b>不</b>在本类里实现 —— Trade 自己只做"应用一次"，
/// 幂等由 Settlement 负责（与第一阶段 I4 一致）。</para>
///
/// <para><b>全有或全无（I15）</b>：<see cref="Apply"/> 严格遵循</para>
/// <list type="bullet">
/// <item><b>先预检</b>：买卖方合法、单价 ≥ 0、买得起、卖方有库存</item>
/// <item><b>再变更</b>：扣买方的钱 + 加卖方的钱 + 移物品</item>
/// <item>任何一步失败 → <b>整笔不发生</b>，状态仍 <c>Pending</c>（被拒）或 <c>Rejected</c></item>
/// </list>
///
/// <para><b>关键设计</b>：<see cref="Apply"/> 的入参是 <b>两个 Wallet + 一个 Inventory + 一个 Inventory + 一个 PriceBook</b>，
/// 由调用方传入。<br/>
/// <b>这里没有"村庄"的概念</b>。摊位费（P2-D02 5%）<b>不</b>在 <see cref="Apply"/> 里
/// 处理 —— 那属于 <c>Settlement</c> 或专门的 <c>StallFee</c> ，
/// 不该让 Trade 自己既负责商品又负责摊位（违反单一职责）。</para>
/// </remarks>
public sealed class Trade
{
    /// <summary>交易标识（复合键）。</summary>
    public TradeId Id { get; }

    /// <summary>卖方英雄标识。</summary>
    public HeroId Seller { get; }

    /// <summary>买方英雄标识。</summary>
    public HeroId Buyer { get; }

    /// <summary>商品标识。</summary>
    public ItemId ItemId { get; }

    /// <summary>商品数量（≥ 1）。</summary>
    public long Quantity { get; }

    /// <summary>单价（钱）。本类<b>不查价格表</b> —— 调用方在构造前自己查，避免这里依赖 <see cref="PriceBook"/>。</summary>
    public long UnitPrice { get; }

    /// <summary>发生时间（模拟秒）。</summary>
    public double SimTime { get; }

    /// <summary>状态（默认 Pending）。</summary>
    public TradeStatus Status { get; private set; } = TradeStatus.Pending;

    /// <summary>交易总金额（仅供展示 / 存档用，不参与 Execute 逻辑）。</summary>
    public long TotalAmount => UnitPrice * Quantity;

    /// <summary>
    /// 构造一笔交易。
    /// </summary>
    /// <remarks>
    /// <b>构造期防线</b>（D-018 守门规矩）：
    /// 买卖方不可相同、Quantity ≥ 1、UnitPrice ≥ 0、Id 字段全非空。
    /// 价格 ≥ 0 而不是 &gt; 0 —— 0 = "白送"（D-021 没用，但**先**不拒，
    /// 留给游戏规则层判断；这里只防负）。
    /// </remarks>
    /// <exception cref="ArgumentException">
    /// 买卖方相同、Quantity 非正、UnitPrice 负、任一 Id 字段空。
    /// </exception>
    public Trade(
        TradeId id,
        HeroId seller,
        HeroId buyer,
        ItemId item,
        long quantity,
        long unitPrice,
        double simTime)
    {
        if (seller == buyer)
        {
            throw new ArgumentException("卖方与买方不可相同。", nameof(seller));
        }

        if (string.IsNullOrEmpty(id.RunId) || string.IsNullOrEmpty(seller.Value)
            || string.IsNullOrEmpty(buyer.Value) || string.IsNullOrEmpty(item.Value))
        {
            throw new ArgumentException("交易 Id 字段不得为空。", nameof(id));
        }

        if (quantity < 1)
        {
            throw new ArgumentException(
                $"数量必须 ≥ 1：{quantity}。", nameof(quantity));
        }

        if (unitPrice < 0)
        {
            throw new ArgumentException(
                $"单价不得为负：{unitPrice}。", nameof(unitPrice));
        }

        if (simTime < 0.0 || double.IsNaN(simTime) || double.IsInfinity(simTime))
        {
            throw new ArgumentOutOfRangeException(
                nameof(simTime), simTime, "发生时间必须是非负有限数。");
        }

        Id = id;
        Seller = seller;
        Buyer = buyer;
        ItemId = item;
        Quantity = quantity;
        UnitPrice = unitPrice;
        SimTime = simTime;
    }

    /// <summary>
    /// 一次<b>预检</b>：把所有能挂的边角都验一遍。
    /// </summary>
    /// <remarks>
    /// <b>不改任何状态</b>。这一步是把"够不够钱"和"够不够库存"看清之后，
    /// 调用方再决定要不要调 <see cref="Apply"/>。
    /// </remarks>
    /// <returns>成功 = true，失败 = false（连原因都没给，调用方自己查）</returns>
    public bool Precheck(Wallet buyerWallet, Inventory sellerBag)
    {
        if (buyerWallet is null) return false;
        if (sellerBag is null) return false;

        long total = TotalAmount;
        // 币种就是「金币」（D-021 拍定的唯一货币）
        if (buyerWallet.Amount(new ResourceId("金币")) < total) return false;   // 买不起
        if (sellerBag.Count(ItemId) < Quantity) return false;       // 库存不够

        return true;
    }

    /// <summary>
    /// 执行交易（I15 全有或全无）。<b>不改任何东西</b> —— 纯函数：
    /// 收 4 个旧值，<b>返 4 个新值</b>。调用方拿这 4 个新值去替换 <c>HeroAgent</c> 的字段。
    /// </summary>
    /// <remarks>
    /// <para><b>纯函数形态</b>：<see cref="Trade"/> 不持有 <c>HeroAgent</c> 引用，
    /// <b>不</b>改任何字段。它只算"在这 4 个输入下，4 个输出该是什么"，
    /// 调用方负责把新值写回。错不了一点 —— 预检、扣除、增加、按序进行，
    /// 任何一步失败 = 整笔失败 = 输入原样返回。</para>
    ///
    /// <para><b>成功</b>：状态变 <see cref="TradeStatus.Succeeded"/>，
    /// 返 <c>(newBuyerWallet, newSellerBag, newSellerWallet, newBuyerBag)</c>。</para>
    /// <para><b>失败</b>：<b>任何</b>一步失败 → 整笔不发生，
    /// 状态变 <see cref="TradeStatus.Rejected"/>，
    /// 4 个入参原样返回（不可变 —— 调用方拿到的就是它自己传进来的引用）。</para>
    ///
    /// <para><b>顺序</b>（预防 Wallet 容量溢出、Inventory 超上限）：</para>
    /// <list type="number">
    /// <item>卖方减物品（<c>TryRemove</c>，不足返 null → 拒绝）</item>
    /// <item>买方减钱（<c>TrySpend</c>，不足返 null → 拒绝）</item>
    /// <item>卖方加钱（<c>TryAdd</c>，不可能失败 —— 加钱无上限、无负数风险）</item>
    /// <item>买方加物品（<c>TryAdd</c>，超容量返 null → 拒绝）</item>
    /// </list>
    /// <para>每步检查 null，<b>任一</b>失败 → 立即返 null（不在前几步变更之后才检查），
    /// 这就是 I15 的"全有或全无"。</para>
    /// </remarks>
    /// <returns>
    /// 成功 = 4 个新引用（元组 <c>(buyerWallet, sellerBag, sellerWallet, buyerBag)</c>）；<br/>
    /// 失败 = null（调用方自己查 <see cref="Status"/> 区分原因）。
    /// </returns>
    public (Wallet buyerWallet, Inventory sellerBag, Wallet sellerWallet, Inventory buyerBag)? Apply(
        Wallet buyerWallet, Inventory sellerBag, Wallet sellerWallet, Inventory buyerBag)
    {
        if (buyerWallet is null || sellerBag is null
            || sellerWallet is null || buyerBag is null)
        {
            Status = TradeStatus.Rejected;   // 缺入参 = 拒绝
            return null;
        }
        if (Status != TradeStatus.Pending) return null;   // 已结束 → 拒绝再跑

        // ── 预检：先看清能不能整笔做完 ──
        if (!Precheck(buyerWallet, sellerBag))
        {
            Status = TradeStatus.Rejected;   // 预检不过 = 拒绝
            return null;
        }

        long total = TotalAmount;
        var gold = new ResourceId("金币");

        // 顺序：减 → 减 → 加 → 加，每步查 null
        var newSellerBag = sellerBag.TryRemove(ItemId, Quantity);
        if (newSellerBag is null)
        {
            Status = TradeStatus.Rejected;
            return null;
        }

        var newBuyerWallet = buyerWallet.TrySpend(gold, total);
        if (newBuyerWallet is null)
        {
            Status = TradeStatus.Rejected;
            return null;
        }

        var newSellerWallet = sellerWallet.TryAdd(gold, total);
        if (newSellerWallet is null)   // 加钱不可能失败 —— 但守门
        {
            Status = TradeStatus.Rejected;
            return null;
        }

        var newBuyerBag = buyerBag.TryAdd(ItemId, Quantity);
        if (newBuyerBag is null)        // 买方背包容量
        {
            Status = TradeStatus.Rejected;
            return null;
        }

        // ── 全有或全无：4 个新引用都拿到了 ──
        Status = TradeStatus.Succeeded;
        return (newBuyerWallet, newSellerBag, newSellerWallet, newBuyerBag);
    }
}
