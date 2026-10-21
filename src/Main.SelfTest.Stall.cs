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
/// 主场景自检的<b>摊位相关</b>部分（--trade / --market / --shop）。
///
/// <para>⚠️ **纯搬移**：这三个自检原本在 <c>Main.SelfTest.cs</c> 里，
/// 因为那条文件撞上了「单文件 ≤ 1200 行」的结构上限（UT-01 的行数封顶），
/// 才被拆到这里 —— <b>逻辑一行没改</b>。</para>
///
/// <para>⚠️ **自检文件里只允许有自检方法** —— 它是「验证装置」，不是「实现装置」。
/// 结构守卫 <c>MainStructureTests</c> 的「自检文件只允许有自检方法」那条
/// **已扩到所有 <c>Main.SelfTest*.cs</c>** ⇒ 拆文件不会变成绕过那条规则的缺口。</para>
/// </summary>
public partial class Main : Node2D
{
    /// <summary>
    /// <c>--trade</c>：第四阶段 3A 真机自检 —— 发物品 → 开摊 → 手动购买 → 失败场景。
    /// </summary>
    /// <remarks>
    /// <para>覆盖：成功购买（双方货币/背包/摊位库存/村库费用）、重复请求幂等、
    /// 库存不足、余额不足；每个失败场景都检查状态不变（无部分结算）。</para>
    /// <para>走的是生产代码路径：<see cref="ItemGrantService.Grant"/>、
    /// <c>StallService.PlaceStall</c>、<c>StallService.TryBuy</c>，不是复制品。</para>
    /// </remarks>
    private void RunTradeSelfTest()
    {
        _tradeTestDone = true;

        if (_states.Count < 2)
        {
            GD.Print("TRADETEST FAIL 英雄不足 2 人，无法验证买卖。");
            return;
        }

        var seller = _heroes[0];
        var buyer = _heroes[1];
        var item = new ClaudeCodeGameStudios.Domain.Items.ItemId("石料");
        var gold = ClaudeCodeGameStudios.Application.Stall.StallService.Gold;

        // ⚠️ **单价必须取自生效价格表，不能写死。**
        // 单价是【待定 P-14】的占位值（现在是 5），开发者随时会改 `config/prices.json`。
        // 写死的话，改一次价格这个自检就红 —— 而「改价格 → 重跑标定」这条承诺
        // 恰恰要在**改价之后**才用得上。
        // （同一类问题已在单元测试上栽过一次：14 条测试把占位价写成了断言事实。）
        long unitPrice = _priceBook.PriceOf(item);
        if (unitPrice <= 0)
        {
            GD.Print("TRADETEST FAIL 石料没有有效单价（价格表里 0 = 不卖）—— 夹具无法成交。");
            return;
        }

        long gross2 = 2 * unitPrice;    // 本自检买 2 份
        // 村抽按「5% + 余数累计」算（D-023 / D-052）：用**纯函数**算期望值。
        // 自检守的是**接线**（钱真的动了），算术本身由 StallVillageCutTests 守。
        var (expectedNet2, expectedCut2, _) = ClaudeCodeGameStudios.Domain.Stall.StallFee.Compute(
            gross2, _stallService.VillageCutCarry);

        long sellerGold0 = seller.Wallet.Amount(gold);
        long buyerGold0 = buyer.Wallet.Amount(gold);
        long villageGold0 = _villageModel.Get(gold);

        // 1. 测试配给 + 开摊
        SelfTestEnsurePresentAndIdle(seller);   // 夹具：摊主必须在场且空闲（否则偶发开摊失败）
        var grant = _itemGrant.Grant(seller.Id, item, 5);
        var placed = _stallService.PlaceStall(seller.Id, item, 5, _clock.Now);
        GD.Print($"TRADETEST 配给={grant.Result}，开摊={(placed is null ? "FAIL" : "OK")}");
        if (!grant.Granted || placed is null)
        {
            GD.Print("TRADETEST FAIL 无法准备交易场景");
            return;
        }

        // 2. 成功购买 2 份
        var buy = _stallService.TryBuy(seller.Id, buyer.Id, item, 2, _clock.Now, "traderun-1");
        long stockAfterBuy = _stallService.GetStall(seller.Id)?.Stock ?? -1;
        long sellerGold1 = seller.Wallet.Amount(gold);
        long buyerGold1 = buyer.Wallet.Amount(gold);
        long villageGold1 = _villageModel.Get(gold);
        bool buyOk = buy.Bought
                     && buyerGold0 - buyerGold1 == gross2
                     && sellerGold1 - sellerGold0 == expectedNet2
                     && buyer.Bag.Count(item) == 2
                     && stockAfterBuy == 3
                     && villageGold1 - villageGold0 == expectedCut2;   // 村抽（可能为 0）
        GD.Print($"TRADETEST 购买={buy.Outcome}，卖家金 {sellerGold0}→{sellerGold1}，" +
                 $"买家金 {buyerGold0}→{buyerGold1}，买家背包={buyer.Bag.Count(item)}，" +
                 $"摊位库存={stockAfterBuy}，村金 {villageGold0}→{villageGold1} → {buyOk}");

        // 3. 重复同一请求键：幂等，状态不变
        var dup = _stallService.TryBuy(seller.Id, buyer.Id, item, 2, _clock.Now, "traderun-1");
        bool dupOk = dup.Outcome == ClaudeCodeGameStudios.Application.Stall.StallService.BuyOutcome.DuplicateRequest
                     && seller.Wallet.Amount(gold) == sellerGold1
                     && buyer.Wallet.Amount(gold) == buyerGold1
                     && buyer.Bag.Count(item) == 2
                     && (_stallService.GetStall(seller.Id)?.Stock ?? -1) == 3;
        GD.Print($"TRADETEST 重复请求={dup.Outcome}，状态不变={dupOk}");

        // 4. 库存不足：状态不变
        var shortStock = _stallService.TryBuy(seller.Id, buyer.Id, item, 999, _clock.Now, "traderun-2");
        bool stockOk = shortStock.Outcome ==
                       ClaudeCodeGameStudios.Application.Stall.StallService.BuyOutcome.InsufficientStock
                       && (_stallService.GetStall(seller.Id)?.Stock ?? -1) == 3
                       && buyer.Wallet.Amount(gold) == buyerGold1;

        // 5. 余额不足：把买家金币压到「买不起 2 份」的状态 → 拒绝且状态不变
        //    ⚠️ 断言的是**性质**（买不起），不是某个写死的余额 ——
        //    该留多少，取决于价格（占位值，开发者随时会改）。
        long poorKeep = Math.Min(buyer.Wallet.Amount(gold), gross2 - 1);
        long toSpend = buyer.Wallet.Amount(gold) - poorKeep;
        if (toSpend > 0) buyer.TrySpendResource(gold, toSpend);
        var poor = _stallService.TryBuy(seller.Id, buyer.Id, item, 2, _clock.Now, "traderun-3");
        bool poorOk = poor.Outcome ==
                      ClaudeCodeGameStudios.Application.Stall.StallService.BuyOutcome.CannotAfford
                      && buyer.Wallet.Amount(gold) < gross2
                      && (_stallService.GetStall(seller.Id)?.Stock ?? -1) == 3
                      && buyer.Bag.Count(item) == 2;

        GD.Print($"TRADETEST 库存不足={shortStock.Outcome}/{stockOk}，余额不足={poor.Outcome}/{poorOk}");
        GD.Print(buyOk && dupOk && stockOk && poorOk ? "TRADETEST PASS" : "TRADETEST FAIL");
    }

