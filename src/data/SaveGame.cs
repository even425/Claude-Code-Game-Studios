using System;
using System.Collections.Generic;
using System.Linq;
using ClaudeCodeGameStudios.Domain.Relationships;

namespace ClaudeCodeGameStudios.Data;

/// <summary>存档格式版本。基线 §13.7 要求存档带版本号并有迁移入口。</summary>
public readonly record struct SaveVersion(int Major, int Minor)
{
    /// <summary>当前版本。<b>【已确认】</b>(0,6) —— R-05 第四轮 / D-054 阶段 4 升。</summary>
    /// <remarks>
    /// <b>0,5 → 0,6</b>（`07` **D-054** 阶段 4 拍板）：存档里增
    /// <list type="bullet">
    /// <item><see cref="HeroSave.GoldCarry"/>（每英雄的<b>待发零头</b>，单位 1/100 金）</item>
    /// <item><see cref="SaveGame.GoldGrantedTotal"/>（累计发放给英雄的金币）</item>
    /// <item><see cref="SaveGame.GoldSpentTotal"/>（累计销毁的金币 —— 升级花掉的）</item>
    /// <item><see cref="SaveGame.VillageTier"/>（村庄层级 —— <b>顺带修既有缺陷</b>）</item>
    /// </list>
    /// <para>
    /// 旧档<b>整份拒绝</b>（I11 + D-021/D-029/D-036 先例），<b>不</b>写迁移器。
    /// </para>
    /// <para>
    /// <b>为什么必须存</b>：待发零头是<b>已经挣到、只是还没发出去</b>的钱，
    /// 读档后丢掉它等于让那部分钱凭空消失 —— 与 D-052 存税额余数是**同一条理由**。
    /// 累计发放/累计销毁则是守恒律的另外两项，不存就<b>对不上账</b>。
    /// </para>
    /// <para>
    /// <b>村庄层级本来就不入档</b>（既有缺陷）：读档后村庄回到 0 级，
    /// 而升级花掉的金币已经记下了 → <b>账实不符</b>。这次一并补上。
    /// </para>
    /// </remarks>
    public static SaveVersion Current => new(0, 6);

    /// <inheritdoc />
    public override string ToString() => $"{Major}.{Minor}";
}

/// <summary>一条存档错误。</summary>
/// <param name="Path">出错的位置，如 <c>village.buildings[2].id</c>。</param>
/// <param name="Message">错在哪。</param>
public readonly record struct SaveError(string Path, string Message);

/// <summary>
/// 存档校验结果。
/// </summary>
/// <remarks>
/// <para>
/// 基线 §13.7 明列的加载红线：<b>「存档加载先校验 ID 与引用，再恢复状态」</b>，
/// 且 <c>05</c> 不变量 <b>I11</b>：<b>引用损坏时加载必须失败并报出可理解的错误，
/// 而不是部分恢复</b>。
/// </para>
/// <para>
/// <b>为什么必须「全有或全无」</b>：部分恢复会留下一个「一半是新、一半是旧」的世界，
/// 而这种状态<b>没有对应的合法玩法规则</b>，出错时也无法复现。
/// </para>
/// </remarks>
public sealed class SaveValidationResult
{
    private SaveValidationResult(bool ok, IReadOnlyList<SaveError> errors)
    {
        IsValid = ok;
        Errors = errors;
    }

    /// <summary>是否全部通过。</summary>
    public bool IsValid { get; }

    /// <summary>全部错误（可理解、可定位）。</summary>
    public IReadOnlyList<SaveError> Errors { get; }

    /// <summary>通过时的结果。</summary>
    public static SaveValidationResult Ok() => new(true, Array.Empty<SaveError>());

    /// <summary>失败时的结果。</summary>
    /// <param name="errors">错误清单。</param>
    public static SaveValidationResult Fail(IReadOnlyList<SaveError> errors)
        => new(false, errors);

    /// <summary>拼成一句人话，便于直接显示给玩家。</summary>
    /// <returns>错误摘要。</returns>
    public string Describe()
    {
        if (IsValid) return "存档校验通过。";
        var head = string.Join("；", Errors.Take(5).Select(e => $"{e.Path}：{e.Message}"));
        var more = Errors.Count > 5 ? $"（另有 {Errors.Count - 5} 项）" : string.Empty;
        return $"存档无法加载，共 {Errors.Count} 项问题。{head}{more}";
    }
}

