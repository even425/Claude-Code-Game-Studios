using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using GdUnit4;

namespace ClaudeCodeGameStudios.Tests.Architecture;

/// <summary>
/// <b>杂谈的事件类型不许与基线的十类名单脱节</b>（基线 §7.2，【已确认】级）。
///
/// <para><b>它守的是什么</b>：基线 §7.2「记什么——事件驱动」列了**十类**该进杂谈的事件。
/// 那十类里，代码里已实现的有五类；缺的五类**每一类都必须有交代**
/// —— 要么实现，要么在 <c>production/38_开发完成度盘点_V1.0.md</c> 的缺口表里**点名**。</para>
///
/// <para><b>为什么要有这条</b>：「已确认但没实现」本身不算错 —— 缺规则就得缺着。
/// 真正的错是<b>没人记得它缺</b>：读者看到「杂谈按事件驱动」，
/// 以为十类都齐了 ⇒ 而其中五类（纠纷 / 关系恶化 / 稀有装备 / 受伤康复 / 本土剧情）
/// **要么没有、要么连规则都没有** ⇒ 这正是「登记表会骗人」的那一类。</para>
///
/// <para><b>三处比对</b>：
/// <list type="number">
/// <item>基线 §7.2 的十类，<b>一类都不能少</b>（基线是我们唯一的尺）。</item>
/// <item>代码里已实现的事件类型，<b>每一个都要能对上十类里的某一类</b>
/// —— 否则说明实现了清单外的东西（不一定是错，但得有人知道）。</item>
/// <item>缺口表里登记为「未实现」的五类，<b>要真的没实现</b>
/// —— 一旦实现了，登记表必须跟着改（否则这份表自己就开始骗人）。</item>
/// </list></para>
///
/// <para><b>映射放在哪：代码的标注里，不在测试里</b>。
/// 每个事件类型声明旁都写着它对应基线哪一类（写在 <c>new("…")</c> 那行的行尾，形如
/// <c>// §7.2「基线类名」</c>），
/// <b>那行标注是唯一真源</b> —— 测试直接读它。
/// ⇒ 为什么不把映射表写死在测试里：那样就有**两份**映射（代码注释 + 测试清单），
/// 改一处忘另一处 ⇒ 守卫会拿旧清单去判新代码。
/// 现在只有一份，改代码时顺手看到标注，改完守卫自然跟着对。</para>
///
/// <para><b>为什么用「三处比对」而不是单看代码</b>：单看代码只能知道「现在有什么」，
/// 不知道「<b>该有什么</b>」；单看文档只能知道「说要什么」，不知道「真做了没有」。
/// 三处对起来，才知道「<b>还差哪几件、每件缺什么</b>」，
/// 以及——<b>文档正文自己写的数字对不对</b>。</para>
///
/// <para><b>它当场抓到的真错</b>：盘点文档 §6 的表是 5 个 ✅ / 5 个 ❌，
/// 而同一节的正文写着「<b>已实现 6 / 10</b>」「缺的 <b>4</b> 类」
/// —— **正文与它自己的表不一致**，而且「缺的 4 类」漏了「本土剧情进展」。
/// 这类错误人眼很难发现（表和段落各看一遍都觉得通），
/// 机器逐行核对一次就抓到了。</para>
/// </summary>
[TestSuite]
public class ChronicleEventCoverageTests
{
    private const string BaselineRelPath =
        "production/异世界村庄经营与英雄冒险游戏_设计基线_V1.2.md";
    private const string InventoryRelPath = "production/38_开发完成度盘点_V1.0.md";
    private const string SrcDirRelPath = "src";

    /// <summary>基线 §7.2 的事件类数（十类）。基线改了，这里要跟着改 —— 并给出理由。</summary>
    private const int ExpectedBaselineKindCount = 10;

    // ── 代码里的事件类型 ────────────────────────────────────────────────

    /// <summary>一个事件类型声明，以及它自带的 §7.2 归类标注。</summary>
    private sealed class CodeEventType
    {
        public string Id = "";
        public string File = "";
        public int Line;
        public bool HasAnnotation;
        public bool IsInternal;            // §7.2（内部…）—— 不入杂谈
        public List<string> KindLabels = new();   // 标注里点名的基线类（原文）
    }

