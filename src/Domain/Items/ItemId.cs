namespace ClaudeCodeGameStudios.Domain.Items;

/// <summary>
/// 物品标识。决定哪些物品是"同一种"——商品、库存、摊位、交易都按 Id 引用。
/// </summary>
/// <remarks>
/// <para><b>Id 不得为空</b>（与 <c>HeroId</c> / <c>ResourceId</c> 一致）。</para>
/// <para><b>Id 与杂谈里的"资源名"是<b>两套</b>东西</b>（D-018 修过的坑）：
/// 杂谈里写"X 份石料"用的是"显示文本"，而物品 Id 是给库存/交易用的机器名。
/// 第一阶段它们被混用，结果奖励进了「铁料」桶、玩家读「材料」桶，材料纹丝不动。
/// 这类「同名异义」在第二阶段<b>仍要避开</b>：摆摊时挂的
/// <c>ItemDefinition.DisplayName</c> 才是杂谈引用的来源，<c>ItemId.Value</c> 只用于库存。</para>
/// </remarks>
public readonly record struct ItemId(string Value)
{
    public static ItemId None => new(string.Empty);

    public override string ToString() => string.IsNullOrEmpty(Value) ? "(无)" : Value;
}
