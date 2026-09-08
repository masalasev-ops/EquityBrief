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

    // The scale the adjustment is carried to. Four places, which is what the
    // provider carries on an adjusted close, so a scaled open is stated no more
    // precisely than the number the factor came from.
    const int Places = 4;

    static ProviderBar Read(JsonElement entry, string ticker)
    {
        // Both closes. The adjusted one is what the store holds
        // (see: The stored series is adjusted); the raw one is the input to the
        // factor and is stored beside it.
        var raw = Price(entry, "close", ticker);
        var adjusted = Price(entry, "adjusted_close", ticker);

        if (raw <= 0)
        {
            throw new FormatException(
                $"{ticker} carries a close of {raw}, and the adjustment factor divides by it. A " +
                "session with no positive close is not a session.");
        }

        // One price set per bar. The provider adjusts the close alone, so the
        // other three are scaled by the same factor. Passing them through
        // unadjusted stores three raw prices beside one adjusted one, which is
        // a bar that could not have traded: 1.2 stored 96 of 756 fixture bars
        // whose close fell outside their own low and high.
        var bar = new ProviderBar(
            Date(entry, "date", ticker),
            Adjust(Price(entry, "open", ticker), adjusted, raw),
            Adjust(Price(entry, "high", ticker), adjusted, raw),
            Adjust(Price(entry, "low", ticker), adjusted, raw),
            adjusted,
            raw,
            Volume(entry, ticker));

        // The guard the code carries, beside the check that reads the store. A
        // bar that could not have traded refuses here rather than being drawn.
        if (bar.Low > bar.Open || bar.Low > bar.Close || bar.High < bar.Open || bar.High < bar.Close)
        {
            throw new FormatException(
                $"{ticker} on {bar.SessionDate:yyyy-MM-dd} adjusts to low {bar.Low}, open {bar.Open}, " +
                $"high {bar.High}, close {bar.Close}, which is a session that could not have traded.");
        }

        return bar;
    }

    // value * adjusted / raw, in that order, so the division happens once at the
    // end rather than on a factor rounded before it is used. Decimal throughout:
    // a price never passes through double, and a ratio of two prices is not a
    // statistic, it is the same price expressed after a corporate action.
    static decimal Adjust(decimal value, decimal adjusted, decimal raw) =>
        adjusted == raw ? value : Trim(Math.Round(value * adjusted / raw, Places, MidpointRounding.ToEven));

    // Trailing zeros removed, because decimal carries its scale and the storage
    // form is text: rounding 165.28 to four places would otherwise be written
    // "165.2800" and read back as a different string for the same number.
    static decimal Trim(decimal value) =>
        decimal.Parse(value.ToString("0.####", CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);

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
