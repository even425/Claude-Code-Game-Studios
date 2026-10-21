"""
对「真机自检不依赖占位价」这条承诺做**变异看红**验证。

**它守的是什么**：`production/qa/price-change-check.py` 证明的是
「换一张价格表 ⇒ 那几个自检仍然全绿」。

⚠️ **为什么这个承诺需要变异看红**：它是一条**否定式**的承诺
（「自检里没有写死价格」）—— 而「没有写死」这种事，**光看代码看不出来**：
数字就摆在那一行，只是它恰好等于今天的占位价。
⇒ 唯一能证明的方式是**故意把它写回去**，看检查会不会红。
⇒ 不做这一步，这条检查可能只是个「永远绿」的摆设。

三档变异（每一档都把一个写死的单价塞回自检里）：

  ① `--trade`：买家付款期望写死回 **10**（= 2 份 × 5 金）
  ② `--dungeon`：战利品开摊的付款期望写死回 **5**
  ③ `--market`：卖家净得期望写死回 **24**（= 25 − 1 村抽）

⇒ 每一档都必须让 `price-change-check.py` **红**（退出码非 0）。

用法：python production/qa/mutate-price-agnostic-selfcheck.py
"""
try:
    import _utf8out  # noqa: F401  — 控制台默认 GBK，印特殊符号会直接崩
except ImportError:
    pass

import hashlib
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
SELFTEST = ROOT / "src" / "Main.SelfTest.cs"
CHECK = ROOT / "production" / "qa" / "price-change-check.py"


def sha(p: Path) -> str:
    return hashlib.sha256(p.read_bytes()).hexdigest()


def run_check():
    """跑那条检查。返回 (退出码, 输出)。"""
    r = subprocess.run(
        [sys.executable, str(CHECK)], cwd=ROOT, capture_output=True, text=True,
        encoding="utf-8", errors="replace")
    return r.returncode, (r.stdout + r.stderr)


def rebuild():
    """重新构建 Debug —— 自检跑的是编译产物 ⇒ 改了源码必须重建才看得见。"""
    return subprocess.run(["dotnet", "build", "--nologo", "-v", "q"], cwd=ROOT,
                          capture_output=True, text=True, encoding="utf-8",
                          errors="replace").returncode


def mutate(label, old, new):
    """改一处 → 跑检查 → 要求**红** → 复原文件（逐字节）。"""
    backup = SELFTEST.read_bytes()
    before = sha(SELFTEST)
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
    SELFTEST.write_text(mutated, encoding="utf-8", newline="")
    try:
        code, out = run_check()
        ok = code != 0        # 检查必须**失败**（红）才算变异成立
        print(f"{'OK  ' if ok else '!!  '}变异 [{label}] -> "
              f"{'RED' if ok else 'GREEN'} (检查退出码 {code}，期望非 0)")
        if not ok:
            for line in out.splitlines():
                if "!!" in line or "OK" in line:
                    print("      " + line.strip()[:160])
        return ok
    finally:
        SELFTEST.write_bytes(backup)
        if sha(SELFTEST) != before:
            print(f"!! 复原失败 [{label}]")
        # ⚠️ **复原后必须重建**：自检跑的是**编译产物** ⇒ 不重建的话，
        # 工作区里会留着**变异体**的二进制 ⇒ 下一轮自检验的是被改坏的代码。
        rebuild()


def main() -> int:
    results = []

    code, out = run_check()
    if code != 0:
        print(f"!! 基线就是红的（检查退出码 {code}）—— 先修好再谈变异")
        for line in out.splitlines():
            if "!!" in line:
                print("      " + line.strip()[:160])
        return 1
    print("OK  基线（未变异）绿 —— 那条承诺当前成立")

    # ── ① `--trade` 的付款期望写死回 10 ────────────────────────────────
    results.append(mutate(
        "`--trade` 把买家付款期望写死回 10（2 份 × 5 金）",
        "&& buyerGold0 - buyerGold1 == gross2",
        "&& buyerGold0 - buyerGold1 == 10",
    ))

    # ── ② `--dungeon` 的付款期望写死回 5 ───────────────────────────────
    results.append(mutate(
        "`--dungeon` 把战利品开摊的付款期望写死回 5",
        "&& buyer.Wallet.Amount(gold) == buyerGold0 - unitPrice;",
        "&& buyer.Wallet.Amount(gold) == buyerGold0 - 5;",
    ))

    # ── ③ `--market` 的卖家净得写死回 24 ───────────────────────────────
    results.append(mutate(
        "`--market` 把卖家净得期望写死回 24（25 − 1 村抽）",
        "&& sellerGold1 - sellerGold0 == expectedNet",
        "&& sellerGold1 - sellerGold0 == 24",
    ))

    print("---")
    bad = results.count(False)
    print(f"变异矩阵：{len(results) - bad}/{len(results)} 符合预期")
    return 1 if bad else 0


if __name__ == "__main__":
    sys.exit(main())
