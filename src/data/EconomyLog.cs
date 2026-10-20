using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ClaudeCodeGameStudios.Domain.Events;

namespace ClaudeCodeGameStudios.Data;

/// <summary>副本掉落行里的一件物品。</summary>
/// <param name="Item">物品标识。</param>
/// <param name="Qty">份数。</param>
public sealed record LootLine(string Item, long Qty);

/// <summary>副本掉落行里的一份发放（谁拿到了什么）。</summary>
/// <param name="Hero">英雄标识。</param>
/// <param name="Item">物品标识。</param>
/// <param name="Qty">份数。</param>
public sealed record GrantLine(string Hero, string Item, long Qty);

/// <summary>
/// 经济流水的一行（JSONL 的一行）。
/// </summary>
/// <remarks>
/// <para><b>为什么是「一个扁平类型 + 可空字段」</b>：流水只有两种行
/// （副本掉落 / 交易），字段集合不同。拆成两个类型则分析脚本要写两套解析；
/// 扁平类型则每一行都有同一组键，<c>jq</c> / pandas 一把梭。</para>
/// <para><b>标识与时间</b>：<see cref="Id"/> 就是领域事件 ID ——
/// 它由「类型 + 参与者 + 序号」确定性生成，<b>跨运行可复现</b>，且天然去重。
/// 用它当交易/副本的业务标识，不需要另造一套编号体系。</para>
/// </remarks>
public sealed record EconomyRow
{
    /// <summary>行类别：<c>DungeonLooted</c> / <c>TradeCompleted</c>。</summary>
    public string Kind { get; init; } = "";

    /// <summary>唯一标识（领域事件 ID）。</summary>
    public string Id { get; init; } = "";

    /// <summary>游戏日（从 1 起）。</summary>
    public long Day { get; init; }

    /// <summary>模拟时间（模拟秒）。</summary>
    public double SimTime { get; init; }

    /// <summary>参与者（副本标识 / 买家名）。</summary>
    public string Actor { get; init; } = "";

    // ── 副本结算 ──

    /// <summary>副本实例标识。</summary>
    public string? RunId { get; init; }

    /// <summary>副本名称。</summary>
    public string? Place { get; init; }

    /// <summary>副本是否成功。</summary>
    public bool? Won { get; init; }

    /// <summary>实际参战名单。</summary>
    public string[]? Participants { get; init; }

    /// <summary>本次产出的战利品（未分配前）。</summary>
    public LootLine[]? Loot { get; init; }

    /// <summary>最终分配结果（谁拿到了什么）。</summary>
    public GrantLine[]? Grants { get; init; }

    // ── 交易 ──

    /// <summary>买方英雄标识。</summary>
    public string? Buyer { get; init; }

    /// <summary>卖方英雄标识。</summary>
    public string? Seller { get; init; }

    /// <summary>商品标识。</summary>
    public string? Item { get; init; }

    /// <summary>成交数量。</summary>
    public long? Qty { get; init; }

    /// <summary>买家实付（= 摊主净得 + 村抽）。</summary>
    public long? Amount { get; init; }

    /// <summary>摊主净得。</summary>
    public long? SellerNet { get; init; }

    /// <summary>村金库抽成。</summary>
    public long? VillageCut { get; init; }
}

