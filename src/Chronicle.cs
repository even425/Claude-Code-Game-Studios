using System;
using System.Collections.Generic;
using System.Linq;


/// <summary>
/// 村庄杂谈 —— 一个文字面板，不是日志，也不是整个游戏。
///
/// 模板经过检查：不含"成功/失败/获得/消耗/属性/奖励"这类系统词。
/// 村民不会那样说话。刻意用碎片、留白、互相矛盾的语气。
/// </summary>
public static class Chronicle
{
    public readonly record struct Entry(int Day, string Text, string? Who);

    private static readonly Random _rng = new();

    private static readonly string[] Others =
    {
        "老李头", "酒保", "村口的大婶", "打铁的", "隔壁王家的", "东头的孩子",
    };

    private static readonly string[] Places =
    {
        "北坡", "南渠", "东林", "河滩", "北境旧堡", "镇上",
    };

    // 槽位：{n}名字 {a}绰号 {t}任务 {r}材料 {g}数量 {l}地名 {o}其他人 {w}来处 {s}特征
    private static readonly Dictionary<string, Dictionary<string, string[]>> Templates = new()
    {
        ["莽"] = new()
        {
            ["take"] = new[]{
                "{o}说，{n}看见告示就去了，也没问是什么活。",
                "又是{n}。别人还在掂量，他已经把家伙什收拾好了。",
            },
            ["good"] = new[]{
                "{a}{n}回来的时候天还没黑。背上{g}份{r}，比要的还多。",
                "{n}在{l}待了比说好的久。回来{g}份{r}，鞋磨穿了一双。",
                "{a}{n}进门第一句是「还有吗」。{g}份{r}堆在门口，人靠着墙就睡了。",
            },
            ["over"] = new[]{
                "{n}带回来的比说好的多。多出来那几块，来路没人问。",
                "{o}偷偷讲，{n}在{l}顺手牵了别的。{g}份{r}交上来了，多的那份没提。",
            },
            ["fail"] = new[]{
                "{a}{n}没按时回来。{o}在{l}找到他，{r}撒了一地，人没事。",
                "{n}空手进门，说{l}白跑一趟。他脸色不好看，但看不出为哪桩。",
            },
        },
        ["傲"] = new()
        {
            ["refuse"] = new[]{
                "「搬{r}？让{o}去。」{a}{n}把话撂下就走，一点余地没留。",
                "{n}瞥了一眼告示，退回来的时候什么也没说。{o}说他嫌这活儿跌份。",
                "又来了。{n}把{r}的差事推了，说「我不是干这个的」。",
            },
            ["take"] = new[]{
                "本以为{a}{n}不会管。听说{l}那地方难，他反倒来了兴致。",
                "{n}路过告示，停下来看了很久，然后把它揭走了。",
            },
            ["good"] = new[]{
                "{a}{n}回来一句话没说，{g}份{r}交到库房，人就进屋了。",
                "{n}不但做完了，还在{l}多待了半日。回来{g}份{r}，脸上看不出满意。",
                "{o}小声说，{n}把活儿干得挑不出毛病，就是全程黑着脸。",
            },
            ["fail"] = new[]{
                "{a}{n}栽了。回来不肯讲经过，只说{l}那地方，不该他去。",
                "{n}这次没挑。回来的时候一句话没说，第二天照常吃饭。",
            },
        },
        ["贪"] = new()
        {
            ["take"] = new[]{
                "{a}{n}揭得比谁都快。别人还在商量，他已经把{r}捆好了。",
                "{n}接活的时候数了两遍要带多少绳索。{o}说他从来不空着手。",
            },
            ["good"] = new[]{
                "{n}把{r}全交上来了。数的人报个数，{n}站在旁边，眼睛没离开那堆。",
                "{a}{n}带回{g}份{r}，然后在村口站了很久，最后还是进屋了。",
            },
            ["pocket"] = new[]{
                "清点的时候短了几份{r}。问了一圈，都说没看见。",
                "{o}讲，{n}交上来的数总比说好的少一点。少多少，从来没人说得准。",
                "库房那边又在对账。{a}{n}今天路过，笑了一下。",
            },
            ["fail"] = new[]{
                "{n}空手回来，站在门口不进来。{o}问他{l}怎么了，他说没赶上。",
                "{a}{n}这趟亏了，回来一句话没说就回了屋。门关得比平时响。",
            },
        },
    };

    /// <summary>
    /// 某个性格没有自己的拒工文案时的回落来源。
    /// 目前只有「傲」会拒工（<c>Main.cs:311</c>），此字段用于防止将来别的性格
    /// 也能拒工时，拒工文案被静默回落到 <c>good</c> 而把「没干」记成「干成了」。
    /// 见 <see cref="Outcome"/>。
    /// </summary>
    private static readonly string[] RefuseFallback = Templates["傲"]["refuse"];

    // ---- 队伍内部的事：性格冲突 ----

