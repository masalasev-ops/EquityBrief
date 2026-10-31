using System.Globalization;
using EquityBrief.Core.Candidates;
using EquityBrief.Core.Families;
using EquityBrief.Core.Filter;
using EquityBrief.Core.Ledger;
using EquityBrief.Core.Loop;
using EquityBrief.Core.Readings;
using EquityBrief.Core.Releases;
using EquityBrief.Core.Sweep;
using EquityBrief.Core.Time;
using EquityBrief.Worker.Cards;
using EquityBrief.Worker.Indices;
using EquityBrief.Worker.Loop;
using EquityBrief.Worker.Sweep;
using Microsoft.Data.Sqlite;
using TestVerdict = EquityBrief.Core.Loop.RuleVerdict;

namespace EquityBrief.Tests.Checks;

// fixture-expectations, 21.1: every registered rule walked by the per-rule test at what it reads, or named with what
// judges it. Each cause the run of 2026-10-27 could not test is routed over constructed registrations: an S&P 400 or 600
// rule stating a level the extended grid's walk does not read to its sweep's own reading, a book off its design's six
// settings to the lay at its own setting, a swing filter version at the ladder's or the nearest bands' plan to the ideas'
// run's plan of that kind, and a version reading a business's reported quarters, the analysts' estimates or sector
// leadership kept with its judge. Over a constructed history the own reading's walk gives that reading's own trades, a
// book off the six's months are the record command's, and the tone condition's opposite is the complement of the
// variant's over the releases read.
// see: Every registered rule is walked by the per-rule test at what it reads, and one the history cannot walk names what judges it instead
public partial class FixtureExpectations
{
    static RegisterRow StandingRow(string candidate, string evaluator, IReadOnlyDictionary<string, double> parameters) =>
        new(1, candidate, "a rule", "a test", evaluator, CandidateEvaluator.Write(parameters), "v", CandidateFamily.Registered, null, FreezeAt, null);