/// <summary>
/// 经济流水落盘 —— JSONL 追加写（运行闭环验证与经济数据采集准备）。
/// </summary>
/// <remarks>
/// <para><b>只落「经济流水」，不落全事件流</b>：全事件流里绝大多数是
/// 日常劳作与社交（37 人 × 每天几条），与分析经济平衡无关，写进去只是让文件变大。
/// 这里只收两类：<b>副本结算</b>与<b>交易</b>。</para>
///
/// <para><b>为什么用 JSONL 而不是 CSV</b>：两类行的字段集合不同，
/// CSV 要么留一堆空列，要么拆两个文件；JSONL 是一行一个对象，
/// 缺字段就不出现，<c>jq</c> 与 pandas 都能直接吃。</para>
///
/// <para><b>为什么不用 <c>FileAccess</c> + Godot 的 <c>JSON</c></b>：
/// 与 <see cref="SavePort"/> 同一个理由 —— Godot 的 JSON 是 <c>Variant</c> 形状的，
/// 与强类型对象对不上；<c>System.Text.Json</c> 在 .NET 里，
/// 不给这一层引入 Godot 依赖。</para>
///
/// <para><b>写盘是追加且立即 flush</b>：采集要跑很久，
/// 中途崩了不能丢掉已经采到的行。存档走的是「临时文件 + 替换」的原子语义，
/// 流水不需要 —— 它是<b>流式</b>数据，半行总比没数据强。</para>
/// </remarks>
public sealed class EconomyLog : IDisposable
{
    /// <summary>副本结算行的类别标识（每场副本一行，成功失败都有）。</summary>
    public const string DungeonSettledKind = "DungeonSettled";

    /// <summary>副本掉落行的类别标识（成功且有战利品时才有一行）。</summary>
    public const string DungeonLootKind = "DungeonLooted";

    /// <summary>交易行的类别标识。</summary>
    public const string TradeKind = "TradeCompleted";

    /// <summary>条目分隔符（物品名不含逗号）。</summary>
    public const char EntrySeparator = ',';

    /// <summary>英雄与物品之间的分隔符（英雄标识不含冒号）。</summary>
    public const char HeroSeparator = ':';

    /// <summary>物品与数量之间的分隔符。</summary>
    public const char QuantitySeparator = '×';

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = false,
        // 模拟时间是 double；写出来要能读回来做统计，精度不能丢。
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly string _path;
    private readonly double _simSecondsPerDay;
    private StreamWriter? _writer;
    private long _rows;

