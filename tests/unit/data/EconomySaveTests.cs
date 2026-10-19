using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GdUnit4;
using ClaudeCodeGameStudios.Data;

namespace ClaudeCodeGameStudios.Tests.Data;

/// <summary>
/// 第四阶段 M4（D-044）经济存档测试：摊位 / 购买幂等键 / 每日购物状态。
///
/// <para><b>本文档保护：</b></para>
/// <list type="bullet">
/// <item>摊位字段（摊主 / 商品 / 库存 / 单价 / 状态 / 开摊时间 / 格位）完好往返 ——
/// 摊位库存是<b>已经从英雄背包挪走的货</b>，丢了就是凭空蒸发。</item>
/// <item>购买业务请求键完好往返 —— 读档后同一请求键必须仍被判重，不得再次扣款/发货。</item>
/// <item><see cref="HeroSave.LastShoppingDay"/> / <see cref="SaveGame.DailyShoppingLastRunDay"/>
/// 完好往返 —— 不存它们，读档后同一天会重复购物。</item>
/// <item>校验器拒绝每一类损坏：断引用、重复摊主、空商品、负库存、负单价、
/// 未知状态、非法时间、营业中无货、空幂等键、非法日号。</item>
/// </list>
/// </summary>
[TestSuite]
public class EconomySaveTests
{
    private string _dir = "";

