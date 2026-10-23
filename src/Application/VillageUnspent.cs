using System;

namespace ClaudeCodeGameStudios.Application;

/// <summary>
/// 村庄「<b>可花却没花</b>」的钱（D-110）—— 采集器不召唤 ⇒ 钱会囤，这个缺口要看得见。
/// </summary>
/// <remarks>
/// <para><b>口径</b> = 村库现金 + 村庄资产 − <b>下一级升级要留的钱</b>。
/// 留着的钱不算「没花」—— 它有明确的用处（升级村庄）。</para>
///
/// <para>⚠️ <b>为什么住在应用层</b>：这段口径写在 <c>Main</c>（表现层）的话，
/// 单测碰不到 ⇒ 改坏了照样绿 ⇒ 搬到这里，<b>生产代码与守卫调同一份</b>
/// （与 D-083 的 <c>BuildingPersistence</c>、D-099 的 <c>TierBuildingService</c> 同款理由）。</para>
/// </remarks>
public static class VillageUnspent
{
    /// <summary>
    /// 村庄「可花却没花」的钱。
    /// </summary>
    /// <param name="villageGold">村库现金。</param>
    /// <param name="asset">村庄资产（已经变成建筑、还能再花的那部分）。</param>
    /// <param name="reserveGold">下一级村庄升级要留的钱（0 = 已满级）。</param>
    /// <returns>可花未花的金额（**永远 ≥ 0**）。</returns>
    /// <remarks>
    /// ⚠️ <b>为什么下限是 0</b>：留的钱可能超过现有现金（村子刚被升级掏空）
    /// ⇒ 算出来的差是负数 ⇒ 那不是「负的未花出去的钱」，只是<b>还没攒够</b> ⇒ 记 0。
    /// </remarks>
    public static long Amount(long villageGold, long asset, long reserveGold)
    {
        long value = villageGold + asset - reserveGold;
        return value > 0 ? value : 0;
    }
}
