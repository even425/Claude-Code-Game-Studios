using System;
using System.Collections.Generic;
using System.Linq;
using GdUnit4;
using ClaudeCodeGameStudios.Application;
using ClaudeCodeGameStudios.Domain.Heroes;
using ClaudeCodeGameStudios.Domain.Items;
using ClaudeCodeGameStudios.Domain.Village;
using ClaudeCodeGameStudios.Domain.WorldEvents;

namespace ClaudeCodeGameStudios.Tests.Application;

/// <summary>
/// <b>商队市场</b>（D-081）：限时、限量、低于市价的临时摊位。
/// </summary>
/// <remarks>
/// <para><b>它守的是开发者拍板的四条</b>：
/// <list type="number">
/// <item>商队摆摊卖货，<b>价格低于市价</b>，<b>限时</b>，<b>限量</b>。</item>
/// <item>英雄用<b>个人金币</b>买 ⇒ <b>钱进村金库</b>（守恒律）。</item>
/// <item>买超量 / 钱不够 ⇒ <b>状态完全不变</b>（I15 四步提交）。</item>
/// <item>读档后<b>限量的账还在</b>（不读回来就能白嫖）。</item>
/// </list>
/// <para>⚠️ <b>为什么限量必须随存档走</b>：不存 ⇒ 读一次档货就补满 ⇒
/// 玩家能靠读档无限进货 ⇒ <b>无限套利</b>（低买高卖）⇒ 限量形同虚设。</para>
/// </remarks>
[TestSuite]
public class CaravanMarketTests
{
    private static readonly ResourceId Gold = new("金币");

    /// <summary>一个村庄 + 一个英雄 + 一个事件簿。</summary>
    private sealed class Fixture
    {
        public Village Village { get; } = new("v1");
        public WorldEventBook Book { get; } = new();
        public HeroAgent Hero { get; }
        public CaravanMarketService Market { get; }
        public WorldEventInstance Instance { get; }

        public Fixture(PriceBook? prices = null, CaravanOffer? offer = null)
        {
            Hero = new HeroAgent
            {
                Name = "阿青",
                Personality = "莽",
                Diligence = "肝",
                Power = 1,
                Speed = 1,
                Grit = 1,
            };
            Hero.AssignId(new HeroId("h-1"));
            Hero.RestoreWallet(Hero.Wallet.TryAdd(Gold, 1000)!);

            Book.Register(WorldEventTemplates.Caravan(11, offer ?? new CaravanOffer("石料", 80, 30)));

            Market = new CaravanMarketService(
                findInstance: id => Book.Instances.FirstOrDefault(i => i.Id == id),
                listInstances: () => Book.Instances,
                prices: prices ?? TestPrices.Book,
                village: Village,
                heroLookup: id => id == Hero.Id ? Hero : null);

            Instance = Book.TriggerByPeriod(
                simDay: 11, now: 0.0, participants: new[] { "h-1" },
                (t, day) => $"inst-{t.Id}-{day}")!;
        }

        /// <summary>推进到「进行中」（商队开卖的那个阶段）。</summary>
        public void AdvanceToOngoing()
        {
            int guard = 0;
            while (Instance.Phase != WorldEventPhase.Ongoing && guard++ < 20)
            {
                Book.Advance(Instance.Id, 0.5);
            }
        }
    }

    // ── 开摊 ─────────────────────────────────────────────────────────────

    /// <summary>开摊：单价 = 市价 × 折扣（<b>断言具体的数</b>），且库存入状态。</summary>
    [TestCase]
    public void OpenStall_LocksPriceAndStock()
    {
        var f = new Fixture();

        bool opened = f.Market.OpenStall(f.Instance, new CaravanOffer("石料", 80, 30));

        Assertions.AssertThat(opened).IsTrue();
        // 石料市价 5（TestPrices.Book 的夹具价）⇒ 八折 = 5 × 80 / 100 = 4。
        Assertions.AssertThat(f.Instance.State[CaravanMarketService.StateUnitPrice]).IsEqual("4");
        Assertions.AssertThat(f.Instance.State[CaravanMarketService.StateStock]).IsEqual("30");
        Assertions.AssertThat(f.Instance.State[CaravanMarketService.StateItem]).IsEqual("石料");
    }

