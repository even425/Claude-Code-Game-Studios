using System;
using System.Collections.Generic;
using ClaudeCodeGameStudios.Domain.Heroes;
using ClaudeCodeGameStudios.Domain.Items;
using ClaudeCodeGameStudios.Domain.Settlement;
using ClaudeCodeGameStudios.Domain.Village;

namespace ClaudeCodeGameStudios.Application;

/// <summary>
/// 副本金币报酬的<b>规则与发放器</b>（D-054-01）。
/// </summary>
/// <remarks>
/// <para><b>为什么需要它</b>：<c>Settlement</c> 的两个既有写入委托分别通向
/// <b>村庄资源池</b>（<c>resourceWriter</c>）与<b>英雄背包</b>（<c>IItemGrantWriter</c>）——
/// <b>都没有通向英雄钱包</b>。而 D-054-01 定了「副本金币按实际参战发放」，
/// 这条钱必须<b>进英雄自己的钱包</b>，不能混进村庄资源池（那是另一个池子）。
/// 所以需要第三个写入入口。</para>
///
/// <para><b>零头怎么算</b>（D-054 阶段 4 的拍板）：<b>每个英雄各自独立累计</b>。
/// 报酬率 X 可以是分数（金币的最小单位是 1/100 金）：
/// <c>X = 0.5</c> 时，某英雄攒够 1 金才发一次（每两天 1 金）。
/// <b>攒在谁那儿</b>：每个英雄一份 —— 全局攒零头会让**先轮到的英雄**拿走整金、
/// 后面的英雄吃亏，那不是「每人每次 X 金」。
/// <b>为什么不销毁</b>：零头是<b>已经挣到、只是还没发出去</b>的钱，
/// 读档后丢掉它等于让那部分钱凭空消失 —— 所以它<b>必须跟着存档走</b>（存档 0.6）。</para>
///
/// <para><b>它守什么</b>：英雄存在、钱包装得下（容量 / 溢出）；
/// 任何一项不合法都<b>不修改任何钱包</b>（<c>Wallet.TryAdd</c> 本身全有或全无）。</para>
///
/// <para><b>它不做什么</b>：不产生事件、不决定金额规则（金额由 <c>SettlementDelta</c> 决定）、
/// 不直接改 <c>Wallet</c> 内部字典 —— 只调 <c>HeroAgent.RestoreWallet</c>。</para>
/// </remarks>
public sealed class WalletGrantService : IWalletGrantWriter
{
    /// <summary>每人每次的报酬率（单位：<b>1/100 金</b>）。</summary>
    /// <remarks>
    /// <b>为什么用 1/100 金</b>：金币是整数，而标定需要的 X 是分数（如 0.5）。
    /// 用 1/100 金做最小单位，X 就能取到 0.01 精度。
    /// </remarks>
    private readonly long _rewardUnits;

    /// <summary>每个英雄的<b>待发零头</b>（单位 1/100 金，范围 [0, 99]）。</summary>
    /// <remarks>
    /// <b>为什么是「每英雄一份」</b>：全局攒零头会让先轮到的英雄拿走整金 ——
    /// 那就不是「每人每次 X 金」了。
    /// </remarks>
    private readonly Dictionary<HeroId, long> _carry = new();

    /// <param name="heroLookup">按 HeroId 查英雄。</param>
    /// <param name="resource">发放的<b>资源种类</b>（正式路径是「金币」）。</param>
    /// <param name="rewardUnits">
    /// 每人每次的报酬率（单位 1/100 金）。<b>注入而不是写死</b>：领域层不认识
    /// 「金币」与「报酬率」这两个业务身份，写死会破坏分层。
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="heroLookup"/> 为 null。</exception>
    /// <exception cref="ArgumentException">资源种类为空，或报酬率为负。</exception>
    public WalletGrantService(
        Func<HeroId, HeroAgent?> heroLookup, ResourceId resource, long rewardUnits)
    {
        _heroLookup = heroLookup ?? throw new ArgumentNullException(nameof(heroLookup));
        if (string.IsNullOrEmpty(resource.Value))
        {
            throw new ArgumentException("资源种类不能为空。", nameof(resource));
        }

        if (rewardUnits < 0)
        {
            throw new ArgumentException("报酬率不得为负。", nameof(rewardUnits));
        }

        _resource = resource;
        _rewardUnits = rewardUnits;
    }

    private readonly Func<HeroId, HeroAgent?> _heroLookup;
    private readonly ResourceId _resource;

    /// <summary>发放的资源种类（只读）。</summary>
    public ResourceId Resource => _resource;

    /// <summary>每人每次的报酬率（单位 1/100 金，只读）。</summary>
    public long RewardUnits => _rewardUnits;

    /// <summary>
    /// 累计<b>发放</b>出去的金币（整数金；守恒律的「累计新增」）。
    /// </summary>
    public long TotalGranted { get; private set; }

    /// <summary>
    /// 累计<b>待发</b>的零头（单位 1/100 金）—— 已挣到但还没进钱包的钱。
    /// </summary>
    /// <remarks>
    /// 它**不在任何钱包里**，所以守恒律的「已入账的钱」那一式不含它；
    /// 但它**是一笔应收**，必须单独记（也要跟着存档走）。
    /// </remarks>
    public long TotalPending => Sum(_carry.Values);