    /// <param name="path">落盘路径（由调用方注入，不写死 <c>user://</c>）。</param>
    /// <param name="simSecondsPerDay">一个模拟日的秒数（用来把模拟时间换算成游戏日）。</param>
    /// <exception cref="ArgumentException"><paramref name="path"/> 为空。</exception>
    public EconomyLog(string path, double simSecondsPerDay)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("流水路径不能为空。", nameof(path));
        }

        _path = path;
        _simSecondsPerDay = simSecondsPerDay > 0.0 ? simSecondsPerDay : 1.0;

        string? directory = System.IO.Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        _writer = new StreamWriter(
            new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false))
        {
            AutoFlush = true,   // 采集要跑很久：崩了不能丢掉已采到的行
        };
    }

    /// <summary>落盘路径。</summary>
    public string FilePath => _path;

    /// <summary>已写入的行数。</summary>
    public long RowCount => _rows;

    /// <summary>
    /// 追加一个事件。<b>只收经济类</b>；其余事件一律忽略。
    /// </summary>
    /// <param name="e">领域事件。</param>
    /// <returns>写入了就 true；不是经济事件或载荷不完整就 false。</returns>
    /// <remarks>
    /// <b>载荷缺字段就跳过</b>，不写半行 —— 半行 JSON 会在分析脚本里变成
    /// 一条「无法解析」的噪声，还会让行数对不上。
    /// </remarks>
    public bool Append(DomainEvent e)
    {
        if (e is null) return false;
        if (_writer is null) return false;

        EconomyRow? row = e.Type.Value switch
        {
            DungeonSettledKind => BuildDungeonSettledRow(e),
            DungeonLootKind => BuildDungeonLootRow(e),
            TradeKind => BuildTradeRow(e),
            _ => null,
        };

        if (row is null) return false;

        _writer.WriteLine(JsonSerializer.Serialize(row, Options));
        _rows++;
        return true;
    }

    /// <summary>关掉文件（flush 并释放句柄）。<b>可重复调用</b>。</summary>
    /// <remarks>
    /// 幂等是必需的：<c>using</c> 与显式 <c>Close()</c> 都会走到这里，
    /// 对已释放的 <c>StreamWriter</c> 再 Flush 会抛「Cannot write to a closed TextWriter」。
    /// </remarks>
    public void Close()
    {
        if (_writer is null) return;
        _writer.Flush();
        _writer.Dispose();
        _writer = null;   // 幂等：第二次 Close 直接返回
    }

    /// <summary>释放文件句柄（等价于 <see cref="Close"/>）。</summary>
    public void Dispose() => Close();

    // ── 行构建 ───────────────────────────────────────────────────────────

    /// <summary>由「整场结论」事件构建一行（<b>每场副本一行</b>，成功失败都有）。</summary>
    /// <param name="e">事件。</param>
    /// <returns>行；没有 runId 就不是副本结算行（返回 null）。</returns>
    /// <remarks>
    /// <b>判据是「有 runId」</b>：同名的 <c>DungeonSettled</c> 还有另外几条
    /// （队伍归来、带回战利品、白跑一趟），它们没有 runId —— 落盘时自然被跳过。
    /// 有了这一行，<b>副本成功率</b>才算得出来：战利品只在成功时才有，
    /// 只记掉落的话失败场次永远采不到。
    /// </remarks>
    private EconomyRow? BuildDungeonSettledRow(DomainEvent e)
    {
        if (!e.Payload.TryGetValue("runId", out var runId) || string.IsNullOrEmpty(runId)) return null;

        bool won = e.Payload.TryGetValue("won", out var wonText) && wonText == "1";

        return new EconomyRow
        {
            Kind = DungeonSettledKind,
            Id = e.Id.Value,
            Day = DayOf(e.OccurredAtSimSeconds),
            SimTime = e.OccurredAtSimSeconds,
            Actor = e.Actor,
            RunId = runId,
            Place = e.Payload.TryGetValue("place", out var place) ? place : null,
            Won = won,
            Participants = SplitList(e.Payload.TryGetValue("participants", out var p) ? p : null),
        };
    }

    /// <summary>由副本结算事件构建一行。</summary>
    /// <param name="e">事件。</param>
    /// <returns>行；载荷不完整时为 null。</returns>
    private EconomyRow? BuildDungeonLootRow(DomainEvent e)
    {
        if (!e.Payload.TryGetValue("runId", out var runId) || string.IsNullOrEmpty(runId)) return null;
        if (!e.Payload.TryGetValue("loot", out var lootText)) return null;
        if (!e.Payload.TryGetValue("grants", out var grantsText)) return null;

        bool won = e.Payload.TryGetValue("won", out var wonText) && wonText == "1";

        return new EconomyRow
        {
            Kind = DungeonLootKind,
            Id = e.Id.Value,
            Day = DayOf(e.OccurredAtSimSeconds),
            SimTime = e.OccurredAtSimSeconds,
            Actor = e.Actor,
            RunId = runId,
            Place = e.Payload.TryGetValue("place", out var place) ? place : null,
            Won = won,
            Participants = SplitList(e.Payload.TryGetValue("participants", out var p) ? p : null),
            Loot = ParseLoot(lootText),
            Grants = ParseGrants(grantsText),
        };
    }

    /// <summary>由交易事件构建一行。</summary>
    /// <param name="e">事件。</param>
    /// <returns>行；载荷不完整时为 null。</returns>
    private EconomyRow? BuildTradeRow(DomainEvent e)
    {
        if (!e.Payload.TryGetValue("buyer", out var buyer) || string.IsNullOrEmpty(buyer)) return null;
        if (!e.Payload.TryGetValue("seller", out var seller) || string.IsNullOrEmpty(seller)) return null;
        if (!e.Payload.TryGetValue("item", out var item) || string.IsNullOrEmpty(item)) return null;

        if (!e.Payload.TryGetValue("qty", out var qtyText) || !long.TryParse(qtyText, out var qty)) return null;
        if (!e.Payload.TryGetValue("amount", out var amountText) || !long.TryParse(amountText, out var amount)) return null;

        long sellerNet = ReadLong(e.Payload, "sellerNet") ?? 0L;
        long villageCut = ReadLong(e.Payload, "villageCut") ?? 0L;

        return new EconomyRow
        {
            Kind = TradeKind,
            Id = e.Id.Value,
            Day = DayOf(e.OccurredAtSimSeconds),
            SimTime = e.OccurredAtSimSeconds,
            Actor = buyer,
            Buyer = buyer,
            Seller = seller,
            Item = item,
            Qty = qty,
            Amount = amount,
            SellerNet = sellerNet,
            VillageCut = villageCut,
        };
    }

    private long DayOf(double simTime)
        => (long)(simTime / _simSecondsPerDay) + 1L;

    private static long? ReadLong(IReadOnlyDictionary<string, string> payload, string key)
        => payload.TryGetValue(key, out var raw) && long.TryParse(raw, out var value) ? value : null;

    // ── 紧凑格式解析 ─────────────────────────────────────────────────────

    /// <summary>
    /// 解析战利品串：<c>石料×2,木料×1</c>。
    /// </summary>
    /// <param name="text">紧凑串。</param>
    /// <returns>解析出的条目；空串返回空数组。</returns>
    /// <remarks>
    /// <b>格式约定</b>：物品名不含逗号与「×」。当前物品（石料 / 木料 / 布匹）满足。
    /// 若将来引入含这些字符的物品名，要改这里 —— <b>不要默默产出错数据</b>。
    /// </remarks>
    public static LootLine[] ParseLoot(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return Array.Empty<LootLine>();

        var lines = new List<LootLine>();
        foreach (var part in text.Split(EntrySeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var idx = part.LastIndexOf(QuantitySeparator);
            if (idx <= 0) continue;

            string item = part[..idx];
            if (!long.TryParse(part[(idx + 1)..], NumberStyles.Integer, CultureInfo.InvariantCulture, out var qty)) continue;
            if (qty <= 0) continue;

            lines.Add(new LootLine(item, qty));
        }

        return lines.ToArray();
    }

    /// <summary>
    /// 解析分配串：<c>h-0:石料×2,h-1:木料×1</c>。
    /// </summary>
    /// <param name="text">紧凑串。</param>
    /// <returns>解析出的发放；空串返回空数组。</returns>
    public static GrantLine[] ParseGrants(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return Array.Empty<GrantLine>();

        var lines = new List<GrantLine>();
        foreach (var part in text.Split(EntrySeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var sep = part.IndexOf(HeroSeparator);
            if (sep <= 0) continue;

            string hero = part[..sep];
            var rest = part[(sep + 1)..];

            var idx = rest.LastIndexOf(QuantitySeparator);
            if (idx <= 0) continue;

            string item = rest[..idx];
            if (!long.TryParse(rest[(idx + 1)..], NumberStyles.Integer, CultureInfo.InvariantCulture, out var qty)) continue;
            if (qty <= 0) continue;

            lines.Add(new GrantLine(hero, item, qty));
        }

        return lines.ToArray();
    }

    /// <summary>把条目列表拼成紧凑串。</summary>
    /// <param name="items">条目。</param>
    /// <returns>紧凑串；空列表返回空串。</returns>
    public static string FormatLoot(IEnumerable<LootLine> items)
        => string.Join(EntrySeparator,
            items.Select(i => $"{i.Item}{QuantitySeparator}{i.Qty}"));

    /// <summary>把发放列表拼成紧凑串。</summary>
    /// <param name="items">发放。</param>
    /// <returns>紧凑串；空列表返回空串。</returns>
    public static string FormatGrants(IEnumerable<GrantLine> items)
        => string.Join(EntrySeparator,
            items.Select(g => $"{g.Hero}{HeroSeparator}{g.Item}{QuantitySeparator}{g.Qty}"));

    /// <summary>把标识列表拼成逗号分隔串。</summary>
    /// <param name="ids">标识。</param>
    /// <returns>逗号分隔串；空列表返回空串。</returns>
    public static string FormatIds(IEnumerable<string> ids) => string.Join(EntrySeparator, ids);

    private static string[] SplitList(string? text)
        => string.IsNullOrWhiteSpace(text)
            ? Array.Empty<string>()
            : text.Split(EntrySeparator, StringSplitOptions.RemoveEmptyEntries);
}
