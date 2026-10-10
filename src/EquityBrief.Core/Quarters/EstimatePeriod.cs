using System.Globalization;
using System.Text.Json;

namespace EquityBrief.Core.Quarters;

// One period of the analysts' earnings trend as a fundamentals answer files it: the current or the next quarter or
// fiscal year, the day it ends, the consensus earnings a share with its range, the year-earlier figure and how many
// analysts estimate it, the same for revenue, the consensus as it stood 7, 30, 60 and 90 days before, and how many
// analysts raised or cut it over the last 7 and 30. Each figure as the provider files it and none where it files none.
// Money a share or a whole company's revenue, so decimal; the counts whole.
// see: The quarters fetch keeps the gross profit and cash flow lines, the estimate trend and the analysts' mean and target its answer carries
public sealed record EstimatePeriod(
    string Period,
    DateOnly PeriodEnd,
    decimal? EpsAverage,
    decimal? EpsLow,
    decimal? EpsHigh,
    decimal? EpsYearAgo,
    int? EpsAnalysts,
    decimal? RevenueAverage,
    decimal? RevenueLow,
    decimal? RevenueHigh,
    decimal? RevenueYearAgo,
    int? RevenueAnalysts,
    decimal? EpsNow,
    decimal? Eps7DaysAgo,
    decimal? Eps30DaysAgo,
    decimal? Eps60DaysAgo,
    decimal? Eps90DaysAgo,
    int? UpLast7Days,
    int? UpLast30Days,
    int? DownLast7Days,
    int? DownLast30Days)
{
    // The provider's words for the four forward periods it files a trend for, in the order the page reads them.
    public const string CurrentQuarter = "0q";
    public const string NextQuarter = "+1q";
    public const string CurrentYear = "0y";
    public const string NextYear = "+1y";

    public static IReadOnlyList<string> Periods { get; } = [CurrentQuarter, NextQuarter, CurrentYear, NextYear];

    // The four forward periods of an answer's earnings trend, each the newest end the answer files under its word, in
    // the order above; a period the answer files none for is left out. The trend is an object keyed by each period's
    // end, and a quarter ending with its fiscal year shares its key with the year, so every entry is read whatever its
    // key and the period is read off the entry's own word.
    public static IReadOnlyList<EstimatePeriod> FromTrend(JsonElement trend)
    {
        if (trend.ValueKind != JsonValueKind.Object)
        {
            return [];
        }

        var entries = trend.EnumerateObject()
            .Select(entry => entry.Value)
            .Where(entry => entry.ValueKind == JsonValueKind.Object && Text(entry, "period") is { } word && Periods.Contains(word) && Day(Text(entry, "date")) is not null)
            .ToArray();

        return
        [
            .. Periods
                .Select(word => entries.Where(entry => Text(entry, "period") == word).ToArray())
                .Where(filed => filed.Length > 0)
                .Select(filed => filed.MaxBy(entry => Day(Text(entry, "date"))))
                .Select(entry => new EstimatePeriod(
                    Text(entry, "period")!,
                    Day(Text(entry, "date"))!.Value,
                    Figure(entry, "earningsEstimateAvg"),
                    Figure(entry, "earningsEstimateLow"),
                    Figure(entry, "earningsEstimateHigh"),
                    Figure(entry, "earningsEstimateYearAgoEps"),
                    Count(entry, "earningsEstimateNumberOfAnalysts"),
                    Figure(entry, "revenueEstimateAvg"),
                    Figure(entry, "revenueEstimateLow"),
                    Figure(entry, "revenueEstimateHigh"),
                    Figure(entry, "revenueEstimateYearAgoEps"),
                    Count(entry, "revenueEstimateNumberOfAnalysts"),
                    Figure(entry, "epsTrendCurrent"),
                    Figure(entry, "epsTrend7daysAgo"),
                    Figure(entry, "epsTrend30daysAgo"),
                    Figure(entry, "epsTrend60daysAgo"),
                    Figure(entry, "epsTrend90daysAgo"),
                    Count(entry, "epsRevisionsUpLast7days"),
                    Count(entry, "epsRevisionsUpLast30days"),
                    Count(entry, "epsRevisionsDownLast7days"),
                    Count(entry, "epsRevisionsDownLast30days"))),
        ];
    }

    static string? Text(JsonElement row, string name) =>
        row.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    static DateOnly? Day(string? text) =>
        DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day) ? day : null;

    // A figure the provider sends as text or as a number, none where it sends neither.
    static decimal? Figure(JsonElement row, string name) =>
        !row.TryGetProperty(name, out var value)
            ? null
            : value.ValueKind switch
            {
                JsonValueKind.Number => value.TryGetDecimal(out var number) ? number : null,
                JsonValueKind.String => decimal.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) ? parsed : null,
                _ => null,
            };

    // A count the provider sends as a figure with decimals, "40.0000", read as the whole number it is and none where it
    // is not one.
    static int? Count(JsonElement row, string name) =>
        Figure(row, name) is { } figure && figure == decimal.Truncate(figure) && figure >= 0 && figure <= int.MaxValue
            ? decimal.ToInt32(figure)
            : null;
}
