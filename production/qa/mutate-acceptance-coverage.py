"""
对「验收覆盖摘要不许过期」守卫（`AcceptanceCoverageCountsTests`）做**变异看红**验证。

这条守卫守的是 `production/06` 的不变量表 / A 断言表（**正文**）与
`production/qa/README.md` 的「验收计划的覆盖现状」表（**摘要**）之间的两个数字。

它写出来的时候，摘要就已经过期了：I9 闭合之后 06 的正文写「✅ 已证明」，
README 还写着「**14** | 2（I9 受阻…）」。

**五档正向变异（都应当红）**：

  ① README 的「已写」数字 15 → 14            → 摘要比正文少 ⇒ 红
  ② README 的「未写」数字 1 → 2              → 摘要比正文多 ⇒ 红
  ③ README 的范围标签 I1–I16 → I1–I17      → 表变长了、摘要没改 ⇒ 红
  ④ 06 的 I15 从「受阻」改成「✅ 已写」       → 正文变了、摘要没改 ⇒ 红
  ⑤ 06 的 I16 首标记换成未知标记「🚧」        → 守卫必须**报错**（不许猜类别）⇒ 红

**两档反向验证（都应当绿 —— 它们证明判据的方向是对的，而不是碰巧绿）**：

  ⑥ 基线本身就是这一档：I16 的状态格里**含「受阻」二字**
     （「原「受阻：字段未回填 `05` §3.9」是过期状态」），
     但它必须**算已写** —— 「历史陈述不算现状」。
     ⚠️ 判据若写成「状态格含『受阻』就算受阻」，这一档立刻红 ——
     这正是本守卫写出来当天踩到的坑（它一度把 I16 数成受阻，
     然后指着 README 的**正确**数字报错）。
  ⑦ 把 I16 状态格里那句历史说明**删掉** ⇒ 仍应绿
     （计数只取决于首标记，与历史叙述的有无无关）。

用法：python production/qa/mutate-acceptance-coverage.py
"""
import hashlib
import re
import subprocess
import sys
from pathlib import Path

try:
    import _utf8out  # noqa: F401  — 控制台默认 GBK，印 ✓/✗ 会直接崩
except ImportError:
    pass

ROOT = Path(__file__).resolve().parents[2]
TESTS = ROOT / "tests" / "Claude-Code-Game-Studios.Tests.csproj"
FILTER = "FullyQualifiedName~AcceptanceCoverageCounts"
PLAN = ROOT / "production" / "06_第一阶段验收与测试计划_V1.0.md"
README = ROOT / "production" / "qa" / "README.md"


def sha(p: Path) -> str:
    return hashlib.sha256(p.read_bytes()).hexdigest()


def run_tests():
    """跑守卫测试。

    返回 (failed_count, output)：
      failed_count >= 0 ⇒ 拿到了测试结果，failed_count 是失败的断言数
      failed_count == -1 ⇒ **没拿到测试结果**（编译失败 / 没发现测试 / 其它）

    ⚠️ 「编译失败」必须与「断言红」分开：变异把代码改到编译不过时，
    `dotnet test` 同样非零退出，但那是**编译错误**，不是守卫抓到了问题
    —— 把它当成「红」就会**养出一条永远红的假守卫**。

    判据用 `-v q`：那种详细度下 `dotnet test` 打印一行摘要
    「已通过! - 失败: 0，通过: 2，…」。`-v normal` **反而没有**这行摘要
    （只有 gdUnit4 的逐条输出）—— 用 `-v normal` 去 grep 摘要会**永远匹配不上**。
    """
    r = subprocess.run(
        ["dotnet", "test", str(TESTS), "-c", "Release", "--nologo", "-v", "q",
         "--filter", FILTER],
        cwd=ROOT, capture_output=True, text=True, encoding="utf-8", errors="replace")
    out = r.stdout + r.stderr
    for pat in (r"失败:\s*(\d+)", r"Failed:\s*(\d+)"):
        m = re.search(pat, out)
        if m:
            return int(m.group(1)), out
    return -1, out


def report(label, failed, expect_red=True):
    color = "RED  " if failed > 0 else ("GREEN" if failed == 0 else "???  ")
    ok = (failed > 0) == expect_red
    print(f"{'OK  ' if ok else '!!  '}变异 [{label}] -> {color} (failed={failed}, "
          f"期望 {'RED' if expect_red else 'GREEN'})")
    return ok


