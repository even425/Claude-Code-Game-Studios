using System.Collections.Generic;
using GdUnit4;
using ClaudeCodeGameStudios.Data;

namespace ClaudeCodeGameStudios.Tests.Data;

/// <summary>
/// SaveVersion (0,3) 升版验证（第三阶段 M6 / P3-D10）。
///
/// <para><b>本文档保护：</b></para>
/// <list type="bullet">
/// <item><see cref="SaveVersion.Current"/> 现在是 (0,3)（第二阶段是 (0,2)）。</item>
/// <item>旧档 (0,2) / (0,1) 整份拒绝（I11 + D-021/D-029 先例）—— <b>不</b>写迁移器。</item>
/// <item>新 <see cref="SaveGame.Relationships"/> / <see cref="SaveGame.AppliedRelationKeys"/>
/// 空集合不破校验（开局没有关系是合法状态）。</item>
/// <item><see cref="RelationshipSave"/> 字段名稳定（JSON 序列化按字段名往返）。</item>
/// </list>
/// </summary>
[TestSuite]
public class SaveVersion03Tests
{
    /// <summary>一份能通过其余校验的最小存档。</summary>
    private static SaveGame ValidSave(SaveVersion version) => new()
    {
        Version = version,
        Village = new VillageSave { Id = "v1" },
        Heroes = new List<HeroSave>
        {
            new()
            {
                Id = "h-alice",
                Name = "Alice",
                Personality = "莽",
                Diligence = "随手",
                Power = 1, Speed = 1, Grit = 1,
            },
        },
    };

    [TestCase]
    public void Current_Is_0_3()
    {
        Assertions.AssertThat(SaveVersion.Current.Major).IsEqual(0);
        Assertions.AssertThat(SaveVersion.Current.Minor).IsEqual(3);
    }

    /// <summary>第二阶段存档 (0,2) 被整份拒绝 —— 玩家现有存档会明确报版本错误。</summary>
    [TestCase]
    public void OldSave_0_2_IsRejected_NoMigration()
    {
        var r = SaveValidator.Validate(ValidSave(new SaveVersion(0, 2)));

        Assertions.AssertThat(r.IsValid).IsFalse();

        bool hasVersionError = false;
        foreach (var err in r.Errors)
        {
            if (err.Path.Contains("version") || err.Message.Contains("版本"))
            {
                hasVersionError = true;
                break;
            }
        }

        Assertions.AssertThat(hasVersionError).IsTrue();
    }

    [TestCase]
    public void OldSave_0_1_IsRejected_NoMigration()
        => Assertions.AssertThat(SaveValidator.Validate(ValidSave(new SaveVersion(0, 1))).IsValid).IsFalse();

    /// <summary>新游戏：没有关系、没有已应用关系键，正常通过。</summary>
    [TestCase]
    public void NewSave_0_3_WithoutRelationships_Passes()
    {
        var save = ValidSave(new SaveVersion(0, 3));

        var r = SaveValidator.Validate(save);

        Assertions.AssertThat(r.IsValid).IsTrue();
        Assertions.AssertThat(save.Relationships.Count).IsEqual(0);
        Assertions.AssertThat(save.AppliedRelationKeys.Count).IsEqual(0);
    }

    /// <summary>带合法关系的 0,3 存档通过。</summary>
    [TestCase]
    public void NewSave_0_3_WithRelationships_Passes()
    {
        var save = ValidSave(new SaveVersion(0, 3));
        save.Heroes.Add(new HeroSave
        {
            Id = "h-bob", Name = "Bob", Personality = "贪", Diligence = "肝",
            Power = 1, Speed = 1, Grit = 1,
        });
        save.Relationships = new List<RelationshipSave>
        {
            new() { FromId = "h-alice", ToId = "h-bob", Value = 12 },
            new() { FromId = "h-bob", ToId = "h-alice", Value = 4 },
        };
        save.AppliedRelationKeys = new List<string> { "social:h-alice->h-bob@10" };

        var r = SaveValidator.Validate(save);

        Assertions.AssertThat(r.IsValid).IsTrue();
    }

    [TestCase]
    public void RelationshipSave_DefaultsAreEmpty_NotNull()
    {
        var save = new SaveGame();

        Assertions.AssertThat(save.Relationships).IsNotNull();
        Assertions.AssertThat(save.Relationships.Count).IsEqual(0);
        Assertions.AssertThat(save.AppliedRelationKeys).IsNotNull();
        Assertions.AssertThat(save.AppliedRelationKeys.Count).IsEqual(0);

        var rel = new RelationshipSave();
        Assertions.AssertThat(rel.FromId).IsEqual("");
        Assertions.AssertThat(rel.ToId).IsEqual("");
        Assertions.AssertThat(rel.Value).IsEqual(0);
    }
}
