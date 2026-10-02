using EquityBrief.Core.Families;
using EquityBrief.Core.Prices;
using EquityBrief.Core.Sweep;

namespace EquityBrief.Worker.Sweep;

// The earnings drift's readings on one member-session some setting of its grid could list: the newest print's
// reaction session and how many sessions back it sits, the surprise, the reaction's rise in typical moves of
// the session before it, its volume over its fifty-session average, tonight's close, the reaction session's
// low the stop sits at, and the lowest band far enough above the close to be a target.
public readonly record struct DriftReading(
    int Name,
    int Bar,
    int Session,
    int Back,
    double Surprise,
    double Reaction,
    double Volume,
    double Close,
    double Stop,
    double Band,
    double Move);

// The earnings drift's sweep: its rule's readings over the stored history and the pulled surprises, the
// listings each setting makes of them, its trade to the stop or the target, and the benchmark of the same
// plan entered on every member that night.
// see: A setup family's sweep replays its own rule over the stored history and proposes the best edge among the settings meeting its floors
public sealed class DriftSweep
{
    // The grid around the provisional setting the sweep proposed its freeze from.
    public static FamilyGrid Grid { get; } = new(
        [
            ("window", [3, DriftRule.ProvisionalWindowSessions, 10]),
            ("reaction", [0.5, DriftRule.ProvisionalReactionMoves, 1.5]),
            ("volume", [1.25, DriftRule.ProvisionalVolumeMultiple, 2.0]),
            ("target", [2.0, DriftRule.TargetRiskMultiple, 3.0]),
        ],
        [1, 1, 1, 1]);

    readonly IReadOnlyList<SweepSeries> series;
    readonly SweepBenchmark.Members members;
    readonly double[][] closes;
    readonly Dictionary<(int Session, double Stop, double Target), double> benchmarks = [];

    public DriftSweep(IReadOnlyList<SweepSeries> series, SweepBenchmark.Members members)
    {
        this.series = series;
        this.members = members;
        closes = [.. series.Select(one => one.Bars.Select(bar => Statistic.FromPrice(bar.Close)).ToArray())];
    }

    // Every member-session on or after the first scored session, held by the index with no gap, on a night the
    // market check left open, whose newest print reacted inside the widest window with a surprise above
    // nothing, a rise and a volume at least the loosest the grid reads, a close still above the reaction's low
    // and a stop below the close: the readings any setting's listings are drawn from.
    public IReadOnlyList<DriftReading> Readings(IReadOnlyList<SweepColumns.Session> sessions, int firstScored)
    {
        var floor = FamilySweep.MarketFloor;
        var widest = Grid.Dials[0].Levels.Max();
        var lowestRise = Grid.Dials[1].Levels.Min();
        var lowestVolume = Grid.Dials[2].Levels.Min();
        var found = new List<DriftReading>();

        for (var name = 0; name < series.Count; name++)
        {
            var one = series[name];

            for (var bar = 0; bar < one.Bars.Length; bar++)
            {
                var session = one.SessionAt[bar];

                if (session < firstScored || !one.Member[bar] || one.Gap[bar] || !(sessions[session].Breadth >= floor))
                {
                    continue;
                }

                var newest = one.NewestSurprise[bar];

                if (newest < 0)
                {
                    continue;
                }

                var reaction = one.SurpriseBar[newest];
                var back = bar - reaction;

                if (reaction < 1 || back < 0 || back >= widest)
                {
                    continue;
                }

                var surprise = one.Name.Surprises[newest].Percent;
                var before = one.Atr[reaction - 1];
                var rise = before > 0 ? (closes[name][reaction] - closes[name][reaction - 1]) / before : double.NaN;
                var volume = one.Volume50[reaction] > 0 ? Statistic.FromVolume(one.Bars[reaction].Volume) / one.Volume50[reaction] : double.NaN;
                var close = closes[name][bar];
                var low = Statistic.FromPrice(one.Bars[reaction].Low);

                if (!(surprise > 0) || !(rise >= lowestRise) || !(volume >= lowestVolume) || !(close > low) || !(low > 0))
                {
                    continue;
                }

                found.Add(new DriftReading(name, bar, session, back, surprise, rise, volume, close, low, BandAbove(one, bar, close), one.Atr[bar]));
            }
        }

        return found;
    }

