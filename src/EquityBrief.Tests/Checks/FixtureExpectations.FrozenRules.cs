using System.Globalization;
using EquityBrief.Core.Candidates;
using EquityBrief.Core.Families;
using EquityBrief.Core.Time;
using EquityBrief.Data;
using EquityBrief.Tests.Harness;
using EquityBrief.Worker.Candidates;
using EquityBrief.Worker.Indices;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Tests.Checks;

// fixture-expectations, 20.11: the S&P 400's and 600's frozen rules read on the night as their own sweeps read them, each
// worked by hand over a constructed night: the market check at the floor a rule states, the index's own breadth or the S&P
// 500's or none, its listings read with the night open so the check is read once; a pullback whose own dial is off read at
// its rule's floor; an S&P 400 drift stating the entry bought on the first close under the close before it within ten
// sessions of its reaction; the S&P 600 drift's revenue read only once its quarters were filed before the session before the night, its
// trades held 63 sessions; and the freeze form keeping the rule of the day before in the background and the variants
// standing before it beside the rules it registers.
// see: Each frozen rule on the S&P 400 and 600 reads its market check at the level its own sweep chose
// see: A drift rule on the S&P 400 or 600 stating the first pullback is bought on the night through the one function its search's entry level reads
// see: A drift's revenue level on the S&P 400 and 600 reads a quarter only once its filing is stored, on the night and in its sweep alike
// see: A freeze on the S&P 400 or 600 is refused only where the family's live rule stands, the variants standing before it kept beside it
public partial class FixtureExpectations
{
    static string FiredOf(IReadOnlyList<IndexAnswer> answers) =>
        string.Join(",", answers.Where(answer => answer.Passed).OrderBy(answer => answer.Place).Select(answer => answer.Ticker));

    static IReadOnlyDictionary<string, double> Moved(IReadOnlyDictionary<string, double> from, params (string Name, double Value)[] moves)
    {
        var moved = new Dictionary<string, double>(from, StringComparer.Ordinal);

        foreach (var (name, value) in moves)
        {
            moved[name] = value;
        }

        return moved;
    }

