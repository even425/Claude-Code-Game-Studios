"""
对 HeroIdUniquenessTests（IT-25 / I1 同一 Id 只对应一个英雄实体）做**变异看红**验证。

这条例外守的是「引用指错人」：两个英雄同号 ⇒ 货记到别人头上、
读档时两个状态映射到同一个实体。

两档变异：
  ① 退回旧写法（按人数当序号 + 不检查是否已占用）⇒ 花名册缺号时撞号
  ② 序号不前进 ⇒ 连续分配出同一个号

用法：python production/qa/mutate-hero-ids.py
"""
import hashlib
import re
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
TESTS = ROOT / "tests" / "Claude-Code-Game-Studios.Tests.csproj"
FILTER = "FullyQualifiedName~HeroIdUniquenessTests"
SRC = "src/Application/HeroIdAllocator.cs"


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


def mutate(label, edits):
    p = ROOT / SRC
    backup = p.read_bytes()
    before = sha(p)
    raw = p.read_text(encoding="utf-8")
    crlf = "\r\n" in raw
    text = raw.replace("\r\n", "\n")
    for old, new in edits:
        old = old.replace("\r\n", "\n")
        new = new.replace("\r\n", "\n")
        if old not in text:
            print(f"!! [{label}] 模式没找到：{old[:60]!r}")
            p.write_bytes(backup)
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

    # ── ① 退回旧写法：按人数当序号 + 不检查是否已占用 ───────────────
    # 这正是原来的 `h-{_heroes.Count}`：花名册是 h-0 / h-2 时 ⇒ 发出 h-2 ⇒ 撞号。
    results.append(mutate(
        "退回旧写法（按人数当序号 + 不检查占用）",
        [
            ("        _next = max + 1;",
             "        _next = _used.Count;   // 变异：退回「按人数当序号」"),
            ("        while (true)\n"
             "        {\n"
             "            string id = $\"h-{_next.ToString(CultureInfo.InvariantCulture)}\";\n"
             "            _next++;\n"
             "            if (_used.Add(id)) return id;\n"
             "        }",
             "        // 变异：不检查是否已占用\n"
             "        string id = $\"h-{_next.ToString(CultureInfo.InvariantCulture)}\";\n"
             "        _next++;\n"
             "        return id;"),
        ],
    ))

    # ── ② 序号不前进 ⇒ 连续分配出同一个号 ──────────────────────────
    # ⚠️ 不能只删掉 `_next++` —— 那样 `while (true)` 会**死循环**
    # （同一个号一直撞 `_used`）。所以整段替换成「不前进也不跳出」。
    results.append(mutate(
        "序号不前进（连续分配出同一个号）",
        [
            ("        while (true)\n"
             "        {\n"
             "            string id = $\"h-{_next.ToString(CultureInfo.InvariantCulture)}\";\n"
             "            _next++;\n"
             "            if (_used.Add(id)) return id;\n"
             "        }",
             "        // 变异：序号不前进，也不检查占用\n"
             "        return $\"h-{_next.ToString(CultureInfo.InvariantCulture)}\";"),
        ],
    ))

    print("---")
    bad = results.count(False)
    print(f"变异矩阵：{len(results) - bad}/{len(results)} 符合预期")
    return 1 if bad else 0


if __name__ == "__main__":
    sys.exit(main())
