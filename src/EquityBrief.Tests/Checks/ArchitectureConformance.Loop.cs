using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using EquityBrief.Core.Candidates;
using EquityBrief.Core.Rules;
using EquityBrief.Core.Time;
using EquityBrief.Tests.Harness;
using EquityBrief.Worker.Candidates;

namespace EquityBrief.Tests.Checks;

// How a mapped test names the code its clause is enforced by: a shipped member,
// named in the test's code, or a stored or drawn name, named in one of its literals.
internal enum Named
{
    Member,
    Stored,
}

// One clause of a guardrail, the subject it is mapped for where its cell lists
// several, and what holds it: a test naming its mechanism, or the carried
// obligation that owes it.
internal sealed record GuardrailPair(
    string Guardrail,
    string Subject,
    string Clause,
    string Test,
    string Mechanism,
    Named Kind,
    string Owed);

// What a mapping is read against, so the constructed proof can hand the reader a
// world of its own.
internal sealed record MappingWorld(
    IReadOnlyDictionary<string, string> Cells,
    IReadOnlyDictionary<string, bool> Tests,
    IReadOnlyList<string> Sources,
    Func<string, bool> MemberShipped,
    Func<string, bool> StoredShipped,
    IReadOnlyDictionary<string, bool> Obligations);

// A test method's body as what runs and what it says: its code with every
// comment and literal blanked, and the text of its literals.
internal sealed record TestBody(string Code, string Literals);

// architecture-conformance: section 13.2 and 13.3, the loop's own two tables.
public partial class ArchitectureConformance
{
    static GuardrailPair Held(string guardrail, string clause, string test, string mechanism, Named kind = Named.Member, string subject = "") =>
        new(guardrail, subject, clause, test, mechanism, kind, string.Empty);

    static GuardrailPair Owed(string guardrail, string clause, string obligation, string subject = "") =>
        new(guardrail, subject, clause, string.Empty, string.Empty, Named.Member, obligation);

    const string PromotionOwed = "A candidate's shadow record computed, and a promotion written against it";

    // Whether each test's assertions hold its clause is a reading, recorded clause by
    // clause in the entry that last changed this table.
    static readonly GuardrailPair[] HeldBy =
    [
        Held("Registered in advance", "every candidate condition is written to the append-only register",
            "AnUpdateAndADeleteAreBothRefusedByTheTableItself", "append only", Named.Stored),
        Held("Registered in advance", "with its rule, its test and the date",
            "ARegistrationIsStoredWithTheRuleTheTestAndTheInstantItWasGiven", "RegisterRow.RegisteredAt"),
        Held("Registered in advance", "before it is scored",
            "ACandidateRegisteredAfterTheNightStartedIsNotEvaluatedByIt", "ShadowColumn.StandingAt"),
        Held("Registered in advance", "The family has a stated maximum size",
            "AnEvaluatorNobodyCarriesAndAFamilyAtItsMaximumAreBothRefusedAtTheWrite", "CandidateFamily.Maximum"),
        Held("Corrected for the family", "the significance threshold is divided by the number of registered candidates",
            "TheDivisorCountsTheRowsRegisteredBeforeTheWindowOpenedAndNoOthers", "CandidateFamily.Divisor"),
        Held("Corrected for the family", "the divisor is recorded with the verdict",
            "TheShareAndTheBarAreDrawnWithTheVerdictAndItsDivisorFromEightFive", "data-divisor", Named.Stored),
        Held("Minimum resolved setups", "no verdict of any kind below the stated minimum",
            "ARecordEarnsAVerdictExactlyWhereTheTestRanAndAWithheldOneIsNeverDrawnAsFailing", "ReasonRecord.HasEarnedAVerdict"),
        Held("Minimum resolved setups", "a higher minimum before a live condition may be retired",
            "ALiveReasonIsNeitherRegisteredNorRetiredThroughTheRegister", "CandidateRegistrar.LiveReasonRefusal"),
        Held("Unresolved is never a win", "a setup that has neither hit its target nor its stop within the time cap",
            "ASetupIsScoredFromItsEntryAndATargetReachedBeforeItIsNeverAWin", "ForwardReturnSeries.OverSetup"),
        Held("Unresolved is never a win", "is reported in its own column",
            "TheReasonTrackDrawsThreeStatesOutOfOneDenominatorAndOutlinesTheUnresolved", "MarkRenderer.ReasonTrack"),
        Held("Unresolved is never a win", "excluded from the rate",
            "AnUnresolvedSetupIsCountedInItsOwnColumnAndInNoFloorNoShareAndNoTail", "ForwardReturnSeries.IsScored"),
        Held("Shadow before live", "no condition appears on the list",
            "ACandidateThatFiresOnEveryMemberReachesNoRowsReasons", "ShortlistSeries.Reasons"),
        Held("Shadow before live", "until its record has crossed the boundary of a look",
            "AFirstLookAtEightBlocksCannotCrossAtTheLevelTheFamilyIsTestedAt", "Looks.CrossedAt"),
        Held("Shadow before live", "no evaluation of a name is shown anywhere",
            "NoScreenCarriesAShadowEvaluationOfAName", "shadow_reasons", Named.Stored),
        Held("Shadow before live", "A candidate's own record is drawn on the run page, beside the candidate and never beside a name",
            "TheRecordRegionStatesEachLooksPowerAndTheNumbersTheCandidateWasRegisteredWith", "MarkRenderer.CandidateRecords"),
        Held("Shadow before live", "with the nightly figure labelled as monitoring and the verdict field changed only by a look",
            "ALookReadsOnlyTheSetupsWhoseWholeWindowHasClosedAndANightlyReadingNeverMovesTheVerdictField", "CandidateRecord.For"),
        Held("Frozen windows", "is not changed during a window it is being measured over",
            "ALiveRuleThatMovedInsideAnOpenWindowIsFoundAndOneThatHasNotIsNot", "RuleVersions.Drifted", subject: "a rule"),
        Held("Frozen windows", "is not changed during a window it is being measured over",
            "AReasonsRecordCountsOnlyTheRowsWrittenUnderTheThresholdTheCodeCarries", "ShortlistSeries.MeasuredUnderAnotherThreshold", subject: "threshold"),
        Held("Frozen windows", "A change starts a new window and the old one is kept",
            "AVersionChangeOpensANewWindowAndKeepsThePreviousOne", "RuleVersionScorer.ReplaceAsync", subject: "a rule"),
        Held("Frozen windows", "A change starts a new window and the old one is kept",
            "EveryThresholdAReasonCarriesIsWrittenOnItsRowsUnderItsOwnName", "ShortlistSeries.Thresholds", subject: "threshold"),
        Held("Adding restarts the clock", "registering a new candidate after the family is set inflates the family",
            "TheShadowRegionStatesHowManyAreRegisteredAndTheDivisorThatNumberSets", "RunScreen.Shadow"),
        Held("Adding restarts the clock", "the correction changes and the affected verdicts are recomputed",
            "AFamilyThatGrowsCorrectsEveryVerdictReadFromItAgain", "ReasonVerdict.For"),
        Held("Every change is recorded", "is written with the evidence that produced it",
            "ARetirementStatesTheEvidenceItWasGivenAndOneGivenNoneIsRefused", "RegisterRow.Evidence", subject: "a retirement"),
        Held("Every change is recorded", "the version it replaced",
            "ARetirementIsANewRowNamingWhatItRetiresAndTheOriginalStands", "RegisterRow.EvaluatorVersion", subject: "a retirement"),
        Held("Every change is recorded", "is written with the evidence that produced it",
            "AVersionIsReplacedInOneWriteThatClosesItWithItsEvidenceAndOpensTheVersionReplacingIt", "RuleVersionRow.Evidence", subject: "a rule version change"),
        Held("Every change is recorded", "the version it replaced",
            "TheVersionVerbOpensAWindowBesideItsLiveOneListsThemAndClosesOneNamingWhatReplacedIt", "RuleVersionRow.ReplacedBy", subject: "a rule version change"),
        Held("Every change is recorded", "is written with the evidence that produced it",
            "APromotedCandidateLeavesTheFamilyByARetirementWhoseEvidenceSaysSo",
            "CandidateFamily.PromotedBy", subject: "a promotion"),
        Held("Every change is recorded", "the version it replaced",
            "ARetirementNamesTheRegistrationItRetiresAndOnlyAPromotedOnesLevelPasses",
            "CandidateRow.Retires", subject: "a promotion"),
    ];

