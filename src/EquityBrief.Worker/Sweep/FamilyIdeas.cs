using EquityBrief.Core.Families;
using EquityBrief.Core.Prices;
using EquityBrief.Core.Sweep;

namespace EquityBrief.Worker.Sweep;

// How an idea reads a drift print's revenue growth: not at all, as a filter keeping a print whose quarter's revenue
// grew on the same quarter a year before, or as the order a night's listings are kept in, the largest growth first.
public enum RevenueUse
{
    None,
    Grew,
    Order,
}

// One idea added alone to a frozen family's rule: its key and its rule in words, what it changes, the exit its
// trades are sold by, the most a night keeps, the market switch a night must pass, whether a listing whose stop
// sits nearer the buy than one typical move is left off, and how it reads a print's revenue growth.
public sealed record FamilyIdea(string Key, string Rule, IdeaKind Kind, IdeaExit Exit, int PerNight, MarketSwitch? Switch, bool StopAtLeastAMove, RevenueUse Revenue = RevenueUse.None)
{
    public bool Market => Kind == IdeaKind.Market;
}

// One idea read on a frozen family: its figures and its test against the family's rule as frozen.
public sealed record FamilyIdeaReading(FamilyIdea Idea, IdeaFigures Figures, IdeaTest Test);

// What the ideas' run read on one frozen family: the family, its rule as frozen in its grid's words, the rule's own
// figures, and every idea read on it, with the tries, the passes and what luck alone passes of that many.
public sealed record FamilyIdeasRead(string Family, string Frozen, IdeaFigures Base, IReadOnlyList<FamilyIdeaReading> Ideas)
{
    public int Tries => Ideas.Count;

    public int Passes => Ideas.Count(one => one.Test.Passes);

    public double Luck => 1.0 * Tries * SweepIdeas.LuckPatterns() / (1 << SweepFigures.Years);

    // Whether every figure an idea came to is the rule's own as frozen, as where it leaves off no listing the rule
    // makes and sells none differently.
    public bool Unchanged(FamilyIdeaReading reading) =>
        reading.Figures.Listed == Base.Listed
        && reading.Figures.Trades == Base.Trades
        && reading.Figures.Edge == Base.Edge
        && reading.Figures.Result == Base.Result
        && reading.Figures.YearTotal.SequenceEqual(Base.YearTotal);
}

// The ideas' run on the breakout and the earnings drift as frozen: each family's live rule at the setting it froze
// at as the base, and each of the pullback's ideas that fits it added alone and judged by the pullback's test: the
// six market switches, the stop sold on touch, the stop at least one typical move under the buy, the best three a
// night, and holds of at most 10 and 20 sessions, with the trailing stops of 2 and 3 typical moves for the drift
// alone, whose plan has a target where the breakout's already trails. Nothing is frozen or registered.
// see: The frozen families are read with the pullback's ideas one at a time, and nothing they show is frozen or registered
public static class FamilyIdeas
{
    // The ideas a family is read with, in the order the report states them.
    public static IReadOnlyList<FamilyIdea> For(string family)
    {
        List<FamilyIdea> ideas =
        [
            Switch("a1", "the share of members above their own 200-day average higher than ten sessions before", MarketSwitch.BreadthRising),
            Switch("a2", "more members making a new 252-session high than a new low", MarketSwitch.HighsOverLows),
            Switch("a3", "the index closing above its 50-session average", MarketSwitch.IndexOverFifty),
            Switch("a4", "the index closing above its 200-session average", MarketSwitch.IndexOverTwoHundred),
            Switch("a5", "the VIX closing under 20", MarketSwitch.VixUnderTwenty),
            Switch("a6", "the VIX closing under its close ten sessions before", MarketSwitch.VixFalling),
            new("b", "a session opening under the stop sells at its open and one whose low reaches it sells at the stop", IdeaKind.Exit, IdeaExit.TouchedStop, FamilySweep.PerNight, null, false),
            new("c", "only the night's first three in the family's own order, once a stock's open trade has kept it off", IdeaKind.Selection, IdeaExit.Base, SweepIdeas.BestOf, null, false),
            new("e", "a listing whose stop sits nearer the buy than one typical move left off", IdeaKind.Selection, IdeaExit.Base, FamilySweep.PerNight, null, true),
            new("h10", "the family's own exit held at most 10 sessions", IdeaKind.Exit, IdeaExit.HoldTen, FamilySweep.PerNight, null, false),
            new("h20", "the family's own exit held at most 20 sessions", IdeaKind.Exit, IdeaExit.HoldTwenty, FamilySweep.PerNight, null, false),
        ];

        if (family == DriftRule.Name)
        {
            ideas.Add(new("d2", "no target: the stop the higher of the plan's and the highest close since the buy less 2 typical moves, never lowered", IdeaKind.Exit, IdeaExit.TrailTwo, FamilySweep.PerNight, null, false));
            ideas.Add(new("d3", "the same trailing stop at 3 typical moves", IdeaKind.Exit, IdeaExit.TrailThree, FamilySweep.PerNight, null, false));
        }

        return ideas;

        static FamilyIdea Switch(string key, string rule, MarketSwitch on) =>
            new(key, "only on nights with " + rule, IdeaKind.Market, IdeaExit.Base, FamilySweep.PerNight, on, false);
    }

