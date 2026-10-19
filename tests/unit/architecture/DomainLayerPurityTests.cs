using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using GdUnit4;

namespace ClaudeCodeGameStudios.Tests.Architecture
{
    /// <summary>
    /// Enforces the D-001 acceptance criterion and invariant I9:
    /// <b>no file under <c>src/Domain/</c> may reference Godot.</b>
    ///
    /// The domain layer is plain C#. If a domain type needs a Godot type to be
    /// expressed, the layering is broken and the type belongs somewhere else —
    /// that is the finding, not something to work around.
    ///
    /// WHY A SOURCE SCAN AND NOT A COMPILE ERROR: a compile-time gate would need
    /// <c>src/Domain/</c> to be its own assembly with no GodotSharp reference.
    /// That is a stronger gate and it is proposed as a follow-up (see
    /// <c>docs/architecture/ADR-0001-layered-architecture.md</c>), but it changes
    /// the shipped <c>.csproj</c> and would need the CI build step revisited.
    /// This scan needs no config change, so it runs on every existing
    /// <c>dotnet test</c> invocation — including the CI job, which already runs
    /// <c>dotnet test</c> on every push and PR.
    ///
    /// A GREEN SUITE MEANS NOTHING UNTIL YOU HAVE SEEN IT GO RED. Three separate
    /// things guard against a vacuous pass here:
    ///   1. <see cref="Scanner_DetectsGodotReference_InSyntheticSource"/> proves
    ///      the scanner's patterns still fire, on source that is not on disk.
    ///   2. <see cref="DomainFolder_Exists"/> fails if <c>src/Domain/</c>
    ///      disappears, so a moved directory cannot silently pass.
    ///   3. The report is asserted as a string rather than a count, so a failure
    ///      prints the offending file, line and text instead of "expected 0".
    ///
    /// Test naming follows <c>.claude/rules/test-standards.md</c>:
    /// class <c>[System]Tests</c>, method <c>[Scenario]_[Expected]</c>.
    ///
    /// API note (read out of GdUnit4Api.dll 5.1.0-rc5 on 2026-10-17, do not
    /// guess): <c>[TestSuite]</c> on the class, <c>[TestCase]</c> on the method,
    /// <c>Assertions.AssertThat(...)</c>. There is no bare <c>Assert</c> symbol,
    /// no <c>GdUnit4.Test</c> namespace, and no <c>AddMessage</c> /
    /// <c>CustomMessage</c> assertion overload — which is why the readable
    /// failure text comes from the asserted value, not from a message argument.
    /// </summary>
    [TestSuite]
    public class DomainLayerPurityTests
    {
        /// <summary>Path of the guarded folder, relative to the repository root.</summary>
        private const string DomainFolder = "src/Domain";

        /// <summary>
        /// Repository root marker. Paired with <see cref="DomainFolder"/> it is
        /// unambiguous, and unlike "the first ancestor named src" it cannot be
        /// satisfied by a nested copy of the layout.
        /// </summary>
        private const string RootMarkerFile = "Claude-Code-Game-Studios.csproj";

        /// <summary>
        /// Ways to reach Godot from the domain layer. Ordered so the message
        /// names the most specific violation first.
        /// </summary>
        private static readonly (string Label, Regex Pattern)[] ProhibitedReferences =
        {
            ("`using Godot`",
                new Regex(@"(?<![\w.])using\s+(?:static\s+)?(?:global::)?Godot\b", RegexOptions.Compiled)),

            ("`using static Godot...`",
                new Regex(@"(?<![\w.])extern\s+alias\s+Godot\b", RegexOptions.Compiled)),

            ("fully-qualified `global::Godot`",
                new Regex(@"(?<![\w.])global::Godot\b", RegexOptions.Compiled)),

            ("fully-qualified `Godot.SomeType`",
                new Regex(@"(?<![\w.])Godot\.[A-Za-z_]", RegexOptions.Compiled)),

            ("`[GlobalClass]` (Godot-only attribute)",
                new Regex(@"\[GlobalClass\]", RegexOptions.Compiled)),
        };

        // ── Arrange / Act / Assert ───────────────────────────────────────────