    [Fact]
    public void EachCauseTheRunOf20261027CouldNotTestIsRoutedToItsSweepsOwnReadingOrNamesItsJudge()
    {
        var pullback400 = IndexEvaluator("pullback-400");
        var breakout400 = IndexEvaluator("breakout-400");
        var drift400 = IndexEvaluator("drift-400");
        var drift600 = IndexEvaluator("drift-600");
        var provisionalDrift = IndexRules.Provisional(drift400);

        // The freezes of 2026-10-20 each state a level the extended grid's walk does not read: the S&P 400's pullback at its
        // own breadth at 35 per cent with the profit and cover check, its breakout at the S&P 500's breadth, the S&P 600's
        // drift held 63 sessions with its revenue grown; the provisional settings state none.
        Assert.True(RuleTests.ReadsOwnSweep(pullback400, FrozenParameters(RemedyFreezes[0].Parameters)));
        Assert.True(RuleTests.ReadsOwnSweep(breakout400, FrozenParameters(RemedyFreezes[1].Parameters)));
        Assert.True(RuleTests.ReadsOwnSweep(drift600, FrozenParameters(RemedyFreezes[6].Parameters)));
        Assert.False(RuleTests.ReadsOwnSweep(pullback400, IndexRules.Provisional(pullback400)));
        Assert.False(RuleTests.ReadsOwnSweep(breakout400, IndexRules.Provisional(breakout400)));
        Assert.False(RuleTests.ReadsOwnSweep(drift400, provisionalDrift));

        // Each cause alone, moved from the provisional setting: the breadth floor, the quality check, the hold, the first
        // pullback's entry, the revenue dial and the S&P 500's breadth; a reading version alone is no level, and one beside a
        // level is read after the sweep's own walk.
        Assert.True(RuleTests.ReadsOwnSweep(drift400, FrozenMoved(provisionalDrift, (IndexRuleCandidate.BreadthFloorParameter, 0.35))));
        Assert.True(RuleTests.ReadsOwnSweep(drift400, FrozenMoved(provisionalDrift, (IndexRuleCandidate.QualityParameter, 2))));
        Assert.True(RuleTests.ReadsOwnSweep(drift400, FrozenMoved(provisionalDrift, (IndexRuleCandidate.HoldParameter, 63))));
        Assert.True(RuleTests.ReadsOwnSweep(drift400, FrozenMoved(provisionalDrift, (IndexDriftCandidate.EntryParameter, 1))));
        Assert.True(RuleTests.ReadsOwnSweep(drift400, FrozenMoved(provisionalDrift, (IndexDriftCandidate.RevenueGrewParameter, 1))));
        Assert.True(RuleTests.ReadsOwnSweep(breakout400, FrozenMoved(IndexRules.Provisional(breakout400), (IndexRuleCandidate.LargeBreadthParameter, 1))));
        Assert.False(RuleTests.ReadsOwnSweep(drift400, FrozenMoved(provisionalDrift, (ReleaseTone.VersionParameter, 5))));
        Assert.True(RuleTests.ReadsOwnSweep(drift400, FrozenMoved(provisionalDrift, (ReleaseTone.VersionParameter, 5), (IndexDriftCandidate.EntryParameter, 1))));
        Assert.DoesNotContain(ReleaseTone.VersionParameter, RuleTests.Unversioned(FrozenMoved(provisionalDrift, (ReleaseTone.VersionParameter, 5))).Keys);

        // The reads a run lays out before it walks: the family and the stop floor of each such breakout or drift rule
        // standing on the index, each once, the pullback's search where such a pullback stands, and none for a provisional
        // setting, a rule in the background or another index's rule.
        var entry = FrozenMoved(provisionalDrift, (IndexDriftCandidate.EntryParameter, 1));
        RegisterRow[] standing =
        [
            StandingRow(FamilyRecords.LivePrefix + "S&P 400 breakout rule", "breakout-400", FrozenParameters(RemedyFreezes[1].Parameters)),
            StandingRow(FamilyRecords.LivePrefix + "S&P 400 drift rule", "drift-400", entry),
            StandingRow("the S&P 400 drift's market variant", "drift-400", FrozenMoved(entry, (IndexRuleCandidate.LargeBreadthParameter, IndexRuleCandidate.NoMarketCheck))),
            StandingRow("a drift variant at the provisional setting", "drift-400", provisionalDrift),
            StandingRow(FamilyRecords.BackgroundPrefix + "S&P 400 drift rule", "drift-400", FrozenMoved(provisionalDrift, (IndexRuleCandidate.HoldParameter, 63))),
            StandingRow(FamilyRecords.LivePrefix + "S&P 400 pullback rule", "pullback-400", FrozenParameters(RemedyFreezes[0].Parameters)),
            StandingRow(FamilyRecords.LivePrefix + "S&P 600 drift rule", "drift-600", FrozenParameters(RemedyFreezes[6].Parameters)),
        ];
        var driftFloor = entry.GetValueOrDefault(IndexDriftCandidate.StopFloorParameter);

        Assert.Equal([(BreakoutRule.Name, 0.0), (DriftRule.Name, driftFloor)], RuleTests.OwnSwingsNeeded(standing, "MID"));
        Assert.True(RuleTests.OwnPullbackNeeded(standing, "MID"));
        Assert.Equal([(DriftRule.Name, FrozenParameters(RemedyFreezes[6].Parameters)[IndexDriftCandidate.StopFloorParameter])], RuleTests.OwnSwingsNeeded(standing, "SML"));
        Assert.False(RuleTests.OwnPullbackNeeded(standing, "SML"));
        Assert.Empty(RuleTests.OwnSwingsNeeded([standing[3], standing[4]], "MID"));

        // A rule stating such a level walked by a run that laid out no reading for its family is kept with why, as is one
        // whose level its sweep's second stage never read; each verdict names what judges it.
        var none = new RuleTestInputs(null!, standing, new Dictionary<string, LoopChange>(StringComparer.Ordinal), [], null!, null!, _ => null, null, _ => { }, Own: OwnReads.None);
        var noRead = RuleTests.ThroughOwnSweep(none, DriftRule.Name, standing[1], drift400, entry);

        Assert.Equal(RuleTests.NoOwnRead, noRead.Why);
        Assert.Equal("not testable: " + RuleTests.NoOwnRead + "; judged by its record from its registration, on paper trading and the Run page", TestVerdict.Of(false, noRead.Why, null, null, null, null, null));
        Assert.Equal(RuleTests.NoOwnRead, RuleTests.ThroughOwnSearch(null, pullback400, standing[5], FrozenParameters(RemedyFreezes[0].Parameters)).Why);

        // The S&P 500's swing filter versions: the plan each trade gate reads, which the ideas' run reads for every
        // candidate, and the versions the history holds no reading of, kept with their judge.
        Assert.Equal(PlanRule.Ladder, RuleTests.PlanOf(new Dictionary<string, double>(StringComparer.Ordinal) { [SwingFilterRule.TradeParameter] = 0 }));
        Assert.Equal(PlanRule.NearestBands, RuleTests.PlanOf(new Dictionary<string, double>(StringComparer.Ordinal) { [SwingFilterRule.TradeParameter] = 1 }));
        Assert.Equal(PlanRule.Clear, RuleTests.PlanOf(new Dictionary<string, double>(StringComparer.Ordinal) { [SwingFilterRule.TradeParameter] = 2 }));
        Assert.Equal(PlanRule.Clear, RuleTests.PlanOf(new Dictionary<string, double>(StringComparer.Ordinal)));
        Assert.Equal(", its trade the plan at the nearest bands", LargePullback.PlanWords(PlanRule.NearestBands));
        Assert.Equal(", its trade the ladder's first tranche", LargePullback.PlanWords(PlanRule.Ladder));
        Assert.Equal(string.Empty, LargePullback.PlanWords(PlanRule.Clear));

        // The judge's words stand after every reason, the verdict still ranked as not testable, and the tester's own line
        // names it the same way.
        Assert.Equal("not testable: a reading; judged by its record from its registration, on paper trading and the Run page", TestVerdict.NotTestable("a reading"));
        Assert.StartsWith(TestVerdict.NotTestablePrefix, TestVerdict.NotTestable("a reading"), StringComparison.Ordinal);
        Assert.Equal(3, TestVerdict.Rank(TestVerdict.NotTestable("a reading")));
        Assert.Equal(
            "drift, a rule: not testable, " + RuleTests.Estimates + "; judged by its record from its registration, on paper trading and the Run page",
            WalkForwardTester.RuleLine(new RuleTested(new RuleUnderTest("drift", "a rule", RuleTests.Variant, "words", LoopProcedures.Risks, 0, Why: RuleTests.Estimates), "its live rule", null, null, RuleTests.Estimates)));
    }

