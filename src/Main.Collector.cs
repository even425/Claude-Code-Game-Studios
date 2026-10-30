using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using ClaudeCodeGameStudios.Application;
using ClaudeCodeGameStudios.Domain.Heroes;
using ClaudeCodeGameStudios.Domain.Items;
using ClaudeCodeGameStudios.Domain.Quests;
using ClaudeCodeGameStudios.Presentation;

/// <summary>
/// 主场景的<b>经济采集器</b>部分（A1 拆分：把「模拟推进」这一类职责收在一处）。
///
/// <para>⚠️ **这是纯代码搬移** —— 逻辑一行没改。采集器是 R-05 的测量装置：
/// 它按日驱动副本、摆摊、村庄升级，并把经济快照落成 jsonl。
/// 它<b>不参与</b>正常玩法的经济（正常玩法那条路是玩家点按钮）。</para>
/// </summary>
public partial class Main : Node2D
{
    /// <summary>
    /// <c>--collect=N</c>：headless 采集驱动器（运行闭环验证与经济数据采集准备）。
    /// </summary>
    /// <remarks>
    /// <para><b>它为什么存在</b>：正常玩法下经济的关键环节（副本、摆摊）**全靠玩家点按钮**
    /// —— 「英雄自动开摆」是从第二阶段 M4 就挂账的 P3/V2 项。
    /// 所以「让游戏自己跑几天」采不到任何经济数据：soak 554 天里
    /// 副本 0 次、摊位 0 个、成交 0 笔，只有采集在涨材料。</para>
    ///
    /// <para><b>它做什么</b>：<b>挂在日切上</b>，跟着真实游戏循环走 ——
    /// 每天副本一次、摆摊一次，自主购物本来就是每日自动的。
    /// 它<b>不改任何规则</b>，只是替玩家按按钮。</para>
    ///
    /// <para><b>为什么挂在日切而不是自己推进时间</b>：自己推进时间会跳过
    /// 「英雄在工地干活」这段状态机 —— 于是每次都挑不出人，副本永远开不了
    /// （第一版就是这么写的：前 6 天零副本）。挂在日切上，
    /// 干活、完工、空闲的节奏由游戏自己走，采集器只在日界插一脚。</para>
    ///
    /// <para><b>它验的是什么</b>：「规则驱动的经济」，不是「玩家操作产生的经济」。
    /// 两者走的是同一套规则（同一个 <c>ResolveDungeon</c>、同一个
    /// <c>StallService.TryBuy</c>、同一个 <c>HeroBehaviorService</c>），
    /// 所以采到的掉落分布与成交价分布对 R-05 二轮是有用的；
    /// 但它<b>不</b>代表玩家的真实操作节奏（玩家不会每天恰好两次副本）。</para>
    /// </remarks>
    private void RunCollect()
    {
        _collectDone = true;

        int days = 30;
        var arg = OS.GetCmdlineUserArgs()
            .FirstOrDefault(a => a.StartsWith("--collect=", StringComparison.Ordinal));
        if (arg is not null
            && int.TryParse(arg.Substring("--collect=".Length), out var parsed) && parsed > 0)
        {
            days = parsed;
        }

        _collectTargetDays = days;
        GD.Print($"COLLECT 开始：目标 {days} 天 · 种子 {BootSeed}" +
                 $"（--seed= 可固定；不固定则每次运行都不同）" +
                 $" · 金币报酬 X = {_goldRewardUnits / 100.0:0.##} 金/参战者" +
                 $" · 价格表 = {_priceBookSource}（{_priceBook.Describe()}）" +
                 $" · 流水路径 = {_economyLog?.FilePath ?? "（无）"}");

        // ⚠️ **D-111 的两个对照开关**（只用于归因实验，正常采集一律开着）：
        //   `--collectbuild=0`  采集器**不盖房子**（D-108 的改动）
        //   `--gatherbonus=0`  关掉**采集点的加成**（D-105 / D-107 的改动）
        // ⇒ 用来回答「市场的停摆是哪个改动带来的」。
        _gatherBonusOff = HasZeroFlag("--gatherbonus=");
        if (_gatherBonusOff)
        {
            GD.Print($"COLLECT 对照实验：采集加成={(_gatherBonusOff ? "关" : "开")}（D-111 归因用）");
        }
    }

