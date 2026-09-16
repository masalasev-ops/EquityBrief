using System.Reflection;
using System.Text.RegularExpressions;
using EquityBrief.Tests.Harness;

namespace EquityBrief.Tests.Checks;

internal sealed record RosterRow(string Check, string Runs, string Asserts);

// coverage-reported. Every check the roster says runs is implemented and is
// invoked by tools/ci.*, and every checkpoint row names a checkpoint that has
// not landed.
//
// This is the one that matters most and is easiest to lose. Under-reporting is
// survivorship: a check that errors loudly gets fixed because it blocks, while a
// check that silently narrows its own scope keeps passing.
public class CoverageReported
{
    // The carrier of a roster row nothing implements yet. Named rather than
    // spelled out at each site, because the report, the command and the suite
    // outcomes all test against it and three literals is three places to drift.
    internal const string NotDueYet = "not due yet";

    // Which class carries which check. Two checks may share a class where they
    // read the same thing, and one class may carry no check at all, but a roster
    // row with nothing behind it is the failure this map exists to expose.
    static readonly Dictionary<string, string> Implementations = new(StringComparer.Ordinal)
    {
        ["writer-ownership"] = "StoreWrites",
        ["component-access"] = "ComponentAccess",
        ["architecture-conformance"] = "ArchitectureConformance",
        ["decision-resolves"] = "DecisionCitations",
        ["no-superseded-citation"] = "DecisionCitations",
        ["obligation-reconciles"] = "ObligationReconciles",
        ["done-condition-producible"] = "DoneConditionProducible",
        ["changelog-reconciles"] = "ChangelogReconciles",
        ["record-append-only"] = "RecordAppendOnly",
        ["pinned-constants"] = "PinnedConstants",
        ["stated-counts"] = "StatedCounts",
        ["banned-prose"] = "BannedProse",
        ["coverage-reported"] = "CoverageReported",
        ["clock-usage"] = "ClockUsage",
        ["path-casing"] = "PathCasing",
        ["store-portability"] = "StorePortability",
        ["price-storage-form"] = "PriceStorageForm",
        ["schema-columns"] = "SchemaColumns",
        ["build-properties-central"] = "BuildPropertiesCentral",
        ["api-isolation"] = "ApiIsolation",
        ["bar-append-only"] = "StoreWrites",
        ["bar-bounds"] = "BarBounds",
        ["read-surface"] = "ReadSurface",
        ["nightly-cost"] = "NightlyCost",
        ["nightly-run"] = "NightlyRun",
        ["gap-refusal"] = "GapRefusal",
        ["corporate-actions"] = "CorporateActions",
        ["news-parse"] = "NewsCoverage",
        ["fixture-expectations"] = "FixtureExpectations",
        ["fixture-replay"] = "FixtureReplay",
        ["listings-coverage"] = "ListingsCoverage",
        ["claim-admissibility"] = "ClaimAdmissibility",
        ["ci-parity"] = "CiParity",
        ["two-platform"] = "TwoPlatform",
        ["register-append-only"] = "RegisterAppendOnly",
        ["rule-versions-scored"] = "RuleVersionsScored",
    };

    internal static IReadOnlyList<RosterRow> Roster()
    {
        // The roster lives in the rules file that loads for a session working in
        // `tools/` or in the test project, which is everyone who reads it. This
        // is the only place in the repository that locates the table; everything
        // else reaches it through `Roster()` or `Coverage()`.
        var rules = Corpus.Read(".claude/rules/checks.md");

        return Regex.Matches(rules, @"^\| `([a-z-]+)` \| ([^|]+) \| ([^|]+) \|$", RegexOptions.Multiline)
            .Select(match => new RosterRow(
                match.Groups[1].Value,
                match.Groups[2].Value.Trim(),
                match.Groups[3].Value.Trim()))
            .ToArray();
    }

    static IReadOnlyList<string> TestClasses() =>
        Assembly.GetExecutingAssembly().GetTypes()
            .Where(type => type.IsPublic && type.GetMethods()
                .Any(method => method.GetCustomAttributes()
                    .Any(attribute => attribute.GetType().Name is "FactAttribute" or "TheoryAttribute")))
            .Select(type => type.Name)
            .ToArray();

