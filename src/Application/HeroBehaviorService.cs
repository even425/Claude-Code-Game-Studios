using System;
using System.Collections.Generic;
using ClaudeCodeGameStudios.Domain.Heroes;
using ClaudeCodeGameStudios.Domain.Items;
using DomainStall = ClaudeCodeGameStudios.Domain.Stall.Stall;
using DomainStallStatus = ClaudeCodeGameStudios.Domain.Stall.StallStatus;

namespace ClaudeCodeGameStudios.Application.Stall;

/// <summary>
/// 英雄<b>自主经济行为</b>的轻量决策层（M4 / D-025）。
/// </summary>
/// <remarks>
/// <para><b>与 <see cref="DecisionFlow"/> 的边界</b>：<c>DecisionFlow</c> 用于"持续行动"
///（花 0.25 日干活 / 摸鱼 / 出发），<b>不能</b>直接拿来"购物"——购物是<b>瞬时</b>动作
/// （付钱 + 收物 + 摊位递减 + 村金库入账，<b>不应</b>占 <c>HeroState.BeginAction</c>）。
/// 本类独立守门，<b>不</b>动 <c>DecisionFlow</c> 已有 S1-S6。</para>
///
/// <para><b>三件事</b>：</para>
/// <list type="number">
/// <item>今天买不买（性格权重 + 频率上限 + 随机）</item>
/// <item>买谁的摊位（v1 随机抽 1 个 Open 摊位）</item>
/// <item>买什么（缺啥买啥 + 钱够）</item>
/// </list>
///
/// <para><b>不</b>做什么：</para>
/// <list type="bullet">
/// <item>不画 UI（应用层不管渲染）</item>
/// <item>不决策卖方（卖摊是 M3 外部触发，英雄自动"开摆"属 P3）</item>
/// <item>不跨摊位扫货（v1 一次只看一个）</item>
/// </list>
///
/// <para><b>频率</b>（D-025）：每英雄每天 ≤ 1 次。<c>LastShoppingDay == today</c> → 跳过。</para>
///
/// <para><b>性格权重</b>（D-025）：</para>
/// <list type="bullet">
/// <item>肝：×1.5</item>
/// <item>莽：×1.2</item>
/// <item>随手（默认）：×1.0</item>
/// <item>傲：×0.5</item>
/// <item>挂机：×0.2</item>
/// </list>
/// <para><b>触发</b>：随机 [0,1) &lt; 性格权重 → 买。性格权重 ≥ 1 → 几乎必买；≤ 0.2 → 几乎不买。</para>
/// </remarks>
public sealed class HeroBehaviorService
{
    private readonly StallService _stall;
    private readonly Func<HeroId, HeroAgent?> _heroLookup;
    private readonly Func<double> _random;   // [0,1) 注入的随机源

    /// <summary>
    /// 自主购物的<b>单种物品背包上限</b>（份）。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么它是 5</b>：原型期定下的数字，本轮<b>不变</b>
    /// （D-053：「不取消上限」，只把判据挪到抽样前）。</para>
    ///
    /// <para><b>⚠️ 它只管自主购物这一条路径</b>：英雄背包的<b>领域层容量是 0 = 无限</b>
    /// （<c>HeroAgent.Bag = new Inventory()</c> → <c>Inventory.Capacity = 0</c>，
    /// 于是容量防线 <c>Inventory.TryAdd</c> <b>从不生效</b>）。
    /// 战利品入包、摆摊卖货、玩家手动购买<b>都不受它约束</b>。
    /// 「只有一处」这个现状是 D-053 审计的主要发现 —— 本轮<b>不</b>把它提升到领域层
    /// （那会连带改变战利品发放等范围外路径），但必须记住
    /// <b>它不是一条全局规则</b>。</para>
    /// </remarks>
    public const long BagItemLimit = 5;

    /// <summary>
    /// 背包里该物品<b>还能再装几份</b>（按 <see cref="BagItemLimit"/> 算）。
    /// </summary>
    /// <param name="bag">背包。</param>
    /// <param name="item">物品。</param>
    /// <returns>还能装几份；已满则 0。</returns>
    /// <remarks>
    /// <b>为什么不用 <see cref="StallMarket.BagRoom"/></b>：那个函数按<b>领域层容量</b>
    /// （<c>Inventory.Capacity</c>）算，而领域层容量是 0（无限）→ 它永远返回
    /// <see cref="StallMarket.Unlimited"/>。自主购物要守的是<b>决策层的 5 份上限</b> ——
    /// 两者是不同的尺子，混用会让「上限」名存实亡。
    /// </remarks>
    private static long BagRoom(Inventory bag, ItemId item)
    {
        long room = BagItemLimit - bag.Count(item);
        return room < 0 ? 0 : room;
    }

