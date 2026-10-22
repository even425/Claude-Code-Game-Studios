using System;
using System.Collections.Generic;
using System.Linq;
using ClaudeCodeGameStudios.Domain.Items;
using ClaudeCodeGameStudios.Domain.Village;

namespace ClaudeCodeGameStudios.Application;

/// <summary>一次采购里「从谁那里买、买什么、几份、多少钱」。</summary>
/// <param name="Hero">卖方。</param>
/// <param name="Item">物品。</param>
/// <param name="Qty">几份。</param>
/// <param name="UnitPrice">单价（市价）。</param>
public sealed record MaterialPick(HeroAgent Hero, ItemId Item, long Qty, long UnitPrice);

/// <summary>一次采购的计划（<b>只算不改</b>）。</summary>
/// <remarks>
/// ⚠️ **两段式**：先 <see cref="MaterialProcurement.Plan"/> 算出计划，
/// 确认「材料够 + 钱够」之后再 <see cref="MaterialProcurement.Commit"/> 提交
/// ⇒ 与建造的扣款合成<b>同一次提交</b>（I15）⇒ 不会出现「材料被买走了、建造却没开工」。
/// </remarks>
public sealed class ProcurementPlan
{
    /// <summary>逐个卖家的取货明细（按提交的顺序）。</summary>
    public IReadOnlyList<MaterialPick> Picks { get; init; } = Array.Empty<MaterialPick>();

    /// <summary>一共买了几份材料。</summary>
    public long Qty => Picks.Sum(p => p.Qty);

    /// <summary>一共要付多少钱。</summary>
    public long Gold => Picks.Sum(p => p.Qty * p.UnitPrice);

    /// <summary>空计划（没有可买的东西）。</summary>
    public static ProcurementPlan Empty { get; } = new();
}

/// <summary>
/// <b>村庄向村民采购建材</b>（D-091 拍板：「材料有用途 —— 村庄建造/修理优先消耗村民背包里的材料，
/// 付钱给他们」）。
/// </summary>
/// <remarks>
/// <para><b>为什么要有它</b>：标定实测（37 人 / 200 天）英雄金币<b>中位数只有 5~6</b>，
/// 根因<b>不是「缺收入」，是「支出没有出口」</b>：
/// 闲人在市场里买走材料（石料 / 木料 / 布匹）⇒ 材料<b>对他们毫无用途</b>
/// ⇒ 钱白流出去 ⇒ 中位数必然趋近 0。
/// ⇒ 这条规则给那些材料<b>一个出口</b>：村庄要建设 ⇒ 就从村民手里买（按市价付款）
/// ⇒ 闲人买来的材料**变成了村庄的建材**（而不是垃圾），
/// 而**他们的钱变成了村庄建设的一部分**。</para>
///
/// <para><b>付款从哪出</b>：<b>村庄资产</b>（D-090 的池子）优先，
/// 不足时用<b>村库现金里超出「下一级村庄升级所需金币」的部分</b>
/// ⇒ 与 <see cref="VillagePurchaseService"/> 同一套「村庄可动用资金」口径，
/// 且<b>升级的钱永远不动</b>。</para>
///
/// <para>⚠️ <b>「一份材料」就是「一份物品」</b>：建造要 40 份材料
/// ⇒ 40 份任意可售物（石料 / 木料 / 布匹）都算 40 份材料，
/// 但<b>价钱按各自的市价</b>（石料 3 / 木料 2 / 布匹 5）⇒ 同一个「份数」，
/// 村民赚到的钱不一样。</para>
///
/// <para>⚠️ <b>不新造经济</b>：单价来自 <see cref="PriceBook"/>（市价表），
/// 村民的钱进他的钱包，材料进建造 ⇒ <b>总量守恒</b>。</para>
/// </remarks>
public sealed class MaterialProcurement
{
    private readonly Village _village;
    private readonly PriceBook _book;
    private readonly ResourceId _gold;
    private readonly Func<long> _reserveOf;

