using System.Collections.Generic;
using System.IO;
using System.Linq;
using GdUnit4;
using ClaudeCodeGameStudios;

namespace ClaudeCodeGameStudios.Tests;

/// <summary>
/// 世界表的<b>外部配置</b>守卫（B4：2026-10-21 把世界表从 C# 字面量搬到 <c>config/worlds.json</c>）。
///
/// <para><b>它守的是什么</b>：外部配置<b>坏了不能让游戏变形</b>——
/// 权重为 0 的世界会<b>静默消失</b>（抽不到、也不报错），缺字段的世界会造出空名字的村民。
/// 加载器必须<b>明确拒收</b>，由调用方退回内置表。</para>
///
/// <para>（为什么是「退回内置表」而不是「中止启动」：与价格表同一条原则——
/// <b>配置坏了要让游戏跑得下去</b>。）</para>
/// </summary>
[TestSuite]
public class HeroFactoryWorldTableTests
{
    private static string ConfigPath()
    {
        var dir = new DirectoryInfo(
            Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location)!);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "config", "worlds.json");
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        throw new FileNotFoundException("找不到 config/worlds.json");
    }

    /// <summary>仓库里那份世界表能读出来，且每个世界都是<b>完整</b>的。</summary>
    [TestCase]
    public void ConfigWorldsFile_Loads_AndEveryWorldIsComplete()
    {
        string json = File.ReadAllText(ConfigPath());

        bool ok = HeroFactory.World.TryLoadFromJson(json, out var worlds, out var error);

        Assertions.AssertThat(ok).IsTrue();
        Assertions.AssertThat(error).IsNull();
        Assertions.AssertThat(worlds.Length).IsGreater(0);
        foreach (var w in worlds)
        {
            Assertions.AssertThat(string.IsNullOrEmpty(w.Name)).IsFalse();
            Assertions.AssertThat(w.Weight).IsGreater(0);
            Assertions.AssertThat(w.Surname.Length).IsGreater(0);
            Assertions.AssertThat(w.Given.Length).IsGreater(0);
            Assertions.AssertThat(w.Traits.Length).IsGreater(0);
            Assertions.AssertThat(w.Personalities.Length).IsGreater(0);
        }
    }

    /// <summary>权重为 0 的世界 ⇒ <b>拒收</b>（否则它会静默消失）。</summary>
    [TestCase]
    public void TryLoadFromJson_NonPositiveWeight_IsRejected()
    {
        const string json = """
        { "worlds": [ { "name": "甲", "weight": 0, "surname": ["甲"], "given": ["乙"],
                       "traits": ["丙"], "personalities": ["傲"] } ] }
        """;

        bool ok = HeroFactory.World.TryLoadFromJson(json, out _, out var error);

        Assertions.AssertThat(ok).IsFalse();
        Assertions.AssertThat(error).IsNotNull();
    }

    /// <summary>名字为空的世界 ⇒ <b>拒收</b>（否则会造出无名村民，且它还在总权重里占份额）。</summary>
    [TestCase]
    public void TryLoadFromJson_EmptyName_IsRejected()
    {
        const string json = """
        { "worlds": [ { "name": "", "weight": 3, "surname": ["甲"], "given": ["乙"],
                       "traits": ["丙"], "personalities": ["傲"] } ] }
        """;

        bool ok = HeroFactory.World.TryLoadFromJson(json, out _, out var error);

        Assertions.AssertThat(ok).IsFalse();
        Assertions.AssertThat(error).IsNotNull();
    }

    /// <summary>缺一组数组 ⇒ 拒收（否则会造出空名字/空痕迹的村民）。</summary>
    [TestCase]
    public void TryLoadFromJson_MissingArray_IsRejected()
    {
        const string json = """
        { "worlds": [ { "name": "甲", "weight": 3, "surname": ["甲"], "given": ["乙"],
                       "personalities": ["傲"] } ] }
        """;

        bool ok = HeroFactory.World.TryLoadFromJson(json, out _, out var error);

        Assertions.AssertThat(ok).IsFalse();
        Assertions.AssertThat(error).IsNotNull();
    }

    /// <summary>坏 JSON ⇒ 拒收 + 有原因（不是静默的空表）。</summary>
    [TestCase]
    public void TryLoadFromJson_BrokenJson_IsRejected()
    {
        bool ok = HeroFactory.World.TryLoadFromJson("{ 这不是 json", out var worlds, out var error);

        Assertions.AssertThat(ok).IsFalse();
        Assertions.AssertThat(worlds.Length).IsEqual(0);
        Assertions.AssertThat(error).IsNotNull();
    }

    /// <summary>空表 ⇒ 拒收（否则「所有世界都没了」会变成一次永久的随机回退）。</summary>
    [TestCase]
    public void TryLoadFromJson_EmptyTable_IsRejected()
    {
        bool ok = HeroFactory.World.TryLoadFromJson("""{ "worlds": [] }""", out _, out var error);

        Assertions.AssertThat(ok).IsFalse();
        Assertions.AssertThat(error).IsNotNull();
    }

    /// <summary>
    /// <b>运行时</b>拿到的世界表一定是<b>可用</b>的（无论它来自外部文件还是内置兜底）。
    /// </summary>
    /// <remarks>
    /// 这条守的是「配置坏了 ⇒ 游戏仍然有世界可抽」——
    /// <c>World.All</c> 是静态字段，构造期只加载一次，所以兜底必须真的兜得住。
    /// </remarks>
    [TestCase]
    public void RuntimeWorldTable_IsUsable()
    {
        var all = HeroFactory.World.All;

        Assertions.AssertThat(all.Length).IsGreater(0);
        foreach (var w in all)
        {
            Assertions.AssertThat(string.IsNullOrEmpty(w.Name)).IsFalse();
            Assertions.AssertThat(w.Weight).IsGreater(0);
            Assertions.AssertThat(w.Surname.Length).IsGreater(0);
            Assertions.AssertThat(w.Given.Length).IsGreater(0);
            Assertions.AssertThat(w.Traits.Length).IsGreater(0);
            Assertions.AssertThat(w.Personalities.Length).IsGreater(0);
        }

        // 权重之和必须为正（否则随机抽签会拿到 total = 0）
        int total = all.Sum(w => w.Weight);
        Assertions.AssertThat(total).IsGreater(0);
    }

    /// <summary>装载真实配置文件 ⇒ 用上外部表；**装完要还原**（它是全局静态）。</summary>
    [TestCase]
    public void LoadWorldsFromFile_WithTheRealFile_ReplacesTheTable()
    {
        var before = HeroFactory.World.All;
        try
        {
            bool usedExternal = HeroFactory.World.LoadWorldsFromFile(ConfigPath());

            Assertions.AssertThat(usedExternal).IsTrue();
            Assertions.AssertThat(HeroFactory.World.All.Length).IsGreater(0);
            foreach (var w in HeroFactory.World.All)
            {
                Assertions.AssertThat(w.Weight).IsGreater(0);
                Assertions.AssertThat(w.Personalities.Length).IsGreater(0);
            }
        }
        finally
        {
            HeroFactory.World.All = before;
        }
    }

    /// <summary>文件不存在 / 坏了 ⇒ <b>退回内置表</b>，游戏仍然有世界可抽。</summary>
    [TestCase]
    public void LoadWorldsFromFile_BadInput_FallsBackToTheBuiltinTable()
    {
        var before = HeroFactory.World.All;
        try
        {
            bool missing = HeroFactory.World.LoadWorldsFromFile(
                Path.Combine(Path.GetTempPath(), "no-such-worlds-xyz.json"));

            string bad = Path.Combine(Path.GetTempPath(), "bad-worlds-xyz.json");
            File.WriteAllText(bad, "{ 这不是 json");
            bool broken = HeroFactory.World.LoadWorldsFromFile(bad);

            Assertions.AssertThat(missing).IsFalse();
            Assertions.AssertThat(broken).IsFalse();
            Assertions.AssertThat(HeroFactory.World.All.Length).IsGreater(0);
        }
        finally
        {
            HeroFactory.World.All = before;
        }
    }
}
