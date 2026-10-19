using System;
using System.Collections.Generic;
using GdUnit4;
using ClaudeCodeGameStudios.Domain.Heroes;
using ClaudeCodeGameStudios.Domain.Quests;

/// <summary>
/// 队伍与任务的单元测试。
///
/// <para><b>本文档保护：</b></para>
/// <ul>
/// <li><b>I2</b>：同一英雄同一时刻至多被分配到一个进行中的任务</li>
/// <li><b>I14</b>：已结束的任务不会回到「进行中」</li>
/// <li><c>02</c> <b>F-01</b>：无效队伍不能启动任务；<b>F-02/F-03</b>：队伍成员与玩家选择一致</li>
/// <li><b>D-006</b>：随队者退出日常决策，但回到村庄后能重新参与</li>
/// </ul>
///
/// <para><b>不要重犯：</b>原型里的 <c>CohesionOf()</c> 按<b>性格计数</b>算配合系数，
/// 那是出厂属性替代关系发展（违反基线 §9.3），已作废。
/// 本模块<b>不内置任何合法性规则</b>——全部由 <see cref="SquadRules"/> 注入。</para>
///
/// <para><b>本文档不规定任何游戏数值。</b>测试用的「最多 N 人」只是夹具；
/// 真实人数上限属【待定】<c>07</c> 的 <b>P-10</b>。</para>
/// </summary>
[TestSuite]
public class QuestsTests
{
    private static HeroState MakeHero(string name)
        => new(new HeroDefinition(new HeroId("h-" + name), name, "来处"));

    /// <summary>
    /// 夹具规则：<b>加入</b>永远允许（单人前置校验放行），<b>队伍完备性</b>按人数判定。
    /// </summary>
    /// <remarks>
    /// 两者必须分开 —— 若把人数下限同时用于两者，逐个加入永远跨不过下限，
    /// 队伍建不起来。该缺陷由本文件实测抓到。
    /// 真实人数上限属【待定】<c>07</c> 的 <b>P-10</b>。
    /// </remarks>
    private static SquadRules SizeRule(int min, int max)
        => new(canJoin: _ => true, isRosterLegal: m => m.Count >= min && m.Count <= max);

    /// <summary>
    /// 单人前置校验：只放行<b>名字</b>在白名单里的英雄。
    /// <para>比对的是 <c>h.Definition.Name</c> 而非 <c>h.Id.Value</c> ——
    /// 后者是 <c>MakeHero</c> 拼出来的 <c>"h-" + 名字</c>。
    /// 拿名字去比 Id 是本文件第三次犯同一类错（前一次是拿偏好类别比活动 Id）。</para>
    /// </summary>
    private static SquadRules JoinableOnly(params string[] names)
    {
        var allowed = new HashSet<string>(names, StringComparer.Ordinal);
        return new SquadRules(canJoin: h => allowed.Contains(h.Definition.Name));
    }

    // ── 单人前置校验（S5）与队伍完备性（F-01）必须分开 ─────────────────────

    /// <summary>单人前置校验不通过时<b>拒绝加入，且队伍不变</b>。</summary>
    [TestCase]
    public void TryJoin_FailingPersonalPrecondition_IsRejected_AndMembershipUnchanged()
    {
        var squad = new Squad(JoinableOnly("甲"));
        squad.TryJoin(MakeHero("甲"));

        bool joined = squad.TryJoin(MakeHero("乙"));   // 不在白名单

        Assertions.AssertThat(joined).IsFalse();
        Assertions.AssertThat(squad.Members.Count).IsEqual(1);
    }

    /// <summary>
    /// 人数<b>下限</b>不满足时，<b>加入仍然成功</b>——它只在出发前拦人。
    /// <para>这是「两种规则分开」的核心可观测后果：下限不会让人建不起队伍。</para>
    /// </summary>
    [TestCase]
    public void TryJoin_BelowRosterMinimum_StillJoins_RosterIsSimplyNotReady()
    {
        var squad = new Squad(SizeRule(2, 4));

        bool joined = squad.TryJoin(MakeHero("独苗"));

        Assertions.AssertThat(joined).IsTrue();
        Assertions.AssertThat(squad.Members.Count).IsEqual(1);
        Assertions.AssertThat(squad.IsRosterReady).IsFalse();
    }

