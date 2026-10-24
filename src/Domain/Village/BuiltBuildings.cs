using System.Linq;

namespace ClaudeCodeGameStudios.Domain.Village;

/// <summary>
/// 「村里有没有那栋建筑」的<b>唯一判定</b>（D-119）。
/// </summary>
/// <remarks>
/// <para>⚠️ <b>为什么要有它</b>：装备线要问三个问题 ——
/// 「武器店建好了吗」「服装店建好了吗」「铁匠铺建好了吗」
/// ⇒ 如果这三处各写一遍 <c>village.Buildings.Any(...)</c>，
/// 就会有三份「建成」的口径 ⇒ 迟早有一处漏判。</para>
/// </remarks>
public static class BuiltBuildings
{
    /// <summary>某栋建筑是否<b>已建成</b>（在建的不算）。</summary>
    /// <param name="village">村庄。</param>
    /// <param name="id">建筑标识。</param>
    /// <returns>已建成为 true。</returns>
    public static bool IsCompleted(Village village, BuildingId id)
    {
        if (village is null) return false;

        foreach (var b in village.Buildings)
        {
            if (b.Id.Value == id.Value && b.Status == BuildingStatus.Completed) return true;
        }

        return false;
    }

    /// <summary>某几栋建筑里，是不是<b>至少有一栋</b>已建成。</summary>
    /// <param name="village">村庄。</param>
    /// <param name="ids">建筑标识。</param>
    /// <returns>至少一栋已建成为 true。</returns>
    public static bool AnyCompleted(Village village, params BuildingId[] ids)
    {
        if (ids is null) return false;

        foreach (var id in ids)
        {
            if (IsCompleted(village, id)) return true;
        }

        return false;
    }
}