    [Fact]
    public void TheToneConditionsOppositeIsTheComplementOfTheVariantsOverTheReleasesRead()
    {
        // Every answer the four results and the four outlooks make: the variant's condition and its opposite never agree,
        // and the condition is results better with an outlook not lowered or withdrawn.
        foreach (var results in new[] { "better", "in line", "worse", "not stated" })
        {
            foreach (var outlook in new[] { "raised", "kept", "lowered or withdrawn", "none given" })
            {
                var answer = ReleaseOn(Utc(2025, 2, 3, 21, 5, 0), $"{results}|{outlook}|neutral|steady").Answer!;

                Assert.NotEqual(ReleaseTone.Passes(answer), RuleTests.IsOpposite(answer));
                Assert.Equal(results == "better" && outlook != "lowered or withdrawn", ReleaseTone.Passes(answer));
            }
        }

        // Over the listing the tone test reads, exactly one of the two holds where its print's release is read, and neither
        // where the reading was refused, the stock holds no release under the version or the bar holds no print; the walk
        // through the sweeps' own reading reads the same off the series and the calendar.
        DateOnly[] calendar = [new(2025, 1, 29), new(2025, 1, 30), new(2025, 1, 31), new(2025, 2, 3), new(2025, 2, 4), new(2025, 2, 5)];
        var series = ToneSeries("AAA", calendar, [new SweepSurprise(new DateOnly(2025, 2, 3), true, 4.0)], [-1, -1, 0, 0, 0, 0]);
        var read = new LoopRead("GSPC", null!, [series], [], null!, null!, NoIncome, calendar, 0, []);
        var tones = new ToneReleases(new Dictionary<long, IReadOnlyDictionary<string, IReadOnlyList<LedgerRelease>>>(), SessionZones.ResolveSessionZone(SessionZones.UnitedStates), new ReleaseCalendar(calendar));
        var listing = new RuleListing(0, 5, 5, null, null);

        IReadOnlyDictionary<string, IReadOnlyList<LedgerRelease>> Held(params LedgerRelease[] releases) =>
            new Dictionary<string, IReadOnlyList<LedgerRelease>>(StringComparer.Ordinal) { ["AAA"] = releases };

        (bool Holds, bool Opposite) Read(params LedgerRelease[] releases) =>
            (RuleTests.ToneHolds(read, listing, Held(releases), tones), RuleTests.ToneOpposite(read, listing, Held(releases), tones));

        Assert.Equal((true, false), Read(ReleaseOn(Utc(2025, 2, 3, 21, 5, 0), "better|kept|neutral|steady")));
        Assert.Equal((false, true), Read(ReleaseOn(Utc(2025, 2, 3, 21, 5, 0), "in line|raised|neutral|steady")));
        Assert.Equal((false, true), Read(ReleaseOn(Utc(2025, 2, 3, 21, 5, 0), "better|lowered or withdrawn|neutral|steady")));
        Assert.Equal((false, true), Read(ReleaseOn(Utc(2025, 2, 3, 21, 5, 0), "worse|none given|cautious|weaker")));
        Assert.Equal((false, false), Read(ReleaseOn(Utc(2025, 2, 3, 21, 5, 0), null)));
        Assert.Equal((false, false), Read());
        Assert.False(RuleTests.ToneOpposite(read, new RuleListing(0, 1, 1, null, null), Held(ReleaseOn(Utc(2025, 1, 29, 21, 0, 0), "in line|raised|neutral|steady")), tones));

        var inLine = Held(ReleaseOn(Utc(2025, 2, 3, 21, 5, 0), "in line|raised|neutral|steady"));

        Assert.True(RuleTests.ToneOpposite(read.Series, read.Calendar, 0, 5, 5, inLine, tones));
        Assert.False(RuleTests.ToneHolds(read.Series, read.Calendar, 0, 5, 5, inLine, tones));

        // The run's line for a tone variant: its trades and mean edge after costs on each side of the cutoff's year beside
        // its opposite's, worked by hand over constructed units, and over the history whole where the version states no
        // cutoff.
        DateOnly[] years = [new(2024, 6, 3), new(2024, 9, 2), new(2025, 3, 3), new(2025, 6, 2)];
        var tone = new ToneSides(5, new DateOnly(2025, 1, 31), [(0, 0.5), (1, null), (2, 1.0), (3, 2.0)], [(1, -1.0), (3, 0.25)]);
        var rule = new RuleUnderTest("drift", "the tone variant", RuleTests.Variant, "words", LoopProcedures.Risks, 0, tone.Variant, Tone: tone);

        Assert.Equal(
            "drift, the tone variant: its tone condition under reading version 5, 2 trade(s) at +0.500 risks before the local model's cutoff's year, 2025, and 2 trade(s) at +1.500 risks from it; its opposite, "
            + RuleTests.OppositeWords
            + ", walked unregistered and written nowhere, 1 trade(s) at -1.000 risks and 1 trade(s) at +0.250 risks",
            RuleTests.ToneLine(rule, years));
        Assert.Equal(
            "drift, the tone variant: its tone condition under reading version 5, 4 trade(s) at +1.167 risks over the history; its opposite, "
            + RuleTests.OppositeWords
            + ", walked unregistered and written nowhere, 2 trade(s) at -0.375 risks; the version states no cutoff",
            RuleTests.ToneLine(rule with { Tone = tone with { Cutoff = null } }, years));
        Assert.Equal(new DateOnly(2025, 1, 31), new ToneReleases(tones.Versions, tones.Zone, tones.Calendar, new Dictionary<long, DateOnly?> { [5] = new DateOnly(2025, 1, 31) }).CutoffOf(5));
        Assert.Null(tones.CutoffOf(5));
    }

