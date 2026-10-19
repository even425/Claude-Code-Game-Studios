using System;
using ClaudeCodeGameStudios.Domain.Heroes;

namespace ClaudeCodeGameStudios.Domain.Relationships;

/// <summary>
/// 一段关系的<b>有向</b>标识：<c>From → To</c>。
/// </summary>
/// <remarks>
/// <para><b>为什么有向</b>：基线 §9.3「关系是双向且可不对称的（A 恨 B，B 不一定恨 A）」，
/// 并已落成 <c>05</c> 不变量 <b>I8</b>「A→B 与 B→A 分别存储」。
/// 因此本类型<b>不是</b>无序对：<c>(A,B)</c> 与 <c>(B,A)</c> 是两个不同的键。</para>
/// <para><b>构造期就拒绝非法引用</b>：空 ID、自己与自己。让非法关系在类型层面
/// 根本建不起来，而不是等写入时再检查 —— 后者总有一条路径会漏。</para>
/// </remarks>
public readonly record struct RelationshipKey
{
    /// <summary>关系的一方（发起 / 持有这份态度的人）。</summary>
    public HeroId From { get; }

    /// <summary>关系的另一方（被看待的人）。</summary>
    public HeroId To { get; }

    /// <summary>
    /// 构造一段有向关系标识。
    /// </summary>
    /// <param name="from">一方英雄标识。</param>
    /// <param name="to">另一方英雄标识。</param>
    /// <exception cref="ArgumentException">
    /// 任一标识为空，或 <paramref name="from"/> 与 <paramref name="to"/> 相同。
    /// </exception>
    public RelationshipKey(HeroId from, HeroId to)
    {
        if (string.IsNullOrEmpty(from.Value))
        {
            throw new ArgumentException("关系的一方标识不能为空。", nameof(from));
        }

        if (string.IsNullOrEmpty(to.Value))
        {
            throw new ArgumentException("关系的另一方标识不能为空。", nameof(to));
        }

        if (from == to)
        {
            throw new ArgumentException(
                $"英雄「{from.Value}」不能与自己建立关系。", nameof(from));
        }

        From = from;
        To = to;
    }

    /// <inheritdoc />
    public override string ToString() => $"{From.Value}->{To.Value}";
}

/// <summary>
/// 关系值 —— <b>单值有符号整数</b>，范围 <c>[-100, +100]</c>，初始 0。
/// </summary>
/// <remarks>
/// <para><b>决策来源：</b><c>07</c> <b>D-036 / P3-D01、P3-D03</b>（开发者 2026-10-19 确认）：
/// 第三阶段先做单值模型；「信任 / 好感」二维留到有欺骗、冲突内容时再拆。</para>
/// <para><b>构造期校验范围，更新时 clamp：</b>存档里出现越界值是数据损坏（加载期拒绝）；
/// 运行中的 <c>+2</c> 累计到边界则应停在边界，而不是让关系越界或抛异常。</para>
/// </remarks>
public readonly record struct Relationship
{
    /// <summary>关系值下限。</summary>
    public const int MinValue = -100;

    /// <summary>关系值上限。</summary>
    public const int MaxValue = 100;

    /// <summary>初始值（没有关系记录 = 这个值）。</summary>
    public const int InitialValue = 0;

    /// <summary>当前关系值。</summary>
    public int Value { get; }

    /// <summary>
    /// 构造一个关系值。越界即抛 —— 越界只可能来自损坏的存档或错误的计算。
    /// </summary>
    /// <param name="value">关系值。</param>
    /// <exception cref="ArgumentOutOfRangeException">值不在 <c>[-100, +100]</c>。</exception>
    public Relationship(int value)
    {
        if (value < MinValue || value > MaxValue)
        {
            throw new ArgumentOutOfRangeException(
                nameof(value), value,
                $"关系值必须在 [{MinValue}, {MaxValue}] 内。越界是数据错误，不是可静默修正的状况。");
        }

        Value = value;
    }

    /// <summary>没有关系记录时的值（0）。</summary>
    public static Relationship Initial => new(InitialValue);

    /// <summary>把任意整数夹到合法范围。</summary>
    /// <param name="value">原始值。</param>
    /// <returns><c>[-100, +100]</c> 内的值。</returns>
    public static int Clamp(int value) => Math.Clamp(value, MinValue, MaxValue);

    /// <summary>
    /// 在当前值上叠加一个变化量，并夹到合法范围。
    /// <b>用 <see cref="long"/> 求和</b>，避免 <c>int</c> 溢出把正加成负。
    /// </summary>
    /// <param name="delta">变化量，可为负。</param>
    /// <returns>新的关系值（clamp 后）。</returns>
    public Relationship WithDelta(int delta)
    {
        long sum = (long)Value + delta;
        return new Relationship((int)Math.Clamp(sum, MinValue, MaxValue));
    }
}

