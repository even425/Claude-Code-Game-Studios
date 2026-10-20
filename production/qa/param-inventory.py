"""
当前实现参数清单 —— 扫描 `src/` 的全部数值常量，分类并生成 / 校验台账。

**为什么要有这个**：计划 §二 的原则是「为了让原型运行，可以采用明确标注的临时参数，
但临时参数必须**集中管理**，不能悄悄变成正式设计规则」。
而「集中管理」这件事此前**没有可执行的含义** —— 规矩写在 `07` §2.1，
代码里的数字却散落各处。**没有守卫的规矩等于没有规矩。**

**本脚本做三件事**：
1. 扫出 `src/` 里**全部**带数值的标量常量；
2. 每个常量必须在下面的分类表里**有归属**（玩法参数 / 非参数）；
3. 玩法参数的**值**必须与文档一致 —— 文档会过期，脚本不会。

用法：
  python production/qa/param-inventory.py            # 生成 Markdown 表
  python production/qa/param-inventory.py --check    # 校验（不一致即退出码 1）
"""
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
SRC = ROOT / "src"

# 标量常量：`private const int X = 4;` / `private static readonly double Y = 0.25;`
SCALAR = re.compile(
    r"^\s*(?:public|private|internal|protected)(?:\s+static)?(?:\s+readonly)?"
    r"(?:\s+const)?\s+[\w<>\[\],\.\?]+\s+(\w+)\s*=\s*(-?[0-9]+(?:\.[0-9]+)?)"
    r"(?:[fFdDmMlLuU])?\s*;")

# ── 分类表（单一真源在 parameters.json）─────────────────────────────
# JSON 里每个条目：file / name / kind / status / note（+ value，参数才有）
# ⚠️ **每个常量都必须在这里有归属** —— 新加一个数字而忘了登记，
# `--check` 与 C# 守卫都会红。这正是「集中管理」的可执行形式。
LEDGER_PATH = ROOT / "production" / "qa" / "parameters.json"


def load_ledger():
    """读台账 JSON，返回 (CLASSIFICATION, EXPECTED_VALUES)。"""
    import json
    data = json.loads(LEDGER_PATH.read_text(encoding="utf-8"))
    classification = {}
    expected = {}
    for row in data["parameters"]:
        key = (row["file"], row["name"])
        classification[key] = (row["kind"], row["status"], row["note"])
        expected[key] = row.get("value", "")
    for row in data["nonParameters"]:
        classification[(row["file"], row["name"])] = (row["kind"], row["status"], row["note"])
    return classification, expected


CLASSIFICATION, EXPECTED_VALUES = load_ledger()


def load_tables():
    """读台账里的「表」类参数（阶段 5 评审要看的四张表）。"""
    import json
    data = json.loads(LEDGER_PATH.read_text(encoding="utf-8"))
    return data.get("tables", [])


def scan():
    """扫出 (相对路径, 常量名, 值文本) 列表。"""
    out = []
    for path in sorted(SRC.rglob("*.cs")):
        rel = path.relative_to(SRC).as_posix()
        for line in path.read_text(encoding="utf-8").split("\n"):
            m = SCALAR.match(line)
            if m:
                out.append((rel, m.group(1), m.group(2)))
    return out


def norm(v):
    """把 '0.10' / '0.1' / '10' 统一成可比较的字符串。"""
    try:
        f = float(v)
    except ValueError:
        return v
    if f == int(f):
        return str(int(f))
    return repr(f)


def check():
    found = scan()
    found_keys = {(r, n) for r, n, _ in found}

    problems = []

    # ① 代码里每个常量都必须有归属
    for key in sorted(found_keys):
        if key not in CLASSIFICATION:
            problems.append(f"未登记的数值常量：{key[0]} :: {key[1]}（新增的数字必须登记）")

    # ② 分类表里的每一项都必须在代码里存在（防止条目腐化）
    for key in sorted(set(CLASSIFICATION) - found_keys):
        problems.append(f"分类表里的 {key[0]} :: {key[1]} 在代码里已经不存在了")

    # ③ 玩法参数的值必须与文档一致
    for rel, name, value in found:
        key = (rel, name)
        if key not in CLASSIFICATION:
            continue
        kind = CLASSIFICATION[key][0]
        if kind != "参数":
            continue
        expected = EXPECTED_VALUES.get(key)
        if expected is None:
            problems.append(f"参数 {rel} :: {name} 没有登记期望值")
        elif norm(expected) != norm(value):
            problems.append(
                f"参数 {rel} :: {name} 的值与文档不一致：代码 {value}，文档 {expected}")

    return found, problems


