using System.Globalization;
using EquityBrief.Core.Candidates;
using EquityBrief.Core.Families;
using EquityBrief.Core.Prices;
using EquityBrief.Core.Readings;
using EquityBrief.Core.Releases;
using EquityBrief.Core.Sweep;
using EquityBrief.Worker.Candidates;
using EquityBrief.Worker.Sweep;

namespace EquityBrief.Worker.Indices;

// One rule of an index's swing family as the register states it: its name, its evaluator, its parameters and the
// instant it was registered.
public sealed record IndexRule(string Candidate, IndexRuleCandidate Evaluator, IReadOnlyDictionary<string, double> Parameters, DateTimeOffset RegisteredAt)
{
    public string Index => Evaluator.Index;

    public string Family => Evaluator.SetupFamily;

    public bool Live => FamilyRecords.IsLive(Candidate);
}

// The readings a rule's levels read on the night, as the member readings, the switches and the S&P 500's market reading
// stored them: each member's industry's return over a month and a quarter and its coverage, each member's peers'
// surprise on each session the rows hold, the index fund against SPY over half a year and a year, the credit fund
// against its average and its own past, and the S&P 500's breadth; and from 20.4 the release of each print a drift's
// reaction can reach, by its ticker and report date, under each reading version a standing rule names; and from 20.11
// each member's quarters' revenue as first filed, over the facts filed before the session before the night, which are
// those the night's filings refresh had stored, read only where a standing drift rule reads it.
// see: A member's coverage is read on the night only over quarters whose fetch read their interest expense
// see: The drift's tone variant lists only where its print's release reads better than expected and its outlook was not lowered or withdrawn
// see: A drift's revenue level on the S&P 400 and 600 reads a quarter only once its filing is stored, on the night and in its sweep alike
public sealed record IndexLevels(
    IReadOnlyDictionary<string, (double? Month, double? Quarter)> Industry,
    IReadOnlyDictionary<string, bool?> Coverage,
    IReadOnlyDictionary<(string Ticker, DateOnly Session), double?> PeerSurprise,
    double? FundHalfYear,
    double? FundYear,
    double? CreditAverage,
    double? CreditChange,
    double? LargeBreadth,
    IReadOnlyDictionary<(string Ticker, DateOnly Report, long Version), PrintRelease>? Releases = null,
    IReadOnlyDictionary<string, IReadOnlyList<QuarterRevenue>>? Revenue = null)
{
    public static IndexLevels None { get; } = new(
        new Dictionary<string, (double?, double?)>(StringComparer.Ordinal),
        new Dictionary<string, bool?>(StringComparer.Ordinal),
        new Dictionary<(string, DateOnly), double?>(),
        null, null, null, null, null);
}

// The registered rules of the S&P 400's and 600's swing families. A freeze registers a family's live rule on an index
// and up to eight variants at one instant, each at settings its index's sweep read: the family's own dials on its
// sweep's grid and the levels its second stage read beside them. On the night each standing rule of an index is read
// through the night reader's own functions at its settings, its levels from the readings the night stored, and keeps
// its own list, five a night in its family's order with one open trade a stock of its own, and its trades with each
// one's result, its round trip at the published table and its benchmark, the same plan on every member of the index
// that night. A family's live rule draws the index's page in the provisional rule's place.
// see: A rule of the S&P 400's or 600's swing families is registered as the family on its index and evaluated by their step alone
// see: A 400 or 600 trade pays the published effective spread for its size and price, and its pass tests read the edge after it
public static class IndexRules
{
    // The reasons a level keeps a listed member off, after the floors and the quality.
    public const string IndustryFell = "its industry fell";

    public const string SwitchClosed = "a switch its rule reads closed";

    public const string NotNearItsHigh = "not near enough its year's high";

    public const string HighNotAsStated = "its year's high not as recent as its rule states";

    public const string VolumeUnder = "volume under its rule's multiple";

    public const string PeersNotAbove = "its peers' surprise not above nothing";

    // From 20.4 the reasons the tone condition keeps a listed drift member off: its print's release read under the rule's
    // version and failing, and no reading of it held under the version.
    public const string ToneFailed = ReleaseTone.FailedOnTheIndex;

    public const string ToneNotHeld = ReleaseTone.NotHeldOnTheIndex;

    // From 20.11 the reason the revenue level keeps a listed drift member off: its print's quarter's revenue not above the
    // same quarter's a year before, or not both read as first filed and stored before the night.
    public const string RevenueNotAbove = "its print's revenue not above the same quarter's a year before";

    // The most rules a family keeps a night, and the most variants a freeze registers beside its live rule.
    public const int PerNight = 5;

    public const int MostVariants = CandidateFamily.Maximum - 1;

    // Each level's values a rule may state, the values its index's second stage read, nought where the level is off.
    static readonly IReadOnlyDictionary<string, double[]> LevelValues = new Dictionary<string, double[]>(StringComparer.Ordinal)
    {
        [IndexRuleCandidate.FloorsParameter] = [Statistic.FromPrice(IndexSweepRunner.DollarVolumeHalf), 1, 2],
        [IndexRuleCandidate.LowestCloseParameter] = [Statistic.FromPrice(MemberReadings.LowestPrice), Statistic.FromPrice(IndexSweepRunner.HigherPrice)],
        [IndexRuleCandidate.QualityParameter] = [(int)IndexQuality.Off, (int)IndexQuality.Profit, (int)IndexQuality.Cover],
        [IndexRuleCandidate.HoldParameter] = [0, 21, 42, 63],
        [IndexRuleCandidate.IndustryFallParameter] = [0, .. MemberReadings.IndustryWindows],
        [IndexRuleCandidate.FundOverSpyParameter] = [0, .. IndexSwitches.SmallWindows],
        [IndexRuleCandidate.CreditAverageParameter] = [0, 1],
        [IndexRuleCandidate.CreditChangeParameter] = [0, 1],
        [IndexBreakoutCandidate.NearnessParameter] = [0, IndexSweepRunner.NearHigh, 1],
        [IndexBreakoutCandidate.RecencyParameter] = [0, IndexSweepRunner.RecentHighSessions, -IndexSweepRunner.RecentHighSessions],
        [IndexBreakoutCandidate.HighVolumeParameter] = [0, IndexSweepRunner.HighestVolume],
        [IndexDriftCandidate.StopFloorParameter] = [0, 1],
        [IndexDriftCandidate.WideWindowParameter] = [0, IndexSweepRunner.DriftWideWindow],
        [IndexDriftCandidate.PeersParameter] = [0, 1],
        [IndexRuleCandidate.BreadthFloorParameter] = [0, .. IndexRuleCandidate.BreadthFloors],
        [IndexDriftCandidate.EntryParameter] = [0, 1],
        [IndexDriftCandidate.RevenueGrewParameter] = [0, 1],
    };

