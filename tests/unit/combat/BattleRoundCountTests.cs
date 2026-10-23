using System;
using System.Collections.Generic;
using System.Linq;
using GdUnit4;
using ClaudeCodeGameStudios.Domain.Combat;

namespace ClaudeCodeGameStudios.Tests.Combat;

/// <summary>
/// <b>战斗回合数的记法</b>（D-100）—— 「打了几回合」到底该记几。
/// </summary>
/// <remarks>
/// <para><b>起因（一个真 bug）</b>：写 D-096 的盗匪自检时，用了「一拳秒杀」的属性
/// ⇒ 战斗在<b>第 1 个回合里</b>就分出胜负 ⇒ 引擎把「<b>【回合0】</b>」写进决定性条目
/// ⇒ <see cref="BattleLogReader.RoundsUsed"/> 按契约<b>大声失败</b>（回合数必须 ≥ 1）。</para>
///
/// <para>⚠️ <b>当时我是「改数值绕过去」的</b>（把怪物血量从 20 改成 200，让战斗多打几回合）
/// ⇒ 测试绿了，<b>但根因还在</b>。而且仓库里**早就有这条坑的注释**
/// （<c>WorldEventConsequenceTests</c>：「不要用『一拳秒杀』的属性」），
/// ⇒ 说明<b>它一直在被人绕开，而没人修</b>。</para>
///
/// <para>⚠️ <b>为什么它是真的 bug（不是「0 回合战斗不存在」那么简单）</b>：
/// 引擎写的是「<b>已经走完</b>的回合数」，而这一战<b>明明走了 1 个回合</b>
/// （只是那一回合里就分出了胜负）⇒ <b>少记了 1 个回合</b>。
/// ⇒ 后果不只是「第 1 回合秒杀会抛」：
/// <b>任何在第 N 回合内结束的战斗，回合数都被少记了 1</b>
/// ⇒ 阶段耗时（0.25 小时/回合）算少了 ⇒ 关系变化的「时间坐标」整个偏了一点。</para>
///
/// <para>⇒ <b>修法</b>：决定性条目里写的必须是「<b>这场战斗走过的回合数</b>」
/// ⇒ 分胜负时算 <c>roundsCompleted + 1</c>（当前这一回合也在内）。</para>
/// </remarks>
[TestSuite]
public class BattleRoundCountTests
{
    /// <summary>造一个「一拳秒杀」的战斗输入（英雄攻击远高于怪物血量）。</summary>
    private static BattleInput OneShotInput(int heroAttack = 60, int monsterHealth = 20)
    {
        var hero = new CombatantState("h-1", "甲", new CombatStats(heroAttack, 10, 500));
        var monster = new CombatantState("m-1", "野怪", new CombatStats(1, 1, monsterHealth));
        return new BattleInput("遭遇一", new[] { hero }, new[] { monster });
    }

    /// <summary>
    /// <b>第 1 回合就分胜负 ⇒ 回合数必须是 1</b>（不是 0）。
    /// </summary>
    /// <remarks>
    /// ⚠️ <b>这一条在修之前是红的</b>：引擎写的是「已走完的回合数」= 0
    /// ⇒ 读回器抛「回合数必须 ≥ 1」⇒ <b>整场战斗崩掉</b>。
    /// </remarks>
    [TestCase]
    public void AFirstRoundKill_ReportsOneRound_NotZero()
    {
        var engine = new BattleEngine();

        var run = engine.Run(OneShotInput());

        Assertions.AssertThat(run.Outcome).IsEqual(BattleOutcome.HeroesWin);
        Assertions.AssertThat(BattleLogReader.RoundsUsed(run.Log)).IsEqual(1);
    }

    /// <summary>
    /// <b>这条战斗必须能跑完</b>（不抛异常）——「一拳秒杀」是<b>合法</b>的战斗。
    /// </summary>
    /// <remarks>
    /// ⚠️ 修之前的症状是：<c>BattleLogReader.RoundsUsed</c> 抛
    /// 「回合数必须 ≥ 1，实际 0」⇒ <b>副本流程与兽潮结算都会崩</b>。
    /// </remarks>
    [TestCase]
    public void AFirstRoundKill_DoesNotBreakTheBattle()
    {
        var engine = new BattleEngine();

        var run = engine.Run(OneShotInput());

        bool threw = false;
        try { BattleLogReader.RoundsUsed(run.Log); }
        catch (InvalidOperationException) { threw = true; }

        Assertions.AssertThat(threw).IsFalse();
    }

    /// <summary>
    /// <b>引擎报出的回合数与日志里读出来的一致</b>（两个出口不许分叉）。
    /// </summary>
    /// <remarks>
    /// ⚠️ 这是「同一个事实的两个出口」⇒ 分叉了就有一处是错的
    /// （D-073 的回合数既是 <c>BattleRun.RoundsUsed</c> 又是日志里的标记）。
    /// </remarks>
    [TestCase]
    public void TheReportedRounds_MatchTheLogMarker()
    {
        var engine = new BattleEngine();

        var run = engine.Run(OneShotInput());

        Assertions.AssertThat(run.RoundsUsed).IsEqual(BattleLogReader.RoundsUsed(run.Log));
    }

    /// <summary>
    /// <b>多回合的战斗：记的是「走过的回合数」</b>（不是「走完的回合数」）。
    /// </summary>
    /// <remarks>
    /// ⚠️ 修之前的症状：战斗在第 5 回合内结束 ⇒ 引擎记 4 ⇒ <b>少记 1</b>
    /// ⇒ 阶段耗时少算 0.25 小时 ⇒ 关系变化的时间坐标偏了。
    /// </remarks>
    [TestCase]
    public void AMultiRoundFight_ReportsTheRoundsItActuallyFought()
    {
        var engine = new BattleEngine();

        // 怪物血量够高 ⇒ 战斗要打好几回合才结束。
        var run = engine.Run(OneShotInput(heroAttack: 5, monsterHealth: 300));

        int rounds = BattleLogReader.RoundsUsed(run.Log);

        Assertions.AssertThat(rounds >= 1).IsTrue();
        // ⚠️ **与引擎自己的计数一致**（不许少记）。
        Assertions.AssertThat(rounds).IsEqual(run.RoundsUsed);
    }

    /// <summary>
    /// <b>超时判负</b>那条路不受影响（回合数照旧是上限 30）。
    /// </summary>
    /// <remarks>
    /// ⚠️ **为什么单独验**：修法改的是「分胜负那一刻记几」，
    /// ⇒ 必须确认<b>超时那条路没被带偏</b>（它是「回合走完 ⇒ 判负」⇒ 记 N）。
    /// </remarks>
    [TestCase]
    public void ATimeoutFight_StillReportsTheRoundLimit()
    {
        var engine = new BattleEngine();

        // 英雄打不动（攻击 1、怪物防 999）⇒ 一直打到回合上限。
        var hero = new CombatantState("h-1", "甲", new CombatStats(1, 1, 5000));
        var monster = new CombatantState("m-1", "野怪", new CombatStats(1, 999, 999999));
        var run = engine.Run(new BattleInput("遭遇一", new[] { hero }, new[] { monster }));

        Assertions.AssertThat(run.Outcome).IsEqual(BattleOutcome.EnemiesWin);
        Assertions.AssertThat(BattleLogReader.RoundsUsed(run.Log)).IsEqual(CombatParameters.RoundLimit);
    }
}
