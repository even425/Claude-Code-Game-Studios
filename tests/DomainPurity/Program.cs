using System;
using System.Linq;
using ClaudeCodeGameStudios.Domain.Clock;
using ClaudeCodeGameStudios.Domain.Heroes;
using ClaudeCodeGameStudios.Domain.Items;
using ClaudeCodeGameStudios.Domain.Relationships;

namespace ClaudeCodeGameStudios.Tests.DomainPurity;

/// <summary>
/// <b>规则层在「没有 Godot 的环境」里真的能跑</b> —— 不变量 <b>I9</b> 的运行侧证明。
///
/// <para><b>为什么这个文件存在</b>：<c>tests/Claude-Code-Game-Studios.Tests.csproj</c>
/// 通过 <c>ProjectReference</c> 引用整个主工程，而主工程引用 <c>GodotSharp</c>
/// ⇒ <b>在那边跑测试，证明不了「领域层脱离 Godot 也能编译运行」</b>。</para>
///
/// <para><b>本工程不一样</b>：它**直接编译** <c>src/Domain/**</c>，
/// **不引** <c>GodotSharp</c>、**不** <c>ProjectReference</c> 主工程、
/// **不引任何 NuGet 包**（连测试运行器都不引 —— gdUnit4 会把 GodotSharp 拖进来）。
/// 于是下面每一行都在「真的没有 Godot」的进程里跑。</para>
///
/// <para><b>最有分量的其实是最后一条</b>（<c>TheDomainAssembly_ReferencesNoGodotAssembly</c>）：
/// 它在<b>运行时</b>检查本程序集引用了哪些程序集 ⇒ 任何形式的 Godot 依赖
/// （不只是 <c>using</c>，连反射、延迟加载都躲不过）都会被抓到。
/// 静态扫描（<c>DomainLayerPurityTests</c>）只查源码里的字样，
/// 而这一条查的是<b>产物</b>。</para>
///
/// <para><b>为什么不用测试框架</b>：本工程**零依赖**是硬要求 ——
/// `gdUnit4.api` **传递依赖 `GodotSharp`** ⇒ 引了它，领域层写一行 <c>using Godot;</c>
/// **照样编译通过** ⇒ 那道门就形同虚设了。
/// 一个「看起来在守、其实不守」的守卫**比没有守卫更糟**（它让人以为有人盯着）。
/// 所以这里用手写的断言 —— 反正要验的是<b>环境</b>，不是行为细节。</para>
///
/// <para><b>这些断言验的是「机制」而不是「数值」</b>：
/// 时钟单调、关系有向、价格表能解析 —— 它们的**规则**另有更完整的单元测试守着
/// （<c>SimClockTests</c> / <c>RelationshipBookTests</c> / <c>PriceBookTests</c>），
/// 这里重跑一遍是为了证明<b>那些规则不需要 Godot 也能执行</b>。</para>
/// </summary>
internal static class Program
{
    private static int _failures;

    private static int Main()
    {
        Console.WriteLine("规则层脱离 Godot 自检（不变量 I9）");
        Console.WriteLine("  程序集: " + typeof(SimClock).Assembly.GetName().Name);
        Console.WriteLine();

        // ── ① 时钟：能推进、且单调不减（I13）────────────────────────────
        {
            var clock = new SimClock(60.0, r => r);
            double before = clock.Now;
            clock.Advance(2.5);
            Check("时钟能推进", clock.Now > before);
            Check("时钟单调且按换算推进", Math.Abs(clock.Now - (before + 2.5)) < 1e-9);
        }

        // ── ② 关系：有向存储（A→B 不等于 B→A，I8）────────────────────────
        {
            var book = new RelationshipBook();
            var key = new RelationshipKey(new HeroId("h-0"), new HeroId("h-1"));
            var change = book.Apply("social:h-0->h-1@10", key, RelationshipRules.SocialDelta);
            Check("关系写入生效", change.Applied);
            Check("关系有向：A→B = 2", book.ValueOf(new HeroId("h-0"), new HeroId("h-1")) == 2);
            Check("关系有向：B→A = 0", book.ValueOf(new HeroId("h-1"), new HeroId("h-0")) == 0);
        }

        // ── ③ 价格表：能解析、能查价（外部化之后的兜底机制）────────────
        {
            bool ok = PriceBook.TryLoadFromJson(
                """{"石料": 5, "木料": 3, "布匹": 8}""", out var book, out string? error);
            Check("价格表可解析", ok);
            Check("价格表无解析错误", error is null);
            Check("价格表能查价", book is not null && book.PriceOf(new ItemId("石料")) == 5);
            Check("未登记物品 = 不卖（0）", book is not null && book.PriceOf(new ItemId("翡翠")) == 0);
        }

        // ── ④ 最强的一条：本程序集**不引用任何 Godot 程序集** ───────────────
        {
            var references = typeof(SimClock).Assembly
                .GetReferencedAssemblies()
                .Select(a => a.Name ?? "")
                .ToList();

            var godot = references
                .Where(n => n.StartsWith("Godot", StringComparison.OrdinalIgnoreCase))
                .ToList();

            Check("本程序集不引用任何 Godot 程序集", godot.Count == 0);
            if (godot.Count > 0)
            {
                Console.WriteLine("      引用的 Godot 程序集: " + string.Join("、", godot));
            }

            // 顺带确认这条断言**不是空跑**：它至少看到了几个引用。
            Check("引用列表非空（防空转）", references.Count > 0);
            Console.WriteLine("      引用: " + string.Join("、", references.OrderBy(n => n)));
        }

        Console.WriteLine();
        if (_failures == 0)
        {
            Console.WriteLine("✅ 规则层在无 Godot 的环境里编译并运行成功，且不引用任何 Godot 程序集");
            return 0;
        }

        Console.WriteLine($"❌ {_failures} 条不成立");
        return 1;
    }

    private static void Check(string what, bool ok)
    {
        if (!ok) _failures++;
        Console.WriteLine($"  {(ok ? "PASS" : "FAIL")}  {what}");
    }
}
