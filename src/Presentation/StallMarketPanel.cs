using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using ClaudeCodeGameStudios.Application.Stall;
using ClaudeCodeGameStudios.Domain.Heroes;
using ClaudeCodeGameStudios.Domain.Items;
using ClaudeCodeGameStudios.Domain.Village;
using DomainStall = ClaudeCodeGameStudios.Domain.Stall.Stall;
using DomainStallStatus = ClaudeCodeGameStudios.Domain.Stall.StallStatus;

namespace ClaudeCodeGameStudios.Presentation;

/// <summary>
/// 摊位面板（R-02 · 正式购买 UI + 摊位库存面板 R-04）。
/// </summary>
/// <remarks>
/// <para><b>两跳交互</b>：先点一个<b>摊位</b>，再点一个<b>买家</b>。
/// 买家这一跳是必须的 —— 本项目<b>没有"玩家化身"</b>：
/// 玩家是村庄管理者，钱和货都记在具体某个英雄名下（R-01 的物品也归英雄个人）。
/// 面板把"这次由谁买"摆到台面上，而不是替玩家猜一个人。</para>
///
/// <para><b>为什么是弹窗而不是右栏常驻</b>：左栏已经排满（花名册 + 队伍 + 7 个按钮），
/// 右栏的杂谈是主体 —— 挤出一个常驻列表区会牺牲杂谈。弹窗零布局成本，
/// 而且和既有的「摆摊」弹窗同构，玩家的心智模型是连贯的。</para>
///
/// <para><b>它不做什么</b>：不结算。成交一律走
/// <see cref="StallService.TryBuy"/>（唯一结算入口，I15 四步提交），
/// 本类只负责"把玩家点了什么"翻译成调用，以及把结果显示出来。</para>
///
/// <para><b>失败不进杂谈</b>：面板内当场回执即可 ——
/// 杂谈是"村里发生了什么"，不是"玩家每次点击的回执"。</para>
/// </remarks>
public sealed class StallMarketPanel
{
    // ── 依赖 ─────────────────────────────────────────────────────────────

    private readonly Node _host;
    private readonly Func<IReadOnlyList<HeroAgent>> _heroes;
    private readonly StallMarketService _market;
    private readonly Func<double> _simTime;
    private readonly Action<StallService.BuyStallResult> _onBought;

    // ── 节点 ─────────────────────────────────────────────────────────────

    private Panel? _panel;
    private Label? _title;
    private Label? _hint;
    private Godot.ItemList? _stallList;
    private Label? _buyerTitle;
    private Godot.ItemList? _buyerList;
    private Label? _result;

    /// <summary>当前选中的摊位（<b>按摊主</b>记，不缓存摊位对象 —— 成交后摊位会被替换）。</summary>
    private HeroId? _selectedOwner;

    /// <param name="host">面板挂载的父节点（场景根）。</param>
    /// <param name="heroes">当前村庄英雄的提供方。</param>
    /// <param name="market">摊位市场（候选买家 + 买尽可能多 + 结算入口）。</param>
    /// <param name="simTime">当前模拟时间（成交时间戳）。</param>
    /// <param name="onBought">成交回调：发事件 + 刷新花名册等。</param>
    public StallMarketPanel(
        Node host,
        Func<IReadOnlyList<HeroAgent>> heroes,
        StallMarketService market,
        Func<double> simTime,
        Action<StallService.BuyStallResult> onBought)
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

    /// <summary>摊位列表的行数。</summary>
    public int StallRowCount => _stallList?.ItemCount ?? 0;

    /// <summary>候选买家列表的行数。</summary>
    public int BuyerRowCount => _buyerList?.ItemCount ?? 0;

    /// <summary>面板回执文本（成交或失败的原因）。</summary>
    public string ResultText => _result?.Text ?? string.Empty;

    /// <summary>面板标题文本。</summary>
    public string TitleText => _title?.Text ?? string.Empty;

    /// <summary>
    /// 模拟玩家点某个摊位（<b>自检用</b>）—— 走的是与真的点 ItemList 完全相同的那条路。
    /// </summary>
    /// <param name="index">摊位行号（从 0 起）。</param>
    /// <returns>是否点到了（行号越界 = false）。</returns>
    /// <remarks>
    /// <b>为什么要留这个口</b>：单测能验"选中摊位后候选名单是什么"，
    /// 但验不了<b>引擎真的会把点击送进来</b> —— 那是引擎层面的事。
    /// 真机上没有指针时，自检就靠它走一遍（与 <c>--click</c> 验花名册同一路数）。
    /// </remarks>
    public bool SelectStall(int index)
    {
        var rows = VisibleStalls();
        if (index < 0 || index >= rows.Count) return false;

        OnStallSelected(index);
        return true;
    }

    /// <summary>
    /// 模拟玩家点某个候选买家（<b>自检用</b>）—— 同上，走同一条路。
    /// </summary>
    /// <param name="index">买家行号（从 0 起）。</param>
    /// <returns>是否点到了（行号越界 = false）。</returns>
    public bool SelectBuyer(int index)
    {
        var stall = SelectedStall();
        if (stall is null) return false;

        var offers = _market.Offers(stall, _heroes());
        if (index < 0 || index >= offers.Count) return false;

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
            // 位置：压在**地图区**偏左，避开右栏（x ≥ 992）。
            // 之前放在 x=860 —— 那儿正好盖住村庄杂谈，弹窗与杂谈叠在一起，
            // 半透明的背景让两边的字叠成一片，谁也读不清。
            Position = new Vector2(300f, 70f),
            CustomMinimumSize = new Vector2(380f, 470f),
        };

