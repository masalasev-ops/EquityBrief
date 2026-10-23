using EquityBrief.Core.Providers;

namespace EquityBrief.Core.Analysts;

// How often a chart page's analysts are asked of Yahoo and how many of a stock's firm actions the page shows, held by the
// page, the read surface and the analyst job alike.
// see: The chart page graphs the report's earnings and revenue beneath its chart with the analysts' figures Yahoo answers when it opens, and no rule reads them from it
public static class AnalystLimits
{
    // The most asks of Yahoo the analyst job makes in a New York day across every stock.
    public const int DailyCap = 100;

    // The fewest minutes between two asks of one stock's analysts, so a page opened again, or a second page, while an ask
    // is out or after one failed asks nothing more.
    public const int AskAgainMinutes = 10;

    // The firm actions the page shows before its control opens the rest.
    public const int ActionsShown = 10;

    // The run log's stages of the analyst job and the ratings job, which the read surface reads the month's calls from.
    public const string AnalystsStage = "analysts";

    public const string RatingsStage = "analyst-ratings";

    // The New York day an instant falls in, as the two instants that bound it, the first inside and the second after.
    public static (DateTimeOffset From, DateTimeOffset To) DayOf(DateTimeOffset instant, TimeZoneInfo zone)
    {
        var day = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, zone).DateTime);

        return (Midnight(day, zone), Midnight(day.AddDays(1), zone));
    }

    static DateTimeOffset Midnight(DateOnly day, TimeZoneInfo zone)
    {
        var local = day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);

        return new DateTimeOffset(local, zone.GetUtcOffset(local)).ToUniversalTime();
    }
}

// The newest answer Yahoo gave about a stock's analysts, with the instant the page asked.
public sealed record YahooAnalystView(DateTimeOffset AskedAt, YahooAnalystAnswer Answer);

// The newest answer TipRanks gave about a stock's ratings, with the instant the press asked.
public sealed record TipRanksView(DateTimeOffset AskedAt, TipRanksAnswer Answer);

// The month's TipRanks calls as the ratings job last read them: how many were used, how many the operator's limit and
// TipRanks' own allow, the lesser of the two, when the count starts again and when it was read.
// see: TipRanks' analyst ratings are asked only on a press, once a stock a day and never past the month's limit
public sealed record TipRanksCalls(int Used, int Limit, DateTimeOffset ResetsAt, DateTimeOffset ReadAt)
{
    // The calls left at an instant: none past the limit, and the whole limit once the count has started again.
    public int Left(DateTimeOffset now) => now >= ResetsAt ? Limit : Math.Max(Limit - Used, 0);

    // The calls used at an instant, none once the count has started again.
    public int UsedAt(DateTimeOffset now) => now >= ResetsAt ? 0 : Used;

    // Whether a call would pass the limit at an instant.
    public bool Spent(DateTimeOffset now) => Left(now) == 0;
}
