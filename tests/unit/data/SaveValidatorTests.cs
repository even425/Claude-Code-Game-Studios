using System;
using System.Collections.Generic;
using System.Linq;
using GdUnit4;
using ClaudeCodeGameStudios.Data;

/// <summary>
/// 存档校验的单元测试 —— M7 的第一道关卡。
///
/// <para><b>本文档保护：</b></para>
/// <ul>
/// <li><b>I11</b>：引用损坏时加载必须<b>失败并报出可理解的错误</b>，而不是部分恢复</li>
/// <li>基线 §13.7 明列：重复发奖、重复应用事件后果、装备丢失或重复、
/// <b>建筑坐标与占地冲突</b>、任务参与者丢失、已完成事件重新进入进行中</li>
/// <li><b>I4</b> 的存档侧：已结算键必须能落盘并往返</li>
/// <li><b>I12</b> 的存档侧：资源余额不得为负</li>
/// </ul>
///
/// <para><b>关键性质是「全有或全无」</b>：部分恢复会留下一个一半新一半旧的世界，
/// 那种状态<b>没有对应的合法玩法规则</b>，出错也无法复现。
/// 因此校验器<b>要么全部通过，要么给出全部错误</b>，绝不放行。</para>
///
/// <para><b>本文档不规定任何游戏数值。</b>格子坐标与资源名只是夹具。</para>
/// </summary>
[TestSuite]
public class SaveValidatorTests
{
    private static SaveGame Valid()
    {
        var save = new SaveGame { SimTime = 123.0 };
        save.Village.Id = "v1";
        save.Village.Resources["木材"] = 40;
        save.Village.Buildings.Add(new BuildingSave { Id = "土屋", X = 1, Y = 1, W = 1, H = 1, Progress = 1.0 });
        // 表现侧字段不能省：校验器拒收「性格为空」的英雄 —— 读档后画不出这个人。
        save.Heroes.Add(new HeroSave { Id = "h1", Name = "阿黄", Personality = "莽", Diligence = "随手" });
        save.Heroes.Add(new HeroSave { Id = "h2", Name = "夸九", Personality = "傲", Diligence = "肝" });
        save.Missions.Add(new MissionSave { Id = "m1", Status = "Succeeded", MemberIds = { "h1", "h2" } });
        save.AppliedSettlementKeys.Add("r1#br1");
        return save;
    }

    private static bool HasErrorFor(SaveValidationResult r, string fragment)
        => r.Errors.Any(e => e.Path.Contains(fragment, StringComparison.OrdinalIgnoreCase));

    // ── 正常路径 ────────────────────────────────────────────────────────────

    /// <summary>一份自洽的存档必须通过。</summary>
    [TestCase]
    public void Validate_AFullyConsistentSave_Passes()
    {
        var result = SaveValidator.Validate(Valid());

        Assertions.AssertThat(result.IsValid).IsTrue();
        Assertions.AssertThat(result.Errors.Count).IsEqual(0);
    }

    /// <summary>已结算键必须能完整往返 —— 不落它，读档后会重复发奖（I4）。</summary>
    [TestCase]
    public void Validate_CarriesAppliedSettlementKeys_ForIdempotencyAcrossSaveLoad()
    {
        var save = Valid();
        save.AppliedSettlementKeys.Add("r2#br2");

        var result = SaveValidator.Validate(save);

        Assertions.AssertThat(result.IsValid).IsTrue();
        Assertions.AssertThat(save.AppliedSettlementKeys.Count).IsEqual(2);
    }

    // ── I11 · 引用断了 ─────────────────────────────────────────────────────

    /// <summary>
    /// 任务成员指向不存在的英雄 → <b>加载必须失败</b>，并指出是哪个任务、哪个成员。
    /// </summary>
    [TestCase]
    public void Validate_DanglingMissionMember_IsRejected_WithAPointableError()
    {
        var save = Valid();
        save.Missions[0].MemberIds.Add("根本没有这个人");

        var result = SaveValidator.Validate(save);

        Assertions.AssertThat(result.IsValid).IsFalse();
        Assertions.AssertThat(HasErrorFor(result, "missions[0].memberIds")).IsTrue();
        Assertions.AssertThat(result.Errors[0].Message.Contains("根本没有这个人")).IsTrue();
    }

