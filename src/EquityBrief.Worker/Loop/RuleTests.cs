using EquityBrief.Core.Candidates;
using EquityBrief.Core.Cards;
using EquityBrief.Core.Families;
using EquityBrief.Core.Filter;
using EquityBrief.Core.Ledger;
using EquityBrief.Core.Loop;
using EquityBrief.Core.Readings;
using EquityBrief.Core.Releases;
using EquityBrief.Core.Sweep;
using EquityBrief.Worker.Cards;
using EquityBrief.Worker.Indices;
using EquityBrief.Worker.Sweep;

namespace EquityBrief.Worker.Loop;

// One rule the monthly per-rule test reads on an index: its family, its name as the register holds it or the provisional
// rule's, whether it is the live rule, the provisional one or a variant, its words, its unit, the sessions a block of its
// waits, and its trades or its book's months over the whole history as its walk gave them, or why the history cannot
// walk it.
public sealed record RuleUnderTest(
    string Family,
    string Rule,
    string Standing,
    string Words,
    string Unit,
    int Cap,
    IReadOnlyList<(int Entry, double? Edge)>? Trades = null,
    IReadOnlyList<(int Opens, double? Edge)>? Months = null,
    string? Why = null)
{
    public bool Book => Unit == LoopProcedures.Points;
}

// One rule's verdict: what it was read against, its evidence over the test years and the four parts of its verdict, none
// for a rule the history cannot walk, which carries why.
public sealed record RuleTested(RuleUnderTest Rule, string Against, LoopEvidence? Evidence, LoopVerdict? Verdict, string? Why)
{
    public bool Passed => Why is null && Verdict is { Passes: true };
}

// What the per-rule test walks an index's rules over: the history as it stood, the register's rules standing when the
// run began, the setting an approval stored for each family, the market series, the heavyweights' history laid out, the
// ledger's readings, the stored trees, the S&P 500's open filter version with its history, the progress line, the
// pullback's candidates where they were read already, and from 20.8 the release readings the drift's tone variant reads.
public sealed record RuleTestInputs(
    LoopRead Read,
    IReadOnlyList<RegisterRow> Standing,
    IReadOnlyDictionary<string, LoopChange> Approved,
    IReadOnlyList<SweepMarketSeries> Market,
    HeavyweightLay Lay,
    LoopReadings Readings,
    Func<long, TreeModel?> Trees,
    (string Version, FilterSettings Settings, LargePullback History)? Filter,
    Action<string> Progress,
    IdeaReplay? Candidates = null,
    ToneReleases? Tones = null);

// From 20.8 the release readings the per-rule test walks the drift's tone variant over: for each reading version whose
// history read is complete, each stock's results releases with their readings under it, read through the one reader of
// the release readings, and the zone and the sessions the release rules place a print's window by.
// see: The release readings join the ledger's catalogue once their history read is complete, and their use is judged on the years after the model's cutoff
public sealed record ToneReleases(
    IReadOnlyDictionary<long, IReadOnlyDictionary<string, IReadOnlyList<LedgerRelease>>> Versions,
    TimeZoneInfo Zone,
    ReleaseCalendar Calendar)
{
    // A version's releases by stock, none where its history read is not complete.
    public IReadOnlyDictionary<string, IReadOnlyList<LedgerRelease>>? Of(long version) => Versions.GetValueOrDefault(version);
}

// The monthly per-rule test on one index. Every family rule standing on the index is walked at its own settings and
// hooks over the history the tester reads: the S&P 500's pullback as the swing filter at its settings, the breakout and
// the drift at their places on their sweeps' grids with a market switch read on the listing's session, the sector
// heavyweights as books at their settings, the S&P 400's and 600's provisional rules and registered rules, a market-off
// variant's walk reading its check open on every session, and the fundamentals-first family's provisional rule by its
// search's own walk at the setting and hooks an approval stored for it. Each variant is then read against its live or provisional rule by the proposals' block sign-flip
// test of the difference in edge after costs, its family's variants on the index read together by the step-down, and
// each live or provisional rule against its own benchmark under the same test, every one held to the same bar and
// screens. A rule the history cannot walk is kept with the reading it lacks and no figure. It adopts and retires nothing.
// see: Every registered rule is tested each month on the test years, each variant against its live rule and each live rule against its benchmark
public static class RuleTests
{
    public const string Live = "live";

