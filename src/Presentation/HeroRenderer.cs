using Godot;

namespace ClaudeCodeGameStudios.Presentation;

/// <summary>
/// 英雄的绘制。
/// </summary>
/// <remarks>
/// <para>
/// <b>它为什么在这里</b>：基线 §13.3 规定「领域对象不应做成 Node，
/// 场景节点负责呈现、领域对象负责规则」。原实现把这个方法放在
/// <c>HeroAgent</c> 上，于是领域对象<b>引用了表现层类型</b>（<c>VillageView</c>）——
/// 这是 A2 技术债的实质：不是「代码放错文件」，而是<b>依赖方向反了</b>。
/// </para>
/// <para>
/// 现在领域侧只暴露<b>只读状态</b>（位置 / 名字 / 性格 / 动画相位 / 是否在干活），
/// 怎么画由本类决定。领域层对 Godot 与表现层<b>零依赖</b>。
/// </para>
/// <para>绘制方式与迁移前<b>逐字一致</b>——这是搬移，不是重画。</para>
/// </remarks>
public static class HeroRenderer
{
    private static readonly Color C_Mang = new("c9584a");
    private static readonly Color C_Ao = new("5f86c8");
    private static readonly Color C_Tan = new("c9a83c");
    private static readonly Color Skin = new("f0d7b4");

    /// <summary>画一个英雄。</summary>
    /// <param name="target">绘制目标（地图节点）。</param>
    /// <param name="h">英雄；只读取状态，不修改。</param>
    public static void Draw(CanvasItem target, HeroAgent h)
    {
        var s = target switch
        {
            VillageView v => VillageView.P(h.MapX, h.MapY, 2f),
            _ => new Vector2(h.MapX, h.MapY),
        };

        var bob = h.IsWorking
            ? Mathf.Sin(h.AnimationPhase * 7.5f) * 2.0f
            : Mathf.Sin(h.AnimationPhase * 2.2f) * 0.8f;
        s += new Vector2(0, bob);

        var body = h.Personality switch
        {
            "傲" => C_Ao,
            "贪" => C_Tan,
            _ => C_Mang,
        };
        var dark = new Color(body.R * 0.55f, body.G * 0.55f, body.B * 0.55f);

        var shadow = target is VillageView vv
            ? VillageView.P(h.MapX, h.MapY, 0)
            : new Vector2(h.MapX, h.MapY);

        target.DrawPolygon(
            new[]
            {
                shadow + new Vector2(-8, 0), shadow + new Vector2(0, 4),
                shadow + new Vector2(8, 0), shadow + new Vector2(0, -4),
            },
            new[]
            {
                new Color(0, 0, 0, 0.22f), new Color(0, 0, 0, 0.22f),
                new Color(0, 0, 0, 0.22f), new Color(0, 0, 0, 0.22f),
            });

        var step = h.IsWorking ? 2f : 0f;
        target.DrawLine(s + new Vector2(-3, -2), s + new Vector2(-3 + step, 2), dark, 3f);
        target.DrawLine(s + new Vector2(3, -2), s + new Vector2(3 - step, 2), dark, 3f);

        target.DrawRect(new Rect2(s.X - 5.5f, s.Y - 24, 11, 22), body);
        target.DrawRect(new Rect2(s.X - 5.5f, s.Y - 13, 11, 3), dark);

        target.DrawLine(s + new Vector2(-5.5f, -21), s + new Vector2(-10, -9), dark, 3f);
        target.DrawLine(s + new Vector2(5.5f, -21), s + new Vector2(10, -9), dark, 3f);

        target.DrawCircle(s + new Vector2(0, -29), 6.5f, Skin);
        target.DrawArc(s + new Vector2(0, -29), 6.5f, Mathf.Pi, Mathf.Tau, 10, dark, 4f);

        target.DrawString(ThemeDB.FallbackFont, s + new Vector2(-34, 16), h.Name,
            HorizontalAlignment.Center, 68, 12, new Color("efe6d4"));
        target.DrawString(ThemeDB.FallbackFont, s + new Vector2(-34, 29), h.Personality,
            HorizontalAlignment.Center, 68, 10, body);
    }
}
