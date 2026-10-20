"""
对 StaticDefinitionSeparationTests（UT-07 / I7）做**变异看红**验证。

规矩：<b>不养烧不着的守卫</b>。

这三条断言守的是「静态定义不被运行时写坏」——
一种**跑起来之后才发生、现场早就没了**的 bug。
所以必须亲眼看见它们在代码被改坏时变红。

用法：python production/qa/mutate-static-defs.py
"""
try:
    import _utf8out  # noqa: F401  — 控制台默认 GBK，印 ✓/✗ 会直接崩
except ImportError:
    pass

import hashlib
import re
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
TESTS = ROOT / "tests" / "Claude-Code-Game-Studios.Tests.csproj"
FILTER = "FullyQualifiedName~StaticDefinitionSeparationTests"


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


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


def read_norm(p):
    raw = p.read_text(encoding="utf-8")
    return raw, ("\r\n" in raw)


def apply_edits(p, edits):
    """edits: [(old, new), ...]；按原换行符匹配与写回。"""
    raw, crlf = read_norm(p)
    text = raw.replace("\r\n", "\n")
    for old, new in edits:
        old = old.replace("\r\n", "\n")
        new = new.replace("\r\n", "\n")
        if old not in text:
            raise AssertionError(f"模式没找到：{old[:70]!r}")
        text = text.replace(old, new, 1)
    if crlf:
        text = text.replace("\n", "\r\n")
    p.write_text(text, encoding="utf-8", newline="")


def mutate(label, edits, paths):
    backups = {p: (ROOT / p).read_bytes() for p in paths}
    before = {p: sha(ROOT / p) for p in paths}
    try:
        for p, ed in edits.items():
            apply_edits(ROOT / p, ed)
        failed, out = run_tests()
        color = "RED  " if failed > 0 else ("GREEN" if failed == 0 else "???  ")
        ok = failed > 0
        print(f"{'OK  ' if ok else '!!  '}变异 [{label}] -> {color} (failed={failed}, 期望 RED)")
        if not ok:
            for line in out.splitlines():
                if "失败" in line or "Expecting" in line:
                    print("      " + line.strip()[:160])
        return ok
    finally:
        for p, data in backups.items():
            (ROOT / p).write_bytes(data)
        for p in paths:
            if sha(ROOT / p) != before[p]:
                print(f"!! 复原失败 [{p}]")


# 把内部字典交出去 —— 一个「写入口」
EXPOSE_DICT = (
    "    private readonly Dictionary<ItemId, long> _prices;",
    "    private readonly Dictionary<ItemId, long> _prices;\n\n"
    "    /// <summary>【变异用】把内部字典交出去。</summary>\n"
    "    public Dictionary<ItemId, long> Prices => _prices;",
)


def main():
    results = []

    # ── 1. 给静态定义加一个公开 setter ────────────────────────────────
    results.append(mutate(
        "给 ItemDefinition 加公开 setter",
        {"src/Domain/Items/ItemDefinition.cs": [
            ("    public string DisplayName { get; }",
             "    public string DisplayName { get; set; }"),
        ]},
        ["src/Domain/Items/ItemDefinition.cs"],
    ))

    # ── 2. 把内部字典交出去（漏出可变集合）──────────────────────────
    results.append(mutate(
        "PriceBook 把内部字典暴露成公开属性",
        {"src/Domain/Items/PriceBook.cs": [EXPOSE_DICT]},
        ["src/Domain/Items/PriceBook.cs"],
    ))

    # ── 3. 组合：既有写入口，又真的在运行时写了 ──────────────────────
    # 只有「写入口 + 真写」一起发生，静态定义才会被改坏 ⇒
    # 行为那条断言（跑完闭环后内容不变）才抓得住。
    results.append(mutate(
        "组合：有写入口 + 运行时真的写回价格表",
        {
            "src/Domain/Items/PriceBook.cs": [EXPOSE_DICT],
            "src/Application/DungeonLootProvider.cs": [
                ("        var errors = LootTableValidator.Validate(table, _catalog, _prices);",
                 "        var errors = LootTableValidator.Validate(table, _catalog, _prices);\n"
                 "        foreach (var pid in new System.Collections.Generic.List<ItemId>(_prices.Prices.Keys))\n"
                 "        {\n"
                 "            _prices.Prices[pid] = _prices.Prices[pid] + 1;\n"
                 "        }"),
            ],
        },
        ["src/Domain/Items/PriceBook.cs", "src/Application/DungeonLootProvider.cs"],
    ))

    print("---")
    bad = results.count(False)
    print(f"变异矩阵：{len(results) - bad}/{len(results)} 符合预期")
    return 1 if bad else 0


if __name__ == "__main__":
    sys.exit(main())
