using System;
using System.Collections.Generic;
using System.Linq;
using ClaudeCodeGameStudios.Domain.Heroes;
using ClaudeCodeGameStudios.Domain.Items;
using ClaudeCodeGameStudios.Domain.Village;

namespace ClaudeCodeGameStudios.Application;

/// <summary>英雄这一天在装备上做了什么（D-119）。</summary>
/// <param name="HeroId">谁。</param>
/// <param name="Action">做了什么（修理 / 买武器 / 买衣服 / 没做）。</param>
/// <param name="Item">涉及的装备（没做时为 null）。</param>
/// <param name="Price">花掉的钱。</param>
public readonly record struct EquipmentDayAction(
    HeroId HeroId,
    string Action,
    ItemId? Item,
    long Price);

/// <summary>
/// <b>英雄的自主装备行为</b>（D-119）—— 每天每个英雄自己看：要不要修？要不要买？
/// </summary>
/// <remarks>
/// <para>⚠️ <b>为什么是自主的</b>：开发者拍板「沿用摆摊那套：日切时自己看需求买」
/// ⇒ 玩家<b>不用点</b> ⇒ 村庄自己转起来。</para>
///
/// <para>⚠️ <b>决策顺序</b>（先急的后要的）：
/// <list type="number">
/// <item><b>修</b>：身上有<b>坏了</b>的装备 ⇒ 修理（铁匠铺在才修）—— 坏了的装备不加成，
/// 所以「修」比「买」急。</item>
/// <item><b>买武器</b>：没有武器 ⇒ 去武器店买。</item>
/// <item><b>买衣服</b>：没有衣服 ⇒ 去服装店买。</item>
/// </list>
/// ⇒ 一个英雄<b>一天最多做一件装备的事</b>（修与买互斥 ⇒ 不会一天掏两次钱）。</para>
///
/// <para>⚠️ <b>买哪一件</b>：店里<b>最便宜</b>的那件（价格相同则按 Id 排序 ⇒ 确定）。</para>
/// </remarks>
public sealed class HeroEquipmentService
{
    private readonly EquipmentShopService _shops;
    private readonly EquipmentCatalog _catalog;

    /// <summary>造一个英雄装备行为服务。</summary>
    /// <param name="shops">商店服务。</param>
    /// <exception cref="ArgumentNullException"><paramref name="shops"/> 为 null。</exception>
    public HeroEquipmentService(EquipmentShopService shops)
    {
        _shops = shops ?? throw new ArgumentNullException(nameof(shops));
        _catalog = shops.Catalog;
    }

    /// <summary>
    /// <b>每日补货</b>（商店侧）：已建成的武器店 / 服装店各补至多 1 件。
    /// </summary>
    /// <param name="village">村庄。</param>
    /// <returns>这次补上了什么。</returns>
    public IReadOnlyList<(BuildingId Shop, ItemId Item)> RestockShops(Village village)
    {
        if (village is null) throw new ArgumentNullException(nameof(village));

        var result = new List<(BuildingId, ItemId)>();

        if (BuiltBuildings.IsCompleted(village, BuildableBuildings.WeaponShop.Id))
        {
            result.AddRange(_shops.Restock(village, new[] { BuildableBuildings.WeaponShop.Id }, EquipmentSlot.Weapon));
        }

        if (BuiltBuildings.IsCompleted(village, BuildableBuildings.ArmorShop.Id))
        {
            result.AddRange(_shops.Restock(village, new[] { BuildableBuildings.ArmorShop.Id }, EquipmentSlot.Armor));
        }

        return result;
    }

    /// <summary>
    /// <b>每个英雄每天做一次装备决策</b>。
    /// </summary>
    /// <param name="heroes">候选英雄。</param>
    /// <param name="village">村庄。</param>
    /// <returns>每个人做了什么。</returns>
    public IReadOnlyList<EquipmentDayAction> RunDay(IReadOnlyList<HeroAgent> heroes, Village village)
    {
        if (heroes is null) throw new ArgumentNullException(nameof(heroes));
        if (village is null) throw new ArgumentNullException(nameof(village));

        var actions = new List<EquipmentDayAction>();

        bool weaponShop = BuiltBuildings.IsCompleted(village, BuildableBuildings.WeaponShop.Id);
        bool armorShop = BuiltBuildings.IsCompleted(village, BuildableBuildings.ArmorShop.Id);
        bool blacksmith = BuiltBuildings.IsCompleted(village, TierBuildings.Blacksmith.Id);

        foreach (var hero in heroes)
        {
            // ① 修（坏了的装备没有加成 ⇒ 比买更急）
            if (blacksmith && hero.Equipment.HasBroken)
            {
                var repair = _shops.TryRepair(hero, village);
                if (repair.Succeeded)
                {
                    actions.Add(new EquipmentDayAction(hero.Id, "修理", repair.Item, repair.Price));
                    continue;
                }
            }

            // ② 买武器
            if (weaponShop && hero.Equipment.HasNoWeapon)
            {
                var item = CheapestInStock(BuildableBuildings.WeaponShop.Id, EquipmentSlot.Weapon);
                if (item is not null)
                {
                    var buy = _shops.TryBuy(hero, BuildableBuildings.WeaponShop.Id, item.Value, village);
                    if (buy.Succeeded)
                    {
                        actions.Add(new EquipmentDayAction(hero.Id, "买武器", buy.Item, buy.Price));
                        continue;
                    }
                }
            }

            // ③ 买衣服
            if (armorShop && hero.Equipment.HasNoArmor)
            {
                var item = CheapestInStock(BuildableBuildings.ArmorShop.Id, EquipmentSlot.Armor);
                if (item is not null)
                {
                    var buy = _shops.TryBuy(hero, BuildableBuildings.ArmorShop.Id, item.Value, village);
                    if (buy.Succeeded)
                    {
                        actions.Add(new EquipmentDayAction(hero.Id, "买衣服", buy.Item, buy.Price));
                        continue;
                    }
                }
            }
        }

        return actions;
    }

    /// <summary>店里最便宜的那件（价格相同按 Id 排序 ⇒ 确定）；没有货返回 null。</summary>
    private ItemId? CheapestInStock(BuildingId shopId, EquipmentSlot slot)
    {
        ItemId? best = null;
        long bestPrice = long.MaxValue;

        foreach (var def in _catalog.ForSlot(slot))
        {
            if (_shops.CountOf(shopId, def.Id) <= 0) continue;
            if (def.Price < bestPrice)
            {
                bestPrice = def.Price;
                best = def.Id;
            }
        }

        return best;
    }
}
