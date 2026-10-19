using System.Collections.Generic;
using GdUnit4;
using ClaudeCodeGameStudios.Presentation;

/// <summary>
/// 花名册行文本的契约 —— 守住 2026-10-19 的「37 人在村里，花名册只显示 14 个姓名」。
/// </summary>
/// <remarks>
/// <para><b>原缺陷</b>：<c>Main.Refresh()</c> 里写死 <c>_heroes.Take(14)</c>。
/// 村里 37 个人，花名册只往 <c>ItemList</c> 里放 14 条 —— 滚动条只有 2 像素的可滚范围，
/// 所以「滚动条拖到底也看不到更多的人」。编译、自检、191 个单测全绿，因为
/// 少显示几个人不会报错，只有「行数必须等于人数」这条断言能看见它。</para>
/// <para><b>为什么测纯函数而不是直接测 <c>ItemList</c></b>：<c>ItemList</c> 是引擎节点，
/// 它的滚动位置在单测里无法可靠观察；而「截断」发生在本类里。
/// 行文本的<b>条数与顺序</b>才是点击索引能对上人的前提。</para>
/// </remarks>
[TestSuite]
public class RosterTests
{
    /// <summary>造 <paramref name="count"/> 个名字不重复的英雄。</summary>
    private static List<HeroAgent> Heroes(int count)
    {
        var heroes = new List<HeroAgent>(count);
        for (int i = 0; i < count; i++)
        {
            // 名字必须唯一：下面要用「第 i 行含第 i 个名字」验行序与下标对齐。
            heroes.Add(new HeroAgent { Name = $"英雄{i:00}" });
        }

        return heroes;
    }

    [TestCase]
    public void EmptyRoster_ShowsOnePlaceholderRow()
    {
        var lines = Roster.Lines(new List<HeroAgent>(), new List<HeroAgent>());

        Assertions.AssertThat(lines.Count).IsEqual(1);
        Assertions.AssertThat(lines[0]).IsEqual(Roster.EmptyRow);
    }

    /// <summary>
    /// 核心断言：<b>37 个人必须有 37 行</b>。旧实现这里是 14。
    /// </summary>
    [TestCase]
    public void ThirtySevenHeroes_ProduceThirtySevenRows_NotTruncated()
    {
        var heroes = Heroes(37);

        var lines = Roster.Lines(heroes, new List<HeroAgent>());

        Assertions.AssertThat(lines.Count).IsEqual(37);
        Assertions.AssertThat(lines[0].Trim()).IsEqual("英雄00");
        Assertions.AssertThat(lines[36].Trim()).IsEqual("英雄36");
    }

    /// <summary>
    /// 行序 = 英雄顺序。点击索引直接当英雄下标用（<c>OnRosterItemActivated</c>），
    /// 这里一旦过滤或重排，玩家就会点到别人。
    /// </summary>
    [TestCase]
    public void RowOrder_MatchesHeroOrder_ForClickIndexMapping()
    {
        var heroes = Heroes(37);
        var lines = Roster.Lines(heroes, new List<HeroAgent>());

        for (int i = 0; i < heroes.Count; i++)
        {
            Assertions.AssertThat(lines[i].Contains(heroes[i].Name)).IsTrue();
        }
    }

    /// <summary>队伍成员行首是 <c>▸</c>，其余是空格 —— 两行名字左端对齐。</summary>
    [TestCase]
    public void SquadMembers_AreMarked_OthersKeepAlignment()
    {
        var heroes = Heroes(5);
        var squad = new List<HeroAgent> { heroes[0], heroes[3] };

        var lines = Roster.Lines(heroes, squad);

        Assertions.AssertThat(lines[0].StartsWith(Roster.InSquadMark)).IsTrue();
        Assertions.AssertThat(lines[3].StartsWith(Roster.InSquadMark)).IsTrue();
        Assertions.AssertThat(lines[1].StartsWith(Roster.NotInSquadMark)).IsTrue();
        Assertions.AssertThat(lines[2].StartsWith(Roster.NotInSquadMark)).IsTrue();
        Assertions.AssertThat(lines[4].StartsWith(Roster.NotInSquadMark)).IsTrue();
    }

    [TestCase]
    public void SameLines_DetectsRealChanges()
    {
        var a = new[] { "▸ 甲", "  乙" };
        var b = new[] { "▸ 甲", "  乙" };
        var squadChanged = new[] { "  甲", "  乙" };
        var summoned = new[] { "▸ 甲", "  乙", "  丙" };

        Assertions.AssertThat(Roster.SameLines(a, b)).IsTrue();
        Assertions.AssertThat(Roster.SameLines(a, squadChanged)).IsFalse();
        Assertions.AssertThat(Roster.SameLines(a, summoned)).IsFalse();
    }
}
