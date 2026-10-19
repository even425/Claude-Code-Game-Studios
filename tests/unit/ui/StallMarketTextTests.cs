using System;
using GdUnit4;
using ClaudeCodeGameStudios.Application.Stall;
using ClaudeCodeGameStudios.Domain.Heroes;
using ClaudeCodeGameStudios.Domain.Items;
using ClaudeCodeGameStudios.Presentation;
using DomainStall = ClaudeCodeGameStudios.Domain.Stall.Stall;
using DomainStallStatus = ClaudeCodeGameStudios.Domain.Stall.StallStatus;

namespace ClaudeCodeGameStudios.Tests.UI;

/// <summary>
/// 摊位面板文案的契约（R-02 · R-04）。
/// </summary>
/// <remarks>
/// <para><b>为什么逐字测</b>：这些行是玩家核对「谁在卖、卖什么、剩多少、多少钱、
/// 这个人能买几份」的唯一入口。格式一变，玩家就少一个可核对的事实 ——
/// 和 <c>TradeFeedback</c>（M5）同一个理由。</para>
/// </remarks>
[TestSuite]
public class StallMarketTextTests
{
    private static readonly ItemId Stone = new("石料");

    private static DomainStall Stall(
        long stock = 3, long unitPrice = 5, DomainStallStatus status = DomainStallStatus.Open)
        // 库存 0 是**派生状态**（售罄），公开构造拒它 —— 只能走 Restore 那条路。
        // 这正是真实世界的来路：开摊时有货，卖完了才是 0。
        => stock == 0
            ? DomainStall.Restore(new HeroId("h-0"), Stone, 0, unitPrice, status, simTime: 0)
            : new DomainStall(new HeroId("h-0"), Stone, stock, unitPrice, status, simTime: 0);

    [TestCase]
    public void StallLine_OpenStall_ShowsSellerItemStockAndPrice()
    {
        string line = StallMarketText.StallLine("瓦德", Stall());

        Assertions.AssertThat(line).IsEqual("瓦德 · 石料 ×3 · 单价 5");
    }

    [TestCase]
    public void StallLine_SoldOutStall_MarksSoldOut()
    {
        string line = StallMarketText.StallLine(
            "瓦德", Stall(stock: 0, status: DomainStallStatus.SoldOut));

        Assertions.AssertThat(line).IsEqual("瓦德 · 石料 ×0 · （已售罄）");
    }

    [TestCase]
    public void StallLine_UnknownSeller_FallsBackToId()
    {
        string line = StallMarketText.StallLine("", Stall());

        Assertions.AssertThat(line).IsEqual("h-0 · 石料 ×3 · 单价 5");
    }

    [TestCase]
    public void BuyerLine_ShowsNameGoldAndMaxQty()
    {
        string line = StallMarketText.BuyerLine(new BuyerOffer(new HeroId("h-1"), "灰七", 95, 19));

        Assertions.AssertThat(line).IsEqual("灰七 · 金95 · 可买 19 份");
    }

    [TestCase]
    public void Bought_Line_ShowsQtyAndAmount()
    {
        string line = StallMarketText.Bought("灰七", "石料", 19, 95);

        Assertions.AssertThat(line).IsEqual("灰七 买走 石料×19，付 95 金币。");
    }

    /// <summary>每个失败分类都要有中文说法，不能落到「未知结果」。</summary>
    [TestCase]
    public void Rejected_EveryOutcomeHasText()
    {
        foreach (StallService.BuyOutcome outcome in
                 Enum.GetValues(typeof(StallService.BuyOutcome)))
        {
            string text = StallMarketText.Rejected(outcome);

            Assertions.AssertThat(string.IsNullOrWhiteSpace(text)).IsFalse();
            Assertions.AssertThat(text).IsNotEqual("没买成（未知结果）。");
        }
    }

    [TestCase]
    public void Rejected_UnknownOutcome_SaysUnknownInsteadOfSilence()
    {
        string text = StallMarketText.Rejected((StallService.BuyOutcome)999);

        Assertions.AssertThat(text).IsEqual("没买成（未知结果）。");
    }
}
