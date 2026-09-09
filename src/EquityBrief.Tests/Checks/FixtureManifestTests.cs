using EquityBrief.Core.Providers;
using EquityBrief.Tests.Harness;

namespace EquityBrief.Tests.Checks;

// The fixture manifest's shape, asserted against fixtures/manifest.schema.json.
//
// The manifest names the endpoint, the query and the UTC instant of every
// captured input, and asserts that no credential appears in any of them. A
// fixture is committed, so a credential in one is published.
public class FixtureManifestTests
{
    // Written with single quotes and swapped, so the JSON in these tests reads
    // as JSON rather than as a wall of escapes.
    static string Json(string text) => text.Replace('\'', '"');

    const string Valid = @"{
        'fixture': 'AAPL-2026-09-04',
        'capturedFor': '2026-09-04',
        'noCredentials': true,
        'inputs': [
            {
                'file': 'bars.csv',
                'endpoint': 'eod/bulk',
                'query': 'symbols=AAPL&period=d',
                'fetchedAt': '2026-09-05T01:15:00Z'
            }
        ]
    }";

    static string Schema => File.ReadAllText(Path.Combine(Repository.Root, "fixtures", "manifest.schema.json"));

    static IReadOnlyList<ManifestFault> Faults(string manifest) =>
        FixtureManifest.Faults(Json(manifest), Schema);

    [Fact]
    public void TheRequiredFieldsComeFromTheSchemaAndNotFromTheCode()
    {
        var manifest = FixtureManifest.RequiredFields(Schema, "manifest");
        var input = FixtureManifest.RequiredFields(Schema, "input");

        Assert.Equal(["fixture", "capturedFor", "noCredentials", "inputs"], manifest);
        Assert.Equal(["file", "endpoint", "query", "fetchedAt"], input);
    }

    [Fact]
    public void AValidManifestHasNoFaults()
    {
        // Without this, every rejection below would pass just as well over a
        // checker that rejects everything.
        Assert.Empty(Faults(Valid));
    }

    [Fact]
    public void AMissingRequiredFieldIsAFault()
    {
        var faults = Faults(Valid.Replace("'capturedFor': '2026-09-04',", string.Empty));

        Assert.Contains(faults, fault => fault.Field == "capturedFor" && fault.Reason.Contains("absent"));
    }

    [Fact]
    public void ADateThatIsNotADateIsAFault()
    {
        Assert.Contains(
            Faults(Valid.Replace("2026-09-04'", "the fourth of September'")),
            fault => fault.Field == "capturedFor");
    }

    [Fact]
    public void AnInstantThatIsNotUtcIsAFault()
    {
        // Every instant is UTC. A local one read on the other machine is a
        // different moment, and nothing in the file would say so.
        Assert.Contains(
            Faults(Valid.Replace("2026-09-05T01:15:00Z", "2026-09-04T21:15:00-04:00")),
            fault => fault.Field == "inputs[0].fetchedAt");
    }

    [Fact]
    public void ACredentialInAQueryIsAFault()
    {
        Assert.Contains(
            Faults(Valid.Replace("symbols=AAPL&period=d", "symbols=AAPL&api_token=abc123")),
            fault => fault.Field == "inputs[0].query" && fault.Reason.Contains("api_token"));
    }

    [Fact]
    public void AnAbsoluteFilePathIsAFault()
    {
        Assert.Contains(
            Faults(Valid.Replace("'file': 'bars.csv'", "'file': '/Users/someone/bars.csv'")),
            fault => fault.Field == "inputs[0].file");
    }

    [Fact]
    public void AManifestAssertingItCarriesCredentialsIsAFault()
    {
        Assert.Contains(
            Faults(Valid.Replace("'noCredentials': true", "'noCredentials': false")),
            fault => fault.Field == "noCredentials");
    }

    [Fact]
    public void AFixtureWithNoInputsIsAFault()
    {
        var faults = Faults(@"{
            'fixture': 'empty-2026-09-04',
            'capturedFor': '2026-09-04',
            'noCredentials': true,
            'inputs': []
        }");

        Assert.Contains(faults, fault => fault.Field == "inputs");
    }

    [Fact]
    public void AFileThatIsNotJsonIsAFaultRatherThanAnException()
    {
        var faults = FixtureManifest.Faults("this is not json", Schema);

        Assert.Single(faults);
        Assert.Contains("not valid JSON", faults[0].Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void TheHarnessReportsTheFixtureThatIsThereAndAbsenceIsStillNeverAPass()
    {
        // Inverted at 1.1, which is where the first input was captured. It read
        // ABSENT from 0.6 until then, and 0.6's done condition was that the
        // harness says absent rather than passing.
        var status = Fixtures.Of(Repository.Root);

        Assert.Equal("PRESENT", status.State);
        Assert.True(status.Folders >= 1, $"The harness reports {status.Folders} captured fixtures, expected at least 1.");

        // The other half is kept rather than deleted. Absence must still report
        // as absence, and over a root with no fixtures that is what it does, so
        // the day a folder is emptied it says so instead of saying nothing.
        using var empty = new TemporaryDirectory();
        var absent = Fixtures.Of(empty.Path);

        Assert.Equal("ABSENT", absent.State);
        Assert.Equal(0, absent.Folders);
        Assert.Contains("never a pass", absent.Note, StringComparison.Ordinal);
    }

    [Fact]
    public void TheFolderAndTheExpectationAgreeAboutThePopulations()
    {
        // owes: The suite's fixture populations read from the expectation rather than written into each test
        //
        // The suite now reads its populations from the membership expectation
        // rather than restating them. Two derivations remain and that is
        // deliberate: `Fixtures.Populations` counts what is on disk and the
        // expectation states what the rules produce over it. They are
        // reconciled here, once, so a disagreement is a finding in one place
        // rather than a literal that quietly stopped matching in twenty-five.
        //
        // 3.6 priced the alternative. Widening the fixture from three names to
        // four turned 28 tests red and the repair touched twenty-five assertion
        // sites across six test classes, one of which was a named constant
        // covering six of them.
        var folder = Fixtures.Of(Repository.Root).Populations.Single(one => one.Constituents > 0);

        Assert.Equal(FixtureExpectation.Constituents, folder.Constituents);
        Assert.Equal(FixtureExpectation.Names.Length, folder.Names);
        Assert.Equal([.. FixtureExpectation.Departed.Order(StringComparer.Ordinal)], [.. folder.Departed.Order(StringComparer.Ordinal)]);

        // The populations carry the property and are floored. A reader that
        // returned nothing would agree with an expectation that stated nothing.
        Assert.True(
            FixtureExpectation.Constituents >= 5,
            $"The membership expectation states {FixtureExpectation.Constituents} constituents, expected at least 5.");

        Assert.True(
            FixtureExpectation.Names.Length >= 4,
            $"The bars expectation names {FixtureExpectation.Names.Length} names, expected at least 4.");

        // Current members and names are the same set in this fixture and are
        // not the same idea. A departed name could carry a series, so the two
        // are asserted apart rather than one being read for the other.
        Assert.Equal(
            [.. FixtureExpectation.CurrentMembers.Order(StringComparer.Ordinal)],
            [.. FixtureExpectation.Names.Order(StringComparer.Ordinal)]);

        Assert.Equal(
            FixtureExpectation.Constituents,
            FixtureExpectation.CurrentMembers.Length + FixtureExpectation.Departed.Length);
    }

    [Fact]
    public void ConstituentsAndNamesAreCountedSeparatelyAndAreNotTheSamePopulation()
    {
        // The distinction, asserted rather than described.
        //
        // fixtures/README.md says a fixture counts two things and that phase 3's
        // "widens to four names" means the second. A distinction that lives only
        // in prose is one a later session reads past, and this one is positioned
        // to be misread: the phase 3 sentence says four and this fixture holds
        // five constituents, so a reader who has not been told they are
        // different populations sees an obligation already met.
        var status = Fixtures.Of(Repository.Root);
        var membership = status.Populations.Single(one => one.Constituents > 0);

        // Different populations, and different in the direction the rule
        // predicts. Not merely two numbers that happen to differ: names is a
        // proper subset of constituents, so the counts cannot be made equal by
        // adding a series for a name that is not in the index.
        Assert.True(
            membership.Distinguishable,
            $"{membership.Fixture} holds {membership.Constituents} constituents and " +
            $"{membership.Names} names. Equal counts make the two indistinguishable on the " +
            "phase report, which is where the difference has to be legible.");

        Assert.True(membership.Names < membership.Constituents);
        Assert.NotEmpty(membership.WithoutSeries);

        // And the rule that makes them differ, which is the one that matters:
        // a constituent with no captured series is a name the backfill would not
        // fetch, meaning one that has left. A current member with no series is a
        // fixture fault rather than a smaller population, because the backfill
        // refuses a current member it has no capture for, so this would fail as
        // a refusal at 1.2 rather than as a count here.
        Assert.Equal(membership.Departed, membership.WithoutSeries);

        // No orphan series either, in the other direction: a bars file naming a
        // ticker that is not a constituent is a name the pipeline never asks
        // for, and it would inflate the names count against nothing.
        var constituents = RecordedIndexMembershipFeed
            .Parse(File.ReadAllText(Path.Combine(
                Repository.Root, "fixtures", membership.Fixture, "index-constituents.json")), "GSPC")
            .Select(one => one.Ticker)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var file in Directory.GetFiles(
            Path.Combine(Repository.Root, "fixtures", membership.Fixture), "bars-*.json"))
        {
            var ticker = Path.GetFileNameWithoutExtension(file)["bars-".Length..];

            Assert.True(constituents.Contains(ticker), $"bars-{ticker}.json names no constituent.");
        }
    }

    [Fact]
    public void TheTwoCountsAreCountedFromTheFolderAndNotFromEachOther()
    {
        // The counting proved against planted folders rather than only against
        // the committed fixture, which has one shape and would let a counter
        // that returned the same number twice pass.
        using var root = new TemporaryDirectory();

        // Three constituents, one of them departed, and a series for two.
        var folder = Planted(root.Path, "three-2026-01-01", ["ONE", "TWO"], Departed: "OUT");
        var populations = Fixtures.Populations(folder);

        Assert.Equal(3, populations.Constituents);
        Assert.Equal(2, populations.Names);
        Assert.Equal(["OUT"], populations.Departed);
        Assert.Equal(["OUT"], populations.WithoutSeries);
        Assert.True(populations.Distinguishable);

        // The counter-test, which is what stops the assertion above from being
        // satisfied by a check that always reports a difference: give the
        // departed name a series too and the two counts agree.
        File.WriteAllText(Path.Combine(folder, "bars-OUT.json"), "[]");
        var equal = Fixtures.Populations(folder);

        Assert.Equal(3, equal.Constituents);
        Assert.Equal(3, equal.Names);
        Assert.False(equal.Distinguishable);
        Assert.Empty(equal.WithoutSeries);

        // And an orphan series counts as a name while naming no constituent,
        // which is the case the committed fixture cannot produce and the one
        // that would inflate the smaller population against nothing.
        File.WriteAllText(Path.Combine(folder, "bars-GHOST.json"), "[]");
        var orphaned = Fixtures.Populations(folder);

        Assert.Equal(3, orphaned.Constituents);
        Assert.Equal(4, orphaned.Names);
        Assert.Equal(["OUT"], orphaned.Departed);

        // A folder with no membership payload counts zero constituents and its
        // names still count, which is the gap fixture's shape at 1.5.
        var series = Path.Combine(root.Path, "fixtures", "gap-2026-01-01");
        Directory.CreateDirectory(series);
        File.WriteAllText(Path.Combine(series, "bars-ONE.json"), "[]");

        var only = Fixtures.Populations(series);

        Assert.Equal(0, only.Constituents);
        Assert.Equal(1, only.Names);
    }

    // A fixture folder with a membership payload in the provider's shape and a
    // series file per named ticker. Written outside the repository, so nothing
    // here can pass by reading the committed fixture.
    static string Planted(string root, string name, string[] withSeries, string Departed)
    {
        var folder = Path.Combine(root, "fixtures", name);
        Directory.CreateDirectory(folder);

        var rows = withSeries
            .Select((ticker, index) =>
                $$"""
                  "{{index}}": { "Code": "{{ticker}}", "StartDate": "2010-01-04", "EndDate": null }
                  """)
            .Append($$"""
                      "{{withSeries.Length}}": { "Code": "{{Departed}}", "StartDate": "2010-01-04", "EndDate": "2024-04-03" }
                      """);

        File.WriteAllText(
            Path.Combine(folder, "index-constituents.json"),
            $$"""{ "HistoricalTickerComponents": { {{string.Join(",", rows)}} } }""");

        foreach (var ticker in withSeries)
        {
            File.WriteAllText(Path.Combine(folder, $"bars-{ticker}.json"), "[]");
        }

        return folder;
    }

    [Fact]
    public void TheFolderDescriptionStatesTheTwoCountsAndBothAreDerived()
    {
        // fixtures/README.md states both numbers in the sentence that draws the
        // distinction. A number written into a document and checked by nothing
        // is the drift this corpus polices everywhere else, and writing the
        // sentence created a fresh instance of it.
        var readme = File.ReadAllText(Path.Combine(Repository.Root, "fixtures", "README.md"));
        var membership = Fixtures.Of(Repository.Root).Populations.Single(one => one.Constituents > 0);

        Assert.Contains(
            $"`{membership.Fixture}` holds {membership.Constituents} constituents and {membership.Names} names.",
            readme,
            StringComparison.Ordinal);
    }

    [Fact]
    public void EveryCapturedFixtureHasAManifestWithNoFaults()
    {
        // Written at 0.6 over zero fixtures so the first one would be checked on
        // the day it landed rather than on the day somebody remembered. It
        // landed at 1.1.
        var folder = Path.Combine(Repository.Root, "fixtures");
        var fixtures = Directory.GetDirectories(folder);

        // Stated, because a walk over zero folders asserts nothing and this test
        // was vacuous from 0.6 until the first fixture landed at 1.1.
        Assert.True(fixtures.Length >= 1, $"Walked {fixtures.Length} fixtures, expected at least 1.");

        foreach (var fixture in fixtures)
        {
            var manifest = Path.Combine(fixture, "manifest.json");

            Assert.True(File.Exists(manifest), $"{fixture} has no manifest.json.");

            // The folder is handed over so the checker opens each captured
            // response rather than only reading the query beside it.
            Assert.Empty(FixtureManifest.Faults(File.ReadAllText(manifest), Schema, fixture));
        }
    }
}
