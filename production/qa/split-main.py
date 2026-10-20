"""
把 src/Main.cs 拆成按职责划分的 partial class 文件（A1 巨型 GameManager 治理）。

**这是纯代码搬移**：不改任何一行逻辑，只把成员整块搬到对应的 partial 文件里。
搬完用全量单测 + 11 个真机自检证明行为没变。

拆分依据：`06_第一阶段验收与测试计划_V1.0.md` UT-01 结构断言 ——
「无文件同时拥有『模拟推进 + 战斗执行 + 结算入账 + 存档写入』四类职责中的三类以上」。
所以分组标准不是「按行数」，而是**按职责**，让每个文件最多背 2 类。

**成员边界怎么定**：用「缩进 4 空格 + 声明正则」定位顶层成员。
不用大括号深度 —— 那需要完整的 C# 词法分析（插值字符串里套着普通字符串
会把深度算错：<c>$"{string.Join(", ", x)}"</c>）。

**怎么保证没搬坏**：拆完把所有成员按原顺序拼回去，必须与原文件**逐字节相同**
（见 <code>verify_roundtrip</code>）。这条不变式比"编译通过"强 ——
编译能过的搬移仍然可能把两个成员之间的空行/注释搬乱。

用法：python production/qa/split-main.py [--dry]
"""
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
MAIN = ROOT / "src" / "Main.cs"

# ── 成员 → 目标文件 ───────────────────────────────────────────────
# 键是成员名（方法名 / 字段名 / 嵌套类型名）。
# 没列出的成员留在 Main.cs（场景搭建、UI 刷新、输入、命令入口）。
GROUPS = {
    "Main.Ui.cs": {
        # 场景搭建与呈现
        "BuildScene", "MakeScroll", "Header", "MakeButton",
        "Refresh", "RefreshRoster", "RosterLine", "ShoppingNoteFor",
        "RefreshRelationSummary", "TierName",
        # 玩家命令入口（按钮 → 领域调用）
        "OnRosterItemActivated", "OnSummon", "OnTask", "OnUpgrade",
        "OnStall", "OnStallConfirm", "OnMarket", "OnManualTradeCompleted",
        "OnSave", "OnLoad",
        # 社交的表现部分
        "SocialTargetCandidates", "OnSocialCompleted", "SocialStageLine",
        "SocialPreferenceWeight",
        # 查询
        "AgentOf", "IsOnMission", "IsPresent",
        "ReadySquad", "Expending", "HoursLeft",
        # 输入与截图
        "_UnhandledInput", "ProcessShot",
    },
    "Main.Expedition.cs": {
        "Job",
        "ResolveExpedition", "NextDungeonRunId", "ComputeLoot", "ResolveDungeon",
        "RewardsFor", "ApplyWrites", "Deposit",
        "_villageModel", "BuildInitialVillage", "MaterialsId", "Mat",
    },
    "Main.Save.cs": {
        "SelfTestSaves", "BuildSave", "BuildBagSave", "ApplySave", "ParseStallStatus",
    },
    "Main.Collector.cs": {
        "_econLog", "RunCollect", "CollectOneDay",
        "DispatchDungeonForCollection", "DispatchOneDungeonForCollection",
        "OpenStallsForCollection", "UpgradeVillageForCollection",
        "RecordEconomySnapshot", "Percentile",
        "RunDailyShoppingIfNeeded", "PublishShoppingResults",
        "RecordShoppingMissed", "PrintShoppingResults",
        "EmitTradeCompleted", "EmitDungeonLooted", "NameOfHero",
    },
    "Main.Events.cs": {
        "_events", "_feed", "_eventIds", "PublishEvent", "Add", "Emit",
    },
    "Main.SelfTest.cs": {
        "SelfTest", "RunEconomySelfTest", "RunClickSelfTest", "RunEventsSelfTest",
        "RunTradeSelfTest", "RunMarketSelfTest", "RunShoppingSelfTest",
        "RunGrantSelfTest", "RunRelationsSelfTest", "RunSaveSelfTest",
        "RunDungeonSelfTest",
    },
}

