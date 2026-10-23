"""
对 D-113「试炼塔每天限一次」的守卫做**变异看红**验证。

**它守的是五件事**：
  ① 服务层：同一天第二次 ⇒ 不打（跳过原因 = 已试过）
  ② 协调器：试过一次 ⇒ 记下「今天试过了」（赢了记、输了也记）
  ③ 存档：LastTrialDay 入档（不存它 ⇒ 读档后那条限制就白设了）
  ④ 校验器：负数的 LastTrialDay 整份拒绝
  ⑤ 接线层：界面那条路**真的把日号传下去**（只有真机自检抓得到）

⚠️ **为什么 ③ 要单独守**：D-104 刚修过同一个家族的缺陷（「永久加成不入档」
   ⇒ 读档后强度凭空蒸发）⇒ 这一条是它的**同款复发**，必须钉住。

用法：python production/qa/mutate-d113-trial-limit.py
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
FILTER = ("FullyQualifiedName~TrialTowerDailyLimitTests"
          "|FullyQualifiedName~SaveVersion011Tests"
          "|FullyQualifiedName~BuildingPurchaseAtomicityTests")

SERVICE = ROOT / "src" / "Application" / "TrialTowerService.cs"
COORDINATOR = ROOT / "src" / "Application" / "BuildingCoordinator.cs"
MAIN_SAVE = ROOT / "src" / "Main.Save.cs"
SAVE_GAME = ROOT / "src" / "data" / "SaveGame.cs"

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


def run_selfcheck(tag: str, marker: str):
    """跑一个真机自检（headless）并看它有没有打出那个标记。"""
    build = subprocess.run(["dotnet", "build", "--nologo", "-v", "q"],
                           cwd=ROOT, capture_output=True, text=True,
                           encoding="utf-8", errors="replace", timeout=300)
    if build.returncode != 0:
        return False, "BUILD FAILED:\n" + (build.stdout or "")[-1500:]

    tmp_save = Path(tempfile.gettempdir()) / "d113-mutate-save.json"
    argv = [find_godot(), "--headless", "--path", str(ROOT), "--quit-after", FRAMES, "--",
            tag, f"--savepath={tmp_save}", f"--seed={SEED}"]
    try:
        proc = subprocess.run(argv, cwd=ROOT, capture_output=True, text=True,
                              encoding="utf-8", errors="replace", timeout=300)
    except subprocess.TimeoutExpired:
        return False, "TIMEOUT: 自检 300 秒未结束"
    out = (proc.stdout or "") + (proc.stderr or "")
    return marker in out, out


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
                if "失败" in line or "Expecting" in line or "BUILDTEST" in line:
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
    passed, out = run_selfcheck("--build", "BUILDTEST D-113 试炼塔每天限一次 ⇒ PASS")
    return not passed, out


def save_selfcheck_checker():
    passed, out = run_selfcheck("--save", "SAVETEST 0.11 PASS")
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
    print("OK  基线（未变异）绿 —— D-113 五条当前成立")

    # ── ① 服务层的「每天一次」判定去掉 ⇒ 必须红 ────────────────────────────
    results.append(mutate(
        "服务层不再拦「今天已经试过」（无限刷回来）",
        SERVICE,
        "        if (todayDay > 0 && lastTrialDay == todayDay)",
        "        if (false)   // 变异：不再拦",
        unit_checker,
    ))

    # ── ② 协调器不记「今天试过了」⇒ 必须红 ───────────────────────────────
    results.append(mutate(
        "协调器不记「今天试过了」（记了也没用）",
        COORDINATOR,
        "        if (todayDay > 0) agent.LastTrialDay = todayDay;",
        "        // 变异：不记日号",
        unit_checker,
    ))

    # ── ③ 存档不写 LastTrialDay ⇒ 必须红（**真机自检**抓：它在 Main.Save.cs 里）──
    results.append(mutate(
        "存档不写 LastTrialDay（读档后那条限制就白设了）",
        MAIN_SAVE,
        "LastTrialDay = h.LastTrialDay,",
        "LastTrialDay = 0,   // 变异：不入档",
        save_selfcheck_checker,
    ))

    # ── ④ 校验器不拒绝负数 ⇒ 必须红 ───────────────────────────────────────
    results.append(mutate(
        "校验器不再拒绝负数的 LastTrialDay",
        SAVE_GAME,
        "            if (h.LastTrialDay < 0)",
        "            if (false && h.LastTrialDay < 0)   // 变异：不再守",
        unit_checker,
    ))

    # ── ⑤ 接线层：界面那条路不传日号 ⇒ 真机自检必须红 ────────────────────
    results.append(mutate(
        "接线层：界面不传日号（限制对玩家失效）",
        ROOT / "src" / "Main.Ui.Trial.cs",
        "TryTrial(challenger.Id, (int)_clock.Day)",
        "TryTrial(challenger.Id)   // 变异：不传日号",
        selfcheck_checker,
    ))

    print("---")
    bad = results.count(False)
    print(f"变异矩阵：{len(results) - bad}/{len(results)} 符合预期")
    return 1 if bad else 0


if __name__ == "__main__":
    sys.exit(main())
