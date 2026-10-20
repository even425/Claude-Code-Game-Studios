using System;
using System.Collections.Generic;
using System.Linq;
using GdUnit4;
using ClaudeCodeGameStudios.Application;
using ClaudeCodeGameStudios.Domain.Combat;

namespace ClaudeCodeGameStudios.Tests.Combat;

/// <summary>
/// <b>战斗能看到队伍配置</b> —— IT-31（队伍配置按规则影响副本结果）的<b>前置</b>。
///
/// <para><b>它守的是什么</b>：战斗解算器<b>拿得到</b>参战者的状态快照
/// （成员、特质修正、偏好权重）—— 而不只是<b>一串标识</b>。</para>
///
/// <para><b>为什么这是「前置」而不是 IT-31 本身</b>：IT-31 要验的是
/// 「<b>队伍配置按规则影响副本结果</b>」，而规则（伤害 / 命中 / 冷却）属【待定】T-004。
/// 但查下来发现，IT-31 之所以<b>连断言形式都写不出来</b>，不只是因为公式没定 ——
/// 还因为<b>战斗压根拿不到输入</b>：<c>IBattleResolver.Resolve</c> 原本只收
/// <c>participantIds</c>（一串标识），而基线 §14.3 的对照实验明确要求固定输入里
/// 包含「<b>相同的英雄队伍（成员、装备、状态）</b>」。
/// ⇒ 先把<b>输入通道</b>接通，再等公式。</para>
///
/// <para><b>本文件不规定任何战斗数值</b>：快照里放的是<b>夹具</b>，
/// 不是设计值（真实特质条目与取值属基线 §4.2 / T-004）。</para>
///
/// <para><b>为什么值得单独守</b>：这个通道<b>没有任何测试盯着</b>的时候，
/// 它会被下一次「顺手重构接口」悄悄删掉 —— 而删掉它的后果<b>不会立刻报错</b>：
/// 战斗照样跑、照样出结果，只是<b>再也不可能用队伍配置</b>。
/// 等 T-004 拍板时才发现，就得连接口带调用方一起改一遍。</para>
/// </summary>
[TestSuite]
public class CombatPartyInputTests
{
    private static CombatantSnapshot MakeCombatant(
        string id, string name, double traitValue = 1.0, double preference = 1.0)
        => CombatantSnapshot.Create(
            id, name,
            new Dictionary<string, double>(StringComparer.Ordinal) { ["夹具特质"] = traitValue },
            new Dictionary<string, double>(StringComparer.Ordinal) { ["夹具偏好"] = preference });

    /// <summary>
    /// 把快照**原样**传给战斗解算器的替身 —— 它记下自己看到的东西。
    /// </summary>
    private sealed class SpyCombat : IBattleResolver, IBattleOutcomeReader
    {
        private const string VerdictWin = "胜";

        public List<CombatPartySnapshot> Seen { get; } = new();

        public IReadOnlyList<BattleLogEntry> Resolve(
            string encounterName, CombatPartySnapshot party)
        {
            Seen.Add(party);
            return new[]
            {
                new BattleLogEntry($"{encounterName}#e0", $"{encounterName}：双方接战", false),
                new BattleLogEntry($"{encounterName}#e1", $"{encounterName}：判定 {VerdictWin}", true),
            };
        }

        public bool DidHeroesWin(IReadOnlyList<BattleLogEntry> log) => true;
    }

    // ── 通道：战斗拿得到队伍配置 ────────────────────────────────────────────

    /// <summary>
    /// 战斗解算器<b>收到的就是那份快照</b>（同一份对象，不是「重新造的等价物」）。
    /// </summary>
    [TestCase]
    public void Resolve_ReceivesThePartySnapshotItWasGiven()
    {
        var spy = new SpyCombat();
        var party = new CombatPartySnapshot(new[]
        {
            MakeCombatant("h-1", "甲", 1.5, 2.0),
            MakeCombatant("h-2", "乙", 0.8, 1.2),
        });

        spy.Resolve("遭遇一", party);

        Assertions.AssertThat(spy.Seen.Count).IsEqual(1);
        Assertions.AssertThat(spy.Seen[0]).IsSame(party);
    }