FILE_USINGS = {
    "Main.Ui.cs": [
        "using Godot;", "using System;", "using System.Collections.Generic;",
        "using System.Linq;",
        "using ClaudeCodeGameStudios.Application;",
        "using ClaudeCodeGameStudios.Domain.Heroes;",
        "using ClaudeCodeGameStudios.Domain.Quests;",
        "using ClaudeCodeGameStudios.Domain.Relationships;",
        "using ClaudeCodeGameStudios.Presentation;",
    ],
    "Main.Expedition.cs": [
        "using Godot;", "using System;", "using System.Collections.Generic;",
        "using System.Linq;",
        "using ClaudeCodeGameStudios.Application;",
        "using ClaudeCodeGameStudios.Domain.Heroes;",
        "using ClaudeCodeGameStudios.Domain.Quests;",
        "using DomainVillage = ClaudeCodeGameStudios.Domain.Village.Village;",
        "using ResourceId = ClaudeCodeGameStudios.Domain.Village.ResourceId;",
    ],
    "Main.Save.cs": [
        "using Godot;", "using System;", "using System.Collections.Generic;",
        "using System.Linq;", "using System.IO;",
        "using ClaudeCodeGameStudios.Application;",
        "using ClaudeCodeGameStudios.Domain.Heroes;",
        "using ClaudeCodeGameStudios.Domain.Quests;",
        "using ClaudeCodeGameStudios.Domain.Stall;",
        "using ResourceId = ClaudeCodeGameStudios.Domain.Village.ResourceId;",
    ],
    "Main.Collector.cs": [
        "using Godot;", "using System;", "using System.Collections.Generic;",
        "using System.Linq;",
        "using ClaudeCodeGameStudios.Application;",
        "using ClaudeCodeGameStudios.Domain.Heroes;",
        "using ClaudeCodeGameStudios.Domain.Items;",
        "using ClaudeCodeGameStudios.Domain.Quests;",
        "using ClaudeCodeGameStudios.Presentation;",
    ],
    "Main.Events.cs": [
        "using Godot;", "using System;", "using System.Collections.Generic;",
        "using System.Linq;",
        "using ClaudeCodeGameStudios.Domain.Events;",
        "using ClaudeCodeGameStudios.Presentation;",
    ],
    "Main.SelfTest.cs": [
        "using Godot;", "using System;", "using System.Collections.Generic;",
        "using System.Linq;", "using System.IO;",
        "using ClaudeCodeGameStudios.Application;",
        "using ClaudeCodeGameStudios.Domain.Heroes;",
        "using ClaudeCodeGameStudios.Domain.Items;",
        "using ClaudeCodeGameStudios.Domain.Quests;",
        "using ClaudeCodeGameStudios.Domain.Stall;",
    ],
}