    // The lowest band whose low edge sits the rule's typical moves or more above the close, none where none does
    // or the bar holds no typical move: the target where it is nearer than the multiple of the risk.
    static double BandAbove(SweepSeries one, int bar, double close)
    {
        var move = one.Atr[bar];

        if (!(move > 0))
        {
            return double.NaN;
        }

        var edges = SweepCandidates.LevelsOn(one, bar)
            .Select(level => Statistic.FromPrice(level.LowEdge))
            .Where(edge => edge > close && (edge - close) / move >= DriftRule.TargetBandMoves)
            .ToArray();

        return edges.Length > 0 ? edges.Min() : double.NaN;
    }

    // The listings one setting makes: a reaction inside the setting's window, on its rise and its volume, bought
    // at the close, stopped at the reaction's low and aimed at the nearer of the band and the setting's
    // multiple of the risk, in the order of the surprise.
    public static IEnumerable<FamilyListing> Listings(IReadOnlyList<DriftReading> readings, int[] setting)
    {
        var window = Grid.Value(setting, 0);
        var rise = Grid.Value(setting, 1);
        var volume = Grid.Value(setting, 2);
        var multiple = Grid.Value(setting, 3);

        foreach (var reading in readings)
        {
            if (!(reading.Back < window) || !(reading.Reaction >= rise) || !(reading.Volume >= volume))
            {
                continue;
            }

            var byRisk = reading.Close + (multiple * (reading.Close - reading.Stop));
            var target = !double.IsNaN(reading.Band) && reading.Band < byRisk ? reading.Band : byRisk;

            yield return new FamilyListing(reading.Name, reading.Bar, reading.Session, reading.Surprise, 0, reading.Close, reading.Stop, target, double.NaN, DriftRule.CapSessions, reading.Move);
        }
    }

    public (double? Result, int Sessions) Exit(FamilyListing listing)
    {
        var result = FamilyWalks.Fixed(closes[listing.Name], listing.Bar, listing.Entry, listing.Stop, listing.Target, listing.Cap, out var sessions);

        return (result, sessions);
    }

    // The same plan entered at the close on every member the index held that night with a bar and no gap, its
    // stop the trade's distance in each member's own typical moves and its target the trade's reward to risk.
    public double Benchmark(FamilyListing listing)
    {
        var move = series[listing.Name].Atr[listing.Bar];
        var risk = listing.Entry - listing.Stop;

        if (!(move > 0) || !(risk > 0))
        {
            return double.NaN;
        }

        var stopMoves = risk / move;
        var rewardToRisk = (listing.Target - listing.Entry) / risk;
        var key = (listing.Session, Math.Round(stopMoves, 6), Math.Round(rewardToRisk, 6));

        if (!benchmarks.TryGetValue(key, out var value))
        {
            value = BenchmarkOn(series, closes, members, listing.Session, stopMoves, rewardToRisk, listing.Cap);
            benchmarks[key] = value;
        }

        return value;
    }

    public static double BenchmarkOn(IReadOnlyList<SweepSeries> series, double[][] closes, SweepBenchmark.Members members, int session, double stopMoves, double rewardToRisk, int cap)
    {
        var names = members.Names[session];
        var bars = members.Bars[session];
        var sum = 0.0;
        var count = 0;

        for (var at = 0; at < names.Length; at++)
        {
            var name = names[at];
            var bar = bars[at];
            var entry = closes[name][bar];
            var risk = stopMoves * series[name].Atr[bar];

            if (!(risk > 0) || entry - risk <= 0)
            {
                continue;
            }

            if (FamilyWalks.Fixed(closes[name], bar, entry, entry - risk, entry + (rewardToRisk * risk), cap, out _) is { } result)
            {
                sum += result;
                count++;
            }
        }

        return count > 0 ? sum / count : double.NaN;
    }
}
