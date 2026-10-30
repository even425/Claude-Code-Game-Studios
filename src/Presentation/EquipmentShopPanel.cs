using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using ClaudeCodeGameStudios.Application;
using ClaudeCodeGameStudios.Application.Stall;
using ClaudeCodeGameStudios.Domain.Items;
using ClaudeCodeGameStudios.Domain.Village;

namespace ClaudeCodeGameStudios.Presentation;

/// <summary>
/// <b>装备店面板</b>（D-127）—— 玩家打开商店，看货、给英雄买装备。
/// </summary>
/// <remarks>
/// <para>⚠️ <b>为什么需要它</b>：D-119 的装备是「<b>英雄自主购买</b>」⇒ 玩家建了商店
/// 却**只能看不能买** ⇒ 自己盖的店完全用不上手 ⇒ 缺一条**玩家能参与**的路。</para>
///
/// <para>⚠️ <b>钱的流向与 NPC 买完全一致</b>：英雄钱包 −价 ⇒ <b>村库 +价</b>
/// ⇒ 守恒律两边不变 ⇒ 玩家手动买**不会**破坏那本账。</para>
///
/// <para>⚠️ <b>货是实时的</b>：面板每次打开都重读库存 ⇒ NPC 买走的东西不会再卖一遍。</para>
/// </remarks>
public sealed class EquipmentShopPanel
{
    private readonly Node _host;
    private readonly Func<IReadOnlyList<HeroAgent>> _heroes;
    private readonly EquipmentShopService _shops;
    private readonly Village _village;
    private readonly Action<string> _onBought;

    private Panel? _panel;
    private Label? _title;
    private Label? _hint;
    private ItemList? _stockList;
    private ItemList? _heroList;
    private Label? _result;

    private readonly List<(BuildingId Shop, ItemId Item)> _stock = new();
    private readonly List<HeroAgent> _candidates = new();
    private int _selectedStock;
    private int _selectedHero;