    // Why a rule's parameters cannot be registered: a value off its dial's grid or off its level's values, the first
    // named; none where every value is one its index's sweep read or the breadth's third value, no market check.
    public static string? Refusal(IndexRuleCandidate evaluator, IReadOnlyDictionary<string, double> parameters)
    {
        // The engines' hooks a swing rule may state beside its own parameters are read by its keeping and its walk and
        // not by its evaluation; a heavyweights book reads none.
        // see: Every engine's settings hooks land together and all default off, so the families' pins move once
        var hooked = evaluator is not IndexHeavyweightCandidate;

        if (hooked)
        {
            try
            {
                EquityBrief.Core.Loop.RuleHooks.Of(parameters);
            }
            catch (ArgumentException refused)
            {
                return refused.Message;
            }
        }

        if (parameters.Keys.Where(name => !hooked || !EquityBrief.Core.Loop.RuleHooks.IsHook(name)).Except([.. evaluator.Parameters, .. ReleaseTone.OptionalOf(evaluator)], StringComparer.Ordinal).FirstOrDefault() is { } unread)
        {
            return $"'{evaluator.Name}' reads no parameter named '{unread}'.";
        }

        if (evaluator.Parameters.FirstOrDefault(name => !parameters.ContainsKey(name)) is { } missing)
        {
            return $"'{evaluator.Name}' reads '{missing}', and the registration states none.";
        }

        // The breadth the market check reads: the index's own and the S&P 500's, the two its index's sweep read, or none.
        // A pullback reading none states its own market dial off as well, since the provisional pullback's setting reads
        // the index's breadth at the same floor inside it. From 20.11 the floor the breadth is read against: one of the
        // four its index's sweep read where the check reads the index's own breadth, the S&P 500 filter's own where it
        // reads the S&P 500's, and nought where it reads none; and a pullback's own market dial, where it is on, at the
        // floor its check reads, so the night reads one floor.
        // see: Each frozen rule on the S&P 400 and 600 reads its market check at the level its own sweep chose
        if (parameters.TryGetValue(IndexRuleCandidate.LargeBreadthParameter, out var breadth))
        {
            if (breadth is not (0 or 1 or IndexRuleCandidate.NoMarketCheck))
            {
                return FormattableString.Invariant($"'{IndexRuleCandidate.LargeBreadthParameter}' is {Number(breadth)}, and a rule's market check reads the index's own breadth at 0, the S&P 500's at 1 and none at {Number(IndexRuleCandidate.NoMarketCheck)} alone.");
            }

            if (breadth == IndexRuleCandidate.NoMarketCheck
                && evaluator is IndexPullbackCandidate
                && parameters.TryGetValue(IndexPullbackCandidate.MarketParameter, out var market)
                && market != 0)
            {
                return FormattableString.Invariant($"a pullback reading no market check states its own market dial off, and '{IndexPullbackCandidate.MarketParameter}' is {Number(market)}.");
            }

            if (parameters.TryGetValue(IndexRuleCandidate.BreadthFloorParameter, out var floor))
            {
                var wanted = breadth switch
                {
                    IndexRuleCandidate.NoMarketCheck => floor == 0 ? null : "0, since a rule reading no market check reads no floor",
                    1 => floor == IndexRuleCandidate.LargeBreadthFloor ? null : FormattableString.Invariant($"{Number(IndexRuleCandidate.LargeBreadthFloor)}, the S&P 500 filter's own floor, since the rule reads the S&P 500's breadth"),
                    _ => IndexRuleCandidate.BreadthFloors.Contains(floor) ? null : FormattableString.Invariant($"one of {string.Join(", ", IndexRuleCandidate.BreadthFloors.Select(Number))}, the floors its index's sweep read the index's own breadth at"),
                };

                if (wanted is not null)
                {
                    return FormattableString.Invariant($"'{IndexRuleCandidate.BreadthFloorParameter}' is {Number(floor)}, and it must be {wanted}.");
                }

                if (evaluator is IndexPullbackCandidate && parameters.TryGetValue(IndexPullbackCandidate.MarketParameter, out var dial) && dial != 0 && dial != floor)
                {
                    return FormattableString.Invariant($"a pullback whose own market dial is on reads it at its market check's floor, and '{IndexPullbackCandidate.MarketParameter}' is {Number(dial)} where '{IndexRuleCandidate.BreadthFloorParameter}' is {Number(floor)}.");
                }
            }
        }

        foreach (var (name, values) in LevelValues.Where(pair => parameters.ContainsKey(pair.Key)))
        {
            if (!values.Contains(parameters[name]))
            {
                return FormattableString.Invariant($"'{name}' is {parameters[name]}, and its index's sweep read it at {string.Join(", ", values.Select(Number))} alone.");
            }
        }

        if (parameters.TryGetValue(IndexRuleCandidate.HoldParameter, out var hold) && hold == 63 && evaluator is not IndexDriftCandidate)
        {
            return "a hold of 63 sessions is the drift's alone, the breakout's and the pullback's own cap being 63 already.";
        }

        return evaluator switch
        {
            IndexBreakoutCandidate => OffTheGrid(BreakoutSweep.Grid, BreakoutValues(parameters)),
            IndexDriftCandidate => OffTheGrid(DriftSweep.Grid, DriftValues(parameters)),
            IndexHeavyweightCandidate => HeavyweightRefusal(parameters),
            _ => PullbackOf(parameters) is null ? "the pullback's dials name a value the extended grid does not hold." : null,
        };
    }

    // Each heavyweights design's own dials and the values its sweep read, a size cut or an industries' count of nought
    // standing for every one; the exits as the pair of whether a holding is sold on no longer leading and under its
    // 200-day average, never neither.
    static readonly (string Name, double[] Values)[] DesignADials =
    [
        (IndexHeavyweightCandidate.LargestParameter, Stated(HeavyweightSweep.Sizes, HeavyweightSweep.EveryCompany)),
        (IndexHeavyweightCandidate.LookBackParameter, Stated(HeavyweightSweep.LookBacks)),
        (IndexHeavyweightCandidate.LeadersParameter, Stated(HeavyweightSweep.LeaderCounts)),
        (IndexHeavyweightCandidate.BetaParameter, [0, 1]),
        (IndexHeavyweightCandidate.LeadingExitParameter, [0, 1]),
        (IndexHeavyweightCandidate.AverageExitParameter, [0, 1]),
        (IndexHeavyweightCandidate.TwelveOneParameter, [0, 1]),
    ];

    static readonly (string Name, double[] Values)[] DesignBDials =
    [
        (IndexHeavyweightCandidate.WindowParameter, Stated(IndexSweepRunner.LeadWindows)),
        (IndexHeavyweightCandidate.IndustriesParameter, Stated(IndexSweepRunner.IndustriesKept, IndexSweepRunner.EveryLeading)),
        (IndexHeavyweightCandidate.MembersParameter, Stated(IndexSweepRunner.MembersPerIndustry)),
    ];

    // A dial's levels as a registration states them, the level standing for every one stated as nought.
    static double[] Stated(IReadOnlyList<int> levels, int every = 0) =>
        [.. levels.Select<int, double>(level => level == every && every != 0 ? 0 : level)];

    // Why a heavyweights rule's parameters cannot be registered: a design that is neither, a dial of its design off the
    // values its sweep read, a dial of the other design not nought, a holding sold on neither exit, or a floor its sweeps
    // did not read; none where every value is one its design's sweep read.
    static string? HeavyweightRefusal(IReadOnlyDictionary<string, double> p)
    {
        var design = p[IndexHeavyweightCandidate.DesignParameter];

        if (design is not (IndexHeavyweightCandidate.DesignA or IndexHeavyweightCandidate.DesignB))
        {
            return FormattableString.Invariant($"'{IndexHeavyweightCandidate.DesignParameter}' is {Number(design)}, and the heavyweights read design (a) as 0 and design (b) as 1 alone.");
        }

        var (own, other) = design == IndexHeavyweightCandidate.DesignA ? (DesignADials, DesignBDials) : (DesignBDials, DesignADials);

        foreach (var (name, values) in own)
        {
            if (!values.Contains(p[name]))
            {
                return FormattableString.Invariant($"'{name}' is {Number(p[name])}, and its design's sweep read it at {string.Join(", ", values.Select(Number))} alone.");
            }
        }

        if (other.FirstOrDefault(dial => p[dial.Name] != 0) is { Name: { } stated })
        {
            return FormattableString.Invariant($"'{stated}' is {Number(p[stated])}, a dial of the other design, which this design's registration states as 0.");
        }

        if (design == IndexHeavyweightCandidate.DesignA && p[IndexHeavyweightCandidate.LeadingExitParameter] == 0 && p[IndexHeavyweightCandidate.AverageExitParameter] == 0)
        {
            return "design (a) sells a holding on no longer leading, under its 200-day average or on both, and the registration states neither.";
        }

        return p[IndexRuleCandidate.FloorsParameter] is 1 or 2
            ? null
            : FormattableString.Invariant($"'{IndexRuleCandidate.FloorsParameter}' is {Number(p[IndexRuleCandidate.FloorsParameter])}, and the heavyweights' sweeps read it at 1 and 2 alone.");
    }

