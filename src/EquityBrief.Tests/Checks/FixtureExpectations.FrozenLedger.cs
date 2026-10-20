using System.Globalization;
using EquityBrief.Core.Candidates;
using EquityBrief.Core.Families;
using EquityBrief.Core.Time;
using EquityBrief.Worker.Candidates;
using EquityBrief.Worker.Indices;
using EquityBrief.Worker.Ledger;

namespace EquityBrief.Tests.Checks;

// fixture-expectations, 20.11: the setup ledger reads each S&P 400 and 600 family's frozen live rule where one stands.
// Each family kind is worked by hand over a constructed store with a frozen rule standing: every setup the family's
// loose gates pass carries the live rule's words, and its live pass is the rule's own pass as the night reads it, the
// market check at the rule's own level, its floors and its quality among the parts; the drift, whose rule buys on the
// first close under the close before it within ten sessions of its reaction, is passed on its reaction's row by the night
// that buys it, its revenue read only once its filing is stored. The ledger reads the register at the instant the
// night's own read of it was taken, and the history build, reading it as it stands when the build runs, writes the
// same rule and the same passes on the same session over a copy of the same store. A family with no live rule keeps its
// provisional reading.
// see: The setup ledger reads each S&P 400 and 600 family's frozen live rule where one stands, and a card draws setups like a pick only once its history is built under that rule
public partial class FixtureExpectations
{
    // One store's statements written in one transaction.
    static void FrozenBatch(TemporaryStore store, IEnumerable<string> statements)
    {
        using var connection = store.Open();
        using var transaction = connection.BeginTransaction();

        foreach (var sql in statements)
        {
            using var command = connection.CreateCommand();

            command.Transaction = transaction;
            command.CommandText = sql;
            command.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    static string FrozenBar(string ticker, DateOnly session, decimal open, decimal high, decimal low, decimal close, long volume) =>
        FormattableString.Invariant($"INSERT INTO bar (ticker, session_date, open, high, low, close, volume, source, observed_at, raw_close) VALUES ('{ticker}', '{Day(session)}', '{open}', '{high}', '{low}', '{close}', {volume}, 'constructed', '{Day(session)}T21:00:00Z', '{close}');");

    static string FrozenMember(string index, string ticker) =>
        $"INSERT INTO membership (index_code, ticker, joined, \"left\", observed_at) VALUES ('{index}', '{ticker}', '2020-01-02', NULL, '2020-01-02T00:00:00Z');";

    static string FrozenPulledMember(string ticker) =>
        $"INSERT INTO pulled_member (index_code, ticker, exchange, pull) VALUES ('MID', '{ticker}', 'US', 'members-pull-test');";

    // A quarter as the night's fetch filed it and as the history's pull filed it, its net and its operating income 10.
    static IEnumerable<string> FrozenQuarter(string ticker, DateOnly end, DateOnly filed, string interest = "NULL") =>
    [
        "INSERT INTO reported_quarter (ticker, fetched_at, session_date, period_end, filing_date, net_income, operating_income) "
            + $"VALUES ('{ticker}', '{Day(filed)}T22:00:00Z', '{Day(filed)}', '{Day(end)}', '{Day(filed)}', '10', '10');",
        "INSERT INTO pulled_income (ticker, period_end, filing_date, net_income, operating_income, interest_expense, pull) "
            + $"VALUES ('{ticker}', '{Day(end)}', '{Day(filed)}', '10', '10', {interest}, 'income-pull-test');",
    ];

    // The rows a family's setups of a session hold on an index, by ticker: the rule's words, its pass and the source.
    static IReadOnlyList<string> FrozenRows(TemporaryStore store, string index, string family, DateOnly session, string source) =>
        FamilyRows(store, $"SELECT ticker, rule, live_pass FROM setup WHERE index_code = '{index}' AND family = '{family}' AND session_date = '{Day(session)}' AND source = '{source}' ORDER BY ticker;");

    static async Task FrozenRegister(TemporaryStore store, DateTimeOffset at, string family, IReadOnlyDictionary<string, double> live) =>
        Assert.Equal(0, (await RegisterVerbAt(store, at, RegisterVerb.IndexFamily, family, "--index", "MID", "--parameters", Typed(live))).Code);

    // The S&P 400 of the closed index families' store, its own breadth 7 of 17, under the provisional rule's 0.45; two
    // S&P 500 members a year of bars deep, LA closing above its 200-day average and LB under it, so the S&P 500's breadth
    // is 1 of 2, and the night's market reading stating it; each S&P 400 member's quarters as the history pulled them
    // beside those the night filed, PR's summing to a loss; and today's members as the history reads them.
    static TemporaryStore FrozenBreakoutStore()
    {
        var store = ClosedIndexStore();

        StoreYear(store, "LA", IndexYear(102m, 1_000_000));
        StoreYear(store, "LB", IndexYear(90m, 1_000_000));
        FrozenBatch(store,
        [
            FrozenMember("GSPC", "LA"),
            FrozenMember("GSPC", "LB"),
            "INSERT INTO market_reading (session_date, members, counted, above, breadth, counted_context, above_context, breadth_context, volume_counted, median_volume_ratio) "
                + $"VALUES ('{IndexNight}', 2, 2, 1, 0.5, 2, 1, 0.5, 2, 1.0);",
            "INSERT INTO pulled_income (ticker, period_end, filing_date, net_income, operating_income, interest_expense, pull) "
                + "SELECT ticker, period_end, filing_date, net_income, operating_income, NULL, 'income-pull-test' FROM reported_quarter;",
            "INSERT INTO pulled_member (index_code, ticker, exchange, pull) SELECT index_code, ticker, 'US', 'members-pull-test' FROM membership WHERE index_code = 'MID';",
        ]);

        return store;
    }

    // The S&P 400's breakout frozen at its own sweep's setting for the index: a close above the 251 sessions' high on 1.5
    // times the volume, ranges at 1 and the stop 3 typical moves beneath, the profit gate, and its market check on the S&P
    // 500's breadth at the S&P 500 filter's own floor of 0.45.
    static Dictionary<string, double> FrozenBreakout() => new(IndexRules.Provisional(IndexEvaluator("breakout-400")), StringComparer.Ordinal)
    {
        [IndexBreakoutCandidate.HighSessionsParameter] = 251,
        [IndexBreakoutCandidate.VolumeMultipleParameter] = 1.5,
        [IndexBreakoutCandidate.RangeCeilingParameter] = 1,
        [IndexBreakoutCandidate.StopMovesParameter] = 3,
        [IndexRuleCandidate.LargeBreadthParameter] = 1,
        [IndexRuleCandidate.BreadthFloorParameter] = 0.45,
    };

    [Fact]
    public async Task TheLedgerReadsTheFrozenBreakoutAsTheNightReadTheRegisterAndTheHistoryBuildWritesTheSameRuleAndPasses()
    {
        var night = DateOnly.ParseExact(IndexNight, "yyyy-MM-dd", CultureInfo.InvariantCulture);
        var live = FrozenBreakout();
        var words = IndexRules.Words(IndexEvaluator("breakout-400"), live);
        var frozenAt = new DateTimeOffset(2026, 10, 3, 12, 0, 0, TimeSpan.Zero);

        using var store = FrozenBreakoutStore();
        var ledger = new SetupLedger(FixedClock.At(new DateTimeOffset(2026, 10, 3, 15, 0, 0, TimeSpan.Zero), SessionZones.UnitedStates), store.DatabaseFile);

        // Six breakout setups on the night, every one above its 63 sessions' high of 101 at 102 on its volume or more with
        // its ranges half the older ones, each over half the index's dollar floor: FL at about $5.1 million a day, IA, IB,
        // IC and ID at about $100 million and PR; NH at 100.2 and the ten at 90 are none.
        string[] setups = ["FL", "IA", "IB", "IC", "ID", "PR"];

        // The night's index families read the register before the freeze, so the ledger reads the provisional breakout,
        // whose market check on the index's own breadth of 7 of 17 at 0.45 passes none of the six.
        await new IndexFamilies(FixedClock.At(new DateTimeOffset(2026, 10, 2, 23, 40, 0, TimeSpan.Zero), SessionZones.UnitedStates), store.DatabaseFile)
            .RunAsync("night-before", default, [], new DateTimeOffset(2026, 10, 2, 23, 30, 0, TimeSpan.Zero));
        await ledger.NightAsync("ledger-before");

        Assert.Equal([.. setups.Select(ticker => $"{ticker}|{LedgerSetups.BreakoutWords}|0")], FrozenRows(store, "MID", BreakoutRule.Name, night, SetupLedger.NightSource));

        // Frozen after that read, and the night's step run again with the night's row still as it was: the ledger reads the
        // register as the night read it, before the freeze, and its rows are the provisional rule's still.
        await FrozenRegister(store, frozenAt, BreakoutRule.Name, live);
        await ledger.NightAsync("ledger-again");

        Assert.Equal([.. setups.Select(ticker => $"{ticker}|{LedgerSetups.BreakoutWords}|0")], FrozenRows(store, "MID", BreakoutRule.Name, night, SetupLedger.NightSource));

        // The night run again reading the register as it stands, after the freeze: its page lists the frozen breakout's four
        // and the ledger reads the same rule. Its market check reads the S&P 500's breadth of 0.5, at or above 0.45, so it
        // is open where the index's own of 0.41 is closed; IA, IB, IC and ID clear the $10 million floor and four
        // profitable quarters, FL's $5.1 million is under the floor and PR's quarters sum to a loss.
        var register = await new CandidateRegistrar(FixedClock.At(frozenAt, SessionZones.UnitedStates), store.DatabaseFile).RowsAsync();
        var readAt = new DateTimeOffset(2026, 10, 3, 13, 0, 0, TimeSpan.Zero);

        await new IndexFamilies(FixedClock.At(readAt, SessionZones.UnitedStates), store.DatabaseFile).RunAsync("night-again", default, register, readAt);

        var outcome = await ledger.NightAsync("ledger-frozen");
        string[] passing = ["IA", "IB", "IC", "ID"];
        var rows = FrozenRows(store, "MID", BreakoutRule.Name, night, SetupLedger.NightSource);

        Assert.Equal([.. setups.Select(ticker => $"{ticker}|{words}|{(passing.Contains(ticker) ? 1 : 0)}")], rows);
        Assert.Equal(passing, FamilyRows(store, $"SELECT ticker FROM index_family_result WHERE index_code = 'MID' AND family = 'breakout' AND session_date = '{IndexNight}' AND passed = 1 ORDER BY ticker;"));
        Assert.Equal(["6|4"], FamilyRows(store, $"SELECT setups, live_passes FROM setup_night WHERE index_code = 'MID' AND family = 'breakout' AND session_date = '{IndexNight}';"));
        Assert.Equal(4, outcome.Indices.Single(index => index.Index == "MID").LivePasses);
        Assert.Contains("the breakout read at its frozen live rule as the register stood at 2026-10-03T13:00:00Z", SetupLedger.Detail(outcome.Indices), StringComparison.Ordinal);

        // The history build over a copy of the same store frozen the same way, reading the register as it stands when it
        // runs and the S&P 500's breadth as it stood from the S&P 500's own history: the same six setups on the night,
        // under the same words, the same four passing.
        using var copy = FrozenBreakoutStore();
        var output = new StringWriter();

        await FrozenRegister(copy, frozenAt, BreakoutRule.Name, live);
        Assert.Equal(0, await new SetupLedger(FixedClock.At(new DateTimeOffset(2026, 10, 3, 15, 0, 0, TimeSpan.Zero), SessionZones.UnitedStates), copy.DatabaseFile)
            .BuildAsync(copy.Root, "MID", night, night, output));

        Assert.Equal(rows, FrozenRows(copy, "MID", BreakoutRule.Name, night, SetupLedger.HistorySource));
        Assert.Contains("ledger-build: on the S&P 400, the breakout read at its frozen live rule as the register stood at 2026-10-03T15:00:00Z", output.ToString(), StringComparison.Ordinal);
        Assert.Contains("ledger-build: reading the S&P 500's history as it stood for its breadth", output.ToString(), StringComparison.Ordinal);
    }

    // The members falling a tenth of a point a session from 120, each under its 200-day average on every session that
    // average is read on.
    static readonly string[] FrozenDecliners = [.. Enumerable.Range(1, 10).Select(at => FormattableString.Invariant($"F{at}"))];

    // The constructed history the business-first family's tests read, on the S&P 400, its three strong members pullback
    // setups on its night, beside the ten decliners: seven of seventeen above their 200-day averages on the night, a
    // breadth of 0.41, under the provisional pullback's own market dial of 0.45 and above the frozen rule's floor of 0.35.
    // Each strong member's four quarters as the night filed them and as the history pulled them, its operating income 10
    // a quarter against interest of 1 for AAA, 30 for BBB and none for CCC; and the night's coverage read off them, 40
    // against 8 passing, 40 against 240 failing and no interest passing.
    static TemporaryStore FrozenPullbackStore()
    {
        var store = new TemporaryStore().Migrated();
        var calendar = BusinessFirstHistory.Calendar(BusinessFirstHistory.Night + 1);
        var statements = new List<string>();

        foreach (var ticker in BusinessFirstHistory.Tickers.Concat(FrozenDecliners))
        {
            statements.Add(FrozenMember("MID", ticker));
            statements.Add(FrozenPulledMember(ticker));

            for (var at = 0; at < calendar.Length; at++)
            {
                if (ticker.StartsWith('F'))
                {
                    var close = 120m - (0.1m * at);
                    var open = at == 0 ? close : close + 0.1m;

                    statements.Add(FrozenBar(ticker, calendar[at], open, open + 0.5m, close - 0.5m, close, 1_000_000));
                }
                else
                {
                    var bar = BusinessFirstHistory.Bar(calendar, ticker, at);

                    statements.Add(FrozenBar(ticker, bar.Session, bar.Open, bar.High, bar.Low, bar.Close, bar.Volume));
                }
            }
        }

        foreach (var (ticker, interest, covered) in new[] { ("AAA", "'1'", 1), ("BBB", "'30'", 0), ("CCC", "NULL", 1) })
        {
            foreach (var quarter in BusinessFirstHistory.Quarters())
            {
                statements.AddRange(FrozenQuarter(ticker, quarter.PeriodEnd, quarter.FilingDate, interest));
            }

            statements.Add($"INSERT INTO member_reading (index_code, session_date, ticker, profit, coverage) VALUES ('MID', '{Day(calendar[^1])}', '{ticker}', 1, {covered});");
        }

        FrozenBatch(store, statements);

        return store;
    }

    // The S&P 400's pullback frozen at its base's dials with its own market dial off, its market check on the index's own
    // breadth at 0.35 and the profit gate and the interest cover, as the S&P 400's own sweep chose.
    static Dictionary<string, double> FrozenPullback() => new(IndexRules.Provisional(IndexEvaluator("pullback-400")), StringComparer.Ordinal)
    {
        [IndexPullbackCandidate.MarketParameter] = 0,
        [IndexRuleCandidate.BreadthFloorParameter] = 0.35,
        [IndexRuleCandidate.QualityParameter] = 2,
    };

    [Fact]
    public async Task TheLedgerReadsTheFrozenPullbackAtItsOwnMarketFloorAndItsCoverAndTheHistoryBuildAgrees()
    {
        var calendar = BusinessFirstHistory.Calendar(BusinessFirstHistory.Night + 1);
        var night = calendar[^1];
        var live = FrozenPullback();
        var words = IndexRules.Words(IndexEvaluator("pullback-400"), live);
        var frozenAt = new DateTimeOffset(2026, 1, 27, 12, 0, 0, TimeSpan.Zero);
        var nightClock = FixedClock.At(new DateTimeOffset(2026, 1, 27, 23, 40, 0, TimeSpan.Zero), SessionZones.UnitedStates);
        string[] setups = ["AAA", "BBB", "CCC"];

        using var store = FrozenPullbackStore();

        // Before the freeze the ledger reads the provisional pullback, the base, whose own market dial at 0.45 the index's
        // breadth of 7 of 17 closes: the three strong members are setups none of which it passes.
        await new SetupLedger(nightClock, store.DatabaseFile).NightAsync("ledger-before");

        var breadth = Assert.Single(FamilyRows(store, $"SELECT breadth FROM setup WHERE index_code = 'MID' AND family = 'pullback' AND ticker = 'AAA' AND session_date = '{Day(night)}';"));

        Assert.Equal(7.0 / 17.0, double.Parse(breadth, CultureInfo.InvariantCulture), 9);
        Assert.Equal([.. setups.Select(ticker => $"{ticker}|{LedgerSetups.PullbackWords("MID")}|0")], FrozenRows(store, "MID", SetupFamilies.Pullback, night, SetupLedger.NightSource));

        // Frozen, and the night read again over the register as it stands, its index families first as the night runs
        // them: the frozen rule's market check reads the index's own breadth of 0.41 at its own floor of 0.35, open, and
        // its quality the cover the night stored, AAA's and CCC's passing and BBB's not. The ledger passes AAA and CCC under
        // the rule's words, as the night's page does.
        await FrozenRegister(store, frozenAt, SetupFamilies.Pullback, live);

        var register = await new CandidateRegistrar(nightClock, store.DatabaseFile).RowsAsync();

        await new IndexFamilies(nightClock, store.DatabaseFile).RunAsync("night-frozen", default, register, nightClock.UtcNow);
        await new SetupLedger(nightClock, store.DatabaseFile).NightAsync("ledger-frozen");

        var rows = FrozenRows(store, "MID", SetupFamilies.Pullback, night, SetupLedger.NightSource);

        Assert.Equal([$"AAA|{words}|1", $"BBB|{words}|0", $"CCC|{words}|1"], rows);
        Assert.Equal(["AAA|1|", "BBB|0|" + IndexNightRead.NoCover, "CCC|1|"], FamilyRows(store, $"SELECT ticker, passed, IFNULL(reason, '') FROM index_family_result WHERE index_code = 'MID' AND family = 'pullback' AND ticker IN ('AAA', 'BBB', 'CCC') AND session_date = '{Day(night)}' ORDER BY ticker;"));

        // The history build over a copy, its cover read off the quarters the history pulled: the same words and passes.
        using var copy = FrozenPullbackStore();

        await FrozenRegister(copy, frozenAt, SetupFamilies.Pullback, live);
        Assert.Equal(0, await new SetupLedger(FixedClock.At(new DateTimeOffset(2026, 1, 28, 15, 0, 0, TimeSpan.Zero), SessionZones.UnitedStates), copy.DatabaseFile)
            .BuildAsync(copy.Root, "MID", night, night, new StringWriter()));

        Assert.Equal(rows, FrozenRows(copy, "MID", SetupFamilies.Pullback, night, SetupLedger.HistorySource));
    }

    // The drift's constructed history: weekdays from 2025-01-06, each member at 100 on 200,000 shares a session, a range of
    // two points, until its reaction on the 281st session, 2026-02-02, the day it reported a surprise of ten per cent
    // before the open: opening at 102 and closing at 104 on 500,000 shares, its low 102, up two typical moves. DA and DB
    // then close at 105 and 106 and on the third session after, 2026-02-05, at 105.5, under the close before and above
    // the stop, then rise a point a session; DC rises a point a session from the reaction on and never closes under the
    // close before.
    const int FrozenReaction = 280;

    static readonly string[] FrozenDrifters = ["DA", "DB", "DC"];

    static (decimal Open, decimal High, decimal Low, decimal Close, long Volume) DrifterBar(string ticker, int at)
    {
        if (at < FrozenReaction)
        {
            return (100m, 101m, 99m, 100m, 200_000);
        }

        if (at == FrozenReaction)
        {
            return (102m, 105m, 102m, 104m, 500_000);
        }

        static decimal Close(string ticker, int at) => at == FrozenReaction ? 104m
            : ticker == "DC" ? 104m + (at - FrozenReaction)
            : (at - FrozenReaction) switch
            {
                1 => 105m,
                2 => 106m,
                var after => 105.5m + (after - 3),
            };

        var (open, close) = (Close(ticker, at - 1), Close(ticker, at));

        return (open, Math.Max(open, close) + 0.5m, Math.Min(open, close) - 0.5m, close, 200_000);
    }

    static IEnumerable<string> DrifterSession(DateOnly[] calendar, int at) =>
        FrozenDrifters.Select(ticker => DrifterBar(ticker, at) is var bar ? FrozenBar(ticker, calendar[at], bar.Open, bar.High, bar.Low, bar.Close, bar.Volume) : string.Empty);

    // The three drifters on the S&P 400 through the session given, each with four profitable quarters and its report; DA's
    // and DB's filers' revenue for the quarter to 2025-12-31 at 120 against 100 a year before, first filed on the reaction
    // session for DA and on the session before the pullback, 2026-02-04, for DB; and today's members as the history reads
    // them.
    static TemporaryStore FrozenDriftStore(DateOnly[] calendar, int through)
    {
        var store = new TemporaryStore().Migrated();
        var statements = new List<string>();

        foreach (var ticker in FrozenDrifters)
        {
            statements.Add(FrozenMember("MID", ticker));
            statements.Add(FrozenPulledMember(ticker));
            statements.Add($"INSERT INTO calendar (ticker, event_date, kind, timing, detail, observed_at) VALUES ('{ticker}', '{Day(calendar[FrozenReaction])}', 'earnings', 'before', '{{\"surprise\":\"10\"}}', '{Day(calendar[FrozenReaction])}T21:00:00Z');");

            foreach (var (end, filed) in new[] { ("2024-12-31", "2025-02-10"), ("2025-03-31", "2025-05-01"), ("2025-06-30", "2025-08-01"), ("2025-09-30", "2025-11-03") })
            {
                statements.AddRange(FrozenQuarter(ticker, DateOnly.ParseExact(end, "yyyy-MM-dd", CultureInfo.InvariantCulture), DateOnly.ParseExact(filed, "yyyy-MM-dd", CultureInfo.InvariantCulture)));
            }
        }

        foreach (var (ticker, cik, filed) in new[] { ("DA", "0000000201", calendar[FrozenReaction]), ("DB", "0000000202", calendar[FrozenReaction + 2]) })
        {
            statements.Add($"INSERT INTO company (ticker, fetched_at, cik) VALUES ('{ticker}', '2026-01-02T23:40:00.000Z', '{cik}');");
            statements.Add($"INSERT INTO filed_fact (cik, concept, period_start, period_end, dollars, filed, form, accession, run_id) VALUES ('{cik}', 'Revenues', '2024-10-01', '2024-12-31', '100', '2025-02-10', '10-Q', 'x-1', 'refresh');");
            statements.Add($"INSERT INTO filed_fact (cik, concept, period_start, period_end, dollars, filed, form, accession, run_id) VALUES ('{cik}', 'Revenues', '2025-10-01', '2025-12-31', '120', '{Day(filed)}', '10-Q', 'x-2', 'refresh');");
        }

        for (var at = 0; at <= through; at++)
        {
            statements.AddRange(DrifterSession(calendar, at));
        }

        FrozenBatch(store, statements);

        return store;
    }

    // The S&P 400's drift frozen within 10 sessions, up half a typical move on 1.25 times the volume, the target at three
    // times the risk and the stop at least a typical move beneath, bought on its first close under the close before it
    // within ten sessions of the reaction, its print's quarter's revenue above the same quarter's a year before as first
    // filed and stored before the night, the profit gate, and no market check.
    static Dictionary<string, double> FrozenDrift() => new(IndexRules.Provisional(IndexEvaluator("drift-400")), StringComparer.Ordinal)
    {
        [IndexDriftCandidate.WindowSessionsParameter] = 10,
        [IndexDriftCandidate.ReactionMovesParameter] = 0.5,
        [IndexDriftCandidate.VolumeMultipleParameter] = 1.25,
        [IndexDriftCandidate.TargetMultipleParameter] = 3,
        [IndexDriftCandidate.StopFloorParameter] = 1,
        [IndexDriftCandidate.EntryParameter] = 1,
        [IndexDriftCandidate.RevenueGrewParameter] = 1,
        [IndexRuleCandidate.LargeBreadthParameter] = IndexRuleCandidate.NoMarketCheck,
        [IndexRuleCandidate.BreadthFloorParameter] = 0,
    };

    [Fact]
    public async Task AFrozenDriftsSetupIsPassedOnItsReactionsRowByTheNightThatBuysItsFirstPullbackItsRevenueReadOnceStored()
    {
        var calendar = BusinessFirstHistory.Calendar(FrozenReaction + 13);
        var (reaction, pullback) = (calendar[FrozenReaction], calendar[FrozenReaction + 3]);
        var live = FrozenDrift();
        var words = IndexRules.Words(IndexEvaluator("drift-400"), live);
        var frozenAt = new DateTimeOffset(2026, 1, 30, 12, 0, 0, TimeSpan.Zero);

        Assert.Equal((new DateOnly(2026, 2, 2), new DateOnly(2026, 2, 5)), (reaction, pullback));

        using var store = FrozenDriftStore(calendar, FrozenReaction);

        SetupLedger LedgerOn(int at) => new(FixedClock.At(new DateTimeOffset(calendar[at].ToDateTime(new TimeOnly(23, 40)), TimeSpan.Zero), SessionZones.UnitedStates), store.DatabaseFile);

        await FrozenRegister(store, frozenAt, DriftRule.Name, live);

        // The reaction's night: the three are drift setups on their reaction, a surprise above nothing and a rise of two
        // typical moves, each under the frozen rule's words and none passed, since the rule buys on the first pullback after
        // the reaction and none has come. The same three are breakout setups there, read at the provisional breakout, no
        // breakout's live rule standing.
        await LedgerOn(FrozenReaction).NightAsync("ledger-reaction");

        Assert.Equal([.. FrozenDrifters.Select(ticker => $"{ticker}|{words}|0")], FrozenRows(store, "MID", DriftRule.Name, reaction, SetupLedger.NightSource));
        Assert.Equal([$"3|{LedgerSetups.BreakoutWords}"], FamilyRows(store, $"SELECT COUNT(*), rule FROM setup WHERE index_code = 'MID' AND family = 'breakout' AND session_date = '{Day(reaction)}' GROUP BY rule;"));

        // The two nights after: every close above the one before it, so nothing is bought.
        foreach (var at in new[] { FrozenReaction + 1, FrozenReaction + 2 })
        {
            FrozenBatch(store, DrifterSession(calendar, at));
            await LedgerOn(at).NightAsync(FormattableString.Invariant($"ledger-{at}"));

            Assert.Equal([.. FrozenDrifters.Select(ticker => $"{ticker}|{words}|0")], FrozenRows(store, "MID", DriftRule.Name, reaction, SetupLedger.NightSource));
        }

        // The pullback's night, DA and DB closing at 105.5 under 106 and above their stop near 101.8, DC at 107: the rule buys
        // DA, whose print's quarter was filed on the reaction and so stored before the session before tonight, 120 over
        // 100; it refuses DB, whose quarter was filed on that session before tonight and is not stored before it; and DC is
        // not bought. The reaction's row of DA is passed tonight and its family's row on that session counts it.
        FrozenBatch(store, DrifterSession(calendar, FrozenReaction + 3));

        var bought = await LedgerOn(FrozenReaction + 3).NightAsync("ledger-pullback");
        var rows = FrozenRows(store, "MID", DriftRule.Name, reaction, SetupLedger.NightSource);

        Assert.Equal([$"DA|{words}|1", $"DB|{words}|0", $"DC|{words}|0"], rows);
        Assert.Equal(["3|3|1"], FamilyRows(store, $"SELECT members, setups, live_passes FROM setup_night WHERE index_code = 'MID' AND family = 'drift' AND session_date = '{Day(reaction)}';"));
        Assert.Equal(1, bought.Indices.Single(index => index.Index == "MID").BoughtSince);
        Assert.Contains("1 setup(s) of the sessions before bought by their live rule tonight", SetupLedger.Detail(bought.Indices), StringComparison.Ordinal);

        // Run again, the night passes nothing more and counts DA once.
        var again = await LedgerOn(FrozenReaction + 3).NightAsync("ledger-pullback-again");

        Assert.Equal(rows, FrozenRows(store, "MID", DriftRule.Name, reaction, SetupLedger.NightSource));
        Assert.Equal(["3|3|1"], FamilyRows(store, $"SELECT members, setups, live_passes FROM setup_night WHERE index_code = 'MID' AND family = 'drift' AND session_date = '{Day(reaction)}';"));
        Assert.Equal(0, again.Indices.Single(index => index.Index == "MID").BoughtSince);

        // The history build over a copy holding twelve sessions after the reaction: read through the session after the
        // reaction, its history ends before the pullback and passes none; read through the twelfth, the rule's ten after
        // the reaction all held, it passes the reaction's row of DA alone, as the nights did.
        using var copy = FrozenDriftStore(calendar, FrozenReaction + 12);

        SetupLedger Builder(int hour) => new(FixedClock.At(new DateTimeOffset(2026, 2, 21, hour, 0, 0, TimeSpan.Zero), SessionZones.UnitedStates), copy.DatabaseFile);

        await FrozenRegister(copy, frozenAt, DriftRule.Name, live);

        Assert.Equal(0, await Builder(15).BuildAsync(copy.Root, "MID", reaction, calendar[FrozenReaction + 1], new StringWriter()));
        Assert.Equal([.. FrozenDrifters.Select(ticker => $"{ticker}|{words}|0")], FrozenRows(copy, "MID", DriftRule.Name, reaction, SetupLedger.HistorySource));

        Assert.Equal(0, await Builder(16).BuildAsync(copy.Root, "MID", reaction, calendar[FrozenReaction + 12], new StringWriter(), again: true));
        Assert.Equal(rows, FrozenRows(copy, "MID", DriftRule.Name, reaction, SetupLedger.HistorySource));
    }

    [Fact]
    public async Task ALiveRuleStatingAPartTheLedgerReadsOnNoSessionLeavesItsFamilyProvisionalAndNamedAndEachRulesReachIsWorkedByHand()
    {
        using var store = new TemporaryStore().Migrated();
        var at = new DateTimeOffset(2026, 10, 3, 12, 0, 0, TimeSpan.Zero);
        var pullback = new Dictionary<string, double>(FrozenPullback(), StringComparer.Ordinal) { [IndexRuleCandidate.IndustryFallParameter] = 21 };

        // The pullback frozen with the industry's fall over 21 sessions, which the history holds no reading of, and the
        // breakout frozen as the S&P 400's sweep chose: the ledger reads the breakout's live rule and leaves the
        // pullback's unread, naming it, its family read at its provisional rule.
        await FrozenRegister(store, at, SetupFamilies.Pullback, pullback);
        await FrozenRegister(store, at.AddSeconds(1), BreakoutRule.Name, FrozenBreakout());

        var register = await new CandidateRegistrar(FixedClock.At(at, SessionZones.UnitedStates), store.DatabaseFile).RowsAsync();
        var read = LedgerLiveRules.Of(register, at.AddMinutes(1), "MID");

        Assert.Equal([BreakoutRule.Name], read.Read.Keys);
        Assert.StartsWith("the live S&P 400 breakout rule at a 251-session high", read.Read[BreakoutRule.Name].Candidate, StringComparison.Ordinal);
        Assert.Equal(
            $"the breakout read at its frozen live rule as the register stood at 2026-10-03T12:01:00Z, the pullback's live rule 'the live S&P 400 {IndexRules.Words(IndexEvaluator("pullback-400"), pullback)}' states the industry's fall, which the ledger reads on no session, so its provisional rule is read",
            read.Words(at.AddMinutes(1)));

        // A second before the freeze no live rule stands, and the S&P 500 and the S&P 600 read none.
        Assert.Empty(LedgerLiveRules.Of(register, at.AddSeconds(-1), "MID").Read);
        Assert.Null(LedgerLiveRules.Of(register, at.AddSeconds(-1), "MID").Words(at));
        Assert.Empty(LedgerLiveRules.Of(register, at.AddMinutes(1), "SML").Read);
        Assert.Empty(LedgerLiveRules.Of(register, at.AddMinutes(1), IndexFamilies.LargeIndex).Read);

        // Each rule's reach, the sessions after a setup's own its rule may still buy it on: none for the pullback and the
        // breakout; ten after the reaction for a drift buying on its first pullback; and for a drift buying at its listing,
        // its window less one, 9 within 10 sessions and 14 where the rule states the wider window of 15.
        IndexRule Rule(string evaluator, IReadOnlyDictionary<string, double> parameters) => new("rule", IndexEvaluator(evaluator), parameters, at);

        var atItsListing = new Dictionary<string, double>(FrozenDrift(), StringComparer.Ordinal) { [IndexDriftCandidate.EntryParameter] = 0 };

        Assert.Equal(
            (0, 0, 10, 9, 14),
            (LedgerLiveRules.Reach(Rule("pullback-400", FrozenPullback())),
             LedgerLiveRules.Reach(Rule("breakout-400", FrozenBreakout())),
             LedgerLiveRules.Reach(Rule("drift-400", FrozenDrift())),
             LedgerLiveRules.Reach(Rule("drift-400", atItsListing)),
             LedgerLiveRules.Reach(Rule("drift-400", new Dictionary<string, double>(atItsListing, StringComparer.Ordinal) { [IndexDriftCandidate.WideWindowParameter] = 15 }))));

        // Each part the ledger reads on no session named once.
        var drift = new Dictionary<string, double>(FrozenDrift(), StringComparer.Ordinal) { [IndexDriftCandidate.PeersParameter] = 1, [IndexRuleCandidate.CreditChangeParameter] = 1 };

        Assert.Equal(["a market switch", "the peers' surprise"], LedgerLiveRules.Unread(Rule("drift-400", drift)));
        Assert.Empty(LedgerLiveRules.Unread(Rule("drift-400", FrozenDrift())));
    }
}
