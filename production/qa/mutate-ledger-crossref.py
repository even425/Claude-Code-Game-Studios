"""
对台账交叉引用守卫（`ParameterLedgerCrossReferenceTests`）做**变异看红**验证。

这条守卫守的是**两张台账之间的链接**：代码层的参数台账
（`production/qa/parameters.json`）与规则层的临时参数台账（`production/07` §2 的 P-01…P-13）。
链接此前只靠**自由文本**维持 —— 而「登记了吗」这种话**不该由人记**。

四档变异：

  ① 台账里写一个不存在的 P 编号（P-99）      ⇒ 断言 1 红（悬空链接）
  ② 拿掉某条待定行的归属指针（status 里的 P 编号）⇒ 断言 2 红（无主的占位值）
  ③ 把 `07` §2 里某一行 P 编号改掉            ⇒ 断言 1 红（**台账**还指着那个编号）
  ④ 把 `07` 的 §2 / §3 标题改名              ⇒ 守卫**明确失败**（结构变了，不是静默放过）

④ 那一档很关键：守卫读的是**标题**来定位 §2 —— 标题一改，
它必须**大声失败**，而不是「没读到 §2 ⇒ 集合为空 ⇒ 断言 1 空过」。

用法：python production/qa/mutate-ledger-crossref.py
"""
try:
    import _utf8out  # noqa: F401  — 控制台默认 GBK，印 ✓/✗ 会直接崩
except ImportError:
    pass

import hashlib
import json
import re
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
LEDGER = ROOT / "production" / "qa" / "parameters.json"
DECISION = ROOT / "production" / "07_技术决策与待确认事项.md"
TESTS = ROOT / "tests" / "Claude-Code-Game-Studios.Tests.csproj"
FILTER = "FullyQualifiedName~ParameterLedgerCrossReferenceTests"


def sha(p: Path) -> str:
    return hashlib.sha256(p.read_bytes()).hexdigest()


def run_tests() -> int:
    r = subprocess.run(
        ["dotnet", "test", str(TESTS), "-c", "Release", "--nologo", "-v", "q",
         "--filter", FILTER],
        cwd=ROOT, capture_output=True, text=True, encoding="utf-8", errors="replace")
    out = r.stdout + r.stderr
    for pat in (r"失败:\s*(\d+)", r"Failed:\s*(\d+)"):
        m = re.search(pat, out)
        if m:
            return int(m.group(1))
    return -1


def mutate_ledger(label, old, new):
    return _mutate(label, LEDGER, old, new)


def mutate_decision(label, old, new):
    return _mutate(label, DECISION, old, new)


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
        failed = run_tests()
        ok = failed > 0
        print(f"{'OK  ' if ok else '!!  '}变异 [{label}] -> "
              f"{'RED' if ok else 'GREEN'} (failed={failed}, 期望 RED)")
        return ok
    finally:
        target.write_bytes(backup)
        if sha(target) != before:
            print(f"!! 复原失败 [{label}]")


def main() -> int:
    results = []

    # ── ① 台账指向一个不存在的 P 编号 ─────────────────────────────────
    results.append(mutate_ledger(
        "台账指向一个不存在的 P 编号（P-99）",
        '"status": "【待定】P-13（D-003）"',
        '"status": "【待定】P-99（D-003）"',
    ))

    # ── ② 拿掉一条待定行的归属指针 ───────────────────────────────────
    results.append(mutate_ledger(
        "某条待定行不再说自己服务于哪个 P/T/D",
        '"status": "【占位】P-10"',
        '"status": "【占位】"',
    ))

    # ── ③ `07` §2 里那一行 P 编号被改掉 ⇒ 台账的引用悬空 ───────────────
    results.append(mutate_decision(
        "`07` §2 里 P-12 那一行被改成了别的编号（台账还指着 P-12）",
        "| P-12 | 副本阶段数",
        "| P-99 | 副本阶段数",
    ))

    # ── ④ `07` 的 §2 标题改名 ⇒ 守卫必须**大声**失败，而不是静默放过 ──
    #    ⚠️ 必须是**真的匹配不上**的标题：守卫用的是前缀查找，
    #    「## 2. 临时参数台账（旧）」仍会被找到 ⇒ 那一档什么也没破坏（绿色是对的）。
    results.append(mutate_decision(
        "`07` 的 §2 标题改名（守卫读不到它 ⇒ 必须失败而不是空过）",
        "## 2. 临时参数台账",
        "## 2. 临时参数表",
    ))

    print("---")
    bad = results.count(False)
    print(f"变异矩阵：{len(results) - bad}/{len(results)} 符合预期")
    return 1 if bad else 0


if __name__ == "__main__":
    sys.exit(main())