def mutate(path: Path, label, old, new, expect_red=True):
    """改一处 → 跑测试 → 判定 → 复原文件（连生成物一起复原）。"""
    backup = path.read_bytes()
    before = sha(path)
    raw = backup.decode("utf-8")
    crlf = "\r\n" in raw
    text = raw.replace("\r\n", "\n")
    if old not in text:
        print(f"!! [{label}] 模式没找到")
        return False
    mutated = text.replace(old, new, 1)
    if crlf:
        mutated = mutated.replace("\n", "\r\n")
    path.write_text(mutated, encoding="utf-8", newline="")
    try:
        failed, out = run_tests()
        ok = report(label, failed, expect_red)
        if not ok and expect_red:
            for line in out.splitlines():
                if "失败" in line or "Expecting" in line:
                    print("      " + line.strip()[:160])
        return ok
    finally:
        path.write_bytes(backup)
        if sha(path) != before:
            print(f"!! 复原失败 [{label}]")


def main():
    results = []

    failed, out = run_tests()
    if failed != 0:
        print(f"!! 基线就是红的（failed={failed}）—— 先修好再谈变异")
        for line in out.splitlines():
            if "失败" in line or "Expecting" in line:
                print("      " + line.strip()[:160])
        return 1
    print("OK  基线（未变异）全绿 —— 判据确实能绿")

    # ── 反向验证 ⑥：基线本身含「受阻」二字却必须算已写 ────────────────
    plan_text = PLAN.read_text(encoding="utf-8")
    i16 = [l for l in plan_text.splitlines() if l.startswith("| **I16**")]
    if len(i16) != 1 or "受阻" not in i16[0]:
        print("!! 反向验证 ⑥ 失效：I16 行不见了，或不再含「受阻」二字 —— "
              "这一档已经失去意义，请改这一档的写法")
        return 1
    print("OK  反向验证 ⑥：基线的 I16 状态格里**确实含「受阻」二字**"
          "（「原「受阻：…」是过期状态」），而它必须算已写 ⇒ 基线绿即证明"
          "「历史陈述不算现状」这条规则真的生效")

    # ── ① README 的「已写」数字改错 ────────────────────────────────────
    results.append(mutate(
        README,
        "README 覆盖现状表的「已写」数字 15 → 14（摘要比正文少）",
        "| 不变量 I1–I16 | **15** | 1 |",
        "| 不变量 I1–I16 | **14** | 1 |",
    ))

    # ── ② README 的「未写」数字改错 ────────────────────────────────────
    results.append(mutate(
        README,
        "README 覆盖现状表的「未写」数字 1 → 2（摘要比正文多）",
        "| 不变量 I1–I16 | **15** | 1 |",
        "| 不变量 I1–I16 | **15** | 2 |",
    ))

    # ── ③ README 的范围标签过期 ────────────────────────────────────────
    results.append(mutate(
        README,
        "README 覆盖现状表的范围标签 I1–I16 → I1–I17（表变长了摘要没改）",
        "| 不变量 I1–I16 | **15** | 1 |",
        "| 不变量 I1–I17 | **15** | 1 |",
    ))

    # ── ④ 06 的 I15 从「受阻」改成「已写」 ─────────────────────────────
    results.append(mutate(
        PLAN,
        "06 的 I15 状态从「受阻」改成「✅ 已写」（正文变了、摘要没改）",
        "| L2 | **受阻**：依赖 P-8，注入策略属【待定】TP-001 |",
        "| L2 | ✅ **已写**（变异探针：假装它写好了） |",
    ))

    # ── ⑤ 06 的 I16 首标记换成未知标记 ────────────────────────────────
    results.append(mutate(
        PLAN,
        "06 的 I16 首标记换成未知标记「🚧」（守卫必须报错，不许猜类别）",
        "✅ **已写**（2026-10-21 核实：字段**已回填**",
        "🚧 **在写**（2026-10-21 核实：字段**已回填**",
    ))

    # ── 反向验证 ⑦：删掉 I16 那句历史说明，仍应绿 ─────────────────────
    results.append(mutate(
        PLAN,
        "把 I16 状态格里那句历史说明删掉（计数只取决于首标记 ⇒ 仍应绿）",
        "。**原「受阻：字段未回填 `05` §3.9」是过期状态**） |",
        "） |",
        expect_red=False,
    ))

    print("---")
    bad = results.count(False)
    print(f"变异矩阵：{len(results) - bad}/{len(results)} 符合预期")
    return 1 if bad else 0


if __name__ == "__main__":
    sys.exit(main())