    public const string Provisional = "provisional";

    public const string Variant = "variant";

    // What a live or provisional rule is read against.
    public const string Benchmark = "its benchmark";

    // What a variant is read against where no rule of its family stands to read it against.
    public const string NoHead = "its live rule";

    // Why a rule cannot be walked over the history.
    public const string NearestBands = "the plan at the nearest bands, which the history's walk does not draw";

    public const string LadderPlan = "the ladder's first tranche, which the history's walk does not draw";

    public const string BusinessState = "a business's reported quarters, which the history keeps no reading of";

    public const string Estimates = "the analysts' estimates, which the store keeps only from 2026-10-03";

    // From 20.8 why a drift rule reading a release reading cannot be walked: the version it names has not read its history
    // whole, so the history holds no reading of most of its prints.
    // see: The release readings join the ledger's catalogue once their history read is complete, and their use is judged on the years after the model's cutoff
    public const string ReleaseReading = "its reading version's history read is not complete";

    public const string Leadership = "sector leadership, which the history's walk does not read";

    public const string OffTheGrid = "a setting off its sweep's grid";

    public const string Switches = "a market switch other than the index over its 200-session average or the VIX under its close 10 sessions before";

    public const string HooksAndSwitch = "an approved change's hooks beside a market switch, which the history's walk does not read together";

    public const string LargeBreadth = "the S&P 500's breadth, which the history's walk does not read on this index";

    public const string NoFilterVersion = "no swing filter version stands open";

    public const string NoSix = "a book none of its design's six settings holds";

    public static string Unread(IEnumerable<string> settings) => "settings the history's walk does not read: " + string.Join(", ", settings);

    public static string HeadUnwalked(RuleUnderTest head) => $"its {head.Standing} rule cannot be walked: {head.Why}";

    // Every rule's verdict, a family at a time in the order given: its live or provisional rule against its benchmark,
    // then its variants against it together.
    public static IReadOnlyList<RuleTested> Judge(LoopRead read, IReadOnlyList<RuleUnderTest> rules)
    {
        var tested = new List<RuleTested>();

        foreach (var family in rules.GroupBy(rule => rule.Family, StringComparer.Ordinal))
        {
            var head = family.FirstOrDefault(rule => rule.Standing != Variant);

            if (head is not null)
            {
                if (head.Why is not null)
                {
                    tested.Add(new RuleTested(head, Benchmark, null, null, head.Why));
                }
                else
                {
                    var evidence = Against(read, head, null);

                    tested.Add(new RuleTested(head, Benchmark, evidence, LoopJudge.Judge([evidence], read.Opens, read.Newest, head.Cap)[0], null));
                }
            }

            var variants = family.Where(rule => rule.Standing == Variant).ToArray();
            RuleUnderTest[] walked = head is { Why: null } ? [.. variants.Where(variant => variant.Why is null)] : [];
            LoopEvidence[] evidences = [.. walked.Select(variant => Against(read, variant, head))];
            var verdicts = evidences.Length == 0 ? [] : LoopJudge.Judge(evidences, read.Opens, read.Newest, walked.Append(head!).Max(rule => rule.Cap));

            foreach (var variant in variants)
            {
                var at = Array.IndexOf(walked, variant);

                tested.Add(at >= 0
                    ? new RuleTested(variant, head!.Rule, evidences[at], verdicts[at], null)
                    : new RuleTested(variant, head?.Rule ?? NoHead, null, null, variant.Why ?? (head is null ? "no live or provisional rule of its family stands on the index" : HeadUnwalked(head))));
            }
        }

        return tested;
    }

    // A rule's evidence over the test years: its units against the rule it is read against, or against its benchmark
    // where none is given, unit for unit at an edge of nothing, since every unit's edge already stands net of it; so the
    // stability screen counts a year by the rule's own units and reads it better where its total stands above nothing.
    public static LoopEvidence Against(LoopRead read, RuleUnderTest rule, RuleUnderTest? against) =>
        rule.Book
            ? LoopProcedures.BookEvidence(read.Folds, read.Calendar, [.. read.Folds.Select(_ => rule.Months)], against?.Months ?? Level(rule.Months))
            : LoopProcedures.Evidence(read.Folds, read.Calendar, [.. read.Folds.Select(_ => rule.Trades)], against?.Trades ?? Level(rule.Trades));

