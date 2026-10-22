"""
对「杂谈事件类型覆盖」守卫（`ChronicleEventCoverageTests`）做**变异看红**验证。

它守的是「**基线 §7.2 的十类 / 代码里的事件类型 / 盘点文档 §6 的缺口表**」三处不许脱节。

**为什么必须变异看红**：这个守卫有**五档**判据（尺子、十类一行、归类通道、
状态与实况一致、正文数字）。任何一档写松了，它就会**永远绿** ——
而「永远绿的守卫」比没有守卫更坏：它让人以为有人在看。
⇒ 每一档都要**故意弄坏一次**，确认它真的红。

五档变异：

  ① **文档正文说谎**（把「已实现 5 / 10」改成 6 / 10）
     ⇒ 证明「正文数字必须与它自己的表一致」这条有牙。
     ⇒ **这正是真事**：本守卫写出来之前，那份文档正文写的就是「6 / 10」，
       而它自己的表是 5 个 ✅ / 5 个 ❌。
  ② **删掉一个事件类型的 §7.2 归类标注**
     ⇒ 证明「新增事件类型必须归类」这条有牙（注释通道真的被读）。
  ③ **把一个标注改成指向当前未实现的类**
     ⇒ 证明「缺口表的状态必须与代码实况一致」这条有牙。
  ④ **从缺口表里删掉一行**
     ⇒ 证明「十类必须一类一行，缺了就是从视野里消失」这条有牙。
  ⑤ **守卫自己的内部标记被改坏**
     ⇒ 证明「判据自己坏掉时必须红，而不是静默放过」。
     ⇒ 这档容易漏：改坏了判据，守卫可能变成「什么都不检查」⇒ 永远绿。

用法：python production/qa/mutate-chronicle-event-coverage.py
"""
import hashlib
import os
import re
import subprocess
import sys
from pathlib import Path

try:
    import _utf8out  # noqa: F401  — 控制台默认 GBK，印特殊符号会直接崩
except ImportError:
    pass

ROOT = Path(__file__).resolve().parents[2]
TESTS = ROOT / "tests" / "Claude-Code-Game-Studios.Tests.csproj"
FILTER = "FullyQualifiedName~ChronicleEventCoverageTests"
MAIN_CS = ROOT / "src" / "Main.cs"
DOC = ROOT / "production" / "38_开发完成度盘点_V1.0.md"
GUARD = ROOT / "tests" / "unit" / "architecture" / "ChronicleEventCoverageTests.cs"


def sha(p: Path) -> str:
    return hashlib.sha256(p.read_bytes()).hexdigest()


def run_tests():
    r = subprocess.run(
        ["dotnet", "test", str(TESTS), "-c", "Release", "--nologo", "-v", "q",
         "--filter", FILTER],
        cwd=ROOT, capture_output=True, text=True, encoding="utf-8", errors="replace",
            env={**os.environ, "MSBUILDDISABLENODEREUSE": "1"})
    out = r.stdout + r.stderr
    if re.search(r"error CS\d+", out):
        # 变异把代码编译坏了 —— 那不算「断言变红」，得说清楚
        return -2, out
    counts = re.findall(r"(?:失败|Failed):\s*(\d+)", out)
    if counts:
        return int(counts[-1]), out
    return -1, out


def mutate(label, target: Path, old, new):
    """改一处 → 跑测试 → 要求红 → 复原文件（逐字节）。"""
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
            print(f"!!  [{label}] -> BUILD-ERROR（变异把代码编译坏了，这不算断言变红）")
            return False
        ok = failed > 0
        print(f"{'OK  ' if ok else '!!  '}变异 [{label}] -> "
              f"{'RED' if ok else 'GREEN'} (failed={failed}, 期望 RED)")
        if not ok:
            for line in out.splitlines():
                if "失败" in line or "Expecting" in line or "but is" in line:
                    print("      " + line.strip()[:200])
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

def main():
    results = []

    failed, out = run_tests()
    if failed != 0:
        print(f"!! 基线就是红的（failed={failed}）—— 先修好再谈变异")
        for line in out.splitlines():
            if "失败" in line or "Expecting" in line:
                print("      " + line.strip()[:200])
        return 1
    print("OK  基线（未变异）全绿 —— 判据确实能绿")

    # ── ① 文档正文说谎 ────────────────────────────────────────────────
    #    真事：这份文档正文曾经写「已实现 6 / 10」「缺的 4 类」，
    #    而它自己的表是 5 个 ✅ / 5 个 ❌。
    results.append(mutate(
        "文档正文的数字说谎（已实现 5 / 10 改成 6 / 10）",
        DOC,
        "**已实现 5 / 10**",
        "**已实现 6 / 10**",
    ))

    # ── ② 删掉一个事件类型的归类标注 ──────────────────────────────────
    #    ⚠️ 替换要覆盖**整行**（含后面的说明文字）——
    #    只吃掉注释的前半截会把「—— 第三阶段 M5：…」留在行尾 ⇒ 编译不过，
    #    那就变成「编译坏了」而不是「断言变红」了（判据要分清这两种失败）。
    results.append(mutate(
        "删掉一个事件类型的 §7.2 归类标注（新增事件类型可以蒙混过关）",
        MAIN_CS,
        'new("HeroSocialized");                 // §7.2「村庄日常（采集、摆摊、社交等）」'
        '—— 第三阶段 M5：英雄之间的社交',
        'new("HeroSocialized");                 // 第三阶段 M5：英雄之间的社交',
    ))

    # ── ③ 把标注指向当前未实现的类 ────────────────────────────────────
    results.append(mutate(
        "把一个标注改成指向当前未实现的类（缺口表的状态就不再可信）",
        MAIN_CS,
        'new("TradeCompleted");                 // §7.2「村庄日常（采集、摆摊、社交等）」'
        '—— 第四阶段 3B：英雄自主购物成交',
        'new("TradeCompleted");                 // §7.2「某位英雄获得稀有装备」'
        '—— 第四阶段 3B：英雄自主购物成交',
    ))

    # ── ④ 从缺口表里删掉一行 ──────────────────────────────────────────
    results.append(mutate(
        "从缺口表里删掉一行（那一类就从「还差什么」的视野里消失）",
        DOC,
        "| 10 | 本土剧情进展 | ❌ | **内容**（本土剧情本身还没做） |\n",
        "",
    ))

    # ── ⑤ 守卫自己的内部标记被改坏 ────────────────────────────────────
    #    改坏判据 ⇒ 守卫可能变成「什么都不检查」⇒ **永远绿** ⇒ 最危险。
    results.append(mutate(
        "守卫的内部标记被改坏（判据自己坏掉时必须红，而不是静默放过）",
        GUARD,
        'private const string InternalMarker = "§7.2（内部）";',
        'private const string InternalMarker = "§7.2（内部性标记不再匹配任何东西）";',
    ))

    print("---")
    bad = results.count(False)
    print(f"变异矩阵：{len(results) - bad}/{len(results)} 符合预期")
    return 1 if bad else 0


if __name__ == "__main__":
    sys.exit(main())