    /// <summary>盘点文档 §6 缺口表的一行。</summary>
    private sealed class DocRow
    {
        public string Kind = "";
        public bool Implemented;
        public List<string> Identifiers = new();
        public string Missing = "";
    }

    // ── 读取工具 ────────────────────────────────────────────────────────

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
        File.ReadAllText(Path.Combine(RepoRoot(), rel.Replace('/', Path.DirectorySeparatorChar)));

    /// <summary>归一化：只留字母与数字（中文也算字母）⇒ 标点差异不影响比对。</summary>
    private static string Normalize(string s) =>
        new string(s.Where(char.IsLetterOrDigit).ToArray());

    // ── ① 基线：十类 ────────────────────────────────────────────────────

    /// <summary>基线 §7.2 的十类事件（表格第一列）。</summary>
    private static List<string> BaselineEventKinds()
    {
        var text = Read(BaselineRelPath);
        int start = text.IndexOf("### 7.2", StringComparison.Ordinal);
        int end = text.IndexOf("### 7.3", StringComparison.Ordinal);
        if (start < 0 || end < 0 || end <= start)
        {
            throw new InvalidOperationException("基线里找不到 §7.2 那一节");
        }

        var kinds = new List<string>();
        foreach (var line in text.Substring(start, end - start).Split('\n'))
        {
            if (!line.TrimStart().StartsWith("|")) continue;
            var cells = line.Split('|').Select(c => c.Trim()).ToArray();
            if (cells.Length < 3) continue;
            var name = cells[1];
            if (name.Length == 0 || name is "事件" or "---") continue;
            if (name.All(c => c == '-')) continue;
            kinds.Add(name);
        }
        return kinds;
    }

    // ── ② 代码：事件类型 + 归类标注 ────────────────────────────────────

    /// <summary>声明式写法：<c>EventTypeId EvX = new("Xxx");</c>（值可能换行）。</summary>
    private static readonly Regex DeclPattern = new(
        @"EventTypeId\s+\w+\s*=\s*new\s*\(\s*""(?<v>[^""]+)""", RegexOptions.Singleline);

    /// <summary>内联写法：<c>new EventTypeId("Xxx")</c>。</summary>
    private static readonly Regex CtorPattern = new(
        @"new\s+EventTypeId\s*\(\s*""(?<v>[^""]+)""", RegexOptions.Singleline);

    /// <summary>标注里的基线类名：<c>「…」</c>（可多个，用 ＋ 连接）。</summary>
    private static readonly Regex KindLabelPattern = new(@"「(?<k>[^」]+)」");

    /// <summary>
    /// 「这条事件类型不进杂谈」的**精确标记**。
    /// <para><b>为什么要用精确标记，而不是「含『内部』二字」</b>：守卫是**扫注释**的 ——
    /// 注释里写什么都会被扫到。写成「含『内部』就当内部事件」时，
    /// 一句 `// §7.2（内部动作事件）—— 动作的「开始 / 结束」是内部信号`
    /// 里的 <c>「开始 / 结束」</c> 会被当成基线类名（**真事：守卫第一次跑就红了**），
    /// 而 `// §7.2「村庄日常」—— 内部信号` 又会被误判成内部事件。
    /// ⇒ 必须是**一个不会被别的词组冒充的整串标记**。</para>
    /// </summary>
    private const string InternalMarker = "§7.2（内部）";

    private static List<CodeEventType> CodeEventTypes()
    {
        var result = new List<CodeEventType>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var srcDir = Path.Combine(RepoRoot(), SrcDirRelPath.Replace('/', Path.DirectorySeparatorChar));

        foreach (var file in Directory.EnumerateFiles(srcDir, "*.cs", SearchOption.AllDirectories))
        {
            var text = File.ReadAllText(file);
            var lines = text.Split('\n');
            var rel = Path.GetRelativePath(RepoRoot(), file);

            foreach (var pattern in new[] { DeclPattern, CtorPattern })
            {
                foreach (Match m in pattern.Matches(text))
                {
                    var valueGroup = m.Groups["v"];
                    int lineNo = text.Substring(0, valueGroup.Index).Count(c => c == '\n') + 1;
                    string key = $"{rel}:{lineNo}:{valueGroup.Value}";
                    if (!seen.Add(key)) continue;

                    var item = new CodeEventType
                    {
                        Id = valueGroup.Value,
                        File = rel,
                        Line = lineNo,
                    };

                    var annotation = FindAnnotation(lines, lineNo - 1);
                    if (annotation is not null)
                    {
                        item.HasAnnotation = true;
                        if (annotation.Contains(InternalMarker, StringComparison.Ordinal))
                        {
                            // ⚠️ 内部标记**优先**：这一行里就算还有别的「…」，也不当作基线类名
                            item.IsInternal = true;
                        }
                        else
                        {
                            item.KindLabels.AddRange(
                                KindLabelPattern.Matches(annotation).Select(x => x.Groups["k"].Value));
                        }
                    }

                    result.Add(item);
                }
            }
        }
        return result;
    }

