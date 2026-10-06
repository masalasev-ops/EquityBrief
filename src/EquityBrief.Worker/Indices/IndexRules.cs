using System.Globalization;
using EquityBrief.Core.Candidates;
using EquityBrief.Core.Families;
using EquityBrief.Core.Prices;
using EquityBrief.Core.Readings;
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
// against its average and its own past, and the S&P 500's breadth.
// see: A member's coverage is read on the night only over quarters whose fetch read their interest expense
public sealed record IndexLevels(
    IReadOnlyDictionary<string, (double? Month, double? Quarter)> Industry,
    IReadOnlyDictionary<string, bool?> Coverage,
    IReadOnlyDictionary<(string Ticker, DateOnly Session), double?> PeerSurprise,
    double? FundHalfYear,
    double? FundYear,
    double? CreditAverage,
    double? CreditChange,
    double? LargeBreadth)
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
        [IndexRuleCandidate.LargeBreadthParameter] = [0, 1],
        [IndexBreakoutCandidate.NearnessParameter] = [0, IndexSweepRunner.NearHigh, 1],
        [IndexBreakoutCandidate.RecencyParameter] = [0, IndexSweepRunner.RecentHighSessions, -IndexSweepRunner.RecentHighSessions],
        [IndexBreakoutCandidate.HighVolumeParameter] = [0, IndexSweepRunner.HighestVolume],
        [IndexDriftCandidate.StopFloorParameter] = [0, 1],
        [IndexDriftCandidate.WideWindowParameter] = [0, IndexSweepRunner.DriftWideWindow],
        [IndexDriftCandidate.PeersParameter] = [0, 1],
    };

    // Why a rule's parameters cannot be registered: a value off its dial's grid or off its level's values, the first
    // named; none where every value is one its index's sweep read.
    public static string? Refusal(IndexRuleCandidate evaluator, IReadOnlyDictionary<string, double> parameters)
    {
        if (parameters.Keys.Except(evaluator.Parameters, StringComparer.Ordinal).FirstOrDefault() is { } unread)
        {
            return $"'{evaluator.Name}' reads no parameter named '{unread}'.";
        }

        if (evaluator.Parameters.FirstOrDefault(name => !parameters.ContainsKey(name)) is { } missing)
        {
            return $"'{evaluator.Name}' reads '{missing}', and the registration states none.";
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
        "the market check on the index's own breadth unless the S&P 500's is stated; a member firing where every part " +
        "passes, the rule's own list at most five a night in the family's order, with one open trade a stock";

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

    // A freeze's registrations: the family's live rule on its index at the settings given and each variant the live rule
    // with the parameters it names moved, at most eight, all of them settings the index's sweep read; or why none can be
    // written, a family on the index already standing among the reasons, since its first freeze is the one form built.
    public static (IReadOnlyList<Registration>? Registrations, string? Refusal) Freeze(
        string family,
        string index,
        IReadOnlyDictionary<string, double> live,
        IReadOnlyList<IReadOnlyDictionary<string, double>> variants,
        IReadOnlyList<RegisterRow> register,
        DateTimeOffset at)
    {
        if (CandidateEvaluators.All.OfType<IndexRuleCandidate>().FirstOrDefault(evaluator => evaluator.SetupFamily == family && evaluator.Index == index) is not { } carried)
        {
            return (null, $"no rule of a family named '{family}' is carried for an index coded '{index}'; the families are {string.Join(", ", CandidateEvaluators.All.OfType<IndexRuleCandidate>().Select(evaluator => evaluator.SetupFamily).Distinct(StringComparer.Ordinal))} and the indices MID and SML.");
        }

        if (variants.Count > MostVariants)
        {
            return (null, FormattableString.Invariant($"{variants.Count} variants were given, and a freeze registers at most {MostVariants} beside its live rule."));
        }

        if (CandidateFamily.In(CandidateFamily.Standing(register, at), carried.Family).Count > 0)
        {
            return (null, $"rules of the {carried.Family} already stand registered; a family on an index is frozen once.");
        }

        if (Refusal(carried, live) is { } wrong)
        {
            return (null, "the live rule: " + wrong);
        }

        var settings = new List<IReadOnlyDictionary<string, double>> { live };

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

            if (settings.Any(one => one.Count == variant.Count && one.All(pair => variant[pair.Key] == pair.Value)))
            {
                return (null, FormattableString.Invariant($"variant {settings.Count} states the settings of a rule given before it."));
            }

            settings.Add(variant);
        }

        var named = IndexRuleCandidate.NameOf(index) + " ";
        var (rule, test) = carried is IndexHeavyweightCandidate ? (HeavyweightRuleWords, HeavyweightTest) : (RuleWords, Test);

        return (
            [
                .. settings.Select((one, place) => new Registration(
                    (place == 0 ? FamilyRecords.LivePrefix : "the ") + named + Words(carried, one),
                    rule,
                    test,
                    carried.Name,
                    one)),
            ],
            null);
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
                $"heavyweights rule of each sector's {(p[IndexHeavyweightCandidate.LargestParameter] == 0 ? "every member" : Number(p[IndexHeavyweightCandidate.LargestParameter]) + " largest members")} leading it over {p[IndexHeavyweightCandidate.LookBackParameter]} sessions, {p[IndexHeavyweightCandidate.LeadersParameter]} leader(s) a sector{(p[IndexHeavyweightCandidate.BetaParameter] == 1 ? " with a beta of at least 1" : string.Empty)}, sold {ExitWords(p)}"),
            IndexHeavyweightCandidate => FormattableString.Invariant(
                $"heavyweights rule following the S&P 500's {(p[IndexHeavyweightCandidate.IndustriesParameter] == 0 ? "every leading industry" : Number(p[IndexHeavyweightCandidate.IndustriesParameter]) + " strongest industries")} over {p[IndexHeavyweightCandidate.WindowParameter]} sessions, {p[IndexHeavyweightCandidate.MembersParameter]} member(s) an industry, sold where it is no longer bought"),
            _ => FormattableString.Invariant(
                $"pullback rule at a strength of {Number(p[IndexPullbackCandidate.StrengthParameter])}, {Number(p[IndexPullbackCandidate.DepthLowParameter])} to {Number(p[IndexPullbackCandidate.DepthHighParameter])} typical moves deep, a dry-up {(p[IndexPullbackCandidate.DryUpParameter] == 0 ? "off" : "under " + Number(p[IndexPullbackCandidate.DryUpParameter]))}, fresh within {p[IndexPullbackCandidate.FreshnessParameter]}, {Number(p[IndexPullbackCandidate.RewardToRiskParameter])} times the risk, the stop {Number(p[IndexPullbackCandidate.StopLowParameter])} to {Number(p[IndexPullbackCandidate.StopHighParameter])} moves, the market {(p[IndexPullbackCandidate.MarketParameter] == 0 ? "off" : "at " + Number(p[IndexPullbackCandidate.MarketParameter]))} and band strength {p[IndexPullbackCandidate.BandParameter]}"),
        };

        return family + LevelWords(evaluator, p);
    }

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

        if (At(IndexRuleCandidate.LargeBreadthParameter, 0) == 1)
        {
            words.Add("its market check on the S&P 500's breadth");
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
        }

        return ", " + string.Join(", ", words);
    }

    static string Number(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    // A rule's read on the night: its family's listings at its settings in its own order, its market check on the
    // index's own breadth or the S&P 500's where it states so, and the first part of its rule past the setup a listed
    // member fails, the floors and the quality and then each level in its sweep's order.
    public static IndexFamilyRead Read(IndexNightInputs inputs, IndexRule rule, IndexLevels levels)
    {
        var p = rule.Parameters;
        var large = p[IndexRuleCandidate.LargeBreadthParameter] == 1;
        var sessions = large ? WithBreadth(inputs.Sessions, inputs.At, levels.LargeBreadth) : inputs.Sessions;
        var open = large ? levels.LargeBreadth >= FamilySweep.MarketFloor : inputs.Open;
        var hold = (int)p[IndexRuleCandidate.HoldParameter];
        var floors = p[IndexRuleCandidate.FloorsParameter] switch { 0.5 => IndexSweepRunner.DollarVolumeHalf, 2 => 2m, _ => 1m };
        var lowest = p[IndexRuleCandidate.LowestCloseParameter] == Statistic.FromPrice(IndexSweepRunner.HigherPrice) ? IndexSweepRunner.HigherPrice : MemberReadings.LowestPrice;
        var quality = (IndexQuality)(int)p[IndexRuleCandidate.QualityParameter];
        var backs = new Dictionary<int, int>();

        IReadOnlyList<(int Name, IndexTrade Trade)> listed = rule.Evaluator switch
        {
            IndexBreakoutCandidate => IndexNightRead.Breakouts(inputs, IndexNightRead.Places(BreakoutSweep.Grid, BreakoutValues(p)), sessions),
            IndexDriftCandidate => Drifts(inputs, p, sessions, backs),
            _ => IndexNightRead.Pullbacks(inputs, new IdeaRule(PullbackOf(p)!.Value, IdeaExit.Base, int.MaxValue, []), IndexNightRead.PullbackCap, sessions),
        };

        if (hold > 0)
        {
            listed = [.. listed.Select(one => (one.Name, one.Trade with { Cap = hold }))];
        }

        var trades = listed.GroupBy(one => one.Name).ToDictionary(group => group.Key, group => group.First().Trade);

        string? FailsOn(int name) =>
            IndexNightRead.FailsOn(inputs, name, floors, lowest, quality, ticker => levels.Coverage.GetValueOrDefault(ticker))
            ?? LevelFails(inputs, rule, levels, name, trades[name], backs);

        return new IndexFamilyRead(listed, open, FailsOn);
    }

    // The drift's listings at its settings, noting each listing's sessions back to its reaction.
    static IReadOnlyList<(int Name, IndexTrade Trade)> Drifts(IndexNightInputs inputs, IReadOnlyDictionary<string, double> p, IReadOnlyList<SweepColumns.Session> sessions, Dictionary<int, int> backs)
    {
        double? window = p[IndexDriftCandidate.WideWindowParameter] > 0 ? p[IndexDriftCandidate.WideWindowParameter] : null;

        foreach (var reading in new DriftSweep(inputs.Series, inputs.Members).Readings(sessions, inputs.At, window).Where(reading => reading.Session == inputs.At))
        {
            backs.TryAdd(reading.Name, reading.Back);
        }

        return IndexNightRead.Drifts(inputs, IndexNightRead.Places(DriftSweep.Grid, DriftValues(p)), p[IndexDriftCandidate.StopFloorParameter], window, sessions);
    }

    // The first level a listed member fails, in its sweep's order; none where it passes every level its rule states.
    static string? LevelFails(IndexNightInputs inputs, IndexRule rule, IndexLevels levels, int name, IndexTrade trade, IReadOnlyDictionary<int, int> backs)
    {
        var p = rule.Parameters;
        var ticker = inputs.Series[name].Name.Ticker;

        if (p[IndexRuleCandidate.IndustryFallParameter] is var fall and > 0
            && levels.Industry.TryGetValue(ticker, out var industry)
            && (fall == MemberReadings.IndustryWindows[0] ? industry.Month : industry.Quarter) is < 0)
        {
            return IndustryFell;
        }

        if (rule.Evaluator is IndexBreakoutCandidate)
        {
            var bar = IndexNightRead.BarOf(inputs.Series[name], inputs.At);
            var (high, since) = MemberReadings.YearHigh(inputs.Series[name].Bars, bar) is { } found
                ? (Statistic.FromPrice(found.High), found.Since)
                : (double.NaN, int.MaxValue);

            if (p[IndexBreakoutCandidate.NearnessParameter] is var near and > 0 && !(Statistic.FromPrice(trade.Entry) / high >= near))
            {
                return NotNearItsHigh;
            }

            if ((p[IndexBreakoutCandidate.RecencyParameter] > 0 && !(since <= IndexSweepRunner.RecentHighSessions))
                || (p[IndexBreakoutCandidate.RecencyParameter] < 0 && !(since > IndexSweepRunner.RecentHighSessions)))
            {
                return HighNotAsStated;
            }

            if (p[IndexBreakoutCandidate.HighVolumeParameter] is var volume and > 0 && !(trade.Order >= volume))
            {
                return VolumeUnder;
            }
        }

        if (rule.Evaluator is IndexDriftCandidate && p[IndexDriftCandidate.PeersParameter] == 1)
        {
            var reaction = backs.TryGetValue(name, out var back) && inputs.At - back >= 0 ? inputs.Calendar[inputs.At - back] : (DateOnly?)null;

            if (reaction is not { } session || levels.PeerSurprise.GetValueOrDefault((ticker, session)) is not > 0)
            {
                return PeersNotAbove;
            }
        }

        // The switches last, as the second stage reads them after every other level.
        if (p[IndexRuleCandidate.FundOverSpyParameter] is var window and > 0
            && (window == IndexSwitches.SmallWindows[0] ? levels.FundHalfYear : levels.FundYear) is not > 1)
        {
            return SwitchClosed;
        }

        if ((p[IndexRuleCandidate.CreditAverageParameter] == 1 && levels.CreditAverage is not > 1)
            || (p[IndexRuleCandidate.CreditChangeParameter] == 1 && levels.CreditChange is not > 1))
        {
            return SwitchClosed;
        }

        return null;
    }

    // The index's sessions with the night's breadth the S&P 500's.
    static IReadOnlyList<SweepColumns.Session> WithBreadth(IReadOnlyList<SweepColumns.Session> sessions, int at, double? breadth) =>
        [.. sessions.Select((one, place) => place == at ? one with { Breadth = breadth } : one)];

}
