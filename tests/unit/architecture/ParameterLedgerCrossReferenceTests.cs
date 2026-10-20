using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using GdUnit4;

namespace ClaudeCodeGameStudios.Tests;

/// <summary>
/// <b>两张台账之间的链接必须是真的</b>：代码层的参数台账
/// （<c>production/qa/parameters.json</c>）与规则层的临时参数台账
/// （<c>production/07</c> §2 的 P-01…P-13）。
///
/// <para><b>它们本该互补</b>（<c>07</c> §2 的原话）：
/// 「清单保证『没有数字被漏掉』，本表保证『已登记的待定项有编号、有归属』。」
/// 而**「有归属」这一半此前是靠自由文本维持的** ——
/// 台账里写「副本有几个阶段（3）—— **台账未登记，见 §3**」，
/// 而 <b>P-12 其实当天就补登进 §2 了</b>。
/// 那句错话还被 <b>机器生成</b>带进了 <c>production/36_当前实现参数清单</c>
/// ⇒ 机器生成的东西也会把过期的话原样传下去。</para>
///
/// <para><b>两条断言</b>：
/// <list type="number">
/// <item><b>链接不许悬空</b>：台账里出现的每个 <c>P-xx</c>，<c>07</c> §2 必须真有这一行
/// （P 项被移走了、台账还在指 ⇒ 红）。</item>
/// <item><b>占位值不许是无主的</b>：每条【待定】/【占位】的参数，
/// 它的 <c>status</c> 必须指明它服务于哪个 <c>P-xx</c> / <c>T-xxx</c> / <c>D-xxx</c>
/// —— 这正是 <c>07</c> §2.1 纪律第 2 条「每处占位必须注明它服务于哪个 T 编号」。
/// 没有归属指针的占位值 ⇒ <b>开发者拍板时没人知道该改它</b> ⇒ 红。</item>
/// </list></para>
///
/// <para><b>为什么这次要动它</b>：修价格那条路时顺手扫了台账其余 4 条待定/占位参数，
/// 发现**只有价格是「配置化」的**（改它是改文件、不碰代码 ⇒ 测试被弄红时
/// 开发者都不知道自己碰了什么）。而这次查到的是另一头的问题：
/// <b>台账与台账之间的链接会烂，而且烂了没人知道</b>。
/// <b>自由文本的「登记了吗」不该由人记</b> —— 该由机器查。</para>
/// </summary>
[TestSuite]
public class ParameterLedgerCrossReferenceTests
{
    private const string LedgerRelPath = "production/qa/parameters.json";
    private const string DecisionRelPath = "production/07_技术决策与待确认事项.md";

    /// <summary>归属指针：P 编号（规则层临时参数）/ T 编号（待定规则）/ D 编号（决策）。</summary>
    private static readonly Regex Owner = new(@"(P-\d+|T-\d+|D-\d+)", RegexOptions.Compiled);

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

