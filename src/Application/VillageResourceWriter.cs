using System;
using System.Collections.Generic;
using ClaudeCodeGameStudios.Domain.Settlement;
using ClaudeCodeGameStudios.Domain.Village;

namespace ClaudeCodeGameStudios.Application;

/// <summary>
/// 村庄资源的<b>整笔写入入口</b>（结算的村庄侧）。
///
/// <para><b>它守什么</b>：不变量 <b>I15</b>「结算要么完整应用，要么不应用」。
/// 这条不变量的<b>村庄那一半</b>就落在这里：一次结算可能同时给村庄加材料、
/// 加金币、扣金币 —— 其中任何一笔不可行（扣成负数 / 加上后溢出），
/// <b>整笔都不能落地</b>。</para>
///
/// <para><b>为什么要有这个类</b>（它以前是 <c>Main.ApplyWrites</c>，
/// 一个<b>私有方法</b>）：不变量写在代码里却<b>没法被测试直接验</b>，
/// 就等于没写。I15 是验收计划里最后一条还标着「受阻」的不变量 ——
/// 它的阻塞点不是「语义没定」（语义在基线里<b>已确认</b>），
/// 而是「<b>从哪个点注入失败</b>」这个测试设计问题（TP-001）。
/// 而注入点必须落在<b>一个能被别人调到的地方</b>，
/// 否则集成测试只能验一个<b>桩</b> —— 桩的原子性是自己写给自己的，
/// 证明不了生产代码的原子性。</para>
///
/// <para><b>为什么是「预检 + 提交」两步</b>：若边校验边提交，
/// <b>中间一笔失败就会留下半笔账</b> —— 那正是 I15 要禁止的。
/// 预检排掉两种失败（扣减后变负、加上后越界）之后，
/// 提交循环里的调用<b>不会抛</b>，因此不存在「写了一半」的路径。</para>
///
/// <para><b>它不做什么</b>：不决定奖励内容（那由 <see cref="SettlementDelta"/> 给出）、
/// 不产生事件、不碰村庄以外的任何状态。</para>
/// </summary>
public sealed class VillageResourceWriter
{
    private readonly Village _village;

    /// <param name="village">村庄的领域模型（材料的唯一存放处，I10）。</param>
    /// <exception cref="ArgumentNullException"><paramref name="village"/> 为 null。</exception>
    public VillageResourceWriter(Village village)
    {
        _village = village ?? throw new ArgumentNullException(nameof(village));
    }

    /// <summary>
    /// 把一批资源增量<b>整笔</b>写入村庄。
    /// </summary>
    /// <param name="writes">本次结算要写的资源增量。</param>
    /// <returns>
    /// <c>true</c> = 整笔已写入；<c>false</c> = <b>一笔都没写</b>
    /// （调用方应把整笔结算拒绝掉）。
    /// </returns>
    /// <remarks>
    /// <b>「一笔都没写」是硬要求</b>：返回 <c>false</c> 之后，
    /// 村庄的资源快照必须与调用前<b>逐项相同</b> —— 这是 I15 在村庄侧的落点。
    /// </remarks>
    public bool Write(ResourceDelta[] writes)
    {
        if (writes is null || writes.Length == 0) return true;

        // ── 预检：任何一笔不可行，整笔拒绝，此时什么都没写 ──
        foreach (var w in writes)
        {
            var id = new ResourceId(w.ResourceId);
            long current = _village.Get(id);
            long next = current + w.Amount;

            if (next < 0) return false;                                  // 会被扣成负数
            if (w.Amount > 0 && current > long.MaxValue - w.Amount) return false;  // 会溢出
        }

        // ── 提交：预检已排除全部失败路径，这里不会抛 ──
        foreach (var w in writes)
        {
            var id = new ResourceId(w.ResourceId);
            if (w.Amount > 0)
            {
                _village.Add(id, w.Amount);
            }
            else
            {
                _village.TrySpend(id, -w.Amount);   // 预检已保证扣得起
            }
        }

        return true;
    }
}
