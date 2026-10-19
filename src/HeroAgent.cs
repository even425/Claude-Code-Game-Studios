using Godot;
using System;
using ClaudeCodeGameStudios.Domain.Heroes;
using ClaudeCodeGameStudios.Domain.Items;
using ClaudeCodeGameStudios.Domain.Village;

/// <summary>
/// 一个村民。
///
/// 他来自**另一个世界**（动画 / 影视 / 游戏里的角色）—— 所以他有脾气、会不想干、
/// 会贪、会拿乔。这些不是装饰：性格决定他**挑不挑这桩活**，
/// 状态决定他**干不干活**。
/// <para><b>不要在这里加作息。</b>本类型原先描述的是「从地球召唤来的真人，
/// 有作息、会在地图上时有时无」—— 那套时区作息与缺席机制已由 <c>07</c> 的
/// <b>D-001 / D-002</b> 判死（基线 §2.2：英雄总在场）。
/// 那些字段（<c>AbsentDays</c> / <c>AbsentWhy</c> / <c>SlackDays</c> /
/// <c>RestlessDays</c>）仍在，但已无人写、无人读，是待清理的迁移残迹。</para>
/// </summary>
public partial class HeroAgent
{
    public string Name { get; init; } = "?";

    /// <summary>
    /// 该英雄的稳定标识（M2 挂账 / M7 兑现）。
    /// </summary>
    /// <remarks>
    /// <b>M7 / D-029 补</b>：M2 立 <c>TradeId</c> 时需要 HeroId，但 HeroAgent 当时没字段。
    /// 摆摊 UI（M7）真用 <c>TradeId</c> 在 <c>StallService.PlaceStall</c> 里时补上。
    /// <b>0,2 存档</b>要存（<c>HeroSave.Id</c> ↔ <c>HeroAgent.HeroId</c>）。
    /// <b>外部设置入口</b>：<see cref="AssignId"/>（internal，只让 <c>Main</c> / <c>HeroFactory</c> 调）。
    /// <see cref="init"/> 只读 set 防运行期改（与 M1 立的 <see cref="RestoreWallet"/> 同款）。
    /// </remarks>
    public HeroId Id { get; private set; } = HeroId.None;

    /// <summary>
    /// 一次性设 <see cref="Id"/>。<b>仅供 hero 创建方（Main / HeroFactory）调</b>。
    /// </summary>
    /// <remarks>
    /// 用 <c>private set</c> + 显式 <c>AssignId</c> 防运行期改；与 <see cref="RestoreWallet"/>
    /// 同款"只供创建方写一次"规矩（M1 已立）。
    /// </remarks>
    internal void AssignId(HeroId id)
    {
        if (string.IsNullOrEmpty(id.Value)) throw new ArgumentNullException(nameof(id));
        if (!string.IsNullOrEmpty(Id.Value))
        {
            throw new InvalidOperationException(
                $"HeroAgent.Id 已设（{Id.Value}），不可重复 AssignId。");
        }
        Id = id;
    }

    public string Personality { get; init; } = "莽";

    /// <summary>第二根性格轴：他投入多少。肝 / 随手 / 挂机。</summary>
    public string Diligence { get; init; } = "随手";

    public int Power { get; init; }
    public int Speed { get; init; }
    public int Grit { get; init; }

    public int DoneCount { get; set; }
    public int RefusedCount { get; set; }
    public int Pocketed { get; set; }


    // ---- 当下状态 -------------------------------------------------------

    /// <summary>缺席几天（病了 / 昨天打架了）。>0 时根本不上线。</summary>
    public int AbsentDays { get; set; }

    /// <summary>缺席的原因，写在花名册上 —— 村长至少知道为什么缺人。</summary>
    public string AbsentWhy { get; set; } = "";

    /// <summary>磨洋工还有几天。</summary>
    public int SlackDays { get; set; }

    /// <summary>心里不静还有几天。</summary>
    public int RestlessDays { get; set; }

    /// <summary>
    /// 该英雄上次<b>自主购物</b>发生的"模拟日号"（D-025）。
    /// </summary>
    /// <remarks>
    /// <b>每英雄每天 ≤ 1 次</b>（D-025 / P-05 频率挂钩）—— 由 <c>HeroBehaviorService</c> 在
    /// <c>SimulateShopping</c> 里读 + 写。0 = 还没购过；与 <c>SimClock</c> 派发的"今日号"比对。
    /// </remarks>
    public int LastShoppingDay { get; set; }


    /// <summary>他会不会出现在地图上。</summary>
    public bool Present => !Away;

