"""
对 D-086「采集与自动卖货」的守卫做**变异看红**验证。

**它守的是五件事**：
  ① 采集的材料进**英雄自己的背包**（⇒ 能卖 ⇒ 才是收入）
  ② **背包满了就白采**（用**决策层那把尺子** = 5 份/种，不是领域层的 Capacity=0）
  ③ 有货就**自动开摊**
  ④ **一人一摊**（D-023）
  ⑤ **今天卖哪一样会轮换**（不然整个市场只剩一种商品）

⚠️ **最要紧的一档**是「材料不进背包」——
那等于采集**对个人毫无意义** ⇒ 没参与副本的人依然一分钱没有
⇒ 回到「钱集中在 8 个人身上」那个问题。

用法：python production/qa/mutate-d086-gather.py
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
FILTER = "FullyQualifiedName~GatherServiceTests"

SERVICE = ROOT / "src" / "Application" / "GatherService.cs"


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
    print("OK  基线（未变异）绿 —— D-086 五条当前成立")

    # ── ① 采集的材料不进背包（个人没有收入）⇒ 必须红 ───────────────────
    results.append(mutate(
        "采集的材料不进背包（个人依然没收入）",
        SERVICE,
        "        hero.ApplyTradeResult(hero.Wallet, bag);\n        return GatheredAmountPerDay;",
        "        // 变异：材料不进背包\n        return GatheredAmountPerDay;",
    ))

    # ── ② 背包上限不检查（无限采集）⇒ 必须红 ───────────────────────────
    results.append(mutate(
        "背包上限不检查（无限采集）",
        SERVICE,
        "        if (hero.Bag.Count(item) >= Stall.HeroBehaviorService.BagItemLimit) return 0L;",
        "        // 变异：不限背包",
    ))

    # ── ③ 不自动开摊（材料烂在背包里）⇒ 必须红 ─────────────────────────
    results.append(mutate(
        "不自动开摊（材料烂在背包里）",
        SERVICE,
        "        return _stalls.PlaceStall(hero.Id, chosen, qty, simTime) is not null;",
        "        // 变异：不开摊\n        return false;",
    ))

    # ── ④ 采集的份数不是 1 ⇒ 必须红 ──────────────────────────────────
    results.append(mutate(
        "采集的份数不是 1（一次采一堆）",
        SERVICE,
        "    public const long GatheredAmountPerDay = 1;",
        "    public const long GatheredAmountPerDay = 10;   // 变异：一次采 10 份",
    ))

    # ── ⑤ 不轮换（永远卖第一样）⇒ 必须红 ──────────────────────────────
    results.append(mutate(
        "不轮换（永远卖第一样 ⇒ 市场只剩一种商品）",
        SERVICE,
        "        var chosen = sellable[(day + heroIndex) % sellable.Count];",
        "        var chosen = sellable[0];   // 变异：不轮换",
    ))

    print("---")
    bad = results.count(False)
    print(f"变异矩阵：{len(results) - bad}/{len(results)} 符合预期")
    return 1 if bad else 0


if __name__ == "__main__":
    sys.exit(main())
