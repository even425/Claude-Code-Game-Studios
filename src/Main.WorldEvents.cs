using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using ClaudeCodeGameStudios.Domain.WorldEvents;

public partial class Main : Node2D
{
    /// <summary>大事件簿（D-079：通用事件框架 + 兽潮 + 商队来访）。</summary>
    private ClaudeCodeGameStudios.Domain.WorldEvents.WorldEventBook _worldEvents = null!;

    /// <summary>本次大事件用掉的实例标识序号（存档要能重建它）。</summary>
    private long _worldEventSeq = 0;

    /// <summary>上次推进大事件阶段时的游戏日（一个游戏日只推一次）。</summary>
    private int _worldEventLastDay = -1;

    /// <summary>大事件簿（D-079）。</summary>
    private void InitWorldEvents()
    {
        _worldEvents = new ClaudeCodeGameStudios.Domain.WorldEvents.WorldEventBook();

        // ⚠️ **两个实例刻意形状不同**（基线 §11.5）：兽潮有战斗、阶段多；
        // 商队**没有战斗**、阶段少 ⇒ 它是「框架是通用的」的可验证证据。
        //
        // ⚠️ **兽潮用自己的一份解算器**（D-080）：它的敌方规模**随村庄层级递增**
        // ⇒ 同一个阶段序号，不同层级要打不同强度的怪 ⇒ 解算器得知道层级。
        // （副本那边的那份**一行都没动** —— 默认行为不变。）
        var beastTideCombat = new ClaudeCodeGameStudios.Application.FormulaCombat(
            monsterStats: stage => ScaleBeastTideMonster(
                ClaudeCodeGameStudios.Domain.Combat.CombatParameters.MonsterFor(stage), _tier));

        _worldEvents.Register(ClaudeCodeGameStudios.Application.WorldEventTemplates.BeastTide(
            periodDays: WorldEventBeastTidePeriodDays, beastTideCombat, beastTideCombat,
            heroStats: BeastTideHeroStats,
            tuning: () => BeastTideTuningBase.ScaledToTier(_tier)));
        _worldEvents.Register(ClaudeCodeGameStudios.Application.WorldEventTemplates.Caravan(
            periodDays: WorldEventCaravanPeriodDays, CaravanOfferBase));

        // D-081：商队市场（开摊 / 在售列表 / 成交）。
        // ⚠️ **延迟取实例**（委托而不是直接持有簿）：本方法跑在 `BuildScene` 之后，
        // 而簿就是这里才建的 ⇒ 直接持有没问题，但**委托更省心**：
        // 将来读档整份替换簿，委托照样指向同一个字段。
        _caravanMarket = new ClaudeCodeGameStudios.Application.CaravanMarketService(
            findInstance: id => _worldEvents?.Instances
                .FirstOrDefault(i => string.Equals(i.Id, id, StringComparison.Ordinal)),
            listInstances: () => _worldEvents?.Instances
                ?? (IReadOnlyList<WorldEventInstance>)Array.Empty<WorldEventInstance>(),
            prices: _priceBook,
            village: _villageModel,
            heroLookup: id => _heroes.FirstOrDefault(h => h.Id == id));
    }

    /// <summary>商队市场（D-081）。</summary>
    private ClaudeCodeGameStudios.Application.CaravanMarketService _caravanMarket = null!;

    /// <summary>兽潮的触发周期（游戏内天数）。</summary>
    private const int WorldEventBeastTidePeriodDays = 7;

    /// <summary>商队来访的触发周期（游戏内天数）。</summary>
    private const int WorldEventCaravanPeriodDays = 11;