    [Fact]
    public async Task AFrozenRulesMarketCheckReadsTheBreadthItStatesAtItsOwnFloor()
    {
        var breakout = IndexEvaluator("breakout-400");
        var provisional = IndexRules.Provisional(breakout);
        var night = DateOnly.ParseExact(IndexNight, "yyyy-MM-dd", CultureInfo.InvariantCulture);

        using var store = ClosedIndexStore();
        await using var connection = new SqliteConnection(StoreConnection.For(store.DatabaseFile));
        await connection.OpenAsync();

        var (names, income) = await IndexFamilies.InputsAsync(connection, "MID", night, default);
        var inputs = IndexNightRead.Prepare("MID", night, names, income)!;

        async Task<IReadOnlyList<IndexAnswer>> AnswersAsync(double breadth, double floor)
        {
            var levels = await IndexFamilies.LevelsAsync(connection, inputs, default);
            var parameters = Moved(provisional, (IndexRuleCandidate.LargeBreadthParameter, breadth), (IndexRuleCandidate.BreadthFloorParameter, floor));

            Assert.Null(IndexRules.Refusal(breakout, parameters));

            return IndexNightRead.Answers(inputs, BreakoutRule.Name, IndexRules.Read(inputs, new IndexRule("rule", breakout, parameters, FreezeAt), levels));
        }

        // Seven of the seventeen members above their averages, a breadth of 7/17, 41.2 per cent: the check on the index's
        // own breadth open at 35 and at 40 per cent, listing IA, IC, ID and IB in the order of their volume, 2.0, 1.8, 1.7
        // and 1.6 times, and closed at 45 and at 50, every member then read as the market check closed.
        Assert.Equal(7.0 / 17.0, inputs.Breadth!.Value, 9);
        Assert.Equal("IA,IC,ID,IB", FiredOf(await AnswersAsync(0, 0.35)));
        Assert.Equal("IA,IC,ID,IB", FiredOf(await AnswersAsync(0, 0.40)));
        Assert.All(await AnswersAsync(0, 0.45), answer => Assert.Equal(IndexNightRead.MarketClosed, answer.Reason));
        Assert.All(await AnswersAsync(0, 0.50), answer => Assert.Equal(IndexNightRead.MarketClosed, answer.Reason));

        // The check its own part, its words the breadth against the floor the rule states.
        var open = (await AnswersAsync(0, 0.40)).Single(answer => answer.Ticker == "IA").Gates![0];
        var closed = (await AnswersAsync(0, 0.45)).Single(answer => answer.Ticker == "IA").Gates![0];

        Assert.Equal((true, "the index's own breadth 41.2% at or above its floor of 40%"), (open.Passed, open.Reason));
        Assert.Equal((false, "the index's own breadth 41.2% below its floor of 45%"), (closed.Passed, closed.Reason));

        // On the S&P 500's breadth at its filter's floor of 45 per cent: open at a stored breadth of 50 per cent, closed at
        // 44, whatever the index's own; and a rule reading none lists on the closed night.
        store.Execute(
            "INSERT INTO market_reading (session_date, members, counted, above, breadth, counted_context, above_context, breadth_context, volume_counted, median_volume_ratio) " +
            $"VALUES ('{IndexNight}', 100, 100, 50, 0.50, 100, 50, 0.50, 100, 1.0);");

        Assert.Equal("IA,IC,ID,IB", FiredOf(await AnswersAsync(1, 0.45)));

        store.Execute($"UPDATE market_reading SET breadth = 0.44 WHERE session_date = '{IndexNight}';");

        var large = await AnswersAsync(1, 0.45);

        Assert.All(large, answer => Assert.Equal(IndexNightRead.MarketClosed, answer.Reason));
        Assert.Equal("the S&P 500's breadth 44.0% below its floor of 45%", large.Single(answer => answer.Ticker == "IA").Gates![0].Reason);
        Assert.Equal("IA,IC,ID,IB", FiredOf(await AnswersAsync(IndexRuleCandidate.NoMarketCheck, 0)));

        // The floor's values the form admits: one of the four on the index's own breadth, 0.45 on the S&P 500's and nought
        // reading none, each other refused with what it must be.
        Assert.Contains("'breadthFloor' is 0.3, and it must be one of 0.35, 0.4, 0.45, 0.5", IndexRules.Refusal(breakout, Moved(provisional, (IndexRuleCandidate.BreadthFloorParameter, 0.30))), StringComparison.Ordinal);
        Assert.Contains("'breadthFloor' is 0.4, and it must be 0.45, the S&P 500 filter's own floor", IndexRules.Refusal(breakout, Moved(provisional, (IndexRuleCandidate.LargeBreadthParameter, 1), (IndexRuleCandidate.BreadthFloorParameter, 0.40))), StringComparison.Ordinal);
        Assert.Contains("'breadthFloor' is 0.45, and it must be 0, since a rule reading no market check reads no floor", IndexRules.Refusal(breakout, Moved(provisional, (IndexRuleCandidate.LargeBreadthParameter, IndexRuleCandidate.NoMarketCheck))), StringComparison.Ordinal);
        Assert.Contains("reads 'breadthFloor', and the registration states none", IndexRules.Refusal(breakout, provisional.Where(pair => pair.Key != IndexRuleCandidate.BreadthFloorParameter).ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal)), StringComparison.Ordinal);

