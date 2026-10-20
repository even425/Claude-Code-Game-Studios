using System;
using System.Collections.Generic;
using System.Globalization;

namespace ClaudeCodeGameStudios.Application;

/// <summary>
/// 英雄 Id 的分配器 —— 保证<b>同一时刻一个 Id 只对应一个英雄实体</b>（不变量 I1）。
///
/// <para><b>它为什么存在</b>：Id 原本是在召唤时直接算出来的
/// <c>$\"h-{_heroes.Count}\"</c> —— 用<b>当前人数</b>当序号。
/// 人数当序号，在花名册<b>连续</b>时恰好等于「下一个序号」，
/// 但只要花名册里<b>缺一个号</b>就会撞号：比如读档后花名册是
/// <c>h-0 / h-2</c>（人数 2），下一个召唤就会拿到 <c>h-2</c> ——
/// <b>两个英雄同号</b>。那会直接违反 I1，而后果不是「显示错乱」，
/// 是<b>存档错乱</b>：读档时两个状态映射到同一个实体。</para>
///
/// <para><b>规则</b>：下一个 Id = <b>已用过的最大序号 + 1</b>，
/// 且跳过任何已占用的号。这样：<list type="bullet">
/// <item>花名册连续（正常情况）⇒ 与旧的「按人数算」<b>完全一致</b>（存档兼容）；</item>
/// <item>花名册有洞 ⇒ 取洞后面的号，<b>绝不撞号</b>，也保持序号单调。</item>
/// </list></para>
///
/// <para><b>Id 是不透明的键</b>（基线 §5.1「跨实体引用一律用 ID」），
/// 所以它长什么样不是玩法决定 —— 但「不重复」是硬要求。</para>
///
/// <para><b>无状态的部分</b>：本类<b>不持有</b>花名册，只在构造时收下
/// 「已被占用的 Id 集合」。这是刻意的：分配器不该能改花名册。</para>
/// </summary>
public sealed class HeroIdAllocator
{
    private readonly HashSet<string> _used;
    private int _next;

    /// <summary>
    /// 构造一个分配器。
    /// </summary>
    /// <param name="takenIds">已被占用的 Id（可为空）。</param>
    public HeroIdAllocator(IEnumerable<string>? takenIds = null)
    {
        _used = new HashSet<string>(StringComparer.Ordinal);
        int max = -1;

        if (takenIds is not null)
        {
            foreach (string id in takenIds)
            {
                if (string.IsNullOrEmpty(id)) continue;
                _used.Add(id);
                if (TryParseSequence(id, out int seq) && seq > max) max = seq;
            }
        }

        _next = max + 1;
    }

    /// <summary>分配下一个<b>未被占用</b>的 Id。</summary>
    /// <returns>形如 <c>h-N</c> 的 Id。</returns>
    public string Next()
    {
        // 正常路径下 _next 一定空闲；但仍然逐个跳过已占用的号 ——
        // 「分配器自己算出来的号」与「花名册里已有的号」可能不一致
        // （例如有人手工塞过一个 id）。跳过比断言便宜，也比撞号便宜。
        while (true)
        {
            string id = $"h-{_next.ToString(CultureInfo.InvariantCulture)}";
            _next++;
            if (_used.Add(id)) return id;
        }
    }

    /// <summary>解析 <c>h-N</c> 形式的序号；不是这个形式就返回 false。</summary>
    private static bool TryParseSequence(string id, out int sequence)
    {
        sequence = -1;
        const string prefix = "h-";
        if (!id.StartsWith(prefix, StringComparison.Ordinal)) return false;
        if (id.Length == prefix.Length) return false;
        return int.TryParse(
            id.Substring(prefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out sequence);
    }
}