    /// <summary>
    /// 命令行里有没有「某个开关 = 0」的参数（对照实验用）。
    /// </summary>
    /// <param name="prefix">参数前缀（如 <c>--collectbuild=</c>）。</param>
    /// <returns>值为 0（或 0.0）时为 true。</returns>
    private static bool HasZeroFlag(string prefix)
    {
        var arg = OS.GetCmdlineUserArgs()
            .FirstOrDefault(a => a.StartsWith(prefix, StringComparison.Ordinal));
        if (arg is null) return false;

        string raw = arg.Substring(prefix.Length);
        return double.TryParse(raw, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out double v)
            && Math.Abs(v) < 1e-9;
    }

    /// <summary>
    /// 采集的一天：副本 → 摆摊 → 升级（够钱就升）→ 记一份经济快照。
    /// 自主购物由每帧的每日闸门自动跑。
    /// </summary>
    /// <remarks>
    /// <b>顺序要紧</b>：先副本（今天的战利品进背包）→ 再摆摊（把战利品上架）
    /// → 然后当天的自主购物就能买到今天刚掉的东西。
    /// 反过来的话，战利品要等到第二天才上架。
    /// <para>
    /// <b>快照取在「日界那一刻」</b>（副本 → 摆摊 → 升级 → 快照 → 之后才是当天的自主购物）。
    /// 所以标记为第 N 天的快照，反映的是<b>第 N 天开始时</b>的世界 ——
    /// <b>当天还没开张</b>。这不是缺陷，而是一个<b>整齐的切面</b>：
    /// 所有数量都取自同一瞬间，逐日核对才对得上。
    /// </para>
    /// <para>
    /// ⚠️ 但它有个<b>必须记住的后果</b>：<b>税额余数</b>与<b>当天的成交</b>不在同一个窗口里
    /// —— 快照里的余数只覆盖<b>到前一天为止</b>的成交。
    /// 拿「全部成交」去配「快照的余数」做税务核对，会<b>恒差最后一天的量</b>
    /// （2026-10-20 的 37 人探针实测差出整整 0.2 金）。
    /// 核对时必须把窗口对齐（见 <c>production/qa/analyze-econ.py</c>）。
    /// </para>
    /// </remarks>
    private void CollectOneDay()
    {
        DispatchDungeonForCollection();
        // ⚠️ **D-086**：开摊走**游戏的那一份**（`OpenStallsForIdleHeroes`），
        // 不再是采集器自己的一套 ⇒ 「测到的」就是「玩到的」。
        OpenStallsForIdleHeroes();
        // ⚠️ **D-125：村庄日常开销**（采集器与真实游戏**同一份**规则 ⇒ 账一致）。
        VillageUpkeepRound();
        // ⚠️ **D-122：先盖店，再升级**（施工队是同一批人 ⇒ 一次只盖一样）。
        // ⇒ 商店盖完后（或在施工时）本轮**不升级** ⇒ 升级等商店建成再说。
        bool shopStarted = BuildShopsForCollection();
        if (!shopStarted) UpgradeVillageForCollection();
        // ⚠️ **D-108**：采集器也要**盖房子**（钱得有出口）⇒ 真实玩家每天都在做这件事。
        RecordEconomySnapshot();
    }

