using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using ClaudeCodeGameStudios.Application;
using ClaudeCodeGameStudios.Domain.Heroes;
using ClaudeCodeGameStudios.Domain.Items;
using ClaudeCodeGameStudios.Domain.Quests;
using ClaudeCodeGameStudios.Domain.Stall;

/// <summary>
/// 主场景的<b>真机自检</b>部分（A1 拆分：把 11 个自检从 <c>Main.cs</c> 里整块搬出）。
///
/// <para>⚠️ **这是纯代码搬移** —— 逻辑一行没改。
/// <b>自检文件里只允许有自检方法</b> —— 它是「验证装置」，不是「实现装置」，
/// 结构守卫 <c>MainStructureTests</c> 会盯着这条（否则自检文件会变成第二个
/// 巨型 GameManager：它什么都碰，于是「谁拥有哪类职责」就再也说不清了）。</para>
/// </summary>
public partial class Main : Node2D
{
    // ---------------------------------------------------------------- 自检
    // （--trade / --market / --shop 三个摊位自检在 `Main.SelfTest.Stall.cs` ——
    //   那边撞上了「单文件 ≤ 1200 行」的上限，拆出去只是为了守住那条结构规则。）

    /// <summary>--selftest：不等人操作，直接跑一遍按键逻辑并打印状态。</summary>
    private void SelfTest()
    {
        GD.Print($"SELFTEST 第{_clock.Day}天 {_clock.Clock} 在村{_heroes.Count(h => h.Present)}/{_heroes.Count}");
        // 只打印当前设计下真实存在的状态。时区作息已由 07 D-001 作废，
        // 故此处不再输出 ScheduleText / PlayHours / IsOnlineAt。
        foreach (var h in _heroes)
            GD.Print($"  {h.Name} {h.Personality}/{h.Diligence} 在村={h.Present} " +
                     $"干活率={h.WorkRate:0.0000}");
        GD.Print($"  btnTask.Disabled={_btnTask.Disabled} btnUpgrade.Disabled={_btnUpgrade.Disabled}");
        var rs = ReadySquad;
        GD.Print($"  队伍 {rs.Count} 人: " + string.Join(" ", rs.Select(h => $"{h.Name}[{h.Personality}/{h.Diligence}]")));
        // 曾经还有一行「性格计数 傲=… 莽=… 贪=…」。那是 CohesionOf 的残留 ——
        // 按性格计数来推配合系数属于出厂属性替代关系发展（基线 §9.3，已作废）。
        // 就算只打印、不影响任何结果，把已判死的东西留在输出里也会让人
        // 以为它还在起作用，所以一并删掉。
    }

    /// <summary>
    /// <c>--economy</c>：开 3 英雄后，每个英雄的 <c>Wallet.金币</c> 应为 100；
    /// 村庄资源池里<b>不应有</b>「金币」一项（村金库初始是空 —— 由 M3 摆摊收费建立）。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么需要这条自检</b>：<c>Main._Ready</c> 里塞 100 钱
    /// 是个<b>纯字符串</b>行为 —— 单测能验"100 塞进去 = 100"，但
    /// <b>「钱进的是英雄而不是村」</b>这件事单测看不到：那是 <c>Main</c> 的接线选择。
    /// D-018 修过的"奖励进错桶"是同一类风险 —— 单测全绿、运行时挂错地方。
    /// 所以在真游戏里直接跑一次 <c>Main._Ready</c> 然后看实际值。</para>
    /// </remarks>
    private void RunEconomySelfTest()
    {
        _economyTestDone = true;
        var gold = new ClaudeCodeGameStudios.Domain.Village.ResourceId("金币");
        long perHeroGold = 0;
        int heroesWithGold = 0;
        foreach (var h in _heroes)
        {
            var amt = h.Wallet.Amount(gold);
            perHeroGold = amt;
            if (amt > 0) heroesWithGold++;
        }
        bool villageHasGold = _villageModel.Get(gold) > 0;

        GD.Print($"ECONTEST 英雄 {_heroes.Count} 名 · 各持金币 {perHeroGold}" +
                 $"（应为 100）· 拥有金币的英雄 {heroesWithGold} 人（应 = {_heroes.Count}）");
        GD.Print($"ECONTEST 村庄 '金币' 余额 {_villageModel.Get(gold)}（应 = 0 —— 村金库由 M3 摆摊收费建立）");

        if (perHeroGold != 100) GD.Print("ECONTEST FAIL 英雄金币不是 100 —— 接线错位");
        if (heroesWithGold != _heroes.Count) GD.Print("ECONTEST FAIL 有英雄没拿到钱");
        if (villageHasGold) GD.Print("ECONTEST FAIL 村金库初始不该有金币 —— 钱混进 Village.Resources 池了");
    }

    /// <summary>
    /// <c>--click</c>：直接调一次花名册点击，看状态是否真的变了。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么需要它</b>：单测验 `OnRosterItemActivated` 的<b>逻辑</b>（应该是
    /// "已编入队则出队，否则入队"），但<b>不能</b>验它**真的被 ItemList 触发** ——
    /// 那是引擎层面的事。</para>
    /// <para>而真玩家在 Godot 里点的效果，**单测又看不到**。
    /// 这条自检直接调 `OnRosterItemActivated(0)`，跑完看一眼 `_squad`，
    /// 截图一张——**看得到、点得到、跑得动**三件事就齐了。</para>
    /// </remarks>
    private void RunClickSelfTest()
    {
        _clickTestDone = true;
        var _ = 0; // no-op marker to keep diff readable
        int before = _squad.Count;
        var first = _heroes[0];
        GD.Print($"CLICKTEST 点前：队伍 {before} 人（{string.Join("、", _squad.Select(h => h.Name))}）");

        OnRosterItemActivated(0);                  // 模拟点第一个人

        int after = _squad.Count;
        bool inSquad = _squad.Contains(first);
        GD.Print($"CLICKTEST 点后：队伍 {after} 人（{string.Join("、", _squad.Select(h => h.Name))}）" +
                 $" 第一人 {first.Name} {(inSquad ? "已" : "未")} 在队" +
                 $"（{after - before:+0;-0;0}）");

        // 再点一次，应该出队
        OnRosterItemActivated(0);
        GD.Print($"CLICKTEST 再点：队伍 {_squad.Count} 人（{string.Join("、", _squad.Select(h => h.Name))}）");
    }

