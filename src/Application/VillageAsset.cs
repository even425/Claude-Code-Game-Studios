using ClaudeCodeGameStudios.Domain.Village;

namespace ClaudeCodeGameStudios.Application;

/// <summary>
/// <b>村庄资产</b> —— 花掉但<b>没有烧掉</b>的钱（它变成了村里的建筑，D-090）。
/// </summary>
/// <remarks>
/// <para>⚠️ <b>为什么要有这个科目</b>：钱有三种归宿 ——
/// <list type="number">
/// <item><b>还在某个钱包里</b>（英雄的钱、村库的钱）⇒ 守恒律左边；</item>
/// <item><b>被烧掉了</b>（商队那 90%）⇒ 记「累计销毁」；</item>
/// <item><b>变成了东西</b>（升级、盖房子）⇒ <b>记成村庄资产</b>（仍在世界上、还能再花）。</item>
/// </list>
/// ⇒ 漏掉第三种 ⇒ 钱**凭空消失** ⇒ 守恒律对不上。</para>
///
/// <para>⚠️ <b>D-108 补上的是「盖房子」那一条</b>：升级的钱一直有记账
/// （D-090），但<b>玩家盖围栏 / 试炼塔花掉的钱此前没有任何地方记账</b>
/// ⇒ 玩家一盖房子，守恒律就破。<b>这是真缺陷</b>，与采集器无关
/// （只是采集器从不盖房子 ⇒ 这个缺陷一直没被看见）。</para>
/// </remarks>
public static class VillageAsset
{
    /// <summary>村庄资产这个**资源标识**（在村庄的资源池里）。</summary>
    public static readonly ResourceId Asset = new("村庄资产");
}
