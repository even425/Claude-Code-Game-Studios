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

    private static readonly Random _rng = new();

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
        int pow, spd, grt;
        do
        {
            pow = 10 + _rng.Next(40);
            spd = 10 + _rng.Next(40);
            grt = 10 + _rng.Next(40);
        } while (pow + spd + grt < 60);

        // 第二根轴：他投入多少。体力好的多半是肝帝，垫底的多数挂机。
        float roll = _rng.NextSingle();
        float liverBar = 0.28f + (grt - 10) / 120f;      // 体力越好越可能是肝帝
        float idleBar  = 0.72f + (grt - 10) / 160f;
        string dil = roll < liverBar ? "肝"
                   : roll < idleBar  ? "随手" : "挂机";

        var (localAt, hours, tz) = ScheduleFor(p, dil);

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

    /// <summary>
    /// 他们从地球各处来。**时区是最要紧的一栏** —— 地球一边是晚上，另一边就是早上，
    /// 所以村庄不会天黑。人越多，24 小时越难有空档。
    ///
    /// 这里给的只是他们**自己那边**的作息偏好，性格只影响偏好不影响许可：
    /// 莽爱在本地深夜上线，贪爱在本地白天，傲什么时候都行。
    /// </summary>
    private static readonly float[] TimeZones =
        { -11f, -8f, -5f, -3f, 0f, 1f, 3f, 5f, 8f, 9f, 13f };

    /// <summary>
    /// 时区是**加权抽，不是按性格切分** —— 裁定（2026-10-17）。
    ///
    /// 切分看着更干净，但它会把时区池劈成两半，早先靠「时区一散洞自己就没了」
    /// 补上的那个清晨空档有可能重新裂开。**池子不动，只压概率。**
    ///
    ///  weighting 的理由：莽是夜里肝副本的人，多半在东亚那边；
    /// 贪是白天做买卖的人，多半在北美。**这是倾向不是规则** ——
    /// 花名册上你慢慢会看出「莽好像都在东边」，但推不出「今晚一定强」。
    /// 可读，不可解。这正是这个游戏要的手感。
    /// </summary>
    private static int WeightFor(string p, float tz)
    {
        bool eastNight = tz >= 8f;      // 东八、东九、新西兰 —— 当地正是深夜
        bool usDay     = tz <= -3f;      // 北美西/中/东岸 —— 当地正是白天
        return p switch
        {
            "莽" => eastNight ? 10 : 1,
            "贪" => usDay ? 10 : 1,
            _    => 1,                    // 傲：什么时候都行
        };
    }

    private static float PickTimezone(string p)
    {
        int total = 0;
        foreach (float tz in TimeZones) total += WeightFor(p, tz);
        int r = _rng.Next(total);
        foreach (float tz in TimeZones)
        {
            r -= WeightFor(p, tz);
            if (r < 0) return tz;
        }
        return TimeZones[TimeZones.Length - 1];
    }

    private static (float localAt, float hours, float tz) ScheduleFor(string p, string dil)
    {
        float localAt = p switch
        {
            "莽" => 19f + _rng.NextSingle() * 4f,          // 本地深夜上线
            "贪" => 9f + _rng.NextSingle() * 6f,           // 本地白天
            _    => 13f + _rng.NextSingle() * 9f,          // 什么时候都行
        };
        float hours = p switch
        {
            "莽" => 4f + _rng.NextSingle() * 4f,
            "贪" => 4f + _rng.NextSingle() * 3f,
            _    => 2.5f + _rng.NextSingle() * 4f,
        };
        hours *= dil switch { "肝" => 1.25f, "挂机" => 0.70f, _ => 1.0f };
        return (localAt, Math.Clamp(hours, 1.5f, 9f), PickTimezone(p));
    }
}
