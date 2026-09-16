using EquityBrief.Core.Prices;
using System.Globalization;

namespace EquityBrief.Core.Returns;

// One session as the forward return arithmetic reads it.
public readonly record struct ReturnBar(DateOnly SessionDate, decimal Close);

// One horizon's outcome for one listing.
//
// `Outcome` is null while the horizon has not matured, which is what an
// immature row reads as. `unresolved` is a value rather than a null, so a setup
// that ran out of time is counted in its own column and never in a rate.
// see: An unresolved setup is never a win
public sealed record ForwardReturn(string Horizon, string? Outcome, DateOnly? ResolvedOn, double? ReturnPct);

// What happened five and twenty-one sessions after a listing, and whether a
// setup reached its target before its stop.
//
// A pure function of a session-ordered series and the plan as it stood that
// night, as every other stage's arithmetic is.
// see: Code owns every number
public static class ForwardReturnSeries
{
    // The two session horizons section 7 names, and the third that matters.
    public const string FiveSessions = "5";
    public const string TwentyOneSessions = "21";
    public const string Setup = "setup";

    public static readonly string[] Horizons = [FiveSessions, TwentyOneSessions, Setup];

    // Section 17's cap: a listed setup resolves when its target is reached, its
    // stop is closed through, or 63 sessions pass.
    public const int SetupSessionCap = 63;

    public const string Win = "win";
    public const string Loss = "loss";
    public const string Unresolved = "unresolved";

    // A setup whose price never came back to the entry zone. Its own value and its
    // own column, never a win and never in a rate: the target was reached at a
    // price the plan did not offer to buy at, so there was no trade to score.
    // see: A setup is scored from its entry, and a target reached before the entry is never a win
    public const string NeverEntered = "never entered";

    // The two session horizons, filled as those sessions mature.
    //
    // A horizon that has not matured has no outcome and no return, which is what
    // an immature row reads as: not yet matured rather than a blank or a zero.
    // The two are different statements and only one of them is true.
    public static ForwardReturn Over(
        int sessions,
        IReadOnlyList<ReturnBar> after,
        decimal listedAt)
    {
        var horizon = sessions.ToString(CultureInfo.InvariantCulture);

        if (after.Count < sessions || listedAt <= 0)
        {
            return new ForwardReturn(horizon, null, null, null);
        }

        var at = after[sessions - 1];
        var change = Statistic.FromRatio((at.Close - listedAt) / listedAt) * 100;

        // A win is a rise and a loss is a fall over a session horizon, which is
        // a different question from the setup's. It is the question the base
        // rate exists to put in proportion: most names are higher after a month
        // regardless.
        // see: Every forward-return figure is shown against the universe base rate
        return new ForwardReturn(horizon, change > 0 ? Win : Loss, at.SessionDate, change);
    }