/// <summary>关系的展示阶段 —— 只由关系值派生，<b>不是</b>独立存储的状态。</summary>
/// <remarks>
/// <para><b>决策来源：</b><c>07</c> <b>D-036 / P3-D09</b>：杂谈只显示「关系阶段跨越」，
/// 避免 37 人村庄里每一次社交都刷屏。</para>
/// <para><b>阈值是占位值：</b>敌对 ≤ -20 / 冷淡 -19..-1 / 普通 0 / 熟识 1..19 / 亲近 ≥ 20。
/// 真值待开发者拍板；本阶段只保证「阶段由值派生、跨档可检测」。</para>
/// </remarks>
public enum RelationshipStage
{
    /// <summary>敌对（≤ -20）。</summary>
    Hostile = 0,

    /// <summary>冷淡（-19 ~ -1）。</summary>
    Cold = 1,

    /// <summary>普通（0，没有关系记录也是这个阶段）。</summary>
    Neutral = 2,

    /// <summary>熟识（1 ~ 19）。</summary>
    Acquainted = 3,

    /// <summary>亲近（≥ 20）。</summary>
    Close = 4,
}

/// <summary>
/// 关系规则常量与派生计算 —— <b>纯函数</b>，不持有状态。
/// </summary>
/// <remarks>
/// <para><b>所有数值都是占位值</b>（<c>07</c> D-036 明确）：范围 ±100、社交 +2、同队副本 +3、
/// 阶段阈值、目标权重系数。开发者拍板后整体替换，调用方一行不用改。</para>
/// <para><b>关系影响行为只改权重、不改合法性</b>：<see cref="TargetWeight"/> 恒为正，
/// 因此「讨厌一个人」只会降低被搭话的概率，<b>不会</b>让任何行为变成不可能
/// （CR-1；<c>05</c> §5.1 I8）。</para>
/// </remarks>
public static class RelationshipRules
{
    /// <summary>一次社交完成后，双方各自的关系变化量。<b>占位值。</b></summary>
    public const int SocialDelta = 2;

    /// <summary>一次同队副本成功后，参与双方各自的关系变化量。<b>占位值。</b></summary>
    public const int CooperationDelta = 3;

    /// <summary>敌对阶段上界（≤ 此值为敌对）。<b>占位值。</b></summary>
    public const int HostileThreshold = -20;

    /// <summary>亲近阶段下界（≥ 此值为亲近）。<b>占位值。</b></summary>
    public const int CloseThreshold = 20;

    /// <summary>关系值 → 目标权重时的除数（`affinity / 50`）。<b>占位值。</b></summary>
    public const double TargetWeightScale = 50.0;

    /// <summary>目标权重下限（恒正，永不把候选权重压到 0）。</summary>
    public const double MinTargetWeight = 0.1;

    /// <summary>目标权重上限。</summary>
    public const double MaxTargetWeight = 3.0;

    /// <summary>由一个关系值求出展示阶段。</summary>
    /// <param name="value">关系值。</param>
    /// <returns>展示阶段。</returns>
    public static RelationshipStage StageOf(int value)
    {
        if (value <= HostileThreshold) return RelationshipStage.Hostile;
        if (value < 0) return RelationshipStage.Cold;
        if (value == 0) return RelationshipStage.Neutral;
        if (value < CloseThreshold) return RelationshipStage.Acquainted;
        return RelationshipStage.Close;
    }

    /// <summary>
    /// 关系对「选择社交对象」的权重修正。
    /// </summary>
    /// <remarks>
    /// <b>公式（P3-D06，占位）：</b><c>W = clamp(1 + affinity / 50, 0.1, 3.0)</c>。
    /// 关系越好越容易被搭话，越差越不容易 —— 但<b>永远 &gt; 0</b>，
    /// 因此只改变概率、不构成「不可能」。
    /// </remarks>
    /// <param name="affinity">发起者对目标的关系值。</param>
    /// <returns>恒为正的目标权重。</returns>
    public static double TargetWeight(int affinity)
    {
        double w = 1.0 + affinity / TargetWeightScale;
        return Math.Clamp(w, MinTargetWeight, MaxTargetWeight);
    }
}
