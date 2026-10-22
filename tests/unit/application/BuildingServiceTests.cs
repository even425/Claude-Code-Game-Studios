using System;
using System.Collections.Generic;
using System.Linq;
using GdUnit4;
using ClaudeCodeGameStudios.Application;
using ClaudeCodeGameStudios.Domain.Combat;
using ClaudeCodeGameStudios.Domain.Heroes;
using ClaudeCodeGameStudios.Domain.Village;

namespace ClaudeCodeGameStudios.Tests.Application;

/// <summary>
/// <b>建造流程</b>（D-074）：围栏与试炼塔的建造、工期与效果。
/// </summary>
/// <remarks>
/// <para><b>它守的是五件事</b>：
/// <list type="number">
/// <item><b>开工要扣两样资源</b>，且<b>一样不够就一样都不扣</b>（I15 / I12）。</item>
/// <item><b>工期由人力决定</b>：人越多盖得越快；没人就<b>停工</b>。</item>
/// <item><b>建成才生效</b>：半成品围栏<b>不</b>提前放开摆摊范围。</item>
/// <item><b>试炼塔只加生命</b>，且加的是<b>永久</b>加成。</item>
/// <item><b>战斗完全确定</b>：同一个英雄进塔，结果永远一样（D-062）。</item>
/// </list>
/// <para><b>为什么要有「一样都不扣」这条</b>：金币与材料是<b>分别校验、一起提交</b>的
/// ⇒ 只扣一样就是「扣了钱没建成」—— 而那种半笔账<b>外面看不出来</b>。</para>
/// </remarks>
[TestSuite]
public class BuildingServiceTests
{
    private static readonly ResourceId Gold = new("金币");
    private static readonly ResourceId Material = new("材料");

    private static (BuildingService Service, Village Village) NewService(
        long gold = 1000, long material = 1000)
    {
        var village = new Village("test");
        village.Add(Gold, gold);
        village.Add(Material, material);
        // 「市集」（真实游戏开局就有它，影响区 11×11）。
        // ⚠️ **为什么夹具里要有它**（D-093）：
        // 新规则「落点必须在村子的地盘里」守的是「落在至少一个已建成建筑的影响区里」
        // ⇒ 没有市集就没有「村子的地盘」。
        // 不放市集的夹具会让「能开工」这条断言失效。
        village.TryPlace(new ClaudeCodeGameStudios.Domain.Village.BuildingInstance(
            new ClaudeCodeGameStudios.Domain.Village.BuildingId("village_square"),
            gridX: -5, gridY: -5, footprintW: 1, footprintH: 1,
            constructionProgress: 1.0,
            affectsAreaW: 11, affectsAreaH: 11));
        return (new BuildingService(village, Gold, Material), village);
    }

    // ── 开工与扣款 ────────────────────────────────────────────────────────

    /// <summary>开工成功 ⇒ 金币与材料<b>各扣一份</b>，建筑进入在建状态。</summary>
    [TestCase]
    public void TryStart_DeductsGoldAndMaterial_AndPlacesTheBuilding()
    {
        var (service, village) = NewService(gold: 500, material: 400);
        var fence = BuildableBuildings.Fence;

        bool started = service.TryStart(fence, workers: 3);

        Assertions.AssertThat(started).IsTrue();
        Assertions.AssertThat(village.Get(Gold)).IsEqual(500 - fence.GoldCost);
        Assertions.AssertThat(village.Get(Material)).IsEqual(400 - fence.MaterialCost);
        Assertions.AssertThat(village.Buildings.Count).IsEqual(2);   // 市集 + 新建的那一栋
        // ⚠️ **按标识取**（不用下标 0）：夹具里现在还有市集（D-093 的落点规则）。
        var newBuilding = village.Buildings.First(b => b.Id.Value == fence.Id.Value);
        Assertions.AssertThat(newBuilding.Status).IsEqual(BuildingStatus.UnderConstruction);
    }

    /// <summary>钱不够 ⇒ <b>材料也不许扣</b>（I15：两边都不扣）。</summary>
    [TestCase]
    public void TryStart_WithoutEnoughGold_DeductsNothing()
    {
        var (service, village) = NewService(gold: 10, material: 400);
        var fence = BuildableBuildings.Fence;

        bool started = service.TryStart(fence, workers: 3);

        Assertions.AssertThat(started).IsFalse();
        Assertions.AssertThat(village.Get(Gold)).IsEqual(10);
        Assertions.AssertThat(village.Get(Material)).IsEqual(400);   // ← 关键：材料没动
        Assertions.AssertThat(village.Buildings.Count).IsEqual(1);   // 只有市集
    }