    /// <summary>造一个面板。</summary>
    /// <param name="host">面板挂载的父节点（场景根）。</param>
    /// <param name="heroes">当前村庄英雄的提供方。</param>
    /// <param name="shops">装备商店（库存与成交）。</param>
    /// <param name="village">村庄（钱进村库）。</param>
    /// <param name="onBought">成交回调（发事件 + 刷新花名册）。</param>
    public EquipmentShopPanel(
        Node host,
        Func<IReadOnlyList<HeroAgent>> heroes,
        EquipmentShopService shops,
        Village village,
        Action<string> onBought)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _heroes = heroes ?? throw new ArgumentNullException(nameof(heroes));
        _shops = shops ?? throw new ArgumentNullException(nameof(shops));
        _village = village ?? throw new ArgumentNullException(nameof(village));
        _onBought = onBought ?? throw new ArgumentNullException(nameof(onBought));
    }

    /// <summary>面板是否开着。</summary>
    public bool IsOpen => _panel is not null && GodotObject.IsInstanceValid(_panel);

    /// <summary>货架上的行数（自检用）。</summary>
    public int StockRowCount => _stockList?.ItemCount ?? 0;

    /// <summary>候选英雄的行数（自检用）。</summary>
    public int HeroRowCount => _heroList?.ItemCount ?? 0;

    /// <summary>结果文字（自检用）。</summary>
    public string ResultText => _result?.Text ?? string.Empty;

    /// <summary>打开面板（或刷新已开的那个）。</summary>
    public void Open()
    {
        if (IsOpen)
        {
            RefreshAll();
            return;
        }

        var panel = new Panel
        {
            Position = new Vector2(300f, 70f),
            CustomMinimumSize = new Vector2(400f, 460f),
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

        _title = MakeLabel("装备店", 14);
        box.AddChild(_title);

        _hint = MakeLabel("", 12);
        box.AddChild(_hint);

        _stockList = MakeList(170);
        _stockList.ItemSelected += index => { _selectedStock = (int)index; RefreshResult(); };
        box.AddChild(_stockList);

        _heroList = MakeList(150);
        _heroList.ItemSelected += index => { _selectedHero = (int)index; RefreshResult(); };
        box.AddChild(_heroList);

        _result = MakeLabel("", 12);
        _result.AddThemeColorOverride("font_color", new Color("9fd6a0"));
        box.AddChild(_result);

        var buy = new Button { Text = "买给选中的英雄" };
        buy.AddThemeFontSizeOverride("font_size", 12);
        buy.Pressed += () => BuySelected();
        box.AddChild(buy);

        var close = new Button { Text = "关闭" };
        close.AddThemeFontSizeOverride("font_size", 12);
        close.Pressed += () => Close();
        box.AddChild(close);

        RefreshAll();
    }

    /// <summary>关闭面板。</summary>
    public void Close()
    {
        if (_panel is null) return;
        _panel.QueueFree();
        _panel = null;
        _stockList = null;
        _heroList = null;
        _title = null;
        _hint = null;
        _result = null;
    }

    /// <summary>刷新全部内容（货与英雄都是实时的）。</summary>
    public void RefreshAll()
    {
        RefreshStock();
        RefreshHeroes();
        RefreshResult();
    }

    /// <summary>刷新货架（只列**已建成**的店）。</summary>
    private void RefreshStock()
    {
        _stock.Clear();
        _stockList?.Clear();

        foreach (var shopId in new[] { BuildableBuildings.WeaponShop.Id, BuildableBuildings.ArmorShop.Id })
        {
            if (!BuiltBuildings.IsCompleted(_village, shopId)) continue;

            foreach (var kv in _shops.StockOf(shopId).OrderBy(k => k.Key.Value, StringComparer.Ordinal))
            {
                var def = _shops.Catalog.Find(kv.Key);
                if (def is null) continue;

                _stock.Add((shopId, kv.Key));
                _stockList?.AddItem(
                    $"{def.DisplayName} · {BonusText(def)} · {def.Price} 金（库存 {kv.Value}）");
            }
        }

        if (_title is not null)
        {
            _title.Text = _stock.Count > 0
                ? $"装备店（在售 {_stock.Count} 件）"
                : "装备店（还没建店，或店里没货）";
        }

        if (_hint is not null)
        {
            _hint.Text = "选一件装备、选一个英雄，再点「买给选中的英雄」。钱从英雄钱包进村库。";
        }
    }

    /// <summary>刷新候选英雄（按金币从多到少）。</summary>
    private void RefreshHeroes()
    {
        _candidates.Clear();
        _heroList?.Clear();

        _candidates.AddRange(_heroes()
            .Where(h => h is not null)
            .OrderByDescending(h => h.Wallet.Amount(StallService.Gold))
            .ToList());

        foreach (var h in _candidates)
        {
            _heroList?.AddItem($"{h.Name} · {h.Wallet.Amount(StallService.Gold)} 金");
        }
    }

    /// <summary>刷新提示（买得起 / 买不起）。</summary>
    private void RefreshResult()
    {
        if (_result is null) return;

        if (_stock.Count == 0)
        {
            _result.Text = "店里没有货。";
            return;
        }

        if (_candidates.Count == 0)
        {
            _result.Text = "村里没有英雄。";
            return;
        }

        int si = Math.Clamp(_selectedStock, 0, _stock.Count - 1);
        int hi = Math.Clamp(_selectedHero, 0, _candidates.Count - 1);
        var (shopId, itemId) = _stock[si];
        var hero = _candidates[hi];
        var def = _shops.Catalog.Find(itemId);
        long price = def?.Price ?? 0;
        long gold = hero.Wallet.Amount(StallService.Gold);

        _result.Text = gold >= price
            ? $"「{def?.DisplayName}」{price} 金 → {hero.Name}（余 {gold - price} 金）"
            : $"「{def?.DisplayName}」{price} 金 —— {hero.Name} 只有 {gold} 金，买不起。";
    }

    /// <summary>买：把选中的装备买给选中的英雄。</summary>
    /// <returns>买成了为 true（自检与玩家按钮共用）。</returns>
    public bool BuySelected()
    {
        if (_stock.Count == 0 || _candidates.Count == 0) return false;

        int si = Math.Clamp(_selectedStock, 0, _stock.Count - 1);
        int hi = Math.Clamp(_selectedHero, 0, _candidates.Count - 1);
        var (shopId, itemId) = _stock[si];
        var hero = _candidates[hi];

        var result = _shops.TryBuy(hero, shopId, itemId, _village);
        if (result.Succeeded)
        {
            _result.Text = $"买到了：{hero.Name} 穿上了 {result.Item?.Value}。";
            _onBought($"{hero.Name} 买了一件 {result.Item?.Value}（{result.Price} 金）");
        }
        else
        {
            _result.Text = $"没买到：{result.Reason}";
        }

        RefreshStock();
        RefreshHeroes();
        return result.Succeeded;
    }

    /// <summary>装备的加成摘要（<c>攻+2</c>）。</summary>
    private static string BonusText(EquipmentDefinition def)
    {
        var parts = new List<string>();
        if (def.AttackBonus != 0) parts.Add($"攻+{def.AttackBonus}");
        if (def.DefenseBonus != 0) parts.Add($"防+{def.DefenseBonus}");
        return parts.Count > 0 ? string.Join("/", parts) : "—";
    }

    private static Label MakeLabel(string text, int size)
    {
        var label = new Label { Text = text };
        label.AddThemeFontSizeOverride("font_size", size);
        return label;
    }

    private static ItemList MakeList(int minHeight)
    {
        var list = new ItemList { CustomMinimumSize = new Vector2(0, minHeight) };
        list.AddThemeFontSizeOverride("font_size", 12);
        return list;
    }
}