    /// <summary>购物决策结果（每英雄调用 1 次的产出）。</summary>
    public enum ShoppingOutcome
    {
        /// <summary>今天已购过（频率上限）→ 跳过。</summary>
        Skipped_AlreadyShoppedToday = 0,
        /// <summary>性格权重 + 随机 → 今天不买。</summary>
        Skipped_NoDesire = 1,
        /// <summary>找不到 Open 摊位 → 跳过。</summary>
        Skipped_NoOpenStall = 2,
        /// <summary>钱不够 / 背包满 / 摊位被抢空 → 前置条件不满足。</summary>
        Skipped_PrecheckFailed = 3,
        /// <summary>实际成交：英雄钱包 + Bag 改，摊位改，村金库改。</summary>
        Bought = 4,
    }

    /// <summary>未成交的<b>结构化</b>原因。</summary>
    /// <remarks>
    /// <para><b>为什么要有它</b>：<see cref="ShoppingResult.Detail"/> 是给调试看的<b>长句</b>
    /// （「金币不足：有 3，需 5」），玩家看的花名册行尾放不下它。
    /// 这里给出<b>结构化</b>的原因，好让表现层自己决定：
    /// 哪些原因值得标在花名册上、每天的汇总该怎么措辞。</para>
    ///
    /// <para><b>哪些原因值得给玩家看</b>：<b>他自己想买、却买不成</b>的那些 ——
    /// 钱不够、背包满、结算失败、村里只有自己的摊。
    /// 「今天不想买」「在远征」「已经买过」「村里没摊」<b>不是</b>想买没买成，
    /// 标出来只会制造噪音。</para>
    /// </remarks>
    public enum ShoppingSkipReason
    {
        /// <summary>成交了，没有原因。</summary>
        None = 0,

        /// <summary>不在场或正在忙（随队外出 / 在工地）。</summary>
        NotPresent = 1,

        /// <summary>今天不想买（性格权重 + 随机没过）。</summary>
        NoDesire = 2,

        /// <summary>今天已经买过（频率上限）。</summary>
        AlreadyShopped = 3,

        /// <summary>村里没有开着的摊位。</summary>
        NoOpenStall = 4,

        /// <summary>村里只有<b>自己</b>开着的摊（同一个人不能自买自卖）。</summary>
        OnlyOwnStall = 5,

        /// <summary>背包里该物品已达上限（<see cref="BagItemLimit"/> 份）。</summary>
        /// <remarks>
        /// <b>D-053 后有两种触发路径</b>：抽中的摊该物品已背满（现在不会发生 ——
        /// 抽样前已过滤），或<b>村里所有能买的摊该物品都背满</b>。
        /// </remarks>
        BagFull = 6,

        /// <summary>金币不够。</summary>
        NotEnoughGold = 7,

        /// <summary>结算失败（摊位被抢空 / 背包满 / 钱包不足）。</summary>
        SettlementFailed = 8,
    }

    /// <summary>单次调用的可观测结果（事件流 / 调试输出用得上）。</summary>
    /// <remarks>
    /// <see cref="Detail"/> 是第四阶段 M5 补的：<see cref="ShoppingOutcome.Skipped_PrecheckFailed"/>
    /// 只说明「前置条件不满足」，不说明是哪一条。把具体原因挂在这里，
    /// <c>--econlog</c> / <c>--shop</c> 才能回答「今天为什么没买」。
    /// 它不是给玩家看的常规文本，<b>不进杂谈</b>。
    /// <para>
    /// <see cref="SkipReason"/> 是 R-07 补的：<b>结构化</b>的同一件事，
    /// 供表现层生成给玩家看的短文案（花名册行尾、每日汇总）。
    /// </para>
    /// </remarks>
    public readonly record struct ShoppingResult(
        ShoppingOutcome Outcome,
        HeroId? Buyer,
        HeroId? Seller,
        ItemId? ItemId,
        long? Quantity,
        long? SellerNet,
        long? VillageCut)
    {
        /// <summary>本结果的具体原因（调试用；可为 null）。</summary>
        public string? Detail { get; init; }

        /// <summary>本结果的结构化原因（给表现层用；成交时为 <see cref="ShoppingSkipReason.None"/>）。</summary>
        public ShoppingSkipReason SkipReason { get; init; } = ShoppingSkipReason.None;
    }

