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
