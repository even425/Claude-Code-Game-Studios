using System;
using System.Collections.Generic;
using System.IO;
using GdUnit4;
using ClaudeCodeGameStudios.Data;
using ClaudeCodeGameStudios.Domain.Heroes;
using ClaudeCodeGameStudios.Domain.Items;
using ClaudeCodeGameStudios.Domain.Village;

namespace ClaudeCodeGameStudios.Tests.Data;

/// <summary>
/// M7 存档 ↔ 运行态 集成测试。
///
/// <para>主场景里的 <c>OnSave</c> / <c>OnLoad</c> 直接持 Godot 节点
/// （<c>Button</c> / <c>Panel</c>），在 unit 框架下不好直接调。
/// 这里把"SaveGame 字段 ↔ HeroAgent.Wallet/Bag"那部分**纯逻辑**抽出来单测：
/// <list type="bullet">
/// <item>写入：HeroAgent → SaveGame.HeroSave.CoinAmount / Bag</item>
/// <item>读出：SaveGame.HeroSave.CoinAmount / Bag → HeroAgent.RestoreWallet / RestoreBag</item>
/// <item>整份 round-trip：保存 → 校验 → 加载</item>
/// </list>
/// </para>
/// </summary>
[TestSuite]
public class SaveLoadIntegrationTests
{
    private const string GoldKey = "金币";
    private static readonly ResourceId GoldResource = new(GoldKey);
    private static readonly ItemId Stone = new("石料");
    private static readonly HeroId AliceId = new("h-alice");

    /// <summary>建一个 alice 持 100 金 + 3 石的 HeroAgent。</summary>
    private static HeroAgent MakeAlice()
    {
        var h = new HeroAgent
        {
            Name = "Alice",
            Personality = "莽",
            Diligence = "随手",
            Power = 1, Speed = 1, Grit = 1,
        };
        h.AssignId(AliceId);
        h.RestoreWallet(new Wallet(new Dictionary<ResourceId, long> { { GoldResource, 100L } }));
        var bag = new Inventory().TryAdd(Stone, 3)!;
        h.RestoreBag(bag);
        return h;
    }

    // ── 写入（HeroAgent → HeroSave） ────────────────────────────────────

    [TestCase]
    public void Write_HeroSave_CapturesCoinAndBag()
    {
        var alice = MakeAlice();
        var hs = BuildHeroSave(alice);
        Assertions.AssertThat(hs.CoinAmount).IsEqual(100L);
        Assertions.AssertThat(hs.Bag.Count).IsEqual(1);
        Assertions.AssertThat(hs.Bag[0].ItemId).IsEqual("石料");
        Assertions.AssertThat(hs.Bag[0].Count).IsEqual(3L);
    }

    [TestCase]
    public void Write_HeroSave_ZeroItemsNotSaved()
    {
        var alice = MakeAlice();
        // 用空 Inventory 还原 —— Bag 0 件
        alice.RestoreBag(new Inventory());
        var hs = BuildHeroSave(alice);
        Assertions.AssertThat(hs.Bag.Count).IsEqual(0);   // 0 不存
    }

    // ── 读出（HeroSave → HeroAgent） ────────────────────────────────────

    [TestCase]
    public void Load_RestoresWalletAndBag_OnHeroAgent()
    {
        var alice = MakeAlice();   // 现有 alice 有 100 金 + 3 石

        // 模拟"读档"：构造一个 SaveGame（不同金币 / 不同 Bag），Restore 给 alice
        var save = new SaveGame
        {
            Version = SaveVersion.Current,
            Heroes = new List<HeroSave>
            {
                new HeroSave
                {
                    Id = "h-alice",
                    CoinAmount = 200L,
                    Bag = new List<BagEntrySave>
                    {
                        new BagEntrySave { ItemId = "石料", Count = 7 },
                        new BagEntrySave { ItemId = "木料", Count = 2 },
                    },
                }
            },
        };
        ApplyHeroSaves(save, new List<HeroAgent> { alice });

        Assertions.AssertThat(alice.Wallet.Amount(GoldResource)).IsEqual(200L);
        Assertions.AssertThat(alice.Bag.Count(Stone)).IsEqual(7L);
        Assertions.AssertThat(alice.Bag.Count(new ItemId("木料"))).IsEqual(2L);
    }