    public static Entry SquadLeft(int day, params string[] names)
    {
        string tpl = Pick(new[]
        {
            $"{string.Join("、", names)} 出发了。村里一下子安静下来。",
            $"{string.Join("、", names)} 天没亮就走了。{Others[_rng.Next(Others.Length)]}在村口看着他们。",
            $"{string.Join("、", names)} 背了东西上路。走了以后，那几间屋的烟囱半天没冒烟。",
        });
        return new Entry(day, tpl, null);
    }

    public static Entry SquadBack(int day, string who, string res, int qty)
    {
        string tpl = Pick(new[]
        {
            $"{who}他们回来了。{qty}份{res}堆在库房门口。",
            "天黑的时候人到了。带回来的比说好的沉。",
            $"{who}进门先喝水，然后才报数：{qty}份{res}。",
        });
        return new Entry(day, tpl, null);
    }

    public static Entry SiteOpened(int day, string what, int workers)
    {
        string tpl = workers switch
        {
            0 => $"{what}的地基挖好了，可是村里没人手。架子立在那儿。",
            1 => $"{what}开工了。{Others[_rng.Next(Others.Length)]}一个人扛着木料，半天没搭上两根柱子。",
            2 => $"{what}开工了。两个人抬一根梁，谁也没嫌重。",
            _ => $"{what}开工了。村里能动的都上了。远处看着像个蚂蚁窝。",
        };
        return new Entry(day, tpl, null);
    }

    public static Entry Building(int day, string what)
    {
        string tpl = Pick(new[]
        {
            $"{what}，前后用了十来天。村里几个人手上都磨出了泡。",
            "收工那天没人在乎是谁先想到的法子。墙立起来了就行。",
            $"{what}。有人说这是全村一起扛下来的，也有人说主要靠那几个能干的。",
        });
        return new Entry(day, tpl, null);
    }

    private static readonly string[] Arrive =
    {
        "从{w}来的{n}今天到了村里。{s}。",
        "{w}那边又送来一个。{a}{n}站在村口没进来，{o}过去拉的。",
        "{n}是{w}来的。村里第一次见这种{s}的人。",
    };

    public static Entry Summoned(int day, HeroAgent h, string world, string trait)
    {
        string tpl = Pick(Arrive);
        return new Entry(day, Fill(tpl, h, "新来的", "木料", 1, null, world, trait), h.Name);
    }

    /// <summary>
    /// 一次副本的结果 —— 战斗打完了没有。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么需要它</b>：验收⑤要求「自动战斗可读」。
    /// 若战斗打完只在杂谈里留下「他们回来了 / 什么也没带回来」，
    /// 玩家看不出<b>打过一场三阶段的仗</b>，只看得出跑了一趟腿。
    /// 这一条让「决定成败的那一场」在杂谈里留下痕迹（D5 的玩家可见形态）。</para>
    /// <para><b>不写伤亡。</b>死亡即复活、无额外惩罚是【已确认】设定，
    /// 所以败北的文案只说「没打下来 / 空手而回」，绝不暗示少了人 ——
    /// 那会与设定相反。</para>
    /// </remarks>
    /// <param name="day">日序号。</param>
    /// <param name="place">副本地名。</param>
    /// <param name="names">参战者名字。</param>
    /// <param name="heroesWon">是否打下来了。</param>
    public static Entry DungeonDecided(int day, string place, string[] names, bool heroesWon)
    {
        string who = string.Join("、", names);
        string key = heroesWon ? "cleared" : "repelled";
        string[] pool = DungeonTemplates[key];
        return new Entry(day, Fill(Pick(pool), Named(who), place, "铁料", 0, place), null);
    }

    /// <summary>副本结果的模板池。键：<c>cleared</c> / <c>repelled</c>。</summary>
    private static readonly Dictionary<string, string[]> DungeonTemplates = new()
    {
        ["cleared"] = new[]
        {
            "{t}最后一道关是{n}他们硬顶过去的。",
            "{n}从{t}出来的时候天还没亮，谁也没说话，但都在笑。",
            "{o}说{t}那几道门，第二天被人重新关上了。",
        },
        ["repelled"] = new[]
        {
            "{t}没打下来。{n}几个空着手回了村。",
            "{n}他们从{t}退回来的时候，谁都没先开口。",
            "{t}那边的事，等{n}睡下才有人提。",
        },

        // ── 逐场（验收⑤）──
        ["clearedDeciding"] = new[]
        {
            "{t}是最后一道。{n}他们顶住了。",
            "{t}：{n}几个是站着出来的。",
        },
        ["repelledStage"] = new[]
        {
            "{t}：没顶住。{n}几个退了出来。",
            "{l}里那一场，{n}几个是被带出来的。",
        },
    };

    /// <summary>造一个只带名字的 <see cref="HeroAgent"/>，供杂谈填槽用。</summary>
    private static HeroAgent Named(string name) => new() { Name = name };

