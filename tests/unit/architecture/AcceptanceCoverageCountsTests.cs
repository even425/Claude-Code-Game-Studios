using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using GdUnit4;

namespace ClaudeCodeGameStudios.Tests;

/// <summary>
/// <b>验收覆盖的数字不许过期</b>：<c>production/06</c> 的不变量表 / A 断言表里
/// 每行的状态，与 <c>production/qa/README.md</c> 的「验收计划的覆盖现状」表里
/// 写的**两个数字**必须一致。
///
/// <para><b>它守的是什么</b>：那两行数字是**手写的摘要**，而它们是**读者最先看的**
/// 东西（「还差几条？」）。摘要与正文分属两处、又没有机器核对 ⇒
/// 正文改了、摘要没改 ⇒ **读者拿到的第一个数字就是错的**。
/// 真事：2026-10-21 I9 闭合之后，<c>06</c> 的正文已经写「✅ 已证明」，
/// 而 README 还写着「**14** | 2（I9 受阻…）」。</para>
///
/// <para><b>状态怎么分类：看格里的<b>首个标记</b>，不看整格有没有某个词</b>。
/// 这条是本守卫写出来当天就踩到的坑：<b>I16</b> 的状态格是
/// 「✅ <b>已写</b>（… <b>原「受阻：字段未回填 <c>05</c> §3.9</b>是过期状态」）」——
/// 它<b>提到</b>「受阻」，但那是在说明<b>旧状态已经过期</b>，不是现在的状态。
/// 按「含『受阻』就算受阻」判 ⇒ I16 被数成受阻 ⇒ 计数错 ⇒ 守卫指着 README
/// 的数字报错，而 README 的数字其实是对的 —— <b>守卫自己错了，还拉错了人</b>。
/// 「历史陈述不算现状」这条原则，本仓库的 <c>LedgerConsistencyTests</c> 早就在用了；
/// 这里必须用同一条原则，否则就是两套判据互相打脸。</para>
///
/// <para><b>标记集合是显式的</b>：首个标记不在下表里 ⇒ <b>报错</b>，而不是猜一个类别。
/// 猜错了会让守卫误报或漏报，而「请把新标记加进集合」是一次性、可读的代价。
/// 这跟本仓库「值由机器跟、措辞由人跟」是同一个思路：集合有限，新增要显式承认。</para>
///
/// <para><b>规则</b>：
/// <list type="number">
/// <item>不变量表：首标记 <c>✅</c> / <c>🟡</c> ⇒ 已写；<c>受阻</c> ⇒ 受阻；
/// <c>需</c> / <c>待定</c> ⇒ 未写；<b>其它 ⇒ 报错</b>。</item>
/// <item>A 断言表：同上（它现在的首标记是 <c>✅</c> 与 <c>需</c>，同样适用）。</item>
/// <item>README 摘要表的<b>两个数字</b>必须等于上两步算出的（已写 / 未写）。</item>
/// <item>README 标题里的<b>范围标签</b>（如 <c>I1–I16</c>、<c>A-01…A-12</c>）
/// 必须等于 06 正文里实际的行数 —— 表长了而摘要没改，同样是骗人。</item>
/// </list></para>
///
/// <para><b>为什么值得写成守卫</b>：这类摘要**天生会过期** ——
/// 不是谁忘了改，而是「改了正文不会有人顺手改摘要」。
/// 机器盯着的摘要才不会再骗人。</para>
/// </summary>
[TestSuite]
public class AcceptanceCoverageCountsTests
{
    private const string PlanDoc = "production/06_第一阶段验收与测试计划_V1.0.md";
    private const string ReadmeDoc = "production/qa/README.md";