    /// <param name="village">村庄（付款方）。</param>
    /// <param name="book">价格表（单价 = 市价）。</param>
    /// <param name="gold">金币的资源种类。</param>
    /// <param name="reserveOf">当前要<b>保住</b>的现金（下一级村庄升级所需金币）。</param>
    /// <exception cref="ArgumentNullException">任一依赖为 null。</exception>
    public MaterialProcurement(
        Village village, PriceBook book, ResourceId gold, Func<long> reserveOf)
    {
        _village = village ?? throw new ArgumentNullException(nameof(village));
        _book = book ?? throw new ArgumentNullException(nameof(book));
        _gold = gold;
        _reserveOf = reserveOf ?? throw new ArgumentNullException(nameof(reserveOf));
    }

    /// <summary>村庄现在<b>能动用的钱</b>（资产 + 现金超出保留金的部分）。</summary>
    public long Funds =>
        _village.Get(VillageUpgradeService.Asset)
        + Math.Max(0L, _village.Get(_gold) - _reserveOf());

    /// <summary><b>上一次提交</b>里「从资产出了多少」（回滚时要原路退回）。</summary>
    private long _lastFromAsset;

    /// <summary><b>上一次提交</b>里「从现金出了多少」（回滚时要原路退回）。</summary>
    private long _lastFromCash;

    /// <summary>
    /// 算一份采购计划（<b>只算不改</b>）。
    /// </summary>
    /// <param name="neededQty">最多买几份（= 建造还缺多少材料）。</param>
    /// <param name="heroes">候选卖方（按给定顺序，先到先得）。</param>
    /// <returns>计划（买不到东西时是空计划）。</returns>
    public ProcurementPlan Plan(long neededQty, IEnumerable<HeroAgent> heroes)
    {
        if (neededQty <= 0 || heroes is null) return ProcurementPlan.Empty;

        long funds = Funds;
        var picks = new List<MaterialPick>();
        long taken = 0;
        long spent = 0;

        foreach (var hero in heroes)
        {
            if (hero is null || taken >= neededQty || funds <= 0) continue;

            // 背包里有什么可卖的（按物品名排序 ⇒ 顺序确定 ⇒ 可复现）
            foreach (var (item, count) in hero.Bag.Snapshot().OrderBy(kv => kv.Key.Value, StringComparer.Ordinal))
            {
                if (taken >= neededQty || funds <= 0) break;
                if (count <= 0) continue;

                long unitPrice = _book.PriceOf(item);
                if (unitPrice <= 0) continue;      // 价格表里没有 = 不卖的东西

                long affordable = funds / unitPrice;
                if (affordable <= 0) break;

                long qty = Math.Min(Math.Min(count, neededQty - taken), affordable);
                if (qty <= 0) continue;

                picks.Add(new MaterialPick(hero, item, qty, unitPrice));
                taken += qty;
                spent += qty * unitPrice;
                funds -= qty * unitPrice;
            }
        }

        return new ProcurementPlan { Picks = picks };
    }

