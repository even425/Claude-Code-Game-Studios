"""
对 D-100「战斗回合数的记法」的守卫做**变异看红**验证。

**它守的是四件事**：
  ① **第 1 回合就分胜负 ⇒ 回合数是 1**（不是 0 —— 0 会让读回器按契约抛异常）
  ② **一场「一拳秒杀」的战斗不会崩**（它合法 ⇒ 不许抛）
  ③ **引擎报出的回合数 == 日志里读出来的**（同一个事实的两个出口不许分叉）
  ④ **超时判负那条路不受影响**（仍是回合上限 30）

⚠️ **为什么这一档重要**：这是一个**真 bug**，而且它**早就在仓库里**
（`WorldEventConsequenceTests` 里甚至写着「不要用『一拳秒杀』的属性」）
⇒ 说明**它一直被绕开，而没人修**。
⚠️ 我自己写 D-096 时也**用同样手法绕过了它**（把怪物血量 20 改成 200）
⇒ 测试绿了，根因还在。

用法：python production/qa/mutate-d100-round-count.py
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
import time
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
TESTS = ROOT / "tests" / "Claude-Code-Game-Studios.Tests.csproj"
FILTER = "FullyQualifiedName~BattleRoundCount"

ENGINE = ROOT / "src" / "Domain" / "Combat" / "BattleEngine.cs"


def sha(p: Path) -> str:
    return hashlib.sha256(p.read_bytes()).hexdigest()


def run_tests():
    # ⚠️ **必须带构建**（不要加 `--no-build`）：实测在本机 `--no-build` 会让
    # vstest 挂死（进程在、CPU 不涨），而带构建的同一条命令只要几秒。
    try:
        r = subprocess.run(
            ["dotnet", "test", str(TESTS), "-c", "Release", "--nologo", "-v", "q",
             "--filter", FILTER],
            cwd=ROOT, capture_output=True, text=True, encoding="utf-8", errors="replace",
            timeout=300,
            env={**os.environ, "MSBUILDDISABLENODEREUSE": "1"})
    except subprocess.TimeoutExpired:
        _kill_dotnet()
        return -2, "TIMEOUT: 测试超过 300 秒未结束（守卫挂死，不是跑红）"
    out = r.stdout + r.stderr
    counts = re.findall(r"(?:失败|Failed):\s*(\d+)", out)
    if counts:
        return int(counts[-1]), out
    return -1, out


def _kill_dotnet():
    subprocess.run(["taskkill", "/F", "/IM", "testhost.exe"], capture_output=True, text=True)
    subprocess.run(["taskkill", "/F", "/IM", "dotnet.exe"], capture_output=True, text=True)


def _restore(target, backup: bytes, expected_sha: str) -> bool:
    for _ in range(3):
        try:
            target.write_bytes(backup)
        except OSError:
            time.sleep(0.4)
            continue

        if sha(target) == expected_sha:
            return True
        time.sleep(0.4)

    print(f"!! 复原失败（文件被占用？）：{target}")
    return False


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
        if failed == -2:
            print(f"!!  变异 [{label}] -> TIMEOUT（守卫挂死，不是跑红）")
            return False
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


def main() -> int:
    results = []

    failed, out = run_tests()
    if failed != 0:
        print(f"!! 基线就是红的（failed={failed}）—— 先修好再谈变异")
        for line in out.splitlines():
            if "失败" in line or "Expecting" in line:
                print("      " + line.strip()[:160])
        return 1
    print("OK  基线（未变异）绿 —— D-100 四条当前成立")

    # ── ① 分胜负时少记一回合（回到 bug 本身）⇒ 必须红 ────────────────────
    results.append(mutate(
        "分胜负时少记一回合（回到 bug 本身）",
        ENGINE,
        "                                roundsUsed = roundsCompleted + 1;\n"
        "                                log.Add(Entry(ref seq, input.EncounterName,\n"
        "                                    DecisiveSummary(input.EncounterName, outcome, roundsUsed, actor),",
        "                                roundsUsed = roundsCompleted;   // 变异：少记一回合\n"
        "                                log.Add(Entry(ref seq, input.EncounterName,\n"
        "                                    DecisiveSummary(input.EncounterName, outcome, roundsUsed, actor),",
    ))

    # ── ② 「回合开始就判负」那条路也被带偏 ⇒ 必须红 ──────────────────────
    #    ⚠️ **第一版这里写的是「开场就分胜负」** ⇒ **那档当时是绿的** ⇒ 那是装饰品：
    #    「回合开始就判负」那条路**根本不可达**（战斗开始时双方都活着）。
    #    ⇒ 改成一条**真能抓**的：**开场判负那条路的 +1 被去掉**
    #    （它是唯一一处「+1 少算了就查不出」的地方 ⇒ 用「超时那条路不被 +1」来交叉验）。
    results.append(mutate(
        "回合末那条路被 +1（回合上限被算成 31）",
        ENGINE,
        "                roundsUsed = roundsCompleted;\n"
        "                log.Add(Entry(ref seq, input.EncounterName, DecisiveSummary(input.EncounterName, outcome, roundsUsed, null), true));",
        "                roundsUsed = roundsCompleted + 1;   // 变异：这条路上也 +1\n"
        "                log.Add(Entry(ref seq, input.EncounterName, DecisiveSummary(input.EncounterName, outcome, roundsUsed, null), true));",
    ))

    # ── ③ 引擎报出的回合数与日志分叉 ⇒ 必须红 ──────────────────────────
    results.append(mutate(
        "引擎报出的回合数与日志分叉",
        ENGINE,
        "        return new BattleRun(outcome, log, roundsUsed);",
        "        return new BattleRun(outcome, log, roundsCompleted);   // 变异：两个出口分叉",
    ))

    # ── ④ 分胜负时**多记**一回合（修过头 ⇒ 耗时算多了）⇒ 必须红 ──────────
    results.append(mutate(
        "分胜负时多记一回合（修过头 ⇒ 耗时算多了）",
        ENGINE,
        "                                roundsUsed = roundsCompleted + 1;\n"
        "                                log.Add(Entry(ref seq, input.EncounterName,\n"
        "                                    DecisiveSummary(input.EncounterName, outcome, roundsUsed, actor),",
        "                                roundsUsed = roundsCompleted + 2;   // 变异：多记一回合\n"
        "                                log.Add(Entry(ref seq, input.EncounterName,\n"
        "                                    DecisiveSummary(input.EncounterName, outcome, roundsUsed, actor),",
    ))

    print("---")
    bad = results.count(False)
    print(f"变异矩阵：{len(results) - bad}/{len(results)} 符合预期")
    return 1 if bad else 0


if __name__ == "__main__":
    sys.exit(main())
