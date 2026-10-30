using Godot;
using System.Linq;

/// <summary>
/// 主场景的<b>装备店界面</b>接线（D-127）—— 玩家打开商店、给英雄买装备。
/// </summary>
/// <remarks>
/// <para>⚠️ <b>这里只做接线</b>：交易规则在 <c>EquipmentShopService</c>
/// （与 NPC 自主购买**同一份** ⇒ 钱一样进村库 ⇒ 守恒律一样成立）。</para>
///
/// <para>⚠️ <b>为什么要有它</b>：D-119 的装备是「英雄自主购买」⇒ 玩家建了店
/// 却**只能看不能买** ⇒ 自己盖的店用不上手 ⇒ 这里补上**玩家能参与**的那条路。</para>
/// </remarks>
public partial class Main : Node2D
{
    /// <summary>装备店面板。</summary>
    private ClaudeCodeGameStudios.Presentation.EquipmentShopPanel? _equipPanel;

    /// <summary>
    /// 造好装备店面板（<b>要在装备服务之后</b>）。
    /// </summary>
    private void InitEquipmentPanel()
    {
        _equipPanel = new ClaudeCodeGameStudios.Presentation.EquipmentShopPanel(
            this,
            () => _heroes,
            _equipmentShops,
            _villageModel,
            message =>
            {
                GD.Print($"EQUIPSHOP {message}");
                Refresh();
            });
    }

    /// <summary>打开装备店面板。</summary>
    private void OnEquipShop()
    {
        if (_equipPanel is null) InitEquipmentPanel();
        _equipPanel?.Open();
    }
}
