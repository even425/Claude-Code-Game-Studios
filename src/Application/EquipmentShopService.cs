using System;
using System.Collections.Generic;
using System.Linq;
using ClaudeCodeGameStudios.Domain.Items;
using ClaudeCodeGameStudios.Domain.Village;

namespace ClaudeCodeGameStudios.Application;

/// <summary>一次购买的结论（D-119）。</summary>
/// <param name="Succeeded">买成了吗。</param>
/// <param name="Reason">没买成的原因（买成时为空串）。</param>
/// <param name="Item">买到的装备。</param>
/// <param name="Price">付掉的钱。</param>
public readonly record struct ShopPurchaseResult(
    bool Succeeded,
    string Reason,
    ItemId? Item,
    long Price)
{
    /// <summary>失败（带原因）。</summary>
    /// <param name="reason">原因。</param>
    /// <returns>失败结果。</returns>
    public static ShopPurchaseResult Fail(string reason) => new(false, reason, null, 0);

    /// <summary>成功。</summary>
    /// <param name="item">买到的装备。</param>
    /// <param name="price">付掉的钱。</param>
    /// <returns>成功结果。</returns>
    public static ShopPurchaseResult Ok(ItemId item, long price) => new(true, "", item, price);
}

/// <summary>
/// <b>装备商店</b>（D-119）—— 村庄卖装备给英雄：补货、库存、买卖。
/// </summary>
/// <remarks>
/// <para>⚠️ <b>为什么要有它</b>：仓库里原本只有两种交易 ——
/// <b>村民摊位</b>（英雄 ↔ 英雄，<c>StallService</c>）与
/// <b>商队</b>（外部 → 英雄，<c>CaravanMarketService</c>），
/// <b>没有「村庄自己的商店」</b> ⇒ 武器店 / 服装店没有可用的链路。</para>
///
/// <para>⚠️ <b>钱的流向必须守得住</b>：
/// <list type="bullet">
/// <item><b>英雄买装备</b>：英雄钱包 −价 ⇒ <b>村库 +价</b>（内部转移 ⇒ 守恒律两边不变）。</item>
/// <item><b>修理</b>：英雄钱包 −修理费 ⇒ <b>村库 +修理费</b>（同上）。</item>
/// <item><b>补货</b>：村库 −金币 −材料 ⇒ <b>村庄资产 +金币</b>（钱变成了货架上的货；
/// 与 D-108「建造花掉的钱记进村庄资产」同一套账）⇒ <b>不能</b>让那笔钱凭空消失。</item>
/// </list></para>
///
/// <para>⚠️ <b>库存是村庄的状态 ⇒ 必须入档</b>：
/// 不存 ⇒ 读档后货架空了 ⇒ 玩家刚买到的装备「凭空买不到」
/// （基线 §15 明列「加载时要避免装备丢失或重复」）。</para>
/// </remarks>
public sealed class EquipmentShopService
{
    /// <summary>补一件装备消耗的<b>村库材料</b>（**建议值**，允许调）。</summary>
    public const long RestockMaterialCost = 1L;

    /// <summary>补一件装备消耗的<b>村库金币</b>（**建议值**，允许调）。</summary>
    public const long RestockGoldCost = 5L;

    /// <summary>同一种装备的库存上限（**建议值**）。</summary>
    public const long MaxStockPerItem = 3L;

    /// <summary>一家店的总库存上限（**建议值**）。</summary>
    public const long MaxStockPerShop = 6L;

    private readonly Dictionary<string, Dictionary<ItemId, long>> _stock = new(StringComparer.Ordinal);
    private readonly EquipmentCatalog _catalog;
    private readonly ResourceId _gold;
    private readonly ResourceId _material;

