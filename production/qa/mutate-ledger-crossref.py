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

    # ── ⑤ 备注里写一个台账里查无此数的数字 ⇒ 断言 3 红 ────────────────
    results.append(mutate_ledger(
        "台账某条的备注里写了一个不存在的数字（值改了、措辞没跟上）",
        '"note": "战斗占位胜率（0.65）"',
        '"note": "战斗占位胜率（0.65，也可能是 0.71）"',
    ))

    # ── ⑥ 「值由机器跟、过期措辞由机器抓」这条链 ──────────────────────
    #    改代码里的常量 → 跑 `--sync-values` → **值**跟着走（✓），
    #    而备注里的旧数字变成「查无此数」⇒ 守卫**红**（✓）——
    #    这正是设计：值不用人跟，措辞必须人跟，而「措辞没跟上」机器会喊。
    src = ROOT / "src" / "Application" / "PlaceholderCombat.cs"
    src_backup = src.read_bytes()
    # ⚠️ 清单文档是**生成**的 —— `--sync-values` 会顺手重写它 ⇒ 必须一起备份/复原，
    # 否则一次变异跑完，仓库里就多一份「不该在」的文档改动。
    doc = ROOT / "production" / "36_当前实现参数清单_V1.0.md"
    doc_backup = doc.read_bytes()
    try:
        text = src_backup.decode("utf-8")
        src.write_text(text.replace("0.65", "0.66"), encoding="utf-8", newline="")
        proc = subprocess.run(
            [sys.executable, str(ROOT / "production" / "qa" / "param-inventory.py"),
             "--sync-values"], cwd=ROOT, capture_output=True, text=True,
            encoding="utf-8", errors="replace")
        ledger_changed = "0.66" in proc.stdout
        failed = run_tests()
        ok = ledger_changed and failed > 0
        print(f"{'OK  ' if ok else '!!  '}变异 [改代码常量 → --sync-values 跟值 → "
              f"备注里的旧数字被守卫抓住] -> "
              f"{'RED' if failed > 0 else 'GREEN'}（值跟上了：{ledger_changed}，期望 RED）")
        results.append(ok)
    finally:
        src.write_bytes(src_backup)
        # 台账里的值要跟着代码一起复原
        subprocess.run(
            [sys.executable, str(ROOT / "production" / "qa" / "param-inventory.py"),
             "--sync-values"], cwd=ROOT, capture_output=True, text=True,
            encoding="utf-8", errors="replace")
        # 文档是生成的 ⇒ 复原成变异前的那一份（别把生成物留在工作区）
        doc.write_bytes(doc_backup)

    # ── ⑦ `07` §2 点名的常量值与台账不符 ⇒ 断言 4 红 ─────────────────
    #    「规则层的台账在撒谎」—— §2 说 `MaxTier = 5`，台账里是别的数。
    results.append(mutate_decision(
        "`07` §2 里点名的常量值与台账不符（规则层的台账在撒谎）",
        "`MaxTier = 5`",
        "`MaxTier = 7`",
    ))

    print("---")
    bad = results.count(False)
    print(f"变异矩阵：{len(results) - bad}/{len(results)} 符合预期")
    return 1 if bad else 0


if __name__ == "__main__":
    sys.exit(main())
