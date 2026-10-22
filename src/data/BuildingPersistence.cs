using System;
using System.Collections.Generic;
using System.Linq;
using ClaudeCodeGameStudios.Domain.Village;

namespace ClaudeCodeGameStudios.Data;

/// <summary>
/// <b>村庄建筑 ↔ 存档</b>的转换（D-083）。
/// </summary>
/// <remarks>
/// <para><b>为什么单独抽出来</b>：存档的写入/恢复在 <c>Main</c> 里（它持 Godot 节点，
/// 单测里不好直接调）⇒ 若把这段逻辑<b>复制</b>到测试里，
/// 测的就成了「<b>一份没人执行的复制品</b>」⇒ 改生产代码时守卫照样绿
/// （实测：五档变异全绿，因为测试压根没碰生产代码）。
/// ⇒ 这里抽成<b>纯函数</b>，<b>生产代码与测试调的是同一份</b>。</para>
///
/// <para>⚠️ <b>建筑必须入档</b>：它们是玩家花<b>钱 + 材料 + 工期</b>换来的
/// ⇒ 读档后它们消失，等于把玩家的投入<b>白拿</b>。</para>
/// </remarks>
public static class BuildingPersistence
{
    /// <summary>把村庄里的建筑导出成存档里的形状。</summary>
    /// <param name="village">村庄。</param>
    /// <returns>建筑存档列表（按放置顺序）。</returns>
    public static List<BuildingSave> Export(Village village)
    {
        if (village is null) throw new ArgumentNullException(nameof(village));

        return village.Buildings.Select(b => new BuildingSave
        {
            Id = b.Id.Value,
            X = b.GridX,
            Y = b.GridY,
            W = b.FootprintW,
            H = b.FootprintH,
            Progress = b.ConstructionProgress,
            AffectsAreaW = b.AffectsAreaW,
            AffectsAreaH = b.AffectsAreaH,
        }).ToList();
    }

    /// <summary>把存档里的建筑放回村庄。</summary>
    /// <param name="village">村庄（会被写入）。</param>
    /// <param name="saves">存档里的建筑。</param>
    /// <param name="onSkipped">
    /// 放不下的建筑（与已有建筑重叠）会被**跳过**，并在这里报一声 ——
    /// ⚠️ **不静默**：存档坏了就是坏了（读档后少了一栋楼却毫无提示，比报错更糟）。
    /// </param>
    /// <returns>成功放下的建筑实例（**包括在建的那些** —— 调用方要把它们登记回「施工中」）。</returns>
    public static IReadOnlyList<BuildingInstance> Import(
        Village village,
        IEnumerable<BuildingSave> saves,
        Action<string>? onSkipped = null)
    {
        if (village is null) throw new ArgumentNullException(nameof(village));

        var restored = new List<BuildingInstance>();
        foreach (var b in saves ?? Enumerable.Empty<BuildingSave>())
        {
            // 开局的市集等**初始建筑已经在村里了** ⇒ 跳过，别重复放。
            if (village.Buildings.Any(x => x.Id.Value == b.Id)) continue;

            var instance = new BuildingInstance(
                new BuildingId(b.Id),
                b.X, b.Y, b.W, b.H, b.Progress, b.AffectsAreaW, b.AffectsAreaH);

            if (!village.TryPlace(instance))
            {
                onSkipped?.Invoke($"建筑「{b.Id}」放不下了（与已有建筑重叠）—— 存档可能坏了。");
                continue;
            }

            restored.Add(instance);
        }

        return restored;
    }
}
