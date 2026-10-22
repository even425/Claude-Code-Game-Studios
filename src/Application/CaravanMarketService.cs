using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ClaudeCodeGameStudios.Domain.Heroes;
using ClaudeCodeGameStudios.Domain.Items;
using ClaudeCodeGameStudios.Domain.Village;
using ClaudeCodeGameStudios.Domain.WorldEvents;

namespace ClaudeCodeGameStudios.Application;

/// <summary>商队当前在售的一档报价（给界面看的只读快照）。</summary>
/// <param name="InstanceId">所属的大事件实例（买的时候要指明它）。</param>
/// <param name="ItemId">卖什么。</param>
/// <param name="UnitPrice">单价（开摊时锁定，不随市价变）。</param>
/// <param name="MarketPrice">开摊当时的市价（用来让玩家看得出「便宜了多少」）。</param>
/// <param name="RemainingStock">还剩多少份。</param>
public readonly record struct CaravanOfferView(
    string InstanceId,
    string ItemId,
    long UnitPrice,
    long MarketPrice,
    long RemainingStock);

/// <summary>一次商队购买的<b>结果分类</b>。</summary>
public enum CaravanBuyOutcome
{
    /// <summary>买成了。</summary>
    Bought = 0,

    /// <summary>入参不合法。</summary>
    InvalidRequest = 1,

    /// <summary>同一个请求键已经成交过（幂等）。</summary>
    DuplicateRequest = 2,

    /// <summary>找不到这个商队（或它没开摊）。</summary>
    NoOffer = 3,

    /// <summary>不在购买窗口里（事件已离开「进行中」阶段 ⇒ 限时结束）。</summary>
    WindowClosed = 4,

    /// <summary>货不够了。</summary>
    InsufficientStock = 5,

    /// <summary>买方钱不够。</summary>
    CannotAfford = 6,

    /// <summary>背包放不下。</summary>
    BagRejected = 7,
}

/// <summary>一次商队购买的<b>结果</b>。</summary>
/// <param name="Outcome">结果分类。</param>
/// <param name="Buyer">买方。</param>
/// <param name="ItemId">买到的物品。</param>
/// <param name="Quantity">买了几份。</param>
/// <param name="UnitPrice">成交单价。</param>
/// <param name="TotalAmount">一共花了多少。</param>
/// <param name="RemainingStock">成交后还剩多少货。</param>
public readonly record struct CaravanBuyResult(
    CaravanBuyOutcome Outcome,
    HeroId Buyer,
    string ItemId,
    long Quantity,
    long UnitPrice,
    long TotalAmount,
    long RemainingStock)
{
    /// <summary>买成了吗。</summary>
    public bool Succeeded => Outcome == CaravanBuyOutcome.Bought;
}

/// <summary>
/// <b>商队市场</b>（D-081）：商队来访时开一个<b>限时、限量、低于市价</b>的摊位。
/// </summary>
/// <remarks>
/// <para><b>规则（D-080 拍板）</b>：商队摆摊卖货，**价格低于市价**、**限时**、**限量**。
/// 守村的人马是「在村全体英雄」（D-080 拍板），买货的人也是**某个英雄**——
/// 本项目**没有玩家化身**，钱和货都记在具体某个人名下（R-01）。</para>
///
/// <para>⚠️ <b>钱怎么走（守恒律）</b>：英雄的<b>个人钱包</b>付钱 ⇒ 钱进<b>村金库</b>。
/// 守恒律是 <c>Σ英雄钱包 + 村库 + 累计销毁 = 初始 + 累计发放</c>；
/// 若钱直接付给「商队」，那部分钱就<b>离开了这个世界</b> ⇒ 等式对不上。
/// ⇒ 让钱进村库 ⇒ 英雄这边减多少、村库这边就加多少 ⇒ <b>等式照样成立</b>。
/// <para>⚠️ <b>这一版的简化</b>：商队的货款结算（钱最终离开村子）<b>暂不实现</b>，
/// 钱全额进村库；以后要加「商队抽成」或「记为销毁」再说。
/// <b>不要</b>自己发明抽成比例——那是开发者的决定。</para>
///
/// <para>⚠️ <b>限量是硬约束</b>：不限制库存 ⇒ 玩家八折买进、按市价卖出 ⇒ <b>无限套利</b>
/// （低买高卖，货无限 ⇒ 金币无限）⇒ 库存必须有限，且<b>随存档走</b>
/// （否则读一次档货就补满 ⇒ 限量形同虚设）。</para>
///
/// <para>⚠️ <b>单价锁定</b>：开摊那一刻按市价 × 折扣算出来并<b>锁死</b>（与普通摊位同款：
/// 开摊时锁定单价，售出过程中不变价）⇒ 市价后来怎么变都不影响已开的摊。</para>
/// </remarks>
public sealed class CaravanMarketService
{
    /// <summary>实例状态键：卖什么。</summary>
    public const string StateItem = "caravan/item";

