using System;
using System.Collections.Generic;
using System.Linq;

namespace ClaudeCodeGameStudios.Domain.Combat;

/// <summary>战斗结果标识。</summary>
/// <param name="Value">标识值。</param>
public readonly record struct BattleResultId(string Value)
{
    /// <inheritdoc />
    public override string ToString() => string.IsNullOrEmpty(Value) ? "(无)" : Value;
}

/// <summary>战斗结果的一次判定条目。</summary>
/// <remarks>
/// <b>每条都必须能说明「它对胜负贡献了什么」。</b>这是基线 §8.5 的验收项 D5
/// 「战斗日志能复现胜负判定所引用的每一个关键事件」的可观测形态。
/// </remarks>
/// <param name="EntryId">条目标识。</param>
/// <param name="Summary">发生了什么。</param>
/// <param name="DecidedOutcome">
/// <c>true</c> 表示这一条<b>直接改变了</b>胜负判定；<c>false</c> 表示它只是过程记录。
/// </param>
public readonly record struct BattleLogEntry(string EntryId, string Summary, bool DecidedOutcome);

/// <summary>
/// 战斗结果。**创建后不可变。**
/// </summary>
/// <remarks>
/// <para>
/// <b>唯一写者：<c>Combat</c> 模块。</b>它负责「自动战斗执行、产出战斗结果」，
/// <b>不负责阶段流转</b>（<c>Dungeon</c>）、
/// <b>不负责奖励内容</b>（<c>Settlement</c>，且属【待定】T-004）。
/// </para>
/// <para><b>不可变</b>：更正方式是「新的结果 + 显式替换 + 记录理由」，而不是原地改字段。</para>
/// </remarks>
public sealed class BattleResult
{
    private BattleResult(
        BattleResultId id, string encounterName, bool heroesWon,
        IReadOnlyList<string> participantSnapshot, IReadOnlyList<BattleLogEntry> log)
    {
        Id = id;
        EncounterName = encounterName;
        HeroesWon = heroesWon;
        Participants = participantSnapshot;
        Log = log;
    }

    /// <summary>结果标识。</summary>
    public BattleResultId Id { get; }

    /// <summary>本次遭遇的名称。</summary>
    public string EncounterName { get; }

    /// <summary>英雄是否获胜。</summary>
    public bool HeroesWon { get; }

    /// <summary>参与者<b>快照</b>，不是活引用。</summary>
    public IReadOnlyList<string> Participants { get; }

    /// <summary>
    /// 战斗日志。
    /// <para>
    /// <b>构造期已保证</b>其中至少有一条 <see cref="BattleLogEntry.DecidedOutcome"/>
    /// 为 <c>true</c> 的条目 —— 否则这场战斗没有任何「关键事件」可供复核，
    /// <see cref="IsVerifiableAgainstLog"/> 将永远为 <c>false</c>，
    /// 也就是<b>把 bug 藏进了画面里</b>。
    /// </para>
    /// </summary>
    public IReadOnlyList<BattleLogEntry> Log { get; }

    /// <summary>
    /// 胜负判定<b>引用</b>的日志条目标识。
    /// </summary>
    /// <remarks>
    /// 基线 §8.5 验收项 <b>D5</b>：战斗日志必须能复现胜负判定所引用的每一个关键事件。
    /// 这条属性就是那句要求的机械落点 —— 判定引用的条目必须<b>全部存在于日志中</b>，
    /// 且<b>互不重复</b>（否则计数错位）。
    /// <para>
    /// 只会由 <see cref="CiteDecidingEntries"/> 写入，每次<b>整体替换</b>；默认空集合
    /// 表示「还没交代过判定依据」，此时 <see cref="IsVerifiableAgainstLog"/> 为 <c>false</c>。
    /// </para>
    /// </remarks>
    public IReadOnlyList<string> DecidingEntryIds { get; private set; } = Array.Empty<string>();

