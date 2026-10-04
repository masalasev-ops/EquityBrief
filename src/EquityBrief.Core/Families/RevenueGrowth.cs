using EquityBrief.Core.Prices;

namespace EquityBrief.Core.Families;

// One print's revenue growth: the quarter it reported and the same quarter a year before, each as first filed, and
// the growth of the one on the other.
public sealed record PrintRevenue(QuarterRevenue Quarter, QuarterRevenue YearBefore, double Growth);

// A print's revenue growth, its quarter's revenue as first filed against the same quarter's a year before.
//
// No period end is stored with a print, so its quarter is the newest the filer's facts state ending in the days before
// the report a quarter is reported within, and the year before's is the quarter ending within a week of a year before
// it, as a fetch's quarters are matched. The span is short of the quarter before's end, so a print whose own quarter
// the facts do not state reads none rather than the quarter before. Both are read as first filed, whatever day that
// was, and the share of picks reading a quarter first filed after their buy is stated beside the figures.
// see: A drift print's revenue growth is its quarter's revenue as first filed against the same quarter a year before
public static class RevenueGrowth
{
    // The days before a report its quarter may end in: nearly every print the history holds was reported inside them,
    // and past them the quarter found is mostly the one before a quarter the facts do not state.
    public const int ReportedWithin = 100;

    // How far the year before's quarter may end from a year before the print's: a week, a fiscal year of 52 or 53 weeks.
    public const int YearBeforeWithin = 7;

    public static PrintRevenue? Of(DateOnly report, IReadOnlyList<QuarterRevenue> quarters)
    {
        var quarter = quarters
            .Where(one => one.End < report && report.DayNumber - one.End.DayNumber <= ReportedWithin)
            .OrderByDescending(one => one.End)
            .FirstOrDefault();

        if (quarter is null)
        {
            return null;
        }

        var target = quarter.End.AddYears(-1);
        var before = quarters
            .Where(one => Math.Abs(one.End.DayNumber - target.DayNumber) <= YearBeforeWithin)
            .OrderBy(one => Math.Abs(one.End.DayNumber - target.DayNumber))
            .ThenBy(one => one.End)
            .FirstOrDefault();

        return before is { Value: > 0m }
            ? new PrintRevenue(quarter, before, Statistic.FromRatio(quarter.Value / before.Value) - 1.0)
            : null;
    }
}
