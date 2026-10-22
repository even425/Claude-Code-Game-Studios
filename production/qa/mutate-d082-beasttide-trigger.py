"""
对 D-082「守不住就别触发兽潮」的守卫做**变异看红**验证。

**它守的是三件事**：
  ① 守不住 ⇒ 这次兽潮**不来**（弱村子不再被罚一笔「必输的税」）
  ② **预演与实战一致**（触发前问的那一次，和真正结算那一次，结论一样）
  ③ **预演没有副作用**（问一次不改任何状态）

⚠️ **最要紧的一档**是「过滤器被忽略」——
守不住也照常触发 ⇒ 弱村子每 7 天被扣一次材料与金币，而它**无论如何也守不住**
⇒ 回到 D-082 之前那个「必输的税」。

用法：python production/qa/mutate-d082-beasttide-trigger.py
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
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
TESTS = ROOT / "tests" / "Claude-Code-Game-Studios.Tests.csproj"
FILTER = "FullyQualifiedName~BeastTideTriggerTests"

BOOK = ROOT / "src" / "Domain" / "WorldEvents" / "WorldEventBook.cs"
BATTLE = ROOT / "src" / "Application" / "BeastTideBattle.cs"
TEMPLATES = ROOT / "src" / "Application" / "WorldEventTemplates.cs"


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
            timeout=300, env={**os.environ, "MSBUILDDISABLENODEREUSE": "1"})
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



def _restore(target, backup: bytes, expected_sha: str) -> bool:
    """把源文件复原回去（**带重试与校验**）。

    ⚠️ **为什么需要重试**：Windows 上 `dotnet test` 的 MSBuild 节点会**复用**
    （node reuse）⇒ 编译服务器在子进程退出后**仍持有源文件的句柄**
    ⇒ 直接写会报 `OSError: [Errno 22]` ⇒ **变异就留在源码里了**
    （实测：D-079 那一档就这样残留过，把幂等机制改成了摆设）。

    ⚠️ **为什么必须校验**：复原失败还接着往下跑 ⇒ 下一个脚本的**基线是红的**
    ⇒ 矩阵「失败」但不是真失败 ⇒ 比没有守卫更糟。
    ⇒ 复原不成功就**大声报错**，让这一轮明确失败。
    """
    import hashlib
    import time

    for attempt in range(3):
        try:
            target.write_bytes(backup)
        except OSError:
            time.sleep(0.4)
            continue

        if hashlib.sha256(target.read_bytes()).hexdigest() == expected_sha:
            return True
        time.sleep(0.4)

    print(f"!! 复原失败（文件被占用？）：{target}")
    return False

def main() -> int:
    results = []

    failed, out = run_tests()
    if failed != 0:
        print(f"!! 基线就是红的（failed={failed}）—— 先修好再谈变异")
        for line in out.splitlines():
            if "失败" in line or "Expecting" in line:
                print("      " + line.strip()[:160])
        return 1
    print("OK  基线（未变异）绿 —— D-082 三条当前成立")

    # ── ① 过滤器被忽略 ⇒ 守不住也照常来 ⇒ 必须红 ───────────────────────
    results.append(mutate(
        "过滤器被忽略（守不住也照常触发）",
        BOOK,
        "            if (shouldTrigger is not null && !shouldTrigger(template)) continue;",
        "            // 变异：不看过滤器",
    ))

    # ── ② 兽潮永远不触发 ⇒ 必须红 ───────────────────────────────────────
    results.append(mutate(
        "兽潮永远不触发",
        BOOK,
        "            if (shouldTrigger is not null && !shouldTrigger(template)) continue;",
        "            if (template.Id == \"beast_tide\") continue;   // 变异：兽潮不来",
    ))

    # ── ③ 预演恒说「守得住」⇒ 必须红（预演与实战不一致）────────────────
    results.append(mutate(
        "预演恒说守得住（预演与实战不一致）",
        BATTLE,
        "        => Resolve(participants, heroStats, resolver, outcomeReader).Won;",
        "        => true;   // 变异：预演不真打",
    ))

    # ── ④ 结算时无视战斗结果（永远算赢）⇒ 必须红 ───────────────────────
    results.append(mutate(
        "结算时永远算赢（无视战斗结果）",
        TEMPLATES,
        "                    return new WorldEventOutcome(battle.Won, consequences);",
        "                    return new WorldEventOutcome(true, consequences);   // 变异：永远赢",
    ))

    # ── ⑤ 没人守也算守得住 ⇒ 必须红 ────────────────────────────────────
    results.append(mutate(
        "没人守也算守得住",
        BATTLE,
        "            return new BeastTideBattleResult(false, 0, Array.Empty<string>());",
        "            return new BeastTideBattleResult(true, 0, Array.Empty<string>());   // 变异",
    ))

    print("---")
    bad = results.count(False)
    print(f"变异矩阵：{len(results) - bad}/{len(results)} 符合预期")
    return 1 if bad else 0


if __name__ == "__main__":
    sys.exit(main())
