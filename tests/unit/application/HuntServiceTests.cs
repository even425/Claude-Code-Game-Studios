using System.Collections.Generic;
using GdUnit4;
using ClaudeCodeGameStudios.Application;
using ClaudeCodeGameStudios.Domain.Combat;
using ClaudeCodeGameStudios.Domain.Heroes;
using ClaudeCodeGameStudios.Domain.Village;
using ClaudeCodeGameStudios.Domain.Wildlife;

namespace ClaudeCodeGameStudios.Tests.Application;

/// <summary>
/// <b>狩猎</b>（D-118）—— 英雄出村打一只野兽。
/// </summary>
/// <remarks>
/// <para><b>它守的是六件事</b>：
/// <list type="number">
/// <item>野外没有野兽 ⇒ 白跑一趟（**不是**「打输了」⇒ 两者必须能分开）。</item>
/// <item>打赢 ⇒ 拿到战利品 ⇒ **而且那只野兽被移走了**（被猎到了）。</item>
/// <item>打输 ⇒ <b>没有</b>战利品，<b>而且野兽还在</b>（它跑了 ⇒ 明天还能猎）。</item>
/// <item>战利品按种类不同（鹿给肉多、狼给皮多、野猪两样各一）。</item>
/// <item>狼是<b>精英档</b>（普通人打不赢）⇒ 强的人打得过、弱的人打不过是<b>设计</b>。</item>
/// <item>猎物是<b>离村子最近</b>的那只（找目标与打猎必须是同一只）。</item>
/// </list></para>
/// </remarks>
[TestSuite]
public class HuntServiceTests
{
    private static readonly HeroId Alice = new("h-alice");

    private static System.Func<float> Constant(float v) => () => v;

    /// <summary>造一只野兽（放在指定的村外格位）。</summary>
    private static WildBeast Beast(BeastKind kind, int x, int y)
        => new($"b-{kind}-{x}-{y}", kind, x, y);

    /// <summary>
    /// 造一套「野兽簿 + 狩猎服务」。
    /// </summary>
    /// <param name="beasts">野外有哪些野兽。</param>
    /// <param name="stats">猎人的战斗属性。</param>
    /// <param name="loot">收到的战利品（记录下来）。</param>
    /// <param name="service">造好的狩猎服务。</param>
    /// <param name="book">野兽簿（用来验野兽还在不在）。</param>
    private static (HuntService Service, WildlifeBook Book, List<IReadOnlyDictionary<string, long>> Loot)
        MakeService(
            IReadOnlyList<WildBeast> beasts,
            CombatStats? stats = null,
            string name = "甲")
    {
        var book = new WildlifeBook();
        foreach (var b in beasts) book.Add(b);

        var loot = new List<IReadOnlyDictionary<string, long>>();
        var combat = new FormulaCombat();
        var service = new HuntService(
            book,
            combat,
            combat,
            id => (name, stats ?? CombatParameters.ForHero("莽", "肝")),
            (id, l) => loot.Add(l));

        return (service, book, loot);
    }

    /// <summary>① 野外没有野兽 ⇒ 白跑一趟（**不是**「打输了」）。</summary>
    [TestCase]
    public void NoBeastsOutside_NothingHappens()
    {
        var (service, _, loot) = MakeService(new List<WildBeast>());

        var outcome = service.Try(Alice);

        Assertions.AssertThat(outcome.Beast).IsNull();
        Assertions.AssertThat(outcome.Won).IsFalse();
        Assertions.AssertThat(loot.Count).IsEqual(0);
    }

    /// <summary>② 打赢 ⇒ 有战利品，且<b>那只野兽被移走</b>。</summary>
    [TestCase]
    public void Winning_GivesLoot_AndRemovesTheBeast()
    {
        var (service, book, loot) = MakeService(
            new List<WildBeast> { Beast(BeastKind.Boar, 20, 12) });

        var outcome = service.Try(Alice);

        Assertions.AssertThat(outcome.Won).IsTrue();
        Assertions.AssertThat(loot.Count).IsEqual(1);
        Assertions.AssertThat(book.Beasts.Count).IsEqual(0);
    }

    /// <summary>③ 打输 ⇒ 没有战利品，<b>野兽还在</b>（它跑了）。</summary>
    [TestCase]
    public void Losing_GivesNoLoot_AndTheBeastStays()
    {
        // 挂机（弱）打精英档的狼 ⇒ 打不赢。
        var (service, book, loot) = MakeService(
            new List<WildBeast> { Beast(BeastKind.Wolf, 20, 12) },
            stats: CombatParameters.ForHero("傲", "挂机"));

        var outcome = service.Try(Alice);

        Assertions.AssertThat(outcome.Won).IsFalse();
        Assertions.AssertThat(loot.Count).IsEqual(0);
        Assertions.AssertThat(book.Beasts.Count).IsEqual(1);
    }

    /// <summary>④ 战利品按种类不同（鹿肉多、狼皮多、野猪两样各一）。</summary>
    [TestCase]
    public void LootDependsOnTheBeastKind()
    {
        var (deer, _, deerLoot) = MakeService(
            new List<WildBeast> { Beast(BeastKind.Deer, 20, 12) });
        deer.Try(Alice);
        Assertions.AssertThat(deerLoot[0]["兽肉"]).IsEqual(2L);

        var (wolf, _, wolfLoot) = MakeService(
            new List<WildBeast> { Beast(BeastKind.Wolf, 20, 12) });
        wolf.Try(Alice);
        Assertions.AssertThat(wolfLoot[0]["兽皮"]).IsEqual(2L);
    }

    /// <summary>⑤ 狼是<b>精英档</b>：强的人打得过、弱的人打不赢。</summary>
    [TestCase]
    public void WolfIsHarderThanBoar()
    {
        var strong = CombatParameters.ForHero("莽", "肝");
        var weak = CombatParameters.ForHero("傲", "挂机");

        var (strongService, _, _) = MakeService(
            new List<WildBeast> { Beast(BeastKind.Wolf, 20, 12) }, stats: strong);
        var (weakService, _, _) = MakeService(
            new List<WildBeast> { Beast(BeastKind.Wolf, 20, 12) }, stats: weak);

        Assertions.AssertThat(strongService.Try(Alice).Won).IsTrue();
        Assertions.AssertThat(weakService.Try(Alice).Won).IsFalse();
    }

    /// <summary>⑥ 猎物是<b>离村子最近</b>的那只（找目标与打猎必须是同一只）。</summary>
    [TestCase]
    public void FindsTheNearestBeast()
    {
        var far = Beast(BeastKind.Boar, 20, 14);
        var near = Beast(BeastKind.Deer, 16, 6);
        var (service, _, loot) = MakeService(new List<WildBeast> { far, near });

        var outcome = service.Try(Alice);

        Assertions.AssertThat(outcome.Beast).IsNotNull();
        Assertions.AssertThat(outcome.Beast!.Id).IsEqual(near.Id);
        // 鹿给肉更多 ⇒ 战利品能证明打的确实是它。
        Assertions.AssertThat(loot[0]["兽肉"]).IsEqual(2L);
    }
}
