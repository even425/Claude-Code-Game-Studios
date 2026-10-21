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
    private const string ReviewRelPath = "production/37_R-05阶段5_数值评审清单_V1.0.md";

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

    /// <summary>规则层台账（<c>07</c> §3）里登记的 T 编号（待定规则）。</summary>
    private static HashSet<string> RegisteredRuleIds()
    {
        string path = Path.Combine(RepoRoot(), DecisionRelPath.Replace('/', Path.DirectorySeparatorChar));
        string text = File.ReadAllText(path).Replace("\r\n", "\n");

        int from = text.IndexOf("## 3. 待定规则台账", StringComparison.Ordinal);
        if (from < 0) throw new InvalidOperationException("找不到 `07` 的 §3 标题 —— 台账结构变了？");
        int to = text.IndexOf("## 4.", from, StringComparison.Ordinal);
        if (to < 0) to = text.Length;

        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match m in Regex.Matches(text[from..to], @"^\|\s*(T-\d+)\s*\|", RegexOptions.Multiline))
        {
            ids.Add(m.Groups[1].Value);
        }
        return ids;
    }

    /// <summary>机器台账里的「表」条目（阶段 5 要评审的那几张）。</summary>
    private sealed record TableRow(string Id, string File, string Status, string Note);

    private static List<TableRow> LoadTableRows()
    {
        string path = Path.Combine(RepoRoot(), LedgerRelPath.Replace('/', Path.DirectorySeparatorChar));
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var rows = new List<TableRow>();
        if (!doc.RootElement.TryGetProperty("tables", out var arr)) return rows;

        foreach (var t in arr.EnumerateArray())
        {
            rows.Add(new TableRow(
                t.GetProperty("id").GetString() ?? "",
                t.GetProperty("file").GetString() ?? "",
                t.GetProperty("status").GetString() ?? "",
                t.TryGetProperty("note", out var n) ? n.GetString() ?? "" : ""));
        }
        return rows;
    }

    /// <summary>一个待评审项（阶段 5 数值评审清单里的一节）及其主编号。</summary>
    private sealed record ReviewItem(string Title, string MainId);

    /// <summary>
    /// 阶段 5 数值评审清单里，**每一组要拍板的数**及其主编号。
    /// <para><b>主编号 = 「在 `07` 的编号」那一格里括号外的第一个编号</b> ——
    /// 括号里的是补充说明（如「P-08 掉落内容 / P-09 掉落条件」），
    /// 主编号才是「这组数等谁拍板」的那一个。</para>
    /// </summary>
    private static List<ReviewItem> ReviewItems()
    {
        string path = Path.Combine(RepoRoot(), ReviewRelPath.Replace('/', Path.DirectorySeparatorChar));
        string text = File.ReadAllText(path).Replace("\r\n", "\n");

        var headings = Regex.Matches(text, @"^##\s+\d+\.\s+(?<t>.+)$", RegexOptions.Multiline);
        var items = new List<ReviewItem>();

        for (int i = 0; i < headings.Count; i++)
        {
            int start = headings[i].Index;
            int end = i + 1 < headings.Count ? headings[i + 1].Index : text.Length;
            var section = text[start..end];

            var cell = Regex.Match(section,
                @"^\|\s*\*\*在\s*`07`\s*的编号\*\*\s*\|\s*(?<v>[^|]+)\|", RegexOptions.Multiline);
            if (!cell.Success) continue;

            var ids = Owner.Matches(cell.Groups["v"].Value).Select(x => x.Value).ToList();
            if (ids.Count == 0) continue;
            items.Add(new ReviewItem(headings[i].Groups["t"].Value.Trim(), ids[0]));
        }
        return items;
    }

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
        // ⚠️ **但要先剥掉「编号型 token」**（D-069 / P-14 / T-004 / I-16 / A-03）：
        // 那些是**决策与台账的编号**，不是参数值 ⇒ 不该被当成「过期的数」。
        // （真事：备注里写「D-069 拍板」⇒ 旧正则把 `D-069` 的 `69` 当成一个独立数字
        //   ⇒ 报「台账里没有这个数」——**误报**，而且它靠「运气」才没误报：
        //   `P-07` 里的 `07` 同理会被拆成 `7`。）
        var idLike = new Regex(@"\b[A-Z]{1,3}-\d{1,3}\b", RegexOptions.Compiled);
        var standalone = new Regex(@"(?<![A-Za-z\-])\d+(?:\.\d+)?", RegexOptions.Compiled);

        var stale = new List<string>();
        foreach (var row in ledger)
        {
            // 先把编号型 token 换成等长空格（保留位置，免得相邻数字被粘成一个）
            var note = idLike.Replace(row.Note, m => new string(' ', m.Length));
            foreach (Match m in standalone.Matches(note))
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

    /// <summary>
    /// <b>阶段 5 要评审的每一组数：主编号必须存在，而且必须被机器台账引用。</b>
    ///
    /// <para><b>它守的是什么</b>：那三组数（基础价格 / 掉落权重与次数 / 稀有度映射）
    /// 是第四阶段收口的**唯一前置**。而「评审清单上写着要定」与
    /// 「机器台账里看得见它」是**两件事** ——
    /// 前者是散文，后者才能在改动时喊一声。</para>
    ///
    /// <para><b>为什么这条不是「多写一行台账」的规矩</b>：它抓的是
    /// <b>「编号指错」</b>这种最难自己发现的错 ——
    /// 2026-10-21 实测：基础价格此前在 <b>四处</b>都被标成 <b>T-007</b>，
    /// 而 T-007 是「<b>存档是否保存随机数状态</b>」；
    /// 同时价格<b>在规则台账里根本没有编号</b>（P-01…P-13 覆盖不到它），
    /// 也<b>不在机器台账的表类参数里</b>。
    /// ⇒ 「T-007 在机器台账里一次都没被引用」这一条事实，
    /// 本守卫本来就能抓住那个错。</para>
    ///
    /// <para><b>两条判据</b>：
    /// <list type="number">
    /// <item>主编号必须<b>存在于</b>规则台账（<c>07</c> §2 的 P 行或 §3 的 T 行）。</item>
    /// <item>主编号必须<b>被机器台账引用</b>（标量条目或表条目都算）——
    /// 否则这组数「机器看不见」，改了也没人知道。</item>
    /// </list></para>
    /// </summary>
    [TestCase]
    public void StageFiveReviewNumbers_HaveAKnownId_ThatTheMachineLedgerTracks()
    {
        var parameterIds = RegisteredParameterIds();
        var ruleIds = RegisteredRuleIds();
        var ledger = LoadLedger();
        var tables = LoadTableRows();
        var items = ReviewItems();

        // 防空转：必须真的读到了三个待评审项，否则本条什么也没守。
        Assertions.AssertThat(items.Count).IsGreaterEqual(3);

        // 机器台账引用了哪些编号（标量 + 表）
        var tracked = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in ledger)
        {
            foreach (Match m in Owner.Matches(row.Status)) tracked.Add(m.Value);
        }
        foreach (var row in tables)
        {
            foreach (Match m in Owner.Matches(row.Status)) tracked.Add(m.Value);
        }

        var problems = new List<string>();
        foreach (var item in items)
        {
            bool known = item.MainId.StartsWith("P-", StringComparison.Ordinal)
                ? parameterIds.Contains(item.MainId)
                : ruleIds.Contains(item.MainId);
            if (!known)
            {
                problems.Add($"待评审项「{item.Title}」的主编号 {item.MainId} 在 `07` 的台账里没有这一行");
                continue;
            }
            if (!tracked.Contains(item.MainId))
            {
                problems.Add($"待评审项「{item.Title}」的主编号 {item.MainId} 在机器台账里没有任何条目引用它"
                             + " ⇒ 这组数机器看不见（改了也不会有人知道）");
            }
        }

        Assertions.AssertThat(string.Join(" | ", problems)).IsEmpty();
    }

    /// <summary>
    /// <b>表类条目也要有归属指针，而且指针不许悬空。</b>
    ///
    /// <para><b>为什么表也要管</b>：标量条目的归属指针早就有守卫盯着，
    /// 而<b>表类条目此前是豁免的</b> ——
    /// 于是「阶段 5 要评审的第一组数（基础价格）注册成了一张表」这件事，
    /// 恰好落进了<b>没人管的那块</b>：它没有编号、也不被任何检查覆盖。</para>
    ///
    /// <para><b>两条判据</b>：
    /// <list type="number">
    /// <item>【待定】/【占位】的表必须说清它服务于哪个 P / T / D。</item>
    /// <item>它引用的 <c>P-xx</c> 必须<b>真的存在于</b> <c>07</c> §2。</item>
    /// </list></para>
    /// </summary>
    [TestCase]
    public void TableRows_SayWhoTheyAreWaitingFor_AndTheirPointerIsNotDangling()
    {
        var parameterIds = RegisteredParameterIds();
        var tables = LoadTableRows();

        // 防空转：必须真的有表。
        Assertions.AssertThat(tables.Count).IsGreaterEqual(4);

        var problems = new List<string>();
        foreach (var row in tables)
        {
            if (row.Status.Contains("待定") || row.Status.Contains("占位"))
            {
                if (!Owner.IsMatch(row.Status))
                {
                    problems.Add($"表 `{row.Id}` 标着待定/占位，但没说自己服务于哪个 P/T/D");
                }
            }

            foreach (Match m in Owner.Matches(row.Status))
            {
                if (!m.Value.StartsWith("P-", StringComparison.Ordinal)) continue;
                if (!parameterIds.Contains(m.Value))
                {
                    problems.Add($"表 `{row.Id}` 指向 {m.Value}，但 `07` §2 没有这一行");
                }
            }
        }

        Assertions.AssertThat(string.Join(" | ", problems)).IsEmpty();
    }
}