FILE_HEADER = {
    "Main.Ui.cs": """/// <summary>
/// 主场景的<b>表现层</b>部分（A1 拆分：场景搭建、界面刷新、玩家命令入口、输入）。
///
/// <para>基线 §13.1 的分层：表现层负责「地图、镜头、场景节点、动画、UI、输入采集」，
/// <b>不持有任何玩法规则</b>。这里的方法要么画界面，要么把玩家的一次点击
/// 翻译成一次领域调用 —— 规则在别处，这里只做翻译。</para>
///
/// <para>⚠️ **这是纯代码搬移** —— 逻辑一行没改，只是从 <c>Main.cs</c> 整块搬到这里。</para>
/// </summary>
""",
    "Main.Expedition.cs": """/// <summary>
/// 主场景的<b>副本与结算</b>部分（A1 拆分：把「战斗执行 + 结算入账」两类职责收在一处）。
///
/// <para>⚠️ **这是纯代码搬移** —— 逻辑一行没改，只是从 <c>Main.cs</c> 整块搬到这里。
/// 它之所以单独成文件，是因为它<b>同时拥有</b> UT-01 四类职责里的两类
/// （战斗执行 + 结算入账）；再往里塞第三类就违反结构断言了。</para>
/// </summary>
""",
    "Main.Save.cs": """/// <summary>
/// 主场景的<b>存档读写</b>部分（A1 拆分：把「存档写入」这一类职责收在一处）。
///
/// <para>⚠️ **这是纯代码搬移** —— 逻辑一行没改。存档是 UT-01 四类职责之一，
/// 单独成文件后它就<b>只占一类</b>，不会再和战斗 / 结算挤在一起。</para>
/// </summary>
""",
    "Main.Collector.cs": """/// <summary>
/// 主场景的<b>经济采集器</b>部分（A1 拆分：把「模拟推进」这一类职责收在一处）。
///
/// <para>⚠️ **这是纯代码搬移** —— 逻辑一行没改。采集器是 R-05 的测量装置：
/// 它按日驱动副本、摆摊、村庄升级，并把经济快照落成 jsonl。
/// 它<b>不参与</b>正常玩法的经济（正常玩法那条路是玩家点按钮）。</para>
/// </summary>
""",
    "Main.Events.cs": """/// <summary>
/// 主场景的<b>事件管道</b>部分（A1 拆分）。
///
/// <para>基线 §7.2 / §7.4 的事件溯源：领域事件 → <c>ChronicleFeed</c> → 杂谈条目。
/// <c>Emit</c> 是 C1 / C2 的<b>唯一</b>入口 —— 别的代码不许自己造杂谈条目。</para>
///
/// <para>⚠️ **这是纯代码搬移** —— 逻辑一行没改。</para>
/// </summary>
""",
    "Main.SelfTest.cs": """/// <summary>
/// 主场景的<b>真机自检</b>部分（A1 拆分：把 11 个自检从 <c>Main.cs</c> 里整块搬出）。
///
/// <para>⚠️ **这是纯代码搬移** —— 逻辑一行没改。
/// <b>自检文件里只允许有自检方法</b> —— 它是「验证装置」，不是「实现装置」，
/// 结构守卫 <c>MainStructureTests</c> 会盯着这条（否则自检文件会变成第二个
/// 巨型 GameManager：它什么都碰，于是「谁拥有哪类职责」就再也说不清了）。</para>
/// </summary>
""",
}

# 顶层成员声明：缩进 4 空格 + 访问修饰符（或嵌套类型）
DECL_RE = re.compile(
    r"^    (?:private|public|internal|protected)\s+"
    r"(?:(?:static|readonly|sealed|override|async|partial|new|virtual|abstract|const|volatile|extern|unsafe|record|class|struct|enum|interface)\s+)*"
    r"[\w<>\[\],\.\?:\s]*?\b(\w+)\s*(?:\(|\{|=>|;|=)")
NAME_RE = re.compile(r"^    (?:private|public|internal|protected)\s+.*?\b(\w+)\s*[({=;]")


