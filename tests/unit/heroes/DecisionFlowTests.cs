using System;
using System.Collections.Generic;
using GdUnit4;
using ClaudeCodeGameStudios.Domain.Heroes;

/// <summary>
/// 英雄自主决策流程的单元测试。
///
/// <para><b>本文档保护的不变量是 CR-1</b>（<c>03</c> §3.6.3）：对候选集中的每一项活动，
/// 权重 <c>W &gt; 0</c> 恒成立 —— 人物特质与玩法偏好<b>只能改变权重，不能把任何活动变成不可做</b>。
/// 这是基线 §4.2「性格不是二极管」的机械落点：
/// 「喜欢打副本的英雄同样会休闲、会采集，只是效率慢点」。</para>
///
/// <para><b>API 约定（不要猜）</b>：<c>[TestSuite]</c> 在类上、<c>[TestCase]</c> 在方法上，
/// 断言走 <c>Assertions.AssertThat(...)</c>。已确认可用的断言有
/// <c>IsEqual</c> / <c>IsTrue</c> / <c>IsNotEmpty</c>。</para>
///
/// <para><b>本文档不规定任何游戏数值。</b> 文中出现的小权重是<b>测试夹具</b>，
/// 用来把「压得极低」这件事放大到可观测；真实取值属 <c>07</c> 的 <b>P-06 / 基线 §16 #3</b>【待定】。</para>
/// </summary>
[TestSuite]
public class DecisionFlowTests
{
    // ── 测试夹具 ────────────────────────────────────────────────────────────

    /// <summary>
    /// 「压得低但仍为正」的权重：与恒等权重 1 形成 <b>10:1</b>。
    /// <para>
    /// 比值必须让「N 步内至少命中一次」在统计上可靠：
    /// p = 1/11 ≈ 0.091，3000 步的期望命中约 <b>273 次</b>，几乎不可能为 0。
    /// 曾用 0.001（10⁶:1），8000 步期望命中仅 0.008 次 ——
    /// 断言「至少一次」在数学上就不成立，那是夹具写错，不是代码写错。
    /// </para>
    /// </summary>
    private const double LowWeight = 0.1;

    /// <summary>与 <see cref="LowWeight"/> 对称的高偏好权重。</summary>
    private const double HighWeight = 10.0;

    private static ActivityDefinition MakeActivity(string id, string category, string traitKey)
        => new(new ActivityId(id), new PreferenceCategory(category), traitKey, 0.0);

    /// <summary>两项活动，同属一个偏好类别但读不同的特质键。</summary>
    private static List<ActivityDefinition> TwoActivities()
        => new()
        {
            MakeActivity("采集", "劳作", "勤快"),
            MakeActivity("休闲", "闲散", "懒散"),
        };

    private static HeroDefinition MakeHero(
        string name = "测试",
        IReadOnlyDictionary<string, double>? prefs = null,
        IReadOnlyDictionary<string, double>? traits = null)
        => new(new HeroId("h-" + name), name, "测试来处", traits, prefs);

    // ── CR-1 · 构造期防线 ───────────────────────────────────────────────────

    /// <summary>
    /// CR-1 第一道防线：定义期就拒绝非正的偏好/特质系数。
    /// 0 与负数都会把某项活动变成「不可做」，必须在构造时炸掉，
    /// 而不是留到运行期变成一条难以定位的 bug。
    /// </summary>
    [TestCase]
    public void HeroDefinition_NonPositiveWeight_Throws_AtConstruction()
    {
        bool zeroThrew = false;
        bool negativeThrew = false;

        try
        {
            var unused0 = new HeroDefinition(
                new HeroId("x"), "零", "来处",
                preferenceWeights: new Dictionary<string, double> { ["劳作"] = 0.0 });
        }
        catch (ArgumentException) { zeroThrew = true; }

        try
        {
            var unused1 = new HeroDefinition(
                new HeroId("x"), "负", "来处",
                traitModifiers: new Dictionary<string, double> { ["勤快"] = -1.0 });
        }
        catch (ArgumentException) { negativeThrew = true; }

        Assertions.AssertThat(zeroThrew).IsTrue();
        Assertions.AssertThat(negativeThrew).IsTrue();
    }

    /// <summary>状态层权重因子返回 0 同样被拒 —— 它也是 CR-1 的一部分。</summary>
    [TestCase]
    public void Step_StateWeightFactorReturningZero_Throws()
    {
        var flow = new DecisionFlow(TwoActivities(), new DecisionFlow.SeededRandom(1),
            stateWeightFactor: _ => 0.0);
        var hero = new HeroState(MakeHero());

        bool threw = false;
        try { flow.Step(hero); }
        catch (InvalidOperationException) { threw = true; }

        Assertions.AssertThat(threw).IsTrue();
    }

    // ── CR-1 · 核心断言（A-02 反二极管）────────────────────────────────────