        /// <summary>
        /// The gate itself. Scans every <c>.cs</c> file under <c>src/Domain/</c>
        /// and fails with a per-file, per-line report naming the offending text.
        /// </summary>
        [TestCase]
        public void DomainLayer_ContainsNoGodotReference_BuildPasses()
        {
            // Arrange — locate the repository from the test output directory.
            string root = FindRepositoryRoot();

            // Act
            string report = BuildViolationReport(root);

            // Assert — asserted as a string, not a count, so the failure output
            // lists the offending files instead of a bare "expected 0".
            Assertions.AssertThat(report).IsEqual(string.Empty);
        }

        /// <summary>
        /// A directory that cannot be found is a failure, not a pass. Without
        /// this, moving or renaming <c>src/Domain/</c> would turn the guard into
        /// a suite that scans nothing and reports success.
        /// </summary>
        [TestCase]
        public void DomainFolder_Exists_GuardHasSomethingToScan()
        {
            // Arrange / Act
            string domainPath = Path.Combine(FindRepositoryRoot(), DomainFolder);

            // Assert
            Assertions.AssertThat(Directory.Exists(domainPath)).IsTrue();
        }

        /// <summary>
        /// Proves the scanner is not vacuous, on source that is never written to
        /// disk. Every prohibited form is fed in, plus the two negative cases
        /// that must NOT trip it: a mention inside a comment, and a mention
        /// inside a string literal. This is the piece that keeps the guard from
        /// silently degrading into a no-op after a future edit to
        /// <see cref="ProhibitedReferences"/>.
        /// </summary>
        [TestCase]
        public void Scanner_DetectsGodotReference_InSyntheticSource()
        {
            // Arrange — each sample must be reported.
            (string Label, string Source)[] mustCatch =
            {
                ("using directive",
                    "using Godot;\nnamespace X { }"),
                ("using sub-namespace",
                    "using Godot.Collections;\nnamespace X { }"),
                ("using on its own line",
                    "using\n    Godot;\nnamespace X { }"),
                ("extern alias",
                    "extern alias Godot;\nnamespace X { }"),
                ("global:: qualified",
                    "namespace X { public Godot.Node N; }"),
                ("fully-qualified type",
                    "namespace X { public Godot.Vector2 V; }"),
                ("global:: qualified type",
                    "namespace X { public global::Godot.Vector2 V; }"),
                ("inherited from a Node",
                    "using Godot;\nnamespace X { public partial class Foo : Node { } }"),
                ("[GlobalClass] on a Resource",
                    "using Godot;\n[GlobalClass] public partial class Def : Resource { }"),
            };

            // Arrange — each sample must NOT be reported.
            (string Label, string Source)[] mustNotCatch =
            {
                ("comment mentioning the forbidden phrase",
                    "// The domain layer must never do: using Godot\nnamespace X { }"),
                ("doc comment mentioning Godot types",
                    "/// <remarks>Not a Node, not a Godot type.</remarks>\nnamespace X { }"),
                ("block comment mentioning Godot",
                    "/* deliberately does not use Godot here */\nnamespace X { }"),
                ("string literal containing the text",
                    "namespace X { public const string Note = \"must not using Godot\"; }"),
                ("verbatim string literal",
                    "namespace X { public const string Note = @\"using Godot;\"; }"),
                ("an unrelated identifier ending in Godot",
                    "namespace X { public int MyGodot; public int GodotCount; }"),
                ("plain C# with no engine reference",
                    "using System;\nnamespace X { public sealed class Hero { public int Id; } }"),
            };

            var builder = new StringBuilder();

            // Act / Assert — positives
            foreach ((string label, string source) in mustCatch)
            {
                var found = FindViolations("Synthetic.cs", source);
                if (found.Count == 0)
                {
                    builder.AppendLine($"NOT DETECTED — scanner would have missed: {label}");
                    builder.AppendLine($"    source: {source.Replace("\n", "\\n")}");
                }
            }

            // Act / Assert — negatives
            foreach ((string label, string source) in mustNotCatch)
            {
                var found = FindViolations("Synthetic.cs", source);
                if (found.Count > 0)
                {
                    builder.AppendLine($"FALSE POSITIVE — scanner flags clean code: {label}");
                    foreach (string v in found)
                    {
                        builder.AppendLine($"    {v}");
                    }
                }
            }

            // Assert
            Assertions.AssertThat(builder.ToString()).IsEqual(string.Empty);
        }

        // ── Helpers ──────────────────────────────────────────────────────────

