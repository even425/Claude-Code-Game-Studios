"""
对 D-087「村庄收购站」的守卫做**变异看红**验证。

**它守的是五件事**：
  ① 钱**从村库到英雄**（村库少多少，英雄就多多少 ⇒ 总量不变）
  ② **预算 = 村库余额 − 下一级村庄升级所需金币**（村庄发展不被抽空）
  ③ **只收「采集品」**（副本掉的石料不进村 —— 否则摊位市场被挤掉、摊位税消失）
  ④ **不在村的英雄跳过**
  ⑤ **一天只收一次**（游戏循环与采集器都可能调它）

⚠️ **为什么这些变异重要**：收购站是「钱回流给闲人」的唯一通道 ——
它一旦**不扣村库的钱**（钱凭空多）或**收了不该收的东西**（市场被挤掉），
标定里的数字会变得很好看，但**游戏已经坏了** ⇒ 必须能当场抓住。

用法：python production/qa/mutate-d087-purchase.py
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
FILTER = "FullyQualifiedName~VillagePurchaseServiceTests"

SERVICE = ROOT / "src" / "Application" / "VillagePurchaseService.cs"


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
    print("OK  基线（未变异）绿 —— D-087 五条当前成立")

    # ── ① 不扣村库的钱（钱凭空多出来）⇒ 必须红 ─────────────────────────
    results.append(mutate(
        "不扣村库的钱（收购凭空造钱）",
        SERVICE,
        "        if (fromCash > 0 && !_village.TrySpend(_gold, fromCash)) return false;",
        "        // 变异：现金不出账\n        if (fromCash > 0 && false) return false;",
    ))

    # ── ② 不管是不是采集品（副本掉的石料也被收走 ⇒ 市场被挤掉）⇒ 必须红 ─
    results.append(mutate(
        "不管是不是采集品（副本掉落物也被收走）",
        SERVICE,
        "            long pending = PendingFor(hero.Id);",
        "            long pending = hero.Bag.Count(new ItemId(PurchasedItemId));",
    ))

    # ── ③ 不留升级的钱（村庄发展被抽空）⇒ 必须红 ──────────────────────
    results.append(mutate(
        "不留升级的钱（预算 = 全部余额）",
        SERVICE,
        "            return _upgrades.CostFor(tier).Gold;",
        "            return 0L;   // 变异：不留保留金",
    ))

    # ── ④ 不在村的英雄也收（副本能带回一堆东西）⇒ 必须红 ──────────────
    results.append(mutate(
        "不在村的英雄也收",
        SERVICE,
        "            if (hero.Away) continue;                       // 不在村 ⇒ 留着下次",
        "            // 变异：不在村也收",
    ))

    # ── ⑤ 同一天可以收第二轮 ⇒ 必须红 ─────────────────────────────────
    results.append(mutate(
        "同一天可以收第二轮（没有日闸门）",
        SERVICE,
        "        if (day == LastRunDay) return records;   // 同一天不跑第二轮",
        "        // 变异：没有日闸门",
    ))

    # ── ⑥ 预算不在整轮内共享（会超发）⇒ 必须红 ────────────────────────
    results.append(mutate(
        "预算不在整轮内共享（村库会被超发）",
        SERVICE,
        "            budget -= amount;",
        "            // 变异：预算不扣",
    ))

    print("---")
    bad = results.count(False)
    print(f"变异矩阵：{len(results) - bad}/{len(results)} 符合预期")
    return 1 if bad else 0


if __name__ == "__main__":
    sys.exit(main())