    /// <summary>单价<b>锁定</b>：后来改了价格表，已开的摊<b>不受影响</b>。</summary>
    [TestCase]
    public void OpenStall_PriceIsLocked_NotAffectedByLaterPriceChanges()
    {
        var f = new Fixture();
        f.Market.OpenStall(f.Instance, new CaravanOffer("石料", 80, 30));
        f.AdvanceToOngoing();

        // 换一张价格表（石料改成 100）—— 用**另一个服务**买，模拟「市价后来变了」。
        var other = new CaravanMarketService(
            findInstance: id => f.Book.Instances.FirstOrDefault(i => i.Id == id),
            listInstances: () => f.Book.Instances,
            prices: new PriceBook(new[] { new KeyValuePair<ItemId, long>(new ItemId("石料"), 100) }),
            village: f.Village,
            heroLookup: id => id == f.Hero.Id ? f.Hero : null);

        var attempt = other.TryBuy(f.Instance.Id, f.Hero.Id, qty: 1, simTime: 0.0);

        Assertions.AssertThat(attempt.Succeeded).IsTrue();
        // 成交价仍是开摊时锁定的 2（不是 100 × 0.8）。
        Assertions.AssertThat(attempt.Result.UnitPrice).IsEqual(4);
        Assertions.AssertThat(attempt.Result.TotalAmount).IsEqual(4);
    }

    /// <summary>商品不在价格表里 ⇒ <b>不开摊</b>（没什么可卖）。</summary>
    [TestCase]
    public void OpenStall_ItemNotSellable_DoesNotOpen()
    {
        var f = new Fixture();

        bool opened = f.Market.OpenStall(f.Instance, new CaravanOffer("龙涎香", 80, 30));

        Assertions.AssertThat(opened).IsFalse();
        Assertions.AssertThat(f.Instance.State.ContainsKey(CaravanMarketService.StateItem)).IsFalse();
    }

    // ── 买 ───────────────────────────────────────────────────────────────

    /// <summary>
    /// 买一单：<b>钱从英雄钱包扣</b>、<b>货进背包</b>、<b>钱分两路</b>（D-088）、<b>库存减</b>。
    /// </summary>
    /// <remarks>
    /// ⚠️ <b>钱分两路</b>（D-088 拍板）：<b>10% 进村库</b>、<b>90% 记为销毁</b>。
    /// ⚠️ **抽成向下取整**：金额很小时（例：4 金）⇒ 10% 不足 1 金 ⇒ 村库拿 0，
    /// 整笔都算销毁 —— 这是「不发明小数位」的直接后果（钱只有整金）。
    /// </remarks>
    [TestCase]
    public void Buy_TransfersMoneyToVillage_AndReducesStock()
    {
        var f = new Fixture();
        f.Market.OpenStall(f.Instance, new CaravanOffer("石料", 80, 30));
        f.AdvanceToOngoing();

        long goldBefore = f.Hero.Wallet.Amount(Gold);
        long villageBefore = f.Village.Get(Gold);
        long bagBefore = f.Hero.Bag.Count(new ItemId("石料"));

        var attempt = f.Market.TryBuy(f.Instance.Id, f.Hero.Id, qty: 1, simTime: 10.0);

        Assertions.AssertThat(attempt.Succeeded).IsTrue();
        Assertions.AssertThat(f.Hero.Wallet.Amount(Gold)).IsEqual(goldBefore - 4);
        // 4 金 × 10% = 0.4 ⇒ 向下取整 0 ⇒ 村库没拿到钱。
        Assertions.AssertThat(f.Village.Get(Gold)).IsEqual(villageBefore);
        Assertions.AssertThat(f.Hero.Bag.Count(new ItemId("石料"))).IsEqual(bagBefore + 1);
        Assertions.AssertThat(f.Instance.State[CaravanMarketService.StateStock]).IsEqual("29");
    }

