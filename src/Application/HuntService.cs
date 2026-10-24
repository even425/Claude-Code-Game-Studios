using System;
using System.Collections.Generic;
using System.Linq;
using ClaudeCodeGameStudios.Domain.Combat;
using ClaudeCodeGameStudios.Domain.Heroes;
using ClaudeCodeGameStudios.Domain.Items;
using ClaudeCodeGameStudios.Domain.Wildlife;

namespace ClaudeCodeGameStudios.Application;

/// <summary>一次狩猎的结果。</summary>
/// <param name="HeroId">猎人。</param>
/// <param name="Won">打赢了吗。</param>
/// <param name="Beast">遇到的野兽（没找到野兽时为 null）。</param>
/// <param name="Loot">拿到的战利品（物品 Id → 份数）。</param>
public readonly record struct HuntOutcome(
    HeroId HeroId,
    bool Won,
    WildBeast? Beast,
    IReadOnlyDictionary<string, long> Loot);

/// <summary>
/// <b>狩猎</b>（D-118）—— 英雄出村打一只野兽。
/// </summary>
/// <remarks>
/// <para><b>为什么要有它</b>：D-117 有了「村外」与野兽，但野兽还只是<b>世界的样子</b>
/// ⇒ 玩家提的「村里的人会跑出去狩猎」还差最后一截。
/// ⇒ 狩猎同时给野兽<b>意义</b>（能被猎）⇒ 也给「材料」一个<b>新出口</b>（兽皮 / 兽肉可卖）。</para>
///
/// <para>⚠️ <b>规则住在应用层（可离线单测）</b>：找野兽 → 打一场 → 发战利品
/// ⇒ 表现层只负责「把英雄画到村外」。</para>
///
/// <para>⚠️ <b>战斗完全确定</b>（D-062 不设命中）⇒ 同一个英雄打同一只野兽，结果永远一样。</para>
/// </remarks>
public sealed class HuntService
{
    /// <summary>野兽种类 ⇒ 它有多凶（战斗解算器的档位：0 普通 / 1 精英 / 2 首领）。</summary>
    /// <remarks>
    /// ⚠️ **建议值**：野猪 = 普通（大多数英雄打得过）、狼 = **精英**（只有强的人打得过）、
    /// 鹿 = 普通（它是「跑得快、不好抓」的那种 ⇒ 给的战利品多）。
    /// </remarks>
    private static int StageOf(BeastKind kind) => kind switch
    {
        BeastKind.Wolf => 1,
        _ => 0,
    };

    /// <summary>野兽种类 ⇒ 它身上有什么（物品 Id → 份数）。</summary>
    private static IReadOnlyDictionary<string, long> LootOf(BeastKind kind) => kind switch
    {
        BeastKind.Deer => new Dictionary<string, long> { ["兽肉"] = 2, ["兽皮"] = 1 },
        BeastKind.Wolf => new Dictionary<string, long> { ["兽皮"] = 2 },
        _ => new Dictionary<string, long> { ["兽皮"] = 1, ["兽肉"] = 1 },
    };

    private readonly WildlifeBook _wildlife;
    private readonly IBattleResolver _resolver;
    private readonly IBattleOutcomeReader _reader;
    private readonly Func<HeroId, (string Name, CombatStats Stats)?> _heroStats;
    private readonly Action<HeroId, IReadOnlyDictionary<string, long>> _grantLoot;

    /// <summary>造一个狩猎服务。</summary>
    /// <param name="wildlife">野兽簿。</param>
    /// <param name="resolver">战斗解算器（与副本、试炼塔共用同一个 ⇒ 战斗规则只有一份）。</param>
    /// <param name="reader">胜负读者（与解算器成对）。</param>
    /// <param name="heroStats">取英雄的名字与战斗属性（没有就当找不到人）。</param>
    /// <param name="grantLoot">发战利品（由调用方接上物品发放入口）。</param>
    public HuntService(
        WildlifeBook wildlife,
        IBattleResolver resolver,
        IBattleOutcomeReader reader,
        Func<HeroId, (string Name, CombatStats Stats)?> heroStats,
        Action<HeroId, IReadOnlyDictionary<string, long>> grantLoot)
    {
        _wildlife = wildlife ?? throw new ArgumentNullException(nameof(wildlife));
        _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));
        _heroStats = heroStats ?? throw new ArgumentNullException(nameof(heroStats));
        _grantLoot = grantLoot ?? throw new ArgumentNullException(nameof(grantLoot));
    }

    /// <summary>现在村外有几只野兽（供界面显示「附近有野兽」）。</summary>
    public int BeastsOutside => _wildlife.Beasts.Count;

    /// <summary>
    /// 找一只野兽（离村子中心最近的那只）—— 供表现层把猎人画到它旁边。
    /// </summary>
    /// <returns>找到的野兽；野外没有野兽时返回 null。</returns>
    public WildBeast? FindTarget()
    {
        WildBeast? best = null;
        int bestDist = int.MaxValue;
        foreach (var beast in _wildlife.Beasts)
        {
            // 离村子中心（7,5）越近越近 ⇒ 先被盯上的那只。
            int dx = beast.X - 7;
            int dy = beast.Y - 5;
            int dist = dx * dx + dy * dy;
            if (dist < bestDist)
            {
                bestDist = dist;
                best = beast;
            }
        }

        return best;
    }

    /// <summary>
    /// 打一场：找到野兽 → 打 → 赢了发战利品并<b>把那只野兽移走</b>（它被猎到了）。
    /// </summary>
    /// <param name="heroId">猎人。</param>
    /// <returns>结果；野外没有野兽时 <c>Won=false</c> 且没有野兽。</returns>
    public HuntOutcome Try(HeroId heroId)
    {
        var hero = _heroStats(heroId);
        if (hero is null) return new HuntOutcome(heroId, false, null, new Dictionary<string, long>());

        var beast = FindTarget();
        if (beast is null)
        {
            return new HuntOutcome(heroId, false, null, new Dictionary<string, long>());
        }

        var party = new CombatPartySnapshot(new[]
        {
            CombatantSnapshot.Create(heroId.Value, hero.Value.Name, stats: hero.Value.Stats),
        });

        var log = _resolver.Resolve("狩猎", party, StageOf(beast.Kind));
        bool won = _reader.DidHeroesWin(log);

        if (!won)
        {
            // ⚠️ **打输不带走野兽**（它跑了）⇒ 明天还能再猎。
            return new HuntOutcome(heroId, false, beast, new Dictionary<string, long>());
        }

        var loot = LootOf(beast.Kind);
        _wildlife.Remove(beast);
        _grantLoot(heroId, loot);

        return new HuntOutcome(heroId, true, beast, loot);
    }
}
