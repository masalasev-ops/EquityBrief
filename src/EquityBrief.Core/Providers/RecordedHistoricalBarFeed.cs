using System.Globalization;
using System.Text.Json;

namespace EquityBrief.Core.Providers;

// The historical bar feed, answered from recorded responses rather than from the
// network. One captured file per ticker, named for it.
//
// It ships for the same reason the membership double does: the fixture replay at
// 3.1 runs the pipeline rather than the tests, and a double the shipped code
// cannot be pointed at is one the pipeline cannot replay through. It holds no
// HTTP client, so a test using it cannot fall back to the live provider.
public sealed class RecordedHistoricalBarFeed(IReadOnlyDictionary<string, string> responses) : IHistoricalBarFeed
{
    public int Requests { get; private set; }

    public static RecordedHistoricalBarFeed FromFolder(string folder) =>
        new(Directory
            .GetFiles(folder, "bars-*.json")
            .ToDictionary(
                path => Path.GetFileNameWithoutExtension(path)["bars-".Length..],
                File.ReadAllText,
                StringComparer.OrdinalIgnoreCase));

    public Task<IReadOnlyList<ProviderBar>> BarsAsync(
        string ticker,
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken = default)
    {
        // Counted per name, because the backfill's whole argument is that it
        // costs one request per name and the run log records the figure.
        Requests++;

        if (!responses.TryGetValue(ticker, out var captured))
        {
            throw new InvalidOperationException(
                $"No captured response for {ticker}. A recorded feed that answered an unknown " +
                "ticker with an empty series would look exactly like a name the provider has no " +
                "history for, and the backfill would record it as done.");
        }

        var bars = Parse(captured, ticker)
            .Where(bar => bar.SessionDate >= from && bar.SessionDate <= to)
            .OrderBy(bar => bar.SessionDate)
            .ToArray();

        return Task.FromResult<IReadOnlyList<ProviderBar>>(bars);
    }

    public static IReadOnlyList<ProviderBar> Parse(string json, string ticker)
    {
        using var document = JsonDocument.Parse(json);

        if (document.RootElement.ValueKind is not JsonValueKind.Array)
        {
            throw new FormatException(
                $"The captured response for {ticker} is not an array of sessions. A payload that " +
                "cannot be parsed must fail rather than answer with no bars, which the backfill " +
                "would store as a name with no history.");
        }

        return document.RootElement.EnumerateArray().Select(entry => ProviderBarReader.Read(entry, ticker)).ToArray();
    }
}
