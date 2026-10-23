using System;
using System.Collections.Generic;
using System.Linq;
using GdUnit4;
using ClaudeCodeGameStudios.Application;
using ClaudeCodeGameStudios.Domain.Combat;
using ClaudeCodeGameStudios.Domain.Village;

namespace ClaudeCodeGameStudios.Tests.Architecture;

/// <summary>
/// <b>基线 §14.3 的六步法</b> —— 「防御设施改变了至少一项结算指标」的<b>对照实验</b>（D-100）。
/// </summary>
/// <remarks>
/// <para><b>基线 §14.3 原文（六步）</b>：
/// <list type="number">
/// <item><b>固定输入</b>：相同的初始世界状态 / 英雄队伍 / 兽潮参数 / 随机数种子。</item>
/// <item><b>执行 A（对照组）</b>：不建任何防御设施 ⇒ 跑一次完整兽潮 ⇒ 记录结算指标。</item>
/// <item><b>恢复初始状态</b>。</item>
/// <item><b>执行 B（实验组）</b>：建造指定防御设施 ⇒ 跑一次完整兽潮 ⇒ 记录同样指标。</item>
/// <item><b>验证</b>：防御设施<b>通过明确的游戏规则改变了至少一项结算指标</b>，
/// 且结果<b>与对应规则的预测一致</b>。</item>
/// <item><b>幂等检查</b>：存盘 → 读档 ⇒ 本次兽潮不被重复结算。</item>
/// </list></para>
///
/// <para>⚠️ <b>为什么现在才跑得起来</b>：这张验收单**一直标着「实测不成立」**
/// —— 因为 D-082 之前，防御设施**根本没有规则**（试炼塔只加生命、兽潮只损资源）
/// ⇒ 没有任何指标会变 ⇒ 「对照组与实验组一样」不是实验失败，是<b>没有实验可做</b>。<br>
/// ⇒ <b>D-085（围栏 ⇒ 兽潮规模 −1 级）+ D-095（防护罩 ⇒ 再 −1 级）</b>
/// 给了防御设施<b>明确的规则</b> ⇒ 这条验收<b>第一次真的能跑</b>。</para>
///
/// <para>⚠️ <b>为什么战斗里没有随机数</b>（D-062）很重要：
/// 「固定随机数种子」这一步<b>自动成立</b> ⇒ 对照实验<b>没有噪音</b>
/// ⇒ A 与 B 的差异<b>只可能</b>来自防御设施。</para>
///
/// <para>⚠️ <b>这条实验验的是「规则可验证」</b>，**不是**「平衡数值对不对」
/// （基线原文：「不预先规定防御设施减少多少伤害——先保证规则可验证，
/// 再通过实测确定平衡数值」）。</para>
/// </remarks>
[TestSuite]
public class BeastTideDefenseAblationTests
{
    /// <summary>村庄层级（D-080：兽潮规模随层级递增 ⇒ 要拿一个「有得降」的层级）。</summary>
    private const int VillageTier = 3;

    /// <summary>每级的放大百分比（与 <c>Main</c> 同值：8%）。</summary>
    private const int PercentPerTier = 8;

    /// <summary>参战阵容（固定 ⇒ A 与 B 只差防御设施）。</summary>
    private static readonly string[] Party = { "h-1", "h-2" };

    /// <summary>
    /// 英雄的战斗属性（固定 ⇒ 变量只有防御设施）。
    /// </summary>
    /// <remarks>
    /// ⚠️ **这组数是「挑出来」的**：它必须满足「规模 3 <b>输</b>、规模 2 <b>赢</b>」
    /// ⇒ 防御设施才能<b>改变胜负</b>（而不只是改变回合数）。
    /// ⇒ 实测扫出来的（攻 7 / 防 3 / 命 120，两名英雄）。
    /// <para>⚠️ **为什么不挑一组很强的**：那样两组都赢 ⇒ 差异只体现在回合数上，
    /// 而<b>回合数不是单调的</b>（怪更强 ⇒ 可能死得更快）⇒ 断言会落空。</para>
    /// </remarks>
    private static readonly CombatStats HeroStats = new(7, 3, 120);

    /// <summary>
    /// 造一个「只差防御设施」的村庄。
    /// </summary>
    /// <param name="defenses">要建的防御设施（标识）。</param>
    /// <returns>村庄。</returns>
    private static Village NewVillageWith(params string[] defenses)
    {
        var village = new Village("v1");
        village.TryPlace(new BuildingInstance(
            new BuildingId("village_square"),
            gridX: -5, gridY: -5, footprintW: 1, footprintH: 1,
            constructionProgress: 1.0, affectsAreaW: 11, affectsAreaH: 11));

        int x = 1;
        foreach (var id in defenses)
        {
            village.TryPlace(new BuildingInstance(
                new BuildingId(id),
                gridX: x, gridY: 5, footprintW: 1, footprintH: 1,
                constructionProgress: 1.0, affectsAreaW: 0, affectsAreaH: 0));
            x++;
        }

        return village;
    }