    // A grid's dial a value is not on, named; none where each value is one of its dial's levels.
    static string? OffTheGrid(FamilyGrid grid, double[] values) =>
        Enumerable.Range(0, grid.Dials.Count)
            .Where(dial => !grid.Dials[dial].Levels.Contains(values[dial]))
            .Select(dial => FormattableString.Invariant($"'{grid.Dials[dial].Dial}' is {Number(values[dial])}, and its sweep's grid holds {string.Join(", ", grid.Dials[dial].Levels.Select(Number))}."))
            .FirstOrDefault();

    // The breakout's four dials as its grid holds them, a ceiling of nought standing for none.
    static double[] BreakoutValues(IReadOnlyDictionary<string, double> p) =>
    [
        p[IndexBreakoutCandidate.HighSessionsParameter],
        p[IndexBreakoutCandidate.VolumeMultipleParameter],
        p[IndexBreakoutCandidate.RangeCeilingParameter] == 0 ? double.PositiveInfinity : p[IndexBreakoutCandidate.RangeCeilingParameter],
        p[IndexBreakoutCandidate.StopMovesParameter],
    ];

    static double[] DriftValues(IReadOnlyDictionary<string, double> p) =>
    [
        p[IndexDriftCandidate.WindowSessionsParameter],
        p[IndexDriftCandidate.ReactionMovesParameter],
        p[IndexDriftCandidate.VolumeMultipleParameter],
        p[IndexDriftCandidate.TargetMultipleParameter],
    ];

    // The pullback's setting on the extended grid, none where a value is not one the grid holds.
    static DialSetting? PullbackOf(IReadOnlyDictionary<string, double> p)
    {
        var grid = SweepGrid.Extended;
        int[] at =
        [
            SweepGrid.IndexOf(grid.StrengthBars, p[IndexPullbackCandidate.StrengthParameter]),
            SweepGrid.IndexOf(grid.DepthLows, p[IndexPullbackCandidate.DepthLowParameter]),
            SweepGrid.IndexOf(grid.DepthHighs, p[IndexPullbackCandidate.DepthHighParameter]),
            SweepGrid.IndexOf(grid.DryUpCeilings, p[IndexPullbackCandidate.DryUpParameter] == 0 ? SweepGrid.Off : p[IndexPullbackCandidate.DryUpParameter]),
            grid.Freshness.ToList().FindIndex(level => level == p[IndexPullbackCandidate.FreshnessParameter]),
            SweepGrid.IndexOf(grid.RewardToRiskFloors, p[IndexPullbackCandidate.RewardToRiskParameter]),
            grid.StopBounds.ToList().FindIndex(bounds => bounds.Low == p[IndexPullbackCandidate.StopLowParameter] && bounds.High == p[IndexPullbackCandidate.StopHighParameter]),
            SweepGrid.IndexOf(grid.MarketFloors, p[IndexPullbackCandidate.MarketParameter] == 0 ? SweepGrid.MarketOff : p[IndexPullbackCandidate.MarketParameter]),
            grid.BandStrengths.ToList().FindIndex(level => level == p[IndexPullbackCandidate.BandParameter]),
        ];

        return at.Any(place => place < 0) ? null : new DialSetting(at[0], at[1], at[2], at[3], at[4], at[5], at[6], at[7], at[8]);
    }

    // A family's provisional rule on an index as a registration states it, which a freeze moves from.
    public static IReadOnlyDictionary<string, double> Provisional(IndexRuleCandidate evaluator)
    {
        // The heavyweights' provisional book: design (a) at the S&P 500's frozen settings within the index, its quality
        // the profit gate and its dollar volume floor the index's own.
        if (evaluator is IndexHeavyweightCandidate)
        {
            var book = IndexHeavyweights.Provisional;

            return new Dictionary<string, double>(StringComparer.Ordinal)
            {
                [IndexHeavyweightCandidate.DesignParameter] = IndexHeavyweightCandidate.DesignA,
                [IndexHeavyweightCandidate.LargestParameter] = book.Largest == HeavyweightSweep.EveryCompany ? 0 : book.Largest,
                [IndexHeavyweightCandidate.LookBackParameter] = book.LookBack,
                [IndexHeavyweightCandidate.LeadersParameter] = book.Leaders,
                [IndexHeavyweightCandidate.BetaParameter] = book.HighBeta ? 1 : 0,
                [IndexHeavyweightCandidate.LeadingExitParameter] = book.Exit is HeavyweightExit.Both or HeavyweightExit.Drop ? 1 : 0,
                [IndexHeavyweightCandidate.AverageExitParameter] = book.Exit is HeavyweightExit.Both or HeavyweightExit.Break ? 1 : 0,
                [IndexHeavyweightCandidate.TwelveOneParameter] = 0,
                [IndexHeavyweightCandidate.WindowParameter] = 0,
                [IndexHeavyweightCandidate.IndustriesParameter] = 0,
                [IndexHeavyweightCandidate.MembersParameter] = 0,
                [IndexRuleCandidate.QualityParameter] = (int)IndexQuality.Profit,
                [IndexRuleCandidate.FloorsParameter] = 1,
            };
        }

        var levels = new Dictionary<string, double>(StringComparer.Ordinal)
        {
            [IndexRuleCandidate.FloorsParameter] = 1,
            [IndexRuleCandidate.LowestCloseParameter] = Statistic.FromPrice(MemberReadings.LowestPrice),
            [IndexRuleCandidate.QualityParameter] = (int)IndexQuality.Profit,
            [IndexRuleCandidate.HoldParameter] = 0,
            [IndexRuleCandidate.IndustryFallParameter] = 0,
            [IndexRuleCandidate.FundOverSpyParameter] = 0,
            [IndexRuleCandidate.CreditAverageParameter] = 0,
            [IndexRuleCandidate.CreditChangeParameter] = 0,
            [IndexRuleCandidate.LargeBreadthParameter] = 0,
            [IndexRuleCandidate.BreadthFloorParameter] = FamilySweep.MarketFloor,
        };

        switch (evaluator)
        {
            case IndexBreakoutCandidate:
            {
                var grid = BreakoutSweep.Grid;
                var at = IndexNightRead.BreakoutAsFrozen;

                levels[IndexBreakoutCandidate.HighSessionsParameter] = grid.Dials[0].Levels[at[0]];
                levels[IndexBreakoutCandidate.VolumeMultipleParameter] = grid.Dials[1].Levels[at[1]];
                levels[IndexBreakoutCandidate.RangeCeilingParameter] = double.IsPositiveInfinity(grid.Dials[2].Levels[at[2]]) ? 0 : grid.Dials[2].Levels[at[2]];
                levels[IndexBreakoutCandidate.StopMovesParameter] = grid.Dials[3].Levels[at[3]];
                levels[IndexBreakoutCandidate.NearnessParameter] = 0;
                levels[IndexBreakoutCandidate.RecencyParameter] = 0;
                levels[IndexBreakoutCandidate.HighVolumeParameter] = 0;
                break;
            }

            case IndexDriftCandidate:
            {
                var grid = DriftSweep.Grid;
                var at = IndexNightRead.DriftAsFrozen;

                levels[IndexDriftCandidate.WindowSessionsParameter] = grid.Dials[0].Levels[at[0]];
                levels[IndexDriftCandidate.ReactionMovesParameter] = grid.Dials[1].Levels[at[1]];
                levels[IndexDriftCandidate.VolumeMultipleParameter] = grid.Dials[2].Levels[at[2]];
                levels[IndexDriftCandidate.TargetMultipleParameter] = grid.Dials[3].Levels[at[3]];
                levels[IndexDriftCandidate.StopFloorParameter] = 0;
                levels[IndexDriftCandidate.WideWindowParameter] = 0;
                levels[IndexDriftCandidate.PeersParameter] = 0;
                levels[IndexDriftCandidate.EntryParameter] = 0;
                levels[IndexDriftCandidate.RevenueGrewParameter] = 0;
                break;
            }

            default:
            {
                var grid = SweepGrid.Extended;
                var setting = SweepIdeas.BaseRule.Setting;
                var (low, high) = grid.StopBounds[setting.Stop];

                levels[IndexPullbackCandidate.StrengthParameter] = grid.StrengthBars[setting.Strength];
                levels[IndexPullbackCandidate.DepthLowParameter] = grid.DepthLows[setting.DepthLow];
                levels[IndexPullbackCandidate.DepthHighParameter] = grid.DepthHighs[setting.DepthHigh];
                levels[IndexPullbackCandidate.DryUpParameter] = double.IsInfinity(grid.DryUpCeilings[setting.DryUp]) ? 0 : grid.DryUpCeilings[setting.DryUp];
                levels[IndexPullbackCandidate.FreshnessParameter] = grid.Freshness[setting.Freshness];
                levels[IndexPullbackCandidate.RewardToRiskParameter] = grid.RewardToRiskFloors[setting.RewardToRisk];
                levels[IndexPullbackCandidate.StopLowParameter] = low;
                levels[IndexPullbackCandidate.StopHighParameter] = high;
                levels[IndexPullbackCandidate.MarketParameter] = double.IsInfinity(grid.MarketFloors[setting.Market]) ? 0 : grid.MarketFloors[setting.Market];
                levels[IndexPullbackCandidate.BandParameter] = grid.BandStrengths[setting.Band];
                break;
            }
        }

        return levels;
    }

