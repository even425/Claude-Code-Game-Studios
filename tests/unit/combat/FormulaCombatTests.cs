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
    /// ⇒ 因此<b>批准的那张数值表必须让 2 人小队打得过第一关</b>。
    ///
    /// <para><b>为什么这条必须写成断言</b>：D-065 的数值是按「混编 <b>4</b> 人」算的
    /// （草案 §3 的算式就是这么写的），而游戏跑的是 2 人小队
    /// ⇒ 两边的前提不一致 ⇒ 数值表在游戏里<b>不成立</b>。
    /// ⇒ 这类「纸面上对、跑起来不成立」的偏差，<b>只有用游戏真实的队伍规模去跑</b>才看得见。</para>
    ///
    /// <para><b>为什么只要求「第一关」</b>（D-070 调难之后）：精英/首领的血量是按
    /// 「<b>普通队能赢、弱队会输</b>」定的 ⇒ <b>两人小队打首领本来就可能超时</b>
    /// （那正是「选谁进队」有后果的意思）⇒ 断言若要求「两人必赢三关」，
    /// 就会把「难度调对了」判成错。</para>
    /// </summary>
    [TestCase]
    public void TheActualTwoHeroParty_CanWinTheFirstStage()
    {
        // 游戏里最常见的一支两人队：一个莽（能肝）+ 一个傲（随手）
        var party = Party(("甲", "莽", "肝"), ("乙", "傲", "随手"));
        var combat = new FormulaCombat();
        var log = combat.Resolve("遭遇一", party, 0);

        Assertions.AssertThat(combat.DidHeroesWin(log)).IsTrue();
    }

    /// <summary>
    /// <b>穷举所有两人队的通关率</b> —— 因为采集器的队伍轮换是**确定性的**
    /// （按名册顺序两两配对）⇒ 它测到的胜率是**配对方式的产物**，不是难度的产物
    /// （实测两个种子：5.4% vs 67.6%，差 12 倍）。
    /// ⇒ 要量难度，只能**穷举**：9 种英雄原型（3 性格 × 3 投入度）⇒ 45 种无序组合。
    /// </summary>
    /// <remarks>
    /// <para><b>断言的是设计意图，不是编出来的平衡数</b>（D-070）：
    /// <b>强队（莽 + 莽）必须打得过首领</b>，<b>弱队（挂机 + 挂机）必须打不过首领</b>
    /// ⇒ 「选谁进队」必须**有后果**。</para>
    /// <para>顺带把各档的胜率分布**打出来**（供标定与调难度看）。</para>
    /// </remarks>
    [TestCase]
    public void EveryTwoHeroCrew_ShowsTheSquadChoiceMatters()
    {
        string[] personalities = { "莽", "傲", "随手" };
        string[] diligences = { "肝", "随手", "挂机" };
        var archetypes = (from p in personalities
                          from d in diligences
                          select (p, d)).ToArray();

        int crews = 0;
        var winsPerStage = new int[3];
        var lossesPerStage = new int[3];

        for (int i = 0; i < archetypes.Length; i++)
        {
            for (int j = i; j < archetypes.Length; j++)
            {
                var (p1, d1) = archetypes[i];
                var (p2, d2) = archetypes[j];
                var party = Party(("甲", p1, d1), ("乙", p2, d2));
                crews++;

                for (int stage = 0; stage < 3; stage++)
                {
                    var combat = new FormulaCombat();
                    var log = combat.Resolve("遭遇", party, stage);
                    if (combat.DidHeroesWin(log)) winsPerStage[stage]++; else lossesPerStage[stage]++;
                }
            }
        }

        // 设计意图（D-070）：强队过首领，弱队过不了首领。
        bool strongWins = DidWin(Party(("甲", "莽", "肝"), ("乙", "莽", "肝")), 2);
        bool weakWins = DidWin(Party(("甲", "随手", "挂机"), ("乙", "随手", "挂机")), 2);

        // 分布（打出来给人看：这是调难度的依据）
        var report = new System.Text.StringBuilder();
        for (int stage = 0; stage < 3; stage++)
        {
            double rate = 100.0 * winsPerStage[stage] / crews;
            report.Append($"{CombatParameters.MonsterNameFor(stage)}: "
                          + $"{winsPerStage[stage]}/{crews} = {rate:F0}% 通关；");
        }
        Console.WriteLine($"[战斗分布] 两人队穷举 {crews} 种：{report}");
        // 测试宿主会吞掉 Console 输出 ⇒ 同时落文件，供人工读取（调试用，不参与判定）。
        try
        {
            var dump = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(), "ccgs-combat-dist.txt");
            System.IO.File.WriteAllText(
                dump,
                $"两人队穷举 {crews} 种（9 原型两两组合）\n{report}\n",
                new System.Text.UTF8Encoding(false));
        }
        catch { /* 落盘失败不影响断言 */ }

        Assertions.AssertThat(strongWins).IsTrue();
        Assertions.AssertThat(weakWins).IsFalse();
    }

    /// <summary>
    /// <b>难度目标必须可验</b>（D-072）：首领档的两人队胜率落在 <b>70–90%</b>。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么这条必须存在</b>：D-070 把「胜率预计落到 70–90%」写进决策，
    /// 但<b>没有人验过它落在哪</b> —— 实测 180 血时只有 37.8% ⇒
    /// 目标写了两轮、从未被机器检查过 ⇒ 数值可以悄悄跑偏。
    /// 现在它是<b>一条断言</b>：改了首领血量而胜率掉出区间 ⇒ 立刻红。</para>
    /// <para><b>为什么是「穷举 45 种两人队」而不是采集器的胜率</b>：
    /// 采集器的队伍轮换是<b>确定性</b>的（按名册顺序两两配对）⇒ 它测到的胜率
    /// 是<b>配对方式的产物</b>（两个种子 5.4% vs 67.6%，差 12 倍），
    /// 不是难度的度量。穷举才有可比性。</para>
    /// <para><b>区间下界 70% 的含义</b>：首领必须<b>有风险</b>（不能人人必过）；
    /// <b>上界 90%</b>：也不能难到「选谁进队都没区别」。</para>
    /// </remarks>
    [TestCase]
    public void BossStage_WinRateAcrossAllTwoHeroCrews_LandsInTheTargetBand()
    {
        string[] personalities = { "莽", "傲", "随手" };
        string[] diligences = { "肝", "随手", "挂机" };
        var archetypes = (from p in personalities
                          from d in diligences
                          select (p, d)).ToArray();

        int crews = 0;
        int wins = 0;
        var lostCrews = new List<string>();

        for (int i = 0; i < archetypes.Length; i++)
        {
            for (int j = i; j < archetypes.Length; j++)
            {
                var (p1, d1) = archetypes[i];
                var (p2, d2) = archetypes[j];
                crews++;

                var party = Party(("甲", p1, d1), ("乙", p2, d2));
                if (DidWin(party, 2)) wins++;
                else lostCrews.Add($"{p1}/{d1}+{p2}/{d2}");
            }
        }

        double rate = 100.0 * wins / crews;
        string detail = $"首领胜率 {wins}/{crews} = {rate:F1}%（目标 70–90%）；"
                        + $"输的队伍：{string.Join("、", lostCrews)}";

        // 诊断信息落文件（测试宿主会吞掉 Console；断言值必须是纯布尔，
        // 混合类型的断言会让 GdUnit4 的报告路径崩掉）。
        try
        {
            var dump = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(), "ccgs-boss-band.txt");
            System.IO.File.WriteAllText(dump, detail, new System.Text.UTF8Encoding(false));
        }
        catch { /* 落盘失败不影响断言 */ }

        // ⚠️ **为什么断言「输掉的是哪几支队伍」，而不是只断言「胜率在 70–90% 之间」**
        // （变异看红时抓到的）：
        //
        // 只断言区间的话，把区间**放宽成 0–100** 之后守卫照样绿 ⇒ 那条断言成了装饰
        // ⇒ 数值可以悄悄漂到「人人必过」也不报。（加一条更宽的元断言也堵不住：
        // 区间一放宽，元断言跟着放宽即可。）
        //
        // ⇒ 真正能自证的判据是**具体的队伍集合**：
        // D-072 实测的 6 支必败队伍**全部含「挂机」**（挂机 ⇒ 攻 5 / 防 4 / 命 60），
        // 而**所有不含挂机的队伍都赢** ⇒ 这条断言同时表达了
        //   · 胜率（39/45 = 86.7%，落在 70–90% 目标区间内）
        //   · 设计意图（「选谁进队要有后果」：投入度最低的那批打不过首领）
        // 而且它**与任何区间写法无关** ⇒ 区间被放宽也照样红。
        //
        // ⚠️ 为什么不写死「39/45」：胜率会随**任何**战斗规则改动而变
        // （改个伤害公式就可能变）⇒ 写死会让每次规则微调都要改守卫 ⇒ 反而更容易被绕过。
        // ⇒ 这里要的是**「难度漂移就报」**，不是「精确等于某个数」。
        string expectedLosers = string.Join("、",
            "莽/挂机+莽/挂机", "莽/挂机+傲/挂机", "莽/挂机+随手/挂机",
            "傲/挂机+傲/挂机", "傲/挂机+随手/挂机", "随手/挂机+随手/挂机");
        string actualLosers = string.Join("、", lostCrews);

        Assertions.AssertThat(actualLosers).IsEqual(expectedLosers);
    }

    private static bool DidWin(CombatPartySnapshot party, int stage)
    {
        var combat = new FormulaCombat();
        return combat.DidHeroesWin(combat.Resolve("遭遇", party, stage));
    }
}
