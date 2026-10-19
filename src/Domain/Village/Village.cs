using System;
using System.Collections.Generic;
using System.Linq;

namespace ClaudeCodeGameStudios.Domain.Village;

/// <summary>资源标识。<b>第一阶段不预置任何具体种类</b>。</summary>
/// <remarks>
/// 资源的种类属【待定】（基线 §16 #12「经济价格、资源产出」；<c>07</c> 的 P-08）。
/// 原型里曾有一份六种材料的清单（<c>stone/dirt/cloth/wood/salvage/iron</c>），
/// 来自 <c>design/registry/entities.yaml</c>，**已被 D-002 判死**，不得复活。
/// 因此本类型是<b>开放的</b>：村庄可以持有任意种类的资源，由调用方定义。
/// </remarks>
/// <param name="Value">资源标识。</param>
public readonly record struct ResourceId(string Value)
{
    /// <inheritdoc />
    public override string ToString() => string.IsNullOrEmpty(Value) ? "(无)" : Value;
}

/// <summary>建筑标识。</summary>
/// <param name="Value">建筑标识。</param>
public readonly record struct BuildingId(string Value)
{
    /// <inheritdoc />
    public override string ToString() => string.IsNullOrEmpty(Value) ? "(无)" : Value;
}

/// <summary>建筑在场状态。</summary>
/// <remarks>
/// <para><b>刻意不含「等级 / tier」。</b>0→5 的直线层级模型已被
/// <c>07</c> 技术债 <b>B2</b> 判死（基线 §12.2：「不暗示六个阶段就是强制线性等级」）。
/// 旧的线性模型「已被 <c>functional-buildings.md</c> 的依赖网明确取代」。</para>
/// <para>建筑之间是否存在相互影响、以及城市如何量化成长，属【待定】（基线 §12.3 / §12.2）。</para>
/// </remarks>
public enum BuildingStatus
{
    /// <summary>在建。占用场地，但不产生任何效果。</summary>
    UnderConstruction = 0,

    /// <summary>已建成。</summary>
    Completed = 1,
}

/// <summary>一栋建筑实例。</summary>
public sealed class BuildingInstance
{
    /// <summary>构造。</summary>
    /// <param name="id">建筑类型标识。</param>
    /// <param name="gridX">占据格位的左下角 X。</param>
    /// <param name="gridY">占据格位的左下角 Y。</param>
    /// <param name="footprintW">占据的格宽。</param>
    /// <param name="footprintH">占据的格高。</param>
    /// <param name="constructionProgress">建造进度，<c>[0,1]</c>。</param>
    /// <param name="affectsAreaW">
    /// <b>M5 / D-033</b> 防护 / 影响区宽度（X 方向半径，格数）。0 = 不影响（M3 旧建筑兼容）。
    /// 例：货摊 1×1 footprint，affects 1×1；市集 3×3 footprint，affects 5×5。
    /// 摊位必须在<b>至少一个</b>建筑的 affects 范围内才能摆。
    /// </param>
    /// <param name="affectsAreaH">M5 / D-033 防护 / 影响区高度（Y 方向半径）。</param>
    /// <exception cref="ArgumentOutOfRangeException">占地尺寸非正、affects 负数，或进度越界。</exception>
    public BuildingInstance(
        BuildingId id, int gridX, int gridY, int footprintW, int footprintH,
        double constructionProgress = 0.0,
        int affectsAreaW = 0, int affectsAreaH = 0)
    {
        if (footprintW <= 0 || footprintH <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(footprintW), $"占地必须为正，实际 {footprintW}×{footprintH}。");
        }

