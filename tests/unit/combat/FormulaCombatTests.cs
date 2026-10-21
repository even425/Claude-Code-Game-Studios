using System;
using System.Collections.Generic;
using System.Linq;
using GdUnit4;
using ClaudeCodeGameStudios.Application;
using ClaudeCodeGameStudios.Domain.Combat;

namespace ClaudeCodeGameStudios.Tests.Combat;

/// <summary>
/// <b>真战斗</b>的测试（【已确认】<c>07</c> <b>D-059…D-065</b>）。
///
/// <para><b>它替代了 <c>PlaceholderCombatTests</c></b>（那个占位实现连同它的
/// 「固定胜率 0.65」一起删掉了 —— 占位自己写着「T-004 拍板、真正的
/// <c>IBattleResolver</c> 落地之后，本常量连同整类一起删除」⇒ 现在条件<b>满足了</b>）。</para>
///
/// <para><b>本文档守五件事</b>：
/// <list type="number">
/// <item><b>两个接缝自洽</b> —— <c>Resolve</c> 写进日志的胜负，
///       <c>DidHeroesWin</c> 必须能原样读回来（<c>DungeonFlow</c> 依赖这一点：
///       若两者矛盾，产出的 <c>BattleResult</c> 会「日志说赢、结论说输」，
///       而 <c>BattleResult</c> 只校验可复核性、<b>不校验真假</b>）。</item>
/// <item><b>读不出就大声失败</b> —— 不回落成默认胜负（<c>07</c> §2.1 第 4 条纪律）。</item>
/// <item><b>属性换算按批准的表来</b>（D-060 / D-065）——
///       性格定攻/防、投入度定生命；<b>未登记的取值要大声失败</b>。</item>
/// <item><b>战斗是确定的</b>（D-062 不设命中判定 ⇒ 战斗里没有随机数）
///       ⇒ 同一份输入永远得到同一场战斗（基线 §8.5 D5）。</item>
/// <item><b>IT-31：队伍配置影响结果</b>（D3）——
///       同一副本、同一敌人，队伍配置不同 ⇒ 结果按规则不同；
///       且<b>同样的输入重跑 ⇒ 同样的结果</b>（基线 §14.3 六步法的第 6 步：幂等检查）。</item>
/// </list></para>
///
/// <para>⚠️ **本文档不发明任何数值**：所有数都来自 <c>CombatParameters</c>
/// （D-065 拍板、参数台账盯着）。</para>
/// </summary>
[TestSuite]
public class FormulaCombatTests
{
    /// <summary>造一支队伍（性格 × 投入度 ⇒ 战斗属性）。</summary>
    private static CombatPartySnapshot Party(
        params (string Name, string Personality, string Diligence)[] members)
        => new CombatPartySnapshot(members.Select(m => CombatantSnapshot.Create(
            m.Name, m.Name,
            stats: CombatParameters.ForHero(m.Personality, m.Diligence))).ToArray());

    /// <summary>全莽的队伍（攻高防低）。</summary>
    private static CombatPartySnapshot Reckless() => Party(
        ("甲", "莽", "随手"), ("乙", "莽", "随手"), ("丙", "莽", "随手"), ("丁", "莽", "随手"));

    /// <summary>全挂机的队伍（投入度 = 挂机 ⇒ 攻防取 5/4、命 60）。</summary>
    private static CombatPartySnapshot Idle() => Party(
        ("甲", "随手", "挂机"), ("乙", "随手", "挂机"), ("丙", "随手", "挂机"), ("丁", "随手", "挂机"));

    /// <summary>混编队伍（性格与投入度各取一档）。</summary>
    private static CombatPartySnapshot Mixed() => Party(
        ("甲", "莽", "肝"), ("乙", "傲", "随手"), ("丙", "随手", "挂机"), ("丁", "随手", "肝"));

    // ── 1 · 日志形状 ───────────────────────────────────────────────────────

