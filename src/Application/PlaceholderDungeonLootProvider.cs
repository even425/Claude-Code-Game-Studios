using System;
using System.Collections.Generic;
using ClaudeCodeGameStudios.Domain.Items;
using ClaudeCodeGameStudios.Domain.Settlement;

namespace ClaudeCodeGameStudios.Application;

/// <summary>
/// 副本战利品的<b>占位来源</b>（R-01）。
/// </summary>
/// <remarks>
/// <para><b>这是占位，不是掉落表。</b>战利品<b>内容</b>属【待定】
/// <c>07</c> 的 <b>T-007</b>（掉落率 / 稀有度 / 掉落权重 —— 后续的 <c>R-05</c>），
/// 现在写下来就是凭空发明数值。因此本实现只做一件事：
/// <b>把任务表现有的奖励份数，换成一种已经登记在价格表里的物品</b>，
/// 让「副本成功 → 物品进个人背包 → 能开摊 → 别人能买」这条链<b>今天就能跑通</b>。</para>
///
/// <para><b>为什么选「石料」</b>：它已在 <see cref="PriceBook.Default"/> 登记（价格 5），
/// 而发放入口要求物品<b>已登记为可售</b>。用已登记的物品，
/// 就不必在本任务里<b>新增一个价格</b>——那正是 R-05 的地盘。</para>
///
/// <para><b>替换方式</b>：T-007 拍板后，换掉 <see cref="LootFor"/> 的实现即可；
/// 分配规则（<see cref="LootSplit"/>）一行都不用改。</para>
/// </remarks>
public sealed class PlaceholderDungeonLootProvider : ILootProvider
{
    /// <summary>
    /// 占位掉落物品。<b>已登记在 <see cref="PriceBook.Default"/> 中</b>，
    /// 所以发放入口不会把它判成「未登记物品」而拒掉。
    /// </summary>
    public static readonly ItemId LootItem = new("石料");

    /// <inheritdoc />
    /// <remarks>
    /// <b>份数沿用任务表的奖励规模</b>（<c>Job.Amt</c>），不新造数字。
    /// 这批份数随后由 <see cref="LootSplit"/> 按参战名单平分。
    /// </remarks>
    public IReadOnlyList<LootStack> LootFor(string dungeonName, long amount)
    {
        if (amount <= 0) return Array.Empty<LootStack>();
        return new[] { new LootStack(LootItem, amount) };
    }
}
