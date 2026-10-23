using System;
using System.Collections.Generic;
using ClaudeCodeGameStudios.Domain.Combat;
using ClaudeCodeGameStudios.Domain.Heroes;

namespace ClaudeCodeGameStudios.Application;

/// <summary>
/// 试炼塔 —— <b>进去打一场，赢了永久加生命</b>（D-074）。
/// </summary>
/// <remarks>
/// <para><b>它做什么</b>：让一名英雄与试炼塔的守卫打一场；
/// 赢了 ⇒ 那名英雄获得<b>永久</b>生命加成；输了 ⇒ 什么也不发生（无惩罚）。</para>
///
/// <para><b>为什么只加生命</b>（D-074）：英雄<b>没有经验 / 等级</b>
/// （`Village` 刻意不含 tier）⇒「升级」只能是属性变强；
/// 而加攻防会直接改战斗胜率（首领档 86.7% 得重测）⇒ 第一版只动生命。</para>
///
/// <para><b>为什么是「永久」</b>（D-074）：限时效果需要状态系统
/// （基线 §4.4 的状态清单仍【待定】）⇒ 第一版只做永久这一半。</para>
///
/// <para><b>战斗完全确定</b>（D-062 不设命中判定 ⇒ 没有随机数）
/// ⇒ 同一名英雄进塔，结果永远一样 ⇒ 「能不能过」是<b>可测的</b>，
/// 而不是「运气好不好」。</para>
/// </remarks>
public sealed class TrialTowerService
{
    /// <summary>
    /// 一次试炼的结果。
    /// </summary>
    /// <param name="HeroId">参试的英雄。</param>
    /// <param name="Won">是否打赢。</param>
    /// <param name="HealthBonusGained">这次获得的生命加成（输了为 0）。</param>
    /// <param name="StatsAfter">打赢后他<b>实际变成</b>的属性（输了 = 原属性）。</param>
    /// <param name="Log">战斗日志。</param>
    /// <param name="SkipReason">
    /// <b>为什么这次没打成</b>（D-113：每天限一次）。
    /// <c>None</c> = 正常试炼；<c>AlreadyTriedToday</c> = 今天已经打过 ⇒ 不打。
    /// </param>
    /// <remarks>
    /// ⚠️ <b>为什么带这个字段</b>：「今天已经试过了」<b>不是失败</b>（他没输，是没上场）
    /// ⇒ 不能拿 <c>Won=false</c> 冒充 ⇒ 否则界面会说「他输了」，而真相是「规则拦下了」。
    /// </remarks>
    public readonly record struct TrialOutcome(
        HeroId HeroId, bool Won, int HealthBonusGained,
        CombatStats StatsAfter,
        IReadOnlyList<BattleLogEntry> Log,
        TrialSkipReason SkipReason = TrialSkipReason.None);

    /// <summary>试炼被跳过的原因（D-113）。</summary>
    public enum TrialSkipReason
    {
        /// <summary>正常试炼（没有跳过）。</summary>
        None = 0,

        /// <summary>今天已经试过了 ⇒ <b>每天限一次</b>（D-113）。</summary>
        AlreadyTriedToday = 1,
    }

    /// <summary>
    /// 赢一场获得的<b>生命加成</b>（D-074）。
    /// </summary>
    /// <remarks>
    /// ⚠️ **这个数是方案值，不是拍板值**：D-074 拍板的是「只加生命」与「永久」，
    /// <b>加多少</b>由本实现给出，取一个能与现有属性表共存的量
    /// （挂机 60 命 → +20 生命 = 提升三分之一；肝 120 命 → +20 = 六分之一）。
    /// ⇒ **改它只需改这一个常量**（并在参数台账登记）。
    /// </remarks>
    public const int HealthBonusPerWin = 20;

    private readonly IBattleResolver _resolver;
    private readonly IBattleOutcomeReader _outcomeReader;

    /// <param name="resolver">战斗解算器。</param>
    /// <param name="outcomeReader">胜负读者（与解算器成对）。</param>
    /// <exception cref="ArgumentNullException">任一参数为 null。</exception>
    public TrialTowerService(IBattleResolver resolver, IBattleOutcomeReader outcomeReader)
    {
        _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
        _outcomeReader = outcomeReader ?? throw new ArgumentNullException(nameof(outcomeReader));
    }

