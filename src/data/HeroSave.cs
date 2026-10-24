using System.Collections.Generic;

namespace ClaudeCodeGameStudios.Data;

/// <summary>
/// <b>英雄相关的存档类型</b>（D-113：从 <c>SaveGame.cs</c> 拆出来 ——
/// 那个文件已经顶到 UT-01 的 1200 行上限，每加一个字段就会越线）。
/// </summary>
/// <remarks>
/// ⚠️ **只是搬家，规则一个字没改** ⇒ 既有调用点（<c>Main.Save.cs</c>、
/// 校验器、守卫）<b>一个字都不用改</b> ⇒ 命名空间相同即可。
/// </remarks>
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

    /// <summary>
    /// 该英雄从试炼塔拿到的<b>永久生命加成</b>（D-074）。<b>0,10 新增</b>（D-104）。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么必须存</b>：这个加成的定义就是「<b>永久</b>」——
    /// 玩家打赢了试炼塔，换来的加生命要<b>一直</b>跟着这个人。
    /// 不存它 ⇒ 读档后加成归零 ⇒ 玩家攒下来的强度<b>凭空蒸发</b>，
    /// 而游戏<b>不会报任何错</b>（只是「这个人好像变弱了」）。</para>
    /// <para><b>为什么是英雄个体</b>：战斗属性按个人换算，
    /// 加成跟着人走而不是跟着塔走（塔只是入口）。</para>
    /// <para><b>合法范围</b>：≥ 0。加成只会<b>增加</b>（<c>+=</c>），
    /// 负数没有任何合法来源 ⇒ 只可能是存档被改坏 ⇒ 整份拒绝。</para>
    /// </remarks>
    public int TrialTowerHealthBonus { get; set; }

    /// <summary>
    /// 该英雄<b>上次试炼</b>是第几天（0 = 从未试过）。<b>0.11 新增</b>（D-113）。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么必须入档</b>：试炼塔<b>每天限一次</b>（D-113）
    /// ⇒ 这一项就是那条限制的「今天已经打过」的凭据
    /// ⇒ <b>不存它 ⇒ 读档后那条限制就白设了</b>（读一次档就能多刷一次）
    /// —— 与 D-104「永久加成不入档」是<b>同一族</b>的缺陷。</para>
    /// <para><b>合法范围</b>：≥ 0（0 = 从未试过；负数只可能是存档被改坏）。</para>
    /// </remarks>
    public int LastTrialDay { get; set; }

    // ── 装备（D-119，存档 0.12 新增）──

    /// <summary>
    /// 该英雄<b>穿着的武器</b>的物品 Id（空串 = 没穿武器）。<b>0.12 新增</b>（D-119）。
    /// </summary>
    /// <remarks>
    /// ⚠️ <b>为什么必须入档</b>：基线 §15 明列「加载时要避免<b>装备丢失或重复</b>」
    /// ⇒ 不存 ⇒ 读档后装备<b>凭空消失</b>，而游戏<b>不报任何错</b>
    /// （这个人只是「忽然变弱了」）—— 与 D-104「永久加成不入档」同一族。</remarks>
    public string EquippedWeaponId { get; set; } = "";

    /// <summary>
    /// 武器的当前耐久（0 = 坏了 ⇒ 加成不生效）。<b>0.12 新增</b>（D-119）。
    /// </summary>
    /// <remarks>
    /// ⚠️ <b>合法范围</b>：0..该装备的耐久上限（超出 ⇒ 存档被改坏 ⇒ 整份拒绝）。</remarks>
    public int WeaponDurability { get; set; }

    /// <summary>
    /// 该英雄<b>穿着的衣服</b>的物品 Id（空串 = 没穿衣服）。<b>0.12 新增</b>（D-119）。
    /// </summary>
    public string EquippedArmorId { get; set; } = "";

    /// <summary>
    /// 衣服的当前耐久（0 = 坏了 ⇒ 加成不生效）。<b>0.12 新增</b>（D-119）。
    /// </summary>
    public int ArmorDurability { get; set; }
}

/// <summary>
/// 存档里的<b>一家商店的货架</b>（D-119，存档 0.12 新增）。
/// </summary>
/// <remarks>
/// ⚠️ <b>为什么库存也要入档</b>：它是村庄的状态 ⇒ 不存 ⇒ 读档后货架空了
/// ⇒ 玩家刚买到的装备「凭空买不到」⇒ 也是「装备丢失或重复」那一族。</remarks>
public sealed class ShopStockSave
{
    /// <summary>店铺（建筑）标识。</summary>
    public string ShopId { get; set; } = "";

    /// <summary>货架上的东西（物品 Id + 数量）。</summary>
    public List<ShopItemSave> Items { get; set; } = new();
}

/// <summary>货架上的一件东西（D-119）。</summary>
public sealed record ShopItemSave
{
    /// <summary>物品 Id（必须是已登记的装备）。</summary>
    public string ItemId { get; set; } = "";

    /// <summary>数量（必须 ≥ 0）。</summary>
    public long Count { get; set; }
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