    /// <summary>材料不够 ⇒ 金币也不许扣（另一半）。</summary>
    [TestCase]
    public void TryStart_WithoutEnoughMaterial_DeductsNothing()
    {
        var (service, village) = NewService(gold: 500, material: 1);
        var fence = BuildableBuildings.Fence;

        bool started = service.TryStart(fence, workers: 3);

        Assertions.AssertThat(started).IsFalse();
        Assertions.AssertThat(village.Get(Gold)).IsEqual(500);      // ← 关键：金币没动
        Assertions.AssertThat(village.Get(Material)).IsEqual(1);
    }

    /// <summary>已建成的不能再建一次。</summary>
    [TestCase]
    public void TryStart_WhenAlreadyCompleted_Refuses()
    {
        var (service, _) = NewService();
        var fence = BuildableBuildings.Fence;

        service.TryStart(fence, workers: 1);
        service.AdvanceConstruction(fence.BaseBuildHours);   // 一个人盖满工时 ⇒ 建成

        bool second = service.TryStart(fence, workers: 1);

        Assertions.AssertThat(second).IsFalse();
        Assertions.AssertThat(service.IsCompleted(fence.Id)).IsTrue();
    }

    /// <summary>
    /// <b>扣了款却放不下建筑 ⇒ 必须退款</b>（不能留下「扣了钱没建成」的半笔账）。
    /// </summary>
    /// <remarks>
    /// <para>⚠️ <b>这条守的是唯一一条「扣款之后」还能失败的路径</b>：
    /// 钱与材料先扣（校验过 ⇒ 一定扣得动），<b>然后</b>才放建筑；
    /// 若放不下（落点被占）⇒ 就已经扣过款了 ⇒ <b>必须退回去</b>。</para>
    /// <para>⚠️ <b>为什么这条必须专门写</b>：「钱不够 / 材料不够」那两条
    /// <b>在扣款之前</b>就被挡住了（<c>CanStart</c>）⇒ 测它们
    /// <b>永远碰不到退款分支</b> ⇒ 退款代码看着在防半笔账，实际一次都没被验过。</para>
    /// </remarks>
    [TestCase]
    public void TryStart_WhenPlacementFails_RefundsBothResources()
    {
        var (service, village) = NewService(gold: 500, material: 400);
        var fence = BuildableBuildings.Fence;

        // 先在**落点**上放一个别的建筑 ⇒ 围栏放不下。
        var blocker = new BuildingInstance(
            new BuildingId("blocker"),
            fence.GridX, fence.GridY, 1, 1,
            constructionProgress: 1.0);
        Assertions.AssertThat(village.TryPlace(blocker)).IsTrue();

        bool started = service.TryStart(fence, workers: 1);

        Assertions.AssertThat(started).IsFalse();
        // ⚠️ 关键：金币与材料**都要退回去**（半笔账 = 只退一样）。
        Assertions.AssertThat(village.Get(Gold)).IsEqual(500);
        Assertions.AssertThat(village.Get(Material)).IsEqual(400);
    }

    // ── 工期由人力决定 ────────────────────────────────────────────────────

    /// <summary>人越多盖得越快 ⇒ 同样时间，人多的那栋进度更高。</summary>
    [TestCase]
    public void MoreWorkers_MeansFasterProgress()
    {
        var fence = BuildableBuildings.Fence;

        // ⚠️ 推进的时间要**小于**工期 ÷ 人数，否则那栋直接建成了
        // （进度到 1 ⇒ 从 ActiveConstructions 里消失 ⇒ 取不到）。
        // 这里推进「工期 ÷ 8」：1 人 ⇒ 12.5%，4 人 ⇒ 50%，两边都没建完。
        double slice = fence.BaseBuildHours / 8.0;

        var (service, _) = NewService();
        service.TryStart(fence, workers: 1);
        service.SetWorkers(fence.Id, 1);
        service.AdvanceConstruction(slice);
        var oneWorker = service.ActiveConstructions[0].Instance.ConstructionProgress;

        // ⚠️ 第二次用**另一个村庄实例**（不是同一个）——
        // 同一村庄里围栏的落点已被占 ⇒ 第二次开工会被拒（占位与已有建筑重叠）。
        var (service2, _) = NewService();
        service2.TryStart(fence, workers: 4);
        service2.SetWorkers(fence.Id, 4);
        service2.AdvanceConstruction(slice);
        var fourWorkers = service2.ActiveConstructions[0].Instance.ConstructionProgress;

        // ⚠️ GdUnit4 没有 IsGreaterThan ⇒ 用「差值 ≥ 正数」表达「严格更大」。
        Assertions.AssertThat(fourWorkers - oneWorker).IsGreaterEqual(0.000001);
    }

