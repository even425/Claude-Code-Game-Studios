using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GdUnit4;

namespace ClaudeCodeGameStudios.Tests;

/// <summary>
/// <b>文档卫生守卫</b>：规格与决策文档里不许出现控制字符。
///
/// <para><b>它守的是什么</b>：一次真实事故。往 <c>production/07</c> 写 Markdown 时用了
/// PowerShell 的<b>双引号 here-string</b>（<c>@"..."@</c>）—— 那是<b>可展开</b>字符串，
/// 里面的 <c>`</c> 仍然按转义处理：<c>`0</c> 变成 NUL、<c>`7</c> 变成 BEL。
/// 于是原文的 <c>`07`</c> 变成了「NUL + 7」，<b>整行被污染</b>，
/// 而肉眼看过去「好像也像那么回事」—— 只是少了一个字符、少了一个反引号。
/// 污染一共 14 处，散在 4 行里，<b>没有任何报错</b>。</para>
///
/// <para><b>为什么要写成测试</b>：那 14 处污染是<b>事后</b>靠「统计 <c>ord(c) &lt; 32</c>」
/// 发现的 —— 也就是说，<b>它能被机器发现，只是没人让机器去发现</b>。
/// 把它变成一条每次 <c>dotnet test</c> 都跑的断言，
/// 下次同样的事故会在<b>写完那一分钟</b>就红，而不是几小时之后。</para>
///
/// <para><b>判据</b>：除 <c>\r</c> / <c>\n</c> / <c>\t</c> 外，
/// Markdown 文件里不应出现任何 ASCII 控制字符。它们在 Markdown 里没有合法用途，
/// 却恰恰是「转义被吃掉」最典型的痕迹。</para>
/// </summary>
[TestSuite]
public class DocumentationHygieneTests
{
    private static string ProductionDir()
    {
        var dir = new DirectoryInfo(
            Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location)!);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "production");
            if (Directory.Exists(candidate)
                && File.Exists(Path.Combine(dir.FullName, "Claude-Code-Game-Studios.csproj")))
            {
                return candidate;
            }
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("找不到 production/ 目录");
    }

    /// <summary>
    /// 规格与决策文档里不许有控制字符（<c>\r</c> / <c>\n</c> / <c>\t</c> 除外）。
    /// </summary>
    [TestCase]
    public void ProductionDocs_ContainNoControlCharacters()
    {
        var offenders = new List<string>();

        foreach (var file in Directory.EnumerateFiles(ProductionDir(), "*.md", SearchOption.TopDirectoryOnly))
        {
            string text = File.ReadAllText(file);
            int lineNo = 0;
            foreach (var line in text.Replace("\r\n", "\n").Split('\n'))
            {
                lineNo++;
                var bad = line.Where(c => char.IsControl(c) && c != '\t').ToList();
                if (bad.Count > 0)
                {
                    offenders.Add($"{Path.GetFileName(file)}:{lineNo} → "
                                  + string.Join(",", bad.Select(c => $"U+{(int)c:X4}")));
                }
            }
        }

        Assertions.AssertThat(string.Join(" | ", offenders)).IsEmpty();
    }
}