    // The coverage record the phase report carries, so a check the roster
    // promises and nothing implements is visible on the page a person reads
    // rather than only inside a run that passed.
    internal static IReadOnlyList<Harness.CheckCoverage> Coverage() =>
        Roster()
            .Select(row => new Harness.CheckCoverage(
                row.Check,
                row.Runs,
                CarrierFor(row.Check),
                Harness.CheckReaches.Of(row.Check) is { } reach
                    ? string.Join(", ", reach.Reads)
                    : "no reach declared"))
            .ToArray();

    [Fact]
    public void EveryRosterRowIsAccountedFor()
    {
        var roster = Roster();

        Assert.True(roster.Count >= 19, $"Read {roster.Count} roster rows, expected at least 19.");

        // The column's two forms and no third. A row in any other form is read
        // by neither half below and by nothing else, which is how "the matrix"
        // would sit unread once the hosted Windows leg that gave it meaning was
        // removed.
        Assert.DoesNotContain(roster, row => row.Runs is not "every CI run" && !row.Runs.StartsWith("from ", StringComparison.Ordinal));

        var everyRun = roster
            .Where(row => row.Runs is "every CI run")
            .Select(row => row.Check)
            .ToArray();

        // Both directions. A roster row with no implementation is a promise
        // nothing keeps; an implementation named here that the roster does not
        // carry is a property nobody wrote down.
        Assert.DoesNotContain(everyRun, check => !Implementations.ContainsKey(check));
        Assert.DoesNotContain(Implementations.Keys, check => !roster.Any(row => row.Check == check));
    }

    [Fact]
    public void EveryImplementedCheckIsAClassThatRuns()
    {
        var classes = TestClasses();

        Assert.True(classes.Count >= 15, $"Found {classes.Count} test classes, expected at least 15.");

        var missing = Implementations
            .Where(pair => !classes.Contains(pair.Value, StringComparer.Ordinal))
            .Select(pair => $"{pair.Key} -> {pair.Value}")
            .ToArray();

        Assert.Empty(missing);
    }

    [Fact]
    public void ThePhaseReportCarriesACoverageRecordForEveryRosterRow()
    {
        // Green means nothing I ran failed, never nothing is wrong. The record
        // is what lets a person read which checks were behind a green run,
        // rather than taking the run's word for its own scope.
        var coverage = Coverage();

        Assert.Equal(Roster().Count, coverage.Count);
        Assert.DoesNotContain(coverage, entry => entry.Carrier.Length == 0);

        // Every row carries a real carrier from 8.3, which is what promoting the
        // last checkpoint row means. It was asserted the other way until then: a
        // row reading "not due yet" had to be present, so that the marker could
        // not quietly stop being written while rows still needed it.
        Assert.DoesNotContain(coverage, entry => entry.Carrier == NotDueYet);

        // The marker is still what an unimplemented row gets, put to the same
        // reader the record above is built by rather than asserted of the corpus,
        // because the corpus no longer holds such a row and a value nothing
        // produces is one that can be renamed with nothing noticing.
        Assert.Equal(NotDueYet, CarrierFor("a-check-nobody-implements"));
        Assert.Equal("CoverageReported", CarrierFor("coverage-reported"));
    }

    // Which class a roster row's check is carried by, or the marker. Named apart
    // from the record so the proof above runs this reader rather than a copy.
    internal static string CarrierFor(string check) =>
        Implementations.TryGetValue(check, out var carrier) ? carrier : NotDueYet;

    [Fact]
    public void TheCiScriptsInvokeTheSuite()
    {
        // A check that is implemented and never run is a check that stopped
        // running, which is the sharpest form of under-reporting.
        foreach (var script in new[] { "ci.sh", "ci.ps1" })
        {
            var text = File.ReadAllText(Repository.Tool(script));

            Assert.Contains("dotnet test", text, StringComparison.Ordinal);
            Assert.Contains("\"suite\"", text, StringComparison.Ordinal);
        }
    }

    // What a rostered "from" row is faulted for, named apart from the fact so the
    // proof below runs the same reader the corpus is measured by. Two faults: a
    // checkpoint the plan does not have, and one the record shows as landed, which
    // is a check rostered to start at a point the build has already passed.
    internal static IReadOnlyList<string> PendingRowFaults(IEnumerable<RosterRow> pending, string plan, string progress)
    {
        var faults = new List<string>();

        foreach (var row in pending)
        {
            var checkpoint = row.Runs["from ".Length..].Trim();

            if (!DuePoints.InThePlan(checkpoint, plan))
            {
                faults.Add($"{row.Check} runs from {checkpoint}, which the plan has neither as a checkpoint nor as a phase.");
            }

            if (DuePoints.HasLanded(checkpoint, progress))
            {
                faults.Add($"{row.Check} is rostered from {checkpoint}, which PROGRESS records as built.");
            }
        }

        return faults;
    }

