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

// One place the plan sells part of what is held.
//
// `Fraction` is a share of the position that already exists, which is scaling
// out and not sizing: it needs no capital figure and it is the same for every
// traded exit, because any other split is a preference dressed as arithmetic.
// see: Each traded exit sells an equal fraction of what is held, and the top of the ladder trails
//
// A `Traded` of false is an exit listed and not acted on, because it is closer
// than two typical days' moves to the blended entry and a move inside the noise
// is worth naming on the chart and not worth paying a spread to act on.
// see: An exit closer than two typical days' moves is listed but not traded
//
// `Trailing` is the top of the ladder, which is a rule rather than a price:
// there is no band above it to name, so what is left is to follow the price up.
public sealed record Exit(
    decimal LowEdge,
    decimal HighEdge,
    bool Traded,
    bool Trailing,
    string Fraction,
    string? Reason);

// One setup in the second book, keyed to a dated event.
//
// Every figure in it is a proposal. Nothing has scored a setup, so a number
// written here as a rule would be a constant nobody could later tell from a
// measured one, and the page says so where a reader will see it.
// see: The event setups' triggers are proposals until resolved setups can score them
public sealed record EventSetup(
    string Name,
    DateOnly EventDate,
    string Trigger,
    decimal Entry,
    decimal Stop,
    decimal Target);

