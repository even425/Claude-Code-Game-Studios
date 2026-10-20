using System;
using System.Collections.Generic;
using System.Linq;
using GdUnit4;
using ClaudeCodeGameStudios.Application;

namespace ClaudeCodeGameStudios.Tests;

/// <summary>
/// <b>IT-25（不变量 I1）</b>：同一时刻一个 <c>Id</c> 对应<b>且只对应</b>一个英雄实体。
///
/// <para><b>它守的是什么</b>：英雄 Id 是「跨实体引用一律用 ID」（基线 §5.1）的锚点 ——
/// 摊位的 OwnerId、结算的幂等键、存档里的英雄条目，全靠它指人。
/// 两个英雄同号，后果不是「界面显示重名」，
/// 而是<b>引用指错人</b>：A 的货记到 B 头上、读档时两个状态映射到同一个实体。</para>
///
/// <para><b>为什么此前没有测试</b>：<c>HeroAgentIdTests</c> 守的是「一个英雄不能被改两次号」
/// （赋值机制），缺的是<b>花名册层面不许有重复号</b>。而重复号<b>不是</b>赋值机制能挡的：
/// 两个<b>不同</b>的 <c>HeroAgent</c> 各自被合法地赋了一次号，号却撞了。</para>
///
/// <para><b>它守的具体形状</b>：Id 由 <see cref="HeroIdAllocator"/> 分配。
/// 旧写法是 <c>$\"h-{_heroes.Count}\"</c>（用<b>当前人数</b>当序号）——
/// 人数当序号，在花名册<b>连续</b>时恰好是「下一个号」，
/// 但花名册<b>缺一号</b>时就会撞号（读档后花名册是 <c>h-0 / h-2</c>，
/// 下一个就拿到 <c>h-2</c>）。这条断言就是钉住那个洞。</para>
///
/// <para><b>本文件不发明任何玩法数值</b>。</para>
/// </summary>
[TestSuite]
public class HeroIdUniquenessTests
{
    /// <summary>空花名册 ⇒ 第一个号是 <c>h-0</c>（与旧写法一致 ⇒ 存档兼容）。</summary>
    [TestCase]
    public void IT25_EmptyRoster_FirstIdIsH0()
    {
        var alloc = new HeroIdAllocator();

        Assertions.AssertThat(alloc.Next()).IsEqual("h-0");
    }

    /// <summary>
    /// 花名册<b>连续</b>时，分配结果与旧的「按人数算」<b>逐个相同</b>。
    /// <para>（这条是<b>存档兼容</b>的保证：已有的存档里是 h-0/h-1/h-2，
    /// 读档后继续召唤必须还是 h-3 —— 否则存档里的号与新号会乱。）</para>
    /// </summary>
    [TestCase]
    public void IT25_ContiguousRoster_KeepsTheOldNumbering()
    {
        var alloc = new HeroIdAllocator(new[] { "h-0", "h-1", "h-2" });

        Assertions.AssertThat(alloc.Next()).IsEqual("h-3");
    }

    /// <summary>
    /// 花名册<b>缺号</b>时，下一个号<b>必须跳过已占用的号</b>。
    /// <para>这一条正是旧写法的洞：人数是 2 ⇒ 旧写法给出 <c>h-2</c>，
    /// 而 <c>h-2</c> <b>已经有人了</b> ⇒ 两个英雄同号。</para>
    /// </summary>
    [TestCase]
    public void IT25_RosterWithAGap_NeverReusesAnOccupiedId()
    {
        var alloc = new HeroIdAllocator(new[] { "h-0", "h-2" });

        string next = alloc.Next();

        Assertions.AssertThat(next).IsNotEqual("h-2");
        Assertions.AssertThat(next).IsNotEqual("h-0");
        Assertions.AssertThat(next).IsEqual("h-3");
    }

    /// <summary>连续分配 N 个号 ⇒ <b>互不相同</b>。</summary>
    [TestCase]
    public void IT25_ConsecutiveAllocations_AreAllDistinct()
    {
        var alloc = new HeroIdAllocator();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        for (int i = 0; i < 50; i++)
        {
            Assertions.AssertThat(seen.Add(alloc.Next())).IsTrue();
        }
    }

    /// <summary>
    /// 花名册里<b>不许</b>出现两个同号的英雄 —— I1 的正面表述。
    /// <para>（用分配器把一整个花名册建出来，逐号查重。）</para>
    /// </summary>
    [TestCase]
    public void IT25_NoTwoHeroesShareTheSameId()
    {
        // 先按「有洞」的历史情形建一个花名册，再继续召唤
        var alloc = new HeroIdAllocator(new[] { "h-0", "h-2" });
        var roster = new List<string> { "h-0", "h-2" };

        for (int i = 0; i < 10; i++) roster.Add(alloc.Next());

        var dupes = roster
            .GroupBy(id => id, StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        Assertions.AssertThat(string.Join(" | ", dupes)).IsEmpty();
        Assertions.AssertThat(roster.Distinct(StringComparer.Ordinal).Count()).IsEqual(roster.Count);
    }

    /// <summary>
    /// 分配器<b>不认识</b>的号（不是 <c>h-N</c> 形式）也必须被避开。
    /// <para>（比如有人手工塞过一个 <c>hero-x</c>：它不参与「最大序号」的计算，
    /// 但仍必须占住那个号本身。）</para>
    /// </summary>
    [TestCase]
    public void IT25_UnknownIdFormat_IsStillAvoided()
    {
        var alloc = new HeroIdAllocator(new[] { "hero-x", "h-0" });

        var taken = new HashSet<string>(new[] { "hero-x", "h-0" }, StringComparer.Ordinal);
        for (int i = 0; i < 5; i++)
        {
            string id = alloc.Next();
            Assertions.AssertThat(taken.Add(id)).IsTrue();
        }
    }
}
