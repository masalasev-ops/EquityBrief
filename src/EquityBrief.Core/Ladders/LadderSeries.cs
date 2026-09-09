using EquityBrief.Core.Levels;

namespace EquityBrief.Core.Ladders;

// What makes a tranche actionable, drawn from figure 10.1's fixed list.
//
// The four are patterns the price has made at the band. A tranche where none of
// them has occurred is not actionable yet, and `ReachesTheZone` says that rather
// than naming a pattern that has not happened: the absence of a pattern is not a
// fifth pattern.
// see: A tranche's condition is the pattern the price has made at its own band
public enum TrancheCondition
{
    ReachesTheZone,
    AvailableNow,
    FailedBreakdown,
    FirstCloseBackAbove,
    SecondDayAfterAShock,
}

// One step of a staged purchase: the band it sits on, what makes it actionable,
// and where it stops.
public sealed record Tranche(
    decimal LowEdge,
    decimal HighEdge,
    TrancheCondition Condition,
    decimal? Stop);

// The position book: the tranches, their stops, and the price the whole thing
// is wrong below.
public sealed record Ladder(
    IReadOnlyList<Tranche> Tranches,
    decimal? Invalidation,
    string? Reason);

// One session, as the ladder reads it.
public readonly record struct LadderBar(DateOnly SessionDate, decimal High, decimal Low, decimal Close);

// The ladder arithmetic, as a pure function of the bands, the close and the
// recent sessions.
//
// It places a position and never sizes one.
// see: The plan places a position and never sizes one
// see: Code owns every number
public static class LadderSeries
{
    // Section 17's counts. Three steps is enough to stage a purchase across a
    // range without the last sitting below the level that invalidates the whole
    // position.
    public const int MostTranches = 3;

    // The lookback a condition is read over, and the shock it is read against.
    // Both are proposals under the same rule as the event setups: nothing has
    // scored a tranche, so a figure written here as a rule would be a constant
    // nobody could later tell from a measured one.
    // see: The event setups' triggers are proposals until resolved setups can score them
    public const int ConditionLookback = 10;

    public const int ShockInTypicalDays = 3;

    public static Ladder For(
        IReadOnlyList<Level> bands,
        decimal close,
        decimal typicalMove,
        IReadOnlyList<LadderBar> recent,
        string trendState)
    {
        // A downtrend carries no tranches at all, and a name whose trend could
        // not be classified carries none either: the label decides whether a
        // plan exists, and a plan placed on a label nobody could read is a
        // purchase on an unmeasured input.
        // see: The stop rule depends on the trend state
        if (trendState is TrendState.Downtrend)
        {
            return new Ladder([], null, "the trend is down, so no tranche is placed");
        }

        if (trendState is TrendState.NotClassified)
        {
            return new Ladder([], null, "the trend state could not be classified, so no tranche is placed");
        }

        // Support bands whose low edge is below the close, nearest first. The
        // low edge and not the whole band, so a band the price is trading in is
        // eligible and keeps its full width.
        // see: A support band whose low edge is below the price carries a tranche, even when the band contains the price
        var eligible = bands
            .Where(band => band.Role == LevelSeries.Support && band.LowEdge < close)
            .OrderByDescending(band => band.LowEdge)
            .ToArray();

        // A band anchored only by a moving average carries no tranche. A short
        // average follows the price, so such a band sits at the price about half
        // the time and the first condition would fire on nothing having
        // happened.
        // see: A moving average is a level on the chart and never an anchor for a tranche
        var anchored = eligible.Where(band => band.HasNonAverageAnchor).ToArray();

        if (anchored.Length == 0)
        {
            return new Ladder(
                [],
                null,
                eligible.Length == 0
                    ? "no support band sits below the price"
                    : "no support band below the price has an anchor other than a moving average");
        }

        var chosen = anchored.Take(MostTranches).ToArray();

        var tranches = new List<Tranche>();

        foreach (var band in chosen)
        {
            // The stop is a daily close below the low edge of the next support
            // band beneath this one, and the next band beneath is the next
            // eligible one whatever anchors it: a band that cannot be bought is
            // still a level the price has to break.
            // see: A tranche is a support band, and its stop is a daily close below the low edge of the next band beneath it
            var beneath = bands
                .Where(other => other.Role == LevelSeries.Support && other.LowEdge < band.LowEdge)
                .OrderByDescending(other => other.LowEdge)
                .FirstOrDefault();

            tranches.Add(new Tranche(
                band.LowEdge,
                band.HighEdge,
                ConditionFor(band, close, typicalMove, recent),
                beneath?.LowEdge));
        }

        // The whole position is wrong below the lowest band the structure
        // depends on, which is the lowest stop the tranches carry. A tranche
        // whose stop is absent has no band beneath it, so the structure ends at
        // that tranche's own low edge.
        var invalidation = tranches
            .Select(tranche => tranche.Stop ?? tranche.LowEdge)
            .DefaultIfEmpty()
            .Min();

        return new Ladder(tranches, invalidation, null);
    }

    // Exactly one condition, tested in a stated order, because a matcher keyed
    // on the first thing that fits answers about everything that fits. The order
    // is the one the decision states and it is asserted in both directions.
    // see: A tranche's condition is the pattern the price has made at its own band
    public static TrancheCondition ConditionFor(
        Level band,
        decimal close,
        decimal typicalMove,
        IReadOnlyList<LadderBar> recent)
    {
        var window = recent.Count <= ConditionLookback
            ? recent
            : [.. recent.Skip(recent.Count - ConditionLookback)];

        if (close >= band.LowEdge && close <= band.HighEdge)
        {
            return TrancheCondition.AvailableNow;
        }

        // A session inside the lookback closed below the band and the price is
        // back at or above it. The breakdown failed.
        if (close >= band.LowEdge && window.Any(bar => bar.Close < band.LowEdge))
        {
            return TrancheCondition.FailedBreakdown;
        }

        // A session inside the lookback moved more than the shock multiple and
        // its low landed in the band, and the day after held above that low. The
        // second day after a shock, once the first day's low has held.
        for (var at = 0; at < window.Count - 1; at++)
        {
            var shock = window[at];

            if (shock.High - shock.Low <= typicalMove * ShockInTypicalDays)
            {
                continue;
            }

            if (shock.Low >= band.LowEdge && shock.Low <= band.HighEdge && window[at + 1].Low >= shock.Low)
            {
                return TrancheCondition.SecondDayAfterAShock;
            }
        }

        // The price is above the band and a session inside the lookback traded
        // into it, so the first close back above the zone after that dip is what
        // makes the tranche actionable.
        if (close > band.HighEdge && window.Any(bar => bar.Low <= band.HighEdge))
        {
            return TrancheCondition.FirstCloseBackAbove;
        }

        return TrancheCondition.ReachesTheZone;
    }
}