    /// <summary>
    /// 找事件类型声明旁的 <c>§7.2</c> 标注。
    /// <para><b>为什么上一行要排除含 <c>new(</c> 的行</b>：这份代码里声明是**两行**写的
    /// （类型名一行、<c>new("X")</c> 一行），上一行往往正是**上一条声明的 <c>new(</c>** ——
    /// 它上面的标注属于**别人**。不排除这一条，SquadBonded 就会把 HeroSocialized 的
    /// 归类当成自己的。</para>
    /// </summary>
    private static string? FindAnnotation(string[] lines, int lineIdx)
    {
        if (lineIdx < 0 || lineIdx >= lines.Length) return null;
        var same = lines[lineIdx];
        if (same.Contains("§7.2", StringComparison.Ordinal)) return same;

        if (lineIdx - 1 >= 0)
        {
            var prev = lines[lineIdx - 1];
            if (prev.Contains("§7.2", StringComparison.Ordinal)
                && !prev.Contains("new(", StringComparison.Ordinal))
            {
                return prev;
            }
        }
        return null;
    }

    // ── ③ 盘点文档：缺口表 ──────────────────────────────────────────────

    private static (List<DocRow> Rows, List<string> Problems) ParseDocRows()
    {
        var problems = new List<string>();
        var rows = new List<DocRow>();

        var text = Read(InventoryRelPath);
        int start = text.IndexOf("## 6. §7.2", StringComparison.Ordinal);
        if (start < 0) throw new InvalidOperationException("盘点文档里找不到 §6 那一节");
        int end = text.IndexOf("## 7.", start, StringComparison.Ordinal);
        if (end < 0) end = text.Length;

        foreach (var line in text.Substring(start, end - start).Split('\n'))
        {
            if (!line.TrimStart().StartsWith("|")) continue;
            var cells = line.Split('|').Select(c => c.Trim()).ToArray();
            if (cells.Length < 4) continue;

            // ⚠️ 第一列是**序号** ⇒ 只有序号是纯数字的行才是缺口表的行
            //    （同一节里还有一张「依赖关系」表，它的第一列是「5 英雄受伤 / 康复」这种文字）。
            if (!Regex.IsMatch(cells[1], @"^\d+$")) continue;

            var kind = cells[2];
            if (kind.Length == 0 || kind.All(c => c == '-')) continue;

            var row = new DocRow { Kind = kind };
            var status = cells[3];
            if (status.StartsWith("✅", StringComparison.Ordinal))
            {
                row.Implemented = true;
            }
            else if (status.StartsWith("❌", StringComparison.Ordinal))
            {
                row.Implemented = false;
            }
            else
            {
                // ⚠️ 不认识的标记 ⇒ **报错，不猜**（与本仓库其它守卫同一条原则）。
                problems.Add($"第 {cells[1]} 行（{kind}）的状态标记「{status}」不认识"
                             + " —— 只认 ✅ / ❌，新增标记要显式承认");
                continue;
            }

            row.Identifiers.AddRange(
                Regex.Matches(status, "`(?<i>[^`]+)`").Select(x => x.Groups["i"].Value));
            row.Missing = cells.Length > 4 ? cells[4] : "";
            rows.Add(row);
        }
        return (rows, problems);
    }

    // ── ④ 归类解析 ──────────────────────────────────────────────────────

