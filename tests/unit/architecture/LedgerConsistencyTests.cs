using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using GdUnit4;

namespace ClaudeCodeGameStudios.Tests;

/// <summary>
/// <b>台账一致性守卫</b>：<c>production/07</c> 的技术债台账里，
/// <b>「还开着什么」不许在三处说成三个样子</b>。
///
/// <para><b>它守的是三处真实的不一致</b>（本守卫写出来时全都存在）：
/// <list type="number">
/// <item>§4.3 的活跃缺陷表写着「C-02 / C-03 <b>全部已清</b>」，
/// §6「遗留事项」表却还写着「<b>未处理</b> —— 归入 D-001 重构范围」。</item>
/// <item>§4 的「现状速览」表写着「架构债 A1–A7：仍开 = <b>无</b>」、
/// 「内容债 B1–B7：仍开 = B1/B2/B3」，紧接着的散文却写
/// 「<b>现在真正还开的只有四项：A1 · B4 · B1/B2/B3</b>」——
/// A1 与 B4 在同一节里被同时说成「已清」和「还开着」。</item>
/// <item>§6 的 M1 完成度一栏写着「<c>Main.cs</c> 632 行<strong>仍是</strong>那个巨型管理器」，
/// 而 A1 早已把它拆成 7 个 partial ⇒ 那句话已经<strong>完全失效</strong>。</item>
/// </list></para>
///
/// <para><b>为什么这类不一致特别有害</b>：读者多半只翻那张「还剩什么」的表。
/// 它写着「未处理」，他就会以为有活没干；或者更糟 —— 以为那 83 行死代码还在，
/// 于是去动已经删掉的东西。<b>一份自己和自己打架的台账，等于没有台账。</b></para>
///
/// <para><b>「历史陈述」不算矛盾</b>：D-001 的决策正文写「<c>Main.cs</c> 632 行是巨型管理器」，
/// 那是<strong>当时</strong>的状况，是决策记录的一部分 ⇒ <b>不许改</b>。
/// 所以本守卫只检查<b>还开着的行</b> —— 一行一旦标了「已清」，它对现状的描述就变成了历史，
/// 历史不改。</para>
/// </summary>
[TestSuite]
public class LedgerConsistencyTests
{
    private const string DecisionDoc = "07_技术决策与待确认事项.md";

    /// <summary>一行里出现这些词 ⇒ 它描述的是<strong>历史</strong>，不参与现状核对。</summary>
    private static readonly string[] ClosedMarkers =
    {
        "已清", "已关闭", "已过期", "已修",
    };

