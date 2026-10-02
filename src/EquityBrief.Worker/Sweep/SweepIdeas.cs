using System.Numerics;
using EquityBrief.Core.Prices;
using EquityBrief.Core.Sweep;

namespace EquityBrief.Worker.Sweep;

// The exits an idea may put in the base's place: the base's own, held up to 63 sessions on closes; the touched
// stop; the trailing stop at 2 or at 3 typical moves; and the base's own held up to 10 or 20 sessions.
public enum IdeaExit
{
    Base,
    TouchedStop,
    TrailTwo,
    TrailThree,
    HoldTen,
    HoldTwenty,
}

// The market switches, each read on the listing session's close.
public enum MarketSwitch
{
    BreadthRising,
    HighsOverLows,
    IndexOverFifty,
    IndexOverTwoHundred,
    VixUnderTwenty,
    VixFalling,
}

// What an idea changes: the nights a stock may be listed on, the exit its trades are sold by, or which listings
// are kept.
public enum IdeaKind
{
    Market,
    Exit,
    Selection,
}

// One rule the ideas' run walks: the live design at a setting of its dials, an exit, the most a night keeps,
// and the market switches a night must pass.
public sealed record IdeaRule(DialSetting Setting, IdeaExit Exit, int PerNight, IReadOnlyList<MarketSwitch> Switches)
{
    public bool HasSwitch => Switches.Count > 0;

    public string Key => string.Join(
        "|",
        Setting.ToString(),
        Exit.ToString(),
        PerNight == int.MaxValue ? "every" : PerNight.ToString(System.Globalization.CultureInfo.InvariantCulture),
        string.Join("+", Switches.Order()));
}

// One idea: its key, its rule in words, the published evidence on it as looked up on 2026-10-01, what it
// changes, and the change it makes to a rule.
public sealed record Idea(string Key, string Rule, string Evidence, IdeaKind Kind, Func<IdeaRule, IdeaRule> Apply)
{
    public bool Market => Kind == IdeaKind.Market;
}

// One listing a rule could keep: the pick it is, the stock and its bar, the session and its scored year, the
// list's order keys, the plan's stop in typical moves, and the plan's outcomes under the base's exits.
public readonly record struct IdeaListing(int Pick, int Name, int Bar, int Session, int Year, double RewardToRisk, double Strength, int Band, double StopMoves, SweepPlanOutcomes Plan);

// One listing the walk kept: what its trade came to in multiples of its risk, none where the history has not
// reached its end, and the same plan entered at the same close on every member that night.
public readonly record struct IdeaTrade(IdeaListing Listing, double? Result, double Benchmark);

// What one rule's trades came to over the eight scored years: the listings kept, the trades with a result, the
// nights a stock was listed against the nights scored, the edge with its standard error and the plain result, each
// year's trades, edge and total, the last three years' edge and total, the edge and the total without the five
// largest results by size, and the share of the trades whose stop sat nearer the buy than one typical move.
public sealed record IdeaFigures(
    string Key,
    int Listed,
    int Trades,
    int Nights,
    int ScoredNights,
    double? Edge,
    double? Result,
    double? StandardError,
    int[] YearTrades,
    double?[] YearEdge,
    double[] YearTotal,
    double? RecentEdge,
    double RecentTotal,
    double? EdgeWithoutLargest,
    double TotalWithoutLargest,
    double? CloseStops)
{
    public double NightShare => ScoredNights > 0 ? 1.0 * Nights / ScoredNights : 0;

    // The total over the eight years, a night with no trade counting nothing.
    public double Total => YearTotal.Sum();
}

// An idea's answers to the test against the rule it was added to: the years it was better in, those among the
// last three, the last three together no lower, still better without the five largest results by size, enough
// trades, enough nights listing a stock, and for a market switch its plain result a trade higher. A market switch
// is read on the year's total result, every other idea on the edge.
// see: A market switch is judged on the year's total result, since the edge subtracts what every member made that night
public sealed record IdeaTest(
    bool OnTotals,
    int YearsBetter,
    int RecentYearsBetter,
    bool RecentNoLower,
    bool BetterWithoutLargest,
    bool EnoughTrades,
    bool EnoughNights,
    bool ResultHigher)
{
    public bool YearsPass => YearsBetter >= SweepIdeas.YearsBetter && RecentYearsBetter >= SweepIdeas.RecentYearsBetter;

    public bool Passes => YearsPass && RecentNoLower && BetterWithoutLargest && EnoughTrades && EnoughNights && ResultHigher;
}

// One idea read on the base: its figures and its test against the base.
public sealed record IdeaReading(Idea Idea, IdeaFigures Figures, IdeaTest Test);