    [TestCase]
    public void Load_SkipsUnknownHeroes_NotThrows()
    {
        var alice = MakeAlice();
        var save = new SaveGame
        {
            Version = SaveVersion.Current,
            Heroes = new List<HeroSave>
            {
                new HeroSave { Id = "h-nobody", CoinAmount = 999, Bag = new List<BagEntrySave>() },
                new HeroSave { Id = "h-alice",   CoinAmount = 50,  Bag = new List<BagEntrySave>() },
            },
        };
        // alice 在运行态里；h-nobody 不在 → 跳过
        ApplyHeroSaves(save, new List<HeroAgent> { alice });
        Assertions.AssertThat(alice.Wallet.Amount(GoldResource)).IsEqual(50L);
    }

    // ── 整份 round-trip ─────────────────────────────────────────────────

    [TestCase]
    public void RoundTrip_SaveThenLoad_PreservesAll()
    {
        var alice = MakeAlice();
        // 写
        var save = new SaveGame
        {
            Version = SaveVersion.Current,
            Village = new VillageSave
            {
                Id = "v1",
                Resources = new Dictionary<string, long> { { GoldKey, 50L } },
                Buildings = new List<BuildingSave>(),
            },
            Heroes = new List<HeroSave> { BuildHeroSave(alice) },
        };
        // 落到临时文件
        var temp = Path.GetTempFileName();
        try
        {
            var port = new SavePort(temp);
            port.Write(save);
            // 读
            var outcome = port.Read();
            Assertions.AssertThat(outcome.Loaded).IsTrue();
            // 应用
            ApplyHeroSaves(outcome.Save!, new List<HeroAgent> { alice });
            Assertions.AssertThat(alice.Wallet.Amount(GoldResource)).IsEqual(100L);
            Assertions.AssertThat(alice.Bag.Count(Stone)).IsEqual(3L);
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
    }

    [TestCase]
    public void RoundTrip_OldSave_0_1_Rejected()
    {
        var temp = Path.GetTempFileName();
        try
        {
            // 写一份 0,1 档
            File.WriteAllText(temp,
                "{\"version\":{\"major\":0,\"minor\":1},\"heroes\":[],\"village\":{}}");
            var port = new SavePort(temp);
            var outcome = port.Read();
            Assertions.AssertThat(outcome.Loaded).IsFalse();
            Assertions.AssertThat(outcome.Validation.IsValid).IsFalse();
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
    }

    // ── 抽出来的纯逻辑 helper（与 Main.OnSave / OnLoad 同款） ────────

    private static HeroSave BuildHeroSave(HeroAgent h)
    {
        var bagList = new List<BagEntrySave>();
        foreach (var kv in h.Bag.Snapshot())
        {
            if (kv.Value > 0)
            {
                bagList.Add(new BagEntrySave { ItemId = kv.Key.Value, Count = kv.Value });
            }
        }
        return new HeroSave
        {
            Id = h.Id.Value,
            Name = h.Name,
            Personality = h.Personality,
            Diligence = h.Diligence,
            Power = h.Power,
            Speed = h.Speed,
            Grit = h.Grit,
            CoinAmount = h.Wallet.Amount(GoldResource),
            Bag = bagList,
        };
    }

    private static void ApplyHeroSaves(SaveGame save, List<HeroAgent> heroes)
    {
        var byId = new Dictionary<string, HeroAgent>(StringComparer.Ordinal);
        foreach (var h in heroes) byId[h.Id.Value] = h;
        foreach (var hs in save.Heroes)
        {
            if (!byId.TryGetValue(hs.Id, out var hero)) continue;
            var wallet = new Wallet(
                new Dictionary<ResourceId, long>
                {
                    [GoldResource] = hs.CoinAmount,
                });
            hero.RestoreWallet(wallet);
            var bagDict = new Dictionary<ItemId, long>();
            foreach (var be in hs.Bag)
            {
                bagDict[new ItemId(be.ItemId)] = be.Count;
            }
            hero.RestoreBag(new Inventory(bagDict));
        }
    }
}
