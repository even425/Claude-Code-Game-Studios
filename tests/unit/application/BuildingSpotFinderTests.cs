using System;
using System.Collections.Generic;
using GdUnit4;
using ClaudeCodeGameStudios.Application;
using ClaudeCodeGameStudios.Domain.Village;

namespace ClaudeCodeGameStudios.Tests.Application;

/// <summary>
/// <b>给建筑找落点</b>（D-122：采集器也要盖商店 ⇒ 它得自己找地方）。
/// </summary>
/// <remarks>
/// <para><b>它守的是四件事</b>：
/// <list type="number">
/// <item><b>默认落点合法时用它</b>（不绕远）。</item>
/// <item><b>默认落点被占时往外找</b>，且找到的是<b>合法</b>的落点。</item>
/// <item><b>找不到就返回 null</b>（不瞎报一个位置）。</item>
/// <item><b>找法是确定的</b>：同样的输入 ⇒ 同样的落点（标定数据才可比）。</item>
/// </list></para>
/// </remarks>
[TestSuite]
public class BuildingSpotFinderTests
{
    private static readonly BuildableBuilding Shop = BuildableBuildings.WeaponShop;

    /// <summary>① 默认落点合法时用它。</summary>
    [TestCase]
    public void TheDefaultSpot_IsUsed_WhenItIsLegal()
    {
        var spot = BuildingSpotFinder.Find(Shop, (x, y) => x == Shop.GridX && y == Shop.GridY);

        Assertions.AssertThat(spot.HasValue).IsTrue();
        Assertions.AssertThat(spot!.Value.X).IsEqual(Shop.GridX);
        Assertions.AssertThat(spot.Value.Y).IsEqual(Shop.GridY);
    }

    /// <summary>② 默认落点被占 ⇒ 往外找，且找到的落点<b>真的合法</b>。</summary>
    [TestCase]
    public void WhenTheDefaultIsTaken_ItFindsAnotherLegalSpot()
    {
        var blocked = new HashSet<(int, int)> { (Shop.GridX, Shop.GridY) };

        var spot = BuildingSpotFinder.Find(
            Shop,
            (x, y) => !blocked.Contains((x, y)));

        Assertions.AssertThat(spot.HasValue).IsTrue();
        Assertions.AssertThat(blocked.Contains((spot!.Value.X, spot.Value.Y))).IsFalse();
    }

    /// <summary>③ 找不到 ⇒ 返回 null。</summary>
    [TestCase]
    public void WhenNothingIsLegal_ItReturnsNull()
    {
        var spot = BuildingSpotFinder.Find(Shop, (x, y) => false);

        Assertions.AssertThat(spot.HasValue).IsFalse();
    }

    /// <summary>④ <b>找法是确定的</b>（同样的输入 ⇒ 同样的落点）。</summary>
    [TestCase]
    public void TheSameInput_GivesTheSameSpot()
    {
        var blocked = new HashSet<(int, int)> { (Shop.GridX, Shop.GridY), (Shop.GridX, Shop.GridY - 1) };

        var first = BuildingSpotFinder.Find(Shop, (x, y) => !blocked.Contains((x, y)));
        var second = BuildingSpotFinder.Find(Shop, (x, y) => !blocked.Contains((x, y)));

        Assertions.AssertThat(first.HasValue).IsTrue();
        Assertions.AssertThat(first!.Value.X).IsEqual(second!.Value.X);
        Assertions.AssertThat(first.Value.Y).IsEqual(second.Value.Y);
    }
}