// One variant: what it changes from the starting point, in words, and its figures.
public sealed record IdeaVariant(string Key, string Change, IdeaFigures Figures);

// What the ideas' run proposes: today's rule, the base and its test against today's rule, every idea read on the
// base, the passing switches together where more than one passed, the ideas the starting point holds, its rule and
// figures with its tests against the base and today's rule, whether the base stands as a variant, and the variants.
public sealed record IdeasProposal(
    IdeaFigures Today,
    IdeaFigures Base,
    IdeaTest BaseAgainstToday,
    IReadOnlyList<IdeaReading> Ideas,
    IdeaReading? Together,
    IReadOnlyList<string> Kept,
    IdeaRule Start,
    IdeaFigures StartFigures,
    IdeaTest StartAgainstBase,
    IdeaTest StartAgainstToday,
    bool BaseIsVariant,
    IReadOnlyList<IdeaVariant> Variants);

// The ideas' run: today's rule with its reward-to-risk floor at 2 as the base, each new idea added to it one at a
// time and judged by one test, the ideas that pass combined into a starting point, and the variants one change
// from it, over the history the sweep reads and with its candidates, nothing written to the store.
// see: The base is today's rule with a reward-to-risk floor of 2 until the ideas' run says otherwise
// see: A new idea is added to the base one at a time and kept only where it is better in six of eight years
public static class SweepIdeas
{
    // The test, fixed before any result is read: better in 6 of the 8 years with 2 of the last 3, the last three
    // together no lower, still higher with the five largest results by size left out, at least 1,000 trades, and
    // a stock listed on at least 40% of the nights for every idea but a market switch.
    public const int YearsBetter = 6;

    public const int RecentYearsBetter = 2;

    public const int RecentYears = 3;

    public const int TradeFloor = 1000;

    public const double NightFloor = 0.40;

    public const int LargestLeftOut = SweepStages.LargestLeftOut;

    // The sessions back a falling VIX and rising breadth are read against, and the longest hold an exit carries.
    public const int Lookback = 10;

    public const int Cap = 63;

    // The VIX's level and the index's two averages, and the window a new high or low is read over.
    public const double VixLevel = 20;

    public const int FastAverage = 50;

    public const int SlowAverage = 200;

    public const int HighLowWindow = 252;

    // The base's own exit held at most these sessions, each one of the holds the candidates carry.
    public const int ShortHold = 10;

    public const int MiddleHold = 20;

    // The best three a night, and the trailing stops' distances in typical moves.
    public const int BestOf = 3;

    public const double TrailTwoMoves = 2;

    public const double TrailThreeMoves = 3;

    // The base's floor and the settings the variants and idea e read, each on the extended grid.
    public const double BaseRewardToRisk = 2;

    public static DialSetting Today { get; } = SweepGrid.Extended.Carry(SweepGrid.Coarse, DialSetting.LiveOnCoarse);

    public static int AtLeastAMove { get; } = SweepGrid.Extended.StopBounds.ToList().IndexOf((1, 4));

    public static int MarketAtFifty { get; } = SweepGrid.IndexOf(SweepGrid.Extended.MarketFloors, 0.50);

    public static int DepthFromOneAndAHalf { get; } = SweepGrid.IndexOf(SweepGrid.Extended.DepthLows, 1.5);

    public static IdeaRule TodaysRule { get; } = new(Today, IdeaExit.Base, int.MaxValue, []);

    public static IdeaRule BaseRule { get; } = TodaysRule with
    {
        Setting = Today with { RewardToRisk = SweepGrid.IndexOf(SweepGrid.Extended.RewardToRiskFloors, BaseRewardToRisk) },
    };

    static Func<IdeaRule, IdeaRule> Switch(MarketSwitch one) => rule => rule with { Switches = [.. rule.Switches.Append(one).Distinct()] };

