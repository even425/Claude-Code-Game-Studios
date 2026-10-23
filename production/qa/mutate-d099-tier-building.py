"""
对 D-099「层级附带建筑（井）」的守卫做**变异看红**验证。

**它守的是六件事**：
  ① 井在**第 2 级**出现（层级阶梯里那一级就叫「有井的村子」）
  ② 第 1 级**没有**井（不许提前出现）
  ③ 升级后它**真的在村里**（不是地图上的装饰）
  ④ **幂等**（不能出现两口井）
  ⑤ 它**不**放开摆摊范围（D-098：只有范围类建筑才放开）
  ⑥ 它**不是可建建筑**（玩家不能花钱自己盖一口井 ⇒ 不能有两个来源）

⚠️ **接线层由真机自检 `--tier` 覆盖**（读档 ⇒ 老档补记 / 入档 / 不重复）。
⚠️ **规则必须住在可单测的地方**：第一版把「升级完成时补齐」写在 `Main`（表现层）
⇒ 单测碰不到 ⇒ **两档变异当时是绿的** ⇒ 已把规则搬进 `VillageUpgradeService`
（`Main` 只负责调用它）⇒ 变异才抓得住。

⚠️ **为什么这一档重要**：之前村里有那口井**只是地图上的手绘装饰**
⇒ 村庄里其实**没有**这栋建筑 ⇒ 「有井的村子」这句话**当时是假的**
（与 D-097 修的是同一族问题）。

用法：python production/qa/mutate-d099-tier-building.py
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
FILTER = "FullyQualifiedName~TierBuildingTests"

TIER = ROOT / "src" / "Domain" / "Village" / "TierBuildings.cs"
SERVICE = ROOT / "src" / "Application" / "TierBuildingService.cs"
RANGE = ROOT / "src" / "Domain" / "Village" / "RangeBuildings.cs"
UPGRADE = ROOT / "src" / "Application" / "VillageUpgradeService.cs"


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
    print("OK  基线（未变异）绿 —— D-099 六条当前成立")

    # ── ① 井提早出现（第 1 级就有）⇒ 必须红 ─────────────────────────────
    results.append(mutate(
        "井提早出现（第 1 级就有）",
        TIER,
        "        Tier: 2,",
        "        Tier: 1,   // 变异：井提早出现",
    ))

    # ── ② 幂等被破坏（升级重入 ⇒ 两口井）⇒ 必须红 ──────────────────────
    results.append(mutate(
        "幂等被破坏（升级重入 ⇒ 两口井）",
        SERVICE,
        "            if (village.Buildings.Any(b => b.Id.Value == definition.Id.Value)) continue;",
        "            // 变异：不查已有 ⇒ 重复放置",
    ))

    # ── ③ 井变成范围类（顺带放开摆摊）⇒ 必须红 ──────────────────────────
    results.append(mutate(
        "井变成范围类（顺带放开摆摊）",
        RANGE,
        "        if (TierBuildings.Find(id) is not null) return false;",
        "        // 变异：层级附带建筑也算范围类",
    ))

    # ── ④ 井进了可建目录（玩家能花钱自己盖一口）⇒ 必须红 ───────────────
    results.append(mutate(
        "井进了可建目录（玩家能花钱自己盖一口）",
        SERVICE,
        "            var instance = new BuildingInstance(\n                definition.Id,",
        "            var instance = new BuildingInstance(\n                new BuildingId(\"fence\"),   // 变异：塞进可建目录那一类",
    ))

    # ── ⑤ 升级完成时不补齐层级附带建筑（井永远不出现）⇒ 必须红 ──────────
    #    ⚠️ **规则必须住在可单测的地方**（`VillageUpgradeService.CompleteUpgrade`）：
    #    第一版把规则写在 `Main`（表现层）⇒ 单测碰不到 ⇒ **这一档当时是绿的**
    #    ⇒ 已把规则搬进 `VillageUpgradeService`，`Main` 只负责调用。
    results.append(mutate(
        "升级完成时不补齐层级附带建筑（井永远不出现）",
        UPGRADE,
        "    public IReadOnlyList<BuildingInstance> CompleteUpgrade(int tier)\n        => TierBuildingService.EnsureForTier(_village, tier);",
        "    public IReadOnlyList<BuildingInstance> CompleteUpgrade(int tier)\n        => System.Array.Empty<BuildingInstance>();   // 变异：从不补齐",
    ))

    # ── ⑥ 补齐时用错层级（该给 2 级补却给 1 级补）⇒ 必须红 ────────────
    results.append(mutate(
        "补齐时用错层级（该给 2 级补却给 1 级补）",
        UPGRADE,
        "    public IReadOnlyList<BuildingInstance> CompleteUpgrade(int tier)\n        => TierBuildingService.EnsureForTier(_village, tier);",
        "    public IReadOnlyList<BuildingInstance> CompleteUpgrade(int tier)\n        => TierBuildingService.EnsureForTier(_village, tier - 1);   // 变异：差一级",
    ))

    print("---")
    bad = results.count(False)
    print(f"变异矩阵：{len(results) - bad}/{len(results)} 符合预期")
    return 1 if bad else 0


if __name__ == "__main__":
    sys.exit(main())
