"""
对 D-076「掉落权重」的守卫做**变异看红**验证。

**它守的是什么**：
  ① 玩家实际拿到的**份数比例** = 50 / 30 / 20
  ② 稀有度：布匹是唯一的「优秀」
  ③ 抽 3 次 ⇒ 总份数 3..6
  ④ 单次抽中的份数：石头 1-2 份，木料/布匹 1 份

⚠️ **最要紧的一档**是「把权重填成 50/30/20」——
那是**看起来最合理**的填法（拍板时说的就是 50/30/20），
但因为石头每次给 1-2 份，它会让玩家实际拿到 60/24/16 ⇒ 石头占六成。
⇒ 这一档**必须红**，否则守卫根本没在守「份数比例」。

用法：python production/qa/mutate-d076-loot.py
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
FILTER = "FullyQualifiedName~LootWeightTests"

PROVIDER = ROOT / "src" / "Application" / "DungeonLootProvider.cs"
CATALOG = ROOT / "src" / "Domain" / "Items" / "ItemCatalog.cs"


def sha(p: Path) -> str:
    return hashlib.sha256(p.read_bytes()).hexdigest()


def run_tests():
    r = subprocess.run(
        ["dotnet", "test", str(TESTS), "-c", "Release", "--nologo", "-v", "q",
         "--filter", FILTER],
        cwd=ROOT, capture_output=True, text=True, encoding="utf-8", errors="replace",
            env={**os.environ, "MSBUILDDISABLENODEREUSE": "1"})
    out = r.stdout + r.stderr
    counts = re.findall(r"(?:失败|Failed):\s*(\d+)", out)
    if counts:
        return int(counts[-1]), out
    return -1, out


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
    print("OK  基线（未变异）绿 —— 那四条性质当前成立")

    # ── ① 把权重填成 50/30/20（看起来最合理，实际份数比例变成 60/24/16）──
    results.append(mutate(
        "权重填成 50/30/20（份数比例实际变成 60/24/16）",
        PROVIDER,
        '            new LootEntry(new ItemId("石料"), 100.0, 1, 2),\n'
        '            new LootEntry(new ItemId("木料"), 90.0, 1, 1),\n'
        '            new LootEntry(new ItemId("布匹"), 60.0, 1, 1),',
        '            new LootEntry(new ItemId("石料"), 50.0, 1, 2),\n'
        '            new LootEntry(new ItemId("木料"), 30.0, 1, 1),\n'
        '            new LootEntry(new ItemId("布匹"), 20.0, 1, 1),',
    ))

    # ── ② 石头每份固定 1 份（份数比例会变 45/30/25）⇒ 必须红 ──────────
    results.append(mutate(
        "石头每份固定 1 份（份数比例变了）",
        PROVIDER,
        '            new LootEntry(new ItemId("石料"), 100.0, 1, 2),',
        '            new LootEntry(new ItemId("石料"), 100.0, 1, 1),',
    ))

    # ── ③ 布匹的稀有度被改成普通 ⇒ 必须红 ─────────────────────────────
    results.append(mutate(
        "布匹不再是「优秀」稀有度",
        CATALOG,
        'new ItemDefinition(new ItemId("布匹"), "布匹", ItemRarity.Uncommon)',
        'new ItemDefinition(new ItemId("布匹"), "布匹", ItemRarity.Common)',
    ))

    # ── ④ 抽的次数被改成 1（总份数不再 3..6）⇒ 必须红 ─────────────────
    results.append(mutate(
        "抽的次数改成 1",
        PROVIDER,
        "        draws: 3,",
        "        draws: 1,",
    ))

    print("---")
    bad = results.count(False)
    print(f"变异矩阵：{len(results) - bad}/{len(results)} 符合预期")
    return 1 if bad else 0


if __name__ == "__main__":
    sys.exit(main())
