using System;
using System.Collections.Generic;
using ClaudeCodeGameStudios.Application;
using ClaudeCodeGameStudios.Domain.Heroes;
using ClaudeCodeGameStudios.Domain.Items;
using ClaudeCodeGameStudios.Domain.Village;
using DomainStall = ClaudeCodeGameStudios.Domain.Stall.Stall;
using DomainStallStatus = ClaudeCodeGameStudios.Domain.Stall.StallStatus;
using DomainStallFee = ClaudeCodeGameStudios.Domain.Stall.StallFee;
using DomainTrade = ClaudeCodeGameStudios.Domain.Trade.Trade;
using DomainTradeId = ClaudeCodeGameStudios.Domain.Trade.TradeId;
using DomainTradeStatus = ClaudeCodeGameStudios.Domain.Trade.TradeStatus;
using DomainVillage = ClaudeCodeGameStudios.Domain.Village.Village;

namespace ClaudeCodeGameStudios.Application.Stall;

/// <summary>
/// 摆摊 / 收摊 / 买摊 三件事的<b>应用层门面</b>。
/// </summary>
/// <remarks>
/// <para><b>职责</b>：把"摆摊 = 挪货 + 注册"、"买摊 = Trade.Apply + 5% 入村金库 + stall 库存递减"
/// 三步按 I15 守门，不让任何一步失败留下半摊子。</para>
///
/// <para><b>不</b>做什么：</para>
/// <list type="bullet">
/// <item>不画摊位（应用层不管渲染）</item>
/// <item>不决策英雄该摆什么（M4 决策流的工作）</item>
/// <item>不挂 Settlement 写村庄——M3 走的是 <c>Village.Add</c> 直写，
///       保留 Settlement 路径给"需要幂等"的战斗奖励流</item>
/// </list>
///
/// <para><b>一人一摊</b>（D-023 / P2-D04）：目录 <c>Dictionary&lt;HeroId, Stall&gt;</c>，
/// <c>PlaceStall</c> 时若该 Hero 已有 <c>Status != Closed</c> 的摊位 → 拒。
/// <c>Closed</c> 摊位 <b>不</b>占位（让英雄开新摊）。</para>
///
/// <para><b>5% 入村金库</b>：D-023 拍定"floor + 残差归卖家"。
/// <c>BuyFromStall</c> 内部 <c>StallFee.Compute(totalAmount)</c> 算两路（卖家净收 + 村入账），
/// 村入账 = 0 时<b>不</b>调 <c>Village.Add</c>（<c>Add</c> 拒 0）。</para>
///
/// <para><b>摆摊前置条件</b>（D-023 / P2-D04，AND 三项）：</para>
/// <list type="bullet">
/// <item>英雄<b>在场</b>（<c>Away == false</c>）</item>
/// <item>英雄<b>空闲</b>（<c>ToSite == false</c>）</item>
/// <item>英雄<b>持可售物品 ≥ qty</b>（<c>Bag.Count(itemId) >= qty</c> 且
///       <c>PriceBook.IsSellable(itemId)</c>）</item>
/// </list>
/// </remarks>
public sealed class StallService
{
    private readonly Dictionary<HeroId, DomainStall> _stalls = new();
    private readonly DomainVillage _village;
    private readonly PriceBook _book;
    private readonly Func<HeroId, HeroAgent?> _heroLookup;

    /// <summary>
    /// 已处理的购买业务键（幂等依据，第四阶段 3A；持久化在 M4 接）。
    /// </summary>
    /// <remarks>
    /// 同一业务请求键第二次调用不会再次扣款/发货。键为空 = 调用方不要求幂等（旧行为）。
    /// </remarks>
    private readonly HashSet<string> _appliedRequestIds = new(StringComparer.Ordinal);

    /// <summary>已处理的购买业务键（只读，供测试与 M4 持久化使用）。</summary>
    public IReadOnlyCollection<string> AppliedRequestIds => _appliedRequestIds;

