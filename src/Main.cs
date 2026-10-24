using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using ClaudeCodeGameStudios.Application;
using ClaudeCodeGameStudios.Presentation;
using ClaudeCodeGameStudios.Domain.Heroes;
using ClaudeCodeGameStudios.Domain.Quests;
using ClaudeCodeGameStudios.Domain.Relationships;
using System.IO;
using System.Globalization;
// 别名而非整包 using：领域类型就叫 Village，而本类已有一个名为 _village 的
// VillageView（表现层节点）。两个都叫「村庄」，混用会读错。
using DomainVillage = ClaudeCodeGameStudios.Domain.Village.Village;
using ResourceId = ClaudeCodeGameStudios.Domain.Village.ResourceId;
using BuildableBuilding = ClaudeCodeGameStudios.Domain.Village.BuildableBuilding;
using BuildableBuildings = ClaudeCodeGameStudios.Domain.Village.BuildableBuildings;

/// <summary>
/// 主场景。整棵树在代码里搭，不依赖 .tscn —— 少一个会悄悄坏掉的序列化文件。
///
/// 运行：godot --path . （主场景已配在 project.godot）
/// 截图：godot --path . -- --shot=res://shot.png
///
/// M0 的立意：**你不按任何键，村庄也在过日子。**
/// 时间自动走、英雄<b>总在场</b>（基线 §2.2：不做在线窗口、不做缺席）、
/// 每天早上村庄自己发生几件事。
///
/// <para><b>这个类被拆成了 7 个文件（A1：2026-10-21）</b> —— 原来 4528 行一个文件，
/// 是基线 §13.4 禁止的巨型 GameManager。拆法<b>按职责</b>，不按行数：
/// <list type="bullet">
/// <item>本文件 = <b>组合根</b>：依赖字段、启动接线、每帧推进、常量表。</item>
/// <item><c>Main.Ui.cs</c> = 表现层：场景搭建、界面刷新、玩家命令入口、输入。</item>
/// <item><c>Main.Expedition.cs</c> = 副本与结算（战斗执行 + 结算入账）。</item>
/// <item><c>Main.Save.cs</c> = 存档读写。</item>
/// <item><c>Main.Collector.cs</c> = 经济采集器（R-05 的测量装置）。</item>
/// <item><c>Main.Events.cs</c> = 事件管道（<c>Emit</c> 是 C1/C2 的唯一入口）。</item>
/// <item><c>Main.SelfTest.cs</c> = 11 个真机自检（验证装置，不是实现装置）。</item>
/// </list>
/// 守卫 <c>MainStructureTests</c> 盯着这个结构：<b>任一文件不得同时拥有
/// 「模拟推进 / 战斗执行 / 结算入账 / 存档写入」中的三类以上</b>，
/// 且每个方法都必须被显式分类（漏分类即红）。</para>
/// </summary>
public partial class Main : Node2D
{
    /// <summary>
    /// 队伍人数上限 —— <b>临时占位</b>（<c>07</c> 的 <b>P-10</b>【待定】）。
    /// </summary>
    /// <remarks>
    /// <b>设计基线从未确认过队伍人数</b>。原来的裸字面量 <c>4</c> 把一个未决项
    /// 焊进了代码，还漏到了界面上（「点名字组队，上限 4」）——
    /// 与「酒馆」「按性格算配合系数」是同一类毛病：
    /// <b>在代码里断言一件还没定的事</b>。见 <c>07</c> §2.1 第 3 条纪律。
    /// </remarks>
    private const int SquadMax = 4;   // 【待定】P-10 —— 开发者拍板后替换

    /// <summary>第三阶段 M2 的社交活动标识（与 <c>DailyLife</c> 的活动集同字面量）。</summary>
    private const string SocialActivityId = "社交";

    /// <summary>
    /// 村庄时钟。应用层适配器，包着领域层的 <c>SimClock</c>。
    /// <b>它取代了 <c>WorldClock</c></b>（已作废，07 D-001）：旧时钟的 <c>DeepNight</c>、
    /// <c>Hour</c> 驱动的上下线判定、以及「人下班后停工」整套前提都不再成立。
    /// </summary>
    private readonly VillageClock _clock = VillageClock.NewVillageClock(OS.GetCmdlineUserArgs());
    /// <summary>
    /// 游戏随机源 —— <b>【待定 T-007】占位</b>，但必须是<b>状态可存档</b>的。
    /// </summary>
    /// <remarks>
    /// 原来用的是 <see cref="Random"/>。它<b>不暴露内部状态</b>，
    /// 于是 <c>SaveGame.RandomState</c> 存不了它，读档后的战斗结果
    /// 就与存档前不可复现 —— 而「读档重放得到同样的结果」正是
    /// <c>DungeonFlow</c> 对战斗结果标识所许的诺。
    /// <para>
    /// <b>部分可复现是最坏的一种</b>：战斗可复现、抽任务不可复现，
    /// 读档后走的是另一条路。所以本类里<b>全部</b>随机都走这一个源。
    /// </para>
    /// </remarks>
    private readonly ClaudeCodeGameStudios.Domain.Heroes.DecisionFlow.SeededRandom _rng = new(BootSeed);

    /// <summary>新开局的种子。<b>读档时会被存档里的状态取代</b>，所以它只对新开局有意义。</summary>
    // System.Environment —— 写全名是因为 Godot 也有一个 Environment，会撞名。
    private static readonly ulong BootSeed = ResolveBootSeed();

    /// <summary>
    /// 解析开局种子：<c>--seed=N</c> 优先；否则按时间派生（<b>每次运行都不同</b>）。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么要能固定</b>（D-054 阶段 3）：采集器的种子来自
    /// <c>Environment.TickCount</c> → 同一份代码两次跑出的数据也不同 →
    /// 跨版本比较只能给区间（实测旧代码两份样本的成交额差 <b>2.3 倍</b>）。
    /// 固定种子让「同一代码两次跑」<b>逐行一致</b>，回归才有可比性。</para>
    /// <para><b>注意</b>：固定种子也固定了 <see cref="SessionToken"/>（副本实例标识的组成部分），
    /// 于是两次运行的副本标识会相同 —— 采集器**不落盘**结算幂等键，因此**不会**互相误伤。
    /// （真机自检会落盘，那种场景别用固定种子。）</para>
    /// <para>【补注 · 原文不改】上面那句提醒针对的是「**同一个存档**上跑两次」：
    /// 副本标识相同 ⇒ 结算幂等键相同 ⇒ 第二次会被当成「已结算」。
    /// 而真机自检**每次都用全新的存档端口**（<c>run-selfchecks.py</c> 每次另起一个临时目录），
    /// 存档里**没有**上一次的结算记录 ⇒ **不会**撞键。
    /// ⇒ 所以真机自检现在**默认用固定种子**：固定种子 ⇒ 每次运行结果可复现
    /// ⇒ CI 不会因为「这次抽到的英雄构成不一样」而偶发红。</para>
    /// </remarks>
    private static ulong ResolveBootSeed()
    {
        foreach (var a in OS.GetCmdlineUserArgs())
        {
            if (a.StartsWith("--seed=", StringComparison.Ordinal)
                && ulong.TryParse(a.Substring("--seed=".Length), out var fixedSeed))
            {
                return fixedSeed;
            }
        }

        return unchecked((ulong)System.Environment.TickCount * 2654435761UL) + 1UL;
    }

    /// <summary>
    /// 本次会话的令牌 —— 参与副本实例标识的构造。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么副本标识要带会话令牌</b>：原来的标识是 <c>d{小时}</c>，
    /// 同一小时内跑第二次副本会得到<b>同一个</b>标识，
    /// 于是结算的幂等键（<c>RunId#BattleResultId</c>）也相同 ——
    /// 第二次会被判成「这个键已经结算过」，<b>奖励被静默跳过</b>：
    /// 不发放，也不报错，玩家只看到「副本打赢了却什么也没拿到」。</para>
    ///
    /// <para>加上会话令牌后<b>跨会话也不会撞</b>：上一局留下的结算键
    /// 永远不会误伤这一局的新副本。副本实例本身不落盘，
    /// 所以「同一个副本被结算两次」只可能发生在<b>一次会话内</b> ——
    /// 而会话内由递增序号保证逐个不同。</para>
    /// </remarks>
    private static readonly string SessionToken = BootSeed.ToString("x16");

    /// <summary>本次会话内的副本序号。副本实例标识必须逐个不同。</summary>
    private int _dungeonRunSeq;

    /// <summary>[0, bound) 内的一个整数。</summary>
    private int NextInt(int bound) => (int)_rng.NextDouble(bound);

    /// <summary>[0, 1) 内的一个数。</summary>
    private double NextUnit() => _rng.NextDouble(1.0);

    private VillageView _village = null!;
    private VBoxContainer _chron = null!;

