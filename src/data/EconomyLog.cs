using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ClaudeCodeGameStudios.Domain.Events;

namespace ClaudeCodeGameStudios.Data;

/// <summary>副本掉落行里的一件物品。</summary>
/// <param name="Item">物品标识。</param>
/// <param name="Qty">份数。</param>
public sealed record LootLine(string Item, long Qty);

/// <summary>副本掉落行里的一份发放（谁拿到了什么）。</summary>
/// <param name="Hero">英雄标识。</param>
/// <param name="Item">物品标识。</param>
/// <param name="Qty">份数。</param>
public sealed record GrantLine(string Hero, string Item, long Qty);

/// <summary>
/// 经济流水的一行（JSONL 的一行）。
/// </summary>
/// <remarks>
/// <para><b>为什么是「一个扁平类型 + 可空字段」</b>：流水只有两种行
/// （副本掉落 / 交易），字段集合不同。拆成两个类型则分析脚本要写两套解析；
/// 扁平类型则每一行都有同一组键，<c>jq</c> / pandas 一把梭。</para>
/// <para><b>标识与时间</b>：<see cref="Id"/> 就是领域事件 ID ——
/// 它由「类型 + 参与者 + 序号」确定性生成，<b>跨运行可复现</b>，且天然去重。
/// 用它当交易/副本的业务标识，不需要另造一套编号体系。</para>
/// </remarks>
public sealed record EconomyRow
{
    /// <summary>行类别：<c>DungeonLooted</c> / <c>TradeCompleted</c>。</summary>
    public string Kind { get; init; } = "";

    /// <summary>唯一标识（领域事件 ID）。</summary>
    public string Id { get; init; } = "";

    /// <summary>游戏日（从 1 起）。</summary>
    public long Day { get; init; }

    /// <summary>模拟时间（模拟秒）。</summary>
    public double SimTime { get; init; }

    /// <summary>参与者（副本标识 / 买家名）。</summary>
    public string Actor { get; init; } = "";

    // ── 副本结算 ──

    /// <summary>副本实例标识。</summary>
    public string? RunId { get; init; }

    /// <summary>副本名称。</summary>
    public string? Place { get; init; }

    /// <summary>副本是否成功。</summary>
    public bool? Won { get; init; }

    /// <summary>实际参战名单。</summary>
    public string[]? Participants { get; init; }

    /// <summary>本次产出的战利品（未分配前）。</summary>
    public LootLine[]? Loot { get; init; }

    /// <summary>最终分配结果（谁拿到了什么）。</summary>
    public GrantLine[]? Grants { get; init; }

    // ── 大事件结算（D-083）──

    /// <summary>这次事件是什么（如「兽潮」）。</summary>
    public string? Event { get; init; }

    /// <summary>这次事件对<b>材料</b>的净变化（正 = 发，负 = 扣）。</summary>
    public long? MaterialDelta { get; init; }

    /// <summary>这次事件对<b>金币</b>的净变化（正 = 发，负 = 扣）。</summary>
    public long? GoldDelta { get; init; }

    // ── 交易 ──

    /// <summary>买方英雄标识。</summary>
    public string? Buyer { get; init; }

    /// <summary>卖方英雄标识。</summary>
    public string? Seller { get; init; }

    /// <summary>商品标识。</summary>
    public string? Item { get; init; }

    /// <summary>成交数量。</summary>
    public long? Qty { get; init; }

    /// <summary>买家实付（= 摊主净得 + 村抽）。</summary>
    public long? Amount { get; init; }

    /// <summary>摊主净得。</summary>
    public long? SellerNet { get; init; }

    /// <summary>村金库抽成。</summary>
    public long? VillageCut { get; init; }

    /// <summary>
    /// <b>记为销毁</b>的金币（D-088：商队货款的 90%）。
    /// </summary>
    /// <remarks>
    /// ⚠️ **它不是「钱消失了」**：守恒律（流通口径）是
    /// <c>Σ英雄钱包 + 村库 + <b>累计销毁</b> = 初始 + 累计发放</c>
    /// ⇒ 销毁的那部分必须<b>有账</b>，否则等式对不上。
    /// （摊位的 5% 村抽<b>不是</b>销毁 —— 那笔钱还在村库里。）
    /// </remarks>
    public long? GoldDestroyed { get; init; }

    // ── 未成（想买没买成） ───────────────────────────────────────────

    /// <summary>
    /// 未成交的<b>结构化原因</b>（仅 <see cref="EconomyLog.ShoppingMissedKind"/> 行有值）。
    /// </summary>
    /// <remarks>
    /// 存<b>枚举名</b>（如 <c>BagFull</c> / <c>NotEnoughGold</c>）而不是自由文本 ——
    /// 自由文本没法分组统计，而这行的全部意义就是「按原因分组数一数」。
    /// </remarks>
    public string? Reason { get; init; }

