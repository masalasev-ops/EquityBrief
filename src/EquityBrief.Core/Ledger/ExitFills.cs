using EquityBrief.Core.Loop;

namespace EquityBrief.Core.Ledger;

// A swing trade's sale: whether it was sold at an open rather than at a close, the bar of the session it was sold on,
// and its result sold there, none while that session's open is not held yet.
public sealed record ExitSale(bool AtOpen, int Bar, double? Result);

// Every swing trade sold on a close's signal is sold at the open of the session after that close, as every entry is
// bought at the open after its night: a close through the stop, at the target or under the trail, and the half a menu's
// half exit sells at the first close reaching its level. The cap's close and a time exit's sell at that close as before.
// Each sale moves the result by the gap from its close to that open over the risk the buy's close set, a half's by half
// of it, so a trade's walk, its sessions and its exits stay where the plan put them. A trade whose sale's open is not
// held yet waits and has no result until it is. The benchmark stays as the entry's fill priced it, each member sold at
// its own closes.
// see: A swing trade sold on a close's signal is sold at the next session's open by the fill step, and no rule's version moves because of it
public static class ExitFills
{
    // Whether an end is sold at the next session's open rather than at its own close.
    public static bool AtNextOpen(string end) => end is SetupEnds.Stop or SetupEnds.Target or SetupEnds.Trail;

    // How a fixed or trailing plan's walk ended after a number of sessions, read off its closes as the replay reads them:
    // an end before the cap's session is the stop's, the target's or the trail's, since nothing else ends a trade early;
    // on the cap's session the target where the close stands at or over it, the stop where under it, the trail where
    // under the trail the closes before set, and the cap otherwise.
    public static string EndOf(Func<int, double> close, int from, double entry, double stop, double? target, double? trail, int sessions, int cap)
    {
        var at = close(from + sessions);

        if (target is { } aim && at >= aim)
        {
            return SetupEnds.Target;
        }

        if (at < stop)
        {
            return SetupEnds.Stop;
        }

        if (sessions < cap)
        {
            return trail is > 0 ? SetupEnds.Trail : target is not null && at >= entry ? SetupEnds.Target : SetupEnds.Stop;
        }

        if (trail is { } distance && distance > 0)
        {
            var floor = stop;

            for (var session = 1; session < sessions; session++)
            {
                floor = Math.Max(floor, close(from + session) - distance);
            }

            if (at < floor)
            {
                return SetupEnds.Trail;
            }
        }

        return SetupEnds.Cap;
    }

    // The session a menu's half exit sold its first half on, counted from the buy: the first close reaching its level
    // before the session the rest ended on, or on that session where the cap ended the rest, since the half is read before
    // the cap's close; none for any other exit and where no close reached it.
    public static int? HalfSoldOn(Func<int, double> close, int from, SetupAnchor anchor, ExitChoice? exit, SetupOutcome outcome)
    {
        if (exit is not { Kind: ExitKind.HalfTarget or ExitKind.HalfBreakEven } choice || outcome.Result is null)
        {
            return null;
        }

        var level = choice.Kind == ExitKind.HalfBreakEven
            ? anchor.Entry + (choice.K * anchor.Risk)
            : anchor.Target ?? anchor.Entry + (ExitMenu.HalfTargetWhereTheTrailHasNone * anchor.Risk);
        var last = outcome.End == SetupEnds.Cap ? outcome.Sessions : outcome.Sessions - 1;

        for (var session = 1; session <= last; session++)
        {
            if (close(from + session) >= level)
            {
                return session;
            }
        }

        return null;
    }

    // A path's result with each sale a close signalled moved to the next session's open, over the risk the buy's close
    // set: the end's sale where its end calls for one, and where a half exit sold its first half that half at half
    // weight, the rest at the other half. `openAfter` is the open of the session after a bar, none where it is not held.
    // A path still open stays open, and one whose open is not held yet waits with no result.
    public static ExitSale Sold(SetupOutcome outcome, Func<int, double> close, Func<int, double?> openAfter, int from, double risk, int? halfOn = null)
    {
        var end = from + outcome.Sessions;

        if (outcome.Result is not { } result || !(risk > 0))
        {
            return new ExitSale(false, end, null);
        }

        var weight = halfOn is null ? 1.0 : 0.5;
        var moved = result;

        if (halfOn is { } half)
        {
            if (openAfter(from + half) is not { } open)
            {
                return new ExitSale(AtNextOpen(outcome.End), AtNextOpen(outcome.End) ? end + 1 : end, null);
            }

            moved += weight * (open - close(from + half)) / risk;
        }

        if (!AtNextOpen(outcome.End))
        {
            return new ExitSale(false, end, moved);
        }

        return openAfter(end) is { } sale
            ? new ExitSale(true, end + 1, moved + (weight * (sale - close(end)) / risk))
            : new ExitSale(true, end + 1, null);
    }

    // What the benchmark would read were its members sold as the trade is, which no rule, record or page reads: the same
    // plan entered on every member that session, each bought at its own next open and sold at its own open after the close
    // that ended it, the mean over the members whose path ended with both opens held; none where no member's did. 21.14's
    // exits check measures it beside the benchmark the fill step keeps, for the operator.
    public static double? Benchmark(IReadOnlyList<(double[] Closes, double[] Opens, int Bar, double Move, bool Stopped)> members, DateOnly session, SetupPlan plan)
    {
        var sum = 0.0;
        var count = 0;

        foreach (var (closes, opens, bar, move, _) in members)
        {
            if (bar < 0 || bar + 1 >= closes.Length || bar + 1 >= opens.Length || !(opens[bar + 1] > 0) || plan.On(session, closes[bar], move) is not { } anchor)
            {
                continue;
            }

            var outcome = SetupReplay.Replay(closes, bar, anchor);
            var bought = TradeFills.AtOpen(outcome.Result, closes[bar], anchor.Risk, opens[bar + 1]);
            var sold = Sold(outcome with { Result = bought }, at => closes[at], at => at + 1 < closes.Length && at + 1 < opens.Length && opens[at + 1] > 0 ? opens[at + 1] : null, bar, anchor.Risk).Result;

            if (sold is { } result)
            {
                sum += result;
                count++;
            }
        }

        return count > 0 ? sum / count : null;
    }
}
