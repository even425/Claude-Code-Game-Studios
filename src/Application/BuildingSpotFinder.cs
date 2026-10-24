using System;
using System.Collections.Generic;
using ClaudeCodeGameStudios.Domain.Village;

namespace ClaudeCodeGameStudios.Application;

/// <summary>
/// <b>给建筑找一块合法落点</b>（D-122：采集器也要盖商店）。
/// </summary>
/// <remarks>
/// <para>⚠️ <b>为什么需要它</b>：玩家盖房时<b>自己点</b>落点（D-093），
/// 而采集器没人点 ⇒ 它得自己找一个 ⇒ 落点的合法性只有一把尺子
/// （<see cref="BuildingService.CanPlaceAt"/>：不重叠 + 落在村子的地盘里）
/// ⇒ 本类只负责<b>找</b>，判定的权威仍在 <see cref="BuildingService"/>。</para>
///
/// <para>⚠️ <b>找法是确定的</b>：从定义的默认落点开始，按「右、下、左、上」螺旋外扩
/// ⇒ 同样的输入 ⇒ 同样的落点 ⇒ 可复现（标定数据才可比）。</para>
/// </remarks>
public static class BuildingSpotFinder
{
    /// <summary>找一块能放下这栋建筑的落点。</summary>
    /// <param name="definition">要建什么。</param>
    /// <param name="canPlaceAt">落点是否合法的判定（<c>BuildingService.CanPlaceAt</c>）。</param>
    /// <param name="maxRadius">最多往外找几圈（防地图边缘时死循环）。</param>
    /// <returns>落点；找不到时返回 null。</returns>
    public static (int X, int Y)? Find(
        BuildableBuilding definition,
        Func<int, int, bool> canPlaceAt,
        int maxRadius = 6)
    {
        if (canPlaceAt is null) throw new ArgumentNullException(nameof(canPlaceAt));

        int baseX = definition.GridX;
        int baseY = definition.GridY;

        if (canPlaceAt(baseX, baseY)) return (baseX, baseY);

        // 螺旋外扩：先 (0,+1) / (0,-1) / (+1,0) / (-1,0)，再往外一圈……
        for (int r = 1; r <= maxRadius; r++)
        {
            for (int dx = -r; dx <= r; dx++)
            {
                for (int dy = -r; dy <= r; dy++)
                {
                    // 只要这一圈的「边」（中间已经找过了）。
                    if (Math.Abs(dx) != r && Math.Abs(dy) != r) continue;

                    int x = baseX + dx;
                    int y = baseY + dy;
                    if (canPlaceAt(x, y)) return (x, y);
                }
            }
        }

        return null;
    }
}
