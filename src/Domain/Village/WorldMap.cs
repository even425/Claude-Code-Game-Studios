namespace ClaudeCodeGameStudios.Domain.Village;

/// <summary>
/// <b>世界地图</b>（D-117）—— 地图有多大、哪里是村子、哪里是野外。
/// </summary>
/// <remarks>
/// <para><b>为什么要有它</b>：地图扩大后，「<b>哪里是村外</b>」这条规则
/// 被<b>两个地方</b>要用 ——
/// ① 表现层：把村外的地面画成另一种颜色、让野兽只出现在村外；
/// ② 领域层：野兽游走时不能跑进村子。
/// ⇒ 若各写一份 ⇒ 迟早分叉 ⇒ **只有这一份**（表现层直接调它）。</para>
///
/// <para>⚠️ **坐标约定</b>：格位可以是<b>负数</b>（地板有原点偏移）⇒
/// 「在地图上」的判断必须用 <see cref="InBounds"/>，不能写 <c>x >= 0</c>。</para>
/// </remarks>
public static class WorldMap
{
    /// <summary>地图的格宽（28 格）。</summary>
    public const int Width = 28;

    /// <summary>地图的格高（20 格）。</summary>
    public const int Height = 20;

    /// <summary>地图原点的 X 偏移（村子留在中间，四周是野外）。</summary>
    public const int OriginX = -8;

    /// <summary>地图原点的 Y 偏移。</summary>
    public const int OriginY = -6;

    /// <summary>村子的地盘 X 起点（之外就是村外）。</summary>
    public const int VillageX0 = 0;

    /// <summary>村子的地盘 Y 起点。</summary>
    public const int VillageY0 = 0;

    /// <summary>村子的地盘 X 终点（之外就是村外）。</summary>
    public const int VillageX1 = 15;

    /// <summary>村子的地盘 Y 终点（之外就是村外）。</summary>
    public const int VillageY1 = 10;

    /// <summary>这一格是不是<b>村外</b>（野兽出没的地方）。</summary>
    public static bool IsWilderness(int gridX, int gridY)
        => gridX < VillageX0 || gridX >= VillageX1
        || gridY < VillageY0 || gridY >= VillageY1;

    /// <summary>这一格在不在地图上（地板的范围）。</summary>
    public static bool InBounds(int gridX, int gridY)
        => gridX >= OriginX && gridX < OriginX + Width
        && gridY >= OriginY && gridY < OriginY + Height;
}
