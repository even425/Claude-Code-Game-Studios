"""
对 D-090「村庄资产：升级花掉的钱不再销毁」的守卫做**变异看红**验证。

**它守的是四件事**：
  ① 升级时**钱没消失**（村库金币 −成本，村庄资产 +成本）
  ② 收购时**资产变少、英雄的钱变多**（资产是收购站的燃料）
  ③ **守恒律**：Σ英雄 + 村库金币 + 村庄资产 + 累计销毁 = 初始 + 累计发放
  ④ **老档兼容**：旧档没有「村庄资产」⇒ 读档时补记（否则钱凭空消失）

⚠️ **为什么这一档最要紧**：D-087 的收购站实测**几乎没开张**（200 天 2~5 笔），
根因就是「升级与收购抢同一笔钱」⇒ D-090 把升级沉淀变成**可再花的资产**。
若这一层记账错了 ⇒ 守恒律要么假绿、要么假红 ⇒ **标定数据不可信**。

用法：python production/qa/mutate-d090-village-asset.py
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
FILTER = "FullyQualifiedName~VillageAssetTests"

UPGRADE = ROOT / "src" / "Application" / "VillageUpgradeService.cs"
PURCHASE = ROOT / "src" / "Application" / "VillagePurchaseService.cs"
SAVE = ROOT / "src" / "data" / "SaveGame.cs"


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
    print("OK  基线（未变异）绿 —— D-090 四条当前成立")

    # ── ① 升级不记资产（钱凭空消失 ⇒ 守恒律破）⇒ 必须红 ───────────────
    results.append(mutate(
        "升级不记资产（钱凭空消失）",
        UPGRADE,
        "        _village.Add(Asset, cost.Gold);",
        "        // 变异：升级不记资产",
    ))

    # ── ② 收购不扣资产（钱凭空多出来）⇒ 必须红 ─────────────────────────
    results.append(mutate(
        "收购不扣资产（钱凭空多出来）",
        PURCHASE,
        "        if (fromAsset > 0 && !_village.TrySpend(Asset, fromAsset)) return false;",
        "        // 变异：不扣资产",
    ))

    # ── ③ 收购预算不含资产（收购站没有燃料）⇒ 必须红 ───────────────────
    results.append(mutate(
        "收购预算不含资产（收购站没有燃料）",
        PURCHASE,
        "    public long Budget => _village.Get(Asset) + CashRoom;",
        "    public long Budget => CashRoom;   // 变异：预算不含资产",
    ))

    # ── ④ 收购可以动用升级的保留金（村庄发展被抽空）⇒ 必须红 ──────────
    results.append(mutate(
        "收购可以动用升级的保留金（村庄发展被抽空）",
        PURCHASE,
        "    public long CashRoom => Math.Max(0L, _village.Get(_gold) - Reserve);",
        "    public long CashRoom => _village.Get(_gold);   // 变异：不留保留金",
    ))

    # ── ⑤ 老档不补记资产（读档后钱凭空消失）⇒ 必须红 ─────────────────
    results.append(mutate(
        "老档不补记资产（读档后钱凭空消失）",
        SAVE,
        "        if (save.Village.Resources.ContainsKey(assetKey)) return 0L;   // 新档：已有 ⇒ 不补\n        return save.GoldSpentTotal > 0 ? save.GoldSpentTotal : 0L;",
        "        return 0L;   // 变异：老档也不补",
    ))

    print("---")
    bad = results.count(False)
    print(f"变异矩阵：{len(results) - bad}/{len(results)} 符合预期")
    return 1 if bad else 0


if __name__ == "__main__":
    sys.exit(main())
