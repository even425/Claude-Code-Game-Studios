"""
对 D-098「摆摊只认范围类建筑」的守卫做**变异看红**验证。

**它守的是五件事**：
  ① **防护罩不放开摆摊范围**（它是专职防御建筑）
  ② **围栏照旧放开**（它的「范围」作用是 D-066 / D-074 已拍板的，不许被顺手改掉）
  ③ **市集照旧放开**（开局一个摊位都摆不了 ⇒ 整个游戏没得玩）
  ④ 试炼塔（影响区 0×0）哪都摆不了
  ⑤ **建筑落点合法性用同一把尺子**（不许一处改一处没改）

⚠️ **为什么这一档重要**：基线 §12.4（**开发者已确认**）说「摆摊位置受防护罩范围约束」，
D-095（**也已确认**）说「防护罩 = 专职防御」—— 两条打架 ⇒ 拍板 B（摆摊只认范围类建筑）。
拍板落地后**必须有守卫**，否则将来改代码时这条决策会悄悄消失。

用法：python production/qa/mutate-d098-stall-range.py
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
FILTER = "FullyQualifiedName~StallRangeScope"

RANGE = ROOT / "src" / "Domain" / "Village" / "RangeBuildings.cs"
STALL = ROOT / "src" / "Application" / "StallService.cs"
BUILD = ROOT / "src" / "Application" / "BuildingService.cs"


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
    print("OK  基线（未变异）绿 —— D-098 五条当前成立")

    # ── ① 防护罩又放开摆摊范围（拍板被悄悄推翻）⇒ 必须红 ────────────────
    results.append(mutate(
        "防护罩又放开摆摊范围（拍板被悄悄推翻）",
        RANGE,
        "        if (definition is null) return true;   // 目录外 ⇒ 范围类（市集 / 历史建筑）\n"
        "        return definition.Value.Effect == BuildingEffect.Range;",
        "        if (definition is null) return true;   // 变异：目录外 ⇒ 范围类\n"
        "        return true;   // 变异：防护罩也放开摆摊范围",
    ))

    # ── ② 围栏不再放开摆摊范围 ⇒ 必须红 ────────────────────────────────
    results.append(mutate(
        "围栏不再放开摆摊范围",
        RANGE,
        "        return definition.Value.Effect == BuildingEffect.Range;",
        "        return definition.Value.Effect == BuildingEffect.TrialTower;   // 变异：范围类认错",
    ))

    # ── ③ 市集被踢出范围类（开局摆不了摊）⇒ 必须红 ──────────────────────
    results.append(mutate(
        "市集被踢出范围类（开局摆不了摊）",
        RANGE,
        "        if (definition is null) return true;   // 目录外 ⇒ 范围类（市集 / 历史建筑）",
        "        if (definition is null) return false;   // 变异：市集被踢出范围类",
    ))

    # ── ④ 摆摊守门忽略「范围类」筛选 ⇒ 必须红 ────────────────────────────
    results.append(mutate(
        "摆摊守门忽略「范围类」筛选",
        STALL,
        "            if (!RangeBuildings.IsRangeBuilding(b)) continue;   // D-098：非范围类不参与",
        "            // 变异：不再筛选范围类",
    ))

    # ── ⑤ 落点合法性没跟着改（两把尺子）⇒ 必须红 ────────────────────────
    results.append(mutate(
        "落点合法性没跟着改（两把尺子）",
        BUILD,
        "            if (!RangeBuildings.IsRangeBuilding(b)) continue;   // D-098：非范围类不参与",
        "            // 变异：落点仍然认所有建筑 ⇒ 两把尺子",
    ))

    print("---")
    bad = results.count(False)
    print(f"变异矩阵：{len(results) - bad}/{len(results)} 符合预期")
    return 1 if bad else 0


if __name__ == "__main__":
    sys.exit(main())