    /// <summary>右栏「关系」摘要 Label（第三阶段 M6，只读）。</summary>
    private Label _relationInfo = null!;

    /// <summary>关系摘要当前显示的行文本 —— 只在变化时更新 Label，避免每帧重排。</summary>
    private readonly List<string> _relationLines = new();
    private Label _stats = null!;
    private Godot.ItemList _roster = null!;

    /// <summary>
    /// 花名册「当前显示在 <see cref="_roster"/> 里」的行文本 —— 用来判断要不要重建。
    /// </summary>
    /// <remarks>
    /// <b>为什么需要这份镜像</b>：<c>ItemList.Clear()</c> 会把滚动位置与选中项一起复位。
    /// 2026-10-19 之前 <c>Refresh()</c> 每帧都 <c>Clear()</c> 再逐条 <c>AddItem()</c>，
    /// 于是玩家拖一下滚动条、下一帧就被打回顶部 —— 看起来「滚了也没有更多人」。
    /// 行文本没变就<b>不要碰</b> <see cref="_roster"/>。
    /// </remarks>
    private readonly List<string> _rosterLines = new();

    private Label _squadInfo = null!;
    private Button _btnTask = null!;
    private Button _btnUpgrade = null!;
    // D-074：独立建筑（围栏 / 试炼塔）—— 与村庄整体升级**并存**的两条路。
    private Button _btnBuildFence = null!;
    private Button _btnBuildTower = null!;

    /// <summary>「建防护罩」按钮（D-095）。</summary>
    private Button _btnBuildShield = null!;
    private Button _btnTrialTower = null!;
    private Button _btnSummon = null!;
    private Button _btnMarket = null!;  // R-02：看摊位（正式购买 UI + 摊位库存面板）
    private Button _btnSave = null!;    // M7：存盘按钮
    private Button _btnLoad = null!;    // M7：读盘按钮

    /// <summary>M7 摆摊服务（持摊位目录 + 5% 入村金库）。</summary>
    private ClaudeCodeGameStudios.Application.Stall.StallService _stallService = null!;

    /// <summary>R-02 摊位面板（两跳：点摊位 → 点买家）。</summary>
    private ClaudeCodeGameStudios.Presentation.StallMarketPanel _stallPanel = null!;

    /// <summary>R-02 摊位市场（候选买家 + 买尽可能多；结算仍走 StallService）。</summary>
    private ClaudeCodeGameStudios.Application.Stall.StallMarketService _market = null!;

    /// <summary>M4 英雄自主经济行为（"每英雄每天 ≤ 1 次" 决策的 DI 入口）。</summary>
    private ClaudeCodeGameStudios.Application.Stall.HeroBehaviorService _heroBehavior = null!;

    /// <summary>每日自主购物调度（第四阶段 3B）：同一游戏日只尝试一次。</summary>
    private ClaudeCodeGameStudios.Application.Stall.DailyShoppingService _dailyShopping = null!;

    private readonly List<HeroAgent> _heroes = new();
    /// <summary>与 <c>_heroes</c> 一一对应的领域状态（基线 §4.1 的「当前状态」层）。</summary>
    private readonly List<HeroState> _states = new();

    /// <summary>
    /// 英雄 Id 的分配器（不变量 <b>I1</b>：同一 Id 只对应一个英雄实体）。
    /// <para>读档后必须<b>重建</b>它（见 <c>Main.Save.cs</c> 的 <c>ApplySave</c>）——
    /// 否则分配器不知道存档里已有哪些号，可能发出一个已被占用的号。</para>
    /// </summary>
    private ClaudeCodeGameStudios.Application.HeroIdAllocator _heroIds = new();

    /// <summary>
    /// 全部英雄关系 —— <b>第三阶段的关系唯一写者</b>（<c>05</c> I10）。
    /// </summary>
    /// <remarks>
    /// 关系有向（I8）：<c>A→B</c> 与 <c>B→A</c> 分别存储。
    /// 社交 / 同队副本通过 <see cref="RelationshipBook.Apply"/> 写入，
    /// 业务键保证同一结果只应用一次（P3-D08）。
    /// </remarks>
    private readonly RelationshipBook _relationship = new();

    /// <summary>关系应用入口（社交 / 同队副本）—— 幂等键与双向规则在应用层。</summary>
    private ClaudeCodeGameStudios.Application.RelationshipService _relationships = null!;

    /// <summary>物品发放的受控入口（第四阶段 M1）。单笔发放供 <c>--grant=</c> 调试自检用；
    /// <b>批量发放是 R-01 的正式路径</b> —— 副本战利品走它进英雄个人背包。</summary>
    private ClaudeCodeGameStudios.Application.ItemGrantService _itemGrant = null!;

    /// <summary>
    /// 副本战利品的<b>内容来源</b>（R-01）。注入点：P-08 拍板后只换实现。
    /// </summary>
    private ClaudeCodeGameStudios.Domain.Settlement.ILootProvider _lootProvider = null!;

    /// <summary>
    /// 最近一次副本的<b>战利品分配</b>（R-01）。<b>只读用途</b>：事件渲染与自检输出。
    /// </summary>
    /// <remarks>
    /// <b>为什么结算不读它</b>：结算的奖励委托是<b>注入</b>的，本次副本的战利品
    /// 通过 <c>Apply(..., rewardsOverride)</c> <b>随调用带进去</b>。
    /// 若让委托反过来读这个字段，任何一次与副本无关的结算都可能把上一场的战利品
    /// 当成自己的奖励再发一遍 —— 那是「状态留在全局、靠调用方记得清」的老毛病。
    /// </remarks>
    private ClaudeCodeGameStudios.Domain.Settlement.LootAllocation _lastDungeonLoot =
        ClaudeCodeGameStudios.Domain.Settlement.LootAllocation.Empty;

    /// <summary>村庄日常循环。取代已删除的 <c>VillageLife</c>（见 07 的 D-012）。</summary>
    private DailyLife _life = null!;
    /// <summary>队伍（UI/渲染用）。<b>合法性由 <see cref="_squadRules"/> 判定</b>，不由它自己决定。</summary>
    private readonly List<HeroAgent> _squad = new();

    /// <summary>
    /// 编队规则的<b>注入点</b>（基线 §13.5：模块通过明确接口协作，不硬编码）。
    /// <para>两条规则<b>必须分开</b>：<c>CanJoin</c> 是「这个人此刻能不能进」（S5 前置校验），
    /// <c>IsRosterLegal</c> 是「这支队伍够不够格出发」。合成一条会让队伍永远建不起来 ——
    /// 若「上限 4」也用于加入，则逐个加入永远跨不过下限。已实测过这个死锁。</para>
    /// </summary>
    private ClaudeCodeGameStudios.Domain.Quests.SquadRules _squadRules = null!;

    /// <summary>进行中的任务。null = 还没出发。</summary>
    private ClaudeCodeGameStudios.Domain.Quests.Mission? _mission;

    /// <summary>副本地名。第一阶段只有一个副本（基线 §8.2）。</summary>
    private const string DungeonName = "北境旧堡";

    /// <summary>
    /// 战斗模块 —— <b>【待定 T-004】占位实现</b>。
    /// <para>
    /// 解算器与结论读者<b>必须是同一个对象</b>：占位实现只写一次胜负，
    /// 再原样读回，两个接缝因此不可能互相矛盾。
    /// </para>
    /// </summary>
    /// <remarks>
    /// <b>为什么不是字段初始化器</b>：C# 不允许字段初始化器引用<b>实例</b>字段，
    /// 而这里要传的正是 <see cref="_rng"/>。所以它在 <see cref="_Ready"/> 里赋值。
    /// </remarks>
    private ClaudeCodeGameStudios.Application.FormulaCombat _combat = null!;
    /// <summary>杂谈呈现。容器在场景构建时注入；本类不拥有 Godot 节点的生命周期。</summary>
    private ChroniclePresenter _chronicle = null!;

    /// <summary>
    /// 结算器 —— <b>副本奖励发放的唯一路径</b>（不变量 I10 / I15）。
    /// </summary>
    /// <remarks>
    /// <para>资源写入接到 <see cref="_villageModel"/> 的写入口（I10），
    /// 而不是直接改 <see cref="Mat"/> —— <c>Mat</c> 是只读的。</para>
    /// <para><b>奖励内容属【待定 T-004】</b>：现在取的是任务表里的
    /// <see cref="Job.Amt"/>，那同样是无出处的占位值。T-004 拍板后，
    /// 只换 <see cref="RewardsFor"/> 一个方法即可。</para>
    /// <para><b>采集任务仍走 <see cref="Deposit"/> 直发</b>，尚未接入本结算器
    /// —— 它没有战斗结果，而 <c>SettlementKey</c> 是
    /// <c>(RunId, BattleResultId)</c> 复合键。这是已知缺口，不是设计。</para>
    /// </remarks>
    /// <remarks>
    /// <b>为什么不是字段初始化器</b>：C# 不允许字段初始化器引用实例方法，
    /// 而本构造器的两个参数正是本类的方法组。所以它在 <see cref="_Ready"/> 里赋值。
    /// </remarks>
    private ClaudeCodeGameStudios.Domain.Settlement.Settlement _settlement = null!;

