"""
对 D-122「采集器也盖商店 ＋ 副本优先派有装备的人」的守卫做**变异看红**验证。

**它守的是四件事**：
  ① 副本选人时**有装备的优先**（两轮扫描的第一轮不能塌）
  ② **坏掉的装备不算「有装备」**（穿了等于没穿）
  ③ 盖店时**必须留够下一级村庄升级的钱**（否则村庄永远升不上去）
  ④ 找落点时**找不到就说找不到**（不瞎报一个位置）

用法：python production/qa/mutate-d122-collector-shops.py
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
FILTER = ("FullyQualifiedName~CrewSelectionTests"
          "|FullyQualifiedName~BuildingSpotFinderTests"
          "|FullyQualifiedName~CollectorBuildingPlanTests")

CREW = ROOT / "src" / "Application" / "CrewSelection.cs"
PLAN = ROOT / "src" / "Application" / "CollectorBuildingPlan.cs"
SPOT = ROOT / "src" / "Application" / "BuildingSpotFinder.cs"


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
            timeout=600,
            env={**os.environ, "MSBUILDDISABLENODEREUSE": "1"})
    except subprocess.TimeoutExpired:
        _kill_dotnet()
        return -2, "TIMEOUT: 测试超过 600 秒未结束（守卫挂死，不是跑红）"
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
    print("OK  基线（未变异）绿 —— D-122 四条当前成立")

    # ── ① 选人时不优先有装备 ⇒ 必须红 ────────────────────────────────────
    results.append(mutate(
        "选人不优先有装备（第一轮塌掉）",
        CREW,
        "            if (!IsEquipped(h)) continue;",
        "            // 变异：不看装备",
    ))

    # ── ② 坏掉的装备也算「有装备」⇒ 必须红 ────────────────────────────────
    results.append(mutate(
        "坏掉的装备也算有装备",
        CREW,
        "        bool weaponOk = gear.Weapon is not null && gear.WeaponDurability > 0;",
        "        bool weaponOk = gear.Weapon is not null;   // 变异：不看耐久",
    ))

    # ── ③ 盖店时不再检查「留升级的钱」 ⇒ 必须红 ───────────────────────────
    # 变异内容：把「留升级钱」那道检查整个关掉（语法完整 ⇒ 能编译 ⇒ 测试会真的跑）。
    results.append(mutate(
        "盖店时不再检查「留升级的钱」",
        PLAN,
        "            if (keepUpgradeReserve && villageGold - definition.GoldCost < nextUpgradeGoldCost) continue;",
        "            if (false && keepUpgradeReserve && villageGold - definition.GoldCost < nextUpgradeGoldCost) continue;",
    ))

    # ── ④ 找不到落点也瞎报一个 ⇒ 必须红 ──────────────────────────────────
    results.append(mutate(
        "找不到落点也瞎报一个",
        SPOT,
        "        return null;\n    }\n}",
        "        return (baseX, baseY);   // 变异：找不到也报一个\n    }\n}",
    ))

    print("---")
    bad = results.count(False)
    print(f"变异矩阵：{len(results) - bad}/{len(results)} 符合预期")
    return 1 if bad else 0


if __name__ == "__main__":
    sys.exit(main())
