using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;


/// <summary>
/// 召唤：每一次都得到一个独一无二的人。
///
/// 关键点：英雄不是从一张随机姓名表里 roll 出来的，而是**先有来处，再有人**。
/// 同一个世界里的人，名字风格和性格倾向都接近 —— 这样"异世界"才是活的，
/// 而不是一堆互不相干的随机字符串。
/// </summary>
public static class HeroFactory
{
    /// <summary>一个世界原型：名字风格 + 性格倾向 + 这个世界的人身上的痕迹。</summary>
    public sealed class World
    {
        public required string Name;
        public required string[] Surname;
        public required string[] Given;
        public required string[] Traits;
        public required string[] Personalities;
        public int Weight;

        /// <summary>
        /// 世界表 —— <b>数据驱动</b>（B4：2026-10-21 外部化）。
        /// </summary>
        /// <remarks>
        /// <para>由 <c>Main</c> 在启动时用 <see cref="LoadWorldsFromFile"/> 装载
        /// （读 <c>config/worlds.json</c>；读不到 / 解析失败 ⇒ <b>退回内置表</b>，
        /// 与价格表同一条原则：<b>配置坏了要让游戏跑得下去</b>，而不是起不来）。</para>
        /// <para><b>为什么不由静态初始化器直接读文件</b>：那要在**类初始化**时调 Godot 的
        /// <c>ProjectSettings</c> —— 单元测试进程里没有引擎，那样会直接把测试宿主带崩。
        /// 装载走 <c>Main</c>（在引擎里跑）⇒ 测试只碰纯 <c>System.IO</c> 路径。</para>
        /// <para>为什么要外部化：<c>src/CLAUDE.md</c>「Gameplay values must be data-driven」；
        /// 而这些值（名字风格、痕迹、性格倾向、出场权重）<b>是玩法值</b>，不是代码。
        /// 外部化之后，开发者调世界表<b>不用重新编译</b>。</para>
        /// </remarks>
        public static World[] All = Builtin;

        /// <summary>
        /// 装载世界表：读 <paramref name="path"/>（外部配置优先），失败 ⇒ <b>退回内置表</b>。
        /// </summary>
        /// <param name="path">世界表文件路径。</param>
        /// <returns>是否用上了外部配置（false = 用的是内置表）。</returns>
        /// <remarks>
        /// <b>为什么失败不抛</b>：世界表坏了应该「照旧用内置的跑」，
        /// 而不是让游戏起不来（同「掉落表配错不发放」那条原则）。
        /// </remarks>
        public static bool LoadWorldsFromFile(string path)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(path) || !System.IO.File.Exists(path))
                {
                    All = Builtin;
                    return false;
                }

                if (!TryLoadFromJson(System.IO.File.ReadAllText(path), out var worlds, out _))
                {
                    All = Builtin;
                    return false;
                }

