using System;
using ClaudeCodeGameStudios.Domain.Heroes;
using ClaudeCodeGameStudios.Domain.Items;

namespace ClaudeCodeGameStudios.Domain.Stall;

/// <summary>
/// 一个英雄的摊位。<b>不可变</b>（与 <see cref="Wallet"/> / <see cref="Inventory"/> / <see cref="PriceBook"/> 一致）。
/// </summary>
/// <remarks>
/// <para><b>单一物品</b>：D-023 拍定"一人一摊 + 一摊一物"。本类只管 <b>一种</b>商品的库存与状态。
/// 英雄要卖多物 → 关旧摊开新摊。</para>
///
/// <para><b>库存从英雄背包挪入</b>：P2-D06 拍定 <c>PlaceStall</c> 时把指定数量从英雄 <c>Bag</c> 移入
/// <see cref="Stock"/>。售罄 = <see cref="Stock"/> = 0 → <see cref="Status"/> = <see cref="StallStatus.SoldOut"/>。
/// 售罄后英雄可选择 <c>CloseStall</c> 把剩余库存（= 0）丢弃，<b>或</b>保留 SoldOut 摊位在地图上
/// （决定权在应用层 / 表现层 —— 本类不强制）。</para>
///
/// <para><b>不可变</b>：本类<b>无</b> setter。<see cref="StallService"/> 持有摊位目录，
/// 修改 = 用新 <see cref="Stall"/> 实例替换旧条目。
/// 与 <see cref="Inventory"/> 同款"返回新对象"模式。</para>
///
/// <para><b>构造期防线</b>（D-018 守门规矩）：</para>
/// <list type="bullet">
/// <item>Owner.HeroId 不得为空（<see cref="string.IsNullOrEmpty"/> 验）</item>
/// <item><see cref="ItemId"/> 不得为空</item>
/// <item><see cref="Stock"/> ≥ 1（开摆时至少挪入 1 份；0 = 应走 SoldOut 路径而非开新摊）</item>
/// <item><see cref="UnitPrice"/> ≥ 0（与 <see cref="ClaudeCodeGameStudios.Domain.Trade.Trade"/> 一致：
/// 0 = 白送，非负才合法）</item>
/// <item><see cref="SimTime"/> 非负有限</item>
/// </list>
/// </remarks>
public sealed class Stall
{
    /// <summary>摊主（持有者）。</summary>
    public HeroId Owner { get; }

    /// <summary>售卖的商品（一种摊位一种商品）。</summary>
    public ItemId ItemId { get; }

    /// <summary>当前库存（售出递减，永不为负）。</summary>
    public long Stock { get; }

    /// <summary>单价。开摊时锁定，售出过程中<b>不</b>变 —— 同一摊位同次会话内不变价。</summary>
    public long UnitPrice { get; }

    /// <summary>摊位状态（Open / SoldOut / Closed，单向迁移）。</summary>
    public StallStatus Status { get; }

    /// <summary>开摊时间（模拟秒）。</summary>
    public double SimTime { get; }

    /// <summary>M5 / D-033：摊位所在格位 X。v1 简化 = 1×1 占用。</summary>
    public int GridX { get; }

    /// <summary>M5 / D-033：摊位所在格位 Y。</summary>
    public int GridY { get; }

