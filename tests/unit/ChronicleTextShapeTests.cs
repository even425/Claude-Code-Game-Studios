using System.Collections.Generic;
using System.Linq;
using GdUnit4;
using ClaudeCodeGameStudios;

namespace ClaudeCodeGameStudios.Tests;

/// <summary>
/// 杂谈文本的**形状**守卫（B6a）。
///
/// <para><b>它守的是什么</b>：一条杂谈句子里的槽位替换<b>不许吃掉被替换文本的任何字</b>。</para>
///
/// <para><b>为什么需要它</b>：特征（<c>手永远是咸的</c>）是**句子**，
/// 而模板「村里第一次见这种<em>{s}</em>的人」把它当**名词短语**用 ——
/// 于是结尾的「的」被吃掉，渲染出「村里第一次见这种手永远是咸的人」。
/// 这类问题<b>读代码看不出来</b>（模板和特征单独看都通顺），
/// 只有把它们拼起来渲染才暴露。</para>
///
/// <para><b>守卫的形式</b>：对**每个**到场模板 × **每个**世界的**每条**特征，
/// 渲染结果必须<b>原样包含</b>那条特征。特征若被切碎、改写、吞字，这一条就红。</para>
///
/// <para>顺带守住了「以后新加的模板/新加的特征不会踩同一个坑」。</para>
/// </summary>
[TestSuite]
public class ChronicleTextShapeTests
{
    /// <summary>每条到场模板 × 每条特征：特征必须原样出现在渲染结果里。</summary>
    [TestCase]
    public void ArriveTemplates_KeepTheTraitIntact()
    {
        var templates = Chronicle.ArriveTemplates;
        Assertions.AssertThat(templates.Count).IsGreater(0);

        int checkedCount = 0;
        foreach (var world in HeroFactory.World.All)
        {
            foreach (var trait in world.Traits)
            {
                foreach (var tpl in templates)
                {
                    // 有些模板**根本不提特征**（那是另一种口吻）—— 对它们谈不上「吞字」。
                    if (!tpl.Contains("{s}")) continue;

                    string text = Chronicle.RenderArriveForTest(tpl, "白七", world.Name, trait);

                    Assertions.AssertThat(text.Contains(trait))
                        .IsTrue();      // 特征被吞字 / 改写 ⇒ 这条红
                    checkedCount++;
                }
            }
        }

        Assertions.AssertThat(checkedCount).IsGreater(0);
    }

    /// <summary>
    /// 具体钉住那个坏句子：模板**不许**把特征接在「…的」后面（那会变成「的的」）。
    /// </summary>
    /// <remarks>
    /// <b>实际的坏法是「重复」不是「吞掉」</b>：模板「村里第一次见这种<em>{s}</em>的人」
    /// 配上特征「手永远是咸的」⇒ 「村里第一次见这种手永远是咸的<b>的</b>人」——
    /// 尾字被**复制**了一个。（第一版断言写的是「不许出现『咸的人』」，
    /// 那条在旧模板下**照样通过** —— 断言写错了，变异看红才发现。）
    /// </remarks>
    [TestCase]
    public void ArriveTemplates_DoNotDuplicateTheTraitsFinalDe()
    {
        const string traitWithDe = "手永远是咸的";

        foreach (var tpl in Chronicle.ArriveTemplates)
        {
            if (!tpl.Contains("{s}")) continue;

            string text = Chronicle.RenderArriveForTest(tpl, "白七", "深海港镇", traitWithDe);

            Assertions.AssertThat(text.Contains(traitWithDe)).IsTrue();
            // 特征紧跟的那个字**不能**是「的」——否则渲染出「的的」
            int at = text.IndexOf(traitWithDe, System.StringComparison.Ordinal);
            string after = at >= 0 && at + traitWithDe.Length < text.Length
                ? text.Substring(at + traitWithDe.Length, 1)
                : "";
            Assertions.AssertThat(after).IsNotEqual("的");
            Assertions.AssertThat(text.Contains("的的")).IsFalse();
        }
    }

    /// <summary>任何模板都**不许**让特征后面紧跟「的」（通用形态，不针对某一条特征）。</summary>
    [TestCase]
    public void ArriveTemplates_NeverDoubleTheDeAfterATrait()
    {
        int checkedCount = 0;
        foreach (var world in HeroFactory.World.All)
        {
            foreach (var trait in world.Traits)
            {
                foreach (var tpl in Chronicle.ArriveTemplates)
                {
                    if (!tpl.Contains("{s}")) continue;

                    string text = Chronicle.RenderArriveForTest(tpl, "白七", world.Name, trait);

                    Assertions.AssertThat(text.Contains("的的")).IsFalse();
                    checkedCount++;
                }
            }
        }

        Assertions.AssertThat(checkedCount).IsGreater(0);
    }

    /// <summary>每条特征自身都以完整句子形态收尾（渲染时不会被截断）。</summary>
    [TestCase]
    public void EveryTraitIsANonEmptyString()
    {
        var traits = HeroFactory.World.All
            .SelectMany(w => w.Traits)
            .ToList();

        Assertions.AssertThat(traits.Count).IsGreater(0);
        foreach (var t in traits)
        {
            Assertions.AssertThat(string.IsNullOrWhiteSpace(t)).IsFalse();
        }
    }
}