# 参数的期望值（与文档一致）—— 单独放，便于与生成表同源
EXPECTED_VALUES = {
    ("Application/DailyLife.cs", "PlaceholderActionFractionOfDay"): "0.25",
    ("Application/DailyLife.cs", "PlaceholderModifierFloor"): "0.10",
    ("Application/DailyLife.cs", "PlaceholderModifierCeiling"): "10.0",
    ("Application/DailyLife.cs", "PlaceholderBuildProgressPerAction"): "0.25",
    ("Application/DungeonFlow.cs", "DeclaredStageCount"): "3",
    ("Application/HeroBehaviorService.cs", "BagItemLimit"): "5",
    ("Application/PlaceholderCombat.cs", "PlaceholderHeroWinRate"): "0.65",
    ("Application/SimulationRunner.cs", "RequiredCatchUpLimit"): "8",
    ("Application/SimulationRunner.cs", "PlaceholderSimSecondsPerDay"): "100.0",
    ("Application/VillageUpgradeService.cs", "MaxTier"): "5",
    ("Domain/Clock/SimClock.cs", "_timeScale"): "1.0",
    ("Domain/Relationships/Relationship.cs", "MinValue"): "-100",
    ("Domain/Relationships/Relationship.cs", "MaxValue"): "100",
    ("Domain/Relationships/Relationship.cs", "InitialValue"): "0",
    ("Domain/Relationships/Relationship.cs", "SocialDelta"): "2",
    ("Domain/Relationships/Relationship.cs", "CooperationDelta"): "3",
    ("Domain/Relationships/Relationship.cs", "HostileThreshold"): "-20",
    ("Domain/Relationships/Relationship.cs", "CloseThreshold"): "20",
    ("Domain/Relationships/Relationship.cs", "TargetWeightScale"): "50.0",
    ("Domain/Relationships/Relationship.cs", "MinTargetWeight"): "0.1",
    ("Domain/Relationships/Relationship.cs", "MaxTargetWeight"): "3.0",
    ("Domain/Stall/StallFee.cs", "RatePer100"): "5",
    ("Domain/Stall/StallFee.cs", "UnitsPerGold"): "100",
    ("Main.cs", "SquadMax"): "4",
    ("Main.cs", "DefaultGoldRewardUnits"): "10",
    ("Main.cs", "PlaceholderBaseSuccessRate"): "0.52",
    ("Main.cs", "PlaceholderAttrScale"): "300",
    ("Main.cs", "PlaceholderSuccessCap"): "0.95",
    ("Main.cs", "PlaceholderPrideRefusalRate"): "0.40",
    ("Main.cs", "PlaceholderPocketRate"): "0.45",
    ("Main.cs", "PlaceholderOverrunRate"): "0.35",
    ("VillageView.cs", "MaxTier"): "5",
}


DOC_PATH = ROOT / "production" / "36_当前实现参数清单_V1.0.md"

DOC_HEADER = """# 36 · 当前实现参数清单

> **它是什么**：本项目代码里**实际在跑**的每一个数值参数 —— 一行一个，附状态与依据。
> **生成方式**：`python production/qa/param-inventory.py --write`（表是生成的，不是手抄的）。
> **校验方式**：`python production/qa/param-inventory.py --check`；并且
> `tests/unit/architecture/ParameterLedgerTests.cs` 在**每次 `dotnet test`** 时自动跑它。
>
> **为什么要它**：计划 §二 说「临时参数必须**集中管理**，不能悄悄变成正式设计规则」。
> 那条规矩此前**没有可执行的含义** —— 规矩写在 `07` §2.1，数字却散落在代码里，
> 也没有任何东西会在数字被改掉时喊一声。
> 本清单 + 守卫把「集中管理」变成**可执行**的：
> ① 代码里**新增**一个数值常量而没登记 ⇒ 守卫红；
> ② 登记的值与代码不一致 ⇒ 守卫红；
> ③ 登记的条目在代码里消失了 ⇒ 守卫红。
>
> **三态的含义**：
> - **【已确认】** —— 开发者拍过板（有 D-0xx 依据）。**不是**「数值平衡已验证」；
> - **【占位】** —— 为了原型跑得下去而设的临时值，**服务某个 P/T 编号**，拍板后由该编号统一替换；
> - **【待定】** —— 尚无任何依据，等开发者决定。

## 1 · 玩法参数（32 个）

"""

