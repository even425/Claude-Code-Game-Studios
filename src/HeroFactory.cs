using Godot;
using System;
using System.Collections.Generic;
using System.Linq;


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

        public static readonly World[] All =
        {
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
                Personalities = new[] { "莽", "傲" },
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
        };

        /// <summary>从名字的第一个字反推来处；反推不出就随便挑一个。</summary>
        public static World OfName(string name)
        {
            if (string.IsNullOrEmpty(name)) return All[0];
            foreach (var w in All)
                if (w.Surname.Contains(name[0].ToString())) return w;
            return All[_rng.Next(All.Length)];
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
