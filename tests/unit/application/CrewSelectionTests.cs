using System;
using System.Collections.Generic;
using GdUnit4;
using ClaudeCodeGameStudios.Application;
using ClaudeCodeGameStudios.Domain.Heroes;
using ClaudeCodeGameStudios.Domain.Items;

namespace ClaudeCodeGameStudios.Tests.Application;

/// <summary>
/// <b>副本选人：优先派有装备的人</b>（D-122，开发者拍板）。
/// </summary>
/// <remarks>
/// <para><b>它守的是五件事</b>：
/// <list type="number">
/// <item><b>有装备的优先</b>（两轮扫描：先有装备的，再补齐）。</item>
/// <item><b>坏掉的装备不算「有装备」</b>（耐久 0 ⇒ 加成不生效 ⇒ 穿了等于没穿）。</item>
/// <item><b>没装备的人也会被派</b>（不能因为没装备就不派人）。</item>
/// <item><b>不可用的人不派</b>（在场 / 在别的任务里 由调用方判定）。</item>
/// <item><b>轮换起点照旧</b>（队伍仍轮换，不会总是同样两个人）。</item>
/// </list></para>
/// </remarks>
[TestSuite]
public class CrewSelectionTests
{
    private static readonly EquipmentCatalog Catalog = EquipmentCatalog.Default;

    private static HeroAgent MakeHero(string id, bool equipped, bool broken = false)
    {
        var hero = new HeroAgent { Name = id, Personality = "莽", Diligence = "随手" };
        hero.AssignId(new HeroId(id));

        if (equipped)
        {
            var sword = Catalog.Find(new ItemId("石剑"))!;
            hero.Equipment = EquipmentService.Equip(hero.Equipment, sword, Catalog);
            if (broken) hero.Equipment = hero.Equipment with { WeaponDurability = 0 };
        }

        return hero;
    }

    /// <summary>① 有装备的优先被选。</summary>
    [TestCase]
    public void EquippedHeroes_ArePickedFirst()
    {
        var a = MakeHero("h-a", equipped: false);
        var b = MakeHero("h-b", equipped: true);

        var crew = CrewSelection.Select(new List<HeroAgent> { a, b }, 0, 1, _ => true);

        Assertions.AssertThat(crew.Count).IsEqual(1);
        Assertions.AssertThat(crew[0].Id.Value).IsEqual("h-b");
    }

    /// <summary>② <b>坏掉的装备不算</b>（耐久 0 ⇒ 没有加成）。</summary>
    [TestCase]
    public void BrokenEquipment_DoesNotCountAsEquipped()
    {
        var a = MakeHero("h-a", equipped: false);
        var b = MakeHero("h-b", equipped: true, broken: true);

        var crew = CrewSelection.Select(new List<HeroAgent> { a, b }, 0, 1, _ => true);

        Assertions.AssertThat(crew.Count).IsEqual(1);
        Assertions.AssertThat(crew[0].Id.Value).IsEqual("h-a");
    }

    /// <summary>③ <b>没装备的人也会被派</b>（不能因为没装备就不派人）。</summary>
    [TestCase]
    public void WithoutEquipment_TheRestStillGetPicked()
    {
        var a = MakeHero("h-a", equipped: false);
        var b = MakeHero("h-b", equipped: false);

        var crew = CrewSelection.Select(new List<HeroAgent> { a, b }, 0, 2, _ => true);

        Assertions.AssertThat(crew.Count).IsEqual(2);
    }

    /// <summary>④ 不可用的人不派。</summary>
    [TestCase]
    public void UnavailableHeroes_AreSkipped()
    {
        var a = MakeHero("h-a", equipped: true);
        var b = MakeHero("h-b", equipped: false);

        var crew = CrewSelection.Select(new List<HeroAgent> { a, b }, 0, 1, h => h.Id.Value != "h-a");

        Assertions.AssertThat(crew.Count).IsEqual(1);
        Assertions.AssertThat(crew[0].Id.Value).IsEqual("h-b");
    }

    /// <summary>⑤ 轮换起点照旧（起点变了，队伍就换人）。</summary>
    [TestCase]
    public void TheStartIndex_StillRotatesTheCrew()
    {
        var a = MakeHero("h-a", equipped: false);
        var b = MakeHero("h-b", equipped: false);

        var first = CrewSelection.Select(new List<HeroAgent> { a, b }, 0, 1, _ => true);
        var second = CrewSelection.Select(new List<HeroAgent> { a, b }, 1, 1, _ => true);

        Assertions.AssertThat(first[0].Id.Value).IsNotEqual(second[0].Id.Value);
    }
}