    /// <summary>
    /// 兽潮的<b>基准数值</b>（层级 0 时用这些；层级更高时按
    /// <see cref="BeastTideTuning.ScaledToTier"/> 放大）。
    /// </summary>
    /// <remarks>
    /// ⚠️ **这些数是开发者拍板的**（D-080）：材料 40 ≈ 一次副本的材料量；
    /// 输的数**刻意比赢的小得多**（15 / 8）—— 实测发现**弱阵容（挂机）无论缩放多少都守不住**
    /// ⇒ 若损失够重，弱村子会**永久失血** ⇒ 失败代价必须温和。
    /// </remarks>
    private static readonly ClaudeCodeGameStudios.Application.BeastTideTuning BeastTideTuningBase =
        new()
        {
            MaterialReward = WorldEventBeastTideMaterialReward,
            GoldReward = WorldEventBeastTideGoldReward,
            MaterialLoss = WorldEventBeastTideMaterialLoss,
            GoldLoss = WorldEventBeastTideGoldLoss,
        };

    /// <summary>兽潮打赢给的材料（基准值；按村庄层级放大）。</summary>
    private const long WorldEventBeastTideMaterialReward = 40;

    /// <summary>兽潮打赢给的金币（基准值；按村庄层级放大）。</summary>
    private const long WorldEventBeastTideGoldReward = 20;

    /// <summary>兽潮打输赔的材料（基准值；按村庄层级放大）。</summary>
    private const long WorldEventBeastTideMaterialLoss = 15;

    /// <summary>兽潮打输赔的金币（基准值；按村庄层级放大）。</summary>
    private const long WorldEventBeastTideGoldLoss = 8;

    /// <summary>
    /// 兽潮敌方的规模缩放：<b>每级村庄 +8%</b>（D-080 拍板「随村庄层级递增」）。
    /// </summary>
    /// <remarks>
    /// ⚠️ **只缩放属性，不改怪的档位**（普通 / 精英 / 首领）⇒ 兽潮的「形状」不变，
    /// 只是更强 ⇒ 与「规模递增」而不是「换一种怪」相符。
    /// <para>⚠️ **8% 是实测出来的**（不是拍脑袋）：兽潮 = 三阶段逐场打、30 回合超时判负。
    /// 实测 3 人阵容：8% ⇒ 常规阵容到 5 级都守得住（精英阶段已 26–28 回合，逼近上限）；
    /// 10% ⇒ 5 级时中等阵容**超时判负**；15% ⇒ 4 级就守不住 ⇒ 村子被自己的发展卡死。</para>
    /// </remarks>
    private const int WorldEventBeastTideScalePercentPerTier = 8;

    /// <summary>商队的报价（D-080：低于市价、限量、限时）。</summary>
    private static readonly ClaudeCodeGameStudios.Application.CaravanOffer CaravanOfferBase =
        new(ItemId: "石料",
            DiscountPercent: WorldEventCaravanDiscountPercent,
            Stock: WorldEventCaravanStock);

    /// <summary>商队的折扣（百分比；80 = 八折 = 低于市价）。</summary>
    private const int WorldEventCaravanDiscountPercent = 80;

    /// <summary>商队带多少货（份）—— 限量是「不产生无限套利」的前提。</summary>
    private const long WorldEventCaravanStock = 30;

    /// <summary>
    /// 按村庄层级把兽潮的怪物属性放大。
    /// </summary>
    /// <param name="baseStats">该阶段的基础属性（档位表里的值）。</param>
    /// <param name="tier">村庄层级（0 起）。</param>
    /// <returns>放大后的属性。</returns>
    private static ClaudeCodeGameStudios.Domain.Combat.CombatStats ScaleBeastTideMonster(
        ClaudeCodeGameStudios.Domain.Combat.CombatStats baseStats, int tier)
        => ClaudeCodeGameStudios.Application.BeastTideScale.ScaleMonster(
            baseStats, tier, WorldEventBeastTideScalePercentPerTier);