    // The thirteen ideas, each setting fixed before any result was read.
    public static IReadOnlyList<Idea> All { get; } =
    [
        new("a1", "the share of members above their own 200-day average higher than ten sessions before", "Zaremba and others (2021): country indices with high breadth beat those with low breadth by 1.19% a month, a comparison of markets that times none. Weak evidence.", IdeaKind.Market, Switch(MarketSwitch.BreadthRising)),
        new("a2", "more members making a new 252-session high than a new 252-session low", "Practitioner use only, from Fosback (1979); no peer-reviewed support was found. Judgement.", IdeaKind.Market, Switch(MarketSwitch.HighsOverLows)),
        new("a3", "the index closing above its 50-session average", "No study was found for the shorter average. Judgement.", IdeaKind.Market, Switch(MarketSwitch.IndexOverFifty)),
        new("a4", "the index closing above its 200-session average", "Faber (2007): a 10-month average rule cut the worst fall from 83.66% to 42.24% with a similar return. Cooper, Gutierrez and Hameed (2004): momentum made 0.93% a month after up markets and lost 0.37% after down markets, 1929 to 1995. Evidence.", IdeaKind.Market, Switch(MarketSwitch.IndexOverTwoHundred)),
        new("a5", "the VIX closing under 20", "Wang and Xu (2015): momentum profits are larger when market volatility is low, 1929 to 2009. Daniel and Moskowitz (2016): momentum crashes follow market falls when volatility is high. Barroso and Santa-Clara (2015): scaling by realised volatility raised the Sharpe ratio from 0.53 to 0.97. All three read realised volatility and not the VIX. Evidence for the direction; the level of 20 is judgement.", IdeaKind.Market, Switch(MarketSwitch.VixUnderTwenty)),
        new("a6", "the VIX closing under its close ten sessions before", "The same three studies of realised volatility as a5. Evidence for the direction; the ten sessions are judgement.", IdeaKind.Market, Switch(MarketSwitch.VixFalling)),
        new("b", "after the buy, a session opening under the stop sells at its open and one whose low touches the stop sells at the stop, the stop read before the target, which stays on closes", "Han, Zhou and Zhu (2016): a 10% stop on momentum stocks, sold when the open or the close crossed it, cut the worst month from -49.79% to -11.36% and more than doubled the Sharpe ratio, 1926 to 2013. Kaminski and Lo (2014): stops add where returns persist and subtract under a random walk. Lo and Remorov (2017): tight stops underperform on US stocks after costs. Evidence for a stop; a touch at the day's low is judgement.", IdeaKind.Exit, rule => rule with { Exit = IdeaExit.TouchedStop }),
        new("c", "only the first three of the night's list, after a stock's open trade has kept it off, in the list's own order: reward to risk, then strength, then band strength, then ticker", "No study was found. Judgement.", IdeaKind.Selection, rule => rule with { PerNight = BestOf }),
        new("d2", "no target: the stop the higher of the plan's stop and the highest close since the buy less 2 typical moves, never lowered, sold at the first close under it, 63 sessions at most", "Dai, Marshall, Nguyen and Visaltanachoti (2021): trailing stops give a lower average return than holding and cut downside risk, and wider ones survive costs. Evidence, which predicts a lower average; the 2 typical moves are judgement.", IdeaKind.Exit, rule => rule with { Exit = IdeaExit.TrailTwo }),
        new("d3", "the same trailing stop at 3 typical moves", "The same study as d2. Evidence, which predicts a lower average; the 3 typical moves are judgement.", IdeaKind.Exit, rule => rule with { Exit = IdeaExit.TrailThree }),
        new("e", "the stop no closer than one typical move: the stop setting of 1 to 4", "Lo and Remorov (2017), on tight stops, loosely. Judgement.", IdeaKind.Selection, rule => rule with { Setting = rule.Setting with { Stop = AtLeastAMove } }),
        new("h10", "the base's own exit held at most 10 sessions", "No study of so short a hold was looked up. Judgement, the operator's idea of 2026-10-01.", IdeaKind.Exit, rule => rule with { Exit = IdeaExit.HoldTen }),
        new("h20", "the base's own exit held at most 20 sessions", "No study of so short a hold was looked up. Judgement, the operator's idea of 2026-10-01.", IdeaKind.Exit, rule => rule with { Exit = IdeaExit.HoldTwenty }),
    ];

    // The series each switch reads, none for the two read off the members' own bars.
    public static string? SeriesOf(MarketSwitch one) => one switch
    {
        MarketSwitch.IndexOverFifty or MarketSwitch.IndexOverTwoHundred => "GSPC",
        MarketSwitch.VixUnderTwenty or MarketSwitch.VixFalling => "VIX",
        _ => null,
    };

    // The ideas left out of a run for reading a series the store holds none of.
    public static IReadOnlyList<string> LeftOut(IReadOnlyList<SweepMarketSeries> market)
    {
        var held = market.Select(one => one.Series).ToHashSet(StringComparer.Ordinal);

        return
        [
            .. All
                .Where(idea => idea.Apply(BaseRule).Switches.Select(SeriesOf).OfType<string>().Any(series => !held.Contains(series)))
                .Select(idea => idea.Key),
        ];
    }

    // How many of the 256 ways eight years can fall, each better or not as a coin would have it, pass the yearly
    // test: 6 of the 8 better with 2 of the last 3.
    public static int LuckPatterns()
    {
        var passing = 0;

        for (var pattern = 0; pattern < 1 << SweepFigures.Years; pattern++)
        {
            var recent = BitOperations.PopCount((uint)(pattern >> (SweepFigures.Years - RecentYears)));

            passing += BitOperations.PopCount((uint)pattern) >= YearsBetter && recent >= RecentYearsBetter ? 1 : 0;
        }

        return passing;
    }