/// <summary>存档里的一个英雄（扁平化后的状态）。</summary>
/// <remarks>
/// <para><b>为什么字段这么多</b>：游戏里一个英雄有<b>两套</b>描述 ——
/// 领域侧是 <c>HeroDefinition</c>（Id / 名字 / 来处 / 性格修正 / 偏好权重，
/// <c>DecisionFlow</c> 靠它做决定），表现侧是 <c>HeroAgent</c>
/// （性格 / 投入度 / 力 / 速 / 韧，<c>HeroRenderer</c> 靠它画）。
/// 两边都要读档才能还原一个<b>同一个</b>的人。</para>
/// <para><b>只存一半会怎样</b>：读档出来的英雄要么画不出来（表现侧缺字段），
/// 要么做不出与存档前一样的决定（领域侧缺字段）—— 后者更难察觉，
/// 因为游戏不会报错，只是「这个人性格好像变了」。</para>
/// </remarks>
public sealed class HeroSave
{
    /// <summary>英雄标识。</summary>
    public string Id { get; set; } = "";
    /// <summary>显示名。</summary>
    public string Name { get; set; } = "";
    /// <summary>在场性：<c>InVillage</c> / <c>OnMission</c>。</summary>
    public string Presence { get; set; } = "InVillage";
    /// <summary>当前活动标识；空串表示空闲。</summary>
    public string CurrentActivity { get; set; } = "";
    /// <summary>行动剩余时长（模拟秒）。</summary>
    public double RemainingActionSeconds { get; set; }
    /// <summary>
    /// 当前行动的目标英雄（第三阶段 M2；空串 = 非目标活动）。
    /// </summary>
    /// <remarks>
    /// 与 <see cref="CurrentActivity"/> 一起恢复：社交进行中存档，读档后仍要记得「和谁」。
    /// <b>0,3 增补字段</b>（R-03 修复）：旧 0,3 存档缺此字段时按空串处理，不破坏加载。
    /// </remarks>
    public string TargetId { get; set; } = "";
    /// <summary>是否已完成首个副本。</summary>
    public bool HasCompletedFirstDungeon { get; set; }

    // ── 表现侧（HeroAgent）──

    /// <summary>性格（莽 / 傲 / 贪）。<b>不能为空</b> —— 空性格画不出人。</summary>
    public string Personality { get; set; } = "";
    /// <summary>投入度（随手 / 肝 / 挂机）。</summary>
    public string Diligence { get; set; } = "";
    /// <summary>力。</summary>
    public int Power { get; set; }
    /// <summary>速。</summary>
    public int Speed { get; set; }
    /// <summary>韧。</summary>
    public int Grit { get; set; }

    // ── 领域侧（HeroDefinition）──

    /// <summary>来处（哪个世界）。</summary>
    public string Origin { get; set; } = "";
    /// <summary>性格修正系数。**每个值必须为正**（不变量 CR-1）。</summary>
    public Dictionary<string, double> TraitModifiers { get; set; } = new();
    /// <summary>偏好权重。**每个值必须为正**（不变量 CR-1）。</summary>
    public Dictionary<string, double> PreferenceWeights { get; set; } = new();

    // ── 经济（M7 / D-029 / 0,2 新增） ──

    /// <summary>
    /// 英雄钱包金币（"金币" key，D-021 单一货币）。
    /// </summary>
    /// <remarks>
    /// <b>0,2 新增</b>：旧档缺此字段 → 整份拒绝（I11 + D-021）。
    /// 这里用 <c>long</c> 而非 <c>Dictionary&lt;string, long&gt;</c>：D-021 拍定"单一货币「金币」"，
    /// v1 不开多资源（材料/食物/...），<b>不</b>为未来可能加的副资源预留序列化。
    /// </remarks>
    public long CoinAmount { get; set; }

    /// <summary>
    /// 英雄背包（物品 Id + 数量）。0,2 新增。
    /// </summary>
    /// <remarks>
    /// 用 <see cref="BagEntrySave"/> 而非 <c>Dictionary&lt;string, long&gt;</c>：
    /// 存档序列化要<b>字段稳定</b>（D-021），避免依赖库反序列化字典的不可控排序。
    /// 空列表 = 背包空。
    /// </remarks>
    public List<BagEntrySave> Bag { get; set; } = new();

    /// <summary>
    /// 该英雄上次<b>自主购物成交</b>的游戏日号（0 = 从未购过）。<b>0,4 新增</b>（D-044）。
    /// </summary>
    /// <remarks>
    /// 不存它，读档后 <c>HeroBehaviorService</c> 就不知道「这个人今天已经买过」，
    /// 同一名英雄会在同一天再成交一次 —— 钱和货都会被重复搬运（D-025 每英雄每天 ≤ 1 次）。
    /// </remarks>
    public int LastShoppingDay { get; set; }

    /// <summary>
    /// 该英雄的<b>待发零头</b>（单位 1/100 金）。<b>0,6 新增</b>（D-054 阶段 4）。
    /// </summary>
    /// <remarks>
    /// <para><b>它是什么</b>：副本金币报酬是「每人每次 X 金」，而 X 可以是<b>分数</b>
    /// （金币的最小单位是 1/100 金）。这个英雄攒到但<b>还没发出去</b>的零头就记在这儿。</para>
    /// <para><b>为什么不存就会丢钱</b>：它是<b>已经挣到</b>的钱。
    /// 读档后丢掉它 = 那部分钱凭空消失 —— 与 D-052 存税额余数同一条理由。</para>
    /// <para><b>合法范围 [0, 99]</b>：满 100 就该发出去一张整金了。</para>
    /// </remarks>
    public long GoldCarry { get; set; }
}

