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
}
