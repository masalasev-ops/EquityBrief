using System.Globalization;
using System.Text.Json;

namespace EquityBrief.Core.Providers;

// The historical bar feed, answered from recorded responses rather than from the
// network. One captured file per ticker, named for it.
//
// It ships for the same reason the membership double does: the fixture replay at
// 2.1 runs the pipeline rather than the tests, and a double the shipped code
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

        return document.RootElement.EnumerateArray().Select(entry => Read(entry, ticker)).ToArray();
    }

    static ProviderBar Read(JsonElement entry, string ticker) => new(
        Date(entry, "date", ticker),
        Price(entry, "open", ticker),
        Price(entry, "high", ticker),
        Price(entry, "low", ticker),

        // The adjusted close, which is what the store holds
        // (see: The stored series is adjusted). The provider sends both, and
        // taking the wrong one would leave the series drifting from every chart
        // the operator compares it against, with nothing failing.
        Price(entry, "adjusted_close", ticker),
        Volume(entry, ticker));

    static DateOnly Date(JsonElement entry, string name, string ticker)
    {
        var text = Text(entry, name, ticker);

        // Exact and invariant, so the parse does not depend on the machine's
        // locale. A day-first string would otherwise be a different date here
        // and a refusal on the runner.
        return DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
            ? parsed
            : throw new FormatException($"{ticker} carries a {name} of '{text}', which is not a date in yyyy-MM-dd.");
    }

    // Read as decimal from the text of the number, never through double.
    // GetDecimal on a JSON number does not go through double, and the money rule
    // is that a price is decimal in code from the moment it is read.
    static decimal Price(JsonElement entry, string name, string ticker)
    {
        if (!entry.TryGetProperty(name, out var value) || value.ValueKind is JsonValueKind.Null)
        {
            throw new FormatException($"{ticker} carries no {name}. A session without a price is not a session.");
        }

        return value.ValueKind switch
        {
            JsonValueKind.Number => value.GetDecimal(),
            JsonValueKind.String when decimal.TryParse(
                value.GetString(), NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture, out var parsed) => parsed,
            _ => throw new FormatException(
                $"{ticker} carries a {name} that is not a number this store will accept. Prices are " +
                "decimal in code, and a value that will not parse as one is not a price."),
        };
    }

    static long Volume(JsonElement entry, string ticker) =>
        entry.TryGetProperty("volume", out var value) && value.ValueKind is JsonValueKind.Number
            ? value.GetInt64()
            : throw new FormatException($"{ticker} carries no volume, or one that is not a whole number.");

    static string Text(JsonElement entry, string name, string ticker) =>
        entry.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.String
            ? value.GetString()!
            : throw new FormatException($"{ticker} carries no {name}, or one that is not a string.");
}
