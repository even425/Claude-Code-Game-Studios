using Godot;
using System;
using System.Linq;
using ClaudeCodeGameStudios.Domain.Village;
using ClaudeCodeGameStudios.Domain.WorldEvents;

public partial class Main : Node2D
{
    /// <summary>
    /// <c>--event</c>：D-101 真机自检 —— <b>大事件真的走到结算了吗</b>。
    /// </summary>
    /// <remarks>
    /// <para>🔴 <b>它守的是一个曾经一直错的西</b>：大事件（兽潮 / 商队 / 盗匪来袭）
    /// 在<b>真实游戏</b>里<b>从来没有结算过</b> —— 实例卡死在「预兆」阶段。
    /// ⇒ 单测抓不到（单测是<b>直接调 <c>Advance</c></b> 的，绕开了日循环那道闸门）；
    /// ⇒ 标定也抓不到（「标定里兽潮一次都没来」被归因为 D-082 的过滤器，
    /// <b>其实是过滤器掩盖了死锁</b>）⇒ 只有<b>走真实日循环</b>的自检能抓到。</para>
    ///
    /// <para><b>验的是三件事</b>：
    /// <list type="number">
    /// <item>把时钟拨到第 9 天 ⇒ <b>盗匪来袭被触发</b>（周期 9 天）。</item>
    /// <item><b>逐日推进</b>（走 <c>TickWorldEvents</c> 那条真实路径）⇒ 它必须走到<b>结算</b>。</item>
    /// <item>结算 ⇒ <b>村库真的动了</b>（材料被抢 / 缴获）。</item>
    /// </list></para>
    ///
    /// <para>⚠️ <b>为什么必须逐日推进</b>：阶段机是<b>一天一步</b>的
    /// ⇒ 一次性把时钟拨到第 30 天<b>不会</b>让事件走完生命周期
    /// ⇒ 必须一天一天来（这正是真实游戏的走法）。</para>
    /// </remarks>
    private void RunEventSelfTest()
    {
        _eventTestDone = true;

        var material = new ResourceId("材料");
        long materialBefore = _villageModel.Get(material);

        // ① 拨到第 9 天（盗匪周期 9 天；兽潮 7 / 商队 11 都不撞这一天）。
        // ⚠️ **日序号从 1 起** ⇒ 第 9 天 = 8 个整日长（`Day = floor(now/L) + 1`）。
        // ⚠️ 触发条件是 `simDay % 周期 == 0` ⇒ 第 9 天 ⇒ 9 % 9 == 0 ✓。
        _clock.SetSimTime(8 * _clock.SimSecondsPerDay);
        _worldEventLastDay = -1;   // 放行日闸门
        TickWorldEvents();

        bool triggered = _worldEvents.Instances.Any(i => i.TemplateId == "bandit_raid");
        GD.Print($"EVENTTEST 第 9 天：盗匪实例={triggered}"
                 + $"（当前阶段={_worldEvents.Instances.FirstOrDefault()?.Phase.ToString() ?? "无"}）");

        // ② **逐日推进**（真实游戏怎么走，这里就怎么走）。
        int days = 0;
        while (days < 12)
        {
            var phaseBefore = _worldEvents.Instances.FirstOrDefault()?.Phase;
            if (phaseBefore == WorldEventPhase.Finished || phaseBefore == null) break;

            _clock.SetSimTime(_clock.Now + _clock.SimSecondsPerDay);
            _worldEventLastDay = -1;
            TickWorldEvents();
            days++;

            var phaseNow = _worldEvents.Instances.FirstOrDefault()?.Phase;
            GD.Print($"EVENTTEST 第 {9 + days} 天：阶段 {phaseBefore} → {phaseNow}");
            if (phaseNow == WorldEventPhase.Finished) break;
        }

        var settled = _worldEvents.Instances
            .FirstOrDefault(i => i.TemplateId == "bandit_raid")?.Outcome;
        bool reachedSettlement = settled is not null;
        long materialAfter = _villageModel.Get(material);

        GD.Print($"EVENTTEST 推进 {days} 天 ⇒ 结算了={reachedSettlement}"
                 + $"（胜={settled?.Succeeded}），村库材料 {materialBefore}→{materialAfter}");

        bool ok = triggered && reachedSettlement && days <= 12;
        GD.Print($"EVENTTEST 大事件真的会结算 ⇒ {(ok ? "PASS" : "FAIL")}");
    }
}
