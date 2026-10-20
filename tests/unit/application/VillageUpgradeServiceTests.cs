using System;
using System.Collections.Generic;
using System.Linq;
using GdUnit4;
using ClaudeCodeGameStudios.Application;
using ClaudeCodeGameStudios.Domain.Village;

namespace ClaudeCodeGameStudios.Tests.Application;

/// <summary>
/// <see cref="VillageUpgradeService"/> 的单元测试（D-054-02：村庄升级同时消耗金币和材料）。
///
/// <para><b>本文档保护一条：</b>
/// <b>要么两边都扣，要么都不扣</b>（I15）——
///
/// 绝不能出现「扣了材料没扣钱」这种半笔账。</para>
///
/// <para><b>本文档不规定层级</b>：层级由 <c>Main</c> 管（本服务只管扣款）；
/// 本服务要守的是**成本表**与**原子性**。</para>
/// </summary>
[TestSuite]
public class VillageUpgradeServiceTests
{
    private static readonly ResourceId Gold = new("金币");
    private static readonly ResourceId Material = new("材料");

    /// <summary>造一个村庄：金库与材料池各有 <paramref name="gold"/> / <paramref name="material"/>。</summary>
    private static (Village village, VillageUpgradeService svc)
        MakeScene(long gold, long material)
    {
        var village = new Village("v1");
        if (gold > 0) village.Add(Gold, gold);
        if (material > 0) village.Add(Material, material);
        return (village, new VillageUpgradeService(village, Gold, Material));
    }

    // ── 成本表 ──────────────────────────────────────────────────────────────

    /// <summary>五级金币成本 50/100/150/200/250，合计 750（D-054 裁决 ⑤）。</summary>
    [TestCase]
    public void CostTable_GoldIs50To250_Totalling750()
    {
        var (_, svc) = MakeScene(0, 0);

        long total = 0;
        for (int tier = 0; tier < VillageUpgradeService.MaxTier; tier++)
        {
            var cost = svc.CostFor(tier);
            total += cost.Gold;
        }

        Assertions.AssertThat(total).IsEqual(750L);
        Assertions.AssertThat(svc.CostFor(0).Gold).IsEqual(50L);
        Assertions.AssertThat(svc.CostFor(1).Gold).IsEqual(100L);
        Assertions.AssertThat(svc.CostFor(2).Gold).IsEqual(150L);
        Assertions.AssertThat(svc.CostFor(3).Gold).IsEqual(200L);
        Assertions.AssertThat(svc.CostFor(4).Gold).IsEqual(250L);
    }

    /// <summary>材料成本沿用既有规则 <c>30 + tier × 25</c>，合计 400（未改）。</summary>
    [TestCase]
    public void CostTable_MaterialUnchanged_30To130_Totalling400()
    {
        var (_, svc) = MakeScene(0, 0);

        long total = 0;
        for (int tier = 0; tier < VillageUpgradeService.MaxTier; tier++)
        {
            var cost = svc.CostFor(tier);
            Assertions.AssertThat(cost.Material).IsEqual(30L + tier * 25L);
            total += cost.Material;
        }

        Assertions.AssertThat(total).IsEqual(400L);
    }

    /// <summary>
    /// <b>跨层一致性</b>：视图层的层级上限必须与规则层一致 ——
    /// 两处数字各写一份，迟早会悄悄分叉（能升到第 6 级，或永远升不到第 5 级）。
    /// </summary>
    [TestCase]
    public void MaxTier_MatchesTheViewLayerConstant()
    {
        Assertions.AssertThat(VillageUpgradeService.MaxTier).IsEqual(VillageView.MaxTier);
    }

    // ── 原子性：要么都扣，要么都不扣 ────────────────────────────────────────

    /// <summary>金币与材料都够 → <b>两边都扣</b>，累计销毁金币正确。</summary>
    [TestCase]
    public void TryUpgrade_WhenBothAffordable_SpendsGoldAndMaterial()
    {
        var (village, svc) = MakeScene(gold: 1000, material: 1000);

        bool ok = svc.TryUpgrade(0);

        Assertions.AssertThat(ok).IsTrue();
        Assertions.AssertThat(village.Get(Gold)).IsEqual(1000L - 50L);
        Assertions.AssertThat(village.Get(Material)).IsEqual(1000L - 30L);
        Assertions.AssertThat(svc.TotalGoldSpent).IsEqual(50L);
        Assertions.AssertThat(svc.UpgradesStarted).IsEqual(1);
    }

