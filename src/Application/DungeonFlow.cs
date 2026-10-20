using System;
using System.Collections.Generic;
using System.Linq;
using ClaudeCodeGameStudios.Domain.Combat;
using ClaudeCodeGameStudios.Domain.Dungeon;

namespace ClaudeCodeGameStudios.Application;

/// <summary>
/// 建筑对一次副本的修饰 —— 一条<b>描述</b>，不是一个数值。
/// </summary>
/// <remarks>
/// <para>
/// 建筑功能整体属【待定】（<c>07</c> 的 <b>T-004</b> / 建筑功能待定）。
/// 因此这里<b>只声明形状</b>：谁影响了副本、以什么名义影响。
/// 它<b>不携带任何倍率或修正值</b> —— 那些数字还没有出处，
/// 此刻写下来就是凭空发明数值（<c>07</c> §2.1 第 1 条纪律）。
/// </para>
/// <para>本类型的实际用途：把修饰<b>记进流程日志</b>，供未来的战斗模块消费。</para>
/// </remarks>
/// <param name="ModifierId">修饰来源的标识（如某项建筑功能）。</param>
/// <param name="SourceId">来源实体标识（如建筑标识）。</param>
public readonly record struct DungeonModifier(string ModifierId, string SourceId);

/// <summary>
/// 建筑影响副本的<b>注入点</b> —— 默认「无影响」。
/// </summary>
/// <remarks>
/// <para>
/// <b>为什么接口留着而默认什么都不做</b>：建筑功能属【待定】。
/// 留接口是为了让「建筑 → 副本」的依赖<b>有一个确定的位置</b>，
/// 而不是等到 T-004 拍板时再去改流程的形状；
/// 默认无影响是为了在拍板之前，副本行为<b>完全不受建筑影响</b>。
/// </para>
/// <para>
/// <b>本阶段它还不能改变战斗</b>：<see cref="IBattleResolver.Resolve"/>
/// 的签名只有遭遇名与参与者，没有接收修饰的位置；
/// 而战斗公式本身尚未定（T-004），现在硬塞进去就是发明规则。
/// 因此流程只负责<b>采集并记录</b>修饰（见 <see cref="DungeonStageRecord.Modifiers"/>）。
/// </para>
/// </remarks>
public interface IDungeonModifier
{
    /// <summary>评估这次副本受到哪些建筑修饰。</summary>
    /// <param name="dungeonName">副本名称。</param>
    /// <param name="participantIds">参与英雄标识（快照）。</param>
    /// <returns>修饰列表；无影响时返回空数组，<b>不得返回 null</b>。</returns>
    DungeonModifier[] Evaluate(string dungeonName, IReadOnlyList<string> participantIds);
}

/// <summary>什么都不做的建筑修饰 —— 副本的<b>默认</b>状态。</summary>
/// <remarks>
/// 用它而不是「无操作委托」是为了让「没有建筑影响」也是一个可命名、可替换的对象，
/// 便于 T-004 拍板后整体替换实现，而不改流程一行。
/// </remarks>
public sealed class NoDungeonModifier : IDungeonModifier
{
    /// <inheritdoc />
    /// <returns>恒为空数组。</returns>
    public DungeonModifier[] Evaluate(string dungeonName, IReadOnlyList<string> participantIds)
        => Array.Empty<DungeonModifier>();
}

/// <summary>
/// 从战斗日志读出<b>胜负结论</b>的注入点。
/// </summary>
/// <remarks>
/// <para>
/// <b>为什么需要它，而不是直接用 <see cref="IBattleResolver"/>。</b>
/// <see cref="IBattleResolver.Resolve"/> 只返回日志，<b>签名里没有胜负</b>。
/// 日志条目上的 <c>DecidedOutcome</c> 只说明「这一条<b>改变了</b>判定」，
/// <b>不说明判成了哪一边</b>。因此流程若要自己得出「谁赢了」，
/// 就必须发明一条规则 —— 而胜负规则属【待定】<b>T-004</b>。
/// 本类因此<b>不做这个判断，只向注入的实现询问</b>。
/// </para>
/// <para>
/// <b>这是一个需要 game-designer / technical-director 拍板的接口形状问题</b>：
/// 保持「两个注入点」（本接口 + <see cref="IBattleResolver"/>），
/// 还是把 <see cref="IBattleResolver"/> 的返回值改成同时带胜负。
/// 在拍板之前，这里选前者，因为它<b>不改任何已存在的领域类型</b>。
/// </para>
/// </remarks>
public interface IBattleOutcomeReader
{
    /// <summary>读出这场战斗的结论。</summary>
    /// <param name="log">战斗日志；本次刚由 resolver 产出。</param>
    /// <returns>英雄方获胜为 <c>true</c>。</returns>
    bool DidHeroesWin(IReadOnlyList<BattleLogEntry> log);
}

