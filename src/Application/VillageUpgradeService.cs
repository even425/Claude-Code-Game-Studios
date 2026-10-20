using System;
using System.Collections.Generic;
using ClaudeCodeGameStudios.Domain.Village;

namespace ClaudeCodeGameStudios.Application;

/// <summary>
/// 村庄升级的<b>扣款与成本</b>（D-054-02：村庄升级同时消耗金币和材料）。
/// </summary>
/// <remarks>
/// <para><b>为什么把它从 <c>Main</c> 里抽出来</b>：升级成本是<b>规则</b>，
/// 而 <c>Main</c> 是表现层/编排层。把「要花多少」与「能不能花得起」放在
/// <c>Main</c> 里，就<b>没法单测</b> —— 而这恰好是本项最需要守的地方
/// （「金币不足**或**材料不足 → <b>都不扣</b>」）。</para>
///
/// <para><b>升级花掉的金币是「销毁」不是「转移」</b>：钱变成了建筑（村庄的固定资产），
/// 不在任何英雄的钱包里，也不在金库里。守恒律里它对应「<b>已销毁金币累计</b>」那一项
/// —— 与 5% 村抽（进金库，只是离开可流通池）**不是同一件事**。</para>
///
/// <para><b>原子性（I15）</b>：金币与材料**分别校验、一起提交**。
/// 任何一边不够 → <b>两边都不扣</b>，状态完全不变。
/// 绝不能出现「扣了材料没扣钱」这种半笔账。</para>
/// </remarks>
public sealed class VillageUpgradeService
{
    /// <summary>一次升级的成本。</summary>
    /// <param name="Gold">金币（D-054 裁决 ⑤）。</param>
    /// <param name="Material">材料（既有规则 <c>30 + tier × 25</c>）。</param>
    public readonly record struct UpgradeCost(long Gold, long Material);

    /// <summary>
    /// 村庄的<b>最高层级</b>（可升级次数 = 该值）。
    /// </summary>
    /// <remarks>
    /// <b>它是规则的所在地</b>：<c>Main</c> 与 <c>VillageView</c> 都以本值为准
    /// （视图层那份由 <c>VillageUpgradeServiceTests</c> 的跨层一致性用例盯着，
    /// 免得两处数字悄悄分叉）。
    /// </remarks>
    public const int MaxTier = 5;

    /// <summary>
    /// 五级升级的成本表（<b>由第 0 级升到第 1 级</b>用的是第 0 项）。
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    /// <item><b>金币</b> 50 / 100 / 150 / 200 / 250，合计 <b>750</b>（D-054 裁决 ⑤）</item>
    /// <item><b>材料</b> 30 / 55 / 80 / 105 / 130，合计 <b>400</b>（既有规则 <c>30 + tier × 25</c>，未改）</item>
    /// </list>
    /// ⚠️ 这些是<b>本轮采用的方案值</b>，**不是「长期经济已平衡」的结论**（D-054 边界）。
    /// 金币成本必须让 5 次升级在 1000 天采集窗口内**可达**（`34` 阶段 4 会逐级核对）。
    /// </remarks>
    private static readonly UpgradeCost[] CostTable =
    {
        new(50L, 30L),
        new(100L, 55L),
        new(150L, 80L),
        new(200L, 105L),
        new(250L, 130L),
    };

    private readonly Village _village;
    private readonly ResourceId _gold;
    private readonly ResourceId _material;

    /// <param name="village">村庄（金库与材料池都在它身上）。</param>
    /// <param name="gold">金币的资源种类。</param>
    /// <param name="material">材料的资源种类。</param>
    /// <exception cref="ArgumentNullException"><paramref name="village"/> 为 null。</exception>
    public VillageUpgradeService(Village village, ResourceId gold, ResourceId material)
    {
        _village = village ?? throw new ArgumentNullException(nameof(village));
        _gold = gold;
        _material = material;
    }

