using Godot;
using System;
using System.Linq;
using ClaudeCodeGameStudios.Domain.Village;

public partial class Main : Node2D
{
    /// <summary>
    /// <c>--tier</c>：D-099 真机自检 —— <b>层级附带建筑（井）</b>那条链。
    /// </summary>
    /// <remarks>
    /// <para><b>验的是四件事</b>（都是<b>接线层</b>，单测验不到）：
    /// <list type="number">
    /// <item><b>老档（没有井的存档）读回来 ⇒ 井被补记</b>（不升存档版本、不写迁移器）。</item>
    /// <item><b>读档 ⇒ 井还在</b>（它是村里的一栋建筑 ⇒ 必须入档）。</item>
    /// <item><b>补齐是幂等的</b>（读档后不该多出第二口井）。</item>
    /// <item><b>D-105：读档后采集产量真的变快</b>（井 = 采集点 ⇒ 实际采到的份数变多）。</item>
    /// </list></para>
    ///
    /// <para>⚠️ <b>顺序很重要</b>：<b>先</b>验老档（那时村里**还没有**井 ⇒ 判据才成立），
    /// <b>后</b>验常规存读档 ⇒ 否则「本来就有的井」会让老档那条判据失去区分力
    /// （这与 D-083 存档自检里「先搅乱再读回来」是同一条纪律）。</para>
    ///
    /// <para>⚠️ <b>为什么单测验不了</b>：「读档」这条链在表现层（<c>Main</c>）
    /// ⇒ 单测只能验规则（<c>TierBuildingService</c>）
    /// ⇒ <b>接线由本自检拉下来</b>（与 D-092 的战利品、D-088 的商队同款理由）。</para>
    /// </remarks>
    private void RunTierSelfTest()
    {
        _tierTestDone = true;

        // ① 开局：第 0 级 ⇒ 村里**没有**井。
        int wellsAtStart = _villageModel.Buildings.Count(b => b.Id.Value == "well");
        GD.Print($"TIERTEST 开局井数={wellsAtStart}（应为 0）");

        // ② **老档兼容**：造一份「层级是 2、但存档里没有井」的存档
        //    （模拟 D-099 之前的存档）⇒ 读档后必须**补记**回来。
        var selfTestSaves = SelfTestSaves();

        var oldSave = BuildSave();
        oldSave.VillageTier = 2;
        oldSave.Village.Buildings.RemoveAll(b => b.Id == "well");
        try
        {
            selfTestSaves.Write(oldSave);
        }
        catch (Exception ex)
        {
            GD.Print($"TIERTEST FAIL 写老档失败：{ex.GetType().Name} {ex.Message}");
            return;
        }

        var oldLoaded = selfTestSaves.Read();
        if (!oldLoaded.Loaded)
        {
            GD.Print($"TIERTEST FAIL 读老档失败：{oldLoaded.Describe()}");
            return;
        }

        ApplySave(oldLoaded.Save);

        int wellsAfterOldLoad = _villageModel.Buildings.Count(b => b.Id.Value == "well");
        var well = _villageModel.Buildings.FirstOrDefault(b => b.Id.Value == "well");
        bool atRightCell = well is not null && well.GridX == 5 && well.GridY == 0;
        bool oldSaveOk = wellsAfterOldLoad == 1 && atRightCell;

        GD.Print($"TIERTEST 读老档（存档里没井、层级 2）后井数={wellsAfterOldLoad}（应为 1 —— 补记），"
                 + $"落在 ({well?.GridX},{well?.GridY})（应为 (5,0)）");

        // ③ 常规存读档：井必须在存档里，且读回来还是一口。
        var save = BuildSave();
        bool wellInSave = save.Village.Buildings.Any(b => b.Id == "well");
        try
        {
            selfTestSaves.Write(save);
        }
        catch (Exception ex)
        {
            GD.Print($"TIERTEST FAIL 写盘失败：{ex.GetType().Name} {ex.Message}");
            return;
        }

        var loaded = selfTestSaves.Read();
        if (!loaded.Loaded)
        {
            GD.Print($"TIERTEST FAIL 读档失败：{loaded.Describe()}");
            return;
        }

        ApplySave(loaded.Save);

        int wellsAfterLoad = _villageModel.Buildings.Count(b => b.Id.Value == "well");
        bool saveOk = wellInSave && wellsAfterLoad == 1;

        GD.Print($"TIERTEST 存档里有井={wellInSave} ⇒ 读档后井数={wellsAfterLoad}（应为 1 —— 不重复）");

        bool ok = wellsAtStart == 0 && oldSaveOk && saveOk;

        // ④ **D-105：井 = 采集点** ⇒ 读档后**采集产量真的变快**（接线层，单测验不到）。
        //    ⚠️ 必须验「实际采到几份」而不只是「加成是几」——
        //    加成算对了却没接到采集上，是最可能出的错。
        long gatheringBonus = ClaudeCodeGameStudios.Domain.Village.GatheringPoints.BonusFor(_villageModel);
        var stoneItem = new ClaudeCodeGameStudios.Domain.Items.ItemId("石料");
        var gatherer = _heroes.FirstOrDefault(h => h.Bag.Count(stoneItem) == 0);
        long gathered = gatherer is null ? -1L : _gather.Gather(gatherer);
        long expectedGathered = ClaudeCodeGameStudios.Application.GatherService.GatheredAmountPerDay
                                + gatheringBonus;
        bool gatheringOk = gatheringBonus == 1 && gathered == expectedGathered;

        GD.Print($"TIERTEST D-105 采集加成={gatheringBonus}（应为 1 —— 井是采集点），"
                 + $"实际采到={gathered}（应为 {expectedGathered}）⇒ {gatheringOk}");

        ok = ok && gatheringOk;
        GD.Print($"TIERTEST 层级附带建筑（井）⇒ {(ok ? "PASS" : "FAIL")}");

        // ⑤ **D-107：铁匠铺**（层级第 4 级 ⇒ 采集 +1，与井**叠加** ⇒ 共 +2）。
        //    ⚠️ 同样要验「**实际采到几份**」⇒ 加成算对了却没接到采集上，是最可能出的错。
        // ⚠️ **走「升级完成」那个入口**（`CompleteUpgrade`）—— 真实游戏里 Main 调的也是它
        //    ⇒ 这样自检才覆盖**真实接线**（直接调 `EnsureForTier` 会绕开它）。
        _upgradeService.CompleteUpgrade(4);
        int shops = _villageModel.Buildings.Count(b => b.Id.Value == "blacksmith");
        var shop = _villageModel.Buildings.FirstOrDefault(b => b.Id.Value == "blacksmith");
        bool shopOk = shops == 1 && shop is not null && shop.GridX == 8 && shop.GridY == 0;

        long bonusAfterShop = ClaudeCodeGameStudios.Domain.Village.GatheringPoints.BonusFor(_villageModel);
        var stoneItem2 = new ClaudeCodeGameStudios.Domain.Items.ItemId("石料");
        var gatherer2 = _heroes.FirstOrDefault(h => h.Bag.Count(stoneItem2) == 0);
        long gathered2 = gatherer2 is null ? -1L : _gather.Gather(gatherer2);
        long expectedGathered2 = ClaudeCodeGameStudios.Application.GatherService.GatheredAmountPerDay
                                 + bonusAfterShop;
        bool gathering2Ok = bonusAfterShop == 2 && gathered2 == expectedGathered2;

        GD.Print($"TIERTEST D-107 铁匠铺数={shops}（应为 1），落在 ({shop?.GridX},{shop?.GridY})（应为 (8,0)）"
                 + $"⇒ {shopOk}；采集加成={bonusAfterShop}（应为 2 —— 井+铁匠铺叠加），"
                 + $"实际采到={gathered2}（应为 {expectedGathered2}）⇒ {gathering2Ok}");

        bool allOk = ok && shopOk && gathering2Ok;
        GD.Print($"TIERTEST 层级附带建筑（井 + 铁匠铺）⇒ {(allOk ? "PASS" : "FAIL")}");

        // ④ **证据图**：地图上要看得见那口井（它画在 (5,0)）。
        //    ⚠️ 这一步只为截图，不参与判定（截图工具另跑）。
        //    ⚠️ **可带层级**（`--tiershot=4`）⇒ 截第 4 级的图（那时铁匠铺也在）。
        var tierShotArg = OS.GetCmdlineUserArgs()
            .FirstOrDefault(a => a.StartsWith("--tiershot=", StringComparison.Ordinal));
        bool wantsTierShot = tierShotArg is not null
            || OS.GetCmdlineUserArgs().Contains("--tiershot");
        if (wantsTierShot)
        {
            int shotTier = 2;
            if (tierShotArg is not null
                && int.TryParse(tierShotArg.Substring("--tiershot=".Length), out int parsed))
            {
                shotTier = parsed;
            }

            _tier = shotTier;
            _village.SetTier(shotTier);
            ClaudeCodeGameStudios.Application.TierBuildingService.EnsureForTier(_villageModel, shotTier);
            Refresh();
            GD.Print($"TIERTEST 已切到第 {shotTier} 级（截图用）—— 井在 (5,0)"
                     + (shotTier >= 4 ? "、铁匠铺在 (8,0)" : "") + "。");
        }
    }
}