    // The words every index rule's registration states for its rule and its test.
    public const string RuleWords =
        "the family's rule on its index at every setting stated, read through the index night reader's own functions over " +
        "the index's own members: the family's sweep's grid at its four dials, or the pullback's nine, each listing held to " +
        "the index's floors at the stated multiple and the stated lowest close, the stated quality, and each level stated, " +
        "the market check on the index's own breadth at the stated floor unless the S&P 500's at its own floor or none is stated; " +
        "a member firing where every part passes, the rule's own list at most five a night in the family's order, with one " +
        "open trade a stock";

    // The words the test of an S&P 400's or 600's rule of the day before its freeze states: kept beside the live rule for a
    // block and read by no look.
    public const string BackgroundTest =
        "Kept beside the family's live rule from the freeze for a block of 63 sessions, its record read on the Run page beside " +
        "the live rule's and as the live rule's is read; read by no look and no monthly test, and retired by the night once " +
        "the block has passed.";

    public const string Test =
        "From the freeze, each rule's record is its trades' result less each trade's own round trip at the published table " +
        "less the same plan's benchmark on every member of the index that night, over blocks of 63 sessions, and a " +
        "checkpoint passes it where the sign-flip test over its whole blocks falls under its level: 0.05 shared among the " +
        "rules the family registers on its index, its own and its variants, the test the register's candidates are judged by.";

    // The words a heavyweights rule's registration states for its rule and its test.
    public const string HeavyweightRuleWords =
        "the sector heavyweights' rule on its index at every setting stated, its book kept by the index families' step over " +
        "the index's own members: design (a), each sector's largest members leading their sector's members at its size " +
        "cut, look-back, leaders, beta and exits, or design (b), the members following the S&P 500's leading industries at " +
        "its window, its industries and its members an industry, each member held to the index's floors at the stated " +
        "multiple and the stated quality, rebalanced on the first night of each month and on its own first night";

    public const string HeavyweightTest =
        "From the freeze, each rule's record is its holdings' result less each one's own round trip at the published table " +
        "less the return of the equal-weighted members it was chosen beside over the same sessions, in percent of the buy, " +
        "each holding counted in the block of 63 sessions it ended in, and a checkpoint passes it where the sign-flip test " +
        "over its whole blocks falls under its level: 0.05 shared among the rules the family registers on its index.";

    // A freeze's registrations: the family's live rule on its index at the settings given, each variant the live rule
    // with the parameters it names moved, all of them settings the index's sweep read, and from 20.11 the family's rule
    // of the day before the freeze, its provisional rule, kept in the background beside them for a block, where neither
    // the live rule nor a variant states its settings; or why none can be written. A family whose live rule stands is
    // refused, its next freeze following that rule's retirement; variants standing before the freeze stand on beside the
    // rules it registers, and a setting given that one of them states is refused, as is a family past the most rules it
    // keeps. A freeze naming the family's retired live rule it replaces keeps that rule in the background in the
    // provisional rule's place, since the page drew it the day before.
    // see: A freeze on the S&P 400 or 600 is refused only where the family's live rule stands, the variants standing before it kept beside it
    // see: A freeze on the S&P 400 or 600 registers a market variant beside each swing rule and keeps the rule it replaces in the background for 63 sessions
    public static (IReadOnlyList<Registration>? Registrations, string? Refusal) Freeze(
        string family,
        string index,
        IReadOnlyDictionary<string, double> live,
        IReadOnlyList<IReadOnlyDictionary<string, double>> variants,
        IReadOnlyList<RegisterRow> register,
        DateTimeOffset at,
        string? replacing = null)
    {
        if (CandidateEvaluators.All.OfType<IndexRuleCandidate>().FirstOrDefault(evaluator => evaluator.SetupFamily == family && evaluator.Index == index) is not { } carried)
        {
            return (null, $"no rule of a family named '{family}' is carried for an index coded '{index}'; the families are {string.Join(", ", CandidateEvaluators.All.OfType<IndexRuleCandidate>().Select(evaluator => evaluator.SetupFamily).Distinct(StringComparer.Ordinal))} and the indices MID and SML.");
        }

        var standing = CandidateFamily.In(CandidateFamily.Standing(register, at), carried.Family);

        if (standing.FirstOrDefault(row => FamilyRecords.IsLive(row.Candidate)) is { } standingLive)
        {
            return (null, $"the {carried.Family}'s live rule '{standingLive.Candidate}' stands registered; a family on an index is frozen again once its live rule is retired.");
        }

        if (variants.Count > MostVariants - standing.Count)
        {
            return (null, FormattableString.Invariant($"{variants.Count} variants were given, and a freeze registers at most {MostVariants - standing.Count} beside its live rule, the family keeping {CandidateFamily.Maximum} with the {standing.Count} standing."));
        }

        if (Refusal(carried, live) is { } wrong)
        {
            return (null, "the live rule: " + wrong);
        }

        static bool Same(IReadOnlyDictionary<string, double> one, IReadOnlyDictionary<string, double> other) =>
            one.Count == other.Count && one.All(pair => other.TryGetValue(pair.Key, out var value) && value == pair.Value);

        var settings = new List<IReadOnlyDictionary<string, double>> { live };
        var stood = standing.Select(row => CandidateEvaluator.Read(row.Parameters)).ToArray();

        if (stood.Any(one => Same(one, live)))
        {
            return (null, "the live rule states the settings of a rule standing in the family.");
        }

        foreach (var moved in variants)
        {
            var variant = new Dictionary<string, double>(live, StringComparer.Ordinal);

            foreach (var (name, value) in moved)
            {
                variant[name] = value;
            }

            if (Refusal(carried, variant) is { } refused)
            {
                return (null, FormattableString.Invariant($"variant {settings.Count}: {refused}"));
            }

            if (settings.Any(one => Same(one, variant)))
            {
                return (null, FormattableString.Invariant($"variant {settings.Count} states the settings of a rule given before it."));
            }

            if (stood.Any(one => Same(one, variant)))
            {
                return (null, FormattableString.Invariant($"variant {settings.Count} states the settings of a rule standing in the family."));
            }

            settings.Add(variant);
        }

        var named = IndexRuleCandidate.NameOf(index) + " ";
        var (rule, test) = carried is IndexHeavyweightCandidate ? (HeavyweightRuleWords, HeavyweightTest) : (RuleWords, Test);
        var today = Provisional(carried);

        if (replacing is not null)
        {
            var naming = register.Where(row => (row.Event == CandidateFamily.Registered ? row.Candidate : row.Retires) == replacing && row.RegisteredAt <= at)
                .OrderBy(row => row.RegisteredAt).ThenBy(row => row.Id).ToArray();
            var registered = naming.Where(row => row.Event == CandidateFamily.Registered).ToArray();

            if (registered.Length == 0 || !FamilyRecords.IsLive(replacing) || registered.Any(row => CandidateFamily.SetupFamilyOf(row.Evaluator) != carried.Family))
            {
                return (null, $"'{replacing}' is no live rule the {carried.Family} registered, so the freeze cannot keep it in the background.");
            }

            if (naming[^1].Event != CandidateFamily.Retired)
            {
                return (null, $"'{replacing}' stands registered; retire it before the freeze that replaces it.");
            }

            today = CandidateEvaluator.Read(registered[^1].Parameters);
        }

        var background = settings.Any(one => Same(one, today)) || stood.Any(one => Same(one, today))
            ? null
            : new Registration(FamilyRecords.BackgroundPrefix + named + Words(carried, today), rule, BackgroundTest, carried.Name, today);

        if (background is not null && settings.Count + standing.Count + 1 > CandidateFamily.Maximum)
        {
            return (null, FormattableString.Invariant($"{variants.Count} variants were given, and with the rule of today kept in the background the family would hold {settings.Count + standing.Count + 1}, past the {CandidateFamily.Maximum} it keeps."));
        }

        return (
            [
                .. settings.Select((one, place) => new Registration(
                    (place == 0 ? FamilyRecords.LivePrefix : "the ") + named + Words(carried, one),
                    rule,
                    test,
                    carried.Name,
                    one)),
                .. background is null ? [] : new[] { background },
            ],
            null);
    }

