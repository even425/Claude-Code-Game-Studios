using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using ClaudeCodeGameStudios.Domain.Events;
using ClaudeCodeGameStudios.Presentation;

/// <summary>
/// 主场景的<b>事件管道</b>部分（A1 拆分）。
///
/// <para>基线 §7.2 / §7.4 的事件溯源：领域事件 → <c>ChronicleFeed</c> → 杂谈条目。
/// <c>Emit</c> 是 C1 / C2 的<b>唯一</b>入口 —— 别的代码不许自己造杂谈条目。</para>
///
/// <para>⚠️ **这是纯代码搬移** —— 逻辑一行没改。</para>
/// </summary>
public partial class Main : Node2D
{
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

        // 经济流水落盘（运行闭环验证与经济数据采集准备）。
        // 挂在这里 —— 事件**已记录**之后：落盘的是「确实发生过的事」，
        // 不是「打算发生的事」。EconomyLog 自己只收经济类事件，其余一律忽略。
        _economyLog?.Append(e);

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
}