    /// <summary>
    /// 村庄税额余数（单位：1/100 金，0..99）—— <b>D-052</b>。
    /// </summary>
    /// <remarks>
    /// <para><b>它是什么</b>：5% 摊位税里<b>还没凑够 1 金</b>的那部分。
    /// 原实现逐笔 floor，单价 3/5/8 时每笔都 floor 到 0 → 村库永远收不到钱
    /// （R-08 实测 5088 金成交额抽出 0 金）。累计之后，任何单价都最终能收齐 5%。</para>
    ///
    /// <para><b>状态所有者为什么是本类</b>：摊位交易只有这一条写路径
    /// （<see cref="TryBuy"/>），余数必须和交易在<b>同一次提交</b>里更新，
    /// 否则会出现「货交了、零头没记」或反之。放在别处就得多一次跨对象协调。</para>
    /// </remarks>
    private long _villageCutCarry;

    /// <summary>村庄税额余数（只读；供存档与测试）。</summary>
    public long VillageCutCarry => _villageCutCarry;

    /// <summary>
    /// 恢复村庄税额余数 —— <b>仅供读档</b>。
    /// </summary>
    /// <param name="carry">余数（单位：1/100 金）。必须 ∈ [0, 99]。</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="carry"/> 不在 [0, 99]。</exception>
    /// <remarks>
    /// 走这个口而不是公开 setter —— 与 <see cref="Restore"/> 同一规矩：
    /// 「只有一个入口」要看得见，否则谁都能在半夜把余数改成 50。
    /// </remarks>
    public void RestoreVillageCutCarry(long carry)
    {
        if (carry < 0 || carry >= DomainStallFee.UnitsPerGold)
        {
            throw new ArgumentOutOfRangeException(
                nameof(carry), carry,
                $"村庄税额余数必须在 [0, {DomainStallFee.UnitsPerGold - 1}] 之间。");
        }

        _villageCutCarry = carry;
    }

    /// <summary>摊位目录（只读）。</summary>
    public IReadOnlyDictionary<HeroId, DomainStall> Stalls => _stalls;

    /// <summary>村庄金库的 ResourceId（D-021 "金币"）。</summary>
    public static readonly ResourceId Gold = new("金币");

    /// <param name="village">村庄（写"金币"金库）。</param>
    /// <param name="book">统一价格表（验"可售"+ 取单价）。</param>
    /// <param name="heroLookup">按 HeroId 找英雄（含 Bag/Wallet/Away/ToSite）。</param>
    public StallService(DomainVillage village, PriceBook book, Func<HeroId, HeroAgent?> heroLookup)
    {
        _village = village ?? throw new ArgumentNullException(nameof(village));
        _book = book ?? throw new ArgumentNullException(nameof(book));
        _heroLookup = heroLookup ?? throw new ArgumentNullException(nameof(heroLookup));
    }

    /// <summary>摆摊结果（成功三件套）。</summary>
    public sealed class PlaceStallResult
    {
        public required HeroId Owner { get; init; }
        public required DomainStall Stall { get; init; }
        public required Inventory NewBag { get; init; }   // 摆摊后英雄 Bag（已扣 qty 份）
    }