        if (affectsAreaW < 0 || affectsAreaH < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(affectsAreaW), $"Affects 范围不得为负，实际 {affectsAreaW}×{affectsAreaH}。");
        }

        if (!double.IsFinite(constructionProgress) || constructionProgress < 0.0 || constructionProgress > 1.0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(constructionProgress), constructionProgress, "建造进度必须落在 [0,1]。");
        }

        Id = id;
        GridX = gridX;
        GridY = gridY;
        FootprintW = footprintW;
        FootprintH = footprintH;
        ConstructionProgress = constructionProgress;
        AffectsAreaW = affectsAreaW;
        AffectsAreaH = affectsAreaH;
    }

    /// <summary>建筑类型。</summary>
    public BuildingId Id { get; }

    /// <summary>占据格位的左下角 X。</summary>
    public int GridX { get; }

    /// <summary>占据格位的左下角 Y。</summary>
    public int GridY { get; }

    /// <summary>占据的格宽。</summary>
    public int FootprintW { get; }

    /// <summary>
    /// M5 / D-033：影响区 X 方向半径（格数）。0 = 不影响任何地方。
    /// </summary>
    /// <remarks>
    /// 摊位摆摊时必须落在<b>至少一个</b>已建成建筑的影响区（<see cref="IsCellInAffectsArea"/>）。
    /// 例：广场 1×1 footprint + 9×9 affects → 摊位可摆在自己 + 周围 8 格（半径 4）。
    /// </remarks>
    public int AffectsAreaW { get; }

    /// <summary>M5 / D-033：影响区 Y 方向半径。</summary>
    public int AffectsAreaH { get; }

    /// <summary>
    /// 询问某格 (x, y) 是否落在本建筑的<b>影响区</b>。
    /// </summary>
    /// <remarks>
    /// <b>边界</b>：<c>AffectsAreaW=1</c> 时，只覆盖 1 格（自身 footprint 那格），
    /// <b>不</b>算 0 距离也算——要周围 1 格需要 <c>AffectsAreaW=2</c>。
    /// </remarks>
    public bool IsCellInAffectsArea(int x, int y)
    {
        if (AffectsAreaW <= 0 || AffectsAreaH <= 0) return false;
        if (Status != BuildingStatus.Completed) return false;   // 未建成 = 不影响
        // 影响区范围 = (footprint 中心 ± 半径)？v1 简化：以 footprint 左下角为基准
        // int 范围 [footprintW - affectsAreaW, footprintW + affectsAreaW] 周延到格子
        // 真正的"半径语义"需要几何学；M5 v1 走**位置检查**：x 在 [gridX - (affectsAreaW - 1), gridX + footprintW - 1 + (affectsAreaW - 1)]
        // 简化：影响区 = [footprint 范围] 向外扩 (affectsAreaW - 1) 格
        int minX = GridX - (AffectsAreaW - 1);
        int maxX = GridX + FootprintW - 1 + (AffectsAreaW - 1);
        int minY = GridY - (AffectsAreaH - 1);
        int maxY = GridY + FootprintH - 1 + (AffectsAreaH - 1);
        return x >= minX && x <= maxX && y >= minY && y <= maxY;
    }

    /// <summary>占据的格高。</summary>
    public int FootprintH { get; }

    /// <summary>建造进度，<c>[0,1]</c>。1 表示建成。</summary>
    public double ConstructionProgress { get; private set; }

    /// <summary>在场状态。</summary>
    public BuildingStatus Status =>
        ConstructionProgress >= 1.0 ? BuildingStatus.Completed : BuildingStatus.UnderConstruction;

    /// <summary>推进建造进度。<b>只增不减</b>。</summary>
    /// <param name="delta">进度增量。</param>
    /// <exception cref="ArgumentOutOfRangeException">增量非负有限数。</exception>
    public void AdvanceConstruction(double delta)
    {
        if (!double.IsFinite(delta) || delta < 0.0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(delta), delta, "建造进度只增不减（不允许负增量）。");
        }

        ConstructionProgress = Math.Min(1.0, ConstructionProgress + delta);
    }

    /// <summary>该建筑占据的每一格。</summary>
    /// <returns>格位坐标集合。</returns>
    public IEnumerable<(int X, int Y)> OccupiedCells()
    {
        for (var x = GridX; x < GridX + FootprintW; x++)
        {
            for (var y = GridY; y < GridY + FootprintH; y++)
            {
                yield return (x, y);
            }
        }
    }
}

/// <summary>村庄 —— 建筑实例与资源存量的<b>唯一写者</b>。</summary>
/// <remarks>
/// <para>依据 <c>05</c> §2.3：村庄与建筑模块负责「村庄实例、建筑实例与其在场状态、
/// 村庄资源存量」，<b>不负责建筑功能的具体效果</b>（基线 §12.3 明确未定）。</para>
/// <para><b>不变量 I12：资源不会变为负数。</b>扣减前必须检查充足。</para>
/// <para><b>不变量 I10：唯一写者。</b>别的模块只能读。</para>
/// </remarks>
public sealed class Village
{
    private readonly Dictionary<string, long> _resources = new(StringComparer.Ordinal);
    private readonly Dictionary<string, BuildingInstance> _buildings = new(StringComparer.Ordinal);

