"""
对 PlayerCommandAuditTests（IT-03 / A-11 决策链无玩家输入）做**变异看红**验证。

这条断言守的是「玩家不能指定英雄做什么」——
一旦有后门，性格就不再决定行为（基线 §4.2 的前提被架空）。

四档变异对应四道防线：
  ① 给行动态加公开 setter        → 结构防线红
  ② 界面层加一条「指定英雄日常」的后门 → 源码审计红线
  ③ 界面层直接写行动态            → 源码审计红线
  ④ 派遣时把英雄的行动**指定**成某个活动 → 行为防线红

用法：python production/qa/mutate-player-commands.py
"""
import hashlib
import re
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
TESTS = ROOT / "tests" / "Claude-Code-Game-Studios.Tests.csproj"
FILTER = "FullyQualifiedName~PlayerCommandAuditTests"


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
        print(f"!! [{label}] 模式没找到")
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


UI_CLASS_HEAD = "public partial class Main : Node2D\n{"


def main():
    results = []

    # ── ① 结构：给行动态加公开 setter ────────────────────────────────
    results.append(mutate(
        "给 HeroState.CurrentActivity 加公开 setter",
        "src/Domain/Heroes/HeroState.cs",
        "    public ActivityId CurrentActivity { get; private set; } = ActivityId.None;",
        "    public ActivityId CurrentActivity { get; set; } = ActivityId.None;",
    ))

    # ── ② 源码审计：界面层开一条「指定英雄日常」的后门 ──────────────
    results.append(mutate(
        "界面层加一条「让玩家指定英雄日常」的后门",
        "src/Main.Ui.cs",
        UI_CLASS_HEAD,
        UI_CLASS_HEAD + "\n"
        "    private void Mutation_Backdoor_SetHeroActivity()\n"
        "    {\n"
        "        if (_states.Count > 0)\n"
        "        {\n"
        "            _states[0].BeginAction(new ActivityId(\"采集\"), 10.0);\n"
        "        }\n"
        "    }\n",
    ))

    # ── ③ 源码审计：界面层直接写行动态 ──────────────────────────────
    # ⚠️ 注意：`_states[0].CurrentActivity = ...` **编译不过** —— setter 是私有的，
    # 这正是防线①的效果（它把「直接赋值」挡在编译期）。
    # 所以这里换一条**可编译**的写法：调公开的 `ClearTarget()` ——
    # 它同样是写行动态（`CurrentTarget`），而且编译器拦不住。
    results.append(mutate(
        "界面层释放英雄的行动目标（ClearTarget）",
        "src/Main.Ui.cs",
        UI_CLASS_HEAD,
        UI_CLASS_HEAD + "\n"
        "    private void Mutation_ClearHeroTarget()\n"
        "    {\n"
        "        if (_states.Count > 0)\n"
        "        {\n"
        "            _states[0].ClearTarget();\n"
        "        }\n"
        "    }\n",
    ))

    # ── ④ 行为：派遣时把行动「指定」成某个活动 ──────────────────────
    # 注意这一档与「释放」的区别：释放是合法的（英雄离队，手上的活作废），
    # **指定**才是违规。所以变异必须是「指定」而不是「清空」。
    results.append(mutate(
        "派遣时把英雄的行动指定成「副本」（而非释放）",
        "src/Domain/Quests/Quests.cs",
        "        foreach (var m in _members) m.SetOnMission();",
        "        foreach (var m in _members)\n"
        "        {\n"
        "            m.SetOnMission();\n"
        "            m.BeginAction(new ClaudeCodeGameStudios.Domain.Heroes.ActivityId(\"副本\"), 3600.0);\n"
        "        }",
    ))

    print("---")
    bad = results.count(False)
    print(f"变异矩阵：{len(results) - bad}/{len(results)} 符合预期")
    return 1 if bad else 0


if __name__ == "__main__":
    sys.exit(main())
