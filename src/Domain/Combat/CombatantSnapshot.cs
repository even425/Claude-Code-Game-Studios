using System;
using System.Collections.Generic;
using System.Linq;

namespace ClaudeCodeGameStudios.Domain.Combat;

/// <summary>
/// 一名参战者的<b>状态快照</b> —— 战斗解算时能看到的「这个人当时是什么情况」。
///
/// <para><b>它为什么存在</b>：<see cref="IBattleResolver"/> 原本只拿到
/// <b>一串参与者标识</b>（<c>participantIds</c>）⇒ 战斗<b>结构上就看不到</b>
/// 「队伍是谁配置的」，而基线 §14.3 的验收写法明确要求对照实验的固定输入里
/// 包含「<b>相同的英雄队伍（成员、装备、状态）</b>」。
/// ⇒ 没有这份快照，「<b>队伍配置按规则影响副本结果</b>」（IT-31）这条验收
/// <b>写不出来</b> —— 不是因为公式没定，而是因为<b>战斗压根拿不到输入</b>。</para>
///
/// <para><b>为什么是纯数据（值类型）</b>：战斗解算必须能<b>离线复现</b>
/// （基线 §8.5 D5：战斗日志要能复现胜负判定所引用的每一个关键事件）。
/// 若它持有活对象，解算结果就会随「解算那一刻对象的状态」漂移 ⇒ 不可复现。
/// 快照是<b>值</b>，所以「第几次战斗、谁在场、带了什么」都被钉死在一份数据里。</para>
///
/// <para><b>它不决定任何规则</b>：伤害 / 命中 / 冷却怎么算属【待定】T-004。
/// 本类型只负责<b>把输入递到手</b>，不负责解释输入 —— 那是「战斗规则」的事。
/// </para>
///
/// <para><b>为什么不在 <c>DungeonRun</c> 里存它</b>：<c>05</c> §3.5 明写
/// <c>DungeonRun</c> 持有「<b>参与英雄 ID 快照</b>」—— 那是<b>规格</b>，不是实现偷懒。
/// 副本实例的职责是「谁去了」（结算要对应「谁去了」，而不是「读档瞬间队伍里还有谁」），
/// <b>不是</b>「每个人当时什么状态」。两者是不同的问题，塞进一个实体会让两个问题互相污染。</para>
/// </summary>
/// <param name="Id">英雄标识（与副本实例的参与者名单对应）。</param>
/// <param name="Name">显示名（写进战斗日志，便于人读）。</param>
/// <param name="TraitModifiers">
/// 人物特质的修正系数（特性 ID → 系数）。
/// <para>⚠️ <b>具体条目与取值属【待定】</b>（基线 §4.2 / T-004）；
/// 这里只提供<b>载体</b>，不规定里面该有什么。</para>
/// </param>
/// <param name="PreferenceWeights">玩法偏好的基础权重（偏好类别 ID → 权重）。【待定】P-06。</param>
public readonly record struct CombatantSnapshot(
    string Id,
    string Name,
    IReadOnlyDictionary<string, double> TraitModifiers,
    IReadOnlyDictionary<string, double> PreferenceWeights)
{
    /// <summary>造一份快照：把两本字典<b>复制</b>一份（不是持有原对象）。</summary>
    /// <param name="id">英雄标识。</param>
    /// <param name="name">显示名。</param>
    /// <param name="traitModifiers">特质修正系数；null 视为空。</param>
    /// <param name="preferenceWeights">偏好权重；null 视为空。</param>
    /// <remarks>
    /// <b>为什么要复制</b>：字典是可变引用。若快照直接持有原字典，
    /// 解算之后再改一次英雄状态，**已经算出来的战斗结果就变了**
    /// ⇒ 战斗不可复现（D5）。复制一次的成本可以忽略。
    /// </remarks>
    public static CombatantSnapshot Create(
        string id,
        string name,
        IReadOnlyDictionary<string, double>? traitModifiers = null,
        IReadOnlyDictionary<string, double>? preferenceWeights = null)
        => new(id ?? string.Empty,
               name ?? string.Empty,
               Copy(traitModifiers),
               Copy(preferenceWeights));

    private static IReadOnlyDictionary<string, double> Copy(
        IReadOnlyDictionary<string, double>? source)
        => source is null
            ? new Dictionary<string, double>(StringComparer.Ordinal)
            : new Dictionary<string, double>(source, StringComparer.Ordinal);
}

/// <summary>
/// 一场遭遇的<b>参战方快照</b> —— 战斗解算拿到的完整输入。
///
/// <para>它把「<b>是谁</b>」（名单）与「<b>他们当时什么情况</b>」（状态）放在一起 ——
/// 因为这两样都是战斗解算<b>必须</b>看到的，而缺任何一样都验不了
/// 「队伍配置按规则影响结果」。</para>
/// </summary>
public sealed class CombatPartySnapshot
{
    /// <summary>空队伍（不合法：副本必须至少有参与者）。</summary>
    /// <exception cref="ArgumentException">名单为空。</exception>
    public CombatPartySnapshot(IReadOnlyList<CombatantSnapshot> combatants)
    {
        if (combatants is null || combatants.Count == 0)
        {
            throw new ArgumentException("参战队伍不能为空。", nameof(combatants));
        }
        Combatants = combatants.ToArray();
    }

    /// <summary>参战者快照（按名册顺序）。</summary>
    public IReadOnlyList<CombatantSnapshot> Combatants { get; }

    /// <summary>参战者标识名单（与 <c>DungeonRun.Participants</c> 对应）。</summary>
    public IReadOnlyList<string> ParticipantIds =>
        Combatants.Select(c => c.Id).ToArray();

    /// <summary>队伍人数。</summary>
    public int Count => Combatants.Count;

    /// <summary>
    /// 按<b>只有标识</b>造一份快照（状态全空）。
    /// </summary>
    /// <param name="ids">参与者标识。</param>
    /// <remarks>
    /// ⚠️ 这是「<b>队伍状态不明</b>」，**不是**「队伍状态为空」。
    /// 它存在的唯一理由是让既有调用点不必改 ——
    /// 但只要战斗规则开始用队伍配置，调用方就**必须**传真实快照
    /// （否则规则会静悄悄地看到一片空白）。
    /// </remarks>
    /// <exception cref="ArgumentException">标识列表为空。</exception>
    public static CombatPartySnapshot FromIds(IReadOnlyList<string> ids)
    {
        if (ids is null || ids.Count == 0)
        {
            throw new ArgumentException("参战队伍不能为空。", nameof(ids));
        }
        return new CombatPartySnapshot(
            ids.Select(id => CombatantSnapshot.Create(id, id)).ToArray());
    }
}