/// <summary>
/// 存档里的一个物品条目（0,2 新增）。
/// </summary>
/// <remarks>
/// <b>为什么用 record</b>：纯数据载体，无行为；<c>record</c> 让 JSON 序列化输出稳定字段名。
/// <b>为什么不用 <c>Dictionary&lt;string, long&gt;</c></b>：避免依赖第三方库的反序列化顺序。
/// </remarks>
public sealed record BagEntrySave
{
    /// <summary>物品 Id（<see cref="Domain.Items.ItemId.Value"/>）。</summary>
    public string ItemId { get; set; } = "";

    /// <summary>数量。<b>必须 ≥ 0</b>（0 也合法 —— 但 Bag 应在存档前清理 0 数量的项）。</summary>
    public long Count { get; set; }
}

/// <summary>
/// 存档里的一段有向关系（0,3 新增，第三阶段 M6 / P3-D10）。
/// </summary>
/// <remarks>
/// <b>有向</b>：<see cref="FromId"/> → <see cref="ToId"/>；反向关系是另一条记录（I8）。
/// 用扁平字段而不是字典，理由与 <see cref="BagEntrySave"/> 相同：字段名稳定，序列化可读。
/// </remarks>
public sealed class RelationshipSave
{
    /// <summary>关系的一方英雄 ID。必须能在 <see cref="SaveGame.Heroes"/> 中找到。</summary>
    public string FromId { get; set; } = "";

    /// <summary>关系的另一方英雄 ID。不得等于 <see cref="FromId"/>。</summary>
    public string ToId { get; set; } = "";

    /// <summary>关系值，范围与 <see cref="Relationship"/> 相同（[-100, +100]）。</summary>
    public int Value { get; set; }
}

/// <summary>
/// 存档里的一个摊位（<b>0,4 新增</b>，第四阶段 M4 / D-044）。
/// </summary>
/// <remarks>
/// <para><b>为什么必须存摊位</b>：摆摊时货已经从英雄背包<b>挪进</b> <c>Stall.Stock</c>
/// （P2-D06）。不存摊位，读档后这些货既不在背包、也不在摊位 —— 凭空蒸发，
/// 而玩家只看到「东西没了」。</para>
/// <para><b>一人一摊</b>（D-023 / P2-D04）：<see cref="OwnerId"/> 在整个列表中唯一，
/// 由 <see cref="SaveValidator"/> 守。</para>
/// <para>扁平字段而非字典，理由与 <see cref="BagEntrySave"/> 相同：字段名稳定、可读。</para>
/// </remarks>
public sealed class StallSave
{
    /// <summary>摊主英雄 ID。必须能在 <see cref="SaveGame.Heroes"/> 中找到，且唯一。</summary>
    public string OwnerId { get; set; } = "";

    /// <summary>摊位商品 Id（一摊一物）。</summary>
    public string ItemId { get; set; } = "";

    /// <summary>摊位库存。<b>必须 ≥ 0</b>（0 = 已售罄或已收摊）。</summary>
    public long Stock { get; set; }

    /// <summary>单价（开摊时锁定）。<b>必须 ≥ 0</b>（0 = 白送，合法）。</summary>
    public long UnitPrice { get; set; }

    /// <summary>状态字符串：<c>Open</c> / <c>SoldOut</c> / <c>Closed</c>。</summary>
    public string Status { get; set; } = "Open";

    /// <summary>开摊时间（模拟秒）。<b>必须是非负有限数。</b></summary>
    public double SimTime { get; set; }

    /// <summary>摊位所在格位 X（M5 / D-033，v1 为 1×1 占用）。</summary>
    public int GridX { get; set; }

    /// <summary>摊位所在格位 Y。</summary>
    public int GridY { get; set; }
}

/// <summary>存档里的一个任务。</summary>
public sealed class MissionSave
{
    /// <summary>任务标识。</summary>
    public string Id { get; set; } = "";
    /// <summary>状态字符串。</summary>
    public string Status { get; set; } = "Created";
    /// <summary>成员英雄标识。</summary>
    public List<string> MemberIds { get; set; } = new();
}

/// <summary>存档里的村庄。</summary>
public sealed class VillageSave
{
    /// <summary>村庄标识。</summary>
    public string Id { get; set; } = "";
    /// <summary>资源存量。</summary>
    public Dictionary<string, long> Resources { get; set; } = new();

    /// <summary>
    /// 村庄税额余数（单位：1/100 金）—— <b>0.5 新增</b>（D-052）。
    /// </summary>
    /// <remarks>
    /// <para>5% 摊位税里<b>还没凑够 1 金</b>的那部分。D-052 把「残差归卖家」
    /// 改成「残差累计归村庄金库」，于是这笔零头就成了**村庄的账**，
    /// 必须跟着存档走 —— 否则读档后它会凭空消失（等于少收了玩家的钱）。</para>
    /// <para><b>不塞进 <see cref="Resources"/></b>：那是「村庄持有的资源」，
    /// 塞一个记账用的零头进去会让资源池的语义变脏（D-052 明确禁止）。</para>
    /// <para><b>合法范围 [0, 99]</b>：单位是 1/100 金，满 100 就该入账了。</para>
    /// </remarks>
    public long VillageCutRemainder { get; set; }

