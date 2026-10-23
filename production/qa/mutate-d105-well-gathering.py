"""
对 D-105「井 = 采集点」的守卫做**变异看红**验证。

**它守的是六件事**：
  ① 没有采集点 ⇒ 采集产量不变（D-086 的 1 份/天）
  ② 有采集点 ⇒ 每天多采 1 份
  ③ **施工中**的井不算（还没打井呢）
  ④ 背包装不下 ⇒ 只采装得下的那部分
  ⑤ 升到第 2 级 ⇒ 井自动出现 ⇒ 加成自动生效（端到端）
  ⑥ 判定依据是**效果**而不是名字；别的建筑不许加产量

⚠️ **为什么第 ⑥ 条要单独守**：D-098 的「范围类」是**查不到就算范围类**（宁可多算），
   而「加产量」必须**宁可漏算**（多算 = 白送材料 ⇒ 经济标定会漂）
   ⇒ 两条规则方向相反，**必须分别钉住**，否则有人照着 D-098 的写法抄就会抄反。

⚠️ **两类变异**：
  - ① ~ ⑥ 是**单元层**的（`dotnet test`）
  - ⑦ 是**接线层**的（`Main.cs` 里那一行委托）⇒ **只有真机自检抓得到**
    （单元测试碰不到 `Main`）⇒ 这一档是本脚本的重点。

用法：
  python production/qa/mutate-d105-well-gathering.py              # 全部（需要能重编 Debug）
  python production/qa/mutate-d105-well-gathering.py --unit-only  # 只跑单元层（后台扫描占用 Debug 时用）
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
FILTER = "FullyQualifiedName~WellGatheringTests|FullyQualifiedName~GatherServiceTests"

POINTS = ROOT / "src" / "Domain" / "Village" / "GatheringPoints.cs"
TIER_BUILDINGS = ROOT / "src" / "Domain" / "Village" / "TierBuildings.cs"
GATHER = ROOT / "src" / "Application" / "GatherService.cs"
MAIN = ROOT / "src" / "Main.cs"

GODOT_CANDIDATES = [
    os.environ.get("GODOT"),
    r"C:\Tools\Godot\4.7.2\godot.exe",
]
FRAMES = "600"
SEED = "20261021"
UNIT_ONLY = "--unit-only" in sys.argv


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
    """跑真机自检 `--tier`（headless）。

    返回 (是否出现 "TIERTEST 层级附带建筑 ⇒ PASS", 全部输出)。
    ⚠️ headless 加载的是 **Debug** 的程序集 ⇒ 变异后必须重新构建 Debug。
    """
    build = subprocess.run(["dotnet", "build", "--nologo", "-v", "q"],
                           cwd=ROOT, capture_output=True, text=True,
                           encoding="utf-8", errors="replace", timeout=300)
    if build.returncode != 0:
        return False, "BUILD FAILED:\n" + (build.stdout or "")[-1500:]

    tmp_save = Path(tempfile.gettempdir()) / "d105-mutate-save.json"
    argv = [find_godot(), "--headless", "--path", str(ROOT), "--quit-after", FRAMES, "--",
            "--tier", f"--savepath={tmp_save}", f"--seed={SEED}"]
    try:
        proc = subprocess.run(argv, cwd=ROOT, capture_output=True, text=True,
                              encoding="utf-8", errors="replace", timeout=300)
    except subprocess.TimeoutExpired:
        return False, "TIMEOUT: 自检 300 秒未结束"
    out = (proc.stdout or "") + (proc.stderr or "")
    return "TIERTEST 层级附带建筑 ⇒ PASS" in out, out


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
    """改一处 → 跑检查 → 要求**红** → 复原文件（逐字节 + 校验 SHA-256）。"""
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
    # 红 = 「PASS」不见了（要么 FAIL，要么根本没跑到）
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
    print("OK  基线（未变异）绿 —— D-105 六条当前成立")

    # ── ① 采集点加成改成 0（采集点白给）⇒ 必须红 ─────────────────────────
    results.append(mutate(
        "采集点加成改成 0（井白给了）",
        POINTS,
        "    public const long BonusPerPoint = 1L;",
        "    public const long BonusPerPoint = 0L;   // 变异：采集点白给",
        unit_checker,
    ))

    # ── ② 判定改成「按名字」⇒ 必须红 ────────────────────────────────────
    #    ⚠️ 名字是显示、会被改；效果是规则 ⇒ 判据必须是效果。
    results.append(mutate(
        "采集点判定改成按名字（效果被忽略）",
        POINTS,
        "        => definition is not null && definition.Value.Effect == BuildingEffect.Gathering;",
        "        => definition is not null && definition.Value.Name == \"井\";   // 变异：按名字判",
        unit_checker,
    ))

    # ── ③ 施工中的井也算（还没打井就变快）⇒ 必须红 ──────────────────────
    results.append(mutate(
        "施工中的井也算采集点",
        POINTS,
        "            .Where(b => IsGatheringPoint(b) && b.ConstructionProgress >= 1.0)",
        "            .Where(b => IsGatheringPoint(b))   // 变异：施工中就生效",
        unit_checker,
    ))

    # ── ④ 采集时压根不加成 ⇒ 必须红 ─────────────────────────────────────
    results.append(mutate(
        "采集时压根不加成",
        GATHER,
        "        long wanted = GatheredAmountPerDay + BonusFromGatheringPoints();",
        "        long wanted = GatheredAmountPerDay;   // 变异：加成没接上",
        unit_checker,
    ))

    # ── ⑤ 不裁背包（装不下也硬塞）⇒ 必须红 ──────────────────────────────
    results.append(mutate(
        "不按背包剩余空间裁剪（装不下也硬塞）",
        GATHER,
        "        long amount = wanted < room ? wanted : room;",
        "        long amount = wanted;   // 变异：不裁剪",
        unit_checker,
    ))

    # ── ⑥ 井的效果被抹掉（不再是采集点）⇒ 必须红 ────────────────────────
    results.append(mutate(
        "井的效果被抹掉（不再是采集点）",
        TIER_BUILDINGS,
        "        Effect: BuildingEffect.Gathering);",
        "        Effect: BuildingEffect.None);   // 变异：井没有效果了",
        unit_checker,
    ))

    if not UNIT_ONLY:
        # ── ⑦ 接线层：`Main.cs` 的委托被摘掉 ⇒ 真机自检必须红 ────────────
        #    （单元测试碰不到 Main ⇒ 这一档只有真机自检抓得到）
        results.append(mutate(
            "接线层：Main.cs 没把村庄的采集加成接进去",
            MAIN,
            "        _gather = new ClaudeCodeGameStudios.Application.GatherService(\n"
            "            _stallService, _priceBook,\n"
            "            () => ClaudeCodeGameStudios.Domain.Village.GatheringPoints.BonusFor(_villageModel));",
            "        _gather = new ClaudeCodeGameStudios.Application.GatherService(\n"
            "            _stallService, _priceBook);   // 变异：接线被摘掉",
            selfcheck_checker,
        ))
    else:
        print("-- 跳过接线层那一档（--unit-only：Debug 程序集正被后台标定占用）")

    print("---")
    bad = results.count(False)
    print(f"变异矩阵：{len(results) - bad}/{len(results)} 符合预期")
    return 1 if bad else 0


if __name__ == "__main__":
    sys.exit(main())