    /// <summary>
    /// 试炼塔的守卫 —— 用<b>精英档</b>怪物（<c>CombatParameters</c> 的第二档）。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么是精英档而不是普通档</b>（实测定的，不是估的）：
    /// 逐档实测九个英雄原型打各档守卫的结果 ——
    /// <list type="bullet">
    /// <item><b>普通档：九种<b>全部能赢</b></b>（连挂机都是 17 回合胜）
    ///       ⇒ 拿它当门槛等于<b>没有门槛</b>（谁都能过 ⇒ 加成白送）。</item>
    /// <item><b>精英档：只有「莽 / 肝」能赢</b>（22 回合），其余八种全负
    ///       ⇒ 门槛<b>真的有区分度</b>：最强的组合能过，其余人过不了。</item>
    /// <item>首领档：只有「莽 / 肝」能赢（20 回合）—— 但那是副本最终目标，
    ///       拿它当「升级入口」的门槛太狠了。</item>
    /// </list>
    /// ⇒ 取精英档：<b>门槛明确，且不是最终目标</b>。</para>
    /// <para>⚠️ 这个选择意味着「试炼塔是给最强的人用的」——
    /// 这与基线 §12.1「试炼塔 = 让玩家快速升级」一致（升级来源本就该有门槛）。</para>
    /// </remarks>
    private const int GuardTierIndex = 1;

    /// <summary>
    /// 让一名英雄进塔打一场。
    /// </summary>
    /// <param name="heroId">参试英雄。</param>
    /// <param name="heroName">显示名（写进战斗日志）。</param>
    /// <param name="stats">他现在的战斗属性（含已有的试炼塔加成）。</param>
    /// <param name="todayDay">今天是第几天（0 = 不知道 ⇒ 不限次数）。</param>
    /// <param name="lastTrialDay">他上次试炼是第几天（0 = 从未试过）。</param>
    /// <returns>试炼结果。</returns>
    /// <exception cref="ArgumentException">英雄标识或名字为空。</exception>
    /// <exception cref="InvalidOperationException">战斗属性不明（全 0）⇒ 不能开打。</exception>
    public TrialOutcome Try(
        HeroId heroId,
        string heroName,
        CombatStats stats,
        int todayDay = 0,
        int lastTrialDay = 0)
    {
        if (string.IsNullOrEmpty(heroId.Value))
        {
            throw new ArgumentException("英雄标识不能为空。", nameof(heroId));
        }

        if (string.IsNullOrEmpty(heroName))
        {
            throw new ArgumentException("英雄名字不能为空。", nameof(heroName));
        }

        // ⚠️ **D-113：每天限一次** ——
        // 以前**没有任何限制** ⇒ 一个「莽 / 肝」的英雄可以**无限刷**试炼塔
        // ⇒ 永久加生命**无上限** ⇒ 副本难度形同虚设。
        // ⇒ 现在：同一天第二次 ⇒ **不打**（不是「输了」，是「规则拦下了」）。
        //
        // ⚠️ **为什么在这里判、而不是让调用方自己拦**：
        // 调用方有好几处（界面 / 采集器 / 未来的脚本）⇒ 规则只有一处实现，才守得住。
        if (todayDay > 0 && lastTrialDay == todayDay)
        {
            return new TrialOutcome(
                heroId, Won: false, HealthBonusGained: 0,
                StatsAfter: stats,
                Log: Array.Empty<BattleLogEntry>(),
                SkipReason: TrialSkipReason.AlreadyTriedToday);
        }

        if (!stats.IsKnown)
        {
            throw new InvalidOperationException(
                $"英雄「{heroName}」的战斗属性不明（攻/防/命 全 0）—— 不能进塔。");
        }

        var party = new CombatPartySnapshot(new[]
        {
            CombatantSnapshot.Create(heroId.Value, heroName, stats: stats),
        });

        var log = _resolver.Resolve("试炼塔", party, GuardTierIndex);
        bool won = _outcomeReader.DidHeroesWin(log);

        // ⚠️ **「变成什么」在这里算**（D-074：只加生命）——
        // 攻防**原封不动**，只有生命加上。
        // ⇒ 调用方只负责把 StatsAfter 应用到英雄身上，
        // 而「只加生命」这条规则**只有这一处实现** ⇒ 守得住。
        var after = won
            ? stats with { Health = stats.Health + HealthBonusPerWin }
            : stats;

        return new TrialOutcome(
            heroId, won, won ? HealthBonusPerWin : 0, after, log);
    }
}
