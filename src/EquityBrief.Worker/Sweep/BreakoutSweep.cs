using EquityBrief.Core.Families;
using EquityBrief.Core.Prices;
using EquityBrief.Core.Sweep;

namespace EquityBrief.Worker.Sweep;

// The breakout family's readings on one member-session some setting of its grid could list: the close, the
// highest high of each window of sessions before it, the volume over its fifty-session average, the newer
// ranges over the older ones, and the typical move.
public readonly record struct BreakoutReading(int Name, int Bar, int Session, double Close, double HighShort, double HighYear, double Volume, double Ranges, double Move);

// The breakout family's sweep: its rule's readings over the stored history, the listings each setting makes of
// them, its trailing trade, and the benchmark of the same trailing plan entered on every member that night.
// see: A setup family's sweep replays its own rule over the stored history and proposes the best edge among the settings meeting its floors, or brings the strongest where none does
public sealed class BreakoutSweep
{
    // The shorter window the year's high is read against beside the rule's own, half a year of sessions.
    public const int ShortHighSessions = 126;

    // The grid around the provisional setting the sweep proposed its freeze from.
    public static FamilyGrid Grid { get; } = new(
        [
            ("high", [ShortHighSessions, BreakoutRule.ProvisionalHighSessions]),
            ("volume", [1.25, BreakoutRule.VolumeMultiple, 2.0]),
            ("ceiling", [0.85, BreakoutRule.ProvisionalRangeCeiling, double.PositiveInfinity]),
            ("stop", [1.5, BreakoutRule.ProvisionalStopMoves, 3]),
        ],
        [1, 1, 1, 1]);

    readonly IReadOnlyList<SweepSeries> series;
    readonly SweepBenchmark.Members members;
    readonly double[][] closes;
    readonly Dictionary<(int Session, double Stop), double> benchmarks = [];

    public BreakoutSweep(IReadOnlyList<SweepSeries> series, SweepBenchmark.Members members)
    {
        this.series = series;
        this.members = members;
        closes = [.. series.Select(one => one.Bars.Select(bar => Statistic.FromPrice(bar.Close)).ToArray())];
    }

    // Every member-session on or after the first scored session, held by the index with no gap, on a night the
    // market check left open, whose close stands above the shorter window's high on at least the lowest volume
    // multiple the grid reads: the readings any setting's listings are drawn from.
    public IReadOnlyList<BreakoutReading> Readings(IReadOnlyList<SweepColumns.Session> sessions, int firstScored)
    {
        var floor = FamilySweep.MarketFloor;
        var loosest = Grid.Dials[1].Levels.Min();
        var found = new List<BreakoutReading>();

        for (var name = 0; name < series.Count; name++)
        {
            var one = series[name];
            var shortHigh = HighsBefore(one.Bars, ShortHighSessions);
            var yearHigh = HighsBefore(one.Bars, BreakoutRule.ProvisionalHighSessions);
            var ranges = RangesBefore(one.Bars, BreakoutRule.RangeSessions);

            for (var bar = 0; bar < one.Bars.Length; bar++)
            {
                var session = one.SessionAt[bar];

                if (session < firstScored || !one.Member[bar] || one.Gap[bar] || !(sessions[session].Breadth >= floor))
                {
                    continue;
                }

                var close = closes[name][bar];
                var volume = one.Volume50[bar] > 0 ? Statistic.FromVolume(one.Bars[bar].Volume) / one.Volume50[bar] : double.NaN;

                if (!(close > shortHigh[bar]) || !(volume >= loosest))
                {
                    continue;
                }

                found.Add(new BreakoutReading(name, bar, session, close, shortHigh[bar], yearHigh[bar], volume, ranges[bar], one.Atr[bar]));
            }
        }

        return found;
    }

    // The listings one setting makes: a close above its window's high, on the volume multiple, after ranges no
    // wider than the ceiling's share of the older ones where the ceiling is read, bought at the close with the
    // stop the setting's typical moves beneath and trailing the highest close by that distance, in the order
    // of the volume multiple; a night with no typical move places no stop and lists nothing.
    public static IEnumerable<FamilyListing> Listings(IReadOnlyList<BreakoutReading> readings, int[] setting)
    {
        var high = Grid.Value(setting, 0);
        var volume = Grid.Value(setting, 1);
        var ceiling = Grid.Value(setting, 2);
        var moves = Grid.Value(setting, 3);

        foreach (var reading in readings)
        {
            var above = high >= BreakoutRule.ProvisionalHighSessions ? reading.Close > reading.HighYear : reading.Close > reading.HighShort;
            var tightened = double.IsPositiveInfinity(ceiling) || reading.Ranges <= ceiling;
            var risk = moves * reading.Move;

            if (!above || !(reading.Volume >= volume) || !tightened || !(risk > 0) || reading.Close - risk <= 0)
            {
                continue;
            }

            yield return new FamilyListing(reading.Name, reading.Bar, reading.Session, reading.Volume, 0, reading.Close, reading.Close - risk, double.NaN, risk, BreakoutRule.CapSessions, reading.Move);
        }
    }

