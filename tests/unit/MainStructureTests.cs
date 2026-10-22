using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using GdUnit4;

namespace ClaudeCodeGameStudios.Tests;

/// <summary>
/// <b>UT-01 结构断言</b>（<c>06_第一阶段验收与测试计划_V1.0.md</c> §结构断言）——
/// 把「不许有巨型 GameManager」从一句口号变成<b>会红的测试</b>。
///
/// <para><b>它守什么</b>：<c>06</c> 的原文是
/// 「无文件同时拥有『模拟推进 + 战斗执行 + 结算入账 + 存档写入』四类职责中的三类以上」。
/// 判据是「<b>拥有</b>」—— 哪个文件<b>实现</b>了这些活儿。
/// 玩家命令入口（<c>OnTask</c> / <c>OnStall</c>）只是<b>翻译</b>点击，
/// 规则在别处，所以不算「拥有」。</para>
///
/// <para><b>为什么要有「穷尽性」这一条</b>：只检查「清单里的成员有没有越界」是不够的 ——
/// 清单会腐化，<b>新加的方法默认落进白名单</b>，于是规则慢慢从测试的视野里溜走。
/// 所以这里反过来：<b><c>src/Main*.cs</c> 里的每个方法都必须被显式分类</b>，
/// 漏分类就红 —— 新增方法的人必须回答「我这条属于哪一类（或都不属于）」。</para>
///
/// <para><b>为什么自检文件另有���条</b>：<c>Main.SelfTest.cs</c> 会碰到所有系统
/// （它当然要跑副本、跑存档、跑交易）。如果按「碰到就算拥有」算，
/// 它会立刻变成第二个巨型 GameManager —— 而且是最坏的那种：
/// <b>验证装置伪装成实现装置</b>，「谁拥有哪类职责」就再也说不清了。
/// 所以规定：<b>自检文件里只允许有自检方法</b>。</para>
/// </summary>
[TestSuite]
public class MainStructureTests
{
    // ── UT-01 的四类职责 ────────────────────────────────────────────
    private static readonly (string Name, string[] Members)[] Responsibilities =
    {
        ("模拟推进", new[]
        {
            "_Process", "TickBuild", "TickIndependentBuilds", "OnIndependentBuildingCompleted",
            "TickWorldEvents", "InitWorldEvents", "ScaleBeastTideMonster", "BeastTideHeroStats",
            "ShouldTrigger", "BeastTideEffectiveTier", "RestoreWorldEvents",
            "RunCollect", "CollectOneDay", "OpenStallsForIdleHeroes", "RunDailyShoppingIfNeeded",
            "DispatchDungeonForCollection", "DispatchOneDungeonForCollection",
            "UpgradeVillageForCollection", "VillagePurchaseRound",
        }),
        ("战斗执行", new[]
        {
            "ResolveDungeon", "ComputeLoot", "ResolveExpedition", "NextDungeonRunId",
            "CombatStatsFor", "PrepareDungeon",
        }),
        ("结算入账", new[]
        {
            "RewardsFor", "ApplyWrites", "Deposit", "GatherFor",
            "EmitTradeCompleted", "EmitDungeonLooted",
            "ApplyWorldEventEffect", "ApplyVillageResourceChange",
        }),
        ("存档写入", new[]
        {
            "BuildSave", "BuildBagSave", "ApplySave",
        }),
    };