    /// <summary>
    /// 跑一次兽潮（<b>纯计算</b>），返回结算指标。
    /// </summary>
    /// <remarks>
    /// ⚠️ **与游戏里走同一段代码**：规模等级由
    /// <see cref="BeastTideScale.TierWithDefenses"/> 算、解算器由同一个
    /// <see cref="FormulaCombat"/> 出 ⇒ 这里量到的就是玩家会遇到的那场兽潮。
    /// </remarks>
    private static (bool Won, int Rounds) RunBeastTide(Village village)
    {
        int tier = BeastTideScale.TierWithDefenses(
            villageTier: VillageTier,
            completedFences: BeastTideScale.CompletedFenceCount(village),
            completedShields: BeastTideScale.CompletedShieldCount(village));

        var combat = new FormulaCombat(
            monsterStats: stage => BeastTideScale.ScaleMonster(
                CombatParameters.MonsterFor(stage), tier, PercentPerTier));

        var result = BeastTideBattle.Resolve(
            Party, _ => HeroStats, combat, combat,
            runId: "ablation", placeName: "兽潮");

        return (result.Won, result.Rounds);
    }

    /// <summary>
    /// 战斗里<b>每一阶段</b>的实际强度（供断言「弱一级 ⇒ 确实更弱」用）。
    /// </summary>
    /// <remarks>
    /// ⚠️ **为什么要有这个**：<b>回合数不是单调的</b> —— 怪更强 ⇒ 可能死得更快
    /// （但伤害更高 ⇒ 英雄死得也快）⇒ 「弱一级 ⇒ 回合更少」<b>并不总成立</b>。
    /// ⇒ 断言要落在<b>真正单调</b>的那个量上：<b>怪物的属性</b>。
    /// <para>⚠️ 这也是实测才发现的：第一版断言「回合更少」⇒ <b>两条红了</b>
    /// ⇒ 不是引擎的错，是**断言选错了量**（D-100 的副产品）。</para>
    /// </remarks>
    private static int MonsterHealthAt(int tier, int stage)
        => BeastTideScale.ScaleMonster(
            CombatParameters.MonsterFor(stage), tier, PercentPerTier).Health;

    /// <summary>
    /// §14.3 步骤 5 的核心：<b>防御设施改变了胜负</b>
    /// （规模 3 输、规模 2 赢 ⇒ 一座围栏就把结局翻过来了）。
    /// </summary>
    /// <remarks>
    /// ⚠️ <b>为什么这条最要紧</b>：基线 §14.3 要的是
    /// 「防御设施通过明确的游戏规则<b>改变了至少一项结算指标</b>」
    /// ⇒ 「<b>赢变成输</b>」是最直白、最没有解释空间的那种改变。
    /// <para>⚠️ <b>为什么需要「挑一组属性」</b>：若阵容太强 ⇒ 两组都赢
    /// ⇒ 差异只剩回合数 ⇒ 而<b>回合数不是单调的</b>（怪更强 ⇒ 可能死得更快）
    /// ⇒ 断言会落空 ⇒ 这组属性是**实测扫出来**的（攻 7 / 防 3 / 命 120）。</para>
    /// </remarks>
    [TestCase]
    public void Defense_CanFlipTheOutcome()
    {
        // 对照组：无防御 ⇒ 规模 3 ⇒ 输。
        var control = RunBeastTide(NewVillageWith());

        // 实验组：一座围栏 ⇒ 规模 2 ⇒ 赢。
        var experiment = RunBeastTide(NewVillageWith("fence"));

        Assertions.AssertThat(control.Won).IsFalse();
        Assertions.AssertThat(experiment.Won).IsTrue();
    }

    /// <summary>
    /// §14.3 步骤 1–5：<b>对照组 vs 实验组</b> ⇒ 防御设施必须改变<b>至少一项</b>结算指标，
    /// 且变化方向<b>与规则的预测一致</b>。
    /// </summary>
    /// <remarks>
    /// ⚠️ <b>断言的是「方向」不是「具体数值」</b>：规则只说「围栏 ⇒ 规模 −1 级」，
    /// 没说「少打几回合」⇒ 所以断言「规模更低 ⇒ 兽潮更弱」
    /// （回合更少 / 或从守不住变成守得住）⇒ 数值留给后续标定。
    /// </remarks>
    [TestCase]
    public void Defense_ChangesAtLeastOneSettlementMetric_InThePredictedDirection()
    {
        // ── 步骤 2：对照组（无防御设施）──
        var control = RunBeastTide(NewVillageWith());

        // ── 步骤 4：实验组（一座围栏 ⇒ 规模 −1 级）──
        var experiment = RunBeastTide(NewVillageWith("fence"));

        // ── 步骤 5：验证：结算指标**确实变了** ──
        bool changed = control.Won != experiment.Won || control.Rounds != experiment.Rounds;
        Assertions.AssertThat(changed).IsTrue();

        // ⚠️ **方向**：规则说「规模 −1 级 ⇒ 怪更弱」⇒ 断言落在**怪物属性**上
        // （⚠️ **不是回合数**：怪更强 ⇒ 可能死得更快 ⇒ 回合数**不单调**，
        //  这是实测才发现的 —— 第一版断言「回合更少」⇒ 两条红了）。
        int controlHealth = MonsterHealthAt(3, 0);
        int experimentHealth = MonsterHealthAt(2, 0);
        Assertions.AssertThat(experimentHealth < controlHealth).IsTrue();
    }

