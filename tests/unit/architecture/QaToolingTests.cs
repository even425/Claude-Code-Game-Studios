using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using GdUnit4;

namespace ClaudeCodeGameStudios.Tests;

/// <summary>
/// <b>QA 工具箱守卫</b>：<c>production/qa/</c> 里的工具必须<b>跑得起来</b>，
/// 且 <c>production/qa/README.md</c> 必须与磁盘上的文件<b>对得上</b>。
///
/// <para><b>它守的是一件很具体、而且已经发生过的事</b>：
/// 本机 Windows 控制台默认是 <b>GBK（cp936）</b>，
/// 而脚本里满是「✓ / ✗ / ⇒ / ✅」这类符号 —— <b>GBK 里没有它们</b>
/// ⇒ <c>print</c> 抛 <c>UnicodeEncodeError</c> ⇒
/// <c>run-selfchecks.py</c> 跑到<b>第一个通过项</b>就崩，
/// 后面 6 个自检<b>一个都没跑</b>，而它自己最后那句「全部通过」压根没机会打印。
///
/// <para><b>更值得警惕的是</b>：这种崩溃发生在<b>守卫自己身上</b> ——
/// 「工具跑不动」和「工具跑了但没发现问题」在日志上看起来一模一样。
/// 所以这条守卫不是给脚本的，是给<b>「工具确实被跑过」</b>这件事兜底的。</para>
///
/// <para><b>四条断言</b>：
/// <list type="number">
/// <item>每个 QA 脚本都必须 <c>import _utf8out</c>（否则换台机器 / 换个控制台就崩）</item>
/// <item>README 的「变异矩阵一览」必须与磁盘上的 <c>mutate-*.py</c> <b>一一对应</b>
/// （新增脚本忘了登记 ⇒ 红；登记了不存在的脚本 ⇒ 红）</item>
/// <item>README 首表里点名的工具必须<b>真的存在</b>（文档不许指向空气）</item>
/// <item>README 写的自检数量必须等于 <c>run-selfchecks.py</c> 里 <c>CHECKS</c> 的条数</item>
/// </list></para>
///
/// <para><b>为什么不用「跑一遍每个脚本」来代替</b>：那些脚本会改文件、跑引擎、要显示器，
/// 慢且有副作用；这里要守的是<b>「清单与现实一致」</b>，静态检查足够且确定。</para>
/// </summary>
[TestSuite]
public class QaToolingTests
{
    private const string QaDirRelPath = "production/qa";
    private const string ReadmeRelPath = "production/qa/README.md";

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

    private static string QaDir() =>
        Path.Combine(RepoRoot(), QaDirRelPath.Replace('/', Path.DirectorySeparatorChar));

    private static string ReadmeText() =>
        File.ReadAllText(Path.Combine(RepoRoot(), ReadmeRelPath.Replace('/', Path.DirectorySeparatorChar)));

    private static List<string> QaScripts() =>
        Directory.EnumerateFiles(QaDir(), "*.py", SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileName)
            .Where(n => n is not null)
            .Select(n => n!)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

    // ── 断言 1：每个脚本都得先让输出说得出来 ────────────────────────────

    /// <summary>
    /// 每个 QA 脚本都必须 <c>import _utf8out</c> ⇒ <b>控制台是 GBK 时不会直接崩</b>。
    /// <para><b>防空转</b>：本断言先确认「扫到的脚本不是空的」——
    /// 否则「一条都没扫到」也会「通过」，那这条守卫就是个摆设。</para>
    /// </summary>
    [TestCase]
    public void EveryQaScript_ImportsUtf8Out()
    {
        var scripts = QaScripts();

        // 防空转：扫到的脚本必须不为空，且至少包含两个已知脚本。
        Assertions.AssertThat(scripts.Count).IsGreaterEqual(2);
        Assertions.AssertThat(scripts.Contains("run-selfchecks.py")).IsTrue();

        var missing = scripts
            .Where(n => n != "_utf8out.py")
            .Where(n => !File.ReadAllText(Path.Combine(QaDir(), n)).Contains("import _utf8out"))
            .Select(n => $"{n} 没有 import _utf8out")
            .ToList();

        Assertions.AssertThat(string.Join(" | ", missing)).IsEmpty();
    }

