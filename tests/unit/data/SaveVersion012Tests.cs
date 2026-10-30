using System.Collections.Generic;
using GdUnit4;
using ClaudeCodeGameStudios.Data;
using ClaudeCodeGameStudios.Domain.Items;

namespace ClaudeCodeGameStudios.Tests.Data;

/// <summary>
/// SaveVersion (0,12) 升版验证（D-119：装备线）。
/// </summary>
/// <remarks>
/// <para><b>本文档保护：</b></para>
/// <list type="bullet">
/// <item><see cref="SaveVersion.Current"/> 现在是 (0,12)（上一轮是 (0,11)）。</item>
/// <item>旧档 (0,11) 整份拒绝（I11 先例）—— <b>不</b>写迁移器。</item>
/// <item>0,12 新增：每人身上的<b>装备 + 耐久</b>（武器槽 / 衣服槽）、
/// 以及村庄的<b>商店货架</b>。</item>
/// </list></para>
/// <para>
/// ⚠️ <b>为什么必须存</b>：基线 §15 明列「加载时要避免<b>装备丢失或重复</b>」
/// ⇒ 不存 ⇒ 读档后装备与耐久**凭空消失**，而游戏**不报任何错**
/// （这个人只是「忽然变弱了」）—— 与 D-104「永久加成不入档」同一族。
/// </para>
/// <para>
/// ⚠️ <b>每条守卫都断言「是哪一条检查拒的」</b>（按 path 找）
/// ⇒ 否则关掉被测那条检查，存档仍可能因为别的原因被拒 ⇒ 守卫成了装饰品。
/// </para>
/// </remarks>
[TestSuite]
public class SaveVersion012Tests
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
    public void Current_Is_0_13()
    {
        Assertions.AssertThat(SaveVersion.Current.Major).IsEqual(0);
        Assertions.AssertThat(SaveVersion.Current.Minor).IsEqual(13);
    }

    /// <summary>
    /// <b>(0,12) 旧档被整份拒绝</b> —— 0.13 的这一刀（开发者已知悉并认可）。
    /// </summary>
    /// <remarks>
    /// (0,12) 档<b>没有</b>「累计村庄日常开销」⇒ 读它等于读出一份
    /// 「读档后守恒律左边少一笔（钱凭空消失）」的存档 ⇒ 那比拒读更糟。
    /// </remarks>
    [TestCase]
    public void OldSave_0_12_IsRejected_NoMigration()
    {
        var r = SaveValidator.Validate(ValidSave(new SaveVersion(0, 12)));

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
    /// <b>(0,11) 旧档被整份拒绝</b> —— 0,12 的这一刀（开发者已知悉并认可）。
    /// </summary>
    /// <remarks>
    /// (0,11) 档<b>没有</b>装备与耐久 ⇒ 读它等于读出一份
    /// 「玩家攒下来的装备凭空蒸发」的存档 ⇒ 那比拒读更糟。
    /// </remarks>
    [TestCase]
    public void OldSave_0_11_IsRejected_NoMigration()
    {
        var r = SaveValidator.Validate(ValidSave(new SaveVersion(0, 11)));

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

    // ── 装备字段 ────────────────────────────────────────────────────────

    /// <summary>
    /// <b>没穿装备</b>（空 Id + 耐久 0）合法 —— 新档里所有人都是这样。
    /// </summary>
    [TestCase]
    public void NoEquipment_IsValid()
    {
        var save = ValidSave(SaveVersion.Current);

        var r = SaveValidator.Validate(save);

        Assertions.AssertThat(r.IsValid).IsTrue();
    }

    /// <summary>
    /// <b>穿了装备</b>（登记过的 Id + 合法耐久）合法。
    /// </summary>
    [TestCase]
    public void Equipped_RegisteredItem_WithLegalDurability_IsValid()
    {
        var save = ValidSave(SaveVersion.Current);
        save.Heroes[0].EquippedWeaponId = "石剑";
        save.Heroes[0].WeaponDurability = 12;
        save.Heroes[0].EquippedArmorId = "皮甲";
        save.Heroes[0].ArmorDurability = 18;

        var r = SaveValidator.Validate(save);

        Assertions.AssertThat(r.IsValid).IsTrue();
    }

    /// <summary>
    /// <b>未登记的装备 Id 整份拒绝</b> —— 读出来会变成「一件说不清是什么的东西」。
    /// </summary>
    [TestCase]
    public void Unregistered_Equipment_IsRejected_ByThatRule()
    {
        var save = ValidSave(SaveVersion.Current);
        save.Heroes[0].EquippedWeaponId = "屠龙刀";

        var r = SaveValidator.Validate(save);

        Assertions.AssertThat(r.IsValid).IsFalse();

        bool rejectedByThisRule = false;
        foreach (var err in r.Errors)
        {
            if (err.Path.EndsWith("equippedWeaponId")) { rejectedByThisRule = true; break; }
        }

        Assertions.AssertThat(rejectedByThisRule).IsTrue();
    }

    /// <summary>
    /// <b>槽位不对整份拒绝</b>（武器槽里放了一件衣服）。
    /// </summary>
    [TestCase]
    public void WrongSlot_IsRejected_ByThatRule()
    {
        var save = ValidSave(SaveVersion.Current);
        save.Heroes[0].EquippedWeaponId = "皮甲";
        save.Heroes[0].WeaponDurability = 10;

        var r = SaveValidator.Validate(save);

        Assertions.AssertThat(r.IsValid).IsFalse();

        bool rejectedByThisRule = false;
        foreach (var err in r.Errors)
        {
            if (err.Path.EndsWith("equippedWeaponId") && err.Message.Contains("槽"))
            {
                rejectedByThisRule = true;
                break;
            }
        }

        Assertions.AssertThat(rejectedByThisRule).IsTrue();
    }

    /// <summary>
    /// <b>耐久越界整份拒绝</b>（超过该装备的耐久上限 / 为负）。
    /// </summary>
    [TestCase]
    public void Durability_OutOfRange_IsRejected_ByThatRule()
    {
        var tooBig = ValidSave(SaveVersion.Current);
        tooBig.Heroes[0].EquippedWeaponId = "石剑";
        tooBig.Heroes[0].WeaponDurability = 999;

        var rTooBig = SaveValidator.Validate(tooBig);
        Assertions.AssertThat(rTooBig.IsValid).IsFalse();
        bool rejectedTooBig = false;
        foreach (var err in rTooBig.Errors)
        {
            if (err.Path.EndsWith("weaponDurability")) { rejectedTooBig = true; break; }
        }
        Assertions.AssertThat(rejectedTooBig).IsTrue();

        var negative = ValidSave(SaveVersion.Current);
        negative.Heroes[0].EquippedWeaponId = "石剑";
        negative.Heroes[0].WeaponDurability = -1;

        var rNegative = SaveValidator.Validate(negative);
        Assertions.AssertThat(rNegative.IsValid).IsFalse();
        bool rejectedNegative = false;
        foreach (var err in rNegative.Errors)
        {
            if (err.Path.EndsWith("weaponDurability")) { rejectedNegative = true; break; }
        }
        Assertions.AssertThat(rejectedNegative).IsTrue();
    }

    /// <summary>
    /// <b>没穿却有耐久 ⇒ 拒绝</b>（残留数据 ⇒ 读出来会变成「幽灵耐久」）。
    /// </summary>
    [TestCase]
    public void NoEquipment_WithDurability_IsRejected_ByThatRule()
    {
        var save = ValidSave(SaveVersion.Current);
        save.Heroes[0].EquippedWeaponId = "";
        save.Heroes[0].WeaponDurability = 5;

        var r = SaveValidator.Validate(save);

        Assertions.AssertThat(r.IsValid).IsFalse();

        bool rejectedByThisRule = false;
        foreach (var err in r.Errors)
        {
            if (err.Path.EndsWith("weaponDurability")) { rejectedByThisRule = true; break; }
        }

        Assertions.AssertThat(rejectedByThisRule).IsTrue();
    }

    /// <summary>
    /// <b>写盘 → 读盘，装备与耐久原样回来</b>（存档里真的有这些字段）。
    /// </summary>
    [TestCase]
    public void Equipment_SurvivesWriteThenRead()
    {
        var save = ValidSave(SaveVersion.Current);
        save.Heroes[0].EquippedWeaponId = "猎弓";
        save.Heroes[0].WeaponDurability = 7;
        save.Heroes[0].EquippedArmorId = "锁甲";
        save.Heroes[0].ArmorDurability = 22;
        save.Village.Shops = new List<ShopStockSave>
        {
            new()
            {
                ShopId = "weapon_shop",
                Items = new List<ShopItemSave> { new() { ItemId = "石剑", Count = 2 } },
            },
        };

        string path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "d119-equipment-" + System.Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var port = new SavePort(path);
            port.Write(save);
            var loaded = port.Read();

            Assertions.AssertThat(loaded.Loaded).IsTrue();
            Assertions.AssertThat(loaded.Save!.Heroes[0].EquippedWeaponId).IsEqual("猎弓");
            Assertions.AssertThat(loaded.Save.Heroes[0].WeaponDurability).IsEqual(7);
            Assertions.AssertThat(loaded.Save.Heroes[0].EquippedArmorId).IsEqual("锁甲");
            Assertions.AssertThat(loaded.Save.Village.Shops.Count).IsEqual(1);
            Assertions.AssertThat(loaded.Save.Village.Shops[0].Items[0].ItemId).IsEqual("石剑");
            Assertions.AssertThat(loaded.Save.Village.Shops[0].Items[0].Count).IsEqual(2L);
        }
        finally
        {
            if (System.IO.File.Exists(path)) System.IO.File.Delete(path);
        }
    }

    // ── 商店货架 ────────────────────────────────────────────────────────

    /// <summary>货架上的货必须是**已登记的装备**。</summary>
    [TestCase]
    public void ShopStock_UnknownItem_IsRejected_ByThatRule()
    {
        var save = ValidSave(SaveVersion.Current);
        save.Village.Shops = new List<ShopStockSave>
        {
            new()
            {
                ShopId = "weapon_shop",
                Items = new List<ShopItemSave> { new() { ItemId = "石料", Count = 3 } },
            },
        };

        var r = SaveValidator.Validate(save);

        Assertions.AssertThat(r.IsValid).IsFalse();

        bool rejectedByThisRule = false;
        foreach (var err in r.Errors)
        {
            if (err.Path.EndsWith("itemId")) { rejectedByThisRule = true; break; }
        }

        Assertions.AssertThat(rejectedByThisRule).IsTrue();
    }

    /// <summary>货架数量不能为负。</summary>
    [TestCase]
    public void ShopStock_NegativeCount_IsRejected_ByThatRule()
    {
        var save = ValidSave(SaveVersion.Current);
        save.Village.Shops = new List<ShopStockSave>
        {
            new()
            {
                ShopId = "weapon_shop",
                Items = new List<ShopItemSave> { new() { ItemId = "石剑", Count = -1 } },
            },
        };

        var r = SaveValidator.Validate(save);

        Assertions.AssertThat(r.IsValid).IsFalse();

        bool rejectedByThisRule = false;
        foreach (var err in r.Errors)
        {
            if (err.Path.EndsWith("count")) { rejectedByThisRule = true; break; }
        }

        Assertions.AssertThat(rejectedByThisRule).IsTrue();
    }

    /// <summary>合法的货架（登记过的装备 + 非负数量）通过。</summary>
    [TestCase]
    public void ShopStock_Valid_IsAccepted()
    {
        var save = ValidSave(SaveVersion.Current);
        save.Village.Shops = new List<ShopStockSave>
        {
            new()
            {
                ShopId = "weapon_shop",
                Items = new List<ShopItemSave> { new() { ItemId = "石剑", Count = 2 } },
            },
            new()
            {
                ShopId = "armor_shop",
                Items = new List<ShopItemSave> { new() { ItemId = "皮甲", Count = 1 } },
            },
        };

        var r = SaveValidator.Validate(save);

        Assertions.AssertThat(r.IsValid).IsTrue();
    }
}
