using System;
using System.Collections.Generic;
using System.Linq;
using GdUnit4;
using ClaudeCodeGameStudios.Domain.Items;
using ClaudeCodeGameStudios.Domain.Settlement;

namespace ClaudeCodeGameStudios.Tests.Settlement;

/// <summary>
/// 掉落表配置验证器的契约（R-05）。
/// </summary>
/// <remarks>
/// <para><b>为什么测它</b>：掉落表引用的是 <c>ItemId</c>，
/// 而「这个 Id 是不是合法物品、卖不卖得出去」要拿别处的表才能查。
/// 没有验证器时，拼错的 Id 会一路走到发奖那一步才被
/// <c>ItemGrantService</c> 判成 <c>UnknownItem</c> 拒掉 ——
/// <b>错误发生在离原因最远的地方</b>（R-01 踩过这个坑）。</para>
/// </remarks>
[TestSuite]
public class LootTableValidatorTests
{
    private static readonly ItemId Stone = new("石料");
    private static readonly ItemId Cloth = new("布匹");
    private static readonly ItemId Unknown = new("玄铁");

    private static ItemCatalog Catalog() => ItemCatalog.Default;

    private static LootTable Table(params LootEntry[] entries)
        => new("北境旧堡", draws: 3, entries);

    [TestCase]
    public void Validate_ValidTable_HasNoErrors()
    {
        var table = Table(new LootEntry(Stone, 60.0, 1, 2), new LootEntry(Cloth, 40.0, 1, 1));

        var errors = LootTableValidator.Validate(table, Catalog(), TestPrices.Book);

        Assertions.AssertThat(errors.Count).IsEqual(0);
        Assertions.AssertThat(LootTableValidator.IsValid(table, Catalog(), TestPrices.Book)).IsTrue();
    }

    [TestCase]
    public void Validate_EmptyPool_ReportsError()
    {
        var errors = LootTableValidator.Validate(Table(), Catalog(), TestPrices.Book);

        Assertions.AssertThat(errors.Count).IsGreater(0);
        Assertions.AssertThat(string.Join("|", errors)).Contains("池子是空的");
    }

    [TestCase]
    public void Validate_UnknownItem_ReportsError()
    {
        var table = Table(new LootEntry(Unknown, 10.0, 1, 1));

        var errors = LootTableValidator.Validate(table, Catalog(), TestPrices.Book);

        Assertions.AssertThat(string.Join("|", errors)).Contains("没有在物品注册表里登记");
    }

    [TestCase]
    public void Validate_UnsellableItem_ReportsError()
    {
        // 登记了但价格 0 —— 发到背包也没人买得了、也摆不了摊。
        var catalog = new ItemCatalog(new[]
        {
            new ItemDefinition(Stone, "石料", ItemRarity.Common),
        });
        var prices = new PriceBook(new[]
        {
            new KeyValuePair<ItemId, long>(Stone, 0L),   // 0 = 不卖
        });
        var table = Table(new LootEntry(Stone, 10.0, 1, 1));

        var errors = LootTableValidator.Validate(table, catalog, prices);

        Assertions.AssertThat(string.Join("|", errors)).Contains("不可售");
    }

    [TestCase]
    public void Validate_NonPositiveWeight_ReportsError()
    {
        var table = Table(new LootEntry(Stone, 0.0, 1, 1));

        var errors = LootTableValidator.Validate(table, Catalog(), TestPrices.Book);

        Assertions.AssertThat(string.Join("|", errors)).Contains("配置非法");
    }

    [TestCase]
    public void Validate_NonPositiveQuantity_ReportsError()
    {
        var table = Table(new LootEntry(Stone, 10.0, 0, 1));

        var errors = LootTableValidator.Validate(table, Catalog(), TestPrices.Book);

        Assertions.AssertThat(string.Join("|", errors)).Contains("配置非法");
    }

    [TestCase]
    public void Validate_InvertedQuantityRange_ReportsError()
    {
        var table = Table(new LootEntry(Stone, 10.0, 5, 2));

        var errors = LootTableValidator.Validate(table, Catalog(), TestPrices.Book);

        Assertions.AssertThat(string.Join("|", errors)).Contains("配置非法");
    }

    [TestCase]
    public void Validate_AllWeightsZero_ReportsError()
    {
        var table = Table(new LootEntry(Stone, 0.0, 1, 1), new LootEntry(Cloth, 0.0, 1, 1));

        var errors = LootTableValidator.Validate(table, Catalog(), TestPrices.Book);

        Assertions.AssertThat(string.Join("|", errors)).Contains("权重");
    }

    /// <summary>权重是<b>归一化</b>的 —— 总和不必是 100 或 1。</summary>
    [TestCase]
    public void Validate_ArbitraryWeightSum_IsAccepted()
    {
        var table = Table(new LootEntry(Stone, 600.0, 1, 1), new LootEntry(Cloth, 400.0, 1, 1));

        var errors = LootTableValidator.Validate(table, Catalog(), TestPrices.Book);

        Assertions.AssertThat(errors.Count).IsEqual(0);
    }
}