    public (double? Result, int Sessions) Exit(FamilyListing listing)
    {
        var result = FamilyWalks.Trailing(closes[listing.Name], listing.Bar, listing.Entry, listing.Stop, listing.Trail, listing.Cap, out var sessions);

        return (result, sessions);
    }

    // The same trailing plan entered at the close on every member the index held that night with a bar and no
    // gap, its stop and its trail the setting's typical moves in each member's own, the average of the results
    // the history reaches the end of.
    public double Benchmark(FamilyListing listing)
    {
        var moves = listing.Trail / series[listing.Name].Atr[listing.Bar];
        var key = (listing.Session, Math.Round(moves, 6));

        if (benchmarks.TryGetValue(key, out var held))
        {
            return held;
        }

        var value = BenchmarkOn(series, closes, members, listing.Session, moves, listing.Cap);

        benchmarks[key] = value;

        return value;
    }

    public static double BenchmarkOn(IReadOnlyList<SweepSeries> series, double[][] closes, SweepBenchmark.Members members, int session, double moves, int cap) =>
        BenchmarkCounted(series, closes, members, session, moves, cap).Average;

    // The same, with how many members' results it averaged, which a registered rule's stored trade carries.
    public static (double Average, int Members) BenchmarkCounted(IReadOnlyList<SweepSeries> series, double[][] closes, SweepBenchmark.Members members, int session, double moves, int cap)
    {
        var names = members.Names[session];
        var bars = members.Bars[session];
        var sum = 0.0;
        var count = 0;

        for (var at = 0; at < names.Length; at++)
        {
            var name = names[at];
            var bar = bars[at];
            var move = series[name].Atr[bar];
            var entry = closes[name][bar];
            var risk = moves * move;

            if (!(risk > 0) || entry - risk <= 0)
            {
                continue;
            }

            if (FamilyWalks.Trailing(closes[name], bar, entry, entry - risk, risk, cap, out _) is { } result)
            {
                sum += result;
                count++;
            }
        }

        return (count > 0 ? sum / count : double.NaN, count);
    }

    // The highest high of the given count of sessions before each bar, none where fewer stand before it.
    public static double[] HighsBefore(SweepBar[] bars, int sessions)
    {
        var highs = new double[bars.Length];
        var window = new LinkedList<int>();

        for (var bar = 0; bar < bars.Length; bar++)
        {
            while (window.First is { } oldest && oldest.Value < bar - sessions)
            {
                window.RemoveFirst();
            }

            highs[bar] = bar >= sessions && window.First is { } top ? Statistic.FromPrice(bars[top.Value].High) : double.NaN;

            while (window.Last is { } newest && bars[newest.Value].High <= bars[bar].High)
            {
                window.RemoveLast();
            }

            window.AddLast(bar);
        }

        return highs;
    }

    // The mean daily range as a share of the close over the given count of sessions before each bar, against
    // that of the same count before them, none where too few sessions stand before it or the older held none.
    public static double[] RangesBefore(SweepBar[] bars, int sessions)
    {
        var ratios = new double[bars.Length];
        var sums = new double[bars.Length + 1];
        var unread = new int[bars.Length + 1];

        for (var bar = 0; bar < bars.Length; bar++)
        {
            var close = Statistic.FromPrice(bars[bar].Close);
            var held = close > 0;

            sums[bar + 1] = sums[bar] + (held ? Statistic.FromPrice(bars[bar].High - bars[bar].Low) / close : 0);
            unread[bar + 1] = unread[bar] + (held ? 0 : 1);
        }

        for (var bar = 0; bar < bars.Length; bar++)
        {
            if (bar < sessions * 2 || unread[bar] - unread[bar - (sessions * 2)] > 0)
            {
                ratios[bar] = double.NaN;

                continue;
            }

            var newer = sums[bar] - sums[bar - sessions];
            var older = sums[bar - sessions] - sums[bar - (sessions * 2)];

            ratios[bar] = older > 0 ? newer / older : double.NaN;
        }

        return ratios;
    }
}
