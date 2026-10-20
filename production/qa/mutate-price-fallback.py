"""
对「价格表两份副本」这条链路做**变异看红**验证。

价格表是同一份数据的**两个副本**：`config/prices.json`（开发者改的那份）与
`PriceBook.Default`（配置读不到时的兜底）。守卫
`PriceBookConfigConsistencyTests` 要求两者**严格相等**。

四档变异：

  ① 改 JSON 里的一个价（不动兜底）      ⇒ 副本守卫**红**（它确实在守）
  ② 同上，然后跑 `sync-price-fallback.py` ⇒ 守卫**绿**（工具真能修好）
  ③ 价格表里有负价 / 非整数              ⇒ 同步脚本**报错且不动文件**
  ④ `PriceBook.cs` 的兜底块被破坏        ⇒ 同步脚本**报错且不动文件**

③ ④ 是「工具会不会**帮倒忙**」：一个同步脚本如果在你给它坏输入时
默默改坏源文件，那它比没有脚本更危险。

用法：python production/qa/mutate-price-fallback.py
"""
try:
    import _utf8out  # noqa: F401  — 控制台默认 GBK，印 ✓/✗ 会直接崩
except ImportError:
    pass

import hashlib
import json
import re
import shutil
import subprocess
import sys
import tempfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
PRICES = ROOT / "config" / "prices.json"
FALLBACK = ROOT / "src" / "Domain" / "Items" / "PriceBook.cs"
SYNC = ROOT / "production" / "qa" / "sync-price-fallback.py"
TESTS = ROOT / "tests" / "Claude-Code-Game-Studios.Tests.csproj"
FILTER = "FullyQualifiedName~PriceBookConfigConsistencyTests"


def sha(p: Path) -> str:
    return hashlib.sha256(p.read_bytes()).hexdigest()


LAST_OUTPUT = ""


def run_tests(filter: str = FILTER) -> int:
    global LAST_OUTPUT
    r = subprocess.run(
        ["dotnet", "test", str(TESTS), "-c", "Release", "--nologo", "-v", "q",
         "--filter", filter],
        cwd=ROOT, capture_output=True, text=True, encoding="utf-8", errors="replace")
    out = r.stdout + r.stderr
    LAST_OUTPUT = out
    for pat in (r"失败:\s*(\d+)", r"Failed:\s*(\d+)"):
        m = re.search(pat, out)
        if m:
            return int(m.group(1))
    return -1


def report(label: str, ok: bool, detail: str) -> bool:
    """打印结果；**解析不出失败计数时把输出尾巴打出来** ——
    一个只报「-1」的变异脚本等于没说话（它没告诉你到底是编译挂了还是断言挂了）。"""
    mark = "OK  " if ok else "!!  "
    print(f"{mark}变异 [{label}] -> {detail}")
    if not ok and LAST_OUTPUT:
        # 只打**有信息量**的行（编译错误 / 断言失败 / 汇总）——
        # 打尾巴会被一片编译警告淹没。
        keep = [l for l in LAST_OUTPUT.splitlines()
                if ("error" in l.lower() or "失败" in l or "Expecting" in l
                    or "错误" in l or "通过" in l)]
        tail = "\n".join(keep[-10:]) if keep else "\n".join(LAST_OUTPUT.strip().splitlines()[-6:])
        print("      " + tail.replace("\n", "\n      ")[:1200])
    return ok


def run_sync(prices_path: Path | None = None) -> tuple[int, str]:
    argv = [sys.executable, str(SYNC)]
    if prices_path is not None:
        argv += ["--prices", str(prices_path)]
    p = subprocess.run(argv, cwd=ROOT, capture_output=True, text=True,
                       encoding="utf-8", errors="replace")
    return p.returncode, (p.stdout or "") + (p.stderr or "")


def set_price(name: str, value: int) -> None:
    raw = PRICES.read_text(encoding="utf-8")
    raw = re.sub(rf'("{re.escape(name)}"\s*:\s*)\d+', rf"\g<1>{value}", raw, count=1)
    PRICES.write_text(raw, encoding="utf-8", newline="")


