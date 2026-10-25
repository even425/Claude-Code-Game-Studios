using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using ClaudeCodeGameStudios.Application;
using ClaudeCodeGameStudios.Domain.Combat;
using ClaudeCodeGameStudios.Domain.Items;

/// <summary>
/// <b>副本胜率扫描</b>（D-123）—— 装备上线后，**难度有没有被悄悄推高**。
/// </summary>
/// <remarks>
/// <para>⚠️ <b>为什么要有这个</b>：D-119 给英雄加了装备加成（+5 攻 / +5 防）
/// ⇒ 战斗属性变了 ⇒ **难度基准（首领档 86.7%，D-072）就作废了**
/// ⇒ 若没人复测，难度会<b>悄悄漂</b>（玩家越打越轻松，而表上写着 86.7%）。</para>
///
/// <para>⚠️ <b>口径</b>：枚举 <b>12 种英雄属性</b>（4 性格 × 3 投入度），
/// 两人队取<b>无序组合</b>（66 种）⇒ 每档怪物跑一遍，<b>裸装</b>与<b>全装备</b>各算一次。
/// ⇒ 输出的是<b>胜率表</b>（不是断言）⇒ 数值由开发者拍板。</para>
///
/// <para>⚠️ <b>为什么不写成断言</b>：胜率是<b>要开发者拍板的数值</b>，
/// 写成断言就等于我替开发者定了标准 ⇒ 只做测量。</para>
/// </remarks>
public partial class Main : Node2D
{
    /// <summary>
    /// <b>副本专用</b>的战斗解算器（D-124）—— 副本难度表与其它遭遇**分开**。
    /// </summary>
    /// <remarks>
    /// ⚠️ **为什么要分开**：副本、试炼塔、狩猎、兽潮**共用同一张怪物表**
    /// ⇒ 把副本难度抬上去（装备 = 通关许可证）会**一起**把
    /// 「试炼塔守卫 / 狼 / 兽潮」弄坏 ⇒ 那些遭遇的难度**不该跟着副本走**。
    /// <br>⚠️ **为什么住在这个文件**：`Main.cs` 已经顶到 UT-01 的 1200 行上限。
    /// </remarks>
    private ClaudeCodeGameStudios.Application.FormulaCombat _dungeonCombat = null!;

    /// <summary>
    /// 造好副本专用的解算器（普通 60 / 精英 210 / 首领 180 —— D-124 实测挑出来的）。
    /// </summary>
    private void InitDungeonCombat()
    {
        _dungeonCombat = new ClaudeCodeGameStudios.Application.FormulaCombat(
            monsterStats: stage => CombatParameters.DungeonFor(stage));
    }

    /// <summary>是否已经跑过胜率扫描。</summary>
    private bool _winRateTestDone;

    /// <summary>扫描用的性格与投入度（= 游戏里能创建的全部组合）。</summary>
    private static readonly string[] WinRatePersonalities = { "莽", "傲", "随手", "贪" };

    private static readonly string[] WinRateDiligences = { "肝", "随手", "挂机" };

