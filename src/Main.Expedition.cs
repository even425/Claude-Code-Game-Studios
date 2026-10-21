using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using ClaudeCodeGameStudios.Application;
using ClaudeCodeGameStudios.Domain.Heroes;
using ClaudeCodeGameStudios.Domain.Quests;
using DomainVillage = ClaudeCodeGameStudios.Domain.Village.Village;
using ResourceId = ClaudeCodeGameStudios.Domain.Village.ResourceId;

/// <summary>
/// 主场景的<b>副本与结算</b>部分（A1 拆分：把「战斗执行 + 结算入账」两类职责收在一处）。
///
/// <para>⚠️ **这是纯代码搬移** —— 逻辑一行没改，只是从 <c>Main.cs</c> 整块搬到这里。
/// 它之所以单独成文件，是因为它<b>同时拥有</b> UT-01 四类职责里的两类
/// （战斗执行 + 结算入账）；再往里塞第三类就违反结构断言了。</para>
/// </summary>
public partial class Main : Node2D
{
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
    /// 本次结算的奖励：村庄资源 + <paramref name="loot"/> 里的个人战利品
    /// + （副本时）每位实际参战英雄的金币。
    /// </summary>
    /// <param name="heroesWon">战斗是否胜利。</param>
    /// <param name="loot">本次副本的战利品分配（由调用方随这次结算带进来）。</param>
    /// <param name="participants">
    /// 本次副本的<b>实际参战英雄</b>（快照）。<b>null</b> = 与副本无关的结算 → 不发金币。
    /// </param>
    /// <remarks>
    /// <b>三半奖励，一次结算</b>（R-01 + D-054-01）：村庄资源、英雄个人物品、
    /// 英雄个人金币被放进<b>同一个增量</b>，于是 <c>Settlement.Apply</c> 能用
    /// <b>同一把幂等锁</b>把它们一起落下去 —— 中间任何一步失败都不会留下
    /// 「村庄拿到了、英雄没拿到」的半笔账。
    /// <br><b>但三者不总是同时发生</b>：金币<b>失败也发</b>（D-054-01 裁决 ①），
    /// 材料与战利品仍<b>失败不发</b>（D-010 Q1，见该条原处的加注）。
    /// </remarks>
    private ClaudeCodeGameStudios.Domain.Settlement.SettlementDelta RewardsFor(
        bool heroesWon,
        ClaudeCodeGameStudios.Domain.Settlement.LootAllocation loot,
        IReadOnlyList<string>? participants = null)
    {
        var delta = new ClaudeCodeGameStudios.Domain.Settlement.SettlementDelta();

        // ── 英雄个人：金币（D-054-01）──
        //    **失败也发** —— 这是 D-054 引入的**唯一一项「失败也发」的奖励**。
        //    参战名单用副本实例的**快照**（D-047 D02）：结算必须对应「谁去了」，
        //    而不是「读档瞬间队伍里还有谁」。
        //    金额不在这里给：报酬率 X 是**规则**，住在写入入口里
        //    （`WalletGrantService` 负责按英雄分别累计零头）。
        if (_goldRewardUnits > 0 && participants is not null)
        {
            foreach (var id in participants)
            {
                if (string.IsNullOrEmpty(id)) continue;
                delta.AddWalletGrant(new HeroId(id));
            }
        }

        // 失败不发**材料与战利品**（D-010 Q1「失败不给奖励」）。
        // 注意这里<b>照常返回一个（可能含金币的）增量</b>而不是抛错 ——
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
        => _villageWriter.Write(writes);

    /// <summary>
    /// 村庄资源的整笔写入入口（<b>I15</b> 的村庄侧）。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么它是个服务而不是本类里的私有方法</b>：不变量 I15
    /// 「结算要么完整应用，要么不应用」的<b>村庄那一半</b>就在这个写入里 ——
    /// 而它以前是私有方法 ⇒ 集成测试<b>够不着</b> ⇒ 只能验一个桩，
    /// 桩的原子性是自己写给自己的，<b>证明不了生产代码的原子性</b>。
    /// 抽成服务后，IT-13 能在<b>真服务</b>上验「村庄资源与英雄持有物的总增量
    /// 要么全为 0，要么等于完整增量」。</para>
    /// </remarks>
    private ClaudeCodeGameStudios.Application.VillageResourceWriter _villageWriter = null!;

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

    /// <summary>存入材料。非正增量<b>什么都不做</b>（<see cref="DomainVillage.Add"/> 要求增量为正）。</summary>
    private void Deposit(long amount)
    {
        if (amount > 0) _villageModel.Add(MaterialsId, amount);
    }

    /// <summary>一个任务。Hours 是**游戏内小时**——北境旧堡要跑一整天。
    /// Kind 决定它算「粗活」还是「有对手的活」，影响傲的接不接。</summary>
    private readonly record struct Job(string Name, string Res, int Amt, float Hours, bool Glory);

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

        // R-05：整条管线（派生子流 → 抽取 → 分配）住在 `LootPipeline` 里 ——
        // 它是**抽取与分配共用同一条随机流**的那处接缝，必须有测试盯住。
        //
        // 为什么共用一条：同一个副本标识 → 同一个种子 → 同样的抽取顺序 →
        // 同样的掉落 + 同样的分配。读档后重放得到完全一样的结果。
        // 为什么要独立于全局流：掉落若从 Main._rng 抽，会**插队** ——
        // 一次副本掉落会改变此后所有战斗与日常的结果。
        return ClaudeCodeGameStudios.Domain.Settlement.LootPipeline.Run(
            _lootProvider, DungeonName, participants, outcome.Run.Id.Value);
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

        // 参战队伍的**状态快照** —— 战斗解算能看到「谁在场、当时什么情况」。
        // ⚠️ 它与上面的标识名单**必须来自同一份名单**（任务成员 / 随队者），
        // 否则战斗看到的队伍与结算认定的队伍就不是同一批人。
        var party = new ClaudeCodeGameStudios.Domain.Combat.CombatPartySnapshot(
            (_mission is not null
                ? _mission.Members.Select(m => m.Id.Value)
                : crew.Select(h => h.Id.Value))
            .Select(id =>
            {
                var st = _states.FirstOrDefault(s => s.Definition.Id.Value == id);
                // 战斗属性：按**性格 × 投入度**换算（D-060 / D-065）。
                // ⚠️ 它在**英雄个体**上（`HeroAgent`），不在静态定义里 ⇒ 要按标识找那个人；
                // 找不到 ⇒ 属性「不明」⇒ 战斗会**大声失败**（而不是让一个没有战斗属性的人参战）。
                var agent = crew.FirstOrDefault(h => h.Id.Value == id)
                         ?? _heroes.FirstOrDefault(h => h.Id.Value == id);
                var stats = agent is null
                    ? ClaudeCodeGameStudios.Domain.Combat.CombatStats.Unknown
                    : ClaudeCodeGameStudios.Domain.Combat.CombatParameters.ForHero(
                        agent.Personality, agent.Diligence);
                return ClaudeCodeGameStudios.Domain.Combat.CombatantSnapshot.Create(
                    id,
                    st?.Definition.Name ?? id,
                    st?.Definition.TraitModifiers,
                    st?.Definition.PreferenceWeights,
                    stats);
            })
            .ToArray());

        var flow = ClaudeCodeGameStudios.Application.DungeonFlow.Begin(
            new ClaudeCodeGameStudios.Domain.Dungeon.DungeonRunId(NextDungeonRunId()),
            DungeonName, ids, _combat, _combat, party: party);

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
        //
        // 【经济数据采集】补 runId + 参战名单：这条**每场副本都发**（成功失败都发），
        // 落盘后就能算出**副本成功率** —— 而战利品只在成功时才有，
        // 只记掉落的话失败场次永远采不到，成功率就无从谈起。
        // 判据是「有 runId」：同样叫 DungeonSettled 的另外几条（队伍归来、
        // 带回战利品、白跑一趟）没有 runId，落盘时会自然被跳过。
        Emit(EvDungeonSettled, DungeonName, new[] { StDungeonRun, StSettlement },
            new Dictionary<string, string>
            {
                ["place"] = DungeonName,
                ["won"] = outcome.Succeeded ? "1" : "0",
                ["names"] = string.Join("、", names),
                ["runId"] = outcome.Run.Id.Value,
                ["participants"] = ClaudeCodeGameStudios.Data.EconomyLog.FormatIds(
                    outcome.Run.Participants.ToArray()),
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
            granted = _settlement.Apply(key, outcome.Succeeded,
                won => RewardsFor(won, loot, outcome.Run.Participants));
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

            // 运行闭环验证与经济数据采集准备：把「这次副本产出了什么、分给了谁」
            // 记成一条**可落盘**的事件。
            //
            // 为什么必须有它：掉落原先只存在于内存事件里，存一次档就全没了 ——
            // 经济流水无从采集，R-05 二轮的掉落分布也就无从谈起。
            //
            // 它**不进杂谈**（渲染委托返回 null，与「日常劳作」同一路）：
            // 37 人每天一两条「某某捡到了石料」会把村庄杂谈淹掉。
            EmitDungeonLooted(outcome, loot);

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
}
