using System.Collections.Generic;
using System.Linq;

namespace ClaudeCodeGameStudios.Presentation;

/// <summary>
/// 花名册的行文本 —— <b>纯函数，不碰任何 Node</b>。
/// </summary>
/// <remarks>
/// <para><b>为什么单独拎出来</b>：2026-10-19 的「37 人在村里、花名册只有 14 个名字」
/// 根因之一是 <c>Main.Refresh()</c> 里写死的 <c>_heroes.Take(14)</c>。
/// 那行代码编译得过、自检也跑得过 —— 因为它只是少显示几个人，不报错。
/// 把「行文本怎么来」收进一个可被测试直接调用的纯函数，才能用一条
/// <c>37 人 → 37 行</c> 的断言把这类截断钉死。</para>
/// <para><b>它不管什么</b>：不判断谁能进队（那是 <c>SquadRules</c> 的事），
/// 也不创建 <c>ItemList</c>（节点的生命周期归场景）。它只回答
/// 「按现在的名册和队伍，这 37 行字应该长什么样」。</para>
/// </remarks>
public static class Roster
{
    /// <summary>一个人都没有时的唯一一行。</summary>
    public const string EmptyRow = "（还没召唤）";

    /// <summary>已编入队伍的行首标记。</summary>
    public const string InSquadMark = "▸";

    /// <summary>未编入队伍的行首占位 —— 让两行名字左端对齐，不出现锯齿。</summary>
    public const string NotInSquadMark = " ";

    /// <summary>
    /// 按英雄顺序生成花名册的每一行。
    /// </summary>
    /// <remarks>
    /// <b>不截断。</b>行序与 <paramref name="heroes"/> 严格一一对应 ——
    /// <c>Main.OnRosterItemActivated(index)</c> 直接拿行号当英雄下标用，
    /// 这里一旦过滤或重排，玩家点到的就是别人。由 <c>RosterTests</c> 守住。
    /// </remarks>
    /// <param name="heroes">全部英雄，顺序即显示顺序。</param>
    /// <param name="squad">当前队伍，用来决定行首标记。</param>
    /// <returns>行文本；为空名册时返回只含 <see cref="EmptyRow"/> 的一行。</returns>
    public static List<string> Lines(
        IReadOnlyList<HeroAgent> heroes, IReadOnlyCollection<HeroAgent> squad)
    {
        var lines = new List<string>(heroes.Count + 1);

        foreach (var hero in heroes)
        {
            string mark = squad.Contains(hero) ? InSquadMark : NotInSquadMark;
            lines.Add($"{mark} {hero.Name}");
        }

        if (lines.Count == 0) lines.Add(EmptyRow);
        return lines;
    }

    /// <summary>
    /// 两份行文本是否完全相同。
    /// </summary>
    /// <remarks>
    /// 用来决定「要不要重建 <c>ItemList</c>」。重建不是免费的：
    /// <c>Clear()</c> 会把<b>滚动位置与选中项一起复位</b> ——
    /// 每帧重建一次，玩家永远拖不动滚动条（2026-10-19 的第二个根因）。
    /// </remarks>
    /// <param name="left">旧行文本。</param>
    /// <param name="right">新行文本。</param>
    /// <returns>完全相同为 true。</returns>
    public static bool SameLines(IReadOnlyList<string> left, IReadOnlyList<string> right)
    {
        if (ReferenceEquals(left, right)) return true;
        if (left.Count != right.Count) return false;

        for (int i = 0; i < left.Count; i++)
        {
            if (left[i] != right[i]) return false;
        }

        return true;
    }
}
