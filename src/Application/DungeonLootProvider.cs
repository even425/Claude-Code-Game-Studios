using System;
using System.Collections.Generic;
using System.Linq;
using ClaudeCodeGameStudios.Domain.Heroes;
using ClaudeCodeGameStudios.Domain.Items;
using ClaudeCodeGameStudios.Domain.Settlement;

namespace ClaudeCodeGameStudios.Application;

/// <summary>
/// 副本战利品的<b>掉落表实现</b>（R-05）。
/// </summary>
/// <remarks>
/// <para><b>它取代了占位实现</b>：R-01 那个 <c>PlaceholderDungeonLootProvider</c>
/// 只做一件事 —— 把任务表的奖励份数换成一种已登记的物品，
/// 目的是让「副本成功 → 物品进背包 → 能开摊 → 别人能买」这条链<b>先跑通</b>。
/// 现在有真正的掉落表了，占位实现<b>退役</b>（不是留着，是删掉 ——
/// 留着就会有两份"掉落规则"，迟早对不上）。</para>
///
/// <para><b>配置坏了不崩游戏</b>：掉落表不合法（引用了没登记的物品、权重全是 0……）
/// 时，本类<b>不发放任何东西</b>，并把错误报给注入的回调 ——
/// 「这个副本今天不掉东西」比「游戏起不来」好对付得多。</para>
///
/// <para><b>抽多少由表决定</b>：掉落表自己声明抽几次、池子里有什么、各能出几个。
/// 调用方（R-01 的 <c>ComputeLoot</c>）只负责递随机源。</para>
/// </remarks>
public sealed class DungeonLootProvider : ILootProvider
{
    private readonly Dictionary<string, LootTable> _tables;
    private readonly ItemCatalog _catalog;
    private readonly PriceBook _prices;
    private readonly Action<string>? _onConfigError;

    /// <param name="tables">掉落表清单（按副本名索引）。</param>
    /// <param name="catalog">物品注册表（验证引用是否存在、稀有度）。</param>
    /// <param name="prices">价格表（验证物品是否可售）。</param>
    /// <param name="onConfigError">配置出问题时被调用（记一笔）；可为 null。</param>
    /// <exception cref="ArgumentNullException"><paramref name="tables"/> / <paramref name="catalog"/> / <paramref name="prices"/> 为 null。</exception>
    /// <exception cref="ArgumentException">同一个副本名出现了两张表。</exception>
    public DungeonLootProvider(
        IEnumerable<LootTable> tables,
        ItemCatalog catalog,
        PriceBook prices,
        Action<string>? onConfigError = null)
    {
        if (tables is null) throw new ArgumentNullException(nameof(tables));
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _prices = prices ?? throw new ArgumentNullException(nameof(prices));
        _onConfigError = onConfigError;

        _tables = new Dictionary<string, LootTable>(StringComparer.Ordinal);
        foreach (var table in tables)
        {
            if (table is null) continue;
            if (_tables.ContainsKey(table.DungeonName))
            {
                throw new ArgumentException(
                    $"副本「{table.DungeonName}」有多张掉落表 —— 一个副本只能有一张。",
                    nameof(tables));
            }

            _tables[table.DungeonName] = table;
        }
    }

    /// <summary>已登记的掉落表（按副本名）。</summary>
    public IReadOnlyCollection<LootTable> Tables => _tables.Values;

    /// <inheritdoc />
    /// <remarks>
    /// <b>未知副本</b> → 空清单 + 记一笔（不是抛异常：没有掉落表只是"这个副本不掉东西"）。
    /// <b>配置不合法</b> → 空清单 + 把验证器的每条错误都报出来。
    /// </remarks>
    public IReadOnlyList<LootStack> LootFor(string dungeonName, IRandomSource rng)
    {
        if (rng is null) throw new ArgumentNullException(nameof(rng));

        if (string.IsNullOrEmpty(dungeonName)) return Array.Empty<LootStack>();

        if (!_tables.TryGetValue(dungeonName, out var table))
        {
            _onConfigError?.Invoke($"副本「{dungeonName}」没有掉落表 —— 不产出任何战利品。");
            return Array.Empty<LootStack>();
        }

        var errors = LootTableValidator.Validate(table, _catalog, _prices);
        if (errors.Count > 0)
        {
            foreach (var error in errors) _onConfigError?.Invoke(error);
            return Array.Empty<LootStack>();
        }

        return LootRoller.Roll(table, rng);
    }
}

/// <summary>
/// 默认掉落表（R-05）。<b>权重与数量已由 D-076 拍板</b>。
/// </summary>
/// <remarks>
/// <para><b>它们仍不是「平衡值」</b>：D-076 拍的是**比例**（玩家拿到的份数 50/30/20），
/// 比例对了不代表总量对 —— 总量（抽 3 次 ⇒ 3-6 份）仍要靠**标定实测**来定。
/// ⚠️ 这正是 D-076 之后重跑标定的原因。</para>
///
/// <para><b>副本名由调用方给</b>：不写死「北境旧堡」——
/// 副本名散落在两处（表里一份、调用方一份）迟早会打不响，
/// 而「打不响」的表现是<b>静默不掉东西</b>，极难查。</para>
/// </remarks>
public static class DefaultLootTables
{
    /// <summary>造出某个副本的默认掉落表（【已确认】D-076 的权重）。</summary>
    /// <param name="dungeonName">副本名称。</param>
    /// <returns>掉落表。</returns>
    public static LootTable For(string dungeonName) => new(
        dungeonName,
        // 抽几次：**3 次**（每场副本出 3 次抽取 ⇒ 3-6 份东西）。
        // ⚠️ 这个数此前**没有登记进参数台账** ⇒ 台账看不到它 ⇒ 改了没人知道。
        // ⇒ 现在登记（`loot-draws`）。
        draws: 3,
        entries: new[]
        {
            // 权重【已确认】D-076：**100 / 90 / 60**。
            //
            // ⚠️⚠️ **为什么不是 50 / 30 / 20**（这是个很容易踩的坑）：
            //
            // **权重 = 抽中概率，而玩家拿到的是「份数」** ⇒
            //   玩家拿到的份数比例 = 权重 × 数量期望
            // 而石料每次给 **1-2 份**（期望 1.5），木料与布匹固定 1 份。
            // ⇒ 若填 50/30/20，实际份数比例是
            //   50×1.5 : 30×1 : 20×1 = 75 : 30 : 20 = **60% / 24% / 16%**
            //   ⇒ 石头占六成（正是「太单调」的老毛病），**与拍板意图相反**。
            //
            // ⇒ 要得到**玩家拿到 50 / 30 / 20 份**，权重必须**除以数量期望**：
            //   石料 50/1.5 = 33.3，木料 30/1 = 30，布匹 20/1 = 20
            // ⇒ 这里取等比整数 **100 / 90 / 60**（×3）：
            //   100×1.5 : 90×1 : 60×1 = 150 : 90 : 60 = **50% / 30% / 20%** ✓ 精确
            //
            // ⇒ **D-076 拍板的是「玩家拿到的份数比例 50/30/20」**，
            // 不是「抽中概率 50/30/20」—— 这是实测才发现的区别。
            //
            // ⚠️ **数量没动**（石料 1-2 份 / 木料 1 份 / 布匹 1 份）—— D-076 拍板「保持 1-2 份」。
            new LootEntry(new ItemId("石料"), 100.0, 1, 2),
            new LootEntry(new ItemId("木料"), 90.0, 1, 1),
            new LootEntry(new ItemId("布匹"), 60.0, 1, 1),
        });
}
