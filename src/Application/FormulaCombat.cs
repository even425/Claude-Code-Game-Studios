using System;
using System.Collections.Generic;
using System.Linq;
using ClaudeCodeGameStudios.Domain.Combat;

namespace ClaudeCodeGameStudios.Application;

/// <summary>
/// 真正的战斗解算器 —— 按 <c>04</c> §5.4 的<b>七个插拔点</b>跑一场遭遇
/// （【已确认】<c>07</c> <b>D-059…D-065</b>）。
/// </summary>
/// <remarks>
/// <para><b>它替代了 <c>PlaceholderCombat</c></b>（固定胜率 0.65 的占位）——
/// 那个占位自己写着「删除条件：T-004 拍板、真正的 <c>IBattleResolver</c> 落地之后，
/// 本常量连同整类一起删除」⇒ 现在条件<b>满足了</b>。</para>
///
/// <para><b>为什么战斗里没有随机数</b>：D-062 决定<b>不设命中判定</b> ⇒ 战斗完全确定
/// ⇒ <b>同一份输入永远得到同一场战斗</b>（基线 §14.2 用固定种子；这里更彻底：
/// 根本不涉及随机）⇒ 这正是 IT-31 那种对照实验能写出来的前提。</para>
///
/// <para><b>两个接缝必须自洽</b>：<see cref="Resolve"/> 把胜负写进日志的决定性条目，
/// <see cref="DidHeroesWin"/> 再从<b>同一条</b>里读回来 ⇒ 两者不可能互相矛盾
/// （否则会产生一条「日志说赢、结论说输」的战斗结果，而 <c>BattleResult</c> 不会察觉）。</para>
/// </remarks>
public sealed class FormulaCombat : IBattleResolver, IBattleOutcomeReader
{
    private readonly BattleEngine _engine;

    /// <summary>构造一个解算器。</summary>
    /// <param name="rules">插拔点的组合；<c>null</c> 用 D-065 的默认那一套。</param>
    public FormulaCombat(BattleRules? rules = null)
    {
        _engine = new BattleEngine(rules);
    }

    /// <summary>
    /// 解算一次遭遇，产出一条条过程记录 + <b>一条</b>决定性记录。
    /// </summary>
    /// <param name="encounterName">遭遇名称。</param>
    /// <param name="party">参战队伍的状态快照（<b>含战斗属性</b>）。</param>
    /// <param name="stageIndex">阶段序号（0 起）⇒ 决定敌方档位（普通 / 精英 / 首领）。</param>
    /// <returns>战斗日志；恰有一条 <c>DecidedOutcome == true</c>。</returns>
    /// <exception cref="InvalidOperationException">
    /// 队伍里有人的战斗属性<b>不明</b>（全 0）⇒ 战斗不能拿它开打 ⇒ **大声失败**。
    /// </exception>
    public IReadOnlyList<BattleLogEntry> Resolve(
        string encounterName, CombatPartySnapshot party, int stageIndex)
    {
        var heroes = party.Combatants
            .Select(c => new CombatantState(c.Id, c.Name, c.Stats))
            .ToList();

        var monster = new CombatantState(
            $"monster:{stageIndex}",
            CombatParameters.MonsterNameFor(stageIndex),
            CombatParameters.MonsterFor(stageIndex));

        var run = _engine.Run(new BattleInput(encounterName, heroes, new[] { monster }));
        return run.Log;
    }

    /// <summary>
    /// 从战斗日志里<b>读回</b>胜负。
    /// </summary>
    /// <remarks>
    /// <b>不重算、不掷随机数</b> —— 判定只由 <see cref="Resolve"/> 写一次，
    /// 这里只负责把它读出来。两者因此不可能互相矛盾。
    /// </remarks>
    /// <param name="log">本次刚产出的日志。</param>
    /// <returns>我方获胜为 <c>true</c>。</returns>
    /// <exception cref="InvalidOperationException">
    /// 日志里没有<b>恰好一条</b>决定性记录，或那条记录里读不出胜负标记 ——
    /// 说明注入的解算器不是本类，或日志在中途被改过。
    /// </exception>
    public bool DidHeroesWin(IReadOnlyList<BattleLogEntry> log)
    {
        var deciding = (log ?? Array.Empty<BattleLogEntry>())
                       .Where(entry => entry.DecidedOutcome)
                       .ToArray();

        if (deciding.Length != 1)
        {
            throw new InvalidOperationException(
                $"战斗解算器期望日志里恰好一条决定性记录，实际有 {deciding.Length} 条。"
                + "解算器与结论读者必须成对使用 —— 若分别注入，两者可能互相矛盾。");
        }

        string summary = deciding[0].Summary ?? string.Empty;
        bool sawWin = summary.Contains(BattleOutcomeMarkers.HeroesWin, StringComparison.Ordinal);
        bool sawLoss = summary.Contains(BattleOutcomeMarkers.EnemiesWin, StringComparison.Ordinal);

        if (sawWin == sawLoss)
        {
            throw new InvalidOperationException(
                $"无法从决定性条目读出胜负：\"{summary}\"。"
                + "本解算器写不出这种日志 —— 请检查注入的实现是否成对。");
        }

        return sawWin;
    }
}
