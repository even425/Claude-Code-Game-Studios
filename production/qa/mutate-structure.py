"""
对 MainStructureTests（UT-01 结构守卫）做**变异看红**验证。

规矩：<b>不养烧不着的守卫</b>。一条守卫如果「怎么改代码都不会红」，
它就只是个装饰 —— 而且比没有守卫更糟：它给人「这一段有人看着」���错觉。

做法：逐个把源码改成<b>违反规则</b>的样子，看对应的测试有没有变红；
改完必须复原（用 SHA-256 校验复原到位，不是「看着没问题」）。

用法：python production/qa/mutate-structure.py
"""
import hashlib
import re
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
SRC = ROOT / "src"
TESTS = ROOT / "tests" / "Claude-Code-Game-Studios.Tests.csproj"
FILTER = "FullyQualifiedName~MainStructureTests"


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def run_tests(filter=FILTER):
    r = subprocess.run(
        ["dotnet", "test", str(TESTS), "-c", "Release", "--nologo", "-v", "q",
         "--filter", filter],
        cwd=ROOT, capture_output=True, text=True, encoding="utf-8", errors="replace")
    out = r.stdout + r.stderr
    m = re.search(r"失败:\s*(\d+)", out)
    if m:
        return int(m.group(1)), out
    m2 = re.search(r"Failed:\s*(\d+)", out)
    if m2:
        return int(m2.group(1)), out
    return -1, out


class Mutation:
    """一次变异：改文件 → 跑测试 → 复原 → 校验复原到位。"""

    def __init__(self, label, path, old, new, expect_red=True, filter=None):
        self.label = label
        self.path = path
        self.old = old
        self.new = new
        self.expect_red = expect_red
        # 默认只跑 MainStructureTests；有些变异守的是**别的**守卫
        # （例如「领域层不许依赖 Godot」那条在 DomainLayerPurityTests 里，
        #  它比本文件里的那条更强 ⇒ 本文件那条是弱重复，已删）。
        self.filter = filter or FILTER

    def apply(self):
        p = ROOT / self.path
        text = p.read_text(encoding="utf-8")
        if self.old not in text:
            raise AssertionError(f"[{self.label}] 模式没找到：{self.old[:60]!r}")
        p.write_text(text.replace(self.old, self.new, 1), encoding="utf-8", newline="")

    def restore(self, backup):
        (ROOT / self.path).write_bytes(backup)

    def run(self):
        p = ROOT / self.path
        backup = p.read_bytes()
        before = sha(p)
        try:
            self.apply()
            failed, out = run_tests(self.filter)
            color = "RED  " if failed > 0 else ("GREEN" if failed == 0 else "???  ")
            want = "RED" if self.expect_red else "GREEN"
            ok = (failed > 0) == self.expect_red
            mark = "OK  " if ok else "!!  "
            print(f"{mark}变异 [{self.label}] -> {color} (failed={failed}, 期望 {want})")
            if not ok:
                for line in out.splitlines():
                    if "失败" in line or "Expecting" in line:
                        print("      " + line.strip()[:160])
                return False
            return True
        finally:
            self.restore(backup)
            after = sha(p)
            if after != before:
                print(f"!! 复原失败 [{self.label}]：{before[:12]} -> {after[:12]}")
                return False


