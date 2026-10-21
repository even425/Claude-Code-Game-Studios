using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace ClaudeCodeGameStudios.Domain.Combat;

/// <summary>
/// 战斗日志的<b>读回器</b> —— 把引擎写进日志的东西读成结构化数据。
/// </summary>
/// <remarks>
/// <para><b>为什么要有它</b>：战斗引擎算得出「打了几回合」，
/// 但 <see cref="IBattleResolver"/> 只返回日志 ⇒ 回合数<b>被丢掉了</b>。
/// 而 <c>04</c> §5.4 的插拔点 7「阶段耗时」的原意正是<b>报出回合数</b>，
/// 由调用方换算成模拟时间（D-073）⇒ 所以需要一条「读回来」的通道。</para>
/// <para><b>为什么读不出就抛</b>：<c>07</c> §2.1 第 4 条纪律（不静默落到默认值）。
/// 若读不出回合数却悄悄当成 0 ⇒ 副本耗时算成 0 小时 ⇒ <b>时间底座整个塌掉</b>，
/// 而外面看不到任何异常。</para>
/// </remarks>
public static class BattleLogReader
{
    /// <summary>
    /// 从决定性条目里读出这一场打了几个回合。
    /// </summary>
    /// <param name="log">战斗日志（须恰有一条决定性条目，且带回合标记）。</param>
    /// <returns>打过的回合数（≥ 1）。</returns>
    /// <exception cref="InvalidOperationException">
    /// 日志里没有恰好一条决定性条目，或那条条目没有回合标记。
    /// </exception>
    public static int RoundsUsed(IReadOnlyList<BattleLogEntry> log)
    {
        var deciding = (log ?? Array.Empty<BattleLogEntry>())
            .Where(e => e.DecidedOutcome)
            .ToArray();

        if (deciding.Length != 1)
        {
            throw new InvalidOperationException(
                $"读战斗回合数期望日志里恰好一条决定性记录，实际有 {deciding.Length} 条。");
        }

        var match = RoundsPattern.Match(deciding[0].Summary ?? string.Empty);
        if (!match.Success)
        {
            throw new InvalidOperationException(
                $"决定性条目里没有回合标记（{deciding[0].Summary}）—— "
                + "读不出回合数就必须失败，不能当成 0 回合（否则副本耗时会算成 0）。");
        }

        int rounds = int.Parse(match.Groups[1].Value);
        if (rounds < 1)
        {
            throw new InvalidOperationException($"回合数必须 ≥ 1，实际 {rounds}。");
        }

        return rounds;
    }

    /// <summary>回合标记的解析式（锚定：整串 <c>【回合N】</c>）。</summary>
    private static readonly Regex RoundsPattern = new(
        @"【回合(?<n>\d+)】", RegexOptions.Compiled);
}
