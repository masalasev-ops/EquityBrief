using EquityBrief.Core.Sweep;

namespace EquityBrief.Worker.Sweep;

// One level of a new dial, or one market switch, tried alone on one of the strongest settings of an index's sweep: the
// setting's measures without it and with it.
public sealed record DialTry(string Dial, string Level, bool Switch, int Setting, SweepMeasures Without, SweepMeasures With)
{
    public bool Kept => SweepDials.Kept(Without, With, Switch);
}

// One level of a new dial or one market switch as the second stage reads it: the quality, the multiple of the dollar
// volume floor or the hold it sets in place of the provisional one, a test a candidate must pass beside the floors and
// the gate, or the S&P 500's breadth read in place of the index's own.
public sealed record DialLevel(
    string Dial,
    string Name,
    bool Switch = false,
    IndexQuality? Quality = null,
    decimal? Floors = null,
    int? Hold = null,
    Func<SweepCandidate, bool>? Keep = null,
    bool LargeBreadth = false);

// One level that survived, with how many of the strongest settings kept it and the median change it made there, on the
// edge after costs or, for a switch, on the result a trade.
public sealed record DialSurvivor(string Dial, string Level, bool Switch, int KeptOn, double? MedianChange);

// The second stage of an index's sweep: each new dial's levels and each market switch tried alone on the strongest
// settings the first stage read. A level is kept on a setting where it is higher in at least 6 of the 8 years with 2
// of the last 3 and leaves at least 300 trades, on the edge after costs, or for a market switch on the year's total
// result with its result a trade higher as well, as the ideas' run judges a switch; and it survives where at least 6
// of the 10 settings keep it, the bar the pullback sweep's conditions were held to, since a level that changes nothing
// passes one setting about one time in eight.
// see: A market switch is judged on the year's total result, since the edge subtracts what every member made that night
public static class SweepDials
{
    // The strongest settings each level is tried on.
    public const int Settings = 10;

    // The settings that must keep a level for it to survive.
    public const int KeptOn = 6;

    public static bool Kept(SweepMeasures without, SweepMeasures with, bool onTotals)
    {
        if (with.Scored < SweepMeasures.TradeFloor)
        {
            return false;
        }

        var years = Math.Min(without.YearScored.Length, with.YearScored.Length);
        var (better, recent) = (0, 0);

        for (var year = 0; year < years; year++)
        {
            var higher = onTotals
                ? Total(with, year) > Total(without, year)
                : with.YearEdge[year] is { } mine && without.YearEdge[year] is { } theirs && mine > theirs;

            if (higher)
            {
                better++;
                recent += year >= years - SweepIdeas.RecentYears ? 1 : 0;
            }
        }

        return better >= SweepIdeas.YearsBetter
            && recent >= SweepIdeas.RecentYearsBetter
            && (!onTotals || (with.AverageMultiple is { } result && without.AverageMultiple is { } before && result > before));
    }

    // A year's total result, each scored trade's result in multiples of its risk summed, a year with none nothing.
    public static double Total(SweepMeasures measures, int year) => (measures.YearAverageMultiple[year] ?? 0) * measures.YearScored[year];

    // Each level tried with how many settings kept it and the median change it made, the survivors those kept on at
    // least six, in the order the levels were tried.
    public static IReadOnlyList<DialSurvivor> Read(IReadOnlyList<DialTry> tries) =>
    [
        .. tries
            .GroupBy(one => (one.Dial, one.Level, one.Switch))
            .Select(level => new DialSurvivor(
                level.Key.Dial,
                level.Key.Level,
                level.Key.Switch,
                level.Count(one => one.Kept),
                Median([.. level.Select(one => level.Key.Switch ? Change(one.Without.AverageMultiple, one.With.AverageMultiple) : Change(one.Without.Edge, one.With.Edge)).OfType<double>()]))),
    ];

    static double? Change(double? without, double? with) => without is { } before && with is { } after ? after - before : null;

    static double? Median(double[] values)
    {
        if (values.Length == 0)
        {
            return null;
        }

        Array.Sort(values);

        return values.Length % 2 == 1 ? values[values.Length / 2] : (values[(values.Length / 2) - 1] + values[values.Length / 2]) / 2;
    }
}