    /// <summary>超过人数<b>上限</b>时同样不拦加入 —— 只在出发前拦。</summary>
    [TestCase]
    public void TryJoin_AboveRosterMaximum_StillJoins_ButRosterIsNotReady()
    {
        var squad = new Squad(SizeRule(1, 2));
        squad.TryJoin(MakeHero("甲"));
        squad.TryJoin(MakeHero("乙"));

        bool joined = squad.TryJoin(MakeHero("丙"));

        Assertions.AssertThat(joined).IsTrue();
        Assertions.AssertThat(squad.Members.Count).IsEqual(3);
        Assertions.AssertThat(squad.IsRosterReady).IsFalse();
    }

    /// <summary>合法的加入成功，成员与玩家选择一致。</summary>
    [TestCase]
    public void TryJoin_LegalTeam_Succeeds_AndMembersMatchSelection()
    {
        var squad = new Squad(SizeRule(2, 4));
        var a = MakeHero("甲");
        var b = MakeHero("乙");

        squad.TryJoin(a);
        squad.TryJoin(b);

        Assertions.AssertThat(squad.Members.Count).IsEqual(2);
        Assertions.AssertThat(squad.Members[0] == a).IsTrue();
        Assertions.AssertThat(squad.Members[1] == b).IsTrue();
        Assertions.AssertThat(squad.IsRosterReady).IsTrue();
    }

    /// <summary>初始成员<b>单人前置校验</b>不通过时构造即失败 —— 不给「先建后判」的窗口。</summary>
    [TestCase]
    public void Squad_Constructor_FailingPersonalPrecondition_Throws()
    {
        bool threw = false;
        try
        {
            var unused = new Squad(JoinableOnly("甲"), new[] { MakeHero("乙") });
        }
        catch (ArgumentException) { threw = true; }

        Assertions.AssertThat(threw).IsTrue();
    }

    /// <summary>
    /// 默认规则是一切合法 —— 第一阶段不设任何硬编码的合法性条件。
    /// <para>这正是「不重蹈 CohesionOf 覆辙」的落点：规则来自基线，不来自原型。</para>
    /// </summary>
    [TestCase]
    public void Squad_WithDefaultRules_AcceptsAnySize()
    {
        var squad = new Squad(new SquadRules());
        for (var i = 0; i < 10; i++) squad.TryJoin(MakeHero("人" + i));

        Assertions.AssertThat(squad.Members.Count).IsEqual(10);
    }

    // ── F-01 · 无效队伍不能启动任务 ─────────────────────────────────────────

    /// <summary>队伍不合法时不能创建任务。</summary>
    [TestCase]
    public void Mission_Create_WithIllegalSquad_Throws()
    {
        var squad = new Squad(SizeRule(2, 4));
        squad.TryJoin(MakeHero("独苗"));

        bool threw = false;
        try { Mission.Create(new MissionId("m1"), "试探", squad, SizeRule(2, 4)); }
        catch (ArgumentException) { threw = true; }

        Assertions.AssertThat(threw).IsTrue();
    }

    /// <summary>合法队伍可以创建任务，初始状态为 Created。</summary>
    [TestCase]
    public void Mission_Create_WithLegalSquad_Succeeds()
    {
        var rules = SizeRule(1, 4);
        var squad = new Squad(rules);
        squad.TryJoin(MakeHero("甲"));

        var mission = Mission.Create(new MissionId("m1"), "去北边看看", squad, rules);

        Assertions.AssertThat(mission.Status).IsEqual(MissionStatus.Created);
        Assertions.AssertThat(mission.IsFinished).IsFalse();
    }

    // ── I2 · 一人同时只能在一个进行中的任务里 ─────────────────────────────

    /// <summary>已在别的任务里的人不能被再次分配（I2）。</summary>
    [TestCase]
    public void Mission_Start_HeroAlreadyBusy_IsRejected_AndNothingChanges()
    {
        var rules = SizeRule(1, 4);
        var shared = MakeHero("共享");

        var squadA = new Squad(rules);
        squadA.TryJoin(shared);
        var missionA = Mission.Create(new MissionId("mA"), "甲任务", squadA, rules);
        missionA.Start(_ => false);                    // 甲已出发

        var squadB = new Squad(rules);
        squadB.TryJoin(shared);
        var missionB = Mission.Create(new MissionId("mB"), "乙任务", squadB, rules);

        bool threw = false;
        try { missionB.Start(h => h.Id == shared.Id); }
        catch (InvalidOperationException) { threw = true; }

        Assertions.AssertThat(threw).IsTrue();
        Assertions.AssertThat(missionB.Status).IsEqual(MissionStatus.Created);   // 未被启动
        Assertions.AssertThat(missionA.Status).IsEqual(MissionStatus.Active);   // 甲不受影响
    }