    /// <summary>
    /// 把一个标注里的类名解析成基线里的那一类。
    /// <para><b>为什么用「包含」而不是「完全相等」</b>：文档与代码里的写法会有标点差异
    /// （如「村庄日常（采集、摆摊、社交等）」与「村庄日常（采集/摆摊/社交）」），
    /// 归一化后仍可能长短不一 ⇒ 允许**一方包含另一方**，
    /// 但要求**唯一命中** —— 命中 0 个或多个都算错（不猜）。</para>
    /// </summary>
    private static string? ResolveKind(string label, List<string> baseline)
    {
        var n = Normalize(label);
        if (n.Length == 0) return null;
        var hits = baseline.Where(b =>
        {
            var nb = Normalize(b);
            return nb.Contains(n, StringComparison.Ordinal)
                || n.Contains(nb, StringComparison.Ordinal);
        }).ToList();
        return hits.Count == 1 ? hits[0] : null;
    }

    /// <summary>把代码事件类型解析成「基线类 → 事件类型标识集合」。</summary>
    private static (Dictionary<string, HashSet<string>> ByKind,
                     Dictionary<string, string> Unresolved,
                     List<string> Problems) MapCodeToKinds()
    {
        var byKind = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        var unresolved = new Dictionary<string, string>(StringComparer.Ordinal);
        var problems = new List<string>();
        var baseline = BaselineEventKinds();

        foreach (var t in CodeEventTypes())
        {
            if (!t.HasAnnotation)
            {
                problems.Add($"{t.File}:{t.Line} 的事件类型 `{t.Id}` 没有 §7.2 归类标注"
                             + " —— 新增事件类型必须写清它对应基线哪一类");
                continue;
            }
            if (t.IsInternal) continue;   // 内部动作事件，不入杂谈

            if (t.KindLabels.Count == 0)
            {
                problems.Add($"{t.File}:{t.Line} 的事件类型 `{t.Id}` 的标注里没有「基线类名」"
                             + "（要么写 `§7.2「基线类名」`，要么写 `§7.2（内部…）`）");
                continue;
            }

            foreach (var label in t.KindLabels)
            {
                var kind = ResolveKind(label, baseline);
                if (kind is null)
                {
                    var key = $"{t.File}:{t.Line}:{label}";
                    if (!unresolved.ContainsKey(key))
                    {
                        unresolved[key] = label;
                        problems.Add($"{t.File}:{t.Line} 的事件类型 `{t.Id}` 标注的「{label}」"
                                     + $"对不上基线 §7.2 的任何一类（基线十类：{string.Join(" / ", baseline)}）");
                    }
                    continue;
                }
                if (!byKind.TryGetValue(kind, out var set))
                {
                    set = new HashSet<string>(StringComparer.Ordinal);
                    byKind[kind] = set;
                }
                set.Add(t.Id);
            }
        }
        return (byKind, unresolved, problems);
    }

    // ── 断言 ────────────────────────────────────────────────────────────

    /// <summary>
    /// 基线 §7.2 的十类，<b>一类都不能少</b>。
    /// <para><b>为什么这条最基本</b>：基线是我们唯一的尺。尺子被改动而没人知道，
    /// 后面所有比对都失去意义。</para>
    /// </summary>
    [TestCase]
    public void Baseline_ListsTenEventKinds_AndWeCompareAgainstAllOfThem()
    {
        var kinds = BaselineEventKinds();

        // ⚠️ GdUnit4 这一版**没有**带消息的断言重载 ⇒ 把诊断信息放进被断言的值里。
        Assertions.AssertThat(kinds.Count == ExpectedBaselineKindCount
                ? ""
                : $"基线 §7.2 的事件类数变了（现在 {kinds.Count} 条，期望 {ExpectedBaselineKindCount} 条）"
                  + " —— 守卫的尺子必须跟着更新，否则下面的比对是拿旧尺子量新东西")
            .IsEmpty();

        var dup = kinds.GroupBy(k => Normalize(k))
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();
        Assertions.AssertThat(dup.Count == 0
                ? ""
                : $"基线 §7.2 的事件类名有重复：{string.Join(" / ", dup)}")
            .IsEmpty();
    }

