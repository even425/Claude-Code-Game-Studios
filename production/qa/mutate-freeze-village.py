"""
对 DungeonDoesNotFreezeVillageTests（IT-30 / A-10 / D-006）做**变异看红**验证。

D-006 三条：① 模拟时间继续推进 ② 未随队英雄照常行动 ③ 随队英雄不执行日常行为。
副本通常是游戏里最长的一段等待 —— 一旦它把村庄冻住，玩家就只剩盯着进度条。

三档变异分别打这三条：
  ① 时钟不推进       → 村庄被冻住
  ② 在场性不参与筛选 → 随队英雄也开始日常行为
  ③ 入队不校验前置   → 随队英雄能被编进第二个任务

用法：python production/qa/mutate-freeze-village.py
"""
try:
    import _utf8out  # noqa: F401  — 控制台默认 GBK，印 ✓/✗ 会直接崩
except ImportError:
    pass

import hashlib
import os
import re
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
TESTS = ROOT / "tests" / "Claude-Code-Game-Studios.Tests.csproj"
FILTER = "FullyQualifiedName~DungeonDoesNotFreezeVillageTests"


def sha(p):
    return hashlib.sha256(p.read_bytes()).hexdigest()


def run_tests():
    r = subprocess.run(
        ["dotnet", "test", str(TESTS), "-c", "Release", "--nologo", "-v", "q",
         "--filter", FILTER],
        cwd=ROOT, capture_output=True, text=True, encoding="utf-8", errors="replace",
            env={**os.environ, "MSBUILDDISABLENODEREUSE": "1"})
    out = r.stdout + r.stderr
    for pat in (r"失败:\s*(\d+)", r"Failed:\s*(\d+)"):
        m = re.search(pat, out)
        if m:
            return int(m.group(1)), out
    return -1, out


def mutate(label, rel_path, old, new):
    p = ROOT / rel_path
    backup = p.read_bytes()
    before = sha(p)
    raw = p.read_text(encoding="utf-8")
    crlf = "\r\n" in raw
    text = raw.replace("\r\n", "\n")
    old = old.replace("\r\n", "\n")
    new = new.replace("\r\n", "\n")
    if old not in text:
        print(f"!! [{label}] 模式没找到")
        return False
    text = text.replace(old, new, 1)
    if crlf:
        text = text.replace("\n", "\r\n")
    p.write_text(text, encoding="utf-8", newline="")
    try:
        failed, out = run_tests()
        color = "RED  " if failed > 0 else ("GREEN" if failed == 0 else "???  ")
        ok = failed > 0
        print(f"{'OK  ' if ok else '!!  '}变异 [{label}] -> {color} (failed={failed}, 期望 RED)")
        if not ok:
            for line in out.splitlines():
                if "失败" in line or "Expecting" in line:
                    print("      " + line.strip()[:160])
        return ok
    finally:
        p.write_bytes(backup)
        if sha(p) != before:
            print(f"!! 复原失败 [{label}]")


def main():
    results = []

    # ── ① 时钟不推进（村庄被冻住）──────────────────────────────────
    results.append(mutate(
        "模拟时钟不再推进（副本把村庄冻住）",
        "src/Domain/Clock/SimClock.cs",
        "        _now += simDeltaSeconds;\n        _stepCount++;",
        "        _stepCount++;",
    ))

    # ── ② 在场性不参与决策筛选（随队英雄也开始日常行为）────────────
    results.append(mutate(
        "决策筛选不看在场性（随队英雄也参与日常行为）",
        "src/Domain/Heroes/HeroState.cs",
        "    public bool IsEligibleForDecision() => IsIdle && IsInVillage;",
        "    public bool IsEligibleForDecision() => IsIdle;",
    ))

    # ── ③ 入队不校验前置（随队英雄能被编进第二个任务）──────────────
    results.append(mutate(
        "入队不校验前置条件（随队英雄能被编进第二个任务）",
        "src/Domain/Quests/Quests.cs",
        "        if (!Rules.CanJoin(hero)) return false;",
        "        // 变异：去掉前置校验",
    ))

    print("---")
    bad = results.count(False)
    print(f"变异矩阵：{len(results) - bad}/{len(results)} 符合预期")
    return 1 if bad else 0


if __name__ == "__main__":
    sys.exit(main())
