using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using GdUnit4;

namespace ClaudeCodeGameStudios.Tests;

/// <summary>
/// <b>文档引用守卫</b>：**活文档**里引用的测试文件 / 测试类 / 测试方法必须<b>真的存在</b>。
///
/// <para><b>它守的是什么</b>：验收计划（<c>06</c>）是「每条断言由哪个测试守着」的对照表，
/// 读者据此判断「这批验收到底有没有被测」。于是它到处点名具体测试 ——
/// 而**点名会过期**：有人把 <c>SaveVersion04Tests</c> 改名成 <c>SaveVersion06Tests</c>，
/// 或者把某个测试方法删了，文档里的引用就变成**指向空气的指针</b>，
/// 而**文档不会报错、测试也不会报错** —— 只有照着文档去找的人会发现扑空。</para>
///
/// <para><b>同类的真事已经发生过</b>：<c>production/qa/README.md</c> 曾把
/// <c>mutate-worldtable.py</c> 列为已有变异矩阵，而那个文件**从来不存在**。
/// 照着它跑的人只会得到「文件不存在」。现在那条由 <c>QaToolingTests</c> 守着。</para>
///
/// <para><b>只查「活文档」</b>：<b>历史文档不改</b>。
/// <c>10</c> / <c>12</c> / <c>13</c> / <c>14</c> / <c>28</c> 这些
/// 「某阶段的现状审计 / 开发记录」里引用过当时存在的测试文件，
/// 那些文件后来改名或合并了 —— 那是**当时的记录**，把它改成今天的样子就是篡改历史。
/// 本守卫只查三份**仍在维护**的文档：
/// <list type="bullet">
/// <item><c>06</c> 验收与测试计划（活表）</item>
/// <item><c>production/qa/README.md</c>（工具箱清单）</item>
/// <item><c>36</c> 当前实现参数清单（脚本生成）</item>
/// </list></para>
///
/// <para><b>三种引用都要查</b>：文件路径（<c>tests/unit/x/Y.cs</c>）、
/// 类名（<c>YTests</c>）、方法名（<c>YTests.Some_Method</c>）。
/// 只查前两种会漏掉「类还在、方法没了」这一种。</para>
/// </summary>
[TestSuite]
public class DocCitationTests
{
    /// <summary>仍在维护、因而**必须**指对的三份文档。</summary>
    private static readonly string[] LivingDocs =
    {
        "06_第一阶段验收与测试计划_V1.0.md",
        "36_当前实现参数清单_V1.0.md",
        "qa/README.md",
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

    private static string ProductionDir() => Path.Combine(RepoRoot(), "production");

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
        string testsRoot = Path.Combine(RepoRoot(), "tests");
        var files = Directory.EnumerateFiles(testsRoot, "*.cs", SearchOption.AllDirectories)
                             .Select(p => Path.GetRelativePath(RepoRoot(), p))
                             .Select(p => p.Replace('\\', '/'))
                             .ToHashSet(StringComparer.Ordinal);

        var fileRef = new Regex(@"`(?<f>tests/[A-Za-z0-9_\-/\.]+\.cs)`", RegexOptions.Compiled);
        var classRef = new Regex(@"`(?<c>[A-Za-z0-9_]+Tests)`", RegexOptions.Compiled);
        // ⚠️ 方法名用 `\w` 而不是 `[A-Za-z0-9_]` —— 后者**不认非 ASCII 字母**
        // ⇒ 一个含中文的方法名会让整段匹配不上 ⇒ 那一档检查**静默失效**。
        var methodRef = new Regex(@"`(?<c>[A-Za-z0-9_]+Tests)\.(?<m>\w+)`",
                                  RegexOptions.Compiled);

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

            // ① 文件路径
            foreach (Match m in fileRef.Matches(text))
            {
                checkedRefs++;
                string rel = m.Groups["f"].Value;
                if (files.Contains(rel)) continue;
                // ⚠️ **被 .gitignore 排除的路径不算「缺失」** —— 那是**本机工具**，
                // 不是仓库内容（例如 `tests/_spike_test.cs`：一个故意失败的探针，
                // 用来验证「测试失败时 runner 真的会退出 1」）。
                // 不豁免它的话，CI（干净检出）会红，而本机是绿的 ——
                // 同一个引用在两台机器上答案不同。
                if (IsGitIgnored(rel)) continue;
                offenders.Add($"{name} 引用了不存在的测试文件 {rel}");
            }

            // ② 类名
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

        // 防空转：必须真的扫到了引用，否则「一个都没查」也会「通过」。
        Assertions.AssertThat(checkedRefs).IsGreaterEqual(10);
        Assertions.AssertThat(string.Join(" | ", offenders)).IsEmpty();
    }

    /// <summary>
    /// 这个路径是不是被 <c>.gitignore</c> 明确排除 ⇒ 它是**本机工具**，不是仓库内容。
    /// <para><b>为什么用 <c>git check-ignore</c> 而不自己实现匹配</b>：
    /// gitignore 的语义（否定规则、目录、锚定、优先级）不是几十行能写对的，
    /// 而 <c>git check-ignore</c> 是**权威实现**。没有 git 就返回 false
    /// （退回到「文件必须存在」的严格判据 —— 宁可多报一条，也不放过一个真的空气）。</para>
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