    // The family's rule as frozen, as its grid's levels: each dial at the level its live rule holds.
    public static int[] Frozen(string family, FamilyGrid grid) => family switch
    {
        BreakoutRule.Name => Levels(grid, BreakoutRule.Live.HighSessions, BreakoutRule.Live.VolumeMultiple, BreakoutRule.Live.RangeCeiling, BreakoutRule.Live.StopMoves),
        DriftRule.Name => Levels(grid, DriftRule.Live.WindowSessions, DriftRule.Live.ReactionMoves, DriftRule.Live.VolumeMultiple, DriftRule.Live.TargetRiskMultiple),
        _ => throw new ArgumentException($"The ideas' run reads the breakout and the earnings drift, and not '{family}'.", nameof(family)),
    };

    // Each dial's level at a value, refused where a dial holds no level at it, since a frozen rule off its sweep's
    // grid is not the rule the sweep read.
    public static int[] Levels(FamilyGrid grid, params double[] values)
    {
        if (values.Length != grid.Dials.Count)
        {
            throw new ArgumentException("A setting states one value a dial.", nameof(values));
        }

        return
        [
            .. values.Select((value, dial) =>
            {
                var level = grid.Dials[dial].Levels.ToList().FindIndex(held => held == value || Math.Abs(held - value) < 1e-9);

                return level >= 0
                    ? level
                    : throw new ArgumentException(FormattableString.Invariant($"The frozen rule holds {grid.Dials[dial].Dial} at {value}, a level its sweep's grid does not."), nameof(values));
            }),
        ];
    }

    // A listing's exit under an idea's exit, walked over its stock's own bars: the touched stop on its plan, under
    // its trail where it trails and before its target where it has one; its own exit held at most 10 or 20
    // sessions; or, for a plan with a target, a trail of 2 or 3 typical moves in the target's place.
    public static double? ExitOf(IdeaExit exit, FamilyListing listing, ReadOnlySpan<double> opens, ReadOnlySpan<double> lows, ReadOnlySpan<double> closes, double move, out int sessions)
    {
        var bar = listing.Bar;
        var entry = listing.Entry;
        var stop = listing.Stop;

        switch (exit)
        {
            case IdeaExit.TouchedStop:
                return listing.Trails
                    ? SweepWalk.TouchedTrailing(opens, lows, closes, bar, entry, stop, listing.Trail, listing.Cap, out sessions)
                    : SweepWalk.TouchedStop(opens, lows, closes, bar, entry, stop, listing.Target, listing.Cap, out sessions);

            case IdeaExit.HoldTen or IdeaExit.HoldTwenty:
            {
                var hold = Math.Min(listing.Cap, exit == IdeaExit.HoldTen ? SweepIdeas.ShortHold : SweepIdeas.MiddleHold);

                return listing.Trails
                    ? FamilyWalks.Trailing(closes, bar, entry, stop, listing.Trail, hold, out sessions)
                    : FamilyWalks.Fixed(closes, bar, entry, stop, listing.Target, hold, out sessions);
            }

            case IdeaExit.TrailTwo or IdeaExit.TrailThree:
                return FamilyWalks.Trailing(closes, bar, entry, stop, (exit == IdeaExit.TrailThree ? SweepIdeas.TrailThreeMoves : SweepIdeas.TrailTwoMoves) * move, listing.Cap, out sessions);

            default:
                throw new ArgumentException("The family's own exit is the family's sweep's, and not walked here.", nameof(exit));
        }
    }
}

