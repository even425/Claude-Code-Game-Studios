"""
对 D-124「装备 = 通关许可证」的守卫做**变异看红**验证。

**它守的是四件事**：
  ① 副本用**自己那张难度表**（不与试炼塔 / 狩猎 / 兽潮共用）
  ② 装备的加成**只认攻**（防御 / 生命在当前战斗公式下无效）
  ③ 弱装备 **+2 攻**、强装备 **+4 攻**（两档，不能都变成同一个数）
  ④ 副本三档的血量就是台账上那张（60 / 210 / 180）

用法：python production/qa/mutate-d124-license.py
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
FILTER = ("FullyQualifiedName~EquipmentTests"
          "|FullyQualifiedName~CombatParametersTests"
          "|FullyQualifiedName~FormulaCombatTests"
          "|FullyQualifiedName~ParameterLedgerTests")

EQUIP = ROOT / "src" / "Domain" / "Items" / "EquipmentDefinition.cs"
PARAMS = ROOT / "src" / "Domain" / "Combat" / "CombatParameters.cs"
STATS = ROOT / "src" / "Domain" / "Combat" / "EquipmentStats.cs"


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
    print("OK  基线（未变异）绿 —— D-124 四条当前成立")

    # ── ① 副本的难度表不生效（回到通用表）⇒ 必须红 ────────────────────────
    results.append(mutate(
        "副本难度表不生效（回到通用表）",
        PARAMS,
        "        var m = DungeonTiers[stageIndex];",
        "        var m = MonsterTiers[stageIndex];   // 变异：副本不用自己的表",
    ))

    # ── ② 装备加成也认防御（当前公式下防御无效 ⇒ 加成会白给）⇒ 必须红 ──────
    results.append(mutate(
        "服装又变回防御加成（防御当前无效 ⇒ 白给）",
        EQUIP,
        "            AttackBonus: 2, DefenseBonus: 0, DurabilityMax: 18, Price: 25L, RepairCost: 5L),",
        "            AttackBonus: 2, DefenseBonus: 5, DurabilityMax: 18, Price: 25L, RepairCost: 5L),",
    ))

    # ── ③ 强装备与弱装备加成一样（两档塌成一档）⇒ 必须红 ──────────────────
    results.append(mutate(
        "强装备与弱装备加成一样（两档塌成一档）",
        EQUIP,
        "            AttackBonus: 4, DefenseBonus: 0, DurabilityMax: 14, Price: 45L, RepairCost: 9L),",
        "            AttackBonus: 2, DefenseBonus: 0, DurabilityMax: 14, Price: 45L, RepairCost: 9L),",
    ))

    # ── ④ 副本三档的血量被改掉 ⇒ 必须红（台账会核对）────────────────────────
    results.append(mutate(
        "副本首领档血量被改掉",
        PARAMS,
        "        new CombatArchetype(\"首领\", 8, 4, 180),",
        "        new CombatArchetype(\"首领\", 8, 4, 120),   // 变异",
    ))

    print("---")
    bad = results.count(False)
    print(f"变异矩阵：{len(results) - bad}/{len(results)} 符合预期")
    return 1 if bad else 0


if __name__ == "__main__":
    sys.exit(main())