/// <summary>副本的一个阶段跑完之后留下的记录。</summary>
/// <param name="Stage">刚打完的阶段。</param>
/// <param name="EncounterName">本次遭遇的名称。</param>
/// <param name="Result">本次战斗结果（每一阶段都会产出）。</param>
/// <param name="Modifiers">本阶段采集到的建筑修饰；默认全空。</param>
/// <param name="AttachedToRun">
/// 本场战斗结果<b>是否被关联进了副本实例</b>（不变量 I3）。
/// 只有<b>决定整个副本成败的那一场</b>为 <c>true</c>。
/// </param>
public sealed record DungeonStageRecord(
    DungeonStage Stage,
    string EncounterName,
    BattleResult Result,
    IReadOnlyList<DungeonModifier> Modifiers,
    bool AttachedToRun);

/// <summary>一次副本跑完之后的总结。</summary>
/// <param name="Run">副本实例。</param>
/// <param name="Records">全部阶段记录，按发生顺序。</param>
/// <param name="DecidingBattle">
/// 决定成败的那一场的战斗结果；即被关联进实例的那一个。<b>不会为 null</b>
/// —— 流程只要跑完就必然产生了一个决定性结果。
/// </param>
/// <param name="Succeeded">副本是否成功。</param>
public sealed record DungeonFlowOutcome(
    DungeonRun Run,
    IReadOnlyList<DungeonStageRecord> Records,
    BattleResult DecidingBattle,
    bool Succeeded);

/// <summary>
/// 副本的<b>流程驱动</b> —— 第一阶段（M5）。
/// </summary>
/// <remarks>
/// <para>
/// <b>它做什么</b>：创建副本实例 → 进入 → 逐阶段调
/// <see cref="IBattleResolver"/> 解算 → 产出 <see cref="BattleResult"/> →
/// 推进阶段 → 判定最终目标 → 结算。
/// </para>
///
/// <para>
/// <b>它不做什么（三条硬边界）</b>
/// </para>
/// <list type="number">
/// <item><b>不实现任何战斗规则。</b>本类没有伤害、没有命中、没有冷却、
/// 没有胜率。战斗怎么打由注入的 <see cref="IBattleResolver"/> 与
/// <see cref="IBattleOutcomeReader"/> 决定（【待定】T-004）。
/// 本类<b>一个数都不含</b>。</item>
///
/// <item><b>不碰英雄的日常。</b>本类持有的是参与者的<b>标识快照</b>，
/// 不持有 <c>HeroState</c>、不持有 <c>SimClock</c>、不引用
/// <see cref="DailyLife"/>。副本期间其他英雄照常生活、村庄时间照常推进，
/// 这件事<b>由本类完全无从影响</b>来保证 —— 它没有那些引用。</item>
///
/// <item><b>不决定奖励。</b>结算内容属【待定】T-004。
/// 本类只把实例推到「可结算」状态。</item>
/// </list>
///
/// <para>
/// <b>每个副本至多一个 <see cref="BattleResult"/>（不变量 I3）。</b>
/// 每场战斗都会产出一个结果，但只有<b>决定整个副本成败的那一场</b>
/// 会被 <see cref="DungeonRun.AttachBattleResult"/> 关联进实例。
/// <b>为什么是这一场</b>：流程遇到败仗就终止，所以「最后打的那一场」
/// 与「决定生死的那一场」<b>完全是同一场</b>；而若关联<b>第一场</b>，
/// 一个倒在最终目标的副本会挂上一个「英雄获胜」的战斗结果 ——
/// 那是一条与事实相反的记录。中间那些打赢了但没打完副本的战斗
/// 由本类持有并记在 <see cref="Records"/> 里，不进实例。
/// </para>
///
/// <para>
/// <b>每一场战斗都必须交得出判定依据</b>（基线 §8.5 验收项 <b>D5</b>）。
/// 每次 <see cref="BattleResult.Produce"/> 之后本类都会
/// <see cref="BattleResult.CiteDecidingEntries"/> 引用全部真正改变了判定的条目，
/// 并复核 <see cref="BattleResult.IsVerifiableAgainstLog"/>；
/// 交不出依据的战斗<b>会让流程当场中止</b>，而不是记一笔糊涂账。
/// </para>
///
/// <para>
/// <b>依赖注入，无静态单例</b>：resolver、胜负读者、建筑修饰全部由构造参数给出，
/// 没有 <c>Instance</c>、没有全局状态、没有随机源
/// （战斗结果标识由副本标识与阶段<b>推导</b>，读档重放可复现）。
/// </para>
/// </remarks>
public sealed class DungeonFlow
{
    /// <summary>第一版声明的阶段数（<c>07</c> <b>D-010 Q2</b>：2 场遭遇 + 1 个最终目标）。</summary>
    /// <remarks>
    /// <b>为什么是 3 而不是下限 2</b>：两阶段会让「最终目标」与「阶段」
    /// 塌缩成同一个东西，验证不到基线 §8.2 要求的两者区分。
    /// </remarks>
    public const int DeclaredStageCount = 3;

