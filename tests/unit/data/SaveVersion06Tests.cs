using System.Collections.Generic;
using GdUnit4;
using ClaudeCodeGameStudios.Data;

namespace ClaudeCodeGameStudios.Tests.Data;

/// <summary>
/// SaveVersion (0,6) 升版验证（R-05 第四轮 / D-054 阶段 4）。
/// </summary>
/// <remarks>
/// <para><b>本文档保护：</b></para>
/// <list type="bullet">
/// <item><see cref="SaveVersion.Current"/> 现在是 (0,6)（第三轮是 (0,5)）。</item>
/// <item>旧档 (0,5) / (0,4) / (0,3) / (0,2) / (0,1) 整份拒绝（I11 + D-021/D-029/D-036 先例）——
/// <b>不</b>写迁移器。</item>
/// <item>新 <see cref="HeroSave.GoldCarry"/>（每英雄待发零头）的空默认值 0 不破校验；
/// 越界 → 校验失败（它是 1/100 金的单位，满 100 就该入账了）。</item>
/// <item>新累计字段（<see cref="SaveGame.GoldGrantedTotal"/> / <see cref="SaveGame.GoldSpentTotal"/> /
/// <see cref="SaveGame.VillageUpgrades"/>）为负 → 校验失败。</item>
/// <item>新 <see cref="SaveGame.VillageTier"/> 必须落在 [0, 5]（与
/// <c>VillageUpgradeService.MaxTier</c> 一致）。</item>
/// </list>
/// <para>
/// <b>为什么升版</b>：零头是「已经挣到、只是还没发出去」的钱，
/// 读档后丢掉它等于让那部分钱凭空消失 —— 与 D-052 存税额余数同一条理由。
/// 同时补上<b>累计发放 / 累计销毁 / 村庄层级</b>三个字段：
/// 不存它们，读档后守恒律的账就<b>对不上</b>，村庄层级还会掉回 0 级。
/// </para>
/// <para>
/// ⚠️ <b>代价</b>：升版意味着 <b>(0,5) 的旧存档一律拒读</b>（开发者已知悉并认可）——
/// 没有隐式迁移，宁可让玩家重开，也不要读出一份<b>对不上账</b>的存档。
/// </para>
/// </remarks>
[TestSuite]
public class SaveVersion06Tests
{
    /// <summary>一份能通过其余校验的最小存档。</summary>
    private static SaveGame ValidSave(SaveVersion version) => new()
    {
        Version = version,
        Village = new VillageSave { Id = "v1" },
        Heroes = new List<HeroSave>
        {
            new()
            {
                Id = "h-alice",
                Name = "Alice",
                Personality = "莽",
                Diligence = "随手",
                Power = 1, Speed = 1, Grit = 1,
            },
        },
    };

    [TestCase]
    public void Current_Is_0_6()
    {
        Assertions.AssertThat(SaveVersion.Current.Major).IsEqual(0);
        Assertions.AssertThat(SaveVersion.Current.Minor).IsEqual(6);
    }

    /// <summary>
    /// <b>(0,5) 旧档被整份拒绝</b> —— 0,6 的这一刀（开发者已知悉并认可）。
    /// </summary>
    /// <remarks>
    /// (0,5) 档<b>没有</b>待发零头、累计发放、累计销毁、村庄层级这四样。
    /// 硬读它 = 读出一份「钱凭空少了、村庄等级掉回 0」的存档 ——
    /// 那比拒读更糟。所以宁可让玩家重开（D-052 §2.3 的同一条先例）。
    /// </remarks>
    [TestCase]
    public void OldSave_0_5_IsRejected_NoMigration()
    {
        var r = SaveValidator.Validate(ValidSave(new SaveVersion(0, 5)));

        Assertions.AssertThat(r.IsValid).IsFalse();

        bool hasVersionError = false;
        foreach (var err in r.Errors)
        {
            if (err.Path.Contains("version") || err.Message.Contains("版本"))
            {
                hasVersionError = true;
                break;
            }
        }

        Assertions.AssertThat(hasVersionError).IsTrue();
    }

