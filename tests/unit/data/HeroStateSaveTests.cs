using System;
using System.Collections.Generic;
using System.IO;
using GdUnit4;
using ClaudeCodeGameStudios.Data;

namespace ClaudeCodeGameStudios.Tests.Data;

/// <summary>
/// R-03 回归：读档必须恢复英雄的在场性 / 进行中的行动 / 目标 / 首通标记。
/// </summary>
/// <remarks>
/// <para><b>原缺陷</b>：<c>BuildSave</c> 存了 <c>Presence/CurrentActivity/RemainingActionSeconds/
/// HasCompletedFirstDungeon</c>，<c>ApplySave</c> 却只造了一个空 <c>HeroState</c> ——
/// 读档后随队的人变回在村、进行中的行动凭空消失。</para>
/// <para>本测试覆盖「DTO 能往返」与「非法值整份拒绝」两段；
/// 「<c>Main.ApplySave</c> 真的把它们写回 HeroState」由运行时 <c>--save/--load</c> 自检覆盖。</para>
/// </remarks>
[TestSuite]
public class HeroStateSaveTests
{
    private static SaveGame SaveWith(HeroSave hero)
    {
        return new SaveGame
        {
            Version = SaveVersion.Current,
            Village = new VillageSave { Id = "v1" },
            Heroes = new List<HeroSave> { hero },
        };
    }

    private static HeroSave Hero() => new()
    {
        Id = "h-alice",
        Name = "Alice",
        Personality = "莽",
        Diligence = "随手",
        Power = 1, Speed = 1, Grit = 1,
    };

    private static string TempFile()
        => Path.Combine(Path.GetTempPath(), $"ccgs-herostate-{Guid.NewGuid():N}.json");

    // ── 往返 ──────────────────────────────────────────────────────────────

    /// <summary>行动 / 在场性 / 目标 / 首通标记经 SavePort 写读后一字不差。</summary>
    [TestCase]
    public void SavePort_RoundTripsHeroRuntimeState()
    {
        string path = TempFile();
        try
        {
            var save = SaveWith(new HeroSave
            {
                Id = "h-alice", Name = "Alice", Personality = "莽", Diligence = "随手",
                Power = 1, Speed = 1, Grit = 1,
                Presence = "InVillage",
                CurrentActivity = "社交",
                RemainingActionSeconds = 3.5,
                TargetId = "h-bob",
                HasCompletedFirstDungeon = true,
            });
            save.Heroes.Add(new HeroSave
            {
                Id = "h-bob", Name = "Bob", Personality = "贪", Diligence = "肝",
                Power = 1, Speed = 1, Grit = 1,
            });

            var port = new SavePort(path);
            port.Write(save);
            var loaded = port.Read();

            Assertions.AssertThat(loaded.Loaded).IsTrue();
            var h = loaded.Save.Heroes[0];
            Assertions.AssertThat(h.Presence).IsEqual("InVillage");
            Assertions.AssertThat(h.CurrentActivity).IsEqual("社交");
            Assertions.AssertThat(h.RemainingActionSeconds).IsEqual(3.5);
            Assertions.AssertThat(h.TargetId).IsEqual("h-bob");
            Assertions.AssertThat(h.HasCompletedFirstDungeon).IsTrue();
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    // ── 校验：非法状态整份拒绝 ────────────────────────────────────────────

    [TestCase]
    public void Validator_RejectsUnknownPresence()
    {
        var hero = Hero();
        hero.Presence = "Sleeping";

        Assertions.AssertThat(SaveValidator.Validate(SaveWith(hero)).IsValid).IsFalse();
    }

    [TestCase]
    public void Validator_RejectsNegativeRemainingSeconds()
    {
        var hero = Hero();
        hero.CurrentActivity = "采集";
        hero.RemainingActionSeconds = -1.0;

        Assertions.AssertThat(SaveValidator.Validate(SaveWith(hero)).IsValid).IsFalse();
    }

    /// <summary>随队的人不应同时挂着日常行动 —— 那是两个互斥状态。</summary>
    [TestCase]
    public void Validator_RejectsOnMissionWithActivity()
    {
        var hero = Hero();
        hero.Presence = "OnMission";
        hero.CurrentActivity = "采集";

        Assertions.AssertThat(SaveValidator.Validate(SaveWith(hero)).IsValid).IsFalse();
    }

    /// <summary>目标引用断了 → 拒绝（目标可以排在列表后面，必须前向校验）。</summary>
    [TestCase]
    public void Validator_RejectsUnknownTarget_EvenWhenLaterInList()
    {
        var save = SaveWith(new HeroSave
        {
            Id = "h-alice", Name = "Alice", Personality = "莽", Diligence = "随手",
            Power = 1, Speed = 1, Grit = 1,
            CurrentActivity = "社交",
            RemainingActionSeconds = 2.0,
            TargetId = "h-bob",
        });
        save.Heroes.Add(new HeroSave
        {
            Id = "h-bob", Name = "Bob", Personality = "贪", Diligence = "肝",
            Power = 1, Speed = 1, Grit = 1,
        });

        // 目标在后一位且存在 —— 必须通过（前向引用不能被误判）。
        Assertions.AssertThat(SaveValidator.Validate(save).IsValid).IsTrue();

        // 目标不存在 —— 必须拒绝。
        save.Heroes[0].TargetId = "h-ghost";
        Assertions.AssertThat(SaveValidator.Validate(save).IsValid).IsFalse();
    }

    [TestCase]
    public void Validator_RejectsSelfTarget()
    {
        var hero = Hero();
        hero.CurrentActivity = "社交";
        hero.RemainingActionSeconds = 2.0;
        hero.TargetId = "h-alice";

        Assertions.AssertThat(SaveValidator.Validate(SaveWith(hero)).IsValid).IsFalse();
    }

    /// <summary>没有进行中的行动却记录了目标 —— 状态自相矛盾，拒绝。</summary>
    [TestCase]
    public void Validator_RejectsTargetWithoutActivity()
    {
        var hero = Hero();
        hero.TargetId = "h-bob";

        var save = SaveWith(hero);
        save.Heroes.Add(new HeroSave
        {
            Id = "h-bob", Name = "Bob", Personality = "贪", Diligence = "肝",
            Power = 1, Speed = 1, Grit = 1,
        });

        Assertions.AssertThat(SaveValidator.Validate(save).IsValid).IsFalse();
    }
}
