using EquityBrief.Core.Prices;
using EquityBrief.Core.Providers;

namespace EquityBrief.Core.Bars;

// One pulled session as the store holds it, the four prices one adjusted set.
public sealed record PulledSession(DateOnly Session, decimal Open, decimal High, decimal Low, decimal Close, decimal RawClose, long Volume);

// A split the provider records that its own adjusted closes still carry as a jump, and the sessions before it brought to
// the provider's split: each price before the split's first session on the new basis times its old shares over its new, the
// raw close and the volume as sent. The data stays the provider's: the ratio is its own record of the split.
// see: A split the provider records and its own adjusted closes do not apply is applied to a new pull of the stock's history
public static class SplitCarry
{
    // How close a jump has to come to a split's ratio to be that split: within 15% of the price the ratio gives.
    public const decimal Tolerance = 0.15m;

    public static bool Fits(decimal closeOverClose, decimal newShares, decimal oldShares) =>
        newShares > 0 && oldShares > 0 && Math.Abs((closeOverClose * newShares / oldShares) - 1) < Tolerance;

    public static IReadOnlyList<SplitAnswer> NotCarried(IReadOnlyList<PulledSession> sessions, IReadOnlyList<SplitAnswer> splits)
    {
        var ordered = sessions.OrderBy(session => session.Session).ToArray();
        var left = new List<SplitAnswer>();

        foreach (var split in splits)
        {
            var at = Array.FindIndex(ordered, session => session.Session >= split.ExDate);

            // A plain ratio close to one asks nothing of the closes, since an ordinary day's move would fit it.
            if (at > 0 && ordered[at - 1].Close > 0 && Math.Abs((split.OldShares / split.NewShares) - 1) >= 0.25m && Fits(ordered[at].Close / ordered[at - 1].Close, split.NewShares, split.OldShares))
            {
                left.Add(split);
            }
        }

        return left;
    }

    public static IReadOnlyList<PulledSession> Carried(IReadOnlyList<PulledSession> sessions, IReadOnlyList<SplitAnswer> splits) =>
        [.. sessions.Select(session =>
        {
            var factor = splits.Where(split => session.Session < split.ExDate).Aggregate(1m, (held, split) => held * split.OldShares / split.NewShares);

            return factor == 1m
                ? session
                : session with
                {
                    Open = decimal.Round(session.Open * factor, Statistic.Places),
                    High = decimal.Round(session.High * factor, Statistic.Places),
                    Low = decimal.Round(session.Low * factor, Statistic.Places),
                    Close = decimal.Round(session.Close * factor, Statistic.Places),
                };
        })];
}