    /// <summary>建筑列表。</summary>
    public List<BuildingSave> Buildings { get; set; } = new();
}

/// <summary>存档里的一个建筑。</summary>
public sealed class BuildingSave
{
    /// <summary>建筑类型标识。</summary>
    public string Id { get; set; } = "";
    /// <summary>左下角 X。</summary>
    public int X { get; set; }
    /// <summary>左下角 Y。</summary>
    public int Y { get; set; }
    /// <summary>格宽。</summary>
    public int W { get; set; }
    /// <summary>格高。</summary>
    public int H { get; set; }
    /// <summary>建造进度，<c>[0,1]</c>。</summary>
    public double Progress { get; set; }
}

/// <summary>整个存档。</summary>
public sealed class SaveGame
{
    /// <summary>格式版本。</summary>
    public SaveVersion Version { get; set; } = SaveVersion.Current;

    /// <summary>模拟时间（模拟秒）。</summary>
    public double SimTime { get; set; }

    /// <summary>
    /// 累计<b>发放</b>给英雄的金币（整金）。<b>0,6 新增</b>（D-054 阶段 4）。
    /// </summary>
    /// <remarks>
    /// 守恒律的「累计新增」这一项。不存它，读档后账就<b>对不上</b>。
    /// </remarks>
    public long GoldGrantedTotal { get; set; }

    /// <summary>
    /// 累计<b>销毁</b>掉的金币（升级花掉的那些）。<b>0,6 新增</b>（D-054 阶段 4）。
    /// </summary>
    /// <remarks>
    /// 守恒律的「已销毁」这一项。升级花掉的钱**变成建筑**，
    /// 既不在任何英雄钱包里，也不在金库里 —— 必须单独记一笔。
    /// </remarks>
    public long GoldSpentTotal { get; set; }

    /// <summary>
    /// 村庄层级（0 = 未升级）。<b>0,6 新增</b>（D-054 阶段 4）。
    /// </summary>
    /// <remarks>
    /// <b>顺带修一个既有缺陷</b>：村庄层级<b>本来就不入档</b> ——
    /// 读档后村庄回到 0 级，而升级花掉的金币已经记下了 → <b>账实不符</b>。
    /// </remarks>
    public int VillageTier { get; set; }

    /// <summary>
    /// 累计<b>已付款</b>的升级次数。<b>0,6 新增</b>（D-054 阶段 4）。
    /// </summary>
    /// <remarks>
    /// 与 <see cref="VillageTier"/>（**已建成**的级数）不同：付款时层级还没升上去
    /// （建造要时间）。两个数都存，才对得上账。
    /// </remarks>
    public long VillageUpgrades { get; set; }

    /// <summary>
    /// 随机数状态（十进制字符串）。
    /// </summary>
    /// <remarks>
    /// <para><b>它是【待定】T-007 的实现细节</b>：随机策略本身没定，
    /// 但<b>「必须能存」是存档能成立的前提</b>，所以字段先落在这里。</para>
    /// <para><b>为什么不能用 <c>System.Random</c></b>：它不暴露内部状态。
    /// 于是这里要么存种子（只能回到开局那一刻，不是存档那一刻），
    /// 要么什么都不存（读档后完全不可复现）——
    /// <b>两者都是「看起来能读档、实际不可复现」。</b></para>
    /// <para>读取时若解析不出，整份存档仍会照常加载，只是不恢复随机状态 ——
    /// 这是<b>已知的能力缺口</b>，不是静默失败。</para>
    /// </remarks>
    public string? RandomState { get; set; }

    /// <summary>村庄。</summary>
    public VillageSave Village { get; set; } = new();

    /// <summary>英雄。</summary>
    public List<HeroSave> Heroes { get; set; } = new();

    /// <summary>任务。</summary>
    public List<MissionSave> Missions { get; set; } = new();

    /// <summary>
    /// 已结算的结算键集合 —— <b>必须落盘</b>。
    /// </summary>
    /// <remarks>
    /// 不落它，读档后会重复发奖（不变量 I4）。原型里没有任何存档，
    /// 这条约束此前只存在于文档里；<b>现在它必须能往返</b>。
    /// </remarks>
    public List<string> AppliedSettlementKeys { get; set; } = new();

    /// <summary>
    /// 已应用 <c>TradeId</c> 集合（M6 / D-029 / 0,2 新增）。
    /// </summary>
    /// <remarks>
    /// <b>0,2 新增</b>：与 <see cref="AppliedSettlementKeys"/> 分开存（键空间不冲突，
    /// <c>TradeId.ToString()</c> 与 <c>SettlementKey.ToString()</c> 形态不同），
    /// 旧档缺此字段 → 整份拒绝（I11 + D-021）。
    /// </remarks>
    public List<string> AppliedTradeKeys { get; set; } = new();

