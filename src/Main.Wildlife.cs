using Godot;
using System;
using ClaudeCodeGameStudios.Domain.Wildlife;

public partial class Main : Node2D
{
    /// <summary>
    /// <b>村外的野兽</b>（D-117）—— 领域层持有（可离线单测），这里只负责接线。
    /// </summary>
    private readonly ClaudeCodeGameStudios.Domain.Wildlife.WildlifeBook _wildlife = new();

    /// <summary>野兽的只数（D-117：建议值）。</summary>
    private const int WildlifeCount = 8;

    /// <summary>
    /// 村外的野兽（D-117）：<b>开局长一批</b>，之后每天走一步。
    /// </summary>
    /// <remarks>
    /// <para>⚠️ **数量是建议值**（8 只）：够让「村外有活物」一眼可见，
    /// 又不至于变成「满地都是」。</para>
    /// <para>⚠️ **用 <c>_rng</c>（存档里的那个随机流）** ⇒ 同一个存档种子 ⇒ 同一片野外
    /// ⇒ 复现（不另开一条随机流 ⇒ 不会打乱别的系统的随机序列）。</para>
    /// </remarks>
    private void SeedWildlife()
    {
        _wildlife.Seed(
            WildlifeCount,
            // ⚠️ `NextDouble` 要一个上界 ⇒ 给 1.0 就是 [0,1) 的浮点。
            () => (float)_rng.NextDouble(1.0),
            i => (BeastKind)(i % 3));
    }

    /// <summary>野兽每天走一步（夜里活动）。</summary>
    private void TickWildlife()
    {
        if (_wildlife.Beasts.Count == 0) return;   // 还没长出来（读档后会被重新长）

        _wildlife.WanderOneStep(() => (float)_rng.NextDouble(1.0));
    }

    /// <summary>野兽的步态相位（每帧推进 ⇒ 画面上它们在轻轻动）。</summary>
    /// <param name="delta">帧间隔（秒）。</param>
    public void AdvanceWildlifePhase(float delta)
        => _wildlife.AdvancePhase(delta * 0.6f);
}