                All = worlds;
                return true;
            }
            catch (System.IO.IOException)
            {
                All = Builtin;
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                All = Builtin;
                return false;
            }
        }

        /// <summary>
        /// 内置的世界表（外部配置不可用时的兜底）。
        /// </summary>
        /// <remarks>
        /// <para><b>为什么是 <c>internal</c> 而不是 <c>private</c></b>：
        /// 它与 <c>config/worlds.json</c> 是<b>同一份数据的两个副本</b>，
        /// 而「外部化世界表」这个动作本身就是「把一份数据变成两份」。
        /// 开发者改了 JSON 却忘了同步兜底 ⇒ <b>配置文件在</b>与<b>配置文件不在</b>
        /// 会给出<b>两套不同的世界表</b>（英雄的名字风格、痕迹、性格、出场权重全不一样），
        /// 而且<b>只在配置文件出问题时才暴露</b>。
        /// <para>⇒ 守卫 <c>WorldTableConfigConsistencyTests</c> 逐字段比对这两份副本。</para>
        /// </remarks>
        internal static World[] Builtin =>
        [
            new World {
                Name = "剑与魔法的大陆", Weight = 5,
                Surname = new[] { "塞", "莱", "瓦", "奥", "赫" },
                Given   = new[] { "恩", "瑞", "汀", "尔", "萨", "德", "林" },
                Traits  = new[] { "背着一把用了很多年的剑", "身上有股铁锈味", "说话很响" },
                Personalities = new[] { "傲", "莽" },
            },
            new World {
                Name = "赛博都市", Weight = 4,
                Surname = new[] { "零", "霓", "灰", "白", "夜" },
                Given   = new[] { "七", "九", "绫", "澈", "阑" },
                Traits  = new[] { "手臂上有一道旧接口的疤", "说话很快", "口袋里全是没用的零件" },
                Personalities = new[] { "贪", "傲" },
            },
            new World {
                Name = "废土边境", Weight = 4,
                Surname = new[] { "沙", "石", "铁", "灰", "锈" },
                Given   = new[] { "柯", "砾", "荼", "砂", "无" },
                Traits  = new[] { "在沙里走得太久，嗓子哑了", "左手少了一根指头", "从不脱那件外套" },
                Personalities = new[] { "莽", "贪" },
            },
            new World {
                Name = "深海港镇", Weight = 3,
                Surname = new[] { "潮", "渡", "盐", "汐", "鲸" },
                Given   = new[] { "舟", "汐", "锚", "澜", "屿" },
                Traits  = new[] { "手永远是咸的", "袖口有洗不掉的鱼腥味", "走路带响" },
                Personalities = new[] { "贪", "傲" },
            },
            new World {
                Name = "妖怪小巷", Weight = 3,
                Surname = new[] { "狐", "灯", "纸", "面" },
                Given   = new[] { "火", "影", "香", "伞", "铃" },
                Traits  = new[] { "笑起来露出的牙有点多", "总带着一股檀香味", "走路没有声音" },
                Personalities = new[] { "贪", "傲" },
            },
            new World {
                Name = "星舰编队", Weight = 3,
                Surname = new[] { "星", "远", "澄", "朔", "昴" },
                Given   = new[] { "航", "望", "序", "弦", "域" },
                Traits  = new[] { "还在适应重力的方向", "口袋里有一枚旧船徽", "很少提从前的名字" },
                Personalities = new[] { "傲", "莽" },
            },
        ];

        /// <summary>
        /// 从 <b>JSON 文本</b>读世界表（<b>internal 只为测试</b>）。
        /// </summary>
        /// <remarks>
        /// <para><b>格式</b>：<c>{ "worlds": [ { "name": …, "weight": n, "surname": [...],
        /// "given": [...], "traits": [...], "personalities": [...] } ] }</c></para>
        /// <para><b>校验</b>：名字不能空；四组数组都不能空；
        /// 权重必须是 <b>正数</b>（否则这个世界永远抽不到，而调用方不会报错
        /// ⇒ <b>静默的世界消失</b>）。</para>
        /// </remarks>
        /// <param name="json">JSON 文本。</param>
        /// <param name="worlds">读出来的世界表（成功时）。</param>
        /// <param name="error">失败原因（成功时为 null）。</param>
        /// <returns>成功返回 true。</returns>
        internal static bool TryLoadFromJson(string json, out World[] worlds, out string? error)
        {
            worlds = Array.Empty<World>();
            error = null;

            if (string.IsNullOrWhiteSpace(json))
            {
                error = "世界表文件是空的。";
                return false;
            }

            JsonDocument doc;
            try
            {
                doc = JsonDocument.Parse(json);
            }
            catch (JsonException ex)
            {
                error = $"世界表不是合法的 JSON：{ex.Message}";
                return false;
            }

            using (doc)
            {
                var root = doc.RootElement;
                if (!root.TryGetProperty("worlds", out var arr)
                    || arr.ValueKind != JsonValueKind.Array
                    || arr.GetArrayLength() == 0)
                {
                    error = "世界表里没有 worlds 数组（或数组是空的）。";
                    return false;
                }

                var list = new List<World>();
                foreach (var w in arr.EnumerateArray())
                {
                    string name = w.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String
                        ? n.GetString() ?? ""
                        : "";
                    if (string.IsNullOrEmpty(name))
                    {
                        error = "世界表里有名字为空的世界。";
                        return false;
                    }

                    if (!w.TryGetProperty("weight", out var wt)
                        || wt.ValueKind != JsonValueKind.Number
                        || !wt.TryGetInt32(out int weight)
                        || weight <= 0)
                    {
                        error = $"世界「{name}」的权重必须是正整数。";
                        return false;
                    }

                    var surname = ReadStrings(w, "surname");
                    var given = ReadStrings(w, "given");
                    var traits = ReadStrings(w, "traits");
                    var personalities = ReadStrings(w, "personalities");
                    if (surname.Count == 0 || given.Count == 0
                        || traits.Count == 0 || personalities.Count == 0)
                    {
                        error = $"世界「{name}」缺 surname / given / traits / personalities 之一（或为空）。";
                        return false;
                    }

                    list.Add(new World
                    {
                        Name = name,
                        Weight = weight,
                        Surname = surname.ToArray(),
                        Given = given.ToArray(),
                        Traits = traits.ToArray(),
                        Personalities = personalities.ToArray(),
                    });
                }

                worlds = list.ToArray();
                return true;
            }
        }

        /// <summary>读一个字符串数组（缺字段 / 不是数组 / 元素不是字符串 ⇒ 空表）。</summary>
        private static List<string> ReadStrings(JsonElement obj, string key)
        {
            var result = new List<string>();
            if (!obj.TryGetProperty(key, out var arr) || arr.ValueKind != JsonValueKind.Array)
            {
                return result;
            }

            foreach (var item in arr.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String)
                {
                    string s = item.GetString() ?? "";
                    if (!string.IsNullOrEmpty(s)) result.Add(s);
                }
            }
            return result;
        }
    }

    private static Random _rng = new();

    /// <summary>
    /// 固定召唤用的随机源（D-054 阶段 3：让「同一种子两次跑」真正可复现）。
    /// </summary>
    /// <param name="seed">种子（通常传 <c>Main.BootSeed</c>）。</param>
    /// <remarks>
    /// <para><b>为什么必须有这个</b>：召唤用的是<b>另一个</b>随机源
    /// （<see cref="_rng"/>，<c>System.Random</c>），与游戏主随机流（<c>Main._rng</c>）<b>无关</b>。
    /// 早先只固定了 <c>Main.BootSeed</c>，结果两次跑的<b>英雄本人</b>（名字/世界/性格/属性）
    /// 仍然不同 → 经济行为随之不同 → 采集数据**依然不可复现**
    /// （实测：模拟时间与副本实例标识两次完全一致，但英雄不同、行数不同）。
    /// </para>
    /// <para><b>调用时机</b>：必须在<b>第一次召唤之前</b>（<c>Main._Ready</c> 里
    /// <c>OnSummon()</c> 之前）。</para>
    /// </remarks>
    public static void Seed(ulong seed) => _rng = new Random((int)(seed & 0x7FFFFFFF));

    /// <summary>村民口头上的绰号。杂谈里 {a} 槽位用这个。</summary>
    internal static readonly string[] Nicknames =
    {
        "那个不知死活的", "鼻孔看人的", "数钱比谁都快",
        "摔断过腿的", "说话不留情的", "手比嘴快的",
        "一天都闲不住的", "来了三个月没搭过话的", "连柴火都往怀里搂的",
    };

    public sealed record Summoned(HeroAgent Hero, string WorldName, string Trait);

    public static Summoned Summon()
    {
        int total = World.All.Sum(w => w.Weight);
        int r = _rng.Next(total);
        World w = World.All[0];
        foreach (var cand in World.All)
        {
            r -= cand.Weight;
            if (r < 0) { w = cand; break; }
        }

        // 性格：这个世界里更容易出现的两种里挑一种
        string p = w.Personalities[_rng.Next(w.Personalities.Length)];

        // 名字由世界决定，不由全局姓名表决定
        string name = w.Surname[_rng.Next(w.Surname.Length)] + w.Given[_rng.Next(w.Given.Length)];

        // 保底：三项之和 >= 60。纯随机会 roll 出废人，玩家会觉得召唤系统坏了。
        //
        // ⚠️ **【实现占位 · 不是玩法决定】**（基线 §10.6 / §17「禁止事项」）：
        // 「召唤保底」是 §10.6 的 **【待定】** 项。这个 60 **替开发者回答了它**，
        // 所以必须**显式标注**：它是**为了让原型跑得下去**的占位阈值，
        // **不是**「设计已定的保底值」。正式数值由开发者定（→ §10.8 S1–S7 的一并评审）。
        int pow, spd, grt;
        do
        {
            pow = 10 + _rng.Next(40);
            spd = 10 + _rng.Next(40);
            grt = 10 + _rng.Next(40);
        } while (pow + spd + grt < 60);

        // 第二根轴：他投入多少。体力好的多半是肝帝，垫底的多数挂机。
        //
        // ⚠️ 同上：**【实现占位】**——「勤奋度」与属性的相关性（体力越好越可能是肝帝）
        // 也是占位比例，不是设计结论。
        float roll = _rng.NextSingle();
        float liverBar = 0.28f + (grt - 10) / 120f;      // 体力越好越可能是肝帝
        float idleBar  = 0.72f + (grt - 10) / 160f;
        string dil = roll < liverBar ? "肝"
                   : roll < idleBar  ? "随手" : "挂机";

        // ⚠️ 这里原来算「时区 / 作息」（<c>ScheduleFor</c>）—— **算完就丢**
        // （三个返回值一个都没进下面的对象初始化器）。
        // 那套「时区作息」设计已由 **D-001** 判死（英雄总在场，不做在线窗口），
        // 留着它只会让人以为「作息」还在起作用。2026-10-21 清理。

        var hero = new HeroAgent
        {
            Name = name,
            Personality = p,
            Diligence = dil,
            Power = pow,
            Speed = spd,
            Grit = grt,
        };
        return new Summoned(hero, w.Name, w.Traits[_rng.Next(w.Traits.Length)]);
    }

}
