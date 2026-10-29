namespace EquityBrief.Core.Ledger;

// One settled swing setup's path under three exits, each bought at the open after its session as 19.10 prices every
// entry and each stated in the risk its plan put up at the listing's close: as the ledger stores it, sold at the close
// that triggered its stop, target or trail; sold at the open of the session after that close; and with the target as a
// resting order filled at the target on the first session whose high reaches it, the stop still a close and sold at the
// next open. The time limit ends a trade at its own close under every pricing.
public sealed record ExitPriced(SetupOutcome AtClose, SetupOutcome AtNextOpen, SetupOutcome Resting);

// 21.3's read-only pricing of exits. Nothing here writes anything, and no rule reads it: the night's exits stay where
// the plan's own walk puts them unless a fill step of their own is built on the operator's word.
// see: A read-only check changes no rule, and an exit's pricing changes only by a fill step on the operator's word
public static class ExitPricings
{
    // The three pricings over closes, opens and highs oldest first, the anchor's session at `from`, the buy at the open
    // after it; none where that open is not held or the anchor places no trade. A path the series ends before any exit
    // is open under every pricing, its result none.
    public static ExitPriced? Price(ReadOnlySpan<double> closes, ReadOnlySpan<double> opens, ReadOnlySpan<double> highs, int from, SetupAnchor anchor, double? open)
    {
        if (open is not { } fill || !(fill > 0))
        {
            return null;
        }

        var stored = NextOpenCheck.Replay(closes, from, anchor, fill);

        if (stored is null || stored.End == SetupEnds.None)
        {
            return null;
        }

        return new ExitPriced(stored, AtNextOpen(closes, opens, from, anchor, fill, stored), RestingTarget(closes, opens, highs, from, anchor, fill));
    }

    // The stored path sold at the open of the session after the close that triggered its stop, target or trail, where
    // that open is held, and at the triggering close where the series holds no session after it; the cap's close and
    // an open path stand as stored.
    static SetupOutcome AtNextOpen(ReadOnlySpan<double> closes, ReadOnlySpan<double> opens, int from, SetupAnchor anchor, double fill, SetupOutcome stored)
    {
        if (stored.Result is null || stored.End is SetupEnds.Cap or SetupEnds.Open)
        {
            return stored;
        }

        var triggered = from + stored.Sessions;
        var sale = triggered + 1 < opens.Length && opens[triggered + 1] > 0 ? opens[triggered + 1] : closes[triggered];
        var sessions = triggered + 1 < opens.Length && opens[triggered + 1] > 0 ? stored.Sessions + 1 : stored.Sessions;

        return new SetupOutcome((sale - fill) / anchor.Risk, sessions, stored.End);
    }

    // The target as a resting order: the first session whose high reaches it sells there, before its close is read, so a
    // session reaching the target and closing under the stop sells at the target; a close under the stop, or under the
    // trail the earlier closes set, sells at the next session's open where held and at that close otherwise; the cap's
    // close ends a trade neither reached. A plan whose stop trails names no target and rests no order.
    static SetupOutcome RestingTarget(ReadOnlySpan<double> closes, ReadOnlySpan<double> opens, ReadOnlySpan<double> highs, int from, SetupAnchor anchor, double fill)
    {
        var floor = anchor.Stop;

        for (var session = 1; session <= anchor.Cap; session++)
        {
            var at = from + session;

            if (at >= closes.Length)
            {
                return new SetupOutcome(null, Math.Max(0, Math.Min(anchor.Cap, closes.Length - 1 - from)), SetupEnds.Open);
            }

            if (!anchor.Trails && at < highs.Length && highs[at] >= anchor.Target!.Value)
            {
                return new SetupOutcome((anchor.Target.Value - fill) / anchor.Risk, session, SetupEnds.Target);
            }

            var close = closes[at];

            if (close < floor)
            {
                var held = at + 1 < opens.Length && opens[at + 1] > 0;
                var sale = held ? opens[at + 1] : close;

                return new SetupOutcome((sale - fill) / anchor.Risk, held ? session + 1 : session, anchor.Trails && floor > anchor.Stop ? SetupEnds.Trail : SetupEnds.Stop);
            }

            if (anchor.Trails)
            {
                floor = Math.Max(floor, close - anchor.Trail!.Value);
            }

            if (session == anchor.Cap)
            {
                return new SetupOutcome((close - fill) / anchor.Risk, session, SetupEnds.Cap);
            }
        }

        return new SetupOutcome(null, 0, SetupEnds.Open);
    }
}