// The position book and the second book: the tranches, their stops, the exits,
// the price the whole thing is wrong below, and the setups keyed to a date.
//
// The two never merge. Holding a momentum entry through a print is the thing the
// separation exists to prevent, so the setups are a list of their own rather
// than tranches with a date on them.
// see: The second book is keyed to a dated event, and an earnings print is the only kind on file
public sealed record Ladder(
    IReadOnlyList<Tranche> Tranches,
    IReadOnlyList<Exit> Exits,
    decimal? Invalidation,
    string? Reason,
    IReadOnlyList<EventSetup> Events);

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
    // Section 17's other count: five exits covers the distance from the nearest
    // resistance band to a prior high.
    public const int MostExits = 5;

    // An exit closer than this to the blended entry is listed and not traded.
    public const int NearExitInTypicalDays = 2;

    public const int ConditionLookback = 10;

    public const int ShockInTypicalDays = 3;

    public static Ladder For(
        IReadOnlyList<Level> bands,
        decimal close,
        decimal typicalMove,
        IReadOnlyList<LadderBar> recent,
        string trendState,
        IReadOnlyList<decimal>? swingLows = null,
        DateOnly? nextEvent = null)
    {
        // A downtrend carries no tranches at all, and a name whose trend could
        // not be classified carries none either: the label decides whether a
        // plan exists, and a plan placed on a label nobody could read is a
        // purchase on an unmeasured input.
        // see: The stop rule depends on the trend state
        if (trendState is TrendState.Downtrend)
        {
            return new Ladder([], [], null, "the trend is down, so no tranche is placed", []);
        }

        if (trendState is TrendState.NotClassified)
        {
            return new Ladder([], [], null, "the trend state could not be classified, so no tranche is placed", []);
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
                [],
                null,
                eligible.Length == 0
                    ? "no support band sits below the price"
                    : "no support band below the price has an anchor other than a moving average",
                []);
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
                StopFor(band, beneath?.LowEdge, trendState, swingLows ?? [])));
        }

        // The whole position is wrong below the lowest band the structure
        // depends on, which is the lowest stop the tranches carry. A tranche
        // whose stop is absent has no band beneath it, so the structure ends at
        // that tranche's own low edge.
        var invalidation = tranches
            .Select(tranche => tranche.Stop ?? tranche.LowEdge)
            .DefaultIfEmpty()
            .Min();

        return new Ladder(
            tranches,
            ExitsFor(bands, tranches, typicalMove),
            invalidation,
            null,
            EventsFor(bands, close, typicalMove, nextEvent, recent));
    }

    // Where the stop sits, which the trend decides.
    //
    // In a range it is the low edge of the next band beneath, which is the range
    // floor. In an uptrend it trails: the stop is the higher of that band and
    // the most recent swing low beneath the tranche, so it rises as the
    // structure makes higher lows and is never looser than the range rule.
    // see: The stop rule depends on the trend state
    //
    // Taking the higher of the two rather than the swing low alone is what keeps
    // the rule meaningful. Read literally, "the stop trails the last higher low"
    // puts the stop wherever the last swing low happens to be, which on a name
    // that has run a long way is far below the band beneath and is looser
    // protection than the range rule gives. A trailing stop that can sit below
    // the range floor is not trailing anything.
    public static decimal? StopFor(
        Level band,
        decimal? beneath,
        string trendState,
        IReadOnlyList<decimal> swingLows)
    {
        if (trendState != TrendState.Uptrend)
        {
            return beneath;
        }

        var trailing = swingLows.Where(low => low < band.LowEdge).ToArray();

        if (trailing.Length == 0)
        {
            return beneath;
        }

        // The most recent one beneath the tranche, which is the last in the
        // series the caller hands over in session order.
        var last = trailing[^1];

        return beneath is { } floor ? Math.Max(floor, last) : last;
    }

    // One exit per resistance band above the price, at most five, the nearest
    // first.
    //
    // The near-exit skip is measured from the blended entry, which is the mean
    // of the first two tranche zones' midpoints, or the first alone where there
    // is only one. That is what section 17's row names, and it is the price the
    // position is actually carried at once the plan has staged what it can.
    public static IReadOnlyList<Exit> ExitsFor(
        IReadOnlyList<Level> bands,
        IReadOnlyList<Tranche> tranches,
        decimal typicalMove)
    {
        if (tranches.Count == 0)
        {
            return [];
        }

        var midpoints = tranches
            .Take(2)
            .Select(tranche => (tranche.LowEdge + tranche.HighEdge) / 2)
            .ToArray();

        var blended = midpoints.Sum() / midpoints.Length;
        var near = typicalMove * NearExitInTypicalDays;

        var above = bands
            .Where(band => band.Role == LevelSeries.Resistance)
            .OrderBy(band => band.LowEdge)
            .Take(MostExits)
            .ToArray();

        var traded = above.Where(band => band.LowEdge - blended >= near).ToArray();

        // The top of the ladder is the highest traded exit, and it is a trailing
        // rule rather than a price: there is no band above it to name, so what
        // is left is to follow the price up. A name whose every exit is skipped
        // has no top of the ladder, which is a plan that says take nothing here
        // rather than one with a rule attached to nothing.
        var top = traded.Length == 0 ? null : traded[^1];

        // Equal fractions over the traded exits, stated as the share rather than
        // computed into a quantity: what is held is the reader's.
        var share = traded.Length == 0 ? "0" : $"1/{traded.Length}";

        return
        [
            .. above.Select(band =>
            {
                var isTraded = band.LowEdge - blended >= near;

                return new Exit(
                    band.LowEdge,
                    band.HighEdge,
                    isTraded,
                    isTraded && ReferenceEquals(band, top),
                    isTraded ? share : "0",
                    isTraded
                        ? null
                        : $"closer than {NearExitInTypicalDays} typical days' moves to the blended entry");
            }),
        ];
    }

    // The three setups keyed to the print, from figure 10.1's own list: a
    // breakout before it, a flush after it, and a gap up after it.
    //
    // Every figure is a proposal and the page says so. What is not a proposal is
    // the shape: each carries a trigger, an entry, a stop and a target, and each
    // is built from vocabulary the corpus already has, being band edges, the
    // typical daily move and the twenty-session horizon. A setup built from a
    // figure nothing else uses would be a number with no reading behind it.
    // see: The event setups' triggers are proposals until resolved setups can score them
    //
    // A name with no date on file gets none of them and says so, rather than
    // producing them from a guessed date.
    // see: The second book is keyed to a dated event, and an earnings print is the only kind on file
    public static IReadOnlyList<EventSetup> EventsFor(
        IReadOnlyList<Level> bands,
        decimal close,
        decimal typicalMove,
        DateOnly? nextEvent,
        IReadOnlyList<LadderBar> recent)
    {
        if (nextEvent is not { } date || recent.Count == 0)
        {
            return [];
        }

        var resistance = bands
            .Where(band => band.Role == LevelSeries.Resistance && band.LowEdge > close)
            .OrderBy(band => band.LowEdge)
            .ToArray();

        var support = bands
            .Where(band => band.Role == LevelSeries.Support && band.LowEdge < close)
            .OrderByDescending(band => band.LowEdge)
            .ToArray();

        var setups = new List<EventSetup>();

        // One. A breakout before the print. The trigger is a daily close above
        // the immediate resistance band's high edge on volume above its fifty-day
        // average, with the print still ahead. The entry is that close, the stop
        // is a close back below that band's low edge, and the target is the next
        // resistance band's low edge.
        if (resistance.Length > 0)
        {
            var band = resistance[0];
            var entry = band.HighEdge;

            setups.Add(new EventSetup(
                "a breakout before the print",
                date,
                $"a daily close above {band.HighEdge} on volume above its fifty-day average, with the print still ahead",
                entry,
                StopBelow(band, typicalMove),
                TargetAbove(resistance, entry, typicalMove)));
        }

        // Two. A flush after the print. The trigger is the session after the
        // print closing down by more than two typical days and inside a support
        // band, and the entry waits for the following close once that session's
        // low has held. The stop is a close below that low; the target is the
        // price the flush came from, which is the band above it.
        if (support.Length > 0)
        {
            var band = support[0];

            // The entry is the low edge, which is where a flush lands, and the
            // target is the price it flushed from. Entering at the high edge
            // would be buying the top of the band the price has just fallen
            // through, and on a name whose close sits inside that band it is a
            // target below the entry: AAPL's close is inside its immediate
            // support band, so the first arrangement gave that setup a negative
            // reward. Found by deriving the figures before running the code.
            var target = close > band.HighEdge ? close : band.HighEdge;

            setups.Add(new EventSetup(
                "a flush after the print",
                date,
                $"the session after the print closes down more than {NearExitInTypicalDays} typical days and inside {band.LowEdge} to {band.HighEdge}, and the next session holds its low",
                band.LowEdge,
                StopBelow(band, typicalMove),
                target));
        }

        // Three. A gap up after the print. The trigger is the session after the
        // print opening above the immediate resistance band and closing above
        // its own open, which is a gap the day did not give back. The stop is a
        // close back inside the band.
        if (resistance.Length > 0)
        {
            var band = resistance[0];
            var entry = band.HighEdge + typicalMove;

            setups.Add(new EventSetup(
                "a gap up after the print",
                date,
                $"the session after the print opens above {band.HighEdge} and closes above its own open",
                entry,
                StopBelow(band, typicalMove),
                TargetAbove(resistance, entry, typicalMove)));
        }

        return setups;
    }

    // A stop a typical day's move below the band rather than at its low edge.
    //
    // At the edge it is inside the noise, which is the reason the position
    // book's stops sit below the next band down rather than just below the
    // tranche. On a band whose edges are one price, which is most of them, a
    // stop at the low edge is the entry: the setup could never win and would
    // stop out on the session that triggered it.
    // see: A tranche is a support band, and its stop is a daily close below the low edge of the next band beneath it
    static decimal StopBelow(Level band, decimal typicalMove) => band.LowEdge - typicalMove;

    // The first resistance band above the entry, or two typical days above it
    // where there is none.
    //
    // Above the entry and not merely the next band: a gap up enters above the
    // band it cleared, and the band after it can sit below that entry, which
    // would be a target the setup starts past.
    static decimal TargetAbove(IReadOnlyList<Level> resistance, decimal entry, decimal typicalMove)
    {
        var above = resistance.FirstOrDefault(band => band.LowEdge > entry);

        return above?.LowEdge ?? entry + (typicalMove * 2);
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
