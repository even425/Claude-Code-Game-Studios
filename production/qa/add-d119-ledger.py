# -*- coding: utf-8 -*-
"""D-119：把装备线的新数值登记进参数台账（production/qa/parameters.json）。

只做三件事：
1. parameters 表加 5 个新常量（磨损 / 补货 / 库存上限）。
2. tables 表更新两张：物品稀有度映射、加装备四件；价格表加装备四件。
3. 写回时保持 JSON 结构与缩进（ensure_ascii=False, indent=2）。
"""
import io
import json
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import _utf8out  # noqa: F401  （Windows 控制台 GBK ⇒ 中文输出要转码）

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
PATH = os.path.join(ROOT, "production", "qa", "parameters.json")

with io.open(PATH, "r", encoding="utf-8", newline="") as f:
    doc = json.load(f)

new_params = [
    {
        "file": "Application/EquipmentService.cs",
        "name": "WearDownPerBattle",
        "kind": "参数",
        "status": "【建议值】D-119",
        "note": "每打一场战斗，装备掉的耐久（副本 / 试炼塔 / 狩猎 / 兽潮四处一致）",
        "value": "1",
    },
    {
        "file": "Application/EquipmentShopService.cs",
        "name": "RestockMaterialCost",
        "kind": "参数",
        "status": "【建议值】D-119",
        "note": "商店补一件装备消耗的村库材料份数（材料过剩的出口）",
        "value": "1",
    },
    {
        "file": "Application/EquipmentShopService.cs",
        "name": "RestockGoldCost",
        "kind": "参数",
        "status": "【建议值】D-119",
        "note": "商店补一件装备消耗的村库金币（记进村庄资产 ⇒ 钱不凭空消失）",
        "value": "5",
    },
    {
        "file": "Application/EquipmentShopService.cs",
        "name": "MaxStockPerItem",
        "kind": "参数",
        "status": "【建议值】D-119",
        "note": "同一种装备在一家店里的库存上限",
        "value": "3",
    },
    {
        "file": "Application/EquipmentShopService.cs",
        "name": "MaxStockPerShop",
        "kind": "参数",
        "status": "【建议值】D-119",
        "note": "一家店的总库存上限",
        "value": "6",
    },
]

existing = {(r["file"], r["name"]) for r in doc["parameters"]}
for row in new_params:
    key = (row["file"], row["name"])
    if key in existing:
        for i, r in enumerate(doc["parameters"]):
            if (r["file"], r["name"]) == key:
                doc["parameters"][i] = row
    else:
        doc["parameters"].append(row)

for t in doc["tables"]:
    if t["id"] == "item-rarity-mapping":
        for add in ("石剑=Common", "猎弓=Uncommon", "皮甲=Common", "锁甲=Uncommon"):
            if add not in t["expected"]:
                t["expected"].append(add)
        t["note"] = ("物品 → 稀有度映射（石料/木料 = 普通，布匹 = 优秀，"
                     "兽皮/兽肉 = 普通；**D-119 新增装备四件**：石剑/皮甲 = 普通，"
                     "猎弓/锁甲 = 优秀）。**占位**，阶段 5 会一并复核")
    elif t["id"] == "base-prices":
        for add in ("石剑=30", "猎弓=45", "皮甲=25", "锁甲=40"):
            if add not in t["expected"]:
                t["expected"].append(add)
        t["expected"] = sorted(t["expected"])
        t["note"] = ("物品单价（石料 3 / 木料 2 / 布匹 5）—— 2026-10-21 开发者拍板（P-14），"
                     "**不再是占位值**。**D-119 新增装备四件**（石剑 30 / 猎弓 45 / 皮甲 25 / 锁甲 40，建议值）。"
                     "**登记的是外部配置那一份**（运行时真正生效的那份），内置兜底 `PriceBook.Default` "
                     "由 `PriceBookConfigConsistencyTests` 比对，两份不许分叉")

with io.open(PATH, "w", encoding="utf-8", newline="") as f:
    json.dump(doc, f, ensure_ascii=False, indent=2)
    f.write("\n")

print("OK parameters=%d tables=%d" % (len(doc["parameters"]), len(doc["tables"])))