    /// <summary>实例状态键：成交单价（开摊时锁定）。</summary>
    public const string StateUnitPrice = "caravan/unitPrice";

    /// <summary>实例状态键：开摊当时的市价（只用于展示「便宜了多少」）。</summary>
    public const string StateMarketPrice = "caravan/marketPrice";

    /// <summary>实例状态键：还剩多少份。</summary>
    public const string StateStock = "caravan/stock";

    /// <summary>幂等键前缀（复用实例的「已应用后果键」—— 它本来就随存档走）。</summary>
    private const string BuyKeyPrefix = "caravan/buy/";

    private readonly Func<string, WorldEventInstance?> _findInstance;
    private readonly Func<IReadOnlyList<WorldEventInstance>> _listInstances;
    private readonly PriceBook _prices;
    private readonly Village _village;
    private readonly Func<HeroId, HeroAgent?> _heroLookup;

    /// <summary>构造商队市场。</summary>
    /// <param name="findInstance">按实例标识找事件实例。</param>
    /// <param name="listInstances">列出全部事件实例（用来找当前在售的商队）。</param>
    /// <param name="prices">价格表（开摊时算单价用）。</param>
    /// <param name="village">村庄（收钱的地方）。</param>
    /// <param name="heroLookup">按英雄标识找人。</param>
    public CaravanMarketService(
        Func<string, WorldEventInstance?> findInstance,
        Func<IReadOnlyList<WorldEventInstance>> listInstances,
        PriceBook prices,
        Village village,
        Func<HeroId, HeroAgent?> heroLookup)
    {
        _findInstance = findInstance ?? throw new ArgumentNullException(nameof(findInstance));
        _listInstances = listInstances ?? throw new ArgumentNullException(nameof(listInstances));
        _prices = prices ?? throw new ArgumentNullException(nameof(prices));
        _village = village ?? throw new ArgumentNullException(nameof(village));
        _heroLookup = heroLookup ?? throw new ArgumentNullException(nameof(heroLookup));
    }

    /// <summary>
    /// <b>开摊</b>：把报价写进实例状态（单价锁定、库存入档）。
    /// </summary>
    /// <param name="instance">商队事件实例。</param>
    /// <param name="offer">报价（物品 / 折扣 / 库存）。</param>
    /// <returns>开成了吗（商品不在价格表里 ⇒ 没什么可卖 ⇒ 不开）。</returns>
    /// <remarks>
    /// ⚠️ **单价在开摊这一刻算一次**：市价 × 折扣（向下取整，**至少 1**）——
    /// 打折不能打成 0 元白送（那是另一种规则）。
    /// </remarks>
    public bool OpenStall(WorldEventInstance instance, CaravanOffer offer)
    {
        if (instance is null) throw new ArgumentNullException(nameof(instance));
        if (offer is null) throw new ArgumentNullException(nameof(offer));

        var itemId = new ItemId(offer.ItemId);
        long marketPrice = _prices.PriceOf(itemId);
        if (marketPrice <= 0)
        {
            // 不在价格表里 = 不卖的东西 ⇒ 商队没有它可卖 ⇒ 不开摊。
            return false;
        }

        long unitPrice = Math.Max(1, marketPrice * offer.DiscountPercent / 100);

        instance.SetState(StateItem, offer.ItemId);
        instance.SetState(StateUnitPrice, unitPrice.ToString(CultureInfo.InvariantCulture));
        instance.SetState(StateMarketPrice, marketPrice.ToString(CultureInfo.InvariantCulture));
        instance.SetState(StateStock, offer.Stock.ToString(CultureInfo.InvariantCulture));
        return true;
    }