    // Walks one rule's listings night by night: on each session the listings of stocks no kept trade still holds,
    // in the list's own order, the first so many a night kept, each holding its stock through the session its
    // trade ended on or, where the history has not reached its end, through its cap.
    public static List<IdeaTrade> Walk(
        IEnumerable<IdeaListing> listings,
        IReadOnlyList<string> tickers,
        int perNight,
        Func<IdeaListing, (double? Result, int Sessions, double Benchmark)> exit)
    {
        var kept = new List<IdeaTrade>();
        var openUntil = new Dictionary<int, int>();

        foreach (var night in listings.GroupBy(listing => listing.Session).OrderBy(group => group.Key))
        {
            var taken = 0;

            foreach (var listing in night
                .OrderByDescending(one => one.RewardToRisk)
                .ThenByDescending(one => one.Strength)
                .ThenByDescending(one => one.Band)
                .ThenBy(one => tickers[one.Name], StringComparer.Ordinal))
            {
                if (taken == perNight)
                {
                    break;
                }

                if (openUntil.TryGetValue(listing.Name, out var held) && listing.Session <= held)
                {
                    continue;
                }

                var (result, sessions, benchmark) = exit(listing);

                openUntil[listing.Name] = listing.Session + sessions;
                kept.Add(new IdeaTrade(listing, result, result is null ? double.NaN : benchmark));
                taken++;
            }
        }

        return kept;
    }

    public static IdeaFigures Figures(string key, IReadOnlyList<IdeaTrade> trades, int scoredNights)
    {
        var years = SweepFigures.Years;
        var withResult = trades.Where(trade => trade.Result is not null && trade.Listing.Year is >= 0 and < SweepFigures.Years).ToArray();
        var edges = withResult.Where(trade => !double.IsNaN(trade.Benchmark)).ToArray();
        var yearTrades = new int[years];
        var yearEdge = new double?[years];
        var yearTotal = new double[years];

        for (var year = 0; year < years; year++)
        {
            var inYear = edges.Where(trade => trade.Listing.Year == year).ToArray();

            yearTrades[year] = withResult.Count(trade => trade.Listing.Year == year);
            yearEdge[year] = inYear.Length > 0 ? inYear.Average(EdgeOf) : null;
            yearTotal[year] = withResult.Where(trade => trade.Listing.Year == year).Sum(trade => trade.Result!.Value);
        }

        var recent = edges.Where(trade => trade.Listing.Year >= years - RecentYears).ToArray();
        var trimmedEdges = edges.OrderByDescending(trade => Math.Abs(trade.Result!.Value)).Skip(LargestLeftOut).ToArray();
        var trimmed = withResult.OrderByDescending(trade => Math.Abs(trade.Result!.Value)).Skip(LargestLeftOut).ToArray();
        double? edge = edges.Length > 0 ? edges.Average(EdgeOf) : null;
        double? error = null;

        if (edges.Length > 1 && edge is { } mean)
        {
            var variance = edges.Sum(trade => Math.Pow(EdgeOf(trade) - mean, 2)) / (edges.Length - 1);

            error = Math.Sqrt(variance / edges.Length);
        }

        return new IdeaFigures(
            key,
            trades.Count,
            withResult.Length,
            trades.Select(trade => trade.Listing.Session).Distinct().Count(),
            scoredNights,
            edge,
            withResult.Length > 0 ? withResult.Average(trade => trade.Result!.Value) : null,
            error,
            yearTrades,
            yearEdge,
            yearTotal,
            recent.Length > 0 ? recent.Average(EdgeOf) : null,
            yearTotal.Skip(years - RecentYears).Sum(),
            trimmedEdges.Length > 0 ? trimmedEdges.Average(EdgeOf) : null,
            trimmed.Sum(trade => trade.Result!.Value),
            withResult.Length > 0 ? 1.0 * withResult.Count(trade => trade.Listing.StopMoves < 1) / withResult.Length : null);
    }

    static double EdgeOf(IdeaTrade trade) => trade.Result!.Value - trade.Benchmark;