    // The words a cell may join its clauses with, which no clause need hold.
    static readonly string[] Joiners = ["and", "or", "so"];

    [Fact]
    public void EveryGuardrailIsMappedClauseByClauseToATestThatNamesItsMechanismOrTheObligationThatOwesIt()
    {
        var table = ArchitectureTables.In(File.ReadAllText(Repository.Architecture))
            .Single(one => one.Heading == "13.3 The guardrails");

        var cells = table.Body
            .Where(row => row.Count > 1 && row[0].Length > 0)
            .ToDictionary(row => row[0], row => row[1], StringComparer.Ordinal);

        Assert.True(cells.Count >= 8, $"Read {cells.Count} guardrail(s) from 13.3, expected at least 8.");

        var tests = Assembly.GetExecutingAssembly().GetTypes()
            .SelectMany(type => type.GetMethods())
            .Select(method => (method.Name, Fact: method.GetCustomAttribute<FactAttribute>(inherit: true)))
            .Where(method => method.Fact is not null)
            .GroupBy(method => method.Name, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Any(method => method.Fact!.Skip is not null), StringComparer.Ordinal);

        var separator = Path.DirectorySeparatorChar;

        var sources = Directory.EnumerateFiles(Path.Combine(Repository.Root, "src", "EquityBrief.Tests"), "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{separator}bin{separator}", StringComparison.Ordinal)
                && !path.Contains($"{separator}obj{separator}", StringComparison.Ordinal))
            .Select(File.ReadAllText)
            .ToArray();

        var shippedTypes = new[]
            {
                typeof(Core.Returns.ReasonVerdict).Assembly,
                typeof(Data.Migrations.SchemaMigrations).Assembly,
                typeof(CandidateRegistrar).Assembly,
                typeof(Api.Reading.RunScreen).Assembly,
                typeof(Web.Marks.MarkRenderer).Assembly,
            }
            .SelectMany(assembly => assembly.GetTypes())
            .ToArray();

        var shippedText = Repository.SourceFiles()
            .Where(path => !path.Contains($"{separator}EquityBrief.Tests{separator}", StringComparison.Ordinal))
            .Select(File.ReadAllText)
            .ToArray();

        var obligations = ObligationReconciles.In(Corpus.Read("docs/BUILD_PLAN.md"))
            .ToDictionary(row => row.Name, row => !row.Discharged, StringComparer.Ordinal);

        var world = new MappingWorld(
            cells,
            tests,
            sources,
            mechanism => mechanism.Split('.') is [var type, var member]
                && shippedTypes.Any(candidate => candidate.Name == type
                    && candidate.GetMember(member, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance).Length > 0),
            name => shippedText.Any(text => text.Contains(name, StringComparison.Ordinal)),
            obligations);

        Assert.Empty(MappingFaults(HeldBy, world));

