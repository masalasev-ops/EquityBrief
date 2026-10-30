using EquityBrief.Core.Candidates;
using EquityBrief.Core.Families;
using EquityBrief.Core.Filter;
using EquityBrief.Core.Ledger;
using EquityBrief.Core.Loop;
using EquityBrief.Core.Sweep;
using EquityBrief.Core.Time;
using EquityBrief.Tests.Harness;
using EquityBrief.Worker.Candidates;
using EquityBrief.Worker.Families;
using EquityBrief.Worker.Indices;
using EquityBrief.Worker.Loop;
using EquityBrief.Worker.Sweep;

namespace EquityBrief.Tests.Checks;

// fixture-expectations, 17.5: the engines' hooks read off a rule's own parameters and each off where it is not stated,
// a family rule registered with them beside its parameters and a heavyweights book refusing them, a hooked trade walked
// under its exit and an unhooked one as it always was, a rule's list kept and ordered by its hooks where they read
// readings and as it stands where none, on the S&P 500 and on an index over the readings its night supplies, the
// autopsy's figures worked by hand over constructed paths, and the five strongest exits chosen on the learning years
// alone.
// see: The engines' hooks are eight forms a registration states and all default off, and every source a rule pins moves only in a checkpoint that registers every rule reading it again
// see: The trade autopsy proposes exits of a fixed menu that only a checkpoint widens, each tested as the procedure that chose it
public partial class FixtureExpectations
{
    // The rows 17.5's autopsy adds that this check reaches: section 17's menu and proposals and section 18's path no bars
    // reach.
    internal static readonly string[] AutopsyClaims =
    [
        CheckReach.Key(Scope.LimitsTable, "The exit menu"),
        CheckReach.Key(Scope.LimitsTable, "The autopsy's proposals"),
        CheckReach.Key(Scope.FailureTable, "A path no bars reach"),
    ];

    // The rows 21.4's hook forms add that this check reaches: section 17's exposure and ranking.
    internal static readonly string[] HookFormClaims =
    [
        CheckReach.Key(Scope.LimitsTable, "The exposure"),
        CheckReach.Key(Scope.LimitsTable, "The ranking"),
    ];

    // Every row 17.5's autopsy adds, named after phase 16's report until phase 17's own pair is checked: the findings'
    // store and the three above.
    internal static string[] AutopsyRows =>
    [
        CheckReach.Key(Scope.StoresTable, "Loop findings"),
        .. AutopsyClaims,
    ];

    static int Column(string column) => LedgerReadings.All.Select((reading, at) => (reading, at)).Single(pair => pair.reading.Column == column).at;

    static double?[] HookReadings(double? closeOverLong, double? moveShare, double? closeOverFifty)
    {
        var readings = new double?[LedgerReadings.Count];

        readings[Column("close_over_long")] = closeOverLong;
        readings[Column("move_share")] = moveShare;
        readings[Column("close_over_fifty")] = closeOverFifty;

        return readings;
    }

    [Fact]
    public void ARulesHooksAreReadOffItsParametersAndEachIsOffWhereItIsNotStated()
    {
        var plain = new Dictionary<string, double>(StringComparer.Ordinal) { ["highSessions"] = 126, ["volumeMultiple"] = 1.5 };
        var off = RuleHooks.Of(plain);

        Assert.True(off.IsOff);
        Assert.False(off.ReadsReadings);
        Assert.Null(off.ExitChoice);
        Assert.Null(off.Words());

        var hooked = new Dictionary<string, double>(plain, StringComparer.Ordinal)
        {
            ["exit"] = 2,
            ["also_close_over_long_above"] = 1,
            ["also_move_share_below"] = 0.05,
            ["score_close_over_fifty"] = 2,
            ["score_floor"] = 2.1,
        };
        var hooks = RuleHooks.Of(hooked);

        // The second exit of the menu, the stop raised to the buy at a risk; two conditions; a score of twice the close
        // over its 50-session average with its floor at 2.1.
        Assert.Equal(ExitKind.BreakEven, hooks.ExitChoice!.Kind);
        Assert.Equal(1.0, hooks.ExitChoice.K);
        Assert.Equal([("close_over_long", true, 1.0), ("move_share", false, 0.05)], hooks.Also.Select(one => (one.Column, one.Above, one.Level)));
        Assert.Equal(2.0, hooks.Score[Column("close_over_fifty")]);
        Assert.Equal(2.1, hooks.ScoreFloor);
        Assert.Equal(
            "exit: the stop raised to the buy once a close is 1 risks up; also requires close_over_long at or above 1; also requires move_share at or under 0.05; ordered by a score over close_over_fifty, a score under 2.1 left off",
            hooks.Words());

        // A hook's name is one of the five forms over a reading the catalogue holds; the rule's own parameters are
        // what is left.
        Assert.True(RuleHooks.IsHook("exit"));
        Assert.True(RuleHooks.IsHook("also_close_over_long_above"));
        Assert.False(RuleHooks.IsHook("also_no_such_reading_above"));
        Assert.False(RuleHooks.IsHook("highSessions"));
        Assert.Equal(["highSessions", "volumeMultiple"], RuleHooks.Without(hooked).Keys.Order(StringComparer.Ordinal));
        Assert.Throws<ArgumentException>(() => RuleHooks.Of(new Dictionary<string, double>(StringComparer.Ordinal) { ["exit"] = 31 }));

        // A member meets the conditions at 1.02 over its average and 0.03 of its close a move; 0.98 fails the first, 0.06
        // the second, and a reading not held fails whatever its level.
        Assert.True(hooks.Passes(HookReadings(1.02, 0.03, 1.1)));
        Assert.False(hooks.Passes(HookReadings(0.98, 0.03, 1.1)));
        Assert.False(hooks.Passes(HookReadings(1.02, 0.06, 1.1)));
        Assert.False(hooks.Passes(HookReadings(null, 0.03, 1.1)));
        Assert.Equal(2.2, hooks.ScoreOf(HookReadings(1.02, 0.03, 1.1))!.Value, 12);
        Assert.Null(hooks.ScoreOf(HookReadings(1.02, 0.03, null)));

        // In its own order A, B, C, D and E: B scores 2.0, under the floor; C fails a condition; D scores 2.4 and A and
        // E 2.2, so D first, then A and E in their own order. With no hook the list stands as it was.
        var readings = new Dictionary<string, double?[]>(StringComparer.Ordinal)
        {
            ["A"] = HookReadings(1.02, 0.03, 1.1),
            ["B"] = HookReadings(1.02, 0.03, 1.0),
            ["C"] = HookReadings(0.95, 0.03, 1.5),
            ["D"] = HookReadings(1.02, 0.03, 1.2),
            ["E"] = HookReadings(1.02, 0.03, 1.1),
        };
        string[] own = ["A", "B", "C", "D", "E"];

        Assert.Equal(["D", "A", "E"], hooks.Order(own, ticker => readings[ticker]));
        Assert.Equal(own, off.Order(own, ticker => readings[ticker]));
    }

