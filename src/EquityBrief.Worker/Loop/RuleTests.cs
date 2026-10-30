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
// walk it; from 21.1 a tone variant carries its opposite's walk beside its own.
public sealed record RuleUnderTest(
    string Family,
    string Rule,
    string Standing,
    string Words,
    string Unit,
    int Cap,
    IReadOnlyList<(int Entry, double? Edge)>? Trades = null,
    IReadOnlyList<(int Opens, double? Edge)>? Months = null,
    string? Why = null,
    ToneSides? Tone = null,
    IReadOnlyList<(int Entry, double? Edge)>? OpenFloor = null,
    int Skipped = 0)
{
    public bool Book => Unit == LoopProcedures.Points;
}

// From 21.1 a tone variant's walk and its opposite's over the same history, each trade by the session it was listed on,
// with the reading version and the local model's cutoff it states, for the figures the run prints on each side of the
// cutoff's year; the opposite is walked unregistered and written nowhere.
// see: Every registered rule is walked by the per-rule test at what it reads, and one the history cannot walk names what judges it instead
public sealed record ToneSides(long Version, DateOnly? Cutoff, IReadOnlyList<(int Entry, double? Edge)> Variant, IReadOnlyList<(int Entry, double? Edge)> Opposite);

// From 21.1 the sweeps' own readings of an index's history the per-rule test walks a registered rule through where it
// states a level the extended grid's walk does not read: the breakout's and the drift's at each stop floor a drift rule
// states, and the pullback's search's, each read once a run and only where a rule standing on the index needs it, the
// pullback's charged its round trip once.
// see: Every registered rule is walked by the per-rule test at what it reads, and one the history cannot walk names what judges it instead
public sealed record OwnReads(
    IReadOnlyDictionary<(string Family, double StopFloor), IndexSweepRunner.OwnSwingRead> Swings,
    IndexSweepRunner.OwnPullbackRead? Pullback)
{
    public static OwnReads None { get; } = new(new Dictionary<(string Family, double StopFloor), IndexSweepRunner.OwnSwingRead>(), null);
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
// pullback's candidates where they were read already, from 20.8 the release readings the drift's tone variant reads, from
// 22.11 Quality at a fair price's history laid out where a freeze stands on the index, from 22.3 the short-term dip's
// history as its sweep reads it where a freeze stands on the index, and from 21.1 the sweeps' own readings of the index's
// history where a registered rule states a level the extended grid's walk does not read.
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
    ToneReleases? Tones = null,
    QualityLay? Quality = null,
    IndexSweepRunner.DipRead? Dip = null,
    ImprovingLay? Improving = null,
    OwnReads? Own = null,
    Func<int, int, string?>? FiledStates = null)
{
    // From 21.4 the business's state off the SEC's facts as first filed for a stock on a session, which the S&P 500 pullback
    // version leaving off a deteriorating business is walked at: the one handed in, or the catalogue's own through the
    // readings; none where the run handed neither, which keeps that version with the reading it lacks.
    public Func<int, int, string?>? FiledStateOf => FiledStates ?? (Readings is { } readings ? readings.BusinessStateFiled : null);
}

// From 20.8 the release readings the per-rule test walks the drift's tone variant over: for each reading version whose
// history read is complete, each stock's results releases with their readings under it, read through the one reader of
// the release readings, and the zone and the sessions the release rules place a print's window by; from 21.1 the local
// model's cutoff each version states, which the tone figures are split at.
// see: The release readings join the ledger's catalogue once their history read is complete, and their use is judged on the years after the model's cutoff
public sealed record ToneReleases(
    IReadOnlyDictionary<long, IReadOnlyDictionary<string, IReadOnlyList<LedgerRelease>>> Versions,
    TimeZoneInfo Zone,
    ReleaseCalendar Calendar,
    IReadOnlyDictionary<long, DateOnly?>? Cutoffs = null)
{
    // A version's releases by stock, none where its history read is not complete.
    public IReadOnlyDictionary<string, IReadOnlyList<LedgerRelease>>? Of(long version) => Versions.GetValueOrDefault(version);

    // A version's cutoff, none where it states none or the run was handed none.
    public DateOnly? CutoffOf(long version) => Cutoffs is { } cutoffs && cutoffs.TryGetValue(version, out var cutoff) ? cutoff : null;
}

// The monthly per-rule test on one index. Every family rule standing on the index is walked at its own settings and
// hooks over the history the tester reads: the S&P 500's pullback as the swing filter at its settings and, from 21.1, at
// the plan its version's trade gate reads, the breakout and the drift at their places on their sweeps' grids with a market
// switch read on the listing's session, the sector heavyweights as books at their settings, the S&P 400's and 600's
// provisional rules and registered rules, a market-off variant's walk reading its check open on every session, from 21.1
// a registered rule stating a level the extended grid's walk does not read walked by its sweep's own reading at every
// level it states and a book at any setting of its design's grid by the lay, and the fundamentals-first family's
// provisional rule by its search's own walk at the setting and hooks an approval stored for it. Each variant is then read
// against its live or provisional rule by the proposals' block sign-flip test of the difference in edge after costs, its
// family's variants on the index read together by the step-down, and each live or provisional rule against its own
// benchmark under the same test, every one held to the same bar and screens. A rule the history cannot walk is kept with
// the reading it lacks and no figure, and its verdict names what judges it instead. It adopts and retires nothing.
// see: Every registered rule is tested each month on the test years, each variant against its live rule and each live rule against its benchmark
// see: Every registered rule is walked by the per-rule test at what it reads, and one the history cannot walk names what judges it instead
public static class RuleTests
{
    public const string Live = "live";

