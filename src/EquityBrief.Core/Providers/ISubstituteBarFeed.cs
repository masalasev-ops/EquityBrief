using System.Globalization;
using System.Text.Json;
using EquityBrief.Core.Prices;

namespace EquityBrief.Core.Providers;

// One session's bar as the second source states it, its prices read from the text of each number and carried to four
// places, the scale the bar store holds.
public sealed record DayBar(DateOnly Session, decimal Open, decimal High, decimal Low, decimal Close, long Volume);

// One session's high, low and close as the check states them.
public sealed record DayRange(DateOnly Session, decimal High, decimal Low, decimal Close);

// The two free sources asked for a member whose row the provider's day file refused: Yahoo's daily chart for the
// session's bar, and Nasdaq's quote for its high, low and close to check that bar against. Neither is the provider's,
// so neither counts against its allowance, and neither is asked on a night every member's row reads.
// see: A member's day bar the provider sends impossible is skipped and asked of Yahoo with Nasdaq's check once every file is read
public interface ISubstituteBarFeed
{
    // The session's bar, or none where the answer holds no bar dated the session.
    Task<DayBar?> BarAsync(string ticker, DateOnly session, CancellationToken cancellation = default);

    // The session's high, low and close, or none where the answer states none for that session.
    Task<DayRange?> RangeAsync(string ticker, DateOnly session, CancellationToken cancellation = default);

    int Requests { get; }
}

// Whether a second-source bar takes the refused row's place, and why not where it does not.
// see: A member's day bar the provider sends impossible is skipped and asked of Yahoo with Nasdaq's check once every file is read
public static class SubstituteBars
{
    // What the store's `source` says of a bar written in a refused row's place.
    public const string Source = "substitute";

    // The furthest a price may stand from the price it is checked against: half a cent, which takes in a price one
    // source states to the cent and another to the tenth of one.
    public const decimal Tolerance = 0.005m;

    // The most members a night asks the two sources for, the refused rows past it left as the provider sent them.
    public const int Most = 20;

    // The scale a second-source price is carried to, the bar store's.
    public const int Places = 4;

    // The second source's bar where every check holds, read at the provider's own adjustment where the refused row
    // stated both closes; otherwise none, and the first check that failed.
    public static (ProviderBar? Bar, string Why) Judge(UnreadableRow refused, DateOnly session, DayBar? bar, DayRange? range)
    {
        var day = session.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        if (bar is null)
        {
            return (null, $"Yahoo holds no bar for {day}");
        }

        if (bar.Session != session)
        {
            return (null, FormattableString.Invariant($"Yahoo's newest bar is for {bar.Session:yyyy-MM-dd} and not {day}"));
        }

        if (bar.Low <= 0 || bar.Low > bar.Open || bar.Low > bar.Close || bar.High < bar.Open || bar.High < bar.Close)
        {
            return (null, FormattableString.Invariant($"Yahoo's bar for {day}, low {bar.Low}, open {bar.Open}, high {bar.High}, close {bar.Close}, could not have traded either"));
        }

        if (Apart(bar.Open, refused.Open) is { } open)
        {
            return (null, FormattableString.Invariant($"Yahoo's open {bar.Open} stands {open} from the provider's {refused.Open}"));
        }

        if (Apart(bar.Close, refused.Close) is { } close)
        {
            return (null, FormattableString.Invariant($"Yahoo's close {bar.Close} stands {close} from the provider's {refused.Close}"));
        }

        if (range is null)
        {
            return (null, $"Nasdaq states no high and low for {day}");
        }

        if (Apart(bar.High, range.High) is not null || Apart(bar.Low, range.Low) is not null || Apart(bar.Close, range.Close) is not null)
        {
            return (null, FormattableString.Invariant(
                $"Yahoo's high {bar.High}, low {bar.Low} and close {bar.Close} are not Nasdaq's {range.High}, {range.Low} and {range.Close}"));
        }

        // The provider's own adjustment of the session, where its row stated both closes, so the substitute sits on the
        // scale the provider's file would have stored it at; a row stating neither is the session's own scale.
        var (adjusted, raw) = refused is { Close: { } stated, AdjustedClose: { } scaled } && stated > 0
            ? (scaled, stated)
            : (bar.Close, bar.Close);

        return (new ProviderBar(
            session,
            Adjust(bar.Open, adjusted, raw),
            Adjust(bar.High, adjusted, raw),
            Adjust(bar.Low, adjusted, raw),
            Adjust(bar.Close, adjusted, raw),
            bar.Close,
            bar.Volume), "stored from Yahoo, checked against Nasdaq");
    }

    // How far a price stands from the one it is checked against where that is past the tolerance, and none where it
    // is within it or there is nothing to check against.
    static decimal? Apart(decimal price, decimal? against) =>
        against is { } other && Math.Abs(price - other) > Tolerance ? Math.Abs(price - other) : null;

    static decimal Adjust(decimal value, decimal adjusted, decimal raw) =>
        adjusted == raw ? value : PriceForm.Round(value * adjusted / raw, Places);