    // A family's variants on its index registered with no live rule, its provisional rule staying the live one: each the
    // provisional setting with the parameters it names moved, at most eight, every one at settings its index's sweep read
    // and none stating the provisional setting itself or a setting given before it; or why none can be written, any rule
    // of the family standing on the index among the reasons, since a freeze is then the form that changes it.
    // see: An index family's variants may stand registered before its freeze, its provisional rule staying the live one
    public static (IReadOnlyList<Registration>? Registrations, string? Refusal) Variants(
        string family,
        string index,
        IReadOnlyList<IReadOnlyDictionary<string, double>> variants,
        IReadOnlyList<RegisterRow> register,
        DateTimeOffset at)
    {
        if (CandidateEvaluators.All.OfType<IndexRuleCandidate>().FirstOrDefault(evaluator => evaluator.SetupFamily == family && evaluator.Index == index) is not { } carried)
        {
            return (null, $"no rule of a family named '{family}' is carried for an index coded '{index}'; the families are {string.Join(", ", CandidateEvaluators.All.OfType<IndexRuleCandidate>().Select(evaluator => evaluator.SetupFamily).Distinct(StringComparer.Ordinal))} and the indices MID and SML.");
        }

        if (variants.Count == 0)
        {
            return (null, "no variant was given; each variant names the parameters it moves from the provisional rule.");
        }

        if (variants.Count > MostVariants)
        {
            return (null, FormattableString.Invariant($"{variants.Count} variants were given, and at most {MostVariants} stand beside a family's provisional rule."));
        }

        if (CandidateFamily.In(CandidateFamily.Standing(register, at), carried.Family).Count > 0)
        {
            return (null, $"rules of the {carried.Family} already stand registered; variants are registered beside the provisional rule alone, and a family whose rules stand changes by a freeze once they are retired.");
        }

        return VariantsOf(carried, index, variants);
    }

    // From 20.4 a family's variants on its index registered again whole to add one beside its provisional rule: every
    // variant standing stated again among those given and at least one given that does not stand, each the provisional
    // setting with the parameters it names moved and refused as the form without it refuses one; or why none can be
    // written, no rule of the family standing among the reasons. The registrar retires the standing variants and
    // registers those given at one instant. From 20.11 the same beside a family's frozen live rule, each variant the live
    // rule's setting with the parameters it names moved, the live rule and the rule kept in the background standing on
    // untouched.
    // see: An S&P 400 or 600 family's variants are registered again whole beside its provisional rule or its frozen live rule
    // see: A freeze on the S&P 400 or 600 registers a market variant beside each swing rule and keeps the rule it replaces in the background for 63 sessions
    public static (IReadOnlyList<Registration>? Registrations, string? Refusal) VariantsAgain(
        string family,
        string index,
        IReadOnlyList<IReadOnlyDictionary<string, double>> variants,
        IReadOnlyList<RegisterRow> register,
        DateTimeOffset at)
    {
        if (CandidateEvaluators.All.OfType<IndexRuleCandidate>().FirstOrDefault(evaluator => evaluator.SetupFamily == family && evaluator.Index == index) is not { } carried)
        {
            return (null, $"no rule of a family named '{family}' is carried for an index coded '{index}'; the families are {string.Join(", ", CandidateEvaluators.All.OfType<IndexRuleCandidate>().Select(evaluator => evaluator.SetupFamily).Distinct(StringComparer.Ordinal))} and the indices MID and SML.");
        }

        var rows = CandidateFamily.In(CandidateFamily.Standing(register, at), carried.Family);

        if (rows.Count == 0)
        {
            return (null, $"no rule of the {carried.Family} stands registered, so there are no variants to register again; the form without --again registers them.");
        }

        var live = rows.FirstOrDefault(row => FamilyRecords.IsLive(row.Candidate));
        var standing = rows.Where(row => !FamilyRecords.IsLive(row.Candidate) && !FamilyRecords.IsBackground(row.Candidate)).ToArray();
        var (registrations, refused) = VariantsOf(carried, index, variants, live is null ? null : CandidateEvaluator.Read(live.Parameters), rows.Count - standing.Length);

        if (registrations is null)
        {
            return (null, refused);
        }

        static bool Same(IReadOnlyDictionary<string, double> one, IReadOnlyDictionary<string, double> other) =>
            one.Count == other.Count && one.All(pair => other.TryGetValue(pair.Key, out var value) && value == pair.Value);

        if (standing.FirstOrDefault(row => !registrations.Any(one => Same(one.Parameters, CandidateEvaluator.Read(row.Parameters)))) is { } left)
        {
            return (null, $"'{left.Candidate}' stands and is not among the variants given; this form registers the family's variants again whole to add one, and a variant left out is retired on its own.");
        }

        if (registrations.Count == standing.Length)
        {
            return (null, "every variant given stands already, so none would be added; this form registers the family's variants again whole to add one.");
        }

        return (registrations, null);
    }

