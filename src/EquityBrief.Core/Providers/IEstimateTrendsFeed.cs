using System.Globalization;
using System.Text.Json;

namespace EquityBrief.Core.Providers;

// One stock's earnings trend for its current fiscal year as the provider's trends answer states it on the day it is
// asked: the year's end, the consensus earnings a share now and 7, 30, 60 and 90 days before, and the analysts raising
// and cutting it over 7 and 30 days, each none where the answer files none.
public sealed record NightTrend(
    string Ticker,
    DateOnly PeriodEnd,
    decimal? Now,
    decimal? WeekBefore,
    decimal? MonthBefore,
    decimal? TwoMonthsBefore,
    decimal? QuarterBefore,
    int? UpWeek,
    int? UpMonth,
    int? DownWeek,
    int? DownMonth);

// The provider's earnings trends, many symbols in one request at one weighted call a symbol, asked by the night's trends
// fetcher alone.
// see: The night asks every member's estimate trend once a night in requests of a page of symbols
public interface IEstimateTrendsFeed
{
    Task<IReadOnlyList<NightTrend>> TrendsAsync(IReadOnlyList<string> tickers, CancellationToken cancellation = default);

    // The requests made and the symbols they asked for, the second the weighted calls they cost.
    int Requests { get; }

    int Symbols { get; }
}

// The trends answer as the provider sends it: an object whose "trends" holds an array a symbol, each record naming its
// code with the exchange's suffix, its period's end and its word, and its figures as text; the current fiscal year is the
// record filed under "0y", the newest end where several are. A symbol whose array holds no "0y" record answers nothing
// for it. An answer holding no "trends" array, or a record whose code or end cannot be read, is refused.
public static class EstimateTrends
{
    public const string CurrentYear = "0y";

    public const string ExchangeSuffix = ".US";

    // The symbols one request of the night asks for. An answer carries about a hundred records a symbol, 74 kilobytes of
    // AAPL's in 21.2's capture, so a page of 50 is under four megabytes and the night's 1,505 members about 31 requests.
    public const int PageSymbols = 50;

    public static IReadOnlyList<NightTrend> Parse(string json)
    {
        using var document = JsonDocument.Parse(json);

        if (document.RootElement.ValueKind != JsonValueKind.Object
            || !document.RootElement.TryGetProperty("trends", out var trends)
            || trends.ValueKind != JsonValueKind.Array)
        {
            throw new FormatException("The trends answer holds no \"trends\" array, so it is not the provider's trends.");
        }

        var read = new List<NightTrend>();

        foreach (var symbol in trends.EnumerateArray())
        {
            if (symbol.ValueKind != JsonValueKind.Array)
            {
                throw new FormatException("An entry of the trends answer is not a symbol's array of records.");
            }

            NightTrend? year = null;

            foreach (var record in symbol.EnumerateArray())
            {
                if (Text(record, "period") != CurrentYear)
                {
                    continue;
                }

                var code = Text(record, "code") ?? throw new FormatException("A record of the trends answer names no code.");
                var end = Text(record, "date") is { } date && DateOnly.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day)
                    ? day
                    : throw new FormatException($"The trends answer's record for {code} carries no end it can read.");
                var trend = new NightTrend(
                    code.EndsWith(ExchangeSuffix, StringComparison.Ordinal) ? code[..^ExchangeSuffix.Length] : code,
                    end,
                    Price(record, "epsTrendCurrent"),
                    Price(record, "epsTrend7daysAgo"),
                    Price(record, "epsTrend30daysAgo"),
                    Price(record, "epsTrend60daysAgo"),
                    Price(record, "epsTrend90daysAgo"),
                    Count(record, "epsRevisionsUpLast7days"),
                    Count(record, "epsRevisionsUpLast30days"),
                    Count(record, "epsRevisionsDownLast7days"),
                    Count(record, "epsRevisionsDownLast30days"));

                if (year is null || trend.PeriodEnd > year.PeriodEnd)
                {
                    year = trend;
                }
            }

            if (year is not null)
            {
                read.Add(year);
            }
        }

        return read;
    }

    static string? Text(JsonElement record, string name) =>
        record.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    static decimal? Price(JsonElement record, string name) =>
        Text(record, name) is { } text && decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var figure) ? figure : null;

    static int? Count(JsonElement record, string name) =>
        Price(record, name) is { } figure ? (int)decimal.Round(figure) : null;
}
