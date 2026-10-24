using System;
using System.Collections.Generic;
using System.Linq;

namespace ClaudeCodeGameStudios.Domain.Items;

/// <summary>装备戴在哪个槽位（D-119）。</summary>
public enum EquipmentSlot
{
    /// <summary>不是装备。</summary>
    None = 0,

    /// <summary>武器槽（攻击向）。</summary>
    Weapon = 1,

    /// <summary>衣服槽（防御向）。</summary>
    Armor = 2,
}

/// <summary>
/// 一件<b>装备</b>的定义（D-119）—— 槽位 / 加成 / 耐久 / 售价 / 修理费。
/// </summary>
/// <remarks>
/// <para>⚠️ <b>为什么不塞进 <see cref="ItemDefinition"/></b>：那是一张「所有物品」的表
/// （石料 / 木料 / 布匹…），而装备多了<b>槽位、加成、耐久</b>三组只属于装备的字段
/// ⇒ 塞进去 ⇒ 每件普通物品都要填三个「无」，而且<b>普通物品会被误当成装备</b>。
/// ⇒ 分成两张表：<see cref="ItemCatalog"/> 管「是什么」，本表管「能不能穿、能加什么」。</para>
///
/// <para>⚠️ <b>数值是建议值</b>（开发者拍板「耐用、要有修理」，
/// 具体数值允许调）：见 <c>07</c> 的 D-119 与参数台账。</para>
/// </remarks>
public sealed class EquipmentDefinition
{
    /// <summary>机器名（与 <see cref="ItemDefinition.Id"/> 同一个 Id ⇒ 物品表里也登记一份）。</summary>
    public ItemId Id { get; }

    /// <summary>显示名。</summary>
    public string DisplayName { get; }

    /// <summary>戴在哪个槽位。</summary>
    public EquipmentSlot Slot { get; }

    /// <summary>攻击加成（耐久为 0 时<b>不生效</b>）。</summary>
    public int AttackBonus { get; }

    /// <summary>防御加成（耐久为 0 时<b>不生效</b>）。</summary>
    public int DefenseBonus { get; }

    /// <summary>耐久上限（修满时的耐久值）。</summary>
    public int DurabilityMax { get; }

    /// <summary>售价（英雄买它要付的钱，进村库）。</summary>
    public long Price { get; }

    /// <summary>修理费（把耐久修满要付的钱，进村库）。</summary>
    public long RepairCost { get; }

    /// <summary>构造一个装备定义。</summary>
    /// <param name="id">机器名；不得为空。</param>
    /// <param name="displayName">显示名。</param>
    /// <param name="slot">槽位。</param>
    /// <param name="attackBonus">攻击加成。</param>
    /// <param name="defenseBonus">防御加成。</param>
    /// <param name="durabilityMax">耐久上限；必须 &gt; 0。</param>
    /// <param name="price">售价；不得为负。</param>
    /// <param name="repairCost">修理费；不得为负。</param>
    /// <exception cref="ArgumentException">Id 为空。</exception>
    /// <exception cref="ArgumentOutOfRangeException">耐久上限不为正，或价格 / 修理费为负。</exception>
    public EquipmentDefinition(
        ItemId id,
        string displayName,
        EquipmentSlot slot,
        int AttackBonus,
        int DefenseBonus,
        int DurabilityMax,
        long Price,
        long RepairCost)
    {
        if (string.IsNullOrEmpty(id.Value))
        {
            throw new ArgumentException("装备 Id 不得为空。", nameof(id));
        }

        if (slot == EquipmentSlot.None)
        {
            throw new ArgumentOutOfRangeException(nameof(slot), slot, "装备必须有槽位。");
        }

        if (DurabilityMax <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(DurabilityMax), DurabilityMax, "耐久上限必须为正。");
        }

        if (Price < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(Price), Price, "售价不得为负。");
        }

        if (RepairCost < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(RepairCost), RepairCost, "修理费不得为负。");
        }

        Id = id;
        DisplayName = displayName ?? string.Empty;
        Slot = slot;
        this.AttackBonus = AttackBonus;
        this.DefenseBonus = DefenseBonus;
        this.DurabilityMax = DurabilityMax;
        this.Price = Price;
        this.RepairCost = RepairCost;
    }

    /// <summary>这个槽位是不是武器。</summary>
    public bool IsWeapon => Slot == EquipmentSlot.Weapon;
}

