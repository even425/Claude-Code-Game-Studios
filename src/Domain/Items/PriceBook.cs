using System;
using System.Collections.Generic;
using System.Linq;

namespace ClaudeCodeGameStudios.Domain.Items;

/// <summary>
/// 商品的<b>固定价格表</b>：<see cref="ItemId"/> → 单价（钱）。
/// </summary>
/// <remarks>
/// <para><b>不可变</b>。任何修改 = 返回新对象（与 <see cref="Wallet"/> / <see cref="Inventory"/>
/// 一致；M1 已立的规矩）。存档（M7）会按 <c>Dictionary&lt;ItemId,long&gt;</c> 序列化为 DTO。</para>
///
/// <para><b>为什么<b>不可变</b></b>：交易要查价时拿到的应是<b>固定参考</b>，
/// 不是"查的时候另一个线程把价改了"的快照。不可变 + 用读时取引用就够，
/// 不必上锁。</para>
///
/// <para><b>统一表（同一物同价）</b>：D-021 / P2-D03 明确"同一物品全球统一价"。
/// 本类不是"每个摊位各有一份价"——同一物不论在哪个摊位、哪种交易里都是同一个价。
/// 这避免了"同物不同价 + 套利"的玩法漏洞（M3 摆摊后会冒出来）。</para>
///
/// <para><b>价格 0 的语义</b>：0 = 该物品在价格表里被标为"不卖"。
/// 调用方要先查价查到 0 = 不应上架 —— 这是 M3 摆摊的前置检查。
/// 负数构造期直接拒（与 I12 防线一致的"不为负"）</para>
/// </remarks>
public sealed class PriceBook
{
    private readonly Dictionary<ItemId, long> _prices;

    /// <summary>构造一张空价格表。</summary>
    public PriceBook()
        : this((IEnumerable<KeyValuePair<ItemId, long>>)Array.Empty<KeyValuePair<ItemId, long>>()) { }

    /// <summary>构造一张带初始价格的表。</summary>
    /// <remarks>
    /// <b>价格 0 = "不卖"</b>，合法。<br/>
    /// <b>价格负</b> = 构造期直接拒 —— 与 I12 防线一致的"不为负"原则。
    /// </remarks>
    /// <param name="initial">初始价格清单。</param>
    /// <exception cref="ArgumentException">初始清单中任意价格 <b>负数</b>。</exception>
    public PriceBook(IEnumerable<KeyValuePair<ItemId, long>> initial)
    {
        if (initial is null) throw new ArgumentNullException(nameof(initial));

        _prices = new Dictionary<ItemId, long>(ItemIdComparer.Comparer);
        foreach (var kv in initial)
        {
            // M1 缺口：ItemId 自身没拒空值（只是不能为 null）。
            // 价格表**多守一道**：空 Id 是不合法的商品标识 —— 不让"商品名称空字符串"溜进表里。
            if (string.IsNullOrEmpty(kv.Key.Value))
            {
                throw new ArgumentException(
                    $"商品 Id 不得为空字符串。", nameof(initial));
            }

            if (kv.Value < 0)
            {
                throw new ArgumentException(
                    $"价格不得为负：{kv.Key.Value} = {kv.Value}。", nameof(initial));
            }

            _prices[kv.Key] = kv.Value;
        }
    }

    /// <summary>该物品的单价。<b>未登记返回 0</b> —— 0 = "不卖"。</summary>
    public long PriceOf(ItemId id)
        => _prices.TryGetValue(id, out var p) ? p : 0L;

    /// <summary>该物品<b>是否登记</b>为可售（价格 &gt; 0）。</summary>
    public bool IsSellable(ItemId id)
        => _prices.TryGetValue(id, out var p) && p > 0;

    /// <summary>
    /// 替换整个价格表。<b>仅供读档（M7）</b>。
    /// </summary>
    /// <remarks>
    /// 调用方应从 <c>SaveValidator.Validate</c> 通过后的存档里拿值。
    /// 运行中调它就是绕过"价格统一"不变量 —— 不在测试纪律下做事的人会这么做。
    /// </remarks>
    public PriceBook WithPrices(IEnumerable<KeyValuePair<ItemId, long>> prices)
    {
        if (prices is null) throw new ArgumentNullException(nameof(prices));
        return new PriceBook(prices);
    }

    /// <summary>
    /// 默认价格表（<b>【待定 T-007 占位】</b>）。
    /// </summary>
    /// <remarks>
    /// <para>这 3 个数只是<b>占位</b> —— 让 M2 的 Trade 能跑起来、有东西可买。<br/>
    /// D-021 / P2-D03 拍的是"固定表"这条机制，<b>不是</b>这几个具体价格。
    /// 实际价格由开发者与 ChatGPT 商定后定（很可能放在外部配置里 —— 见 TODO）。</para>
    /// <para><b>【D-054 阶段 7 · 2026-10-21】外部价格表已落地</b>：
    /// <c>config/prices.json</c>（放在带 <c>.gdignore</c> 的目录里 ⇒ Godot 不会导入它）。
    /// 找不到 / 解析失败 ⇒ <b>退回本占位表</b>（行为与之前完全一致）。
    /// ⇒ 「开发者定价格 → 我重跑标定」这条路现在是**改一个文件 + 跑一条命令**。</para>
    /// </remarks>
    public static PriceBook Default { get; } = new PriceBook(new[]
    {
        new KeyValuePair<ItemId, long>(new ItemId("石料"), 5L),
        new KeyValuePair<ItemId, long>(new ItemId("木料"), 3L),
        new KeyValuePair<ItemId, long>(new ItemId("布匹"), 8L),
    });