    [Fact]
    public void APendingRowNamingAPointThePlanLacksOrTheRecordHasPassedIsFound()
    {
        // The permanent proof, over a constructed roster, plan and record. The
        // corpus cannot exercise either fault while the one row it holds names a
        // checkpoint of a phase nothing has built, which the phase 8 planning
        // sweep showed: moving that row back to the checkpoint the phase 7
        // sign-off found it at left the suite green, because neither 8.1 nor 8.3
        // has landed and a row naming either passes. So the branch is exercised
        // here instead of being left to fire for the first time on the night 8.1's
        // entry is written.
        const string Plan = "### 8.3 The candidate register\nMigration creating the register.\n\n";
        const string Record = "### 8.3 - the candidate register   2026-10-01\nBuilt:      the register.\n\n";

        RosterRow Row(string from) => new("register-append-only", "from " + from, "the register refuses updates");

        // A checkpoint the plan has and the record does not: no fault.
        Assert.Empty(PendingRowFaults([Row("8.3")], Plan, string.Empty));

        // The same checkpoint once the record shows it built.
        Assert.Equal(
            ["register-append-only is rostered from 8.3, which PROGRESS records as built."],
            PendingRowFaults([Row("8.3")], Plan, Record));

        // And a checkpoint the plan does not have at all.
        Assert.Equal(
            ["register-append-only runs from 9.9, which the plan has neither as a checkpoint nor as a phase."],
            PendingRowFaults([Row("9.9")], Plan, string.Empty));
    }

    [Fact]
    public void EveryCheckpointRowNamesOneThatHasNotLanded()
    {
        var plan = Corpus.Read("docs/BUILD_PLAN.md");
        var progress = Corpus.Read("docs/PROGRESS.md");

        var pending = Roster().Where(row => row.Runs.StartsWith("from ", StringComparison.Ordinal)).ToArray();

        // The population is context and carries no floor, from 8.3.
        //
        // It was floored while any row remained, and the floor fell as checks
        // were promoted: 5 until 1.4 promoted nightly-cost, three until 5.4
        // promoted listings-coverage, two until 6.3 promoted claim-admissibility,
        // one until 8.3 promoted the register. There is nothing left to floor,
        // and a floor of one over an empty set is a floor that fails for the
        // opposite of the reason it was written: not because a row names a
        // landed checkpoint, but because the roster ran out of rows, which is the
        // build working.
        //
        // What that costs is stated rather than absorbed: this half now runs over
        // a set that may be empty, which is what the rules file forbids of a
        // scope carrying a property. So the property is carried by the
        // constructed proof above instead, which exercises both faults over a
        // roster, a plan and a record written here. A run that reads no pending
        // row is reporting that the roster has none, and the proof is what says
        // the reader could still find one. No assertion is written on the count
        // itself, because an assertion that cannot fail is the under-reporting
        // this check exists to refuse; the number is carried in the message
        // below, which is where the scope is stated.

        // The checkpoint itself exists only once its phase is planned, which
        // BUILD_PLAN does at the previous phase's sign-off. What has to be true
        // now is that its phase is in the plan and that nothing has recorded the
        // checkpoint as landed.
        //
        // Both questions go through the same reader the reconciliation uses,
        // rather than being asked again here with a text match. Asked again, they
        // were the same prefix defect: "### 1.4 -" in the record reads as landed
        // whatever the entry beneath it says, so a planning pass headed with a
        // building checkpoint would have retired a roster row that has not
        // started running. They are asked through `PendingRowFaults` so the proof
        // above runs this reader rather than a copy of it.
        var faults = PendingRowFaults(pending, plan, progress);

        Assert.True(
            faults.Count == 0,
            $"{faults.Count} of {pending.Length} checkpoint row(s) name a point the plan lacks or the " +
            $"record has passed: {string.Join("; ", faults)}");

        foreach (var row in pending)
        {
            Assert.False(
                Implementations.ContainsKey(row.Check),
                $"{row.Check} is not due until {row.Runs["from ".Length..].Trim()}.");
        }
    }
}
