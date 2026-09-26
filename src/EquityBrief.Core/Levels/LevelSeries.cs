using EquityBrief.Core.Prices;

namespace EquityBrief.Core.Levels;

// What a member is evidence of.
//
// The kind string beside it is what a reader sees, and it is finer: "swing
// high", "sma200", "retracement 61.8". This enum is what the rules are written
// against, because three of the five scoring and flagging rules turn on the
// source rather than on the name.
public enum MemberSource
{
    Swing,
    Average,
    Retracement,
    Shelf,
    Touch,
}

// One piece of evidence for a band, as SCHEMA's members column carries it: a
// kind, a price and a date.
public readonly record struct LevelMember(MemberSource Source, string Kind, decimal Price, DateOnly Date);

// One session, as the level builder reads it.
public readonly record struct LevelBar(DateOnly SessionDate, decimal High, decimal Low, decimal Close);

// One band.
public sealed record Level(
    decimal LowEdge,
    decimal HighEdge,
    string Role,
    bool Immediate,
    int Strength,
    bool HasNonAverageAnchor,
    IReadOnlyList<LevelMember> Members);

// The level arithmetic, as a pure function of the four candidate sources.
//
// Figure 9.1's five steps in order: collect, merge, add touches, assign roles,
// score. Written as one function because the steps are not independent, and
// separated from the component that reads and writes for the reason the other
// three builders are.
// see: Code owns every number
// see: Levels come from four sources: swings, moving averages, retracements of the last two swings, and heavy volume shelves
public static class LevelSeries
{
    public const string Support = "support";
    public const string Resistance = "resistance";

    // Section 5's fixed fractions. Written out rather than generated, because
    // they are a convention with no formula: 23.6 and 78.6 are not round
    // numbers and a reader checking them against the document has to see them.
    public static IReadOnlyList<decimal> Retracements { get; } = [0.236m, 0.382m, 0.500m, 0.618m, 0.786m];

    // Two candidates closer than this many typical days' moves join one band.
    public const decimal MergeDistanceInTypicalMoves = 0.5m;

    // No band is wider than this many typical days' moves.
    // see: A band is no wider than two typical days' moves, and a chain that would be wider splits at its widest gap
    public const decimal MaximumWidthInTypicalMoves = 2m;

    // The window for the recency point in the strength score.
    public const int RecentSessions = 20;

    public static IReadOnlyList<Level> For(
        IReadOnlyList<LevelBar> window,
        IReadOnlyList<LevelMember> candidates,
        decimal close,
        decimal mergeDistance,
        decimal typicalMove,
        DateOnly asOf)
    {
        if (window.Count == 0 || candidates.Count == 0)
        {
            return [];
        }

        var bands = Merged(candidates, mergeDistance, typicalMove * MaximumWidthInTypicalMoves);

        Touched(bands, window);

        return Scored(bands, window, close, asOf);
    }

    // The five retracements between the window's most recent swing high and its
    // most recent swing low, in whichever order they occurred.
    //
    // The direction is what "in whichever order" settles. A move that ran from
    // the low up to the high retraces downward from the high, so the levels sit
    // at the high less each fraction of the distance. A move that ran from the
    // high down to the low retraces upward, so they sit at the low plus each
    // fraction. The two produce different prices, because the fractions are not
    // symmetric: 23.6 and 78.6 do not sum to one.
    //
    // A window holding no swing of one of the two kinds produces nothing, rather
    // than retracements drawn from a substitute end.
    //
    // Each level is rounded to the form every price in this store carries. A
    // fraction of a distance has the scale of both multiplied, so 0.236 of a
    // seventy point range is a seven place number, and a band edge is a primary
    // key column: two expressions for one price that render differently are two
    // rows.
    // see: A retracement is drawn between the last swing high and the last swing low
    public static IReadOnlyList<LevelMember> RetracementsBetween(
        (decimal Price, DateOnly Date)? lastHigh,
        (decimal Price, DateOnly Date)? lastLow)
    {
        if (lastHigh is not { } high || lastLow is not { } low)
        {
            return [];
        }

        var span = high.Price - low.Price;

        if (span <= 0)
        {
            return [];
        }

        // The later of the two swings is when the move ended, which is both the
        // direction and the date the member carries.
        var upward = low.Date < high.Date;
        var ended = upward ? high.Date : low.Date;

        return
        [
            .. Retracements.Select(fraction => new LevelMember(
                MemberSource.Retracement,
                FormattableString.Invariant($"retracement {fraction * 100:0.#}"),
                PriceForm.Round(upward ? high.Price - (fraction * span) : low.Price + (fraction * span)),
                ended)),
        ];
    }

