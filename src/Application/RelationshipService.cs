using System;
using System.Collections.Generic;
using ClaudeCodeGameStudios.Domain.Heroes;
using ClaudeCodeGameStudios.Domain.Relationships;

namespace ClaudeCodeGameStudios.Application;

/// <summary>一次社交对双向关系的影响。</summary>
/// <param name="Forward">发起者对目标的关系变化。</param>
/// <param name="Backward">目标对发起者的关系变化（独立记录，I8）。</param>
public readonly record struct SocialTouched(
    RelationshipChange Forward, RelationshipChange Backward)
{
    /// <summary>任一方向真的变了。</summary>
    public bool Changed => Forward.Changed || Backward.Changed;

    /// <summary>任一方向跨过了展示阶段（杂谈是否说话的依据，P3-D09）。</summary>
    public bool StageChanged => Forward.StageChanged || Backward.StageChanged;
}

/// <summary>一次合作对队内关系的影响。</summary>
/// <param name="PairsChanged">真正发生变化的关系对数。</param>
public readonly record struct CooperationTouched(int PairsChanged)
{
    /// <summary>是否至少有对关系变了。</summary>
    public bool Changed => PairsChanged > 0;
}

/// <summary>
/// 关系变化的<b>应用入口</b> —— 把「什么行为、和谁、在什么时候」翻译成
/// 幂等的 <see cref="RelationshipBook.Apply"/> 调用。
/// </summary>
/// <remarks>
/// <para><b>为什么单独一层</b>：<c>Main</c> 仍是巨型管理器（审计 R-09）。
/// 关系写入的<b>键格式</b>与<b>双向规则</b>属于业务规则，放在这里才能单测；
/// <c>Main</c> 只负责「行为发生了 → 调本类 → 拿结果发事件」。</para>
/// <para><b>幂等键（P3-D08）：</b></para>
/// <list type="bullet">
/// <item>社交：<c>social:{actor}->{target}@{simTime:0.###}</c>，两个方向各一条。</item>
/// <item>同队副本：<c>dungeon:{runId}:{a}->{b}</c>，每个有序对一条。</item>
/// </list>
/// <para>同一键重复调用不会再加关系 —— 读档重放、重复结算都不会把关系翻倍。</para>
/// </remarks>
public sealed class RelationshipService
{
    private readonly RelationshipBook _book;

    /// <param name="book">关系唯一写者。</param>
    /// <exception cref="ArgumentNullException"><paramref name="book"/> 为 null。</exception>
    public RelationshipService(RelationshipBook book)
    {
        _book = book ?? throw new ArgumentNullException(nameof(book));
    }

    /// <summary>
    /// 应用一次社交：<b>双向各自</b>增加 <see cref="RelationshipRules.SocialDelta"/>。
    /// </summary>
    /// <param name="actor">发起社交的英雄。</param>
    /// <param name="target">社交对象。</param>
    /// <param name="simTime">完成时的模拟时间（进入幂等键）。</param>
    /// <returns>双向变化结果。</returns>
    public SocialTouched Socialize(HeroId actor, HeroId target, double simTime)
    {
        string stamp = simTime.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);

        var forward = _book.Apply(
            $"social:{actor.Value}->{target.Value}@{stamp}",
            new RelationshipKey(actor, target), RelationshipRules.SocialDelta);

        var backward = _book.Apply(
            $"social:{target.Value}->{actor.Value}@{stamp}",
            new RelationshipKey(target, actor), RelationshipRules.SocialDelta);

        return new SocialTouched(forward, backward);
    }

    /// <summary>
    /// 应用一次同队副本合作：队内<b>每一对</b>双向各加
    /// <see cref="RelationshipRules.CooperationDelta"/>。
    /// </summary>
    /// <remarks>
    /// 以 <paramref name="runId"/> 为幂等键 —— 同一副本只加一次；
    /// 队伍只有 1 人时没有「对」，返回 0（不产生事件）。
    /// </remarks>
    /// <param name="runId">副本实例标识（幂等依据）。</param>
    /// <param name="members">参战英雄（快照 Id）。</param>
    /// <returns>真正变化的关系对数。</returns>
    /// <exception cref="ArgumentException"><paramref name="runId"/> 为空。</exception>
    public CooperationTouched Cooperate(string runId, IReadOnlyList<HeroId> members)
    {
        if (string.IsNullOrWhiteSpace(runId))
        {
            throw new ArgumentException("副本合作必须有 runId —— 它是幂等的唯一依据。", nameof(runId));
        }

        if (members is null) throw new ArgumentNullException(nameof(members));

        int changedPairs = 0;
        for (int i = 0; i < members.Count; i++)
        {
            for (int j = i + 1; j < members.Count; j++)
            {
                var a = members[i];
                var b = members[j];

                if (string.IsNullOrEmpty(a.Value) || string.IsNullOrEmpty(b.Value) || a == b) continue;

                var ab = _book.Apply(
                    $"dungeon:{runId}:{a.Value}->{b.Value}",
                    new RelationshipKey(a, b), RelationshipRules.CooperationDelta);
                var ba = _book.Apply(
                    $"dungeon:{runId}:{b.Value}->{a.Value}",
                    new RelationshipKey(b, a), RelationshipRules.CooperationDelta);

                if (ab.Changed || ba.Changed) changedPairs++;
            }
        }

        return new CooperationTouched(changedPairs);
    }
}