    /// <summary>
    /// 买家在这次尝试时的<b>钱包余额</b>（金币；买之前）。<b>D-054 阶段 5 新增</b>。
    /// </summary>
    /// <remarks>
    /// <b>为什么要有它</b>：「未成·钱不够」这个原因<b>分不清</b>
    /// ① 英雄<b>真的没钱</b>（余额 0~2）② 英雄有钱但<b>抽中了一个买不起的摊</b>（余额 40）。
    /// ① 是经济事实，② 是<b>抽样运气</b>（抽样不按购买力过滤）——
    /// 两者的设计含义完全相反。没有这个字段就永远分不开。
    /// </remarks>
    public long? BuyerGold { get; init; }

    /// <summary>
    /// 当时<b>买得起的那批摊中最便宜的单价</b>（金币）。<b>D-054 阶段 5 新增</b>。
    /// </summary>
    /// <remarks>
    /// 与 <see cref="BuyerGold"/> 合起来就能判定：
    /// <c>BuyerGold ≥ CheapestPrice</c> ⇒ <b>有便宜选项却没抽中</b>（抽样运气）；
    /// 否则 ⇒ <b>全场都买不起</b>（真 scarcity）。
    /// </remarks>
    public long? CheapestPrice { get; init; }

    // ── 每日经济快照（D-054 阶段 3）─────────────────────────────────────
    //
    // 守恒律的每一项都要能在流水里查到，否则「逐日核对」只能靠推算：
    //   Σ英雄金币 + 村库 + 已销毁金币 + 税额余数 = 初始金币 + 累计新增金币

    /// <summary>快照日的<b>英雄金币合计</b>（守恒律的「Σ英雄金币」）。</summary>
    public long? HeroGoldTotal { get; init; }

    /// <summary>快照日的<b>村庄金库余额</b>（守恒律的「村库」）。</summary>
    public long? VillageGold { get; init; }

    /// <summary>快照日的<b>村庄材料池</b>（升级材料线的进度）。</summary>
    public long? VillageMaterial { get; init; }

    /// <summary>累计<b>发放</b>给英雄的金币（守恒律的「累计新增」）。</summary>
    public long? GoldGranted { get; init; }

    /// <summary>累计<b>销毁</b>掉的金币（升级花掉的那些 → 变成建筑）。</summary>
    public long? GoldSpent { get; init; }

    /// <summary>
    /// 其中<b>商队销毁</b>掉的那部分（D-088；已含在 <see cref="GoldSpent"/> 里）。
    /// </summary>
    /// <remarks>
    /// 单列出来是为了<b>可核对</b>：升级花掉的钱与商队销毁的钱是两回事，
    /// 混在一起就只能看到一个总数 ⇒ 「商队到底带走了多少钱」永远算不出来。
    /// </remarks>
    public long? CaravanDestroyed { get; init; }

    /// <summary>累计升级次数（= 已建成的级数）。</summary>
    public long? Upgrades { get; init; }

    /// <summary>快照日的<b>税额余数</b>（单位 1/100 金；守恒律的「余数」）。</summary>
    public long? TaxCarry { get; init; }

    /// <summary>快照日的<b>英雄人数</b>（初始金币池 = 人数 × 100）。</summary>
    public long? HeroCount { get; init; }

    /// <summary>快照日<b>在工地的人手</b>数（建造中会抽走市场供给 → 数据偏差要标注）。</summary>
    public long? Builders { get; init; }

    /// <summary>快照日<b>待发零头</b>（单位 1/100 金；已挣到但还没进钱包的钱）。</summary>
    public long? GoldPending { get; init; }

    // ── 人均金币的分布（D-054 阶段 5）────────────────────────────────────
    //
    // 「流通池稳态」只管**合计**稳不稳；**合计稳 ≠ 钱分得匀**。
    // 快照只记合计 ⇒ 分不清「人人 40 金」与「3 个人 500 金、34 个人 0 金」——
    // 而这两种经济体感天差地别。
    // ⇒ 记 5 个分位点（最小 / p25 / 中位 / p75 / 最大），足以看出集中度。

    /// <summary>快照日<b>最穷</b>的英雄的钱包余额（金）。</summary>
    public long? HeroGoldMin { get; init; }

    /// <summary>快照日英雄钱包余额的 <b>p25</b>（下四分位；金）。</summary>
    public long? HeroGoldP25 { get; init; }

    /// <summary>快照日英雄钱包余额的<b>中位数</b>（金）。</summary>
    public long? HeroGoldMedian { get; init; }

    /// <summary>快照日英雄钱包余额的 <b>p75</b>（上四分位；金）。</summary>
    public long? HeroGoldP75 { get; init; }

    /// <summary>快照日<b>最富</b>的英雄的钱包余额（金）。</summary>
    public long? HeroGoldMax { get; init; }

    /// <summary>快照日<b>最富</b>的英雄的标识。</summary>
    /// <remarks>
    /// <b>为什么要有它</b>：分布字段只说「钱集中了」，<b>不说是谁</b>。
    /// 若**始终是同一个人**在攒钱 ⇒ 是<b>规则/个体</b>层面的问题（一个人可以无限囤、
    /// 别人买不动）；若**在轮换** ⇒ 是<b>系统性</b>的（每个人轮流富一阵）。
    /// **两者的修法完全不同**，而只靠分布分不开。
    /// </remarks>
    public string? HeroGoldTopId { get; init; }

