using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using GdUnit4;

namespace ClaudeCodeGameStudios.Tests;

/// <summary>
/// <b>参数台账守卫</b>：代码里的每一个数值常量都必须在台账里有归属，
/// 且<b>值必须与台账一致</b>。
///
/// <para><b>它守的是什么</b>：计划 §二 的原则是「临时参数必须<b>集中管理</b>，
/// 不能悄悄变成正式设计规则」。那条规矩此前<b>没有可执行的含义</b> ——
/// 规矩写在 <c>07</c> §2.1，数字却散落在代码里，而且<b>没有任何东西会在数字被改掉时喊一声</b>。
/// 于是「占位值悄悄变成正式规则」这件事，只需要一次顺手改动就发生了。</para>
///
/// <para><b>三条断言</b>：
/// <list type="number">
/// <item><b>覆盖率</b>：代码里的每个数值常量都必须在台账里有归属
/// （要么是玩法参数，要么被显式认领为「表现层 / 技术值 / 哨兵」）。
/// <b>新增一个数字而忘了登记 ⇒ 红。</b></item>
/// <item><b>一致性</b>：玩法参数的<b>值</b>必须与台账一致 ⇒ <b>改值忘了改台账 ⇒ 红。</b></item>
/// <item><b>文档</b>：清单文档 <c>production/36_当前实现参数清单_V1.0.md</c> 必须逐行包含每个参数
/// ⇒ <b>文档与代码分叉 ⇒ 红</b>（文档会过期，守卫不会）。</item>
/// </list></para>
///
/// <para><b>单一真源</b>：<c>production/qa/parameters.json</c>。
/// 生成脚本 <c>production/qa/param-inventory.py</c> 与本守卫读的是同一份 JSON ——
/// 两边不会各说各话。</para>
///
/// <para><b>已知边界</b>：本守卫只扫<b>标量常量</b>。
/// 查表 / 数组里的数字（升级成本表、价格表、掉落权重、性格权重）<b>不在覆盖范围内</b> ——
/// 那是本文的边界，不是「那些数不重要」。</para>
/// </summary>
[TestSuite]
public class ParameterLedgerTests
{
    private const string LedgerRelPath = "production/qa/parameters.json";
    private const string DocRelPath = "production/36_当前实现参数清单_V1.0.md";

