/// <summary>
/// 世界时钟 —— 村庄的时间。
///
/// 关键点：**时间不靠玩家点击推进。** 你不按任何键，村庄也在过日子、
/// 人也在上线下线。这是「村庄是活的」的全部前提 —— 没有它，后面所有
/// 系统都还是一个菜单。
///
/// 一个「天」是 DayLength 秒真实时间。目前 75 秒，方便一分钟看完一天。
/// </summary>
public sealed class WorldClock
{
    /// <summary>一个游戏日的真实秒数。调小 = 日子过得快。</summary>
    public float DayLength { get; } = 75f;

    /// <summary>测试用：--daylen=8 可以把一天压到 8 秒，不然 headless 等不到跨天。</summary>
    public static float DayLengthOverride(string[] args)
    {
        foreach (var a in args)
            if (a.StartsWith("--daylen=") &&
                float.TryParse(a[9..], System.Globalization.NumberStyles.Float,
                               System.Globalization.CultureInfo.InvariantCulture, out float v) && v > 0.5f)
                return v;
        return 75f;
    }

    public int Day { get; private set; } = 1;

    /// <summary>
    /// 一天内的进度 0..1。开局定在上午十点 —— 玩家打开游戏时村里得有人。
    /// </summary>
    private float _t = 10f / 24f;

    /// <summary>这一帧如果跨天了，这里是新的天数；否则为 0。外部消费一次就作废。</summary>
    public int Rolled { get; private set; }

    /// <summary>一天内的时刻，0..24。</summary>
    public float Hour => _t * 24f;

    /// <summary>从开局起一共过了多少小时。任务剩余时间用它算，才能跨天。</summary>
    public float AbsoluteHour => (Day - 1) * 24f + Hour;

    public string Phase => Hour < 5f ? "深夜"
                        : Hour < 8f ? "清早"
                        : Hour < 11f ? "上午"
                        : Hour < 14f ? "晌午"
                        : Hour < 18f ? "下午"
                        : Hour < 21f ? "傍晚" : "夜里";

    /// <summary>这个点上整个村子通常都是空的。</summary>
    public bool DeepNight => Hour >= 22f || Hour < 6f;

    public string Clock
    {
        get
        {
            int hh = (int)Hour;
            int mm = (int)(Hour * 60f) % 60;
            return $"{hh:00}:{mm:00}";
        }
    }

    public void Tick(float dt)
    {
        _t += dt / DayLength;
        Rolled = 0;
        while (_t >= 1f)
        {
            _t -= 1f;
            Day++;
            Rolled = Day;
        }
    }

    public void ConsumeRoll() => Rolled = 0;

    public WorldClock() { }

    public WorldClock(float dayLength) => DayLength = dayLength;
}
