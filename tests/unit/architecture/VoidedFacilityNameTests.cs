using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using GdUnit4;

namespace ClaudeCodeGameStudios.Tests.Architecture
{
    /// <summary>
    /// Forbids naming a <b>【待定】 facility shape</b> in a user-visible string.
    ///
    /// <para><b>The defect this catches.</b> The summon facility's shape is
    /// 【待定】 under <c>07</c>'s <b>D-006</b>. Naming it anyway — in a button, a
    /// window title, a village tier — silently turns an open design question
    /// into an apparent decision, because every player and every reader of the
    /// build takes the name as the answer. This has already happened twice in
    /// this project: the button said 「酒馆召唤」 and was renamed, and the
    /// village tier still said 「冒险者旅舍」. Fixing one occurrence and
    /// declaring the matter closed is how the second one survived.</para>
    ///
    /// <para><b>What this guard deliberately does NOT cover</b> — read this
    /// before treating a green run as "the voided design is gone":</para>
    /// <list type="bullet">
    /// <item>Voided <b>code</b> that still lives in the prototype and is tracked
    /// separately: <c>HeroAgent.AbsentDays</c> / <c>AbsentWhy</c> /
    /// <c>SlackDays</c> / <c>RestlessDays</c>, the whole of
    /// <c>src/WorldClock.cs</c> including <c>DeepNight</c>, and
    /// <c>HeroFactory</c>'s <c>TimeZones</c> / <c>PickTimezone</c> /
    /// <c>WeightFor</c> / <c>ScheduleFor</c> scheduling. Those are real, still
    /// compiled, and are an open migration item — this guard says nothing
    /// about them.</item>
    /// <item>Any facility shape other than the two words listed below.</item>
    /// </list>
    /// <para>A green run means exactly one thing: <b>these two words no longer
    /// appear in any string the player can see.</b> Nothing more.</para>
    ///
    /// <para><b>WHY COMMENTS ARE BLANKED BUT STRING LITERALS ARE NOT.</b>
    /// Comments in this repository constantly name what was removed — that is
    /// how a reader learns the removal happened, and blanking them would break
    /// the record. String literals are the opposite: they are exactly where an
    /// asserted design decision shows up in the running game. So the scanner
    /// strips comments and keeps literals.</para>
    ///
    /// <para><b>A GREEN SUITE MEANS NOTHING UNTIL YOU HAVE SEEN IT GO RED.</b>
    /// Guarded three ways, as in <see cref="DomainLayerPurityTests"/>:
    /// <see cref="SourceFolder_Exists_GuardHasSomethingToScan"/> fails if
    /// <c>src/</c> moves, <see cref="Scanner_DetectsFacilityName_InSyntheticSource"/>
    /// proves the patterns still fire on source that is never written to disk,
    /// and the report is asserted as a string so a failure names the offender.</para>
    /// </summary>
    [TestSuite]
    public class VoidedFacilityNameTests
    {
        /// <summary>Path of the scanned folder, relative to the repository root.</summary>
        private const string SourceFolder = "src";

        /// <summary>Repository-root marker; unambiguous alongside <see cref="SourceFolder"/>.</summary>
        private const string RootMarkerFile = "Claude-Code-Game-Studios.csproj";

        /// <summary>
        /// 【待定】 facility shapes under D-006. Each entry is a word that names
        /// the summon facility — the tavern/inn. The rule is about the
        /// <b>shape being undecided</b>, not about these two spellings forever:
        /// if D-006 is ever resolved and the game ships an inn, this list is
        /// where that gets revisited.
        /// </summary>
        private static readonly string[] ProhibitedWords = { "旅舍", "酒馆" };

        // ── Arrange / Act / Assert ───────────────────────────────────────────

        /// <summary>The gate. Empty report means clean.</summary>
        [TestCase]
        public void SourceLayer_ContainsNoVoidedFacilityName_BuildPasses()
        {
            // Arrange
            string root = FindRepositoryRoot();

            // Act
            string report = BuildViolationReport(root);

            // Assert — as a string, so a failure prints the file and line.
            Assertions.AssertThat(report).IsEqual(string.Empty);
        }

        /// <summary>A folder that cannot be found is a failure, not a pass.</summary>
        [TestCase]
        public void SourceFolder_Exists_GuardHasSomethingToScan()
        {
            // Arrange / Act
            string sourcePath = Path.Combine(FindRepositoryRoot(), SourceFolder);

            // Assert
            Assertions.AssertThat(Directory.Exists(sourcePath)).IsTrue();
        }