    /// <summary>战斗结果标识的分隔符。推导式标识的一部分。</summary>
    private const string BattleIdSeparator = "#";

    /// <summary>被驱动的副本实例。流程是它的唯一写者之一（阶段流转）。</summary>
    private readonly DungeonRun _run;

    /// <summary>战斗解算器（注入点；战斗规则不由本类实现）。</summary>
    private readonly IBattleResolver _resolver;

    /// <summary>胜负结论读者（注入点；见该接口的说明）。</summary>
    private readonly IBattleOutcomeReader _outcomeReader;

    /// <summary>建筑修饰来源（注入点；默认无影响）。</summary>
    private readonly IDungeonModifier _modifiers;

    /// <summary>阶段 → 遭遇名的映射（注入点；默认见 <see cref="DefaultEncounterName"/>）。</summary>
    private readonly Func<DungeonStage, string> _encounterNaming;

    /// <summary>
    /// 参战队伍的<b>状态快照</b> —— 战斗解算能看到的「谁在场、什么情况」。
    /// </summary>
    /// <remarks>
    /// <para>⚠️ <b>它为什么是独立的一份，而不是 <c>DungeonRun.Participants</c> 本身</b>：
    /// <c>05</c> §3.5 明写 <c>DungeonRun</c> 持有「<b>参与英雄 ID 快照</b>」——
    /// 那是<b>规格</b>，不是实现偷懒。副本实例回答的是「<b>谁去了</b>」
    /// （结算要对应「谁去了」，不是「读档瞬间队伍里还有谁」），
    /// 而战斗要回答的是「<b>他们当时什么情况</b>」。<b>两个问题，不塞进一个实体。</b></para>
    /// </remarks>
    private readonly CombatPartySnapshot _party;

    /// <summary>已完成的阶段记录，按发生顺序。</summary>
    private readonly List<DungeonStageRecord> _records = new();

    /// <summary>
    /// 构造一个流程。<b>不负责进入副本</b> —— 由调用方决定何时 <c>Enter</c>。
    /// </summary>
    /// <param name="run">副本实例；阶段流转由本流程推进。</param>
    /// <param name="resolver">战斗解算器。</param>
    /// <param name="outcomeReader">胜负结论读者。</param>
    /// <param name="modifiers">建筑修饰来源；<c>null</c> 表示<b>无影响</b>（默认）。</param>
    /// <param name="encounterNaming">阶段 → 遭遇名；<c>null</c> 时用 <see cref="DefaultEncounterName"/>。</param>
    /// <param name="party">
    /// 参战队伍的<b>状态快照</b>；<c>null</c> 时按参与者标识造一份<b>空状态</b>的快照
    /// （只有标识，没有特质/偏好）。
    /// <para>⚠️ <b>空状态快照不是「没有队伍」</b>，而是「队伍状态不明」。
    /// 它让既有调用点不必改，但也意味着<b>战斗看不到队伍配置</b> ——
    /// 真要用「队伍配置影响结果」这条规则，调用方必须传真实快照。</para>
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="run"/>、<paramref name="resolver"/> 或 <paramref name="outcomeReader"/> 为 null。</exception>
    public DungeonFlow(
        DungeonRun run,
        IBattleResolver resolver,
        IBattleOutcomeReader outcomeReader,
        IDungeonModifier? modifiers = null,
        Func<DungeonStage, string>? encounterNaming = null,
        CombatPartySnapshot? party = null)
    {
        _run = run ?? throw new ArgumentNullException(nameof(run));
        _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
        _outcomeReader = outcomeReader ?? throw new ArgumentNullException(nameof(outcomeReader));
        _modifiers = modifiers ?? new NoDungeonModifier();
        _encounterNaming = encounterNaming ?? DefaultEncounterName;
        _party = party ?? CombatPartySnapshot.FromIds(_run.Participants);
    }

