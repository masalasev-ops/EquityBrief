using EquityBrief.Core.Providers;

namespace EquityBrief.Tests.Harness;

// The two counts a fixture carries, which are not the same number.
//
// Constituents are the rows the membership payload holds, current and departed.
// Names are the tickers with a captured price series. A departed name is owed no
// history, so it has a constituent row and no bars, which is why the second
// count is the smaller one and why a document saying a fixture widens to four
// names is not satisfied by a fifth constituent.
//
// Both are derived from the folder rather than stated anywhere. The distinction
// was written in fixtures/README.md first, and a distinction that lives only in
// prose is one a later session reads past, which this one is positioned for:
// phase 3's sentence says four and this fixture holds five.
internal sealed record FixturePopulations(
    string Fixture,
    int Constituents,
    int Names,
    IReadOnlyList<string> Departed,
    IReadOnlyList<string> WithoutSeries)
{
    internal bool Distinguishable => Constituents != Names;
}

internal sealed record FixtureStatus(
    int Folders,
    string State,
    string Note,
    IReadOnlyList<FixturePopulations> Populations)
{
    internal int Constituents => Populations.Sum(population => population.Constituents);

    internal int Names => Populations.Sum(population => population.Names);
}

// What the harness can say about the committed fixture.
//
// A fixture that is not there is reported as absent. It is never reported as
// passing and never left out, because a stage nobody mentions reads as a stage
// that went fine.
internal static class Fixtures
{
    const string Constituents = "index-constituents.json";
    const string SeriesPrefix = "bars-";

    internal static FixtureStatus Of(string root)
    {
        var folder = Path.Combine(root, "fixtures");

        var folders = Directory.Exists(folder)
            ? Directory.GetDirectories(folder).OrderBy(path => path, StringComparer.Ordinal).ToArray()
            : [];

        return folders.Length == 0
            ? new FixtureStatus(
                0,
                "ABSENT",
                "no fixture is captured. 0.6 builds the folder and its manifest schema and " +
                "nothing else; the first captured inputs and the first manifest arrive with the " +
                "gap fixture at 1.3, and phase 1's expectations at 1.8. Out of scope until then, " +
                "and never a pass.",
                [])
            : new FixtureStatus(
                folders.Length,
                "PRESENT",
                "each folder's manifest is checked against fixtures/manifest.schema.json. " +
                "Constituents and names are counted separately: a name is a ticker with a " +
                "captured price series, a constituent is a membership row, and a departed " +
                "constituent is owed no series.",
                [.. folders.Select(Populations)]);
    }

    // One folder's two populations, read off the folder rather than off a
    // manifest field, so a file added without a manifest entry still counts and
    // the manifest check is the one that objects to it.
    internal static FixturePopulations Populations(string folder)
    {
        var names = Directory
            .GetFiles(folder, SeriesPrefix + "*.json")
            .Select(path => Path.GetFileNameWithoutExtension(path)[SeriesPrefix.Length..])
            .OrderBy(ticker => ticker, StringComparer.Ordinal)
            .ToArray();

        var captured = Path.Combine(folder, Constituents);

        if (!File.Exists(captured))
        {
            // A fixture need not carry a membership payload. The gap fixture at
            // 1.5 is a constructed series and nothing else, so its constituent
            // count is zero rather than absent, and its names still count.
            return new FixturePopulations(Path.GetFileName(folder), 0, names.Length, [], []);
        }

        // Through the shipped parser rather than by reading the JSON here, so
        // the count is the one the pipeline sees. A parser that stopped reading
        // a constituent would change this figure rather than leave it agreeing
        // with a second reading of the same file.
        var constituents = RecordedIndexMembershipFeed.Parse(File.ReadAllText(captured), "GSPC");

        return new FixturePopulations(
            Path.GetFileName(folder),
            constituents.Count,
            names.Length,
            [.. constituents.Where(one => one.Left is not null).Select(one => one.Ticker).OrderBy(ticker => ticker, StringComparer.Ordinal)],
            [.. constituents.Select(one => one.Ticker).Except(names, StringComparer.Ordinal).OrderBy(ticker => ticker, StringComparer.Ordinal)]);
    }
}