    /// <summary>开摊。</summary>
    /// <param name="owner">摊主。</param>
    /// <param name="itemId">商品。</param>
    /// <param name="qty">挪入摊位库存的数量（≥ 1）。</param>
    /// <param name="simTime">开摊时间（模拟秒）。</param>
    /// <returns>成功 = 三个新引用；失败 = null。</returns>
    public PlaceStallResult? PlaceStall(
        HeroId owner, ItemId itemId, long qty, double simTime,
        int gridX = 0, int gridY = 0)
    {
        // 入参防线
        if (string.IsNullOrEmpty(owner.Value)) return null;
        if (string.IsNullOrEmpty(itemId.Value)) return null;
        if (qty < 1) return null;
        if (simTime < 0.0 || double.IsNaN(simTime) || double.IsInfinity(simTime)) return null;

        // 英雄存在
        var hero = _heroLookup(owner);
        if (hero is null) return null;

        // D-023 / P2-D04：AND 三项
        if (hero.Away) return null;                  // 不在场
        if (hero.ToSite) return null;                // 忙
        if (hero.Bag.Count(itemId) < qty) return null;   // 货不够
        if (!_book.IsSellable(itemId)) return null;  // 商品不卖

        // 一人一摊：已有 Open / SoldOut → 拒
        if (_stalls.TryGetValue(owner, out var existing)
            && existing.Status != DomainStallStatus.Closed)
        {
            return null;
        }

        // M5 / D-033 位置校验：
        // (1) (gridX, gridY) 不能与建筑占用格重叠
        // (2) (gridX, gridY) 必须在**至少一个**已建成**范围类**建筑的影响区内
        //
        // ⚠️ **D-098：只认「范围类」建筑**（围栏 + 市集）。
        // 防护罩是**专职防御**建筑（D-095）⇒ 它**不**放开摆摊范围。
        // ⇒ 这是「两层检查」的反面：这里**只有一处**判定 ⇒ 权威唯一。
        if (_village.IsOccupied(gridX, gridY)) return null;   // 占建筑 → 拒
        bool inAnyAffects = false;
        foreach (var b in _village.Buildings)
        {
            if (!RangeBuildings.IsRangeBuilding(b)) continue;   // D-098：非范围类不参与
            if (b.IsCellInAffectsArea(gridX, gridY)) { inAnyAffects = true; break; }
        }
        if (!inAnyAffects) return null;   // 0 范围建筑影响 → 拒（v1 简化）

        long unitPrice = _book.PriceOf(itemId);
        if (unitPrice < 0) return null;   // 不应发生（IsSellable 守过）—— 守门

        // I15 提交
        var newBag = hero.Bag.TryRemove(itemId, qty);
        if (newBag is null) return null;
        var newStall = new DomainStall(owner, itemId, qty, unitPrice, DomainStallStatus.Open, simTime,
            gridX, gridY);

        hero.ApplyTradeResult(hero.Wallet, newBag);   // 只换 Bag —— Wallet 复用
        _stalls[owner] = newStall;
        return new PlaceStallResult { Owner = owner, Stall = newStall, NewBag = newBag };
    }

    /// <summary>买摊结果（成功三件套）。</summary>
    public sealed class BuyStallResult
    {
        public required HeroId Seller { get; init; }
        public required HeroId Buyer { get; init; }
        public required DomainStall NewStall { get; init; }   // 库存递减
        public required long TotalAmount { get; init; }
        public required long SellerNet { get; init; }
        public required long VillageCut { get; init; }

        /// <summary>
        /// 买了几份（R-02 补）。
        /// </summary>
        /// <remarks>
        /// <b>为什么以前没有</b>：结果里只有金额，而金额 = 单价 × 份数 ——
        /// 单价为 0（白送摊，合法）时<b>除回去</b>会除以 0。
        /// 于是「到底买了几份」这件事只能由调用方自己记着，
        /// 而调用方记的那份<b>可能和实际成交的那份对不上</b>。
        /// 成交结果自己带份数，谁都不用猜。
        /// </remarks>
        public required long Quantity { get; init; }
    }

    /// <summary>一次购买请求的结果分类（3A：把“为什么失败”说清楚）。</summary>
    public enum BuyOutcome
    {
        /// <summary>成交，状态已更新。</summary>
        Bought = 0,
        /// <summary>同一业务请求键之前已处理（幂等拒绝）。</summary>
        DuplicateRequest = 1,
        /// <summary>入参非法（空 ID、数量 &lt;1、时间非法、英雄不存在等）。</summary>
        InvalidRequest = 2,
        /// <summary>买卖双方相同。</summary>
        SameHero = 3,
        /// <summary>卖方没有摊位。</summary>
        NoStall = 4,
        /// <summary>摊位不在 Open 状态。</summary>
        StallNotOpen = 5,
        /// <summary>买的商品与摊位商品不一致。</summary>
        WrongItem = 6,
        /// <summary>摊位库存不足。</summary>
        InsufficientStock = 7,
        /// <summary>买方金币不足。</summary>
        CannotAfford = 8,
        /// <summary>买方背包拒收（容量等）。</summary>
        BagRejected = 9,
        /// <summary>卖方钱包拒收（溢出等）。</summary>
        WalletRejected = 10,
    }

