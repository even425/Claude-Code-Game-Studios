using GdUnit4;
using ClaudeCodeGameStudios.Application;
using ClaudeCodeGameStudios.Domain.Combat;
using ClaudeCodeGameStudios.Domain.Heroes;

namespace ClaudeCodeGameStudios.Tests.Application;

/// <summary>
/// <b>试炼塔每天限一次</b>（D-113，开发者拍板「给试炼塔加限制」）。
/// </summary>
/// <remarks>
/// <para><b>为什么必须有这条</b>：试炼塔此前<b>没有任何限制</b> ⇒
/// 一个「莽 / 肝」的英雄可以<b>无限刷</b> ⇒ 永久加生命<b>无上限</b>
/// ⇒ 副本难度形同虚设（D-074 的「只加生命」在无限刷面前等于没有门槛）。</para>
///
/// <para><b>它守的是六件事</b>：
/// <list type="number">
/// <item>同一天第二次 ⇒ <b>不打</b>（不是「输了」⇒ 单独一个跳过原因）。</item>
/// <item>跳过时<b>没有战斗</b>（战斗日志是空的 ⇒ 证明真的没开打）。</item>
/// <item>跳过时<b>没有加成</b>。</item>
/// <item>换一天 ⇒ 照常能试。</item>
/// <item>协调器：试过一次后<b>记下「今天试过了」</b>（赢了记、输了也记）。</item>
/// <item>协调器：今天已经打过 ⇒ <b>什么都不改</b>（加成不变、日号不变）。</item>
/// </list></para>
/// </remarks>
[TestSuite]
public class TrialTowerDailyLimitTests
{
    private static readonly HeroId Alice = new("h-alice");

    /// <summary>建一个「莽 / 肝」的英雄（打得过精英档守卫）。</summary>
    private static HeroAgent MakeHero(string personality = "莽", string diligence = "肝")
    {
        var hero = new HeroAgent { Name = "甲", Personality = personality, Diligence = diligence };
        hero.AssignId(Alice);
        return hero;
    }

    /// <summary>① 同一天第二次 ⇒ 不打（跳过原因是「今天已经试过了」）。</summary>
    [TestCase]
    public void SecondTrialOnTheSameDay_IsSkipped()
    {
        var combat = new FormulaCombat();
        var tower = new TrialTowerService(combat, combat);
        var stats = CombatParameters.ForHero("莽", "肝");

        var outcome = tower.Try(Alice, "甲", stats, todayDay: 7, lastTrialDay: 7);

        Assertions.AssertThat(outcome.SkipReason)
            .IsEqual(TrialTowerService.TrialSkipReason.AlreadyTriedToday);
        Assertions.AssertThat(outcome.Won).IsFalse();
        Assertions.AssertThat(outcome.HealthBonusGained).IsEqual(0);
    }

    /// <summary>② 跳过时<b>没有战斗</b>（日志为空 ⇒ 证明真的没开打）。</summary>
    [TestCase]
    public void SkippedTrial_HasNoBattleLog()
    {
        var combat = new FormulaCombat();
        var tower = new TrialTowerService(combat, combat);
        var stats = CombatParameters.ForHero("莽", "肝");

        var outcome = tower.Try(Alice, "甲", stats, todayDay: 7, lastTrialDay: 7);

        Assertions.AssertThat(outcome.Log.Count).IsEqual(0);
    }

    /// <summary>③ 换一天 ⇒ 照常能试（限制只是「每天一次」）。</summary>
    [TestCase]
    public void NextDay_CanTrialAgain()
    {
        var combat = new FormulaCombat();
        var tower = new TrialTowerService(combat, combat);
        var stats = CombatParameters.ForHero("莽", "肝");

        var outcome = tower.Try(Alice, "甲", stats, todayDay: 8, lastTrialDay: 7);

        Assertions.AssertThat(outcome.SkipReason).IsEqual(TrialTowerService.TrialSkipReason.None);
        Assertions.AssertThat(outcome.Won).IsTrue();
        Assertions.AssertThat(outcome.HealthBonusGained)
            .IsEqual(TrialTowerService.HealthBonusPerWin);
    }

    /// <summary>④ 协调器：试过一次 ⇒ 记下「今天试过了」。</summary>
    [TestCase]
    public void Coordinator_RecordsTheTrialDay()
    {
        var combat = new FormulaCombat();
        var tower = new TrialTowerService(combat, combat);
        var hero = MakeHero();
        var coordinator = new BuildingCoordinator(
            new BuildingService(new ClaudeCodeGameStudios.Domain.Village.Village("v1"),
                new ClaudeCodeGameStudios.Domain.Village.ResourceId("金币"),
                new ClaudeCodeGameStudios.Domain.Village.ResourceId("材料")),
            tower, id => hero, ClaudeCodeGameStudios.Domain.Items.EquipmentCatalog.Default);

        coordinator.TryTrial(Alice, todayDay: 12);

        Assertions.AssertThat(hero.LastTrialDay).IsEqual(12);
    }

    /// <summary>⑤ 协调器：今天已经打过 ⇒ <b>什么都不改</b>（加成不变、日号不变）。</summary>
    [TestCase]
    public void Coordinator_SameDaySecondTrial_ChangesNothing()
    {
        var combat = new FormulaCombat();
        var tower = new TrialTowerService(combat, combat);
        var hero = MakeHero();
        var coordinator = new BuildingCoordinator(
            new BuildingService(new ClaudeCodeGameStudios.Domain.Village.Village("v1"),
                new ClaudeCodeGameStudios.Domain.Village.ResourceId("金币"),
                new ClaudeCodeGameStudios.Domain.Village.ResourceId("材料")),
            tower, id => hero, ClaudeCodeGameStudios.Domain.Items.EquipmentCatalog.Default);

        coordinator.TryTrial(Alice, todayDay: 12);
        int bonusAfterFirst = hero.TrialTowerHealthBonus;
        hero.LastTrialDay = 12;      // 明确：今天已经打过

        var second = coordinator.TryTrial(Alice, todayDay: 12);

        Assertions.AssertThat(second!.Value.SkipReason)
            .IsEqual(TrialTowerService.TrialSkipReason.AlreadyTriedToday);
        Assertions.AssertThat(hero.TrialTowerHealthBonus).IsEqual(bonusAfterFirst);
    }

    /// <summary>
    /// ⑥ <b>输了也记「今天试过了」</b> —— 每天只有<b>一次</b>机会，不是「一次机会只给赢的人」。
    /// </summary>
    [TestCase]
    public void Coordinator_LosingTrial_StillConsumesTheDay()
    {
        var combat = new FormulaCombat();
        var tower = new TrialTowerService(combat, combat);
        // 弱组合（傲 / 挂机：攻 5 / 防 4 / 命 60）⇒ 打不过精英档守卫。
        var hero = MakeHero("傲", "挂机");
        var coordinator = new BuildingCoordinator(
            new BuildingService(new ClaudeCodeGameStudios.Domain.Village.Village("v1"),
                new ClaudeCodeGameStudios.Domain.Village.ResourceId("金币"),
                new ClaudeCodeGameStudios.Domain.Village.ResourceId("材料")),
            tower, id => hero, ClaudeCodeGameStudios.Domain.Items.EquipmentCatalog.Default);

        var outcome = coordinator.TryTrial(Alice, todayDay: 5);

        Assertions.AssertThat(outcome!.Value.Won).IsFalse();
        Assertions.AssertThat(hero.LastTrialDay).IsEqual(5);
    }
}
