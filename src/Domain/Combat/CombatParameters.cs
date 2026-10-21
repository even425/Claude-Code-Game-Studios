using System;
using System.Collections.Generic;
using System.Linq;

namespace ClaudeCodeGameStudios.Domain.Combat;

/// <summary>一名参战者的<b>战斗属性</b>（攻击 / 防御 / 生命）。</summary>
/// <remarks>
/// <para><b>它为什么是值类型</b>：战斗结果必须能<b>离线复现</b>（基线 §8.5 D5）——
/// 解算之后再改一次属性，<b>已经算出来的战斗就变了</b>。
/// 同 <see cref="CombatantSnapshot"/> 的理由。</para>
/// <para><b>生命为 0 表示「状态不明」</b>，不是一个合法的初始状态
/// ⇒ 战斗引擎遇到它会<b>大声失败</b>，而不是「一个没有战斗属性的人参战了」。</para>
/// </remarks>
/// <param name="Attack">攻击。</param>
/// <param name="Defense">防御。</param>
/// <param name="Health">生命。</remarks>
public readonly record struct CombatStats(int Attack, int Defense, int Health)
{
    /// <summary>「状态不明」的属性（全 0）。</summary>
    public static readonly CombatStats Unknown = new(0, 0, 0);

    /// <summary>还站着吗（还有血）。</summary>
    /// <remarks>
    /// ⚠️ **为什么叫「IsStanding」而不是「IsAlive」**：不变量 I6（<c>05</c> §7）规定
    /// 「死亡代价相关的任何字段<b>永不建模</b>」⇒ 守卫 <c>DeathHasNoPersistentCostTests</c>
    /// 会拦下任何名字像「生死位」的<b>布尔</b>成员（<c>IsAlive</c> / <c>IsDead</c> / 「存活」…）。
    /// 而本属性是<b>战斗过程里的瞬时状态</b>（还有血就还在打），**不是**英雄的存亡标记
    /// ⇒ 用游戏语言「还站着」⇒ 既不撞守卫，<b>也不改变 I6 的意思</b>。
    /// </remarks>
    public bool IsStanding => Health > 0;

    /// <summary>这三个属性是已知的吗（不是全 0）。</summary>
    public bool IsKnown => !(Attack == 0 && Defense == 0 && Health == 0);
}

/// <summary>一个<b>战斗原型</b>（名字 + 三个属性）。</summary>
/// <remarks>
/// <para>它把「**谁**」与「**多强**」放在一起 ⇒ 换算表、怪物表都用它，
/// 而战斗日志里也能直接写它的名字（人读得懂）。</para>
/// <para>⚠️ 表里的每个数都属【已确认】<c>07</c> <b>D-065</b>（开发者 2026-10-21 拍板），
/// 并由参数台账盯住（表 <c>combat-parameters</c>）。</para>
/// </remarks>
/// <param name="Name">原型名（性格 / 投入度 / 怪物档位名）。</param>
/// <param name="Attack">攻击。</param>
/// <param name="Defense">防御。</param>
/// <param name="Health">生命。</param>
public readonly record struct CombatArchetype(string Name, int Attack, int Defense, int Health);

/// <summary>
/// 战斗参数的<b>单一真源</b> —— D-065 拍板的那批数。
/// </summary>
/// <remarks>
/// <para><b>为什么它们在这里而不是散在各处</b>：临时参数必须集中管理
/// （<c>07</c> §2.1 的纪律）⇒ 数字只出现在这一处，参数台账逐项盯着它们。</para>
/// <para><b>换算的形状（D-060 + D-065）</b>：
/// <list type="bullet">
/// <item><b>攻 / 防由性格定</b>：莽 12/3（冲得快也死得快）· 傲 8/8（硬碰硬）· 随手 8/6（中庸）。</item>
/// <item><b>生命由投入度定</b>：肝 120（磨）· 随手 80 · 挂机 60。</item>
/// <item><b>投入度为「挂机」时，攻 / 防取「挂机」那组</b>（5/4）——
/// 草案的定位是「挂机 = 三项都最低」，而代码里性格是**两轴**，
/// 「挂机」只动生命的话就不是「全低」了 ⇒ 这里让**投入度优先**一次（见 <c>07</c> D-065 的落地方案说明）。</item>
/// </list></para>
/// <para><b>怪物的三档</b>一一对应副本的三个阶段（普通 = 遭遇一 · 精英 = 遭遇二 · 首领 = 最终目标），
/// 怪物**会动手**（D-064）⇒ 才有「谁打谁」的 AI 与「英雄受伤」这条 §7.2 缺口。</para>
/// </remarks>
public static class CombatParameters
{
    /// <summary>性格 → 攻击 / 防御。<b>生命那一列是 0</b>（生命由投入度定）。</summary>
    public static readonly CombatArchetype[] PersonalityStats =
    {
        new("莽", 12, 3, 0),
        new("傲", 8, 8, 0),
        new("随手", 8, 6, 0),
    };

    /// <summary>投入度 → 生命。<b>攻 / 防那两列是 0</b>（攻 / 防由性格定）。</summary>
    public static readonly CombatArchetype[] DiligenceHealth =
    {
        new("肝", 0, 0, 120),
        new("随手", 0, 0, 80),
        new("挂机", 0, 0, 60),
    };