    // A family's variants as given, each the provisional setting, or the live rule's where one stands, with the
    // parameters it names moved, the family keeping at most nine with the rules standing beside them, every one at
    // settings its index's sweep read and none stating the setting moved from or a setting given before it.
    static (IReadOnlyList<Registration>? Registrations, string? Refusal) VariantsOf(IndexRuleCandidate carried, string index, IReadOnlyList<IReadOnlyDictionary<string, double>> variants, IReadOnlyDictionary<string, double>? live = null, int keptBeside = 0)
    {
        var movedFrom = live is null ? "the provisional rule" : "the live rule";

        if (variants.Count == 0)
        {
            return (null, $"no variant was given; each variant names the parameters it moves from {movedFrom}.");
        }

        if (variants.Count > MostVariants - Math.Max(0, keptBeside - 1))
        {
            return (null, FormattableString.Invariant($"{variants.Count} variants were given, and at most {MostVariants - Math.Max(0, keptBeside - 1)} stand beside a family's {(live is null ? "provisional" : "live")} rule."));
        }

        var provisional = live ?? Provisional(carried);
        var settings = new List<IReadOnlyDictionary<string, double>> { provisional };

        foreach (var moved in variants)
        {
            var variant = new Dictionary<string, double>(provisional, StringComparer.Ordinal);

            foreach (var (name, value) in moved)
            {
                variant[name] = value;
            }

            if (Refusal(carried, variant) is { } refused)
            {
                return (null, FormattableString.Invariant($"variant {settings.Count}: {refused}"));
            }

            if (settings.Any(one => one.Count == variant.Count && one.All(pair => variant.TryGetValue(pair.Key, out var value) && value == pair.Value)))
            {
                return (null, FormattableString.Invariant($"variant {settings.Count} states {(live is null ? "the provisional rule's" : "the live rule's")} settings or a variant's given before it."));
            }

            settings.Add(variant);
        }

        var named = IndexRuleCandidate.NameOf(index) + " ";
        var (rule, test) = carried is IndexHeavyweightCandidate ? (HeavyweightRuleWords, HeavyweightTest) : (RuleWords, Test);

        return ([.. settings.Skip(1).Select(one => new Registration("the " + named + Words(carried, one), rule, test, carried.Name, one))], null);
    }

    // A rule's settings in words, which its registration's name carries after the family on its index.
    public static string Words(IndexRuleCandidate evaluator, IReadOnlyDictionary<string, double> p)
    {
        var family = evaluator switch
        {
            IndexBreakoutCandidate => FormattableString.Invariant(
                $"breakout rule at a {p[IndexBreakoutCandidate.HighSessionsParameter]}-session high, {Number(p[IndexBreakoutCandidate.VolumeMultipleParameter])} times the volume, ranges at {(p[IndexBreakoutCandidate.RangeCeilingParameter] == 0 ? "no ceiling" : Number(p[IndexBreakoutCandidate.RangeCeilingParameter]))} and the stop {Number(p[IndexBreakoutCandidate.StopMovesParameter])} typical moves beneath"),
            IndexDriftCandidate => FormattableString.Invariant(
                $"drift rule within {p[IndexDriftCandidate.WindowSessionsParameter]} sessions, up {Number(p[IndexDriftCandidate.ReactionMovesParameter])} typical moves on {Number(p[IndexDriftCandidate.VolumeMultipleParameter])} times the volume, the target at {Number(p[IndexDriftCandidate.TargetMultipleParameter])} times the risk")
                + (p[IndexDriftCandidate.StopFloorParameter] > 0 ? FormattableString.Invariant($", the stop at least {Number(p[IndexDriftCandidate.StopFloorParameter])} typical move beneath") : string.Empty),
            IndexHeavyweightCandidate when p[IndexHeavyweightCandidate.DesignParameter] == IndexHeavyweightCandidate.DesignA => FormattableString.Invariant(
                $"heavyweights rule of each sector's {(p[IndexHeavyweightCandidate.LargestParameter] == 0 ? "every member" : Number(p[IndexHeavyweightCandidate.LargestParameter]) + " largest members")} leading it {StrengthWords(p)}, {p[IndexHeavyweightCandidate.LeadersParameter]} leader(s) a sector{(p[IndexHeavyweightCandidate.BetaParameter] == 1 ? " with a beta of at least 1" : string.Empty)}, sold {ExitWords(p)}"),
            IndexHeavyweightCandidate => FormattableString.Invariant(
                $"heavyweights rule following the S&P 500's {(p[IndexHeavyweightCandidate.IndustriesParameter] == 0 ? "every leading industry" : Number(p[IndexHeavyweightCandidate.IndustriesParameter]) + " strongest industries")} over {p[IndexHeavyweightCandidate.WindowParameter]} sessions, {p[IndexHeavyweightCandidate.MembersParameter]} member(s) an industry, sold where it is no longer bought"),
            _ => FormattableString.Invariant(
                $"pullback rule at a strength of {Number(p[IndexPullbackCandidate.StrengthParameter])}, {Number(p[IndexPullbackCandidate.DepthLowParameter])} to {Number(p[IndexPullbackCandidate.DepthHighParameter])} typical moves deep, a dry-up {(p[IndexPullbackCandidate.DryUpParameter] == 0 ? "off" : "under " + Number(p[IndexPullbackCandidate.DryUpParameter]))}, fresh within {p[IndexPullbackCandidate.FreshnessParameter]}, {Number(p[IndexPullbackCandidate.RewardToRiskParameter])} times the risk, the stop {Number(p[IndexPullbackCandidate.StopLowParameter])} to {Number(p[IndexPullbackCandidate.StopHighParameter])} moves, {(p[IndexPullbackCandidate.MarketParameter] == 0 ? "its own market floor off" : "the market at " + Number(p[IndexPullbackCandidate.MarketParameter]))} and band strength {p[IndexPullbackCandidate.BandParameter]}"),
        };

        return family + LevelWords(evaluator, p);
    }

    // What a design (a) leader leads its sector on, in words: its return over the look-back, or twelve months' strength
    // skipping the latest month as the night reads it.
    static string StrengthWords(IReadOnlyDictionary<string, double> p) =>
        TwelveOne(p)
            ? FormattableString.Invariant($"on twelve months' strength skipping the latest month, from {TwelveMonthStrength.From} sessions back to {TwelveMonthStrength.Skipped}")
            : FormattableString.Invariant($"over {p[IndexHeavyweightCandidate.LookBackParameter]} sessions");

    // Whether a design (a) rule reads twelve months' strength in its look-back's place, none where its registration
    // states no such dial.
    public static bool TwelveOne(IReadOnlyDictionary<string, double> p) =>
        p.TryGetValue(IndexHeavyweightCandidate.TwelveOneParameter, out var stated) && stated == 1;

    // What sells a design (a) holding besides its stock leaving the index, in words.
    static string ExitWords(IReadOnlyDictionary<string, double> p) =>
        (p[IndexHeavyweightCandidate.LeadingExitParameter], p[IndexHeavyweightCandidate.AverageExitParameter]) switch
        {
            (1, 1) => "on no longer leading or under its 200-day average",
            (1, _) => "on no longer leading",
            _ => "under its 200-day average",
        };

    // A design (a) rule's setting as its sweep's grid holds it: its size cut, look-back, leaders and beta, a sector's
    // return its members' mean, a monthly rebalance, and its exit.
    public static HeavyweightSetting HeavyweightSettingOf(IReadOnlyDictionary<string, double> p) => new(
        p[IndexHeavyweightCandidate.LargestParameter] == 0 ? HeavyweightSweep.EveryCompany : (int)p[IndexHeavyweightCandidate.LargestParameter],
        (int)p[IndexHeavyweightCandidate.LookBackParameter],
        (int)p[IndexHeavyweightCandidate.LeadersParameter],
        HeavyweightSectorReturn.Members,
        p[IndexHeavyweightCandidate.BetaParameter] == 1,
        HeavyweightPeriod.Month,
        (p[IndexHeavyweightCandidate.LeadingExitParameter], p[IndexHeavyweightCandidate.AverageExitParameter]) switch
        {
            (1, 1) => HeavyweightExit.Both,
            (1, _) => HeavyweightExit.Drop,
            _ => HeavyweightExit.Break,
        });

