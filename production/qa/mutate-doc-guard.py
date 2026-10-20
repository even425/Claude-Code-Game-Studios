"""
对 DocumentationHygieneTests 的两条「已判死设计线」守卫做**变异看红**验证。

  ① `VoidedDesignLine_Audits_CarryTheStatusBanner`
     —— 把某份旧审查报告的状态横幅删掉 ⇒ 守卫必须红
  ② `AnyProductionDoc_ReferencingTheVoidedGddLine_AlsoSaysItIsVoid`
     —— 新写一份提到 `design/gdd/` 却**没说它已死**的文档 ⇒ 守卫必须红

用法：python production/qa/mutate-doc-guard.py
"""
try:
    import _utf8out  # noqa: F401  — 控制台默认 GBK，印 ✓/✗ 会直接崩
except ImportError:
    pass

import hashlib
import re
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
TESTS = ROOT / "tests" / "Claude-Code-Game-Studios.Tests.csproj"
FILTER = "FullyQualifiedName~DocumentationHygieneTests"
MARKER = "【2026-10-21 补状态标记 · 审查对象已判死】"
PROBE = ROOT / "production" / "zz-mutation-probe.md"


def sha(p):
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


def report(label, failed, expect_red=True):
    color = "RED  " if failed > 0 else ("GREEN" if failed == 0 else "???  ")
    ok = (failed > 0) == expect_red
    print(f"{'OK  ' if ok else '!!  '}变异 [{label}] -> {color} (failed={failed}, 期望 RED)")
    return ok


def main():
    results = []

    # ── ① 删掉一份旧审查报告的状态横幅 ───────────────────────────────
    target = ROOT / "production" / "terminology-sweep.md"
    backup = target.read_bytes()
    before = sha(target)
    try:
        text = backup.decode("utf-8")
        # 删掉含标记的那一行 + 它后面的横幅块
        lines = text.split("\n")
        keep = []
        skipping = False
        for l in lines:
            if MARKER in l:
                skipping = True
                continue
            if skipping and (l.startswith(">") or l.strip() == ""):
                continue
            skipping = False
            keep.append(l)
        target.write_text("\n".join(keep), encoding="utf-8", newline="")
        failed, _ = run_tests()
        results.append(report("删掉一份旧审查报告的状态横幅", failed))
    finally:
        target.write_bytes(backup)
        if sha(target) != before:
            print("!! 复原失败")

    # ── ② 新写一份提到已判死 GDD 却没说它死了的文档 ──────────────────
    existed = PROBE.exists()
    old = PROBE.read_bytes() if existed else None
    try:
        PROBE.write_text(
            "# 变异探针\n\n"
            "这里提到 design/gdd/ 里的某份 GDD，但**没有**说它已经作废。\n",
            encoding="utf-8", newline="")
        failed, _ = run_tests()
        results.append(report("新写一份提到已判死 GDD 却不声明作废的文档", failed))
    finally:
        if existed:
            PROBE.write_bytes(old)
        elif PROBE.exists():
            PROBE.unlink()

    print("---")
    bad = results.count(False)
    print(f"变异矩阵：{len(results) - bad}/{len(results)} 符合预期")
    return 1 if bad else 0


if __name__ == "__main__":
    sys.exit(main())