    // An idea's figures against the rule it was added to: on the edge, or on the year's total for a market
    // switch, which must also raise the plain result a trade and is held to no floor of nights.
    public static IdeaTest Test(IdeaFigures idea, IdeaFigures against, bool onTotals)
    {
        var better = 0;
        var recent = 0;

        for (var year = 0; year < SweepFigures.Years; year++)
        {
            var higher = onTotals
                ? idea.YearTotal[year] > against.YearTotal[year]
                : idea.YearEdge[year] is { } mine && against.YearEdge[year] is { } theirs && mine > theirs;

            if (higher)
            {
                better++;
                recent += year >= SweepFigures.Years - RecentYears ? 1 : 0;
            }
        }

        return new IdeaTest(
            onTotals,
            better,
            recent,
            onTotals ? idea.RecentTotal >= against.RecentTotal : idea.RecentEdge is { } edge && against.RecentEdge is { } theirsRecent && edge >= theirsRecent,
            onTotals ? idea.TotalWithoutLargest > against.TotalWithoutLargest : idea.EdgeWithoutLargest is { } trimmed && against.EdgeWithoutLargest is { } theirsTrimmed && trimmed > theirsTrimmed,
            idea.Trades >= TradeFloor,
            onTotals || idea.NightShare >= NightFloor,
            !onTotals || (idea.Result is { } result && against.Result is { } theirsResult && result > theirsResult));
    }

    // Whether an idea outside the starting point was higher than the base over the eight years: on the total for a
    // market switch, on the edge for every other.
    public static bool Higher(IdeaReading reading, IdeaFigures against) =>
        reading.Idea.Market
            ? reading.Figures.Total > against.Total
            : reading.Figures.Edge is { } edge && against.Edge is { } theirs && edge > theirs;

    // The proposal. Each idea is read on the base and tested against it. The market switches that pass are
    // switched on together and tested as one idea, the best single one by its total standing in where they do not
    // pass together; the exits are alternatives, so of those that pass the one with the higher edge is taken. The
    // starting point is the base with every idea taken, tested as a whole, on the total where it holds a switch;
    // where the whole fails, the ideas are added in order of their gain in edge over the base, each kept only
    // where the whole still passes; where none passes it is the base, and where the base fails against today's
    // rule it is today's rule with the base a variant. The variants are every idea outside the starting point
    // that was higher than the base, the market floor at 50% and the depth from 1.5, each one change from it.
    public static IdeasProposal Propose(IReadOnlyList<Idea> ideas, Func<string, IdeaRule, IdeaFigures> evaluate)
    {
        var today = evaluate("today's rule", TodaysRule);
        var baseFigures = evaluate("the base", BaseRule);
        var baseAgainstToday = Test(baseFigures, today, onTotals: false);
        var readings = new List<IdeaReading>();

        foreach (var idea in ideas)
        {
            var figures = evaluate(idea.Key, idea.Apply(BaseRule));

            readings.Add(new IdeaReading(idea, figures, Test(figures, baseFigures, idea.Market)));
        }

        var switches = readings.Where(reading => reading.Idea.Market && reading.Test.Passes).ToArray();
        IdeaReading? together = null;
        IdeaReading? market = switches.Length == 1 ? switches[0] : null;

        if (switches.Length > 1)
        {
            var all = new Idea(
                string.Join("+", switches.Select(reading => reading.Idea.Key)),
                "the passing switches together: " + string.Join("; ", switches.Select(reading => reading.Idea.Rule)),
                "Each as its own idea states.",
                IdeaKind.Market,
                rule => switches.Aggregate(rule, (held, reading) => reading.Idea.Apply(held)));
            var figures = evaluate(all.Key, all.Apply(BaseRule));

            together = new IdeaReading(all, figures, Test(figures, baseFigures, onTotals: true));
            market = together.Test.Passes
                ? together
                : switches.OrderByDescending(reading => reading.Figures.Total).ThenBy(reading => reading.Idea.Key, StringComparer.Ordinal).First();
        }

        var exit = readings
            .Where(reading => reading.Idea.Kind == IdeaKind.Exit && reading.Test.Passes)
            .OrderByDescending(reading => reading.Figures.Edge ?? double.MinValue)
            .ThenBy(reading => reading.Idea.Key, StringComparer.Ordinal)
            .FirstOrDefault();
        var taken = readings.Where(reading => reading.Idea.Kind == IdeaKind.Selection && reading.Test.Passes).ToList();

        if (market is not null)
        {
            taken.Add(market);
        }

        if (exit is not null)
        {
            taken.Add(exit);
        }

        var chosen = taken
            .OrderByDescending(reading => (reading.Figures.Edge ?? double.MinValue) - (baseFigures.Edge ?? 0))
            .ThenBy(reading => reading.Idea.Key, StringComparer.Ordinal)
            .ToList();

        IdeaRule Applied(IEnumerable<IdeaReading> held) => held.Aggregate(BaseRule, (rule, reading) => reading.Idea.Apply(rule));

        bool Passes(IReadOnlyList<IdeaReading> held)
        {
            var rule = Applied(held);

            return Test(evaluate(string.Join("+", held.Select(reading => reading.Idea.Key)), rule), baseFigures, rule.HasSwitch).Passes;
        }

        var kept = new List<IdeaReading>();

        if (chosen.Count > 0)
        {
            if (Passes(chosen))
            {
                kept.AddRange(chosen);
            }
            else
            {
                foreach (var reading in chosen)
                {
                    if (Passes([.. kept, reading]))
                    {
                        kept.Add(reading);
                    }
                }
            }
        }

        var baseIsVariant = !baseAgainstToday.Passes;

        if (baseIsVariant)
        {
            kept.Clear();
        }

        var start = baseIsVariant ? TodaysRule : Applied(kept);
        var startFigures = baseIsVariant ? today : kept.Count == 0 ? baseFigures : evaluate("the starting point", start);
        var inStart = new HashSet<string>(StringComparer.Ordinal);

        foreach (var reading in kept)
        {
            inStart.UnionWith(ReferenceEquals(reading, together) ? switches.Select(one => one.Idea.Key) : [reading.Idea.Key]);
        }

        var variants = new List<IdeaVariant>();

        if (baseIsVariant)
        {
            variants.Add(new IdeaVariant("the base", "the reward-to-risk floor at 2", baseFigures));
        }

        foreach (var reading in readings.Where(reading => !inStart.Contains(reading.Idea.Key) && Higher(reading, baseFigures)))
        {
            variants.Add(new IdeaVariant(reading.Idea.Key, reading.Idea.Rule, evaluate(reading.Idea.Key + " on the starting point", reading.Idea.Apply(start))));
        }

        variants.Add(new IdeaVariant("market 50%", "the market floor at 50%", evaluate("market 50% on the starting point", start with { Setting = start.Setting with { Market = MarketAtFifty } })));
        variants.Add(new IdeaVariant("depth 1.5", "the pullback's depth from 1.5 typical moves", evaluate("depth 1.5 on the starting point", start with { Setting = start.Setting with { DepthLow = DepthFromOneAndAHalf } })));

        return new IdeasProposal(
            today,
            baseFigures,
            baseAgainstToday,
            readings,
            together,
            [.. kept.Select(reading => reading.Idea.Key)],
            start,
            startFigures,
            Test(startFigures, baseFigures, start.HasSwitch),
            Test(startFigures, today, start.HasSwitch),
            baseIsVariant,
            variants);
    }

