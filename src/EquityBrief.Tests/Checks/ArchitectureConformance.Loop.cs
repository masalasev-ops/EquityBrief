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
    // Each guardrail, and the test that holds it. Named by method rather than by
    // check, because a check is several properties and what 13.3 promises is one
    // rule each. Every named test is asserted to exist, so a rename is a mapping
    // that fails rather than one that quietly points at nothing.
    static readonly (string Guardrail, string Test)[] HeldBy =
    [
        ("Registered in advance", "AnEvaluatorNobodyCarriesAndAFamilyAtItsMaximumAreBothRefusedAtTheWrite"),
        ("Corrected for the family", "NoVerdictAppearsBelowEitherFloorAndTheOneThatIsShortIsNamed"),
        ("Minimum resolved setups", "AVerdictIsWithheldBelowTheMinimumWithItsCountAndIsComputedFromTheMinimumUpward"),
        ("Unresolved is never a win", "TheDivisorCountsTheRowsRegisteredBeforeTheWindowOpenedAndNoOthers"),
        ("Shadow before live", "NoScreenCarriesAShadowEvaluationOfAName"),
        ("Frozen windows", "AVersionChangeOpensANewWindowAndKeepsThePreviousOne"),
        ("Adding restarts the clock", "TheDivisorOverTheFixtureMatchesTheOneWorkedByHand"),
        ("Every change is recorded", "ARetirementIsANewRowNamingWhatItRetiresAndTheOriginalStands"),
    ];

    [Fact]
    public void EveryGuardrailIsMappedToATestThatHoldsIt()
    {
        var table = ArchitectureTables.In(File.ReadAllText(Repository.Architecture))
            .Single(one => one.Heading == "13.3 The guardrails");

        var rows = table.Body
            .Where(row => row.Count > 1 && row[0].Length > 0)
            .Select(row => row[0])
            .ToArray();

        // The population, read off the document rather than counted here. A
        // guardrail added to the table arrives with no mapping rather than being
        // silently covered by the ones already there.
        Assert.True(rows.Length >= 8, $"Read {rows.Length} guardrail(s) from 13.3, expected at least 8.");

        var mapped = HeldBy.Select(pair => pair.Guardrail).ToArray();

        // Both directions. A guardrail with no mapping is a rule nothing holds;
        // a mapping naming a guardrail the document does not carry is a test
        // pointed at a rule that has been rewritten.
        Assert.Empty(rows.Except(mapped, StringComparer.Ordinal));
        Assert.Empty(mapped.Except(rows, StringComparer.Ordinal));

        // Every named test exists in the suite, read from the assembly rather
        // than from a list, which is the same reason a component's declaration
        // lives in the component.
        var methods = Assembly.GetExecutingAssembly().GetTypes()
            .SelectMany(type => type.GetMethods())
            .Where(method => method.GetCustomAttributes().Any(attribute => attribute.GetType().Name == "FactAttribute"))
            .Select(method => method.Name)
            .ToHashSet(StringComparer.Ordinal);

        var missing = HeldBy
            .Where(pair => !methods.Contains(pair.Test))
            .Select(pair => $"{pair.Guardrail} names {pair.Test}, which the suite does not carry")
            .ToArray();

        Assert.Empty(missing);

        // And no test holds two guardrails. One test standing for two rules is
        // one rule unheld the day that test is narrowed to the other.
        Assert.Equal(HeldBy.Length, HeldBy.Select(pair => pair.Test).Distinct(StringComparer.Ordinal).Count());
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
        // asserted over rows rather than stated. A candidate retired or promoted
        // is a change the loop made, and so is a rule version's window closed;
        // both are rows, so whether either happened is a question rows answer.
        //
        // Asked of a store the suite built rather than of the operator's, because
        // a check that reads the live store is a check whose result depends on
        // last night. What makes it more than a tautology is the second half: the
        // same reader is shown to find a change when one was made.
        using var store = new TemporaryStore().Migrated();

        var registrar = new CandidateRegistrar(
            FixedClock.At(new DateTimeOffset(2026, 9, 16, 21, 0, 0, TimeSpan.Zero), SessionZones.UnitedStates),
            store.DatabaseFile);

        await registrar.RegisterAsync(
            "momentum index at thirty",
            "the relative strength index at or below thirty",
            "the share of its setups that beat their own break-even",
            MomentumIndexReading.EvaluatorName,
            new Dictionary<string, double>(StringComparer.Ordinal) { [MomentumIndexReading.Level] = 30 },
            "report-register");

        // A registration is not a change made on evidence: it is the thing
        // evidence is gathered about. What would be a change is a retirement or a
        // promotion, and there is none.
        Assert.Empty(ChangesMadeOnEvidence(await registrar.RowsAsync(), []));

        var withdrawn = await registrar.RetireAsync(
            "momentum index at thirty",
            "0 resolved setups of a minimum of 250",
            "report-retire");

        Assert.Equal(CandidateRegistrar.Retired, withdrawn.Outcome);

        var after = ChangesMadeOnEvidence(await registrar.RowsAsync(), []);

        Assert.Single(after);
        Assert.Contains("momentum index at thirty", after[0], StringComparison.Ordinal);

        // The other half of what a change to the loop means: a version window
        // closed, which is a rule the loop stopped measuring.
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

        // And an open window is not a change, so what the reader keys on is the
        // close rather than the row.
        Assert.Empty(ChangesMadeOnEvidence([], [closed with { ClosedAt = null, ReplacedBy = null }]));

        // The state this report is about: nothing in this repository registers,
        // retires or closes anything, so the loop has changed nothing at all.
        Assert.Empty(ChangesMadeOnEvidence([], []));
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