    /// <summary>
    /// 累计<b>销毁</b>掉的金币（升级花掉的那些）。
    /// </summary>
    /// <remarks>
    /// 守恒律（<b>流通口径</b>）要用：
    /// `Σ英雄钱包金币 + 村库金币 + 累计销毁金币 = 初始金币 + 累计发放金币`。
    /// 金库只进不出时，<b>已销毁金币 = 累计升级花费</b>。
    /// <para>
    /// ⚠️ 那条等式<b>不含「税额余数」</b>（记账零头，不是钱），
    /// <b>也不含「待发零头」</b>（还没进钱包的应收）—— 两者都不是钱在的地方。
    /// </para>
    /// </remarks>
    public long TotalGoldSpent { get; private set; }

    /// <summary>累计升级次数（= 累计花掉的金币对应的级数）。</summary>
    public int UpgradesStarted { get; private set; }

    /// <summary>
    /// 恢复累计计数 —— <b>仅供读档</b>。
    /// </summary>
    /// <param name="goldSpent">累计销毁的金币（整金）。</param>
    /// <param name="upgrades">累计已付款的升级次数。</param>
    /// <exception cref="ArgumentOutOfRangeException">任一计数为负。</exception>
    public void RestoreTotals(long goldSpent, long upgrades)
    {
        if (goldSpent < 0) throw new ArgumentOutOfRangeException(nameof(goldSpent));
        if (upgrades < 0) throw new ArgumentOutOfRangeException(nameof(upgrades));
        TotalGoldSpent = goldSpent;
        UpgradesStarted = (int)upgrades;
    }

    /// <summary>
    /// 升到第 <paramref name="tier"/> + 1 级需要的成本。
    /// </summary>
    /// <param name="tier">当前层级（0 起）。</param>
    /// <returns>成本；层级越界返回 <c>default</c>（金币与材料都是 0）。</returns>
    public UpgradeCost CostFor(int tier)
        => tier >= 0 && tier < CostTable.Length ? CostTable[tier] : default;

    /// <summary>
    /// 现在能不能升这一级（<b>只读，不改状态</b>）。
    /// </summary>
    /// <param name="tier">当前层级（0 起）。</param>
    /// <returns>层级已满 / 金币不足 / 材料不足 → false。</returns>
    public bool CanUpgrade(int tier)
    {
        if (tier < 0 || tier >= MaxTier) return false;

        var cost = CostFor(tier);
        if (cost.Gold <= 0 || cost.Material <= 0) return false;

        return _village.Get(_gold) >= cost.Gold
            && _village.Get(_material) >= cost.Material;
    }

    /// <summary>
    /// 支付升级成本。<b>要么两边都扣，要么都不扣</b>（I15）。
    /// </summary>
    /// <param name="tier">当前层级（0 起）。</param>
    /// <returns>成功返回 true；层级已满或任一资源不足返回 false，<b>状态完全不变</b>。</returns>
    /// <remarks>
    /// <para><b>顺序</b>：先<b>两边都校验</b> → 再扣金币 → 再扣材料。
    /// 校验过了之后扣减不该再失败；万一材料那一步真的失败（理论上不会），
    /// 就把金币<b>还回去</b> —— 宁可多写一行，也不能留下半笔账。</para>
    /// </remarks>
    public bool TryUpgrade(int tier)
    {
        if (!CanUpgrade(tier)) return false;

        var cost = CostFor(tier);

        // 提交：金币先走（它是「销毁」的那一份，要记进累计）
        if (!_village.TrySpend(_gold, cost.Gold)) return false;

        if (!_village.TrySpend(_material, cost.Material))
        {
            // 理论不可达（预检已过，且期间没有别的写者）。真到了这里也要把账做平。
            _village.Add(_gold, cost.Gold);
            return false;
        }

        TotalGoldSpent += cost.Gold;
        UpgradesStarted++;
        return true;
    }

    /// <summary>成本表（只读快照）——供界面显示与测试核对。</summary>
    public IReadOnlyList<UpgradeCost> AllCosts => CostTable;
}
