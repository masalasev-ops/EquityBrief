using System.Reflection;
using EquityBrief.Core.Candidates;
using EquityBrief.Core.Rules;
using EquityBrief.Core.Time;
using EquityBrief.Tests.Harness;
using EquityBrief.Worker.Candidates;

namespace EquityBrief.Tests.Checks;

// architecture-conformance, 8.7: the loop's own two tables.
//
// Section 13.2 says which checkpoint builds each thing that can improve, and
// 13.3 says the rules the loop is safe under. Both were placed at the report
// rather than at the first checkpoint that touches them, because a table claimed
// before most of it exists is a verdict over rows nothing can be wrong about.
//
// The guardrails are mapped to the tests that hold them rather than asserted
// again here. A second implementation of a guardrail inside the report would be
// the report agreeing with itself; what the report can say is that each rule
// written before any data existed has something behind it now.
public partial class ArchitectureConformance
{
    // Each clause of each guardrail, the test that holds it, and the code that
    // test has to exercise for the pairing to mean anything.
    //
    // By clause rather than by guardrail, because a guardrail is several promises
    // and a test holding one of them holds none of the others: 8.7 mapped each
    // guardrail to one test, and three of the eight pointed at tests about
    // something else while the mapping check, which asked only that each name
    // existed and none was used twice, passed. The mechanism is what closes that:
    // the test's own body, read from its source with its comments stripped, has
    // to name the code the clause is enforced by, so a test chosen for its name
    // alone is refused. A clause is quoted from the guardrail's own cell, so a
    // guardrail rewritten leaves its mapping pointing at words no longer there.
    //
    // What a mechanism cannot say is whether the test asserts the clause well.
    // That is a reading, and the pairings below are what a reader starts from.
    static readonly (string Guardrail, string Clause, string Test, string Mechanism)[] HeldBy =
    [
        ("Registered in advance", "written to the append-only register with its rule, its test and the date",
            "AnUpdateAndADeleteAreBothRefusedByTheTableItself", "DELETE"),
        ("Registered in advance", "before it is scored",
            "ACandidateRegisteredAfterTheNightStartedIsNotEvaluatedByIt", "ShadowColumn.StandingAt"),
        ("Registered in advance", "The family has a stated maximum size",
            "AnEvaluatorNobodyCarriesAndAFamilyAtItsMaximumAreBothRefusedAtTheWrite", "CandidateFamily.Maximum"),
        ("Corrected for the family", "the significance threshold is divided by the number of registered candidates",
            "TheDivisorCountsTheRowsRegisteredBeforeTheWindowOpenedAndNoOthers", "CandidateFamily.Divisor"),
        ("Corrected for the family", "the divisor is recorded with the verdict",
            "TheShareAndTheBarAreDrawnWithTheVerdictAndItsDivisorFromEightFive", "data-divisor"),
        ("Minimum resolved setups", "no verdict of any kind below the stated minimum",
            "NoVerdictAppearsBelowEitherFloorAndTheOneThatIsShortIsNamed", "ReasonVerdict.BelowTheResolvedMinimum"),
        ("Minimum resolved setups", "a higher minimum before a live condition may be retired",
            "ALiveReasonIsNeitherRegisteredNorRetiredThroughTheRegister", "ReasonVerdict.MinimumBeforeALiveReasonIsRetired"),
        ("Unresolved is never a win", "neither hit its target nor its stop within the time cap",
            "ASetupIsScoredFromItsEntryAndATargetReachedBeforeItIsNeverAWin", "ForwardReturnSeries.Unresolved"),
        ("Unresolved is never a win", "reported in its own column and excluded from the rate",
            "ASetupNobodyEnteredIsDrawnInItsOwnColumnAndCountsTowardNoRate", "RunScreen.Tracks"),
        ("Shadow before live", "no condition appears on the list until it has a shadow record meeting the minimum",
            "SectionElevensSixReasonsAreTheSixTheCodeEvaluates", "ShortlistSeries.Reasons"),
        ("Shadow before live", "Shadow conditions are scored and stored nightly",
            "AShadowCandidateIsEvaluatedOnTheNightsNoLiveReasonFired", "shadow_reasons"),
        ("Shadow before live", "shown nowhere",
            "NoScreenCarriesAShadowEvaluationOfAName", "shadow_reasons"),
        ("Frozen windows", "a rule or threshold is not changed during a window it is being measured over",
            "ALiveRuleThatMovedInsideAnOpenWindowIsFoundAndOneThatHasNotIsNot", "RuleVersions.Drifted"),
        ("Frozen windows", "A change starts a new window and the old one is kept",
            "AVersionChangeOpensANewWindowAndKeepsThePreviousOne", "CloseAsync"),
        ("Adding restarts the clock", "registering a new candidate after the family is set inflates the family",
            "TheShadowRegionStatesHowManyAreRegisteredAndTheDivisorThatNumberSets", "RunScreen.Shadow"),
        ("Adding restarts the clock", "the correction changes and the affected verdicts are recomputed",
            "AFamilyThatGrowsCorrectsEveryVerdictReadFromItAgain", "ReasonVerdict.For"),
        ("Every change is recorded", "written with the evidence that produced it",
            "ARetirementIsANewRowNamingWhatItRetiresAndTheOriginalStands", "Evidence"),
        ("Every change is recorded", "the version it replaced",
            "TheVersionVerbOpensAWindowBesideItsLiveOneListsThemAndClosesOneNamingWhatReplacedIt", "ReplacedBy"),
    ];