    // Sort by price; two candidates closer than the merge distance join one
    // band, whose edges are its lowest and highest member.
    //
    // Against the previous candidate rather than against the band's low edge, so
    // three prices each a third of the distance apart are one band. That is what
    // "two candidates closer than half a typical day's move join one band" says,
    // and it is what turns a cluster into a band rather than into a band and a
    // straggler.
    //
    // Strictly closer, so two candidates exactly the merge distance apart are
    // two bands. The document says closer than, and the boundary has to fall on
    // one side of the line whichever way it is written.
    //
    // A chain of close pairs can reach prices anyone trading them can tell
    // apart, since the merge distance holds between neighbours and not between
    // the chain's two ends, so a chain wider than the maximum width is split.
    // see: A band is no wider than two typical days' moves, and a chain that would be wider splits at its widest gap
    static List<List<LevelMember>> Merged(IReadOnlyList<LevelMember> candidates, decimal mergeDistance, decimal maximumWidth)
    {
        var sorted = candidates.OrderBy(member => member.Price).ThenBy(member => member.Kind, StringComparer.Ordinal).ToArray();
        var chains = new List<List<LevelMember>> { new() { sorted[0] } };

        for (var index = 1; index < sorted.Length; index++)
        {
            if (sorted[index].Price - sorted[index - 1].Price < mergeDistance)
            {
                chains[^1].Add(sorted[index]);
            }
            else
            {
                chains.Add([sorted[index]]);
            }
        }

        var bands = new List<List<LevelMember>>();

        foreach (var chain in chains)
        {
            Split(chain, maximumWidth, bands);
        }

        return bands;
    }

    // A chain no wider than the maximum is one band. A wider one splits at the
    // widest gap between neighbouring candidates, the lower of two equal gaps,
    // and each part is split again until it fits. A width exactly at the maximum
    // fits, since the maximum is the widest a band may be.
    static void Split(List<LevelMember> chain, decimal maximumWidth, List<List<LevelMember>> bands)
    {
        if (chain[^1].Price - chain[0].Price <= maximumWidth)
        {
            bands.Add(chain);

            return;
        }

        var widest = 1;

        for (var index = 2; index < chain.Count; index++)
        {
            if (chain[index].Price - chain[index - 1].Price > chain[widest].Price - chain[widest - 1].Price)
            {
                widest = index;
            }
        }

        Split(chain[..widest], maximumWidth, bands);
        Split(chain[widest..], maximumWidth, bands);
    }

    // Each visit the price paid a band joins it as one touch.
    //
    // A visit is a run of consecutive sessions whose low or high lay inside the
    // band, so a band the price entered, stayed in for three weeks and left was
    // visited once and not fifteen times: how long the price sat in a band is
    // not how many times the band held it. The touch is dated by the session the
    // visit arrived on and priced at that session's low where the low reached,
    // because the report cites dated lows inside a support band, and at its high
    // otherwise.
    //
    // A visit holding a session the band already has as a swing or a retracement
    // adds nothing, since that session's evidence is in the band once already.
    // An average and a shelf carry the night's date rather than a session of
    // their own, so neither stands for a visit.
    //
    // Added after the edges are fixed, which is what makes a touch unable to
    // create or widen a band.
    // see: A touch is one visit to a band, counted only where the band holds no swing or retracement from that visit, and it never creates a band
    static void Touched(List<List<LevelMember>> bands, IReadOnlyList<LevelBar> window)
    {
        foreach (var band in bands)
        {
            var low = band.Min(member => member.Price);
            var high = band.Max(member => member.Price);
            var evidenced = band
                .Where(member => member.Source is MemberSource.Swing or MemberSource.Retracement)
                .Select(member => member.Date)
                .ToHashSet();

            var visits = new List<LevelMember>();
            LevelMember? arrived = null;
            var held = false;

            foreach (var bar in window)
            {
                var lowReached = bar.Low >= low && bar.Low <= high;
                var highReached = bar.High >= low && bar.High <= high;

                if (!lowReached && !highReached)
                {
                    if (arrived is { } visit && !held)
                    {
                        visits.Add(visit);
                    }

                    arrived = null;
                    held = false;

                    continue;
                }

                arrived ??= new LevelMember(
                    MemberSource.Touch,
                    "touch",
                    lowReached ? bar.Low : bar.High,
                    bar.SessionDate);
                held |= evidenced.Contains(bar.SessionDate);
            }

            if (arrived is { } last && !held)
            {
                visits.Add(last);
            }

            band.AddRange(visits);
        }
    }

