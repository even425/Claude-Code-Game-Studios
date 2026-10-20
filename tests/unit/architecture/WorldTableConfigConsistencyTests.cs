using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using GdUnit4;

namespace ClaudeCodeGameStudios.Tests;

/// <summary>
/// <b>世界表的两份副本必须一致</b>：<c>config/worlds.json</c>（外部配置）
/// 与 <see cref="HeroFactory.World.Builtin"/>（内置兜底）。
///
/// <para><b>它守的是什么</b>：和价格表那对副本是<b>同一种隐患</b> ——
/// 外部化一个配置表，就是把一份数据变成两份。
/// 开发者改了 JSON 却忘了同步兜底 ⇒ 配置文件在 / 不在，
/// 英雄的<b>名字风格、痕迹、性格倾向、出场权重</b>会完全不同，
/// 而且<b>只在配置文件出问题时才暴露</b>。</para>
///
/// <para><b>为什么逐字段比</b>：只比「世界数量」会漏掉「名字改了但数量没变」；
/// 只比「名字 + 权重」会漏掉「痕迹 / 性格改了」。
/// 这里的比对覆盖 <b>全部六个字段</b>（name / weight / surname / given / traits / personalities）。</para>
/// </summary>
[TestSuite]
public class WorldTableConfigConsistencyTests
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

    private static string ConfigPath() => Path.Combine(RepoRoot(), "config", "worlds.json");

    /// <summary>读外部世界表（跳过以 <c>_</c> 开头的注释键）。</summary>
    private static List<(string Name, int Weight, string[] Surname, string[] Given,
                         string[] Traits, string[] Personalities)> ReadConfig()
    {
        var result = new List<(string, int, string[], string[], string[], string[])>();
        using var doc = JsonDocument.Parse(File.ReadAllText(ConfigPath()));

        if (!doc.RootElement.TryGetProperty("worlds", out var arr)) return result;

        foreach (var w in arr.EnumerateArray())
        {
            string[] Strs(string key) => w.TryGetProperty(key, out var v)
                ? v.EnumerateArray().Select(x => x.GetString() ?? "").ToArray()
                : Array.Empty<string>();

            result.Add((
                w.GetProperty("name").GetString() ?? "",
                w.GetProperty("weight").GetInt32(),
                Strs("surname"), Strs("given"), Strs("traits"), Strs("personalities")));
        }
        return result;
    }

    private static string Join(IEnumerable<string> xs) => string.Join("/", xs);

    /// <summary>外部世界表与内置兜底<b>逐字段相等</b>。</summary>
    [TestCase]
    public void ConfigWorlds_AndBuiltinFallback_AreIdentical()
    {
        var config = ReadConfig();
        var builtin = HeroFactory.World.Builtin
            .Select(w => (w.Name, w.Weight, w.Surname, w.Given, w.Traits, w.Personalities))
            .ToList();

        var problems = new List<string>();

        if (config.Count != builtin.Count)
        {
            problems.Add($"世界数量不同：配置 {config.Count}，内置 {builtin.Count}");
        }

        int n = Math.Min(config.Count, builtin.Count);
        for (int i = 0; i < n; i++)
        {
            var c = config[i];
            var b = builtin[i];
            if (c.Name != b.Name) problems.Add($"第 {i} 个世界：名称「{c.Name}」≠「{b.Name}」");
            if (c.Weight != b.Weight) problems.Add($"{c.Name}：权重 {c.Weight} ≠ {b.Weight}");
            if (Join(c.Surname) != Join(b.Surname)) problems.Add($"{c.Name}：姓集合不同");
            if (Join(c.Given) != Join(b.Given)) problems.Add($"{c.Name}：名集合不同");
            if (Join(c.Traits) != Join(b.Traits)) problems.Add($"{c.Name}：痕迹集合不同");
            if (Join(c.Personalities) != Join(b.Personalities)) problems.Add($"{c.Name}：性格集合不同");
        }

        Assertions.AssertThat(string.Join(" | ", problems)).IsEmpty();
    }

    /// <summary>
    /// 外部世界表<b>必须真的能被加载器解析</b> —— 否则游戏会静默退回兜底，
    /// 而开发者以为自己的世界表生效了。
    /// </summary>
    [TestCase]
    public void ConfigWorlds_AreParseableByTheLoader()
    {
        bool ok = HeroFactory.World.TryLoadFromJson(
            File.ReadAllText(ConfigPath()), out HeroFactory.World[]? worlds, out string? error);

        Assertions.AssertThat(ok).IsTrue();
        Assertions.AssertThat(error).IsNull();
        Assertions.AssertThat(worlds).IsNotNull();
        Assertions.AssertThat(worlds!.Length).IsGreater(0);
    }

    /// <summary>
    /// <b>防空转</b>：两份副本里确实都有世界，且数量相同 ——
    /// 否则「逐字段相等」可能只是「两边都空」。
    /// </summary>
    [TestCase]
    public void BothCopiesActuallyContainWorlds()
    {
        var config = ReadConfig();
        var builtin = HeroFactory.World.Builtin;

        Assertions.AssertThat(config.Count).IsGreater(0);
        Assertions.AssertThat(builtin.Length).IsGreater(0);
        Assertions.AssertThat(builtin.Length).IsEqual(config.Count);
    }
}