    /// <summary>
    /// 跑一遍胜率扫描并打印表格（<b>裸装 vs 全装备</b>）。
    /// </summary>
    private void RunWinRateSelfTest()
    {
        _winRateTestDone = true;

        var combat = new FormulaCombat();
        var catalog = ClaudeCodeGameStudios.Domain.Items.EquipmentCatalog.Default;
        var sword = catalog.Find(new ItemId("石剑"));
        var armor = catalog.Find(new ItemId("皮甲"));

        // 12 种英雄属性。
        var stats = new List<(string Name, CombatStats Stats)>();
        foreach (var p in WinRatePersonalities)
        {
            foreach (var d in WinRateDiligences)
            {
                stats.Add(($"{p}/{d}", CombatParameters.ForHero(p, d)));
            }
        }

        GD.Print("WINRATE 口径：12 种英雄属性（4 性格 × 3 投入度）× 两人队无序组合 × 三档怪物");
        GD.Print("WINRATE 基准：**副本专用难度表**（D-124）· 加成为「攻」⇒ 裸装 / +2 / +4");

        // ⚠️ 这里用**副本那张表**（`DungeonFor`）⇒ 数字就是玩家实际会遇到的那条曲线。
        for (int stage = 0; stage < 3; stage++)
        {
            var stageCombat = new FormulaCombat(
                monsterStats: s => CombatParameters.DungeonFor(s));

            var cells = new List<string>();
            foreach (int gear in new[] { 0, 2, 4 })
            {
                int wins = 0;
                int total = 0;
                for (int i = 0; i < stats.Count; i++)
                {
                    for (int j = i; j < stats.Count; j++)
                    {
                        total++;
                        var a = stats[i].Stats with { Attack = stats[i].Stats.Attack + gear };
                        var b = stats[j].Stats with { Attack = stats[j].Stats.Attack + gear };
                        if (DidWin(stageCombat, MakeParty(a, b), stage)) wins++;
                    }
                }
                cells.Add($"{100.0 * wins / total:F1}%");
            }

            GD.Print($"WINRATE {CombatParameters.MonsterNameFor(stage)} | 裸装 {cells[0]} | +2攻 {cells[1]} | +4攻 {cells[2]}");
        }

        // ── 加成档位扫描：装备加多少，难度才不会被推平 ──────────────────────
        // ⚠️ D-119 的 +5攻/+5防 把首领档从 87.2% 推到 **100%** ⇒ 副本变成必过
        // ⇒ 这里扫几档，让**开发者拍板**（数值是拍板项，不是实现细节）。
        GD.Print("WINRATE 档位扫描（攻+防 各加 X）：X | 普通 | 精英 | 首领");
        int[] bonusSteps = { 0, 1, 2, 3, 4, 5, 6, 8 };
        foreach (int step in bonusSteps)
        {
            var rates = new double[3];
            for (int stage = 0; stage < 3; stage++)
            {
                int wins = 0;
                int total = 0;
                for (int i = 0; i < stats.Count; i++)
                {
                    for (int j = i; j < stats.Count; j++)
                    {
                        total++;
                        var a = stats[i].Stats with { Attack = stats[i].Stats.Attack + step, Defense = stats[i].Stats.Defense + step };
                        var b = stats[j].Stats with { Attack = stats[j].Stats.Attack + step, Defense = stats[j].Stats.Defense + step };
                        if (DidWin(combat, MakeParty(a, b), stage)) wins++;
                    }
                }
                rates[stage] = total > 0 ? 100.0 * wins / total : 0.0;
            }
            GD.Print($"WINRATE +{step} | {rates[0]:F1}% | {rates[1]:F1}% | {rates[2]:F1}%");
        }

        // ── 二维扫描：怪物血量 × 装备加成（**只改扫描用的属性，不动生产表**）────
        // ⚠️ 上面那张表说明「调小装备加成」这条路走不通：+1 无感、+2 直接 100%
        // ⇒ 要保住难度，只能**加强怪物**（或改装备的作用方式 ⇒ 那是设计拍板项）。
        // ⇒ 这里用 `FormulaCombat(monsterStats: …)` 注入扫描用的怪物属性（**不动生产表**）。
        GD.Print("WINRATE 二维扫描（首领档）：怪物血量 | 裸装 | +2 | +4 | +5 | +6");
        int[] healthSteps = { 120, 140, 160, 180, 200 };
        int[] gearSteps = { 0, 2, 4, 5, 6 };
        foreach (int hp in healthSteps)
        {
            var scanCombat = new FormulaCombat(
                monsterStats: stage => CombatParameters.MonsterFor(stage) with { Health = hp });

            var cells = new List<string>();
            foreach (int gear in gearSteps)
            {
                int wins = 0;
                int total = 0;
                for (int i = 0; i < stats.Count; i++)
                {
                    for (int j = i; j < stats.Count; j++)
                    {
                        total++;
                        var a = stats[i].Stats with { Attack = stats[i].Stats.Attack + gear, Defense = stats[i].Stats.Defense + gear };
                        var b = stats[j].Stats with { Attack = stats[j].Stats.Attack + gear, Defense = stats[j].Stats.Defense + gear };
                        if (DidWin(scanCombat, MakeParty(a, b), 2)) wins++;
                    }
                }
                cells.Add($"{100.0 * wins / total:F1}%");
            }

            GD.Print($"WINRATE 血量{hp} | {string.Join(" | ", cells)}");
        }

        // ── 单维扫描：只加攻 / 只加防 / 只加生命，各自有各自的台阶 ────────────
        // ⚠️ 为什么要分维度测：二维表里「装备 +5 ⇒ 100%」看不出**是哪一维**推的
        // ⇒ 而「加攻」与「加防」对怪物的效果可能完全不同（⇒ 梯度也不同）。
        GD.Print("WINRATE 单维扫描（首领档，血量 120）：值 | 只加攻 | 只加防 | 只加生命");
        int[] singleSteps = { 0, 1, 2, 3, 4, 5, 6, 8, 10, 15, 20 };
        foreach (int v in singleSteps)
        {
            int atkWins = 0, defWins = 0, hpWins = 0, total = 0;

            for (int i = 0; i < stats.Count; i++)
            {
                for (int j = i; j < stats.Count; j++)
                {
                    total++;

                    var atkParty = MakeParty(
                        stats[i].Stats with { Attack = stats[i].Stats.Attack + v },
                        stats[j].Stats with { Attack = stats[j].Stats.Attack + v });
                    if (DidWin(combat, atkParty, 2)) atkWins++;

                    var defParty = MakeParty(
                        stats[i].Stats with { Defense = stats[i].Stats.Defense + v },
                        stats[j].Stats with { Defense = stats[j].Stats.Defense + v });
                    if (DidWin(combat, defParty, 2)) defWins++;

                    var hpParty = MakeParty(
                        stats[i].Stats with { Health = stats[i].Stats.Health + v },
                        stats[j].Stats with { Health = stats[j].Stats.Health + v });
                    if (DidWin(combat, hpParty, 2)) hpWins++;
                }
            }

            GD.Print($"WINRATE 单值 {v} | {100.0 * atkWins / total:F1}% | {100.0 * defWins / total:F1}% | {100.0 * hpWins / total:F1}%");
        }

        // ── 逐档扫描：怪物血量 × 装备加攻（D-124：装备 = 通关许可证）────────
        // ⚠️ 开发者拍板：「装备 = 通关许可证」⇒ 目标是
        //    **裸装难（40~60%）/ 弱装备半许可证（65~80%）/ 强装备能过（85~95%）**
        // ⇒ 下面这张表用来**挑血量**（每个加成都要落在目标区间）。
        int[][] healthCandidates =
        {
            new[] { 60, 140 },               // 普通档（练级档 ⇒ 保持必过）
            new[] { 180, 190, 200, 210 },     // 精英档
            new[] { 160, 170, 180, 190, 200 },// 首领档
        };
        int[] gearScan = { 0, 2, 3, 4, 5 };

        for (int stage = 0; stage < 3; stage++)
        {
            string stageName = CombatParameters.MonsterNameFor(stage);
            GD.Print($"WINRATE 逐档 {stageName}：血量 | " + string.Join(" | ", gearScan.Select(g => "+" + g)));

            foreach (int hp in healthCandidates[stage])
            {
                var scanCombat = new FormulaCombat(
                    monsterStats: s => CombatParameters.MonsterFor(s) with { Health = hp });

                var cells = new List<string>();
                foreach (int gear in gearScan)
                {
                    int wins = 0;
                    int total = 0;
                    for (int i = 0; i < stats.Count; i++)
                    {
                        for (int j = i; j < stats.Count; j++)
                        {
                            total++;
                            var a = stats[i].Stats with { Attack = stats[i].Stats.Attack + gear };
                            var b = stats[j].Stats with { Attack = stats[j].Stats.Attack + gear };
                            if (DidWin(scanCombat, MakeParty(a, b), stage)) wins++;
                        }
                    }
                    cells.Add($"{100.0 * wins / total:F1}%");
                }

                GD.Print($"WINRATE 逐档 {stageName} 血量{hp} | {string.Join(" | ", cells)}");
            }
        }

        GD.Print("WINRATE DONE");

        // ⚠️ **跑完就退出**（这个自检没有别的后续）⇒ 免得窗口一直开着。
        GetTree().Quit();
    }

    /// <summary>造一个两人小队。</summary>
    private static CombatPartySnapshot MakeParty(CombatStats a, CombatStats b)
        => new CombatPartySnapshot(new[]
        {
            CombatantSnapshot.Create("h-a", "甲", stats: a),
            CombatantSnapshot.Create("h-b", "乙", stats: b),
        });

    /// <summary>这一队打这一档，赢了吗。</summary>
    private static bool DidWin(FormulaCombat combat, CombatPartySnapshot party, int stage)
    {
        var log = combat.Resolve("副本", party, stage);
        return combat.DidHeroesWin(log);
    }
}