    /// <summary>每场战斗都恰好产出一条决定性记录 —— 否则战斗不可复核（D5）。</summary>
    [TestCase]
    public void Resolve_ProducesExactlyOneDecidingEntry_SoTheBattleIsVerifiable()
    {
        var problems = new List<string>();
        var parties = new[] { Reckless(), Idle(), Mixed() };

        foreach (var party in parties)
        {
            for (int stage = 0; stage < 3; stage++)
            {
                var combat = new FormulaCombat();
                var log = combat.Resolve("遭遇一", party, stage);
                int deciding = log.Count(e => e.DecidedOutcome);
                if (deciding != 1)
                {
                    problems.Add($"阶段 {stage}：决定性记录 {deciding} 条，应为 1 条。");
                }
            }
        }

        Assertions.AssertThat(string.Join(" | ", problems)).IsEqual(string.Empty);
    }

    /// <summary>条目标识按遭遇名派生，因此不同遭遇之间不会撞 ID。</summary>
    [TestCase]
    public void Resolve_DerivesEntryIdsFromTheEncounterName_SoStagesDoNotCollide()
    {
        var combat = new FormulaCombat();
        var party = Mixed();

        var first = combat.Resolve("遭遇一", party, 0);
        var second = combat.Resolve("遭遇二", party, 1);

        var a = first.Select(e => e.EntryId).ToArray();
        var b = second.Select(e => e.EntryId).ToArray();
        Assertions.AssertThat(a.Intersect(b).Any()).IsFalse();
    }

    // ── 2 · 两个接缝自洽 ───────────────────────────────────────────────────

    /// <summary>写进日志的胜负，必须能被读回 —— 一次不落。</summary>
    /// <remarks>
    /// 这条是本测试类<b>最要紧的一条</b>。若哪天有人图省事，让
    /// <c>DidHeroesWin</c> 自己也重算一次公式，两者就会各自决定胜负 ——
    /// 而 <c>BattleResult</c> 不会发现，因为它只检查「引用了真实存在的条目」，
    /// 不检查「结论和日志是否一致」。
    /// </remarks>
    [TestCase]
    public void DidHeroesWin_ReadsBackExactlyWhatResolveWrote_EveryTime()
    {
        var problems = new List<string>();
        int wins = 0;
        int losses = 0;
        var parties = new[] { Reckless(), Idle(), Mixed() };

        foreach (var party in parties)
        {
            for (int stage = 0; stage < 3; stage++)
            {
                var combat = new FormulaCombat();
                var log = combat.Resolve("遭遇一", party, stage);

                bool reported = combat.DidHeroesWin(log);
                string deciding = log.First(e => e.DecidedOutcome).Summary;
                bool logSaysWin = deciding.Contains(BattleOutcomeMarkers.HeroesWin, StringComparison.Ordinal);

                if (reported != logSaysWin)
                {
                    problems.Add($"阶段 {stage}：日志说 {(logSaysWin ? "赢" : "输")}，读者报 {(reported ? "赢" : "输")}");
                }

                if (reported) wins++; else losses++;
            }
        }

        // 顺带确认两种走向都出现过 —— 全赢或全输说明读回逻辑写死了
        if (wins == 0 || losses == 0)
        {
            problems.Add($"只出现了 {(wins == 0 ? "败" : "胜")}：读回逻辑可能写死了一边。");
        }

        Assertions.AssertThat(string.Join(" | ", problems)).IsEqual(string.Empty);
    }

    /// <summary>赢标记读成赢、败标记读成败。</summary>
    [TestCase]
    public void DidHeroesWin_MapsEachMarkerToItsOwnAnswer()
    {
        var combat = new FormulaCombat();

        var winLog = new[] { new BattleLogEntry("e1", "遭遇一：【我方胜】", true) };
        var lossLog = new[] { new BattleLogEntry("e1", "遭遇一：【敌方胜】", true) };

        Assertions.AssertThat(combat.DidHeroesWin(winLog)).IsTrue();
        Assertions.AssertThat(combat.DidHeroesWin(lossLog)).IsFalse();
    }