    /// <summary>
    /// 采集用盖店：<b>有余钱就盖一间武器店 / 服装店</b>（D-122，开发者拍板）。
    /// </summary>
    /// <remarks>
    /// <para>⚠️ <b>只盖商店，不盖别的</b>：D-112 的教训 —— 采集器盖房（围栏 / 试炼塔）
    /// 曾把收购站的预算抽干 ⇒ 背包占满 ⇒ <b>市场在第 100 天停摆</b>。
    /// ⇒ 这次候选表里<b>只有两间店</b>，且沿用 D-108 的老规矩：
    /// <b>盖完还得买得起下一级村庄升级</b>（否则村庄永远升不上去）。</para>
    ///
    /// <para>⚠️ <b>工期与人力</b>：走的是<b>玩家那套建造流程</b>
    /// （<c>BuildingService</c> ⇒ 进度由人力推进 ⇒ 与真实游戏一致）。</para>
    /// </remarks>
    private bool BuildShopsForCollection()
    {
        var candidates = new[]
        {
            ClaudeCodeGameStudios.Domain.Village.BuildableBuildings.WeaponShop,
            ClaudeCodeGameStudios.Domain.Village.BuildableBuildings.ArmorShop,
        };

        // ⚠️ **已经有店在施工** ⇒ 本轮不发起新的（施工队是同一批人 ⇒ 一次只盖一样）。
        foreach (var active in _buildingService.ActiveConstructions)
        {
            foreach (var c in candidates)
            {
                if (active.Definition.Id.Value == c.Id.Value) return true;
            }
        }

        // ⚠️ **只挑「还没建成」的那间**（同一种建筑只能有一栋 ⇒ 已建成的不再考虑）。
        var pending = new List<ClaudeCodeGameStudios.Domain.Village.BuildableBuilding>();
        foreach (var c in candidates)
        {
            if (!_buildingService.IsCompleted(c.Id)) pending.Add(c);
        }

        // 两间都盖好了 ⇒ 本轮不盖 ⇒ **也让出升级的钱**（商店不再是唯一出口）。
        if (pending.Count == 0) return true;

        // 已经盖了几栋（同一种建筑只能有一栋 ⇒ 计数只可能是 0 或 1）。
        var existing = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var c in candidates)
        {
            int count = 0;
            foreach (var b in _villageModel.Buildings)
            {
                if (b.Id.Value == c.Id.Value) count++;
            }
            existing[c.Id.Value] = count;
        }

        // ⚠️ **留够升级的钱**：下一级村庄升级要多少（满级时是 0）。
        long nextUpgradeGold = _tier < ClaudeCodeGameStudios.Application.VillageUpgradeService.MaxTier
            ? _upgradeService.CostFor(_tier).Gold
            : 0L;

        var plan = ClaudeCodeGameStudios.Application.CollectorBuildingPlan.Plan(
            VillageGold,
            nextUpgradeGold,
            pending,
            existing,
            def => _buildingService.CanStart(def),
            def => ClaudeCodeGameStudios.Application.BuildingSpotFinder.Find(
                def, (x, y) => _buildingService.CanPlaceAt(def, x, y)),
            // ⚠️ **D-122：商店不占升级的钱** —— 升级 5 级共 750 金，
            // 而商店 100 金 ⇒ 若也必须留升级钱，它就**永远排不上**
            // （实测：采集器一天都没盖成店 ⇒ 装备线在标定里从不发生）。
            keepUpgradeReserve: false);

        if (plan is null) return false;

        // ⚠️ **人力由每帧的 `AssignWorkers` 分配** ⇒ 这里给 0（不占人）也照样会推进。
        if (!_buildingService.TryStartAt(plan.Value.Definition, plan.Value.GridX, plan.Value.GridY, 0))
        {
            return false;
        }