    /// <param name="stall">摆摊/买摊应用层。</param>
    /// <param name="heroLookup">按 HeroId 找英雄（含 Personality / Diligence / LastShoppingDay / Bag / Wallet）。</param>
    /// <param name="random">[0,1) 随机源。注入而非全局，固定种子可复现。</param>
    public HeroBehaviorService(
        StallService stall,
        Func<HeroId, HeroAgent?> heroLookup,
        Func<double> random)
    {
        _stall = stall ?? throw new ArgumentNullException(nameof(stall));
        _heroLookup = heroLookup ?? throw new ArgumentNullException(nameof(heroLookup));
        _random = random ?? throw new ArgumentNullException(nameof(random));
    }

    /// <summary>性格权重（D-025 表）。</summary>
    public static double DesireMultiplier(string personality, string diligence)
    {
        double p = personality switch
        {
            "莽" => 1.2,
            "傲" => 0.5,
            _    => 1.0,    // 默认 / 未知 = 1
        };
        double d = diligence switch
        {
            "肝"   => 1.5,
            "挂机" => 0.2,
            _      => 1.0,    // 默认 / 随手 / 未知 = 1
        };
        return p * d;   // 0.1..1.8 区间，最坏 × 最坏 = 0.5×0.2 = 0.1（"傲"+"挂机"几乎不买）
    }