    // ── 3 · 读不出就大声失败 ───────────────────────────────────────────────

    /// <summary>没有决定性记录 → 抛错，<b>不</b>默认算赢。</summary>
    [TestCase]
    public void DidHeroesWin_WithNoDecidingEntry_ThrowsRatherThanGuessing()
    {
        var combat = new FormulaCombat();
        var log = new[] { new BattleLogEntry("e0", "遭遇一：双方接战。", false) };

        bool threw = false;
        try { combat.DidHeroesWin(log); }
        catch (InvalidOperationException) { threw = true; }

        Assertions.AssertThat(threw).IsTrue();
    }

    /// <summary>两条决定性记录 → 抛错（说明解算器与读者不是成对的）。</summary>
    [TestCase]
    public void DidHeroesWin_WithTwoDecidingEntries_Throws()
    {
        var combat = new FormulaCombat();
        var log = new[]
        {
            new BattleLogEntry("e1", "遭遇一：【我方胜】", true),
            new BattleLogEntry("e2", "遭遇一：【敌方胜】", true),
        };

        bool threw = false;
        try { combat.DidHeroesWin(log); }
        catch (InvalidOperationException) { threw = true; }

        Assertions.AssertThat(threw).IsTrue();
    }

    /// <summary>日志里没有任何胜负标记（来自别的解算器）→ 抛错。</summary>
    [TestCase]
    public void DidHeroesWin_WithAForeignLog_ThrowsRatherThanGuessing()
    {
        var combat = new FormulaCombat();
        var log = new[] { new BattleLogEntry("e1", "阿黄击破守卫。", true) };

        bool threw = false;
        try { combat.DidHeroesWin(log); }
        catch (InvalidOperationException) { threw = true; }

        Assertions.AssertThat(threw).IsTrue();
    }

    /// <summary>两个标记同时出现（日志被改坏）→ 抛错。</summary>
    [TestCase]
    public void DidHeroesWin_WithBothMarkersInOneEntry_Throws()
    {
        var combat = new FormulaCombat();
        var log = new[] { new BattleLogEntry("e1", "遭遇一：【我方胜】【敌方胜】", true) };

        bool threw = false;
        try { combat.DidHeroesWin(log); }
        catch (InvalidOperationException) { threw = true; }

        Assertions.AssertThat(threw).IsTrue();
    }

    // ── 4 · 属性换算（D-060 / D-065）──────────────────────────────────────

    /// <summary>性格定攻 / 防，投入度定生命 —— 逐格核对批准的那张表。</summary>
    [TestCase]
    public void AttributeConversion_FollowsTheApprovedTable()
    {
        var cases = new (string Personality, string Diligence, int Atk, int Def, int Hp)[]
        {
            ("莽", "肝", 12, 3, 120),
            ("莽", "随手", 12, 3, 80),
            ("傲", "随手", 8, 8, 80),
            ("随手", "挂机", 5, 4, 60),   // 挂机 ⇒ 攻防取「挂机」那组（草案：全低）
            ("随手", "肝", 8, 6, 120),
        };

        var problems = new List<string>();
        foreach (var c in cases)
        {
            var s = CombatParameters.ForHero(c.Personality, c.Diligence);
            if (s.Attack != c.Atk || s.Defense != c.Def || s.Health != c.Hp)
            {
                problems.Add($"{c.Personality}/{c.Diligence} ⇒ {s.Attack}/{s.Defense}/{s.Health}，"
                             + $"应为 {c.Atk}/{c.Def}/{c.Hp}");
            }
        }

        Assertions.AssertThat(string.Join(" | ", problems)).IsEqual(string.Empty);
    }

    /// <summary>表里没有的取值 → <b>大声失败</b>（不静默落到「随手」）。</summary>
    [TestCase]
    public void AttributeConversion_WithAnUnregisteredValue_Throws()
    {
        bool threw = false;
        try { CombatParameters.ForHero("暴躁", "随手"); }
        catch (InvalidOperationException) { threw = true; }

        Assertions.AssertThat(threw).IsTrue();
    }

