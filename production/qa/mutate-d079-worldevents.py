"""
对 D-079「世界与大事件」的守卫做**变异看红**验证。

**它守的是基线 §11.6 的 V1–V4**：
  ① V1 事件按模板走完生命周期
  ② V2 **后果不被重复应用**（结算后存档、读档，再结算也不重发）
  ③ V3 事件可预告（预兆在「进行中」之前）
  ④ V4 事件不越权改数据

⚠️ **最要紧的一档**是「幂等键不生效」——
若 `MarkApplied` 总返回 true（永远当第一次），后果会被**反复应用**
⇒ 存档读档后重放 ⇒ 村庄被重复扣血/加钱 ⇒ 必须红。

用法：python production/qa/mutate-d079-worldevents.py
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
FILTER = "FullyQualifiedName~WorldEventTests"

BOOK = ROOT / "src" / "Domain" / "WorldEvents" / "WorldEventBook.cs"
INSTANCE = ROOT / "src" / "Domain" / "WorldEvents" / "WorldEventTemplate.cs"
TEMPLATES = ROOT / "src" / "Application" / "WorldEventTemplates.cs"


def sha(p: Path) -> str:
    return hashlib.sha256(p.read_bytes()).hexdigest()


def run_tests():
    # ⚠️ **必须带构建**（不要加 `--no-build`）：
    # 实测在本机 `--no-build` 会让 vstest **挂死**（进程在、CPU 不涨、永远不出结果），
    # 而带构建的同一条命令只要 4 秒。⇒ 变异改的是源码 ⇒ 每次都必须重新编译进程序集。
    r = None
    try:
        r = subprocess.run(
            ["dotnet", "test", str(TESTS), "-c", "Release", "--nologo", "-v", "q",
             "--filter", FILTER],
            cwd=ROOT, capture_output=True, text=True, encoding="utf-8", errors="replace",
            timeout=300, env={**os.environ, "MSBUILDDISABLENODEREUSE": "1"})
    except subprocess.TimeoutExpired:
        # ⚠️ **挂死不是「跑红」**：守卫的循环被改坏时会永远等下去
        # ⇒ 这一档必须**明确报出来**（TIMEOUT），不能当成「没红 ⇒ 守卫生效」，
        # 更不能无限等下去。
        _kill_dotnet()
        return -2, "TIMEOUT: 测试超过 300 秒未结束（守卫挂死，不是跑红）"
    out = r.stdout + r.stderr
    counts = re.findall(r"(?:失败|Failed):\s*(\d+)", out)
    if counts:
        return int(counts[-1]), out
    return -1, out


def _kill_dotnet():
    """清掉挂死的测试宿主（否则它会一直占着 CPU 与文件锁）。"""
    subprocess.run(["taskkill", "/F", "/IM", "testhost.exe"],
                   capture_output=True, text=True)
    subprocess.run(["taskkill", "/F", "/IM", "dotnet.exe"],
                   capture_output=True, text=True)


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
    print("OK  基线（未变异）绿 —— V1–V4 当前成立")

    # ── ① 幂等键失效（总当第一次应用）⇒ 后果会被反复应用 ⇒ 必须红 ──────
    results.append(mutate(
        "幂等键失效（后果会被反复应用）",
        INSTANCE,
        "        return _appliedConsequences.Add(key);",
        "        _appliedConsequences.Add(key);   // 变异：总当第一次\n"
        "        return true;",
    ))

    # ── ② 幂等键不记录（每次都是新集合）⇒ 读档后重放会重发 ⇒ 必须红 ──
    results.append(mutate(
        "已应用的后果不记录（读档后重放会重发）",
        INSTANCE,
        "        return _appliedConsequences.Add(key);",
        "        return true;   // 变异：什么都不记",
    ))

    # ── ③ 阶段推进跳到「结束」⇒ V1 的生命周期断言必须红 ───────────────
    results.append(mutate(
        "阶段推进直接跳到结束（生命周期被跳过）",
        BOOK,
        "        if (index + 1 < template.Phases.Count)\n"
        "        {\n"
        "            instance.Phase = template.Phases[index + 1];\n"
        "        }\n"
        "        else\n"
        "        {\n"
        "            instance.Phase = WorldEventPhase.Finished;\n"
        "        }",
        "        // 变异：一步跳到结束\n"
        "        instance.Phase = WorldEventPhase.Finished;",
    ))

    # ── ④ 结算不看阶段（任何阶段都能结算）⇒ 必须红 ─────────────────────
    results.append(mutate(
        "结算不看阶段（任何阶段都能结算）",
        BOOK,
        "        if (instance.Phase != WorldEventPhase.Settlement) return null;",
        "        // 变异：不看阶段",
    ))

    # ── ⑤ 触发不看周期（每天都触发）⇒ 必须红 ─────────────────────────
    results.append(mutate(
        "触发不看周期（每天都触发）",
        BOOK,
        "            if (simDay % template.PeriodDays != 0) continue;",
        "            // 变异：不看周期",
    ))

    print("---")
    bad = results.count(False)
    print(f"变异矩阵：{len(results) - bad}/{len(results)} 符合预期")
    return 1 if bad else 0


if __name__ == "__main__":
    sys.exit(main())