    /// <summary>
    /// 提交一份采购计划（<b>扣村庄的钱、拿走货、钱进卖方钱包</b>）。
    /// </summary>
    /// <param name="plan">要提交的计划（用 <see cref="Plan"/> 算出来的）。</param>
    /// <returns>提交成功为 true；失败则<b>什么都不改</b>。</returns>
    /// <remarks>
    /// ⚠️ **失败时状态完全不变**：钱不够 / 货已经不在背包里 ⇒ 直接返回 false，
    /// 不做「扣了一半」的半笔账（I15）。
    /// </remarks>
    public bool Commit(ProcurementPlan plan)
    {
        if (plan is null || plan.Picks.Count == 0) return true;

        // ① 先校验（只算不改）：村庄的钱够吗？
        long total = plan.Gold;
        if (total <= 0) return true;

        long fromAsset = Math.Min(_village.Get(VillageUpgradeService.Asset), total);
        long fromCash = total - fromAsset;
        if (fromCash > Math.Max(0L, _village.Get(_gold) - _reserveOf())) return false;

        // ② 逐个卖方提交（钱包是唯一的写者：钱进卖方口袋、货离袋）
        //
        // ⚠️ **同一个若师可能出现多次**（他背包里有多种物品）
        // ⇒ 第二笔必须**接着第一笔的新钱包算**，
        // 否则会用「原钱包」覆盖掉第一笔的钱
        // ⇒ **钱平白消失**（实测：145 金只到账 44 金，守恒律当天就对不上了）。
        var committed = new Dictionary<ClaudeCodeGameStudios.Domain.Heroes.HeroId, (Wallet Wallet, Inventory Bag)>();
        long paid = 0;

        foreach (var pick in plan.Picks)
        {
            var baseState = committed.TryGetValue(pick.Hero.Id, out var st)
                ? st
                : (pick.Hero.Wallet, pick.Hero.Bag);

            var newBag = baseState.Bag.TryRemove(pick.Item, pick.Qty);
            if (newBag is null) return false;    // 货不在了 ⇒ 整体不提交

            var newWallet = baseState.Wallet.TryAdd(_gold, pick.Qty * pick.UnitPrice);
            if (newWallet is null) return false;

            committed[pick.Hero.Id] = (newWallet, newBag);
            paid += pick.Qty * pick.UnitPrice;
        }

        // ③ 村庄付款（资产优先，再动现金）
        if (fromAsset > 0 && !_village.TrySpend(VillageUpgradeService.Asset, fromAsset)) return false;
        if (fromCash > 0 && !_village.TrySpend(_gold, fromCash)) return false;
        _lastFromAsset = fromAsset;
        _lastFromCash = fromCash;

        // ④ 写回卖方（不可失败的赋值）
        foreach (var pick in plan.Picks)
        {
            var state = committed[pick.Hero.Id];
            pick.Hero.ApplyTradeResult(state.Wallet, state.Bag);
        }

        return true;
    }

    /// <summary>
    /// <b>撤销一次已提交的采购</b>（D-091：建造最终没开工时用）。
    /// </summary>
    /// <param name="plan">当初提交的那份计划。</param>
    /// <remarks>
    /// ⚠️ **为什么要退**：货已经离开村民的背包、钱已经进了他们的钱包
    /// ⇒ 若建造最终没成立（落点被占等），就必须把<b>货还回去、钱收回来</b>
    /// ⇒ 否则就是「白拿人家的货」⇒ 村民的钱和货都没了。
    /// </remarks>
    public void Rollback(ProcurementPlan plan)
    {
        if (plan is null || plan.Picks.Count == 0) return;

        // ⚠️ **同一个若常可能出现多次** ⇒ 同样要接着上一笔的状态（否则会收重钱）。
        var state = new Dictionary<ClaudeCodeGameStudios.Domain.Heroes.HeroId, (Wallet Wallet, Inventory Bag)>();

        foreach (var pick in plan.Picks)
        {
            var baseState = state.TryGetValue(pick.Hero.Id, out var st)
                ? st
                : (pick.Hero.Wallet, pick.Hero.Bag);

            // 钱收回来（钱包是唯一的写者）
            var newWallet = baseState.Wallet.TrySpend(_gold, pick.Qty * pick.UnitPrice);
            if (newWallet is null) continue;

            // 货还回去
            var newBag = baseState.Bag.TryAdd(pick.Item, pick.Qty);
            if (newBag is null) continue;

            state[pick.Hero.Id] = (newWallet, newBag);
        }

        foreach (var pick in plan.Picks)
        {
            if (!state.TryGetValue(pick.Hero.Id, out var st)) continue;
            pick.Hero.ApplyTradeResult(st.Wallet, st.Bag);
        }

        // 村庄那边把钱**原路**退回来（资产那一部分回资产，现金那一部分回现金）
        if (_lastFromAsset > 0) _village.Add(VillageUpgradeService.Asset, _lastFromAsset);
        if (_lastFromCash > 0) _village.Add(_gold, _lastFromCash);
        _lastFromAsset = 0;
        _lastFromCash = 0;
    }
}