    /// <summary>
    /// 一名英雄一天的购物决策。
    /// </summary>
    /// <remarks>
    /// <b>4 道防线 + 1 个真正动作</b>：
    /// </remarks>
    /// <param name="buyerId">要购物哪位英雄。</param>
    /// <param name="today">当前模拟日号（与 <c>LastShoppingDay</c> 比对）。</param>
    /// <param name="simTime">成交时间（模拟秒）。</param>
    public ShoppingResult SimulateShopping(HeroId buyerId, int today, double simTime)
    {
        if (string.IsNullOrEmpty(buyerId.Value))
        {
            return new ShoppingResult(ShoppingOutcome.Skipped_PrecheckFailed, buyerId, null, null, null, null, null)
            {
                Detail = "买家 Id 为空",
            };
        }

        var buyer = _heroLookup(buyerId);
        if (buyer is null)
        {
            return new ShoppingResult(ShoppingOutcome.Skipped_PrecheckFailed, buyerId, null, null, null, null, null)
            {
                Detail = "找不到这位英雄",
            };
        }

        // 1. 频率上限：今天已购过
        if (buyer.LastShoppingDay == today)
        {
            return new ShoppingResult(ShoppingOutcome.Skipped_AlreadyShoppedToday, buyerId, null, null, null, null, null)
            {
                Detail = $"今天已经买过（第 {today} 天）",
                SkipReason = ShoppingSkipReason.AlreadyShopped,
            };
        }

        // 2. 在场 + 空闲（与 PlaceStall 同步 —— 摆摊前置的镜像）
        if (buyer.Away || buyer.ToSite)
        {
            return new ShoppingResult(ShoppingOutcome.Skipped_PrecheckFailed, buyerId, null, null, null, null, null)
            {
                Detail = "不在场或正在忙（随队外出 / 在工地）",
                SkipReason = ShoppingSkipReason.NotPresent,
            };
        }

        // 3. 性格权重 + 随机
        double desire = DesireMultiplier(buyer.Personality, buyer.Diligence);
        double r = _random();
        if (!(r < desire))   // 与 D-025 "随机 < 性格权重才买" 一致
        {
            return new ShoppingResult(ShoppingOutcome.Skipped_NoDesire, buyerId, null, null, null, null, null)
            {
                Detail = $"随机 {r:0.00} ≥ 意愿 {desire:0.00}",
                SkipReason = ShoppingSkipReason.NoDesire,
            };
        }

        // 4. 找 1 个 Open 摊位（v1 简单：随机抽）
        var openStalls = ListOpenStalls();
        if (openStalls.Count == 0)
        {
            return new ShoppingResult(ShoppingOutcome.Skipped_NoOpenStall, buyerId, null, null, null, null, null)
            {
                Detail = "村里没有开着的摊位",
                SkipReason = ShoppingSkipReason.NoOpenStall,
            };
        }

        // 5. R-03 + D-053：抽样**前**排除两类摊。
        //
        //    ① **自己开的摊**（R-03）：抽到必然被 BuyFromStall 以 SameHero 拒绝 ——
        //       等于白白浪费当天的购物机会（不成交 ⇒ LastShoppingDay 不写）。
        //
        //    ② **背包里该物品已满**的摊（D-053）：旧规则是「抽到才发现背满 → 整笔放弃」，
        //       当天就此白过 —— 村里还有别的摊，英雄却根本没去看。
        //       把判据挪到抽样前，他就会转去买别的材料。
        //
        //    上限**没有取消**（BagItemLimit 仍是 5）：只是不再用它「一票否决」，
        //    而是让它决定「这个摊买不买」「这一笔买几份」。
        var notOwn = new List<DomainStall>(openStalls.Count);
        foreach (var s in openStalls)
        {
            if (s.Owner != buyerId) notOwn.Add(s);
        }

        var buyable = new List<DomainStall>(notOwn.Count);
        int bagFullStalls = 0;
        foreach (var s in notOwn)
        {
            if (BagRoom(buyer.Bag, s.ItemId) < 1)
            {
                bagFullStalls++;
                continue;
            }
            buyable.Add(s);
        }

        if (buyable.Count == 0)
        {
            // 「一个摊都买不了」有两种原因，处置完全不同 —— 不能混报。
            if (notOwn.Count == 0)
            {
                return new ShoppingResult(ShoppingOutcome.Skipped_PrecheckFailed, buyerId, null, null, null, null, null)
                {
                    Detail = "村里只有自己开着的摊（同一个人不能自买自卖）",
                    SkipReason = ShoppingSkipReason.OnlyOwnStall,
                };
            }

            return new ShoppingResult(ShoppingOutcome.Skipped_PrecheckFailed, buyerId, null, null, null, null, null)
            {
                Detail = $"背包里相关物品都已满（上限 {BagItemLimit}）：{bagFullStalls} 个摊都买不了",
                SkipReason = ShoppingSkipReason.BagFull,
            };
        }

        var pickIdx = (int)(_random() * buyable.Count);
        if (pickIdx >= buyable.Count) pickIdx = buyable.Count - 1;   // 边界
        var stall = buyable[pickIdx];

        // 6. 买多少（D-053）：**尽可能多** = min(摊位库存, 余额 ÷ 单价, 背包余量)。
        //    背包余量按 BagItemLimit 算 —— 上限仍在起作用（单次最多 5 份）。
        long gold = buyer.Wallet.Amount(StallService.Gold);
        long room = BagRoom(buyer.Bag, stall.ItemId);      // 抽样前已保证 ≥ 1
        long qty = StallMarket.MaxAffordable(stall.Stock, stall.UnitPrice, gold, room);

        if (qty < 1)
        {
            // 背包装得下（room ≥ 1）、摊也还有货（Open 摊 Stock ≥ 1）——
            // 买不成**只可能是钱不够**。
            return new ShoppingResult(ShoppingOutcome.Skipped_PrecheckFailed, buyerId, stall.Owner, stall.ItemId, null, null, null)
            {
                Detail = $"金币不足：有 {gold}，需 {stall.UnitPrice}",
                SkipReason = ShoppingSkipReason.NotEnoughGold,
            };
        }

        // 7. 真买
        var buyResult = _stall.BuyFromStall(stall.Owner, buyerId, stall.ItemId, qty, simTime);
        if (buyResult is null)
        {
            return new ShoppingResult(ShoppingOutcome.Skipped_PrecheckFailed, buyerId, stall.Owner, stall.ItemId, qty, null, null)
            {
                Detail = "结算失败（摊位被抢空 / 钱包不足）",
                SkipReason = ShoppingSkipReason.SettlementFailed,
            };
        }

        // 频率上限：标记"今天已购"
        buyer.LastShoppingDay = today;

        return new ShoppingResult(
            ShoppingOutcome.Bought,
            buyerId,
            stall.Owner,
            stall.ItemId,
            qty,
            buyResult.SellerNet,
            buyResult.VillageCut)
        {
            Detail = $"买了 {stall.ItemId.Value}×{qty}，付 {buyResult.SellerNet + buyResult.VillageCut} 金币",
        };
    }

    /// <summary>列出所有 Open 摊位（顺序确定：按摊主 Id 排序）。</summary>
    /// <remarks>
    /// <b>为什么用 <c>Snapshot()</c> 而不是直接遍历字典</b>：
    /// <c>Dictionary</c> 的遍历顺序<b>不是契约</b> —— 增删之后顺序会变。
    /// 抽样下标依赖列表顺序，顺序不稳 = 同一个种子的两次运行挑到不同的摊 =
    /// 测试不可复现。<c>Snapshot()</c> 按摊主 Id 排序，顺序稳定。
    /// </remarks>
    private List<DomainStall> ListOpenStalls()
    {
        var list = new List<DomainStall>();
        foreach (var s in _stall.Snapshot())
        {
            if (s.Status == DomainStallStatus.Open) list.Add(s);
        }
        return list;
    }
}