    /// <summary>快照日<b>最富</b>英雄的<b>名字</b>。</summary>
    /// <remarks>
    /// <b>为什么 id 之外还要名字</b>：经济流水里的交易 / 未成行**只记名字不记 id**
    /// ⇒ 拿到 id 也**接不上流水**，追踪不了某个英雄的行为。
    /// 名字是流水那一侧的键。两个都记，才能把「谁最富」和「他卖了多少 / 买不了几次」对上。
    /// </remarks>
    public string? HeroGoldTopName { get; init; }
}

/// <summary>
/// 经济流水落盘 —— JSONL 追加写（运行闭环验证与经济数据采集准备）。
/// </summary>
/// <remarks>
/// <para><b>只落「经济流水」，不落全事件流</b>：全事件流里绝大多数是
/// 日常劳作与社交（37 人 × 每天几条），与分析经济平衡无关，写进去只是让文件变大。
/// 这里只收两类：<b>副本结算</b>与<b>交易</b>。</para>
///
/// <para><b>为什么用 JSONL 而不是 CSV</b>：两类行的字段集合不同，
/// CSV 要么留一堆空列，要么拆两个文件；JSONL 是一行一个对象，
/// 缺字段就不出现，<c>jq</c> 与 pandas 都能直接吃。</para>
///
/// <para><b>为什么不用 <c>FileAccess</c> + Godot 的 <c>JSON</c></b>：
/// 与 <see cref="SavePort"/> 同一个理由 —— Godot 的 JSON 是 <c>Variant</c> 形状的，
/// 与强类型对象对不上；<c>System.Text.Json</c> 在 .NET 里，
/// 不给这一层引入 Godot 依赖。</para>
///
/// <para><b>写盘是追加且立即 flush</b>：采集要跑很久，
/// 中途崩了不能丢掉已经采到的行。存档走的是「临时文件 + 替换」的原子语义，
/// 流水不需要 —— 它是<b>流式</b>数据，半行总比没数据强。</para>
/// </remarks>
public sealed class EconomyLog : IDisposable
{
    /// <summary>副本结算行的类别标识（每场副本一行，成功失败都有）。</summary>
    public const string DungeonSettledKind = "DungeonSettled";

    /// <summary>副本掉落行的类别标识（成功且有战利品时才有一行）。</summary>
    public const string DungeonLootKind = "DungeonLooted";

    /// <summary>交易行的类别标识。</summary>
    public const string TradeKind = "TradeCompleted";

    /// <summary>
    /// <b>大事件结算</b>行的类别标识（D-083）。
    /// </summary>
    /// <remarks>
    /// <b>为什么必须有它</b>：兽潮每 7 天动一次村库的<b>材料与金币</b>
    /// （赢了发、输了扣）⇒ 它是村库收支的<b>一个来源</b>。
    /// 不记它 ⇒ 标定脚本看到的就是「村库的钱莫名少了」⇒
    /// <b>每一次「钱去哪了」的追问都会先怀疑错方向</b>（实测：这次标定就是靠手写分析
    /// 才把「材料 7.4 万」和「流通金在缩」归到兽潮头上）。
    /// </remarks>
    public const string WorldEventSettledKind = "WorldEventSettled";

    /// <summary>
    /// 「<b>村庄收购</b>」行的类别标识（<b>每笔一行</b>；D-087）。
    /// </summary>
    /// <remarks>
    /// ⚠️ **为什么是「直写」而不是事件**（同 <see cref="AppendShoppingMissed"/> 的理由）：
    /// 收购是<b>每天几十笔的例行公事</b> ⇒ 一次一杂谈会把村庄刷屏 ⇒
    /// 它进<b>经济流水</b>（可被分析脚本逐笔核对），不进杂谈。
    /// </remarks>
    public const string VillagePurchaseKind = "VillagePurchase";

    /// <summary>
    /// 「<b>商队成交</b>」行的类别标识（D-088）。
    /// </summary>
    /// <remarks>
    /// ⚠️ **它必须单列一类**，不能并进 <see cref="TradeKind"/>：
    /// 摊位交易是「村抽 5%」，商队是「<b>村库抽成 10% + 90% 记为销毁</b>」
    /// ⇒ 两者的钱走法完全不同 ⇒ 分析脚本若把它们混在一起算，
    /// 「村抽 = 成交额 5%」那条等式会被商队那一行<b>弄坏</b>。
    /// </remarks>
    public const string CaravanTradeKind = "CaravanTrade";

    /// <summary>
    /// 「想买未成」行的类别标识（<b>每英雄每原因一行</b>）。
    /// </summary>
    /// <remarks>
    /// <b>为什么单独一类</b>：副本结算 / 掉落 / 交易都对应<b>真实成交</b>，
    /// 而这一类对应<b>没成交</b>。它不写进杂谈（37 人每天各一条会刷屏），
    /// 但<b>必须</b>落盘 —— 否则「背包满占未成原因多少」永远算不出来（见
    /// <see cref="AppendShoppingMissed"/>）。
    /// </remarks>
    public const string ShoppingMissedKind = "ShoppingMissed";