    /// <summary>
    /// 第四阶段存档 (0,4) 被整份拒绝 —— 玩家现有存档会明确报版本错误。
    /// </summary>
    [TestCase]
    public void OldSave_0_4_IsRejected_NoMigration()
    {
        var r = SaveValidator.Validate(ValidSave(new SaveVersion(0, 4)));

        Assertions.AssertThat(r.IsValid).IsFalse();

        bool hasVersionError = false;
        foreach (var err in r.Errors)
        {
            if (err.Path.Contains("version") || err.Message.Contains("版本"))
            {
                hasVersionError = true;
                break;
            }
        }

        Assertions.AssertThat(hasVersionError).IsTrue();
    }

    [TestCase]
    public void OldSave_0_3_IsRejected_NoMigration()
        => Assertions.AssertThat(SaveValidator.Validate(ValidSave(new SaveVersion(0, 3))).IsValid).IsFalse();

    [TestCase]
    public void OldSave_0_2_IsRejected_NoMigration()
        => Assertions.AssertThat(SaveValidator.Validate(ValidSave(new SaveVersion(0, 2))).IsValid).IsFalse();

    [TestCase]
    public void OldSave_0_1_IsRejected_NoMigration()
        => Assertions.AssertThat(SaveValidator.Validate(ValidSave(new SaveVersion(0, 1))).IsValid).IsFalse();

    /// <summary>新游戏：没有摊位、没有购买键、每日购物未运行、余数为 0，正常通过。</summary>
    [TestCase]
    public void NewSave_0_6_WithoutEconomyData_Passes()
    {
        var save = ValidSave(new SaveVersion(0, 6));

        var r = SaveValidator.Validate(save);

        Assertions.AssertThat(r.IsValid).IsTrue();
        Assertions.AssertThat(save.Stalls.Count).IsEqual(0);
        Assertions.AssertThat(save.AppliedPurchaseKeys.Count).IsEqual(0);
        Assertions.AssertThat(save.DailyShoppingLastRunDay).IsEqual(int.MinValue);
        Assertions.AssertThat(save.Heroes[0].LastShoppingDay).IsEqual(0);
        Assertions.AssertThat(save.Village.VillageCutRemainder).IsEqual(0L);
    }

    /// <summary>带合法摊位 / 购买键 / 每日购物日的 0,6 存档通过。</summary>
    [TestCase]
    public void NewSave_0_6_WithStallAndPurchaseKeys_Passes()
    {
        var save = ValidSave(new SaveVersion(0, 6));
        save.Heroes[0].LastShoppingDay = 2;
        save.DailyShoppingLastRunDay = 2;
        save.AppliedPurchaseKeys = new List<string> { "saverun-1" };
        save.Village.VillageCutRemainder = 42;
        save.Stalls = new List<StallSave>
        {
            new()
            {
                OwnerId = "h-alice", ItemId = "石料", Stock = 3, UnitPrice = 5,
                Status = "Open", SimTime = 10.0, GridX = 1, GridY = 2,
            },
            new()
            {
                OwnerId = "h-bob", ItemId = "木料", Stock = 0, UnitPrice = 0,
                Status = "SoldOut", SimTime = 12.0,
            },
        };
        save.Heroes.Add(new HeroSave
        {
            Id = "h-bob", Name = "Bob", Personality = "贪", Diligence = "肝",
            Power = 1, Speed = 1, Grit = 1,
        });

        var r = SaveValidator.Validate(save);

        Assertions.AssertThat(r.IsValid).IsTrue();
    }

