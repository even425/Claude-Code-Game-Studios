using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using ClaudeCodeGameStudios.Application;
using ClaudeCodeGameStudios.Domain.Heroes;
using ClaudeCodeGameStudios.Domain.Quests;
using ClaudeCodeGameStudios.Domain.Stall;
using ResourceId = ClaudeCodeGameStudios.Domain.Village.ResourceId;

/// <summary>
/// 主场景的<b>存档读写</b>部分（A1 拆分：把「存档写入」这一类职责收在一处）。
///
/// <para>⚠️ **这是纯代码搬移** —— 逻辑一行没改。存档是 UT-01 四类职责之一，
/// 单独成文件后它就<b>只占一类</b>，不会再和战斗 / 结算挤在一起。</para>
/// </summary>
public partial class Main : Node2D
{
    /// <summary>
    /// 造一个<b>自检专用</b>的存档端口（默认不碰玩家存档）。
    /// </summary>
    /// <returns>存档端口。</returns>
    private ClaudeCodeGameStudios.Data.SavePort SelfTestSaves()
    {
        var path = OS.GetCmdlineUserArgs()
            .FirstOrDefault(a => a.StartsWith("--savepath=", StringComparison.Ordinal))?
            .Substring("--savepath=".Length)
            ?? $"user://{SelfTestSaveFileName}";

        path = Godot.ProjectSettings.GlobalizePath(path);
        return new ClaudeCodeGameStudios.Data.SavePort(path);
    }

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
        // D-054 阶段 4（存档 0.6）：守恒律的「累计新增」与「已销毁」两项。
        // 不存它们，读档后账就对不上。
        GoldGrantedTotal = _walletWriter.TotalGranted,
        GoldSpentTotal = _upgradeService.TotalGoldSpent,
        VillageUpgrades = _upgradeService.UpgradesStarted,
        // 村庄层级（**顺带修既有缺陷**：本来不入档，读档后回到 0 级而钱已经花了）
        VillageTier = _tier,
        // 存的是随机源状态，不是种子：只存种子的话，读档后只能回到开局那一刻，
        // 而不是回到存档那一刻。
        RandomState = _rng.State.ToString(),
        Village = new ClaudeCodeGameStudios.Data.VillageSave
        {
            Id = _villageModel.Id,
            Resources = new Dictionary<string, long>(_villageModel.Snapshot()),
            // R-05 第二轮 / D-052：村庄税额余数（0.5 新增）。
            // 它是「已经向买家收走、还没凑够 1 金」的钱，必须跟着存档走。
            VillageCutRemainder = _stallService.VillageCutCarry,
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
                // D-054 阶段 4（存档 0.6）：该英雄的**待发零头**（单位 1/100 金）。
                // 不存它 = 已挣到但还没发出去的钱凭空消失。
                GoldCarry = _walletWriter.PendingOf(h.Id),
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
        // D-079：大事件实例 + **已应用的后果键**（幂等依据）。
        // 不存它 ⇒ 读档后重放结算会把后果**再应用一次**（基线 §11.6 V2）。
        WorldEvents = _worldEvents.Snapshot().Select(e => new ClaudeCodeGameStudios.Data.WorldEventSave
        {
            Id = e.Id,
            TemplateId = e.TemplateId,
            Phase = (int)e.Phase,
            StartedAt = e.StartedAt,
            Progress = e.Progress,
            Participants = e.Participants.ToList(),
            AppliedConsequences = e.AppliedConsequences.OrderBy(k => k, StringComparer.Ordinal).ToList(),
            Succeeded = e.Outcome?.Succeeded,
            // D-081：实例状态（商队还剩多少货）—— **不存它，读档就能白嫖**。
            State = e.State.ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal),
        }).ToList(),
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

        // I1：读档后**重建 Id 分配器** —— 它必须知道存档里已经占了哪些号，
        // 否则下一个召唤可能发出一个已被占用的号（两个英雄同号 ⇒ 读档时
        // 两个状态映射到同一个实体）。分配器只拿「已占用的号」，不碰花名册。
        _heroIds = new ClaudeCodeGameStudios.Application.HeroIdAllocator(
            save.Heroes.Select(h => h.Id));

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
            // D-054 阶段 4（存档 0.6）：恢复该英雄的**待发零头**。
            // 不恢复它 = 已挣到但还没发出去的钱凭空消失（与税额余数同一条理由）。
            _walletWriter.RestorePending(new ClaudeCodeGameStudios.Domain.Heroes.HeroId(hs.Id), hs.GoldCarry);
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

        // R-05 第二轮 / D-052：村庄税额余数（0.5 新增）。
        // 校验器已保证它在 [0, 99]；这里再走一次带检查的入口 ——
        // 存档被外部改坏时，宁可拒绝读档，也不要带一个非法余数进运行时。
        _stallService.RestoreVillageCutCarry(save.Village.VillageCutRemainder);

        // 每日购物闸门：读档回到存档那天，同一天不应再跑一遍尝试。
        _dailyShopping.RestoreLastRunDay(save.DailyShoppingLastRunDay);

        // D-079：大事件实例（跨日进行中的 + 已应用的后果键）。
        // ⚠️ **必须在事件簿已建好之后**（`InitWorldEvents` 在 `_Ready` 里跑）
        // ⇒ 读档入口要在 `_Ready` 之后 ⇒ 这里是安全的。
        RestoreWorldEvents(save.WorldEvents);

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

        // ── D-054 阶段 4（存档 0.6）：货币侧的三个状态 ──
        // ① 累计发放 / 累计销毁（守恒律的两项；不恢复就读档后账对不上）
        _walletWriter.RestoreTotalGranted(save.GoldGrantedTotal);
        _upgradeService.RestoreTotals(save.GoldSpentTotal, save.VillageUpgrades);
        // ② 村庄层级（**顺带修既有缺陷**：本来不入档 → 读档后回到 0 级，而钱已经花了）
        _tier = save.VillageTier;
        _village.SetTier(_tier);
        _buildProgress = 0f;
        _buildTarget = -1;

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
}
