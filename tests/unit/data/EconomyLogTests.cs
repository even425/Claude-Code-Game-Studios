using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using GdUnit4;
using ClaudeCodeGameStudios.Data;
using ClaudeCodeGameStudios.Domain.Events;

namespace ClaudeCodeGameStudios.Tests.Data;

/// <summary>
/// 经济流水落盘的契约（运行闭环验证与经济数据采集准备）。
/// </summary>
/// <remarks>
/// <para><b>为什么测它</b>：流水是 R-05 二轮的**唯一数据来源** ——
/// 掉落分布、成交价分布、货币流动全靠它。落错了、漏了、写成半行，
/// 分析脚本就会得到「看起来有数据、其实不对」的结果。</para>
/// </remarks>
[TestSuite]
public class EconomyLogTests
{
    private const double SimSecondsPerDay = 75.0;
    private const string Dungeon = "北境旧堡";

    private static readonly EventTypeId LootType = new("DungeonLooted");
    private static readonly EventTypeId TradeType = new("TradeCompleted");
    private static readonly EventTypeId OtherType = new("HeroSummoned");

    private static readonly StateKey StHeroBag = new("hero.bag");

    /// <summary>一个临时文件路径（每个用例一份，避免互相干扰）。</summary>
    private static string TempPath(string tag)
        => Path.Combine(Path.GetTempPath(), $"econ-log-test-{tag}-{Guid.NewGuid():N}.jsonl");

    private static DomainEvent LootEvent(
        string runId, string loot, string grants, string participants = "h-0,h-1")
        => DomainEvent.Create(
            new EventId($"DungeonLooted:{runId}:0"), LootType, runId,
            occurredAtSimSeconds: 7 * SimSecondsPerDay,
            changedStates: new[] { StHeroBag },
            payload: new Dictionary<string, string>
            {
                ["runId"] = runId,
                ["place"] = Dungeon,
                ["won"] = "1",
                ["participants"] = participants,
                ["loot"] = loot,
                ["grants"] = grants,
            });

    private static DomainEvent TradeEvent(
        string buyer, string seller, string item, long qty, long amount, long sellerNet, long cut)
        => DomainEvent.Create(
            new EventId($"TradeCompleted:{buyer}:0"), TradeType, buyer,
            occurredAtSimSeconds: 7 * SimSecondsPerDay,
            changedStates: new[] { StHeroBag },
            payload: new Dictionary<string, string>
            {
                ["buyer"] = buyer,
                ["seller"] = seller,
                ["item"] = item,
                ["qty"] = qty.ToString(),
                ["amount"] = amount.ToString(),
                ["sellerNet"] = sellerNet.ToString(),
                ["villageCut"] = cut.ToString(),
            });

    // ── 紧凑格式往返 ─────────────────────────────────────────────────────

    [TestCase]
    public void FormatLoot_ThenParse_RoundTrips()
    {
        var items = new[]
        {
            new LootLine("石料", 2),
            new LootLine("木料", 1),
        };

        string text = EconomyLog.FormatLoot(items);
        var parsed = EconomyLog.ParseLoot(text);

        Assertions.AssertThat(text).IsEqual("石料×2,木料×1");
        Assertions.AssertThat(parsed.Length).IsEqual(2);
        Assertions.AssertThat(parsed[0].Item).IsEqual("石料");
        Assertions.AssertThat(parsed[0].Qty).IsEqual(2L);
        Assertions.AssertThat(parsed[1].Item).IsEqual("木料");
    }

    [TestCase]
    public void FormatGrants_ThenParse_RoundTrips()
    {
        var items = new[]
        {
            new GrantLine("h-0", "石料", 2),
            new GrantLine("h-1", "木料", 1),
        };

        string text = EconomyLog.FormatGrants(items);
        var parsed = EconomyLog.ParseGrants(text);

        Assertions.AssertThat(text).IsEqual("h-0:石料×2,h-1:木料×1");
        Assertions.AssertThat(parsed.Length).IsEqual(2);
        Assertions.AssertThat(parsed[0].Hero).IsEqual("h-0");
        Assertions.AssertThat(parsed[0].Item).IsEqual("石料");
        Assertions.AssertThat(parsed[0].Qty).IsEqual(2L);
    }

