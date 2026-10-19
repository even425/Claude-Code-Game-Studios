using System;
using System.Collections.Generic;
using System.IO;
using GdUnit4;
using ClaudeCodeGameStudios.Data;

namespace ClaudeCodeGameStudios.Tests.Data;

/// <summary>
/// 第三阶段 M6：关系数据的存档校验与往返（P3-D10）。
/// </summary>
/// <remarks>
/// <para><b>依据：</b>基线 §13.7 + 不变量 I11 —— 引用断了整份拒绝，不部分恢复；
/// <c>07</c> D-036 / P3-D10 —— v0.3 新增 <see cref="SaveGame.Relationships"/> 与
/// <see cref="SaveGame.AppliedRelationKeys"/>，旧档拒绝。</para>
/// </remarks>
[TestSuite]
public class RelationshipSaveTests
{
    private static SaveGame TwoHeroSave()
    {
        return new SaveGame
        {
            Version = SaveVersion.Current,
            Village = new VillageSave { Id = "v1" },
            Heroes = new List<HeroSave>
            {
                new()
                {
                    Id = "h-alice", Name = "Alice", Personality = "莽", Diligence = "随手",
                    Power = 1, Speed = 1, Grit = 1,
                },
                new()
                {
                    Id = "h-bob", Name = "Bob", Personality = "贪", Diligence = "肝",
                    Power = 1, Speed = 1, Grit = 1,
                },
            },
            Relationships = new List<RelationshipSave>
            {
                new() { FromId = "h-alice", ToId = "h-bob", Value = 12 },
                new() { FromId = "h-bob", ToId = "h-alice", Value = 4 },
            },
            AppliedRelationKeys = new List<string> { "social:h-alice->h-bob@10" },
        };
    }

    private static string TempFile()
        => Path.Combine(Path.GetTempPath(), $"ccgs-rel-{Guid.NewGuid():N}.json");

    // ── 校验：引用与值域 ──────────────────────────────────────────────────

    /// <summary>关系引用不存在的英雄 → 整份拒绝（I11）。</summary>
    [TestCase]
    public void Validator_RejectsMissingHeroReference()
    {
        var save = TwoHeroSave();
        save.Relationships[0].ToId = "h-ghost";

        var r = SaveValidator.Validate(save);

        bool mentionsGhost = false;
        foreach (var err in r.Errors)
        {
            if (err.Message.Contains("h-ghost")) { mentionsGhost = true; break; }
        }

        Assertions.AssertThat(r.IsValid).IsFalse();
        Assertions.AssertThat(mentionsGhost).IsTrue();
    }

    /// <summary>自己与自己建立关系 → 拒绝。</summary>
    [TestCase]
    public void Validator_RejectsSelfRelation()
    {
        var save = TwoHeroSave();
        save.Relationships.Add(new RelationshipSave { FromId = "h-alice", ToId = "h-alice", Value = 3 });

        Assertions.AssertThat(SaveValidator.Validate(save).IsValid).IsFalse();
    }

    /// <summary>同一有向对被写了两遍 → 数据损坏，拒绝。</summary>
    [TestCase]
    public void Validator_RejectsDuplicateDirectedPair()
    {
        var save = TwoHeroSave();
        save.Relationships.Add(new RelationshipSave { FromId = "h-alice", ToId = "h-bob", Value = 1 });

        Assertions.AssertThat(SaveValidator.Validate(save).IsValid).IsFalse();
    }

    /// <summary>关系值越出 [-100, +100] → 拒绝。</summary>
    [TestCase]
    public void Validator_RejectsOutOfRangeValue()
    {
        var save = TwoHeroSave();
        save.Relationships[0].Value = 101;

        Assertions.AssertThat(SaveValidator.Validate(save).IsValid).IsFalse();
    }

    /// <summary>空串不能作为幂等键。</summary>
    [TestCase]
    public void Validator_RejectsEmptyAppliedRelationKey()
    {
        var save = TwoHeroSave();
        save.AppliedRelationKeys.Add(" ");

        Assertions.AssertThat(SaveValidator.Validate(save).IsValid).IsFalse();
    }

    // ── 往返（真实写盘 / 读盘）───────────────────────────────────────────

    /// <summary>
    /// 关系与幂等键经 <see cref="SavePort"/> 写盘、读盘后完全一致；
    /// 反向关系独立保存（I8）。
    /// </summary>
    [TestCase]
    public void SavePort_RoundTripsRelationshipsAndKeys()
    {
        string path = TempFile();
        try
        {
            var port = new SavePort(path);
            port.Write(TwoHeroSave());

            var loaded = port.Read();

            Assertions.AssertThat(loaded.Loaded).IsTrue();
            Assertions.AssertThat(loaded.Save.Version).IsEqual(SaveVersion.Current);
            Assertions.AssertThat(loaded.Save.Relationships.Count).IsEqual(2);

            var forward = loaded.Save.Relationships.Find(
                r => r.FromId == "h-alice" && r.ToId == "h-bob");
            var backward = loaded.Save.Relationships.Find(
                r => r.FromId == "h-bob" && r.ToId == "h-alice");

            Assertions.AssertThat(forward).IsNotNull();
            Assertions.AssertThat(forward.Value).IsEqual(12);
            Assertions.AssertThat(backward).IsNotNull();
            Assertions.AssertThat(backward.Value).IsEqual(4);

            Assertions.AssertThat(loaded.Save.AppliedRelationKeys.Count).IsEqual(1);
            Assertions.AssertThat(loaded.Save.AppliedRelationKeys[0])
                .IsEqual("social:h-alice->h-bob@10");
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    /// <summary>旧版 (0,2) 存档即使其它字段全对，也必须被整份拒绝。</summary>
    [TestCase]
    public void SavePort_RejectsOldVersionFile()
    {
        string path = TempFile();
        try
        {
            var old = TwoHeroSave();
            old.Version = new SaveVersion(0, 2);
            File.WriteAllText(path,
                System.Text.Json.JsonSerializer.Serialize(old,
                    new System.Text.Json.JsonSerializerOptions
                    {
                        WriteIndented = true,
                        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
                    }));

            var loaded = new SavePort(path).Read();

            Assertions.AssertThat(loaded.Loaded).IsFalse();
            Assertions.AssertThat(loaded.Describe().Contains("版本")).IsTrue();
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