    /// <summary>余数必须是 [0, 99]（单位 1/100 金）—— 越界即拒。</summary>
    [TestCase]
    public void VillageCutRemainder_OutOfRange_IsRejected()
    {
        var negative = ValidSave(new SaveVersion(0, 6));
        negative.Village.VillageCutRemainder = -1;
        Assertions.AssertThat(SaveValidator.Validate(negative).IsValid).IsFalse();

        var tooBig = ValidSave(new SaveVersion(0, 6));
        tooBig.Village.VillageCutRemainder = 100;
        Assertions.AssertThat(SaveValidator.Validate(tooBig).IsValid).IsFalse();
    }

    [TestCase]
    public void VillageCutRemainder_BoundaryValues_Pass()
    {
        var zero = ValidSave(new SaveVersion(0, 6));
        zero.Village.VillageCutRemainder = 0;
        Assertions.AssertThat(SaveValidator.Validate(zero).IsValid).IsTrue();

        var max = ValidSave(new SaveVersion(0, 6));
        max.Village.VillageCutRemainder = 99;
        Assertions.AssertThat(SaveValidator.Validate(max).IsValid).IsTrue();
    }

    // ── 0,6 新字段（0,5 时代不存在）────────────────────────────────────────

    /// <summary>
    /// <b>待发零头</b>必须落在 [0, 99]（单位 1/100 金）—— 越界即拒。
    /// </summary>
    /// <remarks>
    /// 零头是<b>已经挣到</b>的钱：满 100 就该发出去一张整金了，
    /// 还留着 ≥100 说明存档本身是坏的 —— 读进去会变成「一发就发 2 金」。
    /// </remarks>
    [TestCase]
    public void HeroGoldCarry_OutOfRange_IsRejected()
    {
        var negative = ValidSave(new SaveVersion(0, 6));
        negative.Heroes[0].GoldCarry = -1;
        Assertions.AssertThat(SaveValidator.Validate(negative).IsValid).IsFalse();

        var tooBig = ValidSave(new SaveVersion(0, 6));
        tooBig.Heroes[0].GoldCarry = 100;
        Assertions.AssertThat(SaveValidator.Validate(tooBig).IsValid).IsFalse();
    }

    [TestCase]
    public void HeroGoldCarry_BoundaryValues_Pass()
    {
        var zero = ValidSave(new SaveVersion(0, 6));
        zero.Heroes[0].GoldCarry = 0;
        Assertions.AssertThat(SaveValidator.Validate(zero).IsValid).IsTrue();

        var max = ValidSave(new SaveVersion(0, 6));
        max.Heroes[0].GoldCarry = 99;
        Assertions.AssertThat(SaveValidator.Validate(max).IsValid).IsTrue();
    }

    /// <summary>
    /// 累计发放 / 累计销毁 / 累计升级次数<b>为负</b> → 拒。
    /// </summary>
    /// <remarks>
    /// 这三个是守恒律的账本科目：负数意味着「钱被凭空造出来 / 被凭空销毁」——
    /// 那不是合法状态。
    /// </remarks>
    [TestCase]
    public void EconomyTotals_Negative_IsRejected()
    {
        var granted = ValidSave(new SaveVersion(0, 6));
        granted.GoldGrantedTotal = -1;
        Assertions.AssertThat(SaveValidator.Validate(granted).IsValid).IsFalse();

        var spent = ValidSave(new SaveVersion(0, 6));
        spent.GoldSpentTotal = -1;
        Assertions.AssertThat(SaveValidator.Validate(spent).IsValid).IsFalse();

        var upgrades = ValidSave(new SaveVersion(0, 6));
        upgrades.VillageUpgrades = -1;
        Assertions.AssertThat(SaveValidator.Validate(upgrades).IsValid).IsFalse();
    }