    /// <summary>
    /// D-088：<b>钱分两路</b> —— 村库抽成 10%，其余 90% 记为销毁，
    /// 而<b>两笔加起来正好是买家付的钱</b>（守恒律：钱没有凭空多也没有凭空少）。
    /// </summary>
    [TestCase]
    public void Buy_SplitsMoneyIntoVillageCutAndDestroyed()
    {
        var f = new Fixture();
        f.Market.OpenStall(f.Instance, new CaravanOffer("石料", 80, 30));
        f.AdvanceToOngoing();

        long villageBefore = f.Village.Get(Gold);
        long goldBefore = f.Hero.Wallet.Amount(Gold);

        // 30 份 × 4 金 = 120 金 ⇒ 抽成 12，销毁 108。
        var attempt = f.Market.TryBuy(f.Instance.Id, f.Hero.Id, qty: 30, simTime: 10.0);

        Assertions.AssertThat(attempt.Succeeded).IsTrue();
        Assertions.AssertThat(attempt.Result.TotalAmount).IsEqual(120);
        Assertions.AssertThat(attempt.Result.VillageCut).IsEqual(12);
        Assertions.AssertThat(attempt.Result.Destroyed).IsEqual(108);

        Assertions.AssertThat(f.Village.Get(Gold)).IsEqual(villageBefore + 12);
        Assertions.AssertThat(f.Hero.Wallet.Amount(Gold)).IsEqual(goldBefore - 120);
        Assertions.AssertThat(f.Market.TotalDestroyedGold).IsEqual(108);

        // ⚠️ **总量守恒**：买家少的 = 村库多的 + 销毁的。
        Assertions.AssertThat(attempt.Result.VillageCut + attempt.Result.Destroyed)
            .IsEqual(attempt.Result.TotalAmount);
    }

    /// <summary>D-088：<b>累计销毁</b>要能累加（多笔成交 ⇒ 守恒律那一项要对得上）。</summary>
    [TestCase]
    public void Buy_AccumulatesDestroyedGold()
    {
        var f = new Fixture();
        f.Market.OpenStall(f.Instance, new CaravanOffer("石料", 80, 30));
        f.AdvanceToOngoing();

        f.Market.TryBuy(f.Instance.Id, f.Hero.Id, qty: 15, simTime: 10.0);
        f.Market.TryBuy(f.Instance.Id, f.Hero.Id, qty: 10, simTime: 20.0);

        // 60 的 90% = 54；40 的 90% = 36。
        Assertions.AssertThat(f.Market.TotalDestroyedGold).IsEqual(90);
    }

    /// <summary>买超量 ⇒ 失败，且<b>状态完全不变</b>（I15）。</summary>
    [TestCase]
    public void Buy_MoreThanStock_Fails_AndChangesNothing()
    {
        var f = new Fixture();
        f.Market.OpenStall(f.Instance, new CaravanOffer("石料", 80, 30));
        f.AdvanceToOngoing();

        long goldBefore = f.Hero.Wallet.Amount(Gold);
        long villageBefore = f.Village.Get(Gold);

        var attempt = f.Market.TryBuy(f.Instance.Id, f.Hero.Id, qty: 31, simTime: 10.0);

        Assertions.AssertThat(attempt.Outcome).IsEqual(CaravanBuyOutcome.InsufficientStock);
        Assertions.AssertThat(f.Hero.Wallet.Amount(Gold)).IsEqual(goldBefore);
        Assertions.AssertThat(f.Village.Get(Gold)).IsEqual(villageBefore);
        Assertions.AssertThat(f.Instance.State[CaravanMarketService.StateStock]).IsEqual("30");
    }

    /// <summary>钱不够 ⇒ 失败，且<b>状态完全不变</b>。</summary>
    [TestCase]
    public void Buy_WithoutEnoughGold_Fails_AndChangesNothing()
    {
        var f = new Fixture();
        f.Market.OpenStall(f.Instance, new CaravanOffer("石料", 80, 30));
        f.AdvanceToOngoing();
        f.Hero.RestoreWallet(f.Hero.Wallet.TrySpend(Gold, 999)!);   // 只剩 1 金

        long villageBefore = f.Village.Get(Gold);

        var attempt = f.Market.TryBuy(f.Instance.Id, f.Hero.Id, qty: 1, simTime: 10.0);

        Assertions.AssertThat(attempt.Outcome).IsEqual(CaravanBuyOutcome.CannotAfford);
        Assertions.AssertThat(f.Hero.Wallet.Amount(Gold)).IsEqual(1);
        Assertions.AssertThat(f.Village.Get(Gold)).IsEqual(villageBefore);
        Assertions.AssertThat(f.Instance.State[CaravanMarketService.StateStock]).IsEqual("30");
    }

