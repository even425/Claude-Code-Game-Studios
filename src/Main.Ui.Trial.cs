using Godot;
using System.Collections.Generic;
using ClaudeCodeGameStudios.Domain.Village;

public partial class Main : Node2D
{
    /// <summary>
    /// <b>试炼塔的界面</b>（D-113：从 <c>Main.Ui.cs</c> 拆出来 —— 那个文件
    /// 已经顶到 UT-01 的 1200 行上限）。
    /// </summary>
    /// <remarks>
    /// ⚠️ **只是搬家**：<c>OnTrialTower</c> 仍是界面的入口，
    /// <c>RunTrialTower</c> 是它的实现（<b>返回结果</b>，好让真机自检断言
    /// 「同一天第二次真的没打」—— 那件事只发生在这条路上）。
    /// </remarks>
    /// <summary>
    /// D-074：进试炼塔打一场（<b>第 1 个在场且空闲的英雄</b>）。
    /// </summary>
    /// <remarks>
    /// <b>赢了 ⇒ 永久加生命</b>；输了什么也不发生（无惩罚）。
    /// ⇒ 战斗完全确定（D-062 不设命中）⇒ 同一个英雄进塔，结果永远一样。
    /// </remarks>
    /// <summary>界面那条路：进试炼塔（薄壳 —— 只负责打印）。</summary>
    private void OnTrialTower()
    {
        TrialTowerUiResult result = RunTrialTower();
        if (result == TrialTowerUiResult.SkippedToday)
        {
            GD.Print("TRIAL 今天已经试过一次了（每天限一次）。");
        }
    }

    /// <summary>「进试炼塔」这条路的<b>结果</b>（D-113：要能断言「今天被拦下了」）。</summary>
    /// <remarks>
    /// ⚠️ <b>为什么要有这个返回值</b>：真机自检要验「<b>同一天第二次真的没打</b>」
    /// ⇒ 而那件事<b>只发生在界面这条路上</b>（界面负责挑人、传日号）
    /// ⇒ 自检若绕开界面，就<b>验不到界面漏传日号</b>这种接线错误。
    /// </remarks>
    private enum TrialTowerUiResult
    {
        /// <summary>试炼塔还没建好。</summary>
        NoTower,
        /// <summary>村口没人（都在路上或工地上）。</summary>
        NoHero,
        /// <summary>今天已经试过了 ⇒ <b>没打</b>（D-113）。</summary>
        SkippedToday,
        /// <summary>打赢了（永久加生命）。</summary>
        Won,
        /// <summary>没打过守卫（无惩罚）。</summary>
        Lost,
    }

    /// <summary>进试炼塔打一场（界面那条路的<b>实现</b>，返回结果供自检断言）。</summary>
    /// <returns>这次的结果。</returns>
    private TrialTowerUiResult RunTrialTower()
    {
        if (!_buildingService.IsCompleted(BuildableBuildings.TrialTower.Id))
        {
            GD.Print("TRIAL 试炼塔还没建好。");
            Refresh();
            return TrialTowerUiResult.NoTower;
        }

        HeroAgent? challenger = null;
        foreach (var h in _heroes)
        {
            if (h.Away || h.ToSite) continue;
            if (!IsPresent(h)) continue;
            challenger = h;
            break;
        }

        if (challenger is null)
        {
            GD.Print("TRIAL 村口没人（都在路上或工地上）。");
            Refresh();
            return TrialTowerUiResult.NoHero;
        }

        var outcome = _buildingCoordinator.TryTrial(challenger.Id, (int)_clock.Day);
        if (outcome is null)
        {
            GD.Print("TRIAL 找不到这个人。");
            Refresh();
            return TrialTowerUiResult.NoHero;
        }

        // ⚠️ **D-113：今天已经试过了** ⇒ 不是「他输了」，是「规则拦下了」
        // ⇒ 单独说，别让玩家以为他没打过守卫。
        if (outcome.Value.SkipReason
            == ClaudeCodeGameStudios.Application.TrialTowerService.TrialSkipReason.AlreadyTriedToday)
        {
            Refresh();
            return TrialTowerUiResult.SkippedToday;
        }

        if (outcome.Value.Won)
        {
            Emit(EvTrialTowerCleared, challenger.Name,
                new[] { StHeroTally, StVillageBuilding },
                new Dictionary<string, string>
                {
                    ["hero"] = challenger.Name,
                    ["bonus"] = outcome.Value.HealthBonusGained.ToString(),
                    ["total"] = challenger.TrialTowerHealthBonus.ToString(),
                },
                ev => Chronicle.TrialTowerWon((int)_clock.Day, ev.Payload["hero"], ev.Payload["total"]).Text);
            Refresh();
            return TrialTowerUiResult.Won;
        }

        GD.Print($"TRIAL {challenger.Name} 没打过试炼塔的守卫（无惩罚）。");
        Refresh();
        return TrialTowerUiResult.Lost;
    }
}
