namespace ClaudeCodeGameStudios.Domain.Items;

/// <summary>
/// 物品的<b>稀有度</b>（R-05 / T-007）。五级。
/// </summary>
/// <remarks>
/// <para><b>为什么稀有度是独立概念，不塞进 ItemType</b>：
/// 「武器 / 材料 / 消耗品」是<b>类型</b>，「普通 / 史诗」是<b>稀有度</b> ——
/// 一个石料和一个传说武器可以都是"材料/装备类"，但稀有度天差地别。
/// 两个轴混在一起，改一个要动另一个。</para>
///
/// <para><b>稀有度不自动决定价格</b>：基础价格走独立的配置
/// （<see cref="PriceBook"/>）。稀有度是「这东西有多罕见」，
/// 价格是「这东西值多少」—— 相关，但不是同一件事、也不是自动推导。
/// 自动推导会让「同稀有度必同价」这种错误规则悄悄长出来。</para>
///
/// <para><b>稀有度不自动决定属性</b>：品质加成属另一套规则，当前不做。</para>
///
/// <para><b>首版只有五个值，且不要求每级都有人用</b>：
/// 落表时按实际物品标注，<b>不为填满五级而凭空造物品</b>。</para>
/// </remarks>
public enum ItemRarity
{
    /// <summary>普通 —— 基础材料与消耗品，承担日常交易与基础供给。</summary>
    Common = 0,

    /// <summary>优秀 —— 品质更好的材料或装备，中等价值。</summary>
    Uncommon = 1,

    /// <summary>稀有 —— 中后期有价值的东西，形成中期追求。</summary>
    Rare = 2,

    /// <summary>史诗 —— 低频高价值，可成为高阶副本的追求目标。</summary>
    Epic = 3,

    /// <summary>传说 —— 极低频的特殊物品，面向长期目标。</summary>
    Legendary = 4,
}