    // A rule's benchmark as units: each of its own at an edge of nothing, one with no edge left with none.
    public static IReadOnlyList<(int At, double? Edge)> Level(IReadOnlyList<(int At, double? Edge)>? units) =>
        [.. (units ?? []).Select(one => (one.At, one.Edge.HasValue ? 0.0 : one.Edge))];

    // Every family rule standing on the index, walked: the families in the page's order, each one's live or provisional
    // rule first and then its variants, the pullback's candidates read once for every walk of them.
    public static IReadOnlyList<RuleUnderTest> Walk(RuleTestInputs inputs)
    {
        var large = inputs.Read.Index == WalkForwardTester.LargeIndex;
        var read = inputs.Candidates;

        IdeaReplay Candidates() => read ??= SweepIdeasRunner.Read(inputs.Read.Inputs, inputs.Market, inputs.Progress).Replay;

        var pullback = large ? null : RuleWalk.Pullback(inputs.Read, inputs.Market, inputs.Progress, inputs.Approved.GetValueOrDefault(SetupFamilies.Pullback), inputs.Readings, inputs.Trees, candidates: Candidates());

        return
        [
            .. large ? LargePullbacks(inputs) : IndexPullbacks(inputs, pullback!, Candidates),
            .. Swings(inputs, BreakoutRule.Name),
            .. Swings(inputs, DriftRule.Name),
            Fundamentals(inputs, Candidates()),
            .. Heavyweights(inputs),
        ];
    }

    static IReadOnlyList<(int Entry, double? Edge)> Units(IEnumerable<ExitProcedures.Walked> walked) => [.. walked.Select(one => (one.Entry, one.Edge))];

    static string StandingOf(RegisterRow row) => FamilyRecords.IsLive(row.Candidate) ? Live : Variant;

    // The live rule first, then the variants in the register's order.
    static IEnumerable<RegisterRow> LiveFirst(IEnumerable<RegisterRow> rows) => rows.OrderByDescending(row => FamilyRecords.IsLive(row.Candidate)).ThenBy(row => row.Id);

    // The S&P 500's pullback: each swing filter rule at its own settings, a rule reading no market check at the market
    // floor off and one keeping the night's best few at that many a night.
    static IEnumerable<RuleUnderTest> LargePullbacks(RuleTestInputs inputs)
    {
        foreach (var row in LiveFirst(inputs.Standing.Where(row => CandidateEvaluators.Find(row.Evaluator) is SwingFilterRule)))
        {
            var p = CandidateEvaluator.Read(row.Parameters);
            var live = FamilyRecords.IsLive(row.Candidate);
            var why = (p.GetValueOrDefault(SwingFilterRule.TradeParameter, 2)) switch
            {
                0 => LadderPlan,
                1 => NearestBands,
                _ => p.GetValueOrDefault(SwingFilterRule.SkipDeterioratingParameter) == 1 ? BusinessState
                    : p.GetValueOrDefault(SwingFilterRule.RaisedEstimatesParameter) == 1 ? Estimates
                    : p.GetValueOrDefault(SwingFilterRule.LeaderSectorsParameter) > 0 ? Leadership
                    : inputs.Filter is null ? NoFilterVersion
                    : null,
            };

            if (why is not null)
            {
                yield return new RuleUnderTest(SetupFamilies.Pullback, row.Candidate, StandingOf(row), row.Rule, LoopProcedures.Risks, SweepIdeas.Cap, Why: why);

                continue;
            }

            var filter = inputs.Filter!.Value;
            var settings = SwingFilterRule.SettingsOf(p);

            if (p.GetValueOrDefault(SwingFilterRule.MarketGateParameter, 1) == 0)
            {
                settings = settings with { BreadthFloor = SweepGrid.MarketOff };
            }

            var perNight = p.GetValueOrDefault(SwingFilterRule.BestOfParameter) is > 0 and var best ? (int)best : SetupFamilies.ListedANight;
            var walk = RuleWalk.LargeSwingFilter(inputs.Read, filter.History, filter.Version, settings, inputs.Progress, live ? inputs.Approved.GetValueOrDefault(SetupFamilies.Pullback) : null, inputs.Readings, inputs.Trees, perNight);

            yield return new RuleUnderTest(SetupFamilies.Pullback, row.Candidate, StandingOf(row), row.Rule, LoopProcedures.Risks, walk.Cap, Units(walk.Own));
        }
    }