    /// <summary>构造一个摊位。</summary>
    /// <remarks>
    /// <b>构造期防线</b>见类注释。所有不变量在此一次性设下，之后<b>无</b> setter。
    /// </remarks>
    /// <exception cref="ArgumentException">Owner / ItemId 空；Stock = 0；UnitPrice 负；Status 越界。</exception>
    /// <exception cref="ArgumentOutOfRangeException">Stock 负；UnitPrice 负；SimTime 越界。</exception>
    public Stall(HeroId owner, ItemId itemId, long stock, long unitPrice, StallStatus status, double simTime,
        int gridX = 0, int gridY = 0)
    {
        if (string.IsNullOrEmpty(owner.Value))
        {
            throw new ArgumentException("摊主 HeroId 不得为空。", nameof(owner));
        }
        if (string.IsNullOrEmpty(itemId.Value))
        {
            throw new ArgumentException("商品 Id 不得为空。", nameof(itemId));
        }
        if (stock < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(stock), stock, "开摊时库存必须 ≥ 1（售罄走另一条路）。");
        }
        if (unitPrice < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(unitPrice), unitPrice, "单价不得为负（0 = 白送，合法）。");
        }
        if (simTime < 0.0 || double.IsNaN(simTime) || double.IsInfinity(simTime))
        {
            throw new ArgumentOutOfRangeException(
                nameof(simTime), simTime, "开摊时间必须是非负有限数。");
        }
        if (status != StallStatus.Open && status != StallStatus.SoldOut && status != StallStatus.Closed)
        {
            throw new ArgumentException($"未知摊位状态：{(int)status}。", nameof(status));
        }

        Owner = owner;
        ItemId = itemId;
        Stock = stock;
        UnitPrice = unitPrice;
        Status = status;
        SimTime = simTime;
        GridX = gridX;
        GridY = gridY;
    }

    /// <summary>售出 <paramref name="qty"/> 份后，<b>返新 Stall</b>；售罄时自动变 SoldOut。</summary>
    /// <remarks>
    /// <b>纯函数</b>：不持任何引用，只算新值。售罄 = <c>Stock - qty ≤ 0</c> → 新摊位 <see cref="StallStatus.SoldOut"/>。
    /// </remarks>
    /// <param name="qty">售出数量（≥ 1）。</param>
    /// <returns>新 <see cref="Stall"/>，库存减 <paramref name="qty"/>，售罄时状态自动转 SoldOut。</returns>
    /// <exception cref="ArgumentOutOfRangeException">qty &lt; 1 或 qty &gt; Stock。</exception>
    public Stall AfterSale(long qty)
    {
        if (qty < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(qty), qty, "售出数量必须 ≥ 1。");
        }
        if (qty > Stock)
        {
            throw new ArgumentOutOfRangeException(
                nameof(qty), qty, $"售出数量超过库存（{Stock}）。");
        }

        long newStock = Stock - qty;
        var newStatus = newStock == 0 ? StallStatus.SoldOut : Status;
        return CreateInternal(Owner, ItemId, newStock, UnitPrice, newStatus, SimTime, GridX, GridY);
    }

    /// <summary>收摊：状态变 Closed，<b>不</b>改 Stock（= 0 时也会收）。</summary>
    public Stall Closed()
    {
        if (Status == StallStatus.Closed) return this;   // 已收 = 幂等返回
        return CreateInternal(Owner, ItemId, Stock, UnitPrice, StallStatus.Closed, SimTime, GridX, GridY);
    }

    /// <summary>
    /// 内部工厂：与公开构造同款防线，<b>额外</b>允许 Stock = 0（售罄态）。
    /// </summary>
    /// <remarks>
    /// 公开构造拒 Stock &lt; 1 是因为"开摆"必有货；售罄/收摊是<b>派生</b>状态，
    /// 库存归 0 是合法结果。改用内部工厂一次过构造 —— 不为派生状态开放"外部 0 stock"路径。
    /// </remarks>
    private static Stall CreateInternal(
        HeroId owner, ItemId itemId, long stock, long unitPrice,
        StallStatus status, double simTime, int gridX = 0, int gridY = 0)
    {
        // 防线（与公开构造同款，但允许 stock=0）
        if (string.IsNullOrEmpty(owner.Value))
            throw new ArgumentException("摊主 HeroId 不得为空。", nameof(owner));
        if (string.IsNullOrEmpty(itemId.Value))
            throw new ArgumentException("商品 Id 不得为空。", nameof(itemId));
        if (stock < 0)
            throw new ArgumentOutOfRangeException(nameof(stock), stock, "库存不得为负。");
        if (unitPrice < 0)
            throw new ArgumentOutOfRangeException(
                nameof(unitPrice), unitPrice, "单价不得为负（0 = 白送，合法）。");
        if (simTime < 0.0 || double.IsNaN(simTime) || double.IsInfinity(simTime))
            throw new ArgumentOutOfRangeException(
                nameof(simTime), simTime, "开摊时间必须是非负有限数。");

        return new Stall(owner, itemId, stock, unitPrice, status, simTime, gridX, gridY, skipStockCheck: true);
    }

    /// <summary>仅供内部工厂调用 —— 跳过 Stock &lt; 1 检查。</summary>
    private Stall(
        HeroId owner, ItemId itemId, long stock, long unitPrice,
        StallStatus status, double simTime, int gridX, int gridY, bool skipStockCheck)
    {
        if (!skipStockCheck && stock < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(stock), stock, "开摊时库存必须 ≥ 1（售罄走另一条路）。");
        }
        if (unitPrice < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(unitPrice), unitPrice, "单价不得为负（0 = 白送，合法）。");
        }
        if (simTime < 0.0 || double.IsNaN(simTime) || double.IsInfinity(simTime))
        {
            throw new ArgumentOutOfRangeException(
                nameof(simTime), simTime, "开摊时间必须是非负有限数。");
        }
        if (status != StallStatus.Open && status != StallStatus.SoldOut && status != StallStatus.Closed)
        {
            throw new ArgumentException($"未知摊位状态：{(int)status}。", nameof(status));
        }
        if (string.IsNullOrEmpty(owner.Value))
        {
            throw new ArgumentException("摊主 HeroId 不得为空。", nameof(owner));
        }
        if (string.IsNullOrEmpty(itemId.Value))
        {
            throw new ArgumentException("商品 Id 不得为空。", nameof(itemId));
        }
        Owner = owner;
        ItemId = itemId;
        Stock = stock;
        UnitPrice = unitPrice;
        Status = status;
        SimTime = simTime;
        GridX = gridX;
        GridY = gridY;
    }
}
