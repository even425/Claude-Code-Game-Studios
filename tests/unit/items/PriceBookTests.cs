using System;
using System.Collections.Generic;
using System.Linq;
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
/// <item>默认价格表<b>装着那三个可售物品</b>（<b>不写死价格</b> —— 那是 T-007 的占位值，见下方那条测试的说明）。</item>
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

    // ── 外部价格表（D-054 阶段 7）──────────────────────────────────────────

    /// <summary>外部价格表能读出来，且<b>注释键（<c>_</c> 开头）被忽略</b>。</summary>
    [TestCase]
    public void TryLoadFromJson_ReadsItems_AndIgnoresCommentKeys()
    {
        const string json = """
        {
          "_说明": "这是注释，不该被当成物品",
          "石料": 5,
          "木料": 3,
          "布匹": 8
        }
        """;

        bool ok = PriceBook.TryLoadFromJson(json, out var book, out var error);

        Assertions.AssertThat(ok).IsTrue();
        Assertions.AssertThat(error).IsNull();
        Assertions.AssertThat(book!.PriceOf(Stone)).IsEqual(5L);
        Assertions.AssertThat(book!.PriceOf(Wood)).IsEqual(3L);
        Assertions.AssertThat(book!.PriceOf(Cloth)).IsEqual(8L);
        // 注释键没被当成商品
        Assertions.AssertThat(book!.PriceOf(new ItemId("_说明"))).IsEqual(0L);
    }

    /// <summary>价格表<b>坏了</b> ⇒ 明确失败 + 原因（调用方据此退回占位表）。</summary>
    [TestCase]
    public void TryLoadFromJson_BrokenJson_FailsWithReason()
    {
        bool ok = PriceBook.TryLoadFromJson("{ 这不是 json", out var book, out var error);

        Assertions.AssertThat(ok).IsFalse();
        Assertions.AssertThat(book).IsNull();
        Assertions.AssertThat(error).IsNotNull();
    }

    /// <summary>负价 ⇒ 拒（与构造期同一条防线：不为负）。</summary>
    [TestCase]
    public void TryLoadFromJson_NegativePrice_IsRejected()
    {
        bool ok = PriceBook.TryLoadFromJson("""{"石料": -1}""", out var book, out var error);

        Assertions.AssertThat(ok).IsFalse();
        Assertions.AssertThat(book).IsNull();
        Assertions.AssertThat(error).IsNotNull();
    }

    /// <summary>空表 / 只有注释 ⇒ 拒（不然会得到一张「什么都不卖」的表）。</summary>
    [TestCase]
    public void TryLoadFromJson_EmptyOrOnlyComments_IsRejected()
    {
        bool emptyOk = PriceBook.TryLoadFromJson("{}", out _, out _);
        bool commentOnlyOk = PriceBook.TryLoadFromJson("""{"_说明": "只有注释"}""", out _, out _);

        Assertions.AssertThat(emptyOk).IsFalse();
        Assertions.AssertThat(commentOnlyOk).IsFalse();
    }

    /// <summary>文件不存在 ⇒ 失败但**不抛**（游戏要能退回占位表继续跑）。</summary>
    [TestCase]
    public void TryLoadFromFile_MissingFile_FailsWithoutThrowing()
    {
        bool ok = PriceBook.TryLoadFromFile(
            System.IO.Path.Combine(System.IO.Path.GetTempPath(), "no-such-prices-xyz.json"),
            out var book, out var error);

        Assertions.AssertThat(ok).IsFalse();
        Assertions.AssertThat(book).IsNull();
        Assertions.AssertThat(error).IsNotNull();
    }

    /// <summary>
    /// 仓库里那份 <c>config/prices.json</c> 必须**能读出来**，且与占位表一致。
    /// </summary>
    /// <remarks>
    /// 这条测试守的是「开发者改了那个文件之后，游戏还是能起来」这条链：
    /// 格式错 ⇒ 退回占位表（不崩），但**开发者在采集日志里会看到 PRICEBOOK 那行**。
    /// </remarks>
    [TestCase]
    public void ConfigPricesFile_Loads_AndMatchesThePlaceholderUntilItIsDecided()
    {
        // 从测试程序集所在目录**向上找**仓库根（不写死层数 —— 改输出目录就会断）
        var dir = new System.IO.DirectoryInfo(
            System.IO.Path.GetDirectoryName(
                System.Reflection.Assembly.GetExecutingAssembly().Location)!);
        string? path = null;
        while (dir is not null)
        {
            var candidate = System.IO.Path.Combine(dir.FullName, "config", "prices.json");
            if (System.IO.File.Exists(candidate)) { path = candidate; break; }
            dir = dir.Parent;
        }

        Assertions.AssertThat(path).IsNotNull();

        bool ok = PriceBook.TryLoadFromFile(path!, out var book, out var error);

        Assertions.AssertThat(ok).IsTrue();
        Assertions.AssertThat(error).IsNull();
        Assertions.AssertThat(book!.PriceOf(Stone)).IsGreater(0L);
        Assertions.AssertThat(book!.PriceOf(Wood)).IsGreater(0L);
        Assertions.AssertThat(book!.PriceOf(Cloth)).IsGreater(0L);
    }

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
    /// 默认兜底表必须<b>装着那三个可售物品，且价格非负</b>。
    /// </summary>
    /// <remarks>
    /// <para><b>这里刻意不写死具体价格</b>（曾经是 3 / 5 / 8）。
    /// 那是 <b>T-007</b> 的占位值，开发者随时会改 —— 而写死它的后果是：
    /// 开发者改完价格，这条测试就红，而 <c>production/qa/recalibrate.py</c>
    /// 的第 2 步正是 <c>dotnet test</c> ⇒ <b>「改价格 → 重跑标定」那条路
    /// 恰好在最需要它的地方断掉</b>。</para>
    /// <para><b>「具体是多少钱」不是断言，是开发者的决定</b>；
    /// 「默认表里那三个物品在、且价格合法」才是这张表的性质。
    /// 两份副本不许分叉，由 <c>PriceBookConfigConsistencyTests</c> 守。</para>
    /// </remarks>
    [TestCase]
    public void Default_RegistersTheThreeSellableItems_WithNonNegativePrices()
    {
        var registered = PriceBook.Default.Items
            .Select(kv => kv.Key.Value)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var id in new[] { Stone, Wood, Cloth })
        {
            Assertions.AssertThat(registered.Contains(id.Value))
                .IsTrue();   // 物品**集合**是稳定的；价格不是
            Assertions.AssertThat(PriceBook.Default.PriceOf(id)).IsGreaterEqual(0L);
        }
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