        // The reader, over a constructed table, suite, source and plan, so none of the
        // faults it reports can be absent by being unreadable.
        var constructed = new MappingWorld(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["A rule"] = "a thing is refused at the write, and shown nowhere (" + "see" + ": A decision)",
                ["Another rule"] = "a thing nobody holds",
                ["A listed rule"] = "a draft or a copy is kept and dated",
                ["A wider rule"] = "a thing is refused and it is priced",
            },
            new Dictionary<string, bool>(StringComparer.Ordinal)
            {
                ["TheWriteRefusesIt"] = false,
                ["ANameThatSoundsRight"] = false,
                ["ANameThatSoundsRightOnThePage"] = false,
                ["AStoredNameInAComment"] = false,
                ["ASkippedTest"] = true,
                ["TheDraftIsKept"] = false,
                ["TheDraftIsDated"] = false,
                ["TheCopyIsKept"] = false,
            },
            [
                "public class Held\n{\n" +
                "    [Fact]\n    public void ANameThatSoundsRightOnThePage()\n    {\n        Assert.NotNull(Registrar.Refusal(rows));\n    }\n\n" +
                "    public void ANameThatSoundsRight(int times)\n    {\n        Assert.NotNull(Registrar.Refusal(rows));\n    }\n\n" +
                "    [Fact]\n    public void TheWriteRefusesIt()\n    {\n        // Registrar.Refusal is named in a comment too.\n        Assert.NotNull(Registrar.Refusal(rows));\n    }\n\n" +
                "    [Fact]\n    public void ANameThatSoundsRight()\n    {\n        var said = \"Registrar.Refusal { would be the thing\"; /* Registrar.Refusal */ Assert.True(true); // Registrar.Refusal\n        var RegistrarRefusalCount = '{';\n    }\n\n" +
                "    [Fact]\n    public void AStoredNameInAComment()\n    {\n        // refused_at\n        Assert.Equal(1, Refused.At);\n    }\n\n" +
                "    [Fact]\n    public void ASkippedTest()\n    {\n        Assert.NotNull(Registrar.Refusal(rows));\n    }\n\n" +
                "    [Fact]\n    public void TheDraftIsKept()\n    {\n        Assert.NotNull(Drafts.Keep(rows));\n    }\n\n" +
                "    [Fact]\n    public void TheDraftIsDated()\n    {\n        Assert.NotNull(Drafts.Keep(rows));\n    }\n\n" +
                "    [Fact]\n    public void TheCopyIsKept()\n    {\n        Assert.NotNull(Drafts.Keep(rows));\n    }\n}\n",
            ],
            mechanism => mechanism is "Registrar.Refusal" or "Drafts.Keep",
            name => name is "refused_at",
            new Dictionary<string, bool>(StringComparer.Ordinal) { ["An owed row"] = true, ["A discharged row"] = false });

        Assert.Equal(
            [
                "A rule's clause 'shown nowhere' names ANameThatSoundsRight, whose code does not name Registrar.Refusal",
                "A rule's clause 'a clause the cell does not carry' is not in its cell",
                "A rule's clause 'refused at the write' names ATestTheSuiteDoesNotCarry, which the suite does not carry",
                "A rule's clause 'refused at the write' names ASkippedTest, which the suite skips",
                "A rule's clause 'refused at the write' names TheWriteRefusesIt, whose mechanism Registrar.Missing the shipped code does not carry",
                "A rule's clause 'refused at the write' names AStoredNameInAComment, whose literals do not name refused_at",
                "Another rule's clause 'a thing nobody holds' is owed by 'A discharged row', which is discharged",
                "Another rule's clause 'a thing nobody holds' is owed by 'A row nobody wrote', which the carried obligations table does not hold",
                "A listed rule's clause 'is kept' is mapped for a draft and not for a copy",
                "A wider rule carries 'it is priced', which no mapped clause or subject covers",
                "TheWriteRefusesIt holds more than one clause",
            ],
            MappingFaults(
                [
                    Held("A rule", "refused at the write", "TheWriteRefusesIt", "Registrar.Refusal"),
                    Held("A rule", "shown nowhere", "ANameThatSoundsRight", "Registrar.Refusal"),
                    Held("A rule", "a clause the cell does not carry", "TheWriteRefusesIt", "Registrar.Refusal"),
                    Held("A rule", "refused at the write", "ATestTheSuiteDoesNotCarry", "Registrar.Refusal"),
                    Held("A rule", "refused at the write", "ASkippedTest", "Registrar.Refusal"),
                    Held("A rule", "refused at the write", "TheWriteRefusesIt", "Registrar.Missing"),
                    Held("A rule", "refused at the write", "AStoredNameInAComment", "refused_at", Named.Stored),
                    Held("A rule", "a thing is", "TheWriteRefusesIt", "Registrar.Refusal"),
                    Owed("Another rule", "a thing nobody holds", "A discharged row"),
                    Owed("Another rule", "a thing nobody holds", "A row nobody wrote"),
                    Held("A listed rule", "is kept", "TheDraftIsKept", "Drafts.Keep", subject: "a draft"),
                    Held("A listed rule", "and dated", "TheDraftIsDated", "Drafts.Keep", subject: "a draft"),
                    Held("A listed rule", "and dated", "TheCopyIsKept", "Drafts.Keep", subject: "a copy"),
                    Held("A wider rule", "a thing is refused", "TheWriteRefusesIt", "Registrar.Refusal"),
                ],
                constructed));

        // Each half on its own, so the list above is not the only thing that says the
        // reader stays quiet over a mapping it should accept.
        Assert.Empty(MappingFaults(
            [
                Held("A listed rule", "is kept", "TheDraftIsKept", "Drafts.Keep", subject: "a draft"),
                Held("A listed rule", "is kept", "TheCopyIsKept", "Drafts.Keep", subject: "a copy"),
                Held("A listed rule", "and dated", "TheDraftIsDated", "Drafts.Keep", subject: "a draft"),
                Owed("A listed rule", "and dated", "An owed row", subject: "a copy"),
            ],
            constructed with { Cells = new Dictionary<string, string>(StringComparer.Ordinal) { ["A listed rule"] = constructed.Cells["A listed rule"] } }));
    }

    internal static IReadOnlyList<string> MappingFaults(IReadOnlyList<GuardrailPair> mapping, MappingWorld world)
    {
        var faults = new List<string>();

        foreach (var pair in mapping)
        {
            if (!world.Cells.TryGetValue(pair.Guardrail, out var cell))
            {
                faults.Add($"{pair.Guardrail} is mapped and the table carries no such guardrail");
                continue;
            }

            if (!Uncited(cell).Contains(pair.Clause, StringComparison.Ordinal))
            {
                faults.Add($"{pair.Guardrail}'s clause '{pair.Clause}' is not in its cell");
                continue;
            }

            if (pair.Subject.Length > 0 && !Uncited(cell).Contains(pair.Subject, StringComparison.Ordinal))
            {
                faults.Add($"{pair.Guardrail}'s subject '{pair.Subject}' is not in its cell");
                continue;
            }

            var where = $"{pair.Guardrail}'s clause '{pair.Clause}'";

            if (pair.Owed.Length > 0)
            {
                if (!world.Obligations.TryGetValue(pair.Owed, out var open))
                {
                    faults.Add($"{where} is owed by '{pair.Owed}', which the carried obligations table does not hold");
                }
                else if (!open)
                {
                    faults.Add($"{where} is owed by '{pair.Owed}', which is discharged");
                }

                continue;
            }

            if (!world.Tests.TryGetValue(pair.Test, out var skipped))
            {
                faults.Add($"{where} names {pair.Test}, which the suite does not carry");
                continue;
            }

            if (skipped)
            {
                faults.Add($"{where} names {pair.Test}, which the suite skips");
                continue;
            }

            if (pair.Kind == Named.Member ? !world.MemberShipped(pair.Mechanism) : !world.StoredShipped(pair.Mechanism))
            {
                faults.Add($"{where} names {pair.Test}, whose mechanism {pair.Mechanism} the shipped code does not carry");
                continue;
            }

            var body = BodyOf(pair.Test, world.Sources);

            if (pair.Kind == Named.Member
                && (body is null || !NamesWhole(body.Code, pair.Mechanism[(pair.Mechanism.LastIndexOf('.') + 1)..], "_")))
            {
                faults.Add($"{where} names {pair.Test}, whose code does not name {pair.Mechanism}");
            }

            if (pair.Kind == Named.Stored && (body is null || !NamesWhole(body.Literals, pair.Mechanism, "_-")))
            {
                faults.Add($"{where} names {pair.Test}, whose literals do not name {pair.Mechanism}");
            }
        }

        foreach (var (guardrail, cell) in world.Cells)
        {
            var pairs = mapping.Where(pair => pair.Guardrail == guardrail).ToArray();

            if (pairs.Length == 0)
            {
                faults.Add($"{guardrail} has no clause mapped");
                continue;
            }

            // A clause mapped by subject is mapped for every subject its guardrail lists.
            var subjects = pairs.Where(pair => pair.Subject.Length > 0).Select(pair => pair.Subject).Distinct(StringComparer.Ordinal).ToArray();

            foreach (var clause in pairs.Where(pair => pair.Subject.Length > 0).Select(pair => pair.Clause).Distinct(StringComparer.Ordinal))
            {
                var missing = subjects
                    .Where(subject => !pairs.Any(pair => pair.Clause == clause && pair.Subject == subject))
                    .ToArray();

                if (missing.Length > 0)
                {
                    var mapped = subjects.Except(missing, StringComparer.Ordinal);
                    faults.Add($"{guardrail}'s clause '{clause}' is mapped for {string.Join(", ", mapped)} and not for {string.Join(", ", missing)}");
                }
            }

            var text = Uncited(cell);
            var covered = new bool[text.Length];

            foreach (var span in pairs.SelectMany(pair => new[] { pair.Clause, pair.Subject }).Where(span => span.Length > 0))
            {
                var at = text.IndexOf(span, StringComparison.Ordinal);

                for (var index = at; at >= 0 && index < at + span.Length; index++)
                {
                    covered[index] = true;
                }
            }

            var uncovered = Regex.Matches(
                    new string([.. text.Select((character, index) => covered[index] ? ' ' : character)]),
                    @"[A-Za-z][A-Za-z'-]*")
                .Select(word => word.Value)
                .Where(word => !Joiners.Contains(word, StringComparer.Ordinal))
                .ToArray();

            if (uncovered.Length > 0)
            {
                faults.Add($"{guardrail} carries '{string.Join(" ", uncovered)}', which no mapped clause or subject covers");
            }
        }

        faults.AddRange(mapping
            .Where(pair => pair.Test.Length > 0)
            .GroupBy(pair => pair.Test, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => $"{group.Key} holds more than one clause"));

        return faults;
    }

    static string Uncited(string cell) =>
        Regex.Replace(cell, @"\s*\((?:see|owes): [^)]*\)", string.Empty);

    static bool NamesWhole(string text, string name, string joined) =>
        Regex.IsMatch(
            text,
            $"(?<![A-Za-z0-9{Regex.Escape(joined)}]){Regex.Escape(name)}(?![A-Za-z0-9{Regex.Escape(joined)}])");

    // The parameterless test method of exactly this name, lexed, or null where no
    // source declares one.
    internal static TestBody? BodyOf(string test, IReadOnlyList<string> sources)
    {
        foreach (var source in sources)
        {
            var declared = Regex.Match(source, @"public (?:async Task|void) " + Regex.Escape(test) + @"\(\)\s*\{");

            if (!declared.Success)
            {
                continue;
            }

            var lexer = new BodyLexer(source, declared.Index + declared.Length);

            return lexer.Body()
                ? new TestBody(lexer.Code.ToString(), lexer.Literals.ToString())
                : throw new InvalidOperationException($"{test}'s body does not close, so it was not read.");
        }

        return null;
    }

    sealed class BodyLexer(string source, int at)
    {
        int index = at;

        internal StringBuilder Code { get; } = new();

        internal StringBuilder Literals { get; } = new();

        char Peek(int ahead = 0) => index + ahead < source.Length ? source[index + ahead] : '\0';

        internal bool Body()
        {
            var depth = 1;

            while (index < source.Length)
            {
                var here = Peek();

                if (here == '}' && --depth == 0)
                {
                    index++;
                    return true;
                }

                if (here == '{')
                {
                    depth++;
                }

                if (!Token())
                {
                    Code.Append(here);
                    index++;
                }
            }

            return false;
        }

        bool Token()
        {
            var here = Peek();
            var next = Peek(1);

            if (here == '/' && next == '/')
            {
                while (index < source.Length && Peek() != '\n')
                {
                    index++;
                }

                Code.Append(' ');
                return true;
            }

            if (here == '/' && next == '*')
            {
                var close = source.IndexOf("*/", index + 2, StringComparison.Ordinal);
                index = close < 0 ? source.Length : close + 2;
                Code.Append(' ');
                return true;
            }

            if (here == '\'')
            {
                var end = index + (next == '\\' ? 3 : 2);

                while (end < source.Length && source[end] != '\'')
                {
                    end++;
                }

                Literals.Append(source, index + 1, end - index - 1).Append('\n');
                index = end + 1;
                Code.Append(' ');
                return true;
            }

            var prefix = Regex.Match(source[index..Math.Min(index + 12, source.Length)], "^(\\$*)(@?)(\\$*)(\"+)");

            if (!prefix.Success || (here != '$' && here != '@' && here != '"'))
            {
                return false;
            }

            var interpolated = prefix.Groups[1].Length + prefix.Groups[3].Length > 0;
            var verbatim = prefix.Groups[2].Length > 0;
            var quotes = prefix.Groups[4].Length;

            if (quotes >= 3 && !verbatim)
            {
                index += prefix.Length;
                var close = source.IndexOf(new string('"', quotes), index, StringComparison.Ordinal);

                if (close < 0)
                {
                    return false;
                }

                Literals.Append(source, index, close - index).Append('\n');
                index = close + quotes;
                Code.Append(' ');
                return true;
            }

            if (quotes == 2 && !verbatim && !interpolated)
            {
                index += prefix.Length;
                Code.Append(' ');
                return true;
            }

            index += prefix.Length - quotes + 1;
            Quoted(interpolated, verbatim);
            Code.Append(' ');
            return true;
        }

        void Quoted(bool interpolated, bool verbatim)
        {
            while (index < source.Length)
            {
                var here = Peek();

                if (verbatim && here == '"' && Peek(1) == '"')
                {
                    Literals.Append('"');
                    index += 2;
                    continue;
                }

                if (here == '"')
                {
                    index++;
                    break;
                }

                if (!verbatim && here == '\\')
                {
                    Literals.Append(source, index, Math.Min(2, source.Length - index));
                    index += 2;
                    continue;
                }

                if (interpolated && (here is '{' or '}') && Peek(1) == here)
                {
                    Literals.Append(here);
                    index += 2;
                    continue;
                }

                if (interpolated && here == '{')
                {
                    index++;
                    var depth = 1;

                    while (index < source.Length)
                    {
                        var inside = Peek();

                        if (inside == '}' && --depth == 0)
                        {
                            index++;
                            break;
                        }

                        if (inside == '{')
                        {
                            depth++;
                        }

                        if (!Token())
                        {
                            Code.Append(inside);
                            index++;
                        }
                    }

                    Code.Append(' ');
                    Literals.Append(' ');
                    continue;
                }

                Literals.Append(here);
                index++;
            }

            Literals.Append('\n');
        }
    }

    // What each of 13.2's rows resolves to: the checkpoints its Phase cell names, or the
    // operating obligation it cites where it names none.
    static readonly (string Row, string[] Checkpoints, string Obligation)[] WhatCanImprove =
    [
        ("Condition thresholds", [], "The six reason thresholds calibrated from the nights they fired on"),
        ("Which conditions exist", ["8.3", "8.4"], ""),
        ("The ladder rules", ["8.6"], ""),
    ];

    [Fact]
    public void EveryRowOfWhatCanImproveIsReadAgainstTheCheckpointsItNamesOrTheObligationItCites()
    {
        var table = ArchitectureTables.In(File.ReadAllText(Repository.Architecture))
            .Single(one => one.Heading == "13.2 Three things that can improve, shallowest first");

        var cells = table.Body
            .Where(row => row.Count > 2 && row[0].Length > 0)
            .ToDictionary(row => row[0], row => row[2], StringComparer.Ordinal);

        Assert.Empty(WhatCanImproveFaults(
            WhatCanImprove,
            cells,
            PlanCheckpoints.All(),
            ObligationReconciles.In(Corpus.Read("docs/BUILD_PLAN.md")),
            Corpus.Read("docs/PROGRESS.md")));

        // The reader over a constructed table, plan, obligation and record.
        PlanCheckpoint[] plan =
        [
            new("9.1", "### 9.1 The widget register\nBuilds it.\n"),
            new("9.2", "### 9.2 The sprocket column\nBuilds it.\n"),
            new("9.3", "### 9.3 Another register of sprockets\nBuilds it.\n"),
        ];

        var obligations = ObligationReconciles.In(
            "## Carried obligations\n\n| Obligation | Created at | Due at | What produces the evidence |\n|---|---|---|---|\n" +
            "| **Widgets counted from nights** | 9.0 | operating | 30 nights of widgets, read on the run page, which 9.1 builds |\n",
            floor: 1);

        const string Record = "### 9.1 - the widget register   2026-10-01\nBuilt:      it.\n\n";

        var constructed = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Widgets"] = "9, at the widget register and the sprocket column",
            ["Counts"] = "none. It waits on 30 nights, read on the run page (" + "owes" + ": Widgets counted from nights)",
            ["Cogs"] = "9, at the cog table",
            ["Sprockets"] = "9, at the register",
            ["Late"] = "none. It waits on 60 nights, read on the run page (" + "owes" + ": Widgets counted from nights)",
            ["Unread"] = "9, at the widget register",
        };

        Assert.Equal(
            [
                "Widgets names 9.2, which the record does not show built",
                "Cogs names 'cog table', which no checkpoint of phase 9 is titled for",
                "Sprockets names 'register', which 2 checkpoints of phase 9 are titled for",
                "Sprockets resolves to nothing, and its reading says 9.3",
                "Late cites 'Widgets counted from nights', whose trigger of 30 its cell does not state",
                "Missing is read and the table carries no such row",
                "Unread is in the table and no reading names it",
            ],
            WhatCanImproveFaults(
                [
                    ("Widgets", ["9.1", "9.2"], ""),
                    ("Counts", [], "Widgets counted from nights"),
                    ("Cogs", [], ""),
                    ("Sprockets", ["9.3"], ""),
                    ("Late", [], "Widgets counted from nights"),
                    ("Missing", ["9.1"], ""),
                ],
                constructed,
                plan,
                obligations,
                Record));
    }

    internal static IReadOnlyList<string> WhatCanImproveFaults(
        IReadOnlyList<(string Row, string[] Checkpoints, string Obligation)> reading,
        IReadOnlyDictionary<string, string> cells,
        IReadOnlyList<PlanCheckpoint> plan,
        IReadOnlyList<Obligation> obligations,
        string progress)
    {
        var faults = new List<string>();

        foreach (var (row, checkpoints, obligation) in reading)
        {
            if (!cells.TryGetValue(row, out var cell))
            {
                faults.Add($"{row} is read and the table carries no such row");
                continue;
            }

            var phase = Regex.Match(cell, @"^(\d+), at (.+)$");

            if (phase.Success)
            {
                var named = new List<string>();

                foreach (var part in phase.Groups[2].Value.Split(" and ").Select(part => Regex.Replace(part.Trim(), "^the ", string.Empty)))
                {
                    var titled = plan
                        .Where(point => DuePoints.PhaseOf(point.Id) == phase.Groups[1].Value
                            && point.Text.Split('\n')[0].Contains(part, StringComparison.OrdinalIgnoreCase))
                        .ToArray();

                    if (titled.Length == 1)
                    {
                        named.Add(titled[0].Id);
                        continue;
                    }

                    faults.Add(titled.Length == 0
                        ? $"{row} names '{part}', which no checkpoint of phase {phase.Groups[1].Value} is titled for"
                        : $"{row} names '{part}', which {titled.Length} checkpoints of phase {phase.Groups[1].Value} are titled for");
                }

                if (!named.SequenceEqual(checkpoints, StringComparer.Ordinal) && named.Count > 0)
                {
                    faults.Add($"{row} resolves to {string.Join(", ", named)}, and its reading says {string.Join(", ", checkpoints)}");
                }
                else if (named.Count == 0 && checkpoints.Length > 0)
                {
                    faults.Add($"{row} resolves to nothing, and its reading says {string.Join(", ", checkpoints)}");
                }

                faults.AddRange(named
                    .Where(id => !DuePoints.HasLanded(id, progress))
                    .Select(id => $"{row} names {id}, which the record does not show built"));

                continue;
            }

            var cited = Corpus.Citations(Corpus.Obligation, cell, "docs/ARCHITECTURE.html").Select(citation => citation.Detail).ToArray();

            if (cited.Length != 1 || cited[0] != obligation)
            {
                faults.Add($"{row} names no phase and cites {(cited.Length == 0 ? "no obligation" : string.Join(", ", cited))}, and its reading says '{obligation}'");
                continue;
            }

            var owed = obligations.SingleOrDefault(candidate => candidate.Name == obligation);

            if (owed is null || !owed.SaysOperating || owed.Discharged)
            {
                faults.Add($"{row} cites '{obligation}', which is not an open operating row");
                continue;
            }

            var trigger = Regex.Match(owed.Producer, @"^\s*(\d+) (\w+)");
            var surface = Regex.Match(owed.Producer, @"read on the ([^,]+)");

            if (!cell.Contains($"{trigger.Groups[1].Value} {trigger.Groups[2].Value}", StringComparison.Ordinal))
            {
                faults.Add($"{row} cites '{obligation}', whose trigger of {trigger.Groups[1].Value} its cell does not state");
            }

            if (!cell.Contains($"read on the {surface.Groups[1].Value}", StringComparison.Ordinal))
            {
                faults.Add($"{row} cites '{obligation}', whose surface its cell does not name");
            }
        }

        faults.AddRange(cells.Keys
            .Where(row => !reading.Any(read => read.Row == row))
            .Select(row => $"{row} is in the table and no reading names it"));

        return faults;
    }

    [Fact]
    public async Task TheLoopHasChangedNothingOnEvidenceBelowItsStatedMinimum()
    {
        // The done condition that is about the world rather than about the code,
        // asserted over the register's rows and the version windows a whole night
        // ran over. A candidate retired or a version's window closed is a change
        // the loop made, and a night is the one part of the loop that runs with
        // nobody deciding: so the store holds a registered candidate and a version
        // open beside its live window, the night runs over evidence far below the
        // minimum, and the rows are read before and after it.
        //
        // Asked of a store the suite built rather than of the operator's, because
        // a check that reads the live store is a check whose result depends on
        // last night.
        using var store = new TemporaryStore().Migrated();

        var night = new DateTimeOffset(2026, 9, 8, 21, 10, 0, TimeSpan.Zero);
        var before = FixedClock.At(night.AddHours(-1), SessionZones.UnitedStates);

        var registrar = new CandidateRegistrar(before, store.DatabaseFile);
        var scorer = new Worker.Rules.RuleVersionScorer(before, store.DatabaseFile);

        Assert.Equal(CandidateRegistrar.Registered, (await registrar.RegisterAsync(
            "momentum index at thirty",
            "the relative strength index at or below thirty",
            "the share of its setups that beat their own break-even",
            MomentumIndexReading.EvaluatorName,
            new Dictionary<string, double>(StringComparer.Ordinal) { [MomentumIndexReading.Level] = 30 },
            "report-register")).Outcome);

        Assert.Null(await scorer.OpenLiveAsync(LadderRules.NearExitSkip, "report-live"));
        Assert.Null(await scorer.OpenAsync(
            LadderRules.NearExitSkip,
            "three typical days",
            new Dictionary<string, double>(StringComparer.Ordinal) { ["nearExitInTypicalDays"] = 3 },
            "report-version"));

        var registerBefore = await registrar.RowsAsync();
        var windowsBefore = await scorer.VersionsAsync();

        var code = await Worker.Nightly.RunAsync(
            new Core.Configuration.StoreLocation(Path.GetDirectoryName(store.DatabaseFile)!),
            Path.Combine(Repository.Root, "fixtures", "membership-2026-09-05"),
            "GSPC",
            FixedClock.At(night, SessionZones.UnitedStates),
            new StringWriter(),
            new StringWriter(),
            "report-night");

        Assert.Equal(0, code);

        long Count(string sql)
        {
            using var connection = store.Open();
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            return Convert.ToInt64(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);
        }

        // The night evaluated the candidate on every listing row it wrote and scored the
        // version on every one, skipping neither.
        var listed = Count("SELECT COUNT(*) FROM listing;");

        Assert.True(listed >= 4, $"The night wrote {listed} listing row(s), expected at least 4.");

        Assert.Equal(
            (listed, 0L, listed),
            (
                Count("SELECT COUNT(*) FROM listing, json_each(listing.shadow_reasons, '$.candidates') AS evaluated WHERE json_extract(evaluated.value, '$.candidate') = 'momentum index at thirty';"),
                Count("SELECT COUNT(*) FROM listing, json_each(listing.shadow_reasons, '$.skipped') AS skipped WHERE json_extract(skipped.value, '$.candidate') = 'momentum index at thirty';"),
                Count("SELECT COUNT(*) FROM version_score WHERE version = 'three typical days';")));

        // On evidence below the minimum, stated rather than assumed.
        var resolved = Count(
            $"SELECT COUNT(*) FROM forward_return WHERE outcome IN ('{Core.Returns.ForwardReturnSeries.Win}', '{Core.Returns.ForwardReturnSeries.Loss}');");

        Assert.True(
            resolved < Core.Returns.ReasonVerdict.MinimumResolved,
            $"The night's store holds {resolved} resolved setups, which is not the evidence below the minimum this is about.");

        // And the rows the loop's changes would be are as they were, row for row.
        var registerAfter = await registrar.RowsAsync();
        var windowsAfter = await scorer.VersionsAsync();

        Assert.Equal(registerBefore, registerAfter);
        Assert.Equal(windowsBefore, windowsAfter);
        Assert.Empty(ChangesMadeOnEvidence(registerAfter, windowsAfter));

        // What makes that more than a reader that cannot find anything: the same
        // reader finds a retirement, finds a closed window, and does not read an
        // open window as a change.
        var withdrawn = await new CandidateRegistrar(FixedClock.At(night.AddDays(1), SessionZones.UnitedStates), store.DatabaseFile)
            .RetireAsync("momentum index at thirty", "0 resolved setups of a minimum of 250", "report-retire");

        Assert.Equal(CandidateRegistrar.Retired, withdrawn.Outcome);
        Assert.Contains("momentum index at thirty", Assert.Single(ChangesMadeOnEvidence(await registrar.RowsAsync(), windowsAfter)), StringComparison.Ordinal);

        var closed = new RuleVersionRow(
            LadderRules.NearExitSkip,
            "three typical days",
            "{}",
            "000000000000",
            "8.6",
            new DateTimeOffset(2026, 9, 16, 21, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 9, 17, 21, 0, 0, TimeSpan.Zero),
            "four typical days");

        Assert.Single(ChangesMadeOnEvidence([], [closed]));
        Assert.Empty(ChangesMadeOnEvidence([], [closed with { ClosedAt = null, ReplacedBy = null }]));
    }

    // What counts as the loop having changed something: a candidate retired, or a
    // rule version's window closed. Named apart from the fact so the proof above
    // runs this reader rather than a copy of it.
    internal static IReadOnlyList<string> ChangesMadeOnEvidence(
        IReadOnlyList<RegisterRow> register,
        IReadOnlyList<RuleVersionRow> versions)
    {
        var changes = new List<string>();

        changes.AddRange(register
            .Where(row => row.Event == CandidateFamily.Retired)
            .Select(row => $"'{row.Retires}' was retired on the evidence: {row.Evidence}"));

        changes.AddRange(versions
            .Where(row => row.ClosedAt is not null)
            .Select(row => $"'{row.Version}' of '{row.Rule}' had its window closed, replaced by '{row.ReplacedBy}'"));

        return changes;
    }

    // 8.0's pair. A plan or a correction that moves the claims names the move here.
    const int PredictedClaims = 361;

    const int PredictedOutOfScope = 0;

    // Rows the harness reads as the parts they state where the prediction counted one claim.
    static readonly string[] ReadAsItsParts =
    [
        CheckReach.Key("15.10 Run", "Shadow candidates"),
        CheckReach.Key("15.11 How a reason's record is displayed", "At or above the minimum"),
        CheckReach.Key(Scope.LimitsTable, "Frozen measurement windows"),
    ];

    // Phase 9's own rows, added when the phase opened and out of scope until its
    // checkpoints land. Held apart from the rows above because those are claims the
    // prediction missed and these are claims it could not have made: 8.0 predicted
    // the corpus at the end of phase 8, and a phase opened after it is not a miss.
    static readonly string[] PhaseNineRows =
    [
        CheckReach.Key("15.7 Tonight", "Research, per row"),
        CheckReach.Key("15.7 Tonight", "Ask for a report"),
        CheckReach.Key("15.15 Queue", "Outstanding"),
        CheckReach.Key("15.15 Queue", "Being written"),
        CheckReach.Key("15.15 Queue", "Settled"),
        CheckReach.Key("15.15 Queue", "Take it out"),
        CheckReach.Key("15.15 Queue", "Which lane would write one"),
    ];

    // The ones a checkpoint has since drawn, which carry a verdict and no longer read as
    // out of scope. A row moves here in the commit that builds it, so the out-of-scope
    // figure below is stated rather than followed: a row that stopped being drawn would
    // read as unexamined and fail rather than quietly rejoining the count above.
    static readonly string[] PhaseNineDrawn =
    [
        CheckReach.Key("15.7 Tonight", "Research, per row"),
        CheckReach.Key("15.7 Tonight", "Ask for a report"),
        CheckReach.Key("15.15 Queue", "Outstanding"),
        CheckReach.Key("15.15 Queue", "Being written"),
        CheckReach.Key("15.15 Queue", "Settled"),
        CheckReach.Key("15.15 Queue", "Take it out"),
        CheckReach.Key("15.15 Queue", "Which lane would write one"),
    ];

    // Phase 10's rows, each drawn by the checkpoint that added it, so none reads as out of scope:
    // 10.1's reward to risk on tonight's list, the run page's measure of the list's order as the
    // six parts its row states, and the blocks section 17 pins; 10.2's record region as the ten
    // parts its own row states, with the looks, the calibrated bar and the power section 17 pins.
    static readonly string[] PhaseTenRows =
    [
        CheckReach.Key("15.7 Tonight", "The list, the reward to risk or the plan's reason for none"),
        CheckReach.Key("15.10 Run", "Tonight's order, the three orders of tonight's list over the twenty each would draw"),
        CheckReach.Key("15.10 Run", "Tonight's order, the old order named as the benchmark"),
        CheckReach.Key("15.10 Run", "Tonight's order, the setups each order drew"),
        CheckReach.Key("15.10 Run", "Tonight's order, the setups whose whole window has closed"),
        CheckReach.Key("15.10 Run", "Tonight's order, the blocks holding one against the floor"),
        CheckReach.Key("15.10 Run", "Tonight's order, no comparison drawn before every order reaches it"),
        CheckReach.Key(Scope.LimitsTable, "Blocks a record is judged over"),
        CheckReach.Key("15.10 Run", "Candidates' records, the verdict field stating what the last look read and what triggers the next"),
        CheckReach.Key("15.10 Run", "Candidates' records, the nightly figure beside it labelled as monitoring"),
        CheckReach.Key("15.10 Run", "Candidates' records, the Poisson-binomial tail labelled and deciding nothing"),
        CheckReach.Key("15.10 Run", "Candidates' records, each look's setups and the share they won against the bar the calibration set"),
        CheckReach.Key("15.10 Run", "Candidates' records, the level that look spent and whether it crossed its boundary"),
        CheckReach.Key("15.10 Run", "Candidates' records, the smallest excess that look could have detected"),
        CheckReach.Key("15.10 Run", "Candidates' records, the design effect and what a loss cost in multiples of the planned risk"),
        CheckReach.Key("15.10 Run", "Candidates' records, the setups entered and stopped on one session and those stopped out on a session the name reported on"),
        CheckReach.Key("15.10 Run", "Candidates' records, the step the graph stands at with its level and the count of candidates ever registered"),
        CheckReach.Key("15.10 Run", "Candidates' records, no name anywhere in it"),
        CheckReach.Key(Scope.LimitsTable, "Looks a candidate's verdict is read at"),
        CheckReach.Key(Scope.LimitsTable, "The calibrated bar"),
        CheckReach.Key(Scope.LimitsTable, "Power stated at a look"),

        // The 10.4 correction, which gave a version's record a store of its own because the two
        // tables it was read from are dropped a year back.
        CheckReach.Key(Scope.StoresTable, "Version blocks"),
    ];

    // Phase 11's rows, written by the document pass at 11.0 before any of them is drawn, each
    // placed at the checkpoint that builds it and read as out of scope until that one lands.
    static readonly string[] PhaseElevenRows =
    [
        CheckReach.Key("15.9 Name", "Each move beside its group"),
        // 11.6 read the Peers row as its parts, each a claim of its own.
        CheckReach.Key("15.9 Name", "Peers, beneath the table of the biggest moves"),
        CheckReach.Key("15.9 Name", "Peers, every member of the name's group by price alone"),
        CheckReach.Key("15.9 Name", "Peers, in ticker order with the name's own row marked"),
        CheckReach.Key("15.9 Name", "Peers, ranking none"),
        CheckReach.Key("15.9 Name", "Peers, each row carrying the close"),
        CheckReach.Key("15.9 Name", "Peers, how far it sits below the stored year's high"),
        CheckReach.Key("15.9 Name", "Peers, its return over sixty sessions"),
        CheckReach.Key("15.9 Name", "Peers, its trend state"),
        CheckReach.Key("15.9 Name", "Peers, the distance row mark"),
        CheckReach.Key("15.9 Name", "Peers, a key saying how to read it"),
        // 11.7 read the Earnings reactions row as its parts, each a claim of its own.
        CheckReach.Key("15.9 Name", "Earnings reactions, beside the earnings setups"),
        CheckReach.Key("15.9 Name", "Earnings reactions, one row per print over the calendar's year behind"),
        CheckReach.Key("15.9 Name", "Earnings reactions, each row carrying its report date"),
        CheckReach.Key("15.9 Name", "Earnings reactions, its timing"),
        CheckReach.Key("15.9 Name", "Earnings reactions, the session it moved on"),
        CheckReach.Key("15.9 Name", "Earnings reactions, the estimate"),
        CheckReach.Key("15.9 Name", "Earnings reactions, the actual"),
        CheckReach.Key("15.9 Name", "Earnings reactions, the provider's surprise"),
        CheckReach.Key("15.9 Name", "Earnings reactions, that session's move"),
        CheckReach.Key("15.9 Name", "Earnings reactions, a print with no filed estimate saying none was filed"),
        CheckReach.Key("15.9 Name", "Earnings reactions, a key saying how to read it"),
        // 11.8 read the Dividend row as its parts, each a claim of its own.
        CheckReach.Key("15.9 Name", "Dividend, in the numbers section"),
        CheckReach.Key("15.9 Name", "Dividend, from the newest filing alone"),
        CheckReach.Key("15.9 Name", "Dividend, the forward annual rate"),
        CheckReach.Key("15.9 Name", "Dividend, the forward yield as the provider states it"),
        CheckReach.Key("15.9 Name", "Dividend, the payout ratio"),
        CheckReach.Key("15.9 Name", "Dividend, the ex-dividend date and the pay date"),
        CheckReach.Key("15.9 Name", "Dividend, one line saying the provider files none"),

        // 11.2, the queue page's row stating when each request will be written, which
        // arrives drawn.
        CheckReach.Key("15.15 Queue", "When each will be written"),

        // 11.3, tonight's row and the selected name stating the report's state, drawn here.
        CheckReach.Key("15.7 Tonight", "The report's state"),

        // 11.4, section 14's step writing the night's own request and section 17's count of one.
        CheckReach.Key(NightlyRunSteps.Heading, "Ask for a report on the first name drawn on tonight's list, one request marked as asked by the night unless that name has one outstanding or being written, and start the drain as a press does, whose pass is its own run at the off-peak rate with its calls and its requests on its own rows (see: The night asks for a report on the first name of its list)."),
        CheckReach.Key(Scope.LimitsTable, "Reports the night asks for"),

        // 11.5, section 17's floor for a name's industry to be its group.
        CheckReach.Key(Scope.LimitsTable, "Group floor"),

        // 11.6, section 17's window for a peer's return, the peer readings' own store and
        // their fixture row.
        CheckReach.Key(Scope.LimitsTable, "Peer return window"),
        CheckReach.Key(Scope.StoresTable, "Peer readings"),
        CheckReach.Key(Scope.FixtureTable, "peers"),

        // 11.7, the reaction record's own store and its fixture row.
        CheckReach.Key(Scope.StoresTable, "Earnings reactions"),
        CheckReach.Key(Scope.FixtureTable, "reactions"),
    ];

    // The ones a phase 11 checkpoint has since drawn. A row moves here in the commit that
    // builds it, for the reason the phase 9 list above says.
    static readonly string[] PhaseElevenDrawn =
    [
        CheckReach.Key("15.15 Queue", "When each will be written"),
        CheckReach.Key("15.7 Tonight", "The report's state"),
        CheckReach.Key(NightlyRunSteps.Heading, "Ask for a report on the first name drawn on tonight's list, one request marked as asked by the night unless that name has one outstanding or being written, and start the drain as a press does, whose pass is its own run at the off-peak rate with its calls and its requests on its own rows (see: The night asks for a report on the first name of its list)."),
        CheckReach.Key(Scope.LimitsTable, "Reports the night asks for"),
        CheckReach.Key("15.9 Name", "Each move beside its group"),
        CheckReach.Key(Scope.LimitsTable, "Group floor"),
        CheckReach.Key("15.9 Name", "Peers, beneath the table of the biggest moves"),
        CheckReach.Key("15.9 Name", "Peers, every member of the name's group by price alone"),
        CheckReach.Key("15.9 Name", "Peers, in ticker order with the name's own row marked"),
        CheckReach.Key("15.9 Name", "Peers, ranking none"),
        CheckReach.Key("15.9 Name", "Peers, each row carrying the close"),
        CheckReach.Key("15.9 Name", "Peers, how far it sits below the stored year's high"),
        CheckReach.Key("15.9 Name", "Peers, its return over sixty sessions"),
        CheckReach.Key("15.9 Name", "Peers, its trend state"),
        CheckReach.Key("15.9 Name", "Peers, the distance row mark"),
        CheckReach.Key("15.9 Name", "Peers, a key saying how to read it"),
        CheckReach.Key(Scope.LimitsTable, "Peer return window"),
        CheckReach.Key(Scope.StoresTable, "Peer readings"),
        CheckReach.Key(Scope.FixtureTable, "peers"),
        CheckReach.Key("15.9 Name", "Earnings reactions, beside the earnings setups"),
        CheckReach.Key("15.9 Name", "Earnings reactions, one row per print over the calendar's year behind"),
        CheckReach.Key("15.9 Name", "Earnings reactions, each row carrying its report date"),
        CheckReach.Key("15.9 Name", "Earnings reactions, its timing"),
        CheckReach.Key("15.9 Name", "Earnings reactions, the session it moved on"),
        CheckReach.Key("15.9 Name", "Earnings reactions, the estimate"),
        CheckReach.Key("15.9 Name", "Earnings reactions, the actual"),
        CheckReach.Key("15.9 Name", "Earnings reactions, the provider's surprise"),
        CheckReach.Key("15.9 Name", "Earnings reactions, that session's move"),
        CheckReach.Key("15.9 Name", "Earnings reactions, a print with no filed estimate saying none was filed"),
        CheckReach.Key("15.9 Name", "Earnings reactions, a key saying how to read it"),
        CheckReach.Key(Scope.StoresTable, "Earnings reactions"),
        CheckReach.Key(Scope.FixtureTable, "reactions"),
        CheckReach.Key("15.9 Name", "Dividend, in the numbers section"),
        CheckReach.Key("15.9 Name", "Dividend, from the newest filing alone"),
        CheckReach.Key("15.9 Name", "Dividend, the forward annual rate"),
        CheckReach.Key("15.9 Name", "Dividend, the forward yield as the provider states it"),
        CheckReach.Key("15.9 Name", "Dividend, the payout ratio"),
        CheckReach.Key("15.9 Name", "Dividend, the ex-dividend date and the pay date"),
        CheckReach.Key("15.9 Name", "Dividend, one line saying the provider files none"),
    ];

    // Rows the document gained after the prediction, each one claim.
    static readonly string[] AddedAfterThePrediction =
    [
        CheckReach.Key(Scope.LimitsTable, "The three candidates' numbers"),
        CheckReach.Key(Scope.FailureTable, "A ticker the index feed stops listing"),
        CheckReach.Key(Scope.FailureTable, "The provider serves no year for a name the backfill asks for"),
        CheckReach.Key(Scope.LimitsTable, "Waiting on another writer"),
        CheckReach.Key("15.8 Universe", "Researched"),
        CheckReach.Key("15.9 Name", "Listing history"),
        CheckReach.Key("15.9 Name", "A pass as it runs"),
        CheckReach.Key("15.9 Name", "Sections left out"),
        CheckReach.Key("15.9 Name", "Contents"),
        CheckReach.Key("15.9 Name", "How far each band is"),
        CheckReach.Key("15.9 Name", "The case for and the case against"),
        CheckReach.Key("15.9 Name", "The risks as parts"),

        // The 5.8 correction that numbered tonight's list, whose row gains two parts: the rows'
        // places and the line counting them.
        CheckReach.Key("15.7 Tonight", "The list, each numbered by its place in that order"),
        CheckReach.Key("15.7 Tonight", "The list, a line above them stating how many are drawn of how many fired"),

        // 9.2, the worker's half of the request store, which is a component of its own
        // rather than the runner's: the pass writes the research and this moves the
        // request beside it.
        CheckReach.Key(Scope.CatalogueTable, "Request drain"),
        CheckReach.Key(Scope.MatrixTable, "Request drain"),
        .. PhaseNineRows,
        .. PhaseTenRows,
        .. PhaseElevenRows,
    ];


    // Rows the document lost after the prediction. The provenance footer went at the 5.8
    // correction that stopped the page drawing a refused draft: every line of it restated
    // a date already stamped on the card it described, and that promise is now held there.
    static readonly string[] RemovedAfterThePrediction =
    [
        CheckReach.Key("15.9 Name", "Provenance footer, computed tonight"),
        CheckReach.Key("15.9 Name", "Provenance footer, fundamentals as of a filing date"),
        CheckReach.Key("15.9 Name", "Provenance footer, research as of the date it was written"),
    ];

    [Fact]
    public void ThePairEightZeroPredictedIsCheckedAgainstTheActual()
    {
        // The verdicts the corpus declares; whether each PASS held in a run is the phase report's figure.
        var report = Report();

        var expected = PredictedClaims
            + ReadAsItsParts.Sum(key => Scope.ElementsOf(key).Count - 1)
            + AddedAfterThePrediction.Length
            - RemovedAfterThePrediction.Length;

        Assert.All(AddedAfterThePrediction, key => Assert.Contains(report.Claims, claim => CheckReach.Key(claim.Table, claim.Subject) == key));
        Assert.All(RemovedAfterThePrediction, key => Assert.DoesNotContain(report.Claims, claim => CheckReach.Key(claim.Table, claim.Subject) == key));

        // Out of scope was zero while phase 8 was the last phase. Phase 9's rows are
        // placed at checkpoints the record does not carry, so each reads as out of scope
        // and none of them passes, which is what the pass figure is short by. Phase 11's
        // are the same, from the document pass that wrote them to the checkpoint that
        // draws each.
        var outOfScope = PredictedOutOfScope
            + PhaseNineRows.Length - PhaseNineDrawn.Length
            + PhaseElevenRows.Length - PhaseElevenDrawn.Length;

        Assert.All(PhaseNineDrawn, key => Assert.Contains(PhaseNineRows, row => row == key));
        Assert.All(PhaseElevenDrawn, key => Assert.Contains(PhaseElevenRows, row => row == key));

        Assert.Equal(
            (expected, outOfScope, 0, expected - outOfScope),
            (report.Claims.Count, report.Count(Verdict.OutOfScope), report.Count(Verdict.Unexamined), report.Count(Verdict.Pass)));
    }
}