    /// <summary>村庄实例标识。</summary>
    public string Id { get; }

    /// <summary>已放置的建筑，按放置顺序。</summary>
    public IReadOnlyList<BuildingInstance> Buildings => _buildings.Values.ToList();

    /// <param name="id">村庄标识。</param>
    /// <exception cref="ArgumentException">标识为空。</exception>
    public Village(string id)
    {
        if (string.IsNullOrEmpty(id)) throw new ArgumentException("村庄标识不能为空。", nameof(id));
        Id = id;
    }

    // ── 资源 ────────────────────────────────────────────────────────────────

    /// <summary>当前持有量；未持有过该种类时返回 0。</summary>
    /// <param name="resource">资源种类。</param>
    /// <returns>持有量。</returns>
    public long Get(ResourceId resource)
        => resource.Value.Length > 0 && _resources.TryGetValue(resource.Value, out var n) ? n : 0L;

    /// <summary>增加资源。</summary>
    /// <param name="resource">资源种类。</param>
    /// <param name="amount">增量，必须为正。</param>
    /// <exception cref="ArgumentOutOfRangeException">增量为非正数或非有限数。</exception>
    /// <exception cref="OverflowException">结果超出 long 范围。</exception>
    public void Add(ResourceId resource, long amount)
    {
        if (amount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(amount), amount, "增量必须为正。");
        }

        var next = checked(Get(resource) + amount);
        _resources[resource.Value] = next;
    }

    /// <summary>
    /// 扣减资源。<b>扣减前检查充足</b>（不变量 I12）——
    /// 资源不会被扣成负数，而是<b>拒绝扣减</b>。
    /// </summary>
    /// <param name="resource">资源种类。</param>
    /// <param name="amount">扣减量，必须为正。</param>
    /// <returns><c>true</c> 表示已扣减；<c>false</c> 表示持有量不足，**状态未改变</b>。</returns>
    /// <exception cref="ArgumentOutOfRangeException">扣减量为非正数。</exception>
    public bool TrySpend(ResourceId resource, long amount)
    {
        if (amount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(amount), amount, "扣减量必须为正。");
        }

        if (Get(resource) < amount) return false;

        _resources[resource.Value] = Get(resource) - amount;
        return true;
    }

    /// <summary>当前持有的全部资源种类及其数量。</summary>
    public IReadOnlyDictionary<string, long> Snapshot()
        => new Dictionary<string, long>(_resources, StringComparer.Ordinal);

    // ── 建筑 ────────────────────────────────────────────────────────────────

    /// <summary>
    /// 放置一栋建筑。<b>占位冲突会被拒绝</b>（基线 §13.7「建筑坐标与占地冲突」）。
    /// </summary>
    /// <param name="building">建筑实例。</param>
    /// <returns>放置成功时为 <c>true</c>；占位与已有建筑重叠时为 <c>false</c>，<b>状态未改变</b>。</returns>
    /// <exception cref="ArgumentNullException"><paramref name="building"/> 为 null。</exception>
    public bool TryPlace(BuildingInstance building)
    {
        if (building is null) throw new ArgumentNullException(nameof(building));

        foreach (var (x, y) in building.OccupiedCells())
        {
            foreach (var existing in _buildings.Values)
            {
                foreach (var (ex, ey) in existing.OccupiedCells())
                {
                    if (ex == x && ey == y) return false;
                }
            }
        }

        var key = $"{building.Id.Value}@{building.GridX},{building.GridY}";
        _buildings[key] = building;
        return true;
    }

    /// <summary>该格位是否已被占用。</summary>
    /// <param name="x">格位 X。</param>
    /// <param name="y">格位 Y。</param>
    /// <returns>已占用时为 <c>true</c>。</returns>
    public bool IsOccupied(int x, int y)
    {
        foreach (var b in _buildings.Values)
        {
            foreach (var (bx, by) in b.OccupiedCells())
            {
                if (bx == x && by == y) return true;
            }
        }
        return false;
    }
}
