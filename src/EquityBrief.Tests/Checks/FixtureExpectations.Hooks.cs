using EquityBrief.Core.Candidates;
using EquityBrief.Core.Families;
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
// see: Every engine's settings hooks land together and all default off, so the families' pins move once
// see: The trade autopsy proposes exits of a fixed menu, each tested as the procedure that chose it
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
        Assert.Throws<ArgumentException>(() => RuleHooks.Of(new Dictionary<string, double>(StringComparer.Ordinal) { ["exit"] = 27 }));

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

    [Fact]
    public void TheHooksAndTheMenuArePinnedByTheRulesTheyHookAndByNoOtherEvaluator()
    {
        // The S&P 500's breakout and drift and every S&P 400 and 600 rule pin the hooks' and the menu's sources beside
        // their own; the swing filter, the three conditions and the S&P 500's heavyweights do not, so the pin move of the
        // hooks moved none of their versions, and no shared evaluation source names them.
        string[] hooked = ["breakout", "breakout-400", "breakout-600", "drift", "drift-400", "drift-600", "heavyweight-400", "heavyweight-600", "pullback-400", "pullback-600"];

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

        parameters["exit"] = 27;

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
        Assert.Contains("menu numbers its exits", IndexRules.Refusal(drift, new Dictionary<string, double>(swing, StringComparer.Ordinal) { ["exit"] = 27 }), StringComparison.Ordinal);
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