    /// <summary>某英雄当前的待发零头（单位 1/100 金；没有则 0）。</summary>
    /// <param name="hero">英雄。</param>
    public long PendingOf(HeroId hero) => _carry.TryGetValue(hero, out var c) ? c : 0L;

    /// <summary>
    /// 恢复某英雄的待发零头 —— <b>仅供读档</b>。
    /// </summary>
    /// <param name="hero">英雄。</param>
    /// <param name="units">待发零头（单位 1/100 金），必须落在 [0, 99]。</param>
    /// <exception cref="ArgumentOutOfRangeException">零头超出合法范围。</exception>
    public void RestorePending(HeroId hero, long units)
    {
        if (units < 0 || units >= 100)
        {
            throw new ArgumentOutOfRangeException(
                nameof(units), units, "待发零头必须落在 [0, 99]（单位 1/100 金）。");
        }

        if (units == 0) { _carry.Remove(hero); return; }
        _carry[hero] = units;
    }

    /// <summary>
    /// 恢复累计发放的金币 —— <b>仅供读档</b>。
    /// </summary>
    /// <param name="amount">累计发放的整金。</param>
    public void RestoreTotalGranted(long amount)
    {
        if (amount < 0) throw new ArgumentOutOfRangeException(nameof(amount));
        TotalGranted = amount;
    }

    /// <summary>
    /// 预检：这批发放是否<b>全部</b>可行。<b>不改变任何状态。</b>
    /// </summary>
    /// <param name="grants">发放明细（谁有资格领一次报酬）。</param>
    /// <returns>全部可行返回 true；任何一条不可行返回 false（此时整笔结算都不该应用）。</returns>
    public bool CanGrant(IReadOnlyList<WalletGrant> grants)
    {
        if (grants is null || grants.Count == 0) return true;
        return BuildFinalWallets(grants) is not null;
    }

    /// <summary>
    /// 提交：把报酬发出去。<b>调用前必须已通过 <see cref="CanGrant"/>。</b>
    /// </summary>
    /// <param name="grants">发放明细。</param>
    /// <exception cref="InvalidOperationException">
    /// 这批发放不可行 —— 说明调用方跳过了预检，那是编程错误。
    /// </exception>
    public void Grant(IReadOnlyList<WalletGrant> grants)
    {
        if (grants is null || grants.Count == 0) return;

        var final = BuildFinalWallets(grants);
        if (final is null)
        {
            throw new InvalidOperationException(
                "这批钱包发放不可行（英雄不存在、钱包装不下或会溢出）。"
                + "Grant 必须在 CanGrant 通过之后调用 —— 跳过了预检就不能保证「全有或全无」。");
        }

        // 全部换上。到这一步已经没有任何会失败的路径了。
        foreach (var (hero, wallet) in final.Wallets)
        {
            hero.RestoreWallet(wallet);
        }

        // 零头与累计：只记真正发出去的部分。
        foreach (var (hero, pending) in final.Pending)
        {
            if (pending == 0) _carry.Remove(hero);
            else _carry[hero] = pending;
        }

        TotalGranted += final.Paid;
    }

    /// <summary>
    /// 算出这批发放之后每个英雄的新钱包与新零头。算不出来（任何一条不可行）返回 <c>null</c>。
    /// </summary>
    /// <param name="grants">发放明细。</param>
    private FinalState? BuildFinalWallets(IReadOnlyList<WalletGrant> grants)
    {
        var order = new List<HeroId>();
        var wallets = new Dictionary<HeroId, Wallet>();
        var pendings = new Dictionary<HeroId, long>();
        long paid = 0;

        foreach (var g in grants)
        {
            if (string.IsNullOrEmpty(g.Hero.Value)) return null;

            var hero = _heroLookup(g.Hero);
            if (hero is null) return null;

            if (!wallets.TryGetValue(g.Hero, out var wallet))
            {
                wallet = hero.Wallet;
                wallets[g.Hero] = wallet;
                pendings[g.Hero] = PendingOf(g.Hero);
                order.Add(g.Hero);
            }

            // 零头累计：够 1 金就发 1 金（可能一次发多金）。
            long total = pendings[g.Hero] + _rewardUnits;
            long whole = total / 100;
            long rest = total % 100;

            if (whole > 0)
            {
                // 不可变钱包：TryAdd 返回新对象，装不下就返回 null —— 整批放弃。
                var next = wallet.TryAdd(_resource, whole);
                if (next is null) return null;
                wallet = next;
                paid += whole;
            }

            wallets[g.Hero] = wallet;
            pendings[g.Hero] = rest;
        }

        var result = new List<(HeroAgent, Wallet)>(order.Count);
        foreach (var id in order)
        {
            var hero = _heroLookup(id);
            if (hero is null) return null;   // 名单在计算过程中失效：整批放弃
            result.Add((hero, wallets[id]));
        }

        return new FinalState(result, pendings, paid);
    }

    /// <summary>一批发放算完后的中间结果。</summary>
    private sealed record FinalState(
        List<(HeroAgent Hero, Wallet Wallet)> Wallets,
        Dictionary<HeroId, long> Pending,
        long Paid);

    private static long Sum(IEnumerable<long> values)
    {
        long total = 0;
        foreach (var v in values) total += v;
        return total;
    }
}
