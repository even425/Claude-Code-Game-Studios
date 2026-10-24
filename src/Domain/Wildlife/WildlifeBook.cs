using System;
using System.Collections.Generic;
using System.Linq;
using ClaudeCodeGameStudios.Domain.Village;

namespace ClaudeCodeGameStudios.Domain.Wildlife;

/// <summary>野兽的种类（D-117）。</summary>
public enum BeastKind
{
    /// <summary>野猪：最常见，最好打。</summary>
    Boar = 0,

    /// <summary>狼：比野猪凶一点。</summary>
    Wolf = 1,

    /// <summary>鹿：跑得快、不好抓（打猎的「技术活」）。</summary>
    Deer = 2,
}

/// <summary>
/// 一只<b>村外的野兽</b>（D-117）。
/// </summary>
/// <remarks>
/// <para>⚠️ <b>它只住在村外</b>（<see cref="WorldMap.IsWilderness"/>）⇒
/// 玩家一眼能看出「村外有活物」，而且<b>野兽不会溜进村里</b>。</para>
///
/// <para>⚠️ <b>它不入档</b>：野生生物是「世界的样子」，不是「玩家的进度」
/// ⇒ 读档后重新长出来即可（不占存档、不影响守恒律）。</para>
/// </remarks>
public sealed class WildBeast
{
    /// <summary>野兽的标识。</summary>
    public string Id { get; }

    /// <summary>种类。</summary>
    public BeastKind Kind { get; }

    /// <summary>当前格位 X。</summary>
    public int X { get; private set; }

    /// <summary>当前格位 Y。</summary>
    public int Y { get; private set; }

    /// <summary>朝向/步态相位（0..1，循环）—— 用来让画面上的野兽「在动」。</summary>
    public float Phase { get; private set; }

    // ── D-120：在两格之间**慢慢走过去**（不再瞬移）────────────────────────

    /// <summary>这一步的<b>起点</b>格位 X。</summary>
    public int FromX { get; private set; }

    /// <summary>这一步的<b>起点</summary>格位 Y。</summary>
    public int FromY { get; private set; }

    /// <summary>这一步的<b>目标</b>格位 X。</summary>
    public int TargetX { get; private set; }

    /// <summary>这一步的<b>目标</b>格位 Y。</summary>
    public int TargetY { get; private set; }

    /// <summary>这一步开始时的模拟时刻（秒）。</summary>
    public double StepStartTime { get; private set; }

    /// <summary>一个模拟日有多少秒（用来把「时间」换算成「走了百分之几」）。</summary>
    public double DaySeconds { get; private set; } = 1.0;

    /// <summary>造一只野兽。</summary>
    /// <param name="id">标识。</param>
    /// <param name="kind">种类。</param>
    /// <param name="x">初始格位 X。</param>
    /// <param name="y">初始格位 Y。</param>
    public WildBeast(string id, BeastKind kind, int x, int y)
    {
        if (string.IsNullOrEmpty(id)) throw new ArgumentException("野兽标识不能为空。", nameof(id));
        Id = id;
        Kind = kind;
        X = x;
        Y = y;
        FromX = x;
        FromY = y;
        TargetX = x;
        TargetY = y;
    }

    /// <summary>
    /// 开始「往某一格走过去」（<b>一整天走一格</b> ⇒ 观感是慢悠悠地溜达）。
    /// </summary>
    /// <param name="x">目标格位 X。</param>
    /// <param name="y">目标格位 Y。</param>
    /// <param name="simTime">现在的模拟时刻（秒）。</param>
    /// <param name="daySeconds">一个模拟日的秒数。</param>
    /// <remarks>
    /// ⚠️ <b>为什么不再瞬移</b>（D-120）：原来「每天走一步」是<b>瞬间换格</b>
    /// ⇒ 画面上野兽像被瞬移 ⇒ 观感是「跑得飞快」；改成在一格之间<b>慢慢挪</b>
    /// ⇒ 一天走一格 ⇒ 慢得合理。
    /// <br>⚠️ <b>逻辑位置（<see cref="X"/> / <see cref="Y"/>）立刻更新</b>
    /// ⇒ 「它现在在哪一格」这件事仍然只有一份答案（不与显示位置分叉）。
    /// </remarks>
    public void BeginWalkTo(int x, int y, double simTime, double daySeconds)
    {
        FromX = X;
        FromY = Y;
        TargetX = x;
        TargetY = y;
        StepStartTime = simTime;
        DaySeconds = daySeconds <= 0.0 ? 1.0 : daySeconds;
        X = x;
        Y = y;
    }

    /// <summary>这一步走了百分之几（0..1）。</summary>
    /// <param name="simTime">现在的模拟时刻（秒）。</param>
    public float WalkProgress(double simTime)
    {
        if (DaySeconds <= 0.0) return 1f;
        double t = (simTime - StepStartTime) / DaySeconds;
        if (t < 0.0) t = 0.0;
        if (t > 1.0) t = 1.0;
        return (float)t;
    }

