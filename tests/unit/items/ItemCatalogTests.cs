using System;
using System.Collections.Generic;
using System.Linq;
using GdUnit4;
using ClaudeCodeGameStudios.Domain.Items;

namespace ClaudeCodeGameStudios.Tests.Items;

/// <summary>
/// 物品注册表的契约（R-05）。
/// </summary>
/// <remarks>
/// <para><b>为什么测它</b>：<c>ItemDefinition</c> 此前<b>全仓无实例化</b> ——
/// 类在、没人用。稀有度挂在它上面而不给它一个容器，等于把规则挂在没人引用的类上。
/// 注册表就是那个容器，也是掉落表验证器「这个物品存不存在」的唯一判据。</para>
/// </remarks>
[TestSuite]
public class ItemCatalogTests
{
    private static readonly ItemId Stone = new("石料");
    private static readonly ItemId Wood = new("木料");
    private static readonly ItemId Cloth = new("布匹");
    private static readonly ItemId Unknown = new("玄铁");

    [TestCase]
    public void Empty_HasNoItems()
    {
        Assertions.AssertThat(ItemCatalog.Empty.Count).IsEqual(0);
        Assertions.AssertThat(ItemCatalog.Empty.Contains(Stone)).IsFalse();
    }

    [TestCase]
    public void Find_RegisteredItem_ReturnsDefinition()
    {
        var catalog = new ItemCatalog(new[]
        {
            new ItemDefinition(Stone, "石料", ItemRarity.Common),
        });

        var def = catalog.Find(Stone);

        Assertions.AssertThat(def).IsNotNull();
        Assertions.AssertThat(def!.Rarity).IsEqual(ItemRarity.Common);
        Assertions.AssertThat(def.DisplayName).IsEqual("石料");
    }

    [TestCase]
    public void Find_UnregisteredItem_ReturnsNull()
    {
        var catalog = new ItemCatalog(new[]
        {
            new ItemDefinition(Stone, "石料", ItemRarity.Common),
        });

        Assertions.AssertThat(catalog.Find(Unknown)).IsNull();
        Assertions.AssertThat(catalog.RarityOf(Unknown)).IsNull();
    }

    [TestCase]
    public void DuplicateId_IsRejected()
    {
        bool threw = false;
        try
        {
            _ = new ItemCatalog(new[]
            {
                new ItemDefinition(Stone, "石料", ItemRarity.Common),
                new ItemDefinition(Stone, "另一种石料", ItemRarity.Rare),
            });
        }
        catch (InvalidOperationException)
        {
            threw = true;
        }

        Assertions.AssertThat(threw).IsTrue();
    }

    [TestCase]
    public void EmptyId_IsRejected()
    {
        bool threw = false;
        try
        {
            _ = new ItemCatalog(new[] { new ItemDefinition(ItemId.None, "无名", ItemRarity.Common) });
        }
        catch (ArgumentException)
        {
            threw = true;
        }

        Assertions.AssertThat(threw).IsTrue();
    }

    /// <summary>默认注册表必须与默认价格表<b>一一对应</b> —— 少登记一个，掉落表验证器就会报错。</summary>
    [TestCase]
    public void Default_MatchesDefaultPriceBook()
    {
        var catalog = ItemCatalog.Default;
        var prices = TestPrices.Book;

        foreach (var item in catalog.All)
        {
            Assertions.AssertThat(prices.IsSellable(item.Id)).IsTrue();
        }

        Assertions.AssertThat(catalog.Count).IsEqual(5);
    }

    [TestCase]
    public void All_IsSortedById()
    {
        var ids = ItemCatalog.Default.All.Select(d => d.Id.Value).ToArray();

        // ⚠️ **D-118**：多了两样狩猎战利品（兽皮 / 兽肉）⇒ 五个。
        Assertions.AssertThat(ids).IsEqual(new[] { "兽皮", "兽肉", "布匹", "木料", "石料" });
    }
}
