"""
对 D-095「防护罩」的守卫做**变异看红**验证。

**它守的是四件事**：
  ① 防护罩**建成** ⇒ 兽潮规模等级 −1
  ② **在建**的不算（罩子没搭好挡不住）
  ③ 与围栏**可叠加**（围栏 = 范围、防护罩 = 防御，职责不同）
  ④ 它**在建造目录里**、玩家能建

⚠️ **为什么这一档重要**：防护罩是基线 §12.1 里**唯一带明确功能**的新建筑
（"保护村庄不被外面攻击"）⇒ 若它的规则其实没生效，基线 §12.5 的 B1 验收
（"建筑功能符合已确认定义"）就是**假绿**。

用法：python production/qa/mutate-d095-shield.py
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
FILTER = "FullyQualifiedName~ShieldDefenseTests"

SCALE = ROOT / "src" / "Application" / "BeastTideScale.cs"
BUILDINGS = ROOT / "src" / "Domain" / "Village" / "BuildableBuilding.cs"


def sha(p: Path) -> str:
    return hashlib.sha256(p.read_bytes()).hexdigest()


def run_tests():
    # ⚠️ **必须带构建**（不要加 `--no-build`）：实测在本机 `--no-build` 会让
    # vstest 挂死（进程在、CPU 不涨），而带构建的同一条命令只要几秒。
    # ⇒ `MSBUILDDISABLENODEREUSE=1`：禁用 MSBuild 节点复用 ⇒ 编译服务器不留文件句柄
    #   （否则下一步的「复原」会失败 ⇒ **变异残留在源码里**）。
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
    """清掉挂死的测试宿主（否则它会一直占着 CPU 与文件锁）。"""
    subprocess.run(["taskkill", "/F", "/IM", "testhost.exe"], capture_output=True, text=True)
    subprocess.run(["taskkill", "/F", "/IM", "dotnet.exe"], capture_output=True, text=True)


def _restore(target, backup: bytes, expected_sha: str) -> bool:
    """把源文件复原回去（**带重试与校验**）。"""
    for attempt in range(3):
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
    print("OK  基线（未变异）绿 —— D-095 四条当前成立")

    # ── ① 防护罩不降兽潮等级（功能其实没生效）⇒ 必须红 ─────────────────
    results.append(mutate(
        "防护罩不降兽潮等级（功能没生效）",
        SCALE,
        "        int tier = villageTier - completedFences - completedShields;",
        "        int tier = villageTier - completedFences;   // 变异：忽略防护罩",
    ))

    # ── ② 在建的防护罩也算（半成品罩子也挡）⇒ 必须红 ───────────────────
    results.append(mutate(
        "在建的防护罩也算（半成品罩子也挡）",
        SCALE,
        "            if (b.ConstructionProgress >= 1.0) count++;\n        }\n\n        return count;\n    }",
        "            count++;   // 变异：不管建没建好\n        }\n\n        return count;\n    }",
    ))

    # ── ③ 防护罩不进建造目录（玩家建不了）⇒ 必须红 ─────────────────────
    results.append(mutate(
        "防护罩不进建造目录（玩家建不了）",
        BUILDINGS,
        "        new[] { Fence, TrialTower, Shield };",
        "        new[] { Fence, TrialTower };   // 变异：目录里没有防护罩",
    ))

    # ── ④ 围栏与防护罩不叠加 ⇒ 必须红 ─────────────────────────────────
    results.append(mutate(
        "围栏与防护罩不叠加（只算一种防御）",
        SCALE,
        "        int tier = villageTier - completedFences - completedShields;",
        "        int tier = villageTier - Math.Max(completedFences, completedShields);   // 变异：不叠加",
    ))

    print("---")
    bad = results.count(False)
    print(f"变异矩阵：{len(results) - bad}/{len(results)} 符合预期")
    return 1 if bad else 0


if __name__ == "__main__":
    sys.exit(main())
