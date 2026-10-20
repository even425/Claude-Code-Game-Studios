using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using GdUnit4;
using ClaudeCodeGameStudios.Domain.Items;

namespace ClaudeCodeGameStudios.Tests;

/// <summary>
/// <b>价格表的两份副本必须一致</b>：<c>config/prices.json</c>（外部配置）
/// 与 <see cref="PriceBook.Default"/>（内置兜底）。
///
/// <para><b>它守的是什么</b>：这是同一份数据的<b>两个副本</b>。
/// 开发者把最终价格写进 <c>config/prices.json</c>（正是 <c>T-007</c> 要做的事），
/// 如果内置兜底忘了同步，于是：<list type="bullet">
/// <item>配置文件<b>在</b> ⇒ 新价格生效；</item>
/// <item>配置文件<b>不在</b>（丢了 / 解析失败 / 路径写错）⇒ <b>静默退回占位价格</b>。</item>
/// </list>
/// 两种情况的<b>经济完全不同</b>，而且第二种**只在配置文件出问题时才暴露** ——
/// 那正是你在查别的问题的时候。</para>
///
/// <para><b>为什么它值得一条守卫</b>：外部化价格表这个改造本身（阶段 7）
/// 就是「把一份数据变成两份」的动作。<b>复制数据这件事，必须配一个「两份不许分叉」的检查</b> ——
/// 否则它就是一处静默的定时炸弹。</para>
///
/// <para><b>变体</b>：配置文件里的价格**全是占位**时，两份必须**完全相同**；
/// 开发者一旦在 JSON 里定了最终价格，内置兜底<b>也必须跟着改成同样的数</b> ——
/// 「兜底」指的是<b>机制</b>（配置读不到时游戏仍能跑），不是「兜底到旧经济」。
/// 所以本守卫要求<b>严格相等</b>，而不是「差不多」。</para>
/// </summary>
[TestSuite]
public class PriceBookConfigConsistencyTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(
            Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location)!);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Claude-Code-Game-Studios.csproj"))
                && Directory.Exists(Path.Combine(dir.FullName, "config")))
            {
                return dir.FullName;
            }
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("找不到仓库根目录");
    }

    private static string ConfigPath() =>
        Path.Combine(RepoRoot(), "config", "prices.json");

    /// <summary>读外部价格表（跳过以 <c>_</c> 开头的注释键）。</summary>
    private static Dictionary<string, long> ReadConfigPrices()
    {
        var result = new Dictionary<string, long>(StringComparer.Ordinal);
        using var doc = JsonDocument.Parse(File.ReadAllText(ConfigPath()));

        foreach (var prop in doc.RootElement.EnumerateObject())
        {
            if (prop.Name.StartsWith("_")) continue;      // 注释键
            result[prop.Name] = prop.Value.GetInt64();
        }
        return result;
    }

    /// <summary>
    /// 外部配置与内置兜底<b>逐项相等</b>。
    /// </summary>
    [TestCase]
    public void ConfigPrices_AndBuiltinFallback_AreIdentical()
    {
        var config = ReadConfigPrices();
        var builtin = PriceBook.Default.Items
            .ToDictionary(kv => kv.Key.Value, kv => kv.Value, StringComparer.Ordinal);

        var problems = new List<string>();

        foreach (var kv in config)
        {
            if (!builtin.TryGetValue(kv.Key, out long actual))
            {
                problems.Add($"{kv.Key}：配置里有（{kv.Value}），内置兜底里没有");
            }
            else if (actual != kv.Value)
            {
                problems.Add($"{kv.Key}：配置 {kv.Value} ≠ 内置兜底 {actual}");
            }
        }

        foreach (var kv in builtin)
        {
            if (!config.ContainsKey(kv.Key))
            {
                problems.Add($"{kv.Key}：内置兜底里有（{kv.Value}），配置里没有");
            }
        }

        Assertions.AssertThat(string.Join(" | ", problems)).IsEmpty();
    }

    /// <summary>
    /// 外部配置<b>必须真的能被解析</b> —— 否则游戏会静默退回兜底，
    /// 而开发者以为自己的价格生效了。
    /// </summary>
    [TestCase]
    public void ConfigPrices_AreParseableByTheLoader()
    {
        bool ok = PriceBook.TryLoadFromJson(File.ReadAllText(ConfigPath()),
                                            out PriceBook? book, out string? error);

        Assertions.AssertThat(ok).IsTrue();
        Assertions.AssertThat(error).IsNull();
        Assertions.AssertThat(book).IsNotNull();

        // 解析出来的表与内置兜底也必须一致（这条是上一条的另一面）
        var loaded = book!.Items.ToDictionary(kv => kv.Key.Value, kv => kv.Value, StringComparer.Ordinal);
        var builtin = PriceBook.Default.Items
            .ToDictionary(kv => kv.Key.Value, kv => kv.Value, StringComparer.Ordinal);

        var problems = loaded
            .Where(kv => !builtin.TryGetValue(kv.Key, out long b) || b != kv.Value)
            .Select(kv => $"{kv.Key}：配置 {kv.Value} ≠ 内置兜底 {(builtin.TryGetValue(kv.Key, out long b) ? b.ToString() : "缺失")}")
            .ToList();

        Assertions.AssertThat(string.Join(" | ", problems)).IsEmpty();
    }

    /// <summary>
    /// 价格<b>不得为负</b>；<b>0 是合法的</b>（含义是「该物品不卖」，
    /// 见 <c>config/prices.json</c> 的说明与 <see cref="PriceBook"/> 的构造校验）。
    /// </summary>
    [TestCase]
    public void ConfigPrices_AreNotNegative()
    {
        var bad = ReadConfigPrices()
            .Where(kv => kv.Value < 0)
            .Select(kv => $"{kv.Key} = {kv.Value}")
            .ToList();

        Assertions.AssertThat(string.Join(" | ", bad)).IsEmpty();
    }

    /// <summary>
    /// <b>防空转</b>：配置里确实有价格，且内置兜底里确实有对应条目 ——
    /// 否则上面两条可能只是「两边都是空的，所以相等」。
    /// </summary>
    [TestCase]
    public void BothCopiesActuallyContainPrices()
    {
        var config = ReadConfigPrices();
        var builtin = PriceBook.Default.Items;

        Assertions.AssertThat(config.Count).IsGreater(0);
        Assertions.AssertThat(builtin.Count).IsGreater(0);
        Assertions.AssertThat(builtin.Count).IsEqual(config.Count);
    }
}