DOC_FOOTER = """
## 2 · 非参数（表现层 / 技术值 / 哨兵）

这些数字**不是玩法参数**，不进台账：布局尺寸、显示上限、初始值、哨兵值。
把它们列出来不是为了管它们，而是为了让「代码里全部的数值常量都被认领过」这句话成立 ——
**否则「新增一个数字忘了登记」这件事会被淹没在一堆无关数字里**。

## 3 · 三个台账缺口（本次核实发现）

1. **副本阶段数（`DeclaredStageCount = 3`）没有登记**。`07` §2 的 P-07…P-09 覆盖了
   「战斗规则的全部数值 / 结算奖励内容 / 副本失败条件」，但**阶段数**不在其中。
   ⇒ 建议补 **P-12 = 副本阶段数**，卡 **T-004**。
2. **村庄最高层级（`MaxTier = 5`）没有登记**，且**在两处各写了一遍**
   （`VillageUpgradeService.MaxTier` 与 `VillageView.MaxTier`）——
   两处分叉的经典隐患（`VillageUpgradeServiceTests` 的跨层一致性用例盯着，但那是**测试**，
   不是**台账**）。⇒ 建议补 **P-13 = 村庄最高层级**，卡 **D-003**；
   并把 `VillageView.MaxTier` 改成读服务的那个（单一来源）。
3. **`07` §2 的 P-11 那一行写「台账当前为空表」** —— **过期状态**：
   台账有 P-01…P-11 共 11 行，不是空的。

## 4 · 覆盖范围与已知边界

| 东西 | 在哪 | 覆盖情况 |
|---|---|---|
| 标量数值常量（64 处） | `src/` 全部 | ✅ **全覆盖**（32 个玩法参数 + 32 个表现层/技术/哨兵值） |
| **表类参数**（4 张） | 见 §1.1 | ✅ **全覆盖**：升级成本、掉落权重与数量、性格权重、稀有度映射 |
| 价格表（石料/木料/布匹） | `config/prices.json` ↔ `PriceBook.Default` | ✅ 由 `PriceBookConfigConsistencyTests` **逐项比对**（两份副本不许分叉） |
| 世界表 | `config/worlds.json` ↔ `HeroFactory.World.Builtin` | ✅ 由 `WorldTableConfigConsistencyTests` **逐字段比对** |

> ⚠️ **已知边界**：本文的**标量扫描**只认 `const` / `static readonly` 的**单值**声明。
> **表类参数**靠 4 个专门的抽取器（`extractor` 字段）覆盖，覆盖到本文列出的那 4 张；
> **别的表**（例如某个服务里的一组系数）若要纳入，需再加一个抽取器。
> 这是本文的已知边界，不是「那些数不重要」——
> **新增一张表，就该给它加一个抽取器**，否则它就在覆盖范围之外了。
"""


def render(found):
    """生成 Markdown 表。"""
    rows = []
    for rel, name, value in found:
        key = (rel, name)
        if key not in CLASSIFICATION:
            continue
        kind, status, note = CLASSIFICATION[key]
        shown = EXPECTED_VALUES.get(key, value)
        rows.append((kind, rel, name, shown, status, note))

    rows.sort(key=lambda r: (r[0] != "参数", r[1], r[2]))
    lines = ["| 类别 | 位置 | 常量 | 当前值 | 状态 | 说明 |",
             "|---|---|---|---|---|---|"]
    for kind, rel, name, value, status, note in rows:
        lines.append(f"| {kind} | `{rel}` | `{name}` | `{value}` | {status} | {note} |")
    return "\n".join(lines)


