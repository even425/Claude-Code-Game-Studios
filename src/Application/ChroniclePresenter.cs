using System.Collections.Generic;
using System.Linq;
using Godot;

namespace ClaudeCodeGameStudios.Application;

/// <summary>
/// 杂谈面板的呈现。
/// </summary>
/// <remarks>
/// <para>
/// <b>它为什么在 Application 层而不是 Domain</b>：基线 §13.2 规定应用层负责「玩家指令入口、
/// 用例协调、场景与会话生命周期」，§13.3 规定<b>领域对象</b>不应做成 Node。
/// 本类直接操作 Godot 节点，因此属于<b>呈现/应用侧</b>，不是领域对象。
/// </para>
/// <para>
/// <b>它不管什么</b>：不判断事件值不值得记（那是 <c>Events</c> 模块的事），
/// 不生成条目（那条链是「领域事件 → 杂谈条目」，见 <c>05</c> §2.3）。
/// 本类只做「已决定的条目 → 玩家看得见的文本」。
/// </para>
/// <para>
/// <b>容器由外部创建并注入</b>：节点的所有权仍归场景，
/// 呈现器只往里写，不负责创建与销毁之外的任何生命周期管理。
/// </para>
/// </remarks>
public sealed class ChroniclePresenter
{
    private readonly List<Chronicle.Entry> _entries = new();
    private bool _dirty;

    /// <param name="container">杂谈容器。不得为 null。</param>
    /// <param name="maxEntries">保留的条目上限。</param>
    /// <exception cref="System.ArgumentNullException"><paramref name="container"/> 为 null。</exception>
    public ChroniclePresenter(VBoxContainer container, int maxEntries = 40)
    {
        Container = container ?? throw new System.ArgumentNullException(nameof(container));
        MaxEntries = maxEntries;
    }

    /// <summary>条目容器。</summary>
    public VBoxContainer Container { get; }

    /// <summary>保留的条目上限。</summary>
    public int MaxEntries { get; }

    /// <summary>当前条目数。</summary>
    public int Count => _entries.Count;

    /// <summary>是否需要重建界面。</summary>
    public bool IsDirty => _dirty;

    /// <summary>
    /// 追加一条条目。<b>最新的排在最前</b>，超出上限的丢弃最旧的。
    /// </summary>
    /// <param name="entry">条目。<c>Entry</c> 是值类型，不存在 null 的情形。</param>
    public void Add(Chronicle.Entry entry)
    {
        _entries.Insert(0, entry);
        while (_entries.Count > MaxEntries) _entries.RemoveAt(_entries.Count - 1);
        _dirty = true;
    }

    /// <summary>
    /// 清空全部条目（读档时用）。
    /// </summary>
    /// <remarks>
    /// 读档是「换一个世界」：旧时间线的杂谈继续留在面板上会与新世界混在一起。
    /// 清空后由下一次 <see cref="RebuildIfDirty"/> 把容器也清掉。
    /// </remarks>
    public void Clear()
    {
        _entries.Clear();
        _dirty = true;
    }

    /// <summary>最近若干条的文本摘要，供 HUD 显示。</summary>
    /// <param name="n">取几条。</param>
    /// <returns>文本列表，按最新在前。</returns>
    public IEnumerable<string> Recent(int n) => _entries.Take(n).Select(e => e.Text);

    /// <summary>
    /// 若有改动则重建界面。<b>没改动时直接返回</b> —— 避免每帧重排节点。
    /// </summary>
    /// <param name="visibleCount">最多显示几条。</param>
    /// <returns>本次是否真的重建了。</returns>
    public bool RebuildIfDirty(int visibleCount = 16)
    {
        if (!_dirty) return false;
        _dirty = false;

        foreach (var child in Container.GetChildren()) child.QueueFree();

        foreach (var e in _entries.Take(visibleCount))
        {
            var line = new Label { Text = e.Text, AutowrapMode = TextServer.AutowrapMode.WordSmart };
            line.AddThemeFontSizeOverride("font_size", 12);
            line.AddThemeColorOverride("font_color", new Color("e6dfd4"));
            // 宽度不写死：滚动区横向已关闭，标签会拿到栏宽并按栏宽换行。
            // 写死一个宽度反而可能把整栏撑出横向滚动条。
            line.CustomMinimumSize = Vector2.Zero;
            Container.AddChild(line);

            if (e.Who is null) continue;
            var who = new Label { Text = $"—— {e.Who}" };
            who.AddThemeFontSizeOverride("font_size", 10);
            who.AddThemeColorOverride("font_color", new Color("8a7f6e"));
            Container.AddChild(who);
        }

        return true;
    }
}
