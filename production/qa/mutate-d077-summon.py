"""
对 D-077「召唤消耗」的守卫做**变异看红**验证。

**它守的是什么**（基线 §10.8 的 S1 / S4 / S5）：
  ① 钱不够 ⇒ 拒绝，**一文钱都不扣**
  ② 扣费准确：恰好少 100
  ③ **资源不会变成负数**
  ④ 能退钱（召唤没成时不留半笔账）

⚠️ **最要紧的一档**是「扣费不检查余额」——
若服务直接扣钱而**不先查**（或查错了地方），
资源就可能**变成负数** ⇒ 那是 I12 的破口。
⇒ 这一档**必须红**。

用法：python production/qa/mutate-d077-summon.py
"""
try:
    import _utf8out  # noqa: F401  — 控制台默认 GBK，印特殊符号会直接崩
except ImportError:
    pass

import hashlib
import re
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
TESTS = ROOT / "tests" / "Claude-Code-Game-Studios.Tests.csproj"
FILTER = "FullyQualifiedName~SummonServiceTests"

SERVICE = ROOT / "src" / "Application" / "SummonService.cs"
VILLAGE = ROOT / "src" / "Domain" / "Village" / "Village.cs"


def sha(p: Path) -> str:
    return hashlib.sha256(p.read_bytes()).hexdigest()


def run_tests():
    # ⚠️ **必须带构建**（不要加 `--no-build`）：实测在本机 `--no-build` 会让
    # vstest 挂死（进程在、CPU 不涨），而带构建的同一条命令只要几秒。
    # ⇒ 变异改的是源码 ⇒ 每次都必须重新编译进程序集。
    try:
        r = subprocess.run(
            ["dotnet", "test", str(TESTS), "-c", "Release", "--nologo", "-v", "q",
             "--filter", FILTER],
            cwd=ROOT, capture_output=True, text=True, encoding="utf-8", errors="replace",
            timeout=300)
    except subprocess.TimeoutExpired:
        # ⚠️ **挂死不是「跑红」**：守卫的循环被改坏时会永远等下去
        # ⇒ 这一档必须**明确报出来**（TIMEOUT），不能当成「没红 ⇒ 守卫生效」。
        _kill_dotnet()
        return -2, "TIMEOUT: 测试超过 300 秒未结束（守卫挂死，不是跑红）"
    out = r.stdout + r.stderr
    counts = re.findall(r"(?:失败|Failed):\s*(\d+)", out)
    if counts:
        return int(counts[-1]), out
    return -1, out


def _kill_dotnet():
    """清掉挂死的测试宿主（否则它会一直占着 CPU 与文件锁）。"""
    subprocess.run(["taskkill", "/F", "/IM", "testhost.exe"], capture_output=True, text=True)
    subprocess.run(["taskkill", "/F", "/IM", "dotnet.exe"], capture_output=True, text=True)


def mutate(label, target: Path, old, new):
    """改一处 → 跑测试 → 要求**红** → 复原文件（逐字节）。"""
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
            print(f"!!  变异 [{label}] -> TIMEOUT（守卫挂死，不是跑红）")
            return False
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


def main() -> int:
    results = []

    failed, out = run_tests()
    if failed != 0:
        print(f"!! 基线就是红的（failed={failed}）—— 先修好再谈变异")
        for line in out.splitlines():
            if "失败" in line or "Expecting" in line:
                print("      " + line.strip()[:160])
        return 1
    print("OK  基线（未变异）绿 —— 那四条性质当前成立")

    # ── ① 扣费不检查余额 ⇒ 资源可能变成负数 ⇒ 必须红 ──────────────────
    # ⚠️ 变异点选在**真正握着余额检查的那一行**（`Village.TrySpend`），
    # 而不是服务层那层冗余的外层检查 —— 外层检查被去掉时 `TrySpend` 仍会兜住
    # ⇒ 那样变异**不改变行为**，抓不到任何东西。
    results.append(mutate(
        "Village.TrySpend 不检查余额（资源可能变成负数）",
        VILLAGE,
        "        if (Get(resource) < amount) return false;",
        "        // 变异：不查余额就扣（资源会变成负数）",
    ))

    # ── ② 扣的金额不是 100（扣 1 金）⇒ 必须红 ─────────────────────────
    results.append(mutate(
        "扣的金额变成 1 金",
        SERVICE,
        "    public bool TryPayCost() => _village.TrySpend(_gold, GoldCost);",
        "    public bool TryPayCost() => _village.TrySpend(_gold, 1);   // 变异：扣得少了",
    ))

    # ── ③ 召唤费被写成 0（免费召唤）⇒ 必须红 ───────────────────────────
    results.append(mutate(
        "召唤费变成 0（免费召唤）",
        SERVICE,
        "    public const long GoldCost = 100;",
        "    public const long GoldCost = 0;   // 变异：免费",
    ))

    # ── ④ 退款变成不还钱 ⇒ 必须红 ─────────────────────────────────────
    results.append(mutate(
        "退款不还钱（召唤没成时的半笔账）",
        SERVICE,
        "        _village.Add(_gold, amount);",
        "        // 变异：不还钱",
    ))

    print("---")
    bad = results.count(False)
    print(f"变异矩阵：{len(results) - bad}/{len(results)} 符合预期")
    return 1 if bad else 0


if __name__ == "__main__":
    sys.exit(main())