/// <summary>
/// <b>装备注册表</b>（D-119）—— 谁是装备、它加什么、能穿多久。
/// </summary>
/// <remarks>
/// <para>⚠️ <b>它与 <see cref="ItemCatalog"/> 是两张表，但 Id 必须一一对应</b>：
/// 装备也要登记进物品表（因为发奖 / 摆摊那套按物品走）⇒
/// 「物品表有、价格表没有」会漏价，「价格表有、物品表没有」发不出去 ⇒
/// 守卫 <c>EquipmentCatalogTests</c> 专门盯这两条。</para>
/// </remarks>
public sealed class EquipmentCatalog
{
    private readonly Dictionary<ItemId, EquipmentDefinition> _definitions;

    /// <summary>构造一张装备注册表。</summary>
    /// <param name="definitions">装备定义清单；Id 不得重复。</param>
    /// <exception cref="ArgumentNullException"><paramref name="definitions"/> 为 null。</exception>
    /// <exception cref="InvalidOperationException">两个定义用了同一个 Id。</exception>
    public EquipmentCatalog(IEnumerable<EquipmentDefinition> definitions)
    {
        if (definitions is null) throw new ArgumentNullException(nameof(definitions));

        _definitions = new Dictionary<ItemId, EquipmentDefinition>(ItemIdComparer.Comparer);
        foreach (var def in definitions)
        {
            if (def is null) continue;
            if (_definitions.ContainsKey(def.Id))
            {
                throw new InvalidOperationException(
                    $"装备 {def.Id.Value} 重复登记：同一个 Id 只能有一条定义。");
            }

            _definitions[def.Id] = def;
        }
    }

    /// <summary>空注册表。</summary>
    public static EquipmentCatalog Empty { get; } = new(Array.Empty<EquipmentDefinition>());

    /// <summary>
    /// 默认注册表（D-119 的四件装备）。
    /// </summary>
    /// <remarks>
    /// ⚠️ <b>数值全是建议值</b>（开发者拍板的是「武器店 + 服装店 + 自主购买 + 耐久损耗 +
    /// 铁匠铺修理」这套<b>结构</b>，数值允许调）。
    /// <br>⚠️ <b>加成的量级对着战斗基准定的</b>：首领档两人队胜率 86.7%（D-072）
    /// ⇒ +5 攻击约等于多打一刀，<b>不会</b>把难度掀翻；重测交给你拍板。
    /// </remarks>
    public static EquipmentCatalog Default { get; } = new(new[]
    {
        // 武器（攻击向）
        new EquipmentDefinition(new ItemId("石剑"), "石剑", EquipmentSlot.Weapon,
            AttackBonus: 5, DefenseBonus: 0, DurabilityMax: 20, Price: 30L, RepairCost: 6L),
        new EquipmentDefinition(new ItemId("猎弓"), "猎弓", EquipmentSlot.Weapon,
            AttackBonus: 8, DefenseBonus: 0, DurabilityMax: 14, Price: 45L, RepairCost: 9L),
        // 服装（防御向）
        new EquipmentDefinition(new ItemId("皮甲"), "皮甲", EquipmentSlot.Armor,
            AttackBonus: 0, DefenseBonus: 5, DurabilityMax: 18, Price: 25L, RepairCost: 5L),
        new EquipmentDefinition(new ItemId("锁甲"), "锁甲", EquipmentSlot.Armor,
            AttackBonus: 0, DefenseBonus: 8, DurabilityMax: 22, Price: 40L, RepairCost: 8L),
    });

    /// <summary>全部定义（按 Id 排序，稳定顺序便于比对）。</summary>
    public IReadOnlyList<EquipmentDefinition> All =>
        _definitions.Values.OrderBy(d => d.Id.Value, StringComparer.Ordinal).ToArray();

    /// <summary>登记了多少件装备。</summary>
    public int Count => _definitions.Count;

    /// <summary>查某件装备的定义；没登记返回 null。</summary>
    /// <param name="id">物品标识。</param>
    /// <returns>定义；未登记时为 null。</returns>
    public EquipmentDefinition? Find(ItemId id)
    {
        if (string.IsNullOrEmpty(id.Value)) return null;
        return _definitions.TryGetValue(id, out var def) ? def : null;
    }

    /// <summary>某个物品是不是装备。</summary>
    /// <param name="id">物品标识。</param>
    /// <returns>是装备为 true。</returns>
    public bool IsEquipment(ItemId id) => Find(id) is not null;

    /// <summary>某个槽位上的所有装备（按 Id 排序）。</summary>
    /// <param name="slot">槽位。</param>
    /// <returns>该槽位的全部装备。</returns>
    public IReadOnlyList<EquipmentDefinition> ForSlot(EquipmentSlot slot)
        => All.Where(d => d.Slot == slot).ToArray();
}