    // The levels a rule states that its provisional rule does not, each in words, a level its family states none of read
    // at its provisional value.
    static string LevelWords(IndexRuleCandidate evaluator, IReadOnlyDictionary<string, double> p)
    {
        var words = new List<string>();
        var floors = p[IndexRuleCandidate.FloorsParameter];

        double At(string name, double provisional) => p.TryGetValue(name, out var stated) ? stated : provisional;

        if (floors != 1)
        {
            words.Add(FormattableString.Invariant($"the dollar volume floor at {Number(floors)} times"));
        }

        if (At(IndexRuleCandidate.LowestCloseParameter, Statistic.FromPrice(MemberReadings.LowestPrice)) is var lowest && lowest != Statistic.FromPrice(MemberReadings.LowestPrice))
        {
            words.Add(FormattableString.Invariant($"a close of at least ${Number(lowest)}"));
        }

        words.Add(p[IndexRuleCandidate.QualityParameter] switch
        {
            0 => "no quality gate",
            2 => "the profit gate and the interest cover",
            _ => "the profit gate",
        });

        if (At(IndexRuleCandidate.HoldParameter, 0) is var hold and > 0)
        {
            words.Add(FormattableString.Invariant($"held {hold} sessions"));
        }

        if (At(IndexRuleCandidate.IndustryFallParameter, 0) is var fall and > 0)
        {
            words.Add(FormattableString.Invariant($"kept off where its industry fell over {fall} sessions"));
        }

        if (At(IndexRuleCandidate.FundOverSpyParameter, 0) is var fund and > 0)
        {
            words.Add(FormattableString.Invariant($"listing while the index's fund leads SPY over {fund} sessions"));
        }

        if (At(IndexRuleCandidate.CreditAverageParameter, 0) == 1)
        {
            words.Add("while HYG stands above its 50-session average");
        }

        if (At(IndexRuleCandidate.CreditChangeParameter, 0) == 1)
        {
            words.Add("while HYG is up over 63 sessions");
        }

        var breadth = At(IndexRuleCandidate.LargeBreadthParameter, 0);

        if (breadth == 1)
        {
            words.Add("its market check on the S&P 500's breadth");
        }
        else if (breadth == IndexRuleCandidate.NoMarketCheck)
        {
            words.Add("the " + IndexRuleCandidate.MarketCheckOff);
        }
        else if (At(IndexRuleCandidate.BreadthFloorParameter, FamilySweep.MarketFloor) is var floor && floor != FamilySweep.MarketFloor)
        {
            words.Add(FormattableString.Invariant($"its market check on the index's own breadth at {floor * 100:0}%"));
        }

        if (evaluator is IndexBreakoutCandidate)
        {
            if (p[IndexBreakoutCandidate.NearnessParameter] > 0)
            {
                words.Add(FormattableString.Invariant($"the close at least {Number(p[IndexBreakoutCandidate.NearnessParameter])} of its year's high"));
            }

            if (p[IndexBreakoutCandidate.RecencyParameter] != 0)
            {
                words.Add(p[IndexBreakoutCandidate.RecencyParameter] > 0 ? "its year's high made within 63 sessions" : "its year's high older than 63 sessions");
            }

            if (p[IndexBreakoutCandidate.HighVolumeParameter] > 0)
            {
                words.Add(FormattableString.Invariant($"volume at least {Number(p[IndexBreakoutCandidate.HighVolumeParameter])} times its average"));
            }
        }

        if (evaluator is IndexDriftCandidate)
        {
            if (p[IndexDriftCandidate.WideWindowParameter] > 0)
            {
                words.Add(FormattableString.Invariant($"the window {p[IndexDriftCandidate.WideWindowParameter]} sessions"));
            }

            if (p[IndexDriftCandidate.PeersParameter] == 1)
            {
                words.Add("its peers' surprise before the reaction above nothing");
            }

            if (At(IndexDriftCandidate.EntryParameter, 0) == 1)
            {
                words.Add(FormattableString.Invariant($"bought on its first close under the close before it within {DriftSweep.FirstPullbackSessions} sessions of the reaction, its stop where its listing set it"));
            }

            if (At(IndexDriftCandidate.RevenueGrewParameter, 0) == 1)
            {
                words.Add("its print's quarter's revenue above the same quarter's a year before, both as first filed and stored before the night");
            }

            if (ReleaseTone.VersionOf(p) is var version and > 0)
            {
                words.Add(FormattableString.Invariant($"its print's release reading better with its outlook not cut under reading version {version}"));
            }
        }

        return ", " + string.Join(", ", words);
    }

