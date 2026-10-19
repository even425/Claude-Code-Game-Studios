using System;
using System.Collections.Generic;
using System.Linq;
using ClaudeCodeGameStudios.Domain.Heroes;

namespace ClaudeCodeGameStudios.Domain.Quests;

/// <summary>任务标识。</summary>
/// <param name="Value">标识值。</param>
public readonly record struct MissionId(string Value)
{
    /// <inheritdoc />
    public override string ToString() => string.IsNullOrEmpty(Value) ? "(无)" : Value;
}

/// <summary>任务生命周期状态。</summary>
/// <remarks>
/// 状态机是单向的：进行中的任务<b>不会回到已结束</b>。
/// 那正是不变量 I14（已结束的任务不会回到「进行中」）的字面形态。
/// </remarks>
public enum MissionStatus
{
    /// <summary>已创建但尚未开始。</summary>
    Created = 0,

    /// <summary>进行中。</summary>
    Active = 1,

    /// <summary>已成功完成。</summary>
    Succeeded = 2,

    /// <summary>已失败。</summary>
    Failed = 3,

    /// <summary>已中止（如全队撤回）。</summary>
    Aborted = 4,
}

/// <summary>
/// 队伍规则 —— 合法性判定的<b>注入点</b>。
/// </summary>
/// <remarks>
/// <para>
/// <b>为什么是注入而不是硬编码：</b>队伍人数上限属【待定】（<c>07</c> 的 <b>P-10</b>、
/// 基线 §16 #5）。原型里曾有一个 <c>CohesionOf()</c>：按<b>性格计数</b>输出配合系数
/// （两个傲 0.55×、两个贪 0.85×）—— 那是<b>出厂属性替代关系发展</b>，违反基线 §9.3，
/// 已随时区系统一并作废。<b>本模块不重蹈覆辙。</b>
/// </para>
/// <para>
/// <b>两种规则必须分开，不能共用一条：</b>
/// </para>
/// <list type="bullet">
/// <item>
/// <b><see cref="CanJoin"/>（单人前置校验）</b> —— 这名英雄<b>此刻能不能</b>被加进来。
/// 对应 <c>03</c> §3.1 的 <b>S5 前置校验</b>。
/// </item>
/// <item>
/// <b><see cref="IsRosterLegal"/>（队伍完备性）</b> —— 这支队伍<b>够不够格出发</b>。
/// 对应 <c>02</c> <b>F-01</b>「无效队伍不能启动任务」。
/// </item>
/// </list>
/// <para>
/// <b>合成一条会造成死锁</b>：若「人数下限 2」同时用于两者，则逐个加入永远跨不过下限，
/// 队伍<b>永远建不起来</b>。这是本模块在写测试时被实测抓到的设计缺陷。
/// </para>
/// </remarks>
public sealed class SquadRules
{
    private readonly Func<HeroState, bool> _canJoin;
    private readonly Func<IReadOnlyList<HeroState>, bool> _isRosterLegal;

    /// <param name="canJoin">单人前置校验。传 null 表示任何人都能加。</param>
    /// <param name="isRosterLegal">队伍完备性。传 null 表示任何队伍都算完备。</param>
    public SquadRules(
        Func<HeroState, bool>? canJoin = null,
        Func<IReadOnlyList<HeroState>, bool>? isRosterLegal = null)
    {
        _canJoin = canJoin ?? (_ => true);
        _isRosterLegal = isRosterLegal ?? (_ => true);
    }

    /// <summary>这名英雄此刻能否被加入（S5 前置校验）。</summary>
    /// <param name="hero">候选英雄。</param>
    public bool CanJoin(HeroState hero) => _canJoin(hero);

    /// <summary>这支队伍是否够格出发（F-01）。</summary>
    /// <param name="members">候选成员。</param>
    public bool IsRosterLegal(IReadOnlyList<HeroState> members) => _isRosterLegal(members);
}

/// <summary>一支队伍。</summary>
public sealed class Squad
{
    private readonly List<HeroState> _members = new();

    /// <param name="rules">合法性规则。第一阶段通常由调用方注入。</param>
    /// <param name="members">初始成员；会逐个经<b>单人前置校验</b>。</param>
    /// <exception cref="ArgumentNullException">成员集合中含 null。</exception>
    public Squad(SquadRules rules, IEnumerable<HeroState>? members = null)
    {
        Rules = rules ?? throw new ArgumentNullException(nameof(rules));
        if (members is null) return;

        var list = members.ToList();
        if (list.Any(m => m is null)) throw new ArgumentNullException(nameof(members));

        // 注意：初始成员只做**单人前置校验**，不检查队伍完备性 ——
        // 完备性是「出发前」的事（F-01），在建队过程中检查它会让队伍永远建不起来。
        foreach (var m in list)
        {
            if (!Rules.CanJoin(m))
            {
                throw new ArgumentException($"英雄 {m.Id} 不满足加入前置校验。", nameof(members));
            }
        }

        _members.AddRange(list);
    }

    /// <summary>合法性规则。</summary>
    public SquadRules Rules { get; }

    /// <summary>成员。只读。</summary>
    public IReadOnlyList<HeroState> Members => _members;

    /// <summary>当前队伍是否<b>够格出发</b>（F-01）。它可以随成员变动而失效。</summary>
    public bool IsRosterReady => Rules.IsRosterLegal(_members);