    /// <summary>投入度 =「挂机」时的攻 / 防（草案的「挂机」行：全低）。</summary>
    public static readonly CombatArchetype[] IdleAttackDefense =
    {
        new("挂机", 5, 4, 0),
    };

    /// <summary>
    /// 怪物的三档（一一对应副本的三个阶段）。
    /// </summary>
    /// <remarks>
    /// ⚠️ **2026-10-21 晚 · 按 2 人小队重算（D-068）**：
    /// 原值（普通 8/4/120 · 精英 11/6/220 · 首领 14/8/340）是按「<b>4 人</b>小队」算的
    /// —— 而**游戏实际只派 2 人**（采集器「取前两个可用的人」＋ 自检的两人队）
    /// ⇒ 两人打精英/首领**打不过** ⇒ **副本必然失败 ⇒ 玩家拿不到任何战利品**
    /// ⇒ 经济只剩「副本金币进账」而没有市场（实测 1000 天：副本 9989 次、<b>胜利 0 次</b>、
    /// 成交 0 笔、流通金每天 +2.00 —— 那是 X × 每天的副本次数，不是经济）。
    /// ⇒ 现在的值让**两人小队**大约 8 / 15 / 20 回合通关，**弱队（如两个挂机）会在精英档输掉**
    /// ——「选谁进队」这才有后果（D3 / IT-31）。
    /// </remarks>
    public static readonly CombatArchetype[] MonsterTiers =
    {
        new("普通", 6, 2, 60),
        new("精英", 7, 3, 100),
        new("首领", 8, 4, 120),
    };

    /// <summary>出手间隔（回合）—— 冷却 1 回合 ⇒ 每 2 回合出手一次。</summary>
    public const int RoundsPerAction = 2;

    /// <summary>回合上限 —— 超时<b>判负</b>（与 D-010 Q1「失败不给奖励」一致）。</summary>
    public const int RoundLimit = 30;

    /// <summary>中毒：每回合造成的伤害（固定值，不走防御）。</summary>
    public const int PoisonDamagePerRound = 2;

    /// <summary>中毒：持续的回合数（战斗结束即解除）。</summary>
    public const int PoisonDurationRounds = 5;

    /// <summary>
    /// 英雄的战斗属性：性格（攻 / 防）× 投入度（生命）。
    /// </summary>
    /// <param name="personality">性格（<c>HeroAgent.Personality</c>）。</param>
    /// <param name="diligence">投入度（<c>HeroAgent.Diligence</c>）。</param>
    /// <returns>战斗属性。</returns>
    /// <exception cref="InvalidOperationException">
    /// 性格或投入度不在表里 ⇒ <b>大声失败</b>：静默落到「随手」会让一个**没登记**的取值
    /// 悄悄变成设计规则（<c>07</c> §2.1 的纪律）。
    /// </exception>
    public static CombatStats ForHero(string personality, string diligence)
    {
        var p = Require(PersonalityStats, personality, nameof(personality));
        var d = Require(DiligenceHealth, diligence, nameof(diligence));

        // 投入度为「挂机」⇒ 攻 / 防取「挂机」那组（草案：挂机 = 全低）
        var idle = IdleAttackDefense.FirstOrDefault(x => x.Name == diligence);
        int attack = idle.Name is null ? p.Attack : idle.Attack;
        int defense = idle.Name is null ? p.Defense : idle.Defense;

        return new CombatStats(attack, defense, d.Health);
    }

    /// <summary>某一档怪物的属性（按阶段序号 0 / 1 / 2 ⇒ 普通 / 精英 / 首领）。</summary>
    /// <param name="tierIndex">阶段序号（0 起）。</param>
    /// <returns>怪物的战斗属性。</returns>
    /// <exception cref="ArgumentOutOfRangeException">阶段序号超出三档。</exception>
    public static CombatStats MonsterFor(int tierIndex)
    {
        if (tierIndex < 0 || tierIndex >= MonsterTiers.Length)
        {
            throw new ArgumentOutOfRangeException(
                nameof(tierIndex), tierIndex,
                $"怪物只有 {MonsterTiers.Length} 档（普通 / 精英 / 首领），阶段 {tierIndex} 没有对应档位。");
        }
        var m = MonsterTiers[tierIndex];
        return new CombatStats(m.Attack, m.Defense, m.Health);
    }

    /// <summary>某一档怪物的名字（写进战斗日志）。</summary>
    /// <param name="tierIndex">阶段序号（0 起）。</param>
    public static string MonsterNameFor(int tierIndex)
    {
        if (tierIndex < 0 || tierIndex >= MonsterTiers.Length)
        {
            throw new ArgumentOutOfRangeException(
                nameof(tierIndex), tierIndex, "怪物档位越界。");
        }
        return MonsterTiers[tierIndex].Name;
    }

    private static CombatArchetype Require(
        IReadOnlyList<CombatArchetype> table, string value, string paramName)
    {
        foreach (var row in table)
        {
            if (row.Name == value) return row;
        }
        throw new InvalidOperationException(
            $"战斗参数表里没有「{value}」（{paramName}）"
            + $" —— 表里有：{string.Join(" / ", table.Select(x => x.Name))}。"
            + "新增取值要先落 `07` 的决策，再改这张表。");
    }
}