    /// <summary>一次购买尝试：结果分类 + 成功时的数据（失败为 null）。</summary>
    /// <param name="Outcome">结果分类。</param>
    /// <param name="Result">成交时的结果；失败为 null。</param>
    public readonly record struct BuyAttempt(BuyOutcome Outcome, BuyStallResult? Result)
    {
        /// <summary>是否成交。</summary>
        public bool Bought => Outcome == BuyOutcome.Bought;
    }

    /// <summary>买摊（I15：预检 → 算 → 一次性提交）。</summary>
    /// <remarks>
    /// <para><b>不</b>走 <see cref="DomainTrade.Trade.Apply"/>：<c>Trade.Apply</c> 假设
    /// 卖方库存<b>在 hero.Bag</b>，但摆摊后货已挪入 <see cref="DomainStall.Stock"/>，
    /// hero.Bag <b>不含</b>摊位上的货。强行传 <c>sellerHero.Bag</c> 会让
    /// <c>Precheck</c> 的"卖方库存够"假拒。摊位的"卖方库存"是 <c>stall.Stock</c>，
    /// 单独走 I15 守门。</para>
    ///
    /// <para><b>四步提交</b>（顺序为“先无法失败的、后可能失败的”，失败回滚面最小）：</para>
    /// <list type="number">
    /// <item>村金库入账：<c>Village.Add(金币, villageCut)</c>（villageCut &gt; 0 才调；可能抛溢出）</item>
    /// <item>卖方收钱：<c>Wallet.TryAdd(金币, sellerNet)</c></item>
    /// <item>买方付钱：<c>Wallet.TrySpend(金币, totalAmount)</c></item>
    /// <item>买方收货：<c>Bag.TryAdd(itemId, qty)</c> + 摊位库存递减</item>
    /// </list>
    /// <para>任何一步失败 → 不提交；提交阶段只剩不可失败的赋值。</para>
    /// </remarks>
    /// <param name="seller">摊主。</param>
    /// <param name="buyer">买方英雄。</param>
    /// <param name="itemId">商品（与摊位一致）。</param>
    /// <param name="qty">买几份（≥ 1）。</param>
    /// <param name="simTime">成交时间（模拟秒）。</param>
    /// <returns>成功三件套；失败 null。</returns>
    public BuyStallResult? BuyFromStall(
        HeroId seller, HeroId buyer, ItemId itemId, long qty, double simTime)
        => TryBuy(seller, buyer, itemId, qty, simTime, requestId: "").Result;

