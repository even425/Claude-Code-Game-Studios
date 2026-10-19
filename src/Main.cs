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
// 别名而非整包 using：领域类型就叫 Village，而本类已有一个名为 _village 的
// VillageView（表现层节点）。两个都叫「村庄」，混用会读错。
using DomainVillage = ClaudeCodeGameStudios.Domain.Village.Village;
using ResourceId = ClaudeCodeGameStudios.Domain.Village.ResourceId;

/// <summary>
/// 主场景。整棵树在代码里搭，不依赖 .tscn —— 少一个会悄悄坏掉的序列化文件。
///
/// 运行：godot --path . （主场景已配在 project.godot）
/// 截图：godot --path . -- --shot=res://shot.png
///
/// M0 的立意：**你不按任何键，村庄也在过日子。**
/// 时间自动走、英雄<b>总在场</b>（基线 §2.2：不做在线窗口、不做缺席）、
/// 每天早上村庄自己发生几件事。
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
    private static readonly ulong BootSeed =
        unchecked((ulong)System.Environment.TickCount * 2654435761UL) + 1UL;

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
    private Button _btnSummon = null!;
    private Button _btnStall = null!;   // M7：摆摊按钮
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
    /// 副本战利品的<b>内容来源</b>（R-01）。注入点：T-007 拍板后只换实现。
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
    private ClaudeCodeGameStudios.Application.PlaceholderCombat _combat = null!;
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

    /// <summary>本次副本要发的数量。<b>【待定 T-004】无出处。</b></summary>
    private long _pendingRewardAmount;

    /// <summary>副本胜利时发什么。<b>失败一律发空</b>（D-010 Q1「失败不给奖励」）。</summary>
    /// <remarks>
    /// <b>这是默认奖励</b>：<b>不含个人物品</b>。与副本无关的结算走它 ——
    /// 那里本来就没有战利品可发。
    /// 副本的结算走 <see cref="RewardsFor(bool, ClaudeCodeGameStudios.Domain.Settlement.LootAllocation)"/>，
    /// 把本次的战利品<b>随调用带进去</b>。
    /// </remarks>
    private ClaudeCodeGameStudios.Domain.Settlement.SettlementDelta RewardsFor(bool heroesWon)
        => RewardsFor(heroesWon, ClaudeCodeGameStudios.Domain.Settlement.LootAllocation.Empty);

    /// <summary>
    /// 本次结算的奖励：村庄资源 + <paramref name="loot"/> 里的个人战利品。
    /// </summary>
    /// <param name="heroesWon">战斗是否胜利。</param>
    /// <param name="loot">本次副本的战利品分配（由调用方随这次结算带进来）。</param>
    /// <remarks>
    /// <b>两半奖励，一次结算</b>（R-01）：村庄资源（既有行为）与英雄个人物品
    /// （D04「不设公共奖励池，物品直接归个人」）被放进<b>同一个增量</b>，
    /// 于是 <c>Settlement.Apply</c> 能用<b>同一把幂等锁</b>把它们一起落下去 ——
    /// 中间任何一步失败都不会留下「村庄拿到了、英雄没拿到」的半笔账。
    /// </remarks>
    private ClaudeCodeGameStudios.Domain.Settlement.SettlementDelta RewardsFor(
        bool heroesWon, ClaudeCodeGameStudios.Domain.Settlement.LootAllocation loot)
    {
        var delta = new ClaudeCodeGameStudios.Domain.Settlement.SettlementDelta();

        // 失败不发任何东西。注意这里<b>照常返回一个空的增量</b>而不是抛错 ——
        // 「失败也要发生一次结算」正是幂等键要防的那条路：若失败时不结算，
        // 同一次远征被重放就会重复发放胜利奖励。
        if (!heroesWon) return delta;

        // ── 村庄侧：材料（既有行为，数值无出处这一点不变）──
        if (!string.IsNullOrEmpty(_pendingRewardResource) && _pendingRewardAmount > 0)
        {
            delta.AddResource(_pendingRewardResource, _pendingRewardAmount);
        }

        // ── 英雄个人：战利品（R-01 · D01–D04）──
        foreach (var grant in loot.Grants)
        {
            delta.AddItemGrant(grant.Hero, grant.Item, grant.Quantity);
        }

        return delta;
    }

    /// <summary>
    /// 把结算的写入接到村庄的写入口（<b>不变量 I10</b>），并保证
    /// <b>全有或全无</b>（<b>I15</b>）。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么必须先全量预检再提交</b>：<c>Settlement.Apply</c> 把
    /// 「写入是否成功」的判断完全交给本委托，而 <c>Village</c> 的
    /// <c>Add</c> 在溢出时会抛、<c>TrySpend</c> 在余额不足时会返回 <c>false</c>。
    /// 若边校验边提交，<b>中间一笔失败就会留下半笔账</b> —— 那正是 I15 要禁止的。</para>
    /// <para>预检同时排掉两种失败：扣减后变负、加上后越界。
    /// 两者都被拦下之后，提交循环里的调用<b>不会抛</b>，
    /// 因此不存在「写了一半」的路径。</para>
    /// </remarks>
    /// <param name="writes">本次结算要写的资源增量。</param>
    /// <returns><c>true</c> 表示整笔已写入；<c>false</c> 表示<b>一笔都没写</b>。</returns>
    private bool ApplyWrites(ClaudeCodeGameStudios.Domain.Settlement.ResourceDelta[] writes)
    {
        if (writes is null || writes.Length == 0) return true;

        // ── 预检：任何一笔不可行，整笔拒绝，此时什么都没写 ──
        foreach (var w in writes)
        {
            var id = new ResourceId(w.ResourceId);
            long current = _villageModel.Get(id);
            long next = current + w.Amount;

            if (next < 0) return false;                                  // 会被扣成负数
            if (w.Amount > 0 && current > long.MaxValue - w.Amount) return false;  // 会溢出
        }

        // ── 提交：预检已排除全部失败路径，这里不会抛 ──
        foreach (var w in writes)
        {
            var id = new ResourceId(w.ResourceId);
            if (w.Amount > 0)
            {
                _villageModel.Add(id, w.Amount);
            }
            else
            {
                _villageModel.TrySpend(id, -w.Amount);   // 预检已保证扣得起
            }
        }

        return true;
    }

    /// <summary>
    /// 村庄的<b>领域模型</b> —— 材料的唯一存放处（不变量 I10）。
    /// </summary>
    /// <remarks>
    /// <para><b>它以前根本不存在。</b>本类原先有个 <c>int _mat</c>，
    /// 材料只存在于表现层；而 <c>Domain/Village</c> 那个带
    /// <see cref="DomainVillage.TrySpend"/>（I12 资源不变负）的类型
    /// <b>从未被 new 过</b>。</para>
    /// <para><b>为什么现在必须接上</b>：<c>Settlement</c> 的资源写入按
    /// I10 <b>必须接到村庄模块的写入口</b>。若把结算的奖励写进
    /// <c>_mat</c> 而日常产出走别的路，就会出现<b>两个材料池</b> ——
    /// 比现在更难查。</para>
    /// <para><b>注意别与 <see cref="_village"/> 混淆</b>：那是
    /// <c>VillageView</c>，表现层节点，只管画。</para>
    /// </remarks>
    private readonly DomainVillage _villageModel = BuildInitialVillage();

    /// <summary>M5 / D-033：开局在 (-5, -5) 放一个市集，影响区覆盖 (0,0)——默认摆摊位置能过位置守门。</summary>
    private static DomainVillage BuildInitialVillage()
    {
        var v = new DomainVillage("村庄");
        var market = new ClaudeCodeGameStudios.Domain.Village.BuildingInstance(
            new ClaudeCodeGameStudios.Domain.Village.BuildingId("village_square"),
            gridX: -5, gridY: -5, footprintW: 1, footprintH: 1,
            constructionProgress: 1.0,
            affectsAreaW: 11, affectsAreaH: 11);
        v.TryPlace(market);
        return v;
    }

    /// <summary>材料的资源种类。</summary>
    private static readonly ResourceId MaterialsId = new("材料");

    /// <summary>当前材料量。<b>只读</b> —— 改动一律走 <see cref="Deposit"/> 或 <see cref="DomainVillage.TrySpend"/>。</summary>
    private long Mat => _villageModel.Get(MaterialsId);

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

    /// <summary>存入材料。非正增量<b>什么都不做</b>（<see cref="DomainVillage.Add"/> 要求增量为正）。</summary>
    private void Deposit(long amount)
    {
        if (amount > 0) _villageModel.Add(MaterialsId, amount);
    }

    private int _tier = 0;
    private float _buildProgress = 0f;
    /// <summary>-1 = 没在施工。判施工一律看这个整数，不要看浮点。</summary>
    private int _buildTarget = -1;

    /// <summary>出征中：结束时刻（绝对小时）。0 = 没出征。</summary>
    private double _pendingEnd = 0.0;
    private Job _pendingJob;

    private bool _selfTestDone;
    private int _shotFrame = -1;
    private string _shotPath = "";

    /// <summary>一个任务。Hours 是**游戏内小时**——北境旧堡要跑一整天。
    /// Kind 决定它算「粗活」还是「有对手的活」，影响傲的接不接。</summary>
    private readonly record struct Job(string Name, string Res, int Amt, float Hours, bool Glory);

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
        BuildScene();

        // 关系应用入口：社交、同队副本都走它（幂等键与双向规则在应用层）。
        _relationships = new ClaudeCodeGameStudios.Application.RelationshipService(_relationship);

        // 第四阶段 M1：物品发放入口。单笔发放供 --grant 调试自检；
        // **批量发放是 R-01 的正式路径** —— 副本战利品走它进英雄个人背包，
        // 不另建一套平行的物品系统。
        _itemGrant = new ClaudeCodeGameStudios.Application.ItemGrantService(
            id => _heroes.FirstOrDefault(h => h.Id == id),
            ClaudeCodeGameStudios.Domain.Items.PriceBook.Default);

        // R-01：副本战利品的**占位**内容来源。T-007 拍板后只换这一处实现，
        // 分配规则（LootSplit）一行都不用改。
        _lootProvider = new ClaudeCodeGameStudios.Application.PlaceholderDungeonLootProvider();

        // 结算器必须在这里造：它的两个委托是本类的方法组，
        // 字段初始化器引用不了（C# 的限制）。必须在 BuildScene 之后 ——
        // ApplyWrites 要用 _villageModel。
        //
        // 第四个参数是**个人物品的写入入口**（R-01）：村庄资源与英雄个人物品
        // 因此进的是同一次结算、共用同一把幂等锁（I15：要么完整应用，要么不应用）。
        _settlement = new ClaudeCodeGameStudios.Domain.Settlement.Settlement(
            RewardsFor, ApplyWrites, itemWriter: _itemGrant);

        // 战斗模块与本类其余随机同源 —— 否则只有战斗可复现，
        // 而「战斗可复现、抽任务不可复现」是最坏的一种半成品。
        _combat = new ClaudeCodeGameStudios.Application.PlaceholderCombat(_rng);

        // 日常循环的初始化必须早于 OnSummon —— 它要往 _states 里加条目
        // 第三阶段 M2：随机源改为 Main._rng（可存档），社交目标与关系权重在此注入。
        _life = new DailyLife(_rng, DailyLife.DefaultEffects(
            n => Deposit(n), d => _buildProgress = MathF.Min(1f, _buildProgress + (float)d),
            onSocial: OnSocialCompleted),
            activityDurationSeconds: _clock.SimSecondsPerDay * DailyLife.PlaceholderActionFractionOfDay,
            targetCandidates: SocialTargetCandidates,
            targetWeight: (hero, targetId) => RelationshipRules.TargetWeight(
                _relationship.ValueOf(hero.Id, targetId)));

        for (int i = 0; i < 3; i++) OnSummon();
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
        var left = new VBoxContainer
        {
            Position = new Vector2(UiLayout.LeftX, UiLayout.PanelTopY),
            Size = new Vector2(UiLayout.LeftW, UiLayout.PanelHeight),
        };
        left.AddThemeConstantOverride("separation", (int)UiLayout.VBoxSeparation);
        AddChild(left);

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
        _btnSummon = MakeButton("招募一个人", left, OnSummon);
        _btnStall = MakeButton("摆摊", left, OnStall);
        _btnMarket = MakeButton("看摊位", left, OnMarket);
        _btnSave = MakeButton("存盘", left, OnSave);
        _btnLoad = MakeButton("读盘", left, OnLoad);
        // 修 Phase 1 自检写盘 bug：user:// → 本地路径
        var savePath = OS.GetCmdlineUserArgs()
            .FirstOrDefault(a => a.StartsWith("--savepath=", StringComparison.Ordinal))?
            .Substring("--savepath=".Length)
            ?? $"user://{ClaudeCodeGameStudios.Data.SavePort.DefaultFileName}";
        savePath = Godot.ProjectSettings.GlobalizePath(savePath);
        _saves = new ClaudeCodeGameStudios.Data.SavePort(savePath);

        // M7 摆摊服务 —— 依赖 _villageModel + PriceBook + _heroes
        // heroLookup 委托：用 HeroAgent.Id（已设）查 —— D-0…（略）
        _stallService = new ClaudeCodeGameStudios.Application.Stall.StallService(
            _villageModel,
            ClaudeCodeGameStudios.Domain.Items.PriceBook.Default,
            id => _heroes.FirstOrDefault(h => h.Id == id));
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
        }

        // 4. 每日自主购物（第四阶段 3B）：按游戏日触发一次。
        //    每帧只判一个整数（LastRunDay == today 就直接返回），不会每帧执行购物逻辑。
        RunDailyShoppingIfNeeded();

        // 5. 出征回来了没有
        if (Expending && _clock.AbsoluteHour >= _pendingEnd) ResolveExpedition();

        TickBuild(dt);

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
        if (OS.GetCmdlineUserArgs().Contains("--market") && !_marketTestDone) RunMarketSelfTest();
        if (OS.GetCmdlineUserArgs().Contains("--shop") && !_shopTestDone) RunShoppingSelfTest();
        if (OS.GetCmdlineUserArgs().Any(a => a is "--save" or "--load") && !_saveTestDone)
        {
            RunSaveSelfTest();
        }
        ProcessShot(dt);
    }

    /// <summary>--soak：不按键跑若干天，每天打一行。用来证明「村庄真的会自己过日子」。</summary>
    private readonly bool _soak = OS.GetCmdlineUserArgs().Contains("--soak");

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

    private void OnSummon()
    {
        var s = HeroFactory.Summon();
        // M7 / D-030：把 HeroId 同步到 HeroAgent（M2 挂账兑现）。
        // **同一个 id 必须在 Add 之前算好**：原来 Agent 用 Add 前的 Count、Definition 用
        // Add 后的 Count，于是两边恒差 1（h-0 vs h-1）。M4 之前没有任何东西同时引用两边，
        // 直到摊位的 OwnerId（Agent）要去 heroes[]（Definition Id）里找引用才暴露。
        string id = $"h-{_heroes.Count}";
        s.Hero.AssignId(new HeroId(id));
        _heroes.Add(s.Hero);
        _states.Add(new HeroState(new HeroDefinition(
            new HeroId(id), s.Hero.Name, s.WorldName,
            preferenceWeights: new Dictionary<string, double>(StringComparer.Ordinal)
            {
                // 【待定 / P3-D05】性格影响「社交」活动权重；采集/建屋仍取恒等 1。
                ["社交"] = SocialPreferenceWeight(s.Hero.Personality),
            })));
        _village.AddHero(s.Hero);
        _village.HasPeople = true;
        Emit(EvSummoned, s.Hero.Name, new[] { StHeroRoster },
            new Dictionary<string, string>
            {
                ["world"] = s.WorldName, ["trait"] = s.Trait,
                ["personality"] = s.Hero.Personality,
            },
            _ => Chronicle.Summoned((int)_clock.Day, s.Hero, s.WorldName, s.Trait).Text);
        Refresh();
    }

    private void OnTask()
    {
        if (Expending) return;
        var ready = ReadySquad;
        if (ready.Count == 0) return;

        _pendingJob = Jobs[NextInt(Jobs.Length)];
        _pendingEnd = _clock.AbsoluteHour + _pendingJob.Hours;

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
            return;
        }
        catch (InvalidOperationException)
        {
            // 有成员已在别的任务里（不变量 I2）
            _mission = null;
            return;
        }

        var squadNames = ready.Select(h => h.Name).ToArray();
        Emit(EvDispatched, string.Join("、", squadNames),
            new[] { StHeroActivity, StDungeonRun },
            new Dictionary<string, string>
            {
                ["job"] = _pendingJob.Name,
                ["names"] = string.Join("、", squadNames),
                ["glory"] = _pendingJob.Glory ? "1" : "0",
            },
            _ => Chronicle.SquadLeft((int)_clock.Day, squadNames).Text);
        foreach (var h in ready) h.Away = true;      // 领域层已把 HeroState 置为随队；这里同步表现层的 HeroAgent

        Refresh();
    }

    private void ResolveExpedition()
    {
        var job = _pendingJob;
        var crew = _squad.Where(h => h.Away).ToList();

        // 副本任务（北境旧堡探查）走已测的 DungeonFlow：整个副本的成败由
        // 副本结果决定。采集任务仍走下面那条老路径 —— 它的成功率先标记为
        // 【待定】（见 ResolveExpedition 末尾的说明），那是另一笔账。
        if (job.Glory)
        {
            ResolveDungeon(job, crew);
            _mission = null;
            return;
        }

        // 任务收尾由领域层执行：它负责把成员从「随队」放回「在村」，
        // 并把状态推进到终态（不变量 I14：已结束不再回到进行中）。
        // 表现层不手动清 Away —— 那是英雄状态自己的事。
        if (_mission is not null)
        {
            _mission.Finish(MissionStatus.Succeeded);
            _mission = null;
            foreach (var h in crew) h.Away = false;    // 同步表现层的 HeroAgent
        }

        // 傲不是不干活 —— 他是看不上**粗活**。有对手的活他抢着去，
        // 采石伐木这种他嫌跌份，40% 的可能撂挑子。但撂了不等于不能干：
        // 他真去干了，速度也只有别人的 0.6 倍。**没有「只有他能做的活」。**
        var going = new List<HeroAgent>();
        foreach (var h in crew)
        {
            if (h.Personality == "傲" && !job.Glory && NextUnit() < PlaceholderPrideRefusalRate)
            {
                h.RefusedCount++;
                Emit(EvDailyWork, h.Name, new[] { StHeroTally },
                    new Dictionary<string, string>
                    {
                        ["kind"] = "refuse", ["job"] = job.Name,
                        ["res"] = job.Res, ["qty"] = "0",
                    },
                    _ => Chronicle.Outcome((int)_clock.Day, h, job.Name, job.Res, 0, null, "refuse").Text);
                continue;
            }
            going.Add(h);
        }
        if (going.Count == 0) going.Add(crew[0]);

        var good = new List<HeroAgent>();
        foreach (var h in going)
        {
            float chance = PlaceholderBaseSuccessRate + (h.Power + h.Speed) / PlaceholderAttrScale;
            chance *= PersonalityRateMultiplier(h.Personality);
            if (NextUnit() < MathF.Min(PlaceholderSuccessCap, chance)) good.Add(h);
        }
        if (good.Count == 0) good.Add(going[0]);

        int total = 0;
        foreach (var h in going)
        {
            if (good.Contains(h))
            {
                h.DoneCount++;
                int q = job.Amt / good.Count + h.Power / 8;
                if (h.Personality == "贪" && NextUnit() < PlaceholderPocketRate)
                {
                    int kept = Math.Max(1, q / 4);
                    q -= kept; h.Pocketed += kept;

                    // 私藏是**状态变化**（Pocketed 加了），所以它是事件；
                    // 它同时也是 §7.2 里「交易发生纠纷」的种子。
                    int keptFinal = kept;
                    Emit(EvDailyWork, h.Name, new[] { StHeroPocketed },
                        new Dictionary<string, string>
                        {
                            ["kind"] = "pocket", ["job"] = job.Name,
                            ["res"] = job.Res, ["qty"] = keptFinal.ToString(),
                        },
                        ev => Chronicle.Outcome((int)_clock.Day, h, job.Name, job.Res,
                            int.Parse(ev.Payload["qty"]), null, ev.Payload["kind"]).Text);
                }

                total += q;
                int carried = q;
                bool overran = h.Personality == "莽" && NextUnit() < PlaceholderOverrunRate;
                Emit(EvDailyWork, h.Name, new[] { StHeroActivity, StHeroTally },
                    new Dictionary<string, string>
                    {
                        ["kind"] = overran ? "over" : "good", ["job"] = job.Name,
                        ["res"] = job.Res, ["qty"] = carried.ToString(),
                    },
                    ev => Chronicle.Outcome((int)_clock.Day, h, job.Name, job.Res,
                        int.Parse(ev.Payload["qty"]), null, ev.Payload["kind"]).Text);
            }
            else
            {
                Emit(EvDailyWork, h.Name, new[] { StHeroActivity, StHeroTally },
                    new Dictionary<string, string>
                    {
                        ["kind"] = "fail", ["job"] = job.Name,
                        ["res"] = job.Res, ["qty"] = "0",
                    },
                    _ => Chronicle.Outcome((int)_clock.Day, h, job.Name, job.Res, 0, null, "fail").Text);
            }
        }

        Deposit(total);
        var cameBack = string.Join("、", good.Select(h => h.Name));
        Emit(EvDailyWork, cameBack, new[] { StVillageResource },
            new Dictionary<string, string>
            {
                ["job"] = job.Name, ["res"] = job.Res,
                ["qty"] = total.ToString(), ["names"] = cameBack,
            },
            ev => Chronicle.SquadBack((int)_clock.Day, ev.Payload["names"], ev.Payload["res"],
                int.Parse(ev.Payload["qty"])).Text);

        foreach (var h in crew) h.Away = false;
        _pendingEnd = 0f;
        Refresh();
    }

    /// <summary>
    /// 副本任务收尾 —— 走 <see cref="ClaudeCodeGameStudios.Application.DungeonFlow"/>。
    /// </summary>
    /// <remarks>
    /// <para><b>成败来自副本，不来自表现层的公式。</b>
    /// 这条路径存在的理由就是替掉旧代码里那句无条件
    /// <c>MissionStatus.Succeeded</c> —— 那会让任务<b>永远成功</b>，
    /// 于是「失败不给奖励也不额外惩罚」（D-010 Q1）里的「不给奖励」那一半
    /// 根本无从发生。</para>
    ///
    /// <para><b>不变量 I3</b>：整个副本只挂一个战斗结果，由
    /// <c>DungeonFlow</c> 负责（挂「最后打的那一场」），
    /// 本方法不碰副本实例。</para>
    ///
    /// <para><b>失败不给奖励，也不额外惩罚</b>（D-010 Q1）：不发材料、
    /// 不扣属性、不改关系、没有人留下 —— 唯一「少」的就是没拿到东西。
    /// <b>成功</b>则结算奖励 + 同队各加关系（P3-D04：同队副本成功是关系来源，失败不是）。</para>
    /// </remarks>
    /// <param name="job">本次任务。</param>
    /// <param name="crew">参战者（表现层的 <see cref="HeroAgent"/>）。</param>
    /// <returns>本次副本的完整结果，供自检与结算核对。</returns>
    /// <remarks>
    /// <b>本方法不清空 <see cref="_mission"/></b> —— 只负责把它推进到终态。
    /// 清空由调用方做，这样自检路径能在调用后仍读到任务的真实状态，
    /// 而不必在生产代码里为测试留后门。
    /// </remarks>
    /// <summary>
    /// 生成下一个副本实例标识。
    /// </summary>
    /// <returns>本次副本的实例标识。</returns>
    /// <remarks>
    /// <b>必须逐个不同</b>：它的一半是结算的幂等键
    /// （<c>SettlementKey(RunId, BattleResultId)</c>）。
    /// 两个副本若拿到同一个标识，第二个会被判成「已经结算过」，
    /// <b>奖励被静默跳过</b> —— 玩家看到的是「打赢了却什么也没拿到」。
    /// <para>
    /// 构成 = <b>小时 + 会话内序号 + 会话令牌</b>。前两项保证同一局内不重复；
    /// 会话令牌保证<b>跨局</b>也不重复（上一局留下的结算键不会误伤这一局）。
    /// </para>
    /// </remarks>
    private string NextDungeonRunId()
    {
        _dungeonRunSeq++;
        return $"d{_clock.AbsoluteHour:0}.{_dungeonRunSeq}.{SessionToken}";
    }

    /// <summary>
    /// 算出本次副本的战利品分配（R-01）。
    /// </summary>
    /// <param name="outcome">副本结果。</param>
    /// <param name="job">本次任务（提供奖励规模）。</param>
    /// <returns>分配结果。失败时是空分配。</returns>
    /// <remarks>
    /// <para><b>参战名单取自副本实例的快照</b>（<c>DungeonRun.Participants</c>），
    /// 不是当前队伍 —— 这正是 D02「实际参加该次副本的队伍成员」，
    /// 也是 E03「以本次副本实际参战快照为准」的实现。</para>
    ///
    /// <para><b>随机用独立派生的子流</b>：种子由<b>副本实例标识</b>派生
    /// （<see cref="LootSplit.DeriveSeed"/>），所以
    /// 「同一个副本 → 同一份分配」永远成立，
    /// 而掉落<b>不会插队</b>进战斗与日常共用的那条全局随机流。</para>
    /// </remarks>
    private ClaudeCodeGameStudios.Domain.Settlement.LootAllocation ComputeLoot(
        ClaudeCodeGameStudios.Application.DungeonFlowOutcome outcome, Job job)
    {
        // 失败不给战利品（D-010 Q1 / R01-D05）。
        if (!outcome.Succeeded) return ClaudeCodeGameStudios.Domain.Settlement.LootAllocation.Empty;

        var participants = outcome.Run.Participants
            .Select(id => new ClaudeCodeGameStudios.Domain.Heroes.HeroId(id))
            .ToArray();

        // E01 / E02：没有可靠的参战名单就不发放 —— 绝不退化成「全村都有份」。
        if (participants.Length == 0)
        {
            GD.Print($"LOOT 副本 {outcome.Run.Id} 没有参战名单，不发放战利品（记录结算异常）。");
            return ClaudeCodeGameStudios.Domain.Settlement.LootAllocation.Empty;
        }

        var loot = _lootProvider.LootFor(DungeonName, job.Amt);
        if (loot is null || loot.Count == 0)
        {
            return ClaudeCodeGameStudios.Domain.Settlement.LootAllocation.Empty;
        }

        var rng = new ClaudeCodeGameStudios.Domain.Heroes.DecisionFlow.SeededRandom(
            ClaudeCodeGameStudios.Domain.Settlement.LootSplit.DeriveSeed(outcome.Run.Id.Value));

        return ClaudeCodeGameStudios.Domain.Settlement.LootSplit.Split(loot, participants, rng);
    }

    private ClaudeCodeGameStudios.Application.DungeonFlowOutcome ResolveDungeon(
        Job job, List<HeroAgent> crew)
    {
        // 参与者标识取自**任务自己的成员名单** —— 那是领域层的记录，
        // 不必在这里靠 _states/_heroes 的下标对齐反推。
        //
        // 【R-01 修掉的坑】回退分支原来取的是 h.**Name**（显示名），
        // 而主分支取的是 m.Id.**Value**（英雄标识）。奖励资格要按**标识**判定
        // （背包查找、关系幂等键都用标识），显示名会与其它地方对不上 ——
        // 同一个英雄在两条路径上是两个不同的「人」。
        var ids = _mission is not null
            ? _mission.Members.Select(m => m.Id.Value).ToArray()
            : crew.Select(h => h.Id.Value).ToArray();

        var flow = ClaudeCodeGameStudios.Application.DungeonFlow.Begin(
            new ClaudeCodeGameStudios.Domain.Dungeon.DungeonRunId(NextDungeonRunId()),
            DungeonName, ids, _combat, _combat);

        var outcome = flow.RunToCompletion();
        flow.Settle();          // I4：整场只结算一次

        if (_mission is not null)
        {
            _mission.Finish(outcome.Succeeded
                ? MissionStatus.Succeeded
                : MissionStatus.Failed);
        }

        foreach (var h in crew) h.Away = false;

        var names = crew.Select(h => h.Name).ToArray();

        // ── 逐场战斗各发一个事件（验收⑤「自动战斗可读性」）──
        // 只说最后一句结论的话，玩家看不出打过一场三阶段的仗 ——
        // 那与「根本没打」在画面上没有区别。
        foreach (var record in outcome.Records)
        {
            var rec = record;
            Emit(EvDungeonFought, rec.EncounterName, new[] { StDungeonRun, StBattleResult },
                new Dictionary<string, string>
                {
                    ["encounter"] = rec.EncounterName,
                    ["stage"] = rec.Stage.ToString(),
                    ["won"] = rec.Result.HeroesWon ? "1" : "0",
                    ["deciding"] = rec.AttachedToRun ? "1" : "0",
                    ["verifiable"] = rec.Result.IsVerifiableAgainstLog() ? "1" : "0",
                    ["names"] = string.Join("、", names),
                    ["place"] = DungeonName,
                },
                ev => Chronicle.EncounterFought(
                    (int)_clock.Day, ev.Payload["place"], ev.Payload["encounter"],
                    ev.Payload["names"].Split('、'),
                    ev.Payload["won"] == "1",
                    ev.Payload["deciding"] == "1").Text);
        }

        // 整场结论：副本状态变了（EncounterX → Succeeded/Failed），是真状态变化
        Emit(EvDungeonSettled, DungeonName, new[] { StDungeonRun, StSettlement },
            new Dictionary<string, string>
            {
                ["place"] = DungeonName,
                ["won"] = outcome.Succeeded ? "1" : "0",
                ["names"] = string.Join("、", names),
            },
            ev => Chronicle.DungeonDecided(
                (int)_clock.Day, ev.Payload["place"],
                ev.Payload["names"].Split('、'),
                ev.Payload["won"] == "1").Text);

        // ── 结算：奖励只由 Settlement 发放（I10 / I15），表现层不再自己加材料 ──
        //
        // 注意用的是 **MaterialsId**，不是 job.Res。
        //
        // 【踩过的坑】第一次接线时这里传了 job.Res（「铁料」）。结果奖励进了
        // 「铁料」桶，而界面读的是「材料」桶 —— 任务显示 Succeeded、材料纹丝不动。
        // 单元测试测不到这个：它们验的是 Settlement 自己，不验「游戏把奖励
        // 放进了哪个桶」。是 --dungeon 的运行输出逮到的。
        //
        // **Job.Res 是杂谈里的显示文字，不是资源身份。** 第一阶段只有一个
        // 材料桶；真正的资源分类属【待定】T-004 / P-08。
        _pendingRewardResource = MaterialsId.Value;
        _pendingRewardAmount = job.Amt;

        // ── R-01：战利品分配（成功才有；失败一律不发）──
        // 分配在结算**之前**算好：参战名单取自副本实例的快照（D02 / E03），
        // 随机用由副本标识派生的独立子流（同一个副本 → 同一份分配）。
        //
        // **战利品随这次结算带进去**（rewardsOverride），而不是放进会话级共享状态：
        // 否则任何一次与副本无关的结算都可能把这一场的战利品再发一遍。
        var loot = ComputeLoot(outcome, job);
        _lastDungeonLoot = loot;

        var key = new ClaudeCodeGameStudios.Domain.Settlement.SettlementKey(
            outcome.Run.Id.Value, outcome.Run.BattleResultId);

        bool granted;
        try
        {
            granted = _settlement.Apply(key, outcome.Succeeded, won => RewardsFor(won, loot));
        }
        catch (InvalidOperationException ex)
        {
            // 结算被拒（背包装不下、物品未登记、余额不足……）：整笔未应用，
            // 幂等键**没有**落下 —— 状态保持不变，可以重试。
            //
            // 这里不能静默当作「发过了」（E04 / E07：不得静默吞掉奖励），
            // 也不能让异常一路冒到游戏循环里把画面打断。
            // 失败没有改变任何状态，所以它**不是**一个领域事件
            // （反装饰规则：事件必须声明它改了什么）——
            // 因此记进杂谈与日志，而不是走 Emit。
            granted = false;
            GD.Print($"SETTLEMENT 副本 {outcome.Run.Id} 的结算被拒：{ex.Message}");
            Add(new Chronicle.Entry(
                (int)_clock.Day, $"这次副本的奖励没能发下来（{ex.Message}）。", null));
        }

        if (granted && outcome.Succeeded)
        {
            foreach (var h in crew) h.DoneCount++;

            // 第三阶段 M3：同队成功 = 一次真实合作经历（P3-D04）。
            // 以副本实例为幂等键 —— 重复结算不会二次加关系（P3-D08）。
            var touched = _relationships.Cooperate(
                outcome.Run.Id.Value, crew.Select(h => h.Id).ToList());
            if (touched.Changed)
            {
                Emit(EvSquadBonded, string.Join("、", names), new[] { StRelationship },
                    new Dictionary<string, string>
                    {
                        ["place"] = DungeonName,
                        ["names"] = string.Join("、", names),
                        ["pairs"] = touched.PairsChanged.ToString(),
                    },
                    _ => $"{string.Join("、", names)}一起从{DungeonName}回来了，彼此熟了一些。");
            }

            Emit(EvDungeonSettled, string.Join("、", names),
                new[] { StVillageResource, StSettlement },
                new Dictionary<string, string>
                {
                    ["res"] = job.Res,
                    ["qty"] = _pendingRewardAmount.ToString(),
                    ["names"] = string.Join("、", names),
                },
                ev => Chronicle.SquadBack((int)_clock.Day, ev.Payload["names"],
                    ev.Payload["res"], int.Parse(ev.Payload["qty"])).Text);

            // ── R-01：个人战利品入包 → 一条可追溯的事件 ──
            // 物品进了英雄的背包（<c>StHeroBag</c>），这是<b>真的状态变化</b>，
            // 所以它配得上一个事件；而事件必须说清「谁拿到了什么」。
            if (!loot.IsEmpty)
            {
                var lootLines = loot.Grants
                    .Select(g => $"{NameOfHero(g.Hero)} {g.Item.Value}×{g.Quantity}")
                    .ToArray();
                Emit(EvDungeonSettled, string.Join("、", names),
                    new[] { StHeroBag, StSettlement },
                    new Dictionary<string, string>
                    {
                        ["place"] = DungeonName,
                        ["names"] = string.Join("、", names),
                        ["loot"] = string.Join("、", lootLines),
                    },
                    ev => $"{ev.Payload["names"]}从{ev.Payload["place"]}带回了战利品：{ev.Payload["loot"]}。");
            }
        }
        else if (!outcome.Succeeded)
        {
            // 失败：结算照常发生，但增量是空的 —— D-010 Q1「失败不给奖励，
            // 也不额外惩罚」。这里只记「白跑一趟」，不发任何东西。
            foreach (var h in crew)
            {
                Emit(EvDungeonSettled, h.Name, new[] { StHeroActivity, StSettlement },
                    new Dictionary<string, string>
                    {
                        ["job"] = job.Name, ["res"] = job.Res,
                        ["qty"] = "0", ["place"] = DungeonName,
                    },
                    ev => Chronicle.Outcome((int)_clock.Day, h, ev.Payload["job"],
                        ev.Payload["res"], 0, ev.Payload["place"], "fail").Text);
            }
        }
        // granted == false 且成功 = 该键已结算过（I4），本次不重复发放，也不记「带回来了」。

        _pendingEnd = 0f;
        Refresh();

        return outcome;
    }

    private void OnUpgrade()
    {
        if (_buildTarget >= 0) return;                      // 一次只盖一样
        int cost = 30 + _tier * 25;
        if (_tier >= VillageView.MaxTier || Mat < cost) { Refresh(); return; }

        // 钱变材料，剩下的由人去干
        _villageModel.TrySpend(MaterialsId, cost);
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
    /// M7 摆摊按钮 handler —— 弹 LineEdit 输入物品名 + 数量，<b>第 1 个在场且空闲的英雄</b>开摊。
    /// </summary>
    /// <remarks>
    /// <b>v1 极简</b>（D-029）：不选英雄（自动选第一个在场 + 空闲 + 持可售物 ≥ 1 的），
    /// 不开"选物品下拉"（直接输物品名）—— 完整 Panel v2 再做。
    /// 队伍 <b>未选</b>时也能摆摊（前提：英雄在场）。
    /// </remarks>
    private void OnStall()
    {
        // 找第一个可摆摊的英雄（在场 + 空闲 + 持可售物品 ≥ 1）
        HeroAgent? owner = null;
        foreach (var h in _heroes)
        {
            if (h.Away) continue;
            if (h.ToSite) continue;
            // 持任意一个 PriceBook 登记的物品 ≥ 1
            var anySellable = false;
            foreach (var item in new[] { "石料", "木料", "布匹" })
            {
                if (h.Bag.Count(new ClaudeCodeGameStudios.Domain.Items.ItemId(item)) >= 1)
                {
                    anySellable = true;
                    break;
                }
            }
            if (anySellable) { owner = h; break; }
        }
        if (owner is null)
        {
            Add(new Chronicle.Entry(
                (int)_clock.Day, "没人能摆摊：要么都在忙，要么背包里没可卖的（石料/木料/布匹）。", null));
            Refresh();
            return;
        }

        // 弹个临时 LineEdit + Label
        var panel = new Panel { Position = new Vector2(900, 200), CustomMinimumSize = new Vector2(280, 100) };
        AddChild(panel);
        var title = new Label { Text = $"摆摊 — {owner.Name}", Position = new Vector2(10, 6) };
        panel.AddChild(title);
        var hint = new Label { Text = "格式：物品名:数量  (例:石料:5)", Position = new Vector2(10, 28) };
        panel.AddChild(hint);
        var input = new LineEdit { Position = new Vector2(10, 54), CustomMinimumSize = new Vector2(260, 0) };
        panel.AddChild(input);
        var ok = new Button { Text = "开摆", Position = new Vector2(10, 78), CustomMinimumSize = new Vector2(80, 0) };
        panel.AddChild(ok);
        var cancel = new Button { Text = "取消", Position = new Vector2(190, 78), CustomMinimumSize = new Vector2(80, 0) };
        panel.AddChild(cancel);

        // 回车也确认
        input.TextSubmitted += (s) => OnStallConfirm(panel, owner, input.Text);
        ok.Pressed += () => OnStallConfirm(panel, owner, input.Text);
        cancel.Pressed += () => panel.QueueFree();
        input.GrabFocus();
    }

    private void OnStallConfirm(Panel panel, HeroAgent owner, string raw)
    {
        try
        {
            // 格式："物品名:数量"
            var parts = raw.Split(':');
            if (parts.Length != 2 || !long.TryParse(parts[1].Trim(), out var qty) || qty < 1)
            {
                Add(new Chronicle.Entry(
                    (int)_clock.Day, $"摆摊格式错：{raw}（要「物品名:数量」，例「石料:5」）", null));
                panel.QueueFree();
                Refresh();
                return;
            }
            var itemId = new ClaudeCodeGameStudios.Domain.Items.ItemId(parts[0].Trim());
            var simTime = _clock.Day * 24.0;
            var r = _stallService.PlaceStall(owner.Id, itemId, qty, simTime);
            if (r is null)
            {
                Add(new Chronicle.Entry(
                    (int)_clock.Day,
                    $"{owner.Name} 摆摊失败（{itemId.Value} × {qty}）—— 检查背包/价格/已开摊。", null));
            }
            else
            {
                Add(new Chronicle.Entry(
                    (int)_clock.Day,
                    $"{owner.Name} 开摊卖 {itemId.Value} × {qty}（单价 {r.Stall.UnitPrice}）。", owner.Name));
            }
        }
        finally
        {
            panel.QueueFree();
            Refresh();
        }
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

    private void Add(Chronicle.Entry e) => _chronicle.Add(e);

    /// <summary>把领域事件变成杂谈条目（基线 §7.2：杂谈根据已发生的事件生成消息）。</summary>
    /// <remarks>
    /// <para><b>只有「做完了」才进杂谈，「开始了」不进。</b></para>
    /// <para>「开始」是过程不是结果。基线 §7.3 明写「不记录系统内部状态变化」，
    /// 而 §7.2 列出的九条该记事件里也没有「某人开始了某事」——
    /// 有的是「采集」「摆摊」「社交」这些<b>做完的事</b>。</para>
    /// <para><c>HeroActionStarted</c> 事件<b>本身仍然被记录</b>（存档与状态追溯要用它），
    /// 只是不进玩家看的杂谈。这样杂谈不会每天被 12 条「开始做点事」淹掉。</para>
    /// <para><b>日常劳动（采集 / 建屋）同样只进事件日志、不进杂谈</b>（2026-10-19 开发者拍板，
    /// 见 <c>07</c> D-034）。取舍规则在 <see cref="ChroniclePolicy"/>：它们一天发生几十次，
    /// 却能独占整个聊天面板 —— 玩家要的是「村里出了什么事」，不是劳作流水账。</para>
    /// </remarks>
    private void PublishEvent(ClaudeCodeGameStudios.Domain.Events.DomainEvent ev)
    {
        // 过程性事件不进杂谈（见上文）
        if (ev.Type.Value != "HeroActionCompleted") return;

        var who = ev.Payload.TryGetValue("speaker", out var sp) ? sp : ev.Actor;
        var what = ev.Payload.TryGetValue("activity", out var act) ? act : "一桩活";

        // 这条事件来自 DailyLife 自己的 EventIdGenerator，但**必须记进本类的日志** ——
        // ChronicleFeed 会拒绝为「不在日志里的事件」生成条目（基线 §7.2 / C1）。
        // 在此之前它绕过整条管道直塞杂谈，所以 §7.4 的 C1 一直是 1。
        if (!_events.Record(ev)) return;      // I5：同一事件只记一次

        // 社交完成另有专用事件（含对象与关系变化量）。通用的「X做完了社交。」
        // 既没有对象也没有结果，进杂谈只是噪音 —— 记录事件、不产条目。
        if (what == SocialActivityId)
        {
            _feed.Publish(ev, _events, _ => null);
            return;
        }

        // 日常劳作：事件照记（存档 / 追溯 / C1 都要它），但渲染委托返回 null ——
        // 那正是 ChronicleFeed 明写的「有些事件不写入杂谈」出口。
        if (!ChroniclePolicy.IsWorthShowing(what))
        {
            _feed.Publish(ev, _events, _ => null);
            return;
        }

        string text = "";
        bool published = _feed.Publish(ev, _events, e =>
        {
            text = $"{who}做完了{what}。";
            return new ClaudeCodeGameStudios.Domain.Events.ChronicleEntry(ev.Id, text, who);
        });

        if (!published || string.IsNullOrEmpty(text)) return;
        Add(new Chronicle.Entry((int)_clock.Day, text, who));
    }

    // ---------------------------------------------------------------- 界面

    private void Refresh()
    {
        int on = _heroes.Count(h => h.Present);
        int away = _squad.Count(h => h.Away);

        _stats.Text = _buildTarget >= 0
            ? $"{TierName(_tier)}    第 {_clock.Day} 天 {_clock.Clock}    材料 {Mat}    村金 {VillageGold}    在村 {on}/{_heroes.Count}    工地：{_village.BuildName}"
            : $"{TierName(_tier)}    第 {_clock.Day} 天 {_clock.Clock}    材料 {Mat}    村金 {VillageGold}    在村 {on}/{_heroes.Count}";

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
        int cost = 30 + _tier * 25;
        if (_buildTarget >= 0)
        {
            int pct = (int)(_buildProgress * 100);
            _btnUpgrade.Text = $"正在盖 {UpgradeNames[_buildTarget - 1]}  {pct}%";
            _btnUpgrade.Disabled = true;
        }
        else
        {
            _btnUpgrade.Text = _tier >= VillageView.MaxTier ? "已是王国第一的城"
                : $"{UpgradeNames[_tier]}  ·  {cost}";
            _btnUpgrade.Disabled = _tier >= VillageView.MaxTier || Mat < cost;
        }

        _btnSummon.Disabled = false;
        _btnSummon.Text = $"招募一个人（{_heroes.Count} 人在村里）";

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
            hero => EconomySummary.For(hero, ClaudeCodeGameStudios.Application.Stall.StallService.Gold));

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

    // ---------------------------------------------------------------- 自检

    /// <summary>--selftest：不等人操作，直接跑一遍按键逻辑并打印状态。</summary>
    private void SelfTest()
    {
        GD.Print($"SELFTEST 第{_clock.Day}天 {_clock.Clock} 在村{_heroes.Count(h => h.Present)}/{_heroes.Count}");
        // 只打印当前设计下真实存在的状态。时区作息已由 07 D-001 作废，
        // 故此处不再输出 ScheduleText / PlayHours / IsOnlineAt。
        foreach (var h in _heroes)
            GD.Print($"  {h.Name} {h.Personality}/{h.Diligence} 在村={h.Present} " +
                     $"干活率={h.WorkRate:0.0000}");
        GD.Print($"  btnTask.Disabled={_btnTask.Disabled} btnUpgrade.Disabled={_btnUpgrade.Disabled}");
        var rs = ReadySquad;
        GD.Print($"  队伍 {rs.Count} 人: " + string.Join(" ", rs.Select(h => $"{h.Name}[{h.Personality}/{h.Diligence}]")));
        // 曾经还有一行「性格计数 傲=… 莽=… 贪=…」。那是 CohesionOf 的残留 ——
        // 按性格计数来推配合系数属于出厂属性替代关系发展（基线 §9.3，已作废）。
        // 就算只打印、不影响任何结果，把已判死的东西留在输出里也会让人
        // 以为它还在起作用，所以一并删掉。
    }

    private bool _eventsTestDone;
    private bool _clickTestDone;
    private bool _economyTestDone;

    /// <summary>
    /// <c>--economy</c>：开 3 英雄后，每个英雄的 <c>Wallet.金币</c> 应为 100；
    /// 村庄资源池里<b>不应有</b>「金币」一项（村金库初始是空 —— 由 M3 摆摊收费建立）。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么需要这条自检</b>：<c>Main._Ready</c> 里塞 100 钱
    /// 是个<b>纯字符串</b>行为 —— 单测能验"100 塞进去 = 100"，但
    /// <b>「钱进的是英雄而不是村」</b>这件事单测看不到：那是 <c>Main</c> 的接线选择。
    /// D-018 修过的"奖励进错桶"是同一类风险 —— 单测全绿、运行时挂错地方。
    /// 所以在真游戏里直接跑一次 <c>Main._Ready</c> 然后看实际值。</para>
    /// </remarks>
    private void RunEconomySelfTest()
    {
        _economyTestDone = true;
        var gold = new ClaudeCodeGameStudios.Domain.Village.ResourceId("金币");
        long perHeroGold = 0;
        int heroesWithGold = 0;
        foreach (var h in _heroes)
        {
            var amt = h.Wallet.Amount(gold);
            perHeroGold = amt;
            if (amt > 0) heroesWithGold++;
        }
        bool villageHasGold = _villageModel.Get(gold) > 0;

        GD.Print($"ECONTEST 英雄 {_heroes.Count} 名 · 各持金币 {perHeroGold}" +
                 $"（应为 100）· 拥有金币的英雄 {heroesWithGold} 人（应 = {_heroes.Count}）");
        GD.Print($"ECONTEST 村庄 '金币' 余额 {_villageModel.Get(gold)}（应 = 0 —— 村金库由 M3 摆摊收费建立）");

        if (perHeroGold != 100) GD.Print("ECONTEST FAIL 英雄金币不是 100 —— 接线错位");
        if (heroesWithGold != _heroes.Count) GD.Print("ECONTEST FAIL 有英雄没拿到钱");
        if (villageHasGold) GD.Print("ECONTEST FAIL 村金库初始不该有金币 —— 钱混进 Village.Resources 池了");
    }

    /// <summary>
    /// <c>--click</c>：直接调一次花名册点击，看状态是否真的变了。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么需要它</b>：单测验 `OnRosterItemActivated` 的<b>逻辑</b>（应该是
    /// "已编入队则出队，否则入队"），但<b>不能</b>验它**真的被 ItemList 触发** ——
    /// 那是引擎层面的事。</para>
    /// <para>而真玩家在 Godot 里点的效果，**单测又看不到**。
    /// 这条自检直接调 `OnRosterItemActivated(0)`，跑完看一眼 `_squad`，
    /// 截图一张——**看得到、点得到、跑得动**三件事就齐了。</para>
    /// </remarks>
    private void RunClickSelfTest()
    {
        _clickTestDone = true;
        var _ = 0; // no-op marker to keep diff readable
        int before = _squad.Count;
        var first = _heroes[0];
        GD.Print($"CLICKTEST 点前：队伍 {before} 人（{string.Join("、", _squad.Select(h => h.Name))}）");

        OnRosterItemActivated(0);                  // 模拟点第一个人

        int after = _squad.Count;
        bool inSquad = _squad.Contains(first);
        GD.Print($"CLICKTEST 点后：队伍 {after} 人（{string.Join("、", _squad.Select(h => h.Name))}）" +
                 $" 第一人 {first.Name} {(inSquad ? "已" : "未")} 在队" +
                 $"（{after - before:+0;-0;0}）");

        // 再点一次，应该出队
        OnRosterItemActivated(0);
        GD.Print($"CLICKTEST 再点：队伍 {_squad.Count} 人（{string.Join("、", _squad.Select(h => h.Name))}）");
    }

    /// <summary>
    /// <c>--events</c>：逐条验证基线 §7.4 的 C1–C4。
    /// </summary>
    /// <remarks>
    /// <b>为什么必须验</b>：这四项是基线<b>明写</b>的杂谈验收标准。
    /// 在事件管道接上之前，杂谈是「到处直接塞文本」——
    /// C1（每条都能匹配到一个事件 ID）恒为 <b>不通过</b>，
    /// 而那正是本轮要修的东西。**不能只靠代码结构声称它成立。**
    /// </remarks>
    private void RunEventsSelfTest()
    {
        _eventsTestDone = true;

        int presented = _chronicle.Count;
        int published = _feed.Entries.Count;
        int logged = _events.Count;

        GD.Print($"EVTEST 事件日志 {logged} 条 · 已发布杂谈 {published} 条 · 已呈现 {presented} 条");

        // ── C1：每条杂谈都能追溯到一个具体事件 ──
        var publishedIds = _feed.PublishedSourceIds.ToArray();
        var untraceable = _feed.Entries
            .Where(e => !publishedIds.Contains(e.SourceEventId.Value))
            .ToArray();
        GD.Print($"EVTEST C1 可追溯性：{published - untraceable.Length}/{published} 条能匹配到来源事件" +
                 $"（无法匹配 {untraceable.Length} 条，必须为 0）");

        // ── C2：同一事件不重复记两条 ──
        var counts = _feed.Entries
            .GroupBy(e => e.SourceEventId.Value)
            .Select(g => new { Id = g.Key, N = g.Count() })
            .ToArray();
        int overOnce = counts.Count(c => c.N > 1);
        GD.Print($"EVTEST C2 去重：{published} 条来自 {counts.Length} 个不同事件" +
                 $"（超过 1 条的事件 {overOnce} 个，必须为 0）");

        // ── C3：事件发生时杂谈有反应 ──
        int beforeEvents = _events.Count, beforeEntries = _feed.Entries.Count;
        var crew = ReadySquad;
        if (crew.Count > 0)
        {
            var memberStates = crew.Select(h => _states[_heroes.IndexOf(h)]).ToArray();
            try
            {
                _mission = Mission.Create(
                    new MissionId($"etest{_clock.AbsoluteHour:0}"), "北境旧堡探查",
                    new Squad(_squadRules, memberStates), _squadRules);
                _mission.Start(IsOnMission);
                foreach (var h in crew) h.Away = true;
                ResolveDungeon(new Job(DungeonName, "铁料", 38, 11f, true), crew);
                _mission = null;
            }
            catch (Exception ex)
            {
                GD.Print($"EVTEST C3 触发失败：{ex.GetType().Name} {ex.Message}");
            }
        }
        GD.Print($"EVTEST C3 触发一次副本：新增事件 {_events.Count - beforeEvents} 个、" +
                 $"新增杂谈 {_feed.Entries.Count - beforeEntries} 条");

        // ── C4：没有事件就不产生条目 —— 杂谈里不该有空条目 ──
        var empties = _feed.Entries.Where(e => string.IsNullOrWhiteSpace(e.Text)).ToArray();
        GD.Print($"EVTEST C4 空条目 {empties.Length} 条（必须为 0）");

        // 逐条列出，让人能直接核对
        foreach (var e in _feed.Entries.TakeLast(8))
        {
            GD.Print($"EVTEST   [{e.SourceEventId}] {e.Text}");
        }
    }

    private bool _dungeonTestDone;
    private bool _tradeTestDone;
    private bool _marketTestDone;

    /// <summary>
    /// <c>--trade</c>：第四阶段 3A 真机自检 —— 发物品 → 开摊 → 手动购买 → 失败场景。
    /// </summary>
    /// <remarks>
    /// <para>覆盖：成功购买（双方货币/背包/摊位库存/村库费用）、重复请求幂等、
    /// 库存不足、余额不足；每个失败场景都检查状态不变（无部分结算）。</para>
    /// <para>走的是生产代码路径：<see cref="ItemGrantService.Grant"/>、
    /// <c>StallService.PlaceStall</c>、<c>StallService.TryBuy</c>，不是复制品。</para>
    /// </remarks>
    private void RunTradeSelfTest()
    {
        _tradeTestDone = true;

        if (_states.Count < 2)
        {
            GD.Print("TRADETEST FAIL 英雄不足 2 人，无法验证买卖。");
            return;
        }

        var seller = _heroes[0];
        var buyer = _heroes[1];
        var item = new ClaudeCodeGameStudios.Domain.Items.ItemId("石料");
        var gold = ClaudeCodeGameStudios.Application.Stall.StallService.Gold;

        long sellerGold0 = seller.Wallet.Amount(gold);
        long buyerGold0 = buyer.Wallet.Amount(gold);
        long villageGold0 = _villageModel.Get(gold);

        // 1. 测试配给 + 开摊
        var grant = _itemGrant.Grant(seller.Id, item, 5);
        var placed = _stallService.PlaceStall(seller.Id, item, 5, _clock.Now);
        GD.Print($"TRADETEST 配给={grant.Result}，开摊={(placed is null ? "FAIL" : "OK")}");
        if (!grant.Granted || placed is null)
        {
            GD.Print("TRADETEST FAIL 无法准备交易场景");
            return;
        }

        // 2. 成功购买 2 份
        var buy = _stallService.TryBuy(seller.Id, buyer.Id, item, 2, _clock.Now, "traderun-1");
        long stockAfterBuy = _stallService.GetStall(seller.Id)?.Stock ?? -1;
        long sellerGold1 = seller.Wallet.Amount(gold);
        long buyerGold1 = buyer.Wallet.Amount(gold);
        long villageGold1 = _villageModel.Get(gold);
        bool buyOk = buy.Bought
                     && buyerGold0 - buyerGold1 == 10
                     && sellerGold1 - sellerGold0 == 10   // 10×5% floor = 0，全归卖家
                     && buyer.Bag.Count(item) == 2
                     && stockAfterBuy == 3
                     && villageGold1 == villageGold0;     // 村费为 0
        GD.Print($"TRADETEST 购买={buy.Outcome}，卖家金 {sellerGold0}→{sellerGold1}，" +
                 $"买家金 {buyerGold0}→{buyerGold1}，买家背包={buyer.Bag.Count(item)}，" +
                 $"摊位库存={stockAfterBuy}，村金 {villageGold0}→{villageGold1} → {buyOk}");

        // 3. 重复同一请求键：幂等，状态不变
        var dup = _stallService.TryBuy(seller.Id, buyer.Id, item, 2, _clock.Now, "traderun-1");
        bool dupOk = dup.Outcome == ClaudeCodeGameStudios.Application.Stall.StallService.BuyOutcome.DuplicateRequest
                     && seller.Wallet.Amount(gold) == sellerGold1
                     && buyer.Wallet.Amount(gold) == buyerGold1
                     && buyer.Bag.Count(item) == 2
                     && (_stallService.GetStall(seller.Id)?.Stock ?? -1) == 3;
        GD.Print($"TRADETEST 重复请求={dup.Outcome}，状态不变={dupOk}");

        // 4. 库存不足：状态不变
        var shortStock = _stallService.TryBuy(seller.Id, buyer.Id, item, 999, _clock.Now, "traderun-2");
        bool stockOk = shortStock.Outcome ==
                       ClaudeCodeGameStudios.Application.Stall.StallService.BuyOutcome.InsufficientStock
                       && (_stallService.GetStall(seller.Id)?.Stock ?? -1) == 3
                       && buyer.Wallet.Amount(gold) == buyerGold1;

        // 5. 余额不足：把买家金币压到 5，再买 2 份（需 10）→ 拒绝且状态不变
        buyer.TrySpendResource(gold, buyerGold1 - 5);
        var poor = _stallService.TryBuy(seller.Id, buyer.Id, item, 2, _clock.Now, "traderun-3");
        bool poorOk = poor.Outcome ==
                      ClaudeCodeGameStudios.Application.Stall.StallService.BuyOutcome.CannotAfford
                      && buyer.Wallet.Amount(gold) == 5
                      && (_stallService.GetStall(seller.Id)?.Stock ?? -1) == 3
                      && buyer.Bag.Count(item) == 2;

        GD.Print($"TRADETEST 库存不足={shortStock.Outcome}/{stockOk}，余额不足={poor.Outcome}/{poorOk}");
        GD.Print(buyOk && dupOk && stockOk && poorOk ? "TRADETEST PASS" : "TRADETEST FAIL");
    }

    /// <summary>
    /// <c>--market</c>：R-02 真机自检 —— 摊位面板背后的那条路。
    /// </summary>
    /// <remarks>
    /// <para><b>验的是"面板点下去会发生什么"</b>：候选买家怎么算、
    /// 「尽可能多」买多少、成交后双方的钱和货对不对、失败时状态动不动。
    /// 走的是生产代码路径（<see cref="ClaudeCodeGameStudios.Application.Stall.StallMarketService"/>
    /// → <see cref="ClaudeCodeGameStudios.Application.Stall.StallService.TryBuy"/>），不是复制品。</para>
    ///
    /// <para><b>为什么不用鼠标点</b>：自检跑在 headless 里，没有指针。
    /// 面板的「点摊位 → 点买家」两跳，等价于「取摊位 → 取候选 → 买」——
    /// 面板只是把玩家的点击翻译成这三步。验这三步，就验了面板的语义。</para>
    /// </remarks>
    private void RunMarketSelfTest()
    {
        _marketTestDone = true;

        if (_states.Count < 2)
        {
            GD.Print("MARKETTEST FAIL 英雄不足 2 人，无法验证买卖。");
            return;
        }

        var seller = _heroes[0];
        var buyer = _heroes[1];
        var item = new ClaudeCodeGameStudios.Domain.Items.ItemId("石料");
        var gold = ClaudeCodeGameStudios.Application.Stall.StallService.Gold;

        long sellerGold0 = seller.Wallet.Amount(gold);
        long buyerGold0 = buyer.Wallet.Amount(gold);
        long villageGold0 = _villageModel.Get(gold);

        // 1. 配给 + 开摊：摊主有 5 份石料，摊上摆 5 份。
        var grant = _itemGrant.Grant(seller.Id, item, 5);
        var placed = _stallService.PlaceStall(seller.Id, item, 5, _clock.Now);
        if (!grant.Granted || placed is null)
        {
            GD.Print("MARKETTEST FAIL 无法准备交易场景");
            return;
        }

        var stall = _stallService.GetStall(seller.Id);
        if (stall is null)
        {
            GD.Print("MARKETTEST FAIL 摊位丢失");
            return;
        }

        // 2. 候选买家：摊主本人不在列表里（在列表里就是"点了必然失败"）。
        var offers = _market.Offers(stall, _heroes);
        bool sellerNotCandidate = !offers.Any(o => o.Id == seller.Id);
        bool buyerIsCandidate = offers.Any(o => o.Id == buyer.Id);
        var buyerOffer = offers.FirstOrDefault(o => o.Id == buyer.Id);
        GD.Print($"MARKETTEST 摊位={ClaudeCodeGameStudios.Presentation.StallMarketText.StallLine(seller.Name, stall)}");
        GD.Print($"MARKETTEST 候选 {offers.Count} 人：摊主不在={sellerNotCandidate}，买家在={buyerIsCandidate}，" +
                 $"买家可买={buyerOffer.MaxQty} 份");

        // 3. 走面板：打开 → 点摊位 → 点买家 → 成交。
        //    这一段刻意**经过面板**，而不是直接调服务 ——
        //    「玩家点下去会发生什么」正是本轮要验的东西。
        //    面板内部最终调的就是 _market.BuyAsMuchAsPossible（唯一结算入口 TryBuy）。
        _stallPanel.Open();
        bool pickedStall = _stallPanel.SelectStall(0);
        int buyerIndex = offers.ToList().FindIndex(o => o.Id == buyer.Id);
        int buyerRows = _stallPanel.BuyerRowCount;
        bool pickedBuyer = buyerIndex >= 0 && _stallPanel.SelectBuyer(buyerIndex);
        GD.Print($"MARKETTEST 面板：选中摊位={pickedStall}，候选行数={buyerRows}，" +
                 $"选中买家={pickedBuyer}，回执=「{_stallPanel.ResultText}」");

        long stockAfter = _stallService.GetStall(seller.Id)?.Stock ?? -1;
        long sellerGold1 = seller.Wallet.Amount(gold);
        long buyerGold1 = buyer.Wallet.Amount(gold);
        long villageGold1 = _villageModel.Get(gold);

        // 5 份 × 5 金 = 25：村抽 floor(25×5/100) = 1，卖家净得 24。
        bool buyOk = pickedStall && pickedBuyer && buyerRows >= 1
                     && buyerGold0 - buyerGold1 == 25
                     && sellerGold1 - sellerGold0 == 24
                     && villageGold1 - villageGold0 == 1
                     && buyer.Bag.Count(item) == 5
                     && stockAfter == 0;
        GD.Print($"MARKETTEST 购买后：买家金 {buyerGold0}→{buyerGold1}，" +
                 $"摊主金 {sellerGold0}→{sellerGold1}，村金 {villageGold0}→{villageGold1}，" +
                 $"买家背包={buyer.Bag.Count(item)}，摊位库存={stockAfter} → {buyOk}");

        // 4. 售罄后再买：摊位不再是 Open → 拒绝，且状态不变。
        var afterSoldOut = _market.BuyAsMuchAsPossible(stall, buyer.Id, _clock.Now);
        bool soldOutOk = !afterSoldOut.Bought
                         && buyer.Wallet.Amount(gold) == buyerGold1
                         && buyer.Bag.Count(item) == 5;
        GD.Print($"MARKETTEST 售罄后再买={afterSoldOut.Outcome}，状态不变={soldOutOk}");

        // 5. 面板文案：R-04 的库存/单价确实上界面了。
        string line = ClaudeCodeGameStudios.Presentation.StallMarketText.StallLine(seller.Name, stall);
        bool lineOk = line.Contains("石料") && line.Contains("单价");
        GD.Print($"MARKETTEST 摊位行=「{line}」含物品与单价={lineOk}");

        GD.Print(sellerNotCandidate && buyerIsCandidate && buyOk && soldOutOk && lineOk
            ? "MARKETTEST PASS"
            : "MARKETTEST FAIL");
    }

    private bool _shopTestDone;

    /// <summary>
    /// <c>--econlog</c>：每次每日购物跑完后，把<b>逐人</b>结果（成交 / 跳过 / 失败及原因）
    /// 打到控制台。第四阶段 M5 的「自主行为结果可调试验证」。
    /// </summary>
    private readonly bool _econLog = OS.GetCmdlineUserArgs().Contains("--econlog");

    /// <summary>最近一次每日购物的逐人结果（<c>--shop</c> 明细输出用）。</summary>
    private IReadOnlyList<ClaudeCodeGameStudios.Application.Stall.HeroBehaviorService.ShoppingResult>
        _lastShoppingResults = Array.Empty<ClaudeCodeGameStudios.Application.Stall.HeroBehaviorService.ShoppingResult>();

    /// <summary>最近一次每日购物对应的游戏日。</summary>
    private int _lastShoppingDay;

    /// <summary>每日自主购物：按游戏日触发一次（服务内部有同日闸门）。</summary>
    private void RunDailyShoppingIfNeeded()
    {
        int today = (int)_clock.Day;
        if (_dailyShopping.LastRunDay == today) return;

        var ids = new List<HeroId>(_states.Count);
        foreach (var state in _states) ids.Add(state.Id);

        var results = _dailyShopping.RunDay(today, ids, _clock.Now);
        _lastShoppingResults = results;
        _lastShoppingDay = today;

        // 先输出可观测结果，再发事件 —— 输出失败原因不需要事件（反装饰规则）。
        if (_econLog) PrintShoppingResults("ECONLOG", today, results);

        PublishShoppingResults(results);
    }

    /// <summary>把「成交」结果转成领域事件；跳过/失败一律不发事件（反装饰规则）。</summary>
    private void PublishShoppingResults(
        IReadOnlyList<ClaudeCodeGameStudios.Application.Stall.HeroBehaviorService.ShoppingResult> results)
    {
        foreach (var result in results)
        {
            if (result.Outcome !=
                ClaudeCodeGameStudios.Application.Stall.HeroBehaviorService.ShoppingOutcome.Bought)
            {
                continue;
            }

            EmitTradeCompleted(result);
        }
    }

    /// <summary>
    /// 把一次每日购物的逐人结果打到控制台。<b>只给调试看，不进杂谈</b> ——
    /// 37 个人每天各一条「今天没买」会立刻淹掉村庄杂谈。
    /// </summary>
    /// <param name="tag">日志前缀（<c>ECONLOG</c> / <c>SHOPTEST</c>）。</param>
    /// <param name="day">这批结果对应的游戏日。</param>
    /// <param name="results">逐人结果（顺序与英雄顺序一致）。</param>
    private void PrintShoppingResults(
        string tag, int day,
        IReadOnlyList<ClaudeCodeGameStudios.Application.Stall.HeroBehaviorService.ShoppingResult> results)
    {
        int bought = 0;
        foreach (var result in results)
        {
            if (result.Outcome ==
                ClaudeCodeGameStudios.Application.Stall.HeroBehaviorService.ShoppingOutcome.Bought)
            {
                bought++;
            }
        }

        GD.Print($"{tag} 第{day}天 尝试{results.Count}人，成交{bought}人");
        foreach (var result in results)
        {
            string who = result.Buyer is { } id ? NameOfHero(id) : "?";
            GD.Print($"  {who}: {ClaudeCodeGameStudios.Application.Stall.ShoppingFeedback.Describe(result)}");
        }
    }

    /// <summary>一条真实成交 → 一个可追溯事件。</summary>
    private void EmitTradeCompleted(
        ClaudeCodeGameStudios.Application.Stall.HeroBehaviorService.ShoppingResult result)
    {
        if (result.Buyer is null || result.Seller is null || result.ItemId is null) return;

        var buyer = result.Buyer.Value;
        var seller = result.Seller.Value;
        var item = result.ItemId.Value.Value;   // ItemId? → ItemId → string
        string buyerName = NameOfHero(buyer);
        string sellerName = NameOfHero(seller);
        long qty = result.Quantity ?? 0;
        long villageCut = result.VillageCut ?? 0;

        var changed = new List<ClaudeCodeGameStudios.Domain.Events.StateKey>
        {
            StHeroWallet,
            StHeroBag,
        };
        if (villageCut > 0) changed.Add(StVillageResource);

        var payload = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["buyer"] = buyerName,
            ["seller"] = sellerName,
            ["item"] = item,
            ["qty"] = qty.ToString(),
            ["sellerNet"] = (result.SellerNet ?? 0).ToString(),
            ["amount"] = ((result.SellerNet ?? 0) + villageCut).ToString(),
            ["villageCut"] = villageCut.ToString(),
        };

        // M5：成交条目要能核对钱去哪了 —— 付多少、摊主得多少、村抽多少。
        // 文本规则住在 TradeFeedback 里（纯函数、可逐字测），这里只提供已发生的事实。
        Emit(EvTradeCompleted, buyerName, changed, payload,
            ev => TradeFeedback.Line(
                buyerName, sellerName, item, qty,
                (result.SellerNet ?? 0) + villageCut, result.SellerNet ?? 0, villageCut));
    }

    /// <summary>
    /// 玩家<b>手动</b>成交 → 一个可追溯事件（R-02）。
    /// </summary>
    /// <param name="result">买摊结果。</param>
    /// <remarks>
    /// <para><b>与自主购物走同一条事件</b>：两者改的是同一样东西（钱、货、摊位库存），
    /// 就不该有两种记法 —— 玩家在杂谈里看到「谁买了什么」时，
    /// 不该还得分辨这一笔是英雄自己买的还是他点的。</para>
    ///
    /// <para><b>份数直接取结果里的 <c>Quantity</c></b>，不拿金额除单价 ——
    /// 单价可以是 0（白送摊），除法会炸。</para>
    /// </remarks>
    private void EmitTradeCompleted(
        ClaudeCodeGameStudios.Application.Stall.StallService.BuyStallResult result)
    {
        string buyerName = NameOfHero(result.Buyer);
        string sellerName = NameOfHero(result.Seller);
        string item = result.NewStall.ItemId.Value;
        long qty = result.Quantity;

        var changed = new List<ClaudeCodeGameStudios.Domain.Events.StateKey>
        {
            StHeroWallet,
            StHeroBag,
        };
        if (result.VillageCut > 0) changed.Add(StVillageResource);

        var payload = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["buyer"] = buyerName,
            ["seller"] = sellerName,
            ["item"] = item,
            ["qty"] = qty.ToString(),
            ["sellerNet"] = result.SellerNet.ToString(),
            ["amount"] = result.TotalAmount.ToString(),
            ["villageCut"] = result.VillageCut.ToString(),
        };

        Emit(EvTradeCompleted, buyerName, changed, payload,
            ev => TradeFeedback.Line(
                buyerName, sellerName, item, qty,
                result.TotalAmount, result.SellerNet, result.VillageCut));
    }

    /// <summary>按 HeroId 找显示名；找不到时回落为 Id。</summary>
    private string NameOfHero(HeroId id)
    {
        foreach (var hero in _heroes)
        {
            if (hero.Id == id) return hero.Name;
        }

        return id.Value;
    }

    /// <summary>
    /// <c>--shop</c>：第四阶段 3B 真机自检 —— 有物品/有开放摊位时自主购物成交，同日不重复。
    /// </summary>
    /// <remarks>
    /// <para>走生产路径：<see cref="ItemGrantService.Grant"/> → <c>PlaceStall</c> →
    /// 逐日 <see cref="RunDailyShoppingIfNeeded"/>（跨天推进）→ 事件管线。</para>
    /// <para>最多推进 30 个游戏日等待成交（随机性格/投骰；30 天内未成交视为失败）；
    /// 成交后同日再调用一次，验证闸门不重复执行。</para>
    /// </remarks>
    private void RunShoppingSelfTest()
    {
        _shopTestDone = true;

        if (_states.Count < 2)
        {
            GD.Print("SHOPTEST FAIL 英雄不足 2 人");
            return;
        }

        var seller = _heroes[0];
        var buyer = _heroes[1];
        var item = new ClaudeCodeGameStudios.Domain.Items.ItemId("石料");
        var gold = ClaudeCodeGameStudios.Application.Stall.StallService.Gold;

        _itemGrant.Grant(seller.Id, item, 100);
        if (_stallService.GetStall(seller.Id) is null)
        {
            var placed = _stallService.PlaceStall(seller.Id, item, 100, _clock.Now);
            if (placed is null)
            {
                GD.Print("SHOPTEST FAIL 开摊失败（英雄可能不在场/在忙）");
                return;
            }
        }

        long buyerGold0 = buyer.Wallet.Amount(gold);
        long stock0 = _stallService.GetStall(seller.Id)?.Stock ?? -1;
        int events0 = _events.Count;
        int startDay = (int)_clock.Day;
        bool bought = false;
        int days = 0;

        while (!bought && days < 30)
        {
            _clock.SetSimTime(_clock.Now + _clock.SimSecondsPerDay);
            RunDailyShoppingIfNeeded();
            if (buyer.LastShoppingDay == (int)_clock.Day) bought = true;
            days++;
        }

        long stock1 = _stallService.GetStall(seller.Id)?.Stock ?? -1;
        long buyerGold1 = buyer.Wallet.Amount(gold);
        int events1 = _events.Count;

        // 同日再调一次：闸门应直接返回，不产生新事件/状态变化
        long goldCheck = buyerGold1;
        long stockCheck = stock1;
        int eventsCheck = events1;
        RunDailyShoppingIfNeeded();
        bool noRepeat = events1 == _events.Count
                        && buyer.Wallet.Amount(gold) == goldCheck
                        && (_stallService.GetStall(seller.Id)?.Stock ?? -1) == stockCheck;

        // M5：把成交当天的逐人结果打出来 —— 谁买了、谁跳过了、为什么。
        if (_lastShoppingResults.Count > 0)
        {
            PrintShoppingResults("SHOPTEST 明细", _lastShoppingDay, _lastShoppingResults);
        }

        bool stateOk = bought && buyerGold1 < buyerGold0 && stock1 < stock0 && events1 > events0;
        GD.Print($"SHOPTEST 第{startDay}→{_clock.Day}天/尝试{days}天：成交={bought}，" +
                 $"买家金 {buyerGold0}→{buyerGold1}，摊位库存 {stock0}→{stock1}，" +
                 $"事件 {events0}→{events1}");
        GD.Print($"SHOPTEST 同日重复调用无新增={noRepeat}");
        GD.Print(stateOk && noRepeat ? "SHOPTEST PASS" : "SHOPTEST FAIL");
    }

    private bool _grantTestDone;

    /// <summary>
    /// <c>--grant=英雄Id:物品名:数量</c>：第四阶段 M1 的测试配给入口。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么是调试入口</b>：正式物品来源（副本战利品）的奖励归属规则尚未确认；
    /// 先用受控入口验证「背包 → 摆摊 → 购买」链路。它<b>不</b>接入正常游戏流程。</para>
    /// <para>自检同时验证非法发放（未知物品、数量 0）不会改变背包。</para>
    /// </remarks>
    private void RunGrantSelfTest()
    {
        _grantTestDone = true;

        string? arg = OS.GetCmdlineUserArgs()
            .FirstOrDefault(a => a.StartsWith("--grant=", StringComparison.Ordinal));
        if (arg is null) return;

        string spec = arg.Substring("--grant=".Length);
        var parts = spec.Split(':');
        if (parts.Length != 3 || !long.TryParse(parts[2], out long qty))
        {
            GD.Print($"GRANT FAIL 格式应为 --grant=英雄Id:物品名:数量（收到「{spec}」）");
            return;
        }

        var heroId = new ClaudeCodeGameStudios.Domain.Heroes.HeroId(parts[0].Trim());
        var item = new ClaudeCodeGameStudios.Domain.Items.ItemId(parts[1].Trim());
        HeroAgent? hero = _heroes.FirstOrDefault(h => h.Id == heroId);
        if (hero is null)
        {
            GD.Print($"GRANT FAIL 找不到英雄 {heroId.Value}");
            return;
        }

        long before = hero.Bag.Count(item);
        var result = _itemGrant.Grant(heroId, item, qty);
        GD.Print($"GRANT {hero.Name} {item.Value} +{qty} → {result.Result}" +
                 $"（背包 {before} → {hero.Bag.Count(item)}）");

        long afterValid = hero.Bag.Count(item);
        var badItem = _itemGrant.Grant(heroId, new ClaudeCodeGameStudios.Domain.Items.ItemId("不存在的东西"), 1);
        var badQty = _itemGrant.Grant(heroId, item, 0);
        bool unchanged = hero.Bag.Count(item) == afterValid;
        GD.Print($"GRANT 非法用例：未知物品={badItem.Result}，数量0={badQty.Result}，背包不变={unchanged}");
        GD.Print("GRANT 背包快照 = {" + string.Join("、",
            hero.Bag.Snapshot().Select(kv => $"{kv.Key.Value}×{kv.Value}")) + "}");

        bool pass = result.Granted && result.NewCount == before + qty && unchanged;
        GD.Print(pass ? "GRANT PASS" : "GRANT FAIL");
    }

    private bool _relationsTestDone;

    /// <summary>
    /// <c>--relations</c>：第三阶段真机自检 —— 社交写关系、幂等、存读档往返。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么必须运行时验</b>：单测验的是 <c>RelationshipBook</c> /
    /// <c>RelationshipService</c> / <c>SaveValidator</c>，验不了
    /// 「<c>Main</c> 有没有真的把关系装进存档、读档有没有真的恢复」——
    /// 这正是第二阶段「端口全绿但游戏存了个空壳」的同一类风险。</para>
    /// <para><b>它跑的是生产代码路径</b>：<see cref="RelationshipService.Socialize"/>、
    /// <see cref="BuildSave"/>、<see cref="ApplySave"/>，不是复制品。</para>
    /// </remarks>
    private void RunRelationsSelfTest()
    {
        _relationsTestDone = true;

        if (_states.Count < 2)
        {
            GD.Print("RELTEST FAIL 英雄不足 2 人，无法验证社交关系。");
            return;
        }

        var a = _states[0].Id;
        var b = _states[1].Id;
        GD.Print($"RELTEST 初始：{a.Value}→{b.Value} = {_relationship.ValueOf(a, b)}，" +
                 $"{b.Value}→{a.Value} = {_relationship.ValueOf(b, a)}");

        // ── 1. 一次社交：双向各 +2 ──
        var touched = _relationships.Socialize(a, b, _clock.Now);
        GD.Print($"RELTEST 一次社交：forward changed={touched.Forward.Changed} " +
                 $"{touched.Forward.Before.Value}→{touched.Forward.After.Value}；" +
                 $"backward changed={touched.Backward.Changed} " +
                 $"{touched.Backward.Before.Value}→{touched.Backward.After.Value}");

        // ── 2. 同一业务键重复处理：必须不生效 ──
        var duplicate = _relationships.Socialize(a, b, _clock.Now);
        GD.Print($"RELTEST 重复同一业务键：changed={duplicate.Changed}（必须 false），" +
                 $"当前 {a.Value}→{b.Value} = {_relationship.ValueOf(a, b)}（必须仍是 2）");

        GD.Print($"RELTEST 关系条数 = {_relationship.Count}，幂等键 {_relationship.AppliedKeys.Count} 个，" +
                 $"杂谈 {_chronicle.Count} 条");

        // ── 2.5 失败路径：目标随队离开 → 社交失败，关系不变、不发事件 ──
        int beforeFailed = _relationship.ValueOf(a, b);
        var targetState = _states[1];
        targetState.SetOnMission();
        OnSocialCompleted(a.Value, b);
        int afterFailed = _relationship.ValueOf(a, b);
        targetState.SetInVillage();
        GD.Print($"RELTEST 目标随队时社交：关系 {beforeFailed}→{afterFailed}" +
                 $"（必须不变），{(beforeFailed == afterFailed ? "PASS" : "FAIL")}");

        // ── 2.6 同队副本合作：每对双向 +3；同一 runId 重复处理不生效 ──
        var coop = _relationships.Cooperate("reltest-run", new[] { a, b });
        var coopAgain = _relationships.Cooperate("reltest-run", new[] { a, b });
        GD.Print($"RELTEST 副本合作：变化对 {coop.PairsChanged}，重复同一 runId 后变化对 {coopAgain.PairsChanged}" +
                 $"（必须 0），当前 {a.Value}→{b.Value} = {_relationship.ValueOf(a, b)}");

        // ── 3. 存档：关系与幂等键必须真的装进去 ──
        var save = BuildSave();
        GD.Print($"RELTEST BuildSave：版本 {save.Version}，" +
                 $"关系 {save.Relationships.Count} 条，幂等键 {save.AppliedRelationKeys.Count} 个");

        try
        {
            _saves.Write(save);
        }
        catch (Exception ex)
        {
            GD.Print($"RELTEST FAIL 写盘失败：{ex.GetType().Name} {ex.Message}");
            return;
        }

        int savedForward = _relationship.ValueOf(a, b);
        int savedBackward = _relationship.ValueOf(b, a);

        // ── 4. 扰动：再换一个业务键加一次关系 ──
        _relationships.Socialize(a, b, _clock.Now + 1000.0);
        GD.Print($"RELTEST 存盘后扰动：{a.Value}→{b.Value} = {_relationship.ValueOf(a, b)}" +
                 $"（应大于存盘时的 {savedForward}）");

        // ── 5. 读档：关系必须回到存盘那一刻 ──
        var loaded = _saves.Read();
        if (!loaded.Loaded)
        {
            GD.Print($"RELTEST FAIL 读档失败：{loaded.Describe()}");
            return;
        }

        ApplySave(loaded.Save);

        var restoredA = new HeroId(loaded.Save.Heroes[0].Id);
        var restoredB = new HeroId(loaded.Save.Heroes[1].Id);
        int restoredForward = _relationship.ValueOf(restoredA, restoredB);
        int restoredBackward = _relationship.ValueOf(restoredB, restoredA);

        GD.Print($"RELTEST 读档后：{restoredA.Value}→{restoredB.Value} = {restoredForward}" +
                 $"（存盘时 {savedForward}），{restoredB.Value}→{restoredA.Value} = {restoredBackward}" +
                 $"（存盘时 {savedBackward}），幂等键 {_relationship.AppliedKeys.Count} 个");

        bool pass = restoredForward == savedForward
                    && restoredBackward == savedBackward
                    && _relationship.AppliedKeys.Count == save.AppliedRelationKeys.Count;
        GD.Print(pass ? "RELTEST PASS 关系存档往返一致" : "RELTEST FAIL 关系存档往返不一致");
    }

    /// <summary>
    /// <c>--dungeon</c>：真的派一次副本远征并把结果打出来。
    /// </summary>
    /// <remarks>
    /// <para><b>它补的是哪一截</b>：单元测试证明
    /// <c>DungeonFlow</c> 是对的，但证明不了<b>「游戏真的调用了它」</b>。
    /// 而远征要跑 11 个游戏内小时，截图只在第 30 帧拍 ——
    /// 所以<b>没有任何一张截图能证明副本跑过</b>。这个开关补的正是这一截。</para>
    /// <para><b>它调用的是 <see cref="ResolveDungeon"/> 本身</b>，不是一份复制的流程。
    /// 走复制品验证等于验证了一段没人会执行的代码。</para>
    /// </remarks>
    // ── 事件溯源（基线 §7.2 / §7.4）──────────────────────────────────────
    //
    // 杂谈**必须**从已发生的事件长出来。`ChronicleFeed.Publish` 会拒绝
    // 「不在日志里的事件」，所以这条管道不是风格偏好，是硬约束：
    // 任何一条杂谈都拿不出事件 ID，就进不来。

    private ClaudeCodeGameStudios.Domain.Events.EventLog _events = new();
    private ClaudeCodeGameStudios.Domain.Events.ChronicleFeed _feed = new();
    private ClaudeCodeGameStudios.Domain.Events.EventIdGenerator _eventIds = new();

    // ── 事件类型（对应基线 §7.2 的名单）──
    private static readonly ClaudeCodeGameStudios.Domain.Events.EventTypeId EvSummoned =
        new("HeroSummoned");                 // §7.2「召唤到新英雄」
    private static readonly ClaudeCodeGameStudios.Domain.Events.EventTypeId EvDispatched =
        new("SquadDispatched");               // §7.2「大事件的发生」
    private static readonly ClaudeCodeGameStudios.Domain.Events.EventTypeId EvDailyWork =
        new("VillageDailyWork");              // §7.2「村庄日常（采集、摆摊、社交等）」
    private static readonly ClaudeCodeGameStudios.Domain.Events.EventTypeId EvDungeonFought =
        new("DungeonEncounterFought");        // §7.2「大事件的发生」
    private static readonly ClaudeCodeGameStudios.Domain.Events.EventTypeId EvDungeonSettled =
        new("DungeonSettled");                // §7.2「大事件的结算」
    private static readonly ClaudeCodeGameStudios.Domain.Events.EventTypeId EvBuildingDone =
        new("BuildingCompleted");             // §7.2「建筑建成 / 升级」
    private static readonly ClaudeCodeGameStudios.Domain.Events.EventTypeId EvSocialized =
        new("HeroSocialized");                 // 第三阶段 M5：英雄之间的社交
    private static readonly ClaudeCodeGameStudios.Domain.Events.EventTypeId EvSquadBonded =
        new("SquadBonded");                    // 第三阶段 M5：同队副本合作（一条聚合）
    private static readonly ClaudeCodeGameStudios.Domain.Events.EventTypeId EvTradeCompleted =
        new("TradeCompleted");                 // 第四阶段 3B：英雄自主购物成交

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
    private static readonly ClaudeCodeGameStudios.Domain.Events.StateKey StHeroWallet =
        new("hero.wallet");                    // 第四阶段 3B：英雄货币变化
    private static readonly ClaudeCodeGameStudios.Domain.Events.StateKey StHeroBag =
        new("hero.bag");                       // 第四阶段 3B：英雄背包变化

    /// <summary>已记录的事件数（自检用）。</summary>
    private int EventCount => _events.Count;

    /// <summary>
    /// <b>杂谈的唯一出口</b>：事件 → 日志 → 条目 → 呈现。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么必须走这一条路</b>（基线 §7.4 <b>C1</b>）：
    /// 「断言每条消息都能匹配到一个已发生的事件 ID；无法匹配的条目数为 0」。
    /// 直接 <c>Add(...)</c> 塞进去的条目<b>拿不出事件 ID</b>，
    /// C1 立刻为 1 —— 而这条正是基线给杂谈定的第一验收项。</para>
    /// <para><b>C2「同一事件不重复记两条」</b>由 <c>ChronicleFeed</c> 按事件 Id 去重；
    /// <b>C3「事件发生时杂谈有反应」</b>由本方法保证；
    /// <b>C4「不刷屏」</b>则靠「没有事件就不调本方法」——
    /// 杂谈里<b>不会出现空条目</b>，因为条目只在事件成功渲染时才产生。</para>
    /// </remarks>
    /// <param name="type">事件类型（对应 §7.2 的一类）。</param>
    /// <param name="actor">参与者（英雄名 / 队伍 / 副本名）。</param>
    /// <param name="changed">这个事件改变了哪些状态。<b>不能为空。</b></param>
    /// <param name="payload">只读载荷，供渲染取用。</param>
    /// <param name="render">把事件变成一句话。返回 null 表示不产生条目。</param>
    /// <returns>是否真的产生了一条杂谈。</returns>
    private bool Emit(
        ClaudeCodeGameStudios.Domain.Events.EventTypeId type,
        string actor,
        IEnumerable<ClaudeCodeGameStudios.Domain.Events.StateKey> changed,
        IReadOnlyDictionary<string, string>? payload,
        Func<ClaudeCodeGameStudios.Domain.Events.DomainEvent, string?> render)
    {
        // 构造期就会因为「声明不出状态变化」而抛 —— 反装饰规则在这里生效。
        var e = ClaudeCodeGameStudios.Domain.Events.DomainEvent.Create(
            _eventIds.Next(type, actor), type, actor, _clock.Now, changed, payload);

        if (!_events.Record(e)) return false;   // I5：同一事件只记一次

        string text = "";
        bool published = _feed.Publish(e, _events, ev =>
        {
            text = render(ev);
            return text is null ? null : new ClaudeCodeGameStudios.Domain.Events.ChronicleEntry(e.Id, text, ev.Actor);
        });

        if (!published || string.IsNullOrEmpty(text)) return false;

        Add(new Chronicle.Entry((int)_clock.Day, text, e.Actor));
        return true;
    }

    // ── 存档 ──────────────────────────────────────────────────────────────

    /// <summary>
    /// 存档端口。路径由构造参数给出，不写死 <c>user://</c> —— 自检要写到别处。
    /// <b>Phase 1 自检写 bug</b>：<c>user://</c> 路径在 SavePort 里被当成本地路径用，
    /// Windows 下解析成 <c>D:\...\user:</c>（带冒号）写盘失败。
    /// <b>修复</b>：_Ready 末尾用 <c>ProjectSettings.GlobalizePath</c> 解析。
    /// </summary>
    private ClaudeCodeGameStudios.Data.SavePort _saves = null!;

    /// <summary>
    /// 把运行中的状态装进一份 <see cref="ClaudeCodeGameStudios.Data.SaveGame"/>。
    /// </summary>
    /// <remarks>
    /// <b>这是「存」的一半，另一半是 <see cref="ApplySave"/>。</b>
    /// 两者必须覆盖<b>同一批</b>东西 —— 少存一样，读档就少一样，
    /// 而症状往往出现在很 unrelated 的地方（读档后英雄不再干活之类）。
    /// </remarks>
    private ClaudeCodeGameStudios.Data.SaveGame BuildSave() => new()
    {
        Version = ClaudeCodeGameStudios.Data.SaveVersion.Current,
        SimTime = _clock.Now,
        // 存的是随机源状态，不是种子：只存种子的话，读档后只能回到开局那一刻，
        // 而不是回到存档那一刻。
        RandomState = _rng.State.ToString(),
        Village = new ClaudeCodeGameStudios.Data.VillageSave
        {
            Id = _villageModel.Id,
            Resources = new Dictionary<string, long>(_villageModel.Snapshot()),
            // 建筑：表现层的工地尚未进入领域 Village（属 D-003 / B1-B3），故为空。
            // 这不是「忘了存」——是那边还没有可存的东西。
            Buildings = new List<ClaudeCodeGameStudios.Data.BuildingSave>(),
        },
        Heroes = _states.Select(s =>
        {
            var h = AgentOf(s);
            return new ClaudeCodeGameStudios.Data.HeroSave
            {
                Id = s.Definition.Id.Value,
                Name = h.Name,
                Presence = s.Presence.ToString(),
                CurrentActivity = s.CurrentActivity.Value,
                RemainingActionSeconds = s.RemainingActionSeconds,
                TargetId = s.CurrentTarget.Value,
                HasCompletedFirstDungeon = s.HasCompletedFirstDungeon,
                Personality = h.Personality,
                Diligence = h.Diligence,
                Power = h.Power,
                Speed = h.Speed,
                Grit = h.Grit,
                // M7 / D-030：金币 + 背包
                CoinAmount = h.Wallet.Amount(new ClaudeCodeGameStudios.Domain.Village.ResourceId("金币")),
                Bag = BuildBagSave(h.Bag),
                // 第四阶段 M4：自主购物的每日频率状态（0 = 从未购过）
                LastShoppingDay = h.LastShoppingDay,
                Origin = s.Definition.Origin,
                TraitModifiers = new Dictionary<string, double>(s.Definition.TraitModifiers),
                PreferenceWeights = new Dictionary<string, double>(s.Definition.PreferenceWeights),
            };
        }).ToList(),
        Missions = _mission is null
            ? new List<ClaudeCodeGameStudios.Data.MissionSave>()
            : new List<ClaudeCodeGameStudios.Data.MissionSave>
            {
                new()
                {
                    Id = _mission.Id.Value,
                    Status = _mission.Status.ToString(),
                    MemberIds = _mission.Members.Select(m => m.Definition.Id.Value).ToList(),
                },
            },
        // ← M6 与 M7 的接缝：不存它，读档后同一批结算会被重复发放（I4）
        AppliedSettlementKeys = _settlement.AppliedKeys
            .OrderBy(k => k, StringComparer.Ordinal).ToList(),
        // 编成也要存：不存就只能读档时「按在场者取前 N 个」重建，
        // 那是猜测 —— 玩家挑好的搭配会悄悄换成另一批人，且不报错。
        SquadMemberIds = _squad
            .Select(h => _states[_heroes.IndexOf(h)].Definition.Id.Value).ToList(),
        // 第三阶段 M6：关系（有向、逐条）与幂等键。不存关系，读档后社交白做；
        // 不存幂等键，同一批社交/副本会在读档后被重复应用。
        Relationships = _relationship.Snapshot()
            .Select(r => new ClaudeCodeGameStudios.Data.RelationshipSave
            {
                FromId = r.Key.From.Value,
                ToId = r.Key.To.Value,
                Value = r.Value,
            }).ToList(),
        AppliedRelationKeys = _relationship.AppliedKeys
            .OrderBy(k => k, StringComparer.Ordinal).ToList(),
        // 第四阶段 M4：摊位（库存是从英雄背包挪走的货）、购买幂等键、每日购物闸门。
        // 三者缺一，读档都会造出「存档前没发生过的世界」。
        Stalls = _stallService.Snapshot().Select(s => new ClaudeCodeGameStudios.Data.StallSave
        {
            OwnerId = s.Owner.Value,
            ItemId = s.ItemId.Value,
            Stock = s.Stock,
            UnitPrice = s.UnitPrice,
            Status = s.Status.ToString(),
            SimTime = s.SimTime,
            GridX = s.GridX,
            GridY = s.GridY,
        }).ToList(),
        AppliedPurchaseKeys = _stallService.AppliedRequestIds
            .OrderBy(k => k, StringComparer.Ordinal).ToList(),
        DailyShoppingLastRunDay = _dailyShopping.LastRunDay,
    };

    /// <summary>
    /// 按一份<b>已通过校验</b>的存档重建运行状态。<b>全有或全无。</b>
    /// </summary>
    /// <remarks>
    /// <b>调用前必须已经过 <c>SaveValidator</c>。</b>校验不过的存档
    /// 一律<b>整份拒绝</b>，不做部分恢复（不变量 I11）——
    /// 「能救几个救几个」会造出玩家与规则都解释不了的状态。
    /// </remarks>
    private static System.Collections.Generic.List<ClaudeCodeGameStudios.Data.BagEntrySave> BuildBagSave(
        ClaudeCodeGameStudios.Domain.Items.Inventory bag)
    {
        var list = new System.Collections.Generic.List<ClaudeCodeGameStudios.Data.BagEntrySave>();
        foreach (var kv in bag.Snapshot())
        {
            if (kv.Value > 0)
            {
                list.Add(new ClaudeCodeGameStudios.Data.BagEntrySave
                {
                    ItemId = kv.Key.Value,
                    Count = kv.Value,
                });
            }
        }
        return list;
    }
    private void ApplySave(ClaudeCodeGameStudios.Data.SaveGame save)
    {
        // ── 随机源先恢复：之后的一切随机都应接着存档那一刻往下走 ──
        if (ulong.TryParse(save.RandomState, out ulong state)) _rng.RestoreState(state);

        // ── 资源：按差额增删，而不是直接设值（Village 没有 setter）──
        foreach (var id in _villageModel.Snapshot().Keys.ToList())
        {
            long wanted = save.Village.Resources.TryGetValue(id, out var t) ? t : 0;
            long diff = wanted - _villageModel.Get(new ResourceId(id));
            if (diff > 0) _villageModel.Add(new ResourceId(id), diff);
            else if (diff < 0) _villageModel.TrySpend(new ResourceId(id), -diff);
        }

        foreach (var (id, amount) in save.Village.Resources)
        {
            long diff = amount - _villageModel.Get(new ResourceId(id));
            if (diff > 0) _villageModel.Add(new ResourceId(id), diff);
            else if (diff < 0) _villageModel.TrySpend(new ResourceId(id), -diff);
        }

        // ── 花名册：整份替换，不追加 ──
        _heroes.Clear();
        _states.Clear();
        _squad.Clear();
        _village.ClearHeroes();

        foreach (var hs in save.Heroes)
        {
            var agent = new HeroAgent
            {
                Name = hs.Name,
                Personality = hs.Personality,
                Diligence = hs.Diligence,
                Power = hs.Power,
                Speed = hs.Speed,
                Grit = hs.Grit,
            };
            // M7 / D-030：读档新建 hero 也要设 HeroId（AssignId 兑现 M2 挂账）
            agent.AssignId(new ClaudeCodeGameStudios.Domain.Heroes.HeroId(hs.Id));
            // M7：还原 Wallet / Bag（coinAmount + BagEntrySave 列表）
            agent.RestoreWallet(new ClaudeCodeGameStudios.Domain.Items.Wallet(
                new Dictionary<ClaudeCodeGameStudios.Domain.Village.ResourceId, long>
                {
                    [new ClaudeCodeGameStudios.Domain.Village.ResourceId("金币")] = hs.CoinAmount,
                }));
            var bagDict = new Dictionary<ClaudeCodeGameStudios.Domain.Items.ItemId, long>();
            foreach (var be in hs.Bag) bagDict[new ClaudeCodeGameStudios.Domain.Items.ItemId(be.ItemId)] = be.Count;
            agent.RestoreBag(new ClaudeCodeGameStudios.Domain.Items.Inventory(bagDict));
            // 第四阶段 M4：自主购物的每日频率状态（0 = 从未购过）
            agent.LastShoppingDay = hs.LastShoppingDay;
            _heroes.Add(agent);
            _village.AddHero(agent);
            _village.HasPeople = true;

            _states.Add(new HeroState(new ClaudeCodeGameStudios.Domain.Heroes.HeroDefinition(
                new ClaudeCodeGameStudios.Domain.Heroes.HeroId(hs.Id),
                hs.Name, hs.Origin,
                hs.TraitModifiers, hs.PreferenceWeights)));

            // ── R-03：恢复英雄的在场性 / 进行中的行动 / 目标 / 首通标记 ──
            // 以前这里只造了空状态：读档后随队的人会变回在村、行动凭空消失，
            // 而「读档前后世界一致」正是第三阶段 M6 的验收项之一。
            var restoredState = _states[^1];
            if (hs.HasCompletedFirstDungeon) restoredState.MarkFirstDungeonCompleted();

            if (hs.Presence == "OnMission")
            {
                restoredState.SetOnMission();          // 随队：不参与日常决策、不持有日常行动
            }
            else
            {
                restoredState.SetInVillage();
                if (!string.IsNullOrEmpty(hs.CurrentActivity))
                {
                    restoredState.BeginAction(
                        new ActivityId(hs.CurrentActivity),
                        hs.RemainingActionSeconds,
                        new HeroId(hs.TargetId));
                }
            }
        }

        // ── 结算幂等键：M6 与 M7 的接缝。不恢复就会重复发放 ──
        _settlement.RestoreApplied(save.AppliedSettlementKeys);

        // ── 关系：整份恢复（存档已过 SaveValidator；域层再挡一次非法输入）──
        var relationRecords = new List<ClaudeCodeGameStudios.Domain.Relationships.RelationshipRecord>(
            save.Relationships.Count);
        foreach (var r in save.Relationships)
        {
            relationRecords.Add(new ClaudeCodeGameStudios.Domain.Relationships.RelationshipRecord(
                new ClaudeCodeGameStudios.Domain.Relationships.RelationshipKey(
                    new HeroId(r.FromId), new HeroId(r.ToId)),
                r.Value));
        }

        _relationship.Restore(relationRecords, save.AppliedRelationKeys);

        // ── 第四阶段 M4：摊位 / 购买幂等键 / 每日购物闸门 ──
        // 摊位必须先于“英雄重建”之后恢复：Restore 本身只存目录，
        // 但读档后的购买要通过 _heroLookup 找到新 Agent —— 那个委托每次调用都重新查
        // _heroes，所以这里恢复目录即可，不持有旧 Agent 引用。
        // 库存是「已经从背包挪走的货」：不恢复，这些货会既不在背包也不在摊位。
        var stallRecords = new List<ClaudeCodeGameStudios.Domain.Stall.Stall>(save.Stalls.Count);
        foreach (var s in save.Stalls)
        {
            stallRecords.Add(ClaudeCodeGameStudios.Domain.Stall.Stall.Restore(
                new HeroId(s.OwnerId),
                new ClaudeCodeGameStudios.Domain.Items.ItemId(s.ItemId),
                s.Stock,
                s.UnitPrice,
                ParseStallStatus(s.Status),
                s.SimTime,
                s.GridX,
                s.GridY));
        }

        _stallService.Restore(stallRecords, save.AppliedPurchaseKeys);

        // 每日购物闸门：读档回到存档那天，同一天不应再跑一遍尝试。
        _dailyShopping.RestoreLastRunDay(save.DailyShoppingLastRunDay);

        // ── 事件管道重置（M0 审计 R-04）──
        // 读档是换一个世界：旧事件/旧杂谈继续参与去重，会让跨档复用的事件 ID
        // 被 EventLog 静默丢弃（「关系发生了，但杂谈里没有」）。这里整体换新。
        _events = new ClaudeCodeGameStudios.Domain.Events.EventLog();
        _feed = new ClaudeCodeGameStudios.Domain.Events.ChronicleFeed();
        _eventIds = new ClaudeCodeGameStudios.Domain.Events.EventIdGenerator();
        _chronicle.Clear();

        // ── 模拟时间：向前推进到存档那一刻 ──
        // 直接置位，**不是**向前推进 —— 存档可能比现在更早，
        // 曾经用 AdvanceTo（只向前推进），读档后时间停在 531 而存档是 31 ——
        // 因为存档更早，而它对「目标在更早」静默无操作。
        _clock.SetSimTime(save.SimTime);

        // 编成按**存档**恢复，不是「取前 N 个在场者」——
        // 后者是猜测，玩家挑好的搭配会被悄悄换掉，而且不报错。
        for (int i = 0; i < _states.Count; i++)
        {
            if (save.SquadMemberIds.Contains(_states[i].Definition.Id.Value))
            {
                _squad.Add(_heroes[i]);
            }
        }

        Refresh();
    }

    /// <summary>
    /// 存档里的摊位状态串 → 领域枚举。<b>只接受 <c>SaveValidator</c> 已放行的三种</b>。
    /// </summary>
    /// <remarks>
    /// 校验器已经拒绝了未知状态；走到这里的未知值只可能是“校验器与解析器不同步”，
    /// 那是应当炸掉的编程错误 —— 兜底成 Open 会读出一个比存档更“乐观”的世界。
    /// </remarks>
    private static ClaudeCodeGameStudios.Domain.Stall.StallStatus ParseStallStatus(string status)
        => status switch
        {
            "Open" => ClaudeCodeGameStudios.Domain.Stall.StallStatus.Open,
            "SoldOut" => ClaudeCodeGameStudios.Domain.Stall.StallStatus.SoldOut,
            "Closed" => ClaudeCodeGameStudios.Domain.Stall.StallStatus.Closed,
            _ => throw new InvalidOperationException(
                $"存档校验通过却出现未知摊位状态「{status}」—— 这是应当炸掉的编程错误，不是可兜底的状况。"),
        };

    private bool _saveTestDone;

    /// <summary>
    /// <c>--save</c> / <c>--load</c>：真的写一次盘、真的读一次档并打印前后对照。
    /// </summary>
    /// <remarks>
    /// <b>为什么要专门做这个开关</b>：<c>SavePortTests</c> 验的是端口
    /// （写盘、校验、整份拒绝），验不了<b>游戏有没有真的把运行状态装进去</b>。
    /// 而存档最典型的失败正是「端口全绿，但游戏存了个空壳」。
    /// </remarks>
    private void RunSaveSelfTest()
    {
        _saveTestDone = true;

        // ── R-03：先摆出三种「必须被读档恢复」的英雄状态 ──
        // ① 在村、正在采集（有剩余时长）② 随队远征中 ③ 已完成首个副本
        if (_states.Count >= 3)
        {
            var acting = _states[0];
            acting.SetOnMission();       // 先释放可能已存在的行动（测试夹具）
            acting.SetInVillage();
            acting.BeginAction(new ActivityId("采集"), 3.5);

            _states[1].SetOnMission();
            _states[2].MarkFirstDungeonCompleted();
        }

        // ── M4：先摆出「必须被读档恢复」的经济状态 ──
        // 开一个摊（货从背包挪进 Stall.Stock），用固定请求键买一次，
        // 再把买家的 LastShoppingDay 设成今天（模拟“今天已经自主购物过”）。
        string? stallItemId = null;
        long stallStockBefore = 0;
        bool stallPrepared = false;
        if (_heroes.Count >= 2)
        {
            var seller = _heroes[0];
            var buyer = _heroes[1];
            var stallItem = new ClaudeCodeGameStudios.Domain.Items.ItemId("石料");

            _itemGrant.Grant(seller.Id, stallItem, 10);
            var placed = _stallService.PlaceStall(seller.Id, stallItem, 10, _clock.Now);
            if (placed is not null)
            {
                var buy = _stallService.TryBuy(
                    seller.Id, buyer.Id, stallItem, 2, _clock.Now, "saverun-1");
                buyer.LastShoppingDay = (int)_clock.Day;   // 模拟“今天已经自主购物过”
                stallItemId = stallItem.Value;
                stallStockBefore = _stallService.GetStall(seller.Id)?.Stock ?? -1;
                stallPrepared = buy.Bought;
            }

            GD.Print($"SAVETEST M4 准备：开摊={(placed is null ? "FAIL" : "OK")}，" +
                     $"购买={(stallPrepared ? "Bought" : "FAIL")}，摊位库存={stallStockBefore}，" +
                     $"每日购物日={_dailyShopping.LastRunDay}，" +
                     $"买家LastShoppingDay={buyer.LastShoppingDay}");
        }

        // 英雄两套描述（HeroAgent / HeroState.Definition）的 id 必须一致 ——
        // 摊位 OwnerId 来自 Agent，存档引用校验查的是 Definition。M4 之前两者差 1 却没东西引用。
        bool idsAligned = _heroes.Count == _states.Count;
        for (int i = 0; idsAligned && i < _heroes.Count; i++)
        {
            idsAligned = _heroes[i].Id.Value == _states[i].Definition.Id.Value;
        }

        GD.Print($"SAVETEST 英雄 Id 对齐（Agent ↔ Definition）= {idsAligned}" +
                 (idsAligned ? "" :
                     $"：{string.Join("、", _heroes.Select((h, i) => $"{h.Name} {h.Id.Value}/{_states[i].Definition.Id.Value}"))}"));

        var save = BuildSave();
        GD.Print($"SAVETEST 存档路径 = {_saves.FilePath}");
        GD.Print($"SAVETEST 将写入：版本 {save.Version} 模拟时间 {save.SimTime:0.###}" +
                 $" 随机状态 {save.RandomState}");
        GD.Print($"SAVETEST 英雄 {save.Heroes.Count} 名" +
                 $"（{string.Join("、", save.Heroes.Select(h => h.Name))}）" +
                 $" 资源 {{{string.Join(", ", save.Village.Resources.Select(kv => kv.Key + ":" + kv.Value))}}}" +
                 $" 已结算键 {save.AppliedSettlementKeys.Count} 个");
        GD.Print($"SAVETEST 将写入：摊位 {save.Stalls.Count} 个" +
                 $"（{string.Join("、", save.Stalls.Select(s => $"{s.OwnerId}:{s.ItemId}×{s.Stock}@{s.Status}"))}），" +
                 $"购买幂等键 {save.AppliedPurchaseKeys.Count} 个" +
                 $"（{string.Join("、", save.AppliedPurchaseKeys)}），" +
                 $"每日购物日 {save.DailyShoppingLastRunDay}");

        try
        {
            _saves.Write(save);
        }
        catch (Exception ex)
        {
            GD.Print($"SAVETEST 写盘失败：{ex.GetType().Name} {ex.Message}");
            return;
        }

        GD.Print($"SAVETEST 写盘成功，文件 {new FileInfo(_saves.FilePath).Length} 字节");

        // ── 存之前把游戏搅乱：材料花掉、队伍拆散、时间走一段 ──
        long matBefore = Mat;
        string squadBefore = string.Join("、", _squad.Select(h => h.Name));
        _squad.Clear();                                   // 拆散队伍
        _villageModel.TrySpend(MaterialsId, matBefore);
        _clock.SetSimTime(_clock.Now + 500);
        ulong rngBefore = _rng.State;
        // M4：把摊位 / 幂等键 / 每日购物状态也搅乱 ——
        // 它们必须是被读档「找回来」的，而不是本来就在内存里。
        _stallService.Restore(
            Array.Empty<ClaudeCodeGameStudios.Domain.Stall.Stall>(),
            Array.Empty<string>());
        _dailyShopping.RestoreLastRunDay(int.MinValue);
        if (_heroes.Count >= 2) _heroes[1].LastShoppingDay = 0;
        GD.Print($"SAVETEST 故意搅乱：材料 {matBefore} → {Mat}，队伍「{squadBefore}」已拆散，" +
                 $"摊位已清空、每日购物闸门与 LastShoppingDay 已清零，随机状态已推进");

        // ── 读档 ──
        var loaded = _saves.Read();
        GD.Print($"SAVETEST 读档结果 Loaded={loaded.Loaded}" +
                 (loaded.Loaded ? "" : $" 原因：{loaded.Describe()}"));
        if (!loaded.Loaded) return;

        ApplySave(loaded.Save!);

        GD.Print($"SAVETEST 读档后：材料 {Mat}（应为 {matBefore}）" +
                 $" 模拟时间 {_clock.Now:0.###}（应为 {save.SimTime:0.###}）" +
                 $" 随机状态 {_rng.State}（应为 {rngBefore}）");
        GD.Print($"SAVETEST 读档后花名册 {string.Join("、", _heroes.Select(h => h.Name))}" +
                 $"（应为 {string.Join("、", save.Heroes.Select(h => h.Name))}）");
        GD.Print($"SAVETEST 读档后队伍「{string.Join("、", _squad.Select(h => h.Name))}」" +
                 $"（应为「{squadBefore}」——编成必须按存档恢复，不能「取前 N 个」重建）");
        GD.Print($"SAVETEST 已恢复的结算键 {string.Join(" | ", _settlement.AppliedKeys)}");

        // ── R-03：在场性 / 行动 / 目标 / 首通标记是否真的恢复 ──
        if (_states.Count >= 3)
        {
            var acting = _states[0];
            var away = _states[1];
            var firstDungeon = _states[2];

            bool actingOk = acting.CurrentActivity.Value == "采集"
                            && Math.Abs(acting.RemainingActionSeconds - 3.5) < 1e-6;
            bool awayOk = away.Presence == Presence.OnMission;
            bool firstOk = firstDungeon.HasCompletedFirstDungeon;

            GD.Print($"SAVETEST R-03 行动 {acting.CurrentActivity.Value}" +
                     $"/{acting.RemainingActionSeconds:0.###}（应为 采集/3.5）→ {actingOk}；" +
                     $"随队 {away.Presence}（应为 OnMission）→ {awayOk}；" +
                     $"首通 {firstDungeon.HasCompletedFirstDungeon}（应为 True）→ {firstOk}");
            GD.Print(actingOk && awayOk && firstOk ? "SAVETEST R-03 PASS" : "SAVETEST R-03 FAIL");
        }

        // ── M4：摊位 / 购买幂等键 / 每日购物状态是否真的恢复 ──
        // 只断「字段读回来了」不够：真正的性质是**读档后重放同一请求键不会再次扣款/发货**。
        if (stallPrepared && stallItemId is not null)
        {
            var seller = _heroes[0];
            var buyer = _heroes[1];
            var restoredStall = _stallService.GetStall(seller.Id);

            bool stallOk = restoredStall is not null
                           && restoredStall.ItemId.Value == stallItemId
                           && restoredStall.Stock == stallStockBefore
                           && restoredStall.Status
                              == ClaudeCodeGameStudios.Domain.Stall.StallStatus.Open;
            bool keyOk = _stallService.AppliedRequestIds.Contains("saverun-1");
            bool lastShoppingOk = buyer.LastShoppingDay == (int)_clock.Day;
            bool gateOk = _dailyShopping.LastRunDay == save.DailyShoppingLastRunDay;

            long stockBeforeReplay = restoredStall?.Stock ?? -1;
            var replay = _stallService.TryBuy(
                seller.Id, buyer.Id,
                new ClaudeCodeGameStudios.Domain.Items.ItemId(stallItemId),
                2, _clock.Now, "saverun-1");
            long stockAfterReplay = _stallService.GetStall(seller.Id)?.Stock ?? -1;
            bool replayOk = replay.Outcome
                                == ClaudeCodeGameStudios.Application.Stall.StallService.BuyOutcome.DuplicateRequest
                            && stockAfterReplay == stockBeforeReplay;

            GD.Print($"SAVETEST M4 摊位 库存={restoredStall?.Stock}（应为 {stallStockBefore}），" +
                     $"状态={restoredStall?.Status}（应为 Open），商品={restoredStall?.ItemId.Value}（应为 {stallItemId}）→ {stallOk}；" +
                     $"幂等键={keyOk}；每日购物日={_dailyShopping.LastRunDay}（应为 {save.DailyShoppingLastRunDay}）→ {gateOk}；" +
                     $"买家LastShoppingDay={buyer.LastShoppingDay}（应为 {(int)_clock.Day}）→ {lastShoppingOk}");
            GD.Print($"SAVETEST M4 读档后重放购买键 {replay.Outcome}" +
                     $"（必须 DuplicateRequest），库存 {stockBeforeReplay} → {stockAfterReplay}（必须不变）→ {replayOk}");
            GD.Print(idsAligned && stallOk && keyOk && lastShoppingOk && gateOk && replayOk
                ? "SAVETEST M4 PASS" : "SAVETEST M4 FAIL");
        }

        // ── M6 与 M7 的接缝真正生效了吗 ──
        // 键存回来只说明「写进去了、读出来了」；真正的性质是
        // **读档后重放同一笔结算会被拒绝**（I4 / 基线 §13.7）。
        // 不恢复键的话，材料会在读档后暴涨 —— 而症状出现在很远的地方，极难归因。
        if (_settlement.AppliedKeys.Count > 0)
        {
            long matBeforeReplay = Mat;
            string key = _settlement.AppliedKeys.First();
            // BattleResultId 一律按「胜利」重放：若幂等真的生效，
            // 它连算都不该算，无论传 true 还是 false。
            bool reapplied = _settlement.Apply(
                new ClaudeCodeGameStudios.Domain.Settlement.SettlementKey(
                    key.Split('#')[0],
                    string.Join("#", key.Split('#').Skip(1))),
                heroesWon: true);

            GD.Print($"SAVETEST 读档后重放已结算的键 → Apply 返回 {reapplied}" +
                     $"（必须 false）材料 {matBeforeReplay} → {Mat}（必须不变）");
        }
        GD.Print($"SAVETEST 资源快照 {{{string.Join(", ",
            _villageModel.Snapshot().Select(kv => kv.Key + ":" + kv.Value))}}}");
    }

    private void RunDungeonSelfTest()
    {
        _dungeonTestDone = true;

        // 真的走一遍派单 —— 换成副本任务（Glory == true），那是走 DungeonFlow 的那条路。
        _pendingJob = Jobs.First(j => j.Glory);

        // 队伍是玩家点花名册组出来的（开局**不预编**），而自检没有人点 ——
        // 所以这里走**同一个入口**组一支队伍出来，不绕过领域规则：
        // 加入与出发合法性仍然由 SquadRules 判定。
        if (_squad.Count == 0)
        {
            for (int i = 0; i < _heroes.Count && _squad.Count < 2; i++)
            {
                OnRosterItemActivated(i);
            }
            GD.Print($"DUNGEONTEST 自检组队：{_squad.Count} 人（与玩家同一入口）");
        }

        var crew = ReadySquad;
        if (crew.Count == 0) { GD.Print("DUNGEONTEST 无人在场，跑不了。"); return; }

        var memberStates = crew.Select(h => _states[_heroes.IndexOf(h)]).ToArray();
        try
        {
            _mission = Mission.Create(
                new MissionId($"dtest{_clock.AbsoluteHour:0}"), _pendingJob.Name,
                new Squad(_squadRules, memberStates), _squadRules);
            _mission.Start(IsOnMission);
        }
        catch (Exception ex)
        {
            GD.Print($"DUNGEONTEST 派单失败：{ex.GetType().Name} {ex.Message}");
            return;
        }

        foreach (var h in crew) h.Away = true;

        long matBefore = Mat;
        int notesBefore = _chronicle.Count;

        // ── R-01：发放**之前**的背包快照（对照用）──
        var bagBefore = crew.ToDictionary(
            h => h.Id.Value,
            h => h.Bag.Snapshot().ToDictionary(kv => kv.Key.Value, kv => kv.Value));

        var outcome = ResolveDungeon(_pendingJob, crew);

        GD.Print($"DUNGEONTEST 队伍 {crew.Count} 人 → {_pendingJob.Name}");
        GD.Print($"DUNGEONTEST 任务状态 = {_mission?.Status}（ResolveDungeon 不清空 _mission，由本处清）");
        _mission = null;

        foreach (var r in outcome.Records)
        {
            GD.Print($"  {r.Stage} 「{r.EncounterName}」 {(r.Result.HeroesWon ? "胜" : "负")}" +
                     $" 可复核={r.Result.IsVerifiableAgainstLog()}" +
                     $" 引用={r.Result.DecidingEntryIds.Count} 条" +
                     $" 挂入实例={r.AttachedToRun}");
        }

        GD.Print($"DUNGEONTEST 声明阶段数 {ClaudeCodeGameStudios.Application.DungeonFlow.DeclaredStageCount}" +
                 $" 实际打了 {outcome.Records.Count} 场");
        GD.Print($"DUNGEONTEST 实例上挂着的结果 = {outcome.Run.BattleResultId}" +
                 $"（I3：只能有一个）");
        GD.Print($"DUNGEONTEST 副本状态 = {outcome.Run.Status} 整场成功 = {outcome.Succeeded}");
        GD.Print($"DUNGEONTEST 材料 {matBefore} → {Mat}" +
                 $"（失败则必须不变：D-010 Q1 失败不给奖励）");
        // 整个资源快照：只打总数的话，「奖励进了别的桶」这类 bug 完全看不出来
        // —— 界面就显示一个材料数。踩过一次，所以这里连桶一起打。
        GD.Print("DUNGEONTEST 资源快照 = {" +
                 string.Join(", ",
                     _villageModel.Snapshot().Select(kv => $"{kv.Key}:{kv.Value}")) + "}");

        // ── I4：把同一个结算键再 Apply 一次，必须一分不加 ──
        // 单测验的是 Settlement 自己，验不了「游戏会不会真的重复调用它」。
        // 而「读档重放」正是基线 §13.7 要防的场景。
        var replayKey = new ClaudeCodeGameStudios.Domain.Settlement.SettlementKey(
            outcome.Run.Id.Value, outcome.Run.BattleResultId);
        long beforeReplay = Mat;
        bool appliedAgain = _settlement.Apply(replayKey, outcome.Succeeded);
        GD.Print($"DUNGEONTEST 重放同一结算键 → Apply 返回 {appliedAgain}，" +
                 $"材料 {beforeReplay} → {Mat}" +
                 $"（材料必须不变、Apply 必须返回 false：I4 已完成事件不会重复应用）");

        // ── R-01：战利品分配与入包 ──
        // 单测验的是分配规则与发放入口，验不了「游戏有没有真的把战利品
        // 落到参战者的个人背包里」—— 那正是 D-018「奖励进错桶」的同一类风险。
        var loot = _lastDungeonLoot;
        GD.Print($"DUNGEONTEST 战利品分配 = {loot.Grants.Count} 条（副本 {outcome.Run.Id}）：" +
                 (loot.Grants.Count == 0 ? "（无）" :
                  string.Join("、", loot.Grants.Select(g => $"{g.Hero.Value} {g.Item.Value}×{g.Quantity}"))));

        bool lootInBags = true;
        long lootTotal = 0;
        foreach (var g in loot.Grants)
        {
            var hero = _heroes.FirstOrDefault(h => h.Id == g.Hero);
            if (hero is null) { lootInBags = false; continue; }

            long before = bagBefore.TryGetValue(g.Hero.Value, out var snap)
                && snap.TryGetValue(g.Item.Value, out var n) ? n : 0;
            long after = hero.Bag.Count(g.Item);
            lootTotal += g.Quantity;

            if (after - before != g.Quantity) lootInBags = false;
            GD.Print($"  {hero.Name}：{g.Item.Value} {before} → {after}（应 +{g.Quantity}）");
        }

        // 参战者之外的英雄：一件都不能多（未参战者不参与分配）。
        bool nobodyElseGotLoot = true;
        foreach (var hero in _heroes)
        {
            if (loot.Grants.Any(g => g.Hero == hero.Id)) continue;
            foreach (var g in loot.Grants)
            {
                long before = bagBefore.TryGetValue(hero.Id.Value, out var snap)
                    && snap.TryGetValue(g.Item.Value, out var n) ? n : 0;
                if (hero.Bag.Count(g.Item) - before != 0) nobodyElseGotLoot = false;
            }
        }

        GD.Print($"DUNGEONTEST 战利品入包 = {lootInBags}，未参战者没拿到 = {nobodyElseGotLoot}" +
                 $"（失败时两人都必须是 true：D-010 Q1 失败不给奖励）");

        // ── R-01 M4：拿到的物品要能真的开摊、被另一位英雄买走 ──
        if (loot.Grants.Count > 0 && _heroes.Count >= 2)
        {
            var firstGrant = loot.Grants[0];
            var seller = _heroes.FirstOrDefault(h => h.Id == firstGrant.Hero);
            var buyer = _heroes.FirstOrDefault(h => h.Id != firstGrant.Hero);
            var item = firstGrant.Item;
            var gold = ClaudeCodeGameStudios.Application.Stall.StallService.Gold;

            if (seller is not null && buyer is not null)
            {
                long stock = Math.Min(3, seller.Bag.Count(item));
                long buyerGold0 = buyer.Wallet.Amount(gold);
                long buyerBag0 = buyer.Bag.Count(item);

                var placed = stock > 0
                    ? _stallService.PlaceStall(seller.Id, item, stock, _clock.Now)
                    : null;

                var bought = placed is null
                    ? default
                    : _stallService.TryBuy(seller.Id, buyer.Id, item, 1, _clock.Now, "dungeonrun-1");

                bool chainOk = placed is not null
                              && bought.Bought
                              && buyer.Bag.Count(item) == buyerBag0 + 1
                              && buyer.Wallet.Amount(gold) == buyerGold0 - 5;   // 石料单价 5

                GD.Print($"DUNGEONTEST 战利品开摊={(placed is null ? "FAIL" : $"OK（{seller.Name} 摆 {stock} 份）")}，" +
                         $"他人购买={bought.Outcome}，买家背包 {buyerBag0} → {buyer.Bag.Count(item)}，" +
                         $"买家金 {buyerGold0} → {buyer.Wallet.Amount(gold)} → {chainOk}");
            }
        }

        // ── R-01 M3：奖励要能存进档、读档后仍属于正确的人 ──
        var saveWithLoot = BuildSave();
        bool lootPersisted = true;
        foreach (var g in loot.Grants)
        {
            var heroSave = saveWithLoot.Heroes.FirstOrDefault(h => h.Id == g.Hero.Value);
            long saved = heroSave?.Bag
                .Where(b => b.ItemId == g.Item.Value)
                .Sum(b => b.Count) ?? 0;
            if (saved <= 0) lootPersisted = false;
        }
        GD.Print($"DUNGEONTEST 战利品已入档 = {lootPersisted}，" +
                 $"已结算键 {saveWithLoot.AppliedSettlementKeys.Count} 个" +
                 $"（存档版本 {saveWithLoot.Version}，R-01 未新增字段）");

        foreach (string note in _chronicle.Recent(_chronicle.Count - notesBefore))
        {
            GD.Print($"DUNGEONTEST 杂谈| {note}");
        }
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is not InputEventKey { Pressed: true } k) return;
        switch (k.Keycode)
        {
            case Key.F: OnSummon(); break;
            case Key.R: OnTask(); break;
            case Key.Q: OnUpgrade(); break;
        }
    }

    private void ProcessShot(float dt)
    {
        if (_shotFrame < 0)
            foreach (var a in OS.GetCmdlineUserArgs())
                if (a.StartsWith("--shot=")) { _shotPath = a[7..]; _shotFrame = 0; }
        if (_shotFrame < 0) return;
        _shotFrame += (int)(dt * 60);
        if (_shotFrame >= 30)
        {
            var img = GetViewport().GetTexture().GetImage();
            GD.Print($"SHOT saved: {_shotPath} err={img.SavePng(_shotPath)}");
            _shotFrame = -1;
            GetTree().Quit();
        }
    }
}
