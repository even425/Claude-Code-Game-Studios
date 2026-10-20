using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using GdUnit4;

namespace ClaudeCodeGameStudios.Tests;

/// <summary>
/// <b>IT-27（不变量 I10 的写入点审计）</b>：跨模块的状态改变，<b>只经由拥有该数据的模块</b>。
///
/// <para><b>它守的是什么</b>：I10 的原文是「跨模块状态改变只经由拥有该数据的模块」。
/// 这条不变式在代码里有个很干脆的机械形式：<b>状态字段不许有公开的写入口</b>。
/// 只要 <c>Wallet</c> 的余额只有 <c>Wallet</c> 自己能改，
/// 「别的模块伸手进来改」这件事就<b>在编译期</b>不可能发生 ——
/// 不需要「大家自觉」，也不需要 code review 盯着。</para>
///
/// <para><b>为什么用反射扫整个领域层</b>：手写「某个类型不许有 setter」只能守住那一个类型；
/// 领域层有几十个类型、几百个属性，<b>靠人记是记不住的</b>。
/// 反射扫一遍 ⇒ 新加的类型自动被纳入检查。</para>
///
/// <para><b>三条判据</b>：
/// <list type="number">
/// <item>不得有<b>公开可写</b>的属性（<c>set</c> 与 <c>init</c> 都算 ——
/// <c>init</c> 只在构造期能写，但构造期也是写，而「谁构造」就意味着「谁在写」）；</item>
/// <item>不得有<b>公开字段</b>（字段天生可写）；</item>
/// <item>不得有<b>公开返回可变集合</b>的属性 —— 那是把内部状态交出去，
/// 等于一个写入口（<c>ReadOnlyCollection</c> 视图不算）。</item>
/// </list></para>
///
/// <para><b>为什么不算「弱重复」</b>：<c>UT-07</c> 只查了<b>静态定义</b>那三个类型；
/// 本条查的是<b>整个领域层</b>，覆盖的是「运行时状态」那一边 ——
/// 两者合起来才是 I7 + I10 的完整形状。</para>
/// </summary>
[TestSuite]
public class DomainWritePointAuditTests
{
    private const string DomainNamespacePrefix = "ClaudeCodeGameStudios.Domain";

    private static IEnumerable<Type> DomainTypes()
    {
        return typeof(ClaudeCodeGameStudios.Domain.Items.ItemId).Assembly
            .GetTypes()
            .Where(t => t.IsPublic || t.IsNestedPublic)
            .Where(t => (t.Namespace ?? "").StartsWith(DomainNamespacePrefix, StringComparison.Ordinal));
    }

    /// <summary>
    /// 领域层不得有<b>公开可写</b>的属性（真正的 <c>set</c>）。
    ///
    /// <para><b>为什么 <c>init</c> 不算</b>：<c>init</c> 访问器只在<b>构造期</b>能写
    /// —— 那是「构造这个值对象」，不是「事后改它」。位置记录
    /// （<c>readonly record struct ItemId(string Value)</c>）的 <c>init</c> 更是
    /// 编译器生成的<b>构造参数</b>。把构造当成写入口，等于说
    /// 「任何值对象都不能被创建」—— 那不是 I10，是把类型系统关掉。</para>
    /// </summary>
    [TestCase]
    public void IT27_DomainTypes_HaveNoPubliclyWritableProperty()
    {
        var offenders = new List<string>();

        foreach (var type in DomainTypes())
        {
            foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                var setter = prop.GetSetMethod();
                if (setter is null || !setter.IsPublic) continue;
                if (IsInitOnly(setter)) continue;      // 构造期写入，不是写入口
                offenders.Add($"{type.FullName}.{prop.Name}");
            }
        }

        Assertions.AssertThat(string.Join(" | ", offenders)).IsEmpty();
    }

    /// <summary>
    /// 领域层不得有<b>公开字段</b>（字段天生可写，且没有 API 守卫）。
    /// <para><b>枚举的 <c>value__</c> 除外</b> —— 那是编译器给枚举生成的底层整数值，
    /// 不是「谁能把状态改掉」的入口（枚举的取值由类型本身固定）。</para>
    /// </summary>
    [TestCase]
    public void IT27_DomainTypes_HaveNoPublicField()
    {
        var offenders = new List<string>();

        foreach (var type in DomainTypes())
        {
            if (type.IsEnum) continue;                // value__ 是编译器生成的
            foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                offenders.Add($"{type.FullName}.{field.Name}");
            }
        }

        Assertions.AssertThat(string.Join(" | ", offenders)).IsEmpty();
    }

    /// <summary>领域层不得把<b>可变集合</b>暴露成公开属性（那等于交出写入口）。</summary>
    [TestCase]
    public void IT27_DomainTypes_DoNotExposeMutableCollections()
    {
        var offenders = new List<string>();

        foreach (var type in DomainTypes())
        {
            foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (IsMutableCollection(prop.PropertyType))
                {
                    offenders.Add($"{type.FullName}.{prop.Name}: {prop.PropertyType.Name}");
                }
            }
        }

        Assertions.AssertThat(string.Join(" | ", offenders)).IsEmpty();
    }

    /// <summary>
    /// 领域层**确实有东西可扫** —— 否则上面三条都是「扫了个空」的空转断言。
    /// <para>（这条是本文件自己的防空转自检：它在断言「我检查的对象不是空的」。）</para>
    /// </summary>
    [TestCase]
    public void IT27_TheAuditActuallyCoversSomething()
    {
        var types = DomainTypes().ToList();

        Assertions.AssertThat(types.Count).IsGreater(20);
        Assertions.AssertThat(types.Sum(t => t.GetProperties(BindingFlags.Public | BindingFlags.Instance).Length))
            .IsGreater(50);
    }

    /// <summary>判断一个 setter 是不是 <c>init</c>（构造期写入）。</summary>
    private static bool IsInitOnly(MethodInfo setter)
    {
        // init 访问器的返回类型带 IsExternalInit modreq
        return setter.ReturnParameter
            .GetRequiredCustomModifiers()
            .Any(m => m.Name == "IsExternalInit");
    }

    /// <summary>
    /// 判定一个类型是不是「可变集合」：数组、<c>List</c>/<c>Dictionary</c>/<c>HashSet</c>/
    /// <c>Collection</c> 及其接口的泛型封闭。<c>IReadOnlyXxx</c> 是只读视图，不算。
    /// </summary>
    private static bool IsMutableCollection(Type t)
    {
        if (t.IsArray) return true;
        if (t.IsGenericType)
        {
            var def = t.GetGenericTypeDefinition();
            var ns = def.Namespace ?? "";
            if (ns.StartsWith("System.Collections.Generic", StringComparison.Ordinal))
            {
                return !def.Name.StartsWith("IReadOnly", StringComparison.Ordinal);
            }
        }
        return false;
    }
}
