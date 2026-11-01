namespace EquityBrief.Core.Ledger;

// From 21.18, on the operator's ruling of 2026-11-01, a swing trade's target as a sell order standing from its entry: on a
// session after the entry's that opens at or above the target the trade is sold at that open, and on any session whose
// high reaches the target, the entry's own among them, at the target, whatever that session's close does; the entry's
// own open never sells it. A close under the stop, or under the trail the earlier closes set, is sold at the next
// session's open as every swing trade is, and the cap's close ends a trade neither reached. A plan whose stop trails
// names no target and stands no order. Nothing a rule reads calls it: 21.18's read-only print alone.
public static class StandingTarget
{
    // The path over closes, opens and highs oldest first, the anchor's session at `from` and the buy at `fill`, the open
    // of the session after it, each result in the risk the plan put up at the listing's close; none where the anchor
    // places no trade, and open where the series ends before any exit or a stop's sale's open is not held yet.
    public static SetupOutcome? Sold(ReadOnlySpan<double> closes, ReadOnlySpan<double> opens, ReadOnlySpan<double> highs, int from, SetupAnchor anchor, double fill)
    {
        if (!(anchor.Risk > 0) || !(fill > 0) || (anchor.Trails ? !(anchor.Trail > 0) : !(anchor.Target > anchor.Entry)))
        {
            return null;
        }

        var floor = anchor.Stop;

        for (var session = 1; session <= anchor.Cap; session++)
        {
            var at = from + session;

            if (at >= closes.Length)
            {
                return new SetupOutcome(null, Math.Max(0, Math.Min(anchor.Cap, closes.Length - 1 - from)), SetupEnds.Open);
            }

            if (anchor.Target is { } target)
            {
                if (session > 1 && at < opens.Length && opens[at] >= target)
                {
                    return new SetupOutcome((opens[at] - fill) / anchor.Risk, session, SetupEnds.Target);
                }

                if (at < highs.Length && highs[at] >= target)
                {
                    return new SetupOutcome((target - fill) / anchor.Risk, session, SetupEnds.Target);
                }
            }

            var close = closes[at];

            if (close < floor)
            {
                var end = anchor.Trails && floor > anchor.Stop ? SetupEnds.Trail : SetupEnds.Stop;

                return at + 1 < opens.Length && opens[at + 1] > 0
                    ? new SetupOutcome((opens[at + 1] - fill) / anchor.Risk, session, end)
                    : new SetupOutcome(null, session, SetupEnds.Open);
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

    // The same plan on every member the index held that session, each bought at its own next open and its target standing
    // as the trade's does, the mean over the members whose path ended; none where no member's did.
    public static double? Benchmark(IReadOnlyList<(double[] Closes, double[] Opens, double[] Highs, int Bar, double Move)> members, DateOnly session, SetupPlan plan)
    {
        var (sum, count) = (0.0, 0);

        foreach (var (closes, opens, highs, bar, move) in members)
        {
            if (bar < 0 || bar + 1 >= closes.Length || bar + 1 >= opens.Length || !(opens[bar + 1] > 0) || plan.On(session, closes[bar], move) is not { } anchor)
            {
                continue;
            }

            if (Sold(closes, opens, highs, bar, anchor, opens[bar + 1]) is { Result: { } result })
            {
                sum += result;
                count++;
            }
        }

        return count > 0 ? sum / count : null;
    }
}
