using System.Linq;
using GdUnit4;
using ClaudeCodeGameStudios.Application;
using ClaudeCodeGameStudios.Domain.Combat;
using ClaudeCodeGameStudios.Domain.Items;

namespace ClaudeCodeGameStudios.Tests.Items;

/// <summary>
/// <b>装备本身</b>（D-119）：加成、耐久、穿上、磨损、修理。
/// </summary>
/// <remarks>
/// <para><b>它守的是七件事</b>：
/// <list type="number">
/// <item><b>加成生效</b>：穿上武器 ⇒ 攻击加上它的加成（衣服 ⇒ 防御）。</item>
/// <item><b>耐久为 0 ⇒ 加成不生效</b>（坏了就是没有防护 ⇒ 不是「减半」）。</item>
/// <item><b>穿上替换旧的</b>（不是叠穿）。</item>
/// <item><b>磨损只扣 1，且不低于 0</b>。</item>
/// <item><b>修理只修坏的</b>：好的不动、坏的修满。</item>
/// <item><b>修理费只算坏的那几件</b>。</item>
/// <item><b>每件装备都登记在物品表里</b>（否则发不出去 —— 发奖入口要求「可售」）。</item>
/// </list></para>
/// <para>
/// ⚠️ <b>这些规则住在应用层 / 领域层（纯函数）</b> ⇒ 单测直接调，
/// 改坏实现会立刻红（变异矩阵盯着它们）。
/// </para>
/// </remarks>
[TestSuite]
public class EquipmentTests
{
    private static readonly EquipmentCatalog Catalog = EquipmentCatalog.Default;

    private static CombatStats Base() => new(10, 4, 80);

    /// <summary>① 穿上武器 ⇒ 攻击加上它的加成；衣服 ⇒ 防御。</summary>
    [TestCase]
    public void Equipping_AddsTheBonus()
    {
        var sword = Catalog.Find(new ItemId("石剑"))!;
        var gear = EquipmentService.Equip(EquippedGear.Empty, sword, Catalog);

        var stats = EquipmentStats.Resolve(Base(), gear, Catalog);

        Assertions.AssertThat(stats.Attack).IsEqual(Base().Attack + sword.AttackBonus);
        Assertions.AssertThat(stats.Defense).IsEqual(Base().Defense);
        Assertions.AssertThat(stats.Health).IsEqual(Base().Health);
    }

    /// <summary>② 耐久为 0 ⇒ <b>加成不生效</b>（不是减半）。</summary>
    [TestCase]
    public void BrokenEquipment_GivesNoBonus()
    {
        var sword = Catalog.Find(new ItemId("石剑"))!;
        var gear = EquipmentService.Equip(EquippedGear.Empty, sword, Catalog) with { WeaponDurability = 0 };

        var stats = EquipmentStats.Resolve(Base(), gear, Catalog);

        Assertions.AssertThat(stats.Attack).IsEqual(Base().Attack);
    }

    /// <summary>③ 穿上第二把 ⇒ <b>替换</b>第一把（不是叠穿）。</summary>
    [TestCase]
    public void Equipping_ReplacesTheOldOne()
    {
        var sword = Catalog.Find(new ItemId("石剑"))!;
        var bow = Catalog.Find(new ItemId("猎弓"))!;

        var gear = EquipmentService.Equip(EquippedGear.Empty, sword, Catalog);
        gear = EquipmentService.Equip(gear, bow, Catalog);

        Assertions.AssertThat(gear.Weapon).IsEqual(bow.Id);
        Assertions.AssertThat(gear.WeaponDurability).IsEqual(bow.DurabilityMax);
    }

    /// <summary>④ 磨损：每次 -1，且<b>不低于 0</b>。</summary>
    [TestCase]
    public void WearDown_LosesOnePoint_ButNeverBelowZero()
    {
        var sword = Catalog.Find(new ItemId("石剑"))!;
        var gear = EquipmentService.Equip(EquippedGear.Empty, sword, Catalog);

        gear = EquipmentService.WearDown(gear);
        Assertions.AssertThat(gear.WeaponDurability).IsEqual(sword.DurabilityMax - 1);

        // 一直磨到 0，再磨也不能变成负数。
        for (int i = 0; i < sword.DurabilityMax + 5; i++) gear = EquipmentService.WearDown(gear);
        Assertions.AssertThat(gear.WeaponDurability).IsEqual(0);
    }

    /// <summary>⑤ 修理：<b>只修坏的</b>（好的不动、坏的修满）。</summary>
    [TestCase]
    public void Repair_OnlyFixesTheBrokenOnes()
    {
        var sword = Catalog.Find(new ItemId("石剑"))!;
        var armor = Catalog.Find(new ItemId("皮甲"))!;

        var gear = EquipmentService.Equip(EquippedGear.Empty, sword, Catalog);
        gear = EquipmentService.Equip(gear, armor, Catalog);
        gear = gear with { WeaponDurability = 0, ArmorDurability = 7 };

        var fixedGear = EquipmentService.Repair(gear, Catalog);

        Assertions.AssertThat(fixedGear.WeaponDurability).IsEqual(sword.DurabilityMax);
        Assertions.AssertThat(fixedGear.ArmorDurability).IsEqual(7);   // 好的不动
    }