// A frozen family's listings as the ideas read them: the family's rule at the setting it froze at over the history,
// each idea's rule walked over them, its exits the family's own where the idea keeps them and walked over the
// members' bars where it puts a new one in their place, and the benchmark for each exit the same plan on every
// member that night under that exit.
public sealed class FamilyIdeaReplay
{
    readonly FamilySweepRunner.Adapter adapter;
    readonly FamilyListing[] listings;
    readonly IReadOnlyList<SweepSeries> series;
    readonly SweepBenchmark.Members members;
    readonly bool[][] switches;
    readonly string[] tickers;
    readonly Func<int, int> yearOf;
    readonly int nights;
    readonly double[][] opens;
    readonly double[][] lows;
    readonly double[][] closes;
    readonly Dictionary<(int Session, IdeaExit Exit, bool Trails, double StopMoves, double Second), double> benchmarks = [];
    readonly IReadOnlyList<DateOnly>? calendar;
    readonly Dictionary<string, IReadOnlyList<QuarterRevenue>> quarters = new(StringComparer.Ordinal);
    readonly Dictionary<(int Name, int Bar), PrintRevenue?> growths = [];
    readonly Dictionary<string, (int Read, int FiledAfterTheBuy)> revenueRead = new(StringComparer.Ordinal);

    // Over a drift's history, the calendar its sessions index and each name's revenue as its filer filed it give each
    // listing its print's revenue growth, which the ideas reading it read; without them no listing has one.
    public FamilyIdeaReplay(
        FamilySweepRunner.Adapter adapter,
        int[] frozen,
        IReadOnlyList<SweepSeries> series,
        SweepBenchmark.Members members,
        bool[][] switches,
        Func<int, int> yearOf,
        int nights,
        IReadOnlyList<DateOnly>? calendar = null,
        IReadOnlyDictionary<string, IReadOnlyList<FiledRevenue>>? revenue = null)
    {
        this.adapter = adapter;
        this.series = series;
        this.members = members;
        this.switches = switches;
        this.yearOf = yearOf;
        this.nights = nights;
        this.calendar = calendar;
        listings = [.. adapter.Listings(frozen)];
        tickers = [.. series.Select(one => one.Name.Ticker)];
        opens = [.. series.Select(one => one.Bars.Select(bar => Statistic.FromPrice(bar.Open)).ToArray())];
        lows = [.. series.Select(one => one.Bars.Select(bar => Statistic.FromPrice(bar.Low)).ToArray())];
        closes = [.. series.Select(one => one.Bars.Select(bar => Statistic.FromPrice(bar.Close)).ToArray())];

        foreach (var (ticker, facts) in revenue ?? new Dictionary<string, IReadOnlyList<FiledRevenue>>())
        {
            quarters[ticker] = FirstFiledRevenue.Quarters(facts);
        }
    }

    public int Listings => listings.Length;

    // How many of the rule's own listings read a revenue growth.
    public int ListingsWithRevenue => listings.Count(listing => RevenueOf(listing) is not null);

    // For an idea reading the revenue, how many of its trades read a growth, and of those how many read a quarter first
    // filed after the session they were bought on.
    public IReadOnlyDictionary<string, (int Read, int FiledAfterTheBuy)> RevenueRead => revenueRead;

    // A listing's print's revenue growth: the newest print on or before its bar, read over its filer's quarters as
    // first filed; none where the name has no print, no filer or no quarter the reading finds.
    // see: A drift print's revenue growth is its quarter's revenue as first filed against the same quarter a year before
    public PrintRevenue? RevenueOf(FamilyListing listing)
    {
        if (growths.TryGetValue((listing.Name, listing.Bar), out var known))
        {
            return known;
        }

        var one = series[listing.Name];
        var newest = one.NewestSurprise[listing.Bar];
        var read = newest >= 0 && quarters.TryGetValue(one.Name.Ticker, out var filed)
            ? RevenueGrowth.Of(one.Name.Surprises[newest].EventDate, filed)
            : null;

        growths[(listing.Name, listing.Bar)] = read;

        return read;
    }

