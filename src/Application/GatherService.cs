using System;
using System.Collections.Generic;
using ClaudeCodeGameStudios.Domain.Items;

namespace ClaudeCodeGameStudios.Application;

/// <summary>
/// <b>采集与自动卖货</b>（D-086 拍板：「自主采集：每天 1 份材料」）。
/// </summary>
/// <remarks>
/// <para><b>为什么要它</b>：标定实测（37 人 / 200 天）发现
/// <b>钱高度集中在 8 个人身上</b>（英雄金币中位只有 6 金，639 次自主购物失败
/// 且<b>全是「真没钱」</b>）⇒ 根因：<b>收入只来自副本</b>，而每场只有 2 人参与
/// ⇒ 35 个人永远没有收入。</para>
///
/// <para>⚠️ <b>为什么「采集」早就存在却没用</b>：采集活动一直在 `DailyLife` 的活动集里，
/// 但它的材料进的是<b>村庄的材料池</b>（⇒ 村庄材料已经过剩到 7.5 万份）
/// ⇒ <b>英雄自己拿不到</b> ⇒ 卖不了 ⇒ <b>不构成个人收入</b>；
/// 而且英雄的<b>自主摆摊</b>也不在活动集里 ⇒ 就算材料进了背包，也<b>没有自动变现的路径</b>。</para>
///
/// <para>⇒ 本类补上那两截：<b>① 采集的材料进英雄自己的背包</b>；
/// <b>② 有货就自动开摊</b>（复用现成的摆摊规则：价格表定价 + 5% 摊位费
/// ⇒ <b>不新造经济</b>）。</para>
///
/// <para>⚠️ <b>D-105</b>：采集产量受村庄的<b>采集点</b>影响（井 = 采集点，
/// 开发者拍板）⇒ 每有一个采集点，采集者每天<b>多采 1 份</b>。
/// ⇒ 加成用<b>委托</b>注入（每次现问村庄）⇒ 升级 / 读档后自动生效。</para>
/// </remarks>
public sealed class GatherService
{
    /// <summary>
    /// 采集一次得到的物品（D-086：<b>石料</b>）。
    /// </summary>
    /// <remarks>
    /// ⚠️ **为什么是石料**：它是最基础的材料（价格表里 3 金）⇒
    /// 「每天 1 份」≈ 3 金/天，相对副本（真实玩法约 10 金/天）约占三成 ——
    /// 足以让没参战的人有钱可花，又不会把副本挤掉。
    /// <para>⚠️ **不能是布匹**（最贵）⇒ 采集会变成印钞机。</para>
    /// </remarks>
    public const string GatheredItemId = "石料";

    /// <summary>采集一次得到的份数（D-086 拍板：<b>每天 1 份</b>）。</summary>
    public const long GatheredAmountPerDay = 1;

    /// <summary>一个摊一次摆多少份（留出多次成交的余地）。</summary>
    public const long StallQuantityPerOpen = 3;

    private readonly Stall.StallService _stalls;
    private readonly PriceBook _book;
    private readonly Func<long> _gatheringBonus;

    /// <summary>构造采集服务。</summary>
    /// <param name="stalls">摆摊服务（开摊与成交都走它 ⇒ 规则只有一份）。</param>
    /// <param name="book">价格表（哪些能卖、卖多少钱）。</param>
    /// <param name="gatheringBonus">
    /// 村庄给的<b>采集加成</b>（D-105：每个采集点 +N 份/天）。
    /// <b>可为 null</b>（等价于没有采集点）。
    /// </param>
    /// <remarks>
    /// ⚠️ <b>为什么用委托而不是直接把村庄塞进来</b>：
    /// 采集服务要的是「<b>现在</b>有多少加成」⇒ 委托每次现问 ⇒
    /// 升级 / 读档 / 建筑被拆之后<b>自动跟着变</b>，
    /// 而缓存一个数会在村庄变化后变成<b>过期值</b>（那是很难查的一类错）。
    /// </remarks>
    public GatherService(Stall.StallService stalls, PriceBook book, Func<long>? gatheringBonus = null)
    {
        _stalls = stalls ?? throw new ArgumentNullException(nameof(stalls));
        _book = book ?? throw new ArgumentNullException(nameof(book));
        _gatheringBonus = gatheringBonus ?? (() => 0L);
    }

