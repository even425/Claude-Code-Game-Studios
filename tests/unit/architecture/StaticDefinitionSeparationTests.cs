using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using GdUnit4;
using ClaudeCodeGameStudios.Application;
using ClaudeCodeGameStudios.Application.Stall;
using ClaudeCodeGameStudios.Domain.Heroes;
using ClaudeCodeGameStudios.Domain.Items;
using DomainVillage = ClaudeCodeGameStudios.Domain.Village.Village;
using DomainResourceId = ClaudeCodeGameStudios.Domain.Village.ResourceId;

namespace ClaudeCodeGameStudios.Tests;

/// <summary>
/// <b>UT-07（不变量 I7）</b>：静态定义与运行时状态<b>必须分离</b>。
///
/// <para><b>它守的是什么</b>：物品定义、价格表这些「静态定义」是<b>整局不变</b>的
/// （基线 §13.6）。而「谁手里有几份石料」是<b>运行时状态</b>，随交易变化。
/// 一旦运行时把定义改写了 —— 哪怕只改一处 —— 后果是
/// <b>下一局的起点就变了</b>：玩家会发现「昨天石料 3 金，今天 5 金」，
/// 而且<b>没有任何报错</b>。这类 bug 极难定位，因为它在「跑起来之后」才发生，
/// 而现场早就没了。</para>
///
/// <para><b>为什么用两条断言守</b>：光比「跑完一局前后定义有没有变」是不够的 ——
/// 定义类型如果<b>根本没有公开的写入口</b>，那它就<b>不可能</b>被写坏；
/// 而如果有写入口，比对又可能因为「这次没走到那条路径」而漏判。
/// 所以：一条查<b>结构</b>（有没有写入口），一条查<b>行为</b>（真跑一遍闭环后没变）。</para>
///
/// <para><b>本文件不发明任何玩法数值</b>。</para>
/// </summary>
[TestSuite]
public class StaticDefinitionSeparationTests
{
    private static readonly HeroId Seller = new("h-seller");
    private static readonly HeroId Buyer = new("h-buyer");
    private static readonly ItemId Stone = new("石料");
    private static readonly DomainResourceId Gold = new("金币");

    // ── 快照 ──────────────────────────────────────────────────────────

    /// <summary>
    /// 静态定义的<b>全部可观察内容</b>，压成一段可比对的文本。
    /// <para>逐项列出（不是整对象 ToString）—— 对象 ToString 往往是类型名，
    /// 比对它等于没比。</para>
    /// </summary>
    private static string SnapshotStaticDefinitions()
    {
        var lines = new List<string>();

        var catalog = ItemCatalog.Default;
        lines.Add($"catalog.count={catalog.Count}");
        foreach (var def in catalog.All.OrderBy(d => d.Id.Value, StringComparer.Ordinal))
        {
            lines.Add($"catalog[{def.Id.Value}] name={def.DisplayName} rarity={def.Rarity}");
        }

        lines.Add($"prices={TestPrices.Book.Describe()}");

        return string.Join("\n", lines);
    }

    // ── UT-07 行为断言 ───────────────────────────────────────────────

    /// <summary>
    /// <b>UT-07 / I7</b>：跑完一段<b>真的</b>闭环（掉落 → 摆摊 → 购买）后，
    /// 静态定义的逐字段内容必须<b>分毫未变</b>。
    ///
    /// <para><b>为什么闭环要用这几个模块</b>：它们是<b>真正读</b>静态定义的那几条路
    /// —— 掉落校验器用 <c>ItemCatalog</c> 与 <c>PriceBook</c>，
    /// 摆摊与定价用 <c>PriceBook</c>。别的模块不碰定义，跑了也证明不了什么。</para>
    /// </summary>
    [TestCase]
    public void UT07_RunningAClosedLoop_DoesNotChangeStaticDefinitions()
    {
        string before = SnapshotStaticDefinitions();

        // ── 掉落：走真实的掉落提供器（它会拿静态定义做校验）────────────
        var provider = new DungeonLootProvider(
            new[] { DefaultLootTables.For("测试副本") },
            ItemCatalog.Default,
            TestPrices.Book);

        var loot = provider.LootFor("测试副本", new DecisionFlow.SeededRandom(0xC0FFEE));
        Assertions.AssertThat(loot).IsNotEmpty();

        // ── 摆摊 + 购买：走真实的摊位服务（定价走 PriceBook）──────────
        var (heroes, village) = MakeScene();
        var stalls = new StallService(
            village, TestPrices.Book,
            id => heroes.TryGetValue(id, out var h) ? h : null);

        var placed = stalls.PlaceStall(Seller, Stone, 5, 0.0);
        Assertions.AssertThat(placed).IsNotNull();

        var bought = stalls.TryBuy(Seller, Buyer, Stone, 1, 0.0);
        Assertions.AssertThat(bought).IsNotNull();

        // ── 比对 ──────────────────────────────────────────────────────
        Assertions.AssertThat(SnapshotStaticDefinitions()).IsEqual(before);
    }