    /// <summary>画面上该画在哪（格位浮点坐标，含格间插值）。</summary>
    /// <param name="simTime">现在的模拟时刻（秒）。</param>
    public float DisplayX(double simTime) => FromX + (TargetX - FromX) * Ease(WalkProgress(simTime));

    /// <summary>画面上该画在哪（格位浮点坐标，含格间插值）。</summary>
    /// <param name="simTime">现在的模拟时刻（秒）。</param>
    public float DisplayY(double simTime) => FromY + (TargetY - FromY) * Ease(WalkProgress(simTime));

    /// <summary>缓动（起步与停步都慢 ⇒ 不像瞬移）。</summary>
    private static float Ease(float t) => t * t * (3f - 2f * t);

    /// <summary>把它挪到某一格（**只允许落在村外**）。</summary>
    /// <param name="x">格位 X。</param>
    /// <param name="y">格位 Y。</param>
    /// <returns>挪成功为 true（落在村里或地图外 ⇒ 拒绝）。</returns>
    public bool TryMoveTo(int x, int y)
    {
        if (!WorldMap.InBounds(x, y)) return false;
        if (!WorldMap.IsWilderness(x, y)) return false;   // ⚠️ 野兽不进村
        X = x;
        Y = y;
        return true;
    }

    /// <summary>推进一点相位（让它看起来在动）。</summary>
    /// <param name="delta">相位增量。</param>
    public void Advance(float delta)
    {
        Phase += delta;
        if (Phase > 1f) Phase -= 1f;
        if (Phase < 0f) Phase += 1f;
    }
}

/// <summary>
/// <b>野外的野兽</b>（D-117）—— 谁在哪儿、它们怎么游走。
/// </summary>
/// <remarks>
/// <para>⚠️ <b>规则住在领域层</b>（可离线单测）⇒ 表现层只负责「照着画」
/// ⇒ 改坏游走规则时守卫立刻红。</para>
/// </remarks>
public sealed class WildlifeBook
{
    private readonly List<WildBeast> _beasts = new();

    /// <summary>当前的野兽。</summary>
    public IReadOnlyList<WildBeast> Beasts => _beasts;

    /// <summary>某种野兽有多少只（按种类数）。</summary>
    /// <param name="kind">种类。</param>
    /// <returns>数量。</returns>
    public int CountOf(BeastKind kind) => _beasts.Count(b => b.Kind == kind);

    /// <summary>
    /// 生成一批野兽（每只都落在<b>村外</b>）。
    /// </summary>
    /// <param name="count">要生成多少只。</param>
    /// <param name="roll">取格位用的随机数（0..1 的浮点；由调用方提供 ⇒ 可复现）。</param>
    /// <param name="kindOf">第几只该是什么种类（由调用方提供 ⇒ 可复现）。</param>
    public void Seed(int count, Func<float> roll, Func<int, BeastKind> kindOf)
    {
        if (roll is null) throw new ArgumentNullException(nameof(roll));
        if (kindOf is null) throw new ArgumentNullException(nameof(kindOf));

        _beasts.Clear();
        for (int i = 0; i < count; i++)
        {
            // ⚠️ **必须落在村外** ⇒ 反复取点直到取到（上限 64 次 ⇒ 极端情况下放弃这一只）。
            bool placed = false;
            for (int attempt = 0; attempt < 64 && !placed; attempt++)
            {
                int x = WorldMap.OriginX + (int)(roll() * WorldMap.Width);
                int y = WorldMap.OriginY + (int)(roll() * WorldMap.Height);
                if (!WorldMap.IsWilderness(x, y)) continue;

                _beasts.Add(new WildBeast($"beast-{i}", kindOf(i), x, y));
                placed = true;
            }
        }
    }

    /// <summary>
    /// 让所有野兽**走一步**（每天一次）。
    /// </summary>
    /// <param name="roll">取方向用的随机数（0..1）。</param>
    /// <returns>这次真正挪动了多少只（挪不动就原地不动）。</returns>
    /// <remarks>
    /// ⚠️ **野兽不会跑进村子** ⇒ 走向里若落进村子 ⇒ 那一步**不走**
    /// （宁可原地踏步，也不要让野兽出现在村口）。
    /// </remarks>
    public int WanderOneStep(Func<float> roll)
    {
        if (roll is null) throw new ArgumentNullException(nameof(roll));

        int moved = 0;
        foreach (var beast in _beasts)
        {
            // 四个方向：右上、右下、左下、左上（等距投影的四个邻格）。
            int dir = (int)(roll() * 4f) % 4;
            int dx = dir == 0 || dir == 1 ? 1 : -1;
            int dy = dir == 1 || dir == 2 ? 1 : -1;

            if (beast.TryMoveTo(beast.X + dx, beast.Y + dy)) moved++;
        }

        return moved;
    }

