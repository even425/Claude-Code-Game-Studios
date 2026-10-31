using Godot;
using System;
using System.Globalization;
using System.Linq;

/// <summary>
/// 主场景的<b>村庄日常开销</b>接线（D-125）。
///
/// <para>⚠️ <b>规则在 <c>VillageUpkeepService</c></b>（应用层纯函数）⇒ 本文件只负责
/// 「什么时候扣、扣多少」⇒ 采集器与真实游戏走<b>同一份</b>。</para>
///
/// <para>⚠️ <b>数值是待拍板的建议值</b>：默认 <b>0</b>（不扣 ⇒ 现状不变），
/// 可用 <c>--upkeep=N</c> 覆盖（用于标定实验）。</para>
/// </summary>
public partial class Main : Node2D
{
    /// <summary>每天扣多少日常开销（0 = 不扣）；可用 <c>--upkeep=</c> 覆盖。</summary>
    private long? _upkeepOverride;   // 命令行 `--upkeep=N` 的覆盖值（null = 按层级取）

    /// <summary>解析 <c>--upkeep=N</c>（每天的村庄日常开销，金）。</summary>
    /// <returns>开销金额（0 = 不扣）。</returns>
    private static long? ParseUpkeepPerDay()
    {
        var arg = OS.GetCmdlineUserArgs()
            .FirstOrDefault(a => a.StartsWith("--upkeep=", StringComparison.Ordinal));
        if (arg is null) return null;   // 没给 ⇒ 按村庄层级取值（D-130）

        var raw = arg.Substring("--upkeep=".Length);
        if (!long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out long value) || value < 0)
        {
            GD.Print($"UPKEEP 参数无效：「{raw}」。每天的日常开销必须是非负整数（金）⇒ 这次不收。");
            return 0L;
        }

        return value;
    }

    /// <summary>
    /// 收一天的村庄日常开销（<b>只扣闲钱</b>：不动下一级村庄升级的储备）。
    /// </summary>
    private void VillageUpkeepRound()
    {
        // ⚠️ **D-130：默认按村庄层级取**（村庄越大维护越贵）⇒ 显式给了 `--upkeep=N` 才覆盖。
        long upkeep = _upkeepOverride ?? ClaudeCodeGameStudios.Application.VillageUpkeepService.UpkeepForTier(_tier);
        if (upkeep <= 0) return;

        long reserve = _tier < ClaudeCodeGameStudios.Application.VillageUpgradeService.MaxTier
            ? _upgradeService.CostFor(_tier).Gold
            : 0L;

        long charged = ClaudeCodeGameStudios.Application.VillageUpkeepService.Charge(
            _villageModel,
            ClaudeCodeGameStudios.Application.Stall.StallService.Gold,
            reserve,
            upkeep);

        if (charged > 0)
        {
            // ⚠️ **记账在服务内部做**（`VillageUpkeepService.Charge`）
            // ⇒ 调用方不会忘 ⇒ 守恒律不会破。

            GD.Print($"UPKEEP 村庄日常开销 {charged} 金（村库余额 {VillageGold}）");
        }
    }
}
