using System;
using GdUnit4;
using ClaudeCodeGameStudios.Domain.Items;

namespace ClaudeCodeGameStudios.Tests.Items;

/// <summary>
/// <see cref="PriceBook"/> 的单元测试。
///
/// <para><b>本文档保护：</b></para>
/// <list type="bullet">
/// <item>负价防线：构造期拒收负数（不能"以后再处理"）。</item>
/// <item>未登记 = 0（语义："不卖"，不是"免费"）。</item>
/// <item>默认价格表 3 项商品、价格与 D-021 占位一致（占位=故意圆整数，3 / 5 / 8）。</item>
/// <item><see cref="PriceBook.WithPrices"/>：读档专用，整张表替换。</item>
/// </list>
///
/// <para><b>为什么不测防线外</b>：定价规则属 P2-D03 / D-021 【待定】，<br/>
/// 这里只测"价格表的容器 + 防线"两件事，<b>不</b>去试"摆摊收费 5%"等游戏规则</para>
/// </summary>
[TestSuite]
public class PriceBookTests
{
    private static readonly ItemId Stone = new("石料");
    private static readonly ItemId Wood = new("木料");
    private static readonly ItemId Cloth = new("布匹");
    private static readonly ItemId Jade = new("翡翠");   // 故意没在默认表

    // ── 构造 ────────────────────────────────────────────────────────────────

    /// <summary>负价必须被拒 —— 构造期防线，事后改不回去。</summary>
    [TestCase]
    public void PriceBook_WithNegativePrice_IsRejectedAtConstruction()
    {
        bool threw = false;
        try
        {
            new PriceBook(new[] { new System.Collections.Generic.KeyValuePair<ItemId, long>(Stone, -1L) });
        }
        catch (ArgumentException) { threw = true; }
        Assertions.AssertThat(threw).IsTrue();
    }

    /// <summary>空 ItemId 必须被拒 —— ItemId.None 不能当合法商品。</summary>
    [TestCase]
    public void PriceBook_WithEmptyItemId_IsRejected()
    {
        bool threw = false;
        try
        {
            new PriceBook(new[] { new System.Collections.Generic.KeyValuePair<ItemId, long>(ItemId.None, 5L) });
        }
        catch (ArgumentException) { threw = true; }
        Assertions.AssertThat(threw).IsTrue();
    }

    // ── 查价 ──────────────────────────────────────────────────────────────

    /// <summary>未登记的物品 = 0（语义"不卖"，不是"免费拿"）。</summary>
    [TestCase]
    public void PriceOf_UnregisteredItem_ReturnsZero()
    {
        var book = new PriceBook();
        Assertions.AssertThat(book.PriceOf(Jade)).IsEqual(0L);
    }

    /// <summary>登记过但价格 0 = 也是"不卖"，<c>IsSellable</c> 返 false。</summary>
    [TestCase]
    public void IsSellable_PriceZero_ReturnsFalse_EvenIfRegistered()
    {
        var book = new PriceBook(new[] { new System.Collections.Generic.KeyValuePair<ItemId, long>(Jade, 0L) });
        Assertions.AssertThat(book.PriceOf(Jade)).IsEqual(0L);
        Assertions.AssertThat(book.IsSellable(Jade)).IsFalse();
    }

    /// <summary>登记过且价格 &gt; 0 = 可售。</summary>
    [TestCase]
    public void IsSellable_PriceAboveZero_ReturnsTrue()
    {
        var book = new PriceBook(new[] { new System.Collections.Generic.KeyValuePair<ItemId, long>(Stone, 5L) });
        Assertions.AssertThat(book.IsSellable(Stone)).IsTrue();
    }

    // ── 默认表 ────────────────────────────────────────────────────────────

    /// <summary>
    /// 默认表的<b>占位价格</b>必须是 D-021 写下的值（3 / 5 / 8）。
    /// </summary>
    /// <remarks>
    /// 未来若调成 "石料 4 / 木料 4 / 布匹 12"，<b>同时</b>改本测试与
    /// <c>PriceBook.Default</c> —— 不要分开调，让"占位值"始终在测试里也有记录。
    /// </remarks>
    [TestCase]
    public void Default_HasThreeItems_AtThePlaceholderPrices()
    {
        Assertions.AssertThat(PriceBook.Default.PriceOf(Stone)).IsEqual(5L);
        Assertions.AssertThat(PriceBook.Default.PriceOf(Wood)).IsEqual(3L);
        Assertions.AssertThat(PriceBook.Default.PriceOf(Cloth)).IsEqual(8L);
    }

    /// <summary>默认表里**没有**"翡翠"。验证未登记行为一致：未登记 = 0。</summary>
    [TestCase]
    public void Default_UnregisteredItem_ReturnsZero()
    {
        Assertions.AssertThat(PriceBook.Default.PriceOf(Jade)).IsEqual(0L);
    }

    // ── WithPrices（仅供读档）───────────────────────────────────────────

    /// <summary>WithPrices 把整张表换成新表，原对象不动。</summary>
    [TestCase]
    public void WithPrices_ReplacesTheWholeTable()
    {
        var book = new PriceBook(new[] { new System.Collections.Generic.KeyValuePair<ItemId, long>(Stone, 5L) });
        var replaced = book.WithPrices(new[] { new System.Collections.Generic.KeyValuePair<ItemId, long>(Wood, 99L) });

        // 原表保持
        Assertions.AssertThat(book.PriceOf(Stone)).IsEqual(5L);
        Assertions.AssertThat(book.PriceOf(Wood)).IsEqual(0L);   // 原表没登记
        // 新表
        Assertions.AssertThat(replaced.PriceOf(Wood)).IsEqual(99L);
        Assertions.AssertThat(replaced.PriceOf(Stone)).IsEqual(0L);   // 新表没登记
    }
}
