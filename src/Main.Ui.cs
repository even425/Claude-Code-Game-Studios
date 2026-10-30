using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using ClaudeCodeGameStudios.Application;
using ClaudeCodeGameStudios.Domain.Heroes;
using ClaudeCodeGameStudios.Domain.Quests;
using ClaudeCodeGameStudios.Domain.Relationships;
using ClaudeCodeGameStudios.Presentation;
using BuildableBuilding = ClaudeCodeGameStudios.Domain.Village.BuildableBuilding;
using BuildableBuildings = ClaudeCodeGameStudios.Domain.Village.BuildableBuildings;

/// <summary>
/// 主场景的<b>表现层</b>部分（A1 拆分：场景搭建、界面刷新、玩家命令入口、输入）。
///
/// <para>基线 §13.1 的分层：表现层负责「地图、镜头、场景节点、动画、UI、输入采集」，
/// <b>不持有任何玩法规则</b>。这里的方法要么画界面，要么把玩家的一次点击
/// 翻译成一次领域调用 —— 规则在别处，这里只做翻译。</para>
///
/// <para>⚠️ **这是纯代码搬移** —— 逻辑一行没改，只是从 <c>Main.cs</c> 整块搬到这里。</para>
/// </summary>
public partial class Main : Node2D
{
    // ---------------------------------------------------------------- 场景