    /// <summary>
    /// 守村英雄的战斗属性 —— <b>用他自己的</b>（D-080）。
    /// </summary>
    /// <remarks>
    /// ⚠️ 之前这里是<b>写死的 6/2/60</b> ⇒ 谁去守村、村里有没有强者，
    /// 对兽潮的胜负**毫无影响** ⇒ 「守村」这件事是假的。
    /// ⇒ 现在走英雄自己的属性（与副本那边同一套换算，含试炼塔的永久加生命）。
    /// </remarks>
    /// <param name="heroId">英雄标识。</param>
    /// <returns>战斗属性。</returns>
    /// <exception cref="InvalidOperationException">花名册里没有这个人（状态被改坏了）。</exception>
    private ClaudeCodeGameStudios.Domain.Combat.CombatStats BeastTideHeroStats(string heroId)
    {
        var agent = _heroes.FirstOrDefault(h => h.Id.Value == heroId);
        if (agent is null)
        {
            throw new InvalidOperationException(
                $"兽潮参与者「{heroId}」不在花名册里 —— 状态被改坏了。");
        }

        return CombatStatsFor(agent);
    }

    /// <summary>
    /// 每天推进一次大事件（D-079）。
    /// </summary>
    /// <remarks>
    /// <para><b>触发</b>：到了模板的周期就建实例（基线 §11.1「按游戏内周期触发」）。</para>
    /// <para><b>推进</b>：实例按<b>模板声明的阶段</b>往前走；走到「结算」阶段就结算一次
    /// （幂等：已应用的后果不会应用第二次）。</para>
    /// <para>⚠️ <b>一帧只推一次</b>（按游戏日闸门）——
    /// 否则 60 帧/秒会把事件在一天内推完。</para>
    /// </remarks>
    private void TickWorldEvents()
    {
        if (_worldEvents is null) return;

        int today = (int)_clock.Day;
        if (_worldEventLastDay == today) return;
        _worldEventLastDay = today;

        // ① 触发：周期到了就建一个实例。
        var instance = _worldEvents.TriggerByPeriod(
            today, _clock.Now,
            participants: _states.Select(s => s.Id.Value).ToArray(),
            instanceIdFactory: (template, day) =>
            {
                _worldEventSeq++;
                return $"{template.Id}@{day}.{_worldEventSeq}";
            });

        if (instance is not null)
        {
            var template = _worldEvents.Template(instance.TemplateId);

            // D-081：商队实例一触发就**开摊**（算单价、锁库存进实例状态）。
            // ⚠️ **限时不在这里管**：窗口 = 实例处于「进行中」阶段 ⇒ 阶段机一到结算就自动关窗。
            if (instance.TemplateId == ClaudeCodeGameStudios.Application.WorldEventTemplates.CaravanId)
            {
                if (!_caravanMarket.OpenStall(instance, CaravanOfferBase))
                {
                    GD.Print($"WORLDEVENT 商队没开摊：「{CaravanOfferBase.ItemId}」不在价格表里。");
                }
            }

            Emit(EvWorldEventStarted, template?.Name ?? instance.TemplateId,
                new[] { StWorldEvent },
                new Dictionary<string, string>
                {
                    ["event"] = template?.Name ?? instance.TemplateId,
                    ["template"] = instance.TemplateId,
                    ["phase"] = instance.Phase.ToString(),
                    ["instance"] = instance.Id,
                },
                ev => Chronicle.WorldEventOmen((int)_clock.Day, ev.Payload["event"]).Text);
        }

        // ② 推进：每个活跃实例往前一步。
        foreach (var active in _worldEvents.ActiveInstances.ToArray())
        {
            // 「预兆」要多待几天（玩家要能读到预告），其余阶段一天一步。
            bool isOmen = active.Phase == WorldEventPhase.Omen;
            int omenDays = active.Progress switch
            {
                0.0 => 0,          // 刚创建：这次不动
                _ => 1,
            };
            if (isOmen && omenDays == 0) continue;

            var moved = _worldEvents.Advance(active.Id, progress: 0.5);
            if (moved is null) continue;

            // ③ 结算：走到「结算」阶段就结一次。
            if (moved.Phase == WorldEventPhase.Settlement)
            {
                // 后果**实际落地**的说明（不是模板写的「打算给多少」——
                // 扣不动的时候实际扣的数不一样，杂谈里要写真数）。
                var applied = new List<string>();

                var outcome = _worldEvents.Settle(moved.Id, (key, effect) =>
                {
                    applied.Add(ApplyWorldEventEffect(effect));
                });
                if (outcome is not null)
                {
                    var template = _worldEvents.Template(moved.TemplateId);
                    var notes = applied.Count > 0
                        ? applied
                        : outcome.Consequences.Values.Select(c => c.Description).ToList();

                    Emit(EvWorldEventSettled, template?.Name ?? moved.TemplateId,
                        new[] { StWorldEvent, StSettlement },
                        new Dictionary<string, string>
                        {
                            ["event"] = template?.Name ?? moved.TemplateId,
                            ["succeeded"] = outcome.Succeeded ? "1" : "0",
                            ["instance"] = moved.Id,
                            ["consequences"] = string.Join(" / ", notes),
                        },
                        ev => Chronicle.WorldEventSettled(
                            (int)_clock.Day, ev.Payload["event"],
                            ev.Payload["succeeded"] == "1").Text);
                }
            }
        }
    }