    /// <summary>首个标记 ⇒ 类别。新增标记必须显式加进来（见类注释）。</summary>
    private static readonly (string Marker, string Category)[] LeadingMarkers =
    {
        ("✅", "已写"),
        ("🟡", "已写"),
        ("**受阻**", "受阻"),
        ("受阻", "受阻"),
        ("需", "未写"),
        ("待定", "未写"),
        ("⛔", "未写"),
        ("❌", "未写"),
        ("🔴", "未写"),
    };

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(
            Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location)!);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Claude-Code-Game-Studios.csproj"))
                && Directory.Exists(Path.Combine(dir.FullName, "src")))
            {
                return dir.FullName;
            }
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("找不到仓库根目录");
    }

    private static string Read(string rel) =>
        File.ReadAllText(Path.Combine(RepoRoot(), rel.Replace('/', Path.DirectorySeparatorChar)))
            .Replace("\r\n", "\n");

    /// <summary>把一行 Markdown 表格拆成格（去掉首尾两个空壳）。</summary>
    private static List<string> SplitRow(string line)
    {
        var cells = line.Split('|').ToList();
        if (cells.Count > 0 && cells[0].Trim().Length == 0) cells.RemoveAt(0);
        if (cells.Count > 0 && cells[cells.Count - 1].Trim().Length == 0) cells.RemoveAt(cells.Count - 1);
        return cells.Select(c => c.Trim()).ToList();
    }

    private static bool IsSeparatorRow(List<string> cells) =>
        cells.Count > 0 && cells.All(c => Regex.IsMatch(c, @"^:?-{2,}:?$"));

    /// <summary>取表格行的最后一格（状态格）。</summary>
    private static string LastCell(string row)
    {
        var cells = row.Split('|');
        for (int i = cells.Length - 1; i >= 0; i--)
        {
            if (cells[i].Trim().Length > 0) return cells[i];
        }
        return "";
    }

    /// <summary>按<b>首个标记</b>分类状态格。</summary>
    private static string ClassifyStatus(string status)
    {
        string s = status.Trim();
        foreach (var (marker, category) in LeadingMarkers)
        {
            if (s.StartsWith(marker, StringComparison.Ordinal)) return category;
        }
        throw new InvalidOperationException(
            $"状态格的首个标记不在守卫的已知集合里：「{s.Substring(0, Math.Min(40, s.Length))}…」"
            + " —— 请在 AcceptanceCoverageCountsTests.LeadingMarkers 里显式承认这个标记。");
    }

    /// <summary>README 覆盖现状表里的一行。</summary>
    private sealed class CoverageRow
    {
        public string Label = "";
        public int Written;
        public int Pending;
        public int RangeStart = -1;
        public int RangeEnd = -1;
    }

    /// <summary>
    /// 解析 README 的「验收计划的覆盖现状」表。
    /// <para><b>为什么按表结构找行、而不是按行首字面量找</b>：按字面量（"以「| 不变量」开头"）
    /// 的话，人把标题措辞从「不变量 I1–I16」改成「不变量表 I1–I16」就得改守卫 ——
    /// <b>措辞一变就红</b>是误报，不是保护。按表结构找：表头是「表 | 已写 | 未写 | …」，
    /// 它下面的数据行按第一格区分是哪张表 ⇒ 措辞随便改。</para>
    /// </summary>
    private static List<CoverageRow> ParseReadmeCoverageTable()
    {
        var lines = Read(ReadmeDoc).Split('\n');

        int header = -1;
        for (int i = 0; i < lines.Length; i++)
        {
            var cells = SplitRow(lines[i]);
            if (cells.Count >= 4 && cells[0] == "表") { header = i; break; }
        }
        if (header < 0)
        {
            throw new InvalidOperationException(
                "README 里找不到「验收计划的覆盖现状」表头（第一格应为「表」）");
        }

        var rows = new List<CoverageRow>();
        for (int i = header + 1; i < lines.Length; i++)
        {
            var line = lines[i];
            if (!line.TrimStart().StartsWith("|")) break;
            var cells = SplitRow(line);
            if (cells.Count < 4) break;
            if (IsSeparatorRow(cells)) continue;

            var row = new CoverageRow { Label = cells[0] };
            row.Written = ParseCount(cells[1], "已写");
            row.Pending = ParseCount(cells[2], "未写");

            // 范围标签：I1–I16 / A-01…A-12
            var m = Regex.Match(row.Label, @"(\d+)\s*[\u2013\u2014\u2026~]\s*(?:[A-Za-z]-?)?(\d+)");
            if (m.Success)
            {
                row.RangeStart = int.Parse(m.Groups[1].Value);
                row.RangeEnd = int.Parse(m.Groups[2].Value);
            }
            rows.Add(row);
        }

        if (rows.Count != 2)
        {
            throw new InvalidOperationException(
                $"覆盖现状表应当恰好两行（不变量 / A 断言），实际 {rows.Count} 行");
        }
        return rows;
    }

    private static int ParseCount(string cell, string what)
    {
        string digits = Regex.Replace(cell, @"[^0-9]", "");
        if (digits.Length == 0)
        {
            throw new InvalidOperationException($"覆盖现状表的「{what}」那一格里没有数字：「{cell}」");
        }
        return int.Parse(digits);
    }

    /// <summary>06 正文里一张表的行 + 分类计数。</summary>
    private sealed class PlanTable
    {
        public List<string> Rows = new();
        public int Written;
        public int Blocked;
        public int Pending;
    }

    private static PlanTable ReadPlanTable(string rowPattern)
    {
        var plan = Read(PlanDoc);
        var table = new PlanTable();
        table.Rows = plan.Split('\n').Where(l => Regex.IsMatch(l, rowPattern)).ToList();

        foreach (var row in table.Rows)
        {
            switch (ClassifyStatus(LastCell(row)))
            {
                case "已写": table.Written++; break;
                case "受阻": table.Blocked++; table.Pending++; break;
                default: table.Pending++; break;
            }
        }
        return table;
    }

    [TestCase]
    public void InvariantCounts_InReadme_MatchThePlanTable()
    {
        var table = ReadPlanTable(@"^\|\s*\*\*I\d+\*\*\s*\|");

        // 防空转：必须真的匹配到一张连续的 I 表，且编号从 I1 起连续
        Assertions.AssertThat(table.Rows.Count).IsGreaterEqual(16);
        for (int i = 0; i < table.Rows.Count; i++)
        {
            Assertions.AssertThat(Regex.Match(table.Rows[i], @"I\d+").Value).IsEqual("I" + (i + 1));
        }

        var readme = ParseReadmeCoverageTable()
            .FirstOrDefault(r => r.Label.Contains("不变量"))
            ?? throw new InvalidOperationException("覆盖现状表里没有「不变量」那一行");

        // 范围标签也要对：表变长了而摘要没改 ⇒ 摘要骗人
        Assertions.AssertThat(readme.RangeEnd).IsEqual(table.Rows.Count);

        Assertions.AssertThat(readme.Written).IsEqual(table.Written);
        Assertions.AssertThat(readme.Pending).IsEqual(table.Pending);
    }

    [TestCase]
    public void AAssertionCounts_InReadme_MatchThePlanTable()
    {
        var table = ReadPlanTable(@"^\|\s*A-\d+\s*\|");

        // 防空转：必须真的匹配到一张连续的 A 表，且编号从 A-01 起连续
        Assertions.AssertThat(table.Rows.Count).IsGreaterEqual(12);
        for (int i = 0; i < table.Rows.Count; i++)
        {
            Assertions.AssertThat(Regex.Match(table.Rows[i], @"A-\d+").Value)
                .IsEqual("A-" + (i + 1).ToString("00"));
        }

        var readme = ParseReadmeCoverageTable()
            .FirstOrDefault(r => r.Label.Contains("A-"))
            ?? throw new InvalidOperationException("覆盖现状表里没有「A-01…A-12」那一行");

        Assertions.AssertThat(readme.RangeEnd).IsEqual(table.Rows.Count);
        Assertions.AssertThat(readme.Written).IsEqual(table.Written);
        Assertions.AssertThat(readme.Pending).IsEqual(table.Pending);
    }
}