    /// <summary>
    /// 产出一个战斗结果。
    /// </summary>
    /// <param name="id">结果标识。</param>
    /// <param name="encounterName">遭遇名称。</param>
    /// <param name="heroesWon">胜负。</param>
    /// <param name="participants">参与者标识列表。</param>
    /// <param name="log">
    /// 战斗日志。<b>不得为空</b>，且<b>至少含一条
    /// <see cref="BattleLogEntry.DecidedOutcome"/> 为 <c>true</c> 的条目</b>。
    /// </param>
    /// <exception cref="ArgumentException">
    /// 标识为空、没有参与者、日志为空，
    /// 或<b>日志中没有任何一条真正改变了判定的条目</b>（D5）。
    /// </exception>
    public static BattleResult Produce(
        BattleResultId id,
        string encounterName,
        bool heroesWon,
        IReadOnlyList<string> participants,
        IReadOnlyList<BattleLogEntry> log)
    {
        if (string.IsNullOrEmpty(id.Value))
        {
            throw new ArgumentException("战斗结果标识不能为空。", nameof(id));
        }

        if (participants is null || participants.Count == 0)
        {
            throw new ArgumentException("战斗必须有参与者。", nameof(participants));
        }

        if (log is null || log.Count == 0)
        {
            throw new ArgumentException(
                $"战斗 {id} 的日志为空。基线 §8.5 验收项 D5 要求战斗日志能复现胜负判定，"
                + "一场没有日志的战斗无法被玩家理解，也无法被测试复核。",
                nameof(log));
        }

        // 「有日志」还不够：一条 DecidedOutcome 全为 false 的日志，每一条都只是过程记录，
        // 于是 IsVerifiableAgainstLog() 永远为 false —— 战斗结果无法交代自己凭什么赢的。
        // 这必须在**构造期**就炸掉：留到运行期，症状只会表现为「复核面板一直是空的」，
        // 也就是把 bug 藏进了画面里。
        if (!log.Any(e => e.DecidedOutcome))
        {
            throw new ArgumentException(
                $"战斗 {id} 的日志里没有任何一条 DecidedOutcome == true 的条目。"
                + "基线 §8.5 验收项 D5：战斗日志必须能复现胜负判定所引用的每一个关键事件 ——"
                + "一场没有关键事件的战斗无法被复核；日志不是装饰。",
                nameof(log));
        }

        // 日志自身的条目 ID 必须互不相同：重复会让「引用某条目」变得有歧义，
        // 而 IsVerifiableAgainstLog 里的按 ID 建索引也会在运行期抛异常。
        // 与「引用重复」是同一类计数错位，在**构造期**一并堵掉。
        var seenIds = new HashSet<string>(StringComparer.Ordinal);
        var duplicated = new List<string>();
        foreach (var e in log)
        {
            if (!seenIds.Add(e.EntryId)) duplicated.Add(e.EntryId ?? "(空)");
        }

        if (duplicated.Count > 0)
        {
            throw new ArgumentException(
                $"战斗 {id} 的日志里有重复的条目 ID：{string.Join(", ", duplicated)}。"
                + "条目 ID 重复会让「判定引用了哪一条」失去唯一性，"
                + "基线 §8.5 D5 的复核也就不成立。",
                nameof(log));
        }

        return new BattleResult(id, encounterName ?? string.Empty, heroesWon,
            participants.ToArray(), log.ToArray());
    }

    /// <summary>
    /// 声明这次胜负判定<b>引用了哪些日志条目</b>（D5）。
    /// </summary>
    /// <remarks>
    /// <b>整体替换，不是叠加。</b>第二次调用会用 <paramref name="entryIds"/>
    /// 完整覆盖第一次的结果（空集合即「清空引用」）—— 判定依据永远是
    /// <b>最近一次</b>声明的那一份，不存在需要逐条撤销的残留。
    /// <para>
    /// 引用的条目必须<b>各不相同</b>：重复引用会让「引用的条目数」与
    /// 「实际影响判定的条目数」对不上，复核时计数错位。
    /// </para>
    /// </remarks>
    /// <param name="entryIds">被引用的条目标识。<c>null</c> 视为空集合。</param>
    /// <exception cref="ArgumentException">
    /// 引用了<b>不存在于日志中</b>的条目（引用了一件没记录的事，玩家无法复核），
    /// 或<b>同一个条目被引用了多次</b>（复核计数会错位）。D5。
    /// </exception>
    public void CiteDecidingEntries(IEnumerable<string> entryIds)
    {
        var cited = (entryIds ?? Enumerable.Empty<string>()).ToList();
        var known = new HashSet<string>(Log.Select(e => e.EntryId), StringComparer.Ordinal);

        var missing = cited.Where(c => !known.Contains(c)).ToList();
        if (missing.Count > 0)
        {
            throw new ArgumentException(
                $"判定引用了日志中不存在的条目：{string.Join(", ", missing)}。"
                + "基线 §8.5 D5：战斗日志必须能复现胜负判定所引用的每一个关键事件 ——"
                + "引用一件没记录的事，玩家就无法理解结果。",
                nameof(entryIds));
        }

        // 缺失检查之后再查重复：先确认「引用的都是真条目」，再去问「有没有把同一条数两遍」。
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var repeated = new List<string>();
        foreach (var citedId in cited)
        {
            if (!seen.Add(citedId))
            {
                repeated.Add(citedId ?? "(空)");
            }
        }

        if (repeated.Count > 0)
        {
            throw new ArgumentException(
                $"同一条日志条目被引用了多次：{string.Join(", ", repeated)}。"
                + "基线 §8.5 D5：战斗日志必须能复现胜负判定所引用的每一个关键事件 ——"
                + "同一条目只算一次，重复引用会让「引用的条目数」与「实际影响判定的条目数」对不上，"
                + "复核时计数错位。",
                nameof(entryIds));
        }

        // 只有全部检查通过才写入。失败时 DecidingEntryIds 保持调用前的值，
        // 不会出现「半提交」的引用集合。
        DecidingEntryIds = cited;
    }