    // The frozen records' constructed history with a fourth member, D, rising two hundredths a session at about $7 million
    // a session, so on A's breakout session two of the four members close above their 200-session average and the S&P
    // 600's own breadth is a half, open at every one of its floors; D's constant volume makes no breakout of its own.
    static TemporaryStore OwnLevelStore()
    {
        var store = FrozenRecordStore();
        var calendar = Weekdays(new DateOnly(2018, 1, 1), 360);

        using var connection = store.Open();
        using var transaction = connection.BeginTransaction();
        using var insert = connection.CreateCommand();

        insert.Transaction = transaction;
        insert.CommandText = "INSERT INTO bar (ticker, session_date, open, high, low, close, volume, source, observed_at, raw_close) VALUES ('D', $day, $close, $high, $low, $close, 70000, 'constructed', '2026-10-06T00:00:00Z', $close);";

        var session = insert.Parameters.Add("$day", SqliteType.Text);
        var close = insert.Parameters.Add("$close", SqliteType.Text);
        var high = insert.Parameters.Add("$high", SqliteType.Text);
        var low = insert.Parameters.Add("$low", SqliteType.Text);

        for (var day = 0; day < calendar.Length; day++)
        {
            var price = 100m + (0.02m * day);

            session.Value = calendar[day].ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            close.Value = price.ToString(CultureInfo.InvariantCulture);
            high.Value = (price + 1m).ToString(CultureInfo.InvariantCulture);
            low.Value = (price - 1m).ToString(CultureInfo.InvariantCulture);
            insert.ExecuteNonQuery();
        }

        using var rest = connection.CreateCommand();

        rest.Transaction = transaction;
        rest.CommandText =
            "INSERT INTO pulled_member (index_code, ticker, exchange, name, sector, industry, pull) VALUES ('MID', 'D', 'US', NULL, NULL, NULL, 'constructed'), ('SML', 'D', 'US', NULL, NULL, NULL, 'constructed');"
            + string.Concat(
                from quarter in new[] { ("2017-12-31", "2018-02-15"), ("2018-03-31", "2018-05-10"), ("2018-06-30", "2018-08-09"), ("2018-09-30", "2018-11-08") }
                select $"INSERT INTO pulled_income (ticker, period_end, filing_date, net_income, operating_income, interest_expense, pull) VALUES ('D', '{quarter.Item1}', '{quarter.Item2}', '1.00', '2.00', NULL, 'constructed');");
        rest.ExecuteNonQuery();
        transaction.Commit();

        return store;
    }

