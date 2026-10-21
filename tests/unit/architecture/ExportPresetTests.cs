using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using GdUnit4;

namespace ClaudeCodeGameStudios.Tests.Architecture;

/// <summary>
/// <b>导出预设不许分叉</b>（TP-002 的一半）。
///
/// <para><b>它守的是什么</b>：导出预设名是<b>项目定义的字符串</b>（不是引擎默认值），
/// 而它同时出现在<b>两个地方</b>：<c>export_presets.cfg</c> 的
/// <c>preset.0.name</c>，与 <c>project.yaml</c> 的 <c>commands.build</c>。
/// 两处分属两处 ⇒ 迟早分叉 ⇒ 后果是 <b>Unknown export preset</b>（配置错），
/// 更糟的是<b>悄悄导出了错的平台</b>。</para>
///
/// <para><b>为什么这条值得守</b>：<c>export_presets.cfg</c> 原先被
/// <c>.gitignore</c> 排除 ⇒ 它<b>不在版本库里</b> ⇒ 两处**必然**分叉
/// （干净克隆根本读不到预设）。现在它进版本库了 ⇒ 这条守卫才有东西可守。</para>
///
/// <para><b>为什么断言的是「错误类型」而不是「导出成功」</b>：
/// 导出还需要 <b>Godot 的导出模板</b>（约 1.2 GB，装在
/// <c>%APPDATA%\Godot\export_templates\</c>）—— 那是<b>引擎的运行时依赖</b>，
/// 不在版本库里，CI 上也没有。
/// ⇒ 守卫不能要求「导出成功」，但它<b>能</b>要求
/// <b>「失败的原因不是『预设没找到』」</b> ——
/// 「Unknown export preset」= <b>项目配错了</b>（要红），
/// 「No export template found」= <b>环境没准备好</b>（不该红）。
/// <b>这两种错必须分得清</b>，否则「环境缺东西」会被误当成「代码坏了」。</para>
/// </summary>
[TestSuite]
public class ExportPresetTests
{
    private const string PresetsRelPath = "export_presets.cfg";
    private const string ProjectYamlRelPath = "project.yaml";

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

    /// <summary>QA 工具目录（<c>production/qa/</c>）。</summary>
    private static string QaDir() =>
        Path.Combine(RepoRoot(), "production", "qa");

    /// <summary>从 <c>export_presets.cfg</c> 里取第一个预设的名字。</summary>
    private static string? PresetName()
    {
        var text = Read(PresetsRelPath);
        var m = Regex.Match(text, @"^\s*name\s*=\s*""([^""]+)""", RegexOptions.Multiline);
        return m.Success ? m.Groups[1].Value : null;
    }

    /// <summary>从 <c>project.yaml</c> 的 <c>commands.build</c> 里取引用的预设名。</summary>
    private static string? BuildCommand()
    {
        var text = Read(ProjectYamlRelPath);
        // ⚠️ build 的值里含**转义引号**（\"Windows Desktop\"）⇒
        //    用 [^"]+ 去匹配会**在第一个转义引号处截断**。
        //    ⇒ 取到**行尾**，再把 \" 还原成 "。
        var m = Regex.Match(text, @"^\s*build\s*:\s*""(?<cmd>.*)$", RegexOptions.Multiline);
        if (!m.Success) return null;
        return m.Groups["cmd"].Value.TrimEnd().Replace("\\\"", "\"");
    }

    // ── 断言 1：预设文件必须在版本库里 ──────────────────────────────────────

    /// <summary>
    /// <c>export_presets.cfg</c> 必须<b>进版本库</b>。
    /// <para><b>为什么</b>：它与 <c>project.yaml</c> 的 <c>commands.build</c>
    /// 讲的是<b>同一个名字</b>，而两处分属两处 —— 不进版本库就<b>必然分叉</b>
    /// （干净克隆里根本没有这个文件）。
    /// ⇒ 由 <c>git check-ignore</c> 判定：被忽略就是没进版本库。</para>
    /// </summary>
    [TestCase]
    public void ExportPresets_IsTrackedByGit_NotIgnored()
    {
        var ignored = RunGit("check-ignore", "-q", PresetsRelPath);
        // ⚠️ GdUnit4 这一版**没有**带消息的布尔断言 ⇒ 把诊断信息放进被断言的值里。
        Assertions.AssertThat(ignored == 0
                ? $"{PresetsRelPath} 被 .gitignore 排除了 ⇒ 干净克隆里没有它 ⇒ 预设名必然与 project.yaml 分叉"
                : "")
            .IsEmpty();

        // 它也必须真的**存在**（不只是没被忽略）
        Assertions.AssertThat(File.Exists(Path.Combine(RepoRoot(), PresetsRelPath))).IsTrue();
    }

    // ── 断言 2：预设名两处一致 ─────────────────────────────────────────────

