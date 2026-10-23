"""
对 D-107「铁匠铺 = 采集点（层级第 4 级附带建筑）」的守卫做**变异看红**验证。

**它守的是六件事**：
  ① 第 4 级 ⇒ 铁匠铺出现（第 3 级不许提前出现）
  ② 它是**建好的**（升级送的 ⇒ 一出现就生效）
  ③ 它是**采集点** ⇒ 与井**叠加**（第 4 级 = +2 份/天）
  ④ 它不是范围类 / 不是可建建筑
  ⑤ 它有**自己的画法**（D-097：长得不一样才认得出）
  ⑥ 它的落点在可见地图上、且**不与井重叠**（重叠 ⇒ 后放的那栋放不下 ⇒ 静默少一栋）

⚠️ **为什么要单独守「不重叠」**：两栋都是**层级附带**建筑、落点都是**写死的**
   ⇒ 一旦写重了，`TryPlace` 会失败 ⇒ `EnsureForTier` **静默跳过**（它不抛异常）
   ⇒ 村庄里就**少一栋**，而游戏**不报任何错**。

⚠️ **最后一档是接线层**：`CompleteUpgrade`（真实升级路径的那个入口）只补第 2 级的井
   ⇒ 单元测试能抓到（TierBuildingTests 直接调它）⇒ 但**真机自检**也必须能抓到
   ⇒ 所以两档都跑。

用法：python production/qa/mutate-d107-blacksmith.py
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
import tempfile
import time
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
TESTS = ROOT / "tests" / "Claude-Code-Game-Studios.Tests.csproj"
FILTER = ("FullyQualifiedName~BlacksmithBuildingTests"
          "|FullyQualifiedName~WellGatheringTests"
          "|FullyQualifiedName~TierBuildingTests"
          "|FullyQualifiedName~BuildingVisualTests")

TIER_BUILDINGS = ROOT / "src" / "Domain" / "Village" / "TierBuildings.cs"
VISUAL = ROOT / "src" / "Presentation" / "BuildingVisual.cs"
UPGRADE = ROOT / "src" / "Application" / "VillageUpgradeService.cs"

GODOT_CANDIDATES = [
    os.environ.get("GODOT"),
    r"C:\Tools\Godot\4.7.2\godot.exe",
]
FRAMES = "600"
SEED = "20261021"


def sha(p: Path) -> str:
    return hashlib.sha256(p.read_bytes()).hexdigest()


def find_godot():
    for c in GODOT_CANDIDATES:
        if c and Path(c).exists():
            return c
    raise SystemExit("!! 找不到 Godot 可执行文件（设 GODOT 环境变量）")


def _kill_dotnet():
    subprocess.run(["taskkill", "/F", "/IM", "testhost.exe"], capture_output=True, text=True)
    subprocess.run(["taskkill", "/F", "/IM", "dotnet.exe"], capture_output=True, text=True)


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


def run_tier_selfcheck():
    """跑真机自检 `--tier`（headless）。"""
    build = subprocess.run(["dotnet", "build", "--nologo", "-v", "q"],
                           cwd=ROOT, capture_output=True, text=True,
                           encoding="utf-8", errors="replace", timeout=300)
    if build.returncode != 0:
        return False, "BUILD FAILED:\n" + (build.stdout or "")[-1500:]

    tmp_save = Path(tempfile.gettempdir()) / "d107-mutate-save.json"
    argv = [find_godot(), "--headless", "--path", str(ROOT), "--quit-after", FRAMES, "--",
            "--tier", f"--savepath={tmp_save}", f"--seed={SEED}"]
    try:
        proc = subprocess.run(argv, cwd=ROOT, capture_output=True, text=True,
                              encoding="utf-8", errors="replace", timeout=300)
    except subprocess.TimeoutExpired:
        return False, "TIMEOUT: 自检 300 秒未结束"
    out = (proc.stdout or "") + (proc.stderr or "")
    return "TIERTEST 层级附带建筑（井 + 铁匠铺）⇒ PASS" in out, out


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


def mutate(label, target: Path, old, new, checker):
    backup = target.read_bytes()
    before = sha(target)
    if not _write_mutated(target, old, new):
        return False
    try:
        ok, detail = checker()
        print(f"{'OK  ' if ok else '!!  '}变异 [{label}] -> "
              f"{'RED' if ok else 'GREEN'}（期望 RED）")
        if not ok:
            for line in str(detail).splitlines():
                if "失败" in line or "Expecting" in line or "TIERTEST" in line:
                    print("      " + line.strip()[:160])
        return ok
    finally:
        if not _restore(target, backup, before):
            print(f"!! 复原失败 [{label}]（变异可能留在源码里！）")


def unit_checker():
    failed, out = run_tests()
    if failed == -2:
        print("!!  变异 -> TIMEOUT（守卫挂死，不是跑红）")
        return False, out
    return failed > 0, out


def selfcheck_checker():
    passed, out = run_tier_selfcheck()
    return not passed, out


def main() -> int:
    results = []

    failed, out = run_tests()
    if failed != 0:
        print(f"!! 基线就是红的（failed={failed}）—— 先修好再谈变异")
        for line in out.splitlines():
            if "失败" in line or "Expecting" in line:
                print("      " + line.strip()[:160])
        return 1
    print("OK  基线（未变异）绿 —— D-107 六条当前成立")

    # ── ① 层级改成 3（提早出现）⇒ 必须红 ────────────────────────────────
    results.append(mutate(
        "铁匠铺提早到第 3 级出现",
        TIER_BUILDINGS,
        "        Tier: 4,",
        "        Tier: 3,   // 变异：提早出现",
        unit_checker,
    ))

    # ── ② 效果被抹掉（不再是采集点）⇒ 必须红 ────────────────────────────
    results.append(mutate(
        "铁匠铺的效果被抹掉（不再是采集点）",
        TIER_BUILDINGS,
        "        // D-107：开发者拍板「采集 +1」⇒ 它也是一个采集点（与井叠加）。\n"
        "        Effect: BuildingEffect.Gathering);",
        "        Effect: BuildingEffect.None);   // 变异：没有效果了",
        unit_checker,
    ))

    # ── ③ 落点与井重叠 ⇒ 放不下 ⇒ 静默少一栋 ⇒ 必须红 ───────────────────
    results.append(mutate(
        "铁匠铺的落点与井重叠（(8,0) → (5,0)）",
        TIER_BUILDINGS,
        "        GridX: 8,\n        GridY: 0,",
        "        GridX: 5,   // 变异：与井重叠\n        GridY: 0,",
        unit_checker,
    ))

    # ── ④ 画法映射被删（回到兜底石台）⇒ 必须红 ──────────────────────────
    results.append(mutate(
        "铁匠铺没有专属画法（落回兜底石台）",
        VISUAL,
        "            if (tierBuilding.Value.Id.Value == TierBuildings.Blacksmith.Id.Value) return Blacksmith;",
        "            // 变异：铁匠铺没有专属画法",
        unit_checker,
    ))

    # ── ⑤ 目录里没有铁匠铺（升级不补齐）⇒ 必须红 ────────────────────────
    results.append(mutate(
        "目录里没有铁匠铺（升级不会补齐它）",
        TIER_BUILDINGS,
        "    public static IReadOnlyList<TierBuilding> All { get; } = new[] { Well, Blacksmith };",
        "    public static IReadOnlyList<TierBuilding> All { get; } = new[] { Well };   // 变异：目录里没有铁匠铺",
        unit_checker,
    ))

    # ── ⑥ 接线层：升级完成那个入口只补第 2 级的井 ⇒ 真机自检必须红 ──────
    results.append(mutate(
        "接线层：升级完成入口只补井（不补铁匠铺）",
        UPGRADE,
        "    public IReadOnlyList<BuildingInstance> CompleteUpgrade(int tier)\n"
        "        => TierBuildingService.EnsureForTier(_village, tier);",
        "    public IReadOnlyList<BuildingInstance> CompleteUpgrade(int tier)\n"
        "        => TierBuildingService.EnsureForTier(_village, tier < 4 ? tier : 2);   // 变异：只补到第 2 级",
        selfcheck_checker,
    ))

    print("---")
    bad = results.count(False)
    print(f"变异矩阵：{len(results) - bad}/{len(results)} 符合预期")
    return 1 if bad else 0


if __name__ == "__main__":
    sys.exit(main())
