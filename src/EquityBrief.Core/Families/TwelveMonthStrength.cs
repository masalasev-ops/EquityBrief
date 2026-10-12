using EquityBrief.Core.Sweep;

namespace EquityBrief.Core.Families;

// Twelve months' strength skipping the latest month as a night reads it: a close's return from the oldest close the
// night's year holds to the close a month back, one session short of the twelve months the search read, since the night
// keeps one year of closes and no rule reads an older price; none where either close is missing or the closes do not
// reach back that far.
// see: An index family's variants may stand registered before its freeze, its provisional rule staying the live one
public static class TwelveMonthStrength
{
    // The sessions back the strength reads from, the oldest close a year of 252 holds before the night's, and the month
    // it leaves out.
    public const int From = SweepAxes.TwelveMonthSessions - 1;

    public const int Skipped = SweepAxes.LatestMonthSessions;

    public static double? Of(IReadOnlyList<double> closes, int session)
    {
        var (from, to) = (session - From, session - Skipped);

        return from >= 0 && to < closes.Count && closes[from] > 0 && closes[to] > 0 ? (closes[to] / closes[from]) - 1.0 : null;
    }
}