    // A score over one reading of the catalogue, standing in for a stored model.
    sealed class OneColumnScore(int column) : IScoreModel
    {
        public string Hash => "one-column";

        public int Rows => 0;

        public IReadOnlyList<int> Readings => [column];

        public double? Score(IReadOnlyList<double?> readings) => readings[column];

        public int? Rank(string index, double score) => null;

        public double? FloorAt(string index, double share) => null;

        public IReadOnlyDictionary<string, double> HookParameters(double? floor) => new Dictionary<string, double>(StringComparer.Ordinal);

        public string Canonical() => Hash;
    }

    static double?[] NightReadings(double? breadth, double? vixOverMedian)
    {
        var readings = new double?[LedgerReadings.Count];

        readings[RuleHooks.BreadthColumn] = breadth;
        readings[RuleHooks.VixOverMedianColumn] = vixOverMedian;

        return readings;
    }

    [Fact]
    public void TheExposureTheRankingAndTheExpectedValueOrderAreWorkedByHandOverAConstructedNight()
    {
        static RuleHooks Of(params (string Name, double Value)[] parameters) =>
            RuleHooks.Of(parameters.ToDictionary(pair => pair.Name, pair => pair.Value, StringComparer.Ordinal));

        // The three forms of 21.4 read off a rule's parameters: the exposure as the breadth floor it reads, the ranking by
        // the learned score or by the expected value, and the expected-value order; the exposure reads readings, a ranking
        // by the score reads readings and one by the expected value reads that, and the order reads the expected value.
        var exposed = Of(("exposure", 0.45));
        var rankedByScore = Of(("ranking", 1));
        var rankedByExpected = Of(("ranking", 2));
        var byExpected = Of(("order_expected", 1));

        Assert.Equal((0.45, true, false, false), (exposed.Exposure, exposed.ReadsReadings, exposed.ReadsExpected, exposed.IsOff));
        Assert.Equal((RuleHooks.RankingByScore, true, true, false), (rankedByScore.Ranking, rankedByScore.Ranks, rankedByScore.ReadsReadings, rankedByScore.ReadsExpected));
        Assert.Equal((RuleHooks.RankingByExpected, true, false, true), (rankedByExpected.Ranking, rankedByExpected.Ranks, rankedByExpected.ReadsReadings, rankedByExpected.ReadsExpected));
        Assert.Equal((true, false, true), (byExpected.ByExpected, byExpected.ReadsReadings, byExpected.ReadsExpected));
        Assert.True(Of(("order_expected", 0), ("ranking", 0)).IsOff);
        Assert.All(new[] { "exposure", "ranking", "order_expected" }, name => Assert.True(RuleHooks.IsHook(name)));
        Assert.Equal([true, true, true, true, false, false], new[] { "ranking", "order_expected", "score_floor", "score_close_over_long", "exposure", "exit" }.Select(RuleHooks.IsOrder));

        // Refused: an exposure floor at nought or over one, a ranking the two forms do not name, an order other than on or
        // off, and two orders together.
        Assert.Throws<ArgumentException>(() => Of(("exposure", 0)));
        Assert.Throws<ArgumentException>(() => Of(("exposure", 1.5)));
        Assert.Throws<ArgumentException>(() => Of(("ranking", 3)));
        Assert.Throws<ArgumentException>(() => Of(("order_expected", 2)));
        Assert.Throws<ArgumentException>(() => Of(("ranking", 1), ("score_close_over_long", 1)));
        Assert.Throws<ArgumentException>(() => Of(("ranking", 2), ("order_expected", 1)));
        Assert.Throws<ArgumentException>(() => Of(("order_expected", 1), ("score_floor", 1)));

        // A book's rule reads conditions and a score alone.
        Assert.True(Of(("also_close_over_long_above", 1), ("score_close_over_fifty", 1), ("score_floor", 0.5)).AreABooks);
        Assert.Equal([false, false, false, false], new[] { exposed, rankedByScore, byExpected, Of(("exit", 2)) }.Select(one => one.AreABooks));

        Assert.Equal("exposure: 5 picks where the index's breadth is at or above 0.45 and the VIX at or under its median of the 252 sessions before, 2 where one holds and none where neither, in the market check's place", exposed.Words());
        Assert.Equal("ranked over the family's setups at the ledger's loose gates by the index's newest learned score, the best 5", rankedByScore.Words());
        Assert.Equal("ranked over the family's setups at the ledger's loose gates by the expected value of the setups like each, the best 5", rankedByExpected.Words());
        Assert.Equal("ordered by the expected value of the setups like each, highest first", byExpected.Words());

        // The exposure at both switches on both sides: a breadth of 0.45 at the floor holds and 0.4499 does not; the VIX at
        // its median, 1, holds and a hair over, 1.0001, does not; five picks where both hold, two where one, none where
        // neither, a reading not held not holding, and none on a night with no readings; a rule stating no exposure caps
        // nothing of its own.
        Assert.Equal(5, exposed.PicksUnder(NightReadings(0.45, 1.0)));
        Assert.Equal(2, exposed.PicksUnder(NightReadings(0.4499, 1.0)));
        Assert.Equal(2, exposed.PicksUnder(NightReadings(0.45, 1.0001)));
        Assert.Equal(0, exposed.PicksUnder(NightReadings(0.4499, 1.0001)));
        Assert.Equal(2, exposed.PicksUnder(NightReadings(null, 1.0)));
        Assert.Equal(0, exposed.PicksUnder(null));
        Assert.Null(byExpected.PicksUnder(NightReadings(0.45, 1.0)));

        // Over a constructed night of seven candidates in the rule's own order, the exposure keeps the first five where
        // both switches hold, the first two where one does and none where neither.
        string[] seven = ["A", "B", "C", "D", "E", "F", "G"];

        Assert.Equal(["A", "B", "C", "D", "E"], exposed.Order(seven, _ => NightReadings(0.5, 0.9)));
        Assert.Equal(["A", "B"], exposed.Order(seven, _ => NightReadings(0.3, 0.9)));
        Assert.Empty(exposed.Order(seven, _ => NightReadings(0.3, 1.2)));

        // The ranking by the learned score over the family's setups at the loose gates, seven of them: the best five by a
        // constructed score reading one column, B and E tied at 0.9 in the rule's order, then G, D and A, F sixth and C,
        // which the score cannot read, left off; handed no score, a ranking lists none.
        var scores = new Dictionary<string, double?>(StringComparer.Ordinal) { ["A"] = 0.2, ["B"] = 0.9, ["C"] = null, ["D"] = 0.5, ["E"] = 0.9, ["F"] = 0.1, ["G"] = 0.7 };
        var column = Column("close_over_long");

        double?[] Scored(string ticker)
        {
            var readings = new double?[LedgerReadings.Count];

            readings[column] = scores[ticker];

            return readings;
        }

        Assert.Equal(["B", "E", "G", "D", "A"], rankedByScore.WithRanker(new OneColumnScore(column)).Order(seven, Scored));
        Assert.Empty(rankedByScore.Order(seven, Scored));

        // The same ranking by the expected value of the setups like each: C and E at 0.8, F, A and G, D sixth and B, with
        // no figure, left off; handed no figure, none.
        var expected = new Dictionary<string, double?>(StringComparer.Ordinal) { ["A"] = 0.3, ["B"] = null, ["C"] = 0.8, ["D"] = 0.1, ["E"] = 0.8, ["F"] = 0.6, ["G"] = 0.2 };

        Assert.Equal(["C", "E", "F", "A", "G"], rankedByExpected.Order(seven, Scored, ticker => expected[ticker]));
        Assert.Empty(rankedByExpected.Order(seven, Scored));

        // The expected-value order over the rule's own candidates: those with a figure first by it, then those without in
        // the rule's own order, none left off; handed no figure, the rule's order stands; and with the exposure beside it
        // the ordered list is cut to the picks the night allows.
        Assert.Equal(["C", "E", "F", "A", "G", "D", "B"], byExpected.Order(seven, Scored, ticker => expected[ticker]));
        Assert.Equal(seven, byExpected.Order(seven, Scored));
        Assert.Equal(["C", "E"], Of(("order_expected", 1), ("exposure", 0.45)).Order(seven, _ => NightReadings(0.3, 0.9), ticker => expected[ticker]));

        // A change stating a ranking replaces a standing score whole, as a score does, and a condition stands beside it.
        var standing = new Dictionary<string, double>(StringComparer.Ordinal) { ["score_close_over_long"] = 1, ["score_floor"] = 0.5, ["also_move_share_below"] = 0.05 };
        var moved = LoopChange.OfHooks(new Dictionary<string, double>(StringComparer.Ordinal) { ["ranking"] = 1 }).OnTopOf(new LoopChange(null, null, null, standing)).Hooks;

        Assert.Equal(["also_move_share_below", "ranking"], moved.Keys.Order(StringComparer.Ordinal));
        Assert.Equal(["also_move_share_below", "order_expected"], LoopChange.OfHooks(new Dictionary<string, double>(StringComparer.Ordinal) { ["order_expected"] = 1 }).OnTopOf(new LoopChange(null, null, null, moved)).Hooks.Keys.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void TheHooksAndTheMenuArePinnedByTheRulesTheyHookAndByNoOtherEvaluator()
    {
        // The S&P 500's breakout and drift and every S&P 400 and 600 rule pin the hooks' and the menu's sources beside
        // their own; the swing filter, the three conditions and the S&P 500's heavyweights do not, so the pin move of the
        // hooks moved none of their versions, and no shared evaluation source names them.
        // From 21.4 the dip's, the quality family's and the Fundamentals family's rules read the hooks as well, so they pin
        // the same three sources.
        string[] hooked =
        [
            "breakout", "breakout-400", "breakout-600", "dip-400", "dip-500", "dip-600", "drift", "drift-400", "drift-600", "heavyweight-400", "heavyweight-600",
            "improving-400", "improving-500", "improving-600", "pullback-400", "pullback-600", "quality-400", "quality-500", "quality-600",
        ];

        Assert.Equal(hooked, CandidateEvaluators.All.Where(evaluator => RuleHooks.Sources.All(evaluator.OwnSources.Contains)).Select(evaluator => evaluator.Name).Order(StringComparer.Ordinal));
        Assert.DoesNotContain(CandidateEvaluators.All, evaluator => !hooked.Contains(evaluator.Name) && RuleHooks.Sources.Any(evaluator.OwnSources.Contains));
        Assert.Empty(RuleHooks.Sources.Intersect(CandidateEvaluator.EvaluationSources));
    }

    [Fact]
    public async Task AFamilyRuleIsRegisteredWithItsHooksBesideItsParametersAndAnExitTheMenuDoesNotHoldIsRefused()
    {
        using var store = new TemporaryStore().Migrated();
        var registrar = new CandidateRegistrar(FixedClock.At(new DateTimeOffset(2026, 10, 9, 12, 0, 0, TimeSpan.Zero), SessionZones.UnitedStates), store.DatabaseFile);
        var parameters = new Dictionary<string, double>(BreakoutCandidate.ParametersOf(new BreakoutSettings(126, 1.5, 0.85, 1.5)), StringComparer.Ordinal)
        {
            ["exit"] = 2,
            ["also_close_over_long_above"] = 1,
        };

        var hooked = await registrar.RegisterAsync("the breakout broken even at a risk", "a breakout", "the edge", BreakoutCandidate.EvaluatorName, parameters, "hooks");

        Assert.Equal(CandidateRegistrar.Registered, hooked.Outcome);

        parameters["exit"] = 31;

        var refused = await registrar.RegisterAsync("the breakout at an exit the menu lacks", "a breakout", "the edge", BreakoutCandidate.EvaluatorName, parameters, "hooks-refused");

        Assert.Equal(CandidateRegistrar.Refused, refused.Outcome);
        Assert.Contains("hooks that cannot be read", refused.Detail, StringComparison.Ordinal);

        // A heavyweights book reads no hook, so an exit stated beside its parameters is refused as any parameter it does
        // not read is, and the index's swing rule takes it.
        var heavyweights = (IndexHeavyweightCandidate)CandidateEvaluators.Find("heavyweight-400")!;
        var book = new Dictionary<string, double>(IndexRules.Provisional(heavyweights), StringComparer.Ordinal) { ["exit"] = 2 };
        var unread = await registrar.RegisterAsync("the S&P 400 heavyweights at an exit", "a book", "the edge", heavyweights.Name, book, "hooks-book");

        Assert.Equal(CandidateRegistrar.Refused, unread.Outcome);
        Assert.Contains("reads [", unread.Detail, StringComparison.Ordinal);
        Assert.NotNull(IndexRules.Refusal(heavyweights, book));

        var drift = IndexEvaluator("drift-400");
        var swing = new Dictionary<string, double>(IndexRules.Provisional(drift), StringComparer.Ordinal) { ["exit"] = 2 };

        Assert.Null(IndexRules.Refusal(drift, swing));
        Assert.Equal(CandidateRegistrar.Registered, (await registrar.RegisterAsync("the S&P 400 drift at an exit", "a drift", "the edge", drift.Name, swing, "hooks-drift")).Outcome);
        Assert.Contains("menu numbers its exits", IndexRules.Refusal(drift, new Dictionary<string, double>(swing, StringComparer.Ordinal) { ["exit"] = 31 }), StringComparison.Ordinal);

        // From 21.4 the dip's rule states the hooks as a swing rule does, its sell dial no longer named exit, and a book's
        // rule states conditions and a score alone: an exit beside a quality rule's parameters is refused.
        var dipEvaluator = CandidateEvaluators.Find("dip-500")!;
        var dipParameters = new Dictionary<string, double>(StringComparer.Ordinal)
        {
            [DipCandidate.DropParameter] = -3,
            [DipCandidate.TrendParameter] = 50,
            [DipCandidate.SellParameter] = DipRule.RsiExit,
            [DipCandidate.LimitParameter] = 10,
            [DipCandidate.StopParameter] = 3,
            [DipCandidate.MarketParameter] = 6,
            [DipCandidate.VolumeParameter] = 0,
            ["exposure"] = 0.45,
            ["also_close_over_long_above"] = 1,
        };

        Assert.Equal(CandidateRegistrar.Registered, (await registrar.RegisterAsync("the S&P 500 dip under an exposure", "a dip", "the edge", dipEvaluator.Name, dipParameters, "hooks-dip")).Outcome);

        // A dip rule registered in the form from before 21.4, its sell dial named exit and no sell beside it, is read as
        // the same setting and stored with the dial under its name, so a remedy written then still registers.
        var before = new Dictionary<string, double>(dipParameters, StringComparer.Ordinal);

        before.Remove(DipCandidate.SellParameter);
        before.Remove("exposure");
        before.Remove("also_close_over_long_above");
        before[DipCandidate.SellParameterBefore] = DipRule.RsiExit;

        Assert.Equal(CandidateRegistrar.Registered, (await registrar.RegisterAsync("the S&P 500 dip in the form from before", "a dip", "the edge", dipEvaluator.Name, before, "hooks-dip-before")).Outcome);

        var stored = CandidateEvaluator.Read((await registrar.RowsAsync()).Single(row => row.Candidate == "the S&P 500 dip in the form from before").Parameters);

        Assert.Equal(DipRule.RsiExit, stored[DipCandidate.SellParameter]);
        Assert.False(stored.ContainsKey(DipCandidate.SellParameterBefore));

        var quality = new QualityCandidate(MarketCloses.Index);
        var bookHooks = new Dictionary<string, double>(QualityCandidate.ParametersOf(QualityRule.Starting), StringComparer.Ordinal) { ["also_close_over_long_above"] = 1, ["score_close_over_fifty"] = 1 };

        Assert.Equal(CandidateRegistrar.Registered, (await registrar.RegisterAsync("quality over its long average", "a book", "the edge", quality.Name, bookHooks, "hooks-quality")).Outcome);

        var bookRefused = await registrar.RegisterAsync("quality at an exit", "a book", "the edge", quality.Name, new Dictionary<string, double>(bookHooks, StringComparer.Ordinal) { ["exit"] = 2 }, "hooks-quality-refused");

        Assert.Equal(CandidateRegistrar.Refused, bookRefused.Outcome);
        Assert.Contains("a book does not read", bookRefused.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnIndexRuleIsKeptByItsHooksOverTheNightsReadingsAndWalkedUnderItsExitWhileARuleStatingNoneWalksAsBefore()
    {
        using var store = FrozenIndexStore(1.01);
        var breakout = IndexEvaluator("breakout-400");
        var live = IndexRules.Provisional(breakout);

        Assert.Equal(0, (await RegisterVerbAt(store, FreezeAt, RegisterVerb.IndexFamily, "breakout", "--index", "MID", "--parameters", Typed(live))).Code);

        // Beside the live rule, the same settings also requiring a close at least its long average, ordered by it, and
        // walked under the menu's fourteenth exit, a target at 1.5 risks in place of the trail.
        const string Hooked = "the S&P 400 breakout over its long average";
        var target = ExitMenu.TargetRisks[0];
        var hooked = new Dictionary<string, double>(live, StringComparer.Ordinal) { ["also_close_over_long_above"] = 1, ["score_close_over_long"] = 1, ["exit"] = ExitMenu.Swing.Single(exit => exit.Kind == ExitKind.Target && exit.K == target).Number };
        var registrar = new CandidateRegistrar(FixedClock.At(FreezeAt.AddMinutes(1), SessionZones.UnitedStates), store.DatabaseFile);

        Assert.Equal(CandidateRegistrar.Registered, (await registrar.RegisterAsync(Hooked, "a breakout", "the edge", breakout.Name, hooked, "hooked")).Outcome);

        var register = await registrar.RowsAsync();
        var clock = FixedClock.At(new DateTimeOffset(2026, 10, 2, 23, 40, 0, TimeSpan.Zero), SessionZones.UnitedStates);
        IReadOnlyList<string> Kept(string candidate) => TextRows(store, $"SELECT ticker || '|' || place FROM index_rule_trade WHERE candidate = '{candidate}' ORDER BY place;");

        // A night that supplies no readings: the hooked rule keeps nothing, and the live rule its five.
        await new IndexFamilies(clock, store.DatabaseFile).RunAsync("no-readings", default, register, FreezeAt.AddHours(1));

        var liveName = register.Single(row => FamilyRecords.IsLive(row.Candidate)).Candidate;

        Assert.Empty(Kept(Hooked));
        Assert.Equal(["IE|1", "MV|2", "PN|3", "IA|4", "IC|5"], Kept(liveName));

        // A night that supplies them, the closes over the long average IE 1.1, MV 0.9, PN 1.2, IA 1.05, IC none, ID 1.3 and
        // IB 1.0: MV and IC fail the condition, IB at 1 passes, and the five left are ordered by the reading, ID, PN, IE,
        // IA and IB, while the live rule keeps the five it kept.
        var overLong = new Dictionary<string, double?>(StringComparer.Ordinal) { ["IE"] = 1.1, ["MV"] = 0.9, ["PN"] = 1.2, ["IA"] = 1.05, ["ID"] = 1.3, ["IB"] = 1.0 };

        await new IndexFamilies(clock, store.DatabaseFile).RunAsync(
            "readings",
            default,
            register,
            FreezeAt.AddHours(1),
            (index, _) => Task.FromResult<Func<string, IReadOnlyList<double?>?>>(ticker => HookReadings(overLong.GetValueOrDefault(ticker), null, null)));

        Assert.Equal(["ID|1", "PN|2", "IE|3", "IA|4", "IB|5"], Kept(Hooked));
        Assert.Equal(["IE|1", "MV|2", "PN|3", "IA|4", "IC|5"], Kept(liveName));
        Assert.Equal(["14"], TextRows(store, $"SELECT DISTINCT IFNULL(exit, 'none') FROM index_rule_trade WHERE candidate = '{Hooked}';"));
        Assert.Equal(["none"], TextRows(store, $"SELECT DISTINCT IFNULL(exit, 'none') FROM index_rule_trade WHERE candidate = '{liveName}';"));

        // The night run again handed no readings: the hooked rule lists nothing, and the five trades the run that read
        // them stored stand as they stood, every field the same, while the live rule's five are written again.
        string HookedTrades() => string.Join("; ", TextRows(store, $"SELECT ticker || '|' || place || '|' || entry || '|' || stop || '|' || IFNULL(target, 'none') || '|' || cap || '|' || IFNULL(exit, 'none') FROM index_rule_trade WHERE candidate = '{Hooked}' ORDER BY place;"));
        var hookedKept = HookedTrades();

        await new IndexFamilies(clock, store.DatabaseFile).RunAsync("no-readings-again", default, register, FreezeAt.AddHours(1));

        Assert.Equal(hookedKept, HookedTrades());
        Assert.Equal(["ID|1", "PN|2", "IE|3", "IA|4", "IB|5"], Kept(Hooked));
        Assert.Equal(["IE|1", "MV|2", "PN|3", "IA|4", "IC|5"], Kept(liveName));

        // From 21.4 a rule stating the exposure in the market check's place, at a floor of 0.45: with the breadth at 0.5 and
        // the VIX at 1.2 of its median one switch holds and it keeps the family's first two, IE and MV; with both holding,
        // the VIX at 0.9, its five; with neither, the breadth at 0.3, none; and handed no readings it keeps none and leaves
        // the night's trades as they stood.
        const string Exposed = "the S&P 400 breakout under an exposure";
        var exposedRegistrar = new CandidateRegistrar(FixedClock.At(FreezeAt.AddMinutes(2), SessionZones.UnitedStates), store.DatabaseFile);

        Assert.Equal(CandidateRegistrar.Registered, (await exposedRegistrar.RegisterAsync(Exposed, "a breakout", "the edge", breakout.Name, new Dictionary<string, double>(live, StringComparer.Ordinal) { [RuleHooks.ExposureParameter] = 0.45 }, "exposed")).Outcome);

        var withExposure = await exposedRegistrar.RowsAsync();

        // The night's readings carry the switches beside each member's close over its long average, so the hooked rule
        // keeps its five through these runs.
        double?[] Switched(string ticker, double breadth, double vix)
        {
            var readings = HookReadings(overLong.GetValueOrDefault(ticker), null, null);

            readings[RuleHooks.BreadthColumn] = breadth;
            readings[RuleHooks.VixOverMedianColumn] = vix;

            return readings;
        }

        async Task NightUnder(string run, double breadth, double vix) =>
            await new IndexFamilies(clock, store.DatabaseFile).RunAsync(run, default, withExposure, FreezeAt.AddHours(1), (index, _) => Task.FromResult<Func<string, IReadOnlyList<double?>?>>(ticker => Switched(ticker, breadth, vix)));

        await NightUnder("exposure-one", 0.5, 1.2);
        Assert.Equal(["IE|1", "MV|2"], Kept(Exposed));
        await NightUnder("exposure-both", 0.5, 0.9);
        Assert.Equal(["IE|1", "MV|2", "PN|3", "IA|4", "IC|5"], Kept(Exposed));
        await NightUnder("exposure-neither", 0.3, 1.2);
        Assert.Empty(Kept(Exposed));
        await new IndexFamilies(clock, store.DatabaseFile).RunAsync("exposure-none", default, withExposure, FreezeAt.AddHours(1));
        Assert.Empty(Kept(Exposed));
        Assert.Equal(["IE|1", "MV|2", "PN|3", "IA|4", "IC|5"], Kept(liveName));

        // Three sessions after it IE, held by both rules at one buy and stop, closes 0.6 of a risk up, then 0.2 of a risk
        // under its buy, then 1.6 risks up, each other member flat. The live rule, stating no hook, trails a risk under
        // its highest close and holds through all three, where a stop raised to the buy would have sold the dip; the
        // hooked rule sells IE at the third close, its target at 1.5 risks, for 1.6.
        var held = TextRows(store, $"SELECT entry || '|' || stop FROM index_rule_trade WHERE candidate = '{Hooked}' AND ticker = 'IE';").Single().Split('|');
        var entry = decimal.Parse(held[0], System.Globalization.CultureInfo.InvariantCulture);
        var risk = entry - decimal.Parse(held[1], System.Globalization.CultureInfo.InvariantCulture);
        var night = DateOnly.ParseExact(IndexNight, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        DateOnly[] after = [night.AddDays(3), night.AddDays(4), night.AddDays(5)];
        decimal[] path = [entry + (0.6m * risk), entry - (0.2m * risk), entry + (1.6m * risk)];

        foreach (var ticker in new[] { "IA", "IB", "IC", "ID", "IE", "MV", "PN", "FL", "PR", "NH" })
        {
            var flat = decimal.Parse(TextRows(store, $"SELECT close FROM bar WHERE ticker = '{ticker}' ORDER BY session_date DESC LIMIT 1;").Single(), System.Globalization.CultureInfo.InvariantCulture);

            StoreYear(store, ticker, [.. after.Select((session, at) => ticker == "IE" ? new FamilyBar(session, path[at] + 0.1m, path[at] - 0.1m, path[at], 1_000_000) : new FamilyBar(session, flat + 0.1m, flat - 0.1m, flat, 1_000_000))]);
        }

        await new IndexFamilies(clock, store.DatabaseFile).RunAsync(
            "readings-after",
            default,
            register,
            FreezeAt.AddHours(1),
            (index, _) => Task.FromResult<Func<string, IReadOnlyList<double?>?>>(ticker => HookReadings(overLong.GetValueOrDefault(ticker), null, null)));

        Assert.Equal(["none"], TextRows(store, $"SELECT IFNULL(ended_on, 'none') FROM index_rule_trade WHERE candidate = '{liveName}' AND ticker = 'IE';"));

        var sold = TextRows(store, $"SELECT ended_on || '|' || result FROM index_rule_trade WHERE candidate = '{Hooked}' AND ticker = 'IE';").Single().Split('|');

        Assert.Equal(after[2].ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture), sold[0]);
        Assert.Equal(1.6, double.Parse(sold[1], System.Globalization.CultureInfo.InvariantCulture), 6);
    }

    [Fact]
    public void AHookedTradeIsWalkedUnderItsExitAndAnUnhookedOneAsItAlwaysWas()
    {
        // The menu's constructed path as a stock's stored closes, one a weekday from 2026-01-02.
        var sessions = new List<DateOnly>();

        for (var day = new DateOnly(2026, 1, 2); sessions.Count < MenuPath.Length; day = day.AddDays(1))
        {
            if (day.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday))
            {
                sessions.Add(day);
            }
        }

        var series = sessions.Select((session, at) => (session, MenuPath[at])).ToArray();
        var at = sessions.Select((session, index) => (session, index)).ToDictionary(pair => pair.session, pair => pair.index);

        // Its own exit, the target at 110, sells at the thirteenth session at 2.2 risks; under the second exit of the
        // menu, the stop raised to the buy at a risk, the eleventh sells at -0.1, as the menu's replay worked by hand.
        Assert.Equal((sessions[13], (double?)2.2), Rounded(FamilyRecorder.Walk(sessions[0], 100m, 95m, 110m, 20, series, sessions, at)));
        Assert.Equal((sessions[11], (double?)-0.1), Rounded(FamilyRecorder.Walk(sessions[0], 100m, 95m, 110m, 20, series, sessions, at, exit: 2, riskMoves: 2.5)));

        static (DateOnly, double?) Rounded((DateOnly On, double? Result)? walked) =>
            (walked!.Value.On, walked.Value.Result is { } result ? Math.Round(result, 9) : null);
    }

    [Fact]
    public void ARulesListIsKeptAndOrderedByItsHooksWhereTheyReadReadingsAndAsItStandsWhereNone()
    {
        static (string, ShadowOutcome) Fired(string ticker, double order) =>
            (ticker, new ShadowOutcome("a rule", true, new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [FamilyRuleEvaluator.EntryValue] = "100",
                [FamilyRuleEvaluator.StopValue] = "95",
                [FamilyRuleEvaluator.TargetValue] = "110",
                [FamilyRuleEvaluator.OrderValue] = order.ToString(System.Globalization.CultureInfo.InvariantCulture),
                [FamilyRuleEvaluator.MoveValue] = "2",
            }));

        (string, ShadowOutcome)[] verdicts = [Fired("A", 5), Fired("B", 4), Fired("C", 3), Fired("D", 2), Fired("E", 1)];
        var readings = new Dictionary<string, double?[]>(StringComparer.Ordinal)
        {
            ["A"] = HookReadings(1.02, 0.03, 1.1),
            ["B"] = HookReadings(1.02, 0.03, 1.0),
            ["C"] = HookReadings(0.95, 0.03, 1.5),
            ["D"] = HookReadings(1.02, 0.03, 1.2),
            ["E"] = HookReadings(1.02, 0.03, 1.1),
        };
        var hooks = RuleHooks.Of(new Dictionary<string, double>(StringComparer.Ordinal) { ["also_close_over_long_above"] = 1, ["score_close_over_fifty"] = 2, ["score_floor"] = 2.1 });

        // The family's own order by its figure, A to E, and the hooks' D, A and E, each kept with its place.
        Assert.Equal(["A", "B", "C", "D", "E"], FamilyRecorder.Keep(verdicts, new HashSet<string>(StringComparer.Ordinal)).Select(one => one.Ticker));
        Assert.Equal(["D", "A", "E"], FamilyRecorder.Keep(verdicts, new HashSet<string>(StringComparer.Ordinal), hooks, ticker => readings[ticker]).Select(one => one.Ticker));
        Assert.Equal([1, 2, 3], FamilyRecorder.Keep(verdicts, new HashSet<string>(StringComparer.Ordinal), hooks, ticker => readings[ticker]).Select(one => one.Place));

        // A held stock is passed over in either.
        Assert.Equal(["A", "E"], FamilyRecorder.Keep(verdicts, new HashSet<string>(["D"], StringComparer.Ordinal), hooks, ticker => readings[ticker]).Select(one => one.Ticker));

        // From 21.4 a rule stating the exposure keeps as well a member whose verdict failed the market check alone, the
        // picks the night's switches allow read off the night's readings: five where the breadth stands at the floor and
        // the VIX at its median, two where one holds, none where neither and none on a night handed no readings; a
        // verdict failing its new high as well stays off, and a rule stating no exposure keeps none of them.
        static (string, ShadowOutcome) Closed(string ticker, double order, bool alsoFailing = false) =>
            (ticker, new ShadowOutcome("a rule", false, new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [FamilyRule.Market] = FamilyRecorder.Failed,
                [BreakoutRule.NewHigh] = alsoFailing ? FamilyRecorder.Failed : "passed",
                [FamilyRuleEvaluator.EntryValue] = "100",
                [FamilyRuleEvaluator.StopValue] = "95",
                [FamilyRuleEvaluator.TargetValue] = "110",
                [FamilyRuleEvaluator.OrderValue] = order.ToString(System.Globalization.CultureInfo.InvariantCulture),
                [FamilyRuleEvaluator.MoveValue] = "2",
            }));

        (string, ShadowOutcome)[] closedNight = [Closed("A", 7), Closed("B", 6), Closed("C", 5, alsoFailing: true), Closed("D", 4), Closed("E", 3), Closed("F", 2), Closed("G", 1)];
        var exposure = RuleHooks.Of(new Dictionary<string, double>(StringComparer.Ordinal) { ["exposure"] = 0.45 });
        var nothingHeld = new HashSet<string>(StringComparer.Ordinal);

        // The recorder's word for a failed gate is the one the evaluators' values write, read off a result with the check
        // closed and one with it open.
        Assert.Equal(FamilyRecorder.Failed, FamilyRuleEvaluator.ValuesOf(new FamilyResult("A", BreakoutRule.Name, [new Gate(FamilyRule.Market, false, "closed", FamilyRule.Values())], 100m, 95m, null, 1, []), 2)[FamilyRule.Market]);
        Assert.Equal("passed", FamilyRuleEvaluator.ValuesOf(new FamilyResult("A", BreakoutRule.Name, [new Gate(FamilyRule.Market, true, "open", FamilyRule.Values())], 100m, 95m, null, 1, []), 2)[FamilyRule.Market]);
        Assert.True(FamilyRecorder.FailsOnTheMarketAlone(closedNight[0].Item2));
        Assert.False(FamilyRecorder.FailsOnTheMarketAlone(closedNight[2].Item2));
        Assert.Empty(FamilyRecorder.Keep(closedNight, nothingHeld));
        Assert.Equal(["A", "B", "D", "E", "F"], FamilyRecorder.Keep(closedNight, nothingHeld, exposure, _ => NightReadings(0.45, 1.0)).Select(one => one.Ticker));
        Assert.Equal(["A", "B"], FamilyRecorder.Keep(closedNight, nothingHeld, exposure, _ => NightReadings(0.3, 1.0)).Select(one => one.Ticker));
        Assert.Empty(FamilyRecorder.Keep(closedNight, nothingHeld, exposure, _ => NightReadings(0.3, 1.2)));
        Assert.Empty(FamilyRecorder.Keep(closedNight, nothingHeld, exposure, null));
    }

    [Fact]
    public void ARuleStatingTheRankingKeepsTheBestFiveOfItsFamilysLooseSetupsByTheScoreEachAtTheSetupsOwnPlan()
    {
        // Seven setups at the loose gates, each at its own plan, their scores read off one column: B and E tied at 0.9 in
        // the rows' order, then G, D and A; F sixth and C, which the score cannot read, left off. G's stop stands at its
        // buy and places no trade, so four are kept, each at its own entry, stop and target with the target's distance
        // over the stop's as its reward to risk; a stock held is passed over after the five are chosen.
        var column = Column("close_over_long");
        var scores = new Dictionary<string, double?>(StringComparer.Ordinal) { ["A"] = 0.2, ["B"] = 0.9, ["C"] = null, ["D"] = 0.5, ["E"] = 0.9, ["F"] = 0.1, ["G"] = 0.7 };

        LooseSetup Setup(string ticker, decimal entry = 100m, decimal stop = 95m, decimal? target = 110m)
        {
            var readings = new double?[LedgerReadings.Count];

            readings[column] = scores[ticker];

            return new LooseSetup(ticker, entry, stop, target, null, 20, 2.5, readings);
        }

        LooseSetup[] setups = [Setup("A"), Setup("B"), Setup("C"), Setup("D", 50m, 48m, 56m), Setup("E"), Setup("F"), Setup("G", 200m, 200m, 210m)];
        var byScore = RuleHooks.Of(new Dictionary<string, double>(StringComparer.Ordinal) { ["ranking"] = RuleHooks.RankingByScore });
        var ranking = byScore.WithRanker(new OneColumnScore(column));
        var nothingHeld = new HashSet<string>(StringComparer.Ordinal);
        var kept = FamilyRecorder.KeepRanked(setups, nothingHeld, ranking);

        Assert.Equal(["B", "E", "D", "A"], kept.Select(one => one.Ticker));
        Assert.Equal([1, 2, 3, 4], kept.Select(one => one.Place));
        Assert.Equal((50m, 48m, (decimal?)56m, (double?)2.5, 3.0), (kept[2].Plan.Entry, kept[2].Plan.Stop, kept[2].Plan.Target, kept[2].Plan.RiskMoves, Math.Round(kept[2].Plan.RewardToRisk!.Value, 9)));
        Assert.Equal(["B", "D", "A"], FamilyRecorder.KeepRanked(setups, new HashSet<string>(["E"], StringComparer.Ordinal), ranking).Select(one => one.Ticker));

        // Handed no score, a ranking by the score keeps none; a ranking by the expected value takes the best five by it,
        // C and E at 0.8, F, A and G, with B, holding no figure, left off and G placing no trade.
        var expected = new Dictionary<string, double?>(StringComparer.Ordinal) { ["A"] = 0.3, ["B"] = null, ["C"] = 0.8, ["D"] = 0.1, ["E"] = 0.8, ["F"] = 0.6, ["G"] = 0.2 };
        var byExpected = RuleHooks.Of(new Dictionary<string, double>(StringComparer.Ordinal) { ["ranking"] = RuleHooks.RankingByExpected });

        Assert.Empty(FamilyRecorder.KeepRanked(setups, nothingHeld, byScore));
        Assert.Equal(["C", "E", "F", "A"], FamilyRecorder.KeepRanked(setups, nothingHeld, byExpected, ticker => expected[ticker]).Select(one => one.Ticker));
        Assert.Empty(FamilyRecorder.KeepRanked(setups, nothingHeld, byExpected));
    }

    [Fact]
    public void TheAutopsysFiguresAreWorkedByHandOverConstructedPaths()
    {
        // Three trades bought at 100 with a risk of 5: one up to 108 and stopped at 94, one under at 97 and then at its
        // target at 111, and one up to 102 and stopped at 93.
        TradePath[] paths =
        [
            new([100, 104, 108, 103, 94], 100, 5, SetupEnds.Stop),
            new([100, 98, 97, 111], 100, 5, SetupEnds.Target),
            new([100, 102, 101, 96, 93], 100, 5, SetupEnds.Stop),
        ];

        var figures = PathAutopsy.Read(paths).ToDictionary(figure => figure.Figure);

        // Best closes 1.6, 2.2 and 0.4 risks, the median 1.6; worst -1.2, -0.6 and -1.4, the median -1.2; the best
        // close 2, 3 and 1 sessions in, the median 2; one of the two stopped had closed a risk up first; the one target
        // fell 0.6 under the buy first.
        Assert.Equal(1.6, figures[PathAutopsy.Best].Value!.Value, 9);
        Assert.Equal(-1.2, figures[PathAutopsy.Worst].Value!.Value, 9);
        Assert.Equal(2.0, figures[PathAutopsy.ToBest].Value);
        Assert.Equal(0.5, figures[PathAutopsy.StoppedUp].Value);
        Assert.Equal(2, figures[PathAutopsy.StoppedUp].Trades);
        Assert.Equal(-0.6, figures[PathAutopsy.TargetWorst].Value!.Value, 9);
        Assert.Equal("the median trade's best close was 1.60 risks above its buy before it ended", figures[PathAutopsy.Best].Words);
        Assert.Equal("1 of the 2 trades the stop ended had first closed a risk up", figures[PathAutopsy.StoppedUp].Words);
        Assert.Equal("the trades the target ended fell a median 0.60 risks under the buy first", figures[PathAutopsy.TargetWorst].Words);

        // No trade at all reads none of them.
        Assert.All(PathAutopsy.Read([]), figure => Assert.Null(figure.Value));
    }

    [Fact]
    public void TheFiveStrongestExitsAreChosenOnTheLearningYearsAloneAboveTheRulesOwn()
    {
        // Weekdays from the last of 2018 through 2022's first month, and the fold testing 2022: three learning years,
        // so 113 trades and three years above nothing.
        var calendar = new List<DateOnly>();

        for (var day = new DateOnly(2018, 12, 31); day <= new DateOnly(2022, 1, 31); day = day.AddDays(1))
        {
            if (day.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday))
            {
                calendar.Add(day);
            }
        }

        var fold = LoopFolds.Of(calendar, calendar[^1]).Single();
        var cut = calendar.IndexOf(fold.TestFrom);
        var nights = calendar.Count - 1;

        // 120 trades over 2019 to 2021, each ending five sessions after its buy, at a given edge; the trades of an exit
        // whose ends all fall in the test year learn nothing.
        IReadOnlyList<ExitProcedures.Walked> Trades(double edge, int count = 120, bool endInTheTestYear = false) =>
        [
            .. Enumerable.Range(0, count).Select(at =>
            {
                var entry = 1 + (at * ((cut - 10) / count));
                var exit = endInTheTestYear ? cut + 2 : entry + 5;
                var listing = new FamilyListing(at, entry, entry, 0, 0, 100, 95, 110, double.NaN, 63, 2);

                return new ExitProcedures.Walked(entry, exit, new FamilyTrade(listing, calendar[entry].Year - 2019, edge, 0), "target");
            }),
        ];

        var own = Trades(0.1);
        var byExit = ExitMenu.Swing.Select(exit => exit.Number switch
        {
            <= 4 => Trades(0.1 + (0.01 * exit.Number)),
            5 => Trades(0.5, count: 112),
            <= 10 => Trades(0.1 + (0.01 * exit.Number)),
            26 => Trades(1.0, endInTheTestYear: true),
            _ => Trades(0.05),
        }).ToArray();

        var ranked = ExitProcedures.Rank(fold, calendar, 3, nights, own, byExit);

        // Exits 6 to 10 above the rule's 0.1 at 0.16 to 0.20 and 1 to 4 at 0.11 to 0.14; the fifth, one trade short of
        // 113, and the twenty-sixth, whose trades end in the test year, are not ranked, nor any exit at or under 0.1.
        Assert.Equal([10, 9, 8, 7, 6], ranked.Take(ExitProcedures.Proposals).Select(one => one.Exit.Number));
        Assert.Equal([10, 9, 8, 7, 6, 4, 3, 2, 1], ranked.Select(one => one.Exit.Number));
        Assert.Equal(0.2, ranked[0].Edge, 9);
    }
}