    static IReadOnlyList<Level> Scored(
        List<List<LevelMember>> bands,
        IReadOnlyList<LevelBar> window,
        decimal close,
        DateOnly asOf)
    {
        // The last twenty sessions by session and not by day, so a holiday does
        // not shorten the window.
        var recent = window.Count <= RecentSessions
            ? window[0].SessionDate
            : window[^RecentSessions].SessionDate;

        var levels = new List<Level>();

        foreach (var band in bands)
        {
            // The edges are the anchors' own range. Touches were added after
            // this was read, so they cannot move it.
            var anchors = band.Where(member => member.Source != MemberSource.Touch).ToArray();
            var lowEdge = anchors.Min(member => member.Price);
            var highEdge = anchors.Max(member => member.Price);

            // A band whose low edge is below the close is support, even where
            // the band contains the close. The decision that a straddling band
            // still carries a tranche turns eligibility on the low edge, and a
            // band that carried a tranche while being labelled resistance would
            // be two documents disagreeing about one row.
            // see: A support band whose low edge is below the price carries a tranche, even when the band contains the price
            var role = lowEdge < close ? Support : Resistance;

            // One point per member, touches included, because a touch
            // strengthens. A member whose evidence occurred in the last twenty
            // sessions adds one; an average and a shelf are figures recomputed
            // nightly rather than evidence that occurred, so neither can win it.
            // see: A member's date is the session its evidence occurred on, and a figure recomputed nightly has none of its own
            var occurred = band.Where(member =>
                member.Source is MemberSource.Swing or MemberSource.Touch or MemberSource.Retracement);

            var strength = band.Count
                + (occurred.Any(member => member.Date >= recent) ? 1 : 0)
                + (anchors.Any(member => member.Source == MemberSource.Retracement)
                    && anchors.Any(member => member.Source == MemberSource.Swing) ? 1 : 0)
                + (anchors.Any(member => member.Source == MemberSource.Shelf) ? 1 : 0);

            // An anchor is what creates a band, so a touch is not one. A band
            // held up by three sessions that reached it and one moving average
            // is still a band anchored only on an average.
            var anchored = anchors.Any(member => member.Source != MemberSource.Average);

            levels.Add(new Level(
                lowEdge,
                highEdge,
                role,
                false,
                strength,
                anchored,
                [.. band.OrderBy(member => member.Date).ThenBy(member => member.Price)]));
        }

        return Immediate(levels);
    }

    // The nearest band on each side, marked.
    //
    // By index rather than by value. Two bands can hold the same edges, the same
    // role and the same strength, and a record compares by value, so marking
    // "the band equal to the nearest one" would mark both of them. That is the
    // shape of defect this corpus keeps finding in matchers, and it costs
    // nothing to avoid here.
    //
    // The nearest support is the one with the highest low edge, which puts a
    // band containing the close first because its low edge is below the close
    // and above every other support band's.
    static IReadOnlyList<Level> Immediate(List<Level> levels)
    {
        var support = -1;
        var resistance = -1;

        for (var index = 0; index < levels.Count; index++)
        {
            if (levels[index].Role == Support
                && (support < 0 || levels[index].LowEdge > levels[support].LowEdge))
            {
                support = index;
            }

            if (levels[index].Role == Resistance
                && (resistance < 0 || levels[index].LowEdge < levels[resistance].LowEdge))
            {
                resistance = index;
            }
        }

        if (support >= 0)
        {
            levels[support] = levels[support] with { Immediate = true };
        }

        if (resistance >= 0)
        {
            levels[resistance] = levels[resistance] with { Immediate = true };
        }

        return [.. levels.OrderBy(level => level.LowEdge)];
    }
}
