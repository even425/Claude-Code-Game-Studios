using System;
using System.Collections.Generic;
using GdUnit4;
using ClaudeCodeGameStudios.Application;
using ClaudeCodeGameStudios.Domain.Clock;
using ClaudeCodeGameStudios.Domain.Heroes;
using ClaudeCodeGameStudios.Domain.Quests;

namespace ClaudeCodeGameStudios.Tests;

/// <summary>
/// <b>IT-30（A-10 / D-006 / F-10）</b>：副本<b>不冻结村庄</b>。
///
/// <para><b>它守的是什么</b>：D-006 已裁决三条，缺一不可：
/// <list type="number">
/// <item>副本进行期间，<b>未随队的英雄照常执行自主行为</b>；</item>
/// <item><b>模拟时间继续推进</b>（副本不冻结村庄）；</item>
/// <item><b>随队英雄不执行日常行为</b> —— 他们保持「队伍任务中」。</item>
/// </list></para>
///
/// <para><b>为什么这条值钱</b>：副本通常是<b>游戏里最长的一段等待</b>。
/// 如果副本一开，整个村庄就停摆（时间不走、别人发呆、工地说停就停），
/// 那玩家每下一次副本都要盯着进度条干等 —— 而底线的立意恰恰是
/// 「你不按任何键，村庄也在过日子」。副本<b>不能</b>成为那个例外。</para>
///
/// <para><b>为什么这条此前没人写</b>：它跨了「副本模块 × 决策链 × 时钟」三块
/// （<c>06</c> 把它归在 L2 集成层）。而 L2 缺的正是这一条 ——
/// 单元测试都绿，跨模块的顺序一换就露馅。</para>
///
/// <para><b>本文件不发明任何玩法数值</b>。步长、时长、种子都是<b>测试夹具</b>。</para>
/// </summary>
[TestSuite]
public class DungeonDoesNotFreezeVillageTests
{
    // ── 夹具 ──────────────────────────────────────────────────────────

    private const double FixedStepSeconds = 0.5;
    private const double ActionDurationSeconds = 1.3;
    private const ulong TestSeed = 0xD006_1234UL;

    private static readonly ActivityId Collect = new("采集");
    private static readonly ActivityId Leisure = new("休闲");

    private static List<ActivityDefinition> Activities() => new()
    {
        new ActivityDefinition(Collect, new PreferenceCategory("劳作"), "勤快", ActionDurationSeconds),
        new ActivityDefinition(Leisure, new PreferenceCategory("闲散"), "懒散", ActionDurationSeconds),
    };

    private static HeroState MakeHero(string id) =>
        new(new HeroDefinition(new HeroId(id), id, "测试来处",
            new Dictionary<string, double> { ["勤快"] = 1.5, ["懒散"] = 1.0 },
            new Dictionary<string, double> { ["劳作"] = 1.2, ["闲散"] = 1.0 }));

    /// <summary>一步模拟：推进行动；空闲且在村的英雄做一次决策。</summary>
    private sealed class VillageStep : ISimulationStep
    {
        private readonly IReadOnlyList<HeroState> _heroes;
        private readonly DecisionFlow _flow;
        private readonly List<string> _log;

        public VillageStep(IReadOnlyList<HeroState> heroes, DecisionFlow flow, List<string> log)
        {
            _heroes = heroes;
            _flow = flow;
            _log = log;
        }

        public string Name => "village";

        public void Execute(SimulationStepContext context)
        {
            foreach (var hero in _heroes)
            {
                var finished = hero.AdvanceAction(context.SimDeltaSeconds);
                if (finished != ActivityId.None) _log.Add($"done:{hero.Id.Value}");

                if (!hero.IsEligibleForDecision()) continue;

                var result = _flow.Step(hero);
                if (result.Outcome == DecisionOutcome.Started)
                {
                    _log.Add($"start:{hero.Id.Value}:{result.Chosen.Value}");
                }
            }
        }
    }

    // ── IT-30 ────────────────────────────────────────────────────────

    /// <summary>
    /// <b>IT-30</b>：副本进行期间 —— ① 模拟时间继续推进；② 未随队的英雄照常行动；
    /// ③ 随队英雄不执行日常行为。
    /// </summary>
    [TestCase]
    public void IT30_WhileAHeroIsOnADungeon_TheVillageKeepsLiving()
    {
        var onDungeon = MakeHero("h-away");
        var inVillage = MakeHero("h-home");

        // ── 副本开始：随队英雄进入「队伍任务中」（D-006）────────────
        onDungeon.SetOnMission();
        Assertions.AssertThat(onDungeon.Presence).IsEqual(Presence.OnMission);

        var clock = new SimClock(simSecondsPerDay: 24 * 3600, realToSimSeconds: r => r);
        var flow = new DecisionFlow(Activities(), new DecisionFlow.SeededRandom(TestSeed));
        var log = new List<string>();
        var heroes = new[] { onDungeon, inVillage };
        var runner = new SimulationRunner(
            clock,
            new ISimulationStep[] { new VillageStep(heroes, flow, log) },
            fixedStepSeconds: FixedStepSeconds,
            maxCatchUpSteps: SimulationRunner.RequiredCatchUpLimit);

        // ── 跑 200 个模拟步 ──────────────────────────────────────────
        int guard = 0;
        while (clock.StepCount < 200)
        {
            if (++guard > 10_000_000) throw new InvalidOperationException("模拟没有推进。");
            runner.AdvanceFrame(1.0 / 60.0);
        }

        // ① 模拟时间继续推进（副本没有冻结村庄）
        Assertions.AssertThat(clock.Now).IsGreater(0.0);
        Assertions.AssertThat(clock.StepCount).IsEqual(200L);

        // ② 未随队的英雄照常执行自主行为
        bool homeActed = log.Exists(l => l.StartsWith("start:h-home:"));
        Assertions.AssertThat(homeActed).IsTrue();

        // ③ 随队英雄不执行日常行为
        bool awayActed = log.Exists(l => l.StartsWith("start:h-away:"));
        Assertions.AssertThat(awayActed).IsFalse();
        Assertions.AssertThat(onDungeon.CurrentActivity).IsEqual(ActivityId.None);
    }

    /// <summary>
    /// IT-30 的配套：副本进行期间，<b>随队英雄不可被编入第二个任务</b>
    /// （D-006 明写「参战英雄不可被编入第二个任务」，与不变量 I2 直接兼容）。
    /// </summary>
    [TestCase]
    public void IT30_AHeroOnADungeon_CannotBePutIntoASecondMission()
    {
        var hero = MakeHero("h-away");
        hero.SetOnMission();

        var rules = new SquadRules(
            canJoin: st => st.Presence == Presence.InVillage,
            isRosterLegal: members => members.Count <= 4);

        var squad = new Squad(rules);
        bool joined = squad.TryJoin(hero);

        Assertions.AssertThat(joined).IsFalse();
        Assertions.AssertThat(squad.Members.Count).IsEqual(0);
    }
}