    /// <summary>
    /// <c>--events</c>：逐条验证基线 §7.4 的 C1–C4。
    /// </summary>
    /// <remarks>
    /// <b>为什么必须验</b>：这四项是基线<b>明写</b>的杂谈验收标准。
    /// 在事件管道接上之前，杂谈是「到处直接塞文本」——
    /// C1（每条都能匹配到一个事件 ID）恒为 <b>不通过</b>，
    /// 而那正是本轮要修的东西。**不能只靠代码结构声称它成立。**
    /// </remarks>
    private void RunEventsSelfTest()
    {
        _eventsTestDone = true;

        int presented = _chronicle.Count;
        int published = _feed.Entries.Count;
        int logged = _events.Count;

        GD.Print($"EVTEST 事件日志 {logged} 条 · 已发布杂谈 {published} 条 · 已呈现 {presented} 条");

        // ── C1：每条杂谈都能追溯到一个具体事件 ──
        var publishedIds = _feed.PublishedSourceIds.ToArray();
        var untraceable = _feed.Entries
            .Where(e => !publishedIds.Contains(e.SourceEventId.Value))
            .ToArray();
        GD.Print($"EVTEST C1 可追溯性：{published - untraceable.Length}/{published} 条能匹配到来源事件" +
                 $"（无法匹配 {untraceable.Length} 条，必须为 0）");

        // ── C2：同一事件不重复记两条 ──
        var counts = _feed.Entries
            .GroupBy(e => e.SourceEventId.Value)
            .Select(g => new { Id = g.Key, N = g.Count() })
            .ToArray();
        int overOnce = counts.Count(c => c.N > 1);
        GD.Print($"EVTEST C2 去重：{published} 条来自 {counts.Length} 个不同事件" +
                 $"（超过 1 条的事件 {overOnce} 个，必须为 0）");

        // ── C3：事件发生时杂谈有反应 ──
        int beforeEvents = _events.Count, beforeEntries = _feed.Entries.Count;
        var crew = ReadySquad;
        if (crew.Count > 0)
        {
            var memberStates = crew.Select(h => _states[_heroes.IndexOf(h)]).ToArray();
            try
            {
                _mission = Mission.Create(
                    new MissionId($"etest{_clock.AbsoluteHour:0}"), "北境旧堡探查",
                    new Squad(_squadRules, memberStates), _squadRules);
                _mission.Start(IsOnMission);
                foreach (var h in crew) h.Away = true;
                ResolveDungeon(new Job(DungeonName, "铁料", 38, 11f, true), crew);
                _mission = null;
            }
            catch (Exception ex)
            {
                GD.Print($"EVTEST C3 触发失败：{ex.GetType().Name} {ex.Message}");
            }
        }
        GD.Print($"EVTEST C3 触发一次副本：新增事件 {_events.Count - beforeEvents} 个、" +
                 $"新增杂谈 {_feed.Entries.Count - beforeEntries} 条");

        // ── C4：没有事件就不产生条目 —— 杂谈里不该有空条目 ──
        var empties = _feed.Entries.Where(e => string.IsNullOrWhiteSpace(e.Text)).ToArray();
        GD.Print($"EVTEST C4 空条目 {empties.Length} 条（必须为 0）");

        // 逐条列出，让人能直接核对
        foreach (var e in _feed.Entries.TakeLast(8))
        {
            GD.Print($"EVTEST   [{e.SourceEventId}] {e.Text}");
        }
    }



    /// <summary>
    /// 自检夹具：把某个英雄摆成「<b>在场且空闲</b>」。
    ///
    /// <para><b>为什么需要它</b>：<c>StallService.PlaceStall</c> 的前置条件里有
    /// 「英雄<b>空闲</b>（<c>ToSite == false</c>）」，而 <c>ToSite</c> 是
    /// <b>模拟的随机决策</b>驱动的 ⇒ 自检跑到时那个英雄**可能正在工地干活**
    /// ⇒ 开摊被拒 ⇒ 自检**偶发**失败
    /// （2026-10-21 实测：<c>--shop</c> 偶发「SHOPTEST FAIL 开摊失败（英雄可能不在场/在忙）」，
    /// 症状是「缺 PASS 标记 + 出现 FAIL 标记」）。
    /// </para>
    ///
    /// <para><b>偶发的守卫比坏掉的守卫更糟</b> —— 人会开始无视它。
    /// 而这条前置条件**本身**另有单元测试守着（<c>StallServiceTests</c> 的
    /// 「开摊前置」用例）⇒ 自检只需要**摆出它要验的场景**（有摊可买 → 自主购物成交），
    /// 不该把「英雄恰好空闲」也变成自检的一部分。</para>
    /// </summary>
    private static void SelfTestEnsurePresentAndIdle(HeroAgent hero)
    {
        hero.Away = false;    // 在场
        hero.ToSite = false;  // 空闲（不在工地）
    }


    /// <summary>
    /// <c>--grant=英雄Id:物品名:数量</c>：第四阶段 M1 的测试配给入口。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么是调试入口</b>：正式物品来源（副本战利品）的奖励归属规则尚未确认；
    /// 先用受控入口验证「背包 → 摆摊 → 购买」链路。它<b>不</b>接入正常游戏流程。</para>
    /// <para>自检同时验证非法发放（未知物品、数量 0）不会改变背包。</para>
    /// </remarks>
    private void RunGrantSelfTest()
    {
        _grantTestDone = true;

        string? arg = OS.GetCmdlineUserArgs()
            .FirstOrDefault(a => a.StartsWith("--grant=", StringComparison.Ordinal));
        if (arg is null) return;

        string spec = arg.Substring("--grant=".Length);
        var parts = spec.Split(':');
        if (parts.Length != 3 || !long.TryParse(parts[2], out long qty))
        {
            GD.Print($"GRANT FAIL 格式应为 --grant=英雄Id:物品名:数量（收到「{spec}」）");
            return;
        }

        var heroId = new ClaudeCodeGameStudios.Domain.Heroes.HeroId(parts[0].Trim());
        var item = new ClaudeCodeGameStudios.Domain.Items.ItemId(parts[1].Trim());
        HeroAgent? hero = _heroes.FirstOrDefault(h => h.Id == heroId);
        if (hero is null)
        {
            GD.Print($"GRANT FAIL 找不到英雄 {heroId.Value}");
            return;
        }

        long before = hero.Bag.Count(item);
        var result = _itemGrant.Grant(heroId, item, qty);
        GD.Print($"GRANT {hero.Name} {item.Value} +{qty} → {result.Result}" +
                 $"（背包 {before} → {hero.Bag.Count(item)}）");

        long afterValid = hero.Bag.Count(item);
        var badItem = _itemGrant.Grant(heroId, new ClaudeCodeGameStudios.Domain.Items.ItemId("不存在的东西"), 1);
        var badQty = _itemGrant.Grant(heroId, item, 0);
        bool unchanged = hero.Bag.Count(item) == afterValid;
        GD.Print($"GRANT 非法用例：未知物品={badItem.Result}，数量0={badQty.Result}，背包不变={unchanged}");
        GD.Print("GRANT 背包快照 = {" + string.Join("、",
            hero.Bag.Snapshot().Select(kv => $"{kv.Key.Value}×{kv.Value}")) + "}");

        bool pass = result.Granted && result.NewCount == before + qty && unchanged;
        GD.Print(pass ? "GRANT PASS" : "GRANT FAIL");
    }

