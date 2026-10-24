using System.Collections.Generic;
using GdUnit4;
using ClaudeCodeGameStudios.Application;
using ClaudeCodeGameStudios.Domain.Combat;
using ClaudeCodeGameStudios.Domain.Heroes;
using ClaudeCodeGameStudios.Domain.Items;

namespace ClaudeCodeGameStudios.Tests.Application;

/// <summary>
/// <b>战斗后的装备磨损</b>（D-121）—— 谁上了战场，谁的装备掉耐久。
/// </summary>
/// <remarks>
/// <para><b>它守的是四件事</b>：
/// <list type="number">
/// <item><b>参战的人磨损</b>（每人 -1 点耐久）。</item>
/// <item><b>没参战的人不磨损</b>（名单外的英雄毫发无损）。</item>
/// <item><b>找不到的人不算</b>（英雄不在村里 ⇒ 跳过，<b>不虚报</b>）。</item>
/// <item><b>只有战斗型事件才磨损</b>（商队来访也带参与者，但他们没打仗）。</item>
/// </list></para>
/// </remarks>
[TestSuite]
public class EquipmentWearDownTests
{
    private static readonly EquipmentCatalog Catalog = EquipmentCatalog.Default;

    private static HeroAgent MakeHero(string id, string name = "甲")
    {
        var hero = new HeroAgent { Name = name, Personality = "莽", Diligence = "随手" };
        hero.AssignId(new HeroId(id));
        return hero;
    }

    /// <summary>给英雄穿上一把剑（耐久满）。</summary>
    private static HeroAgent WithSword(string id)
    {
        var hero = MakeHero(id);
        var sword = Catalog.Find(new ItemId("石剑"))!;
        hero.Equipment = EquipmentService.Equip(hero.Equipment, sword, Catalog);
        return hero;
    }

    /// <summary>① 参战的人磨损（每人 -1 点耐久）。</summary>
    [TestCase]
    public void Participants_LoseOneDurability()
    {
        var a = WithSword("h-a");
        var b = WithSword("h-b");
        var sword = Catalog.Find(new ItemId("石剑"))!;

        int worn = EquipmentWearDown.Apply(
            new List<HeroAgent> { a, b }, new[] { "h-a", "h-b" });

        Assertions.AssertThat(worn).IsEqual(2);
        Assertions.AssertThat(a.Equipment.WeaponDurability).IsEqual(sword.DurabilityMax - 1);
        Assertions.AssertThat(b.Equipment.WeaponDurability).IsEqual(sword.DurabilityMax - 1);
    }

    /// <summary>② <b>没参战的人不磨损</b>。</summary>
    [TestCase]
    public void NonParticipants_AreUntouched()
    {
        var fighter = WithSword("h-a");
        var bystander = WithSword("h-b");
        var sword = Catalog.Find(new ItemId("石剑"))!;

        EquipmentWearDown.Apply(new List<HeroAgent> { fighter, bystander }, new[] { "h-a" });

        Assertions.AssertThat(fighter.Equipment.WeaponDurability).IsEqual(sword.DurabilityMax - 1);
        Assertions.AssertThat(bystander.Equipment.WeaponDurability).IsEqual(sword.DurabilityMax);
    }

    /// <summary>③ 找不到的人<b>不算磨损</b>（不虚报）。</summary>
    [TestCase]
    public void UnknownParticipant_IsNotCounted()
    {
        var a = WithSword("h-a");
        var sword = Catalog.Find(new ItemId("石剑"))!;

        int worn = EquipmentWearDown.Apply(new List<HeroAgent> { a }, new[] { "h-ghost" });

        Assertions.AssertThat(worn).IsEqual(0);
        Assertions.AssertThat(a.Equipment.WeaponDurability).IsEqual(sword.DurabilityMax);
    }

    /// <summary>
    /// ④ <b>只有战斗型事件才磨损</b> —— 兽潮 / 盗匪是，商队不是。
    /// </summary>
    /// <remarks>
    /// ⚠️ 这条守的是「参与了大事件 ≠ 打了仗」：
    /// 商队来访也带参与者，可他们**没打任何一场** ⇒ 不能磨损。
    /// </remarks>
    [TestCase]
    public void OnlyCombatTemplates_AreMarkedAsCombat()
    {
        var combat = new FormulaCombat();
        var stats = CombatParameters.ForHero("莽", "随手");

        var beastTide = ClaudeCodeGameStudios.Application.WorldEventTemplates.BeastTide(
            7, combat, combat, _ => stats,
            () => new BeastTideTuning());

        var bandit = ClaudeCodeGameStudios.Application.WorldEventTemplates.BanditRaid(
            9, combat, combat, _ => stats,
            () => new BanditTuning());

        var caravan = ClaudeCodeGameStudios.Application.WorldEventTemplates.Caravan(
            5, new CaravanOffer("石料", 3, 5L));

        Assertions.AssertThat(beastTide.IsCombat).IsTrue();
        Assertions.AssertThat(bandit.IsCombat).IsTrue();
        Assertions.AssertThat(caravan.IsCombat).IsFalse();
    }
}