    /// <summary>
    /// 已应用的购买业务请求键（<b>0,4 新增</b>，第四阶段 M4 / D-044）。
    /// </summary>
    /// <remarks>
    /// <para>对应 <c>StallService.TryBuy(..., requestId)</c> 的幂等键。
    /// <b>必须落盘</b>：读档后同一请求键必须仍被识别为重复，
    /// 否则一次「读档 → 重放」就会再扣一次款、再发一次货。</para>
    /// <para><b>不是</b> <see cref="AppliedTradeKeys"/>：后者是 <c>TradeId</c> 空间，
    /// 留给尚未接线的英雄↔英雄直接交易（D-038 的 R-01/R-02），当前恒为空。
    /// 把 requestId 塞进那个字段会让两个键空间混在一起、以后无法区分。</para>
    /// </remarks>
    public List<string> AppliedPurchaseKeys { get; set; } = new();

    /// <summary>
    /// 每日自主购物最近一次尝试的游戏日（<b>0,4 新增</b>，D-044）。
    /// </summary>
    /// <remarks>
    /// <c>DailyShoppingService</c> 的同日闸门：<see cref="int.MinValue"/> = 从未运行。
    /// 不存它，读档后<b>同一天</b>会再跑一遍「每人最多买一次」的尝试 ——
    /// 已经成交的人由 <see cref="HeroSave.LastShoppingDay"/> 挡住，但当天还没成交的人
    /// 会多一次机会，读档前后世界不一致。
    /// </remarks>
    public int DailyShoppingLastRunDay { get; set; } = int.MinValue;

    /// <summary>
    /// 摊位列表（<b>0,4 新增</b>，D-044）。
    /// </summary>
    /// <remarks>
    /// 每个英雄<b>至多一个</b>摊位（按 <see cref="StallSave.OwnerId"/> 唯一）。
    /// 空列表 = 村里没有摊位。
    /// </remarks>
    public List<StallSave> Stalls { get; set; } = new();

    /// <summary>
    /// 英雄之间的有向关系（0,3 新增，第三阶段 / P3-D10）。
    /// </summary>
    /// <remarks>
    /// 有向：每一对 <c>(From, To)</c> 一条；反向关系是另一条记录。
    /// 英雄引用由 <see cref="SaveValidator"/> 校验 —— 引用断了整份拒绝（I11）。
    /// </remarks>
    public List<RelationshipSave> Relationships { get; set; } = new();

    /// <summary>
    /// 已应用的关系变化业务键（0,3 新增，P3-D08）。
    /// </summary>
    /// <remarks>
    /// <b>必须落盘</b>：不存它，读档后同一批社交 / 副本合作会再次加关系。
    /// 与 <see cref="AppliedSettlementKeys"/> / <see cref="AppliedTradeKeys"/> 同一模式。
    /// </remarks>
    public List<string> AppliedRelationKeys { get; set; } = new();

    /// <summary>
    /// 玩家当前编成的队伍成员（英雄 Id）。
    /// </summary>
    /// <remarks>
    /// <b>为什么必须单独存</b>：<c>Missions</c> 里的 <c>MemberIds</c>
    /// 是<b>某个任务</b>的成员，而这一项是<b>还没出发时站在村里的队伍</b> ——
    /// 两者不是一回事。
    /// <para>
    /// 不存它，读档就只能「按在场者取前 N 个」重建 —— 那是<b>猜测</b>：
    /// 玩家精心挑的搭配会悄悄变成另一批人，而游戏不会报错。
    /// </para>
    /// </remarks>
    public List<string> SquadMemberIds { get; set; } = new();
}

/// <summary>
/// 存档校验器 —— 加载前的关卡。
/// </summary>
/// <remarks>
/// <b>唯一职责：判定这份存档能不能加载。</b>它不负责恢复状态，
/// 恢复是加载器的事；而加载器<b>只有在校验通过之后才被调用</b>。
/// </remarks>
public static class SaveValidator
{
    /// <summary>
    /// 校验一份存档。
    /// </summary>
    /// <param name="save">待校验的存档。</param>
    /// <returns>全部通过时 <see cref="SaveValidationResult.Ok"/>，否则带错误清单。</returns>
    public static SaveValidationResult Validate(SaveGame? save)
    {
        var errors = new List<SaveError>();

        if (save is null)
        {
            errors.Add(new SaveError("(root)", "存档为空。"));
            return SaveValidationResult.Fail(errors);
        }

        // 版本必须可识别 —— 未知版本的迁移入口不存在时，宁可拒绝加载。
        if (save.Version != SaveVersion.Current)
        {
            errors.Add(new SaveError(
                "version",
                $"存档版本 {save.Version} 与当前版本 {SaveVersion.Current} 不符，且没有对应的迁移入口。"));
        }

        if (string.IsNullOrEmpty(save.Village?.Id))
        {
            errors.Add(new SaveError("village.id", "村庄标识为空。"));
        }

        // 先收集全部英雄 ID：目标/引用校验是「前向引用」，不能边遍历边建集合
        // （否则 TargetId 指向列表里排在自己后面的人会被误判为断引用）。
        var allHeroIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var hero in save.Heroes)
        {
            if (!string.IsNullOrEmpty(hero.Id)) allHeroIds.Add(hero.Id);
        }