    /// <summary>⑥ 修理费：<b>只算坏的那几件</b>。</summary>
    [TestCase]
    public void RepairCost_CountsOnlyTheBrokenOnes()
    {
        var sword = Catalog.Find(new ItemId("石剑"))!;
        var armor = Catalog.Find(new ItemId("皮甲"))!;

        var gear = EquipmentService.Equip(EquippedGear.Empty, sword, Catalog);
        gear = EquipmentService.Equip(gear, armor, Catalog);

        // 两件都坏 ⇒ 两笔修理费。
        var both = gear with { WeaponDurability = 0, ArmorDurability = 0 };
        Assertions.AssertThat(EquipmentService.RepairCostOf(both, Catalog))
            .IsEqual(sword.RepairCost + armor.RepairCost);

        // 只有武器坏 ⇒ 只有武器那笔。
        var one = gear with { WeaponDurability = 0 };
        Assertions.AssertThat(EquipmentService.RepairCostOf(one, Catalog)).IsEqual(sword.RepairCost);

        // 都没坏 ⇒ 0。
        Assertions.AssertThat(EquipmentService.RepairCostOf(gear, Catalog)).IsEqual(0L);
    }

    /// <summary>
    /// ⑦ 每件装备都登记在<b>物品表</b>里（否则发奖 / 收购站那条路走不通）。
    /// </summary>
    [TestCase]
    public void EveryEquipment_IsAlsoRegisteredAsAnItem()
    {
        var items = ClaudeCodeGameStudios.Domain.Items.ItemCatalog.Default;

        foreach (var def in Catalog.All)
        {
            Assertions.AssertThat(items.Contains(def.Id)).IsTrue();
        }
    }

    /// <summary>
    /// ⑧ 装备**不会**让属性变低（防与负数）⇒ 攻击力不会因装备而变低。
    /// </summary>
    [TestCase]
    public void Equipment_NeverLowersTheStats()
    {
        var armor = Catalog.Find(new ItemId("皮甲"))!;
        var gear = EquipmentService.Equip(EquippedGear.Empty, armor, Catalog);

        var stats = EquipmentStats.Resolve(Base(), gear, Catalog);

        Assertions.AssertThat(stats.Attack).IsEqual(Base().Attack + armor.AttackBonus);
        Assertions.AssertThat(stats.Defense).IsEqual(Base().Defense + armor.DefenseBonus);
    }

    /// <summary>
    /// ⑨ 装备的加成**只认攻**（防御 / 生命在当前战斗公式下无效 —— D-123 实测）。
    /// </summary>
    /// <remarks>
    /// ⚠️ 防御 / 生命两个字段**保留在表里（值 0）** ⇒ 将来战斗公式一改，填回去就自动生效。
    /// </remarks>
    [TestCase]
    public void Equipment_Bonuses_AreAttackOnly()
    {
        foreach (var def in Catalog.All)
        {
            Assertions.AssertThat(def.DefenseBonus).IsEqual(0);
        }
    }

    /// <summary>
    /// ⑩ **两档的加成必须是具体数值**（弱 +2 / 强 +4）—— D-124 拍板的「许可证」分级。
    /// </summary>
    /// <remarks>
    /// ⚠️ 这条守的是「**两档不能塌成一档**」：若猎弓也变成 +2，
    /// 弱装备与强装备就一样贵、一样强 ⇒ 「许可证」的设计就没了。
    /// </remarks>
    [TestCase]
    public void TheTwoTiers_HaveTheAgreedBonuses()
    {
        Assertions.AssertThat(Catalog.Find(new ItemId("石剑"))!.AttackBonus).IsEqual(2);
        Assertions.AssertThat(Catalog.Find(new ItemId("皮甲"))!.AttackBonus).IsEqual(2);
        Assertions.AssertThat(Catalog.Find(new ItemId("猎弓"))!.AttackBonus).IsEqual(4);
        Assertions.AssertThat(Catalog.Find(new ItemId("锁甲"))!.AttackBonus).IsEqual(4);
    }

    /// <summary>
    /// ⑪ **副本用自己那张难度表**（D-124）—— 且它与通用表**确实不同**。
    /// </summary>
    /// <remarks>
    /// ⚠️ 这条守的是「副本难度被抬高，而**试炼塔 / 狩猎 / 兽潮**的难度原封不动」
    /// ⇒ 通用表（`MonsterTiers`）仍是 60 / 150 / 120；副本表（`DungeonTiers`）是 60 / 210 / 180。
    /// </remarks>
    [TestCase]
    public void Dungeons_UseTheirOwnTierTable_AndTheGeneralTableIsUnchanged()
    {
        var general = ClaudeCodeGameStudios.Domain.Combat.CombatParameters.MonsterTiers;
        var dungeon = ClaudeCodeGameStudios.Domain.Combat.CombatParameters.DungeonTiers;

        Assertions.AssertThat(general.Length).IsEqual(3);
        Assertions.AssertThat(dungeon.Length).IsEqual(3);

        // 通用表**没动**（试炼塔 / 狩猎 / 兽潮靠它）。
        Assertions.AssertThat(general[1].Health).IsEqual(150);
        Assertions.AssertThat(general[2].Health).IsEqual(120);

        // 副本表**抬上去了**（装备 = 通关许可证）。
        Assertions.AssertThat(dungeon[1].Health).IsEqual(210);
        Assertions.AssertThat(dungeon[2].Health).IsEqual(180);

        // 查属性的入口也确实是两张表。
        Assertions.AssertThat(ClaudeCodeGameStudios.Domain.Combat.CombatParameters.MonsterFor(2).Health).IsEqual(120);
        Assertions.AssertThat(ClaudeCodeGameStudios.Domain.Combat.CombatParameters.DungeonFor(2).Health).IsEqual(180);
    }
}
