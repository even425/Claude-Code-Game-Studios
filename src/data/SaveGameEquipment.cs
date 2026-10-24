using System.Collections.Generic;
using ClaudeCodeGameStudios.Domain.Items;

namespace ClaudeCodeGameStudios.Data;

/// <summary>
/// 存档校验里的<b>装备部分</b>（D-119）—— 从 <c>SaveGame.cs</c> 拆出来。
/// </summary>
/// <remarks>
/// <para>⚠️ <b>为什么拆</b>：<c>SaveGame.cs</c> 已经顶到 UT-01 的 1200 行上限
/// ⇒ 装备线的新增校验（两段、约 90 行）单独住一个文件 ⇒ 两边都不越线。</para>
///
/// <para>⚠️ <b>规则只在这里有</b>：装备 Id 必须已登记 / 槽位要对 / 耐久在 0..上限 /
/// 没穿却有耐久 ⇒ 四条，各有各的报错路径 ⇒ 守卫能断言「<b>是哪一条</b>拒的」。</para>
/// </remarks>
public static partial class SaveValidator
{
    /// <summary>
    /// <b>商店货架的存档检查</b>（0.12 新增 · D-119）。
    /// </summary>
    /// <param name="errors">错误清单（就地追加）。</param>
    /// <param name="shops">存档里的货架清单。</param>
    public static void ValidateShopStock(List<SaveError> errors, List<ShopStockSave> shops)
    {
        if (errors is null) return;
        if (shops is null) return;

        for (var i = 0; i < shops.Count; i++)
        {
            var shop = shops[i];
            var path = $"village.shops[{i}]";

            if (shop is null) continue;

            if (string.IsNullOrEmpty(shop.ShopId))
            {
                errors.Add(new SaveError($"{path}.shopId", "店铺标识为空。"));
            }

            for (var j = 0; j < shop.Items.Count; j++)
            {
                var item = shop.Items[j];
                var itemPath = $"{path}.items[{j}]";
                if (item is null) continue;

                if (item.Count < 0)
                {
                    errors.Add(new SaveError($"{itemPath}.count", $"货架数量 {item.Count} 为负。"));
                }

                if (string.IsNullOrEmpty(item.ItemId))
                {
                    errors.Add(new SaveError($"{itemPath}.itemId", "货架上的物品标识为空。"));
                    continue;
                }

                if (EquipmentCatalog.Default.Find(new ItemId(item.ItemId)) is null)
                {
                    errors.Add(new SaveError(
                        $"{itemPath}.itemId",
                        $"货架上的「{item.ItemId}」不是已登记的装备 ⇒ 读档后会变成一件说不清是什么的货。"));
                }
            }
        }
    }

    /// <summary>
    /// <b>一个装备槽位的存档检查</b>（0.12 新增 · D-119）。
    /// </summary>
    /// <param name="errors">错误清单（就地追加）。</param>
    /// <param name="path">英雄条目的路径前缀。</param>
    /// <param name="itemId">装备的物品 Id（空串 = 没穿）。</param>
    /// <param name="durability">当前耐久。</param>
    /// <param name="slot">应当属于哪个槽位。</param>
    /// <param name="idField">Id 字段名（用于报错路径）。</param>
    /// <param name="durabilityField">耐久字段名（用于报错路径）。</param>
    /// <remarks>
    /// ⚠️ <b>三条规则各有各的 path</b>：未登记 / 槽位不对 / 耐久越界 / 没穿却有耐久
    /// ⇒ 守卫能断言「<b>是哪一条</b>拒的」，而不是只说「整体非法」。
    /// </remarks>
    private static void ValidateEquipment(
        List<SaveError> errors,
        string path,
        string itemId,
        int durability,
        EquipmentSlot slot,
        string idField,
        string durabilityField)
    {
        bool empty = string.IsNullOrEmpty(itemId);

        if (empty)
        {
            // 没穿 ⇒ 耐久必须是 0（否则是残留数据 ⇒ 读出来会变成「幽灵耐久」）。
            if (durability != 0)
            {
                errors.Add(new SaveError(
                    $"{path}.{durabilityField}",
                    $"没穿装备却有耐久 {durability}（没穿时耐久必须是 0）。"));
            }

            return;
        }

        var def = EquipmentCatalog.Default.Find(new ItemId(itemId));

        if (def is null)
        {
            errors.Add(new SaveError(
                $"{path}.{idField}",
                $"装备「{itemId}」不是已登记的装备 ⇒ 读档后会变成一件说不清是什么的东西。"));
            return;
        }

        if (def.Slot != slot)
        {
            errors.Add(new SaveError(
                $"{path}.{idField}",
                $"装备「{itemId}」属于「{def.Slot}」槽，不能放在「{slot}」槽里。"));
            return;
        }

        if (durability < 0 || durability > def.DurabilityMax)
        {
            errors.Add(new SaveError(
                $"{path}.{durabilityField}",
                $"装备「{itemId}」的耐久 {durability} 超出合法范围 [0, {def.DurabilityMax}]。"));
        }
    }
}
