using System.Collections.Generic;
using GdUnit4;
using ClaudeCodeGameStudios.Data;
using ClaudeCodeGameStudios.Domain.WorldEvents;

namespace ClaudeCodeGameStudios.Tests.Data;

/// <summary>
/// SaveVersion (0,11) 升版验证（D-113）。
/// </summary>
/// <remarks>
/// <para><b>本文档保护：</b></para>
/// <list type="bullet">
/// <item><see cref="SaveVersion.Current"/> 现在是 (0,11)（上一轮是 (0,10)）。</item>
/// <item>旧档 (0,10) / (0,9) / … / (0,1) 整份拒绝（I11 + D-021/D-029/D-036 先例）——
/// <b>不</b>写迁移器。</item>
/// <item>0,11 新增 <see cref="HeroSave.LastTrialDay"/>（上次试炼的日号 ——
/// 「<b>每天限一次</b>」那条限制的凭据）：≥ 0 合法；<b>负数整份拒绝</b>。</item>
/// <item>0,10 与更早的字段<b>仍然在 0,11 里</b>，它们的校验规则不变
/// （升版只<b>加</b>字段，不放松旧规则）。</item>
/// </list></para>
/// <para>
/// <b>为什么升版</b>：试炼塔<b>每天限一次</b>（D-113）⇒ 这一项就是那条限制的凭据
/// ⇒ 不存它 ⇒ <b>读档后那条限制就白设了</b>（读一次档就能多刷一次永久加成）。
/// </para>
/// <para>
/// ⚠️ <b>代价</b>：升版意味着 <b>(0,10) 的旧存档一律拒读</b>（开发者已知悉并认可）——
/// 没有隐式迁移，宁可让玩家重开，也不要读出一份<b>对不上账</b>的存档。
/// </para>
/// </remarks>
[TestSuite]
public class SaveVersion011Tests
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
    public void Current_Is_0_11()
    {
        Assertions.AssertThat(SaveVersion.Current.Major).IsEqual(0);
        Assertions.AssertThat(SaveVersion.Current.Minor).IsEqual(11);
    }

    /// <summary>
    /// <b>(0,10) 旧档被整份拒绝</b> —— 0,11 的这一刀（开发者已知悉并认可）。
    /// </summary>
    /// <remarks>
    /// (0,10) 档<b>没有</b>「上次试炼的日号」⇒ 读它等于读出一份
    /// 「试炼塔每天限一次可以白刷」的存档 ⇒ 那比拒读更糟（限制形同虚设）。
    /// </remarks>
    [TestCase]
    public void OldSave_0_10_IsRejected_NoMigration()
    {
        var r = SaveValidator.Validate(ValidSave(new SaveVersion(0, 10)));

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
    /// <b>(0,9) 旧档被整份拒绝</b> —— 0,10 的这一刀（开发者已知悉并认可）。
    /// </summary>
    /// <remarks>
    /// (0,9) 档<b>没有</b>试炼塔的永久加成 ⇒ 读它等于读出一份
    /// 「玩家攒下来的强度凭空蒸发」的存档 ⇒ 那比拒读更糟。
    /// </remarks>
    [TestCase]
    public void OldSave_0_9_IsRejected_NoMigration()
    {
        var r = SaveValidator.Validate(ValidSave(new SaveVersion(0, 9)));

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

    // ── D-104：试炼塔的永久生命加成（0,10 新增）────────────────────────────

    /// <summary>
    /// <b>基准必须是合法的</b>：0 是「还没打过试炼塔」的合法值。
    /// </summary>
    /// <remarks>
    /// ⚠️ 先断言基准合法，是为了让后面的「负数被拒」有参照 ——
    /// 否则「整份拒绝」可能是因为别的原因（那这条守卫就抓不住被测的那条检查）。
    /// </remarks>
    [TestCase]
    public void TrialTowerHealthBonus_Zero_IsValid()
    {
        var save = ValidSave(SaveVersion.Current);
        save.Heroes[0].TrialTowerHealthBonus = 0;

        var r = SaveValidator.Validate(save);

        Assertions.AssertThat(r.IsValid).IsTrue();
    }

    /// <summary>
    /// <b>正数合法</b>：打过一次试炼塔（+20 生命）必须能存。
    /// </summary>
    [TestCase]
    public void TrialTowerHealthBonus_Positive_IsValid()
    {
        var save = ValidSave(SaveVersion.Current);
        save.Heroes[0].TrialTowerHealthBonus = 20;

        var r = SaveValidator.Validate(save);

        Assertions.AssertThat(r.IsValid).IsTrue();
    }

    /// <summary>
    /// <b>负数整份拒绝</b> —— 加成只增不减，负数没有任何合法来源。
    /// </summary>
    /// <remarks>
    /// 断言「<b>是哪一条</b>检查拒的」（按 path 找 <c>trialTowerHealthBonus</c>），
    /// 而不是只断言「整体非法」—— 否则关掉被测这条检查，存档仍可能因为
    /// <b>别的原因</b>被拒 ⇒ 守卫照样绿 ⇒ 什么都没验到。
    /// </remarks>
    [TestCase]
    public void TrialTowerHealthBonus_Negative_IsRejected_ByThatRule()
    {
        var save = ValidSave(SaveVersion.Current);
        save.Heroes[0].TrialTowerHealthBonus = -1;

        var r = SaveValidator.Validate(save);

        Assertions.AssertThat(r.IsValid).IsFalse();

        bool rejectedByThisRule = false;
        foreach (var err in r.Errors)
        {
            if (err.Path.EndsWith("trialTowerHealthBonus")) { rejectedByThisRule = true; break; }
        }

        Assertions.AssertThat(rejectedByThisRule).IsTrue();
    }

    /// <summary>
    /// <b>写盘 → 读盘，永久加成原样回来</b>（存档里真的有这个字段）。
    /// </summary>
    /// <remarks>
    /// 这一条守的是「字段真的进了存档文件」——
    /// 校验只管合法性，管不了「写没写」。
    /// 走的是 <see cref="SavePort"/>（真实文件），不是内存里的序列化器。
    /// </remarks>
    [TestCase]
    public void TrialTowerHealthBonus_SurvivesWriteThenRead()
    {
        var save = ValidSave(SaveVersion.Current);
        save.Heroes[0].TrialTowerHealthBonus = 40;

        string path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "d104-trial-bonus-" + System.Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var port = new SavePort(path);
            port.Write(save);
            var loaded = port.Read();

            Assertions.AssertThat(loaded.Loaded).IsTrue();
            Assertions.AssertThat(loaded.Save!.Heroes[0].TrialTowerHealthBonus).IsEqual(40);
        }
        finally
        {
            if (System.IO.File.Exists(path)) System.IO.File.Delete(path);
        }
    }

    // ── D-113：上次试炼的日号（0,11 新增）───────────────────────────────

    /// <summary>0 合法（从未试过）。</summary>
    [TestCase]
    public void LastTrialDay_Zero_IsValid()
    {
        var save = ValidSave(SaveVersion.Current);
        save.Heroes[0].LastTrialDay = 0;

        var r = SaveValidator.Validate(save);

        Assertions.AssertThat(r.IsValid).IsTrue();
    }

    /// <summary>正数合法（试炼过）。</summary>
    [TestCase]
    public void LastTrialDay_Positive_IsValid()
    {
        var save = ValidSave(SaveVersion.Current);
        save.Heroes[0].LastTrialDay = 42;

        var r = SaveValidator.Validate(save);

        Assertions.AssertThat(r.IsValid).IsTrue();
    }

    /// <summary>
    /// <b>负数整份拒绝</b> —— 日号没有负数（0 = 从未试过）。
    /// </summary>
    /// <remarks>
    /// ⚠️ 断言「<b>是哪一条</b>检查拒的」（按 path 找 <c>lastTrialDay</c>），
    /// 而不是只断言「整体非法」—— 否则关掉被测这条检查，存档仍可能因为
    /// <b>别的原因</b>被拒 ⇒ 守卫照样绿 ⇒ 什么都没验到。
    /// </remarks>
    [TestCase]
    public void LastTrialDay_Negative_IsRejected_ByThatRule()
    {
        var save = ValidSave(SaveVersion.Current);
        save.Heroes[0].LastTrialDay = -1;

        var r = SaveValidator.Validate(save);

        Assertions.AssertThat(r.IsValid).IsFalse();

        bool rejectedByThisRule = false;
        foreach (var err in r.Errors)
        {
            if (err.Path.EndsWith("lastTrialDay")) { rejectedByThisRule = true; break; }
        }

        Assertions.AssertThat(rejectedByThisRule).IsTrue();
    }

    /// <summary>
    /// <b>写盘 → 读盘，日号原样回来</b>（存档里真的有这个字段）。
    /// </summary>
    /// <remarks>
    /// ⚠️ **这一条是「每天限一次」在读档后还成立的前提** ——
    /// 不存它 ⇒ 读一次档就能多刷一次永久加成 ⇒ 那条限制形同虚设
    /// （与 D-104「永久加成不入档」同族的缺陷）。
    /// </remarks>
    [TestCase]
    public void LastTrialDay_SurvivesWriteThenRead()
    {
        var save = ValidSave(SaveVersion.Current);
        save.Heroes[0].LastTrialDay = 42;

        string path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "d113-last-trial-" + System.Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var port = new SavePort(path);
            port.Write(save);
            var loaded = port.Read();

            Assertions.AssertThat(loaded.Loaded).IsTrue();
            Assertions.AssertThat(loaded.Save!.Heroes[0].LastTrialDay).IsEqual(42);
        }
        finally
        {
            if (System.IO.File.Exists(path)) System.IO.File.Delete(path);
        }
    }

    /// <summary>
    /// <b>(0,8) 旧档被整份拒绝</b> —— 0,9 的这一刀（开发者已知悉并认可）。
    /// </summary>
    /// <remarks>
    /// (0,8) 档<b>没有</b>建筑 ⇒ 读它等于读出一份「玩家盖的房子全没了」的存档
    /// ⇒ 那比拒读更糟（玩家的钱与工期白花了）。
    /// </remarks>
    [TestCase]
    public void OldSave_0_8_IsRejected_NoMigration()
    {
        var r = SaveValidator.Validate(ValidSave(new SaveVersion(0, 8)));

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
    /// <b>(0,7) 旧档被整份拒绝</b> —— 0,8 的这一刀（开发者已知悉并认可）。
    /// </summary>
    /// <remarks>
    /// (0,7) 档<b>没有</b>大事件实例的「可变部分」⇒ 读它等于读出一份
    /// 「商队的货读档就补满」的存档 ⇒ 那比拒读更糟（限量就白限量了）。
    /// </remarks>
    [TestCase]
    public void OldSave_0_7_IsRejected_NoMigration()
    {
        var r = SaveValidator.Validate(ValidSave(new SaveVersion(0, 7)));

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
    /// <b>(0,6) 旧档被整份拒绝</b> —— 0,7 的这一刀（开发者已知悉并认可）。
    /// </summary>
    /// <remarks>
    /// (0,6) 档<b>没有</b>大事件实例 ⇒ 读它等于读出一份「事件凭空消失」的存档 ——
    /// 那比拒读更糟。所以宁可让玩家重开（D-052 §2.3 的同一条先例）。
    /// </remarks>
    [TestCase]
    public void OldSave_0_6_IsRejected_NoMigration()
    {
        var r = SaveValidator.Validate(ValidSave(new SaveVersion(0, 6)));

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
    /// (0,5) 旧档被整份拒绝 —— 玩家现有存档会明确报版本错误。
    /// </summary>
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

    /// <summary>第四阶段存档 (0,4) 被整份拒绝。</summary>
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
    public void NewSave_0_7_WithoutEconomyData_Passes()
    {
        var save = ValidSave(new SaveVersion(0, 11));

        var r = SaveValidator.Validate(save);

        Assertions.AssertThat(r.IsValid).IsTrue();
        Assertions.AssertThat(save.Stalls.Count).IsEqual(0);
        Assertions.AssertThat(save.AppliedPurchaseKeys.Count).IsEqual(0);
        Assertions.AssertThat(save.DailyShoppingLastRunDay).IsEqual(int.MinValue);
        Assertions.AssertThat(save.Heroes[0].LastShoppingDay).IsEqual(0);
        Assertions.AssertThat(save.Village.VillageCutRemainder).IsEqual(0L);
    }

    /// <summary>带合法摊位 / 购买键 / 每日购物日的 0,7 存档通过。</summary>
    [TestCase]
    public void NewSave_0_7_WithStallAndPurchaseKeys_Passes()
    {
        var save = ValidSave(new SaveVersion(0, 11));
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
        var negative = ValidSave(new SaveVersion(0, 11));
        negative.Village.VillageCutRemainder = -1;
        Assertions.AssertThat(SaveValidator.Validate(negative).IsValid).IsFalse();

        var tooBig = ValidSave(new SaveVersion(0, 11));
        tooBig.Village.VillageCutRemainder = 100;
        Assertions.AssertThat(SaveValidator.Validate(tooBig).IsValid).IsFalse();
    }

    [TestCase]
    public void VillageCutRemainder_BoundaryValues_Pass()
    {
        var zero = ValidSave(new SaveVersion(0, 11));
        zero.Village.VillageCutRemainder = 0;
        Assertions.AssertThat(SaveValidator.Validate(zero).IsValid).IsTrue();

        var max = ValidSave(new SaveVersion(0, 11));
        max.Village.VillageCutRemainder = 99;
        Assertions.AssertThat(SaveValidator.Validate(max).IsValid).IsTrue();
    }

    // ── 0,6 的字段在 0,7 里仍然有效（升版只加字段，不放松旧规则）────────────

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
        var negative = ValidSave(new SaveVersion(0, 11));
        negative.Heroes[0].GoldCarry = -1;
        Assertions.AssertThat(SaveValidator.Validate(negative).IsValid).IsFalse();

        var tooBig = ValidSave(new SaveVersion(0, 11));
        tooBig.Heroes[0].GoldCarry = 100;
        Assertions.AssertThat(SaveValidator.Validate(tooBig).IsValid).IsFalse();
    }

    [TestCase]
    public void HeroGoldCarry_BoundaryValues_Pass()
    {
        var zero = ValidSave(new SaveVersion(0, 11));
        zero.Heroes[0].GoldCarry = 0;
        Assertions.AssertThat(SaveValidator.Validate(zero).IsValid).IsTrue();

        var max = ValidSave(new SaveVersion(0, 11));
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
        var granted = ValidSave(new SaveVersion(0, 11));
        granted.GoldGrantedTotal = -1;
        Assertions.AssertThat(SaveValidator.Validate(granted).IsValid).IsFalse();

        var spent = ValidSave(new SaveVersion(0, 11));
        spent.GoldSpentTotal = -1;
        Assertions.AssertThat(SaveValidator.Validate(spent).IsValid).IsFalse();

        var upgrades = ValidSave(new SaveVersion(0, 11));
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
        var negative = ValidSave(new SaveVersion(0, 11));
        negative.VillageUpgrades = -1;
        Assertions.AssertThat(SaveValidator.Validate(negative).IsValid).IsFalse();

        var tooBig = ValidSave(new SaveVersion(0, 11));
        tooBig.VillageUpgrades = ClaudeCodeGameStudios.Application.VillageUpgradeService.MaxTier + 1;
        Assertions.AssertThat(SaveValidator.Validate(tooBig).IsValid).IsFalse();
    }

    [TestCase]
    public void VillageTier_OutOfRange_IsRejected()
    {
        var negative = ValidSave(new SaveVersion(0, 11));
        negative.VillageTier = -1;
        Assertions.AssertThat(SaveValidator.Validate(negative).IsValid).IsFalse();

        var tooBig = ValidSave(new SaveVersion(0, 11));
        tooBig.VillageTier = ClaudeCodeGameStudios.Application.VillageUpgradeService.MaxTier + 1;
        Assertions.AssertThat(SaveValidator.Validate(tooBig).IsValid).IsFalse();
    }

    [TestCase]
    public void VillageTier_BoundaryValues_Pass()
    {
        var zero = ValidSave(new SaveVersion(0, 11));
        zero.VillageTier = 0;
        Assertions.AssertThat(SaveValidator.Validate(zero).IsValid).IsTrue();

        var max = ValidSave(new SaveVersion(0, 11));
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

    // ── 0,7 新字段：大事件实例（D-079）────────────────────────────────────

    /// <summary>
    /// 0,7 新字段的默认值必须是「合法且中性」：大事件列表为空（不是 null）。
    /// </summary>
    [TestCase]
    public void WorldEvents_DefaultsAreEmpty_NotNull()
    {
        var save = new SaveGame();

        Assertions.AssertThat(save.WorldEvents).IsNotNull();
        Assertions.AssertThat(save.WorldEvents.Count).IsEqual(0);
    }

    /// <summary>一份合法的大事件实例（参与者在册）通过校验。</summary>
    [TestCase]
    public void WorldEvents_WithLegalInstance_Passes()
    {
        var save = ValidSave(new SaveVersion(0, 11));
        save.WorldEvents = new List<WorldEventSave>
        {
            new()
            {
                Id = "inst-beast-7",
                TemplateId = "beast-tide",
                Phase = (int)WorldEventPhase.Ongoing,
                StartedAt = 12.0,
                Progress = 0.5,
                Participants = new List<string> { "h-alice" },
                AppliedConsequences = new List<string> { "beast-tide:loss" },
            },
        };

        Assertions.AssertThat(SaveValidator.Validate(save).IsValid).IsTrue();
    }

    /// <summary>阶段越界 ⇒ 拒（存档被改坏，读出来的事件阶段无解释）。</summary>
    [TestCase]
    public void WorldEvents_PhaseOutOfRange_IsRejected()
    {
        var save = ValidSave(new SaveVersion(0, 11));
        save.WorldEvents = new List<WorldEventSave>
        {
            new() { Id = "inst-x", TemplateId = "beast-tide", Phase = 99 },
        };

        Assertions.AssertThat(SaveValidator.Validate(save).IsValid).IsFalse();
    }

    /// <summary>实例标识重复 ⇒ 拒（幂等键会撞车 ⇒ 后果可能被吞掉或重发）。</summary>
    [TestCase]
    public void WorldEvents_DuplicateId_IsRejected()
    {
        var save = ValidSave(new SaveVersion(0, 11));
        save.WorldEvents = new List<WorldEventSave>
        {
            new() { Id = "inst-x", TemplateId = "beast-tide", Phase = 1 },
            new() { Id = "inst-x", TemplateId = "caravan", Phase = 1 },
        };

        Assertions.AssertThat(SaveValidator.Validate(save).IsValid).IsFalse();
    }

    /// <summary>参与者引用断了 ⇒ 拒（后果不知道该落到谁身上）。</summary>
    [TestCase]
    public void WorldEvents_BrokenParticipantReference_IsRejected()
    {
        var save = ValidSave(new SaveVersion(0, 11));
        save.WorldEvents = new List<WorldEventSave>
        {
            new()
            {
                Id = "inst-x", TemplateId = "beast-tide", Phase = 1,
                Participants = new List<string> { "h-nobody" },
            },
        };

        Assertions.AssertThat(SaveValidator.Validate(save).IsValid).IsFalse();
    }

    /// <summary>空的后果键 ⇒ 拒（空键会让所有空键后果被当成同一个 ⇒ 幂等失效）。</summary>
    [TestCase]
    public void WorldEvents_EmptyConsequenceKey_IsRejected()
    {
        var save = ValidSave(new SaveVersion(0, 11));
        save.WorldEvents = new List<WorldEventSave>
        {
            new()
            {
                Id = "inst-x", TemplateId = "beast-tide", Phase = 1,
                AppliedConsequences = new List<string> { "" },
            },
        };

        Assertions.AssertThat(SaveValidator.Validate(save).IsValid).IsFalse();
    }

    /// <summary>实例标识 / 模板标识为空 ⇒ 拒（这样的事件没法追溯、也没法解释）。</summary>
    [TestCase]
    public void WorldEvents_EmptyIdOrTemplateId_IsRejected()
    {
        var noId = ValidSave(new SaveVersion(0, 11));
        noId.WorldEvents = new List<WorldEventSave> { new() { Id = "", TemplateId = "beast-tide" } };
        Assertions.AssertThat(SaveValidator.Validate(noId).IsValid).IsFalse();

        var noTemplate = ValidSave(new SaveVersion(0, 11));
        noTemplate.WorldEvents = new List<WorldEventSave> { new() { Id = "inst-x", TemplateId = "" } };
        Assertions.AssertThat(SaveValidator.Validate(noTemplate).IsValid).IsFalse();
    }

    // ── 0,8 新字段：实例状态（商队还剩多少货）──────────────────────────

    /// <summary>0,8 新字段的默认值必须是「合法且中性」：状态字典为空（不是 null）。</summary>
    [TestCase]
    public void WorldEventState_DefaultsAreEmpty_NotNull()
    {
        var save = new SaveGame();
        var ev = new WorldEventSave();

        Assertions.AssertThat(ev.State).IsNotNull();
        Assertions.AssertThat(ev.State.Count).IsEqual(0);
        Assertions.AssertThat(save.WorldEvents).IsNotNull();
    }

    /// <summary>带合法状态的实例通过校验。</summary>
    [TestCase]
    public void WorldEventState_WithLegalState_Passes()
    {
        var save = ValidSave(new SaveVersion(0, 11));
        save.WorldEvents = new List<WorldEventSave>
        {
            new()
            {
                Id = "inst-x", TemplateId = "caravan", Phase = 3,
                State = new Dictionary<string, string> { ["caravan/stock"] = "23" },
            },
        };

        Assertions.AssertThat(SaveValidator.Validate(save).IsValid).IsTrue();
    }

    /// <summary>状态为 null ⇒ 拒（存档结构坏了）。</summary>
    [TestCase]
    public void WorldEventState_Null_IsRejected()
    {
        var save = ValidSave(new SaveVersion(0, 11));
        save.WorldEvents = new List<WorldEventSave>
        {
            new() { Id = "inst-x", TemplateId = "caravan", Phase = 3, State = null! },
        };

        Assertions.AssertThat(SaveValidator.Validate(save).IsValid).IsFalse();
    }

    /// <summary>
    /// 空的状态键 / 空的状态值 ⇒ 拒。
    /// </summary>
    /// <remarks>
    /// ⚠️ **空键会让所有空键的状态被当成同一个**（与「空的后果键」同一族问题）⇒ 必须拒。
    /// </remarks>
    [TestCase]
    public void WorldEventState_EmptyKeyOrValue_IsRejected()
    {
        var emptyKey = ValidSave(new SaveVersion(0, 11));
        emptyKey.WorldEvents = new List<WorldEventSave>
        {
            new()
            {
                Id = "inst-x", TemplateId = "caravan", Phase = 3,
                State = new Dictionary<string, string> { [""] = "23" },
            },
        };
        Assertions.AssertThat(SaveValidator.Validate(emptyKey).IsValid).IsFalse();

        var emptyValue = ValidSave(new SaveVersion(0, 11));
        emptyValue.WorldEvents = new List<WorldEventSave>
        {
            new()
            {
                Id = "inst-x", TemplateId = "caravan", Phase = 3,
                State = new Dictionary<string, string> { ["caravan/stock"] = "" },
            },
        };
        Assertions.AssertThat(SaveValidator.Validate(emptyValue).IsValid).IsFalse();
    }
}
