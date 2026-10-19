using GdUnit4;
using ClaudeCodeGameStudios.Domain.Heroes;

namespace ClaudeCodeGameStudios.Tests.Heroes;

/// <summary>
/// <see cref="HeroAgent.AssignId"/> 单元测试（M7 / D-030 兑现 M2 挂账）。
///
/// <para><b>本文档保护：</b></para>
/// <list type="bullet">
/// <item>新建英雄 <see cref="HeroAgent.Id"/> = <see cref="HeroId.None"/>（未设）。</item>
/// <item>首次 <see cref="HeroAgent.AssignId"/> 设上 ID。</item>
/// <item>再次 <see cref="HeroAgent.AssignId"/> 抛 <see cref="System.InvalidOperationException"/>（防覆盖）。</item>
/// <item>传 <see cref="HeroId.None"/> / 空字符串 HeroId 抛 <see cref="System.ArgumentNullException"/>。</item>
/// </list>
/// </summary>
[TestSuite]
public class HeroAgentIdTests
{
    [TestCase]
    public void NewHero_IdIsNone()
    {
        var h = new HeroAgent { Name = "Test" };
        Assertions.AssertThat(h.Id).IsEqual(HeroId.None);
    }

    [TestCase]
    public void AssignId_Once_SetsIt()
    {
        var h = new HeroAgent { Name = "Test" };
        h.AssignId(new HeroId("h-alice"));
        Assertions.AssertThat(h.Id.Value).IsEqual("h-alice");
    }

    [TestCase]
    public void AssignId_Twice_Throws()
    {
        var h = new HeroAgent { Name = "Test" };
        h.AssignId(new HeroId("h-alice"));
        bool threw = false;
        try { h.AssignId(new HeroId("h-bob")); }
        catch (System.InvalidOperationException) { threw = true; }
        Assertions.AssertThat(threw).IsTrue();
        // 第一次设的还在
        Assertions.AssertThat(h.Id.Value).IsEqual("h-alice");
    }

    [TestCase]
    public void AssignId_Empty_Throws()
    {
        var h = new HeroAgent { Name = "Test" };
        bool threw = false;
        try { h.AssignId(new HeroId("")); }
        catch (System.ArgumentNullException) { threw = true; }
        Assertions.AssertThat(threw).IsTrue();
    }

    [TestCase]
    public void AssignId_NoneHeroId_Throws()
    {
        var h = new HeroAgent { Name = "Test" };
        bool threw = false;
        try { h.AssignId(HeroId.None); }
        catch (System.ArgumentNullException) { threw = true; }
        Assertions.AssertThat(threw).IsTrue();
    }
}
