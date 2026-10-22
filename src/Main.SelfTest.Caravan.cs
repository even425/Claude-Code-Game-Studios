using Godot;
using System;
using System.Linq;
using ClaudeCodeGameStudios.Application;
using ClaudeCodeGameStudios.Domain.Items;
using ClaudeCodeGameStudios.Domain.WorldEvents;

public partial class Main : Node2D
{
    /// <summary>
    /// <c>--caravan</c>：D-088 真机自检 —— <b>商队成交</b>那条路上钱到底怎么走。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么要有这条</b>：D-088 拍板「村库抽成 10% + 90% 记为销毁」，
    /// 而 <b>标定里商队成交一直是 0 笔</b>（headless 采集没人点商队）
    /// ⇒ 这条规则<b>只在单元测试里被验过</b>，真实游戏里<b>从没跑过一笔</b>。
    /// ⇒ 单测绿 ≠ 接线对（与 D-018「奖励进错桶」同一族风险）。</para>
    ///
    /// <para><b>验的是三件事</b>（金额必须**算出来**，不能写死）：
    /// <list type="number">
    /// <item>买家付全额 ⇒ 钱包少了「总额」。</item>
    /// <item><b>村库只拿到 10%</b>（向下取整），其余 <b>90% 记为销毁</b>
    /// ⇒ 两笔加起来正好是买家付的钱（<b>钱没有凭空多，也没有凭空少</b>）。</item>
    /// <item>货进背包、库存递减、<b>窗口关了就买不了</b>。</item>
    /// </list></para>
    ///
    /// <para>⚠️ **走生产代码路径**（<see cref="CaravanMarketService"/>），
    /// 不是复制品 —— 面板只是把玩家的点击翻译成调用。</para>
    /// </remarks>
    private void RunCaravanSelfTest()
    {
        _caravanTestDone = true;

        var gold = ClaudeCodeGameStudios.Application.Stall.StallService.Gold;
        var item = new ItemId(CaravanOfferBase.ItemId);

        // ① 触发一个商队实例（周期 11 天 ⇒ 用第 11 天触发）。
        var instance = _worldEvents.TriggerByPeriod(
            simDay: WorldEventCaravanPeriodDays, now: _clock.Now,
            participants: _states.Select(s => s.Id.Value).ToArray(),
            instanceIdFactory: (template, day) => $"selftest-{template.Id}-{day}",
            shouldTrigger: ShouldTrigger);

        if (instance is null)
        {
            GD.Print("CARAVANTEST FAIL 触发不到商队实例（触发过滤器把它挡了？）");
            return;
        }

        // ② 开摊（与游戏里同一段：D-081 的 OpenStall）。
        bool opened = _caravanMarket.OpenStall(instance, CaravanOfferBase);
        if (!opened)
        {
            GD.Print($"CARAVANTEST FAIL 商队开不了摊：「{CaravanOfferBase.ItemId}」不在价格表里");
            return;
        }

        // ③ 推进到「进行中」（窗口就是这一段 ⇒ 限时靠阶段机实现）。
        int guard = 0;
        while (instance.Phase != WorldEventPhase.Ongoing && guard++ < 20)
        {
            _worldEvents.Advance(instance.Id, 0.5);
        }
        if (instance.Phase != WorldEventPhase.Ongoing)
        {
            GD.Print($"CARAVANTEST FAIL 商队没进入「进行中」（停在 {instance.Phase}）");
            return;
        }

        // ④ 找一个买得起的英雄（每位英雄起手 100 金）。
        var buyer = _heroes.FirstOrDefault(h => h.Wallet.Amount(gold) > 0);
        if (buyer is null)
        {
            GD.Print("CARAVANTEST FAIL 没有英雄有钱，买不了");
            return;
        }

        var offer = _caravanMarket.OpenOffers().FirstOrDefault();
        if (offer.UnitPrice <= 0)
        {
            GD.Print("CARAVANTEST FAIL 商队没有有效报价");
            return;
        }

        long qty = Math.Min(2, offer.RemainingStock);
        long amount = offer.UnitPrice * qty;
        long expectedCut = (amount / 100) * ClaudeCodeGameStudios.Application.CaravanMarketService.VillageCutPercent
                         + (amount % 100) * ClaudeCodeGameStudios.Application.CaravanMarketService.VillageCutPercent / 100;
        long expectedDestroyed = amount - expectedCut;

        long buyerGold0 = buyer.Wallet.Amount(gold);
        long villageGold0 = _villageModel.Get(gold);
        long bag0 = buyer.Bag.Count(item);
        long destroyed0 = _caravanMarket.TotalDestroyedGold;

        var attempt = _caravanMarket.TryBuy(instance.Id, buyer.Id, qty, _clock.Now);
        if (!attempt.Succeeded)
        {
            GD.Print($"CARAVANTEST FAIL 买不了：{attempt.Outcome}");
            return;
        }

        long buyerGold1 = buyer.Wallet.Amount(gold);
        long villageGold1 = _villageModel.Get(gold);
        long bag1 = buyer.Bag.Count(item);
        long destroyed1 = _caravanMarket.TotalDestroyedGold;

        bool moneyOk = buyerGold0 - buyerGold1 == amount
                    && villageGold1 - villageGold0 == expectedCut
                    && destroyed1 - destroyed0 == expectedDestroyed
                    && expectedCut + expectedDestroyed == amount;
        bool goodsOk = bag1 == bag0 + qty
                    && attempt.Result.RemainingStock == offer.RemainingStock - qty;

        GD.Print($"CARAVANTEST 成交 {qty} 份「{offer.ItemId}」× {offer.UnitPrice} 金 = {amount} 金"
                 + $"（市价 {offer.MarketPrice}）");
        GD.Print($"CARAVANTEST 买家 {buyer.Name} 金 {buyerGold0}→{buyerGold1}（应少 {amount}），"
                 + $"背包 {bag0}→{bag1}，剩余库存 {offer.RemainingStock}→{attempt.Result.RemainingStock}");
        GD.Print($"CARAVANTEST 村库金 {villageGold0}→{villageGold1}（应得抽成 {expectedCut}），"
                 + $"记为销毁 {destroyed0}→{destroyed1}（应销毁 {expectedDestroyed}）");
        GD.Print($"CARAVANTEST 钱对={moneyOk} 货对={goodsOk} ⇒ "
                 + $"{(moneyOk && goodsOk ? "PASS" : "FAIL")}");

        // ⑤ 窗口关了就买不了（限时是阶段机给的，不是计时器）。
        while (instance.Phase == WorldEventPhase.Ongoing && guard++ < 40)
        {
            _worldEvents.Advance(instance.Id, 0.5);
        }
        var afterClosed = _caravanMarket.TryBuy(instance.Id, buyer.Id, 1, _clock.Now);
        bool closedOk = !afterClosed.Succeeded && afterClosed.Outcome == CaravanBuyOutcome.WindowClosed;
        GD.Print($"CARAVANTEST 窗口关闭后再买={afterClosed.Outcome}（应为 WindowClosed）⇒ "
                 + $"{(closedOk ? "PASS" : "FAIL")}");
    }
}
