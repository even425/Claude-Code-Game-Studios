using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GdUnit4;

namespace ClaudeCodeGameStudios.Tests;

/// <summary>
/// <b>占位价只许出现在两个地方</b>：<c>config/prices.json</c>（开发者改的那份）
/// 与内置兜底 <c>PriceBook.Default</c>。
///
/// <para><b>它守的是什么</b>：价格是 <c>07</c> §2 的<b>临时参数</b>（<b>P-14</b> 待定），
/// 而 <c>07</c> §2.1 那条纪律写的是「<b>临时参数必须集中管理，不能悄悄变成正式设计规则</b>」。
/// 这条纪律之所以需要机器来守，是因为「悄悄变成设计规则」这个过程<b>没有任何报错</b>：
/// 一次「顺手用一下默认价」的引用不会让任何东西变红，只是让那份占位值
/// <b>变成了事实</b>。</para>
///
/// <para><b>真事</b>：2026-10-21，14 条单元测试把占位价（石料 5）写成了断言事实
/// （余额 95 / 90 / 20 / 0 …）。于是开发者一改价格，<b>14 条测试当场红</b> ——
/// 而 <c>production/qa/recalibrate.py</c> 的第 2 步正是 <c>dotnet test</c>，
/// 「改价格 → 重跑标定」这条承诺<b>恰好在最需要它的地方断了</b>。</para>
///
/// <para><b>修法</b>：单元测试<b>自己造一张价格表</b>（<c>tests/unit/TestPrices.cs</c>）——
/// 服务层本来就接受注入价格表，之前只是「恰好」传了全局默认表。
/// 断言里的数仍然是<b>精确的</b>，但它们与<b>产品的占位价彻底脱钩</b>。</para>
///
/// <para><b>为什么留两个例外</b>：
/// <list type="bullet">
/// <item><c>PriceBookTests</c> —— 它<b>就是</b>在测默认表本身；</item>
/// <item><c>PriceBookConfigConsistencyTests</c> —— 它要对的正是「配置 ↔ 兜底」那两份副本。</item>
/// </list>
/// 这两个文件的存在本身就是「占位价登记在案」的记录（测试夹具里的 5 / 3 / 8 也是）。</para>
///
/// <para><b>不查的数字</b>：夹具里那些 <c>5L</c> / <c>95L</c> 之类<b>不查</b> ——
/// 它们是<b>测试自己</b>的数，与产品无关。查它们会把「测试用 5 当夹具」也误判成违规。</para>
/// </summary>
[TestSuite]
public class PlaceholderPriceOnlyInTwoPlacesTests
{
    /// <summary>允许引用 <c>PriceBook.Default</c> 的测试文件（它们就是关于它的那两个）。</summary>
    private static readonly string[] Allowed =
    {
        "tests/unit/items/PriceBookTests.cs",
        "tests/unit/items/PriceBookConfigConsistencyTests.cs",
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

    /// <summary>
    /// 除那两个文件外，测试代码里不许出现 <c>PriceBook.Default</c>。
    /// </summary>
    [TestCase]
    public void Tests_DoNotUseTheGlobalPlaceholderPriceAsAFixture()
    {
        string root = RepoRoot();
        string testsRoot = Path.Combine(root, "tests");

        // ⚠️ 搜索词**拆成两段拼** —— 否则这个守卫文件自己就含有那个连续字符串，
        // 于是它会把自己也判成违规（自指的守卫最容易被自己绊倒）。
        string token = "PriceBook" + ".Default";

        var offenders = new List<string>();
        int scanned = 0;

        foreach (var file in Directory.EnumerateFiles(testsRoot, "*.cs", SearchOption.AllDirectories))
        {
            string rel = Path.GetRelativePath(root, file).Replace('\\', '/');
            if (Allowed.Contains(rel)) continue;

            scanned++;
            // ⚠️ **先剥掉注释** —— 「在注释里提到它」不是「拿它当夹具」。
            // 不剥的话，连「说明为什么要换成夹具」的注释都会被判成违规。
            string code = StripLineComments(File.ReadAllText(file));
            if (code.Contains(token))
            {
                offenders.Add($"{rel} 用了 {token}（占位价）当夹具 —— 请改用 TestPrices.Book");
            }
        }

        // 防空转：必须真的扫到了文件，且那两个例外文件必须**存在**
        // （例外清单本身过期 ⇒ 守卫会变成「什么都在允许」）。
        Assertions.AssertThat(scanned).IsGreater(10);
        foreach (var allowed in Allowed)
        {
            Assertions.AssertThat(File.Exists(Path.Combine(root, allowed.Replace('/', Path.DirectorySeparatorChar))))
                .IsTrue();
        }

        Assertions.AssertThat(string.Join(" | ", offenders)).IsEmpty();
    }

    /// <summary>
    /// 去掉行注释（<c>//</c> 之后到行尾）。
    /// <para><b>已知粗糙处</b>：字符串字面量里的 <c>//</c>（例如 URL）会把该行后半截也切掉 ——
    /// 对本条检查而言无害：切掉的只是「本来就该被忽略的文本」，
    /// 而切掉之后**只会更少**误报，不会更少漏报（漏报由 ① 号用例兜着）。</para>
    /// </summary>
    private static string StripLineComments(string text)
    {
        var lines = text.Replace("\r\n", "\n").Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            int at = lines[i].IndexOf("//", StringComparison.Ordinal);
            if (at >= 0) lines[i] = lines[i][..at];
        }
        return string.Join("\n", lines);
    }
}
