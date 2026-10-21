using System;
using System.Collections.Generic;
using System.Linq;
using ClaudeCodeGameStudios.Domain.Village;

namespace ClaudeCodeGameStudios.Application;

/// <summary>
/// 建造流程 —— **独立建筑的开工、推进与建成**（D-074）。
/// </summary>
/// <remarks>
/// <para><b>它管什么</b>：玩家点「建造围栏 / 建造试炼塔」之后，
/// <b>扣资源 → 开工 → 施工队推进 → 建成</b>这一整条链。
/// <b>村庄的整体升级</b>（5 级阶梯）不在这里 —— 那是既有的
/// <see cref="VillageUpgradeService"/>，两条路并存。</para>
///
/// <para><b>工期由人力决定</b>（D-074）：工期 = <c>BaseBuildHours ÷ 施工人数</c>。
/// ⇒ <b>派去副本的人越多，盖得越慢</b>（这是既有机制里的取舍，D-074 只是把它<b>显式化</b>），
/// 一个���都没派 ⇒ <b>停工</b>（进度不动，但已付的资源不退）。</para>
///
/// <para><b>原子性（I15 / I12）</b>：金币与材料<b>分别校验、一起提交</b>；
/// 任一不够 ⇒ <b>两边都不扣</b>，村庄状态完全不变。
/// ⇒ 绝不允许出现「扣了材料没扣钱」。</para>
///
/// <para><b>为什么建成后才生效</b>（D-074：永久生效）：
/// <see cref="BuildingInstance.Status"/> 在进度到 1 之前是「在建」，
/// 而 <see cref="BuildingInstance.IsCellInAffectsArea"/> 对未建成的建筑<b>直接返回 false</b>
/// ⇒ 半成品围栏<b>不会</b>提前放开摆摊范围。</para>
/// </remarks>
public sealed class BuildingService
{
    private readonly Village _village;
    private readonly ResourceId _gold;
    private readonly ResourceId _material;

    /// <param name="village">村庄（建筑与资源的唯一写者）。</param>
    /// <param name="gold">金币的资源种类。</param>
    /// <param name="material">材料的资源种类。</param>
    /// <exception cref="ArgumentNullException"><paramref name="village"/> 为 null。</exception>
    public BuildingService(Village village, ResourceId gold, ResourceId material)
    {
        _village = village ?? throw new ArgumentNullException(nameof(village));
        _gold = gold;
        _material = material;
    }

    /// <summary>一栋正在施工的建筑。</summary>
    /// <param name="Definition">它要建成的目标。</param>
    /// <param name="Instance">已放置的实例（进度 &lt; 1）。</param>
    /// <param name="Workers">当前在施工的人数。</param>
    /// <param name="RemainingHours">还差多少游戏内小时（人力为 0 时不减少）。</param>
    public sealed record Construction(
        BuildableBuilding Definition,
        BuildingInstance Instance,
        int Workers,
        double RemainingHours);

    private readonly Dictionary<string, Construction> _constructions =
        new(StringComparer.Ordinal);

    /// <summary>当前在施工的建筑（按开工顺序）。</summary>
    public IReadOnlyList<Construction> ActiveConstructions =>
        _constructions.Values.ToList();

    /// <summary>某栋建筑是否已建成（永久生效的那一半）。</summary>
    /// <param name="id">建筑标识。</param>
    /// <returns>已建成为 true。</returns>
    public bool IsCompleted(BuildingId id)
    {
        foreach (var b in _village.Buildings)
        {
            if (b.Id.Value == id.Value && b.Status == BuildingStatus.Completed) return true;
        }

        return false;
    }

    /// <summary>现在能不能开工（<b>只读，不改状态</b>）。</summary>
    /// <param name="definition">要建的建筑。</param>
    /// <returns>已建成 / 在建 / 钱不足 / 材料不足 → false。</returns>
    /// <remarks>
    /// ⚠️ <b>这里<b>不</b>查「落点被占」</b>：占位的<b>权威判定</b>是
    /// <see cref="Village.TryPlace"/>（它才知道与所有已有建筑的重叠）。
    /// 若这里也查一遍，落点被占时就会在<b>扣款之前</b>被拒
    /// ⇒ 「扣了款却放不下 ⇒ 退款」那条路径<b>永远走不到</b>
    /// ⇒ 那段退款代码就是<b>死代码</b>（看着在防半笔账，实际一次都没防过）。
    /// ⇒ 这里只判「能不能开工」，<b>能不能放下由放置那一步说了算</b>。
    /// </remarks>
    public bool CanStart(BuildableBuilding definition)
    {
        if (IsCompleted(definition.Id)) return false;
        if (_constructions.ContainsKey(definition.Id.Value)) return false;
        if (_village.Get(_gold) < definition.GoldCost) return false;
        if (_village.Get(_material) < definition.MaterialCost) return false;

        return true;
    }