    /// <summary>怪物的三档：一一对应副本的三个阶段。</summary>
    [TestCase]
    public void MonsterTiers_MapToTheThreeStages()
    {
        Assertions.AssertThat(CombatParameters.MonsterNameFor(0)).IsEqual("普通");
        Assertions.AssertThat(CombatParameters.MonsterNameFor(1)).IsEqual("精英");
        Assertions.AssertThat(CombatParameters.MonsterNameFor(2)).IsEqual("首领");

        var normal = CombatParameters.MonsterFor(0);
        var boss = CombatParameters.MonsterFor(2);
        Assertions.AssertThat(boss.Health > normal.Health).IsTrue();
        Assertions.AssertThat(boss.Attack > normal.Attack).IsTrue();
    }

    // ── 5 · 战斗是确定的（D-062 ⇒ 没有随机数）──────────────────────────────

    /// <summary>同一份输入重跑 ⇒ **逐条相同**的日志（基线 §8.5 D5 / §14.3 第 6 步）。</summary>
    [TestCase]
    public void Battle_IsDeterministic_SameInputSameLog()
    {
        var problems = new List<string>();

        foreach (var party in new[] { Reckless(), Idle(), Mixed() })
        {
            for (int stage = 0; stage < 3; stage++)
            {
                var a = new FormulaCombat().Resolve("遭遇一", party, stage);
                var b = new FormulaCombat().Resolve("遭遇一", party, stage);

                if (a.Count != b.Count)
                {
                    problems.Add($"阶段 {stage}：两次的日志条数不同（{a.Count} / {b.Count}）。");
                    continue;
                }
                for (int i = 0; i < a.Count; i++)
                {
                    if (a[i].EntryId != b[i].EntryId || a[i].Summary != b[i].Summary
                        || a[i].DecidedOutcome != b[i].DecidedOutcome)
                    {
                        problems.Add($"阶段 {stage}：第 {i} 条两次不同。");
                        break;
                    }
                }
            }
        }

        Assertions.AssertThat(string.Join(" | ", problems)).IsEqual(string.Empty);
    }

    // ── 6 · IT-31：队伍配置按规则影响结果（D3）────────────────────────────

    /// <summary>
    /// <b>队伍配置影响副本结果</b>（基线 §8.5 D3 / §14.3 兽潮六步法）——
    /// 固定副本参数与敌人，只改队伍配置 ⇒ 结果按<b>规则预测</b>改变。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么断言「强弱不同 ⇒ 结果不同」而不是「结果必须变多少」</b>：
    /// 基线 §8.5 D3 原文要求「不要求所有指标单调变化」⇒ 断言写成
    /// 「<b>至少一项指标按规则改变，且与规则预测一致</b>」，
    /// **不预先规定改变多少</b>（那是平衡的事，不是规则的事）。</para>
    /// <para><b>规则预测是什么</b>：D-061 伤害 = 攻击 − 防御、D-062 必中、
    /// D-065 集火 ⇒ <b>队伍的总输出越大，打死对手越快、用的回合越少</b>。</para>
    /// </remarks>
    [TestCase]
    public void PartyComposition_ChangesTheOutcome_AsTheRulePredicts()
    {
        // Act —— 同一个副本、同一个敌人（**首领档**），只改队伍配置
        // ⚠️ 为什么用**首领档**：按 2 人小队重算怪物后（D-068），连「全挂机」的弱队
        // 也能在 25 回合内打过精英 ⇒ 差异要到**第三档**才显出来
        // （弱队每回合只造成 2 点伤害 ⇒ 30 回合打不死首领 ⇒ 超时判负；
        //   强队每回合 16 点 ⇒ 8 回合打死）。
        // ⇒ 而这正是设计要的：**弱队能打过前两场，但过不去首领** ⇒「选谁进队」有后果（D3 / IT-31）。
        const int stage = 2;
        var strong = new FormulaCombat();
        var weak = new FormulaCombat();
        var strongLog = strong.Resolve("遭遇二", Reckless(), stage);
        var weakLog = weak.Resolve("遭遇二", Idle(), stage);

        bool strongWon = strong.DidHeroesWin(strongLog);
        bool weakWon = weak.DidHeroesWin(weakLog);

        // Assert —— 结果**按规则**不同
        Assertions.AssertThat(strongWon).IsTrue();
        Assertions.AssertThat(weakWon).IsFalse();

        // 而且方向与规则预测一致：**输出不够的队伍打得更久**
        // （弱队每回合只造成 2 点伤害 ⇒ 30 回合打不死 ⇒ 超时判负；
        //   强队每回合 36 点 ⇒ 6 回合打死 ⇒ 出手次数自然更少）
        int strongAttacks = CountAttacks(strongLog);
        int weakAttacks = CountAttacks(weakLog);
        Assertions.AssertThat(weakAttacks > strongAttacks).IsTrue();
    }