    /// <summary>
    /// 当前<b>在售</b>的商队报价（只有处于「进行中」阶段的才算在售 —— 限时由阶段机管）。
    /// </summary>
    /// <returns>在售的报价列表。</returns>
    public IReadOnlyList<CaravanOfferView> OpenOffers()
    {
        var result = new List<CaravanOfferView>();
        foreach (var instance in _listInstances())
        {
            if (instance.Phase != WorldEventPhase.Ongoing) continue;
            var view = ViewOf(instance);
            if (view is not null) result.Add(view.Value);
        }

        return result;
    }

    /// <summary>
    /// 买商队的货。
    /// </summary>
    /// <param name="instanceId">商队实例标识。</param>
    /// <param name="buyer">买方英雄。</param>
    /// <param name="qty">买几份（≥ 1）。</param>
    /// <param name="simTime">成交时间（模拟秒）。</param>
    /// <param name="requestId">
    /// 业务请求键。非空时同一键只成交一次（幂等）；空串 = 不做幂等。
    /// </param>
    /// <returns>结果分类；失败时状态不变。</returns>
    /// <remarks>
    /// <para><b>四步提交</b>（照 <c>StallService.TryBuy</c> 的 I15 范式）：
    /// ① 校验（幂等 → 窗口 → 货 → 钱包 → 背包，全部**只算不改**）；
    /// ② 村库入账（**唯一可能抛的一步**）；③ 换买方钱包与背包；④ 扣库存 + 记幂等键。
    /// ⇒ 任何一步失败都**什么都不改**（没有半笔账）。</para>
    /// </remarks>
    public CaravanBuyAttempt TryBuy(
        string instanceId, HeroId buyer, long qty, double simTime, string requestId = "")
    {
        // ── 入参防线 ──
        if (string.IsNullOrEmpty(instanceId)) return Fail(CaravanBuyOutcome.InvalidRequest, buyer);
        if (string.IsNullOrEmpty(buyer.Value)) return Fail(CaravanBuyOutcome.InvalidRequest, buyer);
        if (qty < 1) return Fail(CaravanBuyOutcome.InvalidRequest, buyer);
        if (simTime < 0.0 || double.IsNaN(simTime) || double.IsInfinity(simTime))
        {
            return Fail(CaravanBuyOutcome.InvalidRequest, buyer);
        }

        var instance = _findInstance(instanceId);
        if (instance is null) return Fail(CaravanBuyOutcome.NoOffer, buyer);

        // ── 幂等（先看，不写）──
        // ⚠️ **先查后写**：命中就**什么都不做**（状态不变）。
        // 记键发生在**成交之后** —— 否则「买失败的请求」也会把键用掉。
        bool useRequestKey = !string.IsNullOrWhiteSpace(requestId);
        string buyKey = BuyKeyPrefix + requestId;
        if (useRequestKey && instance.AppliedConsequences.Contains(buyKey))
        {
            return new CaravanBuyAttempt(
                new CaravanBuyResult(CaravanBuyOutcome.DuplicateRequest, buyer, "", 0, 0, 0, 0));
        }

        // ── 限时：只有「进行中」阶段能买 ──
        if (instance.Phase != WorldEventPhase.Ongoing)
        {
            return Fail(CaravanBuyOutcome.WindowClosed, buyer);
        }

        // ── 报价与货 ──
        var view = ViewOf(instance);
        if (view is null) return Fail(CaravanBuyOutcome.NoOffer, buyer);
        if (view.Value.RemainingStock < qty)
        {
            return Fail(CaravanBuyOutcome.InsufficientStock, buyer);
        }

        // ── 金额（溢出安全）──
        if (!TryMultiply(view.Value.UnitPrice, qty, out long totalAmount))
        {
            return Fail(CaravanBuyOutcome.InvalidRequest, buyer);
        }

        // ── 买方：钱包与背包（先算，不改）──
        var hero = _heroLookup(buyer);
        if (hero is null) return Fail(CaravanBuyOutcome.InvalidRequest, buyer);

        var newWallet = hero.Wallet.TrySpend(Stall.StallService.Gold, totalAmount);
        if (newWallet is null) return Fail(CaravanBuyOutcome.CannotAfford, buyer);

        var newBag = hero.Bag.TryAdd(new ItemId(view.Value.ItemId), qty);
        if (newBag is null) return Fail(CaravanBuyOutcome.BagRejected, buyer);

        // ── 村库入账：唯一可能抛的一步（接近上限时 checked 溢出）──
        // 抛了就说明**什么都没改**（钱包与背包还没换）。
        _village.Add(Stall.StallService.Gold, totalAmount);

        // ── 以下都是不可失败的赋值 ──
        hero.ApplyTradeResult(newWallet, newBag);
        long remaining = view.Value.RemainingStock - qty;
        instance.SetState(StateStock, remaining.ToString(CultureInfo.InvariantCulture));
        if (useRequestKey) instance.MarkApplied(buyKey);

        return new CaravanBuyAttempt(new CaravanBuyResult(
            CaravanBuyOutcome.Bought, buyer, view.Value.ItemId, qty,
            view.Value.UnitPrice, totalAmount, remaining));
    }