    /// <summary>
    /// 加入一名成员。检查的是<b>单人前置校验</b>（S5），不是队伍完备性 ——
    /// 否则带人数下限的规则会让队伍永远建不起来。
    /// 拒绝时队伍<b>保持不变</b>（<c>02</c> F-02 / F-03）。
    /// </summary>
    /// <param name="hero">要加入的英雄。</param>
    /// <returns>加入成功时为 <c>true</c>。</returns>
    public bool TryJoin(HeroState hero)
    {
        if (hero is null) throw new ArgumentNullException(nameof(hero));
        if (!Rules.CanJoin(hero)) return false;

        _members.Add(hero);
        return true;
    }

    /// <summary>移出一名成员。</summary>
    /// <param name="hero">要移出的英雄。</param>
    /// <returns>原本在该队中时为 <c>true</c>。</returns>
    public bool TryLeave(HeroState hero) => hero is not null && _members.Remove(hero);
}

/// <summary>一个任务（一次派遣）。</summary>
public sealed class Mission
{
    private readonly List<HeroState> _members = new();

    private Mission(MissionId id, string name, IReadOnlyList<HeroState> members)
    {
        Id = id;
        Name = name;
        _members.AddRange(members);
        Status = MissionStatus.Created;
    }

    /// <summary>任务标识。</summary>
    public MissionId Id { get; }

    /// <summary>任务名称。</summary>
    public string Name { get; }

    /// <summary>成员。只读。</summary>
    public IReadOnlyList<HeroState> Members => _members;

    /// <summary>当前状态。单向迁移，不会回到 <see cref="MissionStatus.Active"/>。</summary>
    public MissionStatus Status { get; private set; }

    /// <summary>是否已结束（成功 / 失败 / 中止）。</summary>
    public bool IsFinished =>
        Status is MissionStatus.Succeeded or MissionStatus.Failed or MissionStatus.Aborted;

    /// <summary>
    /// 创建一个任务。
    /// </summary>
    /// <param name="id">任务标识。</param>
    /// <param name="name">任务名称。</param>
    /// <param name="squad">队伍。</param>
    /// <param name="rules">合法性规则。</param>
    /// <returns>新建的任务（状态为 <see cref="MissionStatus.Created"/>）。</returns>
    /// <exception cref="ArgumentException">队伍当前不合法 —— 无效队伍不能启动任务（<c>02</c> F-01）。</exception>
    /// <exception cref="ArgumentOutOfRangeException">任务标识为空。</exception>
    public static Mission Create(MissionId id, string name, Squad squad, SquadRules rules)
    {
        if (string.IsNullOrEmpty(id.Value))
        {
            throw new ArgumentOutOfRangeException(nameof(id), id, "任务标识不能为空。");
        }

        if (squad is null) throw new ArgumentNullException(nameof(squad));
        if (rules is null) throw new ArgumentNullException(nameof(rules));

        if (!squad.IsRosterReady)
        {
            throw new ArgumentException(
                $"队伍不满足编队合法性规则，拒绝为其创建任务 {id}。"
                + "无效队伍不能启动任务（02 F-01）。",
                nameof(squad));
        }

        return new Mission(id, name ?? string.Empty, squad.Members);
    }

    /// <summary>
    /// 开始任务。**并把每名成员标记为随队**（<c>07</c> D-006：随队者不参与日常决策，
    /// 但村庄时间照常推进）。
    /// </summary>
    /// <exception cref="InvalidOperationException">任务不处于 <c>Created</c> 状态。</exception>
    /// <exception cref="InvalidOperationException">
    /// 有成员已被分配到另一个进行中的任务 —— 不变量 <b>I2</b>。
    /// </exception>
    public void Start(Func<HeroState, bool> isBusy)
    {
        if (Status != MissionStatus.Created)
        {
            throw new InvalidOperationException(
                $"任务 {Id} 处于 {Status}，不能开始。状态只允许 Created → Active。");
        }

        foreach (var m in _members)
        {
            if (isBusy(m))
            {
                throw new InvalidOperationException(
                    $"英雄 {m.Id} 已被分配到另一个进行中的任务。"
                    + "不变量 I2：同一英雄同一时刻至多被分配到一个进行中的任务。");
            }
        }

        foreach (var m in _members) m.SetOnMission();
        Status = MissionStatus.Active;
    }

    /// <summary>结束任务，并把成员放回村庄。</summary>
    /// <param name="result">结束结果；不得为 <c>Created</c> / <c>Active</c>。</param>
    /// <exception cref="ArgumentOutOfRangeException">结果不是结束态。</exception>
    /// <exception cref="InvalidOperationException">任务未在进行中。</exception>
    public void Finish(MissionStatus result)
    {
        if (result is MissionStatus.Created or MissionStatus.Active)
        {
            throw new ArgumentOutOfRangeException(
                nameof(result), result, "只能以 Succeeded / Failed / Aborted 结束。");
        }

        if (Status != MissionStatus.Active)
        {
            throw new InvalidOperationException(
                $"任务 {Id} 处于 {Status}，不能结束。状态不会从已结束回到进行中（不变量 I14）。");
        }

        foreach (var m in _members) m.SetInVillage();
        Status = result;
    }
}
