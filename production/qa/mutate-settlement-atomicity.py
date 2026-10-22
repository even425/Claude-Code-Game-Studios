"""
对 I15 / IT-13 的守卫（`SettlementAtomicityTests`）做**变异看红**验证。

这条守卫守的是**不变量 I15「结算要么完整应用，要么不应用」** ——
一次结算同时动三个地方：村庄资源 / 英雄背包 / 英雄钱包，
**要么三处一起动，要么三处都不动**。

⚠️ 它是验收计划里**最后一条**还标着「受阻」的不变量。
它曾经写不出来，是因为村庄那一侧的写入是 `Main.ApplyWrites`（**私有方法**）
⇒ 集成测试够不着 ⇒ 只能验一个**桩**。现在村庄写入被抽成
`VillageResourceWriter`（与另两个写入入口同构），这条测试才第一次跑在**真服务**上。

四档变异：

  ① 把资源写入提到**物品预检之前** ⇒ 背包装不下时村庄已经拿到材料
     ⇒ **半笔账** ⇒ 红（这正是 I15 要禁止的东西）
  ② 去掉物品预检 ⇒ 背包装不下 ⇒ 物品发放抛异常，但村庄已写入 ⇒ 红
  ③ 失败时也记幂等键 ⇒ 失败后**不可重试** ⇒ 红
  ④ 村庄资源写入不做预检就直接提交 ⇒ 扣减会扣成负数 ⇒ 红

用法：python production/qa/mutate-settlement-atomicity.py
"""
import hashlib
import os
import re
import subprocess
import sys
from pathlib import Path

try:
    import _utf8out  # noqa: F401  — 控制台默认 GBK，印 ✓/✗ 会直接崩
except ImportError:
    pass

ROOT = Path(__file__).resolve().parents[2]
TESTS = ROOT / "tests" / "Claude-Code-Game-Studios.Tests.csproj"
FILTER = "FullyQualifiedName~SettlementAtomicityTests"
SETTLEMENT = ROOT / "src" / "Domain" / "Settlement" / "Settlement.cs"
WRITER = ROOT / "src" / "Application" / "VillageResourceWriter.cs"


def sha(p: Path) -> str:
    return hashlib.sha256(p.read_bytes()).hexdigest()


def run_tests():
    r = subprocess.run(
        ["dotnet", "test", str(TESTS), "-c", "Release", "--nologo", "-v", "q",
         "--filter", FILTER],
        cwd=ROOT, capture_output=True, text=True, encoding="utf-8", errors="replace",
            env={**os.environ, "MSBUILDDISABLENODEREUSE": "1"})
    out = r.stdout + r.stderr
    for pat in (r"失败:\s*(\d+)", r"Failed:\s*(\d+)"):
        m = re.search(pat, out)
        if m:
            return int(m.group(1)), out
    return -1, out


def mutate(label, target: Path, old, new):
    """改一处 → 跑测试 → 要求红 → 复原文件。"""
    backup = target.read_bytes()
    before = sha(target)
    raw = backup.decode("utf-8")
    crlf = "\r\n" in raw
    text = raw.replace("\r\n", "\n")
    old_n = old.replace("\r\n", "\n")
    new_n = new.replace("\r\n", "\n")
    if old_n not in text:
        print(f"!! [{label}] 模式没找到")
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
        if not _restore(target, backup, before):
            print(f"!! 复原失败 [{label}]（变异可能留在源码里！）")


# ① 资源写入提到物品预检之前 ⇒ 半笔账
#
# ⚠️ **怎么改才算真的「顺序反了」**：不能只是「在原处再加一次」——
# 那只是重复调用，顺序没变，守卫可能照样绿。
# 正解：**把资源写入那一段整段挪到物品预检前面** ⇒
# 于是「背包装不下」这件事要到**村庄已经写入之后**才发现 ⇒ 半笔账。
#
# ⚠️ 源码里「物品预检(3)」与「钱包预检(3b)」与「资源写入(4)」是三段，
#    物品预检与资源写入**不连续** ⇒ 不能用「两段互换」，
#    必须**整段替换**：把「资源写入」那段换成「物品预检 + 资源写入」，
#    再把原来的「物品预检」换成空。
MUT_ITEM_PRECHECK = """        // 3. 预检物品：背包装不下 → 整笔不应用（I15）。幂等键不落，可重试。
        if (grants.Length > 0 && !_itemWriter!.CanGrant(grants))
        {
            throw new InvalidOperationException(
                $"结算 {id} 的个人物品发放不可行（背包容量不足、英雄不存在或物品未登记），"
                + "本次结算**整体未应用** —— 状态保持不变。幂等键未记录，可重试。");
        }

"""
MUT_RESOURCE_COMMIT = """        // 4. 一次性提交。写入函数自身必须保证全成功或全不成功（不变量 I15）。
        if (writes.Length > 0 && !_resourceWriter(writes))
        {
            throw new InvalidOperationException(
                $"结算 {id} 的写入被拒，本次结算**整体未应用** —— 状态保持不变。"
                + "不变量 I15：要么完整应用，要么不应用，绝不部分入账。");
        }

"""