    /// <summary>
    /// 缺口表必须**十类都在、一类一行**。
    /// <para><b>为什么</b>：这张表是「还差什么」的**唯一**视图。
    /// 少一行 ⇒ 那一类从视野里消失；多一行 ⇒ 表里出现了基线没有的东西。</para>
    /// </summary>
    [TestCase]
    public void GapTable_CoversEveryBaselineKind_ExactlyOnce()
    {
        var (rows, problems) = ParseDocRows();
        var baseline = BaselineEventKinds();

        // 解析出来的问题（不认识的状态标记）也要报出来
        var resolved = new List<(DocRow Row, string Kind)>();
        foreach (var r in rows)
        {
            var kind = ResolveKind(r.Kind, baseline);
            if (kind is null)
            {
                problems.Add($"缺口表里的「{r.Kind}」对不上基线 §7.2 的任何一类"
                             + $"（基线十类：{string.Join(" / ", baseline)}）");
                continue;
            }
            resolved.Add((r, kind));
        }

        Assertions.AssertThat(problems.Count == 0
                ? ""
                : string.Join("；", problems))
            .IsEmpty();

        var missing = baseline.Where(b => !resolved.Any(x => x.Kind == b)).ToList();
        Assertions.AssertThat(missing.Count == 0
                ? ""
                : $"缺口表里没有这些基线类：{string.Join(" / ", missing)}"
                  + " —— 十类必须一类一行，缺了它们就从「还差什么」的视野里消失了")
            .IsEmpty();

        var extra = resolved.Select(x => x.Kind).Distinct()
            .Where(k => !baseline.Contains(k, StringComparer.Ordinal)).ToList();
        Assertions.AssertThat(extra.Count == 0
                ? ""
                : $"缺口表里出现了基线 §7.2 没有的类：{string.Join(" / ", extra)}")
            .IsEmpty();

        var dup = resolved.GroupBy(x => x.Kind).Where(g => g.Count() > 1)
            .Select(g => g.Key).ToList();
        Assertions.AssertThat(dup.Count == 0
                ? ""
                : $"缺口表里有重复的行：{string.Join(" / ", dup)} —— 一类只能有一行")
            .IsEmpty();
    }

    /// <summary>
    /// 代码里每个事件类型，<b>都要能对上基线十类里的某一类</b>。
    /// <para><b>为什么</b>：实现了清单外的东西不一定错，但<b>得有人知道</b> ——
    /// 否则 §7.2 的名单就悄悄失效了。
    /// 而「知道」这件事必须**写在代码里**：写在测试里就多了一份要同步的副本。</para>
    /// </summary>
    [TestCase]
    public void EveryImplementedEventType_IsClassifiedAgainstTheBaseline()
    {
        var (_, _, problems) = MapCodeToKinds();

        Assertions.AssertThat(problems.Count == 0
                ? ""
                : string.Join("；", problems))
            .IsEmpty();
    }

    /// <summary>
    /// 缺口表标「已实现」与「未实现」，<b>都要与代码实况一致</b>。
    /// <para><b>为什么这条是核心</b>：这张表自己也会骗人 ——
    /// 一旦某类实现了而表没改，读者会以为还缺着 ⇒ 于是跳过它；
    /// 反过来，表里标着 ✅ 而代码里没有 ⇒ 读者以为不用做。
    /// 这正是本仓库反复治的病（「登记了的事，也要有人核对它是不是还成立」）。</para>
    /// </summary>
    [TestCase]
    public void GapTable_Status_MatchesWhatTheCodeActuallyDoes()
    {
        var (rows, _) = ParseDocRows();
        var (byKind, _, _) = MapCodeToKinds();
        var baseline = BaselineEventKinds();
        var problems = new List<string>();

        foreach (var r in rows)
        {
            var kind = ResolveKind(r.Kind, baseline);
            if (kind is null) continue;   // 已由上一条断言负责报
            var actual = byKind.TryGetValue(kind, out var set)
                ? new HashSet<string>(set, StringComparer.Ordinal)
                : new HashSet<string>(StringComparer.Ordinal);

            if (r.Implemented && actual.Count == 0)
            {
                problems.Add($"「{kind}」在缺口表里标着 ✅ 已实现，但代码里**没有任何**事件类型归到它");
            }
            if (!r.Implemented && actual.Count > 0)
            {
                problems.Add($"「{kind}」在缺口表里标着 ❌ 未实现，但代码里已经有："
                             + $"{string.Join(" / ", actual)} ⇒ 缺口表要改（否则它自己开始骗人）");
            }

            // ✅ 行点名的标识集合，必须**恰好**等于代码里归到该类的标识集合
            var named = new HashSet<string>(r.Identifiers, StringComparer.Ordinal);
            if (!named.SetEquals(actual))
            {
                problems.Add($"「{kind}」点名的标识 {Describe(named)} 与代码实况 {Describe(actual)} 不一致"
                             + " —— 文档要写全：实现了哪些事件类型，一个都不能少、一个都不能多");
            }
        }

        Assertions.AssertThat(problems.Count == 0
                ? ""
                : string.Join("；", problems))
            .IsEmpty();
    }

