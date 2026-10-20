"""
对 ParameterLedgerTests（参数台账守卫）做**变异看红**验证。

四条断言各打一档：
  ① 新增一个没登记的数值常量   → 覆盖率断言红
  ② 改掉一个已登记参数的值     → 值一致断言红
  ③ 删掉一个已登记的常量       → 台账腐化断言红
  ④ 从清单文档里删掉一行参数   → 文档断言红

用法：python production/qa/mutate-param-ledger.py
"""
import hashlib
import json
import re
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
TESTS = ROOT / "tests" / "Claude-Code-Game-Studios.Tests.csproj"
FILTER = "FullyQualifiedName~ParameterLedgerTests"
LEDGER = ROOT / "production" / "qa" / "parameters.json"
DOC = ROOT / "production" / "36_当前实现参数清单_V1.0.md"
SOME_SRC = ROOT / "src" / "Application" / "DailyLife.cs"


def sha(p):
    return hashlib.sha256(p.read_bytes()).hexdigest()


def run_tests():
    r = subprocess.run(
        ["dotnet", "test", str(TESTS), "-c", "Release", "--nologo", "-v", "q",
         "--filter", FILTER],
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


def with_backup(paths, mutate):
    """跑一次变异：备份 → 改 → 跑测试 → 复原 → 校验复原。"""
    backups = {p: p.read_bytes() for p in paths}
    hashes = {p: sha(p) for p in paths}
    try:
        mutate()
        failed, _ = run_tests()
        return failed
    finally:
        for p, data in backups.items():
            p.write_bytes(data)
        for p in paths:
            if sha(p) != hashes[p]:
                print(f"!! 复原失败：{p}")


def main():
    results = []

    # ── ① 新增一个没登记的数值常量 ────────────────────────────────────
    def m1():
        p = SOME_SRC
        text = p.read_text(encoding="utf-8")
        anchor = "public sealed class DailyLife\n{\n"
        idx = text.index(anchor) + len(anchor)
        p.write_text(text[:idx] + "    private const int MutationUnregistered = 7;\n\n" + text[idx:],
                    encoding="utf-8", newline="")
    results.append(report("新增一个没登记的数值常量",
                          with_backup([SOME_SRC], m1)))

    # ── ② 改掉一个已登记参数的值 ──────────────────────────────────────
    def m2():
        p = ROOT / "src" / "Domain" / "Stall" / "StallFee.cs"
        text = p.read_text(encoding="utf-8")
        p.write_text(text.replace("RatePer100 = 5", "RatePer100 = 9"), encoding="utf-8", newline="")
    results.append(report("改掉一个已登记参数的值",
                          with_backup([ROOT / "src" / "Domain" / "Stall" / "StallFee.cs"], m2)))

    # ── ③ 台账里留一条「代码里已经不存在」的条目 ──────────────────────
    # ⚠️ 不能直接删代码里的常量 —— 它被别处引用，删了**编译不过**，
    # 那样测的是编译器，不是这条断言。改成往台账里塞一条**代码里没有**的条目，
    # 断言的是同一件事：台账条目腐化了。
    def m3():
        data = json.loads(LEDGER.read_text(encoding="utf-8"))
        data["parameters"].append({
            "file": "src/Main.cs",
            "name": "MutationGhostConstant",
            "kind": "参数",
            "status": "【占位】P-01",
            "note": "【变异用】代码里已经不存在的常量",
            "value": "42",
        })
        LEDGER.write_text(json.dumps(data, ensure_ascii=False, indent=2) + "\n",
                          encoding="utf-8", newline="\n")
    results.append(report("台账里留一条代码里已不存在的条目",
                          with_backup([LEDGER], m3)))

    # ── ④ 从清单文档里删掉一行参数 ────────────────────────────────────
    def m4():
        doc = DOC.read_text(encoding="utf-8")
        lines = [l for l in doc.split("\n") if "`SocialDelta`" not in l]
        DOC.write_text("\n".join(lines), encoding="utf-8", newline="")
    results.append(report("从清单文档里删掉一行参数",
                          with_backup([DOC], m4)))

    # ── ⑤ 表类参数：改掉落权重 ────────────────────────────────────────
    def m5():
        p = ROOT / "src" / "Application" / "DungeonLootProvider.cs"
        t = p.read_text(encoding="utf-8")
        p.write_text(t.replace('new ItemId("石料"), 60.0, 1, 2', 'new ItemId("石料"), 65.0, 1, 2'),
                     encoding="utf-8", newline="")
    results.append(report("改了掉落权重",
                          with_backup([ROOT / "src" / "Application" / "DungeonLootProvider.cs"], m5)))

    # ── ⑥ 表类参数：改升级成本 ────────────────────────────────────────
    def m6():
        p = ROOT / "src" / "Application" / "VillageUpgradeService.cs"
        t = p.read_text(encoding="utf-8")
        p.write_text(t.replace("new(50L, 30L),", "new(60L, 30L),"), encoding="utf-8", newline="")
    results.append(report("改了升级成本",
                          with_backup([ROOT / "src" / "Application" / "VillageUpgradeService.cs"], m6)))

    # ── ⑦ 表类参数：改稀有度映射 ──────────────────────────────────────
    def m7():
        p = ROOT / "src" / "Domain" / "Items" / "ItemCatalog.cs"
        t = p.read_text(encoding="utf-8")
        p.write_text(t.replace('new ItemId("木料"), "木料", ItemRarity.Common',
                                'new ItemId("木料"), "木料", ItemRarity.Uncommon'),
                     encoding="utf-8", newline="")
    results.append(report("改了稀有度映射",
                          with_backup([ROOT / "src" / "Domain" / "Items" / "ItemCatalog.cs"], m7)))

    # ── ⑧ 表类参数：改性格权重 ────────────────────────────────────────
    def m8():
        p = ROOT / "src" / "Application" / "HeroBehaviorService.cs"
        t = p.read_text(encoding="utf-8")
        p.write_text(t.replace('"莽" => 1.2,', '"莽" => 1.3,'), encoding="utf-8", newline="")
    results.append(report("改了性格权重",
                          with_backup([ROOT / "src" / "Application" / "HeroBehaviorService.cs"], m8)))

    # ── ⑨ 文档里漏掉一张表 ────────────────────────────────────────────
    def m9():
        doc = DOC.read_text(encoding="utf-8")
        DOC.write_text(doc.replace("loot-weights-and-quantity", "（漏掉的那张）"),
                       encoding="utf-8", newline="")
    results.append(report("文档里漏掉一张表",
                          with_backup([DOC], m9)))

    print("---")
    bad = results.count(False)
    print(f"变异矩阵：{len(results) - bad}/{len(results)} 符合预期")
    return 1 if bad else 0


if __name__ == "__main__":
    sys.exit(main())