    public const string Provisional = "provisional";

    public const string Variant = "variant";

    // What a live or provisional rule is read against.
    public const string Benchmark = "its benchmark";

    // What a variant is read against where no rule of its family stands to read it against.
    public const string NoHead = "its live rule";

    // Why a rule cannot be walked over the history; from 21.1 a plan the version's trade gate reads is walked by the ideas'
    // run's plan of that kind and no longer kept, and from 21.4 a version reading the business's state off the SEC's facts
    // as first filed is walked at it, the provider's reported quarters alone being what the history keeps no reading of.
    public const string BusinessState = "a business's reported quarters, which the history keeps no reading of";

    // From 21.4 why a version reading the business's state off the SEC's facts was not walked: the run handed the walk no
    // reading of the facts, which only a run reading the history without the ledger's readings does.
    public const string FiledStateUnread = "the business's state off the SEC's facts as first filed, which the run handed no reading of";

    // From 21.1 why an S&P 400 or 600 rule stating a level the extended grid's walk does not read was not walked by its
    // sweep's own reading: the run laid out no such reading for its family, or its book reads twelve months' strength at
    // a window or an industries count no sweep on the index's own history read.
    public const string NoOwnRead = "a level its sweep's own reading would walk, which the run laid out no reading for";

    public const string TwelveOneOffTheBase = "twelve months' strength at a window or an industries count other than design (b)'s base, which no sweep on the index's own history read";