    /// <summary>
    /// <b>限时</b>：事件离开「进行中」阶段 ⇒ 买不了。
    /// </summary>
    /// <remarks>
    /// ⚠️ **窗口就是阶段**（不另造计时器）⇒ 事件推进到「结算」时自动关窗。
    /// </remarks>
    [TestCase]
    public void Buy_AfterWindowClosed_Fails()
    {
        var f = new Fixture();
        f.Market.OpenStall(f.Instance, new CaravanOffer("石料", 80, 30));
        f.AdvanceToOngoing();

        // 推到「结算」⇒ 窗口关闭。
        int guard = 0;
        while (f.Instance.Phase == WorldEventPhase.Ongoing && guard++ < 10)
        {
            f.Book.Advance(f.Instance.Id, 0.5);
        }

        long goldBefore = f.Hero.Wallet.Amount(Gold);

        var attempt = f.Market.TryBuy(f.Instance.Id, f.Hero.Id, qty: 1, simTime: 10.0);

        Assertions.AssertThat(attempt.Outcome).IsEqual(CaravanBuyOutcome.WindowClosed);
        Assertions.AssertThat(f.Hero.Wallet.Amount(Gold)).IsEqual(goldBefore);
    }

    /// <summary>幂等：同一请求键买两次 ⇒ <b>只成交一次</b>。</summary>
    [TestCase]
    public void Buy_SameRequestKey_Twice_OnlyChargesOnce()
    {
        var f = new Fixture();
        f.Market.OpenStall(f.Instance, new CaravanOffer("石料", 80, 30));
        f.AdvanceToOngoing();

        long goldBefore = f.Hero.Wallet.Amount(Gold);

        var first = f.Market.TryBuy(f.Instance.Id, f.Hero.Id, qty: 1, simTime: 10.0, requestId: "req-1");
        var second = f.Market.TryBuy(f.Instance.Id, f.Hero.Id, qty: 1, simTime: 10.0, requestId: "req-1");

        Assertions.AssertThat(first.Succeeded).IsTrue();
        Assertions.AssertThat(second.Outcome).IsEqual(CaravanBuyOutcome.DuplicateRequest);
        Assertions.AssertThat(f.Hero.Wallet.Amount(Gold)).IsEqual(goldBefore - 4);
        Assertions.AssertThat(f.Instance.State[CaravanMarketService.StateStock]).IsEqual("29");
    }

    /// <summary>买失败的请求<b>不该</b>把幂等键用掉（否则重试就买不成了）。</summary>
    [TestCase]
    public void Buy_FailedRequest_DoesNotBurnTheRequestKey()
    {
        var f = new Fixture();
        f.Market.OpenStall(f.Instance, new CaravanOffer("石料", 80, 30));
        f.AdvanceToOngoing();

        // 先失败一次（钱不够）。
        f.Hero.RestoreWallet(f.Hero.Wallet.TrySpend(Gold, 999)!);
        var failed = f.Market.TryBuy(f.Instance.Id, f.Hero.Id, qty: 1, simTime: 10.0, requestId: "req-1");
        Assertions.AssertThat(failed.Outcome).IsEqual(CaravanBuyOutcome.CannotAfford);

        // 给钱之后重试同一个键 ⇒ 应该能成。
        f.Hero.RestoreWallet(f.Hero.Wallet.TryAdd(Gold, 100)!);
        var retry = f.Market.TryBuy(f.Instance.Id, f.Hero.Id, qty: 1, simTime: 10.0, requestId: "req-1");

        Assertions.AssertThat(retry.Succeeded).IsTrue();
    }

    // ── 在售列表 ─────────────────────────────────────────────────────────

