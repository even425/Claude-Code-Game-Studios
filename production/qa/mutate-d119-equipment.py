"""
对 D-119「装备线」的守卫做**变异看红**验证。

**它守的是六件事**：
  ① 耐久为 0 ⇒ **加成不生效**（坏了的装备等于没有）
  ② 补货的钱**记进村庄资产**（钱变成货架上的货 ⇒ 守恒律不破）
  ③ 买装备时钱**进村库**（内部转移 ⇒ 守恒律两边不变）
  ④ 修理**只修坏的**、修理费**只算坏的那几件**
  ⑤ 英雄**先修后买**（坏了的装备没有加成 ⇒ 比买更急）
  ⑥ 存档校验：装备 Id 未登记 / 槽位不对 / 耐久越界 ⇒ **按 path 精确拒绝**

用法：python production/qa/mutate-d119-equipment.py
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
FILTER = "FullyQualifiedName~EquipmentTests|FullyQualifiedName~EquipmentShopServiceTests|FullyQualifiedName~SaveVersion012Tests"

STATS = ROOT / "src" / "Domain" / "Combat" / "EquipmentStats.cs"
SERVICE = ROOT / "src" / "Application" / "EquipmentService.cs"
SHOP = ROOT / "src" / "Application" / "EquipmentShopService.cs"
HERO_EQUIP = ROOT / "src" / "Application" / "HeroEquipmentService.cs"
SAVE_EQUIP = ROOT / "src" / "data" / "SaveGameEquipment.cs"


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
    print("OK  基线（未变异）绿 —— D-119 六条当前成立")

    # ── ① 坏了的装备照样加�� ⇒ 必须红 ─────────────────────────────────────
    results.append(mutate(
        "坏掉的装备照样给加成",
        STATS,
        "if (gear.Weapon is { } weaponId && gear.WeaponDurability > 0)",
        "if (gear.Weapon is { } weaponId)   // 变异：不看耐久",
    ))

    # ── ② 补货的钱不记资产（凭空消失）⇒ 必须红 ────────────────────────────
    results.append(mutate(
        "补货的钱不记进村庄资产（凭空消失）",
        SHOP,
        "            village.Add(VillageAsset.Asset, RestockGoldCost);",
        "            // 变异：不记资产",
    ))

    # ── ③ 买装备的钱不进村库 ⇒ 必须红 ────────────────────────────────────
    results.append(mutate(
        "买装备的钱不进村库（钱凭空消失）",
        SHOP,
        "        village.Add(_gold, price);",
        "        // 变异：钱不进村库",
    ))

    # ── ④ 修理把好的也修了（修理费只算坏的被破坏）⇒ 必须红 ───────────────
    results.append(mutate(
        "修理费把所有装备都算上",
        SERVICE,
        "        if (gear.Weapon is { } w && gear.WeaponDurability <= 0)",
        "        if (gear.Weapon is { } w)   // 变异：好坏都算",
    ))

    # ── ⑤ 先买后修（坏了的不先修）⇒ 必须红 ────────────────────────────────
    results.append(mutate(
        "坏了的装备不先修（先买）",
        HERO_EQUIP,
        "            if (blacksmith && hero.Equipment.HasBroken)",
        "            if (false && blacksmith && hero.Equipment.HasBroken)   // 变异",
    ))

    # ── ⑥ 存档校验放过「未登记的装备」⇒ 必须红 ────────────────────────────
    results.append(mutate(
        "存档校验放过未登记的装备",
        SAVE_EQUIP,
        "        if (def is null)\n        {",
        "        if (def is null && false)   // 变异：放过未登记的装备\n        {",
    ))

    print("---")
    bad = results.count(False)
    print(f"变异矩阵：{len(results) - bad}/{len(results)} 符合预期")
    return 1 if bad else 0


if __name__ == "__main__":
    sys.exit(main())
