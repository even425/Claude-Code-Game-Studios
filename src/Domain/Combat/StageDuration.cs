using System;
using System.Collections.Generic;

namespace ClaudeCodeGameStudios.Domain.Combat;

/// <summary>
/// 阶段耗时 —— <b>战斗回合 ↔ 游戏内时间</b>的换算（D-073）。
/// </summary>
/// <remarks>
/// <para><b>它解决什么</b>：基线 §9.5 R4 要求「关系值的每次变化都能追溯到一个具体经历」，
/// 而「具体经历」必须有<b>时间坐标</b> ⇒ 副本经历了几回合、占了多少游戏内小时，
/// 是关系变化可追溯的底座。副本任务本身已经占用一段时间（`Job.Hours`），
/// 本类负责的是<b>战斗本身</b>那部分耗时。</para>
/// <para><b>为什么放在领域层</b>：它是纯换算（无状态、无 IO）⇒ 与时钟解耦，
/// 可离线验算；时钟那一侧（现实秒 ↔ 模拟秒）仍属 T-001。</para>
/// <para><b>单位</b>：<b>游戏内小时</b>（<c>GameHour</c>）——
/// 与 <c>Job.Hours</c>、<c>VillageClock.Hour</c> 同一个单位
/// ⇒ 可以直接相加，不需要二次换算。</para>
/// </remarks>
public static class StageDuration
{
    /// <summary>
    /// 一个战斗回合占多少<b>游戏内小时</b>。
    /// </summary>
    /// <remarks>
    /// <b>【已确认】D-073：1 回合 = 0.25 游戏内小时（15 分钟）。</b>
    /// <para><b>这个数怎么来的</b>（不是拍脑袋）：一个副本<b>三阶段</b>
    /// （<c>DungeonFlow.DeclaredStageCount</c> = 3），而一场战斗的回合数
    /// 上限是 30（<c>CombatParameters.RoundLimit</c>）。
    /// ⇒ 按「<b>一场副本约跑一天</b>」对齐：3 阶段 × 30 回合 = 90 回合，
    /// 90 × 0.25 = <b>22.5 游戏内小时 ≈ 一天</b>
    /// ⇒ 副本任务现有的 11 小时行程 + 战斗 ≈ <b>一天半</b>，
    /// 落在「北境旧堡要跑一整天」（<c>Job.Hours</c> = 11）的量级里。
    /// </para>
    /// <para><b>为什么不是 1 回合 = 1 小时</b>：那样一个满回合副本要 90 小时
    /// （近 4 天）⇒ 英雄在副本里泡四天，而副本之外的村庄照常过日子 ——
    /// 这与「一次出征跑一天多」的既有量级不符。</para>
    /// </remarks>
    public const double GameHoursPerRound = 0.25;

    /// <summary>
    /// 一次战斗占多少游戏内小时。
    /// </summary>
    /// <param name="rounds">打过的回合数（≥ 1）。</param>
    /// <returns>游戏内小时。</returns>
    /// <exception cref="ArgumentOutOfRangeException">回合数小于 1。</exception>
    public static double GameHoursFor(int rounds)
    {
        if (rounds < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(rounds), rounds, "一场战斗至少打 1 个回合。");
        }

        return rounds * GameHoursPerRound;
    }

    /// <summary>
    /// 一个副本（若干阶段）占多少游戏内小时。
    /// </summary>
    /// <param name="roundsPerStage">各阶段分别打了几回合。</param>
    /// <returns>总游戏内小时。</returns>
    /// <exception cref="ArgumentNullException"><paramref name="roundsPerStage"/> 为 null。</exception>
    public static double GameHoursForStages(IReadOnlyList<int> roundsPerStage)
    {
        if (roundsPerStage is null) throw new ArgumentNullException(nameof(roundsPerStage));

        double total = 0.0;
        for (int i = 0; i < roundsPerStage.Count; i++)
        {
            total += GameHoursFor(roundsPerStage[i]);
        }

        return total;
    }
}
