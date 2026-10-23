"""
对 D-104「试炼塔的永久生命加成必须入档」的守卫做**变异看红**验证。

**它守的是四件事**：
  ① 写盘方向：英雄身上的永久加成**真的被写进存档**（`Export`）
  ② 读盘方向：存档里的永久加成**真的被还回英雄**（`Import`）
  ③ 存档层：负数加成**整份拒绝**（加成只增不减 ⇒ 负数只可能是存档被改坏）
  ④ 版本：0,10 的存档格式**必须**与 0,9 不同（否则老档会被当成新档读进来）

⚠️ **为什么这一档重要**：这是一个**真 bug**（D-074 拍板的是「**永久**」加成，
而它**从来没进过存档** ⇒ 读档后玩家的强度**凭空蒸发**，而且游戏**不报任何错**）。
更要紧的是：它当初就住在 `Main.Save.cs` 里 ⇒ **单测碰不到** ⇒ 没人验过
⇒ 所以这次把转换抽成了纯函数 `TrialTowerBonusPersistence`（生产代码与守卫调同一份），
并且额外用**真机自检**（`SAVETEST 0.10 PASS`）守住 `Main.Save.cs` 那两行接线。

⚠️ **两类变异都要跑**：
  - ① ~ ④ 是**单元层**的（`dotnet test`）
  - ⑤ ~ ⑥ 是**接线层**的（`Main.Save.cs` 里那两行）⇒ **只有真机自检抓得到**
    （单元测试碰不到 `Main`）⇒ 这两档是本脚本的重点。

用法：python production/qa/mutate-d104-trial-bonus.py
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
FILTER = "FullyQualifiedName~TrialTowerBonusPersistenceTests|FullyQualifiedName~SaveVersion010Tests"

PERSIST = ROOT / "src" / "Application" / "TrialTowerBonusPersistence.cs"
SAVE_GAME = ROOT / "src" / "data" / "SaveGame.cs"
MAIN_SAVE = ROOT / "src" / "Main.Save.cs"

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


def run_save_selfcheck():
    """跑真机自检 `--save`（headless）。

    返回 (是否出现 "SAVETEST 0.10 PASS", 全部输出)。
    ⚠️ headless 加载的是 **Debug** 的程序集 ⇒ 变异后必须重新构建 Debug。
    """
    build = subprocess.run(["dotnet", "build", "--nologo", "-v", "q"],
                           cwd=ROOT, capture_output=True, text=True,
                           encoding="utf-8", errors="replace", timeout=300)
    if build.returncode != 0:
        return False, "BUILD FAILED:\n" + (build.stdout or "")[-1500:]

    tmp_save = Path(tempfile.gettempdir()) / "d104-mutate-save.json"
    argv = [find_godot(), "--headless", "--path", str(ROOT), "--quit-after", FRAMES, "--",
            "--save", f"--savepath={tmp_save}", f"--seed={SEED}"]
    try:
        proc = subprocess.run(argv, cwd=ROOT, capture_output=True, text=True,
                              encoding="utf-8", errors="replace", timeout=300)
    except subprocess.TimeoutExpired:
        return False, "TIMEOUT: 自检 300 秒未结束"
    out = (proc.stdout or "") + (proc.stderr or "")
    return "SAVETEST 0.10 PASS" in out, out


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
                if "失败" in line or "Expecting" in line or "SAVETEST" in line:
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
    passed, out = run_save_selfcheck()
    # 红 = 「0.10 PASS」不见了（要么 FAIL，要么根本没跑到）
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
    print("OK  基线（未变异）绿 —— D-104 四条当前成立")

    ok, detail = run_save_selfcheck()
    if not ok:
        print("!! 基线自检就不绿（SAVETEST 0.10 PASS 没出现）—— 先修好再谈变异")
        for line in str(detail).splitlines():
            if "SAVETEST" in line:
                print("      " + line.strip()[:160])
        return 1
    print("OK  基线自检绿 —— 永久加成确实读回来了")

    # ── ① 写盘方向：Export 永远返回 0 ⇒ 必须红 ──────────────────────────
    results.append(mutate(
        "Export 永远返回 0（写出去的加成全是 0）",
        PERSIST,
        "        return agent.TrialTowerHealthBonus;",
        "        return 0;   // 变异：写出去的加成永远是 0",
        unit_checker,
    ))

    # ── ② 读盘方向：Import 不写回 ⇒ 必须红 ──────────────────────────────
    results.append(mutate(
        "Import 不写回（读档后加成归零 —— 回到 bug 本身）",
        PERSIST,
        "        agent.TrialTowerHealthBonus = save.TrialTowerHealthBonus;",
        "        _ = save;   // 变异：什么都不还（永久加成读档后归零）",
        unit_checker,
    ))

    # ── ③ 存档层：去掉「负数整份拒绝」⇒ 必须红 ─────────────────────────
    results.append(mutate(
        "校验器不再拒绝负数加成",
        SAVE_GAME,
        "            if (h.TrialTowerHealthBonus < 0)",
        "            if (false && h.TrialTowerHealthBonus < 0)   // 变异：不再守",
        unit_checker,
    ))

    # ── ④ 版本：把当前版本改回 0,9 ⇒ 必须红 ────────────────────────────
    results.append(mutate(
        "当前版本改回 0,9（老档会被当成新档读进来）",
        SAVE_GAME,
        "    public static SaveVersion Current => new(0, 10);",
        "    public static SaveVersion Current => new(0, 9);   // 变异：版本没升",
        unit_checker,
    ))

    # ── ⑤ 接线层：`Main.Save.cs` 的写盘那行被摘掉 ⇒ 自检必须红 ──────────
    #    （单元测试碰不到 Main ⇒ 这一档只有真机自检抓得到）
    results.append(mutate(
        "接线层：Main.Save.cs 写盘那行不取加成",
        MAIN_SAVE,
        "                TrialTowerHealthBonus = ClaudeCodeGameStudios.Application.TrialTowerBonusPersistence.Export(h),",
        "                TrialTowerHealthBonus = 0,   // 变异：写盘那行被摘掉",
        selfcheck_checker,
    ))

    # ── ⑥ 接线层：`Main.Save.cs` 的读档那行被摘掉 ⇒ 自检必须红 ──────────
    results.append(mutate(
        "接线层：Main.Save.cs 读档那行不恢复加成",
        MAIN_SAVE,
        "            ClaudeCodeGameStudios.Application.TrialTowerBonusPersistence.Import(hs, agent);",
        "            // 变异：读档那行被摘掉（永久加成读档后归零）",
        selfcheck_checker,
    ))

    print("---")
    bad = results.count(False)
    print(f"变异矩阵：{len(results) - bad}/{len(results)} 符合预期")
    return 1 if bad else 0


if __name__ == "__main__":
    sys.exit(main())
