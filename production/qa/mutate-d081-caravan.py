"""
对 D-081「商队临时摊位」的守卫做**变异看红**验证。

**它守的是开发者拍板的四条**：
  ① 商队摆摊卖货，**价格低于市价**、**限时**、**限量**
  ② 英雄用个人金币买 ⇒ **钱进村金库**（守恒律）
  ③ 买超量 / 钱不够 ⇒ **状态完全不变**
  ④ 读档后**限量的账还在**（不读回来就能白嫖）

⚠️ **最要紧的一档**是「不扣库存」——
商队的货是**限量**的（限量是「不产生无限套利」的前提）⇒
不扣库存 ⇒ 玩家能无限买 ⇒ 低价买入、按市价卖出 ⇒ **金币无限**。

用法：python production/qa/mutate-d081-caravan.py
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
FILTER = "FullyQualifiedName~CaravanMarketTests"

SERVICE = ROOT / "src" / "Application" / "CaravanMarketService.cs"


def sha(p: Path) -> str:
    return hashlib.sha256(p.read_bytes()).hexdigest()


def run_tests():
    # ⚠️ **必须带构建**（不要加 `--no-build`）：实测在本机 `--no-build` 会让
    # vstest 挂死（进程在、CPU 不涨），而带构建的同一条命令只要几秒。
    # ⇒ 变异改的是源码 ⇒ 每次都必须重新编译进程序集。
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
    print("OK  基线（未变异）绿 —— D-081 四条当前成立")

    # ── ① 不扣库存 ⇒ 限量失效 ⇒ 必须红 ─────────────────────────────────
    results.append(mutate(
        "不扣库存（限量失效 ⇒ 可无限买）",
        SERVICE,
        "        instance.SetState(StateStock, remaining.ToString(CultureInfo.InvariantCulture));",
        "        // 变异：不扣库存",
    ))

    # ── ② 不锁单价（买时按现价算）⇒ 必须红 ─────────────────────────────
    results.append(mutate(
        "不锁单价（买时按现价算）",
        SERVICE,
        "        if (!TryMultiply(view.Value.UnitPrice, qty, out long totalAmount))",
        "        if (!TryMultiply(_prices.PriceOf(new ItemId(view.Value.ItemId)), qty, out long totalAmount))",
    ))

    # ── ③ 过期也能买（不看阶段）⇒ 必须红 ───────────────────────────────
    results.append(mutate(
        "过期也能买（不看阶段）",
        SERVICE,
        """        if (instance.Phase != WorldEventPhase.Ongoing)
        {
            return Fail(CaravanBuyOutcome.WindowClosed, buyer);
        }""",
        "        // 变异：不看阶段",
    ))

    # ── ④ 幂等失效（不记请求键）⇒ 必须红 ───────────────────────────────
    results.append(mutate(
        "幂等失效（不记请求键 ⇒ 同一单会成交两次）",
        SERVICE,
        "        if (useRequestKey) instance.MarkApplied(buyKey);",
        "        // 变异：不记幂等键",
    ))

    # ── ⑤ 钱没进村库 ⇒ 守恒律破了 ⇒ 必须红 ────────────────────────────
    results.append(mutate(
        "钱没进村库（钱凭空消失）",
        SERVICE,
        "        _village.Add(Stall.StallService.Gold, totalAmount);",
        "        // 变异：钱没进村库",
    ))

    print("---")
    bad = results.count(False)
    print(f"变异矩阵：{len(results) - bad}/{len(results)} 符合预期")
    return 1 if bad else 0


if __name__ == "__main__":
    sys.exit(main())