    // ── D-006 · 随队者退出日常决策 ──────────────────────────────────────────

    /// <summary>开始任务后，成员退出日常决策（在场性变为随队）。</summary>
    [TestCase]
    public void Mission_Start_MembersLeaveTheDailyDecisionPool()
    {
        var rules = SizeRule(1, 4);
        var hero = MakeHero("阿黄");
        var squad = new Squad(rules);
        squad.TryJoin(hero);
        var mission = Mission.Create(new MissionId("m1"), "远征", squad, rules);

        Assertions.AssertThat(hero.IsEligibleForDecision()).IsTrue();   // 出发前
        mission.Start(_ => false);
        Assertions.AssertThat(hero.IsEligibleForDecision()).IsFalse();  // 出发后
    }

    /// <summary>任务结束后成员回到村庄，能重新参与决策。</summary>
    [TestCase]
    public void Mission_Finish_MembersReturnToTheDailyDecisionPool()
    {
        var rules = SizeRule(1, 4);
        var hero = MakeHero("阿黄");
        var squad = new Squad(rules);
        squad.TryJoin(hero);
        var mission = Mission.Create(new MissionId("m1"), "远征", squad, rules);
        mission.Start(_ => false);

        mission.Finish(MissionStatus.Succeeded);

        Assertions.AssertThat(hero.IsEligibleForDecision()).IsTrue();
    }

    // ── I14 · 状态单向 ─────────────────────────────────────────────────────

    /// <summary>已结束的任务不能再结束一次，也不能回到进行中。</summary>
    [TestCase]
    public void Mission_Finish_Twice_IsRejected()
    {
        var rules = SizeRule(1, 4);
        var squad = new Squad(rules);
        squad.TryJoin(MakeHero("甲"));
        var mission = Mission.Create(new MissionId("m1"), "任务", squad, rules);
        mission.Start(_ => false);
        mission.Finish(MissionStatus.Succeeded);

        bool threw = false;
        try { mission.Finish(MissionStatus.Failed); }
        catch (InvalidOperationException) { threw = true; }

        Assertions.AssertThat(threw).IsTrue();
        Assertions.AssertThat(mission.Status).IsEqual(MissionStatus.Succeeded);
    }

    /// <summary>已结束的任务不能重新开始。</summary>
    [TestCase]
    public void Mission_Restart_AfterFinish_IsRejected()
    {
        var rules = SizeRule(1, 4);
        var squad = new Squad(rules);
        squad.TryJoin(MakeHero("甲"));
        var mission = Mission.Create(new MissionId("m1"), "任务", squad, rules);
        mission.Start(_ => false);
        mission.Finish(MissionStatus.Failed);

        bool threw = false;
        try { mission.Start(_ => false); }
        catch (InvalidOperationException) { threw = true; }

        Assertions.AssertThat(threw).IsTrue();
    }

    /// <summary>不处于进行中的任务不能结束 —— 不允许凭空结算。</summary>
    [TestCase]
    public void Mission_Finish_BeforeStart_IsRejected()
    {
        var rules = SizeRule(1, 4);
        var squad = new Squad(rules);
        squad.TryJoin(MakeHero("甲"));
        var mission = Mission.Create(new MissionId("m1"), "任务", squad, rules);

        bool threw = false;
        try { mission.Finish(MissionStatus.Succeeded); }
        catch (InvalidOperationException) { threw = true; }

        Assertions.AssertThat(threw).IsTrue();
    }

    /// <summary>结束态只接受三种；用 Created / Active 结束属于编程错误。</summary>
    [TestCase]
    public void Mission_Finish_WithNonTerminalStatus_IsRejected()
    {
        var rules = SizeRule(1, 4);
        var squad = new Squad(rules);
        squad.TryJoin(MakeHero("甲"));
        var mission = Mission.Create(new MissionId("m1"), "任务", squad, rules);
        mission.Start(_ => false);

        bool threw = false;
        try { mission.Finish(MissionStatus.Active); }
        catch (ArgumentOutOfRangeException) { threw = true; }

        Assertions.AssertThat(threw).IsTrue();
    }
}
