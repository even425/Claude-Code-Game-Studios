"""
对 D-101「大事件的阶段机（一天一步）」的守卫做**变异看红**验证。

**它守的是六件事**：
  ① 逐日推进 ⇒ 实例**真的走到结算**（这是那条曾经让整个大事件系统作废的 bug）
  ② 走到结算 ⇒ **后果真的被应用**
  ③ **创建当天不推进**（预兆要待满一天，玩家得能读到预告）
  ④ 预兆满一天 ⇒ **进入下一阶段**
  ⑤ **一天一步**（不是一天走完）
  ⑥ 「预兆是否待够」这条判据**不依赖 Progress**

⚠️ **为什么这一档重要**：这个 bug 让 **兽潮 / 商队 / 盗匪来袭在真实游戏里
从来没有结算过**（200 天标定里 0 次），而**标定里「兽潮一次都没来」
被误归因为 D-082 的过滤器** ⇒ 过滤器掩盖了死锁 ⇒ 谁都没发现。

用法：python production/qa/mutate-d101-event-advance.py
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
FILTER = "FullyQualifiedName~WorldEventDailyAdvance"

BOOK = ROOT / "src" / "Domain" / "WorldEvents" / "WorldEventBook.cs"


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
    print("OK  基线（未变异）绿 —— D-101 六条当前成立")

    # ── ① 判据改回用 Progress（**回到 bug 本身**）⇒ 必须红 ──────────────
    results.append(mutate(
        "判据改回用 Progress（回到 bug 本身：死锁）",
        BOOK,
        "        double daysSinceStart = (now - instance.StartedAt) / simSecondsPerDay;\n"
        "        return daysSinceStart >= 1.0;",
        "        // 变异：改用 Progress ⇒ 与 Advance 互相锁死 ⇒ 永远停在预兆\n"
        "        return instance.Progress != 0.0;",
    ))

    # ── ② 预兆当天就推进（玩家读不到预告）⇒ 必须红 ─────────────────────
    results.append(mutate(
        "预兆当天就推进（玩家读不到预告）",
        BOOK,
        "        double daysSinceStart = (now - instance.StartedAt) / simSecondsPerDay;\n"
        "        return daysSinceStart >= 1.0;",
        "        double daysSinceStart = (now - instance.StartedAt) / simSecondsPerDay;\n"
        "        return daysSinceStart >= 0.0;   // 变异：当天就推进",
    ))

    # ── ③ 预兆判据被整体跳过（一天一步也没了）⇒ 必须红 ──────────────────
    results.append(mutate(
        "预兆判据被整体跳过（一天一步也没了）",
        BOOK,
        "        if (instance.Phase == WorldEventPhase.Omen && !OmenIsOver(instance, now, simSecondsPerDay))\n"
        "        {\n"
        "            return instance;\n"
        "        }",
        "        // 变异：预兆不拦 ⇒ 一天走完整个生命周期",
    ))

    # ── ④ 一天走两步（阶段跳过）⇒ 必须红 ────────────────────────────────
    results.append(mutate(
        "一天走两步（阶段被跳过）",
        BOOK,
        "        return Advance(instanceId, progress: 0.5);",
        "        Advance(instanceId, progress: 0.5);\n"
        "        return Advance(instanceId, progress: 0.5);   // 变异：一天两步",
    ))

    # ── ⑤ 预兆永远不算结束（实例卡在预兆）⇒ 必须红 ──────────────────────
    results.append(mutate(
        "预兆永远不算结束（实例卡在预兆）",
        BOOK,
        "        double daysSinceStart = (now - instance.StartedAt) / simSecondsPerDay;\n"
        "        return daysSinceStart >= 1.0;",
        "        double daysSinceStart = (now - instance.StartedAt) / simSecondsPerDay;\n"
        "        return daysSinceStart >= 99999.0;   // 变异：预兆永不结束",
    ))

    # ── ⑥ 结算阶段被跳过（后果永不应用）⇒ 必须红 ────────────────────────
    results.append(mutate(
        "结算阶段被跳过（后果永不应用）",
        BOOK,
        "        return Advance(instanceId, progress: 0.5);",
        "        var moved = Advance(instanceId, progress: 0.5);\n"
        "        // 变异：结算阶段被跳过 ⇒ 后果永不落地\n"
        "        if (moved is not null && moved.Phase == WorldEventPhase.Settlement)\n"
        "        {\n"
        "            moved.Phase = WorldEventPhase.Finished;\n"
        "        }\n"
        "        return moved;",
    ))

    print("---")
    bad = results.count(False)
    print(f"变异矩阵：{len(results) - bad}/{len(results)} 符合预期")
    return 1 if bad else 0


if __name__ == "__main__":
    sys.exit(main())
