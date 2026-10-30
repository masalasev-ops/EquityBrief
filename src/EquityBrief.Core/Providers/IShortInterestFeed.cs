using System.Globalization;
using System.Text.Json;
using EquityBrief.Core.Bars;

namespace EquityBrief.Core.Providers;

// One stock's consolidated short interest at one settlement as FINRA publishes it: FINRA's symbol, the settlement date,
// the shares short, and the average daily volume, the days to cover and the revision flag FINRA states, each none where
// FINRA files none.
public sealed record ShortInterestReport(string Symbol, DateOnly Settlement, long SharesShort, long? AverageVolume, double? DaysToCover, string? Revision);

// FINRA's consolidated short interest, keyless and free and not the provider's: the settlement dates one symbol's reports
// carry over a span, and one settlement's reports for many symbols in one request.
// see: FINRA's short interest is read once a settlement after the labeller, and its history once by hand
public interface IShortInterestFeed
{
    Task<IReadOnlyList<DateOnly>> SettlementsAsync(string symbol, DateOnly from, DateOnly through, CancellationToken cancellation = default);

    Task<IReadOnlyList<ShortInterestReport>> SettlementAsync(DateOnly settlement, IReadOnlyList<string> symbols, CancellationToken cancellation = default);

    int Requests { get; }
}

// FINRA's answer as it sends it: an array of records, each naming its symbol and its settlement date and carrying its
// figures as numbers, a field it was not asked for absent. A record naming no symbol or no date, or carrying no shares
// short, is refused, and so is an answer that is not an array.
public static class ShortInterests
{
    // The symbol whose settlements stand for every settlement FINRA has published, since it is reported at each.
    public const string ReferenceSymbol = "AAPL";

    // The first settlement FINRA's query answers, 21.2's measure.
    public static DateOnly FirstSettlement { get; } = new(2017, 12, 29);

    // The symbols one request asks for, under FINRA's page of 5,000 records with room for a symbol reported in two
    // markets.
    public const int SymbolsARequest = 500;

    // The records FINRA's query returns at most in one answer; an answer holding this many may hold fewer than the
    // request matched, so it is refused rather than read short.
    public const int RecordsAnAnswer = 5000;

    // The business days after a settlement FINRA publishes its short interest on, 21.2's measure of its schedule.
    public const int PublishedAfter = 7;

    // The symbol FINRA files a stock under: a share class joined to its ticker with no separator, BRK-B as BRKB, as
    // FINRA's own answers name them.
    public static string SymbolOf(string ticker) =>
        ticker.Replace("-", string.Empty, StringComparison.Ordinal).Replace(".", string.Empty, StringComparison.Ordinal);

    // The business days a publication day counted on weekdays alone is moved later by: the largest miss counting weekdays
    // alone makes against the closure table over the settlements the table covers, measured at the 21.6 correction.
    public const int EstimatedLate = 1;

    // The day FINRA publishes a settlement: the seventh business day after it, a weekday the exchange's closures do not
    // hold where the closure table covers the day. A settlement whose count starts before the table's reach is counted
    // on weekdays alone and moved later by the largest miss that makes, so no report is read before it was published.
    // see: A FINRA publication day counted before the closure table's reach is moved later by the largest miss counting weekdays alone makes where the table reaches
    public static DateOnly PublishedOn(DateOnly settlement)
    {
        var day = settlement;
        var estimated = settlement.AddDays(1) < ExchangeClosures.CoveredFrom;

        for (var counted = 0; counted < PublishedAfter + (estimated ? EstimatedLate : 0);)
        {
            day = day.AddDays(1);

            if (day.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday)
                && (day < ExchangeClosures.CoveredFrom || day > ExchangeClosures.CoveredThrough || ExchangeClosures.IsSession(day)))
            {
                counted++;
            }
        }

        return day;
    }

    public static IReadOnlyList<ShortInterestReport> Parse(string json)
    {
        using var document = JsonDocument.Parse(json);

        if (document.RootElement.ValueKind != JsonValueKind.Array)
        {
            throw new FormatException("FINRA's answer is not an array of records, so it is not its short interest.");
        }

        var read = new List<ShortInterestReport>();

        foreach (var record in document.RootElement.EnumerateArray())
        {
            var symbol = Text(record, "symbolCode") ?? throw new FormatException("A record of FINRA's answer names no symbol.");
            var settlement = Text(record, "settlementDate") is { } date && DateOnly.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day)
                ? day
                : throw new FormatException($"FINRA's record for {symbol} carries no settlement date it can read.");
            var shares = Whole(record, "currentShortPositionQuantity")
                ?? throw new FormatException($"FINRA's record for {symbol} on {settlement.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)} carries no shares short.");

            read.Add(new ShortInterestReport(
                symbol,
                settlement,
                shares,
                Whole(record, "averageDailyVolumeQuantity"),
                record.TryGetProperty("daysToCoverQuantity", out var cover) && cover.ValueKind == JsonValueKind.Number ? cover.GetDouble() : null,
                Text(record, "revisionFlag")));
        }

        return read;
    }

    // The settlement dates an answer's records carry, each once, oldest first.
    public static IReadOnlyList<DateOnly> SettlementsIn(string json) =>
        [.. Parse(json).Select(report => report.Settlement).Distinct().Order()];

    static string? Text(JsonElement record, string name) =>
        record.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    static long? Whole(JsonElement record, string name) =>
        record.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var whole) ? whole : null;
}