    /// <summary>没人施工 ⇒ <b>停工</b>（进度不动，但已付的资源不退）。</summary>
    [TestCase]
    public void WithoutWorkers_ConstructionStalls()
    {
        var (service, village) = NewService();
        var fence = BuildableBuildings.Fence;

        service.TryStart(fence, workers: 0);
        service.SetWorkers(fence.Id, 0);
        service.AdvanceConstruction(fence.BaseBuildHours * 10.0);

        Assertions.AssertThat(service.ActiveConstructions[0].Instance.ConstructionProgress)
            .IsEqual(0.0);
        // 资源已经花在工地上了 —— 停工不退钱。
        Assertions.AssertThat(village.Get(Gold)).IsEqual(1000 - fence.GoldCost);
    }

    /// <summary>一个人盖满工时 ⇒ 建成（进度到 1）。</summary>
    [TestCase]
    public void FullWorkHours_CompletesTheBuilding()
    {
        var (service, _) = NewService();
        var fence = BuildableBuildings.Fence;

        service.TryStart(fence, workers: 1);
        service.SetWorkers(fence.Id, 1);
        var completed = service.AdvanceConstruction(fence.BaseBuildHours);

        Assertions.AssertThat(completed.HasValue).IsTrue();
        Assertions.AssertThat(completed!.Value.Id.Value).IsEqual(fence.Id.Value);
        Assertions.AssertThat(service.IsCompleted(fence.Id)).IsTrue();
        Assertions.AssertThat(service.ActiveConstructions.Count).IsEqual(0);
    }

    // ── 建成才生效 ────────────────────────────────────────────────────────

    /// <summary>
    /// <b>半成品围栏不放开摆摊范围</b>（D-074：建成才永久生效）。
    /// </summary>
    /// <remarks>
    /// ⚠️ 这是「反装饰」的关键一条：若未建成就生效 ⇒ 玩家点一下开工，
    /// 围栏还没盖好就能多摆摊 ⇒ 那条「建成才生效」就是一句装饰。
    /// </remarks>
    [TestCase]
    public void UnfinishedFence_DoesNotWidenTheStallArea()
    {
        var (service, village) = NewService();
        var fence = BuildableBuildings.Fence;

        service.TryStart(fence, workers: 1);

        var building = village.Buildings.First(b => b.Id.Value == fence.Id.Value);
        Assertions.AssertThat(building.Status).IsEqual(BuildingStatus.UnderConstruction);
        // 未建成 ⇒ 影响区判定一律 false（BuildingInstance.IsCellInAffectsArea）。
        Assertions.AssertThat(building.IsCellInAffectsArea(0, 0)).IsFalse();
    }

    /// <summary>建成后的围栏 ⇒ 影响区生效（范围真的扩大了）。</summary>
    [TestCase]
    public void CompletedFence_ExpandsTheUsableArea()
    {
        var (service, village) = NewService();
        var fence = BuildableBuildings.Fence;

        service.TryStart(fence, workers: 1);
        service.SetWorkers(fence.Id, 1);
        service.AdvanceConstruction(fence.BaseBuildHours);

        var building = village.Buildings.First(b => b.Id.Value == fence.Id.Value);
        Assertions.AssertThat(building.Status).IsEqual(BuildingStatus.Completed);
        Assertions.AssertThat(building.IsCellInAffectsArea(0, 0)).IsTrue();
    }

    // ── 试炼塔：只加生命、永久、战斗确定 ─────────────────────────────────

    /// <summary>打赢 ⇒ 生命永久 +20；<b>攻防不变</b>（D-074：只加生命）。</summary>
    [TestCase]
    public void TrialTower_Winning_AddsHealthOnly_Permanently()
    {
        var combat = new FormulaCombat();
        var tower = new TrialTowerService(combat, combat);

        var baseStats = CombatParameters.ForHero("莽", "肝");
        var outcome = tower.Try(new HeroId("h-1"), "甲", baseStats);

        Assertions.AssertThat(outcome.Won).IsTrue();
        Assertions.AssertThat(outcome.HealthBonusGained)
            .IsEqual(TrialTowerService.HealthBonusPerWin);
    }

    /// <summary>输了 ⇒ <b>没有加成</b>（无惩罚）。</summary>
    [TestCase]
    public void TrialTower_Losing_GivesNoBonus()
    {
        var combat = new FormulaCombat();
        var tower = new TrialTowerService(combat, combat);

        // 挂机英雄：攻 5 / 防 4 / 命 60 ⇒ 打不过精英档守卫。
        var weak = CombatParameters.ForHero("随手", "挂机");
        var outcome = tower.Try(new HeroId("h-1"), "甲", weak);

        Assertions.AssertThat(outcome.Won).IsFalse();
        Assertions.AssertThat(outcome.HealthBonusGained).IsEqual(0);
    }