    /// <summary>
    /// 开工：<b>扣资源 + 放置一个在建实例</b>（两边一起提交，I15）。
    /// </summary>
    /// <param name="definition">要建的建筑。</param>
    /// <param name="workers">参与施工的人数（0 也允许 —— 那就是停工）。</param>
    /// <returns>开工成功为 true；<b>失败时村庄状态完全不变</b>。</returns>
    public bool TryStart(BuildableBuilding definition, int workers)
    {
        if (workers < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(workers), workers, "施工人数不能为负。");
        }

        if (!CanStart(definition)) return false;

        // 扣款：**先校验两边，再一起扣**（I15 / I12）。
        //
        // ⚠️ **为什么不用「扣了再退」**：那样「退款」那条分支**根本不可达**
        // （CanStart 已查过两边都够 ⇒ TrySpend 不会失败）⇒ 那段代码是**死代码**，
        // 而死代码看起来在防「半笔账」，实际上**一次都没防过**。
        // ⇒ 先校验 ⇒ 一起扣 ⇒ **要么两边都扣，要么两边都不扣**，没有中间态。
        if (_village.Get(_gold) < definition.GoldCost) return false;
        if (_village.Get(_material) < definition.MaterialCost) return false;

        _village.TrySpend(_gold, definition.GoldCost);
        _village.TrySpend(_material, definition.MaterialCost);

        var instance = new BuildingInstance(
            definition.Id,
            definition.GridX, definition.GridY,
            definition.FootprintW, definition.FootprintH,
            constructionProgress: 0.0,
            affectsAreaW: definition.AffectsAreaW,
            affectsAreaH: definition.AffectsAreaH);

        if (!_village.TryPlace(instance))
        {
            // 落点被占（CanStart 已查过，但建造与放置之间状态可能变了）⇒ 退款。
            _village.Add(_gold, definition.GoldCost);
            _village.Add(_material, definition.MaterialCost);
            return false;
        }

        _constructions[definition.Id.Value] = new Construction(
            definition, instance, workers, definition.BaseBuildHours);

        return true;
    }

    /// <summary>
    /// 推进施工：按<b>当前施工人数</b>折算进度，返回<b>这次建成的建筑</b>。
    /// </summary>
    /// <param name="workedHours">
    /// 这次过了多少游戏内小时。<b>人力为 0 时不推进</b>（停工）。
    /// </param>
    /// <returns>建成的建筑定义；没建成返回 null。</returns>
    /// <remarks>
    /// ⚠️ **进度按「人力 × 时间」线性推进，而不是「÷人数」**——
    /// 前者与既有机制一致（多一个人多一份进度），后者会让
    /// 「1 人盖 8 小时」与「2 人盖 4 小时」在<b>总工时</b>上不等价。
    /// 基数是 <see cref="BuildableBuilding.BaseBuildHours"/>（一个人盖完要多久）。
    /// </remarks>
    public BuildableBuilding? AdvanceConstruction(double workedHours)
    {
        if (!double.IsFinite(workedHours) || workedHours < 0.0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(workedHours), workedHours, "推进的时长必须是非负有限数。");
        }

        BuildableBuilding? completed = null;

        foreach (var id in _constructions.Keys.ToArray())
        {
            var c = _constructions[id];
            if (c.Workers <= 0) continue;      // 没人 ⇒ 停工

            // 一人盖完要 BaseBuildHours 小时 ⇒ 一个人一小时推进 1/BaseBuildHours。
            double progress = c.Workers * workedHours / c.Definition.BaseBuildHours;
            c.Instance.AdvanceConstruction(progress);

            if (c.Instance.ConstructionProgress >= 1.0)
            {
                completed = c.Definition;
                _constructions.Remove(id);
            }
        }

        return completed;
    }

    /// <summary>更新某栋在施工建筑的<b>人数</b>（人手变了要跟着变）。</summary>
    /// <param name="id">建筑标识。</param>
    /// <param name="workers">新的施工人数。</param>
    /// <exception cref="ArgumentOutOfRangeException">人数为负。</exception>
    public void SetWorkers(BuildingId id, int workers)
    {
        if (workers < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(workers), workers, "施工人数不能为负。");
        }

        if (!_constructions.TryGetValue(id.Value, out var c)) return;

        _constructions[id.Value] = c with { Workers = workers };
    }

    /// <summary>取消施工（<b>不退款</b> —— 材料已经变成半成品）。</summary>
    /// <param name="id">建筑标识。</param>
    /// <returns>取消成功为 true（本来就没在建则 false）。</returns>
    public bool Cancel(BuildingId id) => _constructions.Remove(id.Value);
}
