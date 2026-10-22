using System;
using System.Collections.Generic;
using ClaudeCodeGameStudios.Domain.Heroes;
using ClaudeCodeGameStudios.Domain.Items;
using ClaudeCodeGameStudios.Domain.Village;

namespace ClaudeCodeGameStudios.Application;

/// <summary>
/// 一笔「村庄收购」的记录（<b>已成交</b>的那些）。</summary>
/// <param name="Hero">被收购的英雄（卖方）。</param>
/// <param name="ItemId">被收购的物品。</param>
/// <param name="Qty">收了几份。</param>
/// <param name="Amount">付给英雄多少钱。</param>
public readonly record struct VillagePurchaseRecord(
    HeroId Hero,
    string ItemId,
    long Qty,
    long Amount);

/// <summary>
/// <b>村庄收购站</b>（D-087）：村庄每天按<b>市价</b>收购在村英雄的<b>采集品</b>，
/// 钱从村库出、回到英雄钱包。
/// </summary>
/// <remarks>
/// <para><b>为什么要有它</b>（D-087 拍板）：标定实测（37 人 / 200 天）
/// 英雄金币<b>中位数只有 4</b>、最高 475 ⇒ 「没参与副本的人只有支出、没有产出」：
/// 他们的钱在市场里买走材料，而材料没有用途 ⇒ 钱只是从一个人的口袋挪到另一个人的口袋，
/// <b>总量没变，分布更糟</b>。
/// ⇒ 收购站给「不参与副本的人」一条<b>不需要靠市场成交</b>的收入：
/// 村庄是买方，钱从村库流回英雄 ⇒ 分布被拉平。</para>
///
/// <para><b>为什么只收「采集品」</b>：村库材料已经过剩（实测 7.5 万份），
/// 村里并不缺材料；缺的是<b>钱</b>。而如果连副本掉落物一起收，
/// 摊位市场会被村庄挤掉 ⇒ 5% 摊位税（村库唯一的稳定进项）消失 ⇒
/// <b>村库更没钱</b> ⇒ 反而更发不出钱。
/// ⇒ 只收「采集来的」那一份（<see cref="GatherService.GatheredItemId"/>），
/// 副本掉落物仍然走市场。</para>
///
/// <para><b>预算规则（D-087 拍板，D-090 补燃料）</b>：<b>村库余额 − 下一级村庄升级所需金币</b>，
/// <b>再加上村庄资产</b>（D-090：升级花掉的钱不再是「销毁」，而是变成村庄的资产 ⇒ 可以再花）。
/// ⇒ 村庄发展不被抽空：现金里永远留着下一级升级的钱；
/// 而<b>升级沉淀下来的资产</b>就是收购站的燃料
/// （否则两条规则抢同一笔钱 ⇒ 实测 200 天只开张 2~5 笔）。已到最高级 ⇒ 没有「下一级」⇒ 保留金为 0。</para>
///
/// <para><b>不发明数值</b>：本类里<b>没有任何一个经济数值</b> ——
/// 单价来自价格表（<see cref="PriceBook"/>），保留金来自升级成本表
/// （<see cref="VillageUpgradeService"/>）⇒ 开发者改一处，两处同时变。</para>
///
/// <para><b>钱没有消失</b>：收购是<b>村库 → 英雄</b>的转移，
/// 总量不变 ⇒ 守恒律（流通口径）照样成立；
/// 材料是<b>英雄背包 → 村库材料池</b>的转移 ⇒ 材料总量也不变。</para>
/// </remarks>
public sealed class VillagePurchaseService
{
    /// <summary>被收购的物品（= 采集品；D-086 的采集物）。</summary>
    public const string PurchasedItemId = GatherService.GatheredItemId;

    private readonly Village _village;
    private readonly PriceBook _book;
    private readonly ResourceId _gold;
    private readonly ResourceId _material;
    private readonly VillageUpgradeService _upgrades;
    private readonly Func<int> _tierOf;

    /// <summary>每个英雄<b>待售</b>的采集品份数（还没被村庄收走的）。</summary>
    private readonly Dictionary<HeroId, long> _pending = new();