    /// <summary>
    /// 每日经济快照行的类别标识（D-054 阶段 3；<b>每天一行</b>）。
    /// </summary>
    /// <remarks>
    /// <b>为什么需要它</b>：守恒律（<b>流通口径</b>）
    /// 「<c>Σ英雄钱包金币 + 村库金币 + 累计销毁金币 = 初始金币 + 累计发放金币</c>」
    /// 里的每一项都得能在数据里查到，否则「逐日核对」只能靠事后推算 ——
    /// 而**推算就等于没有验证**。
    /// <para>
    /// ⚠️ <b>这里没有「税额余数」</b> —— 余数是记账零头，不是钱
    /// （见 <c>StallFee.Compute</c>：钱在买卖双方之间完整转移）。
    /// </para>
    /// <para>
    /// ⚠️ <b>这里也没有把「待发零头」算进守恒律</b> —— 它是
    /// 「已经挣到、但<b>还没进任何钱包</b>」的<b>应收</b>：
    /// 左边是「钱在哪」（它哪都不在），右边是「已经兑现了多少」
    /// （兑现的那部分才在「累计发放」里）。
    /// 只把它加在一边 = 凭空多算那部分钱（2026-10-20 的 37 人探针实测差出
    /// 整整一个零头：3810 vs 3820）。
    /// 它是<b>独立科目</b>（跟着存档 0.6 的 <c>GoldCarry</c> 走），单独核对自洽性。
    /// </para>
    /// </remarks>
    public const string SnapshotKind = "EconomySnapshot";

    /// <summary>条目分隔符（物品名不含逗号）。</summary>
    public const char EntrySeparator = ',';

    /// <summary>英雄与物品之间的分隔符（英雄标识不含冒号）。</summary>
    public const char HeroSeparator = ':';

    /// <summary>物品与数量之间的分隔符。</summary>
    public const char QuantitySeparator = '×';

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = false,
        // 模拟时间是 double；写出来要能读回来做统计，精度不能丢。
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly string _path;
    private readonly double _simSecondsPerDay;
    private StreamWriter? _writer;
    private long _rows;

    /// <summary>「未成」行的序号（用于拼出行标识；这些行没有领域事件 ID）。</summary>
    private long _missedSeq;

    /// <summary>「每日快照」行的序号（用于拼出行标识）。</summary>
    private long _snapshotSeq;

