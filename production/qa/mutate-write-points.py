"""
对 DomainWritePointAuditTests（IT-27 / I10 唯一写者）做**变异看红**验证。

I10：跨模块的状态改变，只经由拥有该数据的模块。
机械形式：领域层**不得有公开的写入口**。

三档变异，分别打三条判据：
  ① 给领域类型加公开 setter
  ② 把内部字典暴露成公开可变集合
  ③ 加一个公开字段

用法：python production/qa/mutate-write-points.py
"""
try:
    import _utf8out  # noqa: F401  — 控制台默认 GBK，印 ✓/✗ 会直接崩
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
FILTER = "FullyQualifiedName~DomainWritePointAuditTests"
INVENTORY = "src/Domain/Items/Inventory.cs"


def sha(p):
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


def mutate(label, rel_path, old, new):
    p = ROOT / rel_path
    backup = p.read_bytes()
    before = sha(p)
    raw = p.read_text(encoding="utf-8")
    crlf = "\r\n" in raw
    text = raw.replace("\r\n", "\n")
    old = old.replace("\r\n", "\n")
    new = new.replace("\r\n", "\n")
    if old not in text:
        print(f"!! [{label}] 模式没找到：{old[:60]!r}")
        return False
    text = text.replace(old, new, 1)
    if crlf:
        text = text.replace("\n", "\r\n")
    p.write_text(text, encoding="utf-8", newline="")
    try:
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
        p.write_bytes(backup)
        if sha(p) != before:
            print(f"!! 复原失败 [{label}]")


def main():
    results = []

    # ── ① 公开 setter ───────────────────────────────────────────────
    results.append(mutate(
        "给 Inventory.Capacity 加公开 setter",
        INVENTORY,
        "    public long Capacity { get; }",
        "    public long Capacity { get; set; }",
    ))

    # ── ② 暴露内部可变集合 ───────────────────────────────────────────
    results.append(mutate(
        "把 Inventory 的内部字典暴露成公开可变集合",
        INVENTORY,
        "    public IReadOnlyDictionary<ItemId, long> Items => _items;",
        "    public Dictionary<ItemId, long> Items => _items;",
    ))

    # ── ③ 公开字段 ──────────────────────────────────────────────────
    results.append(mutate(
        "给 Inventory 加一个公开字段",
        INVENTORY,
        "    private readonly Dictionary<ItemId, long> _items;",
        "    private readonly Dictionary<ItemId, long> _items;\n\n"
        "    /// <summary>【变异用】公开字段。</summary>\n"
        "    public int LeakedField;",
    ))

    print("---")
    bad = results.count(False)
    print(f"变异矩阵：{len(results) - bad}/{len(results)} 符合预期")
    return 1 if bad else 0


if __name__ == "__main__":
    sys.exit(main())