    // The share of the members holding a 200-day average whose close stood above it, each session, and none on a
    // session where no member held one.
    public static double[] BreadthAboveTheSlowAverage(IReadOnlyList<SweepSeries> series, SweepBenchmark.Members members, int sessions)
    {
        var shares = new double[sessions];

        for (var session = 0; session < sessions; session++)
        {
            var above = 0;
            var held = 0;

            for (var at = 0; at < members.Names[session].Length; at++)
            {
                var one = series[members.Names[session][at]];
                var bar = members.Bars[session][at];

                if (double.IsNaN(one.Sma200[bar]))
                {
                    continue;
                }

                held++;
                above += Statistic.FromPrice(one.Bars[bar].Close) > one.Sma200[bar] ? 1 : 0;
            }

            shares[session] = held > 0 ? 1.0 * above / held : double.NaN;
        }

        return shares;
    }

    // How many members made a new high and a new low each session: a high the highest of the 252 sessions of its
    // own series ending on the session, and a low the lowest, a member holding fewer read for neither.
    public static (int[] Highs, int[] Lows) HighsAndLows(IReadOnlyList<SweepSeries> series, SweepBenchmark.Members members, int sessions)
    {
        var highs = new int[sessions];
        var lows = new int[sessions];
        var newHigh = new bool[series.Count][];
        var newLow = new bool[series.Count][];

        Parallel.For(0, series.Count, name =>
        {
            var bars = series[name].Bars;

            newHigh[name] = Extremes(bars.Select(bar => bar.High).ToArray(), highest: true);
            newLow[name] = Extremes(bars.Select(bar => bar.Low).ToArray(), highest: false);
        });

        for (var session = 0; session < sessions; session++)
        {
            for (var at = 0; at < members.Names[session].Length; at++)
            {
                var name = members.Names[session][at];
                var bar = members.Bars[session][at];

                highs[session] += newHigh[name][bar] ? 1 : 0;
                lows[session] += newLow[name][bar] ? 1 : 0;
            }
        }

        return (highs, lows);
    }