    /// <summary>
    /// 自检装置的方法 —— <b>自检入口</b>，以及它们用的<b>夹具</b>。
    /// 它们是<b>验证装置</b>，不拥有任何职责。
    /// <para>
    /// 夹具也在这里，是因为自检文件**只允许有验证装置**：
    /// 放一个「让英雄在场且空闲」的小工具进来是合规的（它只服务于自检），
    /// 放一段**玩法逻辑**进来才是「验证装置伪装成实现装置」——
    /// 那条断言守的正是这个区别。
    /// </para>
    /// </summary>
    private static readonly string[] SelfTestMethods =
    {
        "SelfTest", "RunEconomySelfTest", "RunClickSelfTest", "RunEventsSelfTest",
        "RunTradeSelfTest", "RunMarketSelfTest", "RunShoppingSelfTest",
        "RunGrantSelfTest", "RunRelationsSelfTest", "RunSaveSelfTest",
        "RunDungeonSelfTest", "RunCaravanSelfTest", "RunBuildSelfTest",
        "SelfTestEnsurePresentAndIdle",   // 自检夹具：把英雄摆成「在场且空闲」
    };

    /// <summary>
    /// 「不拥有四类职责中的任何一类」的方法 —— <b>穷尽性白名单</b>。
    /// 新增方法若不在这里、也不属于四类职责之一，测试就会红。
    /// </summary>
    private static readonly string[] Unclassified =
    {
        // 组合根：启动接线、随机源、参数解析
        "_Ready", "ResolveBootSeed", "NextInt", "NextUnit",
        "LoadPriceBook", "InitialHeroCount", "ParseGoldRewardUnits",
        "PersonalityRateMultiplier",
        // 表现层：场景搭建、界面刷新、玩家命令入口、输入
        "BuildScene", "MakeScroll", "Header", "MakeButton",
        "AgentOf", "IsOnMission", "IsPresent",
        "SocialTargetCandidates", "OnSocialCompleted", "SocialStageLine",
        "SocialPreferenceWeight",
        "OnRosterItemActivated", "OnSummon", "OnSummonPaid", "SummonHero", "OnTask", "OnUpgrade",
        "OnBuild", "OnBuildFence", "OnBuildTower", "OnTrialTower", "RefreshBuildButton",
        // D-093：玩家自选落点（放置模式）
        "BeginPlacement", "CancelPlacement", "PlacePendingAt", "UpdatePlacementPreview",
        "PendingPlacement",
        "OnStall", "OnStallConfirm", "OnMarket", "OnManualTradeCompleted",
        "OnCaravan", "OnCaravanBought",   // D-081：商队面板（玩家命令入口）
        "OnSave", "OnLoad",
        "Refresh", "RefreshRoster", "RosterLine", "ShoppingNoteFor",
        "ActiveSitesText",   // D-082：顶栏显示在施工的几栋（表现层）
        "RefreshRelationSummary", "TierName",
        "_UnhandledInput", "ProcessShot", "ResolveShotDelay",
        "VisibleTextSnapshot",        // MT-04：玩家可见的界面文字快照（表现层）
        // 事件管道：领域事件 → 杂谈条目
        "Add", "PublishEvent", "Emit",
        // 存档：组装 / 读回 / 枚举解析
        "SelfTestSaves", "ParseStallStatus",
        // 采集器的测量与打印（不是玩法推进）
        "RecordEconomySnapshot", "Percentile",
        "PublishShoppingResults", "RecordShoppingMissed", "PrintShoppingResults",
        "NameOfHero",
        // 村庄模型的构造
        "BuildInitialVillage",
    };

    /// <summary>单文件行数上限。UT-01 原文把阈值列为【待定】（「由 lead-programmer 定」）。</summary>
    private const int MaxFileLines = 1200;

