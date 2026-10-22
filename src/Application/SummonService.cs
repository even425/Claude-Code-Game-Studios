using System;
using ClaudeCodeGameStudios.Domain.Village;

namespace ClaudeCodeGameStudios.Application;

/// <summary>
/// 召唤 —— <b>花钱换一个英雄</b>（D-077）。
/// </summary>
/// <remarks>
/// <para><b>规则（D-077，开发者拍板）</b>：
/// <list type="bullet">
/// <item><b>消耗</b>：村庄金库 <b>100 金</b>（与「升级村庄」同源 ⇒ 花钱的地方有竞争）。</item>
/// <item><b>稀有度</b>：<b>不做</b> —— 英雄是独特角色，不是数值卡牌（基线 §10.4）。</item>
/// <item><b>保底 / 重复</b>：<b>都不做</b>（基线 §10.4 原文：「在此问题明确前，
/// <b>不实现</b>重复角色、角色碎片或重复转化机制」）。</item>
/// </list></para>
///
/// <para><b>为什么消耗落在「村金库」</b>：村金库是<b>村庄共有</b>的钱
/// ⇒ 招人要占用村庄的公共积累 ⇒ 与「升级村庄」直接竞争。
/// ⚠️ 村金库目前只有<b>摆摊抽成</b>一个来源（副本奖励进的是材料池）
/// ⇒ 召唤**不是开局就能用的功能**，得先有货、卖出去、抽成攒起来
/// ⇒ 这正好是基线 §10.2「C-经营独立性」要的样子：<b>经营不是为了召唤才存在</b>。</para>
///
/// <para><b>资源不足 ⇒ 拒绝</b>（基线 §10.8 S5）：
/// 钱不够就<b>不召唤</b>，且<b>不扣任何东西</b>（不变量 I12：资源不会变为负数）。</para>
/// </remarks>
public sealed class SummonService
{
    /// <summary>
    /// 召唤一次要花的金币（D-077）。
    /// </summary>
    /// <remarks>
    /// ⚠️ **这个数的锚**：「一个人一天挣多少」⇒ 英雄的日产出约 100 金
    /// ⇒ 招一个人 ≈ 一天的产出 ⇒ 不至于招不起，也不至于白招。
    /// </remarks>
    public const long GoldCost = 100;

    private readonly Village _village;
    private readonly ResourceId _gold;

    /// <param name="village">村庄（金库在这里）。</param>
    /// <param name="gold">金币的资源种类。</param>
    /// <exception cref="ArgumentNullException"><paramref name="village"/> 为 null。</exception>
    public SummonService(Village village, ResourceId gold)
    {
        _village = village ?? throw new ArgumentNullException(nameof(village));
        _gold = gold;
    }

    /// <summary>村金库现在有多少钱。</summary>
    public long VillageGold => _village.Get(_gold);

    /// <summary>现在能不能召唤（<b>只读，不改状态</b>）。</summary>
    public bool CanSummon => VillageGold >= GoldCost;

    /// <summary>
    /// 扣召唤费。<b>只做扣钱这一件事</b>（造英雄是调用方的事）。
    /// </summary>
    /// <returns>
    /// 扣成功为 true；钱不够为 false（<b>此时什么都没变</b>）。
    /// </returns>
    /// <remarks>
    /// <para>⚠️ <b>为什么「扣钱」与「造英雄」分成两步</b>：造英雄要写花名册与领域状态，
    /// 那是表现层的活；而「钱够不够、扣多少」是<b>规则</b>。
    /// ⇒ 规则层只管钱，调用方造完人后若失败，用 <see cref="Refund"/> 退回去
    /// ⇒ <b>不会留下「扣了钱没招到人」的半笔账</b>。</para>
    /// <para>⚠️ <b>余额检查只有一处</b>：<see cref="Village.TrySpend"/> 自己会拒绝余额不足
    /// （不变量 I12 的实现）⇒ 这里<b>不再重复查一遍</b>。
    /// 两处都查的话，去掉任何一处都不改变行为 ⇒ 守卫就<b>抓不到</b>「兜底被破坏」
    /// （变异看红时实测过：两层检查时，破坏内层守卫仍然绿）。
    /// ⇒ 权威判定只在领域层，调用层只转发。</para>
    /// </remarks>
    public bool TryPayCost() => _village.TrySpend(_gold, GoldCost);

    /// <summary>
    /// 退钱 —— 召唤**没成**时把召唤费退回去。
    /// </summary>
    /// <param name="amount">要退的金额（一般是 <see cref="GoldCost"/>）。</param>
    /// <exception cref="ArgumentOutOfRangeException">金额非正。</exception>
    public void Refund(long amount)
    {
        if (amount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(amount), amount, "退款金额必须为正。");
        }

        _village.Add(_gold, amount);
    }
}
