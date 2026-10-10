using EquityBrief.Core.Prices;
using EquityBrief.Core.Quarters;

namespace EquityBrief.Core.Report;

// The analysts' five rating counts, their mean rating and their mean target as one fetch stored them, and the day of it.
public sealed record RatingFetch(DateOnly FetchedOn, int? StrongBuy, int? Buy, int? Hold, int? Sell, int? StrongSell, double? Mean, decimal? Target)
{
    public bool Counted => StrongBuy is not null || Buy is not null || Hold is not null || Sell is not null || StrongSell is not null;
}

// One period's consensus: earnings a share with its range, the year-earlier figure and its growth on it in per cent, how
// many analysts estimate it, and the same for revenue.
public sealed record ConsensusRow(
    string Period,
    DateOnly PeriodEnd,
    decimal? Eps,
    decimal? EpsLow,
    decimal? EpsHigh,
    decimal? EpsYearAgo,
    double? EpsGrowth,
    int? EpsAnalysts,
    decimal? Revenue,
    decimal? RevenueLow,
    decimal? RevenueHigh,
    decimal? RevenueYearAgo,
    double? RevenueGrowth,
    int? RevenueAnalysts);

// One period's revisions: the analysts who raised and cut their estimate over the last 7 and 30 days, and the consensus
// now against 30 days before in per cent of it.
public sealed record RevisionRow(string Period, int? Up7, int? Down7, int? Up30, int? Down30, double? Change30);

// One point of the consensus's own trend: how many days before the fetch, and the consensus then.
public sealed record TrendPoint(int DaysAgo, decimal Eps);

// One calendar month's rating counts, the newest fetch inside it, none where no fetch fell in the month.
public sealed record RatingMonth(int Year, int Month, RatingFetch? Fetch);

// What the analysts say of the company as the newest fetch on or before the night filed it: the consensus for the quarter
// and the year with their ranges, the revisions, the consensus's trend for the year, the rating counts by month over the
// twelve months to the night, and the mean rating and target with the target in per cent of the price drawn.
public sealed record AnalystReading(
    DateOnly FetchedOn,
    IReadOnlyList<ConsensusRow> Consensus,
    IReadOnlyList<RevisionRow> Revisions,
    IReadOnlyDictionary<string, IReadOnlyList<TrendPoint>> Trends,
    IReadOnlyList<RatingMonth> Months,
    double? MeanRating,
    decimal? Target,
    double? TargetAgainstPrice);

// The analysts' figures, each labelled as theirs and dated by the fetch that kept it, worked by one function so the page
// and its file draw the same; none of it reaches written prose.
// see: The name page draws the analysts' figures each labelled as theirs and dated by its fetch, and no written sentence states one
public static class AnalystView
{
    // The months of rating counts drawn, to the night's own.
    public const int Months = 12;

    // The days before the fetch the provider files the consensus as it stood, oldest first, then now.
    public static IReadOnlyList<int> TrendDays { get; } = [90, 60, 30, 7, 0];

    public static AnalystReading? Of(
        DateOnly? fetchedOn,
        IReadOnlyList<EstimatePeriod> trend,
        IReadOnlyList<RatingFetch> fetches,
        DateOnly night,
        decimal? price)
    {
        var newest = fetches.Where(fetch => fetch.FetchedOn <= night).MaxBy(fetch => fetch.FetchedOn);

        if (fetchedOn is null && newest is null)
        {
            return null;
        }

        return new AnalystReading(
            fetchedOn ?? newest!.FetchedOn,
            [
                .. trend.Select(period => new ConsensusRow(
                    period.Period,
                    period.PeriodEnd,
                    period.EpsAverage,
                    period.EpsLow,
                    period.EpsHigh,
                    period.EpsYearAgo,
                    Percents.FromFraction(QuarterFetch.Grown(period.EpsAverage, period.EpsYearAgo)),
                    period.EpsAnalysts,
                    period.RevenueAverage,
                    period.RevenueLow,
                    period.RevenueHigh,
                    period.RevenueYearAgo,
                    Percents.FromFraction(QuarterFetch.Grown(period.RevenueAverage, period.RevenueYearAgo)),
                    period.RevenueAnalysts)),
            ],
            [
                .. trend.Select(period => new RevisionRow(
                    period.Period,
                    period.UpLast7Days,
                    period.DownLast7Days,
                    period.UpLast30Days,
                    period.DownLast30Days,
                    Percents.FromFraction(QuarterFetch.Grown(period.EpsNow, period.Eps30DaysAgo)))),
            ],
            trend.ToDictionary(period => period.Period, period => Points(period), StringComparer.Ordinal),
            ByMonth(fetches, night),
            newest?.Mean,
            newest?.Target,
            newest?.Target is { } target && price is { } shown && shown > 0m ? Percents.FromFraction(QuarterFetch.Grown(target, shown)) : null);
    }

    // The consensus as it stood 90, 60, 30 and 7 days before the fetch and at it, each where the provider filed it.
    static IReadOnlyList<TrendPoint> Points(EstimatePeriod period) =>
        [
            .. new (int Days, decimal? Eps)[]
                {
                    (90, period.Eps90DaysAgo),
                    (60, period.Eps60DaysAgo),
                    (30, period.Eps30DaysAgo),
                    (7, period.Eps7DaysAgo),
                    (0, period.EpsNow),
                }
                .Where(point => point.Eps is not null)
                .Select(point => new TrendPoint(point.Days, point.Eps!.Value)),
        ];

    // The twelve months to the night's, oldest first, each with the newest fetch inside it that filed the counts.
    public static IReadOnlyList<RatingMonth> ByMonth(IReadOnlyList<RatingFetch> fetches, DateOnly night)
    {
        var first = new DateOnly(night.Year, night.Month, 1).AddMonths(-(Months - 1));

        return
        [
            .. Enumerable.Range(0, Months)
                .Select(back => first.AddMonths(back))
                .Select(month => new RatingMonth(
                    month.Year,
                    month.Month,
                    fetches
                        .Where(fetch => fetch.Counted && fetch.FetchedOn.Year == month.Year && fetch.FetchedOn.Month == month.Month && fetch.FetchedOn <= night)
                        .MaxBy(fetch => fetch.FetchedOn))),
        ];
    }
}