    /// <summary>
    /// 让所有野兽**走一步**（每天一次）—— <b>并且是一整天慢慢走过去</b>（D-120）。
    /// </summary>
    /// <param name="roll">取方向用的随机数（0..1）。</param>
    /// <param name="simTime">现在的模拟时刻（秒）。</param>
    /// <param name="daySeconds">一个模拟日的秒数。</param>
    /// <returns>这次真正挪动了多少只（挪不动就原地不动）。</returns>
    /// <remarks>
    /// ⚠️ **野兽不会跑进村子** ⇒ 走向里若落进村子 ⇒ 那一步**不走**
    /// （宁可原地踏步，也不要让野兽出现在村口）。
    /// <br>⚠️ <b>走的过程是一整天</b> ⇒ 画面上它是在**格与格之间挪**，
    /// 不是瞬间换格（D-120：原来那一下看起来像「跑得飞快」）。
    /// </remarks>
    public int WanderOneStep(Func<float> roll, double simTime, double daySeconds)
    {
        if (roll is null) throw new ArgumentNullException(nameof(roll));

        int moved = 0;
        foreach (var beast in _beasts)
        {
            int dir = (int)(roll() * 4f) % 4;
            int dx = dir == 0 || dir == 1 ? 1 : -1;
            int dy = dir == 1 || dir == 2 ? 1 : -1;

            int tx = beast.X + dx;
            int ty = beast.Y + dy;
            if (!WorldMap.InBounds(tx, ty)) continue;
            if (!WorldMap.IsWilderness(tx, ty)) continue;   // ⚠️ 野兽不进村

            beast.BeginWalkTo(tx, ty, simTime, daySeconds);
            moved++;
        }

        return moved;
    }

    /// <summary>推进所有野兽的相位（画面上的「在动」）。</summary>
    /// <param name="delta">相位增量。</param>
    public void AdvancePhase(float delta)
    {
        foreach (var beast in _beasts) beast.Advance(delta);
    }

    /// <summary>
    /// 放一只野兽进野外（测试夹具 / 读档后重新长出来时用）。
    /// </summary>
    /// <param name="beast">要放的那只。</param>
    /// <remarks>
    /// ⚠️ <b>不检查它在不在村外</b>（放进去就是放进去）⇒
    /// 想验「野兽不进村」请用 <see cref="WildBeast.TryMoveTo"/> 与 <see cref="Seed"/>。
    /// </remarks>
    public void Add(WildBeast beast)
    {
        if (beast is null) throw new ArgumentNullException(nameof(beast));
        _beasts.Add(beast);
    }

    /// <summary>清空（开局 / 读档后重新长出来）。</summary>
    public void Clear() => _beasts.Clear();

    /// <summary>
    /// 移走一只野兽（它被猎到了，D-118）。
    /// </summary>
    /// <param name="beast">要移走的那只。</param>
    /// <returns>真的移走了为 true（本来就不在簿子里 ⇒ false）。</returns>
    public bool Remove(WildBeast beast)
    {
        if (beast is null) return false;
        return _beasts.Remove(beast);
    }

    /// <summary>
    /// 野外补货：野兽被猎走之后，<b>过一阵子会有新的来</b>（D-118）。
    /// </summary>
    /// <param name="maxCount">野外最多几只。</param>
    /// <param name="rollChance">这次「来新兽」的概率（0..1）。</param>
    /// <param name="roll">取格位用的随机数（0..1）。</param>
    /// <param name="kindOf">新兽的种类。</param>
    /// <returns>新长出来几只。</returns>
    /// <remarks>
    /// ⚠️ **为什么要补货**：否则猎几只就没了 ⇒ 野外会**越打越空**
    /// ⇒ 玩家跑到后面就没东西可猎。
    /// </remarks>
    public int Respawn(int maxCount, double rollChance, Func<float> roll, Func<int, BeastKind> kindOf)
    {
        if (roll is null) throw new ArgumentNullException(nameof(roll));
        if (kindOf is null) throw new ArgumentNullException(nameof(kindOf));
        if (_beasts.Count >= maxCount) return 0;
        if (roll() >= rollChance) return 0;

        int before = _beasts.Count;
        int id = _beasts.Count;
        for (int attempt = 0; attempt < 64; attempt++)
        {
            int x = WorldMap.OriginX + (int)(roll() * WorldMap.Width);
            int y = WorldMap.OriginY + (int)(roll() * WorldMap.Height);
            if (!WorldMap.IsWilderness(x, y)) continue;

            _beasts.Add(new WildBeast($"beast-r{id}", kindOf(id), x, y));
            break;
        }

        return _beasts.Count - before;
    }
}