    /// <summary>在售列表只列<b>处于「进行中」</b>的商队（限时由阶段机管）。</summary>
    [TestCase]
    public void OpenOffers_OnlyListsCaravansInOngoing()
    {
        var f = new Fixture();
        f.Market.OpenStall(f.Instance, new CaravanOffer("石料", 80, 30));

        Assertions.AssertThat(f.Market.OpenOffers().Count).IsEqual(0);   // 还在预兆

        f.AdvanceToOngoing();

        var offers = f.Market.OpenOffers();
        Assertions.AssertThat(offers.Count).IsEqual(1);
        Assertions.AssertThat(offers[0].ItemId).IsEqual("石料");
        Assertions.AssertThat(offers[0].UnitPrice).IsEqual(4);
        Assertions.AssertThat(offers[0].RemainingStock).IsEqual(30);
        Assertions.AssertThat(offers[0].MarketPrice).IsEqual(5);   // 玩家要能看出比市价便宜
    }

    // ── 存档往返 ─────────────────────────────────────────────────────────

    /// <summary>
    /// <b>存档往返</b>：买一单 → 导出实例 → 换一本簿读回来 ⇒ <b>剩余库存</b>与<b>已买过</b>都还在。
    /// </summary>
    /// <remarks>
    /// ⚠️ 这一条是「限量」能不能成立的关键：不存 ⇒ 读档后又是 30 份 ⇒ 无限套利。
    /// </remarks>
    [TestCase]
    public void SaveAndLoad_KeepsRemainingStock_AndPreventsRebuying()
    {
        var f = new Fixture();
        f.Market.OpenStall(f.Instance, new CaravanOffer("石料", 80, 30));
        f.AdvanceToOngoing();

        f.Market.TryBuy(f.Instance.Id, f.Hero.Id, qty: 1, simTime: 10.0, requestId: "req-1");

        // ── 存档（实例状态 + 已应用的后果键）──
        var saved = new ClaudeCodeGameStudios.Data.WorldEventSave
        {
            Id = f.Instance.Id,
            TemplateId = f.Instance.TemplateId,
            Phase = (int)f.Instance.Phase,
            AppliedConsequences = f.Instance.AppliedConsequences.ToList(),
            State = f.Instance.State.ToDictionary(kv => kv.Key, kv => kv.Value),
        };

        // ── 读档（换一个簿 + 另一个服务）──
        var book2 = new WorldEventBook();
        book2.Register(WorldEventTemplates.Caravan(11, new CaravanOffer("石料", 80, 30)));
        var restored = new WorldEventInstance
        {
            Id = saved.Id,
            TemplateId = saved.TemplateId,
        };
        restored.Phase = (WorldEventPhase)saved.Phase;
        foreach (var key in saved.AppliedConsequences) restored.MarkApplied(key);
        foreach (var (key, value) in saved.State) restored.SetState(key, value);
        book2.Restore(new[] { restored });

        var village2 = new Village("v2");
        var market2 = new CaravanMarketService(
            findInstance: id => book2.Instances.FirstOrDefault(i => i.Id == id),
            listInstances: () => book2.Instances,
            prices: TestPrices.Book,
            village: village2,
            heroLookup: id => id == f.Hero.Id ? f.Hero : null);

        // 库存是 29（不是 30）⇒ 读档没有把货补满。
        var offers = market2.OpenOffers();
        Assertions.AssertThat(offers.Count).IsEqual(1);
        Assertions.AssertThat(offers[0].RemainingStock).IsEqual(29);

        // 同一个请求键再买 ⇒ 幂等拦下（读档后不能重复成交同一单）。
        var again = market2.TryBuy(restored.Id, f.Hero.Id, qty: 1, simTime: 20.0, requestId: "req-1");
        Assertions.AssertThat(again.Outcome).IsEqual(CaravanBuyOutcome.DuplicateRequest);

        // 换一个键 ⇒ 能继续买，但只剩 29 份。
        var next = market2.TryBuy(restored.Id, f.Hero.Id, qty: 1, simTime: 20.0, requestId: "req-2");
        Assertions.AssertThat(next.Succeeded).IsTrue();
        Assertions.AssertThat(next.Result.RemainingStock).IsEqual(28);
    }
}
