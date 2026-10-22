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

    /// <summary>向村民采购建材（D-091）；没有它 = 只用村库材料池（既有行为）。</summary>
    private readonly MaterialProcurement? _procurement;

    /// <summary>候选卖方（D-091）。</summary>
    private readonly Func<IReadOnlyList<HeroAgent>>? _heroes;

    /// <param name="village">村庄（建筑与资源的唯一写者）。</param>
    /// <param name="gold">金币的资源种类。</param>
    /// <param name="material">材料的资源种类。</param>
    /// <param name="procurement">向村民采购建材（D-091；可空 = 不采购）。</param>
    /// <param name="heroes">候选卖方（D-091；可空 = 不采购）。</param>
    /// <exception cref="ArgumentNullException"><paramref name="village"/> 为 null。</exception>
    public BuildingService(
        Village village,
        ResourceId gold,
        ResourceId material,
        MaterialProcurement? procurement = null,
        Func<IReadOnlyList<HeroAgent>>? heroes = null)
    {
        _village = village ?? throw new ArgumentNullException(nameof(village));
        _gold = gold;
        _material = material;
        _procurement = procurement;
        _heroes = heroes;
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

        // 材料：**村库材料池 + 向村民采购的部分**（D-091）⇒
        // 「村民背包里有建材」也算「村里凑得齐料」⇒ 村民的背包**终于有了用途**。
        long fromPool = definition.MaterialCost - ProcurementPlanOf(definition).Qty;
        if (_village.Get(_material) < fromPool) return false;

        return true;
    }

    /// <summary>算一份采购计划（<b>只算不改</b>；没有采购服务时是空计划）。</summary>
    private ProcurementPlan ProcurementPlanOf(BuildableBuilding definition)
    {
        if (_procurement is null || _heroes is null) return ProcurementPlan.Empty;
        return _procurement.Plan(definition.MaterialCost, _heroes());
    }

    /// <summary>
    /// <b>落点合法性</b>（D-093：玩家自选落点时的两条规则）。
    /// </summary>
    /// <param name="definition">要建的建筑。</param>
    /// <param name="gridX">落点的格位 X。</param>
    /// <param name="gridY">落点的格位 Y。</param>
    /// <returns>合法为 true。</returns>
    /// <remarks>
    /// <para>① <b>不与已有建筑重叠</b>（基线 §13.7「建筑坐标与占地冲突」）。</para>
    /// <para>② <b>落在村子的地盘里</b>（落在至少一个已建成建筑的「影响区」里）
    /// ⇒ 与摆摊用的是同一把尺子（摆摊位置守门）。</para>
    /// <para>⚠️ <b>旧的默认落点也走这条规则</b>（围栏 -3,-3 / 试炼塔 -1,-3 都在市集的影响区里）
    /// ⇒ 玩家自选不会把建筑建到村子外面。</para>
    /// </remarks>
    public bool CanPlaceAt(BuildableBuilding definition, int gridX, int gridY)
    {
        for (int dx = 0; dx < definition.FootprintW; dx++)
        {
            for (int dy = 0; dy < definition.FootprintH; dy++)
            {
                if (_village.IsOccupied(gridX + dx, gridY + dy)) return false;
            }
        }

        bool inAnyAffects = false;
        foreach (var b in _village.Buildings)
        {
            if (b.IsCellInAffectsArea(gridX, gridY)) { inAnyAffects = true; break; }
        }

        return inAnyAffects;
    }

    /// <summary>
    /// 开工：<b>扣资源 + 放置一个在建实例</b>（两边一起提交，I15）。
    /// </summary>
    /// <param name="definition">要建的建筑。</param>
    /// <param name="workers">参与施工的人数（0 也允许 —— 那就是停工）。</param>
    /// <returns>开工成功为 true；<b>失败时村庄状态完全不变</b>。</returns>
    public bool TryStart(BuildableBuilding definition, int workers)
        => TryStartAt(definition, definition.GridX, definition.GridY, workers);

    /// <summary>
    /// 开工（<b>玩家自选落点</b>，D-093）。
    /// </summary>
    /// <param name="definition">要建的建筑。</param>
    /// <param name="gridX">落点的格位 X。</param>
    /// <param name="gridY">落点的格位 Y。</param>
    /// <param name="workers">参与施工的人数（0 也允许 —— 那就是停工）。</param>
    /// <returns>开工成功为 true；<b>失败时村庄状态完全不变</b>。</returns>
    /// <remarks>
    /// <para>⚠️ <b>为什么落点要另开一个入口</b>：旧的 <c>TryStart</c> 把落点写死在定义里
    /// （<code>GridX/GridY</code>，那是一个「占位落点」，D-093 之前因为“不做玩家选位置”）
    /// ⇒ 玩家一点也改不了。</para>
    /// <para>⚠️ <b>落点合法性由 <see cref="CanPlaceAt"/>守</b>（不重叠 + 落在村子的地盘里）。</para>
    /// </remarks>
    public bool TryStartAt(BuildableBuilding definition, int gridX, int gridY, int workers)
    {
        if (workers < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(workers), workers, "施工人数不能为负。");
        }

        if (!CanStart(definition)) return false;
        if (!CanPlaceAt(definition, gridX, gridY)) return false;

        // 扣款：**先校验两边，再一起扣**（I15 / I12）。
        //
        // ⚠️ **为什么不用「扣了再退」**：那样「退款」那条分支**根本不可达**
        // （CanStart 已查过两边都够 ⇒ TrySpend 不会失败）⇒ 那段代码是**死代码**，
        // 而死代码看起来在防「半笔账」，实际上**一次都没防过**。
        // ⇒ 先校验 ⇒ 一起扣 ⇒ **要么两边都扣，要么两边都不扣**，没有中间态。
        //
        // ⚠️ **D-091**：材料可能一部分从村民手里买（按市价付钱给他们），
        // 一部分从村库材料池扣 ⇒ 采购与扣款**必须是同一次提交**。
        var plan = ProcurementPlanOf(definition);
        long fromPool = definition.MaterialCost - plan.Qty;
        if (_village.Get(_gold) < definition.GoldCost) return false;
        if (_village.Get(_material) < fromPool) return false;

        // ① 先向村民采购（拿不到货就整体不成立 ⇒ 状态不变）
        if (_procurement is not null && !_procurement.Commit(plan)) return false;

        // ② 再扣建造自己的成本
        _village.TrySpend(_gold, definition.GoldCost);
        if (fromPool > 0) _village.TrySpend(_material, fromPool);

        var instance = new BuildingInstance(
            definition.Id,
            gridX, gridY,
            definition.FootprintW, definition.FootprintH,
            constructionProgress: 0.0,
            affectsAreaW: definition.AffectsAreaW,
            affectsAreaH: definition.AffectsAreaH);

        if (!_village.TryPlace(instance))
        {
            // 落点被占（CanStart 已查过，但建造与放置之间状态可能变了）⇒ 退款。
            _village.Add(_gold, definition.GoldCost);
            _village.Add(_material, fromPool);
            // ⚠️ **采购的部分也要退回去**（D-091）：货还在村民手里没变成建材
            // ⇒ 不能白拿人家的货（他们的钱要退回去，货要还回去）。
            _procurement?.Rollback(plan);
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

    /// <summary>
    /// <b>恢复「施工中」的状态</b>（D-083：读档用）。
    /// </summary>
    /// <param name="instances">读档后放在村里、且进度未满的建筑实例。</param>
    /// <remarks>
    /// ⚠️ **为什么必须恢复**：建造服务的「谁在施工」是<b>它自己的一份清单</b>
    /// ⇒ 只恢复进度（建筑实例里的进度）而不恢复这份清单 ⇒
    /// 那栋楼会**永远停在 40%**（没人推进它了）⇒ 玩家的工期白等了。
    /// <para>⚠️ **施工人数不恢复**：人数每帧按「村里可用的人」重新分配（D-082）⇒
    /// 存一个旧的人数反而会让人凭空多出/少掉人手。</para>
    /// </remarks>
    public void RestoreConstructions(IEnumerable<BuildingInstance> instances)
    {
        if (instances is null) throw new ArgumentNullException(nameof(instances));

        foreach (var instance in instances)
        {
            var definition = BuildableBuildings.Find(instance.Id);
            if (definition is null) continue;   // 不是「可建建筑」目录里的 ⇒ 不登记

            _constructions[instance.Id.Value] = new Construction(
                definition.Value, instance, 0, definition.Value.BaseBuildHours);
        }
    }

    /// <summary>
    /// <b>把可用人力分给各在建建筑</b>（D-082：多栋同时施工）。
    /// </summary>
    /// <param name="availableWorkers">村里可用的总人数（<b>只算一次</b>）。</param>
    /// <returns>分完之后每栋各有多少人（按开工顺序）。</returns>
    /// <exception cref="ArgumentOutOfRangeException">人数为负。</exception>
    /// <remarks>
    /// <para>⚠️ <b>为什么必须有这一步</b>：原来每栋在建建筑都拿到<b>全村</b>的人力
    /// ⇒ 围栏与试炼塔同时施工时，<b>同一批人被算了两遍</b>
    /// ⇒ 两栋一起盖比一栋快<b>一倍</b>（人力凭空翻倍）。
    /// ⇒ 现在人力<b>只算一次</b>，按「均分 + 余数给最早开工的」分下去。</para>
    /// <para>⚠️ <b>为什么是「均分」而不是「先到先得」</b>：先到先得 ⇒ 后开工的那栋
    /// 永远拿不到人 ⇒ 变成「排队」⇒ 那就不是「同时施工」了。
    /// 均分 ⇒ 每栋都在推进（人少时可能有人分到 0 ⇒ 那栋停工，合理）。</para>
    /// <para>⚠️ <b>余数给最早开工的</b>：这样「谁先开工」是可观察的差异，
    /// 且分配是<b>确定的</b>（同样的输入 ⇒ 同样的分配 ⇒ 可复现、可离线验算）。</para>
    /// </remarks>
    public IReadOnlyList<(BuildingId Id, int Workers)> AssignWorkers(int availableWorkers)
    {
        if (availableWorkers < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(availableWorkers), availableWorkers, "可用人数不能为负。");
        }

        var ids = _constructions.Keys.ToArray();
        if (ids.Length == 0) return Array.Empty<(BuildingId, int)>();

        int each = availableWorkers / ids.Length;
        int rest = availableWorkers % ids.Length;

        var result = new List<(BuildingId, int)>(ids.Length);
        for (int i = 0; i < ids.Length; i++)
        {
            int workers = each + (i < rest ? 1 : 0);
            _constructions[ids[i]] = _constructions[ids[i]] with { Workers = workers };
            result.Add((new BuildingId(ids[i]), workers));
        }

        return result;
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
