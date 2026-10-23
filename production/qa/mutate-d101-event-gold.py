"""
对 D-101「大事件的外部收入 / 外部支出」记账的守卫做**变异看红**验证。

**它守的是四件事**：
  ① 大事件**给**村库金币 ⇒ 记进「外部收入」（守恒律的右边）
  ② 大事件**抢走**村库金币 ⇒ 记进「外部支出」（守恒律的左边）
  ③ **材料不算**（只有金币进那条等式）
  ④ **守恒律等式成立**（把两项接进等式 ⇒ 不是「记了个数但等式里没有」）

⚠️ **为什么这一档重要**：D-101 之前大事件**从未真的结算过**（阶段机死锁）
⇒ 这一项**永远是 0** ⇒ 守恒律看着一直是对的
⇒ **一条从未被触发的规则，看起来是「通过」的**。

用法：python production/qa/mutate-d101-event-gold.py
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
FILTER = "FullyQualifiedName~EventGoldAccounting"

APPLIER = ROOT / "src" / "Application" / "WorldEventResourceApplier.cs"


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
    print("OK  基线（未变异）绿 —— D-101 四条当前成立")

    # ── ① 给钱不记外部收入（钱凭空进村库 ⇒ 守恒律破）⇒ 必须红 ──────────
    results.append(mutate(
        "给钱不记外部收入（钱凭空进村库）",
        APPLIER,
        "            if (IsGold(resource)) TotalEventGoldIn += effect.Amount;",
        "            // 变异：不记外部收入 ⇒ 钱凭空进村库",
    ))

    # ── ② 抢钱不记外部支出（钱凭空消失 ⇒ 守恒律破）⇒ 必须红 ────────────
    results.append(mutate(
        "抢钱不记外部支出（钱凭空消失）",
        APPLIER,
        "            if (IsGold(resource) && actual > 0) TotalEventGoldOut += actual;",
        "            // 变异：不记外部支出 ⇒ 钱凭空消失",
    ))

    # ── ③ 材料也记进金币科目 ⇒ 必须红 ──────────────────────────────────
    results.append(mutate(
        "材料也记进金币科目",
        APPLIER,
        "    private static bool IsGold(ResourceId resource)\n"
        "        => resource.Value == \"金币\";",
        "    private static bool IsGold(ResourceId resource)\n"
        "        => true;   // 变异：材料也算金币",
    ))

    # ── ④ 收入与支出混成一个净值（看不出净收支）⇒ 必须红 ─────────────────
    results.append(mutate(
        "收入与支出混成一个净值（看不出净收支）",
        APPLIER,
        "            if (IsGold(resource) && actual > 0) TotalEventGoldOut += actual;",
        "            if (IsGold(resource) && actual > 0) TotalEventGoldIn -= actual;   // 变异：混成一个净值",
    ))

    print("---")
    bad = results.count(False)
    print(f"变异矩阵：{len(results) - bad}/{len(results)} 符合预期")
    return 1 if bad else 0


if __name__ == "__main__":
    sys.exit(main())