    /// <summary>
    /// <c>--market</c>：R-02 真机自检 —— 摊位面板背后的那条路。
    /// </summary>
    /// <remarks>
    /// <para><b>验的是"面板点下去会发生什么"</b>：候选买家怎么算、
    /// 「尽可能多」买多少、成交后双方的钱和货对不对、失败时状态动不动。
    /// 走的是生产代码路径（<see cref="ClaudeCodeGameStudios.Application.Stall.StallMarketService"/>
    /// → <see cref="ClaudeCodeGameStudios.Application.Stall.StallService.TryBuy"/>），不是复制品。</para>
    ///
    /// <para><b>为什么不用鼠标点</b>：自检跑在 headless 里，没有指针。
    /// 面板的「点摊位 → 点买家」两跳，等价于「取摊位 → 取候选 → 买」——
    /// 面板只是把玩家的点击翻译成这三步。验这三步，就验了面板的语义。</para>
    /// </remarks>
    private void RunMarketSelfTest()
    {
        _marketTestDone = true;

        if (_states.Count < 2)
        {
            GD.Print("MARKETTEST FAIL 英雄不足 2 人，无法验证买卖。");
            return;
        }

        var seller = _heroes[0];
        var buyer = _heroes[1];
        var item = new ClaudeCodeGameStudios.Domain.Items.ItemId("石料");
        var gold = ClaudeCodeGameStudios.Application.Stall.StallService.Gold;

        // ⚠️ **单价取自生效价格表**（P-14 的占位值，开发者随时会改）——
        // 而且「尽可能多买多少」本来就**取决于买家的钱与单价**（D-053 的规则：
        // min(摊位库存, 余额 ÷ 单价, 背包余量)）⇒ 期望份数必须**算出来**，
        // 不能写死「5 份」（那是单价恰好 5、买家恰好有 100 金时的巧合）。
        long unitPrice = _priceBook.PriceOf(item);
        if (unitPrice <= 0)
        {
            GD.Print("MARKETTEST FAIL 石料没有有效单价（价格表里 0 = 不卖）—— 夹具无法成交。");
            return;
        }

        long sellerGold0 = seller.Wallet.Amount(gold);
        long buyerGold0 = buyer.Wallet.Amount(gold);
        long villageGold0 = _villageModel.Get(gold);
        long buyerBag0 = buyer.Bag.Count(item);

        long expectedQty = Math.Min(
            Math.Min(5, buyerGold0 / unitPrice),
            ClaudeCodeGameStudios.Application.Stall.HeroBehaviorService.BagItemLimit - buyerBag0);
        if (expectedQty <= 0)
        {
            GD.Print($"MARKETTEST FAIL 买家买不起也装不下 1 份石料"
                     + $"（余额 {buyerGold0}，单价 {unitPrice}，背包已有 {buyerBag0}）—— 夹具不适用。");
            return;
        }
        long gross = expectedQty * unitPrice;
        // 村抽按「5% + 余数累计」算（D-023 / D-052）；余数取**成交前**的那个。
        var (expectedNet, expectedCut, _) = ClaudeCodeGameStudios.Domain.Stall.StallFee.Compute(
            gross, _stallService.VillageCutCarry);

        // 1. 配给 + 开摊：摊主有 5 份石料，摊上摆 5 份。
        SelfTestEnsurePresentAndIdle(seller);   // 夹具：摊主必须在场且空闲（否则偶发开摊失败）
        var grant = _itemGrant.Grant(seller.Id, item, 5);
        var placed = _stallService.PlaceStall(seller.Id, item, 5, _clock.Now);
        if (!grant.Granted || placed is null)
        {
            GD.Print("MARKETTEST FAIL 无法准备交易场景");
            return;
        }

        var stall = _stallService.GetStall(seller.Id);
        if (stall is null)
        {
            GD.Print("MARKETTEST FAIL 摊位丢失");
            return;
        }

        // 2. 候选买家：摊主本人不在列表里（在列表里就是"点了必然失败"）。
        var offers = _market.Offers(stall, _heroes);
        bool sellerNotCandidate = !offers.Any(o => o.Id == seller.Id);
        bool buyerIsCandidate = offers.Any(o => o.Id == buyer.Id);
        var buyerOffer = offers.FirstOrDefault(o => o.Id == buyer.Id);
        GD.Print($"MARKETTEST 摊位={ClaudeCodeGameStudios.Presentation.StallMarketText.StallLine(seller.Name, stall)}");
        GD.Print($"MARKETTEST 候选 {offers.Count} 人：摊主不在={sellerNotCandidate}，买家在={buyerIsCandidate}，" +
                 $"买家可买={buyerOffer.MaxQty} 份");

        // 3. 走面板：打开 → 点摊位 → 点买家 → 成交。
        //    这一段刻意**经过面板**，而不是直接调服务 ——
        //    「玩家点下去会发生什么」正是本轮要验的东西。
        //    面板内部最终调的就是 _market.BuyAsMuchAsPossible（唯一结算入口 TryBuy）。
        _stallPanel.Open();
        bool pickedStall = _stallPanel.SelectStall(0);
        int buyerIndex = offers.ToList().FindIndex(o => o.Id == buyer.Id);
        int buyerRows = _stallPanel.BuyerRowCount;
        bool pickedBuyer = buyerIndex >= 0 && _stallPanel.SelectBuyer(buyerIndex);
        GD.Print($"MARKETTEST 面板：选中摊位={pickedStall}，候选行数={buyerRows}，" +
                 $"选中买家={pickedBuyer}，回执=「{_stallPanel.ResultText}」");

        long stockAfter = _stallService.GetStall(seller.Id)?.Stock ?? -1;
        long sellerGold1 = seller.Wallet.Amount(gold);
        long buyerGold1 = buyer.Wallet.Amount(gold);
        long villageGold1 = _villageModel.Get(gold);

        bool buyOk = pickedStall && pickedBuyer && buyerRows >= 1
                     && buyerGold0 - buyerGold1 == gross
                     && sellerGold1 - sellerGold0 == expectedNet
                     && villageGold1 - villageGold0 == expectedCut
                     && buyer.Bag.Count(item) == buyerBag0 + expectedQty
                     && stockAfter == 5 - expectedQty;
        GD.Print($"MARKETTEST 购买后：买家金 {buyerGold0}→{buyerGold1}，" +
                 $"摊主金 {sellerGold0}→{sellerGold1}，村金 {villageGold0}→{villageGold1}，" +
                 $"买家背包={buyer.Bag.Count(item)}，摊位库存={stockAfter} → {buyOk}");

        // 4. 售罄后再买：摊位不再是 Open → 拒绝，且状态不变。
        var afterSoldOut = _market.BuyAsMuchAsPossible(stall, buyer.Id, _clock.Now);
        bool soldOutOk = !afterSoldOut.Bought
                         && buyer.Wallet.Amount(gold) == buyerGold1
                         && buyer.Bag.Count(item) == buyerBag0 + expectedQty;
        GD.Print($"MARKETTEST 售罄后再买={afterSoldOut.Outcome}，状态不变={soldOutOk}");

        // 5. 面板文案：R-04 的库存/单价确实上界面了。
        string line = ClaudeCodeGameStudios.Presentation.StallMarketText.StallLine(seller.Name, stall);
        bool lineOk = line.Contains("石料") && line.Contains("单价");
        GD.Print($"MARKETTEST 摊位行=「{line}」含物品与单价={lineOk}");

        GD.Print(sellerNotCandidate && buyerIsCandidate && buyOk && soldOutOk && lineOk
            ? "MARKETTEST PASS"
            : "MARKETTEST FAIL");
    }