    /// <summary>一行里出现这些词 ⇒ 它还在开着，它的现状描述必须是真的。</summary>
    private static readonly string[] OpenMarkers =
    {
        "仍开", "未处理", "未完成", "待定",
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

    private static string DecisionText()
    {
        string path = Path.Combine(RepoRoot(), "production", DecisionDoc);
        return File.ReadAllText(path).Replace("\r\n", "\n");
    }

    /// <summary>取两个标题之间的正文；标题不存在就返回空串。</summary>
    private static string Section(string text, string from, string to)
    {
        int a = text.IndexOf(from, StringComparison.Ordinal);
        if (a < 0) return "";
        int b = to is null ? text.Length : text.IndexOf(to, a, StringComparison.Ordinal);
        if (b < 0) b = text.Length;
        return text[a..b];
    }

    // ── 断言 1：§4.3 台账标「已清」的编号，§6 不得还写「未处理」 ────────

    /// <summary>
    /// §4.3 的活跃缺陷台账里标了「已清」的编号，§6「遗留事项」表里不得还写着「未处理」。
    /// </summary>
    [TestCase]
    public void ClearedDefects_AreNotStillListedAsPending()
    {
        string text = DecisionText();

        var cleared = new HashSet<string>(StringComparer.Ordinal);
        var ledgerRow = new Regex(@"^\|\s*\**(?<id>C-\d+)\**\s*\|(?<rest>.*)$", RegexOptions.Multiline);
        foreach (Match m in ledgerRow.Matches(text))
        {
            if (m.Groups["rest"].Value.Contains("已清")) cleared.Add(m.Groups["id"].Value);
        }

        // 防空转：台账里必须真有「已清」的条目，否则本条什么也没守。
        Assertions.AssertThat(cleared.Count).IsGreaterEqual(1);

        string backlog = Section(text, "## 6. 遗留事项", null);
        Assertions.AssertThat(backlog).IsNotEmpty();

        var backlogRow = new Regex(@"^\|\s*\**(?<id>C-\d+)\**[^|]*\|(?<status>.*)$", RegexOptions.Multiline);
        var offenders = new List<string>();
        foreach (Match m in backlogRow.Matches(backlog))
        {
            string id = m.Groups["id"].Value;
            if (!cleared.Contains(id)) continue;
            if (!m.Groups["status"].Value.Contains("未处理")) continue;
            offenders.Add($"§6 遗留事项里 {id} 仍写着「未处理」，但 §4.3 台账已标「已清」");
        }

        Assertions.AssertThat(string.Join(" | ", offenders)).IsEmpty();
    }

    // ── 断言 2：§4「现状速览」与逐条表必须说同一件事 ────────────────────

    /// <summary>
    /// §4 的「现状速览」（表 + 紧跟其后的散文）与 §4.1/4.2/4.3 的逐条表必须给出
    /// <b>同一个</b>「还开着什么」的答案 —— 连数量都要一致。
    /// </summary>
    [TestCase]
    public void LedgerOverview_AgreesWithThePerItemTables()
    {
        string text = DecisionText();

        string overview = Section(text, "### 🔎 现状速览", "### 4.1 ");
        string detail = Section(text, "### 4.1 ", "### 4.4 ");

        Assertions.AssertThat(overview).IsNotEmpty();
        Assertions.AssertThat(detail).IsNotEmpty();

        // ① 逐条表算出来的「还开着」的编号（以「现状」列的说法为准）
        var openFromDetail = new HashSet<string>(StringComparer.Ordinal);
        var row = new Regex(@"^\|\s*\**(?<id>(?:A|B)\d+[a-z]?|C-\d{2})\**\s*\|(?<rest>.*)$",
                             RegexOptions.Multiline);
        foreach (Match m in row.Matches(detail))
        {
            string id = m.Groups["id"].Value;
            // 「现状」是**最后一格**。行尾那个分隔竖线后面是空的，
            // 所以要取最后一个**非空**格 —— 直接取最后一格会拿到空串。
            string status = m.Groups["rest"].Value
                                .Split('|')
                                .Last(c => c.Trim().Length > 0);

            if (ClosedMarkers.Any(c => status.Contains(c))) continue;
            if (OpenMarkers.Any(c => status.Contains(c))) openFromDetail.Add(id);
        }

        // 防空转：逐条表里必须真有「还开着」的条目，否则本条什么也没守。
        Assertions.AssertThat(openFromDetail.Count).IsGreaterEqual(1);

        // ②「速览」表「仍开」列里的编号
        //    注意：速览表在引用块里（每行以 "> " 开头）⇒ 先把块引用前缀去掉。
        var openFromTable = new HashSet<string>(StringComparer.Ordinal);
        var overviewLines = overview.Replace("\r\n", "\n").Split('\n');
        foreach (string raw in overviewLines)
        {
            string line = raw.TrimStart();
            if (line.StartsWith("> ", StringComparison.Ordinal)) line = line[2..].TrimStart();
            if (!line.StartsWith("|", StringComparison.Ordinal)) continue;

            var stillOpen = new Regex(@"^\|\s*\*\*[^|]+\*\*\s*\|(?<cell>[^|]*)\|",
                                      RegexOptions.Compiled).Match(line);
            if (!stillOpen.Success) continue;
            foreach (string id in Ids(stillOpen.Groups["cell"].Value)) openFromTable.Add(id);
        }

        // ③ 速览表**之后**那段散文（「现在真正还开的只有 N 项：……」）里的编号
        int lastTableRow = -1;
        for (int i = 0; i < overviewLines.Length; i++)
        {
            string line = overviewLines[i].TrimStart();
            if (line.StartsWith("> ", StringComparison.Ordinal)) line = line[2..].TrimStart();
            if (line.StartsWith("|", StringComparison.Ordinal)) lastTableRow = i;
        }
        string prose = lastTableRow >= 0
            ? string.Join("\n", overviewLines.Skip(lastTableRow + 1))
            : string.Join("\n", overviewLines);

        var openFromProse = new HashSet<string>(StringComparer.Ordinal);
        foreach (string id in Ids(prose)) openFromProse.Add(id);

        var offenders = new List<string>();
        if (!openFromTable.SetEquals(openFromDetail))
        {
            offenders.Add($"速览表的「仍开」列是 {Show(openFromTable)}，"
                          + $"逐条表算出来是 {Show(openFromDetail)}");
        }
        if (!openFromProse.SetEquals(openFromDetail))
        {
            offenders.Add($"速览表后面的散文列的是 {Show(openFromProse)}，"
                          + $"逐条表算出来是 {Show(openFromDetail)}");
        }

        // ④ 散文里的「N 项」也要等于实际条数
        var count = new Regex(@"还开的只有(?<n>[一二三四五六七八九十\d]+)项");
        Match cm = count.Match(prose);
        if (cm.Success)
        {
            int stated = ChineseNumber(cm.Groups["n"].Value);
            if (stated != openFromDetail.Count)
            {
                offenders.Add($"散文说「只有 {cm.Groups["n"].Value} 项」，"
                              + $"实际是 {openFromDetail.Count} 项（{Show(openFromDetail)}）");
            }
        }

        Assertions.AssertThat(string.Join(" | ", offenders)).IsEmpty();
    }

    // ── 断言 3：还开着的行里，行数声明必须与代码一致 ──────────────────────

    /// <summary>
    /// §4 / §6 里<b>还开着</b>的行：① 它引用的每个 <c>.cs</c> 文件必须<b>真的存在</b>；
    /// ② 它写的「<c>X.cs</c> N 行」必须与实际行数一致。
    /// <para><b>已清的行不参与</b> —— 那是对当时的记录，改它就是篡改历史。</para>
    /// </summary>
    [TestCase]
    public void OpenRows_DescribeTheCodeAsItIsNow()
    {
        string text = DecisionText();

        // 取材范围：**四张债表**（§4.1–§4.4）+ §6 遗留事项表。
        // ⚠️ 「## 4. 技术债台账」这个大标题底下还挂着十几份**决策记录**
        // （D-003 / D-004 / …），那是**历史叙述**（里面写着当时的行数），
        // 拿它当「现状」查就是在要求篡改历史 ⇒ 只取债表本身。
        int s41 = text.IndexOf("### 4.1 ", StringComparison.Ordinal);
        int s44 = text.IndexOf("### 4.4 ", StringComparison.Ordinal);
        int sD3 = text.IndexOf("### D-003", s44, StringComparison.Ordinal);
        Assertions.AssertThat(s41).IsGreaterEqual(0);
        Assertions.AssertThat(s44).IsGreaterEqual(0);

        string scope = text[s41..s44] + text[s44..(sD3 > 0 ? sD3 : text.Length)]
                     + "\n" + Section(text, "## 6. 遗留事项", null);

        var claim = new Regex(@"`(?<file>[A-Za-z0-9_\-./\\]+\.cs)`[^`\n]{0,40}?(?<n>\d{1,5})\s*行",
                              RegexOptions.Compiled);

        // 文件引用：`Foo.cs` 或 `Foo.cs:123`（带行号）
        var fileRef = new Regex(@"`(?<file>[A-Za-z0-9_\-./\\]+\.cs)(?::\d+(?:-\d+)?)?`",
                                RegexOptions.Compiled);

        var offenders = new List<string>();

        foreach (string raw in scope.Split('\n'))
        {
            string line = raw.TrimStart();
            if (line.StartsWith("> ", StringComparison.Ordinal)) line = line[2..].TrimStart();
            if (!line.StartsWith("|", StringComparison.Ordinal)) continue;
            if (ClosedMarkers.Any(c => line.Contains(c))) continue;   // 历史陈述
            if (!OpenMarkers.Any(c => line.Contains(c))) continue;    // 只看还开着的

            // ① 引用的文件必须存在（文档不许指向空气）
            //    正则本身已经保证名字以 .cs 结尾 ⇒ 这里**不能**再用「含不含点」去筛
            //    （那会把 `Main.cs` 这种真文件名也筛掉 —— 守卫就此变成空转）。
            foreach (Match m in fileRef.Matches(line))
            {
                string name = m.Groups["file"].Value;
                if (ResolveSource(name) is null) offenders.Add($"引用了不存在的文件 {name}");
            }

            // ② 行数声明必须与代码一致
            foreach (Match m in claim.Matches(line))
            {
                int stated = int.Parse(m.Groups["n"].Value);
                if (LineCountIsStale(m.Groups["file"].Value, stated))
                {
                    int actual = File.ReadAllLines(ResolveSource(m.Groups["file"].Value)!).Length;
                    offenders.Add($"{m.Groups["file"].Value} 写 {stated} 行，实际 {actual} 行");
                }
            }
        }

        // 防空转：判据必须**真的抓得住**一个错的数字 —— 否则这条守卫可能一直「通过」
        // 却什么都没守（文档里恰好没有行数声明时，它就永远绿）。
        var probe = ResolveSource("Main.cs");
        Assertions.AssertThat(probe).IsNotNull();
        int probeActual = File.ReadAllLines(probe!).Length;
        Assertions.AssertThat(LineCountIsStale("Main.cs", probeActual + 7)).IsTrue();
        Assertions.AssertThat(LineCountIsStale("Main.cs", probeActual)).IsFalse();

        Assertions.AssertThat(string.Join(" | ", offenders)).IsEmpty();
    }

    /// <summary>行数声明是否与实际不符（容许差 1 —— <c>wc -l</c> 与「按行读」的计数口径差 1）。</summary>
    private static bool LineCountIsStale(string file, int stated)
    {
        var resolved = ResolveSource(file);
        if (resolved is null) return false;
        int actual = File.ReadAllLines(resolved).Length;
        return stated != actual && stated != actual - 1;
    }

    // ── 工具 ──────────────────────────────────────────────────────────────

    /// <summary>从一段文字里挑出台账编号（A1 / B6a / C-02 这类）。</summary>
    private static IEnumerable<string> Ids(string text) =>
        Regex.Matches(text, @"(?<![A-Za-z0-9])(?:A[1-9]|B[1-9]|B6a|C-\d{2})(?![A-Za-z0-9])")
             .Select(m => m.Value)
             .Distinct();

    private static string Show(IEnumerable<string> ids) =>
        "{" + string.Join(",", ids.OrderBy(x => x, StringComparer.Ordinal)) + "}";

    /// <summary>中文数字（≤ 20）或阿拉伯数字。</summary>
    private static int ChineseNumber(string s)
    {
        if (int.TryParse(s, out int n)) return n;
        var map = new Dictionary<string, int>
        {
            ["一"] = 1, ["二"] = 2, ["三"] = 3, ["四"] = 4, ["五"] = 5,
            ["六"] = 6, ["七"] = 7, ["八"] = 8, ["九"] = 9, ["十"] = 10,
        };
        int total = 0, current = 0;
        foreach (char c in s)
        {
            string ch = c.ToString();
            if (!map.TryGetValue(ch, out int v)) continue;
            if (v == 10) { total += (current == 0 ? 1 : current) * 10; current = 0; }
            else current = v;
        }
        return total + current;
    }

    /// <summary>
    /// 把文档里写的文件名解析成磁盘上的真实路径。
    /// <para><b>解析不出来就返回 null</b>（守卫不对认不出的东西下结论）；
    /// 按名字搜索时若命中多个，也返回 null —— 宁可放过，不可指错。</para>
    /// </summary>
    private static string? ResolveSource(string name)
    {
        string root = RepoRoot();
        string rel = name.Replace('\\', '/');
        foreach (var candidate in new[]
                 {
                     Path.Combine(root, rel.Replace('/', Path.DirectorySeparatorChar)),
                     Path.Combine(root, "src", rel.Replace('/', Path.DirectorySeparatorChar)),
                 })
        {
            if (File.Exists(candidate)) return candidate;
        }

        var hits = new List<string>();
        foreach (var dir in new[] { "src", "tests" })
        {
            string baseDir = Path.Combine(root, dir);
            if (!Directory.Exists(baseDir)) continue;
            hits.AddRange(Directory.EnumerateFiles(baseDir, Path.GetFileName(rel),
                                                  SearchOption.AllDirectories));
        }
        return hits.Count == 1 ? hits[0] : null;
    }
}