        /// <summary>
        /// Builds the human-readable failure report. Empty string means clean.
        /// </summary>
        private static string BuildViolationReport(string repositoryRoot)
        {
            string domainPath = Path.Combine(repositoryRoot, DomainFolder);
            if (!Directory.Exists(domainPath))
            {
                return $"{DomainFolder}/ does not exist at '{domainPath}'. "
                     + "The domain-layer purity guard cannot run against a folder "
                     + "that is not there — restore the directory or update "
                     + "DomainLayerPurityTests.DomainFolder.";
            }

            var builder = new StringBuilder();
            int filesScanned = 0;

            foreach (string file in EnumerateCSharpFiles(domainPath))
            {
                filesScanned++;
                string source = File.ReadAllText(file);
                var violations = FindViolations(ToRelativePath(file, repositoryRoot), source);
                if (violations.Count == 0)
                {
                    continue;
                }

                builder.AppendLine($"❌ {ToRelativePath(file, repositoryRoot)}");
                foreach (string violation in violations)
                {
                    builder.AppendLine($"     {violation}");
                }

                builder.AppendLine(
                    "     → The domain layer must be plain C#. Move the type out of "
                    + "src/Domain/ (05 §2.1, 基线 §13.3, invariant I9, D-001).");
                builder.AppendLine();
            }

            if (filesScanned == 0)
            {
                // An empty src/Domain/ is the legitimate M1 state — the skeleton
                // exists, the code has not been migrated yet. It is NOT treated as
                // a violation, because "the folder holds no code" cannot violate
                // "the folder's code references no Godot".
                //
                // This is the one place where the guard can pass without having
                // proven anything, so vacuity is covered elsewhere instead:
                //   · Scanner_DetectsGodotReference_InSyntheticSource proves the
                //     patterns fire, on source that is not on disk.
                //   · DomainFolder_Exists_GuardHasSomethingToScan fails if the
                //     folder is moved or renamed, rather than scanning nothing.
                // Once the first domain file lands, filesScanned > 0 always holds.
                return string.Empty;
            }

            return builder.ToString();
        }

        /// <summary>
        /// Returns one formatted line per prohibited reference in
        /// <paramref name="source"/>.
        /// </summary>
        private static List<string> FindViolations(string displayPath, string source)
        {
            var violations = new List<string>();

            // Comments and literals are blanked first, so prose that mentions
            // Godot — which this very repository does constantly — cannot trip
            // the scan. Newlines are preserved so reported line numbers stay true
            // to the original file.
            string code = BlankCommentsAndLiterals(source);

            foreach ((string label, Regex pattern) in ProhibitedReferences)
            {
                foreach (Match match in pattern.Matches(code))
                {
                    violations.Add(
                        $"line {LineOf(code, match.Index)}: {label} — \"{Describe(code, match)}\"");
                }
            }

            return violations;
        }

        /// <summary>
        /// Replaces every character that belongs to a comment, a string literal
        /// or a char literal with a space, leaving newlines intact.
        /// <para>
        /// Handles <c>//</c>, <c>/* */</c>, quoted and verbatim/interpolated
        /// strings and char literals. It does <b>not</b> parse C# raw string
        /// literals (<c>"""…"""</c>, C# 11); content inside one would still be
        /// scanned. Do not put a Godot reference inside a raw string literal in
        /// <c>src/Domain/</c> — that is a bizarre thing to do, but the guard
        /// does not promise to forgive it.
        /// </para>
        /// </summary>
        private static string BlankCommentsAndLiterals(string source)
        {
            char[] buffer = source.ToCharArray();
            int i = 0;

            while (i < source.Length)
            {
                char c = source[i];

                // Line comment
                if (c == '/' && i + 1 < source.Length && source[i + 1] == '/')
                {
                    while (i < source.Length && source[i] != '\n')
                    {
                        buffer[i++] = ' ';
                    }

                    continue;
                }

                // Block comment
                if (c == '/' && i + 1 < source.Length && source[i + 1] == '*')
                {
                    buffer[i] = ' ';
                    buffer[i + 1] = ' ';
                    i += 2;
                    while (i < source.Length
                           && !(source[i] == '*' && i + 1 < source.Length && source[i + 1] == '/'))
                    {
                        if (source[i] != '\n')
                        {
                            buffer[i] = ' ';
                        }

                        i++;
                    }

                    if (i < source.Length)
                    {
                        buffer[i] = ' ';
                        if (i + 1 < source.Length)
                        {
                            buffer[i + 1] = ' ';
                        }

                        i = Math.Min(i + 2, source.Length);
                    }

                    continue;
                }

                // String or char literal
                if (c == '"' || c == '\'')
                {
                    char quote = c;
                    bool verbatim = HasVerbatimPrefix(source, i);
                    buffer[i] = ' ';
                    i++;

                    while (i < source.Length)
                    {
                        char d = source[i];

                        // Escape sequence
                        if (!verbatim && d == '\\' && i + 1 < source.Length)
                        {
                            if (d != '\n')
                            {
                                buffer[i] = ' ';
                            }

                            if (source[i + 1] != '\n')
                            {
                                buffer[i + 1] = ' ';
                            }

                            i += 2;
                            continue;
                        }

                        // Closing quote — doubled inside a verbatim literal
                        if (d == quote)
                        {
                            if (verbatim && i + 1 < source.Length && source[i + 1] == quote)
                            {
                                buffer[i] = ' ';
                                buffer[i + 1] = ' ';
                                i += 2;
                                continue;
                            }

                            buffer[i] = ' ';
                            i++;
                            break;
                        }

                        // Unterminated literal — stop at the line break
                        if (d == '\n' && !verbatim)
                        {
                            i++;
                            break;
                        }

                        if (d != '\n')
                        {
                            buffer[i] = ' ';
                        }

                        i++;
                    }

                    continue;
                }

                i++;
            }

            return new string(buffer);
        }