    /// <summary>标量常量：<c>private const int X = 4;</c> / <c>private static readonly double Y = 0.25;</c></summary>
    private static readonly Regex Scalar = new(
        @"^\s*(?:public|private|internal|protected)(?:\s+static)?(?:\s+readonly)?(?:\s+const)?" +
        @"\s+[\w<>\[\],\.\?]+\s+(\w+)\s*=\s*(-?[0-9]+(?:\.[0-9]+)?)(?:[fFdDmMlLuU])?\s*;",
        RegexOptions.Compiled);

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
                    r.GetProperty("note").GetString() ?? "",
                    r.TryGetProperty("value", out var v) ? v.GetString() ?? "" : ""));
            }
        }
        return rows;
    }

    /// <summary>扫出 (相对路径, 常量名, 值文本)。</summary>
    private static List<(string File, string Name, string Value)> ScanConstants()
    {
        var src = Path.Combine(RepoRoot(), "src");
        var found = new List<(string, string, string)>();
        foreach (var path in Directory.EnumerateFiles(src, "*.cs", SearchOption.AllDirectories))
        {
            string rel = Path.GetRelativePath(src, path).Replace('\\', '/');
            foreach (var line in File.ReadAllLines(path))
            {
                var m = Scalar.Match(line);
                if (m.Success) found.Add((rel, m.Groups[1].Value, m.Groups[2].Value));
            }
        }
        return found;
    }

    /// <summary>把 "0.10" / "0.1" / "10" 统一成可比较的字符串。</summary>
    private static string Norm(string v)
    {
        if (!double.TryParse(v, System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out double f))
        {
            return v;
        }
        return f == Math.Floor(f) ? ((long)f).ToString() : f.ToString("0.#########",
            System.Globalization.CultureInfo.InvariantCulture);
    }

    // ── 断言 1：覆盖率 ──────────────────────────────────────────────────

    /// <summary>
    /// 代码里的每个数值常量都必须在台账里有归属。
    /// <para><b>这是这条守卫的主断言</b>：新加一个数字而忘了登记 ⇒ 这里红。</para>
    /// </summary>
    [TestCase]
    public void EveryNumericConstant_IsClaimedByTheLedger()
    {
        var ledger = LoadLedger();
        var known = new HashSet<(string, string)>(
            ledger.Select(r => (r.File, r.Name)));

        var unclaimed = ScanConstants()
            .Where(c => !known.Contains((c.File, c.Name)))
            .Select(c => $"{c.File} :: {c.Name} = {c.Value}")
            .ToList();

        Assertions.AssertThat(string.Join(" | ", unclaimed)).IsEmpty();
    }

    // ── 断言 2：值一致 ──────────────────────────────────────────────────

    /// <summary>
    /// 玩法参数的<b>值</b>必须与台账一致 ⇒ <b>改值忘了改台账 ⇒ 红</b>。
    /// </summary>
    [TestCase]
    public void ParameterValues_MatchTheLedger()
    {
        var ledger = LoadLedger().Where(r => r.Kind == "参数").ToList();
        var code = ScanConstants().ToDictionary(c => (c.File, c.Name), c => c.Value);

        var drifted = new List<string>();
        foreach (var row in ledger)
        {
            if (!code.TryGetValue((row.File, row.Name), out string? actual))
            {
                drifted.Add($"{row.File} :: {row.Name} 在代码里已经不存在了");
                continue;
            }
            if (Norm(actual) != Norm(row.Value))
            {
                drifted.Add($"{row.File} :: {row.Name} 代码是 {actual}，台账写的是 {row.Value}");
            }
        }

        Assertions.AssertThat(string.Join(" | ", drifted)).IsEmpty();
    }

    // ── 断言 3：台账条目不得腐化 ────────────────────────────────────────

    /// <summary>台账里的每一项都必须**仍在代码里**（否则台账会自己烂掉）。</summary>
    [TestCase]
    public void EveryLedgerEntry_StillExistsInTheCode()
    {
        var ledger = LoadLedger();
        var code = new HashSet<(string, string)>(ScanConstants().Select(c => (c.File, c.Name)));

        var stale = ledger
            .Where(r => !code.Contains((r.File, r.Name)))
            .Select(r => $"{r.File} :: {r.Name}")
            .ToList();

        Assertions.AssertThat(string.Join(" | ", stale)).IsEmpty();
    }

    // ── 断言 4：文档必须逐行包含每个参数 ────────────────────────────────

    /// <summary>
    /// 清单文档必须逐行包含每个玩法参数 ⇒ <b>文档与代码分叉 ⇒ 红</b>。
    /// <para>（文档会过期，守卫不会。）</para>
    /// </summary>
    [TestCase]
    public void TheInventoryDocument_MentionsEveryParameter()
    {
        string docPath = Path.Combine(RepoRoot(), DocRelPath.Replace('/', Path.DirectorySeparatorChar));
        Assertions.AssertThat(File.Exists(docPath)).IsTrue();
        string doc = File.ReadAllText(docPath);

        var missing = LoadLedger()
            .Where(r => r.Kind == "参数")
            .Where(r => !doc.Contains($"`{r.Name}`") || !doc.Contains($"`{r.File}`"))
            .Select(r => $"{r.File} :: {r.Name}")
            .ToList();

        Assertions.AssertThat(string.Join(" | ", missing)).IsEmpty();
    }

    // ── 断言 5：防空转 ──────────────────────────────────────────────────

    /// <summary>
    /// 守卫自己得扫到东西 —— 否则「全绿」可能只是「什么都没扫」。
    /// </summary>
    [TestCase]
    public void TheLedgerActuallyCoversSomething()
    {
        var ledger = LoadLedger();
        var constants = ScanConstants();

        Assertions.AssertThat(constants.Count).IsGreater(50);
        Assertions.AssertThat(ledger.Count(r => r.Kind == "参数")).IsGreater(20);
        Assertions.AssertThat(ledger.Count(r => r.Kind != "参数")).IsGreater(20);
    }
}