    [Fact]
    public void EveryGuardrailIsMappedClauseByClauseToATestThatExercisesItsMechanism()
    {
        var table = ArchitectureTables.In(File.ReadAllText(Repository.Architecture))
            .Single(one => one.Heading == "13.3 The guardrails");

        var rows = table.Body
            .Where(row => row.Count > 1 && row[0].Length > 0)
            .ToDictionary(row => row[0], row => row[1], StringComparer.Ordinal);

        // The population, read off the document rather than counted here. A
        // guardrail added to the table arrives with no mapping rather than being
        // silently covered by the ones already there.
        Assert.True(rows.Count >= 8, $"Read {rows.Count} guardrail(s) from 13.3, expected at least 8.");

        var methods = Assembly.GetExecutingAssembly().GetTypes()
            .SelectMany(type => type.GetMethods())
            .Where(method => method.GetCustomAttributes().Any(attribute => attribute.GetType().Name == "FactAttribute"))
            .Select(method => method.Name)
            .ToHashSet(StringComparer.Ordinal);

        var separator = Path.DirectorySeparatorChar;

        var sources = Directory.EnumerateFiles(Path.Combine(Repository.Root, "src", "EquityBrief.Tests"), "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{separator}bin{separator}", StringComparison.Ordinal)
                && !path.Contains($"{separator}obj{separator}", StringComparison.Ordinal))
            .Select(File.ReadAllText)
            .ToArray();

        Assert.Empty(MappingFaults(HeldBy, rows, methods, sources));

        // No test holds two clauses. One test standing for two promises is one
        // promise unheld the day that test is narrowed to the other.
        Assert.Equal(HeldBy.Length, HeldBy.Select(pair => pair.Test).Distinct(StringComparer.Ordinal).Count());