    /// <summary>
    /// 买摊（带结果分类与可选业务幂等键）。
    /// </summary>
    /// <param name="seller">摊主。</param>
    /// <param name="buyer">买方英雄。</param>
    /// <param name="itemId">商品（与摊位一致）。</param>
    /// <param name="qty">买几份（≥ 1）。</param>
    /// <param name="simTime">成交时间（模拟秒）。</param>
    /// <param name="requestId">
    /// 业务请求键。非空时同一键只成交一次（幂等）；空串/空白 = 不做幂等（旧行为）。
    /// <b>不是</b> <c>TradeId</c>：摊位购买走本方法，不伪造 TradeId。
    /// </param>
    /// <returns>结果分类；失败时状态不变。</returns>
    public BuyAttempt TryBuy(
        HeroId seller, HeroId buyer, ItemId itemId, long qty, double simTime,
        string requestId = "")
    {
        // ── 入参防线 ──
        if (string.IsNullOrEmpty(seller.Value)) return new(BuyOutcome.InvalidRequest, null);
        if (string.IsNullOrEmpty(buyer.Value)) return new(BuyOutcome.InvalidRequest, null);
        if (seller == buyer) return new(BuyOutcome.SameHero, null);
        if (string.IsNullOrEmpty(itemId.Value)) return new(BuyOutcome.InvalidRequest, null);
        if (qty < 1) return new(BuyOutcome.InvalidRequest, null);
        if (simTime < 0.0 || double.IsNaN(simTime) || double.IsInfinity(simTime))
            return new(BuyOutcome.InvalidRequest, null);

        bool useRequestKey = !string.IsNullOrWhiteSpace(requestId);
        if (useRequestKey && _appliedRequestIds.Contains(requestId))
        {
            return new(BuyOutcome.DuplicateRequest, null);
        }

        // ── 摊位 + 状态 ──
        if (!_stalls.TryGetValue(seller, out var stall)) return new(BuyOutcome.NoStall, null);
        if (stall.Status != DomainStallStatus.Open) return new(BuyOutcome.StallNotOpen, null);
        if (stall.ItemId != itemId) return new(BuyOutcome.WrongItem, null);
        if (stall.Stock < qty) return new(BuyOutcome.InsufficientStock, null);

        // ── 双方英雄 ──
        var sellerHero = _heroLookup(seller);
        var buyerHero = _heroLookup(buyer);
        if (sellerHero is null || buyerHero is null) return new(BuyOutcome.InvalidRequest, null);

        // ── 算金额 + 拆 5%（含余数累计，D-052）──
        //
        // 金额乘法**必须溢出安全**：`UnitPrice × qty` 裸乘会溢出 long。
        // 溢出不是「交易失败」，是「这笔单子根本不存在」→ 判为入参非法，
        // **不能让异常冒到游戏循环里**（那会打断整个游戏）。
        if (!TryMultiply(stall.UnitPrice, qty, out long totalAmount))
        {
            return new(BuyOutcome.InvalidRequest, null);
        }

        var (sellerNet, villageCut, newCarry) = DomainStallFee.Compute(totalAmount, _villageCutCarry);

        // ── 预检 + 一次性提交（I15）──
        var newSellerWallet = sellerHero.Wallet.TryAdd(Gold, sellerNet);
        if (newSellerWallet is null) return new(BuyOutcome.WalletRejected, null);

        var newBuyerWallet = buyerHero.Wallet.TrySpend(Gold, totalAmount);
        if (newBuyerWallet is null) return new(BuyOutcome.CannotAfford, null);

        var newBuyerBag = buyerHero.Bag.TryAdd(itemId, qty);
        if (newBuyerBag is null) return new(BuyOutcome.BagRejected, null);

        var newStall = stall.AfterSale(qty);

        // 村金库先写：它是唯一可能抛的一步。它失败 = 什么都没写。
        //
        // ⚠️ **余数必须在它之后写**（D-052）：`Village.Add` 用 checked 算术，
        // 村金库接近上限时会抛 OverflowException。若先写余数再写金库，
        // 抛出来就留下「余数记了、钱没入账」的半笔账。
        if (villageCut > 0)
        {
            _village.Add(Gold, villageCut);
        }

        // 余数落定（纯内存赋值，不会抛）—— 与上面的金库入账是同一笔账的两半。
        _villageCutCarry = newCarry;

        // 以下全是不可失败的赋值 —— 到这里已经没有半笔账的路径。
        sellerHero.ApplyTradeResult(newSellerWallet, sellerHero.Bag);   // 只换 Wallet
        buyerHero.ApplyTradeResult(newBuyerWallet, newBuyerBag);          // 双换
        _stalls[seller] = newStall;

        if (useRequestKey) _appliedRequestIds.Add(requestId);

        return new BuyAttempt(BuyOutcome.Bought, new BuyStallResult
        {
            Seller = seller,
            Buyer = buyer,
            NewStall = newStall,
            TotalAmount = totalAmount,
            SellerNet = sellerNet,
            VillageCut = villageCut,
            Quantity = qty,
        });
    }

    /// <summary>
    /// 溢出安全的乘法（<c>long × long</c>）。
    /// </summary>
    /// <param name="a">左操作数。</param>
    /// <param name="b">右操作数。</param>
    /// <param name="result">乘积；溢出时为 0。</param>
    /// <returns>不溢出返回 true；溢出返回 false。</returns>
    /// <remarks>
    /// <b>为什么不直接用 <c>checked</c></b>：<c>checked</c> 会抛
    /// <see cref="OverflowException"/>，而这条路径上的调用方
    /// （<c>HeroBehaviorService</c> → <c>DailyShoppingService</c> → 游戏循环）
    /// 并没有接住它 —— 异常会一路冒到 <c>_Process</c>，把游戏打断。
    /// 交易金额溢出是「这笔单子不成立」，用<b>返回值</b>表达比用异常合适。
    /// </remarks>
    private static bool TryMultiply(long a, long b, out long result)
    {
        if (a == 0 || b == 0)
        {
            result = 0L;
            return true;
        }

        // 负数不参与交易（单价与数量都非负），负数一律判非法。
        if (a < 0 || b < 0)
        {
            result = 0L;
            return false;
        }

        result = a * b;   // 默认 unchecked 语义；下面的除法比较负责判定溢出
        if (result / b != a)
        {
            result = 0L;
            return false;
        }

        return true;
    }

