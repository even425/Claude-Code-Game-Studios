"""
对「真战斗」守卫（`FormulaCombatTests`）做**变异看红**验证。

**它守的是什么**：D-059…D-065 拍板的那套战斗规则 —— 伤害式子、属性换算、
回合上限、命中、状态效果、AI 目标。守卫 `FormulaCombatTests` 断言它们的**性质**
（队伍配置影响结果、战斗完全确定、日志恰有一条决定性记录、读不出就大声失败）。

⚠️ **为什么必须变异看红**：这些断言都是**否定式**的（「不命中也能打」「同样的输入
给出同样的结果」）—— 光看代码看不出来它有没有真的在守。**唯一能证明的方式是
故意把它改坏，看守卫会不会红。**

三档变异：

  ① **伤害恒为 1**（不减攻击、不减防御）⇒ 强弱队伍打不出差别 ⇒ IT-31 红
  ② **属性换算忽略性格与投入度**（所有人同一套属性）⇒ IT-31 红
  ③ **超时不算失败**（回合用尽 ⇒ 不判负）⇒ 战斗没有决定性记录 ⇒ D5 红

用法：python production/qa/mutate-combat-formula.py
"""
try:
    import _utf8out  # noqa: F401  — 控制台默认 GBK，印特殊符号会直接崩
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
FILTER = "FullyQualifiedName~FormulaCombatTests"
RULES = ROOT / "src" / "Domain" / "Combat" / "BattleRules.cs"
PARAMS = ROOT / "src" / "Domain" / "Combat" / "CombatParameters.cs"


def sha(p: Path) -> str:
    return hashlib.sha256(p.read_bytes()).hexdigest()


def run_tests():
    r = subprocess.run(
        ["dotnet", "test", str(TESTS), "-c", "Release", "--nologo", "-v", "q",
         "--filter", FILTER],
        cwd=ROOT, capture_output=True, text=True, encoding="utf-8", errors="replace",
            env={**os.environ, "MSBUILDDISABLENODEREUSE": "1"})
    out = r.stdout + r.stderr
    counts = re.findall(r"(?:失败|Failed):\s*(\d+)", out)
    if counts:
        return int(counts[-1]), out
    return -1, out


def mutate(label, target: Path, old, new):
    """改一处 → 跑测试 → 要求**红** → 复原文件（逐字节）。"""
    backup = target.read_bytes()
    before = sha(target)
    raw = backup.decode("utf-8")
    crlf = "\r\n" in raw
    text = raw.replace("\r\n", "\n")
    old_n = old.replace("\r\n", "\n")
    new_n = new.replace("\r\n", "\n")
    if old_n not in text:
        print(f"!! [{label}] 模式没找到 —— 变异脚本自己过期了")
        return False
    mutated = text.replace(old_n, new_n, 1)
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
        if not _restore(target, backup, before):
            print(f"!! 复原失败 [{label}]（变异可能留在源码里！）")



def _restore(target, backup: bytes, expected_sha: str) -> bool:
    """把源文件复原回去（**带重试与校验**）。

    ⚠️ **为什么需要重试**：Windows 上 `dotnet test` 的 MSBuild 节点会**复用**
    （node reuse）⇒ 编译服务器在子进程退出后**仍持有源文件的句柄**
    ⇒ 直接写会报 `OSError: [Errno 22]` ⇒ **变异就留在源码里了**
    （实测：D-079 那一档就这样残留过，把幂等机制改成了摆设）。

    ⚠️ **为什么必须校验**：复原失败还接着往下跑 ⇒ 下一个脚本的**基线是红的**
    ⇒ 矩阵「失败」但不是真失败 ⇒ 比没有守卫更糟。
    ⇒ 复原不成功就**大声报错**，让这一轮明确失败。
    """
    import hashlib
    import time

    for attempt in range(3):
        try:
            target.write_bytes(backup)
        except OSError:
            time.sleep(0.4)
            continue

        if hashlib.sha256(target.read_bytes()).hexdigest() == expected_sha:
            return True
        time.sleep(0.4)

    print(f"!! 复原失败（文件被占用？）：{target}")
    return False

def main() -> int:
    results = []

    failed, out = run_tests()
    if failed != 0:
        print(f"!! 基线就是红的（failed={failed}）—— 先修好再谈变异")
        for line in out.splitlines():
            if "失败" in line or "Expecting" in line:
                print("      " + line.strip()[:160])
        return 1
    print("OK  基线（未变异）绿 —— 那些性质当前成立")

    # ── ① 伤害恒为 1（不减攻防）⇒ 强弱队伍打不出差别 ⇒ IT-31 红 ────────
    results.append(mutate(
        "伤害恒为 1（不减攻击也不减防御）",
        RULES,
        "        int raw = attacker.Stats.Attack - defender.Stats.Defense;",
        "        int raw = 1;",
    ))

    # ── ② 属性换算忽略性格与投入度 ⇒ IT-31 红 ────────────────────────
    results.append(mutate(
        "属性换算忽略性格与投入度（所有人同一套属性）",
        PARAMS,
        "        return new CombatStats(attack, defense, d.Health);",
        "        return new CombatStats(8, 6, 80);",
    ))

    # ── ③ 超时不算失败 ⇒ 没有决定性记录 ⇒ D5 红 ──────────────────────
    results.append(mutate(
        "超时不算失败（回合用尽也不判负）",
        RULES,
        "        if (snapshot.RoundsCompleted >= CombatParameters.RoundLimit) return BattleOutcome.EnemiesWin;",
        "        // 变异：超时不再判负",
    ))

    print("---")
    bad = results.count(False)
    print(f"变异矩阵：{len(results) - bad}/{len(results)} 符合预期")
    return 1 if bad else 0


if __name__ == "__main__":
    sys.exit(main())