    /// <summary>
    /// 把一条大事件后果<b>应用出去</b>（基线 §11.4：事件系统不越权改数据）。
    /// </summary>
    /// <remarks>
    /// ⚠️ **这里只落「村庄资源」这一种后果**（D-080 拍板：兽潮只损资源、人畜无损）。
    /// 资源写进 <b>村库</b>（材料桶 / 金币桶），走 <c>Village</c> 自己的入口
    /// ⇒ 账是同一本账（守恒律照样成立）。
    /// </remarks>
    /// <param name="effect">结构化后果（种类 + 数量 + 说明）。</param>
    /// <returns><b>实际落地</b>的说明（给人看；数字是实际发生的）。</returns>
    private string ApplyWorldEventEffect(WorldEventEffect effect)
    {
        switch (effect.Kind)
        {
            case WorldEventEffect.KindMaterial:
                return ApplyVillageResourceChange(MaterialsId, effect, "材料");
            case WorldEventEffect.KindGold:
                return ApplyVillageResourceChange(
                    ClaudeCodeGameStudios.Application.Stall.StallService.Gold, effect, "金币");
            default:
                return effect.Description;
        }
    }

    /// <summary>
    /// 村库资源的加/减（正 = 给，负 = 扣）。
    /// </summary>
    /// <remarks>
    /// ⚠️ <b>扣不动就只扣有的</b>：村库里的材料/金币不够时，
    /// **扣到 0 为止**，**不欠账**（没有「负债」这个状态）——
    /// 否则下一次结算就会凭空扣掉负数，或者让村库变成负数。
    /// </remarks>
    private string ApplyVillageResourceChange(
        ClaudeCodeGameStudios.Domain.Village.ResourceId id,
        WorldEventEffect effect,
        string label)
        => ClaudeCodeGameStudios.Application.WorldEventResourceApplier
            .Apply(_villageModel, id, effect, label).Description;

    /// <summary>大事件簿的存档（读档用）。</summary>
    private void RestoreWorldEvents(IEnumerable<ClaudeCodeGameStudios.Data.WorldEventSave> saves)
    {
        var instances = new List<WorldEventInstance>();
        foreach (var save in saves ?? Array.Empty<ClaudeCodeGameStudios.Data.WorldEventSave>())
        {
            var instance = new WorldEventInstance
            {
                Id = save.Id,
                TemplateId = save.TemplateId,
                StartedAt = save.StartedAt,
                Progress = save.Progress,
                Participants = save.Participants.ToArray(),
            };
            instance.Phase = (WorldEventPhase)save.Phase;
            foreach (var key in save.AppliedConsequences) instance.MarkApplied(key);
            // D-081：实例状态（商队还剩多少货）—— **不读回来，读档就能白嫖**。
            foreach (var (key, value) in save.State) instance.SetState(key, value);
            if (save.Succeeded.HasValue)
            {
                instance.Outcome = new WorldEventOutcome(
                    save.Succeeded.Value,
                    new Dictionary<string, WorldEventEffect>(StringComparer.Ordinal));
            }
            instances.Add(instance);
        }
        _worldEvents.Restore(instances);
    }
}
