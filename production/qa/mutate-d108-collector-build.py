"""
对 D-108「标定器补上花钱的出口 + 建造不许半提交」的守卫做**变异看红**验证。

**它守的是六件事**：
  ① 采购把钱吃掉时，建造**整体失败**、**一分钱都不许多出来**（不许半提交）
  ② 建造花掉的钱**记进「村庄资产」**（钱变成了建筑，没被烧掉）
  ③ 落点放不下（退款那条路）⇒ **资产那一笔也要一起退**
  ④ 采集器盖房：**盖完还得买得起下一级升级**（否则村庄永远升不上去）
  ⑤ 采集器盖房：**围栏与试炼塔轮着盖**（谁盖得少盖谁）
  ⑥ 采集器盖房：钱不够 / 没地方 ⇒ **不盖**

⚠️ **为什么 ①②③ 要紧**：修复前，采集器一盖房子，
   **200 天里 143 天守恒律对不上，差额正好等于一座围栏的造价（80 金）**
   —— 那是**凭空造钱**（D-108 在这里抓到的真缺陷，与采集器无关：
   玩家自己盖房子同样会中招）。

用法：python production/qa/mutate-d108-collector-build.py
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
FILTER = ("FullyQualifiedName~BuildingPurchaseAtomicityTests"
          "|FullyQualifiedName~CollectorBuildingPlanTests")

BUILDING = ROOT / "src" / "Application" / "BuildingService.cs"
PLAN = ROOT / "src" / "Application" / "CollectorBuildingPlan.cs"


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
    print("OK  基线（未变异）绿 —— D-108 六条当前成立")

    # ── ① 扣款失败被忽略（回到「半提交」的缺陷版）⇒ 必须红 ────────────────
    results.append(mutate(
        "扣款失败被忽略（回到「半提交」缺陷版 ⇒ 凭空造钱）",
        BUILDING,
        "        bool goldSpent = _village.TrySpend(_gold, definition.GoldCost);\n"
        "        bool materialSpent = fromPool <= 0 || _village.TrySpend(_material, fromPool);\n"
        "        if (!goldSpent || !materialSpent)",
        "        bool goldSpent = _village.TrySpend(_gold, definition.GoldCost);   // 变异：结果被忽略\n"
        "        bool materialSpent = fromPool <= 0 || _village.TrySpend(_material, fromPool);\n"
        "        if (false)",
        ))

    # ── ② 回滚时不退采购（货白拿）⇒ 必须红 ────────────────────────────────
    results.append(mutate(
        "回滚时不退采购（货白拿：钱退了、货没还）",
        BUILDING,
        "            _procurement?.Rollback(plan);\n            return false;",
        "            // 变异：不退采购\n            return false;",
        ))

    # ── ③ 花掉的钱不记进资产 ⇒ 必须红 ──────────────────────────────────────
    results.append(mutate(
        "建造花掉的钱不记进「村庄资产」（守恒律左边少一笔）",
        BUILDING,
        "        _village.Add(VillageAsset.Asset, definition.GoldCost);",
        "        // 变异：不记账",
        ))

    # ── ④ 资产记成两倍 ⇒ 必须红 ────────────────────────────────────────────
    results.append(mutate(
        "资产记成两倍（钱变成双份建筑）",
        BUILDING,
        "        _village.Add(VillageAsset.Asset, definition.GoldCost);",
        "        _village.Add(VillageAsset.Asset, definition.GoldCost * 2);   // 变异：记两倍",
        ))

    # ── ⑤ 采集器盖房：不留升级的钱 ⇒ 必须红 ────────────────────────────────
    results.append(mutate(
        "采集器盖房不留下一级升级的钱（村庄永远升不上去）",
        PLAN,
        "            if (villageGold - definition.GoldCost < nextUpgradeGoldCost) continue;",
        "            // 变异：不留升级的钱",
        ))

    # ── ⑥ 采集器盖房：不再轮着盖（永远第一个）⇒ 必须红 ──────────────────────
    results.append(mutate(
        "采集器不再轮着盖（永远先盖第一个候选）",
        PLAN,
        "        var ordered = candidates\n"
        "            .OrderBy(c => existingCounts.TryGetValue(c.Id.Value, out int n) ? n : 0)\n"
        "            .ToArray();",
        "        var ordered = candidates.ToArray();   // 变异：不轮着盖",
        ))

    print("---")
    bad = results.count(False)
    print(f"变异矩阵：{len(results) - bad}/{len(results)} 符合预期")
    return 1 if bad else 0


if __name__ == "__main__":
    sys.exit(main())