    // ── 断言 2：README 的变异矩阵一览必须与磁盘一致 ────────────────────

    /// <summary>
    /// 「变异矩阵一览」表必须逐个列出磁盘上的 <c>mutate-*.py</c>，且**不许列出不存在的脚本**。
    /// <para><b>为什么要有这条</b>：README 曾把 <c>mutate-worldtable.py</c> 列为已有，
    /// 而那个文件**从来不存在** —— 文档指向空气，人照着跑只会得到「文件不存在」。</para>
    /// </summary>
    [TestCase]
    public void MutationMatrixTable_MatchesTheFilesOnDisk()
    {
        var readme = ReadmeText();
        var onDisk = QaScripts().Where(n => n.StartsWith("mutate-", StringComparison.Ordinal)).ToList();

        // 防空转：磁盘上必须有变异脚本，否则「表是空的」也会通过。
        Assertions.AssertThat(onDisk.Count).IsGreaterEqual(5);

        var mentioned = Regex.Matches(readme, @"mutate-[A-Za-z0-9\-_]+\.py")
            .Select(m => m.Value)
            .Distinct()
            .ToList();

        var notListed = onDisk.Where(n => !mentioned.Contains(n)).ToList();
        var notOnDisk = mentioned.Where(n => !onDisk.Contains(n)).ToList();

        var problems = new List<string>();
        problems.AddRange(notListed.Select(n => $"{n} 在磁盘上但 README 没登记"));
        problems.AddRange(notOnDisk.Select(n => $"README 写了 {n}，但磁盘上没有这个文件"));
        Assertions.AssertThat(string.Join(" | ", problems)).IsEmpty();
    }

    // ── 断言 3：README 首表点名的工具必须存在 ──────────────────────────

    /// <summary>README 首表里点名的每个 <c>*.py</c> 工具都必须<b>真的在磁盘上</b>。</summary>
    [TestCase]
    public void ReadmeToolTable_OnlyNamesFilesThatExist()
    {
        var readme = ReadmeText();
        var onDisk = QaScripts();

        var named = Regex.Matches(readme, @"`([A-Za-z0-9_\-]+\.py)`")
            .Select(m => m.Groups[1].Value)
            .Distinct()
            .ToList();

        Assertions.AssertThat(named.Count).IsGreaterEqual(5);

        var missing = named.Where(n => !onDisk.Contains(n)).ToList();
        Assertions.AssertThat(string.Join(" | ", missing)).IsEmpty();
    }

    // ── 断言 4：自检数量不许写错 ────────────────────────────────────────

    /// <summary>
    /// README 写的「N 个真机自检」必须等于 <c>run-selfchecks.py</c> 里 <c>CHECKS</c> 的条数。
    /// <para><b>为什么要有这条</b>：README 与脚本都写着「11 个」，
    /// 而 <c>CHECKS</c> 实际只有 <b>7</b> 条 —— 数字会骗人：
    /// 「11 个都过了」听起来比「7 个都过了」更让人放心。</para>
    /// </summary>
    [TestCase]
    public void SelfCheckCount_InReadmeMatchesTheScript()
    {
        var script = File.ReadAllText(Path.Combine(QaDir(), "run-selfchecks.py"));

        // CHECKS 列表里的每一条都有一个 "name": 字段 —— 数它就知道有几个自检。
        var actual = Regex.Matches(script, @"""name"":").Count;
        Assertions.AssertThat(actual).IsGreaterEqual(1);

        var readme = ReadmeText();
        var m = Regex.Match(readme, @"(\d+)\s*个\*\*真机自检\*\*");
        Assertions.AssertThat(m.Success).IsTrue();

        int stated = int.Parse(m.Groups[1].Value);
        Assertions.AssertThat(stated).IsEqual(actual);
    }
}