    /// <summary>出发时离村，回来才重新出现在地图上。</summary>
    public bool Away { get; set; }

    /// <summary>被派去工地干活。</summary>
    public bool ToSite { get; set; }

    /// <summary>是否正在干活。<b>只读</b> —— 绘制方据此决定动画幅度。</summary>
    public bool IsWorking => _working;

    /// <summary>动画相位（模拟秒）。<b>只读</b> —— 领域不管怎么画，只报自己在第几拍。</summary>
    public float AnimationPhase => _bob;

    /// <summary>
    /// 工地位置（地图格）。<b>由应用层注入</b> —— 领域对象不应知道表现层的常量。
    /// 原实现直接读 <c>VillageView.SiteX/SiteY</c>，那是依赖方向反了（基线 §13.3）。
    /// </summary>
    public float SiteX { get; set; }
    public float SiteY { get; set; }

    /// <summary>
    /// <summary>干活速度：属性 × 性格 × 投入度。</summary>
    /// <remarks>
    /// <b>2026-10-18</b> 移除了「缺席 / 摸洋工 / 心里不静」三个乘数 ——
    /// 它们属于作废的当前状态清单（07 D-001）。基线 §4.4 说当前状态是【待定】，
    /// 第一阶段不建模（T-003），所以这里只剩结构上的两层乘数。</remarks>
    /// </summary>
    public float WorkRate
    {
        get
        {
            float r = 0.010f + Power * 0.00040f + Grit * 0.00028f;
            r *= Personality switch { "莽" => 1.35f, "傲" => 0.60f, _ => 1.00f };
            r *= Diligence switch { "肝" => 1.30f, "挂机" => 0.55f, _ => 1.00f };
            return r;
        }
    }


    // ---- 地图位置 -------------------------------------------------------

    // 地图坐标（格子），不是屏幕坐标 —— 投影由 VillageView 负责
    public float MapX { get; private set; }
    public float MapY { get; private set; }
    private float _tx, _ty;
    private float _wait;
    private float _bob;
    private bool _working;
    private readonly Random _rng = new();


    public HeroAgent()
    {
        MapX = 1.5f + _rng.NextSingle() * 10f;
        MapY = 3.0f + _rng.NextSingle() * 4f;
        PickSpot();
    }

    private void PickSpot()
    {
        // 傲的懒得挪窝，去的地方少；莽的到处跑
        if (Personality == "傲")
        {
            _tx = 5.5f + _rng.NextSingle() * 3.5f;
            _ty = 6.0f + _rng.NextSingle() * 1.6f;
        }
        else
        {
            _tx = 0.8f + _rng.NextSingle() * 12.5f;
            _ty = 1.2f + _rng.NextSingle() * 6.5f;
        }
    }

    public void Tick(float dt)
    {
        if (Away) return;               // 外出的人不在村里，不用动
        _bob += dt;

        if (ToSite)
        {
            // 走向工地，到了就一直干
            float sdx = SiteX - MapX, sdy = SiteY + 0.2f - MapY;
            float d2 = sdx * sdx + sdy * sdy;
            if (d2 > 0.05f)
            {
                float vsp = 1.1f * dt;
                float slen = MathF.Sqrt(d2);
                MapX += sdx / slen * vsp; MapY += sdy / slen * vsp;
            }
            _working = true;
            return;
        }

        if (_working)
        {
            _wait -= dt;
            if (_wait <= 0f) { _working = false; PickSpot(); }
            return;
        }
        float dx = _tx - MapX, dy = _ty - MapY;
        if (dx * dx + dy * dy < 0.04f)
        {
            _working = true;
            _wait = Personality switch
            {
                "傲" => 2.6f + _rng.NextSingle() * 2.2f,
                "莽" => 0.7f + _rng.NextSingle(),
                _    => 1.4f + _rng.NextSingle() * 1.4f,
            };
            return;
        }
        // 世界速度换算成屏幕速度要除以投影缩放
        float sp = (0.9f + Speed * 0.02f) * dt / 1.0f;
        float len = MathF.Sqrt(dx * dx + dy * dy);
        MapX += dx / len * sp;
        MapY += dy / len * sp;
    }

}