    private static string Describe(IEnumerable<string> ids)
    {
        var list = ids.OrderBy(x => x, StringComparer.Ordinal).ToList();
        return list.Count == 0 ? "（空）" : string.Join(" / ", list);
    }

    /// <summary>
    /// 标着「未实现」的行，<b>必须写清缺的是什么</b>。
    /// <para><b>为什么</b>：只写一个 ❌ 等于「这里有洞，但没人知道为什么」——
    /// 那和没登记是一回事。要缺规则就明说缺哪条规则。</para>
    /// </summary>
    [TestCase]
    public void MissingKinds_MustSayWhatIsMissing()
    {
        var (rows, _) = ParseDocRows();
        var problems = new List<string>();

        foreach (var r in rows.Where(x => !x.Implemented))
        {
            var text = r.Missing.Trim();
            if (text.Length == 0 || text == "—")
            {
                problems.Add($"「{r.Kind}」标着未实现，但没写缺的是什么");
            }
        }

        Assertions.AssertThat(problems.Count == 0
                ? ""
                : string.Join("；", problems))
            .IsEmpty();
    }

    /// <summary>
    /// §6 正文里写的**数字**，必须与它自己的表格一致。
    /// <para><b>为什么</b>：正文那句「已实现 N / 10」是读者最常看的一句
    /// （它比表格更显眼，也更容易被单独引用）。
    /// 本守卫写出来之前，那句写的是「<b>6 / 10</b>」「缺的 <b>4</b> 类」，
    /// 而它**自己的表**是 5 个 ✅ / 5 个 ❌ ⇒ 正文在说谎。</para>
    /// </summary>
    [TestCase]
    public void SectionProse_Counts_MatchItsOwnTable()
    {
        var (rows, _) = ParseDocRows();
        var text = Read(InventoryRelPath);
        int start = text.IndexOf("## 6. §7.2", StringComparison.Ordinal);
        int end = text.IndexOf("## 7.", start, StringComparison.Ordinal);
        if (end < 0) end = text.Length;
        var section = text.Substring(start, end - start);

        var implemented = rows.Count(r => r.Implemented);
        var missing = rows.Count(r => !r.Implemented);
        var baselineCount = BaselineEventKinds().Count;
        var problems = new List<string>();

        // 「已实现 N / M」
        var m = Regex.Match(section, @"已实现\s*\**\s*(\d+)\s*/\s*(\d+)");
        if (!m.Success)
        {
            problems.Add("§6 正文里找不到「已实现 N / M」这句话 —— 守卫无法核对正文数字"
                         + "（请保留这个写法，或同步改守卫）");
        }
        else
        {
            var said = int.Parse(m.Groups[1].Value);
            var total = int.Parse(m.Groups[2].Value);
            if (said != implemented)
            {
                problems.Add($"§6 正文写「已实现 {said} / {total}」，"
                             + $"但它自己的表是 {implemented} 个 ✅ ⇒ 正文在说谎");
            }
            if (total != baselineCount)
            {
                problems.Add($"§6 正文写的分母是 {total}，基线 §7.2 是 {baselineCount} 类");
            }
        }

        // 「缺的 N 类」
        var g = Regex.Match(section, @"缺[的是]{0,3}\s*\**\s*(\d+)\s*\**\s*类");
        if (!g.Success)
        {
            problems.Add("§6 正文里找不到「缺的 N 类」这句话 —— 守卫无法核对正文数字"
                         + "（请保留这个写法，或同步改守卫）");
        }
        else
        {
            var said = int.Parse(g.Groups[1].Value);
            if (said != missing)
            {
                problems.Add($"§6 正文写「缺的 {said} 类」，但它自己的表是 {missing} 个 ❌ ⇒ 正文在说谎");
            }
        }

        Assertions.AssertThat(problems.Count == 0
                ? ""
                : string.Join("；", problems))
            .IsEmpty();
    }
}