    /// <summary>
    /// 一场遭遇的结果 —— 副本里的<b>单个阶段</b>（验收⑤「自动战斗可读性」）。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么逐场都要说</b>：副本有三场（<c>DungeonFlow.DeclaredStageCount</c>）。
    /// 只说最后一句「打下来了 / 没打下来」，玩家看不出中间发生了什么 ——
    /// 那与「根本没打」在画面上没有区别。</para>
    /// <para><b>决定性那一场措辞不同</b>：它是终结整个副本的那一场，
    /// 说得更重一点。</para>
    /// </remarks>
    /// <param name="day">日序号。</param>
    /// <param name="place">副本地名。</param>
    /// <param name="encounter">这一场的遭遇名。</param>
    /// <param name="names">参战者名字。</param>
    /// <param name="heroesWon">这一场是否打下来了。</param>
    /// <param name="isDeciding">是否是决定整个副本成败的那一场（I3）。</param>
    public static Entry EncounterFought(
        int day, string place, string encounter, string[] names, bool heroesWon, bool isDeciding)
    {
        string who = string.Join("、", names);
        string key = heroesWon
            ? (isDeciding ? "clearedDeciding" : "cleared")
            : "repelledStage";
        string[] pool = DungeonTemplates[key];
        return new Entry(day, Fill(Pick(pool), Named(who), encounter, "铁料", 0, place), null);
    }

    /// <summary>
    /// 决定 <see cref="Outcome"/> 使用哪个模板键。
    /// <para>
    /// 「refuse」**绝不返回 "good"** —— 那会把「没干」记成「干成了」，与领域事实相反
    /// （基线 §7.2：杂谈反映已发生的事件）。某个性格没有自己的拒工文案时，键仍是
    /// "refuse"，由 <see cref="ResolvePool"/> 借用傲的文案。
    /// </para>
    /// <para>
    /// 公开是为了让测试能直接断言**选中了哪个键**。杂谈文本本身每次抽样都不同
    /// （<c>{a}</c> / <c>{o}</c> 随机填充），断言文本会不稳定；断言键是稳定的。
    /// </para>
    /// </summary>
    /// <param name="h">英雄，决定用哪一组模板。</param>
    /// <param name="kind">good | over | pocket | fail | refuse。</param>
    /// <returns>实际使用的模板键。</returns>
    public static string SelectTemplateKey(HeroAgent h, string kind)
    {
        var t = Templates[h.Personality];

        if (kind == "refuse")
        {
            bool hasOwn = t.ContainsKey("refuse");
            bool hasFallback = Templates["傲"].ContainsKey("refuse");
            return hasOwn || hasFallback ? "refuse" : "good";
        }

        string key = kind switch
        {
            "over"   => "over",
            "pocket" => "pocket",
            "fail"   => "fail",
            _        => "good",
        };
        return t.ContainsKey(key) ? key : "good";
    }

    /// <summary>取出 <paramref name="key"/> 对应的模板池；缺文案时按 <see cref="SelectTemplateKey"/> 的约定回落。</summary>
    private static string[] ResolvePool(HeroAgent h, string key)
    {
        var t = Templates[h.Personality];
        if (t.TryGetValue(key, out var own)) return own;
        if (key == "refuse") return RefuseFallback;
        return t["good"];
    }

    /// <summary>按 <paramref name="kind"/> 生成一条杂谈条目。</summary>
    /// <param name="kind">good | over | pocket | fail | refuse。</param>
    public static Entry Outcome(int day, HeroAgent h, string task, string res, int qty,
                                string place, string kind)   // good|over|pocket|fail|refuse
    {
        string[] pool = ResolvePool(h, SelectTemplateKey(h, kind));
        return new Entry(day, Fill(Pick(pool), h, task, res, qty, place), h.Name);
    }

    // ------------------------------------------------------------------

    private static string Pick(string[] a) => a[_rng.Next(a.Length)];
    private static string PickPlace() => Places[_rng.Next(Places.Length)];
    private static string PickOther() => Others[_rng.Next(Others.Length)];

    private static string Fill(string tpl, HeroAgent h, string task, string res, int qty,
                               string? place, string world = "", string trait = "",
                               HeroAgent? other = null)
    {
        return tpl
            .Replace("{x}", h.Name)
            .Replace("{y}", other?.Name ?? "")
            .Replace("{n}", h.Name)
            .Replace("{a}", Nick(h))
            .Replace("{t}", task)
            .Replace("{r}", res)
            .Replace("{g}", qty.ToString())
            .Replace("{l}", place ?? PickPlace())
            .Replace("{o}", PickOther())
            .Replace("{w}", world)
            .Replace("{s}", trait);
    }

    private static string Nick(HeroAgent h) =>
        HeroFactory.Nicknames[_rng.Next(HeroFactory.Nicknames.Length)];
}