/// <summary>
/// 第二阶段 M1：英雄的钱包 + 物品背包。
/// </summary>
/// <remarks>
/// <para><b>挂在 HeroAgent 而不是 HeroState</b>：HeroState 的不变式
/// 是"构造期 + 唯一写者方法"（已有 S5 的 BeginAction / SetInVillage 那一套），
/// 撬动它的不可变性会牵动一摞已通过的测试。<br/>
/// 表现层数据（<c>DoneCount</c> / <c>Pocketed</c> 等）历来就是可改的，
/// 钱包 / 物品这种"英雄携带的资源"按同款放这里，<b>域边界不动</b>。</para>
///
/// <para><b>不可变 + I15</b>：<see cref="Domain.Items.Wallet"/> 与
/// <see cref="Domain.Items.Inventory"/> 自身已守住 —— 这里只<b>放一个字段
/// 与一组简洁的助手方法</b>，<b>不重复守门</b>。所有"不变量防线"集中在
/// <c>Domain/Items/</c>，表现层只是一根调用线。</para>
/// </remarks>
public sealed partial class HeroAgent
{
    /// <summary>该英雄的资金 / 材料包。</summary>
    public Wallet Wallet { get; private set; } = new Wallet();

    /// <summary>该英雄的物品背包。</summary>
    public Inventory Bag { get; private set; } = new Inventory();

    /// <summary>
    /// 替换钱包。<b>仅供读档（M7）</b>。
    /// </summary>
    /// <remarks>
    /// 调用方应<b>从已经过 <c>SaveValidator.Validate</c> 的存档里拿值</b>，不再校验。
    /// 运行中调它就是绕过 I15 —— 不在测试纪律下做事的人会这么做。
    /// </remarks>
    internal void RestoreWallet(Wallet wallet)
    {
        Wallet = wallet ?? throw new ArgumentNullException(nameof(wallet));
    }

    /// <summary>替换物品背包。<b>仅供读档（M7）</b>。</summary>
    internal void RestoreBag(Inventory bag)
    {
        Bag = bag ?? throw new ArgumentNullException(nameof(bag));
    }

    /// <summary>
    /// 给该英雄加资源（钱 / 材料）。
    /// </summary>
    /// <remarks>
    /// <b>不可变</b>：<see cref="Domain.Items.Wallet.TryAdd"/> 返新 Wallet，
    /// 这里只换一个引用。<c>HeroAgent</c> 自身是 mutable，<b>但钱包的不可变性
    /// 由 Wallet 守住</b>，这里只是引用替换。</para>
    /// </remarks>
    /// <returns>成功？</returns>
    public bool TryAddResource(ResourceId id, long amount)
    {
        var next = Wallet.TryAdd(id, amount);
        if (next is null) return false;
        Wallet = next;
        return true;
    }

    /// <summary>扣资源。</summary>
    public bool TrySpendResource(ResourceId id, long amount)
    {
        var next = Wallet.TrySpend(id, amount);
        if (next is null) return false;
        Wallet = next;
        return true;
    }

    /// <summary>加物品。</summary>
    public bool TryAddItem(ItemId id, long delta)
    {
        var next = Bag.TryAdd(id, delta);
        if (next is null) return false;
        Bag = next;
        return true;
    }

    /// <summary>扣物品。</summary>
    public bool TryRemoveItem(ClaudeCodeGameStudios.Domain.Items.ItemId id, long delta)
    {
        var next = Bag.TryRemove(id, delta);
        if (next is null) return false;
        Bag = next;
        return true;
    }

    /// <summary>
    /// 把 <see cref="Trade.Apply"/> 返回的 4 个新引用<b>写回</b>本英雄的字段。
    /// </summary>
    /// <remarks>
    /// <para><b>职责：薄薄一层壳</b>。<c>Trade.Apply</c> 是纯函数：
    /// 算新值、不改任何字段。本方法把新值写回 <see cref="Wallet"/> 与
    /// <see cref="Bag"/> 字段 —— <b>一句替换</b>。</para>
    ///
    /// <para><b>不调 <c>Trade</c> 自身</b>：本方法<b>只</b>负责写回已算好的引用。
    /// 真做结算的是上层（摆摊 / 摆摊 - 交易合用）——
    /// 它先调 <c>Trade.Apply</c>，再把四个返回值交给 <c>ApplyTradeResult</c>。
    /// 两步分开，单元测试各管一段（薄壳的"写回是否真替换字段"）。</para>
    /// </remarks>
    /// <param name="newWallet">新钱包。</param>
    /// <param name="newBag">新背包。</param>
    public void ApplyTradeResult(Wallet newWallet, Inventory newBag)
    {
        if (newWallet is null) throw new ArgumentNullException(nameof(newWallet));
        if (newBag is null) throw new ArgumentNullException(nameof(newBag));
        Wallet = newWallet;
        Bag = newBag;
    }
}
