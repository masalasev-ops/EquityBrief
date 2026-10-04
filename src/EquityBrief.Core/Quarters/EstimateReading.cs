using System.Globalization;
using System.Text.Json;

namespace EquityBrief.Core.Quarters;

// A member's analysts' estimate for its current fiscal year as the provider's answer filed it on a night: the day the
// fiscal year ends, the consensus estimate of its earnings a share then and the same estimate thirty days before, and
// why the night holds none where it holds none.
// see: A member's estimates are raised where its current fiscal year's consensus earnings estimate stands above its level 30 days before
public sealed record EstimateReading(DateOnly? YearEnd, decimal? Current, decimal? DaysAgo, string? NotRead = null)
{
    // The days back the estimate is compared over, which names the provider's own field.
    public const int Days = 30;

    // The provider's word for the current fiscal year among the periods it files a trend for.
    public const string CurrentYear = "0y";

    public static string CurrentField => "epsTrendCurrent";

    public static string DaysAgoField => FormattableString.Invariant($"epsTrend{Days}daysAgo");

    // Raised where the estimate stands above its level the days before, not where it stands level or below, and none
    // where either is not filed, which a rule reading it reads as not raised.
    public bool? Raised => Current is { } now && DaysAgo is { } then ? now > then : null;

    // A reading the night could not take, with why.
    public static EstimateReading Unread(string why) => new(null, null, null, why);

    // The current fiscal year's trend from a fundamentals answer's earnings trend: the period the provider files as the
    // current year with the newest end, its estimate now and the days before, each a figure the provider sends as text
    // or a number; none filed where the answer carries no such period, and a figure not filed read as none.
    public static EstimateReading FromTrend(JsonElement trend)
    {
        if (trend.ValueKind != JsonValueKind.Object)
        {
            return Unread("the answer files no earnings trend");
        }

        var years = trend.EnumerateObject()
            .Select(entry => entry.Value)
            .Where(entry => entry.ValueKind == JsonValueKind.Object && Text(entry, "period") == CurrentYear && Day(Text(entry, "date")) is not null)
            .ToArray();

        if (years.Length == 0)
        {
            return Unread("the answer files no estimate for the current fiscal year");
        }

        var current = years.MaxBy(entry => Day(Text(entry, "date")));

        var reading = new EstimateReading(Day(Text(current, "date")), Figure(current, CurrentField), Figure(current, DaysAgoField));
        var missing = reading.Current is null ? "now" : FormattableString.Invariant($"{Days} days before");

        return reading.Current is null || reading.DaysAgo is null
            ? reading with { NotRead = $"the answer files the current fiscal year's estimate {missing} as none" }
            : reading;
    }

    static string? Text(JsonElement row, string name) =>
        row.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    static DateOnly? Day(string? text) =>
        DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day) ? day : null;

    static decimal? Figure(JsonElement row, string name) =>
        !row.TryGetProperty(name, out var value)
            ? null
            : value.ValueKind switch
            {
                JsonValueKind.Number => value.TryGetDecimal(out var number) ? number : null,
                JsonValueKind.String => decimal.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) ? parsed : null,
                _ => null,
            };
}
