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
//
// `BreakEven` is the share of the time the plan had to be right to come out
// even, and it belongs to the setup horizon alone: the two session horizons ask
// what the market did and have no plan to demand anything. It is present exactly
// where `ReturnPct` is on that horizon, because both are measured from the close
// the setup was entered at and a setup with no known entry close has neither.
// see: An unresolved setup is never a win
// see: A stored break-even is measured from the close the setup was entered at, as a percentage beside the figures it is compared with
// `EnteredAt` and `SessionsLeft` are the fill the setup was scored from and the sessions the cap
// left after it, which is what a bar simulated from the plan is simulated over. They are carried
// rather than stored: the fill of a setup entered and stopped on one session is the worst price
// its zone offered, since the store does not say where in the zone it sat, and a figure that stood
// in for the entry close would be read as one.
// see: A candidate counts a setup entered and stopped in one session as a loss
public sealed record ForwardReturn(
    string Horizon,
    string? Outcome,
    DateOnly? ResolvedOn,
    double? ReturnPct,
    double? BreakEven = null,
    decimal? EnteredAt = null,
    int? SessionsLeft = null);

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
        //
        // No break-even, and by rule rather than by omission: a break-even is the
        // bar a plan set for itself, and these two horizons ask what the market
        // did over a fixed stretch of sessions rather than what a plan demanded.
        // Their bar is the universe base rate, which the column beside them holds.
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
    // A setup starts on the first close at or below the zone's top edge and inside
    // the plan's own range, and a target reached before that close is `never
    // entered`: the move happened at a price the plan did not offer to buy at.
    //
    // Everything here is read on closes, as the stop and the target already were,
    // because a daily bar does not say what order a session's prices came in and a
    // fill read from a session's low would credit an order the store cannot show
    // was filled. The cap is counted from the listing night, since the plan being
    // scored is the one stored that night and it ages with it.
    //
    // The break-even is 8.2's half of the same entry: the share of the time this
    // plan had to be right to come out even, computed from the entry close it was
    // measured from. It rides on every outcome the entry close is known for and on
    // no other, so a row carries a return and a break-even together or carries
    // neither.
    // see: A setup is scored from its entry, and a target reached before the entry is never a win
    // see: A condition is judged against the break-even its own plan demands
    public static ForwardReturn OverSetup(
        IReadOnlyList<ReturnBar> after,
        decimal? stop,
        decimal? target,
        decimal? entryHigh = null,
        decimal? closeAtListing = null,
        decimal? rawCloseAtListing = null)
    {
        // A listing with no plan has no setup to resolve. That is an absence
        // rather than an unresolved setup, and the two are counted differently.
        if (stop is not { } storedStop || target is not { } storedTarget)
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
        if (storedStop >= storedTarget)
        {
            throw new InvalidOperationException(
                $"The stored plan has its stop at {storedStop} and its target at {storedTarget}. A stop at or " +
                "above the target is not a plan, and which of the two a session reached would be " +
                "decided by the order the rules are written in rather than by the series.");
        }

        // The plan keeps the scale the series had on the listing night, and a split
        // or dividend since restates the stored closes and not the plan.
        // see: An outcome once decided is never rewritten, and a setup still in play is scored with its plan scaled by its listing session's adjustment factor
        var restated = closeAtListing is { } adjusted && rawCloseAtListing is { } raw ? adjusted / raw : 1m;

        var floor = storedStop * restated;
        var ceiling = storedTarget * restated;
        var zoneTop = entryHigh * restated;

        // An entry is admitted only inside the plan's own range, so the return and
        // the break-even measured from it are present together or absent together.
        // see: An entry is admitted only inside the plan's own range, and a plan with no entry zone takes its whole range as its zone
        var highestEntry = zoneTop is { } top && top < ceiling ? top : ceiling;

        decimal? entry = closeAtListing is { } listed && listed >= floor && listed <= highestEntry
            ? listed
            : null;

        // Where in the cap the fill sat, the listing's own close counting as the session before the
        // first of them, so the sessions left after a fill are the cap less the sessions up to it.
        var filledAt = entry is null ? -1 : 0;

        for (var session = 0; session < Math.Min(after.Count, SetupSessionCap); session++)
        {
            var bar = after[session];
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
                // went nowhere. The break-even goes with it, for the same reason
                // and not a second one: it is the bar the entry close set, and an
                // entry close nobody knows sets none. Stored setups take this shape,
                // so it is a population to state rather than a case to wave at.
                // The fill a bar is carried for either shape: the close it was
                // entered at where that is known, and the top of the zone, the
                // worst price the plan offered to buy at, where the entry and the
                // stop fell on one session.
                return entry is { } at
                    ? new ForwardReturn(
                        Setup, Loss, bar.SessionDate, ChangeFromEntry(at, bar.Close), BreakEven(at, floor, ceiling),
                        at, SetupSessionCap - filledAt)
                    : new ForwardReturn(
                        Setup, Loss, bar.SessionDate, null, null,
                        highestEntry, SetupSessionCap - (session + 1));
            }

            if (entry is null)
            {
                // Not entered yet. A target reached from above the zone is the
                // move happening without the purchase the plan named.
                if (bar.Close >= ceiling)
                {
                    return new ForwardReturn(Setup, NeverEntered, bar.SessionDate, null);
                }

                if (bar.Close <= highestEntry)
                {
                    entry = bar.Close;
                    filledAt = session + 1;
                }

                continue;
            }

            if (bar.Close >= ceiling)
            {
                return new ForwardReturn(
                    Setup,
                    Win,
                    bar.SessionDate,
                    ChangeFromEntry(entry.Value, bar.Close),
                    BreakEven(entry.Value, floor, ceiling),
                    entry.Value,
                    SetupSessionCap - filledAt);
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
            : new ForwardReturn(
                Setup,
                Unresolved,
                last.SessionDate,
                ChangeFromEntry(entry.Value, last.Close),
                BreakEven(entry.Value, floor, ceiling));
    }

    // What the trade made, from the close it was entered at to the close it
    // resolved on. Null for a setup nobody entered, which has no trade to measure.
    static double? ChangeFromEntry(decimal entry, decimal at) =>
        entry <= 0 ? null : Statistic.FromRatio((at - entry) / entry) * 100;

    // The bar the plan set for itself: the share of the time its target has to be
    // reached before its stop for the setup to come out even.
    //
    // Section 13 derives it from where the bands sit rather than from a benchmark
    // borrowed from elsewhere, which is what makes it the thing a reason is scored
    // against. Its worked example is the case this is asserted over: an entry at
    // 920, a stop at 855 and a first traded target at 1057 put 65 points at risk
    // against 137 of reward, so the setup breaks even at 65 of 202, about 32 per
    // cent. A reason clears its bar by winning more often than its own setups
    // demanded, and a setup with a distant target is allowed to be right rarely.
    //
    // Measured from the close the setup was entered at rather than from the
    // listing's, for the reason the return is: a plan filled at the bottom of its
    // zone risked less and stood to gain more than the same plan filled at the
    // top, and one bar over both scores a trade nobody took.
    //
    // Risk and reward sum to the plan's whole range whichever side of it the entry
    // landed on, so the denominator is `target - stop` and the figure is a share
    // of it. That is why it exists only where the entry sits inside the range: a
    // plan entered above its own target or below its own stop yields no share, and
    // it refuses rather than returning a number outside nought and one hundred. The
    // caller admits an entry only inside the range, so no call from it reaches the
    // refusal.
    //
    // A percentage rather than a fraction, which is the form the two figures on
    // the same row carry and the form the share it is tested against carries. The
    // plan arithmetic the name page draws states its own break-even as a fraction,
    // and the two are different numbers about different entries.
    // see: A stored break-even is measured from the close the setup was entered at, as a percentage beside the figures it is compared with
    // see: A condition is judged against the break-even its own plan demands
    static double? BreakEven(decimal entry, decimal stop, decimal target)
    {
        var risk = entry - stop;
        var reward = target - entry;

        return risk < 0 || reward < 0 || risk + reward <= 0
            ? null
            : Statistic.FromRatio(risk / (risk + reward)) * 100;
    }

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
    public static double? BaseRate(IReadOnlyList<string?> outcomes) => WinShare(outcomes);

    // One reason's record over the setups it produced: how many set a bar at all,
    // how often they cleared it, and the bar itself.
    //
    // The population is the resolved setups carrying a break-even and no others,
    // because the share and the bar are a pair. A share tested against a bar has
    // to be the share of the rows that bar was averaged over, and a setup that
    // entered and stopped on one session is resolved and set no bar. Stored
    // setups take that shape, so the two counts are stated apart rather than
    // assumed equal.
    //
    // The arithmetic is here rather than in the projection that calls it, for the
    // reason the base rate's is: a rendering layer that computes is a second
    // implementation of one rule, and the two disagree eventually. Nothing here
    // decides whether the figures are shown. That is the minimum's job.
    // see: A screen reads and renders, and computes nothing
    // see: A condition is judged against the break-even its own plan demands
    public static (int Scored, double? Share, double? BreakEven) Record(
        IReadOnlyList<(string? Outcome, double? BreakEven)> setups)
    {
        var scored = setups
            .Where(setup => IsScored(setup.Outcome, setup.BreakEven))
            .ToArray();

        return scored.Length == 0
            ? (0, null, null)
            : (scored.Length,
                WinShare([.. scored.Select(setup => setup.Outcome)]),
                scored.Average(setup => setup.BreakEven!.Value));
    }

    // A setup a reason is scored on: resolved as a win or a loss, with the bar its entry close set.
    // The share, the mean bar, both floors and the verdict are all taken over this one set.
    // see: A reason's share, verdict and both floors are counted over the resolved setups that set a bar
    public static bool IsScored(string? outcome, double? breakEven) =>
        outcome is Win or Loss && breakEven is not null;

    // The share of a set of outcomes that won, as a percentage, and none where
    // nothing among them has matured.
    //
    // One piece of arithmetic under two figures, which are the same question
    // asked of two populations: every name-night in a window, and the setups one
    // reason produced. Each names its own population rather than sharing a word
    // for both.
    static double? WinShare(IReadOnlyList<string?> outcomes)
    {
        var counted = outcomes.Where(outcome => outcome is Win or Loss).ToArray();

        return counted.Length == 0
            ? null
            : (double)counted.Count(outcome => outcome == Win) / counted.Length * 100;
    }
}