    // The S&P 400's or 600's pullback: its live rule where a freeze registered one, its provisional rule otherwise, and
    // each registered rule at its dials on the extended grid.
    static IEnumerable<RuleUnderTest> IndexPullbacks(RuleTestInputs inputs, RuleWalk provisional, Func<IdeaReplay> candidates)
    {
        var rows = IndexRows(inputs, SetupFamilies.Pullback);

        if (!rows.Any(row => FamilyRecords.IsLive(row.Candidate)))
        {
            yield return new RuleUnderTest(SetupFamilies.Pullback, RuleRows.ProvisionalRule, Provisional, provisional.Current, LoopProcedures.Risks, provisional.Cap, Units(provisional.Own));
        }

        foreach (var row in LiveFirst(rows))
        {
            var p = CandidateEvaluator.Read(row.Parameters);
            var dials = DialsOf(p);
            var why = LevelsWhy(p, []) ?? (dials is null ? OffTheGrid : null);

            if (why is not null)
            {
                yield return new RuleUnderTest(SetupFamilies.Pullback, row.Candidate, StandingOf(row), row.Rule, LoopProcedures.Risks, SweepIdeas.Cap, Why: why);

                continue;
            }

            var walk = RuleWalk.Pullback(inputs.Read, inputs.Market, inputs.Progress, LoopChange.OfHooks(HooksOf(p)), inputs.Readings, inputs.Trees, dials, candidates());

            yield return new RuleUnderTest(SetupFamilies.Pullback, row.Candidate, StandingOf(row), row.Rule, LoopProcedures.Risks, walk.Cap, Units(walk.Own));
        }
    }

    // An index rule's dials on the extended grid, a pullback reading no market check at the market floor off; none where
    // a dial's value is off the grid.
    public static DialSetting? DialsOf(IReadOnlyDictionary<string, double> p)
    {
        var grid = SweepGrid.Extended;
        var market = p.GetValueOrDefault(IndexPullbackCandidate.MarketParameter) == 0 || p.GetValueOrDefault(IndexRuleCandidate.LargeBreadthParameter) == IndexRuleCandidate.NoMarketCheck
            ? SweepGrid.MarketOff
            : p[IndexPullbackCandidate.MarketParameter];
        var dryUp = p[IndexPullbackCandidate.DryUpParameter] is var ceiling && (ceiling == 0 || ceiling >= 1e9) ? SweepGrid.Off : ceiling;
        int[] at =
        [
            SweepGrid.IndexOf(grid.StrengthBars, p[IndexPullbackCandidate.StrengthParameter]),
            SweepGrid.IndexOf(grid.DepthLows, p[IndexPullbackCandidate.DepthLowParameter]),
            SweepGrid.IndexOf(grid.DepthHighs, p[IndexPullbackCandidate.DepthHighParameter]),
            SweepGrid.IndexOf(grid.DryUpCeilings, dryUp),
            SweepGrid.IndexOf(grid.Freshness, (int)p[IndexPullbackCandidate.FreshnessParameter]),
            SweepGrid.IndexOf(grid.RewardToRiskFloors, p[IndexPullbackCandidate.RewardToRiskParameter]),
            grid.StopBounds.ToList().IndexOf((p[IndexPullbackCandidate.StopLowParameter], p[IndexPullbackCandidate.StopHighParameter])),
            SweepGrid.IndexOf(grid.MarketFloors, market),
            SweepGrid.IndexOf(grid.BandStrengths, (int)p[IndexPullbackCandidate.BandParameter]),
        ];

        return at.Any(one => one < 0) ? null : new DialSetting(at[0], at[1], at[2], at[3], at[4], at[5], at[6], at[7], at[8]);
    }