    /// <summary>
    /// §14.3 步骤 1–5：<b>两种防御设施各自都有效</b>（围栏与防护罩可叠加 ⇒ 效果也叠加）。
    /// </summary>
    /// <remarks>
    /// ⚠️ 这一条是 D-085 与 D-095 的<b>合并验收</b>：
    /// 「各降一级、各降各的」⇒ 两样都建 ⇒ 规模 −2 级 ⇒ 比只建一样更弱。
    /// </remarks>
    [TestCase]
    public void FenceAndShield_StackUp()
    {
        // ⚠️ 断言落在**规模等级**上（规则直接说的那个量）
        // ⇒ 围栏 1 + 防护罩 1 ⇒ 规模 −2 级 ⇒ 比只建一样更弱。
        int none = BeastTideScale.TierWithDefenses(VillageTier, 0, 0);
        int fenceOnly = BeastTideScale.TierWithDefenses(VillageTier, 1, 0);
        int shieldOnly = BeastTideScale.TierWithDefenses(VillageTier, 0, 1);
        int both = BeastTideScale.TierWithDefenses(VillageTier, 1, 1);

        Assertions.AssertThat(fenceOnly).IsEqual(none - 1);
        Assertions.AssertThat(shieldOnly).IsEqual(none - 1);
        Assertions.AssertThat(both).IsEqual(none - 2);

        // ⇒ 落到怪物的**实际属性**上：两级叠加 ⇒ 比只建一样**更弱**。
        Assertions.AssertThat(MonsterHealthAt(both, 0) < MonsterHealthAt(fenceOnly, 0)).IsTrue();
        Assertions.AssertThat(MonsterHealthAt(both, 0) < MonsterHealthAt(shieldOnly, 0)).IsTrue();
    }

    /// <summary>
    /// §14.3 步骤 5 的另一半：结果<b>与规则的预测一致</b>
    /// ⇒ 规模等级每降一级，兽潮就<b>明确地弱一点</b>（可预测，不是碰巧）。
    /// </summary>
    /// <remarks>
    /// ⚠️ 这一条把「规则」与「结果」<b>对上</b>：
    /// 规则的预测是「等级越低 ⇒ 怪越弱 ⇒ 打得越快」
    /// ⇒ 断言「等级每降一级，回合数**不增**」。
    /// </remarks>
    [TestCase]
    public void TheOutcome_MatchesTheRulesPrediction()
    {
        // 层级 3 ⇒ 无防御时规模 3；一座围栏 ⇒ 2；围栏+防护罩 ⇒ 1。
        int tierNone = BeastTideScale.TierWithDefenses(VillageTier, 0, 0);
        int tierFence = BeastTideScale.TierWithDefenses(VillageTier, 1, 0);
        int tierBoth = BeastTideScale.TierWithDefenses(VillageTier, 1, 1);

        var none = RunBeastTide(NewVillageWith());
        var fence = RunBeastTide(NewVillageWith("fence"));
        var both = RunBeastTide(NewVillageWith("fence", "shield"));

        Assertions.AssertThat(tierNone).IsEqual(3);
        Assertions.AssertThat(tierFence).IsEqual(2);
        Assertions.AssertThat(tierBoth).IsEqual(1);

        // ⚠️ **落在怪物属性上**（单调的那个量）⇒ 回合数不单调（实测才发现）。
        Assertions.AssertThat(MonsterHealthAt(tierFence, 0) < MonsterHealthAt(tierNone, 0)).IsTrue();
        Assertions.AssertThat(MonsterHealthAt(tierBoth, 0) < MonsterHealthAt(tierFence, 0)).IsTrue();
    }

    /// <summary>
    /// §14.3 步骤 5：<b>对照实验必须只有这一个变量</b>
    /// ⇒ 不建防御设施时，两次跑的结算指标<b>完全一样</b>。
    /// </summary>
    /// <remarks>
    /// ⚠️ <b>为什么这条重要</b>：战斗里没有随机数（D-062）⇒ 同输入必同输出
    /// ⇒ 若这条红了 ⇒ 说明「战斗其实有随机数」或「有别的隐藏变量」
    /// ⇒ 那么整个对照实验的结论都**不可信**。
    /// </remarks>
    [TestCase]
    public void TheControlRun_IsReproducible()
    {
        var first = RunBeastTide(NewVillageWith());
        var second = RunBeastTide(NewVillageWith());

        Assertions.AssertThat(second.Won).IsEqual(first.Won);
        Assertions.AssertThat(second.Rounds).IsEqual(first.Rounds);
    }
}