    /// <param name="village">村庄（村库金币与材料池都在它身上）。</param>
    /// <param name="book">价格表（收购价 = 市价）。</param>
    /// <param name="gold">金币的资源种类。</param>
    /// <param name="material">村庄材料池的资源种类。</param>
    /// <param name="upgrades">村庄升级服务（取「下一级要多少金币」）。</param>
    /// <param name="tierOf">当前村庄层级的读取口。</param>
    /// <exception cref="ArgumentNullException">任一依赖为 null。</exception>
    public VillagePurchaseService(
        Village village,
        PriceBook book,
        ResourceId gold,
        ResourceId material,
        VillageUpgradeService upgrades,
        Func<int> tierOf)
    {
        _village = village ?? throw new ArgumentNullException(nameof(village));
        _book = book ?? throw new ArgumentNullException(nameof(book));
        _gold = gold;
        _material = material;
        _upgrades = upgrades ?? throw new ArgumentNullException(nameof(upgrades));
        _tierOf = tierOf ?? throw new ArgumentNullException(nameof(tierOf));
    }

    /// <summary>收购价（= 市价；0 表示价格表里没登记这件物品 ⇒ 不收购）。</summary>
    public long UnitPrice => _book.PriceOf(new ItemId(PurchasedItemId));

    /// <summary>
    /// 「下一级村庄升级」要留住的<b>金币</b>（保留金）。
    /// 已到最高级 ⇒ 没有下一级 ⇒ 0。
    /// </summary>
    public long Reserve
    {
        get
        {
            int tier = _tierOf();
            if (tier < 0 || tier >= VillageUpgradeService.MaxTier) return 0L;
            return _upgrades.CostFor(tier).Gold;
        }
    }

    /// <summary>
    /// 村庄资产（D-090）—— 升级沉淀下来的钱，收购站花的就是它。
    /// </summary>
    private static readonly ResourceId Asset = VillageUpgradeService.Asset;

    /// <summary>今天最多能花多少钱收购（资产 + 现金里超出保留金的部分；不小于 0）。</summary>
    public long Budget => _village.Get(Asset) + CashRoom;

    /// <summary>村库里<b>可以动用的现金</b>（余额 − 保留金；不小于 0）。</summary>
    public long CashRoom => Math.Max(0L, _village.Get(_gold) - Reserve);

    /// <summary>最近一次收购轮次所在的游戏日（0 = 还没跑过）。</summary>
    public int LastRunDay { get; private set; }

    /// <summary>某个英雄还剩多少份采集品<b>待售</b>。</summary>
    /// <param name="hero">英雄。</param>
    /// <returns>份数；没有就是 0。</returns>
    public long PendingFor(HeroId hero) => _pending.TryGetValue(hero, out long n) ? n : 0L;

    /// <summary>
    /// 记一笔「这个人今天采到了几份」（⇒ 下一轮村庄收购的对象）。
    /// </summary>
    /// <param name="hero">采集者。</param>
    /// <param name="amount">份数（≤ 0 时什么都不记）。</param>
    /// <remarks>
    /// ⚠️ **为什么单独记一份「待售」**：背包里**分不出**「采集来的石料」与
    /// 「副本掉的石料」（都是同一个 <c>ItemId</c>）⇒ 若村庄照单全收，
    /// 副本掉落物也会被收走 ⇒ 摊位市场被挤掉 ⇒ 5% 摊位税消失（见类型注释）。
    /// ⇒ 「待售」是<b>采集这一侧</b>记的账，收购只认它。
    /// </remarks>
    public void NoteGathered(HeroId hero, long amount)
    {
        if (amount <= 0) return;
        if (string.IsNullOrEmpty(hero.Value)) return;
        _pending[hero] = PendingFor(hero) + amount;
    }

