using System.Collections.Generic;
using GdUnit4;
using ClaudeCodeGameStudios.Domain.Heroes;
using ClaudeCodeGameStudios.Domain.Relationships;
using ClaudeCodeGameStudios.Presentation;

/// <summary>
/// 第三阶段 M6：右栏关系摘要的行文本（纯函数）。
/// </summary>
/// <remarks>
/// <para><b>依据：</b><c>07</c> D-036 / P3-D11：只读展示、数据来自领域快照、
/// 不在 UI 里维护独立关系副本。</para>
/// <para>行格式：<c>A → B +12</c>；按关系绝对值降序，同强度时按名字稳定排序。</para>
/// </remarks>
[TestSuite]
public class RelationshipPresenterTests
{
    private static HeroId H(string id) => new(id);

    private static RelationshipRecord Rec(string from, string to, int value)
        => new(new RelationshipKey(H(from), H(to)), value);

    private static Dictionary<HeroId, string> Names() => new()
    {
        [H("h-alice")] = "Alice",
        [H("h-bob")] = "Bob",
        [H("h-carol")] = "Carol",
        [H("h-dave")] = "Dave",
    };

    // ── 空状态 ────────────────────────────────────────────────────────────

    [TestCase]
    public void NoRelationships_ShowsPlaceholder()
    {
        var lines = RelationshipPresenter.Lines(
            new List<RelationshipRecord>(), Names());

        Assertions.AssertThat(lines.Count).IsEqual(1);
        Assertions.AssertThat(lines[0]).IsEqual(RelationshipPresenter.EmptyText);
    }

    // ── 排序与格式 ────────────────────────────────────────────────────────

    /// <summary>按关系强度（绝对值）降序 —— 最亲近 / 最疏远的先被看到。</summary>
    [TestCase]
    public void Lines_AreSortedByAbsoluteValue()
    {
        var records = new List<RelationshipRecord>
        {
            Rec("h-alice", "h-bob", 5),
            Rec("h-carol", "h-dave", -30),
            Rec("h-bob", "h-carol", 20),
        };

        var lines = RelationshipPresenter.Lines(records, Names());

        Assertions.AssertThat(lines.Count).IsEqual(3);
        Assertions.AssertThat(lines[0]).IsEqual("Carol → Dave  -30");
        Assertions.AssertThat(lines[1]).IsEqual("Bob → Carol  +20");
        Assertions.AssertThat(lines[2]).IsEqual("Alice → Bob  +5");
    }

    /// <summary>同强度时按名字稳定排序（同一份数据每次渲染顺序一致）。</summary>
    [TestCase]
    public void Lines_UseStableTieBreak()
    {
        var records = new List<RelationshipRecord>
        {
            Rec("h-bob", "h-carol", 10),
            Rec("h-alice", "h-dave", 10),
        };

        var lines = RelationshipPresenter.Lines(records, Names());

        Assertions.AssertThat(lines[0]).IsEqual("Alice → Dave  +10");
        Assertions.AssertThat(lines[1]).IsEqual("Bob → Carol  +10");
    }

    /// <summary>名字表缺项时回落为英雄 ID，而不是崩掉或显示空白。</summary>
    [TestCase]
    public void Lines_FallBackToHeroIds()
    {
        var lines = RelationshipPresenter.Lines(
            new List<RelationshipRecord> { Rec("h-x", "h-y", 2) },
            new Dictionary<HeroId, string>());

        Assertions.AssertThat(lines[0]).IsEqual("h-x → h-y  +2");
    }

    /// <summary>行数上限生效：4 行以上只显示前 4 条。</summary>
    [TestCase]
    public void Lines_RespectMaxLines()
    {
        var records = new List<RelationshipRecord>();
        for (int i = 0; i < 8; i++)
        {
            records.Add(Rec("h-a", $"h-{i}", i + 1));
        }

        var lines = RelationshipPresenter.Lines(records, Names());

        Assertions.AssertThat(lines.Count).IsEqual(RelationshipPresenter.MaxLines);
    }

    /// <summary>值 0 的关系不显示（没有发生过任何变化的隐含关系不是「关系」）。</summary>
    [TestCase]
    public void Lines_SkipZeroValues()
    {
        var lines = RelationshipPresenter.Lines(
            new List<RelationshipRecord> { Rec("h-alice", "h-bob", 0) }, Names());

        Assertions.AssertThat(lines.Count).IsEqual(1);
        Assertions.AssertThat(lines[0]).IsEqual(RelationshipPresenter.EmptyText);
    }
}