        /// <summary>
        /// Proves the scanner is neither vacuous nor trigger-happy, on source
        /// that is never written to disk.
        /// </summary>
        [TestCase]
        public void Scanner_DetectsFacilityName_InSyntheticSource()
        {
            // Arrange — each sample must be reported.
            (string Label, string Source)[] mustCatch =
            {
                ("inn as a village tier",
                    "class X { static string T(int t) => t switch { 1 => \"冒险者旅舍\" }; }"),
                ("tavern as a button label",
                    "class X { void B() { btn.Text = \"酒馆\"; } }"),
                ("inn in a verbatim literal",
                    "class X { const string S = @\"旅舍\"; }"),
                ("inn in an interpolated literal",
                    "class X { string S(int n) => $\"n={n} 旅舍\"; }"),
            };

            // Arrange — each sample must NOT be reported.
            (string Label, string Source)[] mustNotCatch =
            {
                ("line comment naming the voided shape",
                    "// 召唤设施形态【待定】，所以不叫「酒馆」。\nclass X { }"),
                ("doc comment naming the voided shape",
                    "/// <remarks>原为「冒险者旅舍」，已按 D-006 改掉。</remarks>\nclass X { }"),
                ("block comment naming the voided shape",
                    "/* 这里曾写 旅舍，见 07 D-006 */\nclass X { }"),
                ("identifier containing a similar word",
                    "class X { public int TavernCount; public string InnKeeper; }"),
                ("ordinary UI string",
                    "class X { void B() { btn.Text = \"招募一个人\"; } }"),
            };

            var builder = new StringBuilder();

            // Act / Assert — positives
            foreach ((string label, string source) in mustCatch)
            {
                if (FindViolations("Synthetic.cs", source).Count == 0)
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

        private static string BuildViolationReport(string repositoryRoot)
        {
            string sourcePath = Path.Combine(repositoryRoot, SourceFolder);
            if (!Directory.Exists(sourcePath))
            {
                return $"{SourceFolder}/ does not exist at '{sourcePath}'. "
                     + "The voided-facility-name guard cannot run against a folder "
                     + "that is not there.";
            }

            var builder = new StringBuilder();
            foreach (string file in EnumerateCSharpFiles(sourcePath))
            {
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
                    "     → 召唤设施的形态属【待定】（07 的 D-006）。给未定的形态起名，"
                    + "等于把一个开放问题变成看似已定的答案 —— 玩家和读代码的人都会把名字当成结论。"
                    + "改用描述性的说法，不要用设施名。");
                builder.AppendLine();
            }

            return builder.ToString();
        }

        private static List<string> FindViolations(string displayPath, string source)
        {
            var violations = new List<string>();

            // Comments blanked; string literals deliberately preserved.
            string code = StripComments(source);

            foreach (string word in ProhibitedWords)
            {
                int from = 0;
                while (true)
                {
                    int at = code.IndexOf(word, from, StringComparison.Ordinal);
                    if (at < 0)
                    {
                        break;
                    }

                    violations.Add(
                        $"line {LineOf(code, at)}: 「{word}」 — \"{Describe(code, at, word.Length)}\"");
                    from = at + word.Length;
                }
            }

            return violations;
        }

        /// <summary>
        /// Replaces comment characters with spaces, leaving string literals and
        /// newlines intact, so reported line numbers stay true to the original.
        /// <para>
        /// A string literal is copied through verbatim (including any <c>//</c>
        /// inside it) so that a URL in a message is not mistaken for a comment.
        /// Verbatim and interpolated literals are handled.
        /// </para>
        /// </summary>
        private static string StripComments(string source)
        {
            char[] buffer = source.ToCharArray();
            int i = 0;

            while (i < source.Length)
            {
                char c = source[i];

                if (c == '/' && i + 1 < source.Length && source[i + 1] == '/')
                {
                    while (i < source.Length && source[i] != '\n')
                    {
                        buffer[i++] = ' ';
                    }

                    continue;
                }

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

                // String or char literal: copy through untouched.
                if (c == '"' || c == '\'')
                {
                    char quote = c;
                    bool verbatim = HasVerbatimPrefix(source, i);
                    i++;

                    while (i < source.Length)
                    {
                        char d = source[i];

                        if (!verbatim && d == '\\' && i + 1 < source.Length)
                        {
                            i += 2;
                            continue;
                        }

                        if (d == quote)
                        {
                            if (verbatim && i + 1 < source.Length && source[i + 1] == quote)
                            {
                                i += 2;
                                continue;
                            }

                            i++;
                            break;
                        }

                        if (d == '\n' && !verbatim)
                        {
                            i++;
                            break;
                        }

                        i++;
                    }

                    continue;
                }

                i++;
            }

            return new string(buffer);
        }

        private static bool HasVerbatimPrefix(string source, int quoteIndex)
        {
            for (int j = quoteIndex - 1; j >= 0; j--)
            {
                if (source[j] == '@')
                {
                    return true;
                }

                if (source[j] != '$')
                {
                    return false;
                }
            }

            return false;
        }

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

        private static string Describe(string code, int index, int length)
        {
            int start = Math.Max(0, index - 16);
            int end = Math.Min(code.Length, index + length + 16);
            string excerpt = code.Substring(start, end - start)
                                 .Replace("\r", string.Empty)
                                 .Replace("\n", " ")
                                 .Trim();
            return excerpt.Length > 60 ? excerpt.Substring(0, 60) + "…" : excerpt;
        }

        private static string FindRepositoryRoot()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);

            while (directory is not null)
            {
                if (File.Exists(Path.Combine(directory.FullName, RootMarkerFile))
                    && Directory.Exists(Path.Combine(directory.FullName, SourceFolder)))
                {
                    return directory.FullName;
                }

                directory = directory.Parent;
            }

            throw new InvalidOperationException(
                $"Could not locate the repository root. Started at '{AppContext.BaseDirectory}' "
                + $"and walked up looking for '{RootMarkerFile}' alongside '{SourceFolder}/'.");
        }

        private static IEnumerable<string> EnumerateCSharpFiles(string directoryPath)
        {
            return Directory.EnumerateFiles(directoryPath, "*.cs", SearchOption.AllDirectories)
                            .Where(path =>
                            {
                                string normalized = path.Replace('\\', '/');
                                return !normalized.Contains("/obj/")
                                    && !normalized.Contains("/bin/");
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