    [TestCase]
    public void ParseLoot_Garbage_ReturnsEmptyRatherThanJunk()
    {
        Assertions.AssertThat(EconomyLog.ParseLoot("石料").Length).IsEqual(0);     // 缺数量
        Assertions.AssertThat(EconomyLog.ParseLoot("石料×abc").Length).IsEqual(0);  // 数量不是数
        Assertions.AssertThat(EconomyLog.ParseLoot("石料×0").Length).IsEqual(0);    // 数量为 0
        Assertions.AssertThat(EconomyLog.ParseLoot(null).Length).IsEqual(0);
    }

    [TestCase]
    public void ParseGrants_Garbage_ReturnsEmptyRatherThanJunk()
    {
        Assertions.AssertThat(EconomyLog.ParseGrants("石料×2").Length).IsEqual(0);   // 缺英雄
        Assertions.AssertThat(EconomyLog.ParseGrants(":石料×2").Length).IsEqual(0);  // 英雄为空
        Assertions.AssertThat(EconomyLog.ParseGrants("h-0:石料×x").Length).IsEqual(0);
    }

    // ── 事件 → 行 ────────────────────────────────────────────────────────

    [TestCase]
    public void Append_DungeonLootEvent_BuildsRowWithParsedLists()
    {
        string path = TempPath("loot");
        try
        {
            using var log = new EconomyLog(path, SimSecondsPerDay);
            bool written = log.Append(LootEvent(
                "d10.1.tok", "石料×2,木料×1", "h-0:石料×2,h-1:木料×1"));

            Assertions.AssertThat(written).IsTrue();
            Assertions.AssertThat(log.RowCount).IsEqual(1L);

            log.Close();   // Windows：活跃写句柄与读冲突 —— 先关再读（真实用法也是采完再分析）
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            var row = doc.RootElement;

            Assertions.AssertThat(row.GetProperty("Kind").GetString()).IsEqual("DungeonLooted");
            Assertions.AssertThat(row.GetProperty("RunId").GetString()).IsEqual("d10.1.tok");
            Assertions.AssertThat(row.GetProperty("Won").GetBoolean()).IsTrue();
            Assertions.AssertThat(row.GetProperty("Day").GetInt64()).IsEqual(8L);   // 7 天整 + 1
            Assertions.AssertThat(row.GetProperty("Loot").GetArrayLength()).IsEqual(2);
            Assertions.AssertThat(row.GetProperty("Grants").GetArrayLength()).IsEqual(2);
            Assertions.AssertThat(row.GetProperty("Participants").GetArrayLength()).IsEqual(2);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [TestCase]
    public void Append_TradeEvent_BuildsRowWithNumericFields()
    {
        string path = TempPath("trade");
        try
        {
            using var log = new EconomyLog(path, SimSecondsPerDay);
            bool written = log.Append(TradeEvent("甲", "乙", "石料", 3, 15, 14, 1));

            Assertions.AssertThat(written).IsTrue();

            log.Close();
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            var row = doc.RootElement;

            Assertions.AssertThat(row.GetProperty("Kind").GetString()).IsEqual("TradeCompleted");
            Assertions.AssertThat(row.GetProperty("Buyer").GetString()).IsEqual("甲");
            Assertions.AssertThat(row.GetProperty("Seller").GetString()).IsEqual("乙");
            Assertions.AssertThat(row.GetProperty("Item").GetString()).IsEqual("石料");
            Assertions.AssertThat(row.GetProperty("Qty").GetInt64()).IsEqual(3L);
            Assertions.AssertThat(row.GetProperty("Amount").GetInt64()).IsEqual(15L);
            Assertions.AssertThat(row.GetProperty("SellerNet").GetInt64()).IsEqual(14L);
            Assertions.AssertThat(row.GetProperty("VillageCut").GetInt64()).IsEqual(1L);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [TestCase]
    public void Append_NonEconomicEvent_IsIgnored()
    {
        string path = TempPath("other");
        try
        {
            using var log = new EconomyLog(path, SimSecondsPerDay);
            var e = DomainEvent.Create(
                new EventId("HeroSummoned:甲:0"), OtherType, "甲",
                occurredAtSimSeconds: 1.0, changedStates: new[] { StHeroBag });

            Assertions.AssertThat(log.Append(e)).IsFalse();
            Assertions.AssertThat(log.RowCount).IsEqual(0L);
            log.Close();
            Assertions.AssertThat(File.ReadAllText(path)).IsEmpty();
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [TestCase]
    public void Append_IncompletePayload_IsSkippedRatherThanHalfWritten()
    {
        string path = TempPath("partial");
        try
        {
            using var log = new EconomyLog(path, SimSecondsPerDay);

            // 交易事件缺 item 与 qty —— 写出去会是半行，分析脚本读不了。
            var broken = DomainEvent.Create(
                new EventId("TradeCompleted:甲:0"), TradeType, "甲",
                occurredAtSimSeconds: 1.0, changedStates: new[] { StHeroBag },
                payload: new Dictionary<string, string>
                {
                    ["buyer"] = "甲",
                    ["seller"] = "乙",
                });

            Assertions.AssertThat(log.Append(broken)).IsFalse();
            Assertions.AssertThat(log.RowCount).IsEqual(0L);
            log.Close();
            Assertions.AssertThat(File.ReadAllText(path)).IsEmpty();
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [TestCase]
    public void Append_DungeonSettledEvent_BuildsOneRowPerDungeon()
    {
        string path = TempPath("settled");
        try
        {
            using var log = new EconomyLog(path, SimSecondsPerDay);

            // 失败的一局也要有行 —— 否则副本成功率算不出来（战利品只在成功时才有）。
            var failed = DomainEvent.Create(
                new EventId("DungeonSettled:北境旧堡:0"), new EventTypeId("DungeonSettled"), "北境旧堡",
                occurredAtSimSeconds: 3 * SimSecondsPerDay,
                changedStates: new[] { StHeroBag },
                payload: new Dictionary<string, string>
                {
                    ["place"] = Dungeon,
                    ["won"] = "0",
                    ["names"] = "甲、乙",
                    ["runId"] = "d10.1.tokA",
                    ["participants"] = "h-0,h-1",
                });

            Assertions.AssertThat(log.Append(failed)).IsTrue();

            log.Close();
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            var row = doc.RootElement;

            Assertions.AssertThat(row.GetProperty("Kind").GetString()).IsEqual("DungeonSettled");
            Assertions.AssertThat(row.GetProperty("RunId").GetString()).IsEqual("d10.1.tokA");
            Assertions.AssertThat(row.GetProperty("Won").GetBoolean()).IsFalse();
            Assertions.AssertThat(row.GetProperty("Participants").GetArrayLength()).IsEqual(2);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [TestCase]
    public void Append_DungeonSettledEvent_WithoutRunId_IsSkipped()
    {
        string path = TempPath("settled-norun");
        try
        {
            using var log = new EconomyLog(path, SimSecondsPerDay);

            // 同名的另一条（队伍归来 / 带回战利品 / 白跑一趟）—— 没有 runId，不是副本结算行。
            var other = DomainEvent.Create(
                new EventId("DungeonSettled:甲:0"), new EventTypeId("DungeonSettled"), "甲",
                occurredAtSimSeconds: 3 * SimSecondsPerDay,
                changedStates: new[] { StHeroBag },
                payload: new Dictionary<string, string>
                {
                    ["place"] = Dungeon,
                    ["names"] = "甲、乙",
                    ["loot"] = "甲 石料×1",
                });

            Assertions.AssertThat(log.Append(other)).IsFalse();
            Assertions.AssertThat(log.RowCount).IsEqual(0L);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [TestCase]
    public void Append_ManyEvents_WritesOneJsonLineEach()
    {
        string path = TempPath("many");
        try
        {
            using var log = new EconomyLog(path, SimSecondsPerDay);
            for (int i = 0; i < 5; i++)
            {
                log.Append(TradeEvent("甲", "乙", "石料", 1, 5, 5, 0));
            }

            log.Close();
            string[] lines = File.ReadAllLines(path);

            Assertions.AssertThat(lines.Length).IsEqual(5);
            Assertions.AssertThat(log.RowCount).IsEqual(5L);
            foreach (var line in lines)
            {
                // 每行都必须是**独立合法**的 JSON —— 半行会让分析脚本整份报错。
                using var doc = JsonDocument.Parse(line);
                Assertions.AssertThat(doc.RootElement.GetProperty("Kind").GetString())
                    .IsEqual("TradeCompleted");
            }
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    // ── 未成原因（D-053：补上测量缺口） ───────────────────────────────

    /// <summary>
    /// 「想买未成」落一行：原因、双方、物品、日期都要在。
    /// </summary>
    /// <remarks>
    /// <b>为什么这条必须有</b>：R-05 二轮发现「成交笔数腰斩」，
    /// 根因一直只能靠代码推断 —— 因为流水里<b>没记未成原因</b>。
    /// 不记就没有分母，「背包满占未成原因多少」永远算不出来。
    /// </remarks>
    [TestCase]
    public void AppendShoppingMissed_WritesRowWithReasonAndDay()
    {
        string path = TempPath("missed");
        try
        {
            using var log = new EconomyLog(path, SimSecondsPerDay);
            bool written = log.AppendShoppingMissed(
                7 * SimSecondsPerDay, "甲", "BagFull", "乙", "石料");

            Assertions.AssertThat(written).IsTrue();
            Assertions.AssertThat(log.RowCount).IsEqual(1L);

            log.Close();   // Windows：活跃写句柄与读冲突 —— 先关再读
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            var row = doc.RootElement;

            Assertions.AssertThat(row.GetProperty("Kind").GetString()).IsEqual("ShoppingMissed");
            Assertions.AssertThat(row.GetProperty("Reason").GetString()).IsEqual("BagFull");
            Assertions.AssertThat(row.GetProperty("Actor").GetString()).IsEqual("甲");
            Assertions.AssertThat(row.GetProperty("Buyer").GetString()).IsEqual("甲");
            Assertions.AssertThat(row.GetProperty("Seller").GetString()).IsEqual("乙");
            Assertions.AssertThat(row.GetProperty("Item").GetString()).IsEqual("石料");
            Assertions.AssertThat(row.GetProperty("Day").GetInt64()).IsEqual(8L);   // 7 天整 + 1
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    /// <summary>
    /// D-054 阶段 5：未成行还要记<b>买家余额</b>与<b>最便宜可买价</b>。
    /// </summary>
    /// <remarks>
    /// 没有这两个字段，「未成·钱不够」就<b>分不清</b>
    /// ① 英雄真没钱（经济事实）② 英雄有钱但抽签抽到贵的（抽样运气）。
    /// </remarks>
    [TestCase]
    public void AppendShoppingMissed_WritesBuyerGoldAndCheapestPrice()
    {
        string path = TempPath("missed-afford");
        try
        {
            using var log = new EconomyLog(path, SimSecondsPerDay);
            bool written = log.AppendShoppingMissed(
                7 * SimSecondsPerDay, "甲", "NotEnoughGold", "乙", "石料",
                buyerGold: 4L, cheapestPrice: 3L);

            Assertions.AssertThat(written).IsTrue();

            log.Close();   // Windows：活跃写句柄与读冲突 —— 先关再读
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            var row = doc.RootElement;

            Assertions.AssertThat(row.GetProperty("BuyerGold").GetInt64()).IsEqual(4L);
            Assertions.AssertThat(row.GetProperty("CheapestPrice").GetInt64()).IsEqual(3L);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    /// <summary>不传这两个数 → 字段为 <c>null</c>（旧样本没有它们，分析脚本据此判「无法判定」）。</summary>
    [TestCase]
    public void AppendShoppingMissed_WithoutAffordability_LeavesFieldsNull()
    {
        string path = TempPath("missed-noafford");
        try
        {
            using var log = new EconomyLog(path, SimSecondsPerDay);
            log.AppendShoppingMissed(1.0, "甲", "BagFull");

            log.Close();
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            var row = doc.RootElement;

            Assertions.AssertThat(row.GetProperty("BuyerGold").ValueKind)
                .IsEqual(System.Text.Json.JsonValueKind.Null);
            Assertions.AssertThat(row.GetProperty("CheapestPrice").ValueKind)
                .IsEqual(System.Text.Json.JsonValueKind.Null);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    /// <summary>缺必要字段 → 拒写（不写半行）。</summary>
    [TestCase]
    public void AppendShoppingMissed_EmptyHeroOrReason_IsRejected()
    {
        string path = TempPath("missed-bad");
        try
        {
            using var log = new EconomyLog(path, SimSecondsPerDay);

            Assertions.AssertThat(log.AppendShoppingMissed(1.0, "", "BagFull")).IsFalse();
            Assertions.AssertThat(log.AppendShoppingMissed(1.0, "甲", "")).IsFalse();
            Assertions.AssertThat(log.RowCount).IsEqual(0L);

            log.Close();
            Assertions.AssertThat(File.ReadAllText(path)).IsEmpty();
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    /// <summary>没有领域事件 ID → 合成的标识必须<b>文件内唯一</b>。</summary>
    [TestCase]
    public void AppendShoppingMissed_ManyRows_GetUniqueIds()
    {
        string path = TempPath("missed-ids");
        try
        {
            using var log = new EconomyLog(path, SimSecondsPerDay);
            for (int i = 0; i < 3; i++)
            {
                log.AppendShoppingMissed(1.0, "甲", "BagFull");
            }

            log.Close();
            string[] lines = File.ReadAllLines(path);
            var ids = new List<string>();
            foreach (var line in lines)
            {
                using var doc = JsonDocument.Parse(line);
                ids.Add(doc.RootElement.GetProperty("Id").GetString()!);
            }

            Assertions.AssertThat(lines.Length).IsEqual(3);
            Assertions.AssertThat(ids.Distinct().Count()).IsEqual(3);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    /// <summary>
    /// 「未成」行<b>不</b>走事件通道：它不是领域事件（没有状态变化）。
    /// </summary>
    /// <remarks>
    /// <c>DomainEvent</c> 构造期就拒绝「声明不出状态变化」的事件，
    /// 所以未成只能是流水层的<b>直写</b>；这条用例钉住「别哪天把它塞回事件流」。
    /// </remarks>
    [TestCase]
    public void Append_NonEconomicEvent_StillIgnored_AfterMissedSupportAdded()
    {
        string path = TempPath("other-after-missed");
        try
        {
            using var log = new EconomyLog(path, SimSecondsPerDay);
            var e = DomainEvent.Create(
                new EventId("HeroSummoned:甲:0"), OtherType, "甲",
                occurredAtSimSeconds: 1.0, changedStates: new[] { StHeroBag });

            Assertions.AssertThat(log.Append(e)).IsFalse();
            Assertions.AssertThat(log.RowCount).IsEqual(0L);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    // ── 每日经济快照（D-054 阶段 3）───────────────────────────────────────

    /// <summary>
    /// 快照行必须<b>把守恒律的每一项都写出来</b> —— 缺一项，「逐日核对」就只能靠推算。
    /// </summary>
    [TestCase]
    public void AppendSnapshot_WritesEveryConservationTerm()
    {
        string path = TempPath("snapshot");
        try
        {
            using var log = new EconomyLog(path, SimSecondsPerDay);
            bool written = log.AppendSnapshot(
                7 * SimSecondsPerDay,
                heroGoldTotal: 345, villageGold: 35, villageMaterial: 482,
                goldGranted: 80, goldSpent: 50, upgrades: 1,
                taxCarry: 10, heroCount: 3, builders: 1);

            Assertions.AssertThat(written).IsTrue();
            Assertions.AssertThat(log.RowCount).IsEqual(1L);

            log.Close();
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            var row = doc.RootElement;

            Assertions.AssertThat(row.GetProperty("Kind").GetString()).IsEqual("EconomySnapshot");
            Assertions.AssertThat(row.GetProperty("Day").GetInt64()).IsEqual(8L);
            Assertions.AssertThat(row.GetProperty("HeroGoldTotal").GetInt64()).IsEqual(345L);
            Assertions.AssertThat(row.GetProperty("VillageGold").GetInt64()).IsEqual(35L);
            Assertions.AssertThat(row.GetProperty("VillageMaterial").GetInt64()).IsEqual(482L);
            Assertions.AssertThat(row.GetProperty("GoldGranted").GetInt64()).IsEqual(80L);
            Assertions.AssertThat(row.GetProperty("GoldSpent").GetInt64()).IsEqual(50L);
            Assertions.AssertThat(row.GetProperty("Upgrades").GetInt64()).IsEqual(1L);
            Assertions.AssertThat(row.GetProperty("TaxCarry").GetInt64()).IsEqual(10L);
            Assertions.AssertThat(row.GetProperty("HeroCount").GetInt64()).IsEqual(3L);
            Assertions.AssertThat(row.GetProperty("Builders").GetInt64()).IsEqual(1L);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    /// <summary>
    /// 快照行还要记<b>人均金币的分布</b>（D-054 阶段 5）——
    /// 「合计稳」不等于「钱分得匀」。
    /// </summary>
    /// <remarks>
    /// 只有 5 个分位点，但足以区分「人人 40 金」与「3 个人 500 金、其余 0 金」——
    /// 而这两种经济体感天差地别，**只看合计是看不出来的**。
    /// </remarks>
    [TestCase]
    public void AppendSnapshot_WritesHeroGoldDistribution()
    {
        string path = TempPath("snapshot-dist");
        try
        {
            using var log = new EconomyLog(path, SimSecondsPerDay);
            bool written = log.AppendSnapshot(
                7 * SimSecondsPerDay,
                heroGoldTotal: 160, villageGold: 35, villageMaterial: 482,
                goldGranted: 80, goldSpent: 50, upgrades: 1,
                taxCarry: 10, heroCount: 3, builders: 1,
                goldPending: 12,
                heroGoldMin: 0, heroGoldP25: 20, heroGoldMedian: 40,
                heroGoldP75: 80, heroGoldMax: 100,
                heroGoldTopId: "h-7");

            Assertions.AssertThat(written).IsTrue();

            log.Close();
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            var row = doc.RootElement;

            Assertions.AssertThat(row.GetProperty("HeroGoldMin").GetInt64()).IsEqual(0L);
            Assertions.AssertThat(row.GetProperty("HeroGoldP25").GetInt64()).IsEqual(20L);
            Assertions.AssertThat(row.GetProperty("HeroGoldMedian").GetInt64()).IsEqual(40L);
            Assertions.AssertThat(row.GetProperty("HeroGoldP75").GetInt64()).IsEqual(80L);
            Assertions.AssertThat(row.GetProperty("HeroGoldMax").GetInt64()).IsEqual(100L);
            // 最富的是谁 —— 「同一个人一直囤」vs「轮流富」，修法完全不同
            Assertions.AssertThat(row.GetProperty("HeroGoldTopId").GetString()).IsEqual("h-7");
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    /// <summary>不传分布 → 字段为 <c>null</c>（旧样本没有它们）。</summary>
    [TestCase]
    public void AppendSnapshot_WithoutDistribution_LeavesFieldsNull()
    {
        string path = TempPath("snapshot-nodist");
        try
        {
            using var log = new EconomyLog(path, SimSecondsPerDay);
            log.AppendSnapshot(1.0, 1, 1, 1, 1, 1, 1, 1, 1, 1);

            log.Close();
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            var row = doc.RootElement;

            foreach (var name in new[] { "HeroGoldMin", "HeroGoldP25", "HeroGoldMedian", "HeroGoldP75", "HeroGoldMax" })
            {
                Assertions.AssertThat(row.GetProperty(name).ValueKind)
                    .IsEqual(System.Text.Json.JsonValueKind.Null);
            }
            Assertions.AssertThat(row.GetProperty("HeroGoldTopId").ValueKind)
                .IsEqual(System.Text.Json.JsonValueKind.Null);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    /// <summary>快照行<b>不依赖</b>领域事件 —— 它是「某一刻的读数」，没有事件 ID。</summary>
    [TestCase]
    public void AppendSnapshot_HasUniqueIdPerCall()
    {
        string path = TempPath("snapshot-ids");
        try
        {
            using var log = new EconomyLog(path, SimSecondsPerDay);
            log.AppendSnapshot(1.0, 1, 1, 1, 1, 1, 1, 1, 1, 1);
            log.AppendSnapshot(1.0, 2, 2, 2, 2, 2, 2, 2, 2, 2);

            log.Close();
            string[] lines = File.ReadAllLines(path);
            Assertions.AssertThat(lines.Length).IsEqual(2);

            var ids = new List<string>();
            foreach (var line in lines)
            {
                using var doc = JsonDocument.Parse(line);
                ids.Add(doc.RootElement.GetProperty("Id").GetString()!);
            }

            Assertions.AssertThat(ids.Distinct().Count()).IsEqual(2);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