        /// <summary>
        /// True when the quote at <paramref name="quoteIndex"/> is preceded by
        /// an <c>@</c> in the same prefix run (<c>@"…"</c>, <c>$@"…"</c>,
        /// <c>@"$"…"</c>), which switches off backslash escaping.
        /// </summary>
        private static bool HasVerbatimPrefix(string source, int quoteIndex)
        {
            for (int j = quoteIndex - 1; j >= 0; j--)
            {
                if (source[j] == '@')
                {
                    return true;
                }

                // Only the interpolation/verbatim prefix run may precede a quote.
                if (source[j] != '$')
                {
                    return false;
                }
            }

            return false;
        }

        /// <summary>1-based line number of a character offset.</summary>
        private static int LineOf(string text, int index)
        {
            int line = 1;
            for (int i = 0; i < index && i < text.Length; i++)
            {
                if (text[i] == '\n')
                {
                    line++;
                }
            }

            return line;
        }

        /// <summary>Short excerpt of the offending text, for the report.</summary>
        private static string Describe(string code, Match match)
        {
            int start = Math.Max(0, match.Index - 12);
            int end = Math.Min(code.Length, match.Index + match.Length + 12);
            string excerpt = code.Substring(start, end - start)
                                 .Replace("\r", string.Empty)
                                 .Replace("\n", " ")
                                 .Trim();
            return excerpt.Length > 60 ? excerpt.Substring(0, 60) + "…" : excerpt;
        }

        /// <summary>
        /// Walks up from the test output directory to the repository root.
        /// </summary>
        private static string FindRepositoryRoot()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);

            while (directory is not null)
            {
                if (File.Exists(Path.Combine(directory.FullName, RootMarkerFile))
                    && Directory.Exists(Path.Combine(directory.FullName, DomainFolder)))
                {
                    return directory.FullName;
                }

                directory = directory.Parent;
            }

            throw new InvalidOperationException(
                $"Could not locate the repository root. Started at '{AppContext.BaseDirectory}' "
                + $"and walked up looking for '{RootMarkerFile}' alongside '{DomainFolder}/'. "
                + "This is a test-harness problem, not a domain-layer violation.");
        }

        /// <summary>Every <c>.cs</c> file under the folder, build output excluded.</summary>
        private static IEnumerable<string> EnumerateCSharpFiles(string directoryPath)
        {
            return Directory.EnumerateFiles(directoryPath, "*.cs", SearchOption.AllDirectories)
                            .Where(path =>
                            {
                                string normalized = path.Replace('\\', '/');
                                return !normalized.Contains("/obj/")
                                    && !normalized.Contains("/bin/")
                                    && !normalized.EndsWith(".godot", StringComparison.Ordinal);
                            })
                            .OrderBy(path => path, StringComparer.Ordinal);
        }

        private static string ToRelativePath(string path, string repositoryRoot)
        {
            string root = repositoryRoot.Replace('\\', '/').TrimEnd('/');
            string normalized = path.Replace('\\', '/');
            return normalized.StartsWith(root, StringComparison.OrdinalIgnoreCase)
                ? normalized.Substring(root.Length).TrimStart('/')
                : normalized;
        }
    }
}