    /// <param name="path">落盘路径（由调用方注入，不写死 <c>user://</c>）。</param>
    /// <param name="simSecondsPerDay">一个模拟日的秒数（用来把模拟时间换算成游戏日）。</param>
    /// <exception cref="ArgumentException"><paramref name="path"/> 为空。</exception>
    public EconomyLog(string path, double simSecondsPerDay)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("流水路径不能为空。", nameof(path));
        }

        _path = path;
        _simSecondsPerDay = simSecondsPerDay > 0.0 ? simSecondsPerDay : 1.0;

        string? directory = System.IO.Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        _writer = new StreamWriter(
            new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false))
        {
            AutoFlush = true,   // 采集要跑很久：崩了不能丢掉已采到的行
        };
    }

    /// <summary>落盘路径。</summary>
    public string FilePath => _path;

    /// <summary>已写入的行数。</summary>
    public long RowCount => _rows;

    /// <summary>
    /// 追加一个事件。<b>只收经济类</b>；其余事件一律忽略。
    /// </summary>
    /// <param name="e">领域事件。</param>
    /// <returns>写入了就 true；不是经济事件或载荷不完整就 false。</returns>
    /// <remarks>
    /// <b>载荷缺字段就跳过</b>，不写半行 —— 半行 JSON 会在分析脚本里变成
    /// 一条「无法解析」的噪声，还会让行数对不上。
    /// </remarks>
    public bool Append(DomainEvent e)
    {
        if (e is null) return false;
        if (_writer is null) return false;

        EconomyRow? row = e.Type.Value switch
        {
            DungeonSettledKind => BuildDungeonSettledRow(e),
            DungeonLootKind => BuildDungeonLootRow(e),
            TradeKind => BuildTradeRow(e),
            WorldEventSettledKind => BuildWorldEventSettledRow(e),
            CaravanTradeKind => BuildCaravanTradeRow(e),
            _ => null,
        };

        if (row is null) return false;

        _writer.WriteLine(JsonSerializer.Serialize(row, Options));
        _rows++;
        return true;
    }

    /// <summary>
    /// 追加一条「<b>想买未成</b>」的流水行（<b>每英雄每原因一行</b>）。
    /// </summary>
    /// <param name="simTime">发生时间（模拟秒）；游戏日由它换算。</param>
    /// <param name="hero">想买没买成的英雄（名字或标识都行，只要稳定）。</param>
    /// <param name="reason">
    /// <b>结构化原因</b>（枚举名，如 <c>BagFull</c> / <c>NotEnoughGold</c>）。
    /// 不要传自由文本 —— 这一行的全部意义就是「按原因分组数一数」。
    /// </param>
    /// <param name="seller">相关摊主（可空）。</param>
    /// <param name="item">相关物品（可空）。</param>
    /// <param name="buyerGold">买家当时的<b>钱包余额</b>（可空）。</param>
    /// <param name="cheapestPrice">当时<b>买得起的摊里最便宜的单价</b>（可空）。</param>
    /// <returns>写入了就 true；文件已关 / 缺必要字段就 false。</returns>
    /// <remarks>
    /// <para><b>为什么它是直写入口，而不是走 <see cref="Append(DomainEvent)"/></b>：
    /// 未成交**没有任何状态变化**（钱没动、货没动、库存没动、
    /// <c>LastShoppingDay</c> 也不写），而 <c>DomainEvent</c> 在<b>构造期</b>
    /// 就拒绝 <c>ChangedStates</c> 为空的事件 —— 「不改变任何状态的事件按定义就是装饰」。
    /// 伪造一个事件来承载它，等于绕过反装饰规则。
    /// → 它是<b>读数</b>，不是事件；与 R-07 的每日汇总同一先例。</para>
    ///
    /// <para><b>为什么需要它</b>：R-05 二轮采集发现「成交笔数从 155 笔/百天降到
    /// 50–70 笔/百天」，根因一直只是「背包上限」的<b>推断</b> ——
    /// 因为流水里<b>根本没记未成原因</b>。不记就没有分母，
    /// 「背包满占未成原因多少」就永远只能靠曲线猜。
    /// 本方法补上这个<b>测量缺口</b>。</para>
    ///
    /// <para><b>只记「想买未成」那几类</b>（调用方负责筛）：
    /// 「今天不想买 / 不在场 / 已经买过 / 村里没摊」是噪音，
    /// 记进去会让分母失真。</para>
    /// </remarks>
    public bool AppendShoppingMissed(
        double simTime, string hero, string reason,
        string? seller = null, string? item = null,
        long? buyerGold = null, long? cheapestPrice = null)
    {
        if (_writer is null) return false;
        if (string.IsNullOrEmpty(hero)) return false;
        if (string.IsNullOrEmpty(reason)) return false;

        long day = DayOf(simTime);
        _missedSeq++;

        var row = new EconomyRow
        {
            Kind = ShoppingMissedKind,
            // 没有领域事件 ID 可用 → 合成一个：日号 + 序号（同一文件内唯一）。
            Id = $"missed-{day}-{_missedSeq}",
            Day = day,
            SimTime = simTime,
            Actor = hero,
            Buyer = hero,
            Seller = seller,
            Item = item,
            Reason = reason,
            BuyerGold = buyerGold,
            CheapestPrice = cheapestPrice,
        };

        _writer.WriteLine(JsonSerializer.Serialize(row, Options));
        _rows++;
        return true;
    }

    /// <summary>
    /// 追加一条<b>每日经济快照</b>（D-054 阶段 3；每天一行）。
    /// </summary>
    /// <param name="simTime">快照时刻（模拟秒）；游戏日由它换算。</param>
    /// <param name="heroGoldTotal">英雄金币合计。</param>
    /// <param name="villageGold">村庄金库余额。</param>
    /// <param name="villageMaterial">村庄材料池。</param>
    /// <param name="goldGranted">累计发放给英雄的金币。</param>
    /// <param name="goldSpent">累计销毁掉的金币（升级花掉的）。</param>
    /// <param name="upgrades">累计升级次数。</param>
    /// <param name="taxCarry">税额余数（单位 1/100 金）。</param>
    /// <param name="heroCount">英雄人数。</param>
    /// <param name="builders">在工地的人手数。</param>
    /// <param name="goldPending">待发零头（单位 1/100 金）。</param>
    /// <param name="heroGoldMin">最穷英雄的钱包余额（金）。</param>
    /// <param name="heroGoldP25">钱包余额 p25（金）。</param>
    /// <param name="heroGoldMedian">钱包余额中位数（金）。</param>
    /// <param name="heroGoldP75">钱包余额 p75（金）。</param>
    /// <param name="heroGoldMax">最富英雄的钱包余额（金）。</param>
    /// <param name="heroGoldTopId">最富英雄的标识。</param>
    /// <param name="heroGoldTopName">最富英雄的名字。</param>
    /// <param name="caravanDestroyed">商队货款里<b>记为销毁</b>的那部分（D-088；已含在 <paramref name="goldSpent"/> 里）。</param>
    /// <returns>写入了就 true；文件已关就 false。</returns>
    /// <remarks>
    /// <b>为什么是「直写」而不是事件</b>：快照是<b>读数</b>（某一刻的经济状态），
    /// 不是一次事件 —— 同一套理由见 <see cref="AppendShoppingMissed"/>。
    /// </remarks>
    public bool AppendSnapshot(
        double simTime,
        long heroGoldTotal,
        long villageGold,
        long villageMaterial,
        long goldGranted,
        long goldSpent,
        long upgrades,
        long taxCarry,
        long heroCount,
        long builders,
        long goldPending = 0,
        long? heroGoldMin = null,
        long? heroGoldP25 = null,
        long? heroGoldMedian = null,
        long? heroGoldP75 = null,
        long? heroGoldMax = null,
        string? heroGoldTopId = null,
        string? heroGoldTopName = null,
        long? caravanDestroyed = null)
    {
        if (_writer is null) return false;

        long day = DayOf(simTime);
        _snapshotSeq++;

        var row = new EconomyRow
        {
            Kind = SnapshotKind,
            Id = $"snapshot-{day}-{_snapshotSeq}",
            Day = day,
            SimTime = simTime,
            HeroGoldTotal = heroGoldTotal,
            VillageGold = villageGold,
            VillageMaterial = villageMaterial,
            GoldGranted = goldGranted,
            GoldSpent = goldSpent,
            Upgrades = upgrades,
            TaxCarry = taxCarry,
            HeroCount = heroCount,
            Builders = builders,
            GoldPending = goldPending,
            HeroGoldMin = heroGoldMin,
            HeroGoldP25 = heroGoldP25,
            HeroGoldMedian = heroGoldMedian,
            HeroGoldP75 = heroGoldP75,
            HeroGoldMax = heroGoldMax,
            HeroGoldTopId = heroGoldTopId,
            HeroGoldTopName = heroGoldTopName,
            CaravanDestroyed = caravanDestroyed,
        };

        _writer.WriteLine(JsonSerializer.Serialize(row, Options));
        _rows++;
        return true;
    }

    /// <summary>关掉文件（flush 并释放句柄）。<b>可重复调用</b>。</summary>    /// <remarks>
    /// 幂等是必需的：<c>using</c> 与显式 <c>Close()</c> 都会走到这里，
    /// 对已释放的 <c>StreamWriter</c> 再 Flush 会抛「Cannot write to a closed TextWriter」。
    /// </remarks>
    public void Close()
    {
        if (_writer is null) return;
        _writer.Flush();
        _writer.Dispose();
        _writer = null;   // 幂等：第二次 Close 直接返回
    }

    /// <summary>释放文件句柄（等价于 <see cref="Close"/>）。</summary>
    public void Dispose() => Close();

    // ── 行构建 ───────────────────────────────────────────────────────────

    /// <summary>由「整场结论」事件构建一行（<b>每场副本一行</b>，成功失败都有）。</summary>
    /// <param name="e">事件。</param>
    /// <returns>行；没有 runId 就不是副本结算行（返回 null）。</returns>
    /// <remarks>
    /// <b>判据是「有 runId」</b>：同名的 <c>DungeonSettled</c> 还有另外几条
    /// （队伍归来、带回战利品、白跑一趟），它们没有 runId —— 落盘时自然被跳过。
    /// 有了这一行，<b>副本成功率</b>才算得出来：战利品只在成功时才有，
    /// 只记掉落的话失败场次永远采不到。
    /// </remarks>
    private EconomyRow? BuildDungeonSettledRow(DomainEvent e)
    {
        if (!e.Payload.TryGetValue("runId", out var runId) || string.IsNullOrEmpty(runId)) return null;

        bool won = e.Payload.TryGetValue("won", out var wonText) && wonText == "1";

        return new EconomyRow
        {
            Kind = DungeonSettledKind,
            Id = e.Id.Value,
            Day = DayOf(e.OccurredAtSimSeconds),
            SimTime = e.OccurredAtSimSeconds,
            Actor = e.Actor,
            RunId = runId,
            Place = e.Payload.TryGetValue("place", out var place) ? place : null,
            Won = won,
            Participants = SplitList(e.Payload.TryGetValue("participants", out var p) ? p : null),
        };
    }

    /// <summary>由副本结算事件构建一行。</summary>
    /// <param name="e">事件。</param>
    /// <returns>行；载荷不完整时为 null。</returns>
    private EconomyRow? BuildDungeonLootRow(DomainEvent e)
    {
        if (!e.Payload.TryGetValue("runId", out var runId) || string.IsNullOrEmpty(runId)) return null;
        if (!e.Payload.TryGetValue("loot", out var lootText)) return null;
        if (!e.Payload.TryGetValue("grants", out var grantsText)) return null;

        bool won = e.Payload.TryGetValue("won", out var wonText) && wonText == "1";

        return new EconomyRow
        {
            Kind = DungeonLootKind,
            Id = e.Id.Value,
            Day = DayOf(e.OccurredAtSimSeconds),
            SimTime = e.OccurredAtSimSeconds,
            Actor = e.Actor,
            RunId = runId,
            Place = e.Payload.TryGetValue("place", out var place) ? place : null,
            Won = won,
            Participants = SplitList(e.Payload.TryGetValue("participants", out var p) ? p : null),
            Loot = ParseLoot(lootText),
            Grants = ParseGrants(grantsText),
        };
    }

    /// <summary>由交易事件构建一行。</summary>
    /// <param name="e">事件。</param>
    /// <returns>行；载荷不完整时为 null。</returns>
    private EconomyRow? BuildTradeRow(DomainEvent e)
    {
        if (!e.Payload.TryGetValue("buyer", out var buyer) || string.IsNullOrEmpty(buyer)) return null;
        if (!e.Payload.TryGetValue("seller", out var seller) || string.IsNullOrEmpty(seller)) return null;
        if (!e.Payload.TryGetValue("item", out var item) || string.IsNullOrEmpty(item)) return null;

        if (!e.Payload.TryGetValue("qty", out var qtyText) || !long.TryParse(qtyText, out var qty)) return null;
        if (!e.Payload.TryGetValue("amount", out var amountText) || !long.TryParse(amountText, out var amount)) return null;

        long sellerNet = ReadLong(e.Payload, "sellerNet") ?? 0L;
        long villageCut = ReadLong(e.Payload, "villageCut") ?? 0L;

        return new EconomyRow
        {
            Kind = TradeKind,
            Id = e.Id.Value,
            Day = DayOf(e.OccurredAtSimSeconds),
            SimTime = e.OccurredAtSimSeconds,
            Actor = buyer,
            Buyer = buyer,
            Seller = seller,
            Item = item,
            Qty = qty,
            Amount = amount,
            SellerNet = sellerNet,
            VillageCut = villageCut,
        };
    }

    /// <summary>由「大事件结算」事件构建一行（D-083）。</summary>
    /// <param name="e">事件。</param>
    /// <returns>行；载荷不完整时为 null。</returns>
    /// <remarks>
    /// ⚠️ **金额必须是机器可读的数字**（<c>material</c> / <c>gold</c> 两个载荷），
    /// 不能是那句给人看的「兽潮守住：材料 +40」⇒ 分析脚本要能**直接加减**。
    /// </remarks>
    private EconomyRow? BuildWorldEventSettledRow(DomainEvent e)
    {
        if (!e.Payload.TryGetValue("event", out var what) || string.IsNullOrEmpty(what)) return null;
        if (!e.Payload.TryGetValue("instance", out var instance) || string.IsNullOrEmpty(instance)) return null;

        bool won = e.Payload.TryGetValue("succeeded", out var wonText) && wonText == "1";

        return new EconomyRow
        {
            Kind = WorldEventSettledKind,
            Id = e.Id.Value,
            Day = DayOf(e.OccurredAtSimSeconds),
            SimTime = e.OccurredAtSimSeconds,
            Actor = e.Actor,
            RunId = instance,
            Place = what,
            Won = won,
            MaterialDelta = ReadLong(e.Payload, "material"),
            GoldDelta = ReadLong(e.Payload, "gold"),
        };
    }

    /// <summary>由「商队成交」事件构建一行（D-088）。</summary>
    /// <param name="e">事件。</param>
    /// <returns>行；载荷不完整时为 null。</returns>
    /// <remarks>
    /// ⚠️ <b>金额必须分得清</b>：总额（<c>amount</c>）= 村库抽成（<c>villageCut</c>）
    /// + 记为销毁（<c>destroyed</c>）⇒ 分析脚本要能直接核对这三者的关系。
    /// </remarks>
    private EconomyRow? BuildCaravanTradeRow(DomainEvent e)
    {
        if (!e.Payload.TryGetValue("buyer", out var buyer) || string.IsNullOrEmpty(buyer)) return null;
        if (!e.Payload.TryGetValue("item", out var item) || string.IsNullOrEmpty(item)) return null;
        if (!e.Payload.TryGetValue("qty", out var qtyText)
            || !long.TryParse(qtyText, NumberStyles.Integer, CultureInfo.InvariantCulture, out long qty)) return null;
        if (!e.Payload.TryGetValue("amount", out var amountText)
            || !long.TryParse(amountText, NumberStyles.Integer, CultureInfo.InvariantCulture, out long amount)) return null;

        return new EconomyRow
        {
            Kind = CaravanTradeKind,
            Id = e.Id.Value,
            Day = DayOf(e.OccurredAtSimSeconds),
            SimTime = e.OccurredAtSimSeconds,
            Actor = buyer,
            Buyer = buyer,
            Item = item,
            Qty = qty,
            Amount = amount,
            VillageCut = ReadLong(e.Payload, "villageCut"),
            GoldDestroyed = ReadLong(e.Payload, "destroyed"),
        };
    }

    /// <summary>
    /// 追加一条「<b>村庄收购</b>」的流水行（<b>每笔一行</b>；D-087）。
    /// </summary>
    /// <param name="simTime">发生时间（模拟秒）；游戏日由它换算。</param>
    /// <param name="hero">被收购的英雄（卖方）。</param>
    /// <param name="item">被收购的物品。</param>
    /// <param name="qty">收了几份。</param>
    /// <param name="amount">付了多少钱。</param>
    /// <returns>写入了就 true；文件已关或参数不全就 false。</returns>
    /// <remarks>
    /// <para>⚠️ **为什么是「直写」**：收购是<b>每天几十笔</b>的例行公事
    /// ⇒ 一次一杂谈会把村庄刷屏 ⇒ 它只进经济流水。</para>
    /// <para>⚠️ **钱的方向**：这一行里钱是<b>村库 → 英雄</b>（与摊位交易相反）⇒
    /// 分析脚本若按「交易行」的口径理解它，会把方向读反。</para>
    /// </remarks>
    public bool AppendVillagePurchase(
        double simTime, string hero, string item, long qty, long amount)
    {
        if (_writer is null) return false;
        if (string.IsNullOrEmpty(hero)) return false;
        if (string.IsNullOrEmpty(item)) return false;
        if (qty <= 0) return false;

        long day = DayOf(simTime);
        _missedSeq++;

        var row = new EconomyRow
        {
            Kind = VillagePurchaseKind,
            Id = $"purchase-{day}-{_missedSeq}",
            Day = day,
            SimTime = simTime,
            Actor = hero,
            Buyer = "村庄",
            Seller = hero,
            Item = item,
            Qty = qty,
            Amount = amount,
        };

        _writer.WriteLine(JsonSerializer.Serialize(row, Options));
        _rows++;
        return true;
    }

    private long DayOf(double simTime)        => (long)(simTime / _simSecondsPerDay) + 1L;

    private static long? ReadLong(IReadOnlyDictionary<string, string> payload, string key)
        => payload.TryGetValue(key, out var raw) && long.TryParse(raw, out var value) ? value : null;

    // ── 紧凑格式解析 ─────────────────────────────────────────────────────

    /// <summary>
    /// 解析战利品串：<c>石料×2,木料×1</c>。
    /// </summary>
    /// <param name="text">紧凑串。</param>
    /// <returns>解析出的条目；空串返回空数组。</returns>
    /// <remarks>
    /// <b>格式约定</b>：物品名不含逗号与「×」。当前物品（石料 / 木料 / 布匹）满足。
    /// 若将来引入含这些字符的物品名，要改这里 —— <b>不要默默产出错数据</b>。
    /// </remarks>
    public static LootLine[] ParseLoot(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return Array.Empty<LootLine>();

        var lines = new List<LootLine>();
        foreach (var part in text.Split(EntrySeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var idx = part.LastIndexOf(QuantitySeparator);
            if (idx <= 0) continue;

            string item = part[..idx];
            if (!long.TryParse(part[(idx + 1)..], NumberStyles.Integer, CultureInfo.InvariantCulture, out var qty)) continue;
            if (qty <= 0) continue;

            lines.Add(new LootLine(item, qty));
        }

        return lines.ToArray();
    }

    /// <summary>
    /// 解析分配串：<c>h-0:石料×2,h-1:木料×1</c>。
    /// </summary>
    /// <param name="text">紧凑串。</param>
    /// <returns>解析出的发放；空串返回空数组。</returns>
    public static GrantLine[] ParseGrants(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return Array.Empty<GrantLine>();

        var lines = new List<GrantLine>();
        foreach (var part in text.Split(EntrySeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var sep = part.IndexOf(HeroSeparator);
            if (sep <= 0) continue;

            string hero = part[..sep];
            var rest = part[(sep + 1)..];

            var idx = rest.LastIndexOf(QuantitySeparator);
            if (idx <= 0) continue;

            string item = rest[..idx];
            if (!long.TryParse(rest[(idx + 1)..], NumberStyles.Integer, CultureInfo.InvariantCulture, out var qty)) continue;
            if (qty <= 0) continue;

            lines.Add(new GrantLine(hero, item, qty));
        }

        return lines.ToArray();
    }

    /// <summary>把条目列表拼成紧凑串。</summary>
    /// <param name="items">条目。</param>
    /// <returns>紧凑串；空列表返回空串。</returns>
    public static string FormatLoot(IEnumerable<LootLine> items)
        => string.Join(EntrySeparator,
            items.Select(i => $"{i.Item}{QuantitySeparator}{i.Qty}"));

    /// <summary>把发放列表拼成紧凑串。</summary>
    /// <param name="items">发放。</param>
    /// <returns>紧凑串；空列表返回空串。</returns>
    public static string FormatGrants(IEnumerable<GrantLine> items)
        => string.Join(EntrySeparator,
            items.Select(g => $"{g.Hero}{HeroSeparator}{g.Item}{QuantitySeparator}{g.Qty}"));

    /// <summary>把标识列表拼成逗号分隔串。</summary>
    /// <param name="ids">标识。</param>
    /// <returns>逗号分隔串；空列表返回空串。</returns>
    public static string FormatIds(IEnumerable<string> ids) => string.Join(EntrySeparator, ids);

    private static string[] SplitList(string? text)
        => string.IsNullOrWhiteSpace(text)
            ? Array.Empty<string>()
            : text.Split(EntrySeparator, StringSplitOptions.RemoveEmptyEntries);
}
