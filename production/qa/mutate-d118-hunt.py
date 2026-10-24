"""
对 D-118「英雄出村狩猎」的守卫做**变异看红**验证。

**它守的是四件事**：
  ① 打赢 ⇒ 那只野兽**被移走**（否则永远打不完 ⇒ 野外成了无限资源）
  ② 打输 ⇒ **没有**战利品（不许「打输了也给」）
  ③ 狼是**精英档**（普通人打不赢 ⇒ 强的人打得过是设计，不是巧合）
  ④ 战利品**按种类**不同（鹿肉多 / 狼皮多 / 野猪两样各一）

用法：python production/qa/mutate-d118-hunt.py
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
FILTER = "FullyQualifiedName~HuntServiceTests"

HUNT = ROOT / "src" / "Application" / "HuntService.cs"
WILDLIFE = ROOT / "src" / "Domain" / "Wildlife" / "WildlifeBook.cs"


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
    print("OK  基线（未变异）绿 —— D-118 四条当前成立")

    # ── ① 打赢不移走野兽 ⇒ 必须红 ────────────────────────────────────────
    results.append(mutate(
        "打赢不移走野兽（永远打不完）",
        HUNT,
        "        _wildlife.Remove(beast);",
        "        // 变异：不移走野兽",
    ))

    # ── ② 打输也给战利品 ⇒ 必须红 ───────────────────────────────────────
    results.append(mutate(
        "打输也给战利品",
        HUNT,
        "            return new HuntOutcome(heroId, false, beast, new Dictionary<string, long>());",
        "            _grantLoot(heroId, LootOf(beast.Kind));   // 变异：打输也发\n"
        "            return new HuntOutcome(heroId, false, beast, new Dictionary<string, long>());",
    ))

    # ── ③ 狼不当精英档（人人都能打）⇒ 必须红 ────────────────────────────
    results.append(mutate(
        "狼不当成精英档（人人都打得起）",
        HUNT,
        "        BeastKind.Wolf => 1,",
        "        BeastKind.Wolf => 0,   // 变异：狼不凶了",
    ))

    # ── ④ 战利品与种类无关 ⇒ 必须红 ──────────────────────────────────────
    results.append(mutate(
        "战利品与种类无关（都给一样）",
        HUNT,
        "        BeastKind.Deer => new Dictionary<string, long> { [\"兽肉\"] = 2, [\"兽皮\"] = 1 },",
        "        BeastKind.Deer => new Dictionary<string, long> { [\"兽肉\"] = 1, [\"兽皮\"] = 1 },   // 变异",
    ))

    print("---")
    bad = results.count(False)
    print(f"变异矩阵：{len(results) - bad}/{len(results)} 符合预期")
    return 1 if bad else 0


if __name__ == "__main__":
    sys.exit(main())