    /// <summary>本次副本要发的资源种类。由 <see cref="ResolveDungeon"/> 在结算前写入。</summary>
    private string _pendingRewardResource = "";

    /// <summary>村庄升级的扣款服务（D-054-02：金币 + 材料）。</summary>
    private ClaudeCodeGameStudios.Application.VillageUpgradeService _upgradeService = null!;

    /// <summary>独立建筑的建造流程（D-074：围栏 / 试炼塔，人力决定工期）。</summary>
    private ClaudeCodeGameStudios.Application.BuildingService _buildingService = null!;

    /// <summary>试炼塔的挑战流程（D-074：打赢 ⇒ 永久加生命）。</summary>
    private ClaudeCodeGameStudios.Application.TrialTowerService _trialTower = null!;

    /// <summary>建造与试炼的接线（D-074：玩家命令 → 领域动作）。</summary>
    private ClaudeCodeGameStudios.Application.BuildingCoordinator _buildingCoordinator = null!;

    /// <summary>召唤的扣费服务（D-077：召唤要花 100 金）。</summary>
    private ClaudeCodeGameStudios.Application.SummonService _summonService = null!;

    /// <summary>给英雄钱包发金币的写入入口（D-054-01；记「累计发放」）。</summary>
    private ClaudeCodeGameStudios.Application.WalletGrantService _walletWriter = null!;

    /// <summary>本次副本要发的数量。<b>【待定 T-004】无出处。</b></summary>
    private long _pendingRewardAmount;

    /// <summary>
    /// 本局使用的<b>价格表</b>（D-054 阶段 7：从 <c>config/prices.json</c> 读，读不到则用占位表）。
    /// </summary>
    /// <remarks>
    /// <b>为什么要外部化</b>：价格本身是【待定 P-14】（开发者与 ChatGPT 商定），
    /// 而<b>货币侧的全部标定</b>（X 的固定点、稳态、分布）都随价格变。
    /// 把表放外面 ⇒ 「开发者定价格 → 重跑标定」= 改一个文件 + 跑一条命令。
    /// </remarks>
    private ClaudeCodeGameStudios.Domain.Items.PriceBook _priceBook =
        ClaudeCodeGameStudios.Domain.Items.PriceBook.Default;

    /// <summary>价格表的<b>来源说明</b>（写进采集日志，便于溯源）。</summary>
    private string _priceBookSource = "内置占位表（P-14 未定）";

    /// <summary>
    /// 载入价格表：<b>外部文件优先</b>，读不到 / 解析失败 ⇒ <b>退回占位表</b>。
    /// </summary>
    /// <remarks>
    /// <para><b>查找顺序</b>：① <c>--prices=&lt;路径&gt;</c>（命令行显式指定）
    /// ② <c>res://config/prices.json</c>（仓库里的那份）
    /// ③ <c>user://prices.json</c>（导出后的运行时覆盖）
    /// ④ 内置占位表。</para>
    /// <para><b>为什么解析失败不中止游戏</b>：价格表坏了应该退回占位表继续跑，
    /// 而不是让游戏起不来（与「掉落表配错不发放」同一条原则）。</para>
    /// </remarks>
    private void LoadPriceBook()
    {
        var candidates = new List<string>();
        var explicitPath = OS.GetCmdlineUserArgs()
            .FirstOrDefault(a => a.StartsWith("--prices=", StringComparison.Ordinal))?
            .Substring("--prices=".Length);
        if (!string.IsNullOrWhiteSpace(explicitPath))
        {
            candidates.Add(explicitPath);
        }

        candidates.Add(Godot.ProjectSettings.GlobalizePath("res://config/prices.json"));
        candidates.Add(Path.Combine(OS.GetUserDataDir(), "prices.json"));

        foreach (var path in candidates)
        {
            if (!ClaudeCodeGameStudios.Domain.Items.PriceBook.TryLoadFromFile(
                    path, out var book, out var error))
            {
                if (File.Exists(path))
                {
                    GD.Print($"PRICEBOOK 忽略 {path}：{error}");
                }
                continue;
            }

            _priceBook = book!;
            _priceBookSource = path;
            GD.Print($"PRICEBOOK 使用外部价格表 {path}：{book!.Describe()}");
            return;
        }

        _priceBook = ClaudeCodeGameStudios.Domain.Items.PriceBook.Default;
        _priceBookSource = "内置占位表（config/prices.json 不存在或不可读）";
        GD.Print($"PRICEBOOK {_priceBookSource}：{_priceBook.Describe()}");
    }

    /// <summary>
    /// 副本金币报酬的默认值（单位 1/100 金）。
    /// </summary>
    /// <remarks>
    /// <b>X = 0.5 金</b>（<b>D-109</b>，2026-10-23 夜；此前是 0.05 金 —— D-069，2026-10-21）。
    ///
    /// <para>⚠️ <b>为什么从 0.05 变成 0.5</b>（这是**第三次**改 X）：</para>
    /// <para>① D-069 那次：「固定点落在 0.05」—— <b>那个判据本身已经失效</b>
    /// （D-106）：把 X 扫穿后发现 <b>X 的全部作用就是「累计发放」那一项</b>
    /// （每 +0.01 X ≈ +0.2 金/天），而钱每天涨 <b>3~11 金</b>
    /// ⇒ <b>X 是弱旋钮</b>，「找变号的那一档」那条判据<b>失效</b>
    /// （X=0.06 / 0.07 两子都在涨，降到 0.03 也照样在涨）。</para>
    /// <para>② 更要紧的是<b>副本报酬本身没有意义</b>：X=0.05 时参战者跑一场拿 <b>0.1 金</b>，
    /// 而采集一天是 <b>3~9 金</b> ⇒ <b>副本的参战动机几乎为零</b>
    /// ⇒ 副本成了纯耗时（基线 §8 的核心玩法之一）。</para>
    /// <para>③ D-109 拍板：<b>X = 0.5 金</b>（每次参战 0.5 金 ⇒ 一场两人副本 1 金，
    /// 量级接近一天的采集），<b>并把判据从「流通金稳态」改成
    /// 「分布健康 + 村庄花得出去」</b>。</para>
    /// <para>⚠️ <b>历史留着</b>（不删）：0.12（1000 天双种子）→ 0.10（2000 天复测）→ 0.05（D-069）
    /// → <b>0.5（D-109）</b>。每一次改动的理由都不同 ⇒
    /// <b>「X 是在一个会变的世界里测出来的」这条教训比这个数本身更重要</b>。</para>
    /// <para>⚠️ <b>边界二仍然有效</b>（D-054 §5.2）：X 不得用「限制钱包」之类的手段去制造曲线；
    /// 这个值是**算出来的**，不是用夹具凑出来的。</para>
    /// </remarks>
    private const long DefaultGoldRewardUnits = 50L;

    /// <summary>
    /// 副本金币报酬率 X（单位 <b>1/100 金</b>）—— 每人每次实际参战获得多少。
    /// </summary>
    /// <remarks>
    /// <b>为什么用 1/100 金</b>：金币是整数，而标定需要的 X 是分数（如 0.5）。
    /// 零头由 <c>WalletGrantService</c> <b>按英雄分别累计</b>，攒够 1 金才发（D-054 阶段 4 拍板）。
    /// </remarks>
    private long _goldRewardUnits = DefaultGoldRewardUnits;

    /// <summary>
    /// 开局召唤多少英雄。
    /// </summary>
    /// <remarks>
    /// <b>正常玩法是 3 人</b>；<b>采集模式默认 37 人</b>（放大规模的代理值，D-054 阶段 4）。
    /// <br>为什么采集要更多：货物地板 = 人数 × 80 金 —— 3 人时地板 240 金，
    /// 「目标 150 金」比地板还低、<b>物理上不可达</b>；37 人时地板 2960 金，
    /// 目标才落在可讨论的区间里。
    /// <br><b>⚠️ 37 不是「设计确定的目标规模」</b> —— 设计文档只说「人口会涨（召唤）」
    /// 与「村庄的目标是超大城市」，<b>没有给出具体人数</b>。
    /// 37 是为了把村庄放大到「一座大城市」而取的<b>代理值</b>；
    /// 因此所有基于它的结论都按<b>人均 / 比率</b>表述，不按绝对值。
    /// <br>可用 <c>--collectheroes=N</c> 覆盖。
    /// </remarks>
    private static int InitialHeroCount()
    {
        const int normalCount = 3;
        const int collectCount = 37;   // 放大规模的代理值（设计未定具体人数）

        var args = OS.GetCmdlineUserArgs();
        if (!args.Any(a => a.StartsWith("--collect=", StringComparison.Ordinal)))
        {
            return normalCount;
        }

        var arg = args.FirstOrDefault(a => a.StartsWith("--collectheroes=", StringComparison.Ordinal));
        if (arg is null) return collectCount;

        var raw = arg.Substring("--collectheroes=".Length);
        if (!int.TryParse(raw, out var n) || n <= 0 || n > 500)
        {
            GD.Print($"COLLECTHEROES 参数无效：「{raw}」。人数必须是 1 ~ 500 之间的整数。");
            return collectCount;
        }

        return n;
    }

