using System.Collections.Generic;
using ClaudeCodeGameStudios.Domain.Items;

namespace ClaudeCodeGameStudios.Tests;

/// <summary>
/// <b>测试自己的价格表</b> —— 单元测试的<b>夹具</b>，不是产品的配置。
///
/// <para><b>为什么需要它</b>：价格是 <c>07</c> §2 的<b>临时参数</b>（<b>P-14</b> 待定），
/// 而开发者随时会改它 —— 改的是 <c>config/prices.json</c> 与内置兜底 <c>PriceBook.Default</c>。
/// 如果单元测试直接用 <c>PriceBook.Default</c>，那么：
/// <list type="number">
/// <item>开发者一改价格，**十几条测试当场红** —— 而那些测试断言的其实是
/// <b>机制</b>（摆摊抽 5%、余额怎么算），不是<b>价格是多少</b>；</item>
/// <item>更要命的是：<c>production/qa/recalibrate.py</c> 的第 2 步就是 <c>dotnet test</c>，
/// 而「改价格 → 重跑标定」正是开发者拿到 P-14 的三个数之后要走的那条路
/// ⇒ <b>那条承诺恰好在最需要它的地方断了</b>。</item>
/// </list></para>
///
/// <para><b>所以：单元测试自己造一张价格表。</b>
/// 服务层（<c>StallService</c> / <c>HeroBehaviorService</c> / <c>ItemCatalog</c> …）
/// 本来就<b>接受注入</b>价格表 —— 之前只是「恰好」传了全局默认表。
/// 现在传这张夹具表 ⇒ 断言里的 5 / 95 / 90 / 20 仍然是<b>精确的</b>，
/// 而它们与<b>产品的占位价彻底脱钩</b>。</para>
///
/// <para><b>「产品的默认表到底对不对」另有守卫</b>：
/// <c>PriceBookConfigConsistencyTests</c> 逐项比对「配置文件 ↔ 内置兜底」两份副本。
/// 那条守卫**故意**用 <c>PriceBook.Default</c> —— 它要的就是那一份。
/// 换句话说：<b>占位价只允许出现在两个地方</b>（配置 + 兜底），<b>别处不许出现</b>
/// —— 由 <c>PlaceholderPriceOnlyInTwoPlacesTests</c> 守着。</para>
///
/// <para><b>夹具里的数从哪来</b>：它们只是「一组能算出整数余额的数」，
/// 与产品该定多少价无关。想验证某个价下的行为，就改这里的数（或者在那个测试里
/// 自己造一张表）—— <b>不要</b>把它当成产品的价格。</para>
/// </summary>
public static class TestPrices
{
    /// <summary>石料单价（夹具值，<b>不是</b>产品价）。</summary>
    public const long Stone = 5L;

    /// <summary>木料单价（夹具值，<b>不是</b>产品价）。</summary>
    public const long Wood = 3L;

    /// <summary>布匹单价（夹具值，<b>不是</b>产品价）。</summary>
    public const long Cloth = 8L;

    /// <summary>测试用的价格表（三个可售物品，各有一个明确的价格）。</summary>
    public static readonly PriceBook Book = new(new[]
    {
        new KeyValuePair<ItemId, long>(new ItemId("石料"), Stone),
        new KeyValuePair<ItemId, long>(new ItemId("木料"), Wood),
        new KeyValuePair<ItemId, long>(new ItemId("布匹"), Cloth),
    });
}