    // ── 目录与扫描 ──────────────────────────────────────────────────
    private static string SrcDir()
    {
        var dir = new DirectoryInfo(
            Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location)!);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "src");
            if (Directory.Exists(candidate)
                && File.Exists(Path.Combine(dir.FullName, "Claude-Code-Game-Studios.csproj")))
            {
                return candidate;
            }
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("找不到 src/ 目录");
    }

    /// <summary>顶层方法声明（4 空格缩进 + 访问修饰符 + 有参数列表）。</summary>
    private static readonly Regex MethodDecl = new(
        @"^    (?:private|public|internal|protected)\s+(?:(?:static|readonly|sealed|override|async|partial|new|virtual|abstract|extern|unsafe)\s+)*" +
        @"[\w<>\[\],\.\?:\s]*?\b(\w+)\s*\(",
        RegexOptions.Compiled);

    private static readonly Regex TypeDecl = new(
        @"^    (?:private|public|internal|protected)\s+(?:(?:sealed|abstract|static|readonly|partial|record|class|struct|enum|interface)\s+)*" +
        @"(?:record\s+struct|record|class|struct|enum|interface)\s+(\w+)",
        RegexOptions.Compiled);

    private static List<string> MethodsOf(string path)
    {
        var result = new List<string>();
        foreach (var line in File.ReadAllLines(path))
        {
            var t = line.TrimStart();
            if (t.StartsWith("//") || t.StartsWith("///") || t.StartsWith("*")) continue;
            // 嵌套类型（如 `private readonly record struct Job(...)`）不是方法
            if (TypeDecl.IsMatch(line)) continue;

            var m = MethodDecl.Match(line);
            if (!m.Success) continue;

            string name = m.Groups[1].Value;
            // `= new(...)` 这类<b>字段初始化器</b>不是方法：
            // 判据是「( 之前出现过 =」⇒ 那对括号属于初始化表达式，不属于参数表。
            int at = line.IndexOf(name, StringComparison.Ordinal);
            if (at > 0 && line.Substring(0, at).Contains('=')) continue;
            if (name is "new" or "typeof" or "default" or "nameof") continue;

            result.Add(name);
        }
        return result;
    }

    private static List<string> TypesOf(string path)
    {
        var result = new List<string>();
        foreach (var line in File.ReadAllLines(path))
        {
            var t = line.TrimStart();
            if (t.StartsWith("//") || t.StartsWith("///") || t.StartsWith("*")) continue;
            var m = TypeDecl.Match(line);
            if (m.Success) result.Add(m.Groups[1].Value);
        }
        return result;
    }

    private static IEnumerable<string> CsFiles(string dir) =>
        Directory.EnumerateFiles(dir, "*.cs", SearchOption.AllDirectories);

    // ── 断言 1：领域层不得依赖 Godot（UT-01 (b)）─────────────────────
    // ⚠️ **这条不写在这里** —— 它已由 `DomainLayerPurityTests` 覆盖，
    // 而且那份**更严**：它会剥掉注释与字符串再扫（否则 `// 这里提到 Godot`
    // 会被当成违规），还自带一条「扫描器不是空转」的自检。
    // **弱重复比没有守卫更糟** —— 它让人以为这一条有人盯着，
    // 实际盯着的却是另一条更容易被改弱的规则。详见该文件。
    [TestCase]
    public void GodotPurity_IsGuardedByTheStrongerExistingTest()
    {
        // 本测试只负责「指路」：确认那条更强的守卫**还在**（文件没被删掉）。
        var path = Path.Combine(SrcDir(), "..", "tests", "unit", "architecture",
                                "DomainLayerPurityTests.cs");
        Assertions.AssertThat(File.Exists(path)).IsTrue();
    }

    // ── 断言 2：领域层不得有 static 可变状态（UT-01 (a)）────────────
    [TestCase]
    public void DomainLayer_HasNoStaticMutableState()
    {
        // 判据：static 成员**且可写** ⇒ 可变全局状态。
        //
        // 先把成员名摘出来，再看名字后面跟的是什么：
        //   `(` ⇒ 方法（不是状态）；`=>` ⇒ 计算属性（不可写）；
        //   `{ ... }` ⇒ 属性（只有带 set 才可写）；`=` / `;` ⇒ 字段（可写）。
        // readonly / const 已在正则里排除。
        //
        // **为什么不能简单按「含 static」判**：领域层有一批**不可变**的常量表
        // （`static PriceBook Default { get; }` / `static HeroId None => ...`）。
        // 把它们算成「可变全局状态」＝ 误报，而误报的守卫比没有守卫更糟 ——
        // 它会逼着人给真问题开豁免。
        var decl = new Regex(
            @"^\s*(?:public|internal|private|protected)\s+static\s+(?!readonly\b|const\b)(?<rest>.+)$",
            RegexOptions.Compiled);
        var nameOf = new Regex(@"(\w+)\s*$", RegexOptions.Compiled);

        var offenders = new List<string>();
        foreach (var file in CsFiles(Path.Combine(SrcDir(), "Domain")))
        {
            foreach (var line in File.ReadAllLines(file))
            {
                var d = decl.Match(line);
                if (!d.Success) continue;
                string rest = d.Groups["rest"].Value.Trim();

                // `static class X` / `static struct X` ⇒ 嵌套类型声明，不是状态字段。
                // （它内部若有 static 字段，那些行会被单独扫到。）
                if (Regex.IsMatch(rest, @"^(?:sealed\s+|abstract\s+|partial\s+)*"
                                        + @"(?:class|struct|record|enum|interface)\b"))
                {
                    continue;
                }

                // 切出「成员名」与「尾部」：尾部是名字后面的第一个
                // `(` / `{` / `=` / `;` 及其后内容。
                int cut = rest.IndexOfAny(new[] { '(', '{', '=', ';' });
                string head = cut < 0 ? rest : rest.Substring(0, cut);
                string tail = cut < 0 ? "" : rest.Substring(cut).TrimStart();

                var nm = nameOf.Match(head);
                if (!nm.Success) continue;

                if (tail.StartsWith("(")) continue;                  // 方法
                if (tail.StartsWith("=>")) continue;                 // 计算属性
                if (tail.StartsWith("{"))
                {
                    // 属性：带 set 才可写；{ get; } / { get; init; } 都不可写
                    if (Regex.IsMatch(tail, @"\{[^}]*\bset\b")) continue;
                    continue;
                }
                // 走到这里就是**可写字段**
                offenders.Add($"{Path.GetFileName(file)}: {line.Trim()}");
            }
        }

        Assertions.AssertThat(string.Join(" | ", offenders)).IsEmpty();
    }

    // ── 断言 3：职责清单不许腐化（每个成员都要真的存在）──────────────
    [TestCase]
    public void ResponsibilityLists_AreNotRotten()
    {
        var all = new HashSet<string>();
        foreach (var file in CsFiles(SrcDir()))
        {
            foreach (var m in MethodsOf(file)) all.Add(m);
        }

        var missing = new List<string>();
        foreach (var (name, members) in Responsibilities)
        {
            foreach (var m in members)
            {
                if (!all.Contains(m)) missing.Add($"{name}.{m}");
            }
        }
        foreach (var m in SelfTestMethods)
        {
            if (!all.Contains(m)) missing.Add($"自检.{m}");
        }

        Assertions.AssertThat(string.Join(" | ", missing)).IsEmpty();
    }

    // ── 断言 4：穷尽性 —— Main 的每个方法都必须被显式分类 ────────────
    [TestCase]
    public void EveryMainMethod_IsExplicitlyClassified()
    {
        var classified = new HashSet<string>(SelfTestMethods);
        foreach (var (_, members) in Responsibilities) classified.UnionWith(members);
        classified.UnionWith(Unclassified);

        var unclassified = new List<string>();
        foreach (var file in CsFiles(SrcDir()).Where(f => Path.GetFileName(f).StartsWith("Main")))
        {
            foreach (var m in MethodsOf(file))
            {
                if (!classified.Contains(m)) unclassified.Add($"{Path.GetFileName(file)}.{m}");
            }
        }

        Assertions.AssertThat(string.Join(" | ", unclassified)).IsEmpty();
    }

    // ── 断言 5：任一文件不得同时拥有三类以上职责（UT-01 (c)）────────
    [TestCase]
    public void NoFile_OwnsThreeOrMoreResponsibilities()
    {
        var offenders = new List<string>();
        foreach (var file in CsFiles(SrcDir()))
        {
            var methods = new HashSet<string>(MethodsOf(file));
            var owned = Responsibilities
                .Where(r => r.Members.Any(m => methods.Contains(m)))
                .Select(r => r.Name)
                .ToList();

            if (owned.Count >= 3)
            {
                offenders.Add($"{Path.GetFileName(file)}: {string.Join(" + ", owned)}");
            }
        }

        Assertions.AssertThat(string.Join(" | ", offenders)).IsEmpty();
    }

    // ── 断言 6：自检文件里只允许有自检方法 ───────────────────────────
    [TestCase]
    public void SelfTestFile_ContainsOnlySelfTestMethods()
    {
        // ⚠️ **扫所有 `Main.SelfTest*.cs`**，不只 `Main.SelfTest.cs`。
        // 原因：自检文件会撞上「单文件 ≤ 1200 行」的上限（UT-01 的行数封顶）⇒
        // 于是自检**必然会被拆成多个文件**（`Main.SelfTest.Stall.cs` 就是这么来的）。
        // ⇒ 若这条断言只盯着**一个**文件名，拆分就变成了**绕过**这条规则的缺口：
        // 把一个玩法方法挪到新的自检文件里，它就不再受「只允许有自检方法」约束。
        var dir = SrcDir();
        var files = CsFiles(dir)
            .Where(f => Path.GetFileName(f).StartsWith("Main.SelfTest"))
            .ToList();

        // 防空转：至少要有那两个文件（否则「扫不到任何自检文件」也是绿）
        Assertions.AssertThat(files.Count).IsGreaterEqual(2);

        var allowed = new HashSet<string>(SelfTestMethods);
        var strays = new List<string>();
        foreach (var file in files)
        {
            foreach (var m in MethodsOf(file))
            {
                if (!allowed.Contains(m)) strays.Add($"{Path.GetFileName(file)}.{m}");
            }
        }

        Assertions.AssertThat(string.Join(" | ", strays)).IsEmpty();
    }

    // ── 断言 7：没有文件重新长成巨石化 ───────────────────────────────
    [TestCase]
    public void NoFile_ExceedsTheLineCeiling()
    {
        var offenders = new List<string>();
        foreach (var file in CsFiles(SrcDir()))
        {
            int n = File.ReadAllLines(file).Length;
            if (n > MaxFileLines)
            {
                offenders.Add($"{Path.GetFileName(file)}: {n} 行（上限 {MaxFileLines}）");
            }
        }

        Assertions.AssertThat(string.Join(" | ", offenders)).IsEmpty();
    }

    // ── 断言 8：主场景确实被拆开了（不是「一个文件行数没超」而已）────
    [TestCase]
    public void Main_IsSplitByResponsibility()
    {
        var dir = SrcDir();
        var parts = new[]
        {
            "Main.cs", "Main.Ui.cs", "Main.Expedition.cs", "Main.Save.cs",
            "Main.Collector.cs", "Main.Events.cs", "Main.SelfTest.cs",
            // 自检拆出来的第二个文件（`Main.SelfTest.cs` 撞上了 1200 行上限 ⇒ 拆）
            "Main.SelfTest.Stall.cs",
        };

        var missing = parts.Where(p => !File.Exists(Path.Combine(dir, p))).ToList();
        Assertions.AssertThat(string.Join(" | ", missing)).IsEmpty();

        // 组合根必须明显瘦身（它原来 4528 行、一个人扛四类职责）
        int root = File.ReadAllLines(Path.Combine(dir, "Main.cs")).Length;
        Assertions.AssertThat(root).IsLessEqual(MaxFileLines);
        Assertions.AssertThat(root).IsLess(1200);
    }
}
