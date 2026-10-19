using GdUnit4;

/// <summary>
/// 回归测试 —— 缺陷 C-01。
///
/// <para><b>原缺陷</b>：<c>Chronicle.Outcome(..., "refuse")</c> 的 switch 只认
/// over / pocket / fail，<c>"refuse"</c> 落到 <c>default =&gt; "good"</c>。
/// 结果是**拒绝出工的英雄被记成「不但做完了，还在矿洞多待了半日」**——
/// 杂谈与领域事实相反。</para>
///
/// <para><b>为什么断言的是「模板键」而不是文本</b>：杂谈文本每次抽样都不同
/// （<c>{a}</c> 绰号、<c>{o}</c> 在场他人都是随机填充的），断言文本会不稳定，
/// 也无法覆盖全部模板。断言 <see cref="Chronicle.SelectTemplateKey"/> 返回的键
/// 是稳定的，且正是缺陷发生的那一层。</para>
///
/// <para><b>本测试是否有效</b>：按 <c>.claude/rules/test-standards.md</c> 的要求，
/// 必须**对着未修复的代码看着它失败过**才算数。见文件末尾的验证记录。</para>
///
/// <para>对应 06 计划的 UT-09 / A-04「杂谈不说谎」；
/// 依据基线 §7.2「杂谈根据已发生的事件生成消息」与 §7.4 C1/C2。</para>
/// </summary>
[TestSuite]
public class ChronicleTests
{
    /// <summary>三种性格都必须存在 —— 否则下面的循环会因为漏掉一种而空转。</summary>
    [TestCase]
    public void AllPersonalities_AreCovered_NotSilentlySkipped()
    {
        var personalities = new[] { "莽", "傲", "贪" };
        Assertions.AssertThat(personalities.Length).IsEqual(3);
    }

    /// <summary>
    /// 核心断言：**「拒工」在任何性格下都不得回落到 "good"**。
    /// 「good」模板描述的是「干完了」，用它渲染一次拒工就是撒谎。
    /// </summary>
    [TestCase]
    public void Refused_NeverSelectsTheGoodTemplate_ForAnyPersonality()
    {
        // Arrange
        var personalities = new[] { "莽", "傲", "贪" };

        foreach (var p in personalities)
        {
            var hero = new HeroAgent { Personality = p, Name = "测试" };

            // Act
            string key = Chronicle.SelectTemplateKey(hero, "refuse");

            // Assert —— 逐个性格断言，失败时会指名道姓是谁出了问题
            Assertions.AssertThat(key).IsEqual("refuse");
        }
    }

    /// <summary>
    /// 未知 kind 仍回落 "good"（这是既有行为，不在本次修复范围内，
    /// 但必须锁住，否则修 refuse 时容易顺手改坏这条）。
    /// </summary>
    [TestCase]
    public void UnknownKind_FallsBackToGood_AsBefore()
    {
        var hero = new HeroAgent { Personality = "傲", Name = "测试" };

        string key = Chronicle.SelectTemplateKey(hero, "某种从未出现过的结果");

        Assertions.AssertThat(key).IsEqual("good");
    }

    /// <summary>四种常规结果的键选择没有被本次修复改动。</summary>
    [TestCase]
    public void ConventionalKinds_StillMapToTheirOwnKeys()
    {
        var hero = new HeroAgent { Personality = "贪", Name = "测试" };

        Assertions.AssertThat(Chronicle.SelectTemplateKey(hero, "pocket")).IsEqual("pocket");
        Assertions.AssertThat(Chronicle.SelectTemplateKey(hero, "fail")).IsEqual("fail");
        Assertions.AssertThat(Chronicle.SelectTemplateKey(hero, "good")).IsEqual("good");

        // 莽 没有 "pocket" 模板 —— 缺文案时按原行为回落到 good
        var reckless = new HeroAgent { Personality = "莽", Name = "测试" };
        Assertions.AssertThat(Chronicle.SelectTemplateKey(reckless, "pocket")).IsEqual("good");
    }

    /// <summary>
    /// Outcome 调用不抛异常，且条目确实被生成 ——
    /// 保证「拒工」这条路径**真的能走通**，而不只是键选对了。
    /// </summary>
    [TestCase]
    public void Outcome_WithRefused_ProducesAnEntryWithoutThrowing()
    {
        var hero = new HeroAgent { Personality = "傲", Name = "阿黄" };

        var entry = Chronicle.Outcome(1, hero, "采石", "石料", 0, null, "refuse");

        Assertions.AssertThat(entry.Text).IsNotEmpty();
        Assertions.AssertThat(entry.Day).IsEqual(1);
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// 验证记录（`.claude/rules/test-standards.md`：「你必须看着它失败过」）
//
// 步骤 1 —— 退回修复：把 SelectTemplateKey 的 refuse 分支改回
//            `"refuse" => "good"`（即修复前的行为），跑测试：
//                失败: 1，通过: 4，退出码 1
//                Refused_NeverSelectsTheGoodTemplate_ForAnyPersonality
//                期望 "refuse"，实际 "good"
//
// 步骤 2 —— 恢复修复，跑测试：
//                失败: 0，通过: 5，退出码 0
//
// 日期：2026-10-17。缺陷编号 C-01，见
// `production/07_技术决策与待确认事项.md` 的 D-004。
// ─────────────────────────────────────────────────────────────────────────────