    /// <summary>
    /// <c>--relations</c>：第三阶段真机自检 —— 社交写关系、幂等、存读档往返。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么必须运行时验</b>：单测验的是 <c>RelationshipBook</c> /
    /// <c>RelationshipService</c> / <c>SaveValidator</c>，验不了
    /// 「<c>Main</c> 有没有真的把关系装进存档、读档有没有真的恢复」——
    /// 这正是第二阶段「端口全绿但游戏存了个空壳」的同一类风险。</para>
    /// <para><b>它跑的是生产代码路径</b>：<see cref="RelationshipService.Socialize"/>、
    /// <see cref="BuildSave"/>、<see cref="ApplySave"/>，不是复制品。</para>
    /// </remarks>
    private void RunRelationsSelfTest()
    {
        _relationsTestDone = true;

        if (_states.Count < 2)
        {
            GD.Print("RELTEST FAIL 英雄不足 2 人，无法验证社交关系。");
            return;
        }

        var a = _states[0].Id;
        var b = _states[1].Id;
        GD.Print($"RELTEST 初始：{a.Value}→{b.Value} = {_relationship.ValueOf(a, b)}，" +
                 $"{b.Value}→{a.Value} = {_relationship.ValueOf(b, a)}");

        // ── 1. 一次社交：双向各 +2 ──
        var touched = _relationships.Socialize(a, b, _clock.Now);
        GD.Print($"RELTEST 一次社交：forward changed={touched.Forward.Changed} " +
                 $"{touched.Forward.Before.Value}→{touched.Forward.After.Value}；" +
                 $"backward changed={touched.Backward.Changed} " +
                 $"{touched.Backward.Before.Value}→{touched.Backward.After.Value}");

        // ── 2. 同一业务键重复处理：必须不生效 ──
        var duplicate = _relationships.Socialize(a, b, _clock.Now);
        GD.Print($"RELTEST 重复同一业务键：changed={duplicate.Changed}（必须 false），" +
                 $"当前 {a.Value}→{b.Value} = {_relationship.ValueOf(a, b)}（必须仍是 2）");

        GD.Print($"RELTEST 关系条数 = {_relationship.Count}，幂等键 {_relationship.AppliedKeys.Count} 个，" +
                 $"杂谈 {_chronicle.Count} 条");

        // ── 2.5 失败路径：目标随队离开 → 社交失败，关系不变、不发事件 ──
        int beforeFailed = _relationship.ValueOf(a, b);
        var targetState = _states[1];
        targetState.SetOnMission();
        OnSocialCompleted(a.Value, b);
        int afterFailed = _relationship.ValueOf(a, b);
        targetState.SetInVillage();
        GD.Print($"RELTEST 目标随队时社交：关系 {beforeFailed}→{afterFailed}" +
                 $"（必须不变），{(beforeFailed == afterFailed ? "PASS" : "FAIL")}");

        // ── 2.6 同队副本合作：每对双向 +3；同一 runId 重复处理不生效 ──
        var coop = _relationships.Cooperate("reltest-run", new[] { a, b });
        var coopAgain = _relationships.Cooperate("reltest-run", new[] { a, b });
        GD.Print($"RELTEST 副本合作：变化对 {coop.PairsChanged}，重复同一 runId 后变化对 {coopAgain.PairsChanged}" +
                 $"（必须 0），当前 {a.Value}→{b.Value} = {_relationship.ValueOf(a, b)}");

        // ── 3. 存档：关系与幂等键必须真的装进去 ──
        //    用**自检专用端口**：默认不写玩家存档（village-save.json）。
        var selfTestSaves = SelfTestSaves();
        var save = BuildSave();
        GD.Print($"RELTEST BuildSave：版本 {save.Version}，" +
                 $"关系 {save.Relationships.Count} 条，幂等键 {save.AppliedRelationKeys.Count} 个");
        GD.Print($"RELTEST 存档路径 = {selfTestSaves.FilePath}（自检专用，非玩家存档）");

        try
        {
            selfTestSaves.Write(save);
        }
        catch (Exception ex)
        {
            GD.Print($"RELTEST FAIL 写盘失败：{ex.GetType().Name} {ex.Message}");
            return;
        }

        int savedForward = _relationship.ValueOf(a, b);
        int savedBackward = _relationship.ValueOf(b, a);

        // ── 4. 扰动：再换一个业务键加一次关系 ──
        _relationships.Socialize(a, b, _clock.Now + 1000.0);
        GD.Print($"RELTEST 存盘后扰动：{a.Value}→{b.Value} = {_relationship.ValueOf(a, b)}" +
                 $"（应大于存盘时的 {savedForward}）");

        // ── 5. 读档：关系必须回到存盘那一刻 ──
        var loaded = selfTestSaves.Read();
        if (!loaded.Loaded)
        {
            GD.Print($"RELTEST FAIL 读档失败：{loaded.Describe()}");
            return;
        }

        ApplySave(loaded.Save);

        var restoredA = new HeroId(loaded.Save.Heroes[0].Id);
        var restoredB = new HeroId(loaded.Save.Heroes[1].Id);
        int restoredForward = _relationship.ValueOf(restoredA, restoredB);
        int restoredBackward = _relationship.ValueOf(restoredB, restoredA);

        GD.Print($"RELTEST 读档后：{restoredA.Value}→{restoredB.Value} = {restoredForward}" +
                 $"（存盘时 {savedForward}），{restoredB.Value}→{restoredA.Value} = {restoredBackward}" +
                 $"（存盘时 {savedBackward}），幂等键 {_relationship.AppliedKeys.Count} 个");

        bool pass = restoredForward == savedForward
                    && restoredBackward == savedBackward
                    && _relationship.AppliedKeys.Count == save.AppliedRelationKeys.Count;
        GD.Print(pass ? "RELTEST PASS 关系存档往返一致" : "RELTEST FAIL 关系存档往返不一致");
    }