    /// <summary>创建并<b>进入</b>一次副本，返回它的流程驱动（常规入口）。</summary>
    /// <param name="id">副本实例标识。</param>
    /// <param name="name">副本名称。</param>
    /// <param name="participants">参与者标识列表（会被 <see cref="DungeonRun.Create"/> 快照）。</param>
    /// <param name="resolver">战斗解算器。</param>
    /// <param name="outcomeReader">胜负结论读者。</param>
    /// <param name="modifiers">建筑修饰来源；<c>null</c> 表示无影响（默认）。</param>
    /// <param name="encounterNaming">阶段 → 遭遇名；<c>null</c> 时用 <see cref="DefaultEncounterName"/>。</param>
    /// <param name="party">
    /// 参战队伍的<b>状态快照</b>；<c>null</c> 时按参与者标识造一份空状态快照。
    /// <para>⚠️ 传了它，战斗就<b>能看到队伍配置</b>（成员、特质、偏好）——
    /// 这是「队伍配置按规则影响副本结果」（IT-31）的<b>前置</b>；
    /// 规则本身仍属【待定】T-004。</para>
    /// </param>
    /// <returns>已处于 <see cref="DungeonStage.EncounterOne"/> 的流程。</returns>
    /// <exception cref="ArgumentException">标识为空或参与者为空（由 <see cref="DungeonRun.Create"/> 抛出）。</exception>
    /// <exception cref="ArgumentNullException"><paramref name="resolver"/> 或 <paramref name="outcomeReader"/> 为 null。</exception>
    public static DungeonFlow Begin(
        DungeonRunId id,
        string name,
        IReadOnlyList<string> participants,
        IBattleResolver resolver,
        IBattleOutcomeReader outcomeReader,
        IDungeonModifier? modifiers = null,
        Func<DungeonStage, string>? encounterNaming = null,
        CombatPartySnapshot? party = null)
    {
        var flow = new DungeonFlow(
            DungeonRun.Create(id, name, participants), resolver, outcomeReader,
            modifiers, encounterNaming, party);

        flow._run.Enter();
        return flow;
    }

    /// <summary>被驱动的副本实例。</summary>
    public DungeonRun Run => _run;

    /// <summary>已完成的阶段记录，按发生顺序。只读快照。</summary>
    public IReadOnlyList<DungeonStageRecord> Records => _records.AsReadOnly();

    /// <summary>流程是否已终止（成功或失败）。终止后不能再 <see cref="Advance"/>。</summary>
    public bool IsFinished => _run.Status != DungeonRunStatus.InProgress;