    // The rule's own figures, or an idea's on it.
    public IdeaFigures Evaluate(string key, FamilyIdea? idea)
    {
        var exit = idea?.Exit ?? IdeaExit.Base;
        var chosen = listings.Where(listing =>
            (idea?.Switch is not { } on || switches[(int)on][listing.Session])
            && !(idea?.StopAtLeastAMove == true && listing.CloseStop)
            && (idea?.Revenue != RevenueUse.Grew || RevenueOf(listing) is { Growth: > 0 }));

        // Ordered by the growth, the largest first, a listing reading none after every one reading one, and the
        // family's own order within each.
        if (idea?.Revenue == RevenueUse.Order)
        {
            chosen = chosen.Select(listing => RevenueOf(listing) is { } read
                ? listing with { Order = read.Growth, ThenBy = listing.Order }
                : listing with { Order = double.NegativeInfinity, ThenBy = listing.Order });
        }

        var trades = FamilySweep.Walk(
            chosen,
            tickers,
            yearOf,
            listing => exit == IdeaExit.Base ? adapter.Exit(listing) : (FamilyIdeas.ExitOf(exit, listing, opens[listing.Name], lows[listing.Name], closes[listing.Name], listing.Move, out var held), held),
            listing => exit == IdeaExit.Base ? adapter.Benchmark(listing) : Benchmark(listing, exit),
            idea?.PerNight ?? FamilySweep.PerNight);

        if (idea is { Revenue: not RevenueUse.None } && calendar is not null)
        {
            var read = trades.Select(trade => (Trade: trade, Revenue: RevenueOf(trade.Listing))).Where(one => one.Revenue is not null).ToArray();

            revenueRead[key] = (read.Length, read.Count(one => one.Revenue!.Quarter.Filed > calendar[one.Trade.Listing.Session]));
        }

        return SweepIdeas.Figures(key, [.. trades.Select((trade, at) => new IdeaTrade(
            new IdeaListing(at, trade.Listing.Name, trade.Listing.Bar, trade.Listing.Session, trade.Year, 0, trade.Listing.Order, 0, StopMoves(trade.Listing), null!),
            trade.Result,
            trade.Benchmark))], nights);
    }

    // The same plan entered at the close on every member the index held that night with a bar, its stop the
    // listing's distance in each member's own typical moves, and its target the listing's reward to risk or its
    // trail the listing's distance, sold by the idea's exit; the average of the results the history reaches.
    public double Benchmark(FamilyListing listing, IdeaExit exit)
    {
        var stopMoves = StopMoves(listing);
        var second = listing.Trails ? listing.Trail / listing.Move : (listing.Target - listing.Entry) / (listing.Entry - listing.Stop);
        var key = (listing.Session, exit, listing.Trails, Math.Round(stopMoves, 6), Math.Round(second, 6));

        if (benchmarks.TryGetValue(key, out var held))
        {
            return held;
        }

        var names = members.Names[listing.Session];
        var bars = members.Bars[listing.Session];
        var sum = 0.0;
        var count = 0;

        for (var at = 0; at < names.Length; at++)
        {
            var name = names[at];
            var bar = bars[at];
            var move = series[name].Atr[bar];
            var entry = closes[name][bar];
            var risk = stopMoves * move;

            if (!(risk > 0) || entry - risk <= 0)
            {
                continue;
            }

            var plan = listing.Trails
                ? listing with { Name = name, Bar = bar, Entry = entry, Stop = entry - risk, Trail = second * move, Move = move }
                : listing with { Name = name, Bar = bar, Entry = entry, Stop = entry - risk, Target = entry + (second * risk), Move = move };

            if (FamilyIdeas.ExitOf(exit, plan, opens[name], lows[name], closes[name], move, out _) is { } result)
            {
                sum += result;
                count++;
            }
        }

        var value = count > 0 ? sum / count : double.NaN;

        benchmarks[key] = value;

        return value;
    }

    static double StopMoves(FamilyListing listing) => listing.Move > 0 ? (listing.Entry - listing.Stop) / listing.Move : double.NaN;
}
