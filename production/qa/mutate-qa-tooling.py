"""
对 QA 工具箱守卫（`QaToolingTests`）做**变异看红**验证。

这条守卫守的是一件很具体的事：**工具本身要跑得起来，且 README 不能指向空气**。
它已经抓到过一次真问题 —— 本机控制台是 GBK，`run-selfchecks.py` 印 `✓` 就崩，
后面 6 个自检一个都没跑，而它最后那句「全部通过」压根没机会打印。

四档变异：

  ① 抽掉某个脚本的 `import _utf8out`  → 断言 1 红（那个脚本在 GBK 控制台上会崩）
  ② 放一个没登记的 `mutate-*.py` 进目录 → 断言 2 红（README 与磁盘分叉）
  ③ 让 README 指向一个不存在的脚本      → 断言 2 / 3 红（文档指向空气）
  ④ 把 README 里的自检数量写错          → 断言 4 红（「11 个」听起来比「7 个」更让人放心）

用法：python production/qa/mutate-qa-tooling.py
"""
import hashlib
import re
import subprocess
import sys
from pathlib import Path

try:
    import _utf8out  # noqa: F401  — 控制台默认 GBK，印 ✓/✗ 会直接崩
except ImportError:
    pass

ROOT = Path(__file__).resolve().parents[2]
QA = ROOT / "production" / "qa"
README = QA / "README.md"
TESTS = ROOT / "tests" / "Claude-Code-Game-Studios.Tests.csproj"
FILTER = "FullyQualifiedName~QaToolingTests"


def sha(p: Path) -> str:
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


def mutate(label, target: Path, old, new):
    """改一处 → 跑测试 → 要求红 → 复原。"""
    backup = target.read_bytes()
    before = sha(target)
    raw = target.read_text(encoding="utf-8")
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
        target.write_bytes(backup)
        if sha(target) != before:
            print(f"!! 复原失败 [{label}]")


def main():
    results = []

    # ── 基线先绿 ────────────────────────────────────────────────────
    failed, out = run_tests()
    if failed != 0:
        print(f"!! 基线就是红的（failed={failed}）—— 先修好再谈变异")
        for line in out.splitlines():
            if "失败" in line or "Expecting" in line:
                print("      " + line.strip()[:160])
        return 1
    print("OK  基线（未变异）全绿 —— 判据确实能绿")

    # ── ① 抽掉一个脚本的 import _utf8out ────────────────────────────
    results.append(mutate(
        "抽掉 run-selfchecks.py 的 import _utf8out（它在 GBK 控制台上会崩）",
        QA / "run-selfchecks.py",
        "try:\n    import _utf8out  # noqa: F401  — 控制台默认 GBK，印 ✓/✗ 会直接崩\nexcept ImportError:\n    pass\n\n",
        "",
    ))

    # ── ② 放一个没登记的变异脚本进目录 ──────────────────────────────
    stray = QA / "mutate-not-registered.py"
    stray.write_text("# 临时：用来验证「README 与磁盘分叉 ⇒ 红」\n", encoding="utf-8")
    try:
        failed, out = run_tests()
        ok = failed > 0
        print(f"{'OK  ' if ok else '!!  '}变异 [放一个没登记的 mutate-*.py 进目录] -> "
              f"{'RED' if ok else 'GREEN'} (failed={failed}, 期望 RED)")
        if not ok:
            for line in out.splitlines():
                if "失败" in line or "Expecting" in line:
                    print("      " + line.strip()[:160])
        results.append(ok)
    finally:
        stray.unlink(missing_ok=True)

    # ── ③ 让 README 指向一个不存在的脚本 ────────────────────────────
    results.append(mutate(
        "README 指向一个不存在的脚本（文档指向空气）",
        README,
        "| `mutate-qa-tooling.py` |",
        "| `mutate-does-not-exist.py` | `mutate-qa-tooling.py` |",
    ))

    # ── ④ 把自检数量写错 ────────────────────────────────────────────
    results.append(mutate(
        "README 把自检数量写成 11（实际 7）",
        README,
        "跑 7 个**真机自检**开关",
        "跑 11 个**真机自检**开关",
    ))

    # ── ⑤ 破坏 annotation 的命令前缀 ────────────────────────────────
    #    这档打的是「失败原因能不能被机器读到」：前缀坏了 ⇒
    #    GitHub 不认这条 workflow command ⇒ 失败原因退回到「只有一句 exit code 1」。
    results.append(mutate(
        "自检脚本的 annotation 丢掉 :: 前缀（GitHub 不认这条命令了）",
        QA / "run-selfchecks.py",
        'print(f"::{kind} title={title}::{flat}")',
        'print(f"{kind} {title} {flat}")',
    ))

    # ── ⑥ 让本地也打印 annotation ───────────────────────────────────
    results.append(mutate(
        "去掉「不在 Actions 里就不输出」的保护（本地日志被污染）",
        QA / "run-selfchecks.py",
        '    if not os.environ.get("GITHUB_ACTIONS"):\n        return\n',
        "",
    ))

    # ── ⑦ 不折叠消息里的换行 ───────────────────────────────────────
    #    GitHub 的 workflow command 是**单行**的：换行会被截断，
    #    甚至可能注入一条新的 workflow command。
    results.append(mutate(
        "annotation 的消息不再折叠换行（多行消息会被截断）",
        QA / "run-selfchecks.py",
        'flat = " ".join(str(message).split())',
        'flat = str(message)',
    ))

    # ── ⑧ 摘要不再写文件 ───────────────────────────────────────────
    #    摘要是「CI 红了时人第一眼看到的东西」——它写不出来，
    #    而那条路径一开始**没有任何测试覆盖**（写不出来也不会红）。
    results.append(mutate(
        "摘要不再写文件（CI 的 job summary 会是空的）",
        QA / "run-selfchecks.py",
        '        path.write_text("\\n".join(lines) + "\\n", encoding="utf-8", newline="\\n")',
        "        pass  # 变异：故意不写摘要",
    ))

    print("---")
    bad = results.count(False)
    print(f"变异矩阵：{len(results) - bad}/{len(results)} 符合预期")
    return 1 if bad else 0


if __name__ == "__main__":
    sys.exit(main())