    /// <summary>重复的英雄标识必须被拒 —— 引用会指错人。</summary>
    [TestCase]
    public void Validate_DuplicateHeroId_IsRejected()
    {
        var save = Valid();
        save.Heroes.Add(new HeroSave { Id = "h1", Name = "冒名者", Personality = "贪" });

        var result = SaveValidator.Validate(save);

        Assertions.AssertThat(result.IsValid).IsFalse();
        Assertions.AssertThat(HasErrorFor(result, "heroes[2].id")).IsTrue();
    }

    /// <summary>重复的任务标识必须被拒。</summary>
    [TestCase]
    public void Validate_DuplicateMissionId_IsRejected()
    {
        var save = Valid();
        save.Missions.Add(new MissionSave { Id = "m1" });

        var result = SaveValidator.Validate(save);

        Assertions.AssertThat(result.IsValid).IsFalse();
        Assertions.AssertThat(HasErrorFor(result, "missions[1].id")).IsTrue();
    }

    // ── 建筑坐标与占地冲突（基线 §13.7 明列）────────────────────────────

    /// <summary>两栋建筑占用同一格 → 拒绝。</summary>
    [TestCase]
    public void Validate_BuildingsOverlappingTheSameCell_IsRejected()
    {
        var save = Valid();
        save.Village.Buildings.Add(new BuildingSave { Id = "井", X = 1, Y = 1, W = 1, H = 1, Progress = 1.0 });

        var result = SaveValidator.Validate(save);

        Assertions.AssertThat(result.IsValid).IsFalse();
        Assertions.AssertThat(HasErrorFor(result, "village.buildings[1]")).IsTrue();
    }

    /// <summary>
    /// <b>部分重叠</b>也要被拒 —— 不能只比较左下角坐标。
    /// <para>这条对应基线 §13.7 明列的「建筑坐标与占地冲突」。</para>
    /// </summary>
    [TestCase]
    public void Validate_BuildingsPartiallyOverlapping_IsRejected()
    {
        var save = Valid();
        save.Village.Buildings.Add(new BuildingSave { Id = "围栏", X = 0, Y = 1, W = 3, H = 1, Progress = 1.0 });

        var result = SaveValidator.Validate(save);

        Assertions.AssertThat(result.IsValid).IsFalse();
        Assertions.AssertThat(HasErrorFor(result, "village.buildings[1]")).IsTrue();
        // 「重叠」出现在 Message 里，不在 Path 里 —— 定位位置与说明原因分属两个字段
        Assertions.AssertThat(result.Errors.Any(e => e.Message.Contains("重叠"))).IsTrue();
    }

    /// <summary>相邻但不重叠是合法的。</summary>
    [TestCase]
    public void Validate_AdjacentBuildings_AreFine()
    {
        var save = Valid();
        save.Village.Buildings.Add(new BuildingSave { Id = "井", X = 2, Y = 1, W = 1, H = 1, Progress = 1.0 });

        var result = SaveValidator.Validate(save);

        Assertions.AssertThat(result.IsValid).IsTrue();
    }

    /// <summary>越界的建造进度必须被拒。</summary>
    [TestCase]
    public void Validate_OutOfRangeConstructionProgress_IsRejected()
    {
        var save = Valid();
        save.Village.Buildings[0].Progress = 1.7;

        var result = SaveValidator.Validate(save);

        Assertions.AssertThat(result.IsValid).IsFalse();
        Assertions.AssertThat(HasErrorFor(result, "progress")).IsTrue();
    }

    /// <summary>非正占地必须被拒 —— 否则占位冲突检测失效。</summary>
    [TestCase]
    public void Validate_NonPositiveFootprint_IsRejected()
    {
        var save = Valid();
        save.Village.Buildings[0].W = 0;

        var result = SaveValidator.Validate(save);

        Assertions.AssertThat(result.IsValid).IsFalse();
    }

