namespace EquityBrief.Core.Readings;

// One quarter's income as filed: the quarter it closes, the day it was filed, and its net income, operating income and
// interest expense, each none where the filing states none.
public sealed record FiledIncome(DateOnly PeriodEnd, DateOnly FilingDate, decimal? NetIncome, decimal? OperatingIncome, decimal? InterestExpense);

// The readings a 400 or 600 rule's floors and quality read, each as it stood on a session from what was filed before
// it, one function the night and the sweep share.
// see: The 400 and 600 rules start provisional with liquidity floors and a profit gate before any testing
public static class MemberReadings
{
    // The sessions the dollar volume is the mean over.
    public const int DollarVolumeSessions = 50;

    // The lowest close a 400 or 600 pick is bought at.
    public const decimal LowestPrice = 5m;

    // The least mean of close times volume over the 50 sessions, by index.
    public const decimal MidCapDollarVolume = 10_000_000m;

    public const decimal SmallCapDollarVolume = 5_000_000m;

    // The quarters the profit gate and the coverage sum.
    public const int Quarters = 4;

    // The operating income the coverage asks for each dollar of interest expense.
    public const decimal CoverageFloor = 2m;

    // The sector whose companies' interest is their operating cost, read as passing the coverage.
    public const string Financials = "Financials";

    // The mean of close times volume over the 50 sessions to the session, the last of the series the session's; none
    // where fewer than 50 are held.
    public static decimal? DollarVolume(IReadOnlyList<(decimal Close, long Volume)> toTheSession) =>
        toTheSession.Count < DollarVolumeSessions
            ? null
            : toTheSession.Skip(toTheSession.Count - DollarVolumeSessions).Average(bar => bar.Close * bar.Volume);

    public static decimal? DollarVolumeFloor(string indexCode) => indexCode switch
    {
        "MID" => MidCapDollarVolume,
        "SML" => SmallCapDollarVolume,
        _ => null,
    };

    // Whether a close and its dollar volume clear the index's floors; an index with none clears them, and a dollar
    // volume not held does not.
    public static bool ClearsTheFloors(string indexCode, decimal close, decimal? dollarVolume) =>
        DollarVolumeFloor(indexCode) is not { } floor
        || (close >= LowestPrice && dollarVolume is { } held && held >= floor);

    // The four newest quarters filed before the session, by the quarter each closes; a quarter filed on the session or
    // after it is not read, and one filed twice is read as first filed.
    public static IReadOnlyList<FiledIncome> FiledBefore(IEnumerable<FiledIncome> quarters, DateOnly session) =>
    [
        .. quarters
            .Where(quarter => quarter.FilingDate < session)
            .GroupBy(quarter => quarter.PeriodEnd)
            .Select(filings => filings.OrderBy(quarter => quarter.FilingDate).First())
            .OrderByDescending(quarter => quarter.PeriodEnd)
            .Take(Quarters),
    ];

    // The profit gate: net income summed over the four newest quarters filed before the session above nothing. Fewer
    // than four filed, or one of them stating no net income, does not pass.
    public static bool Profit(IEnumerable<FiledIncome> quarters, DateOnly session)
    {
        var read = FiledBefore(quarters, session);

        return read.Count == Quarters
            && read.All(quarter => quarter.NetIncome is not null)
            && read.Sum(quarter => quarter.NetIncome!.Value) > 0m;
    }

    // The sessions before a session its year's high is read over.
    public const int YearSessions = 251;

    // The sessions before a reaction its industry's peers' reports are read over.
    public const int PeerSessions = 20;

    // The windows an industry's return is read over.
    public static IReadOnlyList<int> IndustryWindows { get; } = [21, 63];

    // The highest high of the 251 sessions before a bar, the newer of two equal highs, and the sessions since it; none
    // for the first bar.
    public static (decimal High, int Since)? YearHigh(IReadOnlyList<Sweep.SweepBar> bars, int bar)
    {
        (decimal High, int At)? best = null;

        for (var at = Math.Max(0, bar - YearSessions); at < bar; at++)
        {
            if (best is not { } held || bars[at].High >= held.High)
            {
                best = (bars[at].High, at);
            }
        }

        return best is { } found ? (found.High, bar - found.At) : null;
    }

    // A session's volume over the mean of the 50 sessions' volumes the indicator engine stores for it; none where the
    // mean is not held or is nothing.
    public static double? VolumeRatio(long volume, double? average) =>
        average is { } mean && mean > 0 ? volume / mean : null;

    // The value-weighted mean of a group's figures: each member's figure weighted by its company's value, a member with no
    // value above nothing left out, and a group left with none reading none. An industry's return and its peers' mean
    // surprise are both read through it.
    public static double? ValueWeighted(IEnumerable<(decimal Value, double Figure)> members)
    {
        var (weighted, weight) = (0.0, 0.0);

        foreach (var (value, figure) in members)
        {
            if (value <= 0m || double.IsNaN(figure))
            {
                continue;
            }

            var held = Prices.Statistic.FromPrice(value);

            weighted += held * figure;
            weight += held;
        }

        return weight > 0 ? weighted / weight : null;
    }

    // The coverage: operating income over the four newest quarters at least twice their interest expense. A company
    // filing no interest expense on any of them passes, a financial company passes, and fewer than four filed or one
    // stating no operating income does not.
    public static bool Coverage(IEnumerable<FiledIncome> quarters, DateOnly session, string? sector)
    {
        if (string.Equals(sector, Financials, StringComparison.Ordinal))
        {
            return true;
        }

        var read = FiledBefore(quarters, session);

        if (read.Count < Quarters || read.Any(quarter => quarter.OperatingIncome is null))
        {
            return false;
        }

        var interest = read.Sum(quarter => Math.Abs(quarter.InterestExpense ?? 0m));

        return interest == 0m || read.Sum(quarter => quarter.OperatingIncome!.Value) >= CoverageFloor * interest;
    }
}