    // Whether each bar's value is the highest, or the lowest, of the window ending on it, read only where the
    // series holds the whole window.
    public static bool[] Extremes(decimal[] values, bool highest)
    {
        var found = new bool[values.Length];
        var window = new LinkedList<int>();

        for (var bar = 0; bar < values.Length; bar++)
        {
            while (window.First is { } oldest && oldest.Value <= bar - HighLowWindow)
            {
                window.RemoveFirst();
            }

            while (window.Last is { } newest && (highest ? values[newest.Value] <= values[bar] : values[newest.Value] >= values[bar]))
            {
                window.RemoveLast();
            }

            window.AddLast(bar);
            found[bar] = bar >= HighLowWindow - 1 && window.First!.Value == bar;
        }

        return found;
    }

    // A market series' closes on the history's calendar, none on a session it does not hold; a day it holds that
    // the calendar does not, an exchange holiday, is passed over.
    public static double[] OnCalendar(SweepMarketSeries? market, IReadOnlyList<DateOnly> calendar)
    {
        var closes = new double[calendar.Count];

        Array.Fill(closes, double.NaN);

        if (market is null)
        {
            return closes;
        }

        var at = calendar.Select((session, index) => (session, index)).ToDictionary(pair => pair.session, pair => pair.index);

        foreach (var (session, close) in market.Closes)
        {
            if (at.TryGetValue(session, out var index))
            {
                closes[index] = close;
            }
        }

        return closes;
    }

    // The average of the given count of closes ending on each session, none where any of them is missing.
    public static double[] Average(double[] closes, int count)
    {
        var averages = new double[closes.Length];

        for (var session = 0; session < closes.Length; session++)
        {
            averages[session] = double.NaN;

            if (session + 1 < count)
            {
                continue;
            }

            var sum = 0.0;
            var whole = true;

            for (var back = 0; back < count && whole; back++)
            {
                var close = closes[session - back];

                whole = !double.IsNaN(close);
                sum += close;
            }

            averages[session] = whole ? sum / count : double.NaN;
        }

        return averages;
    }

    // Whether each switch passes on each session, read on the session's close as it stood, a reading the history
    // cannot make failing the switch, as a gate fails on an absent value.
    public static bool[][] Switches(double[] breadth, int[] highs, int[] lows, double[] index, double[] vix)
    {
        var sessions = breadth.Length;
        var fast = Average(index, FastAverage);
        var slow = Average(index, SlowAverage);
        var passes = new bool[Enum.GetValues<MarketSwitch>().Length][];

        for (var one = 0; one < passes.Length; one++)
        {
            passes[one] = new bool[sessions];
        }

        for (var session = 0; session < sessions; session++)
        {
            var back = session - Lookback;

            passes[(int)MarketSwitch.BreadthRising][session] = back >= 0 && breadth[session] > breadth[back];
            passes[(int)MarketSwitch.HighsOverLows][session] = highs[session] > lows[session];
            passes[(int)MarketSwitch.IndexOverFifty][session] = index[session] > fast[session];
            passes[(int)MarketSwitch.IndexOverTwoHundred][session] = index[session] > slow[session];
            passes[(int)MarketSwitch.VixUnderTwenty][session] = vix[session] < VixLevel;
            passes[(int)MarketSwitch.VixFalling][session] = back >= 0 && vix[session] < vix[back];
        }

        return passes;
    }
}

// The base's candidates as the ideas read them: the live design's picks, each idea's rule walked over them, its
// exits read off the candidates where the base's own exit carries them and walked over the members' bars where an
// idea puts a new one in its place, and the benchmark for each exit the same plan on every member that night.
public sealed class IdeaReplay
{
    readonly IReadOnlyList<SweepSeries> series;
    readonly IReadOnlyList<SweepCandidate> candidates;
    readonly List<SweepPick> picks;
    readonly SweepBenchmark.Members members;
    readonly bool[][] switches;
    readonly string[] tickers;
    readonly int nights;
    readonly double[][] opens;
    readonly double[][] lows;
    readonly double[][] closes;
    readonly Dictionary<(int Pick, IdeaExit Exit), (double? Result, int Sessions, double Benchmark)> walked = [];
    readonly Dictionary<string, IdeaFigures> read = new(StringComparer.Ordinal);

    public IdeaReplay(IReadOnlyList<SweepSeries> series, IReadOnlyList<SweepCandidate> candidates, SweepBenchmark.Members members, bool[][] switches, int nights)
    {
        this.series = series;
        this.candidates = candidates;
        this.members = members;
        this.switches = switches;
        this.nights = nights;
        picks = SweepStages.Picks(candidates, SweepDesign.Live);
        tickers = [.. series.Select(one => one.Name.Ticker)];
        opens = [.. series.Select(one => one.Bars.Select(bar => Statistic.FromPrice(bar.Open)).ToArray())];
        lows = [.. series.Select(one => one.Bars.Select(bar => Statistic.FromPrice(bar.Low)).ToArray())];
        closes = [.. series.Select(one => one.Bars.Select(bar => Statistic.FromPrice(bar.Close)).ToArray())];
    }