        // The reader, over a constructed table, suite and source, so none of the
        // faults above can be absent by being unreadable.
        var constructedRows = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["A rule"] = "a thing is refused at the write, and it is shown nowhere",
            ["Another rule"] = "a thing nobody holds",
        };

        string[] constructedSource =
        [
            "public class Held\n{\n    [Fact]\n    public void TheWriteRefusesIt()\n    {\n        // Registrar.Refusal is named in a comment too.\n        Assert.NotNull(Registrar.Refusal(rows));\n    }\n\n" +
            "    [Fact]\n    public void ANameThatSoundsRight()\n    {\n        // Registrar.Refusal would be the thing, and the body never calls it.\n        Assert.True(true);\n    }\n}\n",
        ];

        var faults = MappingFaults(
            [
                ("A rule", "refused at the write", "TheWriteRefusesIt", "Registrar.Refusal"),
                ("A rule", "shown nowhere", "ANameThatSoundsRight", "Registrar.Refusal"),
                ("A rule", "a clause the cell does not carry", "TheWriteRefusesIt", "Registrar.Refusal"),
                ("A rule", "refused at the write", "ATestTheSuiteDoesNotCarry", "Registrar.Refusal"),
            ],
            constructedRows,
            new HashSet<string>(["TheWriteRefusesIt", "ANameThatSoundsRight"], StringComparer.Ordinal),
            constructedSource);

        Assert.Equal(
            [
                "A rule's clause 'shown nowhere' names ANameThatSoundsRight, whose body does not exercise Registrar.Refusal",
                "A rule's clause 'a clause the cell does not carry' is not in its cell",
                "A rule's clause 'refused at the write' names ATestTheSuiteDoesNotCarry, which the suite does not carry",
                "Another rule has no clause mapped",
            ],
            faults);
    }

    static IReadOnlyList<string> MappingFaults(
        IReadOnlyList<(string Guardrail, string Clause, string Test, string Mechanism)> mapping,
        IReadOnlyDictionary<string, string> rows,
        IReadOnlySet<string> methods,
        IReadOnlyList<string> sources)
    {
        var faults = new List<string>();

        foreach (var (guardrail, clause, test, mechanism) in mapping)
        {
            if (!rows.TryGetValue(guardrail, out var cell))
            {
                faults.Add($"{guardrail} is mapped and the table carries no such guardrail");
                continue;
            }

            if (!cell.Contains(clause, StringComparison.Ordinal))
            {
                faults.Add($"{guardrail}'s clause '{clause}' is not in its cell");
                continue;
            }

            if (!methods.Contains(test))
            {
                faults.Add($"{guardrail}'s clause '{clause}' names {test}, which the suite does not carry");
                continue;
            }

            if (BodyOf(test, sources) is not { } body || !body.Contains(mechanism, StringComparison.Ordinal))
            {
                faults.Add($"{guardrail}'s clause '{clause}' names {test}, whose body does not exercise {mechanism}");
            }
        }

        faults.AddRange(rows.Keys
            .Where(guardrail => !mapping.Any(pair => pair.Guardrail == guardrail))
            .Select(guardrail => $"{guardrail} has no clause mapped"));

        return faults;
    }

    // A test method's body with its line comments removed, or null where no
    // source declares it.
    static string? BodyOf(string test, IReadOnlyList<string> sources)
    {
        foreach (var source in sources)
        {
            var declared = System.Text.RegularExpressions.Regex.Match(
                source,
                @"public (?:async Task|void) " + System.Text.RegularExpressions.Regex.Escape(test) + @"\(\)\s*\{");

            if (!declared.Success)
            {
                continue;
            }

            var at = declared.Index + declared.Length;
            var depth = 1;

            while (depth > 0 && at < source.Length)
            {
                depth += source[at] switch { '{' => 1, '}' => -1, _ => 0 };
                at++;
            }

            return string.Join(
                "\n",
                source[(declared.Index + declared.Length)..at]
                    .Split('\n')
                    .Select(line => line.TrimStart().StartsWith("//", StringComparison.Ordinal) ? string.Empty : line));
        }

        return null;
    }

    [Fact]
    public void EveryRowOfWhatCanImproveNamesTheCheckpointThatBuiltItOrTheReasonItNamesNone()
    {
        // 13.2's three rows. Two name a checkpoint of phase 8 and the record
        // shows those built; the third names none, and what it names instead is
        // evidence the calendar produces rather than a checkpoint, which is the
        // deferral form the corpus requires.
        var table = ArchitectureTables.In(File.ReadAllText(Repository.Architecture))
            .Single(one => one.Heading == "13.2 Three things that can improve, shallowest first");

        var rows = table.Body.Where(row => row.Count > 2 && row[0].Length > 0).ToArray();

        Assert.Equal(3, rows.Length);

        var progress = Corpus.Read("docs/PROGRESS.md");
        var atAPhase = rows.Where(row => row[2].StartsWith("8,", StringComparison.Ordinal)).ToArray();

        Assert.Equal(2, atAPhase.Length);

        Assert.Equal(
            ["The ladder rules", "Which conditions exist"],
            [.. atAPhase.Select(row => row[0]).Order(StringComparer.Ordinal)]);

        // Both are built, read through the same reader the reconciliation uses
        // rather than by a text match on the record.
        foreach (var checkpoint in new[] { "8.3", "8.4", "8.6" })
        {
            Assert.True(
                DuePoints.HasLanded(checkpoint, progress),
                $"13.2 says phase 8 builds what can improve and the record does not show {checkpoint} as built.");
        }

        // The third names no checkpoint and says why: the calibration waits on
        // nights, which no checkpoint accumulates, so the row carries an
        // operating obligation instead. Its own words are what this reads.
        var carried = Assert.Single(rows, row => row[2].StartsWith("none", StringComparison.Ordinal));

        Assert.Equal("Condition thresholds", carried[0]);
        Assert.Contains("no checkpoint accumulates", carried[2], StringComparison.Ordinal);
        Assert.Contains("operating obligation", carried[2], StringComparison.Ordinal);
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
        // last night. 8.7 asked it of two empty lists handed to the reader, which
        // could not have come back anything but empty.
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

        // The night ran the loop over both: the candidate was evaluated into the
        // shadow column and the version was scored.
        Assert.True(
            Count("SELECT COUNT(*) FROM listing WHERE shadow_reasons LIKE '%momentum index at thirty%';") > 0,
            "The night evaluated no shadow candidate, so it ran nothing of the loop.");
        Assert.True(
            Count("SELECT COUNT(*) FROM version_score;") > 0,
            "The night scored no version, so it ran nothing of the loop.");

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

    [Fact]
    public void ThePairEightZeroPredictedIsCheckedAgainstTheActual()
    {
        // The report's own arithmetic. 8.0 predicted 361 claims and 361 PASS with
        // nothing out of scope. The third is exact and the first two are higher,
        // and what moved is named in the record rather than reconciled away: six
        // section 15 rows the prediction counted as one claim each are read by the
        // harness as the things they state, and the version store's claims got a
        // roster row of their own. Neither is a claim added to the document. The
        // document's rows did not move; the number of verdicts over them did.
        var report = Report();

        Assert.Equal(0, report.Count(Verdict.OutOfScope));
        Assert.Equal(0, report.Count(Verdict.Unexamined));
        Assert.Equal(0, report.Count(Verdict.Fail));

        // Every claim passes, which is what nothing out of scope and nothing
        // unexamined means once nothing has failed.
        Assert.Equal(report.Claims.Count, report.Count(Verdict.Pass));

        // The prediction is the floor, so a run below it is a claim population
        // that shrank rather than one that grew by decomposition.
        Assert.True(
            report.Claims.Count >= 361,
            $"The report carries {report.Claims.Count} claims and 8.0 predicted at least 361.");
    }
}