    /// <summary>
    /// 推进<b>一个</b>阶段：解算一场战斗 → 产出结果 → 复核判定依据 →
    /// 关联决定性结果 → 推进阶段或判定最终目标。
    /// </summary>
    /// <returns>本阶段的记录。</returns>
    /// <exception cref="InvalidOperationException">
    /// 副本尚未进入、已经结束（失败或成功后不再推进），或战斗解算器交回了 null 日志。
    /// </exception>
    /// <exception cref="ArgumentException">
    /// 本场战斗<b>交不出判定依据</b> —— 由 <see cref="BattleResult.Produce"/> 在
    /// <b>构造期</b>抛出（基线 §8.5 D5）。抛出时副本实例尚未被写入任何东西：
    /// 没有关联战斗结果，没有推进阶段，也没有留下阶段记录。
    /// </exception>
    /// <remarks>
    /// <b>失败即终止</b>：任一场失败，实例立刻进入 <see cref="DungeonRunStatus.Failed"/>
    /// 且<b>阶段不再前进</b>，后续 <see cref="Advance"/> 会被拒绝。
    /// 失败不给奖励也不额外惩罚（D-010 Q1）——本类<b>没有</b>执行奖惩的能力。
    /// </remarks>
    public DungeonStageRecord Advance()
    {
        if (_run.Stage == DungeonStage.NotEntered)
        {
            throw new InvalidOperationException(
                $"副本 {_run.Id} 尚未进入，不能推进阶段。");
        }

        if (IsFinished)
        {
            throw new InvalidOperationException(
                $"副本 {_run.Id} 已处于 {_run.Status}，流程已终止，不能再推进阶段。");
        }

        var stage = _run.Stage;
        var encounterName = _encounterNaming(stage);
        var modifiers = _modifiers.Evaluate(_run.Name, _run.Participants)
                        ?? Array.Empty<DungeonModifier>();

        // 1. 解算：胜负由注入的 resolver 产出日志、由注入的 reader 读出结论。
        //    本类只转发，不判断。
        var log = _resolver.Resolve(encounterName, _party)
                  ?? throw new InvalidOperationException(
                      $"战斗解算器 {nameof(IBattleResolver.Resolve)} 对遭遇「{encounterName}」返回了 null。"
                      + "没有日志的战斗无法被复核（基线 §8.5 D5）。");

        var heroesWon = _outcomeReader.DidHeroesWin(log);

        // 2. 产出战斗结果（标识由副本标识 + 阶段推导，读档重放可复现）。
        //    「日志里没有关键事件」在这一行就被拒（ArgumentException，D5）——
        //    比任何事后检查都早：结果还没被构造出来，也就无从被谁拿到。
        var result = BattleResult.Produce(
            BattleResultIdFor(stage), encounterName, heroesWon, _run.Participants, log);

        // 3. 交出判定依据（D5）。这一步在**关联之前**：
        //    一场交不出依据的战斗绝不能先写进副本实例。
        CiteDecidingEntries(result);

        // 4. 关联决定性结果（I3，至多一次）。
        //    流程遇到败仗就终止 ⇒「最后一场」就是「决定生死的那一场」。
        //    赢了但不是最终目标的那场，说明副本还有下一场 —— 不是决定性结果。
        bool attachedToRun = !heroesWon || stage == DungeonStage.FinalGoal;
        if (attachedToRun)
        {
            // 必须在推进状态之前关联：实例一旦离开 InProgress，
            // AttachBattleResult 就会拒绝（I3 的前置条件）。
            _run.AttachBattleResult(result.Id.Value);
        }

        // 5. 推进阶段，或在最终目标上判定成败。
        if (stage == DungeonStage.FinalGoal)
        {
            _run.CompleteFinalGoal(finalGoalAchieved: heroesWon);
        }
        else
        {
            _run.AdvanceStage(succeeded: heroesWon);
        }

        var record = new DungeonStageRecord(
            stage, encounterName, result, modifiers.ToArray(), attachedToRun);

        _records.Add(record);
        return record;
    }

    /// <summary>一直推进到副本结束，返回总结。</summary>
    /// <returns>含全部阶段记录与决定性战斗结果的总结。</returns>
    /// <exception cref="InvalidOperationException">
    /// 单个阶段推进失败（见 <see cref="Advance"/>），或推进次数超过
    /// <see cref="DeclaredStageCount"/> —— 后者是<b>不可能状态</b>的护栏：
    /// 一场永远打不赢的流程不能变成死循环。
    /// 本流程一场战斗都没打过时（实例在交给本流程之前就已结束）同样抛此异常。
    /// </exception>
    public DungeonFlowOutcome RunToCompletion()
    {
        int steps = 0;
        while (!IsFinished)
        {
            Advance();
            if (++steps > DeclaredStageCount)
            {
                throw new InvalidOperationException(
                    $"副本 {_run.Id} 已推进 {steps} 步仍未结束，超过声明的 {DeclaredStageCount} 个阶段。"
                    + "这是不可能状态：流程要么在失败处终止，要么在最终目标处结束。"
                    + "出现它说明阶段流转被改坏了。");
            }
        }

        if (_records.Count == 0)
        {
            // 构造时就已结束的实例（没经过本流程的战斗）没有可总结的东西。
            throw new InvalidOperationException(
                $"副本 {_run.Id} 在本流程中一场战斗都没有打过，无法总结。");
        }

        // 决定性结果 == 最后一场（流程遇到败仗就终止）。
        // 这里按 AttachedToRun 取，而不是取最后一条 —— 若两条规则哪天不一致，
        // 应该报出不一致，而不是悄悄汇报另一场战斗。
        var deciding = _records.Single(record => record.AttachedToRun).Result;
        return new DungeonFlowOutcome(
            _run, _records.AsReadOnly(), deciding, _run.Status == DungeonRunStatus.Succeeded);
    }

