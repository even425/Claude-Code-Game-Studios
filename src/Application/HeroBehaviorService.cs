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

    /// <summary>购物决策结果（每英雄调用 1 次的产出）。</summary>
    public enum ShoppingOutcome
    {
        /// <summary>今天已购过（频率上限）→ 跳过。</summary>
        Skipped_AlreadyShoppedToday = 0,
        /// <summary>性格权重 + 随机 → 今天不买。</summary>
        Skipped_NoDesire = 1,
        /// <summary>找不到 Open 摊位 → 跳过。</summary>
        Skipped_NoOpenStall = 2,
        /// <summary>钱不够 / 背包满 / 摊位被抢空 → 调 <c>BuyFromStall</c> 返 null。</summary>
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

        /// <summary>背包里该物品已达上限（5 份）。</summary>
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

        // 5. R-03：抽样时**排除自己开的摊**。
        //    抽到自己的摊必然被 BuyFromStall 以 SameHero 拒绝 ——
        //    等于白白浪费当天的购物机会（不成交 ⇒ LastShoppingDay 不写）；
        //    村里只有他自己开摊时，更是**永远**抽到自己的摊、永远买不成。
        //    在这里剔除，英雄才会去买别人的摊。
        var buyable = new List<DomainStall>(openStalls.Count);
        foreach (var s in openStalls)
        {
            if (s.Owner != buyerId) buyable.Add(s);
        }

        if (buyable.Count == 0)
        {
            return new ShoppingResult(ShoppingOutcome.Skipped_PrecheckFailed, buyerId, null, null, null, null, null)
            {
                Detail = "村里只有自己开着的摊（同一个人不能自买自卖）",
                SkipReason = ShoppingSkipReason.OnlyOwnStall,
            };
        }

        var pickIdx = (int)(_random() * buyable.Count);
        if (pickIdx >= buyable.Count) pickIdx = buyable.Count - 1;   // 边界
        var stall = buyable[pickIdx];

        // 6. 选物品：摊位上的 ItemId，且 buyer 背包里数量 < 5
        if (buyer.Bag.Count(stall.ItemId) >= 5)
        {
            return new ShoppingResult(ShoppingOutcome.Skipped_PrecheckFailed, buyerId, stall.Owner, stall.ItemId, null, null, null)
            {
                Detail = $"背包里已有 {buyer.Bag.Count(stall.ItemId)} 份{stall.ItemId.Value}（上限 5）",
                SkipReason = ShoppingSkipReason.BagFull,
            };
        }

        // 7. 钱够吗？一次买 1 份
        if (buyer.Wallet.Amount(StallService.Gold) < stall.UnitPrice)
        {
            return new ShoppingResult(ShoppingOutcome.Skipped_PrecheckFailed, buyerId, stall.Owner, stall.ItemId, null, null, null)
            {
                Detail = $"金币不足：有 {buyer.Wallet.Amount(StallService.Gold)}，需 {stall.UnitPrice}",
                SkipReason = ShoppingSkipReason.NotEnoughGold,
            };
        }

        // 8. 真买
        var buyResult = _stall.BuyFromStall(stall.Owner, buyerId, stall.ItemId, 1, simTime);
        if (buyResult is null)
        {
            return new ShoppingResult(ShoppingOutcome.Skipped_PrecheckFailed, buyerId, stall.Owner, stall.ItemId, 1, null, null)
            {
                Detail = "结算失败（摊位被抢空 / 背包满 / 钱包不足）",
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
            1L,
            buyResult.SellerNet,
            buyResult.VillageCut)
        {
            Detail = $"买了 {stall.ItemId.Value}×1，付 {buyResult.SellerNet + buyResult.VillageCut} 金币",
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