    /// <summary>幂等检查（基线 §14.3 六步法的第 6 步）：同样的输入重跑 ⇒ 同样的结果。</summary>
    [TestCase]
    public void SameInputRerun_ProducesTheSameVerdict()
    {
        var problems = new List<string>();

        foreach (var party in new[] { Reckless(), Idle(), Mixed() })
        {
            for (int stage = 0; stage < 3; stage++)
            {
                var combat = new FormulaCombat();
                bool first = combat.DidHeroesWin(combat.Resolve("遭遇一", party, stage));
                var again = new FormulaCombat();
                bool second = again.DidHeroesWin(again.Resolve("遭遇一", party, stage));
                if (first != second)
                {
                    problems.Add($"阶段 {stage}：两次跑的结论不同（{first} / {second}）。");
                }
            }
        }

        Assertions.AssertThat(string.Join(" | ", problems)).IsEqual(string.Empty);
    }

    /// <summary>数一数日志里有多少次出手（每条攻击记录算一次）。</summary>
    private static int CountAttacks(IReadOnlyList<BattleLogEntry> log)
        => log.Count(e => e.Summary.Contains("攻击", StringComparison.Ordinal));

    // ── 7 · 可玩性：游戏实际派的是「2 人小队」 ────────────────────────────

    /// <summary>
    /// <b>游戏实际派出去的队伍是 2 人</b>（采集器 <c>DispatchOneDungeonForCollection</c>：
    /// 「取前两个可用的人」；自检的 <c>--dungeon</c> 同样两人）
    /// ⇒ 因此<b>批准的那张数值表必须让 2 人小队打得过</b>。
    ///
    /// <para><b>为什么这条必须写成断言</b>：D-065 的数值是按「混编 <b>4</b> 人」算的
    /// （草案 §3 的算式就是这么写的），而游戏跑的是 2 人小队
    /// ⇒ 两边的前提不一致 ⇒ 数值表在游戏里<b>不成立</b>。
    /// ⇒ 这类「纸面上对、跑起来不成立」的偏差，<b>只有用游戏真实的队伍规模去跑</b>才看得见。</para>
    /// </summary>
    [TestCase]
    public void TheActualTwoHeroParty_CanWinEveryStage()
    {
        var problems = new List<string>();

        for (int stage = 0; stage < 3; stage++)
        {
            var combat = new FormulaCombat();
            // 游戏里最常见的一支两人队：一个莽（能肝）+ 一个傲（随手）
            var party = Party(("甲", "莽", "肝"), ("乙", "傲", "随手"));
            var log = combat.Resolve("遭遇", party, stage);
            bool won = combat.DidHeroesWin(log);

            if (!won)
            {
                problems.Add($"第 {stage} 档（{CombatParameters.MonsterNameFor(stage)}）："
                             + "两人小队打不赢 ⇒ 副本必然失败 ⇒ 玩家拿不到任何战利品");
            }
        }

        Assertions.AssertThat(string.Join(" | ", problems)).IsEqual(string.Empty);
    }
}
