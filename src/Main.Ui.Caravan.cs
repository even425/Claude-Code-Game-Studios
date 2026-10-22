using Godot;
using System;
using System.Collections.Generic;
using ClaudeCodeGameStudios.Application;

public partial class Main : Node2D
{
    /// <summary>左栏的「看看商队」按钮（D-081）。</summary>
    private Button? _btnCaravan;

    /// <summary>商队面板（D-081）。<b>按需创建</b> —— 它要商队服务，而那在簿建好之后才有。</summary>
    private ClaudeCodeGameStudios.Presentation.CaravanMarketPanel? _caravanPanel;

    /// <summary>
    /// 打开商队面板（D-081）。
    /// </summary>
    /// <remarks>
    /// ⚠️ **没有商队在售时也要给回执**（「现在没有商队在村里」）——
    /// 按钮点了却毫无反应，玩家会以为游戏卡了。
    /// </remarks>
    private void OnCaravan()
    {
        if (_caravanPanel is null)
        {
            _caravanPanel = new ClaudeCodeGameStudios.Presentation.CaravanMarketPanel(
                this,
                () => _heroes,
                _caravanMarket,
                () => _clock.Now,
                OnCaravanBought);
        }

        _caravanPanel.Open();
    }

    /// <summary>
    /// 商队成交之后：发一条可追溯的事件（杂谈里能看到「谁从商队买了什么」）。
    /// </summary>
    /// <param name="result">成交结果。</param>
    /// <remarks>
    /// ⚠️ **用独立的事件类型**（<c>CaravanTrade</c>），不并进 <c>TradeCompleted</c>：
    /// 商队成交的**钱全进村库**（不是 5% 摊位费）⇒ 若并进交易事件，
    /// 经济流水的「村抽 = 成交额 5%」那条等式会被这一行**弄坏**。
    /// </remarks>
    private void OnCaravanBought(CaravanBuyResult result)
    {
        string buyerName = NameOfHero(result.Buyer);

        var changed = new List<ClaudeCodeGameStudios.Domain.Events.StateKey>
        {
            StHeroWallet,
            StHeroBag,
            StVillageResource,
        };

        var payload = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["buyer"] = buyerName,
            ["item"] = result.ItemId,
            ["qty"] = result.Quantity.ToString(),
            ["unitPrice"] = result.UnitPrice.ToString(),
            ["amount"] = result.TotalAmount.ToString(),
            ["remaining"] = result.RemainingStock.ToString(),
            // D-088：钱分两路（村库抽成 10% / 记为销毁 90%）⇒ 流水要能核对这两笔。
            ["villageCut"] = result.VillageCut.ToString(),
            ["destroyed"] = result.Destroyed.ToString(),
        };

        Emit(EvCaravanTrade, buyerName, changed, payload,
            ev => Chronicle.CaravanBought(
                (int)_clock.Day,
                ev.Payload["buyer"],
                ev.Payload["item"],
                int.Parse(ev.Payload["qty"]),
                int.Parse(ev.Payload["unitPrice"])).Text);

        // 钱与货都变了 ⇒ 花名册上的数字要跟着变。
        RefreshRoster();
    }
}
