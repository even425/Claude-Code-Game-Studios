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
        _worldEvents.Register(ClaudeCodeGameStudios.Application.WorldEventTemplates.BeastTide(
            periodDays: WorldEventBeastTidePeriodDays, _combat, _combat));
        _worldEvents.Register(ClaudeCodeGameStudios.Application.WorldEventTemplates.Caravan(
            periodDays: WorldEventCaravanPeriodDays));
    }

    /// <summary>兽潮的触发周期（游戏内天数）。</summary>
    private const int WorldEventBeastTidePeriodDays = 7;

    /// <summary>商队来访的触发周期（游戏内天数）。</summary>
    private const int WorldEventCaravanPeriodDays = 11;

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
                var outcome = _worldEvents.Settle(moved.Id, ApplyWorldEventConsequence);
                if (outcome is not null)
                {
                    var template = _worldEvents.Template(moved.TemplateId);
                    Emit(EvWorldEventSettled, template?.Name ?? moved.TemplateId,
                        new[] { StWorldEvent, StSettlement },
                        new Dictionary<string, string>
                        {
                            ["event"] = template?.Name ?? moved.TemplateId,
                            ["succeeded"] = outcome.Succeeded ? "1" : "0",
                            ["instance"] = moved.Id,
                            ["consequences"] = string.Join(" / ",
                                outcome.Consequences.Select(c => c.Value)),
                        },
                        ev => Chronicle.WorldEventSettled(
                            (int)_clock.Day, ev.Payload["event"],
                            ev.Payload["succeeded"] == "1").Text);
                }
            }
        }
    }

    /// <summary>
    /// 把一条大事件的后果<b>应用出去</b>（基线 §11.4：事件系统不越权改数据）。
    /// </summary>
    /// <remarks>
    /// ⚠️ **这里只做「事情真的发生了」的记账**（发事件、记流水），
    /// <b>不直接改</b>村庄资源 / 英雄属性 —— 那些由对应系统做。
    /// ⇒ 兽潮与商队此刻都还没有「对村庄有实际影响」的规则（D-079 只落框架），
    /// 所以后果的**内容**由模板给，这里只负责**落地与幂等**。
    /// </remarks>
    private void ApplyWorldEventConsequence(string key, string description)
    {
        GD.Print($"WORLDEVENT 后果 {key}：{description}");
    }

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
            if (save.Succeeded.HasValue)
            {
                instance.Outcome = new WorldEventOutcome(
                    save.Succeeded.Value,
                    new Dictionary<string, string>(StringComparer.Ordinal));
            }
            instances.Add(instance);
        }
        _worldEvents.Restore(instances);
    }
}
