using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using ClaudeCodeGameStudios.Application;
using ClaudeCodeGameStudios.Domain.Heroes;

namespace ClaudeCodeGameStudios.Presentation;

/// <summary>
/// <b>商队面板</b>（D-081）：商队来访期间，看它卖什么、多少钱、还剩多少，以及**谁来买**。
/// </summary>
/// <remarks>
/// <para><b>两跳交互</b>（与 <see cref="StallMarketPanel"/> 同构）：先点一档货，再点一个买家。
/// 本项目<b>没有「玩家化身」</b>：钱和货都记在具体某个英雄名下（R-01）⇒
/// 面板把「这次由谁买」摆到台面上，而不是替玩家猜一个人。</para>
///
/// <para>⚠️ <b>一次买一份</b>：商队的货是<b>限量</b>的（限量是「不产生无限套利」的前提）
/// ⇒ 一次点「买尽可能多」会把整摊搬空 ⇒ 每次点击只买 1 份，让玩家自己决定买多少。</para>
///
/// <para><b>它不做什么</b>：不结算。成交一律走
/// <see cref="CaravanMarketService.TryBuy"/>（唯一结算入口），
/// 本类只负责「把玩家点了什么」翻译成调用，以及把结果显示出来。</para>
///
/// <para><b>失败不进杂谈</b>：面板内当场回执即可 —— 杂谈是「村里发生了什么」，
/// 不是「玩家每次点击的回执」。</para>
/// </remarks>
public sealed class CaravanMarketPanel
{
    // ── 依赖 ─────────────────────────────────────────────────────────────

    private readonly Node _host;
    private readonly Func<IReadOnlyList<HeroAgent>> _heroes;
    private readonly CaravanMarketService _market;
    private readonly Func<double> _simTime;
    private readonly Action<CaravanBuyResult> _onBought;

    // ── 节点 ─────────────────────────────────────────────────────────────

    private Panel? _panel;
    private Label? _title;
    private Label? _hint;
    private Godot.ItemList? _offerList;
    private Label? _buyerTitle;
    private Godot.ItemList? _buyerList;
    private Label? _result;

    /// <summary>当前选中的报价（按实例标识记，成交后报价会变）。</summary>
    private string? _selectedInstanceId;

