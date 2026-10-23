using System;
using System.Collections.Generic;
using GdUnit4;
using ClaudeCodeGameStudios.Application;
using ClaudeCodeGameStudios.Application.Stall;
using ClaudeCodeGameStudios.Domain.Heroes;
using ClaudeCodeGameStudios.Domain.Items;
using ClaudeCodeGameStudios.Domain.Village;

namespace ClaudeCodeGameStudios.Tests.Stall;

/// <summary>
/// <b>摆摊位置与「范围建筑」的关系</b> —— 基线 §12.4 的落地探针（D-098）。
/// </summary>
/// <remarks>
/// <para><b>基线 §12.4 原文（开发者已确认）</b>：
/// 「有些玩家还会在村庄周边，<b>防护罩内</b>自行摆摊」⇒
/// 「因此摆摊位置<b>受防护罩范围约束</b>」。</para>
///
/// <para>⚠️ <b>为什么这条一直没被验过</b>：防护罩是 <b>D-095</b> 才加的建筑，
/// 而摆摊位置守门是 <b>D-033</b> 写的 ⇒ 当年写守门时，<b>村子里根本没有防护罩</b>
/// ⇒ 基线 §12.4 那句话<b>至今没有被任何守卫覆盖</b>。</para>
///
/// <para><b>D-098（开发者已拍板 B）</b>：摆摊只认<b>「范围类」建筑</b>
/// （围栏 + 市集），<b>防护罩不参与</b>。
/// ⇒ 基线 §12.4 的执行方式随之明确：<b>「防护罩 / 围栏这类范围建筑共用同一个『范围』原语，
/// 摆摊落在任一范围建筑内即可」</b>；防护罩当前<b>只</b>做防御（兽潮规模 −1 级）。</para>
/// </remarks>
[TestSuite]
public class StallRangeScopeTests
{
    private static readonly ItemId Stone = new("石料");
    private static readonly ResourceId GoldResource = new("金币");

    /// <summary>造一个村庄：<b>只有一个</b>建筑（它的影响区 = 摊位能摆到哪儿）。</summary>
    private static Village NewVillageWithOnly(
        string buildingId, int gridX, int gridY, int affectsW, int affectsH)
    {
        var village = new Village("v1");
        village.TryPlace(new BuildingInstance(
            new BuildingId(buildingId),
            gridX: gridX, gridY: gridY, footprintW: 1, footprintH: 1,
            constructionProgress: 1.0,
            affectsAreaW: affectsW, affectsAreaH: affectsH));
        return village;
    }

    /// <summary>在指定格位摆摊，看成不成功。</summary>
    private static bool CanPlaceStall(Village village, int gridX, int gridY)
    {
        var bag = new Inventory().TryAdd(Stone, 5)!;
        var wallet = new Wallet(new Dictionary<ResourceId, long> { { GoldResource, 100L } });
        var hero = new HeroAgent { Name = "甲" };
        hero.RestoreBag(bag);
        hero.RestoreWallet(wallet);

        var book = new PriceBook(new Dictionary<ItemId, long> { { Stone, 5L } });
        var service = new StallService(village, book, _ => hero);
        return service.PlaceStall(new HeroId("h-1"), Stone, 5, 0.0, gridX, gridY) is not null;
    }

    /// <summary>
    /// 事实：<b>只要是任何一个已建成建筑</b>，它的影响区就允许摆摊
    /// ⇒ 摆摊守门<b>不区分建筑种类</b>。
    /// </summary>
    /// <remarks>
    /// ⚠️ 这条测试<b>不表达「对不对」</b>，只把当前行为固定下来
    /// ⇒ 将来若按基线 §12.4 改成「只认防护罩」，这条会红 ⇒ 那时是有意改动。
    /// </remarks>
    [TestCase]
    public void Today_AnyCompletedBuilding_AffectsWhereStallsCanGo()
    {
        // 村里只有一座「市集」类建筑（影响区 11×11 落在 (-5,-5)）。
        var village = NewVillageWithOnly("village_square", -5, -5, 11, 11);

        bool insideMarket = CanPlaceStall(village, 0, 0);      // 在市集影响区内
        bool outsideMarket = CanPlaceStall(village, 9, 9);     // 完全在外

        Assertions.AssertThat(insideMarket).IsTrue();
        Assertions.AssertThat(outsideMarket).IsFalse();
    }

    /// <summary>
    /// <b>D-098（开发者拍板 B）</b>：<b>防护罩不放开摆摊范围</b>
    /// ⇒ 它是<b>专职防御</b>建筑，与围栏<b>分工不同</b>。
    /// </summary>
    /// <remarks>
    /// ⚠️ <b>这条断言在拍板前是反的</b>：当时实测是「防护罩**也**放开摆摊范围」
    /// ⇒ 与 D-095 文档写的分工不一致 ⇒ 记成 D-098 请开发者拍板
    /// ⇒ 拍板 B ⇒ 改成现在这条（并把实现也改了）。
    /// <para>⚠️ <b>配套</b>：<b>建筑落点合法性用的是同一把尺子</b>（D-093 原话
    /// 「落在村子的地盘里 ⇒ 与摆摊用的是同一把尺子」）⇒ 两处必须同时改，
    /// 否则玩家能靠防护罩在村外盖楼、却不能在那儿摆摊。</para>
    /// </remarks>
    [TestCase]
    public void TheShield_DoesNotOpenUpStallRange()
    {
        // 村里只有一座防护罩（9×9 落在 (0,0)）。
        var village = NewVillageWithOnly("shield", 0, 0, 9, 9);

        bool insideShield = CanPlaceStall(village, 2, 2);      // 罩子范围内
        bool outsideShield = CanPlaceStall(village, 9, 9);    // 罩子范围外

        // ⚠️ **两个都拒**：防护罩不是范围类 ⇒ 它的影响区**根本不看**。
        Assertions.AssertThat(insideShield).IsFalse();
        Assertions.AssertThat(outsideShield).IsFalse();
    }