    /// <summary>规则层台账（<c>07</c> §2）里登记的 P 编号。</summary>
    private static HashSet<string> RegisteredParameterIds()
    {
        string path = Path.Combine(RepoRoot(), DecisionRelPath.Replace('/', Path.DirectorySeparatorChar));
        string text = File.ReadAllText(path).Replace("\r\n", "\n");

        int from = text.IndexOf("## 2. 临时参数台账", StringComparison.Ordinal);
        int to = text.IndexOf("## 3. 待定规则台账", StringComparison.Ordinal);
        if (from < 0 || to < 0 || to <= from)
        {
            throw new InvalidOperationException("找不到 `07` 的 §2 / §3 标题 —— 台账结构变了？");
        }

        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match m in Regex.Matches(text[from..to], @"^\|\s*(P-\d+)\s*\|", RegexOptions.Multiline))
        {
            ids.Add(m.Groups[1].Value);
        }
        return ids;
    }

    private sealed record LedgerRow(string File, string Name, string Kind, string Status,
                                   string Note, string Value);

    private static List<LedgerRow> LoadLedger()
    {
        string path = Path.Combine(RepoRoot(), LedgerRelPath.Replace('/', Path.DirectorySeparatorChar));
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var rows = new List<LedgerRow>();

        foreach (var group in new[] { "parameters", "nonParameters" })
        {
            if (!doc.RootElement.TryGetProperty(group, out var arr)) continue;
            foreach (var r in arr.EnumerateArray())
            {
                rows.Add(new LedgerRow(
                    r.GetProperty("file").GetString() ?? "",
                    r.GetProperty("name").GetString() ?? "",
                    r.GetProperty("kind").GetString() ?? "",
                    r.GetProperty("status").GetString() ?? "",
                    r.TryGetProperty("note", out var n) ? n.GetString() ?? "" : "",
                    r.TryGetProperty("value", out var v) ? v.GetString() ?? "" : ""));
            }
        }
        return rows;
    }

    /// <summary>
    /// 台账里出现的每个 <c>P-xx</c>，<c>07</c> §2 必须真有这一行。
    /// </summary>
    [TestCase]
    public void EveryParameterId_ReferencedByTheLedger_ExistsInTheRuleLedger()
    {
        var registered = RegisteredParameterIds();
        var ledger = LoadLedger();

        // 防空转：规则层台账必须真有 P 行，代码层台账必须真有引用。
        Assertions.AssertThat(registered.Count).IsGreaterEqual(10);
        Assertions.AssertThat(ledger.Count).IsGreater(10);

        var dangling = new List<string>();
        foreach (var row in ledger)
        {
            foreach (Match m in Owner.Matches(row.Status))
            {
                if (!m.Value.StartsWith("P-", StringComparison.Ordinal)) continue;
                if (!registered.Contains(m.Value))
                {
                    dangling.Add($"{row.File}::{row.Name} 指向 {m.Value}，"
                                 + "但 `07` §2 没有这一行");
                }
            }
        }

        Assertions.AssertThat(string.Join(" | ", dangling)).IsEmpty();
    }

    /// <summary>
    /// 每条【待定】/【占位】的参数都必须<b>有归属指针</b>
    /// （服务于哪个 P / T / D）—— 否则开发者拍板时**没人知道该改它**。
    /// </summary>
    [TestCase]
    public void EveryPendingParameter_SaysWhoItIsWaitingFor()
    {
        var ledger = LoadLedger();
        var pending = ledger
            .Where(r => r.Status.Contains("待定") || r.Status.Contains("占位"))
            .ToList();

        // 防空转：台账里必须真有待定/占位项，否则本条什么也没守。
        Assertions.AssertThat(pending.Count).IsGreater(5);

        var orphans = pending
            .Where(r => !Owner.IsMatch(r.Status))
            .Select(r => $"{r.File}::{r.Name} [{r.Status}] 没有说它服务于哪个 P/T/D")
            .ToList();

        Assertions.AssertThat(string.Join(" | ", orphans)).IsEmpty();
    }

    /// <summary>
    /// <b>备注里不许出现「查无此数」的数字</b>。
    ///
    /// <para><b>它守的是什么</b>：备注是**人写的**，值是**机器跟的**
    /// （<c>param-inventory.py --sync-values</c> 会把值同步成代码里的当前值）。
    /// 两者一分离，生成的清单文档就会出现**自相矛盾**的一行：
    /// <c>| PlaceholderHeroWinRate | 0.7 | 【占位】P-07 | 战斗占位胜率（0.65） |</c>
    /// —— 值是 0.7，备注还写着 0.65。</para>
    ///
    /// <para><b>判据</b>：备注里出现的每个「独立数字」，
    /// 必须能在台账的<b>某个值</b>里找到（0.10 与 0.1 视作同一个数）。
    /// 于是「过期的数字」无处可藏 ⇒ 开发者必须把措辞一起改掉。
    /// <b>字母或连字符前缀的数字不算</b>（<c>CR-1</c> / <c>P-13</c> / <c>2026-10-21</c>）。</para>
    /// </summary>
    [TestCase]
    public void Notes_DoNotMentionNumbersThatNoLongerExist()
    {
        var ledger = LoadLedger();

        // 台账里所有值的归一化集合（0.10 与 0.1 视作同一个数）
        var known = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in ledger)
        {
            string v = row.Value.Trim();
            if (v.Length == 0) continue;
            known.Add(NormalizeNumber(v));
        }

        // 防空转：必须真的有值、也真的有备注。
        Assertions.AssertThat(known.Count).IsGreater(5);
        Assertions.AssertThat(ledger.Count).IsGreater(10);

        // 「独立数字」：前面不是字母或连字符（排除 CR-1 / P-13 / 2026-10-21）
        var standalone = new Regex(@"(?<![A-Za-z\-])\d+(?:\.\d+)?", RegexOptions.Compiled);

        var stale = new List<string>();
        foreach (var row in ledger)
        {
            foreach (Match m in standalone.Matches(row.Note))
            {
                string token = NormalizeNumber(m.Groups[0].Value);
                if (known.Contains(token)) continue;
                stale.Add($"{row.File}::{row.Name} 的备注里有个 {m.Groups[0].Value}"
                          + " —— 台账里没有这个数（多半是值改了、措辞没跟上）");
            }
        }

        Assertions.AssertThat(string.Join(" | ", stale)).IsEmpty();
    }

    /// <summary>把数字文本归一化（0.10 与 0.1 视作同一个数）。</summary>
    private static string NormalizeNumber(string v)
    {
        if (double.TryParse(v, System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out double f))
        {
            return f == Math.Floor(f) ? ((long)f).ToString() : f.ToString("0.#########",
                       System.Globalization.CultureInfo.InvariantCulture);
        }
        return v;
    }

    /// <summary>
    /// <c>07</c> §2 的 P 表里<b>点名了常量并写了它的值</b>（<c>`MaxTier = 5`</c>）
    /// —— 那些值必须与台账一致。
    ///
    /// <para><b>为什么这条不能省</b>：开发者改代码里的常量（T-004 的战斗数值、
    /// D-003 的层级数）之后，<c>--sync-values</c> 会把**台账**的值跟上去，
    /// 而 §2 的 P 表是**人写的散文** ⇒ 它会停在旧值上。
    /// 而 §2 是**规则层的台账** —— 它说「村庄最高层级 = 5」，而代码已经是 7，
    /// 那它就在**撒谎**，而且撒的是「规则」层面的谎。</para>
    ///
    /// <para><b>为什么是「按常量名比对」而不是「按数字是否存在于台账」</b>：
    /// 后者太弱 —— <c>5</c> 换个参数也仍然是台账里的某个值（背包上限就是 5），
    /// 于是「MaxTier 从 5 变成 7」这种最该被抓的情况会**溜过去**。
    /// 按名字比对就没有这个问题。</para>
    /// </summary>
    [TestCase]
    public void RuleLedger_QuotedConstantValues_MatchTheLedger()
    {
        var ledger = LoadLedger();

        // 常量名 → 台账里的值（同名多份时必须**都**一致）
        var byName = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var row in ledger)
        {
            if (!byName.TryGetValue(row.Name, out var list))
            {
                list = new List<string>();
                byName[row.Name] = list;
            }
            list.Add(row.Value);
        }

        string path = Path.Combine(RepoRoot(), DecisionRelPath.Replace('/', Path.DirectorySeparatorChar));
        string text = File.ReadAllText(path).Replace("\r\n", "\n");
        int from = text.IndexOf("## 2. 临时参数台账", StringComparison.Ordinal);
        int to = text.IndexOf("## 3. 待定规则台账", StringComparison.Ordinal);
        Assertions.AssertThat(from).IsGreaterEqual(0);
        Assertions.AssertThat(to).IsGreater(from);

        // §2 里形如 `常量名 = 数字` 的引用
        var quoted = new Regex(@"`(?<n>[A-Za-z_][A-Za-z0-9_]*)\s*=\s*(?<v>-?\d+(?:\.\d+)?)`",
                               RegexOptions.Compiled);

        var offenders = new List<string>();
        int checkedRefs = 0;
        foreach (Match m in quoted.Matches(text[from..to]))
        {
            string name = m.Groups["n"].Value;
            string stated = m.Groups["v"].Value;
            checkedRefs++;

            if (!byName.TryGetValue(name, out var values))
            {
                offenders.Add($"§2 提到 `{name} = {stated}`，但台账里没有这个常量");
                continue;
            }
            foreach (string actual in values)
            {
                if (NormalizeNumber(actual) != NormalizeNumber(stated))
                {
                    offenders.Add($"§2 说 `{name} = {stated}`，台账里是 {actual} —— "
                                  + "**规则层的台账在撒谎**（改了代码记得 `--sync-values` 并改这里）");
                }
            }
        }

        // 防空转：§2 必须真的点了几个常量的名，否则本条什么也没守。
        Assertions.AssertThat(checkedRefs).IsGreaterEqual(2);
        Assertions.AssertThat(string.Join(" | ", offenders)).IsEmpty();
    }
}