    /// <summary>
    /// 快照里<b>带得出</b>成员的特质修正与偏好权重 —— 这正是战斗公式要用的东西。
    /// </summary>
    [TestCase]
    public void Snapshot_CarriesTraitModifiersAndPreferenceWeights()
    {
        var combatant = MakeCombatant("h-1", "甲", 1.5, 2.0);

        Assertions.AssertThat(combatant.TraitModifiers["夹具特质"]).IsEqual(1.5);
        Assertions.AssertThat(combatant.PreferenceWeights["夹具偏好"]).IsEqual(2.0);
        Assertions.AssertThat(combatant.Id).IsEqual("h-1");
        Assertions.AssertThat(combatant.Name).IsEqual("甲");
    }

    /// <summary>
    /// 队伍名单<b>按快照</b>给出（不是另外一份名单）——
    /// 否则「战斗看到的队伍」与「副本记录的队伍」可能不是同一批人。
    /// </summary>
    [TestCase]
    public void PartySnapshot_ExposesTheSameParticipantIds()
    {
        var party = new CombatPartySnapshot(new[]
        {
            MakeCombatant("h-1", "甲"),
            MakeCombatant("h-2", "乙"),
            MakeCombatant("h-3", "丙"),
        });

        Assertions.AssertThat(party.ParticipantIds).IsEqual(new[] { "h-1", "h-2", "h-3" });
        Assertions.AssertThat(party.Count).IsEqual(3);
    }

    // ── 快照必须是「值」：解算之后再改状态，算出来的结果不能变 ────────────

    /// <summary>
    /// <b>快照是值，不是活引用</b>：拿到快照之后改原来的字典，
    /// 快照<b>不许跟着变</b>。
    /// <para><b>为什么这条重要</b>：战斗结果必须能<b>离线复现</b>
    /// （基线 §8.5 D5：日志要能复现胜负判定所引用的每一个关键事件）。
    /// 若快照持有原字典，解算之后再改一次英雄状态，
    /// <b>已经算出来的战斗就变了</b> —— 而存档里只存着「谁去了」，
    /// 读档后重放会得到<b>另一个结果</b>。</para>
    /// </summary>
    [TestCase]
    public void Snapshot_IsAValue_NotALiveReference()
    {
        var traits = new Dictionary<string, double>(StringComparer.Ordinal) { ["夹具特质"] = 1.0 };
        var combatant = CombatantSnapshot.Create("h-1", "甲", traits);

        traits["夹具特质"] = 99.0;

        Assertions.AssertThat(combatant.TraitModifiers["夹具特质"]).IsEqual(1.0);
    }

    /// <summary>
    /// <b>偏好权重</b>同样必须是值（不是活引用）。
    /// <para>⚠️ 上一条只查了<b>特质</b>那一本字典 ⇒ 把偏好权重换成活引用，
    /// 它照样绿（变异矩阵实测：3/4 ⇒ 这一档漏网）。
    /// <b>两本字典都得复制</b>，否则「快照是值」这条性质只是<b>半条</b>。</para>
    /// </summary>
    [TestCase]
    public void Snapshot_CopiesPreferenceWeights_Too()
    {
        var weights = new Dictionary<string, double>(StringComparer.Ordinal) { ["夹具偏好"] = 2.0 };
        var combatant = CombatantSnapshot.Create("h-1", "甲", null, weights);

        weights["夹具偏好"] = 99.0;

        Assertions.AssertThat(combatant.PreferenceWeights["夹具偏好"]).IsEqual(2.0);
    }

    /// <summary>
    /// 空队伍<b>不合法</b>：副本必须有参与者（<c>DungeonRun.Create</c> 也这么要求）。
    /// </summary>
    [TestCase]
    public void PartySnapshot_EmptyParty_IsRejected()
    {
        bool threw = false;
        try { _ = new CombatPartySnapshot(Array.Empty<CombatantSnapshot>()); }
        catch (ArgumentException) { threw = true; }

        Assertions.AssertThat(threw).IsTrue();
    }

