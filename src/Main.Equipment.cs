using Godot;
using System.Linq;

/// <summary>
/// 主场景的<b>装备接线</b>部分（D-119）。
///
/// <para>⚠️ <b>这里只做接线</b>：规则在 <c>EquipmentShopService</c> /
/// <c>HeroEquipmentService</c> / <c>EquipmentService</c>（应用层，可离线单测），
/// 这里只负责「每天什么时候跑一次」与「把结果说一句给玩家听」。</para>
///
/// <para>⚠️ <b>为什么不放在 <c>Main.cs</c></b>：UT-01 的 1200 行上限
/// （<c>Main.cs</c> 已经顶到上限，拆分是为了让每一块职责能单独读）。</para>
/// </summary>
public partial class Main : Node2D
{
    /// <summary>
    /// 装备商店（D-119：补货 / 库存 / 买卖）。
    /// </summary>
    /// <remarks>
    /// ⚠️ **字段放在这里而不是 <c>Main.cs</c></b>：<c>Main.cs</c> 已经顶到
    /// UT-01 的 1200 行上限 ⇒ 装备线的接线整体住在本文件（与 <c>Main.Wildlife.cs</c> 同款做法）。
    /// </remarks>
    private ClaudeCodeGameStudios.Application.EquipmentShopService _equipmentShops = null!;

    /// <summary>英雄的自主装备行为（D-119：自己看要不要修、要不要买）。</summary>
    private ClaudeCodeGameStudios.Application.HeroEquipmentService _heroEquipment = null!;

    /// <summary>「建武器店」按钮（D-119）。</summary>
    private Button _btnBuildWeaponShop = null!;

    /// <summary>「建服装店」按钮（D-119）。</summary>
    private Button _btnBuildArmorShop = null!;

    /// <summary>
    /// 造好装备线的两个服务（<b>要在 <c>_villageModel</c> 之后</b>）。
    /// </summary>
    /// <remarks>
    /// ⚠️ 商店的「钱」与「材料」用的是<b>同一个</b>资源口径（村库金币 / 村库材料桶），
    /// 与建造、收购站完全一致 ⇒ 守恒律只有一套科目。
    /// </remarks>
    private void InitEquipmentServices()
    {
        _equipmentShops = new ClaudeCodeGameStudios.Application.EquipmentShopService(
            ClaudeCodeGameStudios.Domain.Items.EquipmentCatalog.Default,
            ClaudeCodeGameStudios.Application.Stall.StallService.Gold,
            MaterialsId);
        _heroEquipment = new ClaudeCodeGameStudios.Application.HeroEquipmentService(_equipmentShops);
    }

    /// <summary>
    /// <b>每日装备回合</b>（D-119）：商店补货 → 英雄自己决定要不要修 / 买。
    /// </summary>
    /// <remarks>
    /// ⚠️ <b>顺序有讲究</b>：
    /// <list type="number">
    /// <item><b>先补货</b>：不然新货要到第二天才卖得出（英雄今天想买却没货）。</item>
    /// <item><b>后买</b>：补货花的是<b>村库</b>的钱与材料，买装备花的是<b>英雄</b>的钱
    /// ⇒ 两边不冲突，但先补货更符合直觉（店里有货才谈得上买）。</item>
    /// </list>
    /// </remarks>
    private void EquipmentRound()
    {
        // ① 商店补货（只有**已建成**的商店才补）
        foreach (var (shop, item) in _heroEquipment.RestockShops(_villageModel))
        {
            GD.Print($"EQUIP 补货 {shop.Value}：进了一件 {item.Value}");
        }

        // ② 英雄自主购买 / 修理（每人每天最多一件装备的事）
        foreach (var act in _heroEquipment.RunDay(_heroes, _villageModel))
        {
            GD.Print($"EQUIP {act.HeroId.Value} {act.Action}：{act.Item?.Value}（{act.Price} 金）");
        }
    }

    /// <summary>
    /// 某个英雄身上的装备摘要（给花名册用）。
    /// </summary>
    /// <param name="hero">英雄。</param>
    /// <returns>例如「石剑(18/20) 皮甲(6/18)」；没穿任何东西时返回空串。</returns>
    /// <remarks>
    /// ⚠️ <b>纯展示</b>：这里不判定「这件装备好不好」，
    /// 判定在 <c>EquipmentCatalog</c> / <c>EquipmentStats</c>。
    /// </remarks>
    private static string GearSummary(HeroAgent hero)
    {
        if (hero is null) return "";

        var catalog = ClaudeCodeGameStudios.Domain.Items.EquipmentCatalog.Default;
        var gear = hero.Equipment;
        var parts = new System.Collections.Generic.List<string>();

        if (gear.Weapon is { } weapon)
        {
            var def = catalog.Find(weapon);
            if (def is not null) parts.Add($"{def.DisplayName}({gear.WeaponDurability}/{def.DurabilityMax})");
        }

        if (gear.Armor is { } armor)
        {
            var def = catalog.Find(armor);
            if (def is not null) parts.Add($"{def.DisplayName}({gear.ArmorDurability}/{def.DurabilityMax})");
        }

        return string.Join(" ", parts);
    }
}