def main() -> int:
    results = []
    prices_backup = PRICES.read_bytes()
    fallback_backup = FALLBACK.read_bytes()
    prices_sha = sha(PRICES)
    fallback_sha = sha(FALLBACK)

    try:
        # ── ① 改 JSON，不动兜底 ⇒ 守卫必须红 ──────────────────────────
        set_price("石料", 7)
        failed = run_tests()
        ok = failed > 0
        print(f"{'OK  ' if ok else '!!  '}变异 [改 JSON 的一个价、不动内置兜底] -> "
              f"{'RED' if ok else 'GREEN'} (failed={failed}, 期望 RED)")
        results.append(ok)

        # ── ② 同上 + 跑同步脚本 ⇒ 守卫必须绿（工具真能修好）────────────
        rc, out = run_sync()
        after = run_tests()
        ok = rc == 0 and after == 0
        print(f"{'OK  ' if ok else '!!  '}变异 [改 JSON 后跑同步脚本] -> "
              f"{'GREEN' if ok else 'RED'} (sync rc={rc}, failed={after}, 期望 GREEN)")
        if not ok:
            print("      " + out.strip()[:300].replace("\n", " | "))
        results.append(ok)

        # 复原（② 里同步脚本改了兜底）
        PRICES.write_bytes(prices_backup)
        FALLBACK.write_bytes(fallback_backup)

        # ── ③ 价格表里有负价 ⇒ 同步脚本必须报错且不动文件 ──────────────
        bad = Path(tempfile.mkdtemp(prefix="price-bad-")) / "prices.json"
        raw = PRICES.read_text(encoding="utf-8")
        raw = re.sub(r'("石料"\s*:\s*)\d+', r"\g<1>-1", raw, count=1)
        bad.write_text(raw, encoding="utf-8", newline="")
        before = sha(FALLBACK)
        rc, out = run_sync(bad)
        untouched = sha(FALLBACK) == before
        ok = rc != 0 and untouched
        print(f"{'OK  ' if ok else '!!  '}变异 [价格表含负价] -> "
              f"{'报错且未动文件' if ok else f'rc={rc} touched={not untouched}'}"
              f"（期望：报错且不动文件）")
        results.append(ok)

        # ── ④ 兜底块被破坏 ⇒ 同步脚本必须报错且不动文件 ────────────────
        #    ⚠️ 这一档必须**当场**复原 —— 否则后面几档是在一棵**编译不过**的树上跑的，
        #    它们的失败就与被测的守卫无关了（我第一版就踩了：靠最外层 finally 复原，
        #    结果后面两档全是「-1」，白跑）。
        fallback_backup_4 = FALLBACK.read_bytes()
        try:
            text = fallback_backup_4.decode("utf-8")
            broken = text.replace("public static PriceBook Default { get; }",
                                  "public static PriceBook DefaultX { get; }", 1)
            FALLBACK.write_text(broken, encoding="utf-8", newline="")
            before = sha(FALLBACK)
            rc, out = run_sync()
            untouched = sha(FALLBACK) == before
            ok = rc != 0 and untouched
            print(f"{'OK  ' if ok else '!!  '}变异 [兜底块被破坏（找不到那一块）] -> "
                  f"{'报错且未动文件' if ok else f'rc={rc} touched={not untouched}'}"
                  f"（期望：报错且不动文件）")
            results.append(ok)
        finally:
            FALLBACK.write_bytes(fallback_backup_4)

        # ── ⑤ 某个测试文件改回用全局占位价 ⇒ 占位价守卫必须红 ──────────
        target = ROOT / "tests" / "unit" / "stall" / "StallServiceTests.cs"
        target_backup = target.read_bytes()
        try:
            text = target_backup.decode("utf-8")
            target.write_text(text.replace("TestPrices.Book", "PriceBook.Default", 1),
                              encoding="utf-8", newline="")
            failed = run_tests("FullyQualifiedName~PlaceholderPriceOnlyInTwoPlaces")
            ok = failed > 0
            results.append(report(
                "某个测试文件改回用全局占位价", ok,
                f"{'RED' if ok else 'GREEN'} (failed={failed}, 期望 RED)"))
        finally:
            target.write_bytes(target_backup)

        # ── ⑥ 守卫的例外清单写错 ⇒ 防空转断言必须红 ──────────────────────
        guard = ROOT / "tests" / "unit" / "architecture" / "PlaceholderPriceOnlyInTwoPlacesTests.cs"
        guard_backup = guard.read_bytes()
        try:
            text = guard_backup.decode("utf-8")
            guard.write_text(text.replace('"tests/unit/items/PriceBookTests.cs",',
                                          '"tests/unit/items/PriceBookTests.cs",\n        "tests/unit/items/ZZZ_不存在的文件.cs",'),
                              encoding="utf-8", newline="")
            failed = run_tests("FullyQualifiedName~PlaceholderPriceOnlyInTwoPlaces")
            ok = failed > 0
            results.append(report(
                "守卫的例外清单里有一个不存在的文件", ok,
                f"{'RED' if ok else 'GREEN'} (failed={failed}, 期望 RED)"))
        finally:
            guard.write_bytes(guard_backup)
    finally:
        PRICES.write_bytes(prices_backup)
        FALLBACK.write_bytes(fallback_backup)
        if sha(PRICES) != prices_sha or sha(FALLBACK) != fallback_sha:
            print("!! 复原失败")

    print("---")
    bad_count = results.count(False)
    print(f"变异矩阵：{len(results) - bad_count}/{len(results)} 符合预期")
    return 1 if bad_count else 0


if __name__ == "__main__":
    sys.exit(main())
