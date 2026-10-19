using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GdUnit4;
using ClaudeCodeGameStudios.Data;

namespace ClaudeCodeGameStudios.Tests.Data;

/// <summary>
/// 存档端口的测试。
///
/// <para><b>本文档保护：</b>不变量 <b>I11</b>（引用断了必须拒绝加载，
/// <b>不是部分恢复</b>）· 基线 §13.7「已完成事件不会重复」所需的
/// <see cref="SaveGame.AppliedSettlementKeys"/> 完好往返 ·
/// 写盘原子性。</para>
///
/// <para><b>本文档不规定任何玩法数值。</b>存档里的一切数字都来自夹具，
/// 不来自设计。</para>
/// </summary>
[TestSuite]
public class SavePortTests
{
    private string _dir = "";

    // 每例一个独立目录 —— 测试之间不得互相依赖（.claude/rules：Isolation）
    //
    // 【踩过的坑】这里原先写的是 [Before] / [After]。那在 gdUnit4 里是
    // **套件级**的（整个测试类跑一次），逐个测试的是 [BeforeTest] / [AfterTest]。
    // 于是全类共用一个目录、共用同一个 "s.json" —— 前一个测试写下的文件
    // 被后一个读到，「文件不存在」那条用例因此变成假绿灯。
    // 属性名与作用域是从 GdUnit4Api.dll 里读出来的，不是猜的。
    [BeforeTest]
    public void MakeDir()
    {
        _dir = Path.Combine(Path.GetTempPath(), "saveport-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    [AfterTest]
    public void DropDir()
    {
        try { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); }
        catch (IOException) { /* 清理失败不该让测试变红 —— 那是临时目录 */ }
    }

    private SavePort Port(string name = "s.json") => new(Path.Combine(_dir, name));

    /// <summary>一份结构完整、应当通过校验的存档。</summary>
    private static SaveGame GoodSave() => new()
    {
        Version = SaveVersion.Current,
        SimTime = 123.5,
        RandomState = "987654321",
        Village = new VillageSave
        {
            Id = "村庄",
            Resources = new Dictionary<string, long> { ["材料"] = 78 },
            Buildings = new List<BuildingSave>
            {
                new() { Id = "井", X = 2, Y = 3, W = 1, H = 1, Progress = 1.0 },
            },
        },
        Heroes = new List<HeroSave>
        {
            // 表现侧字段不能省：校验器会拒收「性格为空」的英雄
            // （读档后画不出这个人）。真实英雄一定有这些。
            new() { Id = "h-甲", Name = "甲", Presence = "InVillage",
                    Personality = "莽", Diligence = "随手", Power = 40, Speed = 30, Grit = 25 },
            new() { Id = "h-乙", Name = "乙", Presence = "OnMission",
                    Personality = "傲", Diligence = "肝", Power = 55, Speed = 20, Grit = 40 },
        },
        Missions = new List<MissionSave>
        {
            new() { Id = "m1", Status = "Active", MemberIds = { "h-甲" } },
        },
        AppliedSettlementKeys = new List<string> { "r1#br1", "r1#br2" },
    };

    // ── 往返 ───────────────────────────────────────────────────────────────

    /// <summary>写入再读出，关键字段逐项相同。</summary>
    [TestCase]
    public void WriteThenRead_RoundTripsTheFieldsThatMatter()
    {
        // Arrange
        var port = Port();
        var original = GoodSave();

        // Act
        port.Write(original);
        var loaded = port.Read();

        // Assert
        Assertions.AssertThat(loaded.Loaded).IsTrue();
        var save = loaded.Save!;
        Assertions.AssertThat(save.Version).IsEqual(original.Version);
        Assertions.AssertThat(save.SimTime).IsEqual(original.SimTime);
        Assertions.AssertThat(save.RandomState).IsEqual(original.RandomState);
        Assertions.AssertThat(save.Village.Id).IsEqual("村庄");
        Assertions.AssertThat(save.Village.Resources["材料"]).IsEqual(78L);
        Assertions.AssertThat(save.Heroes.Count).IsEqual(2);
        Assertions.AssertThat(save.Heroes[1].Presence).IsEqual("OnMission");
        Assertions.AssertThat(save.Missions[0].MemberIds[0]).IsEqual("h-甲");
    }

    /// <summary>
    /// 已结算键必须完好往返 —— <b>基线 §13.7「已完成事件不会重复」全靠它</b>。
    /// </summary>
    /// <remarks>
    /// 这条一旦断了，读档后同一批结算会被<b>重复发放</b>（I4），
    /// 而且症状是「读档后材料暴涨」，极难归因。
    /// </remarks>
    [TestCase]
    public void WriteThenRead_AppliedSettlementKeys_SurviveIntact()
    {
        // Arrange
        var port = Port();

        // Act
        port.Write(GoodSave());
        var save = port.Read().Save!;

        // Assert
        var keys = save.AppliedSettlementKeys.OrderBy(k => k, StringComparer.Ordinal).ToArray();
        Assertions.AssertThat(keys.Length).IsEqual(2);
        Assertions.AssertThat(keys[0]).IsEqual("r1#br1");
        Assertions.AssertThat(keys[1]).IsEqual("r1#br2");
    }

    /// <summary>
    /// 编成名单必须完好往返 —— 玩家的配队是<b>玩家的决定</b>，
    /// 读档把它换成另一批人就是把他没同意过的事执行了一遍。
    /// </summary>
    [TestCase]
    public void WriteThenRead_SquadMemberIds_SurviveIntact()
    {
        // Arrange
        var port = Port();
        var original = GoodSave();
        original.SquadMemberIds.Add("h-甲");

        // Act
        port.Write(original);
        var save = port.Read().Save!;

        // Assert
        Assertions.AssertThat(save.SquadMemberIds.Count).IsEqual(1);
        Assertions.AssertThat(save.SquadMemberIds[0]).IsEqual("h-甲");
    }

    /// <summary>英雄的两套描述都必须往返 —— 只存一半读出来就不是原来那个人。</summary>
    [TestCase]
    public void WriteThenRead_HeroAttributes_SurviveIntact()
    {
        // Arrange
        var port = Port();
        var original = GoodSave();
        original.Heroes[0].Origin = "剑与魔法的大陆";
        original.Heroes[0].TraitModifiers["采集"] = 1.1;
        original.Heroes[0].PreferenceWeights["伐木"] = 0.85;

        // Act
        port.Write(original);
        var hero = port.Read().Save!.Heroes[0];

        // Assert —— 表现侧
        Assertions.AssertThat(hero.Personality).IsEqual("莽");
        Assertions.AssertThat(hero.Diligence).IsEqual("随手");
        Assertions.AssertThat(hero.Power).IsEqual(40);
        Assertions.AssertThat(hero.Speed).IsEqual(30);
        Assertions.AssertThat(hero.Grit).IsEqual(25);
        // 领域侧
        Assertions.AssertThat(hero.Origin).IsEqual("剑与魔法的大陆");
        Assertions.AssertThat(hero.TraitModifiers["采集"]).IsEqual(1.1);
        Assertions.AssertThat(hero.PreferenceWeights["伐木"]).IsEqual(0.85);
    }

    // ── I11 · 整份拒绝，不部分恢复 ─────────────────────────────────────────

    /// <summary>文件不存在 → 明确失败，且给出人能懂的理由。</summary>
    [TestCase]
    public void Read_WithNoFile_FailsWithAReadableReason()
    {
        // Act
        var loaded = Port().Read();

        // Assert
        Assertions.AssertThat(loaded.Loaded).IsFalse();
        Assertions.AssertThat(loaded.Save is null).IsTrue();
        Assertions.AssertThat(loaded.Describe()).IsNotEmpty();
    }

    /// <summary>JSON 坏了 → 拒绝，<b>不尝试捞字段</b>。</summary>
    [TestCase]
    public void Read_WithCorruptJson_FailsRatherThanSalvaging()
    {
        // Arrange
        var port = Port();
        File.WriteAllText(port.FilePath, "{ 这不是 JSON ");

        // Act
        var loaded = port.Read();

        // Assert
        Assertions.AssertThat(loaded.Loaded).IsFalse();
        Assertions.AssertThat(loaded.Save is null).IsTrue();
    }

    /// <summary>
    /// 引用断了（任务成员指向不存在的英雄）→ <b>整份拒绝</b>，
    /// 连没坏的那些字段也不给（不变量 I11）。
    /// </summary>
    /// <remarks>
    /// 这是本文档最重要的一条。「部分恢复」看起来是宽容，
    /// 实际会造成<b>看不出来的不一致</b>：玩家少了一个人却没察觉，
    /// 而任务还记着那个人的名字。
    /// </remarks>
    [TestCase]
    public void Read_WithABrokenReference_ReturnsNullSave_EvenThoughOtherFieldsAreFine()
    {
        // Arrange —— 只有一处坏了，其余全对
        var port = Port();
        var broken = GoodSave();
        broken.Missions[0].MemberIds[0] = "h-根本不存在";
        port.Write(broken);

        // Act
        var loaded = port.Read();

        // Assert
        Assertions.AssertThat(loaded.Loaded).IsFalse();
        // 关键：Save 为 null —— 不是「返回一个缺了成员的任务」
        Assertions.AssertThat(loaded.Save is null).IsTrue();
        Assertions.AssertThat(loaded.Validation.IsValid).IsFalse();
        Assertions.AssertThat(loaded.Validation.Errors.Count).IsEqual(1);
        Assertions.AssertThat(loaded.Validation.Errors[0].Path).IsEqual("missions[0].memberIds[0]");
    }

    /// <summary>版本对不上 → 拒绝（没有迁移入口时，宁可不加载）。</summary>
    [TestCase]
    public void Read_WithAVersionMismatch_IsRejected()
    {
        // Arrange
        var port = Port();
        var future = GoodSave();
        future.Version = new SaveVersion(9, 9);
        port.Write(future);

        // Act / Assert
        var loaded = port.Read();
        Assertions.AssertThat(loaded.Loaded).IsFalse();
        Assertions.AssertThat(loaded.Save is null).IsTrue();
    }

    /// <summary>资源为负 → 拒绝（I12 在存档侧的表现）。</summary>
    [TestCase]
    public void Read_WithNegativeResources_IsRejected()
    {
        // Arrange
        var port = Port();
        var bad = GoodSave();
        bad.Village.Resources["材料"] = -1;
        port.Write(bad);

        // Act / Assert
        var loaded = port.Read();
        Assertions.AssertThat(loaded.Loaded).IsFalse();
        Assertions.AssertThat(loaded.Save is null).IsTrue();
    }

    /// <summary>建筑格位重叠 → 拒绝（基线 §13.7 明列）。</summary>
    [TestCase]
    public void Read_WithOverlappingBuildings_IsRejected()
    {
        // Arrange
        var port = Port();
        var bad = GoodSave();
        bad.Village.Buildings.Add(new BuildingSave
        {
            Id = "重叠的屋", X = 2, Y = 3, W = 2, H = 2, Progress = 0.5,
        });
        port.Write(bad);

        // Act / Assert
        var loaded = port.Read();
        Assertions.AssertThat(loaded.Loaded).IsFalse();
        Assertions.AssertThat(loaded.Save is null).IsTrue();
    }

    // ── 写盘原子性 ─────────────────────────────────────────────────────────

    /// <summary>写完之后不留临时文件。</summary>
    [TestCase]
    public void Write_LeavesNoTemporaryFileBehind()
    {
        // Arrange
        var port = Port();

        // Act
        port.Write(GoodSave());

        // Assert
        Assertions.AssertThat(File.Exists(port.FilePath)).IsTrue();
        Assertions.AssertThat(File.Exists(port.FilePath + ".tmp")).IsFalse();
    }

    /// <summary>连续写两次，第二次的内容生效（不留旧数据）。</summary>
    [TestCase]
    public void Write_Twice_KeepsOnlyTheLatestContent()
    {
        // Arrange
        var port = Port();
        port.Write(GoodSave());

        var second = GoodSave();
        second.Village.Resources["材料"] = 5;
        second.AppliedSettlementKeys = new List<string>();

        // Act
        port.Write(second);
        var save = port.Read().Save!;

        // Assert
        Assertions.AssertThat(save.Village.Resources["材料"]).IsEqual(5L);
        Assertions.AssertThat(save.AppliedSettlementKeys.Count).IsEqual(0);
    }

    // ── 边界 ───────────────────────────────────────────────────────────────

    /// <summary>删除是幂等的：删两次不报错。</summary>
    [TestCase]
    public void Delete_IsIdempotent()
    {
        // Arrange
        var port = Port();
        port.Write(GoodSave());

        // Act
        port.Delete();
        port.Delete();

        // Assert
        Assertions.AssertThat(port.Exists).IsFalse();
    }

    /// <summary>空路径必须被拒 —— 否则会写到当前目录去。</summary>
    [TestCase]
    public void Constructor_WithABlankPath_Throws()
    {
        // Act
        bool threw = false;
        try { new SavePort("   "); }
        catch (ArgumentException) { threw = true; }

        // Assert
        Assertions.AssertThat(threw).IsTrue();
    }

    /// <summary>写 null 必须被拒。</summary>
    [TestCase]
    public void Write_WithNull_Throws()
    {
        // Act
        bool threw = false;
        try { Port().Write(null); }
        catch (ArgumentNullException) { threw = true; }

        // Assert
        Assertions.AssertThat(threw).IsTrue();
    }
}