        // And the rule's words name a floor other than 45 per cent.
        Assert.EndsWith(", the profit gate, its market check on the index's own breadth at 40%", IndexRules.Words(breakout, Moved(provisional, (IndexRuleCandidate.BreadthFloorParameter, 0.40))), StringComparison.Ordinal);
    }

    [Fact]
    public void APullbackWithItsOwnMarketDialOffReadsItsCheckAtTheFloorItsRuleStates()
    {
        // The business-first history's night with its breadth read at 0.40: the provisional pullback, its own dial and its
        // check at 45 per cent, lists none; the frozen S&P 400 pullback's form, its own dial off and its check on the index's
        // own breadth at 35 per cent, lists the setups the base lists on the open night, AAA, BBB and CCC, in that order.
        var inputs = BusinessFirstHistory.Inputs(BusinessFirstHistory.Night + 1);
        var prepared = IndexNightRead.Prepare(IndexFamilies.LargeIndex, inputs.Sessions[^1], inputs.Names, BusinessFirstHistory.Income())!;
        var closed = prepared with { Sessions = [.. prepared.Sessions.Select((one, at) => at == prepared.At ? one with { Breadth = 0.40 } : one)] };
        var pullback = IndexEvaluator("pullback-400");
        var provisional = IndexRules.Provisional(pullback);
        var own = Moved(provisional, (IndexPullbackCandidate.MarketParameter, 0), (IndexRuleCandidate.BreadthFloorParameter, 0.35));

        IReadOnlyList<IndexAnswer> Answers(IndexNightInputs on, IReadOnlyDictionary<string, double> parameters) =>
            IndexNightRead.Answers(on, SetupFamilies.Pullback, IndexRules.Read(on, new IndexRule("rule", pullback, parameters, FreezeAt), IndexLevels.None));

        Assert.Null(IndexRules.Refusal(pullback, own));
        Assert.Equal(["AAA", "BBB", "CCC"], FiredOf(Answers(prepared, provisional)).Split(',').Order(StringComparer.Ordinal));
        Assert.Equal(FiredOf(Answers(prepared, provisional)), FiredOf(Answers(closed, own)));
        Assert.Equal("the index's own breadth 40.0% at or above its floor of 35%", Answers(closed, own).Single(answer => answer.Ticker == "AAA").Gates![0].Reason);
        Assert.All(Answers(closed, provisional), answer => Assert.Equal(IndexNightRead.MarketClosed, answer.Reason));

        // A pullback whose own dial is on reads it at its check's floor, so one floor is read; the frozen form's words name
        // the dial off and the check's floor.
        Assert.Contains("a pullback whose own market dial is on reads it at its market check's floor, and 'market' is 0.45 where 'breadthFloor' is 0.35", IndexRules.Refusal(pullback, Moved(provisional, (IndexRuleCandidate.BreadthFloorParameter, 0.35))), StringComparison.Ordinal);
        Assert.Contains("its own market floor off and band strength 0, the profit gate, its market check on the index's own breadth at 35%", IndexRules.Words(pullback, own), StringComparison.Ordinal);
    }

    // A year of bars ending on the index's night, a calendar day a session, closes near 99 and then 100, a print reported
    // before the open two sessions before the night beating its estimate by 5 per cent, the reaction closing at 101 from 100
    // on 2.5 times the volume with its low at 100, and the two sessions after closing as given.
    static IReadOnlyList<FamilyBar> ReactedTwoBack(decimal after, decimal tonight)
    {
        var night = DateOnly.ParseExact(IndexNight, "yyyy-MM-dd", CultureInfo.InvariantCulture);

        return
        [
            .. Enumerable.Range(0, 252).Select(index => index switch
            {
                < 211 => new FamilyBar(night.AddDays(index - 251), 100m, 98m, 99m, 1_000_000),
                < 231 => new FamilyBar(night.AddDays(index - 251), 101m, 99m, 100m, 1_000_000),
                < 249 => new FamilyBar(night.AddDays(index - 251), 100.5m, 99.5m, 100m, 1_000_000),
                249 => new FamilyBar(night.AddDays(-2), 101.5m, 100m, 101m, 2_500_000),
                250 => new FamilyBar(night.AddDays(-1), Math.Max(after, 101m) + 0.3m, after - 0.3m, after, 1_000_000),
                _ => new FamilyBar(night, Math.Max(tonight, after) + 0.3m, tonight - 0.3m, tonight, 1_000_000),
            }),
        ];
    }

    static TemporaryStore DriftStore(string index, params (string Ticker, IReadOnlyList<FamilyBar> Year, string Reported)[] members)
    {
        var store = new TemporaryStore().Migrated();

        foreach (var (ticker, year, reported) in members)
        {
            StoreYear(store, ticker, year);
            store.Execute($"INSERT INTO membership (index_code, ticker, joined, \"left\", observed_at) VALUES ('{index}', '{ticker}', '2024-01-02', NULL, '2024-01-02T00:00:00Z');");
            store.Execute($"INSERT INTO calendar (ticker, event_date, kind, timing, detail, observed_at) VALUES ('{ticker}', '{reported}', 'earnings', 'before', '{{\"surprise\":\"5.0\"}}', '{reported}T12:00:00Z');");
            Quarters(store, ticker, 10m, 10m, 10m, 10m);
        }

        return store;
    }

    [Fact]
    public async Task AnSAndP400DriftStatingTheEntryIsBoughtOnTheFirstCloseUnderTheCloseBeforeItWithinTenSessionsOfItsReaction()
    {
        var drift = IndexEvaluator("drift-400");
        var night = DateOnly.ParseExact(IndexNight, "yyyy-MM-dd", CultureInfo.InvariantCulture);
        var reported = night.AddDays(-2).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        // Each reacts two sessions before the night: D1 closes up the session after, at 102, and falls on the night to 101.6,
        // its first pullback; D2 falls the session after, to 100.8, its first pullback before the night, and again on it;
        // D3 closes the night where it closed the session before, at 102, no fall; D4 falls on the night to 99.5, through its
        // stop. The stop floor puts each stop one typical move under the reaction's close of 101 where that sits under the
        // reaction's low of 100: Wilder's move of 2 over the ranges of 2 falls over 18 sessions of ranges of 1 to
        // 1 + (13/14)^18, about 1.264, and the reaction's range of 1.5 takes it to about 1.280, a stop near 99.72.
        using var store = DriftStore(
            "MID",
            ("D1", ReactedTwoBack(102m, 101.6m), reported),
            ("D2", ReactedTwoBack(100.8m, 100.7m), reported),
            ("D3", ReactedTwoBack(102m, 102m), reported),
            ("D4", ReactedTwoBack(102m, 99.5m), reported));
        await using var connection = new SqliteConnection(StoreConnection.For(store.DatabaseFile));
        await connection.OpenAsync();

        var (names, income) = await IndexFamilies.InputsAsync(connection, "MID", night, default);
        var inputs = IndexNightRead.Prepare("MID", night, names, income)!;
        var levels = await IndexFamilies.LevelsAsync(connection, inputs, default);

        // An S&P 400 drift stating the entry, at the setting the operator's table of 2026-10-20 named before the 17.1
        // correction: the window 10 sessions, a rise of half a typical move on 1.25 times the volume, the target three times
        // the risk, the stop at least one typical move under the listing's close, its market check on the index's own
        // breadth at 45 per cent, and the entry on the first pullback.
        var frozen = Moved(
            IndexRules.Provisional(drift),
            (IndexDriftCandidate.WindowSessionsParameter, 10),
            (IndexDriftCandidate.VolumeMultipleParameter, 1.25),
            (IndexDriftCandidate.TargetMultipleParameter, 3),
            (IndexDriftCandidate.StopFloorParameter, 1),
            (IndexDriftCandidate.EntryParameter, 1));

        Assert.Null(IndexRules.Refusal(drift, frozen));

        IReadOnlyList<IndexAnswer> Answers(IReadOnlyDictionary<string, double> parameters) =>
            IndexNightRead.Answers(inputs, DriftRule.Name, IndexRules.Read(inputs, new IndexRule("rule", drift, parameters, FreezeAt), levels));

        // Bought at the listing's close the same setting lists D1, D2 and D3, each still above its reaction's low, in the
        // ticker's order on one surprise; on its first pullback it lists D1 alone, at the night's close of 101.6.
        Assert.Equal("D1,D2,D3", FiredOf(Answers(Moved(frozen, (IndexDriftCandidate.EntryParameter, 0)))));

        var entered = Answers(frozen);

        Assert.Equal("D1", FiredOf(entered));
        Assert.Equal((IndexNightRead.NoSetup, IndexNightRead.NoSetup, IndexNightRead.NoSetup), (entered.Single(answer => answer.Ticker == "D2").Reason, entered.Single(answer => answer.Ticker == "D3").Reason, entered.Single(answer => answer.Ticker == "D4").Reason));

        // Its stop the one its listing on the reaction set, the reaction's low or one typical move under the reaction's close
        // where that sits lower, and its target the nearer of the band and three times the risk from the night's close.
        var d1 = Array.FindIndex(inputs.Series, one => one.Name.Ticker == "D1");
        var reaction = Array.BinarySearch(inputs.Series[d1].SessionAt, inputs.At - 2);
        var stop = Math.Min(100.0, 101.0 - inputs.Series[d1].Atr[reaction]);
        var trade = entered.Single(answer => answer.Ticker == "D1");

        Assert.InRange(stop, 99.71, 99.73);
        Assert.Equal(101.6m, trade.Entry);
        Assert.InRange((double)trade.Stop!.Value, stop - 0.0001, stop + 0.0001);
        Assert.True((double)trade.Target!.Value <= 101.6 + (3 * (101.6 - stop)) + 0.0001);
        Assert.True(trade.Target!.Value > trade.Entry);
        Assert.Equal(DriftRule.CapSessions, trade.Cap);

        // The words name the entry, as a card's rule stating it does.
        Assert.Contains("bought on its first close under the close before it within 10 sessions of the reaction, its stop where its listing set it", IndexRules.Words(drift, frozen), StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheSAndP600sFrozenDriftReadsItsPrintsRevenueOnlyOnceItsQuartersWereStoredAndHoldsSixtyThreeSessions()
    {
        var drift = IndexEvaluator("drift-600");
        var night = DateOnly.ParseExact(IndexNight, "yyyy-MM-dd", CultureInfo.InvariantCulture);

        // Each reacts on 2026-10-01, the session before the night, to a print reported before that day's open: R1's quarter
        // to 2026-06-30 filed on 2026-08-05 at 110 against 100 a year before, grown 10 per cent; R2's the same quarter filed
        // on 2026-10-01, the session before the night, which the night's refresh had not stored; R3's at 90, fallen 10 per
        // cent; and R4 with no filer known.
        using var store = DriftStore(
            "SML",
            ("R1", ReactedYear(), "2026-10-01"),
            ("R2", ReactedYear(), "2026-10-01"),
            ("R3", ReactedYear(), "2026-10-01"),
            ("R4", ReactedYear(), "2026-10-01"));

        foreach (var (ticker, cik, now, filed) in new[] { ("R1", "0000000101", "110", "2026-08-05"), ("R2", "0000000102", "110", "2026-10-01"), ("R3", "0000000103", "90", "2026-08-05") })
        {
            store.Execute($"INSERT INTO company (ticker, fetched_at, cik) VALUES ('{ticker}', '2026-08-01T00:00:00Z', '{cik}');");
            store.Execute(
                "INSERT INTO filed_fact (cik, concept, period_start, period_end, dollars, filed, form, accession, run_id) VALUES " +
                $"('{cik}', 'Revenues', '2026-04-01', '2026-06-30', '{now}', '{filed}', '10-Q', '{cik}-26-1', 'r'), " +
                $"('{cik}', 'Revenues', '2025-04-01', '2025-06-30', '100', '2025-08-06', '10-Q', '{cik}-25-1', 'r');");
        }

        await using var connection = new SqliteConnection(StoreConnection.For(store.DatabaseFile));
        await connection.OpenAsync();

        var (names, income) = await IndexFamilies.InputsAsync(connection, "SML", night, default);
        var inputs = IndexNightRead.Prepare("SML", night, names, income)!;
        var levels = await IndexFamilies.LevelsAsync(connection, inputs, default, null, revenue: true);

        // The frozen S&P 600 drift as its sweep run again with the night's timing answered it: the window 3 sessions, a rise
        // of half a typical move on 1.25 times the volume, the target 2.5 times the risk, the stop floored at one typical
        // move, no market check, held 63 sessions, and the revenue grown on the year.
        var frozen = Moved(
            IndexRules.Provisional(drift),
            (IndexDriftCandidate.VolumeMultipleParameter, 1.25),
            (IndexDriftCandidate.StopFloorParameter, 1),
            (IndexRuleCandidate.HoldParameter, 63),
            (IndexRuleCandidate.LargeBreadthParameter, IndexRuleCandidate.NoMarketCheck),
            (IndexRuleCandidate.BreadthFloorParameter, 0),
            (IndexDriftCandidate.RevenueGrewParameter, 1));

        Assert.Null(IndexRules.Refusal(drift, frozen));

        var answers = IndexNightRead.Answers(inputs, DriftRule.Name, IndexRules.Read(inputs, new IndexRule("rule", drift, frozen, FreezeAt), levels))
            .ToDictionary(answer => answer.Ticker, StringComparer.Ordinal);

        // R1 alone, held 63 sessions; R2, R3 and R4 each kept off by the revenue, saying what it read.
        Assert.Equal("R1", FiredOf([.. answers.Values]));
        Assert.Equal(63, answers["R1"].Cap);
        Assert.All(new[] { "R2", "R3", "R4" }, ticker => Assert.Equal(IndexRules.RevenueNotAbove, answers[ticker].Reason));

        static string RevenueWords(IndexAnswer answer) => answer.Gates!.Single(gate => !gate.Passed).Reason;

        Assert.Equal("its print's quarter and the same quarter a year before not both stored as first filed before tonight", RevenueWords(answers["R2"]));
        Assert.Equal("revenue -10.0% on the same quarter a year before, both as first filed", RevenueWords(answers["R3"]));
        Assert.Equal("no quarter of its revenue stored as first filed before tonight", RevenueWords(answers["R4"]));
    }

    [Fact]
    public async Task AFreezeKeepsTheRuleOfTheDayBeforeInTheBackgroundAndTheVariantsStandingBeforeItBesideIt()
    {
        using var store = new TemporaryStore().Migrated();
        var heavyweights = IndexEvaluator("heavyweight-600");
        var provisional = IndexRules.Provisional(heavyweights);
        var at = new DateTimeOffset(2026, 10, 12, 2, 49, 49, TimeSpan.Zero);

        // The S&P 600's two books of 17.11, registered beside its provisional rule before any freeze.
        Assert.Equal(0, (await RegisterVerbAt(store, at, RegisterVerb.IndexVariants, HeavyweightRule.Name, "--index", "SML", "--variants", "twelveOne=1;quality=2")).Code);

        // Frozen at design (a)'s own answer, quality off, 63 sessions, one leader, sold on a break: refused at a setting one
        // of the standing books states, and then written with the books standing on beside it and the provisional book kept
        // in the background, the family four of nine.
        var live = Moved(
            provisional,
            (IndexRuleCandidate.QualityParameter, 0),
            (IndexHeavyweightCandidate.LookBackParameter, 63),
            (IndexHeavyweightCandidate.LeadersParameter, 1),
            (IndexHeavyweightCandidate.LeadingExitParameter, 0),
            (IndexHeavyweightCandidate.AverageExitParameter, 1));

        Assert.Contains(
            "variant 1 states the settings of a rule standing in the family",
            (await RegisterVerbAt(store, at.AddDays(8), RegisterVerb.IndexFamily, HeavyweightRule.Name, "--index", "SML", "--parameters", Typed(live), "--variants", "lookBack=251,leaders=2,soldOnLeading=1,soldUnderAverage=0,quality=2")).Said,
            StringComparison.Ordinal);

        var (code, said) = await RegisterVerbAt(store, at.AddDays(8).AddSeconds(1), RegisterVerb.IndexFamily, HeavyweightRule.Name, "--index", "SML", "--parameters", Typed(live));

        Assert.Equal(0, code);
        Assert.Contains("registered 2 at one instant, family of 4 of 9", said, StringComparison.Ordinal);

        var standing = CandidateFamily.In(CandidateFamily.Standing(await new CandidateRegistrar(FixedClock.At(at.AddDays(9), SessionZones.UnitedStates), store.DatabaseFile).RowsAsync(), at.AddDays(9)), "heavyweight on the S&P 600");

        Assert.Equal(4, standing.Count);
        Assert.Single(standing, row => FamilyRecords.IsLive(row.Candidate));
        Assert.Equal(FamilyRecords.BackgroundPrefix + "S&P 600 " + IndexRules.Words(heavyweights, provisional), Assert.Single(standing, row => FamilyRecords.IsBackground(row.Candidate)).Candidate);
        Assert.Equal(IndexRules.BackgroundTest, standing.Single(row => FamilyRecords.IsBackground(row.Candidate)).Test);
        Assert.Equal(2, standing.Count(row => !FamilyRecords.IsLive(row.Candidate) && !FamilyRecords.IsBackground(row.Candidate)));

        // A freeze while its live rule stands is refused; one at the provisional settings keeps no rule in the background,
        // that rule being the one frozen.
        Assert.Contains("live rule", (await RegisterVerbAt(store, at.AddDays(10), RegisterVerb.IndexFamily, HeavyweightRule.Name, "--index", "SML", "--parameters", Typed(live))).Said, StringComparison.Ordinal);

        var (frozenAtToday, _) = IndexRules.Freeze(HeavyweightRule.Name, "MID", IndexRules.Provisional(IndexEvaluator("heavyweight-400")), [], [], at);

        Assert.Single(frozenAtToday!);
    }

    [Fact]
    public void ARulesStandReadsFromTheRegistrationThatOpenedItAndARegistrationAgainInOneSecondBreaksNothing()
    {
        RegisterRow Row(long id, string? evidence, string @event, DateTimeOffset at) =>
            new(id, @event == CandidateFamily.Registered ? "the background S&P 400 rule" : "the retirement", "rule", "test", "breakout-400", "{}", "v", @event, @event == CandidateFamily.Retired ? "the background S&P 400 rule" : null, at, evidence);

        var registered = new DateTimeOffset(2026, 10, 21, 11, 15, 0, TimeSpan.Zero);
        var moved = new DateTimeOffset(2026, 11, 10, 22, 0, 0, TimeSpan.Zero);

        // Registered on Wednesday 2026-10-21, its stand from that session; registered again unchanged with its retirement in
        // one second on 2026-11-10, as a moved rule is, still from 2026-10-21; retired on its own on 2026-11-12 and registered
        // again on 2026-11-13, from 2026-11-13.
        IReadOnlyList<RegisterRow> rows = [Row(1, null, CandidateFamily.Registered, registered)];

        Assert.Equal(new DateOnly(2026, 10, 21), FamilyRecords.StoodSince(rows, "the background S&P 400 rule"));

        rows = [.. rows, Row(2, "moved", CandidateFamily.Retired, moved), Row(3, null, CandidateFamily.Registered, moved)];

        Assert.Equal(new DateOnly(2026, 10, 21), FamilyRecords.StoodSince(rows, "the background S&P 400 rule"));

        rows = [.. rows, Row(4, "retired", CandidateFamily.Retired, new DateTimeOffset(2026, 11, 12, 12, 0, 0, TimeSpan.Zero)), Row(5, null, CandidateFamily.Registered, new DateTimeOffset(2026, 11, 13, 12, 0, 0, TimeSpan.Zero))];

        Assert.Equal(new DateOnly(2026, 11, 13), FamilyRecords.StoodSince(rows, "the background S&P 400 rule"));
    }
}