    /// <summary>结算副本（幂等由领域保证：不变量 I4）。</summary>
    /// <remarks>
    /// <b>本类不做重复结算的兜底吞异常</b>：第二次调用会抛
    /// <see cref="InvalidOperationException"/>。把错误静音等于让调用方
    /// 以为自己结算成功了。
    /// </remarks>
    /// <exception cref="InvalidOperationException">副本仍在进行中，或已经结算过（I4）。</exception>
    public void Settle()
    {
        if (_run.Status == DungeonRunStatus.InProgress)
        {
            throw new InvalidOperationException(
                $"副本 {_run.Id} 仍在进行中，不能结算。");
        }

        if (_run.Status == DungeonRunStatus.Settled)
        {
            throw new InvalidOperationException(
                $"副本 {_run.Id} 已结算，不能重复结算（不变量 I4 / 基线 §13.7）。");
        }

        _run.MarkSettled();
    }

    /// <summary>
    /// 由副本标识与阶段<b>推导</b>战斗结果标识。
    /// </summary>
    /// <remarks>
    /// <b>为什么推导而不是生成</b>：不引入随机源、不引入全局计数器，
    /// 同一副本的同一阶段永远得到同一个标识 —— 读档后重放得到同样的结果。
    /// </remarks>
    private BattleResultId BattleResultIdFor(DungeonStage stage)
        => new($"{_run.Id.Value}{BattleIdSeparator}{stage}");

    /// <summary>引用全部真正改变了判定的条目（基线 §8.5 D5）。</summary>
    /// <remarks>
    /// <para>
    /// <b>这里刻意没有「复核不通过就抛异常」这一分支。</b>
    /// 它在 <see cref="BattleResult.Produce"/> 加上构造期防线之前是有意义的；
    /// 现在<b>已经不可能触发</b>，四个返回 <c>false</c> 的理由逐条都被上游堵死：
    /// </para>
    /// <list type="number">
    /// <item><c>DecidingEntryIds</c> 为空 —— <c>Produce</c> 已保证日志里至少有一条
    ///       <c>DecidedOutcome</c>，故此处选出的条目非空；</item>
    /// <item>引用了日志里没有的条目 —— 引用的 ID 全部取自 <c>result.Log</c> 本身；</item>
    /// <item>同一条被引用多次 —— <c>Produce</c> 已保证日志条目 ID 互不相同；</item>
    /// <item>引用了「没有改变判定」的条目 —— 这里按 <c>DecidedOutcome</c> 过滤过。</item>
    /// </list>
    /// <para>
    /// <b>烧不着的守卫比没有守卫更糟</b>：它会让读代码的人相信这里存在一道检查。
    /// 拒绝「交不出判定依据的战斗」这件事<b>仍然成立</b>，只是它的唯一落点已经前移到
    /// <see cref="BattleResult.Produce"/> —— 在结果被构造出来之前就拒绝，
    /// 比构造之后再查一遍<b>更早、也更强</b>。
    /// </para>
    /// <para>
    /// <b>如果将来放宽 <c>Produce</c></b>（例如允许某种不带关键事件的战斗），
    /// 这个分支就会重新变得必要 —— 那时应当在这里把它加回来，而不是提前养着一段
    /// 永远不会执行的代码。
    /// </para>
    /// </remarks>
    private static void CiteDecidingEntries(BattleResult result)
    {
        var decidingEntryIds = result.Log
            .Where(entry => entry.DecidedOutcome)
            .Select(entry => entry.EntryId)
            .ToArray();

        result.CiteDecidingEntries(decidingEntryIds);
    }

    /// <summary>默认的阶段 → 遭遇名映射。</summary>
    /// <param name="stage">阶段。</param>
    /// <returns>遭遇名。</returns>
    /// <exception cref="ArgumentOutOfRangeException">阶段为 <see cref="DungeonStage.NotEntered"/> 或未知值。</exception>
    /// <remarks>
    /// <b>这些是名字，不是数值。</b>它们只用来在战斗结果与日志里标识「打的是哪一场」，
    /// 不参与任何规则计算；副本内容（敌人、场景、掉落）尚未定义。
    /// 需要别的命名时经 <c>encounterNaming</c> 注入，不必改本方法。
    /// </remarks>
    public static string DefaultEncounterName(DungeonStage stage) => stage switch
    {
        DungeonStage.EncounterOne => "遭遇一",
        DungeonStage.EncounterTwo => "遭遇二",
        DungeonStage.FinalGoal => "最终目标",
        _ => throw new ArgumentOutOfRangeException(
            nameof(stage), stage, "尚未进入的副本没有遭遇，未知阶段也没有遭遇名。"),
    };
}