    /// <summary>
    /// 采集一次：材料进<b>采集者自己的背包</b>（不进村庄材料池）。
    /// </summary>
    /// <param name="hero">采集者。</param>
    /// <returns>采到几份（<b>背包满了就是 0</b> —— 采回来也放不下）。</returns>
    /// <remarks>
    /// ⚠️ **背包满了 ⇒ 白采**：背包容量（5 份/种）是既有规则
    /// ⇒ 采到放不下就是这一天的劳动白费 ⇒ 这也正是「卖掉才能再采」的压力来源。
    /// </remarks>
    public long Gather(HeroAgent hero)
    {
        if (hero is null) throw new ArgumentNullException(nameof(hero));

        var item = new ItemId(GatheredItemId);

        // ⚠️ **上限用「决策层那把尺子」**（`BagItemLimit` = 5 份/种），
        // **不是** `Inventory.Capacity` —— 后者是 **0（无限）**
        // （D-053 审计的发现：容量防线在领域层**从不生效**）。
        // ⇒ 用错尺子的话，「背包满了就白采」这条规则**根本不会触发**。
        //
        // ⚠️ **D-105：有采集点（井）就多采一些** —— 但背包**装多少就是多少**
        // ⇒ 装不下的那部分**采不到**（不是丢了，是根本没采上来）。
        long room = Stall.HeroBehaviorService.BagItemLimit - hero.Bag.Count(item);
        if (room <= 0) return 0L;

        long wanted = GatheredAmountPerDay + BonusFromGatheringPoints();
        long amount = wanted < room ? wanted : room;

        var bag = hero.Bag.TryAdd(item, amount);
        if (bag is null) return 0L;

        hero.ApplyTradeResult(hero.Wallet, bag);
        return amount;
    }

    /// <summary>
    /// 村庄给的采集加成（D-105：每个采集点 +1 份/天）。
    /// </summary>
    /// <returns>加成的份数（永远 ≥ 0 —— 负数没有合法来源）。</returns>
    /// <remarks>
    /// ⚠️ <b>负数一律当 0</b>：加成的来源是「有几个采集点」⇒ 不会是负数
    /// ⇒ 出现负数只可能是委托被写坏 ⇒ **不把它变成「倒扣材料」**。
    /// </remarks>
    private long BonusFromGatheringPoints()
    {
        long bonus = _gatheringBonus();
        return bonus > 0 ? bonus : 0L;
    }

    /// <summary>
    /// <b>有货就自动开摊</b>（让背包里的材料能变现）。
    /// </summary>
    /// <param name="hero">摊主。</param>
    /// <param name="day">当前游戏日（用于「今天卖哪一样」的轮换）。</param>
    /// <param name="heroIndex">英雄在花名册里的序号（同样用于轮换）。</param>
    /// <param name="simTime">当前模拟时间（开摊时间戳）。</param>
    /// <returns>开成了吗。</returns>
    /// <remarks>
    /// <para>⚠️ **「今天卖哪一样」按日轮换**（沿用采集器原有的做法）：
    /// 背包里的可售物按 <c>(日序 + 英雄序) % 可售数</c> 取一样。
    /// ⇒ 不这么做的话，谁的背包里先有布匹谁就一直卖布匹 ⇒ <b>整个市场只剩一种商品</b>
    /// （这不是采集器的毛病，是规则本身的毛病 ⇒ 所以把它<b>升为游戏规则</b>）。</para>
    /// <para>⚠️ **一人一摊**（D-023）：已有未收的摊就跳过。</para>
    /// </remarks>
    public bool OpenStallFor(HeroAgent hero, int day, int heroIndex, double simTime)
    {
        if (hero is null) throw new ArgumentNullException(nameof(hero));
        if (hero.Away || hero.ToSite) return false;

        var existing = _stalls.GetStall(hero.Id);
        if (existing is not null)
        {
            if (existing.Status == ClaudeCodeGameStudios.Domain.Stall.StallStatus.Open
                && existing.Stock > 0)
            {
                // 摊上还有货 ⇒ 不动它（一人一摊，D-023）。
                return false;
            }

            // ⚠️ **卖光了的摊要收掉重开**（这是采集器原来就有、我在「统一实现」时漏掉的一步）：
            // 不收掉 ⇒ 摊子永远是空的 ⇒ **市场直接停摆**
            // （实测：成交从 4867 笔掉到 37 笔 ⇒ 英雄们没货可买 ⇒ 钱全囤在钱包里）。
            _stalls.CloseStall(hero.Id);
        }

        var sellable = new List<ItemId>();
        foreach (var (item, count) in hero.Bag.Snapshot())
        {
            if (_book.IsSellable(item) && count >= 1) sellable.Add(item);
        }

        if (sellable.Count == 0) return false;

        var chosen = sellable[(day + heroIndex) % sellable.Count];
        long qty = Math.Min(StallQuantityPerOpen, hero.Bag.Count(chosen));

        return _stalls.PlaceStall(hero.Id, chosen, qty, simTime) is not null;
    }
}