    // Why an index rule's levels keep the history from walking it: a level the walk does not read moved from the one it
    // reads, or the S&P 500's breadth; none where the walk reads them all. The walk reads the dollar floor at its
    // multiple of one, the lowest close, the profit gate and the index's own breadth, or no market check.
    public static string? LevelsWhy(IReadOnlyDictionary<string, double> p, IReadOnlyList<string> dials)
    {
        if (p.GetValueOrDefault(IndexRuleCandidate.LargeBreadthParameter) == 1)
        {
            return LargeBreadth;
        }

        var unread = new List<string>();

        void Read(string name, double reads)
        {
            if (p.TryGetValue(name, out var stated) && stated != reads)
            {
                unread.Add(name);
            }
        }

        Read(IndexRuleCandidate.FloorsParameter, 1);
        Read(IndexRuleCandidate.LowestCloseParameter, Core.Prices.Statistic.FromPrice(MemberReadings.LowestPrice));
        Read(IndexRuleCandidate.QualityParameter, 1);

        foreach (var name in new[] { IndexRuleCandidate.HoldParameter, IndexRuleCandidate.IndustryFallParameter, IndexRuleCandidate.FundOverSpyParameter, IndexRuleCandidate.CreditAverageParameter, IndexRuleCandidate.CreditChangeParameter }.Concat(dials))
        {
            Read(name, 0);
        }

        return unread.Count > 0 ? Unread(unread) : null;
    }