        var heroIds = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < save.Heroes.Count; i++)
        {
            var h = save.Heroes[i];
            var path = $"heroes[{i}]";
            if (string.IsNullOrEmpty(h.Id)) errors.Add(new SaveError($"{path}.id", "英雄标识为空。"));
            else if (!heroIds.Add(h.Id)) errors.Add(new SaveError($"{path}.id", $"英雄标识重复：{h.Id}。"));

            // 表现侧：空性格 / 空投入度会画不出人；负属性没有意义。
            // 这些不是「坏了但能救」，而是「读出来就是错的」。
            if (string.IsNullOrEmpty(h.Personality))
            {
                errors.Add(new SaveError($"{path}.personality", "性格为空 —— 读档后画不出这个人。"));
            }

            if (h.Power < 0 || h.Speed < 0 || h.Grit < 0)
            {
                errors.Add(new SaveError(
                    $"{path}", $"属性为负（力 {h.Power} / 速 {h.Speed} / 韧 {h.Grit}）。"));
            }

            // 待发零头（0.6 新增）：单位 1/100 金，必须落在 [0, 99]。
            if (h.GoldCarry < 0 || h.GoldCarry >= 100)
            {
                errors.Add(new SaveError(
                    $"{path}.goldCarry",
                    $"待发零头 {h.GoldCarry} 超出合法范围 [0, 99]（单位 1/100 金）。"));
            }

            // 在场性与行动状态（R-03）：读档要恢复「在村/随队」与「正在做什么」，
            // 非法值不能静默当成默认值 —— 那会读出一个规则解释不了的人。
            if (h.Presence != "InVillage" && h.Presence != "OnMission")
            {
                errors.Add(new SaveError(
                    $"{path}.presence", $"在场性「{h.Presence}」无法识别（只允许 InVillage / OnMission）。"));
            }

            if (!double.IsFinite(h.RemainingActionSeconds) || h.RemainingActionSeconds < 0.0)
            {
                errors.Add(new SaveError(
                    $"{path}.remainingActionSeconds",
                    $"行动剩余时长 {h.RemainingActionSeconds} 必须是非负有限数。"));
            }

            // 自主购物的每日频率状态（0,4 新增，D-044）：日号不能为负。
            // 0 = 从未购过；负数不是任何合法日号，只可能是损坏。
            if (h.LastShoppingDay < 0)
            {
                errors.Add(new SaveError(
                    $"{path}.lastShoppingDay", $"上次自主购物日 {h.LastShoppingDay} 为负（0 = 从未购过）。"));
            }

            // 随队的人不应同时挂着日常行动（SetOnMission 会释放行动）。
            if (h.Presence == "OnMission" && !string.IsNullOrEmpty(h.CurrentActivity))
            {
                errors.Add(new SaveError(
                    $"{path}.currentActivity", "随队英雄不应同时有进行中的日常行动。"));
            }

            // 目标型行动（社交）：目标必须存在，且不能是自己。
            if (!string.IsNullOrEmpty(h.TargetId))
            {
                if (string.IsNullOrEmpty(h.CurrentActivity))
                {
                    errors.Add(new SaveError(
                        $"{path}.targetId", "没有进行中的行动，却记录了行动目标。"));
                }

                if (!allHeroIds.Contains(h.TargetId))
                {
                    errors.Add(new SaveError(
                        $"{path}.targetId", $"行动目标「{h.TargetId}」在英雄列表中不存在 —— 引用断了。"));
                }

                if (h.TargetId == h.Id)
                {
                    errors.Add(new SaveError($"{path}.targetId", "行动目标不能是英雄自己。"));
                }
            }

            // CR-1：性格修正与偏好权重必须严格为正，否则
            // W(a,h,t) = P·M·B·S 会出现零因子，权重不是二极管那条就破了。
            foreach (var (key, value) in h.TraitModifiers)
            {
                if (!(value > 0.0) || double.IsNaN(value) || double.IsInfinity(value))
                {
                    errors.Add(new SaveError(
                        $"{path}.traitModifiers['{key}']", $"性格修正 {value} 不是正有限数（CR-1）。"));
                }
            }

            foreach (var (key, value) in h.PreferenceWeights)
            {
                if (!(value > 0.0) || double.IsNaN(value) || double.IsInfinity(value))
                {
                    errors.Add(new SaveError(
                        $"{path}.preferenceWeights['{key}']", $"偏好权重 {value} 不是正有限数（CR-1）。"));
                }
            }
        }

        var missionIds = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < save.Missions.Count; i++)
        {
            var m = save.Missions[i];
            var path = $"missions[{i}]";
            if (string.IsNullOrEmpty(m.Id)) errors.Add(new SaveError($"{path}.id", "任务标识为空。"));
            else if (!missionIds.Add(m.Id)) errors.Add(new SaveError($"{path}.id", $"任务标识重复：{m.Id}。"));

            // 引用校验：任务成员必须指向存在的英雄（基线 §13.7）
            for (var k = 0; k < m.MemberIds.Count; k++)
            {
                var memberId = m.MemberIds[k];
                if (!heroIds.Contains(memberId))
                {
                    errors.Add(new SaveError(
                        $"{path}.memberIds[{k}]",
                        $"任务成员「{memberId}」在英雄列表中不存在 —— 引用断了。"));
                }
            }
        }

        // 引用校验：编队成员同样必须指向存在的英雄（I11 一致）
        for (var k = 0; k < save.SquadMemberIds.Count; k++)
        {
            if (!heroIds.Contains(save.SquadMemberIds[k]))
            {
                errors.Add(new SaveError(
                    $"squadMemberIds[{k}]",
                    $"队伍成员「{save.SquadMemberIds[k]}」在英雄列表中不存在 —— 引用断了。"));
            }
        }

        // 关系引用与值域（0,3 新增，第三阶段 / P3-D10）
        var relationPairs = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < save.Relationships.Count; i++)
        {
            var r = save.Relationships[i];
            var path = $"relationships[{i}]";

            if (string.IsNullOrEmpty(r.FromId))
            {
                errors.Add(new SaveError($"{path}.fromId", "关系的一方标识为空。"));
            }
            else if (!heroIds.Contains(r.FromId))
            {
                errors.Add(new SaveError(
                    $"{path}.fromId", $"关系的一方「{r.FromId}」在英雄列表中不存在 —— 引用断了。"));
            }

            if (string.IsNullOrEmpty(r.ToId))
            {
                errors.Add(new SaveError($"{path}.toId", "关系的另一方标识为空。"));
            }
            else if (!heroIds.Contains(r.ToId))
            {
                errors.Add(new SaveError(
                    $"{path}.toId", $"关系的另一方「{r.ToId}」在英雄列表中不存在 —— 引用断了。"));
            }

            if (!string.IsNullOrEmpty(r.FromId) && r.FromId == r.ToId)
            {
                errors.Add(new SaveError($"{path}", "关系双方是同一个英雄 —— 自己与自己不能建立关系。"));
            }

            if (r.Value < Relationship.MinValue || r.Value > Relationship.MaxValue)
            {
                errors.Add(new SaveError(
                    $"{path}.value",
                    $"关系值 {r.Value} 越出 [{Relationship.MinValue}, {Relationship.MaxValue}]。"));
            }

            string pairKey = $"{r.FromId}->{r.ToId}";
            if (!relationPairs.Add(pairKey))
            {
                errors.Add(new SaveError($"{path}", $"有向关系重复：{pairKey}。"));
            }
        }

        // 已应用关系键：空串没有幂等意义，视为损坏
        for (var i = 0; i < save.AppliedRelationKeys.Count; i++)
        {
            if (string.IsNullOrWhiteSpace(save.AppliedRelationKeys[i]))
            {
                errors.Add(new SaveError(
                    $"appliedRelationKeys[{i}]", "已应用关系键为空 —— 它不能作为幂等依据。"));
            }
        }

        // 购买幂等键（0,4 新增，D-044）：与关系键同一规矩 —— 空串不是幂等依据。
        for (var i = 0; i < save.AppliedPurchaseKeys.Count; i++)
        {
            if (string.IsNullOrWhiteSpace(save.AppliedPurchaseKeys[i]))
            {
                errors.Add(new SaveError(
                    $"appliedPurchaseKeys[{i}]", "已应用购买请求键为空 —— 它不能作为幂等依据。"));
            }
        }

        // 每日购物闸门（0,4 新增，D-044）：int.MinValue = 从未运行；其余必须是第 1 天起的日号。
        if (save.DailyShoppingLastRunDay != int.MinValue && save.DailyShoppingLastRunDay < 1)
        {
            errors.Add(new SaveError(
                "dailyShoppingLastRunDay",
                $"每日购物最近运行日 {save.DailyShoppingLastRunDay} 非法"
                + $"（应为第 1 天起的日号，或 {int.MinValue} = 从未运行）。"));
        }

        // 摊位（0,4 新增，D-044）
        var stallOwners = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < save.Stalls.Count; i++)
        {
            var s = save.Stalls[i];
            var path = $"stalls[{i}]";

            if (string.IsNullOrEmpty(s.OwnerId))
            {
                errors.Add(new SaveError($"{path}.ownerId", "摊主标识为空。"));
            }
            else if (!heroIds.Contains(s.OwnerId))
            {
                errors.Add(new SaveError(
                    $"{path}.ownerId", $"摊主「{s.OwnerId}」在英雄列表中不存在 —— 引用断了。"));
            }
            else if (!stallOwners.Add(s.OwnerId))
            {
                errors.Add(new SaveError(
                    $"{path}.ownerId", $"同一摊主出现多个摊位：{s.OwnerId}（一人一摊）。"));
            }

            if (string.IsNullOrEmpty(s.ItemId))
            {
                errors.Add(new SaveError($"{path}.itemId", "摊位商品标识为空。"));
            }

            if (s.Stock < 0)
            {
                errors.Add(new SaveError($"{path}.stock", $"摊位库存 {s.Stock} 为负。"));
            }
            else if (s.Status == "Open" && s.Stock < 1)
            {
                // 营业中却无货 = 状态与库存自相矛盾。合法路径在售罄那一刻就转 SoldOut。
                errors.Add(new SaveError(
                    $"{path}.stock", "营业中的摊位库存必须 ≥ 1（售罄应改为 SoldOut）。"));
            }

            if (s.UnitPrice < 0)
            {
                errors.Add(new SaveError(
                    $"{path}.unitPrice", $"摊位单价 {s.UnitPrice} 为负（0 = 白送，合法）。"));
            }

            if (s.Status != "Open" && s.Status != "SoldOut" && s.Status != "Closed")
            {
                errors.Add(new SaveError(
                    $"{path}.status",
                    $"摊位状态「{s.Status}」无法识别（只允许 Open / SoldOut / Closed）。"));
            }

            if (!double.IsFinite(s.SimTime) || s.SimTime < 0.0)
            {
                errors.Add(new SaveError(
                    $"{path}.simTime", $"开摊时间 {s.SimTime} 必须是非负有限数。"));
            }
        }

        for (var i = 0; i < save.Village.Buildings.Count; i++)
        {
            var b = save.Village.Buildings[i];
            var path = $"village.buildings[{i}]";
            if (string.IsNullOrEmpty(b.Id)) errors.Add(new SaveError($"{path}.id", "建筑标识为空。"));
            if (b.W <= 0 || b.H <= 0) errors.Add(new SaveError($"{path}", $"占地必须为正，实际 {b.W}×{b.H}。"));
            if (b.Progress < 0.0 || b.Progress > 1.0)
            {
                errors.Add(new SaveError($"{path}.progress", $"建造进度 {b.Progress} 越出 [0,1]。"));
            }
        }

        // 建筑坐标冲突（基线 §13.7 明列「建筑坐标与占地冲突」）
        var occupied = new Dictionary<(int, int), int>();
        for (var i = 0; i < save.Village.Buildings.Count; i++)
        {
            var b = save.Village.Buildings[i];
            for (var x = b.X; x < b.X + b.W; x++)
            {
                for (var y = b.Y; y < b.Y + b.H; y++)
                {
                    var cell = (x, y);
                    if (occupied.TryGetValue(cell, out var other))
                    {
                        errors.Add(new SaveError(
                            $"village.buildings[{i}]",
                            $"建筑 {i}（{b.Id}）与建筑 {other} 在格位 ({x},{y}) 重叠。"));
                    }
                    else
                    {
                        occupied[cell] = i;
                    }
                }
            }
        }

        // 资源不得为负（不变量 I12 在存档侧的表现）
        foreach (var (resourceId, amount) in save.Village.Resources)
        {
            if (amount < 0)
            {
                errors.Add(new SaveError(
                    $"village.resources['{resourceId}']",
                    $"资源余额 {amount} 为负 —— 不变量 I12。"));
            }
        }

        // 村庄税额余数：单位是 1/100 金，必须落在 [0, 99]（0.5 新增 · D-052）
        if (save.Village.VillageCutRemainder < 0
            || save.Village.VillageCutRemainder >= ClaudeCodeGameStudios.Domain.Stall.StallFee.UnitsPerGold)
        {
            errors.Add(new SaveError(
                "village.villageCutRemainder",
                $"村庄税额余数 {save.Village.VillageCutRemainder} 超出合法范围 "
                + $"[0, {ClaudeCodeGameStudios.Domain.Stall.StallFee.UnitsPerGold - 1}]。"));
        }

        // 0.6 新增（D-054 阶段 4）：累计发放 / 累计销毁 / 村庄层级
        if (save.GoldGrantedTotal < 0)
        {
            errors.Add(new SaveError("goldGrantedTotal", "累计发放的金币为负。"));
        }

        if (save.GoldSpentTotal < 0)
        {
            errors.Add(new SaveError("goldSpentTotal", "累计销毁的金币为负。"));
        }

        if (save.VillageTier < 0
            || save.VillageTier > ClaudeCodeGameStudios.Application.VillageUpgradeService.MaxTier)
        {
            errors.Add(new SaveError(
                "villageTier",
                $"村庄层级 {save.VillageTier} 超出合法范围 "
                + $"[0, {ClaudeCodeGameStudios.Application.VillageUpgradeService.MaxTier}]。"));
        }

        if (save.VillageUpgrades < 0
            || save.VillageUpgrades > ClaudeCodeGameStudios.Application.VillageUpgradeService.MaxTier)
        {
            errors.Add(new SaveError(
                "villageUpgrades",
                $"累计已付款的升级次数 {save.VillageUpgrades} 超出合法范围 "
                + $"[0, {ClaudeCodeGameStudios.Application.VillageUpgradeService.MaxTier}]。"));
        }

        return errors.Count == 0 ? SaveValidationResult.Ok() : SaveValidationResult.Fail(errors);
    }
}