    /// <summary>
    /// <c>export_presets.cfg</c> 的 <c>preset.0.name</c> 必须与
    /// <c>project.yaml</c> 的 <c>commands.build</c> 里引用的那个<b>逐字相同</b>。
    /// </summary>
    [TestCase]
    public void PresetName_MatchesTheBuildCommand()
    {
        var preset = PresetName();
        var build = BuildCommand();

        Assertions.AssertThat(preset).IsNotNull();
        Assertions.AssertThat(build).IsNotNull();

        // build 命令里必须出现这个预设名（用引号包住的精确匹配）
        Assertions.AssertThat(build).Contains($"\"{preset}\"");

        // ⚠️ 反过来也要成立：build 里引用的名字必须**就是**预设文件里的那个 ——
        //    只查「预设名出现在 build 里」不够：build 里可能同时写了别的名字。
        var quoted = Regex.Matches(build, @"""([^""]+)""").Select(m => m.Groups[1].Value);
        var exported = quoted.FirstOrDefault(q => q != "Windows Desktop"
                                                  || q == preset);
        Assertions.AssertThat(exported).IsEqual(preset);
    }

    // ── 断言 3：导出的失败原因不能是「预设没找到」 ──────────────────────────
    //
    // ⚠️ 这一条**不在这里跑**：它要真的调引擎，而 CI 的**主作业**（dotnet test）
    //    **没装 Godot** ⇒ 放在这里会让 CI 必红。
    // ⇒ 动态部分在 `production/qa/export-check.py`，由 CI 的 **Godot 作业**调用
    //    （那个作业装了引擎）。本文件只守**静态**的那些：文件在版本库里 + 名字两处一致。
    //
    //    为什么值得拆开：静态部分**每次 dotnet test 都跑**（快、无依赖），
    //    动态部分**需要引擎**（慢、有环境依赖）。而「预设名对不对」这件事
    //    **静态查不出来** —— 静态只能证明「两个文件里写着同一个字符串」，
    //    证明不了 **Godot 认这个预设**。

    /// <summary>
    /// `export-check.py`（动态那半）必须**存在**，且它必须**真的调一次引擎**。
    /// <para><b>为什么这里要守它</b>：动态那半放在 CI 的 Godot 作业里 ——
    /// 那条路径**不经过 <c>dotnet test</c>** ⇒ 没有别的机制盯着它
    /// ⇒ 它自己坏掉时（改了判据、忘了调用）**没人会发现</b>。
    /// 这一条就是那个「有人发现」。</para>
    /// </summary>
    [TestCase]
    public void ExportCheckScript_Exists_AndActuallyRunsTheEngine()
    {
        var path = Path.Combine(QaDir(), "export-check.py");
        Assertions.AssertThat(File.Exists(path)).IsTrue();

        var text = File.ReadAllText(path);
        // 它必须真的调引擎（不是只读文件）
        Assertions.AssertThat(text).Contains("--export-release");
        // 它必须把「预设错」与「模板缺」**分开**
        // ⚠️ Godot 4.7.2 的原话是「Invalid export preset name」——
        //    早期文档里的 "Unknown export preset" 在这个版本**不出现**。
        //
        // ⚠️ **必须精确匹配「元组定义那一行」**，不能全文 `Contains` ——
        //    那两个措辞在文档字符串与注释里也出现 ⇒ 判据被清空后全文照样能搜到
        //    ⇒ 守卫会**永远绿**（变异矩阵实测踩到）。
        var marks = Regex.Match(text,
            @"WRONG_PRESET_MARKS\s*=\s*\((?<body>[^)]*)\)");
        Assertions.AssertThat(marks.Success).IsTrue();
        var body = marks.Groups["body"].Value;
        Assertions.AssertThat(body.Contains("Unknown export preset")
                             || body.Contains("Invalid export preset name")).IsTrue();

        var tpl = Regex.Match(text, @"MISSING_TEMPLATE_MARKS\s*=\s*\((?<body>[^)]*)\)");
        Assertions.AssertThat(tpl.Success).IsTrue();
        Assertions.AssertThat(tpl.Groups["body"].Value.Length).IsGreaterEqual(1);

        // ⚠️ 光查「脚本里有 --self-test」**太弱** —— 变异把**判断那一行**换掉后，
        //    那个字符串还在 ⇒ 断言照样绿。⇒ 必须查**调用点**：
        //    自测里必须真的**用**了「错预设名会被拒」这个判据。
        Assertions.AssertThat(text).Contains("--self-test");
        Assertions.AssertThat(Regex.IsMatch(text, @"if not says_wrong_preset\(")).IsTrue();
    }

    /// <summary>跑一条 git 命令，返回退出码（0 = 成功 / 被忽略）。</summary>
    private static int RunGit(params string[] args)
    {
        var psi = new System.Diagnostics.ProcessStartInfo("git")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = RepoRoot(),
        };
        foreach (var a in args) psi.ArgumentList.Add(a);

        using var proc = System.Diagnostics.Process.Start(psi);
        if (proc is null) return -1;
        proc.StandardOutput.ReadToEnd();
        proc.StandardError.ReadToEnd();
        proc.WaitForExit(60_000);
        return proc.ExitCode;
    }
}