    // The hooks a rule's parameters state.
    static IReadOnlyDictionary<string, double> HooksOf(IReadOnlyDictionary<string, double> p) =>
        p.Where(pair => RuleHooks.IsHook(pair.Key)).ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);

    static RegisterRow[] IndexRows(RuleTestInputs inputs, string family) =>
        [.. inputs.Standing.Where(row => CandidateEvaluators.Find(row.Evaluator) is IndexRuleCandidate rule && rule.Index == inputs.Read.Index && rule.SetupFamily == family)];

    // The breakout or the drift: on the S&P 500 each registered rule, on the S&P 400 or 600 its live rule or its
    // provisional one and each registered rule, each at its places on its sweep's grid.
    static IEnumerable<RuleUnderTest> Swings(RuleTestInputs inputs, string family)
    {
        var read = inputs.Read;
        var large = read.Index == WalkForwardTester.LargeIndex;
        RegisterRow[] rows = large
            ? [.. inputs.Standing.Where(row => CandidateEvaluators.Find(row.Evaluator) is FamilyRuleEvaluator { Family: var own } evaluator && evaluator is not IndexRuleCandidate && own == family)]
            : IndexRows(inputs, family);

        if (!large && !rows.Any(row => FamilyRecords.IsLive(row.Candidate)))
        {
            var provisional = RuleWalk.Swing(read, family, false, inputs.Approved.GetValueOrDefault(family), inputs.Readings, inputs.Trees);

            yield return new RuleUnderTest(family, RuleRows.ProvisionalRule, Provisional, provisional.Current, LoopProcedures.Risks, provisional.Cap, Units(provisional.Own));
        }

        (bool[] Average, bool[] Vix)? switches = large ? SwitchesOf(read, inputs.Market) : null;

        foreach (var row in LiveFirst(rows))
        {
            yield return SwingAt(inputs, family, row, large, switches);
        }
    }

    // The index's two switches a registration may read, on each session of the calendar: the index above its 200-session
    // average and the VIX under its close 10 sessions before, each read on the session's close as the ideas' run reads it.
    static (bool[] Average, bool[] Vix) SwitchesOf(LoopRead read, IReadOnlyList<SweepMarketSeries> market)
    {
        var sessions = read.Calendar.Length;
        var index = SweepIdeas.OnCalendar(market.FirstOrDefault(one => one.Series == "GSPC"), read.Calendar);
        var vix = SweepIdeas.OnCalendar(market.FirstOrDefault(one => one.Series == "VIX"), read.Calendar);
        var passes = SweepIdeas.Switches(new double[sessions], new int[sessions], new int[sessions], index, vix);

        return (passes[(int)MarketSwitch.IndexOverTwoHundred], passes[(int)MarketSwitch.VixFalling]);
    }

    static RuleUnderTest SwingAt(RuleTestInputs inputs, string family, RegisterRow row, bool large, (bool[] Average, bool[] Vix)? switches)
    {
        var p = CandidateEvaluator.Read(row.Parameters);
        var breakout = family == BreakoutRule.Name;
        var grid = breakout ? BreakoutSweep.Grid : DriftSweep.Grid;

        static double Ceiling(double value) => value >= 1e9 ? double.PositiveInfinity : value;

        double[] values = breakout
            ? [p[BreakoutCandidate.HighSessionsParameter], p[BreakoutCandidate.VolumeMultipleParameter], Ceiling(p[BreakoutCandidate.RangeCeilingParameter]), p[BreakoutCandidate.StopMovesParameter]]
            : [p[DriftCandidate.WindowSessionsParameter], p[DriftCandidate.ReactionMovesParameter], p[DriftCandidate.VolumeMultipleParameter], p.GetValueOrDefault(DriftCandidate.TargetRiskMultipleParameter, p.GetValueOrDefault(IndexDriftCandidate.TargetMultipleParameter))];
        var floor = breakout ? 0 : p.GetValueOrDefault(DriftCandidate.StopFloorMovesParameter, p.GetValueOrDefault(IndexDriftCandidate.StopFloorParameter));
        var places = IndexNightRead.Places(grid, values);
        var hooks = HooksOf(p);
        var marketSwitches = large ? MarketSwitches.Of(p) : MarketSwitches.None;
        string[] dials = breakout
            ? [IndexBreakoutCandidate.NearnessParameter, IndexBreakoutCandidate.RecencyParameter, IndexBreakoutCandidate.HighVolumeParameter]
            : [IndexDriftCandidate.WideWindowParameter, IndexDriftCandidate.PeersParameter];
        if (NotWalkedYet(family, row, inputs.Tones) is { } kept)
        {
            return kept;
        }

        var version = ReleaseTone.VersionOf(p);
        var releases = version > 0 ? inputs.Tones!.Of(version) : null;

        var why = (large ? null : LevelsWhy(p, dials))
            ?? (places.Any(place => place < 0) ? OffTheGrid : null)
            ?? (marketSwitches.IndexAverageSessions is not (0 or SweepIdeas.SlowAverage) || marketSwitches.VixLookbackSessions is not (0 or SweepIdeas.Lookback) ? Switches : null)
            ?? (hooks.Count > 0 && !marketSwitches.Equals(MarketSwitches.None) ? HooksAndSwitch : null);

        if (why is not null)
        {
            return new RuleUnderTest(family, row.Candidate, StandingOf(row), row.Rule, LoopProcedures.Risks, 0, Why: why);
        }

        var change = new LoopChange(places, grid.Key(places), floor > 0 ? floor : null, hooks);
        var walk = RuleWalk.Swing(inputs.Read, family, large, change, inputs.Readings, inputs.Trees, marketOff: !large && p.GetValueOrDefault(IndexRuleCandidate.LargeBreadthParameter) == IndexRuleCandidate.NoMarketCheck);

        bool Open(int at)
        {
            var session = walk.Listings[at].Session;

            return (marketSwitches.IndexAverageSessions == 0 || switches!.Value.Average[session])
                && (marketSwitches.VixLookbackSessions == 0 || switches!.Value.Vix[session]);
        }

        // A switched rule keeps the listings of sessions its switch stands open on, and from 20.8 the tone variant those
        // whose print's release passes its condition, as the night reads the condition after the switches.
        bool Kept(int at) =>
            (marketSwitches.Equals(MarketSwitches.None) || Open(at))
            && (releases is null || ToneHolds(inputs.Read, walk.Listings[at], releases, inputs.Tones!));

        var trades = marketSwitches.Equals(MarketSwitches.None) && releases is null ? walk.Own : walk.Keeping(Kept);

        return new RuleUnderTest(family, row.Candidate, StandingOf(row), row.Rule, LoopProcedures.Risks, walk.Cap, Units(trades));
    }

    // A swing rule kept with why before any walk is read: from 20.8 a drift rule reading a release reading under a version
    // whose history read is not complete, since the history holds no reading of most of its prints; none for a rule whose
    // walk is read.
    // see: The release readings join the ledger's catalogue once their history read is complete, and their use is judged on the years after the model's cutoff
    public static RuleUnderTest? NotWalkedYet(string family, RegisterRow row, ToneReleases? tones) =>
        ReleaseTone.VersionOf(CandidateEvaluator.Read(row.Parameters)) is var version and > 0 && tones?.Of(version) is null
            ? new RuleUnderTest(family, row.Candidate, StandingOf(row), row.Rule, LoopProcedures.Risks, 0, Why: ReleaseReading)
            : null;

    // From 20.8 whether a drift listing over the history passes the tone condition: the release the night of the listing's
    // session reads for the newest print the listing reacts to, by the one availability rule over the stock's releases
    // under the version, read better with its outlook not lowered or withdrawn. A listing whose stock holds no release
    // under the version, or whose release holds no reading, passes none, as on the night.
    // see: The drift's tone variant lists only where its print's release reads better than expected and its outlook was not lowered or withdrawn
    public static bool ToneHolds(LoopRead read, RuleListing listing, IReadOnlyDictionary<string, IReadOnlyList<LedgerRelease>> releases, ToneReleases tones)
    {
        var series = read.Series[listing.Name];

        if (series.NewestSurprise[listing.Bar] is not (var newest and >= 0) || !releases.TryGetValue(series.Name.Ticker, out var held))
        {
            return false;
        }

        var release = ReleaseAvailability.ReleaseFor(held, one => one.Accepted, series.Name.Surprises[newest].EventDate, read.Calendar[listing.Session], tones.Zone, tones.Calendar);

        return release?.Answer is { } answer && ReleaseTone.Passes(answer);
    }

    // The fundamentals-first family's provisional rule, by its search's own walk: the pullback's base listings kept where
    // the family's parts pass at the setting an approval stored for it on the index or its provisional one, walked at the
    // hooks that approval stored and at none of the pullback's.
    // see: The fundamentals-first family is proposed for on every index over its 27 settings, and an approved setting is read by the night in the provisional one's place
    static RuleUnderTest Fundamentals(RuleTestInputs inputs, IdeaReplay candidates)
    {
        var standing = inputs.Approved.GetValueOrDefault(FundamentalsRule.Name);
        var (setting, words) = FundamentalsStanding(standing);
        var walk = RuleWalk.Fundamentals(inputs.Read, candidates, inputs.Readings, setting, inputs.Progress, standing, inputs.Trees);

        return new RuleUnderTest(FundamentalsRule.Name, RuleRows.ProvisionalRule, Provisional, words, LoopProcedures.Risks, walk.Cap, Units(walk.Own));
    }

    // The setting the family's provisional rule is walked at and its words: the one an approval stored, with the hooks it
    // stored, or the provisional setting where none was stored.
    public static (FundamentalsSetting Setting, string Words) FundamentalsStanding(LoopChange? standing)
    {
        var setting = FundamentalsRule.StoredOr(standing?.Places);

        return (setting, RuleHooks.Of(standing?.Hooks ?? LoopChange.NoHooks).Words() is { } hooked ? setting.Key + "; " + hooked : setting.Key);
    }

    // The sector heavyweights: on the S&P 500 each registered book at its setting, on the S&P 400 or 600 its live book
    // or its provisional one and each registered book at the one of its design's six settings it states.
    static IEnumerable<RuleUnderTest> Heavyweights(RuleTestInputs inputs)
    {
        var read = inputs.Read;
        var large = read.Index == WalkForwardTester.LargeIndex;

        if (large)
        {
            foreach (var row in LiveFirst(inputs.Standing.Where(row => CandidateEvaluators.Find(row.Evaluator) is SectorHeavyweightCandidate)))
            {
                var book = inputs.Lay.BookOf(SettingOf(CandidateEvaluator.Read(row.Parameters)), row.Rule);

                yield return new RuleUnderTest(HeavyweightRule.Name, row.Candidate, StandingOf(row), row.Rule, LoopProcedures.Points, BookMonths.LongestMonth, Months: book.Months);
            }

            yield break;
        }

        var rows = inputs.Standing.Where(row => CandidateEvaluators.Find(row.Evaluator) is IndexHeavyweightCandidate rule && rule.Index == read.Index).ToArray();

        if (!rows.Any(row => FamilyRecords.IsLive(row.Candidate)))
        {
            var provisional = inputs.Lay.Book(HeavyweightSix.DesignA[0], designA: true);

            yield return new RuleUnderTest(HeavyweightRule.Name, RuleRows.ProvisionalRule, Provisional, RuleReplay.HeavyweightWords(IndexHeavyweights.Provisional), LoopProcedures.Points, BookMonths.LongestMonth, Months: provisional.Months);
        }

        foreach (var row in LiveFirst(rows))
        {
            var p = CandidateEvaluator.Read(row.Parameters);
            var designA = p.GetValueOrDefault(IndexHeavyweightCandidate.DesignParameter) == IndexHeavyweightCandidate.DesignA;
            var six = p.GetValueOrDefault(IndexRuleCandidate.FloorsParameter, 1) == 1 ? SixOf(p, designA) : null;

            yield return six is null
                ? new RuleUnderTest(HeavyweightRule.Name, row.Candidate, StandingOf(row), row.Rule, LoopProcedures.Points, BookMonths.LongestMonth, Why: NoSix)
                : new RuleUnderTest(HeavyweightRule.Name, row.Candidate, StandingOf(row), row.Rule, LoopProcedures.Points, BookMonths.LongestMonth, Months: inputs.Lay.Book(six, designA).Months);
        }
    }

    // An S&P 500 heavyweights registration's setting as its sweep's grid holds it.
    public static HeavyweightSetting SettingOf(IReadOnlyDictionary<string, double> p) => new(
        p[SectorHeavyweightCandidate.LargestParameter] == 0 ? HeavyweightSweep.EveryCompany : (int)p[SectorHeavyweightCandidate.LargestParameter],
        (int)p[SectorHeavyweightCandidate.LookBackParameter],
        (int)p[SectorHeavyweightCandidate.LeadersParameter],
        p.GetValueOrDefault(SectorHeavyweightCandidate.FundParameter) == 1 ? HeavyweightSectorReturn.Fund : HeavyweightSectorReturn.Members,
        p.GetValueOrDefault(SectorHeavyweightCandidate.BetaParameter) == 1,
        p.GetValueOrDefault(SectorHeavyweightCandidate.WeeklyParameter) == 1 ? HeavyweightPeriod.Week : HeavyweightPeriod.Month,
        (p.GetValueOrDefault(SectorHeavyweightCandidate.LeadingExitParameter), p.GetValueOrDefault(SectorHeavyweightCandidate.AverageExitParameter)) switch
        {
            (1, 1) => HeavyweightExit.Both,
            (1, _) => HeavyweightExit.Drop,
            _ => HeavyweightExit.Break,
        });

    // The one of a design's six settings an S&P 400 or 600 registration states, none where it states another.
    public static HeavyweightSixSetting? SixOf(IReadOnlyDictionary<string, double> p, bool designA)
    {
        var twelveOne = p.GetValueOrDefault(IndexHeavyweightCandidate.TwelveOneParameter) == 1;
        var quality = p.GetValueOrDefault(IndexRuleCandidate.QualityParameter, 1) switch
        {
            0 => IndexQuality.Off,
            2 => IndexQuality.Cover,
            _ => IndexQuality.Profit,
        };

        if (designA)
        {
            var leaders = IndexRules.HeavyweightSettingOf(p);

            return HeavyweightSix.DesignA.FirstOrDefault(six => six.TwelveOne == twelveOne && six.Quality == quality && !six.Quarterly && six.Leaders == leaders);
        }

        return p.GetValueOrDefault(IndexHeavyweightCandidate.WindowParameter) == HeavyweightSix.BaseWindow && p.GetValueOrDefault(IndexHeavyweightCandidate.IndustriesParameter) == HeavyweightSix.BaseIndustries
            ? HeavyweightSix.DesignB.FirstOrDefault(six => six.TwelveOne == twelveOne && six.Quality == quality && !six.Quarterly && six.PerIndustry == (int)p.GetValueOrDefault(IndexHeavyweightCandidate.MembersParameter, 1))
            : null;
    }
}
