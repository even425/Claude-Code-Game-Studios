using System.Collections.Generic;
using System.Linq;
using ClaudeCodeGameStudios.Domain.Heroes;
using ClaudeCodeGameStudios.Domain.Items;
using ClaudeCodeGameStudios.Domain.Village;

namespace ClaudeCodeGameStudios.Presentation;

/// <summary>
/// 花名册行尾的<b>经济摘要</b>（第四阶段 M5）：金币 + 背包里的物品。
/// </summary>
/// <remarks>
/// <para><b>为什么放在花名册行里，而不是新开一个「经济面板」</b>：设计文档明确
/// 「不要先做复杂的经济大盘」。花名册本来就把 37 个人逐行列着，买卖双方都在上面；
/// 在名字后面补一小段状态，就能在读杂谈的交易条目时<b>直接核对</b>两个人的钱和背包。
/// 不需要额外的窗口、翻页或选择器。</para>
/// <para><b>纯函数</b>：不碰 Node，不知道 <c>ItemList</c> 怎么画。
/// 这样「金 0 / 背包 3 种 / 超过两种」这些边界可以用单测钉住 ——
/// 和 <see cref="Roster"/> 是同一个理由。</para>
/// <para><b>宽度纪律</b>：左栏只有 248 像素。背包最多显示
/// <see cref="DefaultMaxBagKinds"/> 种物品，多出来的用 <c>+N</c> 表示种数，
/// 不把整行撑到被裁掉。</para>
/// </remarks>
public static class EconomySummary
{
    /// <summary>默认展示的背包物品种类上限。</summary>
    public const int DefaultMaxBagKinds = 2;

    /// <summary>经济摘要里的金币前缀。</summary>
    public const string GoldPrefix = "金";

    /// <summary>背包里还有更多种物品时的后缀前缀。</summary>
    public const string MorePrefix = "+";

    /// <summary>金币与物品、物品与物品之间的分隔符。</summary>
    public const string Separator = " ";

    /// <summary>
    /// 一个人的经济摘要，例如 <c>金95 石料×1</c>。
    /// </summary>
    /// <param name="hero">英雄。<b>不得为 null</b>。</param>
    /// <param name="gold">金币资源 Id（<c>StallService.Gold</c>，由调用方注入，避免表现层复制字面量）。</param>
    /// <param name="maxBagKinds">背包最多列几种；&lt; 1 按 1 处理。</param>
    /// <returns>经济摘要文本；背包为空时只有金币。</returns>
    /// <remarks>
    /// 物品顺序取 <see cref="Inventory.Snapshot"/>（按 Id 排序），
    /// 因此同一份背包每次渲染结果完全一致 —— 否则每帧都可能重排、重建列表。
    /// </remarks>
    public static string For(HeroAgent hero, ResourceId gold, int maxBagKinds = DefaultMaxBagKinds)
    {
        long amount = hero.Wallet.Amount(gold);
        string goldPart = $"{GoldPrefix}{amount}";

        var bag = BagText(hero.Bag, maxBagKinds);
        return bag.Length == 0 ? goldPart : goldPart + Separator + bag;
    }

    /// <summary>
    /// 只生成背包部分，例如 <c>石料×2 木料×1 +1</c>。
    /// </summary>
    /// <param name="bag">背包。<b>不得为 null</b>。</param>
    /// <param name="maxBagKinds">最多列几种；&lt; 1 按 1 处理。</param>
    /// <returns>背包文本；空背包返回空字符串。</returns>
    public static string BagText(Inventory bag, int maxBagKinds = DefaultMaxBagKinds)
    {
        if (maxBagKinds < 1) maxBagKinds = 1;

        var items = bag.Snapshot();
        if (items.Count == 0) return string.Empty;

        var shown = items.Take(maxBagKinds)
            .Select(kv => $"{kv.Key.Value}×{kv.Value}");

        string text = string.Join(Separator, shown);
        int more = items.Count - maxBagKinds;
        return more > 0 ? text + Separator + $"{MorePrefix}{more}" : text;
    }
}