    // ── I12 · 资源不为负 ───────────────────────────────────────────────────

    /// <summary>负的资源余额必须被拒。</summary>
    [TestCase]
    public void Validate_NegativeResourceBalance_IsRejected()
    {
        var save = Valid();
        save.Village.Resources["木材"] = -1;

        var result = SaveValidator.Validate(save);

        Assertions.AssertThat(result.IsValid).IsFalse();
        Assertions.AssertThat(HasErrorFor(result, "resources")).IsTrue();
    }

    // ── 版本 ────────────────────────────────────────────────────────────────

    /// <summary>
    /// 未知版本必须被拒 —— 没有对应的迁移入口时，宁可拒绝加载。
    /// <para>「假装能读懂一个不认识���格式」是存档最经典的损坏方式。</para>
    /// </summary>
    [TestCase]
    public void Validate_UnknownVersion_IsRejected()
    {
        var save = Valid();
        save.Version = new SaveVersion(99, 0);

        var result = SaveValidator.Validate(save);

        Assertions.AssertThat(result.IsValid).IsFalse();
        Assertions.AssertThat(HasErrorFor(result, "version")).IsTrue();
    }

    /// <summary>空存档必须被拒，且错误可理解。</summary>
    [TestCase]
    public void Validate_NullSave_IsRejected()
    {
        var result = SaveValidator.Validate(null);

        Assertions.AssertThat(result.IsValid).IsFalse();
        Assertions.AssertThat(result.Describe().Contains("存档为空")).IsTrue();
    }

    // ── 错误必须「可理解」（I11）──────────────────────────────────────────

    /// <summary>
    /// 每个错误都要能<b>指到具体位置</b>并说清错在哪 —— 否则玩家看到「存档损坏」也无从下手。
    /// </summary>
    [TestCase]
    public void Describe_ListsEveryProblem_WithPathAndReason()
    {
        var save = Valid();
        save.Missions[0].MemberIds.Add("幽灵");
        save.Village.Resources["木材"] = -5;
        save.Village.Buildings.Add(new BuildingSave { Id = "井", X = 1, Y = 1, W = 1, H = 1 });

        var result = SaveValidator.Validate(save);
        var text = result.Describe();

        Assertions.AssertThat(result.Errors.Count >= 3).IsTrue();
        Assertions.AssertThat(text.Contains("memberIds")).IsTrue();
        Assertions.AssertThat(text.Contains("resources")).IsTrue();
        Assertions.AssertThat(text.Contains("建筑")).IsTrue();
    }

    /// <summary>校验器要<b>一次报出全部问题</b>，而不是修一个报一个 —— 逐条报错等于让玩家反复试。</summary>
    [TestCase]
    public void Validate_ReportsAllProblemsAtOnce_NotJustTheFirst()
    {
        var save = Valid();
        save.Missions[0].MemberIds.Add("幽灵甲");
        save.Missions[0].MemberIds.Add("幽灵乙");
        save.Village.Resources["木材"] = -1;
        save.Village.Resources["布料"] = -2;

        var result = SaveValidator.Validate(save);

        Assertions.AssertThat(result.Errors.Count >= 4).IsTrue();
    }

    // ── 英雄属性的完整性（2026-10-18 随 HeroSave 扩充而加）─────────────────
    //
    // 这些规则加上去时，**8 条既有测试当场转红** —— 它们构造的是
    // 「性格为空」的英雄，而游戏里不可能存在这样的英雄。
    // 那是校验器在做它该做的事，不是夹具写错了。

    /// <summary>性格为空 → 拒收。读档后画不出这个人。</summary>
    [TestCase]
    public void Validate_WithEmptyPersonality_IsRejected()
    {
        var save = Valid();
        save.Heroes[0].Personality = "";

        var result = SaveValidator.Validate(save);

        Assertions.AssertThat(result.IsValid).IsFalse();
        Assertions.AssertThat(result.Errors.Any(e => e.Path.Contains("personality"))).IsTrue();
    }

