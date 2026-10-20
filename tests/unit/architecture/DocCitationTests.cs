using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using GdUnit4;

namespace ClaudeCodeGameStudios.Tests;

/// <summary>
/// <b>文档引用守卫</b>：**活文档**里引用的测试文件 / 测试类 / 测试方法 / 仓库路径
/// 必须<b>真的存在</b>，而且必须**在版本库里**。
///
/// <para><b>它守的是什么</b>：验收计划（<c>06</c>）是「每条断言由哪个测试守着」的对照表，
/// 读者据此判断「这批验收到底有没有被测」。于是它到处点名具体测试与具体文件 ——
/// 而<b>点名会过期</b>：有人把 <c>SaveVersion04Tests</c> 改名成 <c>SaveVersion06Tests</c>，
/// 或者把某个测试方法删了，文档里的引用就变成<b>指向空气的指针</b>，
/// 而<b>文档不会报错、测试也不会报错</b> —— 只有照着文档去找的人会发现扑空。</para>
///
/// <para><b>为什么判据是「在版本库里」而不是「在磁盘上」</b>：
/// 「本机的工作副本」≠「仓库」≠「CI 的干净检出」，三者可以差得很远。
/// 真事：<c>06</c> 引用的 <c>tests/_spike_test.cs</c>（一个故意失败的探针）
/// 被 <c>.gitignore</c> 排除 ⇒ <b>本机有、干净检出里没有</b>
/// ⇒ 同一个引用在两台机器上答案不同：<b>本机全绿、CI 全红</b>。
/// 而更隐蔽的一种是<b>大小写不一致</b>：<c>07</c> 里同一个文件一会儿写
/// <c>src/Data/SaveGame.cs</c>、一会儿写 <c>src/data/SaveGame.cs</c> ——
/// Windows 上两者都能打开，Linux 上只有一种能。</para>
///
/// <para><b>四种引用都要查</b>：
/// <list type="number">
/// <item>仓库路径（<c>production/…</c> / <c>src/…</c> / <c>config/…</c> / <c>tests/…</c>）</item>
/// <item>测试文件路径（上面那类的子集，单列出来是为了报错更直白）</item>
/// <item>测试类名（<c>YTests</c>）</item>
/// <item>测试方法名（<c>YTests.Some_Method</c>）—— 只查前两种会漏掉「类还在、方法没了」</item>
/// </list></para>
///
/// <para><b>只查「活文档」</b>：<b>历史文档不改</b>。
/// <c>10</c> / <c>12</c> / <c>13</c> / <c>14</c> / <c>28</c> 这些
/// 「某阶段的现状审计 / 开发记录」引用过当时存在的文件，那些文件后来改名或合并了 ——
/// 那是<b>当时的记录</b>，把它改成今天的样子就是篡改历史。
/// <c>07</c> 是个混合体：它既有决策记录，也有<b>还开着的债表</b> ——
/// 后者必须经得起核对，前者不参与。</para>
/// </summary>
[TestSuite]
public class DocCitationTests
{
    /// <summary>仍在维护、因而**必须**指对的文档（全文参与核对）。</summary>
    private static readonly string[] LivingDocs =
    {
        "06_第一阶段验收与测试计划_V1.0.md",
        "36_当前实现参数清单_V1.0.md",
        "qa/README.md",
    };

    /// <summary>
    /// 决策台账里<b>还开着的债表</b>（§4.1–§4.4 + §6）。
    /// ⚠️ 「## 4. 技术债台账」这个大标题底下还挂着十几份<b>决策记录</b>
    /// （D-002 / D-003 / …），它们引用过当时存在的文件（作废的、已删的、临时探针）——
    /// 那是<b>历史</b>，不该拿来核对现状。
    /// </summary>
    private const string DecisionDoc = "07_技术决策与待确认事项.md";

    /// <summary>行里出现这些词 ⇒ 它描述的是<strong>历史</strong>，不参与现状核对。</summary>
    private static readonly string[] ClosedMarkers = { "已清", "已关闭", "已过期", "已修" };

    /// <summary>行里出现这些词 ⇒ 它还在开着，它的描述必须是真的。</summary>
    private static readonly string[] OpenMarkers = { "仍开", "未处理", "未完成", "待定" };

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

