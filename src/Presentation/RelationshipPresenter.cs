using System;
using System.Collections.Generic;
using ClaudeCodeGameStudios.Domain.Heroes;
using ClaudeCodeGameStudios.Domain.Relationships;

namespace ClaudeCodeGameStudios.Presentation;

/// <summary>
/// 右栏「关系」摘要的行文本 —— <b>纯函数，不碰 Node</b>。
/// </summary>
/// <remarks>
/// <para><b>第三阶段 M6 的最小 UI（P3-D11）：</b>只读展示「最亲近/最疏远的几对人」，
/// 不提供直接改关系的命令。数据来自 <see cref="RelationshipBook.Snapshot"/>，
/// 不在 UI 里维护任何独立的关系副本。</para>
/// <para><b>为什么是纯函数</b>：行文本可单测；<c>Main</c> 只在文本变化时更新 Label
/// （与花名册同一模式，避免每帧重排）。</para>
/// </remarks>
public static class RelationshipPresenter
{
    /// <summary>最多显示几行。</summary>
    public const int MaxLines = 4;

    /// <summary>还没有任何关系时的占位行。</summary>
    public const string EmptyText = "（还没有人说得上话）";

    /// <summary>
    /// 生成关系摘要行：按关系绝对值从大到小，显示 <c>A → B +12</c>。
    /// </summary>
    /// <param name="records">关系快照（有向）。</param>
    /// <param name="names">英雄 Id → 显示名；缺失时回落为 Id。</param>
    /// <param name="maxLines">最多几行。</param>
    /// <returns>行文本；没有关系时返回只含 <see cref="EmptyText"/> 的一行。</returns>
    public static List<string> Lines(
        IReadOnlyList<RelationshipRecord> records,
        IReadOnlyDictionary<HeroId, string> names,
        int maxLines = MaxLines)
    {
        if (records is null) throw new ArgumentNullException(nameof(records));
        if (names is null) throw new ArgumentNullException(nameof(names));

        var rows = new List<(int Abs, string From, string To, int Value)>();
        foreach (var record in records)
        {
            if (record.Value == 0) continue;

            string from = names.TryGetValue(record.Key.From, out var fromName)
                ? fromName : record.Key.From.Value;
            string to = names.TryGetValue(record.Key.To, out var toName)
                ? toName : record.Key.To.Value;

            rows.Add((Math.Abs(record.Value), from, to, record.Value));
        }

        // 先按「关系强度」降序，再按名字稳定排序 —— 同一份数据每次渲染顺序一致。
        rows.Sort(static (a, b) =>
        {
            int byAbs = b.Abs.CompareTo(a.Abs);
            if (byAbs != 0) return byAbs;

            int byFrom = string.CompareOrdinal(a.From, b.From);
            return byFrom != 0 ? byFrom : string.CompareOrdinal(a.To, b.To);
        });

        var lines = new List<string>();
        for (int i = 0; i < rows.Count && i < maxLines; i++)
        {
            lines.Add($"{rows[i].From} → {rows[i].To}  {rows[i].Value:+0;-0;0}");
        }

        if (lines.Count == 0) lines.Add(EmptyText);
        return lines;
    }
}