def build_doc(found):
    """整篇文档 = 头部 + 参数表 + 尾部（非参数表 + 缺口 + 边界）。"""
    rows = []
    for rel, name, value in found:
        key = (rel, name)
        if key not in CLASSIFICATION:
            continue
        kind, status, note = CLASSIFICATION[key]
        shown = EXPECTED_VALUES.get(key, value)
        rows.append((kind, rel, name, shown, status, note))

    rows.sort(key=lambda r: (r[0] != "参数", r[1], r[2]))
    params = [r for r in rows if r[0] == "参数"]
    others = [r for r in rows if r[0] != "参数"]

    def table(rs):
        out = ["| 类别 | 位置 | 常量 | 当前值 | 状态 | 说明 |",
               "|---|---|---|---|---|---|"]
        for kind, rel, name, value, status, note in rs:
            out.append(f"| {kind} | `{rel}` | `{name}` | `{value}` | {status} | {note} |")
        return "\n".join(out)

    footer = DOC_FOOTER.replace(
        "## 2 · 非参数（表现层 / 技术值 / 哨兵）\n\n这些数字**不是玩法参数**，不进台账：布局尺寸、显示上限、初始值、哨兵值。\n把它们列出来不是为了管它们，而是为了让「代码里全部的数值常量都被认领过」这句话成立 ——\n**否则「新增一个数字忘了登记」这件事会被淹没在一堆无关数字里**。",
        "## 2 · 非参数（表现层 / 技术值 / 哨兵）\n\n"
        "这些数字**不是玩法参数**，不进台账：布局尺寸、显示上限、初始值、哨兵值。\n"
        "把它们列出来不是为了管它们，而是为了让「代码里全部的数值常量都被认领过」这句话成立 ——\n"
        "**否则「新增一个数字忘了登记」这件事会被淹没在一堆无关数字里**。\n\n"
        + table(others))

    # 表类参数（阶段 5 评审要看的那几张）
    tables = load_tables()
    tlines = []
    if tables:
        tlines = ["## 1.1 · 表类参数（阶段 5 要评审的那几张）", "",
                  "| 表 | 位置 | 状态 | 内容 | 说明 |",
                  "|---|---|---|---|---|"]
        for row in tables:
            values = " / ".join(f"`{v}`" for v in row["expected"])
            tlines.append(
                f"| `{row['id']}` | `{row['file']}` | {row['status']} | {values} | {row['note']} |")
        tlines.append("")

    head = DOC_HEADER.replace("（32 个）", f"（{len(params)} 个）")
    return head + table(params) + "\n" + "\n".join(tlines) + footer


def main():
    args = sys.argv[1:]
    check_only = "--check" in args
    write = "--write" in args

    found, problems = check()

    if check_only:
        # 文档必须与生成结果一致（防止手抄走样）
        if DOC_PATH.exists():
            doc = DOC_PATH.read_text(encoding="utf-8")
            expected = build_doc(found)
            if expected.strip() not in doc:
                problems.append(
                    f"{DOC_PATH.name} 的内容与生成结果不一致 —— 重新跑 "
                    f"`python production/qa/param-inventory.py --write`")
        else:
            problems.append(f"{DOC_PATH.name} 不存在")

        if problems:
            print("!! 参数台账与代码不一致：")
            for p in problems:
                print("   ", p)
            return 1
        params = sum(1 for r, n, _ in found
                     if CLASSIFICATION.get((r, n), ("",))[0] == "参数")
        print(f"✅ 参数台账与代码一致：{len(found)} 个数值常量，"
              f"其中玩法参数 {params} 个，其余为表现层/技术值；文档也与生成结果一致")
        return 0

    if write:
        DOC_PATH.write_text(build_doc(found), encoding="utf-8", newline="\n")
        print(f"已写出 {DOC_PATH.relative_to(ROOT)}")
    else:
        print(render(found))

    print()
    if problems:
        print("!! 同时存在下列不一致：")
        for p in problems:
            print("   ", p)
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