    /// <summary>
    /// 跑一轮收购（<b>每天一次</b>）。
    /// </summary>
    /// <param name="day">当前游戏日（同一个日号只跑一次）。</param>
    /// <param name="heroes">全村英雄（不在村的会被跳过）。</param>
    /// <returns>本轮成交的记录（可能为空）。</returns>
    /// <remarks>
    /// <para>⚠️ **顺序很要紧**：收购发生在<b>摆摊之前</b> ——
    /// 否则采集品已经被挪进摊位（背包里没了），村庄就收不到。
    /// ⇒ 「采集 → 村庄收购 → 摆摊 → 每日购物」。</para>
    ///
    /// <para>⚠️ **不在村的英雄跳过**（他的采集品留着，下次再收）——
    /// 副本能带回一堆东西，但那是<b>掉落</b>不是<b>采集</b>，不归收购站管。</para>
    ///
    /// <para>⚠️ **预算在整轮内共享**：村库的钱是<b>一个池子</b>，
    /// 先到先得（英雄按花名册顺序）⇒ 不会超发。</para>
    /// </remarks>
    public IReadOnlyList<VillagePurchaseRecord> RunRound(int day, IEnumerable<HeroAgent> heroes)
    {
        var records = new List<VillagePurchaseRecord>();
        if (heroes is null) return records;
        if (day == LastRunDay) return records;   // 同一天不跑第二轮

        long budget = Budget;
        long price = UnitPrice;
        var item = new ItemId(PurchasedItemId);

        foreach (var hero in heroes)
        {
            if (hero is null) continue;
            if (hero.Away) continue;                       // 不在村 ⇒ 留着下次
            if (price <= 0) break;                         // 价格表里没这件物品 ⇒ 整轮不收

            long pending = PendingFor(hero.Id);
            if (pending <= 0) continue;

            long inBag = hero.Bag.Count(item);
            if (inBag <= 0)
            {
                // 背包里已经没有采集品了（卖掉 / 用掉）⇒ 待售账作废。
                _pending.Remove(hero.Id);
                continue;
            }

            long qty = Math.Min(pending, inBag);

            long affordable = budget / price;
            long buyQty = Math.Min(qty, affordable);
            if (buyQty <= 0) continue;   // 预算不够 ⇒ **保留待售账**，明天再收

            long amount = buyQty * price;
            if (!TrySettle(hero, item, buyQty, amount)) continue;

            // 只扣掉**买走的**那几份 ⇒ 没买走的留在待售账里，等预算够了再收。
            // ⚠️ **不能在这里把待售账清空**：村库的钱常常不够（预算 = 资产 + 现金余量），
            // 一清空，那些采集品就**永远**没有第二次被收的机会
            // （实测：预算不足的那 200 天里，收购站只开张 23 笔 —— 绝大多数待售品被「扔掉」了）。
            long left = pending - buyQty;
            if (left > 0) _pending[hero.Id] = left;
            else _pending.Remove(hero.Id);

            budget -= amount;
            records.Add(new VillagePurchaseRecord(hero.Id, PurchasedItemId, buyQty, amount));
        }

        LastRunDay = day;
        return records;
    }

    /// <summary>
    /// 结算一笔收购（<b>I15：要么全成，要么什么都不改</b>）。
    /// </summary>
    /// <param name="hero">卖方。</param>
    /// <param name="item">物品。</param>
    /// <param name="qty">份数。</param>
    /// <param name="amount">付的钱。</param>
    /// <returns>成交了？</returns>
    /// <remarks>
    /// <para><b>四步</b>：① 算新背包（不提交）② 算新钱包（不提交）
    /// ③ <b>村库先动</b>（材料入池 + 资产/现金出账）④ 写回英雄的背包与钱包。</para>
    ///
    /// <para>⚠️ **材料入池放最前面**：<c>Village.Add</c> 用 checked 算术，
    /// 池子接近上限时会抛 ⇒ 抛的时候<b>钱还没出账、英雄还没拿到钱</b> ⇒ 没有半笔账。
    /// （反过来先扣钱再加材料，抛出来就是「钱扣了、货没到」。）</para>
    ///
    /// <para>⚠️ **钱从两个池子里出（D-090）**：先出<b>现金</b>里超出保留金的部分
    /// （那一部分是真正的闲钱），不够再出<b>村庄资产</b>（升级沉淀下来的那部分）。
    /// ⇒ 现金里<b>永远留着下一级升级的钱</b> ⇒ 村庄发展不会被抽空。</para>
    ///
    /// <para>⚠️ **出账不可能失败**（余额都已校验过，而材料入池不动钱）⇒ 万一它返回
    /// false（不可达），结果是「村庄白得一份材料、英雄没拿到钱」—— <b>钱没有凭空消失</b>。</para>
    /// </remarks>
    private bool TrySettle(HeroAgent hero, ItemId item, long qty, long amount)
    {
        var newBag = hero.Bag.TryRemove(item, qty);
        if (newBag is null) return false;

        var newWallet = hero.Wallet.TryAdd(_gold, amount);
        if (newWallet is null) return false;

        // ① 材料进村库（唯一可能抛的一步：checked 溢出）
        _village.Add(_material, qty);

        // ② 钱出账：先现金（超出保留金的部分），再资产
        long fromCash = Math.Min(amount, CashRoom);
        long fromAsset = amount - fromCash;
        if (_village.Get(Asset) < fromAsset) return false;   // 理论不可达（预算已算过）
        if (fromAsset > 0 && !_village.TrySpend(Asset, fromAsset)) return false;
        if (fromCash > 0 && !_village.TrySpend(_gold, fromCash)) return false;

        // ③ 写回英雄（不可失败的赋值）
        hero.ApplyTradeResult(newWallet, newBag);
        return true;
    }
}