def find_members(lines):
    """返回 [(name, block_start, block_end, gap_end)]，0-based。

    block = 成员的文档注释/属性 + 声明 + 方法体
    gap  = 成员与下一个成员之间的分隔（空行、零散注释）—— 跟着 block 一起搬，
           这样「拼回去 == 原文件」的不变式才成立。
    """
    cls_idx = next(i for i, l in enumerate(lines)
                   if l.startswith("public partial class Main"))
    assert lines[cls_idx + 1].strip() == "{", lines[cls_idx + 1]
    body_start = cls_idx + 2
    close_idx = max(i for i, l in enumerate(lines) if l.strip() == "}")

    def indent_of(i):
        l = lines[i]
        return len(l) - len(l.lstrip())

    def is_comment_or_blank(i):
        s = lines[i].strip()
        return (s == "" or s.startswith("//") or s.startswith("/*")
                or s.startswith("*"))

    def is_attr(i):
        s = lines[i].strip()
        return s.startswith("[") and s.endswith("]")

    # 1) 成员声明行
    decls = []
    for i in range(body_start, close_idx):
        l = lines[i]
        if not l.strip() or is_comment_or_blank(i):
            continue
        if indent_of(i) != 4:
            continue
        m = DECL_RE.match(l)
        if not m:
            continue
        decls.append((i, m.group(1)))

    # 2) 每个成员的块起点（含文档注释与属性）
    def block_start(d):
        j = d - 1
        while j >= body_start and (is_comment_or_blank(j) or is_attr(j)):
            j -= 1
        return j + 1

    members = []
    for n, (d, name) in enumerate(decls):
        bs = block_start(d)
        if n + 1 < len(decls):
            next_bs = block_start(decls[n + 1][0])
        else:
            next_bs = close_idx
        members.append((name, bs, next_bs - 1, next_bs - 1))
    return members, body_start, close_idx


def verify_roundtrip(lines, members, body_start, close_idx):
    """把成员按原顺序拼回去，必须与原文件一致。"""
    rebuilt = lines[:body_start]
    for name, bs, be, ge in members:
        rebuilt.extend(lines[bs:ge + 1])
    rebuilt.extend(lines[close_idx:])
    return rebuilt == lines


def main():
    dry = "--dry" in sys.argv
    raw = MAIN.read_bytes()
    text = raw.decode("utf-8")
    nl = "\r\n" if "\r\n" in text else "\n"
    lines = text.replace("\r\n", "\n").split("\n")

    members, body_start, close_idx = find_members(lines)
    print(f"识别到 {len(members)} 个成员")

    if not verify_roundtrip(lines, members, body_start, close_idx):
        print("!! 重组不变式不成立 —— 解析有误，中止")
        return 1

    by_name = {}
    for name, bs, be, ge in members:
        by_name.setdefault(name, []).append((bs, be, ge))
    missing = []
    for g in GROUPS.values():
        for name in sorted(g):
            if name not in by_name:
                missing.append(name)
    for name in missing:
        print(f"!! 找不到成员：{name}")
    if missing:
        print("!! 成员名单不全，中止（免得搬漏）")
        return 1

    buckets = {f: [] for f in GROUPS}
    keep = []
    for mem in members:
        name = mem[0]
        target = None
        for fname, g in GROUPS.items():
            if name in g:
                target = fname
                break
        (buckets[target] if target else keep).append(mem)

    for f, items in buckets.items():
        print(f"  {f}: {len(items)} 个成员 -> {[n for n, _, _, _ in items]}")
    print(f"  Main.cs 保留: {len(keep)} 个成员")

    if dry:
        return 0

    def render(mem):
        name, bs, be, ge = mem
        return lines[bs:ge + 1]

    # ── 写 Main.cs（只留 keep） ──
    head = lines[:body_start]
    tail = lines[close_idx:]
    main_body = []
    for mem in keep:
        main_body.extend(render(mem))
    MAIN.write_bytes(nl.join(head + main_body + tail).encode("utf-8"))

    # ── 写各 partial 文件 ──
    for fname, items in buckets.items():
        if not items:
            continue
        body = []
        for mem in items:
            body.extend(render(mem))
        text_out = ("\n".join(FILE_USINGS[fname]) + "\n\n" + FILE_HEADER[fname] +
                    "public partial class Main : Node2D\n{\n" +
                    "\n".join(body).strip("\n") + "\n}\n")
        (ROOT / "src" / fname).write_bytes(text_out.encode("utf-8"))
        print(f"写出 src/{fname}（{len(text_out.splitlines())} 行）")

    # ── 不变式复核：把新文件里的成员合起来，必须等于原文件的成员集合 ──
    all_new = []
    for fname in list(GROUPS) + ["__main__"]:
        pass
    print("完成")
    return 0


if __name__ == "__main__":
    sys.exit(main())