    // Yahoo's daily chart as it sends it: one result, its sessions' instants under `timestamp`, and each price under
    // `indicators.quote[0]` in arrays the instants index, a session the chart holds no price for written null and
    // left out. An instant is the session's open, read as a date at the exchange's offset under `meta.gmtoffset`.
    public static IReadOnlyList<DayBar> YahooChart(string json)
    {
        using var document = JsonDocument.Parse(json);

        var chart = document.RootElement.GetProperty("chart");

        if (chart.TryGetProperty("error", out var error) && error.ValueKind is not JsonValueKind.Null)
        {
            throw new FormatException("Yahoo's chart answered an error: " + error.GetRawText());
        }

        var results = chart.GetProperty("result");

        if (results.ValueKind is not JsonValueKind.Array || results.GetArrayLength() == 0)
        {
            return [];
        }

        var result = results[0];

        if (!result.TryGetProperty("timestamp", out var instants) || instants.ValueKind is not JsonValueKind.Array)
        {
            return [];
        }

        var offset = result.GetProperty("meta").TryGetProperty("gmtoffset", out var gmt) && gmt.ValueKind is JsonValueKind.Number
            ? gmt.GetInt64()
            : 0;
        var quote = result.GetProperty("indicators").GetProperty("quote")[0];
        var bars = new List<DayBar>();

        for (var at = 0; at < instants.GetArrayLength(); at++)
        {
            var open = Price(quote, "open", at);
            var high = Price(quote, "high", at);
            var low = Price(quote, "low", at);
            var close = Price(quote, "close", at);
            var volume = quote.GetProperty("volume")[at];

            if (open is null || high is null || low is null || close is null || volume.ValueKind is not JsonValueKind.Number)
            {
                continue;
            }

            var session = DateOnly.FromDateTime(DateTimeOffset.FromUnixTimeSeconds(instants[at].GetInt64() + offset).UtcDateTime);

            bars.Add(new DayBar(session, open.Value, high.Value, low.Value, close.Value, volume.GetInt64()));
        }

        return bars;
    }

    // Read from the text of the number and carried to four places, never through double: the chart writes a price
    // as the nearest single-precision figure, 5.28000020980835 for 5.28.
    static decimal? Price(JsonElement quote, string name, int at)
    {
        var value = quote.GetProperty(name)[at];

        return value.ValueKind is JsonValueKind.Number ? PriceForm.Round(value.GetDecimal(), Places) : null;
    }

    // Nasdaq's historical table as it sends it: a row a session under `data.tradesTable.rows`, its date written month
    // first with slashes and each price a dollar figure in text. An answer holding no table holds no session.
    public static IReadOnlyList<DayRange> NasdaqHistory(string json)
    {
        using var document = JsonDocument.Parse(json);

        if (!document.RootElement.TryGetProperty("data", out var data)
            || data.ValueKind is not JsonValueKind.Object
            || !data.TryGetProperty("tradesTable", out var table)
            || table.ValueKind is not JsonValueKind.Object
            || !table.TryGetProperty("rows", out var rows)
            || rows.ValueKind is not JsonValueKind.Array)
        {
            return [];
        }

        return [.. rows.EnumerateArray().Select(row => new DayRange(
            DateOnly.ParseExact(row.GetProperty("date").GetString()!, "MM/dd/yyyy", CultureInfo.InvariantCulture),
            Dollars(row.GetProperty("high").GetString()),
            Dollars(row.GetProperty("low").GetString()),
            Dollars(row.GetProperty("close").GetString())))];
    }

    // Nasdaq's quote as it sends it outside the regular session: the session it last closed, written "Closed at Oct 12,
    // 2026 4:00 PM ET" under `data.secondaryData.lastTradeTimestamp`, that close under its `lastSalePrice`, and the
    // session's low and high written "4.99 - 5.28" under `data.keyStats.dayrange`. Inside the regular session the quote
    // states no closed session, and none is read.
    public static DayRange? NasdaqInfo(string json)
    {
        using var document = JsonDocument.Parse(json);

        if (!document.RootElement.TryGetProperty("data", out var data)
            || data.ValueKind is not JsonValueKind.Object
            || !data.TryGetProperty("secondaryData", out var closed)
            || closed.ValueKind is not JsonValueKind.Object
            || !data.TryGetProperty("keyStats", out var stats)
            || stats.ValueKind is not JsonValueKind.Object
            || !stats.TryGetProperty("dayrange", out var range)
            || range.ValueKind is not JsonValueKind.Object)
        {
            return null;
        }

        const string Closed = "Closed at ";

        var stamp = closed.GetProperty("lastTradeTimestamp").GetString() ?? string.Empty;

        if (!stamp.StartsWith(Closed, StringComparison.Ordinal))
        {
            return null;
        }

        // "Oct 12, 2026 4:00 PM ET": the date is the first three words.
        var words = stamp[Closed.Length..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var session = DateOnly.ParseExact(string.Join(' ', words.Take(3)), "MMM d, yyyy", CultureInfo.InvariantCulture);
        var span = range.GetProperty("value").GetString()!.Split(" - ", StringSplitOptions.TrimEntries);

        return new DayRange(session, Dollars(span[1]), Dollars(span[0]), Dollars(closed.GetProperty("lastSalePrice").GetString()));
    }

    // A dollar figure as Nasdaq writes one, "$5.465" or "5.28", read as a decimal from its text.
    static decimal Dollars(string? text) =>
        decimal.Parse(
            (text ?? string.Empty).Replace("$", string.Empty, StringComparison.Ordinal).Replace(",", string.Empty, StringComparison.Ordinal).Trim(),
            NumberStyles.AllowDecimalPoint,
            CultureInfo.InvariantCulture);
}
