"""
对 D-091「村庄向村民采购建材」的守卫做**变异看红**验证。

**它守的是五件事**：
  ① 采购时**村民拿到钱**、材料离袋、村庄出账
  ② **总量守恒**（钱没有凭空多出来）
  ③ **预算受限**（村庄的钱不够就只买得起那么多）
  ④ 升级时**优先用村民的材料**（村库材料不够也能升，且村民拿到钱）
  ⑤ **回滚**：建造没成立时货还回去、钱收回来

⚠️ **为什么这一档最要紧**：实测（中位 5~6 金）证明**闲人的支出没有出口** ——
他们在市场里买走材料，而材料毫无用途 ⇒ 钱白流出去。
⇒ 这条规则是「材料有出口」的**唯一实现** ⇒ 它一旦坏了，
「闲人的钱有去处」这件事就**悄无声息地没了**（标定里只看得到「中位没涨」）。

用法：python production/qa/mutate-d091-procurement.py
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
FILTER = "FullyQualifiedName~MaterialProcurementTests"

PROCURE = ROOT / "src" / "Application" / "MaterialProcurement.cs"
UPGRADE = ROOT / "src" / "Application" / "VillageUpgradeService.cs"


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
    print("OK  基线（未变异）绿 —— D-091 五条当前成立")

    # ── ① 村民不拿钱（白拿人家的货）⇒ 必须红 ───────────────────────────
    results.append(mutate(
        "村民不拿钱（白拿人家的货）",
        PROCURE,
        "            var newWallet = baseState.Wallet.TryAdd(_gold, pick.Qty * pick.UnitPrice);",
        "            var newWallet = baseState.Wallet;   // 变异：不付钱",
    ))

    # ── ② 村庄不出账（钱凭空多出来）⇒ 必须红 ───────────────────────────
    results.append(mutate(
        "村庄不出账（钱凭空多出来）",
        PROCURE,
        "        if (fromAsset > 0 && !_village.TrySpend(VillageUpgradeService.Asset, fromAsset)) return false;\n        if (fromCash > 0 && !_village.TrySpend(_gold, fromCash)) return false;",
        "        // 变异：村庄不出账",
    ))

    # ── ③ 预算不限（村庄会被超发）⇒ 必须红 ─────────────────────────────
    results.append(mutate(
        "预算不限（村庄会被超发）",
        PROCURE,
        "        long funds = Funds;",
        "        long funds = long.MaxValue / 4;   // 变异：预算不限",
    ))

    # ── ④ 升级不用村民的材料（材料没有出口）⇒ 必须红 ───────────────────
    results.append(mutate(
        "升级不用村民的材料（材料没有出口）",
        UPGRADE,
        "        var plan = _procurement is null || _heroes is null\n            ? ProcurementPlan.Empty\n            : _procurement.Plan(cost.Material, _heroes());",
        "        var plan = ProcurementPlan.Empty;   // 变异：不采购",
    ))

    # ── ⑤ 回滚不还货 ⇒ 必须红 ───────────────────────────────────────────
    results.append(mutate(
        "回滚不还货（白拿人家的货）",
        PROCURE,
        "            var newBag = baseState.Bag.TryAdd(pick.Item, pick.Qty);",
        "            var newBag = baseState.Bag;   // 变异：不还货",
    ))

    print("---")
    bad = results.count(False)
    print(f"变异矩阵：{len(results) - bad}/{len(results)} 符合预期")
    return 1 if bad else 0


if __name__ == "__main__":
    sys.exit(main())