    /// <summary>
    /// <c>--shop</c>：第四阶段 3B 真机自检 —— 有物品/有开放摊位时自主购物成交，同日不重复。
    /// </summary>
    /// <remarks>
    /// <para>走生产路径：<see cref="ItemGrantService.Grant"/> → <c>PlaceStall</c> →
    /// 逐日 <see cref="RunDailyShoppingIfNeeded"/>（跨天推进）→ 事件管线。</para>
    /// <para>最多推进 30 个游戏日等待成交（随机性格/投骰；30 天内未成交视为失败）；
    /// 成交后同日再调用一次，验证闸门不重复执行。</para>
    /// </remarks>
    private void RunShoppingSelfTest()
    {
        _shopTestDone = true;

        if (_states.Count < 2)
        {
            GD.Print("SHOPTEST FAIL 英雄不足 2 人");
            return;
        }

        var seller = _heroes[0];
        var buyer = _heroes[1];
        var item = new ClaudeCodeGameStudios.Domain.Items.ItemId("石料");
        var gold = ClaudeCodeGameStudios.Application.Stall.StallService.Gold;

        _itemGrant.Grant(seller.Id, item, 100);
        if (_stallService.GetStall(seller.Id) is null)
        {
            SelfTestEnsurePresentAndIdle(seller);   // 夹具：摊主必须在场且空闲（见该方法的说明）
            var placed = _stallService.PlaceStall(seller.Id, item, 100, _clock.Now);
            if (placed is null)
            {
                GD.Print("SHOPTEST FAIL 开摊失败（英雄可能不在场/在忙）");
                return;
            }
        }

        long buyerGold0 = buyer.Wallet.Amount(gold);
        long stock0 = _stallService.GetStall(seller.Id)?.Stock ?? -1;
        int events0 = _events.Count;
        int startDay = (int)_clock.Day;
        bool bought = false;
        int days = 0;

        while (!bought && days < 30)
        {
            _clock.SetSimTime(_clock.Now + _clock.SimSecondsPerDay);
            RunDailyShoppingIfNeeded();
            if (buyer.LastShoppingDay == (int)_clock.Day) bought = true;
            days++;
        }

        long stock1 = _stallService.GetStall(seller.Id)?.Stock ?? -1;
        long buyerGold1 = buyer.Wallet.Amount(gold);
        int events1 = _events.Count;

        // 同日再调一次：闸门应直接返回，不产生新事件/状态变化
        long goldCheck = buyerGold1;
        long stockCheck = stock1;
        int eventsCheck = events1;
        RunDailyShoppingIfNeeded();
        bool noRepeat = events1 == _events.Count
                        && buyer.Wallet.Amount(gold) == goldCheck
                        && (_stallService.GetStall(seller.Id)?.Stock ?? -1) == stockCheck;

        // M5：把成交当天的逐人结果打出来 —— 谁买了、谁跳过了、为什么。
        if (_lastShoppingResults.Count > 0)
        {
            PrintShoppingResults("SHOPTEST 明细", _lastShoppingDay, _lastShoppingResults);
        }

        bool stateOk = bought && buyerGold1 < buyerGold0 && stock1 < stock0 && events1 > events0;
        GD.Print($"SHOPTEST 第{startDay}→{_clock.Day}天/尝试{days}天：成交={bought}，" +
                 $"买家金 {buyerGold0}→{buyerGold1}，摊位库存 {stock0}→{stock1}，" +
                 $"事件 {events0}→{events1}");
        GD.Print($"SHOPTEST 同日重复调用无新增={noRepeat}");

        // ── R-07：想买没买成 的两条反馈 ──────────────────────────────────
        // 把买家的钱清零 → 他想买（欲望够）却买不成。
        // 这条验的是**玩家能看见**：花名册行尾有标记、杂谈里有一条汇总。
        //
        // 欲望是随机的（性格权重），所以像上面等成交那样**逐日推进直到命中** ——
        // 买家的钱一直是 0，一旦他动了念头，就必然落在「钱不够」上。
        buyer.TrySpendResource(gold, buyer.Wallet.Amount(gold));
        int chronBefore = _chronicle.Count;
        int missDays = 0;

        for (int i = 0; i < 30; i++)
        {
            _clock.SetSimTime(_clock.Now + _clock.SimSecondsPerDay);
            RunDailyShoppingIfNeeded();
            if (ShoppingNoteFor(buyer).Contains("钱不够")) { missDays = i + 1; break; }
        }

        string note = ShoppingNoteFor(buyer);
        string chronText = string.Join(" | ", _chronicle.Recent(_chronicle.Count - chronBefore));
        bool noteOk = note.Contains("钱不够");
        bool summaryOk = chronText.Contains("想买没买成");
        GD.Print($"SHOPTEST R-07 第{missDays}天命中：花名册标记=「{note}」→ {noteOk}");
        GD.Print($"SHOPTEST R-07 杂谈新增=「{chronText}」→ {summaryOk}");

        GD.Print(stateOk && noRepeat && noteOk && summaryOk ? "SHOPTEST PASS" : "SHOPTEST FAIL");
    }
}
