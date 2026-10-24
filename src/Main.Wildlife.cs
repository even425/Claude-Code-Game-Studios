using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using ClaudeCodeGameStudios.Domain.Wildlife;

public partial class Main : Node2D
{
    /// <summary>
    /// <b>村外的野兽</b>（D-117）—— 领域层持有（可离线单测），这里只负责接线。
    /// </summary>
    private readonly ClaudeCodeGameStudios.Domain.Wildlife.WildlifeBook _wildlife = new();

    /// <summary>野兽的只数（D-117：建议值）。</summary>
    private const int WildlifeCount = 8;

    /// <summary>
    /// 村外的野兽（D-117）：<b>开局长一批</b>，之后每天走一步。
    /// </summary>
    /// <remarks>
    /// <para>⚠️ **数量是建议值**（8 只）：够让「村外有活物」一眼可见，
    /// 又不至于变成「满地都是」。</para>
    /// <para>⚠️ **用 <c>_rng</c>（存档里的那个随机流）** ⇒ 同一个存档种子 ⇒ 同一片野外
    /// ⇒ 复现（不另开一条随机流 ⇒ 不会打乱别的系统的随机序列）。</para>
    /// </remarks>
    private void SeedWildlife()
    {
        _wildlife.Seed(
            WildlifeCount,
            // ⚠️ `NextDouble` 要一个上界 ⇒ 给 1.0 就是 [0,1) 的浮点。
            () => (float)_rng.NextDouble(1.0),
            i => (BeastKind)(i % 3));
    }

    /// <summary>野兽每天走一步（夜里活动）。</summary>
    private void TickWildlife()
    {
        if (_wildlife.Beasts.Count == 0) return;   // 还没长出来（读档后会被重新长）

        _wildlife.WanderOneStep(() => (float)_rng.NextDouble(1.0));

        // ⚠️ **D-118：野外补货** —— 野兽被猎走之后，野外会**慢慢长回**新的
        // ⇒ 否则打几只就没了 ⇒ 玩家跑到后面就没东西可猎。
        _wildlife.Respawn(
            WildlifeCount,
            WildlifeRespawnChance,
            () => (float)_rng.NextDouble(1.0),
            i => (BeastKind)(i % 3));
    }

    /// <summary>野外每天长回一只野兽的概率（D-118：建议值）。</summary>
    private const double WildlifeRespawnChance = 0.35;

    /// <summary>野兽的步态相位（每帧推进 ⇒ 画面上它们在轻轻动）。</summary>
    /// <param name="delta">帧间隔（秒）。</param>
    public void AdvanceWildlifePhase(float delta)
        => _wildlife.AdvancePhase(delta * 0.6f);

    /// <summary>
    /// <b>狩猎服务</b>（D-118）—— 找野兽 → 打一场 → 发战利品。
    /// </summary>
    /// <remarks>
    /// ⚠️ **战斗解算器与副本、试炼塔共用同一个** ⇒ 战斗规则只有一份。
    /// </remarks>
    private ClaudeCodeGameStudios.Application.HuntService _hunt = null!;

    /// <summary>建狩猎服务（要在 <c>_life</c> 之前，因为效果表要用它）。</summary>
    private void BuildHuntService()
    {
        _hunt = new ClaudeCodeGameStudios.Application.HuntService(
            _wildlife,
            _combat,
            _combat,
            id =>
            {
                var hero = _heroes.FirstOrDefault(h => h.Id.Value == id.Value);
                if (hero is null) return null;
                return (hero.Name, CombatStatsFor(hero));
            },
            (id, loot) =>
            {
                foreach (var kv in loot)
                {
                    _itemGrant.Grant(
                        id,
                        new ClaudeCodeGameStudios.Domain.Items.ItemId(kv.Key),
                        kv.Value);
                }
            });
    }

    /// <summary>
    /// 狩猎完成（活动结束的那一刻）：打一场、发战利品、写一条杂谈。
    /// </summary>
    /// <param name="actorId">猎人的英雄标识。</param>
    private void OnHuntCompleted(string actorId)
    {
        var heroId = new ClaudeCodeGameStudios.Domain.Heroes.HeroId(actorId);
        var outcome = _hunt.Try(heroId);
        if (outcome.Beast is null)
        {
            // 野外没有野兽 ⇒ 白跑一趟（**不惩罚** ⇒ 和「他没打着」不是一回事）。
            GD.Print($"HUNT {actorId} 出门一看，野外没有野兽（白跑一趟）。");
            return;
        }

        var hero = _heroes.FirstOrDefault(h => h.Id.Value == actorId);
        string name = hero?.Name ?? actorId;

        // ⚠️ **D-119：打过的仗会让装备磨损**（-1 点耐久；打输也扣）。
        // ⚠️ 只在**真的打了一场**时扣（野外没野兽 ⇒ 白跑一趟 ⇒ 没打过 ⇒ 不扣）。
        if (hero is not null)
        {
            hero.Equipment = ClaudeCodeGameStudios.Application.EquipmentService.WearDown(hero.Equipment);
        }

        if (!outcome.Won)
        {
            GD.Print($"HUNT {name} 没能拿下 {outcome.Beast.Kind}（空手而归）。");
            return;
        }

        string lootText = string.Join("、", outcome.Loot.Select(kv => $"{kv.Key}×{kv.Value}"));
        GD.Print($"HUNT {name} 猎到了 {outcome.Beast.Kind}：{lootText}");
        // ⚠️ **不另外发事件**：行动完成事件会自己进杂谈（「X 做完了狩猎。」）
        // ⇒ 再发一条就是**同一天两句话**。战利品在背包里、细节在游戏日志里。
    }

    /// <summary>
    /// 狩猎<b>开始</b>时：把猎人送到野兽那儿（表现层）。
    /// </summary>
    /// <param name="actorId">猎人的英雄标识。</param>
    private void OnHuntStarted(string actorId)
    {
        var target = _hunt.FindTarget();
        if (target is null) return;

        var hero = _heroes.FirstOrDefault(h => h.Id.Value == actorId);
        if (hero is null) return;

        // ⚠️ **英雄的位置是表现层的事**（领域对象不该知道格位）⇒ 只给一个目的地。
        hero.SetDestination(target.X + 0.5f, target.Y + 0.5f);
    }
}