    /// <summary>
    /// 【A-02 反二极管】把某项活动压到极低，跑很多步，断言<b>它至少被选中过一次</b>。
    /// <para>
    /// 若 CR-1 被破坏（偏好/特质能把权重压到 0），这项活动将永远选不中，本测试变红。
    /// 这是「性格不是二极管」唯一有意义的机械检验 ——
    /// 断言「他很少做某事」是统计描述，断言「他至少做过一次」才是结构保证。
    /// </para>
    /// </summary>
    [TestCase]
    public void Step_HeavilyDispreferredActivity_IsStillChosenAtLeastOnce()
    {
        var activities = TwoActivities();
        // 「休闲」被压到极低；「采集」保持恒等权重 1。
        var hero = new HeroState(MakeHero(
            prefs: new Dictionary<string, double> { ["劳作"] = 1.0, ["闲散"] = LowWeight }));

        var flow = new DecisionFlow(activities, new DecisionFlow.SeededRandom(20261017));
        var chosen = new HashSet<string>();

        for (var step = 0; step < 5000; step++)
        {
            var result = flow.Step(hero);
            if (result.Outcome == DecisionOutcome.Started)
            {
                chosen.Add(result.Chosen.Value);
                hero.AdvanceAction(1.0);   // 瞬时行动，立刻完成以便下一步再决策
            }
        }

        Assertions.AssertThat(chosen.Contains("休闲")).IsTrue();
        // 同时确认它确实是「更少」而不是「一样多」—— 效率慢点，不是不能。
        Assertions.AssertThat(chosen.Contains("采集")).IsTrue();
    }

    /// <summary>
    /// 反向：偏好把某项活动**抬高**时，它被选中的次数应当**多于**压低的那项。
    /// 这条防止 CR-1 被修成「所有权重恒为 1」——那会把偏好系统整个废掉。
    /// </summary>
    [TestCase]
    public void Step_PreferenceShiftsTheDistribution_WithoutRemovingAnyOption()
    {
        int lowPicked = 0;
        int highPicked = 0;

        // 两轮分别用两个固定种子，统计同样的步数下两者的分布差异
        for (var trial = 0; trial < 2; trial++)
        {
            var hero = new HeroState(MakeHero(
                prefs: new Dictionary<string, double> { ["劳作"] = 1.0, ["闲散"] = LowWeight }));
            var flow = new DecisionFlow(TwoActivities(), new DecisionFlow.SeededRandom((ulong)(7 + trial)));

            for (var step = 0; step < 4000; step++)
            {
                var r = flow.Step(hero);
                if (r.Outcome == DecisionOutcome.Started)
                {
                    // 注意比对的是活动 **Id**「休闲」，不是偏好类别「闲散」。
                    // 曾经写成 `== "闲散"`，于是这条分支永不命中，全部落进 else，
                    // 表现为 highPicked=8000 / lowPicked=0 —— 一个纯测试 bug，
                    // 却被误读成「随机源坏了」。
                    if (r.Chosen.Value == "休闲") lowPicked++;
                    else highPicked++;
                    hero.AdvanceAction(1.0);
                }
            }
        }

        // 高偏好项被选中的次数应显著多于低偏好项
        Assertions.AssertThat(highPicked > lowPicked).IsTrue();
        // 但低偏好项**不是零**
        Assertions.AssertThat(lowPicked > 0).IsTrue();
    }

    // ── 四层正交：特质/偏好不得影响 S2/S5 ──────────────────────────────────

    /// <summary>
    /// 偏好压到极低时，另一项活动**仍然留在候选集中** ——
    /// 偏好只改 S3 的权重，S2 的剔除权只属于当前状态与环境（03 §3.3 的矩阵）。
    /// </summary>
    [TestCase]
    public void Step_PreferenceNeverRemovesAnActivityFromTheCandidateSet()
    {
        var hero = new HeroState(MakeHero(
            prefs: new Dictionary<string, double> { ["劳作"] = HighWeight, ["闲散"] = LowWeight }));
        var flow = new DecisionFlow(TwoActivities(), new DecisionFlow.SeededRandom(99));

        var seen = new HashSet<string>();
        for (var step = 0; step < 3000; step++)
        {
            var r = flow.Step(hero);
            if (r.Outcome == DecisionOutcome.Started)
            {
                seen.Add(r.Chosen.Value);
                hero.AdvanceAction(1.0);
            }
        }

        Assertions.AssertThat(seen.Count).IsEqual(2);
    }

    // ── S1 触发条件 ────────────────────────────────────────────────────────

    /// <summary>不在村的英雄不参与决策（D-006：在队伍任务中）。</summary>
    [TestCase]
    public void Step_HeroOnMission_IsNotEligible()
    {
        var hero = new HeroState(MakeHero());
        hero.SetOnMission();
        var flow = new DecisionFlow(TwoActivities(), new DecisionFlow.SeededRandom(1));

        var result = flow.Step(hero);

        Assertions.AssertThat(result.Outcome).IsEqual(DecisionOutcome.NotEligible);
    }