    // From 21.1 the tone condition's opposite, in the words the run prints.
    public const string OppositeWords = "a release read whose results are not better or whose outlook was lowered or withdrawn";

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
    // rule first and then its variants, the pullback's candidates read once for every walk of them. From 21.5 the
    // fundamentals-first family, which the night computes no longer, is walked no more.
    // see: The monthly loop proposes for seven families on every index where their rule stands, and the fundamentals-first family leaves it with the night
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
            .. Heavyweights(inputs),
            .. (inputs.Quality is { } lay ? Quality(inputs.Standing, inputs.Read.Index, setting => lay.Book(setting).Months) : []),
            .. (inputs.Dip is { } dip ? Dip(inputs.Standing, inputs.Read.Index, parameters => DipTrades(dip, parameters)) : []),
            .. (inputs.Improving is { } improving ? Improving(inputs.Standing, inputs.Read.Index, setting => improving.Book(setting).Months) : []),
        ];
    }

    // From 22.15 the Fundamentals family on any index once a freeze stands on it: each registered rule's book walked by the
    // history test's own lay at its setting, its months the unit, the live rule first; none before a freeze, so the
    // monthly run reads the family from its next run after one.
    // see: Each index's Fundamentals rule is frozen on the operator's answers of 2026-10-28 with its one-step neighbours as versions
    public static IEnumerable<RuleUnderTest> Improving(IReadOnlyList<RegisterRow> standing, string index, Func<ImprovingSweepSetting, IReadOnlyList<(int Opens, double? Edge)>> months)
    {
        foreach (var row in LiveFirst(standing.Where(row => CandidateEvaluators.Find(row.Evaluator) is ImprovingCandidate rule && rule.Index == index)))
        {
            var setting = ImprovingCandidate.SettingsOf(CandidateEvaluator.Read(row.Parameters));

            yield return new RuleUnderTest(ImprovingRule.Name, row.Candidate, StandingOf(row), setting.Words, LoopProcedures.Points, BookMonths.LongestMonth, Months: months(setting));
        }
    }

    // From 22.3 the short-term dip on any index once a freeze stands on it: each registered rule's trades walked at its
    // setting over the whole history, the live rule first, its time limit and the next open the sessions a block waits;
    // none before a freeze, so the monthly run reads the family from its next run after one.
    // see: Each index's dip is frozen at the setting its sweep would run with the neighbours meeting the floors and the market variant as versions
    public static IEnumerable<RuleUnderTest> Dip(IReadOnlyList<RegisterRow> standing, string index, Func<IReadOnlyDictionary<string, double>, IReadOnlyList<(int Entry, double? Edge)>> trades)
    {
        foreach (var row in LiveFirst(standing.Where(row => CandidateEvaluators.Find(row.Evaluator) is DipCandidate rule && rule.Index == index)))
        {
            var parameters = CandidateEvaluator.Read(row.Parameters);
            var setting = ((DipCandidate)CandidateEvaluators.Find(row.Evaluator)!).SettingOf(parameters);

            yield return new RuleUnderTest(DipRule.Name, row.Candidate, StandingOf(row), setting.WordsOn(index), LoopProcedures.Risks, setting.TimeLimit + 1, Trades: trades(parameters));
        }
    }

    // A dip rule's trades over its sweep's history at the setting its registration states, walked by the sweep's own
    // code, five a night and one open trade a stock, each after its round trip, by the session it was listed on.
    public static IReadOnlyList<(int Entry, double? Edge)> DipTrades(IndexSweepRunner.DipRead dip, IReadOnlyDictionary<string, double> parameters)
    {
        var (setting, levels) = dip.SettingOf(parameters);

        return [.. dip.TradesUnder(setting, levels).Select(trade => (trade.Listing.Session, new OwnProcedures.Walked(trade.Listing.Session, null, trade).Edge))];
    }

    // From 22.11 Quality at a fair price on any index once a freeze stands on it: each registered rule's book walked by the
    // history test's own lay at its setting, its months the unit, the live rule first; none before a freeze, so the
    // monthly run reads the family from its next run after one.
    // see: Each index's quality rule is frozen at its starting setting on the operator's word, with its one-step neighbours as versions
    public static IEnumerable<RuleUnderTest> Quality(IReadOnlyList<RegisterRow> standing, string index, Func<QualitySettings, IReadOnlyList<(int Opens, double? Edge)>> months)
    {
        foreach (var row in LiveFirst(standing.Where(row => CandidateEvaluators.Find(row.Evaluator) is QualityCandidate rule && rule.Index == index)))
        {
            var setting = QualityCandidate.SettingsOf(CandidateEvaluator.Read(row.Parameters));

            yield return new RuleUnderTest(QualityRule.Name, row.Candidate, StandingOf(row), setting.Words, LoopProcedures.Points, BookMonths.LongestMonth, Months: months(setting));
        }
    }

    static IReadOnlyList<(int Entry, double? Edge)> Units(IEnumerable<ExitProcedures.Walked> walked) => [.. walked.Select(one => (one.Entry, one.Edge))];

    // The sweep's own trades as units, each by the session it was listed on with its edge after costs over the same plan
    // on every member, as the dip's walk is read.
    static IReadOnlyList<(int Entry, double? Edge)> Units(IEnumerable<FamilyTrade> trades) =>
        [.. trades.Select(trade => (trade.Listing.Session, new OwnProcedures.Walked(trade.Listing.Session, null, trade).Edge))];

    static string StandingOf(RegisterRow row) => FamilyRecords.IsLive(row.Candidate) ? Live : Variant;

    // The live rule first, then the variants in the register's order.
    static IEnumerable<RegisterRow> LiveFirst(IEnumerable<RegisterRow> rows) => rows.OrderByDescending(row => FamilyRecords.IsLive(row.Candidate)).ThenBy(row => row.Id);

    // The S&P 500's pullback: each swing filter rule at its own settings, a rule reading no market check at the market
    // floor off, one keeping the night's best few at that many a night, from 21.1 one whose trade gate reads the plan at
    // the nearest bands or the ladder's first tranche at that plan, which the ideas' run reads for every candidate, and
    // from 21.4 one leaving off a business the SEC's facts as first filed read deteriorating walked over the listings
    // whose state on the night read anything else or none, the state read off the catalogue's own function over the facts
    // filed before each session. A version reading a business's reported quarters, the analysts' estimates or sector
    // leadership is kept with the reading it lacks, which the history holds no reading of.
    // see: The seventh swing family candidate leaves off a member whose business the SEC's facts as first filed read deteriorating through the ledger's catalogue, and the nine are registered again at 21.4 with the eight unchanged versions keeping their records
    public static IEnumerable<RuleUnderTest> LargePullbacks(RuleTestInputs inputs)
    {
        var filedStates = inputs.FiledStateOf;

        foreach (var row in LiveFirst(inputs.Standing.Where(row => CandidateEvaluators.Find(row.Evaluator) is SwingFilterRule)))
        {
            var p = CandidateEvaluator.Read(row.Parameters);
            var live = FamilyRecords.IsLive(row.Candidate);
            var leavesOff = p.GetValueOrDefault(SwingFilterRule.SkipDeterioratingParameter) == 1;
            var readsFiled = p.GetValueOrDefault(SwingFilterRule.FiledStateParameter) == 1;
            var why = leavesOff && !readsFiled ? BusinessState
                : leavesOff && filedStates is null ? FiledStateUnread
                : p.GetValueOrDefault(SwingFilterRule.RaisedEstimatesParameter) == 1 ? Estimates
                : p.GetValueOrDefault(SwingFilterRule.LeaderSectorsParameter) > 0 ? Leadership
                : inputs.Filter is null ? NoFilterVersion
                : null;

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
            var walk = RuleWalk.LargeSwingFilter(inputs.Read, filter.History, filter.Version, settings, inputs.Progress, live ? inputs.Approved.GetValueOrDefault(SetupFamilies.Pullback) : null, inputs.Readings, inputs.Trees, perNight, PlanOf(p));

            // The version leaving off a deteriorating business is walked over the listings its state keeps, each read on
            // the listing's own session, so its trades are the live filter's less the stocks it leaves off that night.
            var own = leavesOff
                ? walk.Keeping(at => !string.Equals(filedStates!(walk.Listings[at].Name, walk.Listings[at].Session), Core.Quarters.FundamentalState.Deteriorating, StringComparison.Ordinal))
                : walk.Own;

            yield return new RuleUnderTest(SetupFamilies.Pullback, row.Candidate, StandingOf(row), row.Rule, LoopProcedures.Risks, walk.Cap, Units(own));
        }
    }

    // From 21.1 the plan a swing filter version's trade gate reads, from its trade parameter: the ladder's first tranche,
    // the plan at the nearest bands, or section 10's, the live plan.
    public static PlanRule PlanOf(IReadOnlyDictionary<string, double> p) => p.GetValueOrDefault(SwingFilterRule.TradeParameter, 2) switch
    {
        0 => PlanRule.Ladder,
        1 => PlanRule.NearestBands,
        _ => PlanRule.Clear,
    };

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

            // From 21.1 a rule stating a level the extended grid's walk does not read, its market check's floor or the
            // profit and cover check among them, is walked by its search's own reading at every level it states.
            if (CandidateEvaluators.Find(row.Evaluator) is IndexRuleCandidate own && ReadsOwnSweep(own, p))
            {
                yield return ThroughOwnSearch(inputs.Own?.Pullback, own, row, p);

                continue;
            }

            if (DialsOf(p) is not { } dials)
            {
                yield return new RuleUnderTest(SetupFamilies.Pullback, row.Candidate, StandingOf(row), row.Rule, LoopProcedures.Risks, SweepIdeas.Cap, Why: OffTheGrid);

                continue;
            }

            var walk = RuleWalk.Pullback(inputs.Read, inputs.Market, inputs.Progress, LoopChange.OfHooks(HooksOf(p)), inputs.Readings, inputs.Trees, dials, candidates());

            yield return new RuleUnderTest(SetupFamilies.Pullback, row.Candidate, StandingOf(row), row.Rule, LoopProcedures.Risks, walk.Cap, Units(walk.Own));
        }
    }

    // From 21.1 a registered pullback walked by its search's own reading of the index's history at the setting and every
    // level its registration states, as its record is replayed: every candidate's plan bought at the open after its listing
    // and charged its round trip, the stages' own walk with one open trade a stock, each kept pick's result less the same
    // plan on every member that session; kept with why where the search reads no such setting or the run laid out no
    // reading.
    // see: Every registered rule is walked by the per-rule test at what it reads, and one the history cannot walk names what judges it instead
    public static RuleUnderTest ThroughOwnSearch(IndexSweepRunner.OwnPullbackRead? own, IndexRuleCandidate evaluator, RegisterRow row, IReadOnlyDictionary<string, double> p)
    {
        RuleUnderTest Kept(string why) => new(SetupFamilies.Pullback, row.Candidate, StandingOf(row), row.Rule, LoopProcedures.Risks, SweepIdeas.Cap, Why: why);

        if (own is null)
        {
            return Kept(NoOwnRead);
        }

        var (at, refused) = RuleReplay.OwnPullback(own, evaluator, p);

        if (at is null)
        {
            return Kept(refused!);
        }

        var (design, picks) = own.Under(at.Levels);
        var setting = own.SettingUnder(at.Point, at.Levels);
        var walked = new HashSet<(int Name, int Session)>();

        SweepStages.Measures(picks, design, setting, own.Space.Conditions(at.Point), own.Nights, walked);

        var exit = design.ExitIndex;
        var floor = SweepStages.StopFloor(setting);
        var units = new List<(int Entry, double? Edge)>();

        foreach (var pick in picks)
        {
            if (!walked.Remove((pick.Name, pick.Session)))
            {
                continue;
            }

            var plan = pick.Plan;
            double? edge = pick.Year is >= 0 and < SweepFigures.Years
                && plan.Code[exit] is SweepPlanOutcomes.Win or SweepPlanOutcomes.Loss or SweepPlanOutcomes.Unresolved
                && !float.IsNaN(plan.Multiple[exit])
                && !float.IsNaN(plan.Benchmark[exit])
                && plan.IsATradeAt(exit, floor)
                    ? plan.EdgeAt(exit)
                    : null;

            units.Add((pick.Session, edge));
        }

        return new RuleUnderTest(SetupFamilies.Pullback, row.Candidate, StandingOf(row), row.Rule, LoopProcedures.Risks, SweepIdeas.Cap, [.. units.OrderBy(unit => unit.Entry)]);
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
    // multiple of one, the lowest close, the profit gate and the index's own breadth at the filter's floor, or no market
    // check; from 20.11 a floor of the index's own breadth other than the filter's is a level it does not read.
    // see: Each frozen rule on the S&P 400 and 600 reads its market check at the level its own sweep chose
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

        if (p.GetValueOrDefault(IndexRuleCandidate.LargeBreadthParameter) == 0)
        {
            Read(IndexRuleCandidate.BreadthFloorParameter, FamilySweep.MarketFloor);
        }

        foreach (var name in new[] { IndexRuleCandidate.HoldParameter, IndexRuleCandidate.IndustryFallParameter, IndexRuleCandidate.FundOverSpyParameter, IndexRuleCandidate.CreditAverageParameter, IndexRuleCandidate.CreditChangeParameter }.Concat(dials))
        {
            Read(name, 0);
        }

        return unread.Count > 0 ? Unread(unread) : null;
    }

    // The hooks a rule's parameters state.
    static IReadOnlyDictionary<string, double> HooksOf(IReadOnlyDictionary<string, double> p) =>
        p.Where(pair => RuleHooks.IsHook(pair.Key)).ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);

    // An index family's standing rules the test reads, the rule of the day before a freeze kept in the background left out,
    // which no monthly test reads.
    // see: A freeze on the S&P 400 or 600 registers a market variant beside each swing rule and keeps the rule it replaces in the background for 63 sessions
    static RegisterRow[] IndexRows(RuleTestInputs inputs, string family) =>
        [.. inputs.Standing.Where(row => CandidateEvaluators.Find(row.Evaluator) is IndexRuleCandidate rule && rule.Index == inputs.Read.Index && rule.SetupFamily == family && !FamilyRecords.IsBackground(row.Candidate))];

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

    // A breakout's or a drift's registration as its sweep's grid reads it: the grid, each dial's value, and the drift's
    // stop floor in typical moves, none for the breakout.
    static (FamilyGrid Grid, double[] Values, double Floor) GridOf(string family, IReadOnlyDictionary<string, double> p)
    {
        var breakout = family == BreakoutRule.Name;

        static double Ceiling(double value) => value >= 1e9 ? double.PositiveInfinity : value;

        double[] values = breakout
            ? [p[BreakoutCandidate.HighSessionsParameter], p[BreakoutCandidate.VolumeMultipleParameter], Ceiling(p[BreakoutCandidate.RangeCeilingParameter]), p[BreakoutCandidate.StopMovesParameter]]
            : [p[DriftCandidate.WindowSessionsParameter], p[DriftCandidate.ReactionMovesParameter], p[DriftCandidate.VolumeMultipleParameter], p.GetValueOrDefault(DriftCandidate.TargetRiskMultipleParameter, p.GetValueOrDefault(IndexDriftCandidate.TargetMultipleParameter))];

        return (breakout ? BreakoutSweep.Grid : DriftSweep.Grid, values, breakout ? 0 : p.GetValueOrDefault(DriftCandidate.StopFloorMovesParameter, p.GetValueOrDefault(IndexDriftCandidate.StopFloorParameter)));
    }

    // From 21.5 an S&P 400's or 600's frozen breakout or drift as the engines read it, at the setting and every level its
    // registration states: on the extended grid's walk where that walk reads its levels, and otherwise over its sweep's own
    // reading of the index's history, which the caller lays out; or why the history cannot walk it.
    // see: The engines test a frozen rule at the levels it states, and a frozen rule's setting moves only by a freeze
    public static (RuleWalk? Walk, string? Why) LiveSwingWalk(LoopRead read, string family, RegisterRow row, IndexSweepRunner.OwnSwingRead? own, LoopReadings? readings, Func<long, TreeModel?>? trees)
    {
        if (CandidateEvaluators.Find(row.Evaluator) is not IndexRuleCandidate evaluator)
        {
            return (null, $"its rule runs on '{row.Evaluator}', which no S&P 400 or 600 swing family carries");
        }

        var p = Unversioned(CandidateEvaluator.Read(row.Parameters));
        var hooks = HooksOf(p);
        var words = FamilyRecords.IsLive(row.Candidate) ? row.Candidate[FamilyRecords.LivePrefix.Length..] : row.Candidate;

        if (ReadsOwnSweep(evaluator, p))
        {
            if (own is null)
            {
                return (null, NoOwnRead);
            }

            var (at, refused) = RuleReplay.OwnSwing(own, evaluator, p);

            return at is null ? (null, refused) : (RuleWalk.OwnSwing(read, own, at.Setting, at.Levels, words, hooks, readings, trees), null);
        }

        var (grid, values, floor) = GridOf(family, p);
        var places = IndexNightRead.Places(grid, values);

        if (places.Any(place => place < 0))
        {
            return (null, OffTheGrid);
        }

        var change = new LoopChange(places, grid.Key(places), floor > 0 ? floor : null, hooks);

        return (RuleWalk.Swing(read, family, false, change, readings, trees, marketOff: p.GetValueOrDefault(IndexRuleCandidate.LargeBreadthParameter) == IndexRuleCandidate.NoMarketCheck), null);
    }

    // From 21.5 an S&P 400's or 600's frozen pullback as the engines read it: at its dials on the extended grid with its
    // hooks, where that grid's walk reads every level it states; or why it cannot, a level only its search's own reading
    // walks, which no engine's walk reads.
    // see: The engines test a frozen rule at the levels it states, and a frozen rule's setting moves only by a freeze
    public static (RuleWalk? Walk, string? Why) LivePullbackWalk(LoopRead read, IReadOnlyList<SweepMarketSeries> market, RegisterRow row, Action<string> progress, LoopReadings? readings, Func<long, TreeModel?>? trees, IdeaReplay candidates)
    {
        var p = CandidateEvaluator.Read(row.Parameters);

        if (CandidateEvaluators.Find(row.Evaluator) is IndexRuleCandidate own && ReadsOwnSweep(own, p))
        {
            return (null, OwnSearchOnly);
        }

        return DialsOf(p) is { } dials
            ? (RuleWalk.Pullback(read, market, progress, LoopChange.OfHooks(HooksOf(p)), readings, trees, dials, candidates), null)
            : (null, OffTheGrid);
    }

    // Why the engines read no frozen pullback stating a level only its search's own reading walks.
    public const string OwnSearchOnly = "its live rule states a level only its search's own reading of the index's history walks, which no engine's walk reads";

    static RuleUnderTest SwingAt(RuleTestInputs inputs, string family, RegisterRow row, bool large, (bool[] Average, bool[] Vix)? switches)
    {
        var p = CandidateEvaluator.Read(row.Parameters);
        var breakout = family == BreakoutRule.Name;
        var (grid, values, floor) = GridOf(family, p);
        var places = IndexNightRead.Places(grid, values);
        var hooks = HooksOf(p);
        var marketSwitches = large ? MarketSwitches.Of(p) : MarketSwitches.None;
        string[] dials = breakout
            ? [IndexBreakoutCandidate.NearnessParameter, IndexBreakoutCandidate.RecencyParameter, IndexBreakoutCandidate.HighVolumeParameter]
            : [IndexDriftCandidate.WideWindowParameter, IndexDriftCandidate.PeersParameter, IndexDriftCandidate.EntryParameter, IndexDriftCandidate.RevenueGrewParameter];
        if (NotWalkedYet(family, row, inputs.Tones) is { } kept)
        {
            return kept;
        }

        // From 21.1 an S&P 400 or 600 rule stating a level the extended grid's walk does not read is walked by its sweep's
        // own reading at every level it states, the tone condition read over the listings those keep.
        if (!large && CandidateEvaluators.Find(row.Evaluator) is IndexRuleCandidate own && ReadsOwnSweep(own, p))
        {
            return ThroughOwnSweep(inputs, family, row, own, p);
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

        // From 21.1 the tone variant's opposite, walked beside it unregistered and written nowhere: the listings its switches
        // keep whose print's release is read and fails the condition.
        bool KeptOpposite(int at) =>
            (marketSwitches.Equals(MarketSwitches.None) || Open(at))
            && releases is not null && ToneOpposite(inputs.Read, walk.Listings[at], releases, inputs.Tones!);

        var trades = marketSwitches.Equals(MarketSwitches.None) && releases is null ? walk.Own : walk.Keeping(Kept);
        var units = Units(trades);
        var tone = releases is null ? null : new ToneSides(version, inputs.Tones!.CutoffOf(version), units, Units(walk.Keeping(KeptOpposite)));

        // From 21.3 the drift's live or provisional rule carries the open floor's walk beside its own: the same listings
        // with a fill whose reward to risk at the open is under the floor skipped, its place on the night spent, as the
        // record command replays it, so the open floor is judged as a one-setting procedure against the rule.
        if (family == DriftRule.Name && StandingOf(row) != Variant)
        {
            var skipped = 0;
            var floored = walk.KeepingSkipping(
                marketSwitches.Equals(MarketSwitches.None) && releases is null ? null : Kept,
                listing =>
                {
                    var leaves = RuleReplay.LeavesUnder(OpenAfter(inputs.Read.Series[listing.Name], listing.Bar), listing, RuleRecorder.OpenFloor);

                    skipped += leaves ? 1 : 0;

                    return leaves;
                });

            return new RuleUnderTest(family, row.Candidate, StandingOf(row), row.Rule, LoopProcedures.Risks, walk.Cap, units, Tone: tone, OpenFloor: Units(floored), Skipped: skipped);
        }

        return new RuleUnderTest(family, row.Candidate, StandingOf(row), row.Rule, LoopProcedures.Risks, walk.Cap, units, Tone: tone);
    }

    // The open of the session after a bar, which a trade is bought at from 19.10, none where the series holds none; the
    // record command's own reading of it.
    static double? OpenAfter(SweepSeries series, int bar) =>
        bar >= 0 && bar + 1 < series.Bars.Length && series.Bars[bar + 1].Open > 0m ? Core.Prices.Statistic.FromPrice(series.Bars[bar + 1].Open) : null;

    // From 21.3 the drift's rules standing on the index as the per-rule test walks them, the live or provisional rule
    // carrying the open floor's walk beside its own, for the open floor's test as a one-setting procedure.
    // see: A read-only check changes no rule, and an exit's pricing changes only by a fill step on the operator's word
    public static IReadOnlyList<RuleUnderTest> Drift(RuleTestInputs inputs) => [.. Swings(inputs, DriftRule.Name)];

    // From 21.1 a registered breakout or drift walked by its sweep's own reading of the index's history at the setting and
    // every level its registration states, as the record command replays a frozen rule: its places on the family's grid
    // with the market check's level it reads, the drift's stop floor, and the second stage's levels together, five a night
    // with one open trade a stock, every trade bought at the open after its listing and after its round trip. A rule naming
    // a reading version is held to the tone condition over the listings its levels keep before the walk, its opposite walked
    // beside it unregistered; one its sweep reads no setting for, or whose family the run laid out no reading for, is kept
    // with why.
    // see: Every registered rule is walked by the per-rule test at what it reads, and one the history cannot walk names what judges it instead
    public static RuleUnderTest ThroughOwnSweep(RuleTestInputs inputs, string family, RegisterRow row, IndexRuleCandidate evaluator, IReadOnlyDictionary<string, double> p)
    {
        var stopFloor = evaluator is IndexDriftCandidate ? p.GetValueOrDefault(IndexDriftCandidate.StopFloorParameter) : 0;

        RuleUnderTest Kept(string why) => new(family, row.Candidate, StandingOf(row), row.Rule, LoopProcedures.Risks, 0, Why: why);

        if (inputs.Own?.Swings.GetValueOrDefault((family, stopFloor)) is not { } own)
        {
            return Kept(NoOwnRead);
        }

        var (at, refused) = RuleReplay.OwnSwing(own, evaluator, Unversioned(p));

        if (at is null)
        {
            return Kept(refused!);
        }

        var listings = own.ListingsUnder(at.Setting, at.Levels);
        var cap = listings.Select(listing => listing.Cap).DefaultIfEmpty(0).Max();
        var version = ReleaseTone.VersionOf(p);

        if (version == 0)
        {
            // From 21.3 the drift's live rule carries the open floor's walk beside its own over the same listings, as the
            // record command replays it, so the open floor is judged against the rule as a one-setting procedure.
            if (family == DriftRule.Name && StandingOf(row) != Variant)
            {
                var skipped = 0;
                var floored = own.Costed(
                    own.AtOpenSkipping(listings, listing =>
                    {
                        var leaves = RuleReplay.LeavesUnder(OpenAfter(own.Series[listing.Name], listing.Bar), listing, RuleRecorder.OpenFloor);

                        skipped += leaves ? 1 : 0;

                        return leaves;
                    }),
                    1);

                return new RuleUnderTest(family, row.Candidate, StandingOf(row), row.Rule, LoopProcedures.Risks, cap, Units(own.Costed(own.AtOpen(listings), 1)), OpenFloor: Units(floored), Skipped: skipped);
            }

            return new RuleUnderTest(family, row.Candidate, StandingOf(row), row.Rule, LoopProcedures.Risks, cap, Units(own.Costed(own.AtOpen(listings), 1)));
        }

        var releases = inputs.Tones!.Of(version)!;
        var variant = Units(own.Costed(own.AtOpen(listings.Where(listing => ToneHolds(own.Series, own.Calendar, listing.Name, listing.Bar, listing.Session, releases, inputs.Tones))), 1));
        var opposite = Units(own.Costed(own.AtOpen(listings.Where(listing => ToneOpposite(own.Series, own.Calendar, listing.Name, listing.Bar, listing.Session, releases, inputs.Tones))), 1));

        return new RuleUnderTest(family, row.Candidate, StandingOf(row), row.Rule, LoopProcedures.Risks, cap, variant, Tone: new ToneSides(version, inputs.Tones.CutoffOf(version), variant, opposite));
    }

    // From 21.1 the dials of a family's second stage a registered rule may state beside its grid places.
    public static IReadOnlyList<string> OwnDialsOf(IndexRuleCandidate evaluator) => evaluator switch
    {
        IndexBreakoutCandidate => [IndexBreakoutCandidate.NearnessParameter, IndexBreakoutCandidate.RecencyParameter, IndexBreakoutCandidate.HighVolumeParameter],
        IndexDriftCandidate => [IndexDriftCandidate.WideWindowParameter, IndexDriftCandidate.PeersParameter, IndexDriftCandidate.EntryParameter, IndexDriftCandidate.RevenueGrewParameter],
        _ => [],
    };

    // From 21.1 whether a registered S&P 400 or 600 rule states a level the extended grid's walk does not read, its market
    // check's floor at a level other than the swing filter's, its quality check, its hold, the drift's first-pullback entry
    // or revenue dial or the S&P 500's breadth among them, so the per-rule test walks it through its sweep's own reading at
    // every level it states, as the record command replays a frozen rule.
    public static bool ReadsOwnSweep(IndexRuleCandidate evaluator, IReadOnlyDictionary<string, double> p) =>
        LevelsWhy(Unversioned(p), OwnDialsOf(evaluator)) is not null;

    // A registration's parameters with the reading version it names left off, so the tone condition is read after the
    // sweep's own walk and not as a level of it.
    public static IReadOnlyDictionary<string, double> Unversioned(IReadOnlyDictionary<string, double> p) =>
        p.Where(pair => pair.Key != ReleaseTone.VersionParameter).ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);

    // From 21.1 the sweeps' own readings a run needs on an index before it walks: the family and the stop floor of each
    // registered breakout or drift rule standing on it that states a level the extended grid's walk does not read, each
    // read once; none on the S&P 500, whose rules state no such level.
    public static IReadOnlyList<(string Family, double StopFloor)> OwnSwingsNeeded(IReadOnlyList<RegisterRow> standing, string index) =>
        [.. standing
            .Select(row => (Row: row, Evaluator: CandidateEvaluators.Find(row.Evaluator) as IndexRuleCandidate))
            .Where(pair => pair.Evaluator is { } rule && rule.Index == index && (rule.SetupFamily == BreakoutRule.Name || rule.SetupFamily == DriftRule.Name) && !FamilyRecords.IsBackground(pair.Row.Candidate))
            .Select(pair => (pair.Evaluator!, Parameters: CandidateEvaluator.Read(pair.Row.Parameters)))
            .Where(rule => ReadsOwnSweep(rule.Item1, rule.Parameters))
            .Select(rule => (rule.Item1.SetupFamily, rule.Item1 is IndexDriftCandidate ? rule.Parameters.GetValueOrDefault(IndexDriftCandidate.StopFloorParameter) : 0))
            .Distinct()];

    // From 21.1 whether a run needs the pullback's search's own reading on an index: a registered pullback standing on it
    // states a level the extended grid's walk does not read.
    public static bool OwnPullbackNeeded(IReadOnlyList<RegisterRow> standing, string index) =>
        standing.Any(row => CandidateEvaluators.Find(row.Evaluator) is IndexPullbackCandidate rule && rule.Index == index && !FamilyRecords.IsBackground(row.Candidate) && ReadsOwnSweep(rule, CandidateEvaluator.Read(row.Parameters)));

    // From 21.1 a tone variant's figures on each side of the local model's cutoff's year, as the run prints them: its own
    // trades and their mean edge after costs, and its opposite's, walked unregistered and written nowhere.
    public static string ToneLine(RuleUnderTest rule, DateOnly[] calendar)
    {
        var tone = rule.Tone!;

        static string Side(IReadOnlyList<(int Entry, double? Edge)> units, Func<int, bool> on)
        {
            var kept = units.Where(unit => on(unit.Entry)).ToArray();
            var edged = kept.Where(unit => unit.Edge is not null).ToArray();

            return edged.Length == 0
                ? FormattableString.Invariant($"{kept.Length} trade(s), none ended")
                : FormattableString.Invariant($"{kept.Length} trade(s) at {edged.Average(unit => unit.Edge!.Value):+0.000;-0.000} risks");
        }

        if (tone.Cutoff is not { } cutoff)
        {
            return FormattableString.Invariant($"{rule.Family}, {rule.Rule}: its tone condition under reading version {tone.Version}, {Side(tone.Variant, _ => true)} over the history; its opposite, {OppositeWords}, walked unregistered and written nowhere, {Side(tone.Opposite, _ => true)}; the version states no cutoff");
        }

        bool Before(int entry) => calendar[entry].Year < cutoff.Year;

        return FormattableString.Invariant($"{rule.Family}, {rule.Rule}: its tone condition under reading version {tone.Version}, {Side(tone.Variant, Before)} before the local model's cutoff's year, {cutoff.Year}, and {Side(tone.Variant, entry => !Before(entry))} from it; its opposite, {OppositeWords}, walked unregistered and written nowhere, {Side(tone.Opposite, Before)} and {Side(tone.Opposite, entry => !Before(entry))}");
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
    public static bool ToneHolds(LoopRead read, RuleListing listing, IReadOnlyDictionary<string, IReadOnlyList<LedgerRelease>> releases, ToneReleases tones) =>
        ToneHolds(read.Series, read.Calendar, listing.Name, listing.Bar, listing.Session, releases, tones);

    public static bool ToneHolds(SweepSeries[] series, DateOnly[] calendar, int name, int bar, int session, IReadOnlyDictionary<string, IReadOnlyList<LedgerRelease>> releases, ToneReleases tones) =>
        ToneAnswer(series, calendar, name, bar, session, releases, tones) is { } answer && ReleaseTone.Passes(answer);

    // From 21.1 whether a drift listing passes the tone condition's opposite: its print's release read, and read not
    // better or with its outlook lowered or withdrawn, the complement of the variant's condition over the releases read; a
    // listing whose release is not read passes neither, as on the night.
    // see: Every registered rule is walked by the per-rule test at what it reads, and one the history cannot walk names what judges it instead
    public static bool ToneOpposite(LoopRead read, RuleListing listing, IReadOnlyDictionary<string, IReadOnlyList<LedgerRelease>> releases, ToneReleases tones) =>
        ToneOpposite(read.Series, read.Calendar, listing.Name, listing.Bar, listing.Session, releases, tones);

    public static bool ToneOpposite(SweepSeries[] series, DateOnly[] calendar, int name, int bar, int session, IReadOnlyDictionary<string, IReadOnlyList<LedgerRelease>> releases, ToneReleases tones) =>
        ToneAnswer(series, calendar, name, bar, session, releases, tones) is { } answer && IsOpposite(answer);

    public static bool IsOpposite(ReleaseAnswer answer) => !ReleaseTone.Passes(answer);

    // The reading of the release the night of a listing's session reads for the newest print the listing reacts to, none
    // where the stock holds no release under the version or its release holds no reading.
    static ReleaseAnswer? ToneAnswer(SweepSeries[] series, DateOnly[] calendar, int name, int bar, int session, IReadOnlyDictionary<string, IReadOnlyList<LedgerRelease>> releases, ToneReleases tones)
    {
        var one = series[name];

        if (one.NewestSurprise[bar] is not (var newest and >= 0) || !releases.TryGetValue(one.Name.Ticker, out var held))
        {
            return null;
        }

        return ReleaseAvailability.ReleaseFor(held, release => release.Accepted, one.Name.Surprises[newest].EventDate, calendar[session], tones.Zone, tones.Calendar)?.Answer;
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

        var rows = inputs.Standing.Where(row => CandidateEvaluators.Find(row.Evaluator) is IndexHeavyweightCandidate rule && rule.Index == read.Index && !FamilyRecords.IsBackground(row.Candidate)).ToArray();

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
                ? BookOffTheSix(inputs.Lay, row, p, designA)
                : new RuleUnderTest(HeavyweightRule.Name, row.Candidate, StandingOf(row), row.Rule, LoopProcedures.Points, BookMonths.LongestMonth, Months: inputs.Lay.Book(six, designA).Months);
        }
    }

    // From 21.1 a registered book at a setting none of its design's six settings holds, walked at its own setting as the
    // record command replays a frozen book: design (a) at the leaders setting, the quality and the floors it states and
    // design (b) at its window, industries and members, each from the lay's full grid; and twelve months' strength
    // skipping the latest month, which the record command replays at no setting, through the six settings' own reader at
    // the registration's quality and floors, design (b)'s at the base window and industries alone.
    // see: Every registered rule is walked by the per-rule test at what it reads, and one the history cannot walk names what judges it instead
    public static RuleUnderTest BookOffTheSix(HeavyweightLay lay, RegisterRow row, IReadOnlyDictionary<string, double> p, bool designA)
    {
        RuleUnderTest Of(IReadOnlyList<(int Opens, double? Edge)> months) => new(HeavyweightRule.Name, row.Candidate, StandingOf(row), row.Rule, LoopProcedures.Points, BookMonths.LongestMonth, Months: months);

        RuleUnderTest Kept(string why) => new(HeavyweightRule.Name, row.Candidate, StandingOf(row), row.Rule, LoopProcedures.Points, BookMonths.LongestMonth, Why: why);

        if (IndexRules.TwelveOne(p))
        {
            var quality = p.GetValueOrDefault(IndexRuleCandidate.QualityParameter, 1) switch
            {
                0 => IndexQuality.Off,
                2 => IndexQuality.Cover,
                _ => IndexQuality.Profit,
            };
            var floors = p.GetValueOrDefault(IndexRuleCandidate.FloorsParameter, 1) == 2 ? 2m : 1m;

            if (designA)
            {
                return Of(lay.TwelveOneBook(new HeavyweightSixSetting(0, row.Rule, true, quality, false, IndexRules.HeavyweightSettingOf(p)), floors, true, row.Rule).Months);
            }

            if (p.GetValueOrDefault(IndexHeavyweightCandidate.WindowParameter) != HeavyweightSix.BaseWindow || p.GetValueOrDefault(IndexHeavyweightCandidate.IndustriesParameter) != HeavyweightSix.BaseIndustries)
            {
                return Kept(TwelveOneOffTheBase);
            }

            return Of(lay.TwelveOneBook(new HeavyweightSixSetting(0, row.Rule, true, quality, false, PerIndustry: (int)p.GetValueOrDefault(IndexHeavyweightCandidate.MembersParameter, 1)), floors, false, row.Rule).Months);
        }

        var (at, refused) = RuleReplay.OwnBook(p);

        if (at is null)
        {
            return Kept(refused!);
        }

        var book = at.Setting is { } setting
            ? lay.BookFrom(lay.RebalancesA(setting, at.Quality, at.Floors), setting.Exit, at.Key)
            : lay.BookFrom(lay.RebalancesB(at.Window, at.Industries, at.Members, at.Quality, at.Floors), HeavyweightExit.Drop, at.Key);

        return Of(book.Months);
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
