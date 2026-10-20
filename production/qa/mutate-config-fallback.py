"""
对两份「外部配置 vs 内置兜底」的一致性守卫做**变异看红**验证。

  PriceBookConfigConsistencyTests  —— 价格表
  WorldTableConfigConsistencyTests —— 世界表

这两对都是**同一份数据的两个副本**。外部化一个配置表，就是把一份数据变成两份；
**复制数据这件事必须配一个「两份不许分叉」的检查**，否则它就是一处静默的定时炸弹：
配置文件在 / 不在 ⇒ 两套不同的经济 / 世界表，而它只在配置出问题时才暴露。

用法：python production/qa/mutate-config-fallback.py
"""
import hashlib
import re
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
TESTS = ROOT / "tests" / "Claude-Code-Game-Studios.Tests.csproj"
FILTER = "FullyQualifiedName~PriceBookConfigConsistencyTests"
FILTER2 = "FullyQualifiedName~WorldTableConfigConsistencyTests"
PRICES = ROOT / "config" / "prices.json"
WORLDS = ROOT / "config" / "worlds.json"
PRICEBK = ROOT / "src" / "Domain" / "Items" / "PriceBook.cs"
WORLDBK = ROOT / "src" / "HeroFactory.cs"


def sha(p):
    return hashlib.sha256(p.read_bytes()).hexdigest()


def run_tests(filter_):
    r = subprocess.run(
        ["dotnet", "test", str(TESTS), "-c", "Release", "--nologo", "-v", "q",
         "--filter", filter_],
        cwd=ROOT, capture_output=True, text=True, encoding="utf-8", errors="replace")
    out = r.stdout + r.stderr
    for pat in (r"失败:\s*(\d+)", r"Failed:\s*(\d+)"):
        m = re.search(pat, out)
        if m:
            return int(m.group(1)), out
    return -1, out


def report(label, failed):
    color = "RED  " if failed > 0 else ("GREEN" if failed == 0 else "???  ")
    ok = failed > 0
    print(f"{'OK  ' if ok else '!!  '}变异 [{label}] -> {color} (failed={failed}, 期望 RED)")
    return ok


def with_backup(paths, mutate, filter_):
    backups = {p: p.read_bytes() for p in paths}
    hashes = {p: sha(p) for p in paths}
    try:
        mutate()
        failed, _ = run_tests(filter_)
        return failed
    finally:
        for p, data in backups.items():
            p.write_bytes(data)
        for p in paths:
            if sha(p) != hashes[p]:
                print(f"!! 复原失败：{p}")


def main():
    results = []

    # ── ① 改了外部价格表 ──────────────────────────────────────────────
    def m1():
        t = PRICES.read_text(encoding="utf-8")
        PRICES.write_text(t.replace('"石料": 5', '"石料": 6'), encoding="utf-8", newline="")
    results.append(report("改了外部价格表（config/prices.json）",
                          with_backup([PRICES], m1, FILTER)))

    # ── ② 改了内置价格兜底 ────────────────────────────────────────────
    def m2():
        t = PRICEBK.read_text(encoding="utf-8")
        PRICEBK.write_text(t.replace('new ItemId("石料"), 5L', 'new ItemId("石料"), 7L'),
                           encoding="utf-8", newline="")
    results.append(report("改了内置价格兜底（PriceBook.Default）",
                          with_backup([PRICEBK], m2, FILTER)))

    # ── ③ 改了外部世界表的名字 ────────────────────────────────────────
    def m3():
        t = WORLDS.read_text(encoding="utf-8")
        WORLDS.write_text(t.replace('"剑与魔法的大陆"', '"剑与魔法的大陆2"'),
                          encoding="utf-8", newline="")
    results.append(report("改了外部世界表的名字（config/worlds.json）",
                          with_backup([WORLDS], m3, FILTER2)))

    # ── ④ 改了内置世界表的一个痕迹（数组内容，不是数量）──────────────
    def m4():
        t = WORLDBK.read_text(encoding="utf-8")
        WORLDBK.write_text(t.replace('Traits  = new[] { "背着一把用了很多年的剑", "身上有股铁锈味", "说话很响" }',
                                      'Traits  = new[] { "背着一把用了很多年的剑", "身上有股铁锈味", "说话很快" }'),
                           encoding="utf-8", newline="")
    results.append(report("改了内置世界表的一个痕迹（HeroFactory.Builtin）",
                          with_backup([WORLDBK], m4, FILTER2)))

    print("---")
    bad = results.count(False)
    print(f"变异矩阵：{len(results) - bad}/{len(results)} 符合预期")
    return 1 if bad else 0


if __name__ == "__main__":
    sys.exit(main())
