using System.Collections.Generic;
using System.Linq;
using GdUnit4;
using ClaudeCodeGameStudios.Application;
using ClaudeCodeGameStudios.Domain.Heroes;
using ClaudeCodeGameStudios.Domain.Items;
using ClaudeCodeGameStudios.Domain.Village;
using ClaudeCodeGameStudios.Tests.Items;

namespace ClaudeCodeGameStudios.Tests.Application;

/// <summary>
/// <b>建造的「半提交」与「钱的去处」</b>（D-108）。
/// </summary>
/// <remarks>
/// <para><b>它守的是两件事</b>：
/// <list type="number">
/// <item><b>建造不许半提交</b>：采购（向村民买建材）会花掉村库的现金
/// ⇒ 若不把两笔现金一起算清楚，扣款会<b>失败却被忽略</b>
/// ⇒ 建筑建成了、钱没扣 ⇒ <b>凭空造出 80 金</b> ⇒ 守恒律当场破。</item>
/// <item><b>花掉的钱要有去处</b>：它没有烧掉，而是<b>变成了建筑</b>
/// ⇒ 记进「村庄资产」（与村庄升级同一语义，D-090）⇒ 守恒律的左边不缺这一笔。</item>
/// </list></para>
/// <para>⚠️ <b>为什么这条现在才被看见</b>：采集器此前<b>从不盖房子</b>
/// ⇒ 这个缺陷一直在仓库里，却从没被触发过。
/// ⇒ 与 D-101（大事件从未结算）、D-104（永久加成不入档）同族：
/// <b>「没被触发过」看起来像「没问题」</b>。</para>
/// </remarks>
[TestSuite]
public class BuildingPurchaseAtomicityTests
{
    private static readonly ResourceId Gold = new("金币");
    private static readonly ResourceId Material = new("材料");
    private static readonly ItemId Stone = new("石料");

    /// <summary>
    /// 造一个「<b>钱会被采购吃掉</b>」的场景：村库现金刚好够建造费，
    /// 材料池**不够** ⇒ 建造要向村民买建材 ⇒ 采购会先花掉现金。
    /// </summary>
    /// <remarks>
    /// ⚠️ <b>材料池要留一点</b>（这里 10 份）：采购只能买它**买得起的那部分**
    /// （村库现金有限）⇒ 剩下的差额仍要从材料池里出 ⇒ 材料池若为 0，
    /// 就会在<b>材料那一步</b>被拒 ⇒ <b>根本走不到</b>「扣款失败」那条路
    /// ⇒ 守卫会变成装饰品（这正是本条最初踩到的坑）。
    /// </remarks>
    private static (BuildingService Service, Village Village, HeroAgent Seller)
        NewScene(long gold, int sellerStone, long villageMaterial = 0)
    {
        var village = new Village("test");
        village.Add(Gold, gold);
        // ⚠️ 材料池默认**空**（`Village.Add` 不许加 0）⇒ 建造只能向村民买。
        if (villageMaterial > 0) village.Add(Material, villageMaterial);
        village.TryPlace(new ClaudeCodeGameStudios.Domain.Village.BuildingInstance(
            new ClaudeCodeGameStudios.Domain.Village.BuildingId("village_square"),
            gridX: -5, gridY: -5, footprintW: 1, footprintH: 1,
            constructionProgress: 1.0,
            affectsAreaW: 11, affectsAreaH: 11));

        var seller = new HeroAgent { Name = "卖建材的" };
        seller.AssignId(new HeroId("h-seller"));
        seller.RestoreBag(new Inventory(new Dictionary<ItemId, long> { [Stone] = sellerStone }));
        seller.RestoreWallet(new Wallet(new Dictionary<ResourceId, long> { [Gold] = 0 }));

        var heroes = new List<HeroAgent> { seller };
        var procurement = new MaterialProcurement(village, TestPrices.Book, Gold, () => 0L);
        var service = new BuildingService(village, Gold, Material, procurement, () => heroes);

        return (service, village, seller);
    }

