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

    // ── 断言 7：变异矩阵必须能**只跑相关的那几个** ────────────────────────

    /// <summary>
    /// <c>verify-all.py</c> 必须支持 <c>--mutations &lt;子串&gt;</c>：
    /// 只跑文件名含该子串的变异脚本。
    /// <para><b>为什么这是纪律能不能落地的关键</b>：全量 19 个变异脚本要<b>几小时</b>
    /// ⇒ 实际上<b>没人会跑</b> ⇒「守卫必须变异看红」这条纪律等于<b>靠自觉</b>。
    /// 有了筛选，「我改了一条守卫 ⇒ 只验它对应的那一个」就从几小时变成几分钟
    /// ⇒ 纪律才<b>可执行</b>。</para>
    /// <para><b>为什么测「参数存在」而不是真的跑一遍</b>：真的跑要几小时，
    /// 而这条守卫每次 <c>dotnet test</c> 都要跑 ⇒ 必须<b>快</b>。
    /// 行为（找不到时明确失败）由本文件下一条断言真的跑一次来验。</para>
    /// </summary>
    [TestCase]
    public void VerifyAll_SupportsSelectingMutationScriptsByName()
    {
        var script = File.ReadAllText(Path.Combine(QaDir(), "verify-all.py"));

        // 防空转：脚本得真的存在，而且不能小到不可能有这些能力
        Assertions.AssertThat(script.Length).IsGreaterEqual(1000);

        Assertions.AssertThat(script).Contains("--mutations");
        // 筛选必须真的作用在「挑哪些脚本」上（而不是只是个没人用的参数）
        Assertions.AssertThat(script).Contains("args.mutations");
        Assertions.AssertThat(script).Contains("mutate-*.py");
    }

    /// <summary>
    /// <c>--mutations</c> 给的名字<b>匹配不到任何脚本时必须失败</b> ——
    /// 静默跳过就是「看起来跑过了、其实什么都没跑」。
    /// <para>⚠️ 这是本仓库反复治的同一类病：<b>「跳过」必须说出来，
    /// 而且在这里必须变成失败</b>（因为「只跑相关的」本来就是用户明确点的名）。</para>
    /// <para>⚠️ 断言查的是 <b>ASCII 标记</b>，不是中文 ——
    /// 中文在某些控制台下会被按系统代码页解码成乱码（本机 GBK），
    /// 靠中文断言的守卫会**永远红**（或者永远绿，看运气）。</para>
    /// </summary>
    [TestCase]
    public void VerifyAll_FailsWhenTheMutationFilterMatchesNothing()
    {
        var script = Path.Combine(QaDir(), "verify-all.py");

        // 故意给一个不可能匹配上的名字 ⇒ 期望退出码非 0
        var psi = new System.Diagnostics.ProcessStartInfo("python")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = RepoRoot(),
        };
        foreach (var a in new[]
        {
            script, "--mutations", "zzz-no-such-mutation-script",
            "--skip-tests", "--skip-build", "--skip-checks",
        })
        {
            psi.ArgumentList.Add(a);
        }

        using var proc = System.Diagnostics.Process.Start(psi);
        Assertions.AssertThat(proc is not null).IsTrue();
        if (proc is null) return;

        var stdout = proc.StandardOutput.ReadToEnd();
        var stderr = proc.StandardError.ReadToEnd();
        Assertions.AssertThat(proc.WaitForExit(120_000)).IsTrue();

        // ⚠️ 把实际输出带进失败消息：否则「断言失败」只有一个空壳，
        //    得再跑一次脚本才知道它到底说了什么。
        Assertions.AssertThat($"exit={proc.ExitCode} stdout={stdout} stderr={stderr}")
            .Contains("[QA] NO-MUTATION-SCRIPTS-MATCH");
        Assertions.AssertThat(proc.ExitCode).IsNotEqual(0);
    }

    // ── 断言 9：价格扫描工具不许替开发者拍板，也不许用单样本下结论 ──────

    /// <summary>
    /// <c>price-scan.py</c> 是给 P-14 提供**决策依据**的工具 ——
    /// 它<b>必须</b>做到三件事，否则它就变成了「替开发者发明数值」：
    /// <list type="number">
    /// <item><b>不碰仓库里的价格表</b>：候选表写在临时目录，用
    /// <c>--prices</c> 做一次性覆盖 ⇒ 仓库状态不被实验污染。</item>
    /// <item><b>默认跑多个种子</b>：单样本的差异可能是噪声
    /// （本仓库铁律：「跨版本比较先量种子方差，再下结论」）。</item>
    /// <item><b>单种子时明说「不能下结论」</b>：一个样本**证明不了**「一致」，
    /// 而这句话恰恰会出现在「我只跑了一遍」的场合。</item>
    /// </list>
    /// </summary>
    [TestCase]
    public void PriceScan_TakesEvidence_WithoutDecidingForTheDeveloper()
    {
        var script = File.ReadAllText(Path.Combine(QaDir(), "price-scan.py"));

        // 防空转：脚本得真的存在，而且不能小到不可能有这些能力
        Assertions.AssertThat(script.Length).IsGreaterEqual(1000);

        // ① 候选表写在临时目录，且用 --prices 一次性覆盖
        Assertions.AssertThat(script).Contains("--prices");
        Assertions.AssertThat(script).Contains("tempfile");

        // ② 默认多个种子（单样本不能下结论）
        Assertions.AssertThat(script).Contains("DEFAULT_SEEDS");
        var m = Regex.Match(script, @"DEFAULT_SEEDS\s*=\s*\[([^\]]+)\]");
        Assertions.AssertThat(m.Success).IsTrue();
        Assertions.AssertThat(m.Groups[1].Value.Split(',').Length).IsGreaterEqual(2);

        // ③ 单种子时必须**明说「不能下结论」**。
        //    ⚠️ 光查「脚本里含这句话」**太弱** —— 变异把**打印那一行**换掉后，
        //    那句话仍在源码里 ⇒ 断言照样绿（变异矩阵实测踩到）。
        //    ⇒ 必须查**分支里**：单种子分支（`if n_seeds < 2:`）的**正文**里
        //    必须同时出现「不能下结论」与「没有意义」
        //    （后者表示它**否定了**「一致」这句话 —— 单样本证明不了「一致」）。
        var singleSeedBranch = Regex.Match(
            script, @"if n_seeds < 2:(?<body>.*?)(?=\n    elif|\n    else)", RegexOptions.Singleline);
        Assertions.AssertThat(singleSeedBranch.Success).IsTrue();
        var body = singleSeedBranch.Groups["body"].Value;
        Assertions.AssertThat(body.Contains("不能下结论")).IsTrue();
        Assertions.AssertThat(body.Contains("没有意义")).IsTrue();

        // ④ 它不许**写**仓库里的价格表。
        //    ⚠️ 不能查「脚本里有没有出现 config/prices.json」——
        //    它的文档字符串里**就该提到**那个路径（说明「我们不碰它」）。
        //    要查的是「有没有**打开它写入**」——
        //    即有没有把 ROOT/config/prices.json 当成**输出**目标。
        //    判据：候选表写到 **mkdtemp 出来的临时目录**，且脚本里
        //    **没有**对仓库价格表的写调用（open( / write_text( 到那个路径）。
        Assertions.AssertThat(script).Contains("mkdtemp");
        Assertions.AssertThat(script.Contains("price-scan-")).IsTrue();
        // 没有对仓库价格表的任何写操作（open 会截断文件）
        Assertions.AssertThat(script.Contains("open(")).IsFalse();
        // 写候选表的那一处，路径变量必须来自临时目录
        var tmpdirUse = Regex.Match(script, @"tmpdir\s*/\s*\(?");
        Assertions.AssertThat(tmpdirUse.Success).IsTrue();
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

    // ── 断言 5：自检失败的原因必须变成机器可读的 annotation ──────────────

    /// <summary>
    /// 自检脚本在 GitHub Actions 里跑失败时，<b>失败原因</b>必须以
    /// <c>::error::</c>（workflow command）输出 ⇒ runner 把它转成
    /// <b>check-run annotation</b> ⇒ <b>公开仓库用无认证的 API 就能读到</b>。
    ///
    /// <para><b>为什么要有这条</b>：CI 的 job 日志<b>要登录才能看</b>
    /// （<c>/logs</c> 端点还要求 admin）⇒ 自动化环境里「CI 红了」
    /// 等于「只知道红了，<b>不知道哪一条、为什么</b>」。
    /// 实测 run #21：Godot 作业红了，而能读到的 annotation 只有一句
    /// 「Process completed with exit code 1.」—— <b>零信息量</b>。</para>
    ///
    /// <para><b>怎么验的</b>：脚本有个 <c>--self-test-annotation</c> 开关，
    /// 它走<b>与真实失败完全同一条</b> <c>_gh_command</c> 代码路径 ⇒
    /// 通道坏了这里就会坏（不是「另写一份演示代码」那种自欺）。
    /// 它也<b>不需要 Godot</b> ⇒ 本机与 CI 都能秒跑。</para>
    /// </summary>
    [TestCase]
    public void SelfCheckScript_ReportsFailuresAsMachineReadableAnnotations()
    {
        var script = Path.Combine(QaDir(), "run-selfchecks.py");

        // ① 在 Actions 里 ⇒ 必须有 ::error
        var inActions = RunPython(script,
            new Dictionary<string, string> { ["GITHUB_ACTIONS"] = "true" },
            "--self-test-annotation");

        // GitHub 的 workflow command 格式：::error <属性以逗号分隔>::<消息>
        // ⇒ 前缀是「::error 」而不是「::error::」（后者永远不会出现 ——
        // 这条守卫第一版就写错过，断言恒为 0，却一度以为是脚本坏了）。
        var errorLines = inActions.Split('\n')
            .Where(l => l.TrimStart().StartsWith("::error ", StringComparison.Ordinal))
            .Select(l => l.TrimEnd('\r'))
            .ToList();

        // 自测那条消息**故意带了换行** ⇒ 折叠正确时恰好一行
        Assertions.AssertThat(errorLines.Count).IsEqual(1);

        // 换行必须被**折叠成空格**（workflow command 是单行的：
        // 换行会被截断，甚至可能注入新的 workflow command）
        Assertions.AssertThat(errorLines[0]).Contains("第一行 第二行");

        // 属性与消息之间必须有一个「::」，且标题不为空
        Assertions.AssertThat(Regex.IsMatch(errorLines[0], @"^::error title=.+::.+$")).IsTrue();

        // ② 不在 Actions 里 ⇒ 一行 annotation 都不该有（别污染本地日志）
        var local = RunPython(script, new Dictionary<string, string>(), "--self-test-annotation");
        Assertions.AssertThat(Regex.IsMatch(local, @"^::error(\s|,|:)", RegexOptions.Multiline)).IsFalse();
    }

    /// <summary>
    /// 自检脚本必须能把结果写成 <b>Markdown 摘要</b>
    /// （CI 传 <c>$GITHUB_STEP_SUMMARY</c> ⇒ 网页上直接看得到，不必翻日志）。
    /// <para><b>为什么值得单独守</b>：这条路径<b>一开始没有任何测试覆盖</b> ——
    /// 「写不出来」和「写错了」都不会红，而它正是「CI 红了时人第一眼看到的东西」。
    /// 「没被测过的代码路径」就是**下一个偶发红灯的候选**。</para>
    /// </summary>
    [TestCase]
    public void SelfCheckScript_WritesASummaryFile()
    {
        var script = Path.Combine(QaDir(), "run-selfchecks.py");
        var tmp = Path.Combine(Path.GetTempPath(), $"qa-summary-{Guid.NewGuid():N}.md");
        try
        {
            RunPython(script, new Dictionary<string, string>(),
                "--self-test-annotation", $"--summary={tmp}");

            Assertions.AssertThat(File.Exists(tmp)).IsTrue();
            var summary = File.ReadAllText(tmp, System.Text.Encoding.UTF8);
            Assertions.AssertThat(summary).Contains("| 自检 |");
            Assertions.AssertThat(summary).Contains("sample");
        }
        finally
        {
            if (File.Exists(tmp)) File.Delete(tmp);
        }
    }

    /// <summary>
    /// 跑一次 Python 脚本，把 stdout 拿回来。
    /// <para><b>为什么自己找解释器</b>：Windows 上 <c>python</c> 有可能是
    /// Microsoft Store 的**占位程序**（App Execution Alias，零字节，
    /// 一调用就去开商店）⇒ 直接 <c>Process.Start("python")</c> 会拿到一个
    /// **立刻退出的空输出** —— 那正是本条守卫第一版栽掉的地方
    /// （断言报「0 行 ::error::」，而脚本手动跑完全正常）。
    /// 所以这里扫 PATH、**跳过零字节文件**，再逐个试，
    /// 并把每个候选的失败原因带进异常里 ⇒ 失败时能一眼看出「为什么没找到」。</para>
    /// </summary>
    private static string RunPython(
        string script, Dictionary<string, string> env, params string[] args)
    {
        var attempts = new List<string>();

        foreach (var (exe, prefix) in PythonCandidates())
        {
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo(exe)
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    WorkingDirectory = RepoRoot(),
                    // ⚠️ 必须显式指定：.NET 默认按**系统代码页**解码
                    // （本机是 GBK，Windows runner 上也不是 UTF-8）
                    // ⇒ Python 写的 UTF-8 会被解成乱码。
                    // 这条守卫第一版就栽在这儿：断言「0 行 ::error::」，
                    // 而脚本其实**跑得好好的**，只是内容被解码成了「鑷閫氶亾」。
                    StandardOutputEncoding = System.Text.Encoding.UTF8,
                    StandardErrorEncoding = System.Text.Encoding.UTF8,
                };
                foreach (var a in prefix) psi.ArgumentList.Add(a);
                psi.ArgumentList.Add(script);
                foreach (var a in args) psi.ArgumentList.Add(a);

                // ⚠️ 必须**先删掉继承来的 GITHUB_ACTIONS**，再按调用方的要求设置。
                // 「不设置」不等于「没有」：CI 上跑 dotnet test 时这个变量本来就是 true
                // ⇒ 靠「环境里恰好没有」来断言，等于写了一条**在 CI 上必然红**的测试
                // （本条守卫第一版就踩了这个坑：本机环境里也恰好带着它）。
                psi.Environment.Remove("GITHUB_ACTIONS");
                foreach (var kv in env) psi.Environment[kv.Key] = kv.Value;

                using var proc = System.Diagnostics.Process.Start(psi);
                if (proc is null)
                {
                    attempts.Add($"{exe}: 启动返回 null");
                    continue;
                }

                var stdout = proc.StandardOutput.ReadToEnd();
                var stderr = proc.StandardError.ReadToEnd();
                if (!proc.WaitForExit(60_000))
                {
                    try { proc.Kill(true); } catch { /* 已经退了 */ }
                    attempts.Add($"{exe}: 超时（60s）");
                    continue;
                }
                if (proc.ExitCode != 0)
                {
                    attempts.Add($"{exe}: 退出码 {proc.ExitCode}，输出「{stderr.Trim()}」");
                    continue;
                }
                return stdout;
            }
            catch (Exception ex)
            {
                attempts.Add($"{exe}: {ex.GetType().Name}");
            }
        }

        throw new InvalidOperationException(
            "找不到可用的 Python 解释器 —— 这条守卫需要 Python 3"
            + "（本仓库的 QA 工具链也是 Python）。逐个候选的结果：" + string.Join(" | ", attempts));
    }

    /// <summary>PATH 里的 Python 解释器候选（跳过零字节的 Store 占位程序）。</summary>
    private static IEnumerable<(string Exe, string[] Prefix)> PythonCandidates()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var rawDir in path.Split(Path.PathSeparator))
        {
            var dir = rawDir.Trim().Trim('"');
            if (dir.Length == 0) continue;
            foreach (var (name, prefix) in new[]
            {
                ("python.exe", Array.Empty<string>()),
                ("python3.exe", Array.Empty<string>()),
                ("py.exe", new[] { "-3" }),
            })
            {
                var full = Path.Combine(dir, name);
                if (!seen.Add(full)) continue;
                FileInfo info;
                try { info = new FileInfo(full); } catch { continue; }
                if (!info.Exists) continue;
                // 零字节 = Microsoft Store 的 App Execution Alias（不是真解释器）
                if (info.Length == 0) continue;
                yield return (full, prefix);
            }
        }
    }
}
