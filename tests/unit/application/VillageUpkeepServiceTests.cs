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

    /// <summary>① 只扣闲钱：<b>升级储备不许动</b>。</summary>
    [TestCase]
    public void OnlyTheSpareMoneyIsCharged()
    {
        var village = MakeVillage(100L);

        long charged = VillageUpkeepService.Charge(village, Gold, reserveGold: 60L, amountPerDay: 10L);

        // 闲钱 = 100 − 60 = 40 ⇒ 扣 10 ⇒ 剩 90（其中 60 仍是升级储备）。
        Assertions.AssertThat(charged).IsEqual(10L);
        Assertions.AssertThat(village.Get(Gold)).IsEqual(90L);
    }

    /// <summary>② 闲钱不足 ⇒ <b>只扣闲钱</b>（升级储备不动）。</summary>
    [TestCase]
    public void WhenTheSpareIsNotEnough_ItOnlyTakesTheSpare()
    {
        var village = MakeVillage(70L);

        long charged = VillageUpkeepService.Charge(village, Gold, reserveGold: 60L, amountPerDay: 20L);

        Assertions.AssertThat(charged).IsEqual(10L);   // 只扣 100-60 之外的那 10
        Assertions.AssertThat(village.Get(Gold)).IsEqual(60L);   // 储备分文未动
    }

    /// <summary>③ <b>扣不动就不扣</b>（不欠账）：现金 ≤ 储备 ⇒ 一分不扣。</summary>
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

    /// <summary>⑥ <b>默认不扣</b>（这条机制待开发者拍板 ⇒ 默认值必须等于「什么都不做」）。</summary>
    [TestCase]
    public void TheDefault_IsZero_MeaningNothingIsCharged()
    {
        Assertions.AssertThat(VillageUpkeepService.DefaultUpkeepPerDay).IsEqual(0L);
    }
}