    /// <summary>
    /// <b>采购把钱吃掉 ⇒ 建造整体失败 ⇒ 一分钱都不许多出来。</b>
    /// </summary>
    /// <remarks>
    /// ⚠️ **这一条在修复前是绿的**（缺陷版）：旧写法先查「村库 ≥ 建造费」、
    /// 再提交采购 ⇒ 采购把现金花光 ⇒ 接着的扣款<b>失败却被忽略</b>
    /// ⇒ 建筑照建 ⇒ <b>凭空多出 80 金</b>（实测：200 天里 143 天守恒律对不上）。
    /// </remarks>
    [TestCase]
    public void ProcurementConsumesTheCash_BuildFailsAndCreatesNoMoney()
    {
        // 围栏要 80 金 + 40 材料；村库只有 100 金、材料池 20 份
        // ⇒ 建造要先向村民买建材（测试价 5 金/份 ⇒ 买得起 20 份 = 100 金）
        // ⇒ 采购把现金吃光 ⇒ 接着的扣款**失败** ⇒ 走回滚那条路。
        var (service, village, seller) = NewScene(gold: 100, sellerStone: 40, villageMaterial: 20);
        var fence = BuildableBuildings.Fence;

        bool started = service.TryStart(fence, workers: 3);

        Assertions.AssertThat(started).IsFalse();

        // ⚠️ **钱没少 ⇒ 也没多**（守恒律的左边不许凭空多出一笔）。
        Assertions.AssertThat(village.Get(Gold)).IsEqual(100);
        Assertions.AssertThat(village.Get(VillageAsset.Asset)).IsEqual(0);
        Assertions.AssertThat(village.Get(Material)).IsEqual(20);

        // 卖方的货与钱都没动（采购要么整体没发生，要么整体发生）。
        Assertions.AssertThat(seller.Bag.Count(Stone)).IsEqual(40);
        Assertions.AssertThat(seller.Wallet.Amount(Gold)).IsEqual(0);

        // 村子里也不该多出一栋在建建筑。
        Assertions.AssertThat(village.Buildings.Count(b => b.Id.Value == fence.Id.Value)).IsEqual(0);    }

    /// <summary>
    /// 建造成功 ⇒ 花掉的建造费<b>记进「村庄资产」</b>（钱变成了建筑，没被烧掉）。
    /// </summary>
    /// <remarks>
    /// ⚠️ 这一条是 D-090 的同一语义：**升级花掉的钱记成资产** ⇒
    /// 玩家盖房子花掉的钱也该记成资产 ⇒ 否则守恒律的左边少一笔。
    /// </remarks>
    [TestCase]
    public void SuccessfulBuild_RecordsTheSpentGoldAsVillageAsset()
    {
        var (service, village, _) = NewScene(gold: 1000, sellerStone: 0, villageMaterial: 1000);
        var fence = BuildableBuildings.Fence;

        bool started = service.TryStart(fence, workers: 3);

        Assertions.AssertThat(started).IsTrue();
        Assertions.AssertThat(village.Get(Gold)).IsEqual(1000 - fence.GoldCost);
        Assertions.AssertThat(village.Get(VillageAsset.Asset)).IsEqual(fence.GoldCost);
    }

    /// <summary>
    /// 落点非法 ⇒ <b>开工被拒</b>：钱没扣、账没记、建筑没建。
    /// </summary>
    /// <remarks>
    /// ⚠️ **如实说明**：<c>TryStartAt</c> 里那条「放置失败 ⇒ 退款」的路
    /// <b>不可达</b>（落点占用在扣款<b>之前</b>就被 <c>CanPlaceAt</c> 挡住了，
    /// 单线程下状态不会在中间变化）⇒ 它<b>不是被守卫覆盖的</b>，
    /// 所以本条只守「<b>可到达的那一段</b>：钱没扣、账没记」。
    /// </remarks>
    [TestCase]
    public void IllegalSpot_NothingHappens()
    {
        var (service, village, _) = NewScene(gold: 1000, sellerStone: 0, villageMaterial: 1000);
        var fence = BuildableBuildings.Fence;

        // 落在市集自己那一格 ⇒ 放不下。
        bool started = service.TryStartAt(fence, -5, -5, workers: 3);

        Assertions.AssertThat(started).IsFalse();
        Assertions.AssertThat(village.Get(Gold)).IsEqual(1000);
        Assertions.AssertThat(village.Get(VillageAsset.Asset)).IsEqual(0);
        Assertions.AssertThat(village.Get(Material)).IsEqual(1000);
    }
}