# ② 去掉物品预检
MUT2_OLD = """        if (grants.Length > 0 && !_itemWriter!.CanGrant(grants))
        {
            throw new InvalidOperationException(
                $"结算 {id} 的个人物品发放不可行（背包容量不足、英雄不存在或物品未登记），"
                + "本次结算**整体未应用** —— 状态保持不变。幂等键未记录，可重试。");
        }
"""
MUT2_NEW = """        // 变异：去掉物品预检
"""

# ③ 失败时也记幂等键
MUT3_OLD = """        if (walletGrants.Length > 0 && !_walletWriter!.CanGrant(walletGrants))
        {
            throw new InvalidOperationException(
                $"结算 {id} 的个人钱包发放不可行（钱包容量不足、英雄不存在或金额溢出），"
                + "本次结算**整体未应用** —— 状态保持不变。幂等键未记录，可重试。");
        }
"""
MUT3_NEW = """        if (walletGrants.Length > 0 && !_walletWriter!.CanGrant(walletGrants))
        {
            _applied.Add(id);   // 变异：失败也记幂等键（失败后不可重试）
            throw new InvalidOperationException("变异：钱包预检失败");
        }
"""

# ④ 村庄资源写入不做预检
MUT4_OLD = """        // ── 预检：任何一笔不可行，整笔拒绝，此时什么都没写 ──
        foreach (var w in writes)
        {
            var id = new ResourceId(w.ResourceId);
            long current = _village.Get(id);
            long next = current + w.Amount;

            if (next < 0) return false;                                  // 会被扣成负数
            if (w.Amount > 0 && current > long.MaxValue - w.Amount) return false;  // 会溢出
        }
"""
MUT4_NEW = """        // 变异：去掉预检（直接提交 ⇒ 扣减会扣成负数）
"""



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

def main():
    results = []

    failed, out = run_tests()
    if failed != 0:
        print(f"!! 基线就是红的（failed={failed}）—— 先修好再谈变异")
        for line in out.splitlines():
            if "失败" in line or "Expecting" in line:
                print("      " + line.strip()[:160])
        return 1
    print("OK  基线（未变异）全绿 —— 判据确实能绿")

    # ── ① 资源写入提到物品预检之前 ────────────────────────────────────
    #    这一档是**最有价值**的：它造出的正是 I15 要禁止的「半笔账」——
    #    村庄拿到了材料，而英雄的背包因为装不下而没拿到石头。
    #    做法是**整段挪动**（不是「在原处再加一次」—— 那样顺序没变，守卫照样绿）。
    #    ⚠️ 两步走：① 把物品预检整段删掉；② 在资源写入那一段**前面**放回去。
    #       顺序反了 ⇒ 背包装不下这件事要到村庄已写入之后才发现。
    def mutate_swap(target: Path, label: str):
        backup = target.read_bytes()
        before = sha(target)
        raw = backup.decode("utf-8")
        crlf = "\r\n" in raw
        text = raw.replace("\r\n", "\n")
        if MUT_ITEM_PRECHECK not in text or MUT_RESOURCE_COMMIT not in text:
            print(f"!! [{label}] 模式没找到")
            return False
        # ① 删掉物品预检（它在资源写入**之前**）
        text = text.replace(MUT_ITEM_PRECHECK, "", 1)
        # ② 放回**资源写入之后** ⇒ 背包装不下这件事要到村庄已写入之后才发现
        text = text.replace(MUT_RESOURCE_COMMIT,
                            MUT_RESOURCE_COMMIT + MUT_ITEM_PRECHECK, 1)
        mutated = text
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

    results.append(mutate_swap(
        SETTLEMENT,
        "把物品预检挪到资源写入之后（背包装不下时村庄已拿到材料 ⇒ 半笔账）"))

    # ── ② 去掉物品预检 ───────────────────────────────────────────────
    results.append(mutate(
        "去掉物品预检（背包装不下 ⇒ 物品发放抛异常，但村庄已写入）",
        SETTLEMENT,
        MUT2_OLD,
        MUT2_NEW,
    ))

    # ── ③ 失败时也记幂等键 ───────────────────────────────────────────
    results.append(mutate(
        "失败时也记幂等键（这笔结算失败后不可重试 ⇒ 奖励永久丢失）",
        SETTLEMENT,
        MUT3_OLD,
        MUT3_NEW,
    ))

    # ── ④ 村庄资源写入不做预检 ───────────────────────────────────────
    results.append(mutate(
        "村庄资源写入不做预检就提交（扣减会扣成负数 ⇒ I12 也破了）",
        WRITER,
        MUT4_OLD,
        MUT4_NEW,
    ))

    print("---")
    bad = results.count(False)
    print(f"变异矩阵：{len(results) - bad}/{len(results)} 符合预期")
    return 1 if bad else 0


if __name__ == "__main__":
    sys.exit(main())