    /// <summary>
    /// <c>--save</c> / <c>--load</c>：真的写一次盘、真的读一次档并打印前后对照。
    /// </summary>
    /// <remarks>
    /// <b>为什么要专门做这个开关</b>：<c>SavePortTests</c> 验的是端口
    /// （写盘、校验、整份拒绝），验不了<b>游戏有没有真的把运行状态装进去</b>。
    /// 而存档最典型的失败正是「端口全绿，但游戏存了个空壳」。
    /// </remarks>
    private void RunSaveSelfTest()
    {
        _saveTestDone = true;

        // ── R-03：先摆出三种「必须被读档恢复」的英雄状态 ──
        // ① 在村、正在采集（有剩余时长）② 随队远征中 ③ 已完成首个副本
        if (_states.Count >= 3)
        {
            var acting = _states[0];
            acting.SetOnMission();       // 先释放可能已存在的行动（测试夹具）
            acting.SetInVillage();
            acting.BeginAction(new ActivityId("采集"), 3.5);

            _states[1].SetOnMission();
            _states[2].MarkFirstDungeonCompleted();
        }

        // ── MT-04 需要一支**非空**的队伍 ──
        //
        // 队伍为空时，「编成有没有被读档找回来」这件事**在界面上看不出来**：
        // 花名册行首全是空占位，队伍栏写着「没派人」⇒ 那时 MT-04 比的是一份
        // **空**的对照，覆盖不到编成 —— 而「编成按存档恢复，不是取前 N 个重建」
        // 恰恰是读档最容易悄悄做错的地方（它做错了也不报错，只是队伍换了人）。
        //
        // 这里放一个人进队伍：行首标记变成「▸」、队伍栏变成人名，
        // 于是「编成有没有被恢复」就成了**看得见**的状态。
        if (_squad.Count == 0 && _heroes.Count > 0) _squad.Add(_heroes[0]);

        // ── M4：先摆出「必须被读档恢复」的经济状态 ──
        // 开一个摊（货从背包挪进 Stall.Stock），用固定请求键买一次，
        // 再把买家的 LastShoppingDay 设成今天（模拟“今天已经自主购物过”）。
        string? stallItemId = null;
        long stallStockBefore = 0;
        bool stallPrepared = false;
        if (_heroes.Count >= 2)
        {
            var seller = _heroes[0];
            var buyer = _heroes[1];
            var stallItem = new ClaudeCodeGameStudios.Domain.Items.ItemId("石料");

            _itemGrant.Grant(seller.Id, stallItem, 10);
            SelfTestEnsurePresentAndIdle(seller);   // 夹具：摊主必须在场且空闲（否则偶发开摊失败）
            var placed = _stallService.PlaceStall(seller.Id, stallItem, 10, _clock.Now);
            if (placed is not null)
            {
                var buy = _stallService.TryBuy(
                    seller.Id, buyer.Id, stallItem, 2, _clock.Now, "saverun-1");
                buyer.LastShoppingDay = (int)_clock.Day;   // 模拟“今天已经自主购物过”
                stallItemId = stallItem.Value;
                stallStockBefore = _stallService.GetStall(seller.Id)?.Stock ?? -1;
                stallPrepared = buy.Bought;
            }

            GD.Print($"SAVETEST M4 准备：开摊={(placed is null ? "FAIL" : "OK")}，" +
                     $"购买={(stallPrepared ? "Bought" : "FAIL")}，摊位库存={stallStockBefore}，" +
                     $"每日购物日={_dailyShopping.LastRunDay}，" +
                     $"买家LastShoppingDay={buyer.LastShoppingDay}");
        }

        // 英雄两套描述（HeroAgent / HeroState.Definition）的 id 必须一致 ——
        // 摊位 OwnerId 来自 Agent，存档引用校验查的是 Definition。M4 之前两者差 1 却没东西引用。
        bool idsAligned = _heroes.Count == _states.Count;
        for (int i = 0; idsAligned && i < _heroes.Count; i++)
        {
            idsAligned = _heroes[i].Id.Value == _states[i].Definition.Id.Value;
        }

        GD.Print($"SAVETEST 英雄 Id 对齐（Agent ↔ Definition）= {idsAligned}" +
                 (idsAligned ? "" :
                     $"：{string.Join("、", _heroes.Select((h, i) => $"{h.Name} {h.Id.Value}/{_states[i].Definition.Id.Value}"))}"));

        // 自检用**自己的**存档端口：默认不写玩家存档（village-save.json）——
        // 「跑个自检把存档清了」是事故，不是特性。
        var selfTestSaves = SelfTestSaves();

        // ── D-054 阶段 4（存档 0.6）：先把五个账本科目备成**非零**，再走「搅乱 → 读回来」──
        //   待发零头 / 累计发放 / 累计销毁 / 村庄层级 / 累计升级次数。
        //   全是 0 时「读回来」等于「什么都没验」，所以这里先摆好已知的值。
        //   （用的是读档路径**同一个**入口 Restore*：那才是要验的东西。）
        const long CarrySeed = 42L;      // 待发零头（1/100 金）
        const long GrantedSeed = 777L;   // 累计发放（金）
        const long SpentSeed = 333L;     // 累计销毁（金）
        const int TierSeed = 2;          // 村庄层级
        const long UpgradesSeed = 2L;    // 累计已付款的升级次数

        _walletWriter.RestorePending(_heroes[0].Id, CarrySeed);
        _walletWriter.RestoreTotalGranted(GrantedSeed);
        _upgradeService.RestoreTotals(SpentSeed, UpgradesSeed);
        _tier = TierSeed;
        _village.SetTier(TierSeed);

        var save = BuildSave();
        GD.Print($"SAVETEST 存档路径 = {selfTestSaves.FilePath}（自检专用，非玩家存档）");
        GD.Print($"SAVETEST 将写入：版本 {save.Version} 模拟时间 {save.SimTime:0.###}" +
                 $" 随机状态 {save.RandomState}");
        GD.Print($"SAVETEST 英雄 {save.Heroes.Count} 名" +
                 $"（{string.Join("、", save.Heroes.Select(h => h.Name))}）" +
                 $" 资源 {{{string.Join(", ", save.Village.Resources.Select(kv => kv.Key + ":" + kv.Value))}}}" +
                 $" 已结算键 {save.AppliedSettlementKeys.Count} 个");
        GD.Print($"SAVETEST 将写入：摊位 {save.Stalls.Count} 个" +
                 $"（{string.Join("、", save.Stalls.Select(s => $"{s.OwnerId}:{s.ItemId}×{s.Stock}@{s.Status}"))}），" +
                 $"购买幂等键 {save.AppliedPurchaseKeys.Count} 个" +
                 $"（{string.Join("、", save.AppliedPurchaseKeys)}），" +
                 $"每日购物日 {save.DailyShoppingLastRunDay}");

        try
        {
            selfTestSaves.Write(save);
        }
        catch (Exception ex)
        {
            GD.Print($"SAVETEST 写盘失败：{ex.GetType().Name} {ex.Message}");
            return;
        }

        GD.Print($"SAVETEST 写盘成功，文件 {new FileInfo(selfTestSaves.FilePath).Length} 字节");

        // ── MT-04（存档重载后的可见状态）：**搅乱之前**先拍一张「玩家看到的文字」──
        //
        // 整个存读档往返都在**同一帧内**完成 ⇒ 时钟不走 ⇒ 前后两份快照应当**完全相同**。
        // （杂谈面板按设计会被清空 —— 读档是换一个世界 —— 所以它不在快照里。）
        //
        // ⚠️ 快照必须在**搅乱之前**拍。搅乱之后再拍，比的就是「搅乱后 vs 读档后」，
        // 差异本来就是**预期的**（材料被花掉了、时间被推进了）⇒ 每次都红 ⇒ 等于没有守卫。
        // 这里要回答的是另一个问题：**玩家存档前看到的，和读档后看到的，是不是同一个世界**。
        Refresh();
        string visibleBefore = VisibleTextSnapshot();

        // ── 存之前把游戏搅乱：材料花掉、队伍拆散、时间走一段 ──
        long matBefore = Mat;
        string squadBefore = string.Join("、", _squad.Select(h => h.Name));
        _squad.Clear();                                   // 拆散队伍
        _villageModel.TrySpend(MaterialsId, matBefore);
        _clock.SetSimTime(_clock.Now + 500);
        ulong rngBefore = _rng.State;
        // M4：把摊位 / 幂等键 / 每日购物状态也搅乱 ——
        // 它们必须是被读档「找回来」的，而不是本来就在内存里。
        _stallService.Restore(
            Array.Empty<ClaudeCodeGameStudios.Domain.Stall.Stall>(),
            Array.Empty<string>());
        _dailyShopping.RestoreLastRunDay(int.MinValue);
        if (_heroes.Count >= 2) _heroes[1].LastShoppingDay = 0;
        // R-05 第二轮 / D-052：税额余数也要搅乱 —— 它必须是被读档「找回来」的。
        long carryBefore = _stallService.VillageCutCarry;
        _stallService.RestoreVillageCutCarry(0);
        // D-054 阶段 4（存档 0.6）：五个账本科目同样搅乱。
        _walletWriter.RestorePending(_heroes[0].Id, 7);
        _walletWriter.RestoreTotalGranted(1);
        _upgradeService.RestoreTotals(2, 1);
        _tier = 4;
        _village.SetTier(4);
        GD.Print($"SAVETEST 故意搅乱：材料 {matBefore} → {Mat}，队伍「{squadBefore}」已拆散，" +
                 $"摊位已清空、每日购物闸门与 LastShoppingDay 已清零，随机状态已推进，" +
                 $"税额余数 {carryBefore} → 0");
        GD.Print($"SAVETEST 0.6 账本科目搅乱：待发零头 {CarrySeed} → 7，累计发放 {GrantedSeed} → 1，" +
                 $"累计销毁 {SpentSeed} → 2，村庄层级 {TierSeed} → 4，累计升级 {UpgradesSeed} → 1");

        // ⚠️ 搅乱之后**必须刷新界面**。
        //
        // 不刷新的话，屏幕上的字还是存档前那一版 ⇒「读档之后屏幕变了没有」这个问题
        // 就没有答案（比的是同一张皮，永远「一致」）⇒ 那是空转，不是守卫。
        // 刷新之后，屏幕显示的是**被搅乱的世界** —— 这才是读档要「换回来」的那张皮。
        Refresh();

        // ── 读档 ──
        var loaded = selfTestSaves.Read();
        GD.Print($"SAVETEST 读档结果 Loaded={loaded.Loaded}" +
                 (loaded.Loaded ? "" : $" 原因：{loaded.Describe()}"));
        if (!loaded.Loaded) return;

        ApplySave(loaded.Save!);

        // ── MT-04（存档重载后的可见状态）：读档后拍两张，缺一不可 ──
        //
        // A：**不额外刷新** —— 这一版回答的是「**读档路径自己**有没有把界面带回来」。
        //    状态还原了但界面没跟着刷新 ⇒ 屏幕上还是被搅乱的世界 ⇒ A ≠ 存档前 ⇒ 抓得到。
        //    （这是 MT-04 真正要防的那类缺陷，也是最容易漏的一类：
        //    状态字段全对、断言全绿，而玩家看到的是上一个世界。）
        string visibleAfterRead = VisibleTextSnapshot();
        //
        // B：**再刷新一次** —— 这一版回答的是「界面**最终**显示的是什么」。
        //    A ≠ B 说明「刷新一次还不够」，那也是缺陷（界面上有一处要两帧才跟上）。
        Refresh();
        string visibleAfter = VisibleTextSnapshot();

        bool visibleSame = visibleBefore == visibleAfterRead && visibleBefore == visibleAfter;
        GD.Print($"SAVETEST MT-04 可见状态一致 = {visibleSame}" +
                 $"（存档前 == 读档后未额外刷新：{visibleBefore == visibleAfterRead}；" +
                 $"存档前 == 读档后已刷新：{visibleBefore == visibleAfter}）");
        if (!visibleSame)
        {
            GD.Print($"SAVETEST MT-04 存档前：{visibleBefore.Replace("\n", " ／ ")}");
            GD.Print($"SAVETEST MT-04 读档后（未额外刷新）：{visibleAfterRead.Replace("\n", " ／ ")}");
            GD.Print($"SAVETEST MT-04 读档后（已刷新）：{visibleAfter.Replace("\n", " ／ ")}");
        }
        GD.Print(visibleSame ? "SAVETEST MT-04 PASS" : "SAVETEST MT-04 FAIL");

        GD.Print($"SAVETEST 读档后：材料 {Mat}（应为 {matBefore}）" +
                 $" 模拟时间 {_clock.Now:0.###}（应为 {save.SimTime:0.###}）" +
                 $" 随机状态 {_rng.State}（应为 {rngBefore}）");
        GD.Print($"SAVETEST 读档后花名册 {string.Join("、", _heroes.Select(h => h.Name))}" +
                 $"（应为 {string.Join("、", save.Heroes.Select(h => h.Name))}）");
        GD.Print($"SAVETEST 读档后队伍「{string.Join("、", _squad.Select(h => h.Name))}」" +
                 $"（应为「{squadBefore}」——编成必须按存档恢复，不能「取前 N 个」重建）");
        GD.Print($"SAVETEST 已恢复的结算键 {string.Join(" | ", _settlement.AppliedKeys)}");
        GD.Print($"SAVETEST 读档后税额余数 {_stallService.VillageCutCarry}" +
                 $"（应为 {carryBefore}）→ {_stallService.VillageCutCarry == carryBefore}");

        // ── D-054 阶段 4（存档 0.6）：五个账本科目必须真的读回来 ──
        //   读不到的后果不是「少了一个数」，而是**守恒律的账从此对不上**：
        //   待发零头丢了 = 已挣到的钱凭空消失；累计发放丢了 = 账目虚高；
        //   村庄层级丢了 = 读档后村庄掉回 0 级，而升级花掉的钱已经记下了。
        long carryAfter06 = _walletWriter.PendingOf(_heroes[0].Id);
        bool econ06Ok =
            carryAfter06 == CarrySeed
            && _walletWriter.TotalGranted == GrantedSeed
            && _upgradeService.TotalGoldSpent == SpentSeed
            && _upgradeService.UpgradesStarted == (int)UpgradesSeed
            && _tier == TierSeed;

        GD.Print($"SAVETEST 0.6 待发零头 {carryAfter06}（应为 {CarrySeed}）；" +
                 $"累计发放 {_walletWriter.TotalGranted}（应为 {GrantedSeed}）；" +
                 $"累计销毁 {_upgradeService.TotalGoldSpent}（应为 {SpentSeed}）；" +
                 $"村庄层级 {_tier}（应为 {TierSeed}）；" +
                 $"累计升级 {_upgradeService.UpgradesStarted}（应为 {UpgradesSeed}）");
        GD.Print(econ06Ok ? "SAVETEST 0.6 PASS" : "SAVETEST 0.6 FAIL");

        // ── R-03：在场性 / 行动 / 目标 / 首通标记是否真的恢复 ──
        if (_states.Count >= 3)
        {
            var acting = _states[0];
            var away = _states[1];
            var firstDungeon = _states[2];

            bool actingOk = acting.CurrentActivity.Value == "采集"
                            && Math.Abs(acting.RemainingActionSeconds - 3.5) < 1e-6;
            bool awayOk = away.Presence == Presence.OnMission;
            bool firstOk = firstDungeon.HasCompletedFirstDungeon;

            GD.Print($"SAVETEST R-03 行动 {acting.CurrentActivity.Value}" +
                     $"/{acting.RemainingActionSeconds:0.###}（应为 采集/3.5）→ {actingOk}；" +
                     $"随队 {away.Presence}（应为 OnMission）→ {awayOk}；" +
                     $"首通 {firstDungeon.HasCompletedFirstDungeon}（应为 True）→ {firstOk}");
            GD.Print(actingOk && awayOk && firstOk ? "SAVETEST R-03 PASS" : "SAVETEST R-03 FAIL");
        }

        // ── M4：摊位 / 购买幂等键 / 每日购物状态是否真的恢复 ──
        // 只断「字段读回来了」不够：真正的性质是**读档后重放同一请求键不会再次扣款/发货**。
        if (stallPrepared && stallItemId is not null)
        {
            var seller = _heroes[0];
            var buyer = _heroes[1];
            var restoredStall = _stallService.GetStall(seller.Id);

            bool stallOk = restoredStall is not null
                           && restoredStall.ItemId.Value == stallItemId
                           && restoredStall.Stock == stallStockBefore
                           && restoredStall.Status
                              == ClaudeCodeGameStudios.Domain.Stall.StallStatus.Open;
            bool keyOk = _stallService.AppliedRequestIds.Contains("saverun-1");
            bool lastShoppingOk = buyer.LastShoppingDay == (int)_clock.Day;
            bool gateOk = _dailyShopping.LastRunDay == save.DailyShoppingLastRunDay;

            long stockBeforeReplay = restoredStall?.Stock ?? -1;
            var replay = _stallService.TryBuy(
                seller.Id, buyer.Id,
                new ClaudeCodeGameStudios.Domain.Items.ItemId(stallItemId),
                2, _clock.Now, "saverun-1");
            long stockAfterReplay = _stallService.GetStall(seller.Id)?.Stock ?? -1;
            bool replayOk = replay.Outcome
                                == ClaudeCodeGameStudios.Application.Stall.StallService.BuyOutcome.DuplicateRequest
                            && stockAfterReplay == stockBeforeReplay;

            GD.Print($"SAVETEST M4 摊位 库存={restoredStall?.Stock}（应为 {stallStockBefore}），" +
                     $"状态={restoredStall?.Status}（应为 Open），商品={restoredStall?.ItemId.Value}（应为 {stallItemId}）→ {stallOk}；" +
                     $"幂等键={keyOk}；每日购物日={_dailyShopping.LastRunDay}（应为 {save.DailyShoppingLastRunDay}）→ {gateOk}；" +
                     $"买家LastShoppingDay={buyer.LastShoppingDay}（应为 {(int)_clock.Day}）→ {lastShoppingOk}");
            GD.Print($"SAVETEST M4 读档后重放购买键 {replay.Outcome}" +
                     $"（必须 DuplicateRequest），库存 {stockBeforeReplay} → {stockAfterReplay}（必须不变）→ {replayOk}");
            GD.Print(idsAligned && stallOk && keyOk && lastShoppingOk && gateOk && replayOk
                ? "SAVETEST M4 PASS" : "SAVETEST M4 FAIL");
        }

        // ── M6 与 M7 的接缝真正生效了吗 ──
        // 键存回来只说明「写进去了、读出来了」；真正的性质是
        // **读档后重放同一笔结算会被拒绝**（I4 / 基线 §13.7）。
        // 不恢复键的话，材料会在读档后暴涨 —— 而症状出现在很远的地方，极难归因。
        if (_settlement.AppliedKeys.Count > 0)
        {
            long matBeforeReplay = Mat;
            string key = _settlement.AppliedKeys.First();
            // BattleResultId 一律按「胜利」重放：若幂等真的生效，
            // 它连算都不该算，无论传 true 还是 false。
            bool reapplied = _settlement.Apply(
                new ClaudeCodeGameStudios.Domain.Settlement.SettlementKey(
                    key.Split('#')[0],
                    string.Join("#", key.Split('#').Skip(1))),
                heroesWon: true);

            GD.Print($"SAVETEST 读档后重放已结算的键 → Apply 返回 {reapplied}" +
                     $"（必须 false）材料 {matBeforeReplay} → {Mat}（必须不变）");
        }
        GD.Print($"SAVETEST 资源快照 {{{string.Join(", ",
            _villageModel.Snapshot().Select(kv => kv.Key + ":" + kv.Value))}}}");
    }

    private void RunDungeonSelfTest()
    {
        _dungeonTestDone = true;

        // 真的走一遍派单 —— 换成副本任务（Glory == true），那是走 DungeonFlow 的那条路。
        _pendingJob = Jobs.First(j => j.Glory);

        // 队伍是玩家点花名册组出来的（开局**不预编**），而自检没有人点 ——
        // 所以这里走**同一个入口**组一支队伍出来，不绕过领域规则：
        // 加入与出发合法性仍然由 SquadRules 判定。
        if (_squad.Count == 0)
        {
            for (int i = 0; i < _heroes.Count && _squad.Count < 2; i++)
            {
                OnRosterItemActivated(i);
            }
            GD.Print($"DUNGEONTEST 自检组队：{_squad.Count} 人（与玩家同一入口）");
        }

        var crew = ReadySquad;
        if (crew.Count == 0) { GD.Print("DUNGEONTEST 无人在场，跑不了。"); return; }

        var memberStates = crew.Select(h => _states[_heroes.IndexOf(h)]).ToArray();
        try
        {
            _mission = Mission.Create(
                new MissionId($"dtest{_clock.AbsoluteHour:0}"), _pendingJob.Name,
                new Squad(_squadRules, memberStates), _squadRules);
            _mission.Start(IsOnMission);
        }
        catch (Exception ex)
        {
            GD.Print($"DUNGEONTEST 派单失败：{ex.GetType().Name} {ex.Message}");
            return;
        }

        foreach (var h in crew) h.Away = true;

        long matBefore = Mat;
        int notesBefore = _chronicle.Count;

        // ── R-01：发放**之前**的背包快照（对照用）──
        var bagBefore = crew.ToDictionary(
            h => h.Id.Value,
            h => h.Bag.Snapshot().ToDictionary(kv => kv.Key.Value, kv => kv.Value));

        // ── D-054-01：发放**之前**的钱包与零头快照（对照用）──
        //    金币**不论成败都发**（D-054-01 裁决 ①）→ 成功局与失败局都要验。
        var goldResource = ClaudeCodeGameStudios.Application.Stall.StallService.Gold;
        var goldBefore = crew.ToDictionary(
            h => h.Id.Value, h => h.Wallet.Amount(goldResource));
        var pendingBefore = crew.ToDictionary(
            h => h.Id.Value, h => _walletWriter.PendingOf(h.Id));

        var outcome = ResolveDungeon(_pendingJob, crew);

        GD.Print($"DUNGEONTEST 队伍 {crew.Count} 人 → {_pendingJob.Name}");
        GD.Print($"DUNGEONTEST 任务状态 = {_mission?.Status}（ResolveDungeon 不清空 _mission，由本处清）");
        _mission = null;

        foreach (var r in outcome.Records)
        {
            GD.Print($"  {r.Stage} 「{r.EncounterName}」 {(r.Result.HeroesWon ? "胜" : "负")}" +
                     $" 可复核={r.Result.IsVerifiableAgainstLog()}" +
                     $" 引用={r.Result.DecidingEntryIds.Count} 条" +
                     $" 挂入实例={r.AttachedToRun}");
        }

        GD.Print($"DUNGEONTEST 声明阶段数 {ClaudeCodeGameStudios.Application.DungeonFlow.DeclaredStageCount}" +
                 $" 实际打了 {outcome.Records.Count} 场");
        GD.Print($"DUNGEONTEST 实例上挂着的结果 = {outcome.Run.BattleResultId}" +
                 $"（I3：只能有一个）");
        GD.Print($"DUNGEONTEST 副本状态 = {outcome.Run.Status} 整场成功 = {outcome.Succeeded}");
        GD.Print($"DUNGEONTEST 材料 {matBefore} → {Mat}" +
                 $"（失败则必须不变：D-010 Q1 失败不给奖励）");
        // 整个资源快照：只打总数的话，「奖励进了别的桶」这类 bug 完全看不出来
        // —— 界面就显示一个材料数。踩过一次，所以这里连桶一起打。
        GD.Print("DUNGEONTEST 资源快照 = {" +
                 string.Join(", ",
                     _villageModel.Snapshot().Select(kv => $"{kv.Key}:{kv.Value}")) + "}");

        // ── D-054-01：金币发放（**不论成败**）──
        //    单测验的是 Settlement 那一层；这里守的是「游戏真的把金币发到了
        //    参战者的钱包里」—— 与 R-01 的战利品同款理由：单测验不出接线。
        var goldAfter = crew.ToDictionary(
            h => h.Id.Value, h => h.Wallet.Amount(goldResource));

        bool goldOk = true;
        foreach (var h in crew)
        {
            long got = goldAfter[h.Id.Value] - goldBefore[h.Id.Value];
            // 零头机制（D-054 阶段 4）：本次实际到手 = floor((原零头 + X) / 100) 金，
            // 余下的零头留在该英雄名下。X 是整数金时，零头恒为 0 → 每次到手恰好 X 金。
            long expected = (pendingBefore[h.Id.Value] + _goldRewardUnits) / 100;
            long pendingAfter = (pendingBefore[h.Id.Value] + _goldRewardUnits) % 100;
            bool oneOk = got == expected
                && _walletWriter.PendingOf(h.Id) == pendingAfter;
            if (!oneOk) goldOk = false;
            GD.Print($"DUNGEONTEST 金币 {h.Name}: {goldBefore[h.Id.Value]} → {goldAfter[h.Id.Value]}" +
                     $"（到手 {got}，应 = {expected}；待发零头 {pendingBefore[h.Id.Value]} → {pendingAfter}（1/100 金）；" +
                     $"X = {_goldRewardUnits / 100.0:0.##} 金；**不论成败都发**：D-054-01）");
        }

        // ── I4：把同一个结算键再 Apply 一次，必须一分不加 ──
        // 单测验的是 Settlement 自己，验不了「游戏会不会真的重复调用它」。
        // 而「读档重放」正是基线 §13.7 要防的场景。
        var replayKey = new ClaudeCodeGameStudios.Domain.Settlement.SettlementKey(
            outcome.Run.Id.Value, outcome.Run.BattleResultId);
        long beforeReplay = Mat;
        long goldBeforeReplay = crew.Sum(h => h.Wallet.Amount(goldResource));
        bool appliedAgain = _settlement.Apply(replayKey, outcome.Succeeded);
        long goldAfterReplay = crew.Sum(h => h.Wallet.Amount(goldResource));
        GD.Print($"DUNGEONTEST 重放同一结算键 → Apply 返回 {appliedAgain}，" +
                 $"材料 {beforeReplay} → {Mat}" +
                 $"（材料必须不变、Apply 必须返回 false：I4 已完成事件不会重复应用）");
        GD.Print($"DUNGEONTEST 重放同一结算键 → 英雄金币合计 {goldBeforeReplay} → {goldAfterReplay}" +
                 $"（必须不变：金币也在同一把幂等锁下，I4）");

        // ── R-01：战利品分配与入包 ──
        // 单测验的是分配规则与发放入口，验不了「游戏有没有真的把战利品
        // 落到参战者的个人背包里」—— 那正是 D-018「奖励进错桶」的同一类风险。
        var loot = _lastDungeonLoot;
        GD.Print($"DUNGEONTEST 战利品分配 = {loot.Grants.Count} 条（副本 {outcome.Run.Id}）：" +
                 (loot.Grants.Count == 0 ? "（无）" :
                  string.Join("、", loot.Grants.Select(g => $"{g.Hero.Value} {g.Item.Value}×{g.Quantity}"))));

        bool lootInBags = true;
        long lootTotal = 0;
        foreach (var g in loot.Grants)
        {
            var hero = _heroes.FirstOrDefault(h => h.Id == g.Hero);
            if (hero is null) { lootInBags = false; continue; }

            long before = bagBefore.TryGetValue(g.Hero.Value, out var snap)
                && snap.TryGetValue(g.Item.Value, out var n) ? n : 0;
            long after = hero.Bag.Count(g.Item);
            lootTotal += g.Quantity;

            if (after - before != g.Quantity) lootInBags = false;
            GD.Print($"  {hero.Name}：{g.Item.Value} {before} → {after}（应 +{g.Quantity}）");
        }

        // 参战者之外的英雄：一件都不能多（未参战者不参与分配）。
        bool nobodyElseGotLoot = true;
        foreach (var hero in _heroes)
        {
            if (loot.Grants.Any(g => g.Hero == hero.Id)) continue;
            foreach (var g in loot.Grants)
            {
                long before = bagBefore.TryGetValue(hero.Id.Value, out var snap)
                    && snap.TryGetValue(g.Item.Value, out var n) ? n : 0;
                if (hero.Bag.Count(g.Item) - before != 0) nobodyElseGotLoot = false;
            }
        }

        GD.Print($"DUNGEONTEST 战利品入包 = {lootInBags}，未参战者没拿到 = {nobodyElseGotLoot}" +
                 $"（失败时两人都必须是 true：D-010 Q1 失败不给奖励）");

        // ── R-01 M3：奖励要能存进档、读档后仍属于正确的人 ──
        //
        // **快照必须在这一刻取**：下面 M4 会故意把战利品摆上摊再卖掉 ——
        // 卖完之后战利品在**买家**的背包里，摊主的背包已经空了。
        // 若在卖完之后才查「摊主的存档里有没有这件战利品」，查到的自然是「没有」，
        // 而那不是入档失败，是**查错了时机**。
        // （R-05 的掉落表出来之前，每次掉落都是 19 份，卖 1 份还剩 18 份，
        //    这个错误一直没暴露 —— 它被"数量足够多"掩盖了。）
        var saveWithLoot = BuildSave();
        bool lootPersisted = true;
        foreach (var g in loot.Grants)
        {
            var heroSave = saveWithLoot.Heroes.FirstOrDefault(h => h.Id == g.Hero.Value);
            long saved = heroSave?.Bag
                .Where(b => b.ItemId == g.Item.Value)
                .Sum(b => b.Count) ?? 0;
            if (saved <= 0) lootPersisted = false;
        }
        GD.Print($"DUNGEONTEST 战利品已入档 = {lootPersisted}，" +
                 $"已结算键 {saveWithLoot.AppliedSettlementKeys.Count} 个" +
                 $"（存档版本 {saveWithLoot.Version}，R-01 未新增字段）");

        // ── R-01 M4：拿到的物品要能真的开摊、被另一位英雄买走 ──
        // ⚠️ **这一段必须每次都跑**（真事：2026-10-21 查出来的）：
        // 副本**有可能失败**（单场胜率 0.65 × 3 个阶段 ≈ 27%）⇒ 失败就没有战利品
        // ⇒ 整段被跳过 ⇒ **它就成了一段装饰**。
        // 实测：固定种子下这次副本**失败** ⇒ 这段从未跑过；
        // 而且它的结果**也从未被要求过** —— 原来只打了 `→ True/False`，
        // runner 的禁词是「FAIL」，**看不见 True/False**。
        // ⇒ 战利品为空时，**用生产路径配给一件同样的物品**，让这条链照样被验。
        //    （「副本掉了什么」由上面的战利品分配 / 入档断言负责；
        //      这里验的是「物品能开摊、能被别人买走」这条**链路**。）
        if (_heroes.Count >= 2)
        {
            var seller = _heroes[0];
            var buyer = _heroes[1];
            var item = new ClaudeCodeGameStudios.Domain.Items.ItemId("石料");

            if (loot.Grants.Count > 0)
            {
                var firstGrant = loot.Grants[0];
                item = firstGrant.Item;
                seller = _heroes.FirstOrDefault(h => h.Id == firstGrant.Hero) ?? seller;
                buyer = _heroes.FirstOrDefault(h => h.Id != firstGrant.Hero) ?? buyer;
            }
            else
            {
                // 副本失败 ⇒ 没有战利品 ⇒ 配给摊主 1 份（生产路径），让这条链照样被验。
                var give = _itemGrant.Grant(seller.Id, item, 1);
                if (!give.Granted)
                {
                    GD.Print("DUNGEONTEST 战利品开摊链 = FAIL（配给失败，无法准备场景）");
                    return;
                }
            }

            var gold = ClaudeCodeGameStudios.Application.Stall.StallService.Gold;

            // ⚠️ 单价取自**生效价格表**（P-14 的占位值，开发者随时会改）——
            // 写死 5 的话，改一次价格这个自检就红，而「改价格 → 重跑标定」
            // 恰恰要在**改价之后**才用得上。
            long unitPrice = _priceBook.PriceOf(item);
            if (unitPrice <= 0)
            {
                // 单价 0 = 「不卖」（价格表的合法取值）⇒ 这条链**不适用**，不是失败。
                GD.Print($"DUNGEONTEST 战利品开摊链 = 跳过（{item.Value} 单价为 0 = 不卖）");
                return;
            }

            {
                long stock = Math.Min(3, seller.Bag.Count(item));
                long buyerGold0 = buyer.Wallet.Amount(gold);
                long buyerBag0 = buyer.Bag.Count(item);

                SelfTestEnsurePresentAndIdle(seller);   // 夹具：摊主必须在场且空闲（否则偶发开摊失败）
                var placed = stock > 0
                    ? _stallService.PlaceStall(seller.Id, item, stock, _clock.Now)
                    : null;

                var bought = placed is null
                    ? default
                    : _stallService.TryBuy(seller.Id, buyer.Id, item, 1, _clock.Now, "dungeonrun-1");

                bool chainOk = placed is not null
                              && bought.Bought
                              && buyer.Bag.Count(item) == buyerBag0 + 1
                              && buyer.Wallet.Amount(gold) == buyerGold0 - unitPrice;

                GD.Print($"DUNGEONTEST 战利品开摊={(placed is null ? "FAIL" : $"OK（{seller.Name} 摆 {stock} 份）")}，" +
                         $"他人购买={bought.Outcome}，买家背包 {buyerBag0} → {buyer.Bag.Count(item)}，" +
                         $"买家金 {buyerGold0} → {buyer.Wallet.Amount(gold)} → {chainOk}");
                // ⚠️ **必须再打一个能被机器读到的 PASS/FAIL 标记**。
                // 原来这一行只打了 `→ {chainOk}`（True/False）⇒ runner 的禁词是「FAIL」，
                // **看不见 True/False** ⇒ 这条断言其实**从未被要求过** —— 是装饰。
                // （真事：2026-10-21 的变异矩阵里，「把付款期望写死回 5」那一档**没被抓住**，
                //   就是因为它。⇒ 打印出来的东西，必须有一个**机器能读的形式**。）
                GD.Print($"DUNGEONTEST 战利品开摊链 = {(chainOk ? "PASS" : "FAIL")}");
            }
        }

        // ── R-01 M3 的存档检查已在上方（战利品刚落地时）完成 ──

        foreach (string note in _chronicle.Recent(_chronicle.Count - notesBefore))
        {
            GD.Print($"DUNGEONTEST 杂谈| {note}");
        }

        // ── D-054-01 汇总判定 ──
        //    「每位参战者 +X」且「重放同一结算键不再发」→ 才算这条规则在**真实游戏**里成立。
        //    单测验的是 Settlement 那一层，验不出这里的接线。
        bool goldPassed = goldOk && goldAfterReplay == goldBeforeReplay;
        GD.Print($"DUNGEONTEST 金币发放 = {goldOk}（每位参战者按 X = {_goldRewardUnits / 100.0:0.##} 金累计，" +
                 $"**不论成败**），" +
                 $"重放后金币合计 {goldBeforeReplay} → {goldAfterReplay}（必须不变：I4）" +
                 $" → D-054-01 {(goldPassed ? "PASS" : "FAIL")}");
    }
}
