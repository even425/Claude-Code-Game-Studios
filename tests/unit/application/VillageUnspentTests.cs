using GdUnit4;
using ClaudeCodeGameStudios.Application;

namespace ClaudeCodeGameStudios.Tests.Application;

/// <summary>
/// <b>村庄「可花却没花」的钱</b>（D-110）—— 标定器不召唤 ⇒ 钱会囤，这个缺口要看得见。
/// </summary>
/// <remarks>
/// <para><b>它守的是四件事</b>：
/// <list type="number">
/// <item>口径 = 村库现金 + 村庄资产 − <b>下一级升级要留的钱</b>（留着的钱不算「没花」）。</item>
/// <item>已满级（保留金 = 0）⇒ 就是现金 + 资产。</item>
/// <item>留的钱超过现有现金 ⇒ 记 <b>0</b>（不是负数 —— 那只是「还没攒够」）。</item>
/// <item>结果<b>永远 ≥ 0</b>。</item>
/// </list></para>
/// </remarks>
[TestSuite]
public class VillageUnspentTests
{
    /// <summary>口径：现金 + 资产 − 保留金。</summary>
    [TestCase]
    public void Amount_IsCashPlusAssetMinusReserve()
    {
        long amount = VillageUnspent.Amount(villageGold: 300, asset: 200, reserveGold: 150);

        Assertions.AssertThat(amount).IsEqual(350L);
    }

    /// <summary>已满级 ⇒ 不用留钱 ⇒ 就是现金 + 资产。</summary>
    [TestCase]
    public void Amount_AtMaxTier_IsCashPlusAsset()
    {
        long amount = VillageUnspent.Amount(villageGold: 300, asset: 200, reserveGold: 0);

        Assertions.AssertThat(amount).IsEqual(500L);
    }

    /// <summary>留的钱超过现有现金 ⇒ 记 0（不是负数）。</summary>
    [TestCase]
    public void Amount_NeverNegative()
    {
        long amount = VillageUnspent.Amount(villageGold: 10, asset: 0, reserveGold: 250);

        Assertions.AssertThat(amount).IsEqual(0L);
    }

    /// <summary>一分不剩 ⇒ 0。</summary>
    [TestCase]
    public void Amount_ZeroWhenNothingIsLeft()
    {
        long amount = VillageUnspent.Amount(villageGold: 0, asset: 0, reserveGold: 0);

        Assertions.AssertThat(amount).IsEqual(0L);
    }
}
