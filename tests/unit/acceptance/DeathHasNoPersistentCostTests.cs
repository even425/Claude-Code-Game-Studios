using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using GdUnit4;
using ClaudeCodeGameStudios.Domain.Combat;
using ClaudeCodeGameStudios.Domain.Heroes;

namespace ClaudeCodeGameStudios.Tests;

/// <summary>
/// <b>IT-16（不变量 I6）</b>：死亡与复活<b>不产生任何持久代价</b>。
///
/// <para><b>它守的是什么</b>：基线 §4.6「死亡后复活，不附加死亡代价」；
/// <c>04</c> §5.7 把它逐条拆开，并给出<b>断言形式</b>：
/// 「死亡前后英雄的<b>属性、持有物、关系、可用性完全一致</b>；
/// <b>不存在</b>表示永久损失的状态字段」；
/// <c>05</c> §7 更狠：「把死亡代价相关的任何字段列为<b>永不建模</b> ——
/// <b>建模它本身就是违规</b>」。</para>
///
/// <para><b>为什么这条特别值得写成测试</b>：死亡代价是一个**很容易被「顺手加上」**的东西。
/// 实现战斗时想到「英雄死了应该有标记」，加一个 <c>IsDead</c> 或 <c>Wounds</c> 字段 ——
/// 当时看完全合理，但那一行代码<b>永久改变了规则</b>：
/// 存档要存它、读档要恢复它、界面要显示它、平衡要为它留位置。
/// 而规则说它<b>根本不该存在</b>。<b>所以它必须由机器守，不能靠自觉。</b></para>
///
/// <para><b>本文件覆盖的是「结构面」</b>（<b>不存在</b>永久代价的字段）——
/// 它<b>现在就能写、且不依赖任何设计数值</b>。
/// 「死亡前后的属性 / 持有物 / 关系 / 可用性完全一致」这一<b>行为面</b>，
/// 需要战斗规则给出「死亡」的定义，属 <c>07</c> 的 <b>T-004</b>，
/// 在那之前<b>无法</b>写（不是漏写，是没有可注入的死亡事件）。</para>
///
/// <para><b>用反射而不是正则扫</b>：正则分不清「字段名」与「注释里提到这个词」；
/// 反射直接看<b>真实的成员</b>，注释与字符串一律不算。</para>
/// </summary>
[TestSuite]
public class DeathHasNoPersistentCostTests
{
    /// <summary>
    /// 表示「永久损失」的关键词（中英）。
    /// <para>命中即视为违规 —— <b>宁可误报也不漏报</b>：
    /// 误报会让人来问「为什么这个字段被拦」，改个名字即可；
    /// 漏报则是一条规则悄悄变成了现实。</para>
    /// </summary>
    private static readonly string[] PermanentLossKeywords =
    {
        "死亡", "阵亡", "身亡", "失效", "不可用", "损毁", "毁坏", "永久",
        "残废", "残疾", "受伤", "伤口", "重创",
        "death", "dead", "died", "die", "permanent", "permanently",
        "cripple", "wound", "injur", "unavail", "destroyed", "ruin",
    };

    /// <summary>领域层全部公开类型。</summary>
    private static IEnumerable<Type> DomainTypes() =>
        typeof(ClaudeCodeGameStudios.Domain.Items.ItemId).Assembly
            .GetTypes()
            .Where(t => t.IsPublic || t.IsNestedPublic)
            .Where(t => (t.Namespace ?? "").StartsWith("ClaudeCodeGameStudios.Domain", StringComparison.Ordinal));