    /// <summary>
    /// 从 <b>JSON 文本</b>读一张价格表。
    /// </summary>
    /// <remarks>
    /// <para><b>格式</b>：一个扁平对象，键 = 物品名，值 = 单价。
    /// <c>{ "石料": 5, "木料": 3, "布匹": 8 }</c></para>
    /// <para><b>以 <c>_</c> 开头的键被当注释忽略</b> —— 这个文件是给人手改的，
    /// 让人能写两行说明而不必担心解析失败。</para>
    /// <para><b>为什么用 <c>System.Text.Json</c> 而不是 Godot 的 JSON</b>：
    /// Godot 的 JSON 是 <c>Variant</c> 形状的，与强类型对象对不上（与
    /// <see cref="ClaudeCodeGameStudios.Data.SavePort"/> 同一个理由）。</para>
    /// </remarks>
    /// <param name="json">JSON 文本。</param>
    /// <param name="book">读出来的价格表（成功时）。</param>
    /// <param name="error">失败原因（成功时为 null）。</param>
    /// <returns>成功返回 true。</returns>
    public static bool TryLoadFromJson(string json, out PriceBook? book, out string? error)
    {
        book = null;
        error = null;

        if (string.IsNullOrWhiteSpace(json))
        {
            error = "价格表文件是空的。";
            return false;
        }

        Dictionary<string, System.Text.Json.JsonElement>? raw;
        try
        {
            // ⚠️ **必须按 JsonElement 读，不能按 long 读**：注释键的值是**字符串**，
            // 按 long 反序列化会让整个文件抛 JsonException ——
            // 那正是「带注释的价格表读不出来」的原因（2026-10-21 实测踩到）。
            raw = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, System.Text.Json.JsonElement>>(json);
        }
        catch (System.Text.Json.JsonException ex)
        {
            error = $"价格表不是合法的 JSON：{ex.Message}";
            return false;
        }

        if (raw is null || raw.Count == 0)
        {
            error = "价格表里没有任何条目。";
            return false;
        }

        var prices = new List<KeyValuePair<ItemId, long>>();
        foreach (var kv in raw)
        {
            if (kv.Key.StartsWith("_", StringComparison.Ordinal)) continue;   // 注释
            if (string.IsNullOrEmpty(kv.Key))
            {
                error = "价格表里有空物品名。";
                return false;
            }

            if (kv.Value.ValueKind != System.Text.Json.JsonValueKind.Number
                || !kv.Value.TryGetInt64(out long value))
            {
                error = $"价格必须是整数：{kv.Key}。";
                return false;
            }

            if (value < 0)
            {
                error = $"价格不得为负：{kv.Key} = {value}。";
                return false;
            }

            prices.Add(new KeyValuePair<ItemId, long>(new ItemId(kv.Key), value));
        }

        if (prices.Count == 0)
        {
            error = "价格表里只有注释，没有任何价格。";
            return false;
        }

        book = new PriceBook(prices);
        return true;
    }

    /// <summary>
    /// 从<b>文件</b>读一张价格表（读不到 / 解析失败都返回 false，<b>不抛</b>）。
    /// </summary>
    /// <param name="path">文件路径。</param>
    /// <param name="book">读出来的价格表（成功时）。</param>
    /// <param name="error">失败原因（成功时为 null）。</param>
    /// <returns>成功返回 true。</returns>
    public static bool TryLoadFromFile(string path, out PriceBook? book, out string? error)
    {
        book = null;
        error = null;

        if (string.IsNullOrWhiteSpace(path))
        {
            error = "价格表路径为空。";
            return false;
        }

        string json;
        try
        {
            if (!System.IO.File.Exists(path))
            {
                error = "文件不存在。";
                return false;
            }

            json = System.IO.File.ReadAllText(path);
        }
        catch (System.IO.IOException ex)
        {
            error = $"读价格表失败：{ex.Message}";
            return false;
        }
        catch (UnauthorizedAccessException ex)
        {
            error = $"读价格表失败（无权限）：{ex.Message}";
            return false;
        }

        return TryLoadFromJson(json, out book, out error);
    }

    /// <summary>这张表的<b>摘要</b>（物品名 → 单价，按物品名排序）—— 写进采集日志，便于溯源。</summary>
    public string Describe()
    {
        var parts = _prices
            .OrderBy(kv => kv.Key.Value, StringComparer.Ordinal)
            .Select(kv => $"{kv.Key.Value}={kv.Value}");
        return string.Join(" ", parts);
    }
}