    /// <param name="host">面板挂载的父节点（场景根）。</param>
    /// <param name="heroes">当前村庄英雄的提供方。</param>
    /// <param name="market">商队市场（在售列表 + 结算入口）。</param>
    /// <param name="simTime">当前模拟时间（成交时间戳）。</param>
    /// <param name="onBought">成交回调：发事件 + 刷新花名册等。</param>
    public CaravanMarketPanel(
        Node host,
        Func<IReadOnlyList<HeroAgent>> heroes,
        CaravanMarketService market,
        Func<double> simTime,
        Action<CaravanBuyResult> onBought)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _heroes = heroes ?? throw new ArgumentNullException(nameof(heroes));
        _market = market ?? throw new ArgumentNullException(nameof(market));
        _simTime = simTime ?? throw new ArgumentNullException(nameof(simTime));
        _onBought = onBought ?? throw new ArgumentNullException(nameof(onBought));
    }

    /// <summary>面板是否开着。</summary>
    public bool IsOpen => _panel is not null && GodotObject.IsInstanceValid(_panel);

    // ── 可观察状态（自检与测试用）─────────────────────────────────────────

    /// <summary>报价列表的行数。</summary>
    public int OfferRowCount => _offerList?.ItemCount ?? 0;

    /// <summary>候选买家列表的行数。</summary>
    public int BuyerRowCount => _buyerList?.ItemCount ?? 0;

    /// <summary>面板回执文本（成交或失败的原因）。</summary>
    public string ResultText => _result?.Text ?? string.Empty;

    /// <summary>面板标题文本。</summary>
    public string TitleText => _title?.Text ?? string.Empty;

    /// <summary>
    /// 模拟玩家点某个报价（<b>自检用</b>）—— 走的是与真的点 ItemList 完全相同的那条路。
    /// </summary>
    /// <param name="index">报价行号（从 0 起）。</param>
    /// <returns>是否点到了（行号越界 = false）。</returns>
    public bool SelectOffer(int index)
    {
        var rows = VisibleOffers();
        if (index < 0 || index >= rows.Count) return false;

        OnOfferSelected(index);
        return true;
    }

    /// <summary>
    /// 模拟玩家点某个候选买家（<b>自检用</b>）—— 同上，走同一条路。
    /// </summary>
    /// <param name="index">买家行号（从 0 起）。</param>
    /// <returns>是否点到了（行号越界 = false）。</returns>
    public bool SelectBuyer(int index)
    {
        var offer = SelectedOffer();
        if (offer is null) return false;

        var buyers = VisibleBuyers(offer.Value);
        if (index < 0 || index >= buyers.Count) return false;

        OnBuyerSelected(index);
        return true;
    }

    /// <summary>打开面板（已开着就只刷新内容）。</summary>
    public void Open()
    {
        if (IsOpen)
        {
            RefreshAll();
            return;
        }

        var panel = new Panel
        {
            // 位置：压在**地图区**偏左，避开右栏（x ≥ 992）—— 与摊位面板同构。
            Position = new Vector2(300f, 70f),
            CustomMinimumSize = new Vector2(380f, 430f),
        };

        var backdrop = new StyleBoxFlat
        {
            BgColor = new Color("171d27"),
            BorderColor = new Color("3c4552"),
            BorderWidthLeft = 1,
            BorderWidthTop = 1,
            BorderWidthRight = 1,
            BorderWidthBottom = 1,
        };
        panel.AddThemeStyleboxOverride("panel", backdrop);

        _host.AddChild(panel);
        _panel = panel;

        var box = new VBoxContainer();
        box.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        box.AddThemeConstantOverride("separation", 4);
        panel.AddChild(box);

        _title = MakeLabel("商队", 14);
        box.AddChild(_title);

        _hint = MakeLabel("", 12);
        box.AddChild(_hint);

        _offerList = MakeList(140);
        _offerList.ItemSelected += OnOfferSelected;
        box.AddChild(_offerList);

        _buyerTitle = MakeLabel("", 12);
        box.AddChild(_buyerTitle);

        _buyerList = MakeList(130);
        _buyerList.ItemSelected += OnBuyerSelected;
        box.AddChild(_buyerList);

        _result = MakeLabel("", 12);
        _result.AddThemeColorOverride("font_color", new Color("9fd6a0"));
        box.AddChild(_result);

        var close = new Button { Text = "关闭" };
        close.AddThemeFontSizeOverride("font_size", 12);
        close.Pressed += Close;
        box.AddChild(close);

        RefreshAll();
    }

    /// <summary>关闭面板。</summary>
    public void Close()
    {
        if (_panel is null) return;
        _panel.QueueFree();
        _panel = null;
        _offerList = null;
        _buyerList = null;
        _title = null;
        _hint = null;
        _buyerTitle = null;
        _result = null;
        _selectedInstanceId = null;
    }

    // ── 刷新 ─────────────────────────────────────────────────────────────

    /// <summary>重建两个列表。</summary>
    private void RefreshAll()
    {
        RefreshOffers();
        RefreshBuyers();
    }

    /// <summary>重建报价列表。</summary>
    private void RefreshOffers()
    {
        if (_offerList is null) return;

        var rows = VisibleOffers();

        _offerList.Clear();
        foreach (var row in rows) _offerList.AddItem(row.Text);

        if (_title is not null)
        {
            _title.Text = _selectedInstanceId is null
                ? $"商队（{rows.Count}）"
                : $"商队（{rows.Count}）· 已选一档货";
        }

        if (_hint is not null)
        {
            _hint.Text = rows.Count == 0
                ? "现在没有商队在村里。"
                : "点一档货，再点一个人来买（一次买一份）。";
        }
    }

    /// <summary>当前可见的报价（<b>列表与点击索引必须用同一份</b>）。</summary>
    private List<(string InstanceId, string Text)> VisibleOffers()
    {
        var rows = new List<(string, string)>();
        foreach (var offer in _market.OpenOffers())
        {
            rows.Add((offer.InstanceId, OfferLine(offer)));
        }

        return rows;
    }

    /// <summary>重建候选买家列表。</summary>
    private void RefreshBuyers()
    {
        if (_buyerList is null) return;

        _buyerList.Clear();

        var offer = SelectedOffer();
        if (offer is null)
        {
            if (_buyerTitle is not null) _buyerTitle.Text = "";
            return;
        }

        var buyers = VisibleBuyers(offer.Value);
        foreach (var buyer in buyers) _buyerList.AddItem(buyer.Text);

        if (_buyerTitle is not null)
        {
            _buyerTitle.Text = buyers.Count == 0
                ? $"买 {offer.Value.ItemId}：没有买得起的在场英雄"
                : $"买 {offer.Value.ItemId}：点一个人成交";
        }
    }

    /// <summary>当前选中的报价（重新取，避免成交后拿到过期数据）。</summary>
    private CaravanOfferView? SelectedOffer()
    {
        if (_selectedInstanceId is null) return null;
        return _market.OpenOffers().FirstOrDefault(o => o.InstanceId == _selectedInstanceId);
    }

    /// <summary>候选买家（在场、买得起、身上放得下）。</summary>
    private List<(HeroId Id, string Name, string Text)> VisibleBuyers(CaravanOfferView offer)
    {
        var rows = new List<(HeroId, string, string)>();
        foreach (var hero in _heroes())
        {
            if (offer.RemainingStock <= 0) break;

            long gold = hero.Wallet.Amount(Application.Stall.StallService.Gold);
            if (gold < offer.UnitPrice) continue;

            rows.Add((hero.Id, hero.Name,
                $"{hero.Name}（{gold} 金）· 一份 {offer.UnitPrice} 金"));
        }

        return rows;
    }

    // ── 交互 ─────────────────────────────────────────────────────────────

    /// <summary>点了某个报价 → 进入「选买家」这一跳。</summary>
    private void OnOfferSelected(long index)
    {
        var rows = VisibleOffers();
        if (index < 0 || index >= rows.Count) return;

        _selectedInstanceId = rows[(int)index].InstanceId;
        if (_result is not null) _result.Text = "";
        RefreshOffers();
        RefreshBuyers();
    }

    /// <summary>点了某个买家 → 买一份。</summary>
    private void OnBuyerSelected(long index)
    {
        var offer = SelectedOffer();
        if (offer is null) return;

        // 列表与点击索引必须用同一份候选表 —— 否则点了一个人、买的是另一个人。
        var buyers = VisibleBuyers(offer.Value);
        if (index < 0 || index >= buyers.Count) return;

        var buyer = buyers[(int)index];
        var attempt = _market.TryBuy(
            offer.Value.InstanceId, buyer.Id, qty: 1, simTime: _simTime());

        if (attempt.Succeeded)
        {
            SetResult($"买了 {attempt.Result.Quantity} 份 {attempt.Result.ItemId}" +
                      $"（{attempt.Result.TotalAmount} 金）· 还剩 {attempt.Result.RemainingStock} 份");
            _onBought(attempt.Result);
        }
        else
        {
            SetResult(RejectedText(attempt.Outcome));
        }

        RefreshAll();
    }

    /// <summary>写一行回执。</summary>
    private void SetResult(string text)
    {
        if (_result is not null) _result.Text = text;
    }

    /// <summary>报价一行（**要让玩家看得出比市价便宜**）。</summary>
    /// <param name="offer">报价。</param>
    /// <returns>显示文本。</returns>
    private static string OfferLine(CaravanOfferView offer)
    {
        string price = offer.MarketPrice > 0
            ? $"{offer.UnitPrice} 金（市价 {offer.MarketPrice}）"
            : $"{offer.UnitPrice} 金";
        return $"{offer.ItemId} · {price} · 剩 {offer.RemainingStock} 份";
    }

    /// <summary>失败原因的一句话（**要让玩家知道下一步该干什么**）。</summary>
    /// <param name="outcome">结果分类。</param>
    /// <returns>显示文本。</returns>
    private static string RejectedText(CaravanBuyOutcome outcome) => outcome switch
    {
        CaravanBuyOutcome.CannotAfford => "钱不够。",
        CaravanBuyOutcome.BagRejected => "背包放不下了。",
        CaravanBuyOutcome.InsufficientStock => "货被买完了。",
        CaravanBuyOutcome.WindowClosed => "商队已经走了。",
        CaravanBuyOutcome.NoOffer => "这档货已经不在了。",
        CaravanBuyOutcome.DuplicateRequest => "这一单已经成交过了。",
        _ => "买不了。",
    };

    /// <summary>造一个统一字号的标签。</summary>
    private static Label MakeLabel(string text, int fontSize)
    {
        var l = new Label { Text = text };
        l.AddThemeFontSizeOverride("font_size", fontSize);
        l.AddThemeColorOverride("font_color", new Color("cfc4b0"));
        l.CustomMinimumSize = new Vector2(0, 18);
        return l;
    }

    /// <summary>造一个列表框（去掉默认底色，与面板融为一体）。</summary>
    /// <param name="height">最小高度（超出则滚动）。</param>
    /// <returns>配好主题的 ItemList。</returns>
    private static Godot.ItemList MakeList(int height)
    {
        var list = new Godot.ItemList { MaxColumns = 1, SelectMode = Godot.ItemList.SelectModeEnum.Single };
        list.AddThemeFontSizeOverride("font_size", 12);
        list.AddThemeColorOverride("font_color", new Color("cfc4b0"));
        list.CustomMinimumSize = new Vector2(0, height);
        list.AddThemeStyleboxOverride("panel", new StyleBoxEmpty());
        list.AddThemeStyleboxOverride("selected", new StyleBoxFlat
        {
            BgColor = new Color("2c3a4d"),
            BorderColor = new Color("4d6180"),
            BorderWidthLeft = 1,
            BorderWidthTop = 1,
            BorderWidthRight = 1,
            BorderWidthBottom = 1,
        });
        return list;
    }
}