        GD.Print($"COLLECT 盖店：{plan.Value.Definition.Name} @ ({plan.Value.GridX},{plan.Value.GridY})");
        return true;
    }

    /// <summary>
    /// 采集用副本：像玩家点「发布任务」那样，先建任务再打副本。
    /// </summary>
    /// <remarks>
    /// <b>为什么走任务流程而不是直接调 <c>ResolveDungeon</c></b>：
    /// 直接调会跳过领域层的「随队 → 归队」状态迁移，
    /// 英雄的 <c>Presence</c> 会卡在 <c>OnMission</c> —— 之后谁都不敢再派他。
    /// 走 <c>Mission</c> 那套，状态迁移由领域层负责（不变量 I2 / I14）。
    /// </remarks>
    private void DispatchDungeonForCollection()
    {
        // 采集模式的人数多了，**一场副本喂不饱全村** —— 材料只从副本来，
        // 37 个英雄共用 1 场/天 → 35 个人永远没货可卖 → 市场只有 2–3 个卖家，
        // 那就不是「37 人的经济」了。
        //
        // ⇒ 副本数随人口缩放：**每 4 人一场**（一个满编小队 4 人对应 1 场 2 人副本的产出量级）。
        int dungeons = Math.Max(1, (_heroes.Count + 3) / 4);
        for (int k = 0; k < dungeons; k++)
        {
            DispatchOneDungeonForCollection(k);
        }
    }

    /// <summary>采集用副本：像玩家点「发布任务」那样，先建任务再打副本。</summary>
    /// <param name="index">今天第几场（用来轮换队伍，避免总是同样两个人）。</param>
    private void DispatchOneDungeonForCollection(int index)
    {
        // 轮换起点：按「日序 × 2 + 场序 × 2」错开，
        // 否则「取前两个可用的人」会永远是同样两个人（他们每场结束就回来了）。
        int n = _heroes.Count;
        int start = n > 0 ? (((int)_clock.Day * 2 + index * 2) % n + n) % n : 0;

        // ⚠️ **D-122：优先派有装备的人**（开发者拍板）⇒ 装备才是成长线，
        // 而不只是「挂在身上的一件东西」。规则在 `CrewSelection`（应用层纯函数）。
        var crew = ClaudeCodeGameStudios.Application.CrewSelection.Select(
            _heroes, start, 2,
            h => !h.Away && !h.ToSite && !IsOnMission(_states[_heroes.IndexOf(h)]))
            .ToList();

        if (crew.Count == 0)
        {
            _collectSkippedDungeons++;
            return;
        }

        var memberStates = crew.Select(h => _states[_heroes.IndexOf(h)]).ToArray();
        try
        {
            _mission = Mission.Create(
                new MissionId($"cm{_clock.AbsoluteHour:0}"), DungeonName,
                new Squad(_squadRules, memberStates), _squadRules);
            _mission.Start(IsOnMission);
            foreach (var h in crew) h.Away = true;

            ResolveDungeon(new Job(DungeonName, "铁料", 38, 11f, true), crew);
        }
        catch (ArgumentException)
        {
            // 无效队伍不能启动任务（02 F-01）—— 静默拒绝，不打断采集。
            _mission = null;
        }
        catch (InvalidOperationException)
        {
            // 有成员已在别的任务里（不变量 I2）。
            _mission = null;
        }
        finally
        {
            _mission = null;
        }
    }

    /// <summary>
    /// 采集用摆摊：<b>已并入游戏的那一份</b>（D-086）。
    /// </summary>
    /// <remarks>
    /// ⚠️ 原来这里有**采集器自己的一套**开摊逻辑（还带一条「今天卖哪一样」的轮换）。
    /// 那条轮换本是**压测专用**的做法，却决定了整个市场的商品分布
    /// （实测：成交额一度有 87–91% 集中在布匹 ⇒ 是采集器规则主导的，不是游戏规则）。
    /// ⇒ 现在它<b>升为游戏规则</b>，游戏与采集器<b>共用同一份实现</b>
    /// （`OpenStallsForIdleHeroes`）⇒ <b>测到的就是玩到的</b>。
    /// </remarks>

    /// <summary>
    /// 采集用升级：<b>金库与材料都够就升一级</b>（D-054 阶段 3）。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么必须有它</b>：D-054-02 定了「村庄升级消耗金币（从金库扣）」，
    /// 而金库是<b>只进不出的池子</b>（D-054 审计的核心发现）。
    /// 采集器若不升级，**金库支出侧在采集里永远验不到** ——
    /// 重采之后金库照样只进不出，**看起来像「修法没生效」**。
    /// 那是一个「验证装置缺失」伪装成「规则无效」的陷阱。</para>
    ///
    /// <para><b>施工队怎么定</b>：**没被派去副本的人**去工地。
    /// ⚠️ <b>不能复用 <see cref="OnUpgrade"/> 的 <c>_squad</c> 逻辑</b> ——
    /// 采集器不用玩家队伍，headless 里 <c>_squad</c> 是**空的**，
    /// 那样会把<b>所有人</b>派去工地 → 副本派不出人、交易全停 → 经济直接死掉。</para>
    /// </remarks>
    private void UpgradeVillageForCollection()
    {
        if (_buildTarget >= 0) return;                       // 一次只盖一样
        if (!_upgradeService.CanUpgrade(_tier)) return;     // 钱或材料不够
        if (!_upgradeService.TryUpgrade(_tier)) return;     // 扣款（两边都扣或都不扣）

        _buildProgress = 0f;
        _buildTarget = _tier + 1;

        // 施工队：**没被派去副本的人**去工地（副本里的人还在路上）。
        foreach (var h in _heroes)
        {
            if (h.Away) continue;
            h.ToSite = true;
        }

        _village.BuildProgress = 0f;
        _village.BuildName = UpgradeNames[_tier];
        GD.Print($"COLLECT 升级 → 第 {_buildTarget} 级（扣 金 {_upgradeService.CostFor(_tier).Gold}" +
                 $" + 材料 {_upgradeService.CostFor(_tier).Material}）；金库余额 {VillageGold}");
    }

    /// <summary>
    /// 村庄<b>可花却没花</b>的钱（D-110）。
    /// </summary>
    /// <remarks>
    /// 口径 = 村库现金 + 村庄资产 − <b>下一级升级要留的钱</b>
    /// （留着的钱不算「没花」—— 它有明确的用处：升级村庄）。
    /// <br>⚠️ <b>为什么要有这一项</b>：采集器<b>不召唤</b>（人口固定 ⇒ 标定可比），
    /// 而召唤是真实游戏里唯一<b>持续</b>的花钱方式 ⇒ 钱只会囤
    /// ⇒ 这一项让「囤着的钱」<b>单独可见</b>（≈ 还能招多少人）。
    /// </remarks>
    private long UnspentVillageGold()
    {
        long asset = _villageModel.Get(ClaudeCodeGameStudios.Application.VillageUpgradeService.Asset);
        long reserve = _upgradeService.CostFor(_tier).Gold;   // 满级时是 0
        return ClaudeCodeGameStudios.Application.VillageUnspent.Amount(VillageGold, asset, reserve);
    }

    /// <summary>
    /// 记一份<b>每日经济快照</b>到流水（D-054 阶段 3）。
    /// </summary>
    /// <remarks>
    /// 守恒律（<b>流通口径</b>）的每一项都在这里：
    /// <c>Σ英雄钱包金币 + 村库金币 + 累计销毁金币 = 初始金币 + 累计发放金币</c>
    /// —— 缺任何一项，「逐日核对」就只能靠推算，而**推算等于没验证**。
    /// <para>
    /// ⚠️ <b>不含「税额余数」</b>：余数是记账零头，不是钱（钱在买卖双方之间完整转移）。
    /// </para>
    /// <para>
    /// ⚠️ <b>也不把「待发零头」算进守恒律</b>：它是「已经挣到、但<b>还没进任何钱包</b>」
    /// 的<b>应收</b> —— 左边是「钱在哪」（它哪都不在），右边是「已经兑现了多少」。
    /// 只把它加在一边 = 凭空多算那部分钱（2026-10-20 的 37 人探针实测差出
    /// 整整一个零头：3810 vs 3820）。
    /// 它作为<b>独立科目</b>照样记在这里（跟着存档 0.6 的 <c>GoldCarry</c> 走），
    /// 单独核对自洽性（每英雄 &lt; 100）。
    /// </para>
    /// </remarks>
    private void RecordEconomySnapshot()
    {
        if (_economyLog is null) return;

        long heroGold = 0;
        int builders = 0;
        var perHero = new List<(string Id, string Name, long Gold)>(_heroes.Count);
        foreach (var h in _heroes)
        {
            long g = h.Wallet.Amount(ClaudeCodeGameStudios.Application.Stall.StallService.Gold);
            heroGold += g;
            perHero.Add((h.Id.Value, h.Name, g));
            if (h.ToSite) builders++;
        }

        // 人均金币的**分布**（D-054 阶段 5）：「合计稳」≠「钱分得匀」。
        // 只有 5 个分位点，但足以看出「人人 40 金」与「3 个人 500 金、其余 0 金」的区别。
        perHero.Sort((a, b) => a.Gold.CompareTo(b.Gold));
        long? min = null, p25 = null, med = null, p75 = null, max = null;
        string? topId = null;
        string? topName = null;
        if (perHero.Count > 0)
        {
            var sorted = perHero.Select(p => p.Gold).ToList();
            min = sorted[0];
            max = sorted[^1];
            topId = perHero[^1].Id;
            topName = perHero[^1].Name;
            med = Percentile(sorted, 0.5);
            p25 = Percentile(sorted, 0.25);
            p75 = Percentile(sorted, 0.75);
        }

        // ⚠️ **累计销毁 = 商队销毁的**（D-090）：升级花掉的钱**不再是「销毁」**，
        // 而是变成**村庄资产**（另一项，仍在守恒律的左边）⇒ 这一项只留商队那 90%。
        long destroyed = _caravanMarket.TotalDestroyedGold
            // ⚠️ **D-125**：村庄日常开销也是「流出村庄的钱」⇒ 属于「已销毁」。
            // ⇒ 不并进来的话，守恒律左边少一笔 ⇒ **钱凭空消失**（实测 0/200）。
            + ClaudeCodeGameStudios.Application.VillageUpkeepService.TotalUpkeepSpent;

        _economyLog.AppendSnapshot(
            _clock.Now,
            heroGold,
            VillageGold,
            Mat,
            _walletWriter.TotalGranted,
            destroyed,
            _upgradeService.UpgradesStarted,
            _stallService.VillageCutCarry,
            _heroes.Count,
            builders,
            _walletWriter.TotalPending,
            min, p25, med, p75, max, topId, topName,
            _caravanMarket.TotalDestroyedGold,
            _villageModel.Get(ClaudeCodeGameStudios.Application.VillageUpgradeService.Asset),
            // ⚠️ **D-101**：大事件的「外部收入 / 外部支出」两项
            // （大事件发的钱是从外面进来的 ⇒ 守恒律右边必须加上它）。
            ClaudeCodeGameStudios.Application.WorldEventResourceApplier.TotalEventGoldIn,
            ClaudeCodeGameStudios.Application.WorldEventResourceApplier.TotalEventGoldOut,
            // ⚠️ **D-110**：村庄「可花却没花」的钱（采集器不召唤 ⇒ 钱只会囤）。
            // 口径 = 村库现金 + 村庄资产 − 下一级升级要留的钱。
            UnspentVillageGold(),
            // ⚠️ **D-114**：村庄的开局启动资金（**从外面进来的钱**）
            // ⇒ 守恒律的右边必须加上它，否则账对不上。
            ClaudeCodeGameStudios.Application.VillageStart.StartingGold);

        // ⚠️ **逐日守恒律自检**（D-091 引入的记账多了一条路 ⇒ 必须自己盯住）：
        // 「Σ英雄钱包 + 村库金币 + 村庄资产 + 累计销毁 = 初始金币 + 累计发放」
        // 对不上 ⇒ **当场打印**，而不是等标定脚本事后才发现。
        // ⇒ 这一行是「账实不符」的唯一现场证据。
        long lhs = heroGold + VillageGold
                 + _villageModel.Get(ClaudeCodeGameStudios.Application.VillageUpgradeService.Asset)
                 + destroyed
                 // ⚠️ **D-101**：被大事件带走的金币（流出系统的）⇒ 算在左边。
                 + ClaudeCodeGameStudios.Application.WorldEventResourceApplier.TotalEventGoldOut;
        long rhs = (long)_heroes.Count * 100 + _walletWriter.TotalGranted
                   // ⚠️ **D-101**：大事件带来的金币（从外面进来的）⇒ 算在右边。
                   + ClaudeCodeGameStudios.Application.WorldEventResourceApplier.TotalEventGoldIn
                   // ⚠️ **D-114**：村庄的开局启动资金（也是从外面进来的）⇒ 算在右边。
                   + ClaudeCodeGameStudios.Application.VillageStart.StartingGold;
        if (lhs != rhs)
        {
            GD.Print($"CONSERVATION 第{(int)_clock.Day}天 对不上：左边 {lhs} / 右边 {rhs}"
                     + $"（差 {lhs - rhs}）· 英雄 {heroGold} · 村库 {VillageGold}"
                     + $" · 资产 {_villageModel.Get(ClaudeCodeGameStudios.Application.VillageUpgradeService.Asset)}"
                     + $" · 销毁 {destroyed} · 发放 {_walletWriter.TotalGranted}"
                     + $" · 升级 {_upgradeService.UpgradesStarted}"
                     + $" · 启动资金 {ClaudeCodeGameStudios.Application.VillageStart.StartingGold}"
                     + $" · 事件收入 {ClaudeCodeGameStudios.Application.WorldEventResourceApplier.TotalEventGoldIn}"
                     + $" · 事件支出 {ClaudeCodeGameStudios.Application.WorldEventResourceApplier.TotalEventGoldOut}");
        }
    }

    /// <summary>取有序数组的**线性插值**分位数（q ∈ [0,1]）。</summary>
    /// <remarks>
    /// 用「最近秩」会跳格（37 人时 p25 只能取到第 10 个或第 9 个），
    /// 线性插值给出的数更接近直觉，**同一份数据的分位数也更稳定**。
    /// </remarks>
    private static long Percentile(List<long> sorted, double q)
    {
        if (sorted.Count == 0) return 0L;
        if (sorted.Count == 1) return sorted[0];

        double pos = q * (sorted.Count - 1);
        int lo = (int)Math.Floor(pos);
        int hi = (int)Math.Ceiling(pos);
        if (lo == hi) return sorted[lo];

        double frac = pos - lo;
        return (long)Math.Round(sorted[lo] + (sorted[hi] - sorted[lo]) * frac);
    }

    /// <summary>
    /// <c>--econlog</c>：每次每日购物跑完后，把<b>逐人</b>结果（成交 / 跳过 / 失败及原因）
    /// 打到控制台。第四阶段 M5 的「自主行为结果可调试验证」。
    /// </summary>
    private readonly bool _econLog = OS.GetCmdlineUserArgs().Contains("--econlog");

    /// <summary>最近一次每日购物的逐人结果（<c>--shop</c> 明细输出用）。</summary>
    private IReadOnlyList<ClaudeCodeGameStudios.Application.Stall.HeroBehaviorService.ShoppingResult>
        _lastShoppingResults = Array.Empty<ClaudeCodeGameStudios.Application.Stall.HeroBehaviorService.ShoppingResult>();

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

        // R-05 三轮（D-053）：把「想买没买成」的原因**落进经济流水**。
        //
        // **不能走事件**：未成交没有状态变化（钱没动、货没动、库存没动），
        // 而 DomainEvent 构造期就会拒绝「声明不出状态变化」的事件
        // （「不改变任何状态的事件按定义就是装饰」）。这里是**读数**，不是事件。
        //
        // 不记就没有分母 —— R-05 二轮发现的「成交笔数腰斩」，
        // 根因一直只能靠代码推断，因为流水里根本没记未成原因。
        RecordShoppingMissed(results);

        // R-07：每天一条「想买没买成」的汇总。
        //
        // 这条**没有改变任何状态** —— 它是「今天有多少人没买成、卡在哪儿」的**读数**，
        // 不是一次事件。所以按反装饰规则**不发事件**，直接进杂谈
        // （与「摆摊失败」「副本结算被拒」同一条先例：都是没改状态、但值得说一声）。
        //
        // **每天只有一条**，且**不点名** —— 37 个人名进杂谈就是刷屏；
        // 名字本来就在花名册行尾上。
        var summary = ClaudeCodeGameStudios.Application.Stall.ShoppingFeedback.MissedSummary(results);
        if (summary is not null)
        {
            Add(new Chronicle.Entry((int)_clock.Day, summary, null));
        }
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
    /// 把「想买没买成」的原因写进经济流水（<b>每英雄每原因一行</b>）。
    /// </summary>
    /// <param name="results">当天的逐人购物结果。</param>
    /// <remarks>
    /// <para><b>只记「值得给玩家看」那几类</b>：口径与 R-07 的花名册行尾、每日汇总
    /// <b>同一份规则</b>（<c>ShoppingFeedback.IsWorthShowing</c>）——
    /// 钱不够 / 背包满 / 没成交 / 只有自己的摊。
    /// 「今天不想买 / 不在场 / 已经买过 / 村里没摊」是<b>噪音</b>，
    /// 记进去会让分母失真，结论就废了。</para>
    ///
    /// <para><b>为什么必须落盘</b>：R-05 二轮采集发现成交笔数腰斩，
    /// 根因却只能靠代码推断 —— 因为流水里<b>没记未成原因</b>。
    /// 本项补上这个测量缺口，让「背包满占未成原因多少」从<b>假设</b>变成<b>实测</b>。</para>
    /// </remarks>
    private void RecordShoppingMissed(
        IReadOnlyList<ClaudeCodeGameStudios.Application.Stall.HeroBehaviorService.ShoppingResult> results)
    {
        if (_economyLog is null) return;

        foreach (var result in results)
        {
            if (!ClaudeCodeGameStudios.Application.Stall.ShoppingFeedback.IsWorthShowing(result.SkipReason))
            {
                continue;
            }
            if (result.Buyer is not { } buyer) continue;

            _economyLog.AppendShoppingMissed(
                _clock.Now,
                NameOfHero(buyer),
                result.SkipReason.ToString(),
                result.Seller is { } seller ? NameOfHero(seller) : null,
                result.ItemId?.Value,
                result.BuyerGold,
                result.CheapestPrice);
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

    /// <summary>
    /// 发一条「副本战利品已结算」事件（可落盘；不进杂谈）。
    /// </summary>
    /// <param name="outcome">副本结果。</param>
    /// <param name="loot">战利品分配结果。</param>
    /// <remarks>
    /// <para><b>「掉落清单」由分配结果汇总而来</b>：分配是<b>数量守恒</b>的
    /// （R-01 的 <c>LootSplit</c> 保证总量不变），所以按物品汇总回去
    /// 就是掉落前的原始清单 —— 不必为此改 <c>ComputeLoot</c> 的签名。</para>
    ///
    /// <para><b>紧凑格式</b>（<c>石料×2,木料×1</c> / <c>h-0:石料×2</c>）：
    /// 事件的 payload 是 <c>string→string</c>，放不进嵌套结构；
    /// 格式与解析器都住在 <see cref="ClaudeCodeGameStudios.Data.EconomyLog"/>，
    /// **只有一处定义**，不会出现「写的和读的不是一套」。</para>
    /// </remarks>
    private void EmitDungeonLooted(
        ClaudeCodeGameStudios.Application.DungeonFlowOutcome outcome,
        ClaudeCodeGameStudios.Domain.Settlement.LootAllocation loot)
    {
        if (loot.Grants.Count == 0) return;

        var lootLines = loot.Grants
            .GroupBy(g => g.Item)
            .Select(g => new ClaudeCodeGameStudios.Data.LootLine(
                g.Key.Value, g.Sum(x => x.Quantity)))
            .ToArray();

        var grantLines = loot.Grants
            .Select(g => new ClaudeCodeGameStudios.Data.GrantLine(
                g.Hero.Value, g.Item.Value, g.Quantity))
            .ToArray();

        var participants = outcome.Run.Participants.ToArray();

        Emit(EvDungeonLooted, outcome.Run.Id.Value, new[] { StHeroBag },
            new Dictionary<string, string>
            {
                ["runId"] = outcome.Run.Id.Value,
                ["place"] = DungeonName,
                ["won"] = outcome.Succeeded ? "1" : "0",
                ["participants"] = ClaudeCodeGameStudios.Data.EconomyLog.FormatIds(participants),
                ["loot"] = ClaudeCodeGameStudios.Data.EconomyLog.FormatLoot(lootLines),
                ["grants"] = ClaudeCodeGameStudios.Data.EconomyLog.FormatGrants(grantLines),
            },
            _ => null);   // 不进杂谈 —— 见 EvDungeonLooted 的说明
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
}