    /// <summary>读出某个实例的报价（状态不全 ⇒ null = 没开摊）。</summary>
    /// <param name="instance">事件实例。</param>
    /// <returns>报价；没开摊时 null。</returns>
    private CaravanOfferView? ViewOf(WorldEventInstance instance)
    {
        var state = instance.State;
        if (!state.TryGetValue(StateItem, out var item)) return null;
        if (!state.TryGetValue(StateUnitPrice, out var price)) return null;
        if (!state.TryGetValue(StateStock, out var stock)) return null;

        state.TryGetValue(StateMarketPrice, out var market);
        if (!long.TryParse(price, NumberStyles.Integer, CultureInfo.InvariantCulture, out long unitPrice))
        {
            return null;
        }

        if (!long.TryParse(stock, NumberStyles.Integer, CultureInfo.InvariantCulture, out long remaining))
        {
            return null;
        }

        long.TryParse(market, NumberStyles.Integer, CultureInfo.InvariantCulture, out long marketPrice);
        return new CaravanOfferView(instance.Id, item, unitPrice, marketPrice, remaining);
    }

    /// <summary>造一个「什么都没改」的失败结果。</summary>
    private static CaravanBuyAttempt Fail(CaravanBuyOutcome outcome, HeroId buyer)
        => new(new CaravanBuyResult(outcome, buyer, "", 0, 0, 0, 0));

    /// <summary>溢出安全的乘法（<c>long × long</c>）。</summary>
    /// <param name="a">左操作数。</param>
    /// <param name="b">右操作数。</param>
    /// <param name="product">乘积（成功时）。</param>
    /// <returns>没溢出吗。</returns>
    private static bool TryMultiply(long a, long b, out long product)
    {
        if (a == 0 || b == 0)
        {
            product = 0;
            return true;
        }

        product = a * b;
        return product / b == a;
    }
}

/// <summary>一次商队购买尝试的<b>包装</b>（含结果分类与结果本体）。</summary>
/// <param name="Result">结果。</param>
public readonly record struct CaravanBuyAttempt(CaravanBuyResult Result)
{
    /// <summary>买成了吗。</summary>
    public bool Succeeded => Result.Succeeded;

    /// <summary>结果分类。</summary>
    public CaravanBuyOutcome Outcome => Result.Outcome;
}