    /// <summary>造一个商店服务。</summary>
    /// <param name="catalog">装备注册表。</param>
    /// <param name="gold">金币的资源种类。</param>
    /// <param name="material">材料的资源种类。</param>
    /// <exception cref="ArgumentNullException"><paramref name="catalog"/> 为 null。</exception>
    public EquipmentShopService(EquipmentCatalog catalog, ResourceId gold, ResourceId material)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _gold = gold;
        _material = material;
    }

    /// <summary>装备注册表（谁在卖）。</summary>
    public EquipmentCatalog Catalog => _catalog;

    /// <summary>某家店的库存（物品 → 数量）。</summary>
    /// <param name="shopId">店铺（建筑）标识。</param>
    /// <returns>库存；没有这家店时是空表。</returns>
    public IReadOnlyDictionary<ItemId, long> StockOf(BuildingId shopId)
    {
        if (_stock.TryGetValue(shopId.Value, out var items))
        {
            return new Dictionary<ItemId, long>(items);
        }

        return new Dictionary<ItemId, long>();
    }

    /// <summary>某家店里某件装备还有几件。</summary>
    /// <param name="shopId">店铺（建筑）标识。</param>
    /// <param name="item">装备。</param>
    /// <returns>数量（0 = 没有）。</returns>
    public long CountOf(BuildingId shopId, ItemId item)
    {
        if (!_stock.TryGetValue(shopId.Value, out var items)) return 0;
        return items.TryGetValue(item, out var qty) ? qty : 0;
    }

    /// <summary>某家店的总库存件数。</summary>
    /// <param name="shopId">店铺（建筑）标识。</param>
    /// <returns>总件数。</returns>
    public long TotalStock(BuildingId shopId)
    {
        if (!_stock.TryGetValue(shopId.Value, out var items)) return 0;
        return items.Values.Sum();
    }

    /// <summary>
    /// <b>每日补货</b>：每家店每天最多补 <b>1 件</b>（材料 + 金币各一份）。
    /// </summary>
    /// <param name="village">村庄（补货的钱与材料从这里出）。</param>
    /// <param name="shopIds">今天要补货的店铺（<b>只传已建成的</b>）。</param>
    /// <param name="slot">这家店卖哪一类装备。</param>
    /// <returns>这次补上了什么（店铺 → 装备）。</returns>
    /// <remarks>
    /// ⚠️ <b>补什么由「最缺的那种」决定</b>（数量最少 ⇒ 最缺），
    /// 同缺时按装备 Id 排序 ⇒ <b>确定的</b>（同样的输入 ⇒ 同样的结果 ⇒ 可复现）。
    /// </remarks>
    public IReadOnlyList<(BuildingId Shop, ItemId Item)> Restock(
        Village village,
        IEnumerable<BuildingId> shopIds,
        EquipmentSlot slot)
    {
        if (village is null) throw new ArgumentNullException(nameof(village));
        if (shopIds is null) throw new ArgumentNullException(nameof(shopIds));

        var result = new List<(BuildingId, ItemId)>();

        foreach (var shopId in shopIds)
        {
            if (TotalStock(shopId) >= MaxStockPerShop) continue;

            var candidate = PickRestockItem(shopId, slot);
            if (candidate is null) continue;

            // ⚠️ **钱与材料都要够** ⇒ 缺一样就不补（不半补：那会造成「货来了但没记账」）。
            if (village.Get(_material) < RestockMaterialCost) continue;
            if (village.Get(_gold) < RestockGoldCost) continue;

            bool goldSpent = village.TrySpend(_gold, RestockGoldCost);
            bool materialSpent = village.TrySpend(_material, RestockMaterialCost);
            if (!goldSpent || !materialSpent)
            {
                // 扣款失败 ⇒ 把**真正扣掉的那部分**退回去（退没扣的会造钱）。
                if (goldSpent) village.Add(_gold, RestockGoldCost);
                if (materialSpent) village.Add(_material, RestockMaterialCost);
                continue;
            }

            // ⚠️ **D-108 同一套账**：补货花掉的钱变成了货架上的货 ⇒ 记进「村庄资产」
            // ⇒ 守恒律的左边不变（钱没有凭空消失，只是变成了货）。
            village.Add(VillageAsset.Asset, RestockGoldCost);

            AddStock(shopId, candidate.Value, 1);
            result.Add((shopId, candidate.Value));
        }

        return result;
    }

    /// <summary>挑一件要补的货（<b>最缺的那种</b>；同缺时按 Id 排序 ⇒ 确定）。</summary>
    private ItemId? PickRestockItem(BuildingId shopId, EquipmentSlot slot)
    {
        ItemId? best = null;
        long bestCount = long.MaxValue;

        foreach (var def in _catalog.ForSlot(slot))
        {
            long count = CountOf(shopId, def.Id);
            if (count >= MaxStockPerItem) continue;
            if (count < bestCount)
            {
                bestCount = count;
                best = def.Id;
            }
        }

        return best;
    }

    /// <summary>
    /// <b>英雄买一件装备</b>（钱进村库，装备直接穿上）。
    /// </summary>
    /// <param name="hero">买的人。</param>
    /// <param name="shopId">店铺（建筑）标识。</param>
    /// <param name="item">要买的装备。</param>
    /// <param name="village">村庄（钱进村库）。</param>
    /// <returns>购买结果。</returns>
    /// <remarks>
    /// ⚠️ <b>买来直接穿上，不进背包</b>：装备一旦进背包就会出现
    /// 「背包里那把剑到底穿没穿」的第二个真相 ⇒ 槽位是<b>唯一</b>的真相。
    /// </remarks>
    public ShopPurchaseResult TryBuy(HeroAgent hero, BuildingId shopId, ItemId item, Village village)
    {
        if (hero is null) throw new ArgumentNullException(nameof(hero));
        if (village is null) throw new ArgumentNullException(nameof(village));

        var def = _catalog.Find(item);
        if (def is null) return ShopPurchaseResult.Fail("这不是装备。");

        if (CountOf(shopId, item) <= 0) return ShopPurchaseResult.Fail("店里没有货。");

        long price = def.Price;
        if (hero.Wallet.Amount(_gold) < price) return ShopPurchaseResult.Fail("钱不够。");

        // 扣钱 ⇒ 进村库（**内部转移** ⇒ 守恒律两边不变）。
        if (!hero.TrySpendResource(_gold, price)) return ShopPurchaseResult.Fail("钱不够。");
        village.Add(_gold, price);

        AddStock(shopId, item, -1);
        hero.Equipment = EquipmentService.Equip(hero.Equipment, def, _catalog);

        return ShopPurchaseResult.Ok(item, price);
    }

    /// <summary>
    /// <b>修理</b>：把身上坏了的装备修好（钱进村库）。
    /// </summary>
    /// <param name="hero">要修的人。</param>
    /// <param name="village">村庄（修理费进村库）。</param>
    /// <returns>修理结果；没坏装备 / 钱不够 / 没有铁匠铺时失败。</returns>
    public ShopPurchaseResult TryRepair(HeroAgent hero, Village village)
    {
        if (hero is null) throw new ArgumentNullException(nameof(hero));
        if (village is null) throw new ArgumentNullException(nameof(village));

        if (!hero.Equipment.HasBroken) return ShopPurchaseResult.Fail("没有坏掉的装备。");

        long cost = EquipmentService.RepairCostOf(hero.Equipment, _catalog);
        if (cost <= 0) return ShopPurchaseResult.Fail("没有坏掉的装备。");
        if (hero.Wallet.Amount(_gold) < cost) return ShopPurchaseResult.Fail("钱不够。");

        if (!hero.TrySpendResource(_gold, cost)) return ShopPurchaseResult.Fail("钱不够。");
        village.Add(_gold, cost);

        hero.Equipment = EquipmentService.Repair(hero.Equipment, _catalog);

        return ShopPurchaseResult.Ok(new ItemId("修理"), cost);
    }

    /// <summary>直接改库存（<b>不检查上限</b>；读档恢复用）。</summary>
    /// <param name="shopId">店铺（建筑）标识。</param>
    /// <param name="item">装备。</param>
    /// <param name="delta">增量（可为负）。</param>
    public void AddStock(BuildingId shopId, ItemId item, long delta)
    {
        if (!_stock.TryGetValue(shopId.Value, out var items))
        {
            items = new Dictionary<ItemId, long>(ItemIdComparer.Comparer);
            _stock[shopId.Value] = items;
        }

        long next = (items.TryGetValue(item, out var qty) ? qty : 0) + delta;
        if (next <= 0)
        {
            items.Remove(item);
            return;
        }

        items[item] = next;
    }

    /// <summary>清空某家店的库存（读档覆盖用）。</summary>
    /// <param name="shopId">店铺（建筑）标识。</param>
    public void ClearStock(BuildingId shopId) => _stock.Remove(shopId.Value);
}