    private void BuildScene()
    {
        var bg = new ColorRect { Color = new Color("14161a") };
        bg.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(bg);

        // 地图的位置与缩放由 UiLayout 算 —— 见该文件的注释：2026-10-19 之前
        // 这里硬写 Position=(500,150)、缩放 1，菱形铺满 800×460，横跨整块屏幕，
        // 左右两栏怎么摆都会压在它身上。
        _village = new VillageView
        {
            Position = UiLayout.MapOrigin,
            Scale = new Vector2(UiLayout.MapScale, UiLayout.MapScale),
        };
        AddChild(_village);

        var top = new Label { Position = new Vector2(UiLayout.LeftX, 6) };
        top.AddThemeFontSizeOverride("font_size", 18);
        top.AddThemeColorOverride("font_color", new Color("e6dfd4"));
        AddChild(top);
        _stats = top;

        // === M8 v1.2 布局：三栏互不相交（几何全部来自 UiLayout，可被测试引用） ===
        // === 左栏：花名册 + 队伍 + 按钮   中间：地图条带   右栏：杂谈 ===
        //
        // ⚠️ **左栏是滚动的**（D-081 修的界面缺陷）：左栏内容的高度**不是常量** ——
        // 「队伍」区会随村庄层级长行（升级选项 + 建筑选项），按钮也从一个长到十一个
        // ⇒ 任何固定高度都会把底部的按钮挤出屏幕（实测：11 个按钮只有 3 个看得见，
        // 「发布任务 / 升级村庄 / 建围栏 / 建试炼塔 / 进试炼塔 / 存盘 / 读盘」全都点不到）。
        // ⇒ 外面套一层 ScrollContainer ⇒ 内容多高都能滚到。
        //
        // ⚠️ **ScrollContainer 放在场景根上（不是 VBox 里）** —— 它的最小尺寸恒为 (0,0)，
        // 放在 VBox 里会被压成 0 高（2026-10-19「花名册没有名字」就是这一族）。
        var leftScroll = new ScrollContainer
        {
            Position = new Vector2(UiLayout.LeftX, UiLayout.PanelTopY),
            Size = new Vector2(UiLayout.LeftW, UiLayout.PanelHeight),
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        AddChild(leftScroll);

        var left = new VBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        leftScroll.AddChild(left);
        left.AddThemeConstantOverride("separation", (int)UiLayout.VBoxSeparation);

        left.AddChild(Header($"村里的人（点名字组队，上限 {SquadMax}）"));
        // 花名册是 ItemList 本身在滚动：最小高度给 UiLayout.RosterMinHeight，
        // 左栏的富余空间由 ExpandFill 吃掉。行文本的生成与「要不要重建」见 Roster。
        _roster = new Godot.ItemList();
        _roster.AddThemeFontSizeOverride("font_size", 12);
        _roster.AddThemeColorOverride("font_color", new Color("cfc4b0"));
        _roster.MaxColumns = 1;
        _roster.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        // 纵向 ExpandFill：ItemList 自己就是滚动控件，直接挂在左栏 VBox 上即可。
        // 2026-10-19 之前它被包在 ScrollContainer 里 —— 滚动轴上的子节点只拿到
        // 自己的最小高度（300），于是 372 高的滚动区里只用到 300、底部空一截；
        // 而真正响应滚轮/滚动条的是 ItemList 自己的内部滚动条，外面那层纯属多余。
        _roster.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        _roster.SelectMode = Godot.ItemList.SelectModeEnum.Single;
        _roster.AllowReselect = true;
        _roster.FocusMode = Control.FocusModeEnum.None;
        _roster.CustomMinimumSize = new Vector2(0, UiLayout.RosterMinHeight);
        _roster.ItemActivated += OnRosterItemActivated;
        left.AddChild(_roster);

        left.AddChild(Header("队伍"));
        _squadInfo = new Label();
        _squadInfo.AddThemeFontSizeOverride("font_size", 12);
        _squadInfo.AddThemeColorOverride("font_color", new Color("cfc4b0"));
        left.AddChild(_squadInfo);

        // 6 个按钮竖排。左栏本身已经很窄，按钮再被 Fill 拉满整栏只会像一条条横杠 ——
        // 所以用 ShrinkBegin：按钮宽度由文字决定，最宽也不超过左栏。
        _btnTask = MakeButton("发布任务", left, OnTask);
        _btnUpgrade = MakeButton("升级村庄", left, OnUpgrade);
        // D-074：独立建筑（围栏 / 试炼塔）与村庄的整体升级**并存** ——
        // 后者是既有的 5 级阶梯，前者是各自独立的建筑。
        _btnBuildFence = MakeButton("建围栏", left, OnBuildFence);
        _btnBuildTower = MakeButton("建试炼塔", left, OnBuildTower);
    // D-095：防护罩（专职防御建筑 ⇒ 兽潮规模 −1 级，与围栏可叠加）。
    _btnBuildShield = MakeButton("建防护罩", left, OnBuildShield);
        // D-119：装备线（武器店 / 服装店 ⇒ 英雄自主买装备；铁匠铺负责修理）。
        _btnBuildWeaponShop = MakeButton("建武器店", left, OnBuildWeaponShop);
        _btnBuildArmorShop = MakeButton("建服装店", left, OnBuildArmorShop);
        _btnTrialTower = MakeButton("进试炼塔", left, OnTrialTower);
        _btnSummon = MakeButton("招募一个人", left, OnSummonPaid);
        _btnMarket = MakeButton("看摊位", left, OnMarket);
        // D-127：装备店（玩家手动买装备；NPC 那边是自主购买）。
        _btnEquipShop = MakeButton("看装备店", left, OnEquipShop);
        // D-081：商队面板（商队来访期间才看得到货）。
        _btnCaravan = MakeButton("看看商队", left, OnCaravan);
        _btnSave = MakeButton("存盘", left, OnSave);
        _btnLoad = MakeButton("读盘", left, OnLoad);
        // 修 Phase 1 自检写盘 bug：user:// → 本地路径
        var savePath = OS.GetCmdlineUserArgs()
            .FirstOrDefault(a => a.StartsWith("--savepath=", StringComparison.Ordinal))?
            .Substring("--savepath=".Length)
            ?? $"user://{ClaudeCodeGameStudios.Data.SavePort.DefaultFileName}";
        savePath = Godot.ProjectSettings.GlobalizePath(savePath);
        _saves = new ClaudeCodeGameStudios.Data.SavePort(savePath);

        // 经济流水落盘（运行闭环验证与经济数据采集准备）：
        // 只有命令行给了 --econlog=<路径> 才有它；正常游戏**不写盘**。
        // 路径照 --savepath= 的先例：先 GlobalizePath，再交给端口。
        var econLogPath = OS.GetCmdlineUserArgs()
            .FirstOrDefault(a => a.StartsWith("--econlog=", StringComparison.Ordinal))?
            .Substring("--econlog=".Length);
        if (string.IsNullOrWhiteSpace(econLogPath)
            && OS.GetCmdlineUserArgs().Any(a => a.StartsWith("--collect=", StringComparison.Ordinal)))
        {
            // 采集模式没给路径 → 用默认名（并把真实路径打出来）。
            econLogPath = "user://economy-collect.jsonl";
        }
        if (!string.IsNullOrWhiteSpace(econLogPath))
        {
            econLogPath = Godot.ProjectSettings.GlobalizePath(econLogPath);
            _economyLog = new ClaudeCodeGameStudios.Data.EconomyLog(econLogPath, _clock.SimSecondsPerDay);
        }

        // M7 摆摊服务 —— 依赖 _villageModel + PriceBook + _heroes
        // heroLookup 委托：用 HeroAgent.Id（已设）查 —— D-0…（略）
        _stallService = new ClaudeCodeGameStudios.Application.Stall.StallService(
            _villageModel,
            _priceBook,
            id => _heroes.FirstOrDefault(h => h.Id == id));

        // D-054-02：村庄升级的扣款（**金币 + 材料**）。
        // 成本表与「能不能花得起」都在服务里 —— Main 只管「点了要盖哪一级」。
        _upgradeService = new ClaudeCodeGameStudios.Application.VillageUpgradeService(
            _villageModel,
            ClaudeCodeGameStudios.Application.Stall.StallService.Gold,
            MaterialsId,
            _procurement,
            () => _heroes);
        // M4 自主经济行为 —— random = (() => _rng.NextDouble())
        // 注入 _stallService 是必须的（行为触发后调 BuyFromStall）
        _heroBehavior = new ClaudeCodeGameStudios.Application.Stall.HeroBehaviorService(
            _stallService,
            id => _heroes.FirstOrDefault(h => h.Id == id),
            () => _rng.NextDouble(1.0));
        // 第四阶段 3B：按游戏日调度一次自主购物（同日重复调用由服务内部闸门挡住）。
        _dailyShopping = new ClaudeCodeGameStudios.Application.Stall.DailyShoppingService(_heroBehavior);

        // 第四阶段 R-02：摊位市场 + 摊位面板（正式购买 UI + 摊位库存面板 R-04）。
        // 面板只做「玩家点了什么 → 调哪个服务」；结算一律走 StallService.TryBuy
        // （唯一结算入口，I15 四步提交）。成交后发交易事件 + 刷新花名册。
        _market = new ClaudeCodeGameStudios.Application.Stall.StallMarketService(
            _stallService,
            ClaudeCodeGameStudios.Application.Stall.StallService.Gold,
            id => _heroes.FirstOrDefault(h => h.Id == id));

        _stallPanel = new ClaudeCodeGameStudios.Presentation.StallMarketPanel(
            this,
            () => _heroes,
            _market,
            () => _clock.Day * 24.0,
            OnManualTradeCompleted);

        // === 右栏：杂谈滚动 ===
        var right = new VBoxContainer
        {
            Position = new Vector2(UiLayout.RightX, UiLayout.PanelTopY),
            Size = new Vector2(UiLayout.RightW, UiLayout.PanelHeight),
        };
        right.AddThemeConstantOverride("separation", (int)UiLayout.VBoxSeparation);
        AddChild(right);
        right.AddChild(Header("村庄杂谈"));
        _chron = new VBoxContainer();
        _chron.AddThemeConstantOverride("separation", 4);
        _chron.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        right.AddChild(MakeScroll(UiLayout.ChronicleMinHeight, _chron));

        // 呈现器在此注入：节点归场景所有，呈现器只往里写
        // （基线 §13.3 —— 领域对象不做成 Node，呈现器在 Application 层）
        _chronicle = new ChroniclePresenter(_chron);

        // 第三阶段 M6：右栏底部的关系摘要（4 行，只读；P3-D11）。
        // 数据来自领域 RelationshipBook 的快照，UI 不存独立的关系副本。
        right.AddChild(Header("关系"));
        _relationInfo = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        _relationInfo.AddThemeFontSizeOverride("font_size", 12);
        _relationInfo.AddThemeColorOverride("font_color", new Color("cfc4b0"));
        _relationInfo.CustomMinimumSize = new Vector2(0, UiLayout.RelationsMinHeight);
        right.AddChild(_relationInfo);

        var hint = new Label
        {
            Position = UiLayout.HintPosition,
            Text = "F 召唤    R 发任务    Q 升级村庄        （不按键村庄也会自己过日子）",
        };
        hint.AddThemeFontSizeOverride("font_size", 12);
        hint.AddThemeColorOverride("font_color", new Color("6b6152"));
        AddChild(hint);
    }

    /// <summary>
    /// 造一个<b>真的能显示内容</b>的滚动区。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么不能只写 <c>Size</c></b>：<c>ScrollContainer.get_minimum_size()</c>
    /// 恒为 (0,0) —— 它<b>不</b>把子节点的最小尺寸往外传播。放进 VBoxContainer 之后，
    /// 父容器按最小尺寸排版，滚动区就被压成 <b>0 高</b>，构造时给的 <c>Size</c>
    /// 在下一帧的排版里被覆盖掉，里面装的东西全被裁没、看不见也点不到。</para>
    /// <para>2026-10-19 的「花名册没有名字」「杂谈不显示信息」就是这一条：两个滚动区
    /// 都是 0 高，而 <c>Refresh()</c> 一直在往里写数据 —— 数据在，只是被裁掉了。
    /// 真正生效的是 <see cref="Control.CustomMinimumSize"/> 加纵向 <c>ExpandFill</c>。</para>
    /// <para>横向滚动显式关掉：关掉之后滚动区才会把宽度也分配给子节点，
    /// 里面自动换行的文字才会按栏宽折行，而不是把整栏撑出一条横向滚动条。</para>
    /// </remarks>
    /// <param name="minHeight">最小高度。必须 &gt; 0，否则等于没给。</param>
    /// <param name="content">内容节点。</param>
    private static ScrollContainer MakeScroll(float minHeight, Control content)
    {
        var scroll = new ScrollContainer
        {
            CustomMinimumSize = new Vector2(0, minHeight),
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        scroll.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        scroll.AddChild(content);
        return scroll;
    }

    private static Label Header(string s)
    {
        var l = new Label { Text = s };
        l.AddThemeFontSizeOverride("font_size", 13);
        l.AddThemeColorOverride("font_color", new Color("9fb4c8"));
        return l;
    }

    /// <summary>造一个「贴着文字」的按钮。</summary>
    /// <remarks>
    /// <para><b>为什么不设宽度下限</b>：<c>CustomMinimumSize.X</c> 只是<b>下限</b>，
    /// 不是宽度。控件在容器里的默认横向标志是 <c>Fill</c>，父容器会把整栏宽度
    /// 分配给它 —— 于是无论下限写 180 还是 0，六个按钮都会变成六条横贯左栏、
    /// 压在地图上的长条。2026-10-19 的「各个按钮太过宽了挡住场景」就是这条。</para>
    /// <para><c>ShrinkBegin</c> 让按钮只拿到「文字宽度 + 内边距」，左对齐排列。
    /// 高度仍给下限，保证一行的点击区够大。</para>
    /// </remarks>
    private static Button MakeButton(string text, Node parent, Action onPress)
    {
        var b = new Button
        {
            Text = text,
            CustomMinimumSize = new Vector2(0, UiLayout.ButtonMinHeight),
            SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin,
        };
        b.AddThemeFontSizeOverride("font_size", 12);
        b.Pressed += onPress;
        parent.AddChild(b);
        return b;
    }

    // ---------------------------------------------------------------- 在场判定

    /// <summary>这个人此刻是否在村里干活。</summary>
    /// <remarks>
    /// <para><b>2026-10-17 塌缩。</b>原实现是 <c>h.AbsentDays &lt;= 0 &amp;&amp; h.IsOnlineAt(_clock.Hour)</c> ——
    /// 那是<b>地球时区作息 + 缺席</b>的门，两者都已由 <c>07</c> <b>D-001</b> 作废。
    /// 现在英雄总在场（基线 §2.2），唯一的「不在」是<b>随队外出</b>。</para>
    /// <para>与 <c>Domain.Heroes.HeroState.IsEligibleForDecision()</c> 的区别：那个还要求<b>空闲</b>（无进行中行动），
    /// 这里只问<b>在不在场</b>。两者语义不同，<b>不要合并</b>。</para>
    /// </remarks>
    /// <summary>
    /// <see cref="HeroState"/> → 对应的 <see cref="HeroAgent"/>。
    /// </summary>
    /// <remarks>
    /// <c>_heroes</c>（表现）与 <c>_states</c>（领域）按下标一一对应，
    /// 在 <c>OnSummon</c> 里同时追加。本方法是这个约定的<b>唯一</b>出口，
    /// 避免各处自己按下标找、找错就静默操作了别人。
    /// </remarks>
    private HeroAgent AgentOf(HeroState state)
    {
        var index = _states.IndexOf(state);
        if (index < 0 || index >= _heroes.Count)
        {
            throw new InvalidOperationException(
                $"领域状态 {state.Id} 在花名册里找不到对应的人 —— 两个列表失去同步。"
                + "这是一个应当炸掉的编程错误，不是可以悄悄兜底的状况。");
        }

        return _heroes[index];
    }

    /// <summary>该英雄是否已经在别的任务里（不变量 I2 的判定源）。</summary>
    private bool IsOnMission(HeroState st) => st.Presence == Presence.OnMission;

    private static bool IsPresent(HeroAgent h) => !h.Away;

    /// <summary>
    /// 第三阶段 M2：社交的目标候选。
    /// </summary>
    /// <remarks>
    /// <para>返回 null = 这项活动不需要目标（采集 / 建屋）；返回列表 = 需要目标。</para>
    /// <para>条件（P3-D07）：<b>在村</b>、不是自己。随队外出的英雄不在可交谈范围。</para>
    /// <para><b>关系只影响权重、不影响候选</b> —— 候选过滤是硬条件，权重在
    /// <c>DecisionFlow</c> 里用 <see cref="RelationshipRules.TargetWeight"/> 计算。</para>
    /// </remarks>
    private IReadOnlyList<HeroId>? SocialTargetCandidates(
        HeroState hero, ActivityDefinition activity)
    {
        if (activity.Id.Value != SocialActivityId) return null;

        var targets = new List<HeroId>();
        foreach (var state in _states)
        {
            if (state.Id == hero.Id) continue;
            if (!state.IsInVillage) continue;   // 随队任务的英雄不在村里
            targets.Add(state.Id);
        }

        return targets;
    }

    /// <summary>
    /// 第三阶段 M3：一次社交完成之后写关系。
    /// </summary>
    /// <remarks>
    /// <para><b>双向各自加</b>（P3-D04/D-036）：A→B 与 B→A 是两条独立记录（I8），
    /// 各用自己的业务键，重复处理只生效一次（P3-D08）。</para>
    /// <para><b>失败不算社交</b>：行动途中目标可能随队离开；此时不改关系、不发事件 ——
    /// 「失败行为不能被记录成成功」。</para>
    /// <para><b>事件始终记录，杂谈只在阶段跨越时说</b>（P3-D09）：关系没变（已到 ±100）
    /// 则连事件都不产生（反装饰规则）。</para>
    /// </remarks>
    private void OnSocialCompleted(string actorId, HeroId targetId)
    {
        if (string.IsNullOrEmpty(targetId.Value)) return;

        HeroState? actor = null;
        HeroState? target = null;
        foreach (var state in _states)
        {
            if (state.Id.Value == actorId) actor = state;
            if (state.Id.Value == targetId.Value) target = state;
        }

        if (actor is null || target is null) return;
        if (!target.IsInVillage) return;   // 目标已随队离开：本次社交失败

        var touched = _relationships.Socialize(actor.Id, target.Id, _clock.Now);
        if (!touched.Changed) return;      // 已到边界：不编造事件

        var payload = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["partner"] = target.Definition.Name,
            ["partnerId"] = target.Id.Value,
            ["delta"] = RelationshipRules.SocialDelta.ToString(),
            ["value"] = touched.Forward.After.Value.ToString(),
        };

        Emit(EvSocialized, actor.Definition.Name, new[] { StRelationship }, payload,
            ev => touched.StageChanged
                ? SocialStageLine(actor.Definition.Name, target.Definition.Name,
                    touched.Forward, touched.Backward)
                : null);
    }

    /// <summary>关系阶段跨越时的一句话（P3-D09；文案属内容设计，先占位）。</summary>
    private static string SocialStageLine(
        string actor, string target, RelationshipChange forward, RelationshipChange backward)
    {
        var reached = forward.StageChanged ? forward.After : backward.After;
        if (reached.Value >= RelationshipRules.CloseThreshold)
        {
            return $"{actor}和{target}处得像老朋友了。";
        }

        return reached.Value > 0
            ? $"{actor}和{target}说上了话。"
            : $"{actor}和{target}之间有点不太对。";
    }

    /// <summary>
    /// 【待定 / P3-D05】性格对「社交」活动权重的影响（占位值，不是设计定案）。
    /// </summary>
    private static double SocialPreferenceWeight(string personality) => personality switch
    {
        "莽" => 1.3,
        "贪" => 1.1,
        "傲" => 0.7,
        _ => 1.0,
    };

    private List<HeroAgent> ReadySquad => _squad.Where(IsPresent).ToList();

    private bool Expending => _pendingEnd > 0.0;
    private double HoursLeft => Math.Max(0.0, _pendingEnd - _clock.AbsoluteHour);

    // ---------------------------------------------------------------- 事件

    /// <summary>
    /// 花名册中<b>每一行的像素高度</b>，必须与下方
    /// <c>_roster</c> 实际渲染时一致 —— 否则「点名字」会点错行。
    /// </summary>
    /// <remarks>
    /// 之前写的是 <c>18</c>（裸字面量），但 <c>AddThemeFontSizeOverride("font_size", 12)</c>
    /// 之后实际行高 ≈ 22–24 像素。**这导致点击 y 在 0–17 时是第 0 行，18–35 也是第 0 行，
    /// 36–53 落到第 2 行** —— 行号与视觉行错位，玩家点名字"没进队伍"。
    /// <b>这是只动这一个常量就能改的坑，不该留。</b>
    /// </remarks>
    private void OnRosterItemActivated(long index)
    {
        if (index < 0 || index >= _heroes.Count) return;
        var h = _heroes[(int)index];

        if (!IsPresent(h))
        {
            // 原本这里往杂谈里塞一条「X 不在村里」。**已删**：
            // 那不是一件发生的事，是玩家操作被拒的回执 ——
            // 基线 §7.2 的十类里没有这一类，而 §7.3 明写不记录无意义的文本。
            // 回执该由按钮状态表达（_squadInfo 已经写了「队伍里的人都不在村口」）。
            Refresh();
            return;
        }

        if (_squad.Contains(h))
        {
            _squad.Remove(h);                       // 移出永远允许
            Refresh();
            return;
        }

        // 加入与出发合法性<b>都走领域层</b>，不在这里硬编码人数上限。
        var state = _states[_heroes.IndexOf(h)];

        // S5 前置校验：这个人此刻能不能被加进来
        if (!_squadRules.CanJoin(state))
        {
            Refresh();   // 同上：回执不进杂谈
            return;
        }

        // F-01 完备性：加上他之后，这支队伍还够格出发吗
        var candidate = new List<HeroState>();
        foreach (var member in _squad) candidate.Add(_states[_heroes.IndexOf(member)]);
        candidate.Add(state);

        var trial = new Squad(_squadRules, candidate);
        if (!trial.IsRosterReady)
        {
            // 超出上限。拒绝，且队伍<b>保持不变</b>（02 F-02/F-03）
            return;
        }

        _squad.Add(h);
        Refresh();
    }

    private void OnTask()
    {
        if (Expending) return;
        var ready = ReadySquad;
        if (ready.Count == 0) return;

        _pendingJob = Jobs[NextInt(Jobs.Length)];

        // 建任务并出发：合法性、占用与幂等都由领域层负责，
        // 表现层不手动改 Away —— 那是英雄状态自己的事（基线 §13.5）。
        var memberStates = new List<HeroState>();
        foreach (var h in ready) memberStates.Add(_states[_heroes.IndexOf(h)]);

        try
        {
            _mission = Mission.Create(
                new MissionId($"m{_clock.AbsoluteHour:0}"), _pendingJob.Name,
                new Squad(_squadRules, memberStates), _squadRules);
            _mission.Start(IsOnMission);
        }
        catch (ArgumentException)
        {
            // 无效队伍不能启动任务（02 F-01）—— 静默拒绝，不打断玩家
            _mission = null;
            return;
        }
        catch (InvalidOperationException)
        {
            // 有成员已在别的任务里（不变量 I2）
            _mission = null;
            return;
        }

        var squadNames = ready.Select(h => h.Name).ToArray();

        // ── D-075：副本的行程时长 = 路程 + 战斗 ──────────────────────
        //
        // 战斗要多久**只有打完才知道** ⇒ 出发时先**预解算**一遍
        // （战斗完全确定 ⇒ 结果与归来时算的必然相同）。
        // ⚠️ 采集任务（非副本）**没有战斗** ⇒ 行程就是任务表里的小时数。
        double battleHours = 0.0;
        if (_pendingJob.Glory)
        {
            var prepared = PrepareDungeon(_mission.Members.Select(m => m.Id.Value).ToArray());
            _preparedDungeon = prepared;
            battleHours = prepared.GameHours;
        }

        // ⚠️ **总时长的算法只有这一处**（`ExpeditionTiming.TotalHours`）——
        // 守卫盯着它，忘了加战斗那半边会立刻红。
        double tripHours = ClaudeCodeGameStudios.Application.ExpeditionTiming.TotalHours(
            _pendingJob.Hours, battleHours);

        _pendingEnd = _clock.AbsoluteHour + tripHours;

        Emit(EvDispatched, string.Join("、", squadNames),
            new[] { StHeroActivity, StDungeonRun },
            new Dictionary<string, string>
            {
                ["job"] = _pendingJob.Name,
                ["names"] = string.Join("、", squadNames),
                ["glory"] = _pendingJob.Glory ? "1" : "0",
                // D-075：这一趟占多久（路程 + 战斗），让玩家看得见。
                ["hours"] = tripHours.ToString("0.##"),
            },
            _ => Chronicle.SquadLeft((int)_clock.Day, squadNames).Text);
        foreach (var h in ready) h.Away = true;      // 领域层已把 HeroState 置为随队；这里同步表现层的 HeroAgent

        Refresh();
    }

    private void OnUpgrade()
    {
        if (_buildTarget >= 0) return;                      // 一次只盖一样

        // D-054-02：升级同时花**金币**（从村庄金库）与**材料**。
        // 「花得起吗」与「扣多少」都在 VillageUpgradeService 里 ——
        // 这里是表现层，只管决定「要盖哪一级」与「派人去盖」。
        if (!_upgradeService.TryUpgrade(_tier)) { Refresh(); return; }

        _buildProgress = 0f;
        _buildTarget = _tier + 1;
        // 队伍里的人要留着跑任务（那是材料来源），没派出去的留下盖房子。
        // 这就是取舍：派出去的人越多，盖得越慢；全派出去就没人盖。
        foreach (var h in _heroes) h.ToSite = !_squad.Contains(h);
        _village.BuildProgress = 0f;
        _village.BuildName = UpgradeNames[_tier];

        // 原本这里发一条「X 开工了」。**已删**：基线 §7.2 那一行写的是
        // 「建筑**建成** / 升级」，开工不在名单里 —— 而 §7.3 明写
        // 「不记录系统内部状态变化」，进度条推进正是状态变化。
        // 开工信息由工地进度条本身表达。
        Refresh();
    }

    /// <summary>
    /// D-074：建造一栋独立建筑（围栏 / 试炼塔）。
    /// </summary>
    /// <remarks>
    /// <para><b>与「升级村庄」并存</b>：后者是既有的 5 级阶梯（整体升级），
    /// 前者是各自独立的建筑 ⇒ 两条路互不干扰。</para>
    /// <para><b>人力决定工期</b>（D-074）：<b>没被派去副本的人</b>去工地
    /// ⇒ 派出去的人越多，盖得越慢；全派出去就<b>停工</b>（已付的资源不退）。</para>
    /// </remarks>
    /// <param name="definition">要建什么。</param>
    private void OnBuild(BuildableBuilding definition)
    {
        // ⚠️ **一次只盖一样**（D-074）：施工队是同一批人 ⇒
        // 村庄升级在施工时，独立建筑拿不到人（停工）⇒ 两条路互斥，
        // 免得玩家以为「两样都在盖」而其实互相偷人力。
        if (_buildTarget >= 0)
        {
            GD.Print($"BUILD {definition.Name} 开不了工（村庄正在升级，工地上有人）。");
            Refresh();
            return;
        }

        if (!_buildingService.CanStart(definition))
        {
            GD.Print($"BUILD {definition.Name} 开不了工（已建成 / 在建 / 落点被占 / 钱或材料不够）。");
            Refresh();
            return;
        }

        // ⚠️ **D-093：不再直接开工** ⇒ 改为「进入放置模式」，落点由玩家点地图决定。
        // 资源与人力都在真正放下那一刻才结算（`PlacePendingAt`）。
        BeginPlacement(definition);
    }

    private void OnBuildFence() => OnBuild(BuildableBuildings.Fence);

    private void OnBuildTower() => OnBuild(BuildableBuildings.TrialTower);

    /// <summary>建防护罩（D-095）。</summary>
    private void OnBuildShield() => OnBuild(BuildableBuildings.Shield);

    /// <summary>建武器店（D-119：卖武器给英雄）。</summary>
    private void OnBuildWeaponShop() => OnBuild(BuildableBuildings.WeaponShop);

    /// <summary>建服装店（D-119：卖衣服给英雄）。</summary>
    private void OnBuildArmorShop() => OnBuild(BuildableBuildings.ArmorShop);

    /// <summary>
    /// 在施工的几栋建筑 + 各自进度（顶栏那行用；没有在建时返回空串）。
    /// </summary>
    /// <returns>例如「围栏 40% · 试炼塔 20%」。</returns>
    private string ActiveSitesText()
    {
        var parts = new List<string>();
        foreach (var c in _buildingService.ActiveConstructions)
        {
            int pct = (int)(c.Instance.ConstructionProgress * 100);
            parts.Add($"{c.Definition.Name} {pct}%");
        }

        return string.Join(" · ", parts);
    }

    /// <summary>
    /// D-074：独立建筑按钮的文案与禁用状态。
    /// </summary>
    /// <param name="button">要刷新的按钮。</param>
    /// <param name="definition">对应的建筑。</param>
    /// <remarks>
    /// <b>为什么按钮上要写成本</b>：升级按钮已经这么做了（D-054-02 的教训：
    /// 只显示材料会让玩家以为「升级不要钱」）⇒ 独立建筑同理。
    /// </remarks>
    private void RefreshBuildButton(Button button, BuildableBuilding definition)
    {
        // ⚠️ **D-093：正在放置时**，按钮改成「取消放置」⇒ 玩家知道怎么退出。
        if (PendingPlacement is not null
            && PendingPlacement.Value.Id.Value == definition.Id.Value)
        {
            button.Text = $"取消放下{definition.Name}";
            button.Disabled = false;
            return;
        }

        if (_buildingService.IsCompleted(definition.Id))
        {
            button.Text = $"{definition.Name}（已建成）";
            button.Disabled = true;
            return;
        }

        var active = _buildingService.ActiveConstructions
            .FirstOrDefault(c => c.Definition.Id.Value == definition.Id.Value);

        if (active is not null)
        {
            int pct = (int)(active.Instance.ConstructionProgress * 100);
            button.Text = $"正在建 {definition.Name}  {pct}%";
            button.Disabled = true;
            return;
        }

        button.Text = $"建{definition.Name}  ·  {definition.GoldCost}金 {definition.MaterialCost}材料";
        // ⚠️ **落点被占也要在按钮上体现**：`CanStart` 不查占位
        // （占位的权威判定在 `Village.TryPlace` —— 见它的说明）⇒ 这里补一次，
        // 免得玩家点了才知道「那儿已经有东西了」。
        button.Disabled = !_buildingService.CanStart(definition)
                          || _villageModel.IsOccupied(definition.GridX, definition.GridY);
    }


    /// <summary>
    /// R-02：打开摊位面板（正式购买 UI + 摊位库存面板 R-04）。
    /// </summary>
    /// <remarks>
    /// <b>为什么是一个按钮而不是右栏常驻区</b>：左栏已经排满，右栏的杂谈是主体 ——
    /// 挤出一个常驻列表区会牺牲杂谈。弹窗零布局成本，与既有「摆摊」弹窗同构。
    /// </remarks>
    private void OnMarket()
    {
        _stallPanel.Open();
    }

    /// <summary>
    /// 玩家手动成交之后：发一条交易事件 + 刷新花名册。
    /// </summary>
    /// <param name="result">成交结果。</param>
    /// <remarks>
    /// <b>成交必须进杂谈</b>：这次交易真的改了钱和货（<c>hero.wallet</c> / <c>hero.bag</c>），
    /// 属于基线 §7.2 里"该被记下来的事"。<b>失败不进</b> —— 失败什么都没改，
    /// 由面板内回执承担（见 <c>StallMarketText.Rejected</c>）。
    /// </remarks>
    private void OnManualTradeCompleted(
        ClaudeCodeGameStudios.Application.Stall.StallService.BuyStallResult result)
    {
        EmitTradeCompleted(result);
        Refresh();
    }

    /// <summary>
    /// M7 存盘 —— 把当前村庄 + 英雄的 0,2 状态写到 <c>user://village-save.json</c>。
    /// </summary>
    /// <remarks>
    /// <para><b>不</b>存：</para>
    /// <list type="bullet">
    /// <item>任务列表（Missions）—— 任务有专属的"未出发/进行中/已返回"状态，存档时若英雄已死，引用会断（D-030 留账）</item>
    /// <item>摊位目录（StallService）—— D-029 拍定不存</item>
    /// <item><c>Settlement.AppliedKeys</c> / <c>AppliedTradeKeys</c> —— M6 立了 Settlement 类但调用方 0，存空集合即可</item>
    /// <item>随机数状态（<c>RandomState</c>）—— 已是空，存档层不丢信息</item>
    /// </list>
    /// <para><b>存</b>：村庄 Resources（含"金币"） + Heroes 列表（CoinAmount + Bag） + SquadMemberIds（不存空，UI 后续接 M8） + SimTime。</para>
    /// </remarks>
    private void OnSave()
    {
        try
        {
            var save = BuildSave();   // 用 Phase 1 已立的 BuildSave —— 含 CoinAmount + Bag（M7）
            _saves.Write(save);
            Add(new Chronicle.Entry(
                (int)_clock.Day, $"存档已存（{_saves.FilePath}）—— 版本 {save.Version}。", null));
        }
        catch (System.Exception ex)
        {
            Add(new Chronicle.Entry(
                (int)_clock.Day, $"存档失败：{ex.Message}", null));
        }
        Refresh();
    }

    /// <summary>M7 读盘 —— 从 0,2 档恢复（不通过则整份拒绝）。</summary>
    private void OnLoad()
    {
        if (!_saves.Exists)
        {
            Add(new Chronicle.Entry(
                (int)_clock.Day, "无存档可读。", null));
            Refresh();
            return;
        }
        var outcome = _saves.Read();
        if (!outcome.Loaded)
        {
            Add(new Chronicle.Entry(
                (int)_clock.Day, $"读档失败：{outcome.Describe()}", null));
            Refresh();
            return;
        }
        try
        {
            ApplySave(outcome.Save!);   // Phase 1 已立的 ApplySave —— 含 Wallet/Bag 还原
            Add(new Chronicle.Entry(
                (int)_clock.Day, $"读档成功（{_saves.FilePath}）。", null));
        }
        catch (System.Exception ex)
        {
            Add(new Chronicle.Entry(
                (int)_clock.Day, $"读档后应用失败：{ex.Message}", null));
        }
        Refresh();
    }

    // ---------------------------------------------------------------- 界面

    private void Refresh()
    {
        int on = _heroes.Count(h => h.Present);
        int away = _squad.Count(h => h.Away);

        // ⚠️ **D-114：顶栏要同时显示「村庄资产」** ——
        // 村庄升级花掉的钱**变成资产**（D-090）⇒ 只显示现金 ⇒ 玩家看到「村金 0」，
        // 而钱其实在资产里 ⇒ **那是在骗玩家**。
        // ⚠️ **D-126：日常开销要看得见** —— 玩家���的钱每天会少一点，
        // 不说清楚就等于「偷偷扣钱」⇒ 顶栏直接标出来。
        string upkeepText = ClaudeCodeGameStudios.Application.VillageUpkeepService.DefaultUpkeepPerDay > 0
            ? $" · 开销 {ClaudeCodeGameStudios.Application.VillageUpkeepService.DefaultUpkeepPerDay}/天"
            : "";
        string villageMoney = $"村金 {VillageGold} · 资产 {VillageAsset}{upkeepText}";

        // ⚠️ **多栋同时施工时要能一眼看出**（D-082）：顶栏那行只放得下一个名字，
        // 所以把在施工的几栋连同进度**合成一句**（「围栏 40% · 试炼塔 20%」）。
        string sites = ActiveSitesText();
        _stats.Text = _buildTarget >= 0
            ? $"{TierName(_tier)}    第 {_clock.Day} 天 {_clock.Clock}    材料 {Mat}    {villageMoney}    在村 {on}/{_heroes.Count}    工地：{_village.BuildName}"
            : sites.Length > 0
                ? $"{TierName(_tier)}    第 {_clock.Day} 天 {_clock.Clock}    材料 {Mat}    {villageMoney}    在村 {on}/{_heroes.Count}    工地：{sites}"
                : $"{TierName(_tier)}    第 {_clock.Day} 天 {_clock.Clock}    材料 {Mat}    {villageMoney}    在村 {on}/{_heroes.Count}";

        RefreshRoster();
        RefreshRelationSummary();

        var ready = ReadySquad;
        _squadInfo.Text = ready.Count == 0
            ? (_squad.Count == 0 ? "没派人" : "队里的人都不在村口")
            : $"{string.Join("、", ready.Select(h => h.Name))}";

        // ---- 任务按钮 ----
        _btnTask.Disabled = Expending || ready.Count == 0;
        _btnTask.Text = Expending
            ? $"{_pendingJob.Name} · 还有 {HoursLeft:0.0} 小时"
            : ready.Count == 0 ? "队里的人不在村口"
            : $"发布任务（{ready.Count} 人）";

        // ---- 升级按钮 ----
        // D-054-02：成本现在有**两项**（金币 + 材料），按钮要**两项都显示** ——
        // 只显示材料会让玩家以为「升级不要钱」。
        var upgradeCost = _upgradeService.CostFor(_tier);
        if (_buildTarget >= 0)
        {
            int pct = (int)(_buildProgress * 100);
            _btnUpgrade.Text = $"正在盖 {UpgradeNames[_buildTarget - 1]}  {pct}%";
            _btnUpgrade.Disabled = true;
        }
        else
        {
            _btnUpgrade.Text = _tier >= ClaudeCodeGameStudios.Application.VillageUpgradeService.MaxTier
                ? "已是王国第一的城"
                : $"{UpgradeNames[_tier]}  ·  {upgradeCost.Gold}金 {upgradeCost.Material}材料";
            _btnUpgrade.Disabled = !_upgradeService.CanUpgrade(_tier);
        }

        // ---- 召唤按钮（D-077：花 100 金从村金库招人）----
        // ⚠️ 按钮上**要写费用**：升级按钮已经这么做了（D-054-02 的教训：
        // 只显示材料会让玩家以为「升级不要钱」）⇒ 召唤同理。
        _btnSummon.Text = _summonService.CanSummon
            ? $"招募一个人  ·  {SummonService.GoldCost}金（村金库 {_summonService.VillageGold}）"
            : $"招募一个人（村金库不足 {_summonService.VillageGold}/{SummonService.GoldCost} 金）";
        _btnSummon.Disabled = !_summonService.CanSummon;

        // ---- D-074：独立建筑的三个按钮 ----
        RefreshBuildButton(_btnBuildFence, BuildableBuildings.Fence);
        RefreshBuildButton(_btnBuildTower, BuildableBuildings.TrialTower);
        RefreshBuildButton(_btnBuildShield, BuildableBuildings.Shield);
        // D-119：装备线的两间店（武器店 / 服装店）。
        RefreshBuildButton(_btnBuildWeaponShop, BuildableBuildings.WeaponShop);
        RefreshBuildButton(_btnBuildArmorShop, BuildableBuildings.ArmorShop);

        _btnTrialTower.Text = _buildingService.IsCompleted(BuildableBuildings.TrialTower.Id)
            ? "进试炼塔（打赢 +20 生命）"
            : "试炼塔还没建";
        _btnTrialTower.Disabled = !_buildingService.IsCompleted(BuildableBuildings.TrialTower.Id);

        // 杂谈只在真的变了的时候重建 —— 每帧重建节点会拖垮帧率
        _chronicle.RebuildIfDirty();
    }

    /// <summary>
    /// 把花名册同步到界面 —— <b>只有行文本真的变了才动 <see cref="_roster"/></b>。
    /// </summary>
    /// <remarks>
    /// <para><b>2026-10-19 的「37 人在村里，花名册只有 14 个名字、滚动条拖到底也没有更多人」
    /// 是两件事叠在一起：</b></para>
    /// <para>① 这里原本是 <c>_heroes.Take(14)</c> —— 数据只有 14 条，滚动条当然滚不出第 15 个。
    /// 行文本现在由 <see cref="Roster.Lines"/> 生成，<b>不截断</b>。</para>
    /// <para>② 这里原本每帧都 <c>Clear()</c> 再逐条 <c>AddItem()</c>。即使去掉上限，
    /// <c>ItemList.Clear()</c> 也会把滚动位置复位 —— 玩家拖一下、下一帧就弹回顶部，
    /// 永远只能看到第一屏。所以重建前先比行文本，没变就<b>什么都不做</b>。</para>
    /// <para><b>第四阶段 M5 的另一半</b>：行尾现在带经济摘要（金币 + 背包），
    /// 金币/物品一变行文本就变。此时<b>不</b> <c>Clear()</c>，而是逐行
    /// <c>SetItemText</c>：行数与顺序没变，滚动位置和选中项都保留。
    /// 只有名册长度变了（召唤 / 读档换整份）才重建。</para>
    /// <para>比较的是<b>渲染文本本身</b>而不是某个版本号：召唤、上下队、金币变化、
    /// 背包变化、读档换整份名册，只要行文本变了一个字符就会更新，不会漏。</para>
    /// </remarks>
    private void RefreshRoster()
    {
        var lines = Roster.Lines(
            _heroes, _squad,
            hero => RosterLine(hero));

        if (Roster.SameLines(lines, _rosterLines)) return;

        if (lines.Count == _rosterLines.Count)
        {
            // 行数没变：原地改文本，别把滚动位置和选中项一起清掉。
            for (int i = 0; i < lines.Count; i++)
            {
                if (lines[i] != _rosterLines[i]) _roster.SetItemText(i, lines[i]);
            }
        }
        else
        {
            _roster.Clear();
            foreach (var line in lines) _roster.AddItem(line);
        }

        _rosterLines.Clear();
        _rosterLines.AddRange(lines);
    }

    /// <summary>
    /// 花名册行尾：经济摘要 + 「想买未成」标记（R-07）。
    /// </summary>
    /// <param name="hero">该英雄。</param>
    /// <returns>行尾文本。</returns>
    /// <remarks>
    /// <b>标记在最后</b>：经济摘要是每一行都有的「状态」，
    /// 标记是偶尔才有的追加信息 —— 放最后，读起来是「名字 → 状态 → 补充」。
    /// </remarks>
    private string RosterLine(HeroAgent hero)
    {
        string economy = EconomySummary.For(
            hero, ClaudeCodeGameStudios.Application.Stall.StallService.Gold);

        // ⚠️ **D-119：身上穿的装备也显示出来**（武器/衣服 + 剩余耐久）。
        // ⇒ 玩家看得见「他穿了什么」与「快坏了」⇒ 修理那条链才有意义。
        string gear = GearSummary(hero);
        if (!string.IsNullOrEmpty(gear)) economy = $"{economy}  {gear}";

        string note = ShoppingNoteFor(hero);
        return string.IsNullOrEmpty(note) ? economy : $"{economy}  {note}";
    }

    /// <summary>
    /// 这个人今天「想买没买成」的原因标记（没有则空串）。
    /// </summary>
    /// <param name="hero">该英雄。</param>
    /// <returns>例如 <c>想买未成·钱不够</c>；不该标时返回空串。</returns>
    /// <remarks>
    /// <b>只标当天的</b>：跨天了还没买成，是昨天的事，不该继续挂在今天的花名册上。
    /// </remarks>
    private string ShoppingNoteFor(HeroAgent hero)
    {
        if (_lastShoppingDay != (int)_clock.Day) return string.Empty;
        if (_lastShoppingResults.Count == 0) return string.Empty;

        foreach (var result in _lastShoppingResults)
        {
            if (result.Buyer is not { } id || id != hero.Id) continue;
            return ClaudeCodeGameStudios.Application.Stall.ShoppingFeedback.RosterNote(result);
        }

        return string.Empty;
    }

    /// <summary>
    /// 把关系摘要同步到右栏（第三阶段 M6）。
    /// </summary>
    /// <remarks>
    /// <para>数据来自 <see cref="RelationshipBook.Snapshot"/>，<b>UI 不存独立的关系副本</b>
    /// （P3-D11 / 第三阶段 §11.5「UI 展示的数据来自领域状态」）。</para>
    /// <para>行文本没变就不碰 Label —— 与花名册同一模式（复用 <see cref="Roster.SameLines"/>，
    /// 那是通用的行文本比较，不为关系再写一份）。</para>
    /// </remarks>
    private void RefreshRelationSummary()
    {
        var names = new Dictionary<HeroId, string>(_states.Count);
        foreach (var state in _states) names[state.Id] = state.Definition.Name;

        var lines = RelationshipPresenter.Lines(_relationship.Snapshot(), names);
        if (Roster.SameLines(lines, _relationLines)) return;

        _relationLines.Clear();
        _relationLines.AddRange(lines);
        _relationInfo.Text = string.Join("\n", lines);
    }


    /// <summary>
    /// 村庄档位名。<b>只描述「村子变成什么样」，不点任何具体设施的名字。</b>
    /// </summary>
    /// <remarks>
    /// 第 1 档原为「冒险者旅舍」。召唤设施的形态属【待定】（<c>07</c> 的 <b>D-006</b>），
    /// 而档位名一旦出现「旅舍」，就等于把这个开放问题写成了已定的答案 ——
    /// 玩家和读代码的人都会把名字当成结论。改用描述性说法：
    /// 「有人来往的村子」说的是一件正在发生的事，不是某种建筑。
    /// <para>
    /// 由 <c>VoidedFacilityNameTests</c> 守住。
    /// </para>
    /// </remarks>
    private static string TierName(int t) => t switch
    {
        0 => "破落户", 1 => "有人来往的村子", 2 => "有井的村子",
        3 => "有围墙的村子", 4 => "铁匠铺", _ => "王国第一的城",
    };


    /// <summary>
    /// MT-04（存档重载后的可见状态）：把**玩家能看到**的界面文字拍成一段文本。
    ///
    /// <para><b>为什么拍文字而不是拍像素</b>：截图像素比对有两处做不到 ——
    /// ① 视口纹理装的是<b>上一帧</b>的渲染，而存读档往返在<b>同一帧内</b>完成
    /// ⇒ 想拿到两份<b>不同</b>的渲染就必须跨帧，跨帧时钟就会走（顶栏时间变），
    /// 而不跨帧就只会拿到**同一张**图 ⇒ 那是**空转**，不是验证；
    /// ② 界面里还有**图形**（等距村庄、树），它们与「状态是否被还原」无关。
    /// <para>而 MT-04 的实质是「**玩家看到的**和存档前一致」——
    /// 界面**文字**正是玩家看到的东西，而且它在同一帧内取 ⇒ 完全确定性。
    /// 它照样能抓住 MT-04 真正要防的那类缺陷：<b>状态还原了、但界面没跟着刷新</b>
    /// （自检会在搅乱之后刷新一次界面 ⇒ 屏幕上是「被搅乱的世界」；
    /// 读档之后若读档路径自己不刷新，屏幕上就还是那个被搅乱的世界 ⇒ 两份快照不同）。
    /// 它也抓得住「可见状态压根没被还原」：村庄层级、模拟时间、材料、编成、名册行。</para>
    ///
    /// <para><b>为什么不含杂谈面板</b>：读档按设计会**重置事件管道并清空杂谈**
    /// （<c>Main.Save.cs</c>「事件管道重置」，M0 审计 R-04：「读档是换一个世界」）。
    /// 事件 ID 不入档 ⇒ 旧杂谈继续参与去重会被 EventLog 静默丢弃
    /// ⇒ 杂谈**必须**清空。MT-04 因此比的是<b>村庄资源 / 英雄持有物 / 任务进度</b>。</para>
    /// </summary>
    private string VisibleTextSnapshot()
    {
        var sb = new System.Text.StringBuilder();
        sb.Append("stats|").Append(_stats?.Text ?? "").Append('\n');
        sb.Append("squad|").Append(_squadInfo?.Text ?? "").Append('\n');
        sb.Append("relation|").Append(_relationInfo?.Text ?? "").Append('\n');
        if (_roster != null)
        {
            // ⚠️ Godot 4.7 的 C# API **没有** `GetItemTexts()`，
            // 只有 `ItemCount` + `GetItemText(i)`。
            for (int i = 0; i < _roster.ItemCount; i++)
            {
                sb.Append("roster|").Append(_roster.GetItemText(i)).Append('\n');
            }
        }
        return sb.ToString();
    }

    /// <summary>
    /// `--shot=<路径>`：跑够一段时间后截一张图并退出。
    /// </summary>
    /// <remarks>
    /// <para>⚠️ <b>截图前先跑多久</b>由 <c>--shotdelay=</c> 给（解析在 <c>Main.ResolveShotDelay</c>）：
    /// 默认 0.5 秒只能拍到「开局那一刻」，而**很多有意义的东西要跑几天才出现**
    /// （D-091 的村庄收购站：村庄升级攒够资产之后才开张）。</para>
    /// <para>⚠️ <b>为什么按「时间」而不是「帧数」计</b>：
    /// 原来写的是 <c>_shotFrame += (int)(dt * 60)</c> ⇒ <b>用帧数当时间</b>。
    /// 机器空闲时帧率极高（几百 fps ⇒ <c>dt × 60 &lt; 1</c>）
    /// ⇒ <b>整数截断成 0</b> ⇒ 永远攒不到 30 帧 ⇒ <b>截图永远不发生</b>。
    /// （实测：机器忙时能截，机器空闲时必卡 —— 同一个代码，行为取决于机器负载。）
    /// ⇒ 改成<b>累计时间</b>：到点就截 ⇒ 行为与帧率无关。</para>
    /// </remarks>
    private void ProcessShot(float dt)
    {
        if (_shotSeconds < 0)
        {
            foreach (var a in OS.GetCmdlineUserArgs())
            {
                if (a.StartsWith("--shot=")) { _shotPath = a[7..]; _shotSeconds = 0.0; }
            }
        }
        if (_shotSeconds < 0) return;

        _shotSeconds += dt;
        if (_shotSeconds >= _shotDelay)
        {
            var img = GetViewport().GetTexture().GetImage();
            GD.Print($"SHOT saved: {_shotPath} err={img.SavePng(_shotPath)}");
            _shotSeconds = -1.0;
            GetTree().Quit();
        }
    }
}