    /// <summary>类型上所有**真实成员**的名字（属性 + 字段；不含方法）。</summary>
    private static IEnumerable<(Type Type, string Member)> MembersOf(Type type)
    {
        foreach (var p in type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
        {
            yield return (type, p.Name);
        }
        foreach (var f in type.GetFields(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
        {
            yield return (type, f.Name);
        }
    }

    // ── 断言 1：领域层不存在「永久损失」的状态成员 ──────────────────────

    /// <summary>
    /// 领域层<b>不得</b>有任何名字暗示「永久损失」的成员。
    /// <para>（<c>05</c> §7：「建模它本身就是违规」。）</para>
    /// </summary>
    [TestCase]
    public void IT16_DomainLayer_HasNoPermanentLossStateMember()
    {
        var offenders = new List<string>();

        foreach (var type in DomainTypes())
        {
            foreach (var (owner, member) in MembersOf(type))
            {
                string lower = member.ToLowerInvariant();
                foreach (var kw in PermanentLossKeywords)
                {
                    if (lower.Contains(kw.ToLowerInvariant()))
                    {
                        offenders.Add($"{owner.FullName}.{member}（命中「{kw}」）");
                        break;
                    }
                }
            }
        }

        Assertions.AssertThat(string.Join(" | ", offenders)).IsEmpty();
    }

    // ── 断言 2：战斗规则的输出不得含「英雄永久失效 / 损毁」 ─────────────

    /// <summary>
    /// <c>04</c> §5.7 点名的两条：
    /// 「战斗规则接口的输出里<b>不得</b>出现『英雄永久失效』『英雄损毁』这类字段」。
    /// <para><c>BattleResult</c> 是那个输出类型。</para>
    /// </summary>
    [TestCase]
    public void IT16_BattleResult_HasNoHeroInvalidationField()
    {
        var offenders = new List<string>();

        foreach (var (owner, member) in MembersOf(typeof(BattleResult)))
        {
            string lower = member.ToLowerInvariant();
            if (PermanentLossKeywords.Any(k => lower.Contains(k.ToLowerInvariant())))
            {
                offenders.Add($"{owner.Name}.{member}");
            }
        }

        Assertions.AssertThat(string.Join(" | ", offenders)).IsEmpty();
    }

    // ── 断言 3：英雄身上不得有「永久死亡 / 不可用 / 装备损毁」状态 ───────

    /// <summary>
    /// <c>05</c> §3.2：「因此 <c>Hero</c> 实体<b>没有</b>『永久死亡』『不可用』
    /// 『装备损毁』这类状态」。
    /// <para>（<c>HeroState</c> 是领域侧的英雄状态；<c>HeroAgent</c> 是它对外的表现层壳。）</para>
    /// </summary>
    [TestCase]
    public void IT16_HeroState_And_HeroAgent_HaveNoPermanentLossState()
    {
        var offenders = new List<string>();

        foreach (var type in new[] { typeof(HeroState), typeof(HeroAgent) })
        {
            foreach (var (owner, member) in MembersOf(type))
            {
                string lower = member.ToLowerInvariant();
                if (PermanentLossKeywords.Any(k => lower.Contains(k.ToLowerInvariant())))
                {
                    offenders.Add($"{owner.Name}.{member}");
                }
            }
        }

        Assertions.AssertThat(string.Join(" | ", offenders)).IsEmpty();
    }

    // ── 断言 4：连「是否已死亡」的布尔位都不许有 ───────────────────────

    /// <summary>
    /// 一个更锐的形状：即使字段名不含关键词，<b>一个「英雄是否已死」的布尔状态</b>
    /// 同样违规 —— 它就是「永久损失」的第一块砖（存档要存它、平衡要为它留位置）。
    /// <para>这里按<b>类型</b>筛：布尔成员且名字像「生死/存亡」的 ⇒ 违规。</para>
    /// </summary>
    [TestCase]
    public void IT16_NoBooleanMemberLooksLikeADeadOrAliveFlag()
    {
        string[] aliveKeywords = { "dead", "alive", "死亡", "存活", "生还", "阵亡" };
        var offenders = new List<string>();

        foreach (var type in DomainTypes())
        {
            foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
            {
                if (prop.PropertyType != typeof(bool) && prop.PropertyType != typeof(bool?)) continue;
                string lower = prop.Name.ToLowerInvariant();
                if (aliveKeywords.Any(k => lower.Contains(k.ToLowerInvariant())))
                {
                    offenders.Add($"{type.FullName}.{prop.Name}");
                }
            }
        }

        Assertions.AssertThat(string.Join(" | ", offenders)).IsEmpty();
    }

    // ── 断言 5：防空转 ──────────────────────────────────────────────────

    /// <summary>
    /// 守卫自己得扫到东西 —— 否则「全绿」可能只是「什么都没扫」。
    /// </summary>
    [TestCase]
    public void IT16_TheAuditActuallyCoversSomething()
    {
        var types = DomainTypes().ToList();
        int members = types.Sum(t => MembersOf(t).Count());

        Assertions.AssertThat(types.Count).IsGreater(40);
        Assertions.AssertThat(members).IsGreater(100);
    }
}