    /// <summary>卖方 10 份石料 + 100 金、买方 100 金；村庄带一个市集影响区。</summary>
    private static (Dictionary<HeroId, HeroAgent> heroes, DomainVillage village) MakeScene()
    {
        var village = new DomainVillage("v-test");
        // 摆摊位置必须落在建筑影响区（M5 / D-033）。
        var market = new ClaudeCodeGameStudios.Domain.Village.BuildingInstance(
            new ClaudeCodeGameStudios.Domain.Village.BuildingId("market"),
            gridX: -5, gridY: -5, footprintW: 1, footprintH: 1,
            constructionProgress: 1.0,
            affectsAreaW: 11, affectsAreaH: 11);
        village.TryPlace(market);

        var heroes = new Dictionary<HeroId, HeroAgent>();

        var sellerBag = new Inventory().TryAdd(Stone, 10);
        var seller = new HeroAgent { Name = "卖方" };
        seller.RestoreBag(sellerBag!);
        seller.RestoreWallet(new Wallet(new Dictionary<DomainResourceId, long> { { Gold, 100L } }));
        heroes[Seller] = seller;

        var buyer = new HeroAgent { Name = "买方" };
        buyer.RestoreBag(new Inventory());
        buyer.RestoreWallet(new Wallet(new Dictionary<DomainResourceId, long> { { Gold, 100L } }));
        heroes[Buyer] = buyer;

        return (heroes, village);
    }

    // ── UT-07 结构断言 ───────────────────────────────────────────────

    /// <summary>
    /// UT-07 的<b>结构面</b>：静态定义类型<b>不得有公开的写入口</b>。
    ///
    /// <para><b>判据</b>：<list type="number">
    /// <item>不得有<b>公开可写</b>的实例属性（有 setter / init-only 也算写入口 ——
    /// init-only 只在构造期能写，但反射能绕过去，保守起见一并禁掉）；</item>
    /// <item>不得有<b>公开返回可变集合</b>的属性（返回 <c>Dictionary</c> /
    /// <c>List</c> 的属性 = 把内部字典漏出去 = 一个写入口）。</item>
    /// </list>
    /// 只查<b>实例</b>成员：静态的 <c>Default</c> 是「取用」，不是「写」，
    /// 但它<b>返回的对象</b>必须不可变 —— 那是上面第一条管的事。</para>
    /// </summary>
    [TestCase]
    public void UT07_StaticDefinitionTypes_HaveNoPublicWritePath()
    {
        var offenders = new List<string>();
        foreach (var t in DefinitionTypes())
        {
            foreach (var prop in t.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (prop.GetSetMethod() is not null)
                {
                    offenders.Add($"{t.Name}.{prop.Name} 有公开 setter");
                }

                if (IsMutableCollection(prop.PropertyType))
                {
                    offenders.Add($"{t.Name}.{prop.Name} 的类型是可变集合（内部状态漏出去了）");
                }
            }
        }

        Assertions.AssertThat(string.Join(" | ", offenders)).IsEmpty();
    }

    /// <summary>
    /// 静态定义类型<b>不得暴露可变集合</b>。
    /// <para>（与上一条同源，但单独一条：上一条关心「能不能写」，
    /// 这一条关心「有没有把字典本体交出去」—— 交出去就等于能写。）</para>
    /// </summary>
    [TestCase]
    public void UT07_StaticDefinitionTypes_DoNotExposeMutableCollections()
    {
        var offenders = new List<string>();
        foreach (var t in DefinitionTypes())
        {
            foreach (var prop in t.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (IsMutableCollection(prop.PropertyType))
                {
                    offenders.Add($"{t.Name}.{prop.Name}: {prop.PropertyType.Name}");
                }
            }
        }

        Assertions.AssertThat(string.Join(" | ", offenders)).IsEmpty();
    }

    private static IEnumerable<Type> DefinitionTypes() => new[]
    {
        typeof(ItemDefinition),
        typeof(ItemCatalog),
        typeof(PriceBook),
    };

    /// <summary>
    /// 判定一个类型是不是「可变集合」：数组、<c>List</c>/<c>Dictionary</c>/
    /// <c>HashSet</c> 及其接口的泛型封闭。<c>IReadOnlyXxx</c> 是只读视图，不算。
    /// </summary>
    private static bool IsMutableCollection(Type t)
    {
        if (t.IsArray) return true;
        if (t.IsGenericType)
        {
            var def = t.GetGenericTypeDefinition();
            var ns = def.Namespace ?? "";
            if (ns.StartsWith("System.Collections.Generic", StringComparison.Ordinal))
            {
                return !def.Name.StartsWith("IReadOnly", StringComparison.Ordinal);
            }
        }
        return false;
    }
}
