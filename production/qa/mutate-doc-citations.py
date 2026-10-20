"""
对文档引用守卫（`DocCitationTests`）做**变异看红**验证。

这条守卫守的是「**活文档**里点名的测试必须真的存在」——
验收计划 `06` 到处点名具体测试（类 / 方法 / 文件），
而**点名会过期**：有人改名或删除，文档里的引用就变成指向空气的指针，
**文档不报错、测试也不报错** —— 只有照着文档去找的人会发现扑空。

四档变异：

  ① `06` 引用一个不存在的测试文件 ⇒ 红
  ② `06` 引用一个不存在的测试类   ⇒ 红
  ③ `06` 引用一个**类还在但方法没了**的 `类.方法` ⇒ 红（只查前两种会漏掉这一档）
  ④ 把守卫自己的「活文档清单」改成一个不存在的文件 ⇒ 红（范围本身也要能被审）

用法：python production/qa/mutate-doc-citations.py
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
DOC = ROOT / "production" / "06_第一阶段验收与测试计划_V1.0.md"
TEST_SRC = ROOT / "tests" / "unit" / "architecture" / "DocCitationTests.cs"
TESTS = ROOT / "tests" / "Claude-Code-Game-Studios.Tests.csproj"
FILTER = "FullyQualifiedName~DocCitationTests"

# 插进 `06` 的「探针」—— 放在「关键事实」那一段后面，免得影响阅读顺序。
PROBE = "探针引用：`tests/unit/zzz_不存在的探针文件.cs` 与 `ZzzNotARealTests` 与 `QuestsTests.Zzz_不存在的方法`。"


def sha(p: Path) -> str:
    return hashlib.sha256(p.read_bytes()).hexdigest()


def run_tests():
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


def mutate_doc(label, old, new):
    """改文档一处 → 跑测试 → 要求红 → 复原。"""
    return _mutate(label, DOC, old, new)


def mutate_src(label, old, new):
    """改守卫源码一处 → 跑测试 → 要求红 → 复原。"""
    return _mutate(label, TEST_SRC, old, new)


def _mutate(label, target: Path, old, new):
    backup = target.read_bytes()
    before = sha(target)
    raw = target.read_text(encoding="utf-8")
    crlf = "\r\n" in raw
    text = raw.replace("\r\n", "\n")
    if old not in text:
        print(f"!! [{label}] 模式没找到")
        return False
    mutated = text.replace(old, new, 1)
    if crlf:
        mutated = mutated.replace("\n", "\r\n")
    target.write_text(mutated, encoding="utf-8", newline="")
    try:
        failed, out = run_tests()
        ok = failed > 0
        print(f"{'OK  ' if ok else '!!  '}变异 [{label}] -> "
              f"{'RED' if ok else 'GREEN'} (failed={failed}, 期望 RED)")
        if not ok:
            for line in out.splitlines():
                if "失败" in line or "Expecting" in line:
                    print("      " + line.strip()[:160])
        return ok
    finally:
        target.write_bytes(backup)
        if sha(target) != before:
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

    anchor = "## 0. 本文的三条硬约束"

    # ── ① 引用不存在的测试文件 ──────────────────────────────────────
    results.append(mutate_doc(
        "06 引用一个不存在的测试文件",
        anchor,
        anchor + "\n\n" + PROBE,
    ))

    # ── ② 引用不存在的测试类（文件存在、类不存在）────────────────────
    results.append(mutate_doc(
        "06 引用一个不存在的测试类",
        anchor,
        anchor + "\n\n探针引用：`ZzzNotARealTests`。",
    ))

    # ── ③ 引用「类还在但方法没了」───────────────────────────────────
    results.append(mutate_doc(
        "06 引用一个类还在、方法却没了的「类.方法」",
        anchor,
        anchor + "\n\n探针引用：`QuestsTests.Zzz_不存在的方法`。",
    ))

    # ── ④ 守卫自己的「活文档清单」写错 ────────────────────────────────
    results.append(mutate_src(
        "守卫的活文档清单里有一个不存在的文件",
        '"06_第一阶段验收与测试计划_V1.0.md",',
        '"06_第一阶段验收与测试计划_V1.0.md",\n        "99_不存在_V1.0.md",',
    ))

    print("---")
    bad = results.count(False)
    print(f"变异矩阵：{len(results) - bad}/{len(results)} 符合预期")
    return 1 if bad else 0


if __name__ == "__main__":
    sys.exit(main())
