"""
对 D-097「建筑在地图上长得不一样」的守卫做**变异看红**验证。

**它守的是四件事**：
  ① 每一栋可建建筑都有**专属画法**（不许落到兜底）
  ② 三种建筑**三种画法**（不许撞车）
  ③ 画法按**效果**判（不是按名字）
  ④ 兜底**不是默认**（只有「不认识」才用）

⚠️ **为什么这一档重要**：D-093 落地时，地图上画的是**一块色块**，
三种建筑**长得一模一样** ⇒ 玩家在地图上看不出自己盖了什么
⇒ 「看得见自己盖了什么」那句话**当时是假的**。

用法：python production/qa/mutate-d097-building-visual.py
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
FILTER = "FullyQualifiedName~BuildingVisualTests"

VISUAL = ROOT / "src" / "Presentation" / "BuildingVisual.cs"
PLACEMENT = ROOT / "src" / "Main.Ui.Placement.cs"


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
    print("OK  基线（未变异）绿 —— D-097 四条当前成立")

    # ── ① 防护罩没有专属画法（落到兜底）⇒ 必须红 ────────────────────────
    results.append(mutate(
        "防护罩没有专属画法（落到兜底）",
        VISUAL,
        "            BuildingEffect.Shield => Shield,",
        "            BuildingEffect.Shield => Generic,   // 变异：没有专属画法",
    ))

    # ── ② 试炼塔与围栏撞车（两种建筑长得一样）⇒ 必须红 ──────────────────
    results.append(mutate(
        "试炼塔与围栏撞车（两种建筑长得一样）",
        VISUAL,
        "            BuildingEffect.TrialTower => TrialTower,",
        "            BuildingEffect.TrialTower => Fence,   // 变异：与围栏撞车",
    ))

    # ── ③ 画法改成按名字判（改名就画错）⇒ 必须红 ────────────────────────
    results.append(mutate(
        "画法改成按名字判（改名就画错）",
        VISUAL,
        """        => building.Effect switch
        {
            BuildingEffect.Range => Fence,
            BuildingEffect.TrialTower => TrialTower,
            BuildingEffect.Shield => Shield,
            _ => Generic,
        };""",
        """        => building.Name switch
        {
            "围栏" => Fence,
            "试炼塔" => TrialTower,
            "防护罩" => Shield,
            _ => Generic,
        };""",
    ))

    # ── ④ 兜底被当成「认识的画法」⇒ 必须红 ──────────────────────────────
    results.append(mutate(
        "兜底被当成「认识的画法」",
        VISUAL,
        """    public static bool IsKnown(string shape)
        => shape == Fence || shape == TrialTower || shape == Shield;""",
        """    public static bool IsKnown(string shape)
        => shape == Fence || shape == TrialTower || shape == Shield || shape == Generic;""",
    ))

    print("---")
    bad = results.count(False)
    print(f"变异矩阵：{len(results) - bad}/{len(results)} 符合预期")
    return 1 if bad else 0


if __name__ == "__main__":
    sys.exit(main())