    // The setup horizon: whether the plan's target was reached before its stop,
    // within the time cap, counting from the session the price first reached the
    // entry the plan named.
    //
    // A close through the stop is a loss and a close at or above the target is a
    // win, whichever comes first. Running out of sessions is `unresolved`, which
    // is a value rather than a null so it is counted in its own column: an
    // unresolved setup is never a win.
    //
    // The entry is what 8.1 added. From 5.5 until then this counted from the
    // listing, so a name that ran straight to its target from a price above the
    // entry zone scored a win for a purchase the plan did not offer: 105 of 147
    // stored wins in the operator's store on 2026-09-16. A setup starts on the
    // first close at or below the zone's top edge, and a target reached before
    // that close is `never entered`.
    //
    // Everything here is read on closes, as the stop and the target already were,
    // because a daily bar does not say what order a session's prices came in and a
    // fill read from a session's low would credit an order the store cannot show
    // was filled. The cap is counted from the listing night, since the plan being
    // scored is the one stored that night and it ages with it.
    // see: A setup is scored from its entry, and a target reached before the entry is never a win
    public static ForwardReturn OverSetup(
        IReadOnlyList<ReturnBar> after,
        decimal? stop,
        decimal? target,
        decimal? entryHigh = null,
        decimal? closeAtListing = null)
    {
        // A listing with no plan has no setup to resolve. That is an absence
        // rather than an unresolved setup, and the two are counted differently.
        if (stop is not { } floor || target is not { } ceiling)
        {
            return new ForwardReturn(Setup, null, null, null);
        }

        // The invariant the branch order below rests on, written down rather
        // than assumed, because 5.5's own mutation showed it cannot be observed:
        // the test is on the close, and one close cannot be both below the stop
        // and at or above the target while the stop is beneath the target. So
        // swapping the two branches changes nothing any series can show, which
        // is the unproducible shape, and the remedy for that class is the
        // invariant asserted where it holds rather than a stronger assertion at
        // the site.
        //
        // A plan whose stop sits at or above its target is not a plan, and a
        // setup scored against one would be scored against whichever branch ran
        // first. It refuses rather than resolving.
        if (floor >= ceiling)
        {
            throw new InvalidOperationException(
                $"The stored plan has its stop at {floor} and its target at {ceiling}. A stop at or " +
                "above the target is not a plan, and which of the two a session reached would be " +
                "decided by the order the rules are written in rather than by the series.");
        }

        // A plan with no entry zone is scored from the listing, as every setup was
        // until 8.1. Nothing the shortlist builder writes is such a plan, and the
        // arithmetic says what it does with one rather than refusing a row the
        // store could hold from before.
        decimal? entry = null;

        if (closeAtListing is { } listed)
        {
            entry = entryHigh is { } top
                ? listed <= top && listed >= floor ? listed : null
                : listed;
        }

        foreach (var bar in after.Take(SetupSessionCap))
        {
            // The stop is tested first, because a session that closed through
            // both is a session the position was stopped out of before it could
            // reach anything. A rule that took the target first would score a
            // gap through the stop as a win. It is tested before the entry for
            // the same reason: a close through the zone and the stop on one
            // session is a fill and a stop, not a setup that never happened.
            if (bar.Close < floor)
            {
                // A session that both entered and stopped has no entry close to
                // measure from: the fill was somewhere in the zone and the store
                // does not say where, so the outcome is a loss with no figure
                // rather than a figure of zero, which would read as a trade that
                // went nowhere.
                return new ForwardReturn(Setup, Loss, bar.SessionDate, entry is { } at ? ChangeFromEntry(at, bar.Close) : null);
            }

            if (entry is null)
            {
                // Not entered yet. A target reached from above the zone is the
                // move happening without the purchase the plan named.
                if (bar.Close >= ceiling)
                {
                    return new ForwardReturn(Setup, NeverEntered, bar.SessionDate, null);
                }

                if (entryHigh is { } zoneTop && bar.Close <= zoneTop)
                {
                    entry = bar.Close;
                }

                continue;
            }

            if (bar.Close >= ceiling)
            {
                return new ForwardReturn(Setup, Win, bar.SessionDate, ChangeFromEntry(entry.Value, bar.Close));
            }
        }

        // Still open inside the cap is not yet matured; past the cap it is
        // unresolved where the setup was entered and never entered where it was
        // not, which are three different statements about one row.
        if (after.Count < SetupSessionCap)
        {
            return new ForwardReturn(Setup, null, null, null);
        }

        var last = after[SetupSessionCap - 1];

        return entry is null
            ? new ForwardReturn(Setup, NeverEntered, last.SessionDate, null)
            : new ForwardReturn(Setup, Unresolved, last.SessionDate, ChangeFromEntry(entry.Value, last.Close));
    }

    // What the trade made, from the close it was entered at to the close it
    // resolved on. Null for a setup nobody entered, which has no trade to measure.
    static double? ChangeFromEntry(decimal entry, decimal at) =>
        entry <= 0 ? null : Statistic.FromRatio((at - entry) / entry) * 100;

    // The universe base rate for one horizon: the share of every name-night in
    // the window that won.
    //
    // The population is every listing row and not the ones where a reason fired,
    // because a listings row exists for every name and a figure computed over the
    // listed ones is a figure over the wrong population.
    // see: The base rate is over every name-night, and never over the listed ones
    //
    // A window with nothing resolved has no rate rather than a rate of zero: a
    // zero says every name fell and nothing says nothing has matured.
    public static double? BaseRate(IReadOnlyList<string?> outcomes)
    {
        var counted = outcomes.Where(outcome => outcome is Win or Loss).ToArray();

        return counted.Length == 0
            ? null
            : (double)counted.Count(outcome => outcome == Win) / counted.Length * 100;
    }
}