    [Fact]
    public async Task ARuleAtALevelItsSweepReadsIsWalkedByThatReadingsOwnTradesAndABookOffTheSixAtItsOwnSetting()
    {
        using var store = OwnLevelStore();
        var history = new SweepHistory(store.DatabaseFile);
        var through = await history.NewestSessionAsync();
        var large = await IndexSweepRunner.OwnSwingRead.LargeAsync(history, through, _ => { }, default);
        var companies = await history.HeavyweightAsync(through);
        var income = await history.IncomeAsync(through);
        var inputs = await history.ReadAsync(through, _ => { }, default, index: "SML", asItStood: true);
        var laid = IndexSweepRunner.OwnSwingRead.Laid("SML", inputs, companies, income, through);
        var breakout = await IndexSweepRunner.OwnSwingRead.ReadAsync(laid, large, BreakoutRule.Name, history, through, 0, default);
        var pullback = await IndexSweepRunner.OwnPullbackRead.ReadAsync(laid, large, history, through, _ => { }, default);
        var lay = await HeavyweightLay.ReadAsync(laid, history, through, _ => { }, default, large);

        pullback.Charge(1);
        SweepRelease(store);

        var own = new OwnReads(new Dictionary<(string Family, double StopFloor), IndexSweepRunner.OwnSwingRead> { [(BreakoutRule.Name, 0)] = breakout }, pullback);
        var testInputs = new RuleTestInputs(laid, [], new Dictionary<string, LoopChange>(StringComparer.Ordinal), [], lay, null!, _ => null, null, _ => { }, Own: own);
        var evaluator = IndexEvaluator("breakout-600");

        // The S&P 600's breakout at its own breadth at 40 per cent, a level the extended grid's walk does not read: A's
        // breakout alone, on a session the index's breadth stands at a half, walked by the sweep's own reading, the one
        // trade's edge that reading's own after costs over the same plan on every member, and the same trades the record
        // command replays the setting with.
        var atForty = FrozenMoved(IndexRules.Provisional(evaluator), (IndexRuleCandidate.LargeBreadthParameter, 0), (IndexRuleCandidate.BreadthFloorParameter, 0.4));
        var row = StandingRow(FamilyRecords.LivePrefix + "S&P 600 breakout rule", "breakout-600", atForty);
        var walked = RuleTests.ThroughOwnSweep(testInputs, BreakoutRule.Name, row, evaluator, atForty);
        var (at, refused) = RuleReplay.OwnSwing(breakout, evaluator, atForty);

        Assert.Null(refused);
        Assert.Null(walked.Why);
        Assert.Equal(RuleTests.Live, walked.Standing);

        var expected = breakout.TradesUnder(at!.Setting, at.Levels).Select(trade => (trade.Listing.Session, new OwnProcedures.Walked(trade.Listing.Session, null, trade).Edge)).ToArray();
        var breakoutSession = Array.IndexOf(laid.Calendar, new DateOnly(2019, 2, 1));

        Assert.Equal(expected, walked.Trades);
        Assert.Equal(breakoutSession, Assert.Single(walked.Trades!).Entry);
        Assert.NotNull(Assert.Single(walked.Trades!).Edge);
        Assert.True(walked.Cap > 0);

        // The same setting at its own breadth at 50 per cent, the half exactly, still lists it; and a level its sweep's second
        // stage never read, HYG above its average, is kept with why.
        var atHalf = FrozenMoved(atForty, (IndexRuleCandidate.BreadthFloorParameter, 0.5));

        Assert.Single(RuleTests.ThroughOwnSweep(testInputs, BreakoutRule.Name, StandingRow(row.Candidate, "breakout-600", atHalf), evaluator, atHalf).Trades!);
        Assert.Equal(
            $"its {IndexRuleCandidate.CreditAverageParameter} at 1 is a level its replay reads none of",
            RuleTests.ThroughOwnSweep(testInputs, BreakoutRule.Name, StandingRow(row.Candidate, "breakout-600", FrozenMoved(atForty, (IndexRuleCandidate.CreditAverageParameter, 1))), evaluator, FrozenMoved(atForty, (IndexRuleCandidate.CreditAverageParameter, 1))).Why);

        // A pullback at the S&P 600's frozen setting, the profit and cover check among its levels, walked by its search's own
        // reading with its units the picks the search's own walk kept.
        var frozenPullback = FrozenParameters(RemedyFreezes[4].Parameters);
        var searched = RuleTests.ThroughOwnSearch(pullback, IndexEvaluator("pullback-600"), StandingRow(FamilyRecords.LivePrefix + "S&P 600 pullback rule", "pullback-600", frozenPullback), frozenPullback);

        Assert.Null(searched.Why);
        Assert.NotNull(searched.Trades);

        // From 21.16 the engines read the same pullback over the same reading, each night's picks in the night's own order,
        // five a night with one open trade a stock: no night of the constructed history holds more than five, so its own
        // walk keeps the trades the per-rule test's does, each at the same edge.
        // see: Every change the engines propose is one its family's night reads, so each is put to the operator
        var pullbackRow = StandingRow(FamilyRecords.LivePrefix + "S&P 600 pullback rule", "pullback-600", frozenPullback);
        var (engine, engineWhy) = RuleTests.LivePullbackWalk(laid, [], pullbackRow, _ => { }, null, null, null!, pullback);

        Assert.Null(engineWhy);

        var engineUnits = engine!.Own.Select(one => (one.Entry, one.Edge)).OrderBy(unit => unit.Entry).ToArray();

        Assert.Equal(searched.Trades!.Select(unit => unit.Entry), engineUnits.Select(unit => unit.Entry));
        Assert.All(searched.Trades!.Zip(engineUnits), pair =>
        {
            Assert.Equal(pair.First.Edge is null, pair.Second.Edge is null);
            Assert.Equal(pair.First.Edge ?? 0, pair.Second.Edge ?? 0, 6);
        });

        // From 21.16 the graded exposure over the same reading with its market check off: five a night where both switches
        // hold keeps the same trades, as many as the night allows where one holds, none where neither.
        // see: The pullback's and the dip's graded exposure stand in their market check's place as a variant, walked as the breakout's and the drift's are
        var pullbackEvaluator = IndexEvaluator("pullback-600");

        Assert.Equal(searched.Trades, RuleTests.GradedOwnPullback(pullback, pullbackEvaluator, frozenPullback, (_, _) => RuleHooks.ExposureFull));
        Assert.Empty(RuleTests.GradedOwnPullback(pullback, pullbackEvaluator, frozenPullback, (_, _) => 0)!);

        var half = RuleTests.GradedOwnPullback(pullback, pullbackEvaluator, frozenPullback, (_, _) => RuleHooks.ExposureHalf)!;

        Assert.All(half.GroupBy(unit => unit.Entry), night => Assert.True(night.Count() <= RuleHooks.ExposureHalf));
        Assert.Subset(searched.Trades!.ToHashSet(), half.ToHashSet());

        // A book off its design's six settings, design (a) at a look-back of 63 sessions with one leader a sector, walked at
        // its own setting with its months the record command's own replay of it; twelve months' strength, which the record
        // command replays at no setting, walked by the six settings' own reader at the registration's quality and floors, a
        // month a row; and design (b) at twelve months' strength off the base window kept with why.
        var bookRow = StandingRow(FamilyRecords.LivePrefix + "S&P 600 heavyweights rule", "heavyweight-600", FrozenParameters(RemedyFreezes[7].Parameters));
        var book = RuleTests.BookOffTheSix(lay, bookRow, FrozenParameters(RemedyFreezes[7].Parameters), designA: true);
        var (bookAt, bookRefused) = RuleReplay.OwnBook(FrozenParameters(RemedyFreezes[7].Parameters));

        Assert.Null(bookRefused);
        Assert.Null(book.Why);
        Assert.Equal(lay.BookFrom(lay.RebalancesA(bookAt!.Setting!, bookAt.Quality, bookAt.Floors), bookAt.Setting!.Exit, bookAt.Key).Months, book.Months);

        // A month's edge runs to the next month's first session, so the last month laid has no row.
        Assert.Equal(lay.Months.Count - 1, book.Months!.Count);

        var twelveOne = FrozenMoved(FrozenParameters(RemedyFreezes[7].Parameters), (IndexHeavyweightCandidate.TwelveOneParameter, 1));
        var twelveOneBook = RuleTests.BookOffTheSix(lay, bookRow, twelveOne, designA: true);

        Assert.Null(twelveOneBook.Why);
        Assert.Equal(lay.Months.Count - 1, twelveOneBook.Months!.Count);

        var offTheBase = FrozenMoved(twelveOne, (IndexHeavyweightCandidate.DesignParameter, IndexHeavyweightCandidate.DesignB), (IndexHeavyweightCandidate.WindowParameter, 126), (IndexHeavyweightCandidate.IndustriesParameter, 10), (IndexHeavyweightCandidate.MembersParameter, 1));

        Assert.Equal(RuleTests.TwelveOneOffTheBase, RuleTests.BookOffTheSix(lay, bookRow, offTheBase, designA: false).Why);
    }
}