    /// <summary>
    /// <b>D-098</b>：<b>围栏</b>是范围类 ⇒ 它<b>照旧</b>放开摆摊范围
    /// （这条不许被顺手改掉 —— 围栏的「范围」作用是 D-066 / D-074 已拍板的）。
    /// </summary>
    [TestCase]
    public void TheFence_StillOpensUpStallRange()
    {
        var village = NewVillageWithOnly("fence", 0, 0, 9, 9);

        bool insideFence = CanPlaceStall(village, 2, 2);
        bool outsideFence = CanPlaceStall(village, 9, 9);

        Assertions.AssertThat(insideFence).IsTrue();
        Assertions.AssertThat(outsideFence).IsFalse();
    }

    /// <summary>
    /// <b>D-098</b>：<b>市集</b>（不在可建目录里）<b>仍是</b>范围类
    /// ⇒ 开局一个摊位都摆不了的话，整个游戏就没得玩了。
    /// </summary>
    /// <remarks>
    /// ⚠️ 这一条守的是「**查不到 ⇒ 算范围类**」那条规则
    /// ⇒ 若有人把市集从范围类里踢掉，这条立刻红。
    /// </remarks>
    [TestCase]
    public void TheMarket_StillOpensUpStallRange()
    {
        var village = NewVillageWithOnly(RangeBuildings.MarketId, -5, -5, 11, 11);

        bool insideMarket = CanPlaceStall(village, 0, 0);
        bool outsideMarket = CanPlaceStall(village, 9, 9);

        Assertions.AssertThat(insideMarket).IsTrue();
        Assertions.AssertThat(outsideMarket).IsFalse();
    }

    /// <summary>
    /// 事实：<b>试炼塔</b>（影响区 0×0）<b>不会</b>放开摆摊范围。
    /// </summary>
    [TestCase]
    public void Today_TheTrialTower_OpensUpNothing()
    {
        var village = NewVillageWithOnly("trial_tower", 0, 0, 0, 0);

        bool anywhere = CanPlaceStall(village, 0, 0);

        Assertions.AssertThat(anywhere).IsFalse();
    }

    /// <summary>
    /// <b>D-098 ⑤</b>：<b>建筑落点合法性用的是同一把尺子</b>
    /// ⇒ 防护罩<b>既不</b>放开摆摊、<b>也不</b>扩大可建范围。
    /// </summary>
    /// <remarks>
    /// ⚠️ <b>为什么这条必须存在</b>：D-093 原话是「落在村子的地盘里
    /// ⇒ <b>与摆摊用的是同一把尺子</b>」⇒ 若只改摆摊那一处、落点那一处没改
    /// ⇒ 玩家能靠防护罩在村外盖楼、却不能在那儿摆摊 ⇒ <b>两把尺子</b>
    /// ⇒ 「同一把尺子」这句话就是假的。
    /// <para>⚠️ 这条断言<b>第一版是绿的</b>（守卫当时是装饰品）⇒ 补了它才抓住。</para>
    /// </remarks>
    [TestCase]
    public void TheShield_DoesNotOpenUpBuildableLandEither()
    {
        // 村里只有一座防护罩（9×9 落在 (0,0)）⇒ 它**不是**范围类。
        var village = NewVillageWithOnly("shield", 0, 0, 9, 9);
        var service = new BuildingService(village, GoldResource, new ResourceId("材料"));

        bool insideShield = service.CanPlaceAt(BuildableBuildings.Fence, 2, 2);
        bool outsideShield = service.CanPlaceAt(BuildableBuildings.Fence, 9, 9);

        // ⚠️ **两个都拒**：防护罩不扩大村子的地盘。
        Assertions.AssertThat(insideShield).IsFalse();
        Assertions.AssertThat(outsideShield).IsFalse();
    }

    /// <summary>
    /// <b>D-098 ⑤ 配套</b>：<b>市集</b>仍然界定村子的地盘
    /// ⇒ 玩家在村子里能盖楼（否则整局游戏没法玩）。
    /// </summary>
    [TestCase]
    public void TheMarket_StillDefinesTheBuildableLand()
    {
        var village = NewVillageWithOnly(RangeBuildings.MarketId, -5, -5, 11, 11);
        var service = new BuildingService(village, GoldResource, new ResourceId("材料"));

        bool insideMarket = service.CanPlaceAt(BuildableBuildings.Fence, 0, 0);
        bool outsideMarket = service.CanPlaceAt(BuildableBuildings.Fence, 9, 9);

        Assertions.AssertThat(insideMarket).IsTrue();
        Assertions.AssertThat(outsideMarket).IsFalse();
    }

}