    private static string ProductionDir() => Path.Combine(RepoRoot(), "production");

    private static string DecisionText()
    {
        string path = Path.Combine(ProductionDir(), DecisionDoc);
        return File.ReadAllText(path).Replace("\r\n", "\n");
    }

    /// <summary>扫出测试工程里的类名 → 源文件。</summary>
    private static Dictionary<string, string> TestClasses()
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var file in Directory.EnumerateFiles(
                     Path.Combine(RepoRoot(), "tests"), "*.cs", SearchOption.AllDirectories))
        {
            string text = File.ReadAllText(file);
            foreach (Match m in Regex.Matches(text, @"\bclass\s+([A-Za-z0-9_]+)"))
            {
                map.TryAdd(m.Groups[1].Value, file);
            }
        }
        return map;
    }

    [TestCase]
    public void LivingDocs_CiteOnlyTestsThatActuallyExist()
    {
        var classes = TestClasses();
        var tracked = TrackedFiles();

        var fileRef = new Regex(@"`(?<f>tests/[A-Za-z0-9_\-/\.]+\.cs)`", RegexOptions.Compiled);
        var pathRef = new Regex(
            @"`(?<p>(?:production|src|tests|config|docs|design|\.claude)/[A-Za-z0-9_\-/\.]*\.[A-Za-z0-9]+)`",
            RegexOptions.Compiled);
        var classRef = new Regex(@"`(?<c>[A-Za-z0-9_]+Tests)`", RegexOptions.Compiled);
        // ⚠️ 方法名用 `\w` 而不是 `[A-Za-z0-9_]` —— 后者**不认非 ASCII 字母**
        // ⇒ 一个含中文的方法名会让整段匹配不上 ⇒ 那一档检查**静默失效**。
        var methodRef = new Regex(@"`(?<c>[A-Za-z0-9_]+Tests)\.(?<m>\w+)`", RegexOptions.Compiled);

        var offenders = new List<string>();
        int checkedRefs = 0;

        foreach (var name in LivingDocs)
        {
            string path = Path.Combine(ProductionDir(), name.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(path))
            {
                offenders.Add($"活文档 {name} 不存在 —— 守卫的范围本身过期了");
                continue;
            }
            string text = File.ReadAllText(path);

            // ① 仓库路径：必须在版本库里（**大小写精确**），或是被 gitignore 排除的本机工具
            foreach (Match m in pathRef.Matches(text))
            {
                checkedRefs++;
                string rel = m.Groups["p"].Value;
                if (tracked.Contains(rel)) continue;
                if (IsGitIgnored(rel)) continue;
                offenders.Add($"{name} 引用了不在版本库里的路径 {rel}");
            }

            // ② 测试类名
            foreach (Match m in classRef.Matches(text))
            {
                checkedRefs++;
                string cls = m.Groups["c"].Value;
                if (!classes.ContainsKey(cls)) offenders.Add($"{name} 引用了不存在的测试类 {cls}");
            }

            // ③ 类.方法
            foreach (Match m in methodRef.Matches(text))
            {
                checkedRefs++;
                string cls = m.Groups["c"].Value;
                string method = m.Groups["m"].Value;
                if (!classes.TryGetValue(cls, out string? source))
                {
                    offenders.Add($"{name} 引用了 {cls}.{method}，但类 {cls} 不存在");
                    continue;
                }
                string body = File.ReadAllText(source);
                if (!Regex.IsMatch(body, $@"\b{Regex.Escape(method)}\s*\("))
                {
                    offenders.Add($"{name} 引用了 {cls}.{method}，但这个方法在 {source} 里找不到");
                }
            }
        }

        // ④ 决策台账里**还开着**的行 —— 同样必须指对
        foreach (string line in OpenRowsOfDecisionDoc())
        {
            foreach (Match m in pathRef.Matches(line))
            {
                checkedRefs++;
                string rel = m.Groups["p"].Value;
                if (tracked.Contains(rel)) continue;
                if (IsGitIgnored(rel)) continue;
                offenders.Add($"{DecisionDoc}（还开着的行）引用了不在版本库里的路径 {rel}");
            }
        }

        // 防空转：必须真的扫到了引用，否则「一个都没查」也会「通过」。
        Assertions.AssertThat(checkedRefs).IsGreaterEqual(10);
        Assertions.AssertThat(string.Join(" | ", offenders)).IsEmpty();
    }

    /// <summary>
    /// 决策台账里<b>还开着的行</b>（债表 §4.1–§4.4 + 遗留事项 §6）。
    /// <para><b>已清的行不参与</b> —— 那是对<b>当时</b>的记录，改它就是篡改历史。</para>
    /// </summary>
    private static IEnumerable<string> OpenRowsOfDecisionDoc()
    {
        string text = DecisionText();

        int s41 = text.IndexOf("### 4.1 ", StringComparison.Ordinal);
        int s44 = text.IndexOf("### 4.4 ", StringComparison.Ordinal);
        int sD3 = text.IndexOf("### D-003", s44, StringComparison.Ordinal);
        int s6 = text.IndexOf("## 6. 遗留事项", StringComparison.Ordinal);

        if (s41 < 0 || s44 < 0) yield break;

        var scope = text[s41..s44] + text[s44..(sD3 > 0 ? sD3 : text.Length)];
        if (s6 >= 0) scope += "\n" + text[s6..];

        foreach (string raw in scope.Split('\n'))
        {
            string line = raw.TrimStart();
            if (line.StartsWith("> ", StringComparison.Ordinal)) line = line[2..].TrimStart();
            if (!line.StartsWith("|", StringComparison.Ordinal)) continue;
            if (ClosedMarkers.Any(c => line.Contains(c))) continue;
            if (!OpenMarkers.Any(c => line.Contains(c))) continue;
            yield return line;
        }
    }

    /// <summary>
    /// 仓库里**被追踪**的文件（<c>git ls-files</c>）—— 大小写精确、就是「CI 检出的那份」。
    /// <para>没有 git 就退回「磁盘上的文件」—— 宁可多报一条，也不放过一个真的空气。</para>
    /// </summary>
    private static HashSet<string> TrackedFiles()
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo("git", "ls-files")
            {
                WorkingDirectory = RepoRoot(),
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var proc = System.Diagnostics.Process.Start(psi);
            if (proc is null) return FilesOnDisk();
            string stdout = proc.StandardOutput.ReadToEnd();
            proc.StandardError.ReadToEnd();
            proc.WaitForExit();
            if (proc.ExitCode != 0) return FilesOnDisk();
            return stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                          .Select(p => p.Trim().Replace('\\', '/'))
                          .ToHashSet(StringComparer.Ordinal);
        }
        catch
        {
            return FilesOnDisk();
        }
    }

    private static HashSet<string> FilesOnDisk()
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in Directory.EnumerateFiles(RepoRoot(), "*",
                                                      SearchOption.AllDirectories))
        {
            if (file.Contains("\\.git\\") || file.Contains("\\bin\\") || file.Contains("\\obj\\"))
                continue;
            set.Add(Path.GetRelativePath(RepoRoot(), file).Replace('\\', '/'));
        }
        return set;
    }

    /// <summary>
    /// 这个路径是不是被 <c>.gitignore</c> 明确排除 ⇒ 它是<b>本机工具</b>，不是仓库内容。
    /// <para><b>为什么用 <c>git check-ignore</c> 而不自己实现匹配</b>：
    /// gitignore 的语义（否定规则、目录、锚定、优先级）不是几十行能写对的，
    /// 而 <c>git check-ignore</c> 是<b>权威实现</b>。没有 git 就返回 false
    /// （退回到严格判据 —— 宁可多报一条，也不放过一个真的空气）。</para>
    /// </summary>
    private static bool IsGitIgnored(string relPath)
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo(
                "git", $"check-ignore -q -- \"{relPath}\"")
            {
                WorkingDirectory = RepoRoot(),
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var proc = System.Diagnostics.Process.Start(psi);
            if (proc is null) return false;
            proc.StandardOutput.ReadToEnd();
            proc.StandardError.ReadToEnd();
            proc.WaitForExit();
            return proc.ExitCode == 0;   // 0 = 被忽略；1 = 没被忽略；128 = 出错
        }
        catch
        {
            return false;
        }
    }
}
