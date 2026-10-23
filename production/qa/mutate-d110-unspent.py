"""
对 D-110「村庄可花却没花的钱」的口径做**变异看红**验证。

**它守的是三件事**：
  ① 口径 = 村库现金 + 村庄资产 − 下一级升级要留的钱
  ② 留的钱超过现有现金 ⇒ 记 **0**（不是负数 —— 那只是「还没攒够」）
  ③ 已满级（保留金 = 0）⇒ 就是现金 + 资产

⚠️ **为什么这一项值得单独守**：它是 D-109 新判据的第二条「**村庄花得出去**」的**唯一数据来源**
   ⇒ 它算错 ⇒ 判据就变成空话（而报告上看起来一切正常）。

用法：python production/qa/mutate-d110-unspent.py
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
FILTER = "FullyQualifiedName~VillageUnspentTests"

UNSPENT = ROOT / "src" / "Application" / "VillageUnspent.cs"


def sha(p: Path) -> str:
    return hashlib.sha256(p.read_bytes()).hexdigest()


def _kill_dotnet():
    subprocess.run(["taskkill", "/F", "/IM", "testhost.exe"], capture_output=True, text=True)
    subprocess.run(["taskkill", "/F", "/IM", "dotnet.exe"], capture_output=True, text=True)


def run_tests():
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


def _write_mutated(target: Path, old, new) -> bool:
    backup = target.read_bytes()
    raw = backup.decode("utf-8")
    crlf = "\r\n" in raw
    text = raw.replace("\r\n", "\n")
    old_n = old.replace("\r\n", "\n")
    new_n = new.replace("\r\n", "\n")
    if old_n not in text:
        print("!! 模式没找到 —— 变异脚本自己过期了")
        return False
    mutated = text.replace(old_n, new_n, 1)
    if crlf:
        mutated = mutated.replace("\n", "\r\n")
    target.write_text(mutated, encoding="utf-8", newline="")
    return True


def mutate(label, target: Path, old, new):
    backup = target.read_bytes()
    before = sha(target)
    if not _write_mutated(target, old, new):
        return False
    try:
        failed, out = run_tests()
        if failed == -2:
            print("!!  变异 -> TIMEOUT（守卫挂死，不是跑红）")
            return False
        ok = failed > 0
        print(f"{'OK  ' if ok else '!!  '}变异 [{label}] -> "
              f"{'RED' if ok else 'GREEN'}（期望 RED）")
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
    print("OK  基线（未变异）绿 —— D-110 三条当前成立")

    # ── ① 不减保留金 ⇒ 必须红 ─────────────────────────────────────────────
    results.append(mutate(
        "不减保留金（把要留的升级钱也算成「没花」）",
        UNSPENT,
        "        long value = villageGold + asset - reserveGold;",
        "        long value = villageGold + asset;   // 变异：不减保留金",
        ))

    # ── ② 允许负数 ⇒ 必须红 ───────────────────────────────────────────────
    results.append(mutate(
        "允许负数（还没攒够就记成负的未花出去）",
        UNSPENT,
        "        return value > 0 ? value : 0;",
        "        return value;   // 变异：允许负数",
        ))

    # ── ③ 把资产也算进「没花」的反面（漏掉资产）⇒ 必须红 ──────────────────
    results.append(mutate(
        "漏掉村庄资产（只算现金）",
        UNSPENT,
        "        long value = villageGold + asset - reserveGold;",
        "        long value = villageGold - reserveGold;   // 变异：漏掉资产",
        ))

    print("---")
    bad = results.count(False)
    print(f"变异矩阵：{len(results) - bad}/{len(results)} 符合预期")
    return 1 if bad else 0


if __name__ == "__main__":
    sys.exit(main())
