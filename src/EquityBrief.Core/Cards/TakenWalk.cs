namespace EquityBrief.Core.Cards;

// How a taken trade stands over the closes from its fill's session on, under its rule's own management and no other:
// sold at the first close under its stop, a close equal to the stop leaving it open; at the first close at or above its
// target; a trailing rule's stop raised after each close to that close less the plan's distance and never lowered; and
// at its cap's close, the fill's own session counted as the first. Every price is read at one scale, so a split inside
// the trade moves the closes, the fill and the plan together and ends nothing.
// see: A taken trade's fill is the next session's open once its bar is stored, and the plan's buy marked provisional until then
public static class TakenWalk
{
    public const string Stopped = "stop";
    public const string Targeted = "target";
    public const string Capped = "cap";
    public const string Sold = "sold";
    public const string Exited = "exit";
    public const string Left = "left";

    // The operator's record of a family on an index draws its average once this many of its trades have ended.
    // see: The operator's own record states its average result once twenty of its trades in a family and index have ended
    public const int RecordMinimum = 20;

    // The session the trade ended on, counted from the fill's session as the first, and why; none while it stands.
    public static (int Session, string Reason)? Follow(IReadOnlyList<decimal> closes, decimal? stop, decimal? target, decimal? trail, int? cap)
    {
        var floor = stop;

        for (var session = 1; session <= closes.Count; session++)
        {
            var close = closes[session - 1];

            if (floor is { } under && close < under)
            {
                return (session, Stopped);
            }

            if (target is { } goal && close >= goal)
            {
                return (session, Targeted);
            }

            if (trail is { } distance && floor is { } raised)
            {
                floor = Math.Max(raised, close - distance);
            }

            if (cap is { } most && session >= most)
            {
                return (session, Capped);
            }
        }

        return null;
    }

    // The trade's risk from its fill: the fill less its stop, or the plan's own distance, its buy less its stop, where
    // the fill sits at or under the stop; none for a rule setting no stop.
    public static decimal? Risk(decimal fill, decimal? stop, decimal? buy)
    {
        if (stop is not { } floor)
        {
            return null;
        }

        if (fill > floor)
        {
            return fill - floor;
        }

        return buy is { } planned && planned > floor ? planned - floor : null;
    }
}
