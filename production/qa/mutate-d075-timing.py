"""
对 D-075「副本在日历上占多久」的守卫做**变异看红**验证。

**它守的是什么**：
  ① 战斗耗时被**加进**总时长（不是只有路程）
  ② 战斗耗时与**真实回合数**对得上（D-073 的换算）
  ③ 非副本任务**没有**战斗

⚠️ **最要紧的一档**是「把战斗那半边丢掉」——
那是这类改动**最自然的退化**：有人改行程计算时只写了路程，
副本就「不占时间」了，而那**看起来完全正常**（队伍出发、回来，只是早回来了）。

用法：python production/qa/mutate-d075-timing.py
"""
try:
    import _utf8out  # noqa: F401  — 控制台默认 GBK，印特殊符号会直接崩
except ImportError:
    pass

import hashlib
import re
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
TESTS = ROOT / "tests" / "Claude-Code-Game-Studios.Tests.csproj"
FILTER = "FullyQualifiedName~ExpeditionTimingTests"

TIMING = ROOT / "src" / "Application" / "ExpeditionTiming.cs"
DURATION = ROOT / "src" / "Domain" / "Combat" / "StageDuration.cs"
DUNGEONFLOW = ROOT / "src" / "Application" / "DungeonFlow.cs"


def sha(p: Path) -> str:
    return hashlib.sha256(p.read_bytes()).hexdigest()


def run_tests():
    r = subprocess.run(
        ["dotnet", "test", str(TESTS), "-c", "Release", "--nologo", "-v", "q",
         "--filter", FILTER],
        cwd=ROOT, capture_output=True, text=True, encoding="utf-8", errors="replace")
    out = r.stdout + r.stderr
    counts = re.findall(r"(?:失败|Failed):\s*(\d+)", out)
    if counts:
        return int(counts[-1]), out
    return -1, out


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
        ok = failed > 0
        print(f"{'OK  ' if ok else '!!  '}变异 [{label}] -> "
              f"{'RED' if ok else 'GREEN'} (failed={failed}, 期望 RED)")
        if not ok:
            for line in out.splitlines():
                if "失败" in line or "Expecting" in line:
                    print("      " + line.strip()[:160])
        return ok
    finally:
        target.write_bytes(backup)
        if sha(target) != before:
            print(f"!! 复原失败 [{label}]")


def main() -> int:
    results = []

    failed, out = run_tests()
    if failed != 0:
        print(f"!! 基线就是红的（failed={failed}）—— 先修好再谈变异")
        for line in out.splitlines():
            if "失败" in line or "Expecting" in line:
                print("      " + line.strip()[:160])
        return 1
    print("OK  基线（未变异）绿 —— 那三条性质当前成立")

    # ── ① 总时长只算路程（战斗那半边被丢掉）⇒ 必须红 ──────────────────
    results.append(mutate(
        "总时长只算路程（战斗耗时被丢掉）",
        TIMING,
        "        return travelHours + battleHours;",
        "        return travelHours;   // 变异：战斗那半边没了",
    ))

    # ── ② 战斗小时数算错（每回合小时数被改）⇒ 换算断了 ⇒ 必须红 ────────
    results.append(mutate(
        "每回合小时数被改（战斗耗时算错）",
        DURATION,
        "    public const double GameHoursPerRound = 0.25;",
        "    public const double GameHoursPerRound = 0.5;   // 变异：换算变了",
    ))

    # ── ③ 副本总回合数被算成 0（战斗耗时算成 0）⇒ 必须红 ───────────────
    results.append(mutate(
        "副本总回合数被算成 0（战斗不花时间）",
        DUNGEONFLOW,
        "            roundsPerStage.Sum(),",
        "            0,   // 变异：战斗不花时间",
    ))

    print("---")
    bad = results.count(False)
    print(f"变异矩阵：{len(results) - bad}/{len(results)} 符合预期")
    return 1 if bad else 0


if __name__ == "__main__":
    sys.exit(main())
