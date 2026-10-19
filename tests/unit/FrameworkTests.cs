using GdUnit4;

/// <summary>
/// Smoke test for the test framework itself, not for game logic.
/// Gameplay tests land in tests/unit/[system]/ as those systems are built.
///
/// Naming follows the C# convention (`.claude/rules/test-standards.md`):
/// class `[System]Tests`, method `[Scenario]_[Expected]`.
///
/// API notes (read out of GdUnit4Api.dll on 2026-10-17, do not guess them):
///   [TestSuite] on the class · [TestCase] on the method ·
///   Assertions.AssertThat(...) — there is no bare `Assert` symbol and no
///   `GdUnit4.Test` namespace.
/// </summary>
[TestSuite]
public class FrameworkTests
{
    [TestCase]
    public void GdUnit4_RunsATest()
    {
        Assertions.AssertThat(1 + 1).IsEqual(2);
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// HOW TO RE-VERIFY THAT THIS SUITE CAN ACTUALLY FAIL
//
// A green suite is only evidence if you have seen it go red. "0 tests,
// Passed, exit 0" is the failure mode this guards against.
//
// Add this temporarily, run `dotnet test`, confirm it prints
//   失败: 1，通过: 0
// and exits 1 — then delete it again:
//
//     [TestCase]
//     public void TemporaryGateCheck()
//     {
//         Assertions.AssertThat(1 + 1).IsEqual(3);
//     }
//
// Last verified this way: 2026-10-17 — reported 失败 1 / 通过 1, exit 1.
// ─────────────────────────────────────────────────────────────────────────────
