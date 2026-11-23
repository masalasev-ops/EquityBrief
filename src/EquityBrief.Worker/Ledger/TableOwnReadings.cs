using EquityBrief.Core.History;
using EquityBrief.Core.Ledger;
using EquityBrief.Worker.Sweep;

namespace EquityBrief.Worker.Ledger;

// The seven readings the every-session table reads its own way over a member's readings as the night works them, which its
// build writes and its check rebuilds alike: the third and fourth alphas off the members' places at the traded price, the two
// liquidity readings off the dollars traded, and the three reach readings over the fixed length of history.
// see: The every-session table reads four readings at the traded price and three over a fixed length of history, the night's own
public static class TableOwnReadings
{
    public static void Apply(double?[] readings, TradedRanks traded, int name, SweepSeries one, int bar)
    {
        void Set(string column, double? value) => readings[LedgerReadings.IndexOf(column)] = value;

        Set("alpha_three", traded.Alpha3(name, bar));
        Set("alpha_four", traded.Alpha4(name, bar));
        Set("liquidity", TableReadings.Liquidity(one.Bars, bar));
        Set("illiquidity", TableReadings.Illiquidity(one.Bars, bar, FactorReadings.MonthSessions));

        var held = one.Bars.Select(each => new Core.EntryCheck.EntryBar(each.Session, each.High, each.Low, each.Close, each.Volume)).ToArray();
        var start = one.WindowStart[bar];
        var older = TableReadings.OlderFrom(start, bar);
        var entry = Core.EntryCheck.EntryReadings.Of(
            one.Name.Ticker,
            new ArraySegment<Core.EntryCheck.EntryBar>(held, start, bar + 1 - start),
            new ArraySegment<Core.EntryCheck.EntryBar>(held, older, start - older),
            double.IsFinite(one.Atr[bar]) ? one.Atr[bar] : null,
            double.IsFinite(one.Rsi[bar]) ? one.Rsi[bar] : null,
            double.IsFinite(one.Volume50[bar]) ? one.Volume50[bar] : null);

        Set("stretch_share", entry?.StretchShare);
        Set("highest_since_years", entry is { } read && (read.HighestSince ?? read.HistoryStart) is { } matched ? (read.Session.DayNumber - matched.DayNumber) / Core.EntryCheck.EntryMeasure.YearDays : null);
        Set("older_days", entry?.OlderDays);

        // see: A bar whose close moved with no volume traded with its volume missing
        if (LongTraded.VolumeMissing(one.Bars, bar))
        {
            foreach (var column in TableReadings.VolumeColumns)
            {
                Set(column, null);
            }
        }
    }
}

// The third and fourth published alphas over the members' places at the price each traded at that day, as the ledger's own
// ranks place them at the stored price: on a session the members are those holding a bar there with no gap, each one's place
// the share of them under its value and half the share level with it, worked once a session from the bars to it alone. A
// member whose day holds no raw close holds no place there. The ledger's own ranks stay as the readings' version pins them.
// see: The every-session table reads four readings at the traded price and three over a fixed length of history, the night's own
public sealed class TradedRanks(IReadOnlyList<SweepSeries> series, SweepBenchmark.Members members)
{
    readonly System.Collections.Concurrent.ConcurrentDictionary<int, Lazy<double?[][]>> sessions = new();

    // Alpha#3, (-1 * correlation(rank(open), rank(volume), 10)), the open as traded.
    public double? Alpha3(int name, int bar) =>
        Placed(name, bar, FactorReadings.Alpha3Sessions, 0) is { } opens
        && Placed(name, bar, FactorReadings.Alpha3Sessions, 1) is { } volumes
        && FactorReadings.Correlation(opens, volumes) is { } correlation
            ? -correlation
            : null;

    // Alpha#4, (-1 * Ts_Rank(rank(low), 9)), the low as traded.
    public double? Alpha4(int name, int bar) =>
        Placed(name, bar, FactorReadings.Alpha4Sessions, 2) is { } lows ? -FactorReadings.TimeRank(lows) : null;

    double[]? Placed(int name, int bar, int count, int value)
    {
        if (bar - count + 1 < 0)
        {
            return null;
        }

        var one = series[name];
        var placed = new double[count];

        for (var at = 0; at < count; at++)
        {
            var session = one.SessionAt[bar - count + 1 + at];

            if (session < 0 || session >= members.Names.Length)
            {
                return null;
            }

            var held = Array.BinarySearch(members.Names[session], name);

            if (held < 0 || sessions.GetOrAdd(session, day => new Lazy<double?[][]>(() => Work(day))).Value[value][held] is not { } place)
            {
                return null;
            }

            placed[at] = place;
        }

        return placed;
    }

    // A session's places on the traded open, the volume and the traded low.
    double?[][] Work(int session)
    {
        var names = members.Names[session];
        var bars = members.Bars[session];
        var places = new double?[3][];

        for (var value = 0; value < 3; value++)
        {
            var figures = new double?[names.Length];

            for (var at = 0; at < names.Length; at++)
            {
                var bar = series[names[at]].Bars[bars[at]];

                figures[at] = value switch
                {
                    0 => bar.Open > 0 && TableReadings.Traded(bar, bar.Open) is { } open ? Core.Prices.Statistic.FromPrice(open) : null,
                    1 => Core.Prices.Statistic.FromVolume(bar.Volume),
                    _ => bar.Low > 0 && TableReadings.Traded(bar, bar.Low) is { } low ? Core.Prices.Statistic.FromPrice(low) : null,
                };
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
}