    /// <summary>金币不够 → <b>材料也不许扣</b>（不能出现「扣了材料没扣钱」）。</summary>
    [TestCase]
    public void TryUpgrade_WhenGoldShort_SpendsNothingAtAll()
    {
        var (village, svc) = MakeScene(gold: 49, material: 1000);

        bool ok = svc.TryUpgrade(0);

        Assertions.AssertThat(ok).IsFalse();
        Assertions.AssertThat(village.Get(Gold)).IsEqual(49L);      // 金币没动
        Assertions.AssertThat(village.Get(Material)).IsEqual(1000L); // 材料也没动
        Assertions.AssertThat(svc.TotalGoldSpent).IsEqual(0L);
        Assertions.AssertThat(svc.UpgradesStarted).IsEqual(0);
    }

    /// <summary>材料不够 → <b>金币也不许扣</b>（另一半）。</summary>
    [TestCase]
    public void TryUpgrade_WhenMaterialShort_SpendsNothingAtAll()
    {
        var (village, svc) = MakeScene(gold: 1000, material: 29);

        bool ok = svc.TryUpgrade(0);

        Assertions.AssertThat(ok).IsFalse();
        Assertions.AssertThat(village.Get(Gold)).IsEqual(1000L);     // 金币没动
        Assertions.AssertThat(village.Get(Material)).IsEqual(29L);  // 材料也没动
        Assertions.AssertThat(svc.TotalGoldSpent).IsEqual(0L);
    }

    /// <summary>刚好够 → 能升（边界不多扣一分）。</summary>
    [TestCase]
    public void TryUpgrade_WhenExactlyAffordable_Succeeds()
    {
        var (village, svc) = MakeScene(gold: 50, material: 30);

        bool ok = svc.TryUpgrade(0);

        Assertions.AssertThat(ok).IsTrue();
        Assertions.AssertThat(village.Get(Gold)).IsEqual(0L);
        Assertions.AssertThat(village.Get(Material)).IsEqual(0L);
    }

    // ── 上限与越界 ──────────────────────────────────────────────────────────

    /// <summary>升满 5 级 → <b>不再有支出</b>（金库出口是有上限的）。</summary>
    [TestCase]
    public void TryUpgrade_AtMaxTier_SpendsNothing()
    {
        var (village, svc) = MakeScene(gold: 10000, material: 10000);

        bool ok = svc.TryUpgrade(VillageUpgradeService.MaxTier);

        Assertions.AssertThat(ok).IsFalse();
        Assertions.AssertThat(village.Get(Gold)).IsEqual(10000L);
        Assertions.AssertThat(village.Get(Material)).IsEqual(10000L);
        Assertions.AssertThat(svc.TotalGoldSpent).IsEqual(0L);
    }

    /// <summary>层级越界（负数）→ 拒绝，不花钱。</summary>
    [TestCase]
    public void TryUpgrade_NegativeTier_SpendsNothing()
    {
        var (village, svc) = MakeScene(gold: 1000, material: 1000);

        bool ok = svc.TryUpgrade(-1);

        Assertions.AssertThat(ok).IsFalse();
        Assertions.AssertThat(village.Get(Gold)).IsEqual(1000L);
    }

    // ── 预检不改状态 ────────────────────────────────────────────────────────

    /// <summary><c>CanUpgrade</c> 只回答「行不行」，<b>不改任何状态</b>。</summary>
    [TestCase]
    public void CanUpgrade_DoesNotChangeAnyState()
    {
        var (village, svc) = MakeScene(gold: 1000, material: 1000);

        bool can = svc.CanUpgrade(0);

        Assertions.AssertThat(can).IsTrue();
        Assertions.AssertThat(village.Get(Gold)).IsEqual(1000L);
        Assertions.AssertThat(village.Get(Material)).IsEqual(1000L);
        Assertions.AssertThat(svc.TotalGoldSpent).IsEqual(0L);
    }

    // ── 连续升级：累计销毁金币 ──────────────────────────────────────────────

    /// <summary>
    /// 五级升完 → 累计销毁 <b>750 金</b>、材料 <b>400</b>；金库余额相应减少。
    /// </summary>
    /// <remarks>
    /// 这条是守恒律的<b>销毁项</b>的来源：升级花掉的金币变成建筑，
    /// 既不在英雄钱包也不在金库 —— 它必须被**单独记一笔**，否则守恒律对不上。
    /// </remarks>
    [TestCase]
    public void TryUpgrade_AllFiveLevels_Spends750GoldAnd400Material()
    {
        var (village, svc) = MakeScene(gold: 750, material: 400);

        for (int tier = 0; tier < VillageUpgradeService.MaxTier; tier++)
        {
            Assertions.AssertThat(svc.TryUpgrade(tier)).IsTrue();
        }

        Assertions.AssertThat(svc.TotalGoldSpent).IsEqual(750L);
        Assertions.AssertThat(svc.UpgradesStarted).IsEqual(VillageUpgradeService.MaxTier);
        Assertions.AssertThat(village.Get(Gold)).IsEqual(0L);
        Assertions.AssertThat(village.Get(Material)).IsEqual(0L);
    }
}
