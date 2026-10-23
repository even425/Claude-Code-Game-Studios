using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using ClaudeCodeGameStudios.Domain.Heroes;
using ClaudeCodeGameStudios.Domain.Village;

namespace ClaudeCodeGameStudios.Application;

/// <summary>
/// D-074 的<b>建造与试炼</b>接线 —— 玩家命令 → 领域动作 → 事件。
/// </summary>
/// <remarks>
/// <para><b>为什么单独一个文件</b>：<c>Main</c> 已经是巨型管理器（审计 R-09），
/// 而「建造 / 试炼」是<b>一整块业务规则</b>（扣资源、派人、推进、加成、事件）
/// ⇒ 放在这里才能单测；<c>Main</c> 只负责「玩家点了 → 调这里 → 刷新画面」。</para>
/// <para>⚠️ <b>本类是应用层</b>，不碰 Godot 类型（除了 <c>GD.Print</c> 的日志）。</para>
/// </remarks>
public sealed class BuildingCoordinator
{
    private readonly BuildingService _buildings;
    private readonly TrialTowerService _trialTower;
    private readonly Func<HeroId, HeroAgent?> _heroLookup;

    /// <param name="buildings">建造流程。</param>
    /// <param name="trialTower">试炼塔挑战。</param>
    /// <param name="heroLookup">按标识找英雄（试炼塔要知道「这个人现在什么属性」）。</param>
    /// <exception cref="ArgumentNullException">任一参数为 null。</exception>
    public BuildingCoordinator(
        BuildingService buildings,
        TrialTowerService trialTower,
        Func<HeroId, HeroAgent?> heroLookup)
    {
        _buildings = buildings ?? throw new ArgumentNullException(nameof(buildings));
        _trialTower = trialTower ?? throw new ArgumentNullException(nameof(trialTower));
        _heroLookup = heroLookup ?? throw new ArgumentNullException(nameof(heroLookup));
    }

    /// <summary>开工一栋建筑。</summary>
    /// <param name="definition">要建什么。</param>
    /// <param name="workers">派去施工的人数。</param>
    /// <returns>开工成功为 true。</returns>
    public bool StartBuild(BuildableBuilding definition, int workers)
        => _buildings.TryStart(definition, workers);

    /// <summary>推进施工（人力决定工期，D-074）。</summary>
    /// <param name="workedHours">这次过了多少游戏内小时。</param>
    /// <param name="onCompleted">建成时回调（用来发事件）。</param>
    public void Advance(double workedHours, Action<BuildableBuilding> onCompleted)
    {
        var completed = _buildings.AdvanceConstruction(workedHours);
        if (completed is not null) onCompleted(completed.Value);
    }

    /// <summary>去试炼塔打一场。</summary>
    /// <param name="heroId">参试英雄。</param>
    /// <param name="todayDay">今天是第几天（0 = 不知道 ⇒ 不限次数）。</param>
    /// <returns>试炼结果；英雄不存在时返回 null。</returns>
    /// <remarks>
    /// ⚠️ <b>D-113：每天限一次</b> ——「今天已经打过」⇒ <b>不打</b>。
    /// ⇒ 那条判定<b>只有一处实现</b>（在 <c>TrialTowerService</c> 里），
    /// 调用方（界面 / 采集器 / 未来的脚本）都走它 ⇒ 守得住。
    /// </remarks>
    public TrialTowerService.TrialOutcome? TryTrial(HeroId heroId, int todayDay = 0)
    {
        var agent = _heroLookup(heroId);
        if (agent is null) return null;

        var stats = ClaudeCodeGameStudios.Domain.Combat.CombatParameters.ForHero(
            agent.Personality, agent.Diligence);

        // ⚠️ **已得的加成要算进去**（D-074：永久）——
        // 否则第二次进塔时，战斗看到的属性是「没加成的」⇒ 加成看起来没生效。
        int bonus = agent.TrialTowerHealthBonus;
        if (bonus != 0) stats = stats with { Health = stats.Health + bonus };

        // ⚠️ `Try` **从不失败**（它要失败会抛）⇒ 这里拿到的一定是有效结果。
        var outcome = _trialTower.Try(heroId, agent.Name, stats, todayDay, agent.LastTrialDay);

        // ⚠️ **D-113：今天已经打过** ⇒ 什么都不做（不消耗今天的机会，也不加属性）。
        if (outcome.SkipReason == TrialTowerService.TrialSkipReason.AlreadyTriedToday)
        {
            return outcome;
        }

        if (outcome.Won)
        {
            // ⚠️ **加成由服务算好**（`StatsAfter`）⇒ 这里只负责**应用**它
            // ⇒ 「只加生命」这条规则只有一处实现（D-074）。
            // 这里取**生命的差值**（而不是直接用 HealthBonusPerWin）：
            // 若哪天服务改成也加攻防，差值会把攻防也算进来 ⇒ 应用的就不只是生命。
            agent.TrialTowerHealthBonus += outcome.StatsAfter.Health - stats.Health;
        }

        // ⚠️ **D-113：记下「今天试过了」**（不管赢没赢 ⇒ 每天只有一次机会）。
        if (todayDay > 0) agent.LastTrialDay = todayDay;

        return outcome;
    }
}