    /// <summary>属性为负 → 拒收。</summary>
    [TestCase]
    public void Validate_WithANegativeAttribute_IsRejected()
    {
        var save = Valid();
        save.Heroes[0].Power = -1;

        var result = SaveValidator.Validate(save);

        Assertions.AssertThat(result.IsValid).IsFalse();
    }

    /// <summary>
    /// 性格修正不是正数 → 拒收（不变量 CR-1）。
    /// </summary>
    /// <remarks>
    /// <b>为什么这条重要</b>：<c>W(a,h,t) = P·M·B·S</c>，四个因子都严格为正
    /// 才保证 <c>W &gt; 0</c>（「性格不是二极管」）。一份把某个修正写成
    /// 0 的存档，读档后这个英雄对某类活动就<b>完全没有权重</b> ——
    /// 而游戏不会报错，只是「这个人再也不干那件事了」。
    /// </remarks>
    [TestCase]
    public void Validate_WithANonPositiveTraitModifier_IsRejected()
    {
        var save = Valid();
        save.Heroes[0].TraitModifiers["采集"] = 0.0;

        var result = SaveValidator.Validate(save);

        Assertions.AssertThat(result.IsValid).IsFalse();
        Assertions.AssertThat(result.Errors.Any(e => e.Path.Contains("traitModifiers"))).IsTrue();
    }

    /// <summary>偏好权重不是正数 → 拒收（同为 CR-1）。</summary>
    [TestCase]
    public void Validate_WithANonPositivePreferenceWeight_IsRejected()
    {
        var save = Valid();
        save.Heroes[0].PreferenceWeights["伐木"] = -1.0;

        var result = SaveValidator.Validate(save);

        Assertions.AssertThat(result.IsValid).IsFalse();
        Assertions.AssertThat(result.Errors.Any(e => e.Path.Contains("preferenceWeights"))).IsTrue();
    }

    /// <summary>
    /// 属性齐全的英雄 → 通过（别把上一组规则写过头）。
    /// </summary>
    /// <remarks>
    /// 反向用例。加了四条新规则而没有这条，就无法区分
    /// 「校验器变严了」与「校验器坏了、什么都拒」。
    /// </remarks>
    [TestCase]
    public void Validate_WithAFullyPopulatedHero_Passes()
    {
        var save = Valid();
        save.Heroes[0].Personality = "莽";
        save.Heroes[0].Diligence = "随手";
        save.Heroes[0].Power = 40;
        save.Heroes[0].Speed = 30;
        save.Heroes[0].Grit = 25;
        save.Heroes[0].Origin = "剑与魔法的大陆";
        save.Heroes[0].TraitModifiers["采集"] = 1.1;
        save.Heroes[0].PreferenceWeights["采集"] = 0.9;

        var result = SaveValidator.Validate(save);

        Assertions.AssertThat(result.IsValid).IsTrue();
    }

    // ── 编成（2026-10-18）─────────────────────────────────────────────────

    /// <summary>
    /// 队伍成员指向不存在的英雄 → 拒收（与任务成员同一条规则）。
    /// </summary>
    /// <remarks>
    /// 不存它，读档时只能「按在场者取前 N 个」重建 —— 那是<b>猜测</b>：
    /// 玩家挑好的搭配会悄悄换成另一批人，而游戏不会报错。
    /// 这条把「编成必须能被引用校验」钉住。
    /// </remarks>
    [TestCase]
    public void Validate_WithADanglingSquadMember_IsRejected()
    {
        var save = Valid();
        save.SquadMemberIds.Add("h-根本没这个人");

        var result = SaveValidator.Validate(save);

        Assertions.AssertThat(result.IsValid).IsFalse();
        Assertions.AssertThat(result.Errors.Any(e => e.Path.StartsWith("squadMemberIds"))).IsTrue();
    }

    /// <summary>编成合法时通过。</summary>
    [TestCase]
    public void Validate_WithAValidSquad_Passes()
    {
        var save = Valid();
        save.SquadMemberIds.Add("h1");
        save.SquadMemberIds.Add("h2");

        var result = SaveValidator.Validate(save);

        Assertions.AssertThat(result.IsValid).IsTrue();
    }
}
