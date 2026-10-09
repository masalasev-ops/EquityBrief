namespace EquityBrief.Core.Loop;

// One trade or holding of a rule's reference, as it entered and what it came to against the same plan on every member.
public sealed record AlarmUnit(DateOnly Entered, double Edge);

// One period of a live rule as the alarm read it: its start, the units that ended in it and their mean edge, the low its
// reference gives at that many units, whether it counted, whether it stood under the low, how many counted periods
// running stood under it, and whether the rule is flagged on it.
public sealed record AlarmPeriod(DateOnly Start, int Units, double? Edge, double? Low, bool Counted, bool Under, int Streak, bool Flagged);

// The live alarm. Each period's mean edge after costs of a live rule's units that ended in it is read against the low
// of its reference, the rule today's trades over the newest tester run's test years: the fifth hundredth of the means of
// as many units as the period holds, drawn ten thousand times at a fixed seed by a stationary bootstrap over the
// reference's weeks, each draw running on to the next week or, one time in four, jumping to a week at random. A period
// holding fewer than five units neither counts nor breaks a run; two counted periods running under the low flag the
// rule. The swing families' periods are months and the sector heavyweights' quarters.
// see: The live alarm flags a rule whose edge stood under its reference's fifth percentile two periods running
public static class LiveAlarm
{
    public const int Draws = 10_000;

    public const int Seed = 20261011;

    public const double Quantile = 0.05;

    public const int FewestUnits = 5;

    public const int Running = 2;

    // The weeks a bootstrap block runs for on average, a jump to a week at random one time in this many.
    public const int WeeksABlock = 4;

    // The period a unit that ended on a day falls in: its month, or for a book its quarter.
    public static DateOnly PeriodOf(DateOnly ended, bool quarterly) =>
        quarterly ? new DateOnly(ended.Year, ((ended.Month - 1) / 3 * 3) + 1, 1) : new DateOnly(ended.Year, ended.Month, 1);

    // The low a period of a number of units is read against, none where the reference holds no unit or the period none.
    public static double? Low(IReadOnlyList<AlarmUnit> reference, int units)
    {
        if (reference.Count == 0 || units <= 0)
        {
            return null;
        }

        var weeks = reference
            .Select((unit, place) => (unit, place))
            .GroupBy(one => one.unit.Entered.AddDays(-(((int)one.unit.Entered.DayOfWeek + 6) % 7)))
            .OrderBy(week => week.Key)
            .Select(week => week.OrderBy(one => one.unit.Entered).ThenBy(one => one.place).Select(one => one.unit.Edge).ToArray())
            .ToArray();
        var random = new Random(Seed);
        var means = new double[Draws];

        for (var draw = 0; draw < Draws; draw++)
        {
            var (taken, sum, week) = (0, 0.0, random.Next(weeks.Length));

            while (true)
            {
                foreach (var edge in weeks[week])
                {
                    sum += edge;

                    if (++taken == units)
                    {
                        break;
                    }
                }

                if (taken == units)
                {
                    break;
                }

                week = random.Next(WeeksABlock) == 0 ? random.Next(weeks.Length) : (week + 1) % weeks.Length;
            }

            means[draw] = sum / units;
        }

        Array.Sort(means);

        return means[(int)Math.Ceiling(Quantile * Draws) - 1];
    }

    // Every period read in order, each counted where it holds the fewest units or more and its reference gives a low, the
    // run of counted periods under the low carried on from the period read before them.
    public static IReadOnlyList<AlarmPeriod> Read(IReadOnlyList<(DateOnly Start, IReadOnlyList<double> Edges)> periods, IReadOnlyList<AlarmUnit> reference, int streak = 0)
    {
        var read = new List<AlarmPeriod>();

        foreach (var (start, edges) in periods.OrderBy(period => period.Start))
        {
            double? edge = edges.Count > 0 ? edges.Average() : null;
            var low = edges.Count >= FewestUnits ? Low(reference, edges.Count) : null;
            var counted = low is not null;
            var under = counted && edge < low;

            if (counted)
            {
                streak = under ? streak + 1 : 0;
            }

            read.Add(new AlarmPeriod(start, edges.Count, edge, low, counted, under, streak, counted && streak >= Running));
        }

        return read;
    }
}
