using GdUnit4;
using ClaudeCodeGameStudios.Presentation;

namespace ClaudeCodeGameStudios.Tests.UI;

/// <summary>
/// 成交杂谈文案的契约（第四阶段 M5）。
/// </summary>
/// <remarks>
/// 这条文案是玩家核对「钱去哪了」的入口：买家实付、摊主净得、村金库抽成。
/// 逐字断言，避免有人把金额从文本里删掉而测试仍然全绿。
/// </remarks>
[TestSuite]
public class TradeFeedbackTests
{
    [TestCase]
    public void NoVillageCut_ShowsAmountAndSellerNet()
    {
        string line = TradeFeedback.Line(
            buyer: "零散", seller: "纸伞", item: "石料", qty: 1,
            amount: 5, sellerNet: 5, villageCut: 0);

        Assertions.AssertThat(line)
            .IsEqual("零散从纸伞的摊上买走了石料×1，付5金币（纸伞得5）。");
    }

    [TestCase]
    public void WithVillageCut_ShowsAllThreeAmounts()
    {
        string line = TradeFeedback.Line(
            buyer: "零散", seller: "纸伞", item: "石料", qty: 2,
            amount: 210, sellerNet: 200, villageCut: 10);

        Assertions.AssertThat(line)
            .IsEqual("零散从纸伞的摊上买走了石料×2，付210金币（纸伞得200，村抽10）。");
    }

    [TestCase]
    public void WithVillageCut_DoesNotHideCut()
    {
        string line = TradeFeedback.Line("甲", "乙", "木料", 1, 105, 100, 5);

        Assertions.AssertThat(line.Contains("村抽5")).IsTrue();
        Assertions.AssertThat(line.Contains("付105金币")).IsTrue();
    }
}