    /// <summary>
    /// 复核：判定引用的每一条都在日志里，且至少有一条真正改变了判定。
    /// </summary>
    /// <returns>可复核时为 <c>true</c>。</returns>
    public bool IsVerifiableAgainstLog()
    {
        if (DecidingEntryIds.Count == 0) return false;

        var byId = Log.ToDictionary(e => e.EntryId, StringComparer.Ordinal);
        foreach (var cited in DecidingEntryIds)
        {
            if (!byId.TryGetValue(cited, out var entry)) return false;
            if (!entry.DecidedOutcome) return false;
        }

        return true;
    }
}

/// <summary>
/// 战斗规则求解器 —— 战斗公式的<b>注入点</b>。
/// </summary>
/// <remarks>
/// <para>
/// <b>【待定】</b>伤害、命中、冷却、状态效果、AI 目标选择<b>全部未定</b>
/// （<c>07</c> 的 <b>P-07 / T-004</c>，基线 §16 #13）。
/// 因此本模块<b>不实现任何公式</b>，只定义接口形状 ——
/// 规则确定之后，接一个实现即可，<b>决策流程与调度顺序不需要重写</b>。
/// </para>
/// <para>
/// 旧原型里的伤害/命中计算已随设计基线更替失效；这里<b>不复刻</b>它。
/// </para>
/// </remarks>
public interface IBattleResolver
{
    /// <summary>
    /// 解算一次遭遇。
    /// </summary>
    /// <param name="encounterName">遭遇名称。</param>
    /// <param name="party">
    /// 参战队伍的<b>状态快照</b>（谁在场、当时什么情况，<b>含战斗属性</b>）。
    /// <para>⚠️ <b>为什么必须有它</b>：原本这里只给一串标识，
    /// 战斗<b>结构上就看不到</b>「队伍是谁配置的」——
    /// 而基线 §14.3 的对照实验明确要求固定输入包含
    /// 「<b>相同的英雄队伍（成员、装备、状态）</b>」。
    /// ⇒ 缺了它，「<b>队伍配置按规则影响副本结果</b>」（IT-31）这条验收**写不出来</b>：
    /// 不是公式没定，而是<b>战斗压根拿不到输入</b>。</para>
    /// <para>规则本身属【已确认】D-059…D-065；本参数只负责<b>把输入递到手</b>。</para>
    /// </param>
    /// <param name="stageIndex">
    /// 当前阶段序号（0 起）—— 决定<b>敌方是哪一档怪物</b>
    /// （D-065：普通 / 精英 / 首领 一一对应副本的三个阶段）。
    /// <para>⚠️ 为什么由<b>调用方</b>给：副本的阶段流转是副本模块的事（<c>04</c> §3.1 L4），
    /// 战斗规则不该自己去数阶段。</para>
    /// </param>
    /// <returns>战斗日志。</returns>
    /// <remarks>
    /// <para>⚠️ <b>契约（D-073）</b>：那条<b>决定性条目</b>必须带<b>回合标记</b>
    /// （<c>【回合N】</c>，由 <see cref="BattleOutcomeMarkers.RoundsMarker"/> 生成），
    /// 指明这一场打了几个回合。
    /// <b>为什么这是硬契约</b>：阶段耗时（回合 ↔ 游戏内小时）由此算出，
    /// 而关系变化要靠它拿到「这段经历发生在什么时候」的底座（基线 §9.5 R4）。
    /// ⇒ 少写标记 ⇒ <see cref="BattleLogReader.RoundsUsed"/> <b>大声失败</b>
    /// （而不是把耗时算成 0）。</para>
    /// </remarks>
    IReadOnlyList<BattleLogEntry> Resolve(
        string encounterName, CombatPartySnapshot party, int stageIndex);
}