    [BeforeTest]
    public void MakeDir()
    {
        _dir = Path.Combine(Path.GetTempPath(), "economysave-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    [AfterTest]
    public void DropDir()
    {
        try { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); }
        catch (IOException) { /* 清理失败不该让测试变红 —— 那是临时目录 */ }
    }

    private SavePort Port() => new(Path.Combine(_dir, "e.json"));

    /// <summary>一份结构完整、应当通过校验的 0,4 存档（无经济状态）。</summary>
    private static SaveGame ValidSave() => new()
    {
        Version = SaveVersion.Current,
        Village = new VillageSave { Id = "v1" },
        Heroes = new List<HeroSave>
        {
            new() { Id = "h-alice", Name = "Alice", Personality = "莽", Diligence = "随手",
                    Power = 1, Speed = 1, Grit = 1 },
            new() { Id = "h-bob", Name = "Bob", Personality = "贪", Diligence = "肝",
                    Power = 1, Speed = 1, Grit = 1 },
        },
    };

    /// <summary>带一个合法摊位 + 两个购买键 + 每日购物状态的存档。</summary>
    private static SaveGame SaveWithStall()
    {
        var save = ValidSave();
        save.Heroes[0].LastShoppingDay = 2;
        save.DailyShoppingLastRunDay = 2;
        save.AppliedPurchaseKeys = new List<string> { "save-run-1", "save-run-2" };
        save.Stalls = new List<StallSave>
        {
            new()
            {
                OwnerId = "h-alice", ItemId = "石料", Stock = 3, UnitPrice = 5,
                Status = "Open", SimTime = 10.5, GridX = 1, GridY = 2,
            },
        };
        return save;
    }

    private static void MustReject(Action<SaveGame> mutate, string pathFragment)
    {
        var save = SaveWithStall();
        mutate(save);

        var r = SaveValidator.Validate(save);

        Assertions.AssertThat(r.IsValid).IsFalse();
        Assertions.AssertThat(r.Errors.Any(e =>
            e.Path.Contains(pathFragment, StringComparison.Ordinal))).IsTrue();
    }

    // ── 往返 ─────────────────────────────────────────────────────────────

    [TestCase]
    public void WriteThenRead_StallFields_SurviveIntact()
    {
        Port().Write(SaveWithStall());
        var stall = Port().Read().Save!.Stalls[0];

        Assertions.AssertThat(stall.OwnerId).IsEqual("h-alice");
        Assertions.AssertThat(stall.ItemId).IsEqual("石料");
        Assertions.AssertThat(stall.Stock).IsEqual(3L);
        Assertions.AssertThat(stall.UnitPrice).IsEqual(5L);
        Assertions.AssertThat(stall.Status).IsEqual("Open");
        Assertions.AssertThat(stall.SimTime).IsEqual(10.5);
        Assertions.AssertThat(stall.GridX).IsEqual(1);
        Assertions.AssertThat(stall.GridY).IsEqual(2);
    }

    [TestCase]
    public void WriteThenRead_PurchaseKeysAndDailyRunDay_SurviveIntact()
    {
        Port().Write(SaveWithStall());
        var save = Port().Read().Save!;

        Assertions.AssertThat(save.AppliedPurchaseKeys.Count).IsEqual(2);
        Assertions.AssertThat(save.AppliedPurchaseKeys.Contains("save-run-1")).IsTrue();
        Assertions.AssertThat(save.AppliedPurchaseKeys.Contains("save-run-2")).IsTrue();
        Assertions.AssertThat(save.DailyShoppingLastRunDay).IsEqual(2);
    }

    [TestCase]
    public void WriteThenRead_HeroLastShoppingDay_SurvivesIntact()
    {
        Port().Write(SaveWithStall());
        var save = Port().Read().Save!;

        Assertions.AssertThat(save.Heroes[0].LastShoppingDay).IsEqual(2);
        Assertions.AssertThat(save.Heroes[1].LastShoppingDay).IsEqual(0);
    }

    /// <summary>售罄摊位库存为 0 —— 公开构造拒绝 0，读档路径必须能恢复它。</summary>
    [TestCase]
    public void WriteThenRead_SoldOutZeroStock_SurvivesIntact()
    {
        var save = SaveWithStall();
        save.Stalls[0].Stock = 0;
        save.Stalls[0].Status = "SoldOut";

        Port().Write(save);
        var loaded = Port().Read();

        Assertions.AssertThat(loaded.Loaded).IsTrue();
        Assertions.AssertThat(loaded.Save!.Stalls[0].Stock).IsEqual(0L);
        Assertions.AssertThat(loaded.Save.Stalls[0].Status).IsEqual("SoldOut");
    }

    // ── 校验器负例 ───────────────────────────────────────────────────────

    [TestCase]
    public void Stall_UnknownOwner_IsRejected()
        => MustReject(s => s.Stalls[0].OwnerId = "h-ghost", "stalls[0].ownerId");

    [TestCase]
    public void Stall_EmptyOwner_IsRejected()
        => MustReject(s => s.Stalls[0].OwnerId = "", "stalls[0].ownerId");

    [TestCase]
    public void Stall_DuplicateOwner_IsRejected()
        => MustReject(s =>
        {
            s.Stalls.Add(new StallSave
            {
                OwnerId = "h-alice", ItemId = "木料", Stock = 1, UnitPrice = 1,
                Status = "Open", SimTime = 1.0,
            });
        }, "stalls[1].ownerId");

    [TestCase]
    public void Stall_EmptyItem_IsRejected()
        => MustReject(s => s.Stalls[0].ItemId = "", "stalls[0].itemId");

    [TestCase]
    public void Stall_NegativeStock_IsRejected()
        => MustReject(s => s.Stalls[0].Stock = -1, "stalls[0].stock");

    /// <summary>营业中却无货 = 状态与库存自相矛盾；合法路径在售罄那一刻就转 SoldOut。</summary>
    [TestCase]
    public void Stall_OpenWithZeroStock_IsRejected()
    {
        MustReject(s => s.Stalls[0].Stock = 0, "stalls[0].stock");

        var soldOut = SaveWithStall();
        soldOut.Stalls[0].Stock = 0;
        soldOut.Stalls[0].Status = "SoldOut";
        Assertions.AssertThat(SaveValidator.Validate(soldOut).IsValid).IsTrue();
    }

    [TestCase]
    public void Stall_NegativeUnitPrice_IsRejected()
        => MustReject(s => s.Stalls[0].UnitPrice = -1, "stalls[0].unitPrice");

    [TestCase]
    public void Stall_UnknownStatus_IsRejected()
        => MustReject(s => s.Stalls[0].Status = "Opening", "stalls[0].status");

    [TestCase]
    public void Stall_NegativeSimTime_IsRejected()
        => MustReject(s => s.Stalls[0].SimTime = -1.0, "stalls[0].simTime");

    [TestCase]
    public void Stall_NaN_SimTime_IsRejected()
        => MustReject(s => s.Stalls[0].SimTime = double.NaN, "stalls[0].simTime");

    [TestCase]
    public void Hero_NegativeLastShoppingDay_IsRejected()
        => MustReject(s => s.Heroes[0].LastShoppingDay = -1, "heroes[0].lastShoppingDay");

    [TestCase]
    public void AppliedPurchaseKeys_EmptyKey_IsRejected()
        => MustReject(s => s.AppliedPurchaseKeys.Add("  "), "appliedPurchaseKeys[2]");

    [TestCase]
    public void DailyShoppingLastRunDay_Zero_IsRejected()
        => MustReject(s => s.DailyShoppingLastRunDay = 0, "dailyShoppingLastRunDay");

    [TestCase]
    public void DailyShoppingLastRunDay_NegativeButNotSentinel_IsRejected()
        => MustReject(s => s.DailyShoppingLastRunDay = -2, "dailyShoppingLastRunDay");
}
