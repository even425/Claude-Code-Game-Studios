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
    /// <para><b>验的是三件事</b>（都是<b>接线层</b>，单测验不到）：
    /// <list type="number">
    /// <item><b>老档（没有井的存档）读回来 ⇒ 井被补记</b>（不升存档版本、不写迁移器）。</item>
    /// <item><b>读档 ⇒ 井还在</b>（它是村里的一栋建筑 ⇒ 必须入档）。</item>
    /// <item><b>补齐是幂等的</b>（读档后不该多出第二口井）。</item>
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
        GD.Print($"TIERTEST 层级附带建筑 ⇒ {(ok ? "PASS" : "FAIL")}");

        // ④ **证据图**：地图上要看得见那口井（它画在 (5,0)）。
        //    ⚠️ 这一步只为截图，不参与判定（截图工具另跑）。
        if (OS.GetCmdlineUserArgs().Contains("--tiershot"))
        {
            _tier = 2;
            _village.SetTier(2);
            Refresh();
            GD.Print("TIERTEST 已切到第 2 级（截图用）—— 井在 (5,0)。");
        }
    }
}