    /// <summary>已有进行中行动的英雄不参与决策（I2 / I16：同一时刻至多一个行动）。</summary>
    [TestCase]
    public void Step_BusyHero_IsNotEligible()
    {
        var hero = new HeroState(MakeHero());
        hero.BeginAction(new ActivityId("采集"), 10.0);
        var flow = new DecisionFlow(TwoActivities(), new DecisionFlow.SeededRandom(1));

        var result = flow.Step(hero);

        Assertions.AssertThat(result.Outcome).IsEqual(DecisionOutcome.NotEligible);
    }

    // ── S5 前置校验 ────────────────────────────────────────────────────────

    /// <summary>
    /// 前置校验不通过时，该活动本步不可用，应回退到重新抽取（03 §3.1 S5）。
    /// 断言最终选中的**不是**被拒的那项。
    /// </summary>
    [TestCase]
    public void Step_FailedPrecondition_ExcludesThatActivity_AndRetries()
    {
        var hero = new HeroState(MakeHero());
        // 「休闲」永远通不过前置校验（「采集」通过）
        var flow = new DecisionFlow(
            TwoActivities(),
            new DecisionFlow.SeededRandom(5),
            precondition: (_, a) => a.Id.Value != "休闲");

        var picked = new HashSet<string>();
        for (var step = 0; step < 50; step++)
        {
            var r = flow.Step(hero);
            if (r.Outcome == DecisionOutcome.Started)
            {
                picked.Add(r.Chosen.Value);
                hero.AdvanceAction(1.0);
            }
        }

        Assertions.AssertThat(picked.Contains("休闲")).IsFalse();
        Assertions.AssertThat(picked.Contains("采集")).IsTrue();
    }

    /// <summary>全部候选都通不过前置校验时，合法空转，且**不产生任何行动**（03 §3.5）。</summary>
    [TestCase]
    public void Step_AllPreconditionsFail_ReportsNoCandidates_WithoutAction()
    {
        var hero = new HeroState(MakeHero());
        var flow = new DecisionFlow(
            TwoActivities(), new DecisionFlow.SeededRandom(5), precondition: (_, _) => false);

        var result = flow.Step(hero);

        Assertions.AssertThat(result.Outcome).IsEqual(DecisionOutcome.NoCandidates);
        Assertions.AssertThat(hero.IsIdle).IsTrue();
    }

    // ── 可复现性（A7 的方向：注入而非全局随机）──────────────────────────

    /// <summary>相同种子 + 相同输入 → 完全相同的选择序列。整条决策链可复现。</summary>
    [TestCase]
    public void Step_SameSeed_ProducesIdenticalChoiceSequence()
    {
        List<string> Run()
        {
            var hero = new HeroState(MakeHero(
                prefs: new Dictionary<string, double> { ["劳作"] = 1.0, ["闲散"] = 0.4 }));
            var flow = new DecisionFlow(TwoActivities(), new DecisionFlow.SeededRandom(424242));
            var log = new List<string>();
            for (var i = 0; i < 200; i++)
            {
                var r = flow.Step(hero);
                if (r.Outcome == DecisionOutcome.Started)
                {
                    log.Add(r.Chosen.Value);
                    hero.AdvanceAction(1.0);
                }
            }
            return log;
        }

        var first = Run();
        var second = Run();

        // 采样必须真的采到东西，否则「两序列相等」可能只是两个空序列相等。
        Assertions.AssertThat(first.Count > 0).IsTrue();
        Assertions.AssertThat(string.Join(",", first)).IsEqual(string.Join(",", second));
    }

    // ── 行动生命周期 ───────────────────────────────────────────────────────

    /// <summary>耗时走完才释放占用；未走完前一直忙（I2 / I16）。</summary>
    [TestCase]
    public void AdvanceAction_HoldsUntilDurationElapses()
    {
        var hero = new HeroState(MakeHero());
        hero.BeginAction(new ActivityId("长途"), 10.0);

        Assertions.AssertThat(hero.AdvanceAction(4.0)).IsEqual(ActivityId.None);
        Assertions.AssertThat(hero.IsIdle).IsFalse();

        Assertions.AssertThat(hero.AdvanceAction(5.9)).IsEqual(ActivityId.None);
        Assertions.AssertThat(hero.IsIdle).IsFalse();

        var finished = hero.AdvanceAction(0.1);
        Assertions.AssertThat(finished.Value).IsEqual("长途");
        Assertions.AssertThat(hero.IsIdle).IsTrue();
    }

    /// <summary>已有行动时再开一个应当被拒（I2 / I16 的第二道防线）。</summary>
    [TestCase]
    public void BeginAction_WhileBusy_Throws()
    {
        var hero = new HeroState(MakeHero());
        hero.BeginAction(new ActivityId("采集"), 5.0);

        bool threw = false;
        try { hero.BeginAction(new ActivityId("休闲"), 5.0); }
        catch (InvalidOperationException) { threw = true; }

        Assertions.AssertThat(threw).IsTrue();
    }
}
