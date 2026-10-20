using System.Collections.Generic;
using GdUnit4;
using ClaudeCodeGameStudios.Data;

namespace ClaudeCodeGameStudios.Tests.Data;

/// <summary>
/// SaveVersion (0,5) 升版验证（R-05 第二轮 / D-052）。
/// </summary>
/// <remarks>
/// <para><b>本文档保护：</b></para>
/// <list type="bullet">
/// <item><see cref="SaveVersion.Current"/> 现在是 (0,5)（第四阶段是 (0,4)）。</item>
/// <item>旧档 (0,4) / (0,3) / (0,2) / (0,1) 整份拒绝（I11 + D-021/D-029/D-036 先例）——
/// <b>不</b>写迁移器。</item>
/// <item>新 <see cref="VillageSave.VillageCutRemainder"/> 的空默认值 0 不破校验
/// （开局没有成交、没有余数是合法状态）。</item>
/// <item>余数超出 [0, 99] → 校验失败（它是 1/100 金的单位，满 100 就该入账了）。</item>
/// </list>
/// <para>
/// <b>为什么升版</b>：余数是「已经向买家收走、只是暂时没入账」的钱，
/// 读档后丢掉它等于让那部分钱凭空消失 —— 所以它必须跟着存档走。
/// </para>
/// </remarks>
[TestSuite]
public class SaveVersion05Tests
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
    public void Current_Is_0_5()
    {
        Assertions.AssertThat(SaveVersion.Current.Major).IsEqual(0);
        Assertions.AssertThat(SaveVersion.Current.Minor).IsEqual(5);
    }

    /// <summary>
    /// 第四阶段存档 (0,4) 被整份拒绝 —— 玩家现有存档会明确报版本错误。
    /// </summary>
    /// <remarks>
    /// 余数是**已经向买家收走**的钱，读档后丢掉它等于凭空少收 ——
    /// 所以宁可让玩家重开，也不做隐式兼容（D-052 §2.3）。
    /// </remarks>
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
    public void NewSave_0_5_WithoutEconomyData_Passes()
    {
        var save = ValidSave(new SaveVersion(0, 5));

        var r = SaveValidator.Validate(save);

        Assertions.AssertThat(r.IsValid).IsTrue();
        Assertions.AssertThat(save.Stalls.Count).IsEqual(0);
        Assertions.AssertThat(save.AppliedPurchaseKeys.Count).IsEqual(0);
        Assertions.AssertThat(save.DailyShoppingLastRunDay).IsEqual(int.MinValue);
        Assertions.AssertThat(save.Heroes[0].LastShoppingDay).IsEqual(0);
        Assertions.AssertThat(save.Village.VillageCutRemainder).IsEqual(0L);
    }

    /// <summary>带合法摊位 / 购买键 / 每日购物日的 0,5 存档通过。</summary>
    [TestCase]
    public void NewSave_0_5_WithStallAndPurchaseKeys_Passes()
    {
        var save = ValidSave(new SaveVersion(0, 5));
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
        var negative = ValidSave(new SaveVersion(0, 5));
        negative.Village.VillageCutRemainder = -1;
        Assertions.AssertThat(SaveValidator.Validate(negative).IsValid).IsFalse();

        var tooBig = ValidSave(new SaveVersion(0, 5));
        tooBig.Village.VillageCutRemainder = 100;
        Assertions.AssertThat(SaveValidator.Validate(tooBig).IsValid).IsFalse();
    }

    [TestCase]
    public void VillageCutRemainder_BoundaryValues_Pass()
    {
        var zero = ValidSave(new SaveVersion(0, 5));
        zero.Village.VillageCutRemainder = 0;
        Assertions.AssertThat(SaveValidator.Validate(zero).IsValid).IsTrue();

        var max = ValidSave(new SaveVersion(0, 5));
        max.Village.VillageCutRemainder = 99;
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