    static string Number(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    // A rule's read on the night: its family's listings at its settings in its own order, its market check on the
    // index's own breadth at the floor it states, on the S&P 500's at the S&P 500 filter's own floor where it states so
    // or open on every night where it states none, and each part of its rule past the setup a member is read against,
    // the floors and the quality and then each level in its sweep's order. From 20.11 the listings are read with the
    // market open on every session they read, as the sweeps on each index's own history read them, so the check is read
    // once, the rule's own part at the rule's own floor, and a night it closed lists none.
    // see: The S&P 400's and 600's nights store every part's answer for every member, the market check its own
    // see: Each frozen rule on the S&P 400 and 600 reads its market check at the level its own sweep chose
    public static IndexFamilyRead Read(IndexNightInputs inputs, IndexRule rule, IndexLevels levels)
    {
        var p = rule.Parameters;
        var breadth = p[IndexRuleCandidate.LargeBreadthParameter];
        var sessions = rule.Evaluator is IndexDriftCandidate ? IndexNightRead.AllOpen(inputs.Sessions) : IndexNightRead.OpenOn(inputs.Sessions, inputs.At);
        var market = breadth switch
        {
            1 => IndexNightRead.MarketGate(levels.LargeBreadth, IndexRuleCandidate.LargeBreadthFloor, IndexNightRead.LargeBreadth),
            IndexRuleCandidate.NoMarketCheck => IndexNightRead.NoMarketGate,
            _ => IndexNightRead.MarketGate(inputs.Breadth, p.TryGetValue(IndexRuleCandidate.BreadthFloorParameter, out var floor) ? floor : FamilySweep.MarketFloor),
        };
        var hold = (int)p[IndexRuleCandidate.HoldParameter];
        var floors = p[IndexRuleCandidate.FloorsParameter] switch { 0.5 => IndexSweepRunner.DollarVolumeHalf, 2 => 2m, _ => 1m };
        var lowest = p[IndexRuleCandidate.LowestCloseParameter] == Statistic.FromPrice(IndexSweepRunner.HigherPrice) ? IndexSweepRunner.HigherPrice : MemberReadings.LowestPrice;
        var quality = (IndexQuality)(int)p[IndexRuleCandidate.QualityParameter];
        var backs = new Dictionary<int, int>();

        IReadOnlyList<(int Name, IndexTrade Trade)> ListedOn(IReadOnlyList<SweepColumns.Session> on)
        {
            IReadOnlyList<(int Name, IndexTrade Trade)> listed = rule.Evaluator switch
            {
                IndexBreakoutCandidate => IndexNightRead.Breakouts(inputs, IndexNightRead.Places(BreakoutSweep.Grid, BreakoutValues(p)), on),
                IndexDriftCandidate => Drifts(inputs, p, on, backs),
                _ => IndexNightRead.Pullbacks(inputs, new IdeaRule(PullbackOf(p)!.Value, IdeaExit.Base, int.MaxValue, []), IndexNightRead.PullbackCap, on),
            };

            return hold > 0 ? [.. listed.Select(one => (one.Name, one.Trade with { Cap = hold }))] : listed;
        }

        var ifOpen = ListedOn(sessions);
        var trades = ifOpen.GroupBy(one => one.Name).ToDictionary(group => group.Key, group => group.First().Trade);

        IReadOnlyList<RulePart> PartsOf(int name) =>
        [
            .. IndexNightRead.PartsOn(inputs, name, floors, lowest, quality, ticker => levels.Coverage.GetValueOrDefault(ticker)),
            .. LevelParts(inputs, rule, levels, name, trades.TryGetValue(name, out var trade) ? trade : null, backs),
        ];

        return new IndexFamilyRead(market.Passed ? ifOpen : [], market, PartsOf) { ListedIfOpen = ifOpen };
    }

    // The drift's listings at its settings, noting each listing's sessions back to its reaction: at its listing's close,
    // or from 20.11 on the first pullback after its reaction where the rule states that entry.
    // see: A drift rule on the S&P 400 or 600 stating the first pullback is bought on the night through the one function its search's entry level reads
    static IReadOnlyList<(int Name, IndexTrade Trade)> Drifts(IndexNightInputs inputs, IReadOnlyDictionary<string, double> p, IReadOnlyList<SweepColumns.Session> sessions, Dictionary<int, int> backs)
    {
        double? window = p[IndexDriftCandidate.WideWindowParameter] > 0 ? p[IndexDriftCandidate.WideWindowParameter] : null;
        var setting = IndexNightRead.Places(DriftSweep.Grid, DriftValues(p));

        if (p.TryGetValue(IndexDriftCandidate.EntryParameter, out var entry) && entry == 1)
        {
            return IndexNightRead.DriftPullbacks(inputs, setting, p[IndexDriftCandidate.StopFloorParameter], window, sessions, backs);
        }

        foreach (var reading in new DriftSweep(inputs.Series, inputs.Members).Readings(sessions, inputs.At, window).Where(reading => reading.Session == inputs.At))
        {
            backs.TryAdd(reading.Name, reading.Back);
        }

        return IndexNightRead.Drifts(inputs, setting, p[IndexDriftCandidate.StopFloorParameter], window, sessions);
    }

    // Each level a rule states as a part of its own, in its sweep's order, a member answered on every one: a level reading
    // the trade its setup places failed for a member no setup listed, and the tone read in its two steps, the release's
    // reading held and then its answer.
    static IReadOnlyList<RulePart> LevelParts(IndexNightInputs inputs, IndexRule rule, IndexLevels levels, int name, IndexTrade? trade, IReadOnlyDictionary<int, int> backs)
    {
        var p = rule.Parameters;
        var ticker = inputs.Series[name].Name.Ticker;
        var parts = new List<RulePart>();

        if (p[IndexRuleCandidate.IndustryFallParameter] is var fall and > 0)
        {
            parts.Add(new(IndustryFell, !(levels.Industry.TryGetValue(ticker, out var industry) && (fall == MemberReadings.IndustryWindows[0] ? industry.Month : industry.Quarter) is < 0)));
        }

        if (rule.Evaluator is IndexBreakoutCandidate)
        {
            var bar = IndexNightRead.BarOf(inputs.Series[name], inputs.At);

            // The year's high over the sessions the night read before the store's year as well, where it read them, as the
            // breakout's own high and the member readings read it.
            // see: The member readings' year's high and a breakout rule's checks on it read the sessions before a short year as the breakout's own high does
            var (high, since) = MemberReadings.YearHigh(inputs.Series[name].Name.Before, inputs.Series[name].Bars, bar) is { } found
                ? (Statistic.FromPrice(found.High), found.Since)
                : (double.NaN, int.MaxValue);

            if (p[IndexBreakoutCandidate.NearnessParameter] is var near and > 0)
            {
                parts.Add(trade is { } listed ? new(NotNearItsHigh, Statistic.FromPrice(listed.Entry) / high >= near) : new(NotNearItsHigh, false, IndexNightRead.NotReadWithoutASetup));
            }

            if (p[IndexBreakoutCandidate.RecencyParameter] is var recency and not 0)
            {
                parts.Add(new(HighNotAsStated, recency > 0 ? since <= IndexSweepRunner.RecentHighSessions : since > IndexSweepRunner.RecentHighSessions));
            }

            if (p[IndexBreakoutCandidate.HighVolumeParameter] is var volume and > 0)
            {
                parts.Add(trade is { } listed ? new(VolumeUnder, listed.Order >= volume) : new(VolumeUnder, false, IndexNightRead.NotReadWithoutASetup));
            }
        }

        if (rule.Evaluator is IndexDriftCandidate && p[IndexDriftCandidate.PeersParameter] == 1)
        {
            var reaction = backs.TryGetValue(name, out var back) && inputs.At - back >= 0 ? inputs.Calendar[inputs.At - back] : (DateOnly?)null;

            parts.Add(new(PeersNotAbove, reaction is { } session && levels.PeerSurprise.GetValueOrDefault((ticker, session)) is > 0));
        }

        // From 20.11 the revenue level after the peers, as its sweep reads it: the quarter of the print the night's bar
        // reads, the newest, against the same quarter a year before, both as first filed over the facts stored before
        // the night; a member whose quarters are not both stored fails it saying so.
        // see: A drift's revenue level on the S&P 400 and 600 reads a quarter only once its filing is stored, on the night and in its sweep alike
        if (rule.Evaluator is IndexDriftCandidate && p.TryGetValue(IndexDriftCandidate.RevenueGrewParameter, out var grew) && grew == 1)
        {
            var series = inputs.Series[name];
            var bar = IndexNightRead.BarOf(series, inputs.At);
            var report = bar >= 0 && series.NewestSurprise[bar] is var newest and >= 0 ? series.Name.Surprises[newest].EventDate : (DateOnly?)null;
            var quarters = levels.Revenue?.GetValueOrDefault(ticker);
            var growth = report is { } day && quarters is not null ? RevenueGrowth.Of(day, quarters) : null;
            var words = report is null ? "no print read for tonight"
                : quarters is null or { Count: 0 } ? "no quarter of its revenue stored as first filed before tonight"
                : growth is null ? "its print's quarter and the same quarter a year before not both stored as first filed before tonight"
                : FormattableString.Invariant($"revenue {growth.Growth * 100:+0.0;-0.0;0.0}% on the same quarter a year before, both as first filed");

            parts.Add(new(RevenueNotAbove, growth is { Growth: > 0 }, words));
        }

        // The switches last, as the second stage reads them after every other level, one part however many the rule
        // states.
        var window = p[IndexRuleCandidate.FundOverSpyParameter];
        var (average, change) = (p[IndexRuleCandidate.CreditAverageParameter] == 1, p[IndexRuleCandidate.CreditChangeParameter] == 1);

        if (window > 0 || average || change)
        {
            var fund = !(window > 0 && (window == IndexSwitches.SmallWindows[0] ? levels.FundHalfYear : levels.FundYear) is not > 1);
            var credit = !((average && levels.CreditAverage is not > 1) || (change && levels.CreditChange is not > 1));

            parts.Add(new(SwitchClosed, fund && credit));
        }

        // The tone condition after the switches, as the S&P 500's drift reads it after its market switches: the release of
        // the print the listing reacts to, the newest the night's bar reads, under the version the rule names.
        // see: The drift's tone variant lists only where its print's release reads better than expected and its outlook was not lowered or withdrawn
        if (rule.Evaluator is IndexDriftCandidate && ReleaseTone.VersionOf(p) is var version and > 0)
        {
            var series = inputs.Series[name];
            var bar = IndexNightRead.BarOf(series, inputs.At);
            PrintRelease? release = null;

            if (bar >= 0 && series.NewestSurprise[bar] is var newest and >= 0)
            {
                levels.Releases?.TryGetValue((ticker, series.Name.Surprises[newest].EventDate, version), out release);
            }

            parts.Add(new(ToneNotHeld, release?.Answer is not null));
            parts.Add(new(ToneFailed, ReleaseTone.GateOf(version, release).Passed));
        }

        return parts;
    }
}