    /// <summary>
    /// 累计升级次数必须落在 [0, MaxTier] —— 越界即拒。
    /// </summary>
    /// <remarks>
    /// 村庄最多升 <c>MaxTier</c> 级，所以「已付款的升级次数」不可能超过它。
    /// 顺手堵掉一个隐患：<c>RestoreTotals</c> 把存档里的 <c>long</c> 强转成 <c>int</c>，
    /// 一个被改坏的存档能让那个转换<b>绕成负数</b>。
    /// </remarks>
    [TestCase]
    public void VillageUpgrades_OutOfRange_IsRejected()
    {
        var negative = ValidSave(new SaveVersion(0, 6));
        negative.VillageUpgrades = -1;
        Assertions.AssertThat(SaveValidator.Validate(negative).IsValid).IsFalse();

        var tooBig = ValidSave(new SaveVersion(0, 6));
        tooBig.VillageUpgrades = ClaudeCodeGameStudios.Application.VillageUpgradeService.MaxTier + 1;
        Assertions.AssertThat(SaveValidator.Validate(tooBig).IsValid).IsFalse();
    }

    [TestCase]
    public void VillageTier_OutOfRange_IsRejected()
    {
        var negative = ValidSave(new SaveVersion(0, 6));
        negative.VillageTier = -1;
        Assertions.AssertThat(SaveValidator.Validate(negative).IsValid).IsFalse();

        var tooBig = ValidSave(new SaveVersion(0, 6));
        tooBig.VillageTier = ClaudeCodeGameStudios.Application.VillageUpgradeService.MaxTier + 1;
        Assertions.AssertThat(SaveValidator.Validate(tooBig).IsValid).IsFalse();
    }

    [TestCase]
    public void VillageTier_BoundaryValues_Pass()
    {
        var zero = ValidSave(new SaveVersion(0, 6));
        zero.VillageTier = 0;
        Assertions.AssertThat(SaveValidator.Validate(zero).IsValid).IsTrue();

        var max = ValidSave(new SaveVersion(0, 6));
        max.VillageTier = ClaudeCodeGameStudios.Application.VillageUpgradeService.MaxTier;
        Assertions.AssertThat(SaveValidator.Validate(max).IsValid).IsTrue();
    }

    [TestCase]
    public void EconomySaveFields_DefaultsAreEmpty_NotNull()
    {
        var save = new SaveGame();

        Assertions.AssertThat(save.Stalls).IsNotNull();
        Assertions.AssertThat(save.Stalls.Count).IsEqual(0);
        Assertions.AssertThat(save.AppliedPurchaseKeys).IsNotNull();
        Assertions.AssertThat(save.AppliedPurchaseKeys.Count).IsEqual(0);
        Assertions.AssertThat(save.DailyShoppingLastRunDay).IsEqual(int.MinValue);
        Assertions.AssertThat(save.Village.VillageCutRemainder).IsEqual(0L);

        var hero = new HeroSave();
        Assertions.AssertThat(hero.LastShoppingDay).IsEqual(0);

        // 0,6 新字段：默认值必须是「合法且中性」的开局状态
        Assertions.AssertThat(hero.GoldCarry).IsEqual(0L);
        Assertions.AssertThat(save.GoldGrantedTotal).IsEqual(0L);
        Assertions.AssertThat(save.GoldSpentTotal).IsEqual(0L);
        Assertions.AssertThat(save.VillageTier).IsEqual(0);
        Assertions.AssertThat(save.VillageUpgrades).IsEqual(0L);

        var stall = new StallSave();
        Assertions.AssertThat(stall.OwnerId).IsEqual("");
        Assertions.AssertThat(stall.ItemId).IsEqual("");
        Assertions.AssertThat(stall.Stock).IsEqual(0L);
        Assertions.AssertThat(stall.UnitPrice).IsEqual(0L);
        Assertions.AssertThat(stall.Status).IsEqual("Open");
        Assertions.AssertThat(stall.SimTime).IsEqual(0.0);
        Assertions.AssertThat(stall.GridX).IsEqual(0);
        Assertions.AssertThat(stall.GridY).IsEqual(0);
    }
}
