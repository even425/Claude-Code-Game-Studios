using GdUnit4;
using ClaudeCodeGameStudios.Application;
using ClaudeCodeGameStudios.Domain.Village;

namespace ClaudeCodeGameStudios.Tests.Application;

/// <summary>
/// <b>村庄日常开销</b>（D-125）—— 只扣闲钱、扣不动就不扣、必须记账。
/// </summary>
/// <remarks>
/// <para><b>它守的是四件事</b>：
/// <list type="number">
/// <item><b>只扣闲钱</b>：<b>不动</b>下一级村庄升级的储备（否则村庄永远升不上去）。</item>
/// <item><b>扣不动就不扣</b>（不欠账）：村库现金不足时只扣到 0。</item>
/// <item><b>扣款必须记账</b>（累计销毁）：否则钱凭空消失 ⇒ 守恒律破（实测 0/200）。</item>
/// <item><b>默认 0</b>（不扣）：这条机制**待开发者拍板** ⇒ 在拍板之前不许悄悄改玩家的钱。</item>
/// </list></para>
/// </remarks>
[TestSuite]
public class VillageUpkeepServiceTests
{
    private static readonly ResourceId Gold = new("金币");

    private static Village MakeVillage(long cash)
    {
        var village = new Village("v1");
        if (cash > 0) village.Add(Gold, cash);
        return village;
    }

    /// <summary>① 只扣闲钱：<b>两级升级储备不许动</b>（D-128）。</summary>
    [TestCase]
    public void OnlyTheSpareMoneyIsCharged()
    {
        var village = MakeVillage(200L);

        long charged = VillageUpkeepService.Charge(village, Gold, reserveGold: 60L, amountPerDay: 10L);

        // 闲钱 = 200 − 60×2 = 80 ⇒ 扣 10 ⇒ 剩 190（其中 120 是两级储备）。
        Assertions.AssertThat(charged).IsEqual(10L);
        Assertions.AssertThat(village.Get(Gold)).IsEqual(190L);
    }

    /// <summary>② 闲钱不足 ⇒ <b>只扣闲钱</b>（两级储备不动）。</summary>
    [TestCase]
    public void WhenTheSpareIsNotEnough_ItOnlyTakesTheSpare()
    {
        var village = MakeVillage(130L);

        long charged = VillageUpkeepService.Charge(village, Gold, reserveGold: 60L, amountPerDay: 20L);

        Assertions.AssertThat(charged).IsEqual(10L);   // 闲钱 = 130 − 120 = 10
        Assertions.AssertThat(village.Get(Gold)).IsEqual(120L);   // 两级储备分文未动
    }

    /// <summary>③ <b>扣不动就不扣</b>（不欠账）：现金 ≤ 两级储备 ⇒ 一分不扣。</summary>
    [TestCase]
    public void WhenThereIsNoSpareAtAll_ItChargesNothing()
    {
        var village = MakeVillage(50L);

        long charged = VillageUpkeepService.Charge(village, Gold, reserveGold: 60L, amountPerDay: 10L);

        Assertions.AssertThat(charged).IsEqual(0L);
        Assertions.AssertThat(village.Get(Gold)).IsEqual(50L);
    }

    /// <summary>④ 金额 ≤ 0 ⇒ <b>什么都不做</b>（默认 0 = 不扣）。</summary>
    [TestCase]
    public void ZeroOrNegative_ChargesNothing()
    {
        var village = MakeVillage(100L);

        long charged = VillageUpkeepService.Charge(village, Gold, reserveGold: 0L, amountPerDay: 0L);

        Assertions.AssertThat(charged).IsEqual(0L);
        Assertions.AssertThat(village.Get(Gold)).IsEqual(100L);
    }

    /// <summary>⑤ 扣掉的每一笔都<b>必须记账</b>（否则守恒律左边少一笔 ⇒ 钱凭空消失）。</summary>
    [TestCase]
    public void EveryCharge_IsRecorded()
    {
        VillageUpkeepService.ResetTotal();
        var village = MakeVillage(100L);

        VillageUpkeepService.Charge(village, Gold, reserveGold: 0L, amountPerDay: 7L);
        VillageUpkeepService.Charge(village, Gold, reserveGold: 0L, amountPerDay: 3L);

        Assertions.AssertThat(VillageUpkeepService.TotalUpkeepSpent).IsEqual(10L);

        VillageUpkeepService.ResetTotal();
        Assertions.AssertThat(VillageUpkeepService.TotalUpkeepSpent).IsEqual(0L);
    }

    /// <summary>⑥ <b>默认值 = 8 金/天</b>（D-126 开发者拍板；采集器与真实游戏**两边都扣**）。</summary>
    [TestCase]
    public void TheDefault_IsEight_PerDay()
    {
        Assertions.AssertThat(VillageUpkeepService.DefaultUpkeepPerDay).IsEqual(8L);
    }

    /// <summary>
    /// ⑦ 维护费<b>可以</b>随村庄层级递增（D-130 的机制）—— 但<b>默认不递增</b>。
    /// </summary>
    /// <remarks>
    /// ⚠️ **为什么默认是 0（不递增）**：D-130 实测递增（每级 +2）**反而让分布判据变红** ——
    /// 村库被抽干 ⇒ 收购站没有预算 ⇒ 村民卖不掉货 ⇒ 中位只剩 9 金、最富 441 金（6 倍集中）。
    /// ⇒ 机制保留（要开就改 <c>UpkeepStepPerTier</c> 一个常量），但**默认关闭**。
    /// </remarks>
    [TestCase]
    public void TheUpkeepCanGrowWithTheVillageTier_ButIsOffByDefault()
    {
        // 默认：所有层级都一样（D-126 拍板的固定 8 金）。
        Assertions.AssertThat(VillageUpkeepService.UpkeepStepPerTier).IsEqual(0L);
        Assertions.AssertThat(VillageUpkeepService.UpkeepForTier(0))
            .IsEqual(VillageUpkeepService.DefaultUpkeepPerDay);
        Assertions.AssertThat(VillageUpkeepService.UpkeepForTier(5))
            .IsEqual(VillageUpkeepService.DefaultUpkeepPerDay);
    }

    /// <summary>⑧ 层级为负 ⇒ 按 0 级算（不倒扣钱）。</summary>
    [TestCase]
    public void ANegativeTier_IsTreatedAsTierZero()
    {
        Assertions.AssertThat(VillageUpkeepService.UpkeepForTier(-3))
            .IsEqual(VillageUpkeepService.UpkeepForTier(0));
    }
}