    /// <summary>
    /// 按标识造的空状态快照：<b>名单在、状态不明</b>。
    /// <para>⚠️ 这是「兜底」，不是「正常路径」——
    /// 它存在的唯一理由是让既有调用点不必改。而<b>一旦战斗规则开始用队伍配置</b>，
    /// 调用方就**必须**传真实快照，否则规则会静悄悄地看到一片空白。</para>
    /// </summary>
    [TestCase]
    public void FromIds_KeepsTheRoster_ButHasNoState()
    {
        var party = CombatPartySnapshot.FromIds(new[] { "h-1", "h-2" });

        Assertions.AssertThat(party.ParticipantIds).IsEqual(new[] { "h-1", "h-2" });
        Assertions.AssertThat(party.Combatants[0].TraitModifiers).IsEmpty();
        Assertions.AssertThat(party.Combatants[0].PreferenceWeights).IsEmpty();
    }

    // ── 传递路径：谁把快照交给了战斗 ────────────────────────────────────────
    //
    // ⚠️ 上面那几条只盯着**快照对象本身**。而「通道被悄悄删掉」的那类洞
    //    **恰好发生在传递路径上**：快照造得好好的，却在某一环被换成
    //    「只有标识的空状态版本」⇒ 战斗<b>照样跑、照样出结果</b>，
    //    只是**再也不可能用队伍配置**。所以下面几条盯的是
    //    **「战斗实际收到的那一份」**，不是「我们造的那一份」。

    /// <summary>
    /// 走<b>真实的流程</b>（<c>DungeonFlow</c>）时，战斗收到的必须是<b>传进去的那份</b>。
    /// <para>这一条才盯住<b>通道</b>：把 <c>DungeonFlow</c> 改成
    /// 「忽略传入的快照、自己造一份只有标识的」⇒ 这里立刻红。</para>
    /// </summary>
    [TestCase]
    public void DungeonFlow_PassesThePartySnapshot_ThroughToTheResolver()
    {
        var spy = new SpyCombat();
        var party = new CombatPartySnapshot(new[]
        {
            MakeCombatant("h-1", "甲", 1.5, 2.0),
            MakeCombatant("h-2", "乙", 0.8, 1.2),
        });

        var flow = DungeonFlow.Begin(
            new ClaudeCodeGameStudios.Domain.Dungeon.DungeonRunId("run-1"), "北境旧堡",
            party.ParticipantIds, spy, spy, party: party);
        flow.RunToCompletion();

        Assertions.AssertThat(spy.Seen.Count).IsGreaterEqual(1);
        // 战斗收到的**就是**那份快照（同一个对象）—— 不是「重新造的等价物」
        Assertions.AssertThat(spy.Seen[0]).IsSame(party);
        // 而且它**带得出状态**（不是被换成只有标识的空状态版本）
        Assertions.AssertThat(spy.Seen[0].Combatants[0].TraitModifiers["夹具特质"]).IsEqual(1.5);
    }

    /// <summary>
    /// 队伍人数<b>不同</b> ⇒ 战斗看到的<b>人数也不同</b>。
    /// <para>这一条盯住「快照真的被用上了」：若流程偷偷换成了
    /// 「自己造一份只有标识的」，人数还在但**状态全空**；
    /// 若连人数都被换掉，这里就红。</para>
    /// </summary>
    [TestCase]
    public void DungeonFlow_ResolverSeesTheActualPartySize()
    {
        var spy = new SpyCombat();
        var party = new CombatPartySnapshot(new[]
        {
            MakeCombatant("h-1", "甲"),
            MakeCombatant("h-2", "乙"),
            MakeCombatant("h-3", "丙"),
        });

        var flow = DungeonFlow.Begin(
            new ClaudeCodeGameStudios.Domain.Dungeon.DungeonRunId("run-2"), "北境旧堡",
            party.ParticipantIds, spy, spy, party: party);
        flow.RunToCompletion();

        Assertions.AssertThat(spy.Seen[0].Count).IsEqual(3);
    }
}
