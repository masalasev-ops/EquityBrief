using System.Collections.Concurrent;
using EquityBrief.Core.Ledger;
using EquityBrief.Core.Prices;
using EquityBrief.Core.Sweep;
using EquityBrief.Worker.Sweep;

namespace EquityBrief.Worker.Ledger;

// The members' places on each session on the values Kakushadze's formulas rank across the index, and the four formulas
// that read them. On a session the members are those holding a bar there with no gap, as the night reads its members,
// and each one's place is the share of them under its value and half the share level with it, its own among them. A
// session's places are worked once, on first asking, from the bars to that session alone, so a history cut at a session
// reads the places the whole history read there.
// see: The published price and volume factors are ledger readings for the engines to search, and no factor chooses a stock
public sealed class FactorRanks(IReadOnlyList<SweepSeries> series, SweepBenchmark.Members members)
{
    // The values ranked: Alpha#1's Ts_ArgMax, Alpha#2's change in the logarithm of volume and its intraday return, Alpha#3's
    // open and volume, and Alpha#4's low.
    enum Ranked
    {
        ArgMax,
        VolumeChange,
        Intraday,
        Open,
        Volume,
        Low,
    }

    const int Values = 6;

    readonly ConcurrentDictionary<int, Lazy<double?[][]>> sessions = new();

    // Alpha#1, (rank(Ts_ArgMax(SignedPower(((returns < 0) ? stddev(returns, 20) : close), 2.), 5)) - 0.5).
    public double? Alpha1(int name, int bar) =>
        PlaceOn(Ranked.ArgMax, series[name].SessionAt[bar], name) is { } place ? place - 0.5 : null;

    // Alpha#2, (-1 * correlation(rank(delta(log(volume), 2)), rank(((close - open) / open)), 6)).
    public double? Alpha2(int name, int bar) => Correlated(name, bar, FactorReadings.Alpha2Sessions, Ranked.VolumeChange, Ranked.Intraday);

    // Alpha#3, (-1 * correlation(rank(open), rank(volume), 10)).
    public double? Alpha3(int name, int bar) => Correlated(name, bar, FactorReadings.Alpha3Sessions, Ranked.Open, Ranked.Volume);

    // Alpha#4, (-1 * Ts_Rank(rank(low), 9)).
    public double? Alpha4(int name, int bar) =>
        Placed(name, bar, FactorReadings.Alpha4Sessions, Ranked.Low) is { } lows ? -FactorReadings.TimeRank(lows) : null;

    double? Correlated(int name, int bar, int count, Ranked one, Ranked other) =>
        Placed(name, bar, count, one) is { } first
        && Placed(name, bar, count, other) is { } second
        && FactorReadings.Correlation(first, second) is { } correlation
            ? -correlation
            : null;

    // The member's places on one value over its bars to the bar, each on its own bar's session, none where a bar's place
    // is not held.
    double[]? Placed(int name, int bar, int count, Ranked value)
    {
        if (bar - count + 1 < 0)
        {
            return null;
        }

        var one = series[name];
        var placed = new double[count];

        for (var at = 0; at < count; at++)
        {
            if (PlaceOn(value, one.SessionAt[bar - count + 1 + at], name) is not { } place)
            {
                return null;
            }

            placed[at] = place;
        }

        return placed;
    }

    // A member's place on a session on one value, none where it is no member there or holds no value.
    double? PlaceOn(Ranked value, int session, int name)
    {
        if (session < 0 || session >= members.Names.Length)
        {
            return null;
        }

        var held = Array.BinarySearch(members.Names[session], name);

        return held < 0 ? null : sessions.GetOrAdd(session, at => new Lazy<double?[][]>(() => Work(at))).Value[(int)value][held];
    }

    double?[][] Work(int session)
    {
        var names = members.Names[session];
        var bars = members.Bars[session];
        var places = new double?[Values][];

        for (var value = 0; value < Values; value++)
        {
            var figures = new double?[names.Length];

            for (var at = 0; at < names.Length; at++)
            {
                figures[at] = Figure((Ranked)value, series[names[at]].Bars, bars[at]);
            }

            var held = figures.OfType<double>().ToArray();
            var placed = new double?[names.Length];

            for (var at = 0; at < names.Length; at++)
            {
                placed[at] = figures[at] is { } one ? FactorReadings.Place(one, held) : null;
            }

            places[value] = placed;
        }

        return places;
    }

    static double? Figure(Ranked value, IReadOnlyList<SweepBar> bars, int bar) => value switch
    {
        Ranked.ArgMax => FactorReadings.ArgMaxValue(bars, bar),
        Ranked.VolumeChange => FactorReadings.VolumeChange(bars, bar),
        Ranked.Intraday => FactorReadings.IntradayReturn(bars, bar),
        Ranked.Open => bars[bar].Open > 0 ? Statistic.FromPrice(bars[bar].Open) : null,
        Ranked.Volume => Statistic.FromVolume(bars[bar].Volume),
        _ => bars[bar].Low > 0 ? Statistic.FromPrice(bars[bar].Low) : null,
    };
}