    /// <summary>收摊：状态变 Closed。<b>不</b>动英雄背包（库存归 0 时也是 Closed 而非"丢货"）。</summary>
    /// <returns>成功 = true；该英雄本就没摊 = false（不是错误）。</returns>
    public bool CloseStall(HeroId owner)
    {
        if (string.IsNullOrEmpty(owner.Value)) return false;
        if (!_stalls.TryGetValue(owner, out var stall)) return false;
        if (stall.Status == DomainStallStatus.Closed) return false;   // 已收
        _stalls[owner] = stall.Closed();
        return true;
    }

    /// <summary>查某英雄的摊位（无摊 = null）。</summary>
    public DomainStall? GetStall(HeroId owner)
    {
        if (string.IsNullOrEmpty(owner.Value)) return null;
        return _stalls.TryGetValue(owner, out var s) ? s : null;
    }

    /// <summary>
    /// 导出全部摊位（顺序确定：按摊主 Id 排序），供存档与测试比对。
    /// </summary>
    /// <returns>摊位的快照列表。</returns>
    public IReadOnlyList<DomainStall> Snapshot()
    {
        var list = new List<DomainStall>(_stalls.Count);
        list.AddRange(_stalls.Values);
        list.Sort(static (a, b) => string.CompareOrdinal(a.Owner.Value, b.Owner.Value));
        return list;
    }

    /// <summary>
    /// 用快照<b>整份替换</b>摊位目录与已处理购买请求键 —— <b>全有或全无</b>（读档用）。
    /// </summary>
    /// <remarks>
    /// 先完整校验到临时表，再一次性替换：任何一条非法输入都不会留下半份摊位目录，
    /// 也不会让旧的幂等键与新摊位错配。
    /// </remarks>
    /// <param name="stalls">摊位列表（一人一摊）。</param>
    /// <param name="appliedRequestIds">已处理的购买请求键。</param>
    /// <exception cref="ArgumentNullException">任一参数为 null。</exception>
    /// <exception cref="ArgumentException">摊位为 null、摊主为空、摊主重复、请求键为空。</exception>
    public void Restore(
        IReadOnlyList<DomainStall> stalls,
        IEnumerable<string> appliedRequestIds)
    {
        if (stalls is null) throw new ArgumentNullException(nameof(stalls));
        if (appliedRequestIds is null) throw new ArgumentNullException(nameof(appliedRequestIds));

        var newStalls = new Dictionary<HeroId, DomainStall>(stalls.Count);
        foreach (var stall in stalls)
        {
            if (stall is null) throw new ArgumentException("摊位不能为 null。", nameof(stalls));
            if (string.IsNullOrEmpty(stall.Owner.Value))
            {
                throw new ArgumentException("摊主不能为空。", nameof(stalls));
            }

            if (!newStalls.TryAdd(stall.Owner, stall))
            {
                throw new ArgumentException(
                    $"同一摊主出现多个摊位：{stall.Owner.Value}（一人一摊）。", nameof(stalls));
            }
        }

        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var key in appliedRequestIds)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                throw new ArgumentException(
                    "已处理购买请求键不能为空 —— 它不能作为幂等依据。", nameof(appliedRequestIds));
            }

            keys.Add(key);
        }

        _stalls.Clear();
        foreach (var (owner, stall) in newStalls) _stalls[owner] = stall;

        _appliedRequestIds.Clear();
        foreach (var key in keys) _appliedRequestIds.Add(key);
    }
}