def main():
    # ── 1. 三类职责挤进同一个文件 ────────────────────────────────────
    # 模拟推进：把 Main.cs 的 TickBuild **整块搬到** Main.Expedition.cs
    # （Expedition 已有「战斗执行 + 结算入账」两类 ⇒ 加上就变三类）。
    # 注意必须是「搬」而不是「复制」——复制会撞同名成员、编译不过，
    # 那测的是编译器，不是这条守卫。
    tick = (SRC / "Main.cs").read_text(encoding="utf-8")
    start = tick.index("    /// <summary>\n    /// 每帧推进工地。")
    end = tick.index("\n    }\n", start) + len("\n    }\n")
    tick_block = tick[start:end]

    m1 = Mutation(
        "把 TickBuild（模拟推进）搬进 Main.Expedition.cs",
        "src/Main.Expedition.cs",
        "public partial class Main : Node2D\n{",
        "public partial class Main : Node2D\n{\n" + tick_block,
    )
    # 配套：把 Main.cs 里的原块删掉（否则同名成员 → 编译错）
    m1b = Mutation(
        "（配套）从 Main.cs 删掉 TickBuild",
        "src/Main.cs",
        tick_block,
        "",
    )

    # ── 2. 新方法没被分类 ────────────────────────────────────────────
    m2 = Mutation(
        "在 Main.cs 里加一个没分类的新方法",
        "src/Main.cs",
        "public partial class Main : Node2D\n{",
        "public partial class Main : Node2D\n{\n    private void BrandNewUnclassifiedThing() { }\n",
    )

    # ── 3. 自检文件里混进非自检方法 ──────────────────────────────────
    m3 = Mutation(
        "在 Main.SelfTest.cs 里混进一个非自检方法",
        "src/Main.SelfTest.cs",
        "public partial class Main : Node2D\n{",
        "public partial class Main : Node2D\n{\n    private void NotASelfTestAtAll() { }\n",
    )

    # ── 4. 领域层引入 Godot 依赖 ──────────────────────────────────────
    # 这条守的是 `DomainLayerPurityTests`（**更强**的那份：它剥注释与字符串，
    # 还自带「扫描器非空转」自检）⇒ 变异要对着**那条**跑。
    m4 = Mutation(
        "给 src/Domain/Items/ItemId.cs 加 using Godot",
        "src/Domain/Items/ItemId.cs",
        "namespace ClaudeCodeGameStudios.Domain.Items;",
        "using Godot;\nnamespace ClaudeCodeGameStudios.Domain.Items;",
        filter="FullyQualifiedName~DomainLayerPurityTests",
    )

    # ── 5. 领域层出现 static 可变字段 ────────────────────────────────
    m5 = Mutation(
        "给领域层加一个 static 可变字段",
        "src/Domain/Items/ItemId.cs",
        "namespace ClaudeCodeGameStudios.Domain.Items;",
        "namespace ClaudeCodeGameStudios.Domain.Items;\n\ninternal static class TempLeak\n{\n"
        "    public static int Counter = 0;\n}",
    )

    # ── 6. 职责清单腐化 ──────────────────────────────────────────────
    # 这一条守的是「清单里的成员必须真的存在」。要让��变它**只能改清单**
    # （改源码会连带把调用点一起改掉，那是另一个测试的事）。
    # 所以这里变异的是**守卫自己**：往清单里塞一个不存在的成员名。
    m6 = Mutation(
        "往职责清单里塞一个不存在的成员（清单腐化）",
        "tests/unit/MainStructureTests.cs",
        '"BuildSave", "BuildBagSave", "ApplySave",',
        '"BuildSave", "BuildBagSave", "ApplySave", "NoSuchMemberExists",',
    )

    # ── 7. 单文件行长上限 ────────────────────────────────────────────
    m7 = Mutation(
        "把 Main.cs 撑到 1200 行以上",
        "src/Main.cs",
        "public partial class Main : Node2D\n{",
        "public partial class Main : Node2D\n{\n" +
        "".join(f"    // 填充行 {i}\n" for i in range(1300)),
    )

    results = []
    # m1 与 m1b 必须**同时**生效 ⇒ 手工成对执行
    p1 = ROOT / "src/Main.Expedition.cs"
    p2 = ROOT / "src/Main.cs"
    b1, b2 = p1.read_bytes(), p2.read_bytes()
    try:
        m1.apply()
        m1b.apply()
        failed, out = run_tests()
        ok = failed > 0
        print(f"{'OK  ' if ok else '!!  '}变异 [{m1.label} + {m1b.label}] -> "
              f"{'RED  ' if failed > 0 else 'GREEN'} (failed={failed}, 期望 RED)")
        results.append(ok)
    finally:
        p1.write_bytes(b1)
        p2.write_bytes(b2)

    for m in (m2, m3, m4, m5, m6, m7):
        results.append(m.run())

    print("---")
    bad = results.count(False)
    print(f"变异矩阵：{len(results) - bad}/{len(results)} 符合预期")
    return 1 if bad else 0


if __name__ == "__main__":
    sys.exit(main())
