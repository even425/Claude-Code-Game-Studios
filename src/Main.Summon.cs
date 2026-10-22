using Godot;
using System;
using System.Collections.Generic;
using ClaudeCodeGameStudios.Application;
using ClaudeCodeGameStudios.Domain.Heroes;

public partial class Main : Node2D
{
    /// <summary>
    /// 召唤一个英雄（<b>不扣费</b>）—— 开局赠送与自检夹具用。
    /// </summary>
    /// <remarks>
    /// ⚠️ <b>玩家的「招募一个人」按钮走的是 <see cref="OnSummonPaid"/></b>
    /// （D-077：召唤要花 100 金）⇒ 这里是<b>免费</b>的那条路。
    /// </remarks>
    private void OnSummon() => SummonHero(free: true);

    /// <summary>
    /// 玩家的「招募一个人」按钮 —— <b>扣 100 金</b>（D-077）。
    /// </summary>
    /// <remarks>
    /// <para>⚠️ <b>钱不够就拒绝，且不扣任何东西</b>（基线 §10.8 S5 + 不变量 I12）。</para>
    /// <para>⚠️ <b>扣钱与造人是一笔账</b>：先扣钱，造人若抛异常 ⇒ <b>把钱退回去</b>
    /// ⇒ 不会留下「扣了钱没招到人」的半笔账。</para>
    /// </remarks>
    private void OnSummonPaid()
    {
        if (!_summonService.TryPayCost())
        {
            GD.Print($"SUMMON 村金库不足：需要 {SummonService.GoldCost} 金，"
                     + $"现有 {_summonService.VillageGold} 金 —— 不召唤、不扣钱。");
            Refresh();
            return;
        }

        try
        {
            SummonHero(free: false);
        }
        catch
        {
            // 造人失败 ⇒ 退款（不能白扣钱）。
            _summonService.Refund(SummonService.GoldCost);
            throw;
        }
    }

    /// <summary>
    /// 造出一个英雄并登记进村庄（花名册 + 领域状态 + 事件）。
    /// </summary>
    /// <param name="free">
    /// 是否为<b>免费</b>召唤（开局赠送 / 自检夹具）——
    /// 免费的那次<b>不扣钱</b>，也不在事件里记消耗。
    /// </param>
    private void SummonHero(bool free)
    {
        var s = HeroFactory.Summon();
        // M7 / D-030：把 HeroId 同步到 HeroAgent（M2 挂账兑现）。
        // **同一个 id 必须在 Add 之前算好**：原来 Agent 用 Add 前的 Count、Definition 用
        // Add 后的 Count，于是两边恒差 1（h-0 vs h-1）。M4 之前没有任何东西同时引用两边，
        // 直到摊位的 OwnerId（Agent）要去 heroes[]（Definition Id）里找引用才暴露。
        //
        // I1（同一 Id 只对应一个英雄实体）：Id 由 `HeroIdAllocator` 分配，
        // **不用 `_heroes.Count`** —— 人数当序号在花名册连续时没问题，
        // 但读档后花名册若缺一号（h-0 / h-2），下一个就会拿到 h-2 ⇒ **两个英雄同号**
        // ⇒ 读档时两个状态映射到同一个实体（存档错乱，不是显示错乱）。
        string id = _heroIds.Next();
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
                // D-077：这次召唤花了多少钱（免费的那次是 0）。
                ["cost"] = free ? "0" : SummonService.GoldCost.ToString(),
            },
            _ => Chronicle.Summoned((int)_clock.Day, s.Hero, s.WorldName, s.Trait).Text);
        Refresh();
    }
}
