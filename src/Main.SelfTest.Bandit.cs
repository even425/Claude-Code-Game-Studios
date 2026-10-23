using Godot;
using System;
using System.Linq;
using ClaudeCodeGameStudios.Application;
using ClaudeCodeGameStudios.Domain.Village;
using ClaudeCodeGameStudios.Domain.WorldEvents;

public partial class Main : Node2D
{
    /// <summary>
    /// <c>--bandit</c>：D-096 真机自检 —— <b>盗匪来袭</b>那条路上材料与金币到底怎么走。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么要有这条</b>：D-096 是<b>第三个大事件实例</b>，
    /// 而它的形状与前两个不同（无「后续影响」阶段、强度不随层级增长）。
    /// ⇒ 若它只在单元测试里跑过，真实游戏里那条「村库材料被抢 / 缴获进村库」的线
    /// 就<b>没人验过</b>（与 D-088 商队同一族风险：单测绿 ≠ 接线对）。</para>
    ///
    /// <para><b>验的是四件事</b>（金额必须<b>算出来</b>，不能写死）：
    /// <list type="number">
    /// <item>盗匪<b>会被触发</b>（不被兽潮那道「守不住就不触发」的闸拦住）。</item>
    /// <item>结算后<b>村库材料真的动了</b>（缴获 ⇒ 变多；被抢 ⇒ 变少）。</item>
    /// <item><b>金币只在打赢时动</b>，打输时<b>一分不扣</b>（盗匪抢的是货不是钱）。</item>
    /// <item>同一个实例<b>结算两次不会重复发</b>（幂等）。</item>
    /// </list></para>
    ///
    /// <para>⚠️ <b>走生产代码路径</b>（<see cref="WorldEventTemplates.BanditRaid"/> +
    /// <see cref="WorldEventResourceApplier"/>），不是复制品。</para>
    /// </remarks>
    private void RunBanditSelfTest()
    {
        _banditTestDone = true;

        var gold = new ResourceId("金币");
        var material = new ResourceId("材料");

        // ① 触发一个盗匪实例（周期 9 天 ⇒ 用第 9 天触发）。
        //    ⚠️ participants 给**空** ⇒ 走「没人出击 ⇒ 必败」那条路（必定被抢），
        //    数字才**确定**（打赢要真打一场，胜负随阵容变）。
        var instance = _worldEvents.TriggerByPeriod(
            simDay: WorldEventBanditPeriodDays, now: _clock.Now,
            participants: Array.Empty<string>(),
            instanceIdFactory: (template, day) => $"selftest-{template.Id}-{day}",
            shouldTrigger: ShouldTrigger);

        if (instance is null)
        {
            GD.Print("BANDITTEST FAIL 触发不到盗匪实例（周期不对？）");
            return;
        }

        if (instance.TemplateId != WorldEventTemplates.BanditRaidId)
        {
            GD.Print($"BANDITTEST FAIL 第 9 天触发的不是盗匪，而是「{instance.TemplateId}」");
            return;
        }

        // ② 推进到「结算」。
        int guard = 0;
        while (instance.Phase != WorldEventPhase.Settlement && guard++ < 20)
        {
            _worldEvents.Advance(instance.Id, 0.5);
        }
        if (instance.Phase != WorldEventPhase.Settlement)
        {
            GD.Print($"BANDITTEST FAIL 盗匪没进入「结算」（停在 {instance.Phase}）");
            return;
        }

        long material0 = _villageModel.Get(material);
        long gold0 = _villageModel.Get(gold);

        // ③ 结算（走与游戏里同一段：后果真的落到村库）。
        var outcome = _worldEvents.Settle(instance.Id, (key, effect) =>
        {
            ApplyWorldEventEffect(effect);
        });

        if (outcome is null)
        {
            GD.Print("BANDITTEST FAIL 结算拿不到结果");
            return;
        }

        long material1 = _villageModel.Get(material);
        long gold1 = _villageModel.Get(gold);

        // ④ 没人出击 ⇒ 必败 ⇒ 材料被抢（扣到 0 为止）、金币一分不动。
        bool lostOk = !outcome.Succeeded;
        bool materialOk = material1 < material0 || (material0 == 0 && material1 == 0);
        bool goldOk = gold1 == gold0;
        bool amountOk = material0 - material1 <= BanditTuningBase.MaterialLoss;

        // ⑤ 幂等：再结一次不会重复扣。
        var again = _worldEvents.Settle(instance.Id, (key, effect) =>
        {
            ApplyWorldEventEffect(effect);
        });
        long material2 = _villageModel.Get(material);
        bool idempotentOk = again is null || material2 == material1;

        GD.Print($"BANDITTEST 盗匪实例 {instance.Id} 结算：胜={outcome.Succeeded}"
                 + $"（没人出击 ⇒ 应为 False）");
        GD.Print($"BANDITTEST 村库材料 {material0}→{material1}（应减少，最多 {BanditTuningBase.MaterialLoss}），"
                 + $"金币 {gold0}→{gold1}（应不变）");
        GD.Print($"BANDITTEST 再结一次：结果={(again is null ? "null（已结算，不再发）" : "又发了一次")}，"
                 + $"材料 {material1}→{material2}（应不变）");
        GD.Print($"BANDITTEST 必败={lostOk} 材料对={materialOk} 金币对={goldOk} "
                 + $"扣减不超量={amountOk} 幂等={idempotentOk} ⇒ "
                 + $"{(lostOk && materialOk && goldOk && amountOk && idempotentOk ? "PASS" : "FAIL")}");
    }
}