    public int Picks => picks.Count;

    // One rule's figures, each rule read once whatever name it is asked under.
    public IdeaFigures Evaluate(string key, IdeaRule rule)
    {
        if (!read.TryGetValue(rule.Key, out var figures))
        {
            figures = SweepIdeas.Figures(key, Trades(rule), nights);
            read[rule.Key] = figures;
        }

        return figures with { Key = key };
    }

    public List<IdeaTrade> Trades(IdeaRule rule)
    {
        var listings = new List<IdeaListing>();

        foreach (var pick in picks)
        {
            if (!pick.Corner.Passes(rule.Setting) || rule.Switches.Any(one => !switches[(int)one][pick.Session]))
            {
                continue;
            }

            var candidate = candidates[pick.Index];

            listings.Add(new IdeaListing(
                pick.Index,
                pick.Name,
                Array.BinarySearch(series[pick.Name].SessionAt, pick.Session),
                pick.Session,
                pick.Year,
                pick.Plan.RewardToRisk,
                candidate.Strength[(int)SweepDesign.Live.Strength],
                candidate.Band[(int)SweepDesign.Live.Support],
                pick.Plan.StopMoves,
                pick.Plan));
        }

        return SweepIdeas.Walk(listings, tickers, rule.PerNight, listing => Exit(listing, rule.Exit));
    }

    (double? Result, int Sessions, double Benchmark) Exit(IdeaListing listing, IdeaExit exit)
    {
        if (exit is IdeaExit.Base or IdeaExit.HoldTen or IdeaExit.HoldTwenty)
        {
            var index = SweepAxes.ExitIndex(exit switch { IdeaExit.HoldTen => SweepIdeas.ShortHold, IdeaExit.HoldTwenty => SweepIdeas.MiddleHold, _ => SweepIdeas.Cap }, breakEven: false);
            var plan = listing.Plan;
            double? result = plan.Code[index] is SweepPlanOutcomes.Win or SweepPlanOutcomes.Loss or SweepPlanOutcomes.Unresolved && !float.IsNaN(plan.Multiple[index])
                ? plan.Multiple[index]
                : null;

            return (result, plan.Ends[index], plan.Benchmark[index]);
        }

        if (!walked.TryGetValue((listing.Pick, exit), out var outcome))
        {
            outcome = Walked(listing, exit);
            walked[(listing.Pick, exit)] = outcome;
        }

        return outcome;
    }

    // A new exit walked over the listing's own bars, its plan the buy at the close with the stop and the target
    // its stop distance and its reward to risk state, and its benchmark the same plan on every member that night.
    (double? Result, int Sessions, double Benchmark) Walked(IdeaListing listing, IdeaExit exit)
    {
        var name = listing.Name;
        var bar = listing.Bar;
        var entry = closes[name][bar];
        var move = series[name].Atr[bar];
        var stop = entry - (listing.StopMoves * move);
        var target = entry + (listing.RewardToRisk * (entry - stop));
        var trail = exit == IdeaExit.TrailThree ? SweepIdeas.TrailThreeMoves : SweepIdeas.TrailTwoMoves;
        var result = exit == IdeaExit.TouchedStop
            ? SweepWalk.TouchedStop(opens[name], lows[name], closes[name], bar, entry, stop, target, SweepIdeas.Cap, out var held)
            : FamilyWalks.Trailing(closes[name], bar, entry, stop, trail * move, SweepIdeas.Cap, out held);
        var sessionAt = series[name].SessionAt;
        var sessions = bar + held < sessionAt.Length ? sessionAt[bar + held] - sessionAt[bar] : held;

        return (result, sessions, Benchmark(listing.Session, listing.StopMoves, listing.RewardToRisk, exit, trail));
    }

    public double Benchmark(int session, double stopMoves, double rewardToRisk, IdeaExit exit, double trail)
    {
        var names = members.Names[session];
        var bars = members.Bars[session];
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

            var result = exit == IdeaExit.TouchedStop
                ? SweepWalk.TouchedStop(opens[name], lows[name], closes[name], bar, entry, entry - risk, entry + (rewardToRisk * risk), SweepIdeas.Cap, out _)
                : FamilyWalks.Trailing(closes[name], bar, entry, entry - risk, trail * move, SweepIdeas.Cap, out _);

            if (result is { } made)
            {
                sum += made;
                count++;
            }
        }

        return count > 0 ? sum / count : double.NaN;
    }
}