        // 不透明底色。Godot 的 Panel 默认样式是半透明的 —— 底下的杂谈会透上来。
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

        _title = MakeLabel("摊位", 14);
        box.AddChild(_title);

        _hint = MakeLabel("", 12);
        box.AddChild(_hint);

        _stallList = MakeList(190);
        _stallList.ItemSelected += OnStallSelected;
        box.AddChild(_stallList);

        _buyerTitle = MakeLabel("", 12);
        box.AddChild(_buyerTitle);

        _buyerList = MakeList(150);
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
        _stallList = null;
        _buyerList = null;
        _title = null;
        _hint = null;
        _buyerTitle = null;
        _result = null;
        _selectedOwner = null;
    }

    // ── 刷新 ─────────────────────────────────────────────────────────────

    /// <summary>重建两个列表。</summary>
    private void RefreshAll()
    {
        RefreshStalls();
        RefreshBuyers();
    }

    /// <summary>重建摊位列表（R-04：库存与单价上界面）。</summary>
    private void RefreshStalls()
    {
        if (_stallList is null) return;

        var rows = VisibleStalls();

        _stallList.Clear();
        foreach (var row in rows) _stallList.AddItem(row.Text);

        if (_title is not null)
        {
            _title.Text = _selectedOwner is null
                ? $"摊位（{rows.Count}）"
                : $"摊位（{rows.Count}）· 已选一个";
        }

        if (_hint is not null)
        {
            _hint.Text = rows.Count == 0
                ? "现在还没有人开摊。"
                : "点一行摊子，看谁能买。";
        }
    }

    /// <summary>当前可见的摊位（<b>列表与点击索引必须用同一份</b>）。</summary>
    private List<(HeroId Owner, string Text)> VisibleStalls()
    {
        var sellers = _heroes().ToDictionary(h => h.Id, h => h.Name);
        var rows = new List<(HeroId Owner, string Text)>();

        foreach (var stall in _market.Stalls)
        {
            if (stall.Status == DomainStallStatus.Closed) continue;   // 已收摊：不占位置
            sellers.TryGetValue(stall.Owner, out var name);
            rows.Add((stall.Owner, StallMarketText.StallLine(name ?? stall.Owner.Value, stall)));
        }

        return rows;
    }

    /// <summary>重建候选买家列表。</summary>
    private void RefreshBuyers()
    {
        if (_buyerList is null) return;

        _buyerList.Clear();

        var stall = SelectedStall();
        if (stall is null)
        {
            if (_buyerTitle is not null) _buyerTitle.Text = "";
            return;
        }

        var offers = _market.Offers(stall, _heroes());
        foreach (var offer in offers) _buyerList.AddItem(StallMarketText.BuyerLine(offer));

        if (_buyerTitle is not null)
        {
            _buyerTitle.Text = offers.Count == 0
                ? $"买 {stall.ItemId.Value}：没有买得起的在场英雄"
                : $"买 {stall.ItemId.Value}：点一个人成交";
        }
    }

    /// <summary>当前选中的摊位（重新取，避免成交后拿到过期对象）。</summary>
    private DomainStall? SelectedStall()
    {
        if (_selectedOwner is null) return null;
        return _market.Stalls.FirstOrDefault(s => s.Owner == _selectedOwner.Value);
    }

    // ── 交互 ─────────────────────────────────────────────────────────────

    /// <summary>点了某个摊位 → 进入"选买家"这一跳。</summary>
    private void OnStallSelected(long index)
    {
        var rows = VisibleStalls();
        if (index < 0 || index >= rows.Count) return;

        _selectedOwner = rows[(int)index].Owner;
        if (_result is not null) _result.Text = "";
        RefreshStalls();
        RefreshBuyers();
    }

    /// <summary>点了某个买家 → 买「尽可能多」。</summary>
    private void OnBuyerSelected(long index)
    {
        var stall = SelectedStall();
        if (stall is null) return;

        // 列表与点击索引必须用同一份候选表 —— 否则点了一个人、买的是另一个人。
        var offers = _market.Offers(stall, _heroes());
        if (index < 0 || index >= offers.Count) return;

        var offer = offers[(int)index];
        var attempt = _market.BuyAsMuchAsPossible(stall, offer.Id, _simTime());

        if (attempt.Bought && attempt.Result is { } result)
        {
            SetResult(StallMarketText.Bought(offer.Name, stall.ItemId.Value, result.Quantity, result.TotalAmount));
            _onBought(result);
        }
        else
        {
            SetResult(StallMarketText.Rejected(attempt.Outcome));
        }

        RefreshAll();
    }

    /// <summary>写一行回执。</summary>
    private void SetResult(string text)
    {
        if (_result is not null) _result.Text = text;
    }

    /// <summary>造一个统一字号的标签。</summary>
    private static Label MakeLabel(string text, int fontSize)
    {
        var l = new Label { Text = text };
        l.AddThemeFontSizeOverride("font_size", fontSize);
        l.AddThemeColorOverride("font_color", new Color("cfc4b0"));
        l.CustomMinimumSize = new Vector2(0, 18);
        return l;
    }

    /// <summary>
    /// 造一个列表框。
    /// </summary>
    /// <param name="height">最小高度（超出则滚动）。</param>
    /// <returns>配好主题的 ItemList。</returns>
    /// <remarks>
    /// <b>为什么把默认底色去掉</b>：Godot 的 ItemList 默认带一块浅色面板底，
    /// 落在本面板的深色底上就是两块突兀的灰砖。去掉之后列表与面板融为一体，
    /// 只有选中项留着高亮 —— 玩家要看的本来就只是那一行。
    /// </remarks>
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
