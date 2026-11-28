using EquityBrief.Core.Sweep;

namespace EquityBrief.Core.History;

// One session's close held against a second source's: the store's, the source's at the store's scale, and whether the two
// agree within 1%.
public sealed record CloseHeld(DateOnly Session, decimal Stored, decimal Source, bool Agrees);

// The store's closes held against a second source's: the one ratio the source's adjusted closes are brought to the store's
// scale by, the median of the sessions both hold, and each session held at it.
public sealed record Audit(decimal Ratio, IReadOnlyList<CloseHeld> Held)
{
    public IEnumerable<CloseHeld> Apart => Held.Where(held => !held.Agrees);
}

// The check's settling of a stock's prices by a second source, the wild prices it reads, and the draw of stock-sessions it
// holds to Tiingo, each by code, no model and no figure a source did not send.
// see: A difference of 1% or more the check finds is settled by a second source's closes where one agrees with a side, and otherwise its stock-session is marked unreliable and skipped by every reader of the table
public static class PriceAudit
{
    // How far a close may stand from a source's at the store's scale and still agree: 1%.
    public const decimal Agree = 0.01m;

    // A wild close moves more than 5 times its typical daily move, the mean of its 20 moves before, and the next close takes
    // back more than half of it.
    public const decimal WildMultiple = 5m;

    public const int TypicalSessions = 20;

    public const decimal Reversed = 0.5m;

    // The sessions on each side of a session a source's closes are held over to set the ratio.
    public const int AroundSessions = 20;

    // The store's closes on the sessions given held to a source's: the ratio the median of the store's close over the
    // source's adjusted close on every session both hold, each session the store holds and the source does not left out.
    // With `ratio` given, as a stretch is held at the ratio of the sessions around it, the sessions are held at that ratio.
    public static Audit? Hold(IReadOnlyList<SweepBar> stored, IReadOnlyList<SourceBar> source, IReadOnlyCollection<DateOnly> sessions, decimal? at = null)
    {
        var theirs = source.GroupBy(bar => bar.Session).ToDictionary(group => group.Key, group => group.First().Adjusted ?? group.First().Close);
        var both = stored.Where(bar => sessions.Contains(bar.Session) && bar.Close > 0 && theirs.TryGetValue(bar.Session, out var close) && close > 0).ToArray();

        if (both.Length == 0)
        {
            return null;
        }

        var ratio = at ?? Median([.. both.Select(bar => bar.Close / theirs[bar.Session])]);

        return new Audit(ratio, [.. both.Select(bar =>
        {
            var scaled = theirs[bar.Session] * ratio;

            return new CloseHeld(bar.Session, bar.Close, decimal.Round(scaled, 4), Math.Abs(bar.Close / scaled - 1m) < Agree);
        })]);
    }

    // The sessions around one, up to twenty a side and the session itself, as the series holds them.
    public static IReadOnlyList<DateOnly> Around(IReadOnlyList<SweepBar> series, DateOnly session)
    {
        var days = series.Select(bar => bar.Session).ToArray();
        var at = Array.BinarySearch(days, session);

        if (at < 0)
        {
            at = ~at;
        }

        return days[Math.Max(0, at - AroundSessions)..Math.Min(days.Length, at + AroundSessions + 1)];
    }

    // Each wild close of a series: a move more than five times the mean of the twenty moves before it, the next move taking
    // back more than half of it in the other direction.
    public static IReadOnlyList<DateOnly> Wild(IReadOnlyList<SweepBar> series)
    {
        var wild = new List<DateOnly>();

        for (var at = TypicalSessions + 1; at + 1 < series.Count; at++)
        {
            if (series[at - 1].Close <= 0 || series[at].Close <= 0)
            {
                continue;
            }

            var typical = 0m;
            var held = true;

            for (var back = at - TypicalSessions; back < at; back++)
            {
                if (series[back - 1].Close <= 0)
                {
                    held = false;

                    break;
                }

                typical += Math.Abs(series[back].Close / series[back - 1].Close - 1m);
            }

            if (!held || typical == 0)
            {
                continue;
            }

            typical /= TypicalSessions;

            var move = series[at].Close / series[at - 1].Close - 1m;
            var next = series[at + 1].Close / series[at].Close - 1m;

            if (Math.Abs(move) > WildMultiple * typical && Math.Sign(next) == -Math.Sign(move) && Math.Abs(next) > Reversed * Math.Abs(move) / (1m + move))
            {
                wild.Add(series[at].Session);
            }
        }

        return wild;
    }

    // A seeded draw of stock-sessions across the population, at most the codes given: the codes ordered by a hash of the seed
    // and the code and the first taken, then the sessions spread across them in turn, each code's ordered by the same hash.
    public static IReadOnlyList<(string Index, string Ticker, DateOnly Session)> Draw(
        IReadOnlyList<(string Index, string Ticker, DateOnly Session)> population,
        int count,
        int codes,
        string seed)
    {
        var byCode = population
            .GroupBy(one => one.Ticker, StringComparer.Ordinal)
            .OrderBy(group => Hash(seed + "|" + group.Key), StringComparer.Ordinal)
            .Take(codes)
            .Select(group => new Queue<(string, string, DateOnly)>(group.OrderBy(one => Hash(FormattableString.Invariant($"{seed}|{one.Ticker}|{one.Index}|{one.Session:yyyy-MM-dd}")), StringComparer.Ordinal)))
            .ToList();
        var drawn = new List<(string, string, DateOnly)>();

        while (drawn.Count < count && byCode.Any(queue => queue.Count > 0))
        {
            foreach (var queue in byCode.Where(queue => queue.Count > 0))
            {
                if (drawn.Count == count)
                {
                    break;
                }

                drawn.Add(queue.Dequeue());
            }
        }

        return drawn;
    }

    // The sessions a code's long prices part from the newest pull's on, put at one scale on the newest both hold, as the long
    // history's class reads them: more than 1% apart.
    public static IReadOnlyList<DateOnly> Parted(IReadOnlyList<SweepBar> own, IReadOnlyList<SweepBar> pulled)
    {
        var theirs = pulled.Where(bar => bar.Close > 0).GroupBy(bar => bar.Session).ToDictionary(group => group.Key, group => group.First().Close);
        var both = own.Where(bar => bar.Close > 0 && theirs.ContainsKey(bar.Session)).OrderBy(bar => bar.Session).ToArray();

        if (both.Length < 2)
        {
            return [];
        }

        var scale = both[^1].Close / theirs[both[^1].Session];

        return [.. both.Where(bar => Math.Abs(bar.Close / theirs[bar.Session] / scale - 1m) > LongPulled.ApartBy).Select(bar => bar.Session)];
    }

    // The period a session falls in, as the operator asked them read.
    public static string Period(DateOnly session) => session.Year switch
    {
        <= 2012 => "2005 to 2012",
        <= 2018 => "2013 to 2018",
        _ => "2019 on",
    };

    static string Hash(string text) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(text)));

    static decimal Median(IReadOnlyList<decimal> values)
    {
        var sorted = values.Order().ToArray();

        return sorted.Length % 2 == 1 ? sorted[sorted.Length / 2] : (sorted[(sorted.Length / 2) - 1] + sorted[sorted.Length / 2]) / 2m;
    }
}