    /// <summary>
    /// 解析命令行 <c>--goldreward=X</c>（X 可为分数，单位金）。
    /// </summary>
    /// <returns>报酬率（单位 1/100 金）；参数非法时返回 <c>null</c>。</returns>
    /// <remarks>
    /// ⚠️ **参数非法必须明确报错并中止采集** —— 静默取整会产出
    /// 「看着像 X=1 其实不是」的样本，那比没有数据更坏（2026-10-21 实测踩过：
    /// <c>--goldreward=0.5</c> 被 <c>long</c> 解析静默丢弃，两轮采集都跑的 X=1）。
    /// </remarks>
    private static long? ParseGoldRewardUnits()
    {
        var arg = OS.GetCmdlineUserArgs()
            .FirstOrDefault(a => a.StartsWith("--goldreward=", StringComparison.Ordinal));
        if (arg is null) return DefaultGoldRewardUnits;

        var raw = arg.Substring("--goldreward=".Length);
        if (!decimal.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            || value < 0m || value > 1000m)
        {
            GD.Print($"GOLDREWARD 参数无效：「{raw}」。"
                     + "X 必须是 0 ~ 1000 之间的非负数（金），且**可以是分数**（如 0.5）。"
                     + "本次采集中止 —— 不产出数据，免得留下「看着像另一个 X」的误导样本。");
            return null;
        }

        return (long)Math.Round(value * 100m, MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// 村金库余额（M3 起由摆摊抽成建立）。<b>只读</b> —— 唯一写者是
    /// <c>StallService.BuyFromStall</c> 的村抽结算。
    /// </summary>
    /// <remarks>
    /// 顶部状态栏显示它，是 M5「经济变化可在现有 UI 中核对」的一半：
    /// 买方/卖方的金币在花名册行尾，村金库在这里。
    /// </remarks>
    private long VillageGold =>
        _villageModel.Get(ClaudeCodeGameStudios.Application.Stall.StallService.Gold);

    /// <summary>
    /// 村庄资产（D-090：升级花掉的钱变成它，它**还在**、还能再花）。
    /// </summary>
    /// <remarks>
    /// ⚠️ **D-114：顶栏要把它显示出来** —— 只显示现金的话，
    /// 玩家会看到「村金 0」，而钱其实在资产里 ⇒ **那是在骗玩家**。
    /// </remarks>
    private long VillageAsset =>
        _villageModel.Get(ClaudeCodeGameStudios.Application.VillageUpgradeService.Asset);

    private int _tier = 0;
    private float _buildProgress = 0f;
    /// <summary>-1 = 没在施工。判施工一律看这个整数，不要看浮点。</summary>
    private int _buildTarget = -1;

    /// <summary>出征中：结束时刻（绝对小时）。0 = 没出征。</summary>
    private double _pendingEnd = 0.0;

    /// <summary>
    /// 出发时<b>预解算</b>好的副本（D-075）—— 归来时用它结算，<b>不重算</b>。
    /// </summary>
    /// <remarks>
    /// <b>为什么要有它</b>：副本在日历上占的时间 = <b>行程 + 战斗</b>，
    /// 而战斗要多久<b>只有打完才知道</b> ⇒ 出发时就得先把战斗算一遍
    /// （战斗完全确定 ⇒ 结果与归来时算的<b>必然相同</b>）。
    /// </remarks>
    private PreparedDungeon? _preparedDungeon = null;
    private Job _pendingJob;

    private bool _selfTestDone;
    /// <summary>截图已等待的秒数（-1 = 没在截图）。<b>按时间</b>，不用帧数（见 ProcessShot）。</summary>
    private double _shotSeconds = -1.0;
    private string _shotPath = "";

    /// <summary>截图前要跑多久（秒）—— 由 <c>--shotdelay=</c> 给（默认 0.5 秒）。</summary>
    private double _shotDelay = 0.5;

    /// <summary>
    /// 读 <c>--shotdelay=</c>（截图前先跑多久）。
    /// </summary>
    /// <remarks>
    /// ⚠️ **为什么需要它**：默认的 0.5 秒只能拍到「开局那一刻」
    /// （三个人、空村子）。而**有意义的画面要跑几天才出现**
    /// （例如 D-091 的村庄收购站：村庄升级攒够资产之后才开张）
    /// ⇒ 没有这个参数，证据图只能拍到「什么都还没发生」的村子。
    /// </remarks>
    private void ResolveShotDelay()
    {
        foreach (var a in OS.GetCmdlineUserArgs())
        {
            if (!a.StartsWith("--shotdelay=")) continue;
            if (double.TryParse(a[11..], System.Globalization.NumberStyles.Float,
                                System.Globalization.CultureInfo.InvariantCulture, out double delay)
                && delay >= 0)
            {
                _shotDelay = delay;
            }
        }
    }

    private static readonly Job[] Jobs =
    {
        new("北坡采石",     "石料", 14, 3f,  false),
        new("南渠挖渠",     "泥土", 18, 5f,  false),
        new("河滩拾荒",     "杂物", 16, 4f,  false),
        new("镇上门市采买", "布匹", 22, 6f,  false),
        new("东林伐木",     "木料", 26, 7f,  false),
        new("北境旧堡探查", "铁料", 38, 11f, true),
    };

    private static readonly string[] UpgradeNames =
        { "盖第四间屋", "挖井", "围起村子", "起铁匠铺", "铺石板路、立旗" };

    // ── 【待定】采集任务的占位参数 ──────────────────────────────────────
    //
    // **这些数不是设计值。** 它们没有出处 —— 来源是原型里为了让
    // 「有人干活、有时不干、有时贪一点」看起来成立而随手填的。
    //
    // 集中在这里只有一个目的：**让它们一眼可辨为占位符**，
    // 并在规则拍板时被整体替换（纪律见 07 §2.1 第 2 条：
    // 用具名常量，不用裸数字）。
    //
    // 副本任务**不走这里** —— 它走 DungeonFlow + PlaceholderCombat。
    // 下面这条采集路径是另一笔账，它什么时候被替换由 T-004 / T-006 拍板决定。

    /// <summary>【待定 T-006】采集任务的基础成功率。<b>无出处。</b></summary>
    private const float PlaceholderBaseSuccessRate = 0.52f;

    /// <summary>【待定 T-006】属性对成功率的缩放除数。<b>无出处。</b></summary>
    private const float PlaceholderAttrScale = 300f;

    /// <summary>【待定 T-006】成功率上限，防止高属性把成功率顶到 1。<b>无出处。</b></summary>
    private const float PlaceholderSuccessCap = 0.95f;

    /// <summary>【待定 T-006】傲拒掉粗活的概率。<b>无出处。</b></summary>
    private const float PlaceholderPrideRefusalRate = 0.40f;

    /// <summary>【待定 T-006】贪私藏一份的概率。<b>无出处。</b></summary>
    private const float PlaceholderPocketRate = 0.45f;

    /// <summary>【待定 T-006】莽超额完成（多带）的概率。<b>无出处。</b></summary>
    private const float PlaceholderOverrunRate = 0.35f;

    /// <summary>【待定 T-006】性格对成功率的倍率。<b>无出处。</b></summary>
    private static float PersonalityRateMultiplier(string personality) => personality switch
    {
        "莽" => 1.15f,
        "傲" => 1.10f,
        _ => 1.00f,
    };

    public override void _Ready()
    {
        // 价格表（D-054 阶段 7）：**必须先于所有用价的服务**装载 ——
        // 摆摊、掉落、发放都拿同一张表，不能有的用外部表、有的用占位表。
        LoadPriceBook();
        ResolveShotDelay();

        // D-091：村庄向村民采购建材（材料有了出口 ⇒ 闲人买来的材料不再是垃圾）。
        // ⚠️ **必须早于 BuildScene**（村庄升级服务在那里创建，而它要这个采购服务）
        // ⇒ 保留金用一个**延迟读取**的委托（_upgradeService 那时还没建好，但调用时才读）。
        _procurement = new ClaudeCodeGameStudios.Application.MaterialProcurement(
            _villageModel,
            _priceBook,
            ClaudeCodeGameStudios.Application.Stall.StallService.Gold,
            () => _tier < ClaudeCodeGameStudios.Application.VillageUpgradeService.MaxTier
                ? _upgradeService.CostFor(_tier).Gold
                : 0L);

        BuildScene();

        // D-117：村外的野兽 —— **开局长一批**（在 BuildScene 之后 ⇒ 随机流已就绪）。
        //    ⚠️ 用存档里那个随机流（`_rng`）⇒ 同一个种子 ⇒ 同一片野外（可复现）。
        SeedWildlife();

        // 关系应用入口：社交、同队副本都走它（幂等键与双向规则在应用层）。
        _relationships = new ClaudeCodeGameStudios.Application.RelationshipService(_relationship);

        // 第四阶段 M1：物品发放入口。单笔发放供 --grant 调试自检；
        // **批量发放是 R-01 的正式路径** —— 副本战利品走它进英雄个人背包，
        // 不另建一套平行的物品系统。
        _itemGrant = new ClaudeCodeGameStudios.Application.ItemGrantService(
            id => _heroes.FirstOrDefault(h => h.Id == id),
            _priceBook);

        // R-05：战利品内容 = **掉落表**（权重抽取）。物品注册表管「是什么、什么稀有度」，
        // 价格表管「多少钱」；掉落表引用物品 Id，配置有问题时**不发放**并记一笔
        // （不崩游戏 —— 「这个副本今天不掉东西」比「游戏起不来」好对付）。
        // 分配规则（LootSplit）一行都不用改。
        _lootProvider = new ClaudeCodeGameStudios.Application.DungeonLootProvider(
            new[] { ClaudeCodeGameStudios.Application.DefaultLootTables.For(DungeonName) },
            ClaudeCodeGameStudios.Domain.Items.ItemCatalog.Default,
            _priceBook,
            error => GD.Print($"LOOTCONFIG {error}"));

        // 结算器必须在这里造：它的两个委托是本类的方法组，
        // 字段初始化器引用不了（C# 的限制）。必须在 BuildScene 之后 ——
        // ApplyWrites 要用 _villageModel。
        //
        // 第四个参数是**个人物品的写入入口**（R-01）：村庄资源与英雄个人物品
        // 因此进的是同一次结算、共用同一把幂等锁（I15：要么完整应用，要么不应用）。
        // D-054-01：给英雄个人钱包发金币的写入入口。
        // **留成字段**（不内联）：它记着「累计发放了多少金币」与「每英雄的待发零头」，
        // 守恒律的「累计新增」那一项要从这里取。
        //
        // ⚠️ 报酬率必须**在构造之前**定好 —— 写入入口把 X 当规则（不放在增量里，
        // 免得同一个数字在两处定义、迟早分叉）。
        var goldUnits = ParseGoldRewardUnits();
        if (goldUnits is null) { GetTree().Quit(); return; }   // 参数非法：明确报错并中止
        _goldRewardUnits = goldUnits.Value;

        _walletWriter = new ClaudeCodeGameStudios.Application.WalletGrantService(
            id => _heroes.FirstOrDefault(h => h.Id == id),
            ClaudeCodeGameStudios.Application.Stall.StallService.Gold,
            _goldRewardUnits);

        // 村庄资源的整笔写入入口（I15 的村庄侧）。
        // ⚠️ 必须在这里造：它要 _villageModel，而 _villageModel 是字段初始化器
        // （BuildInitialVillage）—— 字段初始化器里 new 一个依赖它的服务是不行的。
        _villageWriter = new ClaudeCodeGameStudios.Application.VillageResourceWriter(_villageModel);

        _settlement = new ClaudeCodeGameStudios.Domain.Settlement.Settlement(
            RewardsFor, ApplyWrites, itemWriter: _itemGrant,
            walletWriter: _walletWriter);

        // 战斗规则（D-059…D-065）：**它不再有随机数** —— D-062 决定不设命中判定
        // ⇒ 战斗完全确定 ⇒ 「战斗可复现、抽任务不可复现」那个半成品问题**自动消失**：
        // 战斗不从共享随机流里取数，战斗与日常的随机**互不干扰**。
        // ⇒ 也因此，副本胜负**不再**是「单场 0.65」的占位，而是**涌现值** ⇒
        //    经济标定必须重跑（`production/qa/recalibrate.py`）。
        _combat = new ClaudeCodeGameStudios.Application.FormulaCombat();

        // D-074：独立建筑的建造流程 + 试炼塔挑战。
        // ⚠️ 试炼塔与副本**共用同一个战斗解算器** ⇒ 战斗规则只有一份
        // （D-062 不设命中 ⇒ 战斗完全确定 ⇒ 同一个英雄进塔结果永远一样）。
        _buildingService = new ClaudeCodeGameStudios.Application.BuildingService(
            _villageModel,
            new ClaudeCodeGameStudios.Domain.Village.ResourceId("金币"),
            MaterialsId,
            _procurement,
            () => _heroes);
        _trialTower = new ClaudeCodeGameStudios.Application.TrialTowerService(_combat, _combat);
        _buildingCoordinator = new ClaudeCodeGameStudios.Application.BuildingCoordinator(
            _buildingService, _trialTower, id => _heroes.FirstOrDefault(h => h.Id.Value == id.Value));

        // D-077：召唤要花 100 金（从村金库扣）。
        _summonService = new ClaudeCodeGameStudios.Application.SummonService(
            _villageModel, ClaudeCodeGameStudios.Application.Stall.StallService.Gold);

        // D-079：大事件框架（模板注册表 + 实例簿）。
        InitWorldEvents();

        // 日常循环的初始化必须早于 OnSummon —— 它要往 _states 里加条目
        // 第三阶段 M2：随机源改为 Main._rng（可存档），社交目标与关系权重在此注入。
        // ⚠️ **D-086**：采集的材料进**采集者自己的背包**（⇒ 能卖 ⇒ 才是个人收入），
        // 不再直接进村庄材料池（那个池子已经过剩到 7.5 万份了）。
        // ⚠️ **D-105**：有采集点（井，D-099 的层级附带建筑）⇒ 采集**多采一点**。
        // 加成**每次现问**村庄（委托）⇒ 升级 / 读档后自动跟着变，不会变成过期值。
        _gather = new ClaudeCodeGameStudios.Application.GatherService(
            _stallService, _priceBook,
            // ⚠️ **D-111 对照开关**：`--gatherbonus=0` 可以关掉采集点的加成
            // ⇒ 用来分离「采集加成」这一个变量（见 `07` 的 D-111）。
            () => _gatherBonusOff
                ? 0L
                : ClaudeCodeGameStudios.Domain.Village.GatheringPoints.BonusFor(_villageModel));
        // D-087：村庄收购站 —— 村庄按市价收购在村英雄的采集品，钱从村库回流到人。
        // 预算规则在服务里（村库余额 − 下一级村庄升级所需金币）⇒ 这里只给依赖。
        _purchase = new ClaudeCodeGameStudios.Application.VillagePurchaseService(
            _villageModel,
            _priceBook,
            ClaudeCodeGameStudios.Application.Stall.StallService.Gold,
            MaterialsId,
            _upgradeService,
            () => _tier);
        // ⚠️ **D-118：狩猎服务要在 DailyLife 之前建好**（效果表要用它）。
        BuildHuntService();
        _life = new DailyLife(_rng, DailyLife.DefaultEffects(
            (actorId, n) => GatherFor(actorId, n),
            d => _buildProgress = MathF.Min(1f, _buildProgress + (float)d),
            onSocial: OnSocialCompleted,
            onHunt: OnHuntCompleted),
            activityDurationSeconds: _clock.SimSecondsPerDay * DailyLife.PlaceholderActionFractionOfDay,
            targetCandidates: SocialTargetCandidates,
            targetWeight: (hero, targetId) => RelationshipRules.TargetWeight(
                _relationship.ValueOf(hero.Id, targetId)));

        // D-054 阶段 3：召唤用**另一个**随机源（HeroFactory 自己的 System.Random），
        // 不播种它，「同一种子两次跑」就会得到**不同的英雄** → 数据仍不可复现。
        // 必须在第一次召唤之前播种。
        HeroFactory.Seed(BootSeed);

        // D-054 阶段 8（A7 收尾）：杂谈也有一条自己的随机流（挑模板/挑配角）。
        // 不播种它 → **同一个种子两次跑会写出不同的杂谈**（Determinism 标准不满足，
        // 而且采集样本之间的可比性会打折）。它从会话种子**另派生**一条流，
        // 不与召唤共用同一条（见 Chronicle.DeriveStreamSeed 的说明）。
        Chronicle.Seed(BootSeed);

        // B4（2026-10-21）：**世界表数据驱动** —— 读 config/worlds.json，
        // 读不到 / 坏了 ⇒ 退回内置表（HeroFactory.LoadWorldsFromFile 负责兜底）。
        // 必须在**第一次召唤之前**装载：召唤时才读 World.All。
        bool externalWorlds = HeroFactory.World.LoadWorldsFromFile(
            Godot.ProjectSettings.GlobalizePath("res://config/worlds.json"));
        GD.Print($"WORLDTABLE {(externalWorlds ? "使用外部世界表" : "使用内置世界表")}：" +
                 $"{(externalWorlds ? "config/worlds.json" : "（外部配置不可读）")}" +
                 $"，{HeroFactory.World.All.Length} 个世界");

        for (int i = 0; i < InitialHeroCount(); i++) OnSummon();
        // 原实现在这里把开局三人「钉」在固定在线窗口上（PinOnlineAt），
        // 那属于作废的时区作息。基线 §2.2：英雄总在场，不做在线窗口。
        // 编队规则：加入看「在不在场」，出发看「人数够不够」——两件事分开
        _squadRules = new ClaudeCodeGameStudios.Domain.Quests.SquadRules(
            canJoin: st => IsPresent(AgentOf(st)),
            isRosterLegal: m => m.Count <= SquadMax);

        // ── 开局<b>不预编</b>。原来的 `for i<2 _squad.Add(_heroes[i])` 是"方便玩家"之举，
        // 但<b>反直觉</b>：玩家看不到「他们默认就在队里」，
        // 第一次点他们的名字是<b>出队</b>，于是「我点名字怎么没进队」成为必然。
        // 行为「所见即所得」：点谁就是加谁。
        Deposit(40);                                              // 起手材料

        // 第二阶段 D-021：英雄起手各 100 钱 —— 货币定义是
        // 「金币」（在 Domain.Items.Wallet 之上，由 HeroAgent.TryAddResource 注入）。
        // 第一阶段没有「经济」概念，这 100 钱是 **Village（村庄）** 与 **英雄** 共用
        // 「金币」这个资源身份 —— 摆摊时英雄付钱、村庄收摊位费、
        // 副本成功村庄也发奖励（D-018 修过的坑：**MaterialsId** 是材料，
        // **金**币和材料是两个池子）。
        //
        // 100 是 D-021 的【建议】占位，**不是设计值**。若调成 0 或 1000，
        // 测试 + 自检都能感知 —— 但【建议】数字不该轻易换。
        var gold = new ClaudeCodeGameStudios.Domain.Village.ResourceId("金币");
        foreach (var h in _heroes)
        {
            h.TryAddResource(gold, 100);
        }

        Refresh();
    }

    // ---------------------------------------------------------------- 配合


    // ---------------------------------------------------------------- 每帧

    public override void _Process(double delta)
    {
        float dt = (float)delta;

        // 1. 时间自己走
        _clock.Tick(dt);
        bool newDay = _clock.Rolled;

        // 2. 村庄自己过日子：空闲且在场的英雄做决定，到期的行动结算并产生领域事件
        // 工地位置由应用层注入（领域对象不应知道表现层的常量，基线 §13.3）
        foreach (var h in _heroes) { h.SiteX = VillageView.SiteX; h.SiteY = VillageView.SiteY; }
        foreach (var ev in _life.Step(_states, dt)) PublishEvent(ev);

        // 3. 跨天了
        if (newDay)
        {
            // 每日事件改由 Domain/DecisionFlow 驱动（见 07 的 D-001 迁移记录）。
            // 旧的 VillageLife 八类事件全部建立在作废状态上（AbsentDays / SlackDays /
            // RestlessDays / GrudgeWith），已整体删除。
            _clock.ConsumeRoll();
            if (_soak)
                GD.Print($"SOAK 第{_clock.Day}天 {_clock.Clock} 在村{_heroes.Count(h => h.Present)}/{_heroes.Count} " +
                         $"材料{Mat} 层级{_tier} 杂谈{_chronicle.Count}条 | " +
                         string.Join(" ", _chronicle.Recent(3)));

            // 4c-0. 村庄收购（D-087）：**必须在摆摊之前** ——
            //     否则采集品已经被挪进摊位（背包里没了），村庄就收不到。
            //     钱从村库出（预算 = 村库余额 − 下一级村庄升级所需金币）。
            VillagePurchaseRound();

            // 4c-0. 放置模式的预览跟随鼠标（D-093）。
            //     ⚠️ **已建建筑的标记不在这里更新** —— 那要**每帧**更新
            //     （建筑是玩家当场放下的 ⇒ 只在日切更新就晚了一整天）。
            UpdatePlacementPreview();

            // 4c. 自主卖货（D-086）：背包里有可卖的东西就开摊 ⇒
            //     没参与副本的人也有收入（否则他们的钱包永远是空的）。
            //     ⚠️ 与采集器**共用同一份**（`OpenStallsForIdleHeroes`）⇒ 测到的就是玩到的。
            OpenStallsForIdleHeroes();

            // 采集：副本 → 摆摊。**挂在日切上** —— 跟着真实游戏循环走，
            // 采集器只在日界插一脚，不自己推进时间（自己推进会跳过
            // 「英雄在工地干活」那段状态机，结果永远挑不出人）。
            if (_collectTargetDays > 0)
            {
                CollectOneDay();
                _collectDays++;

                GD.Print($"COLLECT 第{(int)_clock.Day}天 事件{_events.Count}条 杂谈{_chronicle.Count}条 " +
                         $"摊位{_stallService.Stalls.Count}个 流水{_economyLog?.RowCount ?? 0}行 " +
                         $"村金{_villageModel.Get(ClaudeCodeGameStudios.Application.Stall.StallService.Gold)}");

                if (_collectDays >= _collectTargetDays)
                {
                    GD.Print($"COLLECT 完成：{_collectDays} 天 · 事件 {_events.Count} 条 · " +
                             $"流水 {_economyLog?.RowCount ?? 0} 行 · 跳过副本 {_collectSkippedDungeons} 次");

                    // ⚠️ **D-103：采集结束时写一次存档** ——
                    // 原因：**有些机制不进经济流水**（关系变化、村庄升级次数…）
                    // ⇒ 机制覆盖探测器需要**第二路**（读存档）才能查它们
                    // ⇒ 而采集模式原先**从不写盘** ⇒ 那一路永远是空的。
                    // ⚠️ **写的是自检专用端口**（`--savepath=`）⇒ 不会碰玩家存档。
                    try
                    {
                        _saves.Write(BuildSave());
                        GD.Print($"COLLECT 已写存档：{_saves.FilePath}");
                    }
                    catch (Exception ex)
                    {
                        GD.Print($"COLLECT 写存档失败（不影响采集结果）：{ex.GetType().Name} {ex.Message}");
                    }

                    _economyLog?.Close();
                    GetTree().Quit();
                    return;
                }
            }
        }

        // 4. 每日自主购物（第四阶段 3B）：按游戏日触发一次。
        //    每帧只判一个整数（LastRunDay == today 就直接返回），不会每帧执行购物逻辑。
        RunDailyShoppingIfNeeded();

        // 4b. 大事件（D-079）：按游戏日推进一次（触发 → 阶段 → 结算）。
        TickWorldEvents();

        // 4b-0. 村外的野兽（D-117）：**每天走一步**（夜里活动 ⇒ 白天换位置）。
        //    ⚠️ 野兽**不入档**（野生生物是「世界的样子」，不是玩家的进度）。
        TickWildlife();

        // 4b-1. 地图上「已建成的建筑」标记（D-093）—— **每帧**更新
        //   （玩家随时可能放下新建筑 ⇒ 放在日切里会晚一整天才显示）。
        UpdateBuildingMarks();

        // 4b-2. 村外的野兽（D-117）：**每帧**喂给视图（它们的位置每帧都在动）。
        _village.SetWildlife(_wildlife.Beasts);
        AdvanceWildlifePhase(dt);

        // 5. 出征回来了没有
        if (Expending && _clock.AbsoluteHour >= _pendingEnd) ResolveExpedition();

        TickBuild(dt);
        TickIndependentBuilds(dt);

        Refresh();

        if (OS.GetCmdlineUserArgs().Contains("--selftest") && !_selfTestDone) { _selfTestDone = true; SelfTest(); }
        if (OS.GetCmdlineUserArgs().Contains("--dungeon") && !_dungeonTestDone) RunDungeonSelfTest();
        if (OS.GetCmdlineUserArgs().Contains("--events") && !_eventsTestDone) RunEventsSelfTest();
        if (OS.GetCmdlineUserArgs().Contains("--click") && !_clickTestDone) RunClickSelfTest();
        if (OS.GetCmdlineUserArgs().Contains("--economy") && !_economyTestDone) RunEconomySelfTest();
        if (OS.GetCmdlineUserArgs().Contains("--relations") && !_relationsTestDone) RunRelationsSelfTest();
        if (OS.GetCmdlineUserArgs().Any(a => a.StartsWith("--grant=", StringComparison.Ordinal)) && !_grantTestDone)
            RunGrantSelfTest();
        if (OS.GetCmdlineUserArgs().Contains("--trade") && !_tradeTestDone) RunTradeSelfTest();
        if (OS.GetCmdlineUserArgs().Any(a => a.StartsWith("--collect=", StringComparison.Ordinal)) && !_collectDone)
        {
            // 采集驱动器**先跑**：它跑完就退出，后面的自检不该再插进来。
            RunCollect();
            return;
        }
        if (OS.GetCmdlineUserArgs().Contains("--market") && !_marketTestDone) RunMarketSelfTest();
        if (OS.GetCmdlineUserArgs().Contains("--build") && !_buildTestDone) RunBuildSelfTest();
        if (OS.GetCmdlineUserArgs().Contains("--caravan") && !_caravanTestDone) RunCaravanSelfTest();
        if (OS.GetCmdlineUserArgs().Contains("--bandit") && !_banditTestDone) RunBanditSelfTest();
        if (OS.GetCmdlineUserArgs().Contains("--tier") && !_tierTestDone) RunTierSelfTest();
        if (OS.GetCmdlineUserArgs().Contains("--event") && !_eventTestDone) RunEventSelfTest();
        if (OS.GetCmdlineUserArgs().Contains("--shop") && !_shopTestDone) RunShoppingSelfTest();
        if (OS.GetCmdlineUserArgs().Any(a => a is "--save" or "--load") && !_saveTestDone)
        {
            RunSaveSelfTest();
        }
        ProcessShot(dt);
    }

    /// <summary>--soak：不按键跑若干天，每天打一行。用来证明「村庄真的会自己过日子」。</summary>
    private readonly bool _soak = OS.GetCmdlineUserArgs().Contains("--soak");

    /// <summary>
    /// 每帧推进工地。
    /// <para><b>原注释描述的是已作废的设计</b>：「人不在场就停工，但时间照走」依赖时区作息，
    /// 而基线 §2.2 明确<b>英雄总在场</b>。「傍晚人全下了线，进度条停住」这个现象<b>不会再发生</b>。</para>
    /// <para>现在停工的唯一原因是<b>全村没有一个指派到工地的人</b>，或者<b>在场的人没有可做的事</b>。
    /// 时间的压力来自<b>行动耗时</b>（【待定】P-04 / T-002），不是来自「谁在线」。</para>
    /// </summary>
    private void TickBuild(float dt)
    {
        if (_buildTarget < 0) return;

        float rate = 0f;
        foreach (var h in _heroes)
        {
            if (!h.ToSite || !IsPresent(h)) continue;
            rate += h.WorkRate;
        }
        if (rate <= 0f) return;                 // 没人手，停工

        _buildProgress += rate * dt;
        _village.BuildProgress = MathF.Min(1f, _buildProgress);
        if (_buildProgress >= 1f)
        {
            int done = _buildTarget;
            _tier = done;
            _buildProgress = 0f;
            _buildTarget = -1;
            _village.BuildProgress = -1f;
            _village.SetTier(_tier);
            foreach (var h in _heroes) h.ToSite = false;   // 队伍不动，任务照发

            // D-099：这一级**该有的建筑自动出现**（层级附带建筑）。
            // ⚠️ 之前那口井只是地图上的**手绘装饰** ⇒ 村里其实没有这栋建筑
            // ⇒ 现在它是**真的建筑实例**（存在、入档、看得见）。
            // ⚠️ 规则住在 `VillageUpgradeService.CompleteUpgrade`（可离线单测）
            // ⇒ 这里只负责**调用**它。
            foreach (var b in _upgradeService.CompleteUpgrade(_tier))
            {
                GD.Print($"TIER 第 {_tier} 级 ⇒ 村里多了一栋「{b.Id.Value}」。");
            }

            // 建成 / 升级 —— 基线 §7.2 明列的一类
            string what = done == 5
                ? "石板路铺到了村口，旗立起来了"
                : UpgradeNames[done - 1] + "盖成了";
            Emit(EvBuildingDone, what, new[] { StVillageBuilding },
                new Dictionary<string, string>
                {
                    ["what"] = what, ["tier"] = done.ToString(),
                },
                ev => Chronicle.Building((int)_clock.Day, ev.Payload["what"]).Text);
        }
    }

    /// <summary>
    /// 每帧推进<b>独立建筑</b>的施工（D-074：围栏 / 试炼塔）。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么与 <see cref="TickBuild"/> 分开</b>：两者的进度模型不同
    /// —— 村庄升级用「干活速度 × 时间」（既有机制），
    /// 独立建筑用「<b>人数 × 时间 ÷ 工期</b>」（D-074 的「人力决定工期」）。
    /// ⇒ 混在一起会得出两个互不相容的进度定义。</para>
    /// <para><b>为什么一次只盖一样</b>：施工队是<b>同一批人</b>
    /// ⇒ 村庄升级在施工时，独立建筑就<b>拿不到人</b>（停工），
    /// 反之亦然 ⇒ 两条路不会互相偷人力。</para>
    /// </remarks>
    /// <param name="dt">本帧经过的现实秒数。</param>
    private void TickIndependentBuilds(float dt)
    {
        if (_buildingService.ActiveConstructions.Count == 0) return;

        // 现实秒 → 游戏内小时：一个游戏日 = SimSecondsPerDay 模拟秒 = 24 游戏内小时。
        double gameHours = dt * 24.0 / _clock.SimSecondsPerDay;
        if (gameHours <= 0.0) return;

        // 人力 = 没在路上、也不在村庄升级工地的人。
        int workers = _heroes.Count(h => !h.Away && !h.ToSite && IsPresent(h));

        // ⚠️ **人力只算一次**（D-082）：原来每栋在建建筑都拿到**全村**人力
        // ⇒ 围栏与试炼塔同时施工时同一批人被算了两遍 ⇒ 人力凭空翻倍。
        // ⇒ 现在由 `AssignWorkers` **分**下去（均分 + 余数给最早开工的）。
        _buildingService.AssignWorkers(workers);

        _buildingCoordinator.Advance(gameHours, OnIndependentBuildingCompleted);
    }

    /// <summary>独立建筑建成：发事件（基线 §7.2「建筑建成 / 升级」）。</summary>
    /// <param name="definition">刚建成的建筑。</param>
    private void OnIndependentBuildingCompleted(BuildableBuilding definition)
    {
        Emit(EvBuildingConstructed, definition.Name, new[] { StVillageBuilding },
            new Dictionary<string, string>
            {
                ["what"] = definition.Name,
                ["effect"] = definition.Effect.ToString(),
            },
            ev => Chronicle.Building((int)_clock.Day, ev.Payload["what"]).Text);

        GD.Print($"BUILD {definition.Name} 建成。");
    }

    private bool _eventsTestDone;
    private bool _clickTestDone;
    private bool _economyTestDone;

    private bool _dungeonTestDone;
    private bool _tradeTestDone;
    private bool _marketTestDone;

    /// <summary><c>--build</c> 自检跑过了没（D-093：玩家自选落点）。</summary>
    private bool _buildTestDone;

    /// <summary><c>--caravan</c> 自检跑过了没（D-088：商队成交在真实游戏里的第一次）。</summary>
    private bool _caravanTestDone;

    /// <summary><c>--tier</c> 自检跑过了没（D-099：层级附带建筑在真实游戏里的第一次）。</summary>
    private bool _tierTestDone;

    /// <summary><c>--event</c> 自检跑过了没（D-101：大事件在真实游戏里真的结算了吗）。</summary>
    private bool _eventTestDone;

    /// <summary><c>--bandit</c> 自检跑过了没（D-096：盗匪来袭在真实游戏里的第一次）。</summary>
    private bool _banditTestDone;

    private bool _collectDone;

    /// <summary>采集目标天数（0 = 不采集）。</summary>
    private int _collectTargetDays;

    /// <summary>已采集的天数。</summary>
    private int _collectDays;

    /// <summary>因为「没人可派」而跳过的副本次数。</summary>
    private int _collectSkippedDungeons;

    private bool _shopTestDone;

    /// <summary>最近一次每日购物对应的游戏日。</summary>
    private int _lastShoppingDay;

    private bool _grantTestDone;

    private bool _relationsTestDone;

    /// <summary>
    /// 经济流水落盘（运行闭环验证与经济数据采集准备）。<b>为 null = 不落盘</b>。
    /// </summary>
    /// <remarks>
    /// 只有命令行给了 <c>--econlog=&lt;路径&gt;</c> 才有它 —— 正常游戏不写盘。
    /// 它收的是<b>已记录</b>的经济事件（副本掉落 / 交易），不收全事件流。
    /// </remarks>
    private ClaudeCodeGameStudios.Data.EconomyLog? _economyLog;

    // ── 事件类型（对应基线 §7.2 的名单）──
    // ⚠️ **每个事件类型都必须在这里标出它对应基线 §7.2 的哪一类**（写在 `new("…")` 那一行的行尾）。
    //    这行标注是「事件类型 → 基线类」映射的**唯一真源** ——
    //    `ChronicleEventCoverageTests` 直接读它，再去核对盘点文档 §6 的缺口表。
    //    ⇒ 新增事件类型时忘了写标注，守卫会红（而不是默默多出一种没人归类的杂谈）。
    private static readonly ClaudeCodeGameStudios.Domain.Events.EventTypeId EvSummoned =
        new("HeroSummoned");                 // §7.2「召唤到新英雄」
    private static readonly ClaudeCodeGameStudios.Domain.Events.EventTypeId EvDispatched =
        new("SquadDispatched");               // §7.2「大事件的发生与结算」
    private static readonly ClaudeCodeGameStudios.Domain.Events.EventTypeId EvDailyWork =
        new("VillageDailyWork");              // §7.2「村庄日常（采集、摆摊、社交等）」
    private static readonly ClaudeCodeGameStudios.Domain.Events.EventTypeId EvDungeonFought =
        new("DungeonEncounterFought");        // §7.2「大事件的发生与结算」
    private static readonly ClaudeCodeGameStudios.Domain.Events.EventTypeId EvDungeonSettled =
        new("DungeonSettled");                // §7.2「大事件的发生与结算」＋「英雄首次完成副本」
    private static readonly ClaudeCodeGameStudios.Domain.Events.EventTypeId EvBuildingDone =
        new("BuildingCompleted");             // §7.2「建筑建成 / 升级」
    private static readonly ClaudeCodeGameStudios.Domain.Events.EventTypeId EvSocialized =
        new("HeroSocialized");                 // §7.2「村庄日常（采集、摆摊、社交等）」—— 第三阶段 M5：英雄之间的社交
    private static readonly ClaudeCodeGameStudios.Domain.Events.EventTypeId EvSquadBonded =
        new("SquadBonded");                    // §7.2「大事件的发生与结算」—— 第三阶段 M5：同队副本合作（一条聚合）
    private static readonly ClaudeCodeGameStudios.Domain.Events.EventTypeId EvTradeCompleted =
        new("TradeCompleted");                 // §7.2「村庄日常（采集、摆摊、社交等）」—— 第四阶段 3B：英雄自主购物成交
    private static readonly ClaudeCodeGameStudios.Domain.Events.EventTypeId EvRelationshipWorsened =
        new("RelationshipWorsened");           // §7.2「两名英雄关系恶化」—— D-071：同队副本失败 = 队内每对一次纠纷
    private static readonly ClaudeCodeGameStudios.Domain.Events.EventTypeId EvBuildingConstructed =
        new("BuildingConstructed");            // §7.2「建筑建成 / 升级」—— D-074：独立建筑建成
    private static readonly ClaudeCodeGameStudios.Domain.Events.EventTypeId EvTrialTowerCleared =
        new("TrialTowerCleared");              // §7.2「英雄受伤 / 康复」—— D-074：试炼塔打赢 ⇒ 永久加生命
    private static readonly ClaudeCodeGameStudios.Domain.Events.EventTypeId EvWorldEventStarted =
        new("WorldEventStarted");              // §7.2「大事件的发生与结算」—— D-079：大事件预兆
    private static readonly ClaudeCodeGameStudios.Domain.Events.EventTypeId EvWorldEventSettled =
        new("WorldEventSettled");             // §7.2「大事件的发生与结算」—— D-079：大事件结算
    private static readonly ClaudeCodeGameStudios.Domain.Events.EventTypeId EvCaravanTrade =
        new("CaravanTrade");                  // §7.2「村庄日常（采集、摆摊、社交等）」—— D-081：商队成交
    private static readonly ClaudeCodeGameStudios.Domain.Events.EventTypeId EvWorldEventSkipped =
        new("WorldEventSkipped");            // §7.2「大事件的发生与结算」—— D-082：兽潮守不住 ⇒ 这次没来

    /// <summary>
    /// 副本战利品结算（运行闭环验证与经济数据采集准备）。
    /// </summary>
    /// <remarks>
    /// <para><b>它确实改了背包</b>（<c>hero.bag</c>），所以它<b>符合</b>反装饰规则 ——
    /// 声明得出状态变化，事件成立。</para>
    /// <para><b>但它不进杂谈</b>（渲染委托返回 null，与「日常劳作」同一路）：
    /// 37 人每天一两条「某某捡到了石料」会把村庄杂谈淹掉。
    /// 玩家要看的成交信息由 <see cref="EvTradeCompleted"/> 承担。</para>
    /// <para><b>为什么要它</b>：原先副本掉落了什么<b>只存在于内存事件里</b>，
    /// 存一次档就全没了 —— 经济流水无从采集。它把「这次副本产出了什么、
    /// 分给了谁」变成一条<b>可落盘</b>的事实。</para>
    /// </remarks>
    private static readonly ClaudeCodeGameStudios.Domain.Events.EventTypeId EvDungeonLooted =
        new("DungeonLooted");                 // §7.2「英雄首次完成副本」

    // ── 状态键 ──
    // 反装饰规则（03 §5.2 / 基线 §7.3）靠它们在**构造期**生效：
    // 一个事件必须声明自己改变了什么，声明不出就根本创建不出来。
    private static readonly ClaudeCodeGameStudios.Domain.Events.StateKey StHeroActivity =
        new("hero.currentActivity");
    private static readonly ClaudeCodeGameStudios.Domain.Events.StateKey StHeroTally =
        new("hero.tally");
    private static readonly ClaudeCodeGameStudios.Domain.Events.StateKey StHeroPocketed =
        new("hero.pocketed");
    private static readonly ClaudeCodeGameStudios.Domain.Events.StateKey StHeroRoster =
        new("village.heroRoster");
    private static readonly ClaudeCodeGameStudios.Domain.Events.StateKey StVillageResource =
        new("village.resource");
    private static readonly ClaudeCodeGameStudios.Domain.Events.StateKey StVillageBuilding =
        new("village.building");
    private static readonly ClaudeCodeGameStudios.Domain.Events.StateKey StDungeonRun =
        new("dungeon.run");
    private static readonly ClaudeCodeGameStudios.Domain.Events.StateKey StBattleResult =
        new("combat.battleResult");
    private static readonly ClaudeCodeGameStudios.Domain.Events.StateKey StSettlement =
        new("settlement.applied");
    private static readonly ClaudeCodeGameStudios.Domain.Events.StateKey StRelationship =
        new("relationship.value");             // 第三阶段 M5：关系变化
    private static readonly ClaudeCodeGameStudios.Domain.Events.StateKey StWorldEvent =
        new("worldEvent.instance");            // D-079：大事件实例（阶段 / 结算）
    private static readonly ClaudeCodeGameStudios.Domain.Events.StateKey StHeroWallet =
        new("hero.wallet");                    // 第四阶段 3B：英雄货币变化
    private static readonly ClaudeCodeGameStudios.Domain.Events.StateKey StHeroBag =
        new("hero.bag");                       // 第四阶段 3B：英雄背包变化

    /// <summary>已记录的事件数（自检用）。</summary>
    private int EventCount => _events.Count;

    // ── 存档 ──────────────────────────────────────────────────────────────

    /// <summary>
    /// 存档端口。路径由构造参数给出，不写死 <c>user://</c> —— 自检要写到别处。
    /// <b>Phase 1 自检写 bug</b>：<c>user://</c> 路径在 SavePort 里被当成本地路径用，
    /// Windows 下解析成 <c>D:\...\user:</c>（带冒号）写盘失败。
    /// <b>修复</b>：_Ready 末尾用 <c>ProjectSettings.GlobalizePath</c> 解析。
    /// </summary>
    private ClaudeCodeGameStudios.Data.SavePort _saves = null!;

    /// <summary>
    /// 自检专用的存档文件名 —— <b>与玩家存档刻意不同名</b>。
    /// </summary>
    /// <remarks>
    /// <b>为什么</b>：<c>--save</c> / <c>--relations</c> 自检要真的写一次盘、读一次档。
    /// 而 <c>_saves</c> 指向的正是<b>玩家存档</b>（<c>village-save.json</c>）——
    /// 自检覆盖它是「跑个自检把存档清了」这种事故。
    /// 所以自检用<b>自己的端口</b>，默认写 <c>selftest-village-save.json</c>。
    /// <para>
    /// 仍然保留 <c>--savepath=</c> 覆盖能力 —— 显式指定路径是调用方的明确意图
    /// （例如临时目录、或刻意要验某个具体路径）。
    /// </para>
    /// </remarks>
    private const string SelfTestSaveFileName = "selftest-village-save.json";

    private bool _saveTestDone;
}
