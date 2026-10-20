"""
对台账一致性守卫（`LedgerConsistencyTests`）做**变异看红**验证。

这条守卫守的是 `production/07` 内部的三处**自相矛盾** ——
写出来的时候三处都真实存在过：

  ① §4.3 台账标「C-02 / C-03 全部已清」，§6 遗留事项却写「未处理」
  ② §4「现状速览」表说「架构债 A1–A7 仍开 = 无」，紧跟的散文却说「还开着四项：A1 · B4 · …」
  ③ §6 的 M1 完成度写「`Main.cs` 632 行仍是那个巨型管理器」，而 A1 早已拆成 7 个 partial

四档变异分别打这三条 + 一条「文档指向空气」：

  ① §6 的 C-02 改回「未处理」        → 断言 1 红
  ② 速览散文的编号集与计数改错        → 断言 2 红
  ③ M1 行的行数写回 632              → 断言 3 的「行数」红
  ④ M1 行引用一个不存在的 .cs 文件    → 断言 3 的「文件存在」红

用法：python production/qa/mutate-ledger-consistency.py
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
DOC = ROOT / "production" / "07_技术决策与待确认事项.md"
TESTS = ROOT / "tests" / "Claude-Code-Game-Studios.Tests.csproj"
FILTER = "FullyQualifiedName~LedgerConsistencyTests"


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


def mutate(label, old, new):
    """改一处 → 跑测试 → 要求红 → 复原文件。"""
    backup = DOC.read_bytes()
    before = sha(DOC)
    raw = DOC.read_text(encoding="utf-8")
    crlf = "\r\n" in raw
    text = raw.replace("\r\n", "\n")
    if old not in text:
        print(f"!! [{label}] 模式没找到")
        return False
    mutated = text.replace(old, new, 1)
    if crlf:
        mutated = mutated.replace("\n", "\r\n")
    DOC.write_text(mutated, encoding="utf-8", newline="")
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
        DOC.write_bytes(backup)
        if sha(DOC) != before:
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

    # ── ① §6 的 C-02 改回「未处理」 ───────────────────────────────────
    results.append(mutate(
        "§6 遗留事项里 C-02 改回「未处理」（与 §4.3 的「已清」矛盾）",
        "| C-02 `Chronicle.Refused()` 死代码 | ✅ **已关闭**",
        "| C-02 `Chronicle.Refused()` 死代码 | 未处理",
    ))

    # ── ② 速览散文的编号集与计数改错 ───────────────────────────────────
    results.append(mutate(
        "§4 速览散文的编号集与逐条表对不上",
        "> **⇒ 现在真正还开的只有三项**：\n> **B1 / B2 / B3**",
        "> **⇒ 现在真正还开的只有四项**：\n> **A1** · **B4** · **B1 / B2 / B3**",
    ))

    # ── ③ M1 行的行数写回 632 ─────────────────────────────────────────
    # ⚠️ 这里的「现值」必须与 `07` §6 里写的一致（由 LedgerConsistencyTests 核对），
    #    否则这一档会报「模式没找到」而不是「守卫红了」。
    results.append(mutate(
        "§6 的 M1 行把行数写回 632（早已拆成 7 个 partial）",
        "`Main.cs` 908 行，但",
        "`Main.cs` 632 行，但",
    ))

    # ── ④ M1 行引用一个不存在的 .cs 文件 ───────────────────────────────
    #    ⚠️ 必须是**以 .cs 结尾**的名字：守卫只查「文件存在」这一点，
    #    而 `MainStructureTests`（无扩展名）根本不在它的扫描范围内。
    results.append(mutate(
        "§6 的 M1 行引用一个不存在的 .cs 文件（文档指向空气）",
        "`Main.cs` 908 行，但",
        "`MainDecomposed.cs` 902 行，但",
    ))

    print("---")
    bad = results.count(False)
    print(f"变异矩阵：{len(results) - bad}/{len(results)} 符合预期")
    return 1 if bad else 0


if __name__ == "__main__":
    sys.exit(main())