    /// <summary>
    /// <b>门槛真的有区分度</b>：九种英雄原型里<b>只有最强的组合能过</b>。
    /// </summary>
    /// <remarks>
    /// <para>⚠️ 这条是「试炼塔有门槛」的**唯一**证据。
    /// 实现初稿用普通档做守卫 ⇒ <b>实测发现九种原型全都能赢</b>
    /// （连挂机都是 17 回合胜）⇒ 那时的试炼塔等于<b>没有门槛</b>（加成白送）。
    /// ⇒ 守卫改成精英档后，只有「莽 / 肝」能过。</para>
    /// <para>⚠️ 为什么不写死「只有一种能过」：那样守卫档位一改，守卫就红
    /// ⇒ 而那正是**要提醒人**的改动。这里断言的是<b>性质</b>：
    /// 「有人能过、有人过不了」⇒ 门槛存在。</para>
    /// </remarks>
    [TestCase]
    public void TrialTower_Guard_ActuallyGatesWhoCanPass()
    {
        var combat = new FormulaCombat();
        var tower = new TrialTowerService(combat, combat);

        string[] personalities = { "莽", "傲", "随手" };
        string[] diligences = { "肝", "随手", "挂机" };

        int canPass = 0;
        int total = 0;

        foreach (var p in personalities)
        {
            foreach (var d in diligences)
            {
                total++;
                var stats = CombatParameters.ForHero(p, d);
                var outcome = tower.Try(new HeroId("h-1"), "甲", stats);
                if (outcome.Won) canPass++;
            }
        }

        // 门槛存在：不是人人都能过，也不是谁都过不了。
        Assertions.AssertThat(canPass).IsGreaterEqual(1);
        Assertions.AssertThat(canPass < total).IsTrue();
    }

    /// <summary>
    /// <b>战斗完全确定</b>（D-062 不设命中 ⇒ 没有随机数）：
    /// 同一个英雄进塔两次，结果<b>必须一样</b>。
    /// </summary>
    /// <remarks>
    /// ⚠️ 这条守的是「试炼塔的门槛是<b>可测的</b>」——
    /// 若战斗带随机，同一个英雄有时过有时不过 ⇒ 玩家会以为是运气，
    /// 而设计想说的是「你不够强」。
    /// </remarks>
    [TestCase]
    public void TrialTower_SameHeroTwice_GivesTheSameResult()
    {
        var combat = new FormulaCombat();
        var tower = new TrialTowerService(combat, combat);
        var stats = CombatParameters.ForHero("挂机" == "" ? "莽" : "傲", "随手");

        var first = tower.Try(new HeroId("h-1"), "甲", stats);
        var second = tower.Try(new HeroId("h-1"), "甲", stats);

        Assertions.AssertThat(second.Won).IsEqual(first.Won);
        Assertions.AssertThat(second.HealthBonusGained).IsEqual(first.HealthBonusGained);
    }

    /// <summary>
    /// 加成只加在<b>生命</b>上：攻防必须与基础值<b>逐项相等</b>。
    /// </summary>
    /// <remarks>
    /// <para>⚠️ 为什么不只断言「生命变了」：那样加攻防也会绿
    /// ⇒ D-074 的「只加生命」就没有被守住。</para>
    /// <para>⚠️ <b>为什么断言服务返回的 <c>StatsAfter</c> 而不是手算</b>：
    /// 「只加生命」这条规则的<b>实现只有一处</b>（<c>TrialTowerService.Try</c>）——
    /// 若测试自己手算「生命 +20」，那测的是**测试的算术**，不是规则。
    /// ⇒ 断言必须打在实际产出的属性上。</para>
    /// </remarks>
    [TestCase]
    public void TrialTower_Bonus_ChangesHealthAndNothingElse()
    {
        var combat = new FormulaCombat();
        var tower = new TrialTowerService(combat, combat);
        var baseStats = CombatParameters.ForHero("莽", "肝");

        var outcome = tower.Try(new HeroId("h-1"), "甲", baseStats);

        Assertions.AssertThat(outcome.Won).IsTrue();
        // 生命：加了正好一份加成。
        Assertions.AssertThat(outcome.StatsAfter.Health)
            .IsEqual(baseStats.Health + TrialTowerService.HealthBonusPerWin);
        // 攻防：**逐项相等**（加了就红）。
        Assertions.AssertThat(outcome.StatsAfter.Attack).IsEqual(baseStats.Attack);
        Assertions.AssertThat(outcome.StatsAfter.Defense).IsEqual(baseStats.Defense);
    }
}
