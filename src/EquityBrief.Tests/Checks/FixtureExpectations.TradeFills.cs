using System.Globalization;
using System.Text.RegularExpressions;
using EquityBrief.Core.Ledger;
using EquityBrief.Core.Time;
using EquityBrief.Tests.Harness;
using EquityBrief.Worker.Fills;
using EquityBrief.Worker.Indices;
using EquityBrief.Worker.Ledger;

namespace EquityBrief.Tests.Checks;

// fixture-expectations, 19.10's move: every entry priced at the next session's open through the core's one pricing
// function, each figure worked by hand. A trade whose open gaps under its stop, one over its target and one with no next
// session, each through the same exits; the benchmark at the same open; the fill step's figures over a constructed store
// equal to the next-open check's over the same trades; every stored trade holding a fill or a counted reason after the
// fill step runs again; no source a rule's version pins naming the fill; and no read surface or record drawing a result
// from the close-priced columns.
// see: A trade's entry is priced at the next session's open by a fill step of its own, and no rule's version moves because of it
public partial class FixtureExpectations
{
    // The rows 19.10 adds to the architecture: the fill step's catalogue and matrix rows and its store.
    internal static readonly string[] TradeFillRows =
    [
        CheckReach.Key(Scope.CatalogueTable, "Trade filler"),
        CheckReach.Key(Scope.MatrixTable, "Trade filler"),
        CheckReach.Key(Scope.StoresTable, "Trade fills"),
    ];

    [Fact]
    public void AGapUnderTheStopAndOneOverTheTargetAreBoughtAtTheOpenAndEndWhereThePlanEnds()
    {
        // Bought at 100 with its stop at 95, five a risk, and its target at 110.
        var on = new DateOnly(2026, 1, 6);

        // It opens at 94, under its stop, and closes at 94 through it: bought at the close it lost 1.2 risks, bought at
        // the open it sells where it bought, nothing in the close's risk.
        var under = TradeFills.Swing(on, 100, 95, 110, 94, -1.2, null, [])!;

        Assert.Equal((94.0, FillPast.Stop, 0.0), (under.Fill, under.Past, Math.Round(under.Result!.Value, 12)));

        // It opens at 112, over its target, and closes at 111 at it: bought at the close it made 2.2 risks, bought at the
        // open it lost a fifth of one, (111 - 112) / 5.
        var over = TradeFills.Swing(on, 100, 95, 110, 112, 2.2, null, [])!;

        Assert.Equal((112.0, FillPast.Target, -0.2), (over.Fill, over.Past, Math.Round(over.Result!.Value, 12)));

        // Inside its range at 102 it says neither, and an open still running has no result.
        Assert.Null(TradeFills.Swing(on, 100, 95, 110, 102, 2.2, null, [])!.Past);
        Assert.Null(TradeFills.Swing(on, 100, 95, 110, 102, null, null, [])!.Result);

        // With no open held it holds no fill, and nor does a plan whose stop put up no risk at its close.
        Assert.Null(TradeFills.Swing(on, 100, 95, 110, null, 2.2, null, []));
        Assert.Null(TradeFills.Swing(on, 100, 95, 110, 0, 2.2, null, []));
        Assert.Null(TradeFills.Swing(on, 100, 100, 110, 102, 2.2, null, []));

        // A plan whose stop is not stored yet is bought at its open with nothing read against its range and no result,
        // even at an open a stop under its close would have been past.
        var unplanned = TradeFills.Swing(on, 100, null, null, 94, null, null, [])!;

        Assert.Equal((94.0, null as string, null as double?), (unplanned.Fill, unplanned.Past, unplanned.Result));

        // A book's holding bought at 100 and up a tenth at its sale, opening at 104: 0.06 of the close it was sized from.
        var held = TradeFills.Holding(on, 100, 104, 0.10, null, [])!;

        Assert.Equal((null as string, 0.06), (held.Past, Math.Round(held.Result!.Value, 12)));
    }

    [Fact]
    public void TheBenchmarkAtTheOpenMovesEachEndedMemberByItsOwnGapOverItsOwnRisk()
    {
        // A benchmark of 0.5 over two members: one opening two over its close of 100 on a risk of four, one opening five
        // under its close of 50 on a risk of four, and one whose path has not ended left out.
        FillMember[] members =
        [
            new(true, 100, 4, 102),
            new(true, 50, 4, 45),
            new(false, 80, 4, 90),
        ];

        var (mean, counted) = TradeFills.Benchmark(0.5, members);

        // 0.5 less the mean of 2 / 4 and -5 / 4: 0.5 + 0.375.
        Assert.Equal((0.875, 2), (mean!.Value, counted));

        // No ended member holding an open leaves none, and no benchmark at the close none.
        Assert.Equal((null as double?, 0), TradeFills.Benchmark(0.5, [new(true, 100, 4, null)]));
        Assert.Equal((null as double?, 0), TradeFills.Benchmark(null, members));

        // The trade's edge at the open is its result there less that benchmark.
        var fill = TradeFills.Swing(new DateOnly(2026, 1, 6), 100, 96, 108, 102, 2.25, 0.5, members)!;

        Assert.Equal((1.75, 0.875, 0.875), (Math.Round(fill.Result!.Value, 12), Math.Round(fill.Benchmark!.Value, 12), Math.Round(fill.Edge!.Value, 12)));
    }

    [Fact]
    public async Task TheFillStepOverAConstructedStorePricesEachTradeAsTheNextOpenCheckDoesAndLeavesNoneWithoutAFillOrAReason()
    {
        var days = new List<DateOnly>();

        for (var day = NextOpenDay; days.Count < 30; day = day.AddDays(1))
        {
            if (day.DayOfWeek is not DayOfWeek.Saturday and not DayOfWeek.Sunday)
            {
                days.Add(day);
            }
        }

        string Stamp(int at) => days[at].ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        // The next-open check's constructed store: AAA opening at 102 the session after the 21st and closing at 109 over its
        // target, BBB opening at 45 under its stop and closing at 45 through it; CCC holding no bar after the 21st session;
        // each flat with a range of 2 before, its typical move 2.
        (decimal Open, decimal Close, decimal Low, decimal High) Bar(string ticker, int at) => (ticker, at) switch
        {
            ("AAA", 21) => (102, 103, 101, 104),
            ("AAA", >= 22) => (109, 109, 108, 110),
            ("BBB", 21) => (45, 47, 44, 48),
            ("BBB", >= 22) => (45, 45, 44, 46),
            ("AAA", _) => (100, 100, 99, 101),
            ("BBB", _) => (50, 50, 49, 51),
            _ => (20, 20, 19, 21),
        };

        using var store = new TemporaryStore().Migrated();

        foreach (var ticker in new[] { "AAA", "BBB", "CCC" })
        {
            store.Execute($"INSERT INTO membership (index_code, ticker, joined, \"left\", observed_at) VALUES ('GSPC', '{ticker}', NULL, NULL, '2026-02-13T21:10:00Z');");

            for (var at = 0; at < (ticker == "CCC" ? 21 : days.Count); at++)
            {
                var (open, close, low, high) = Bar(ticker, at);

                store.Execute($"INSERT INTO bar (ticker, session_date, open, high, low, close, volume, source, observed_at, raw_close) VALUES ('{ticker}', '{Stamp(at)}', '{open}', '{high}', '{low}', '{close}', 1000000, 'constructed', '{Stamp(at)}T21:00:00Z', '{close}');");
            }

            store.Execute($"INSERT INTO indicator (ticker, session_date, name, value, bar_count) VALUES ('{ticker}', '{Stamp(20)}', 'atr14', 2, 21);");
        }

        // The same two setups on the ledger at their stored open figures, and as a registered rule's two trades at the
        // close, with a third trade on CCC, whose stock holds no next session, a fourth listed on the newest session, and a
        // fifth on AAA's tenth session whose stop sits at its buy.
        foreach (var (ticker, entry, stop, target, result, edge, end) in new[] { ("AAA", 100, 96, 108, 1.75, 0.875, "target"), ("BBB", 50, 46, 58, 0.0, -0.875, "stop") })
        {
            store.Execute(
                "INSERT INTO setup (index_code, family, ticker, session_date, rule, live_pass, entry, stop, target, cap, result, benchmark, edge, sessions, end, ended_on, settled, source, pin) "
                + $"VALUES ('GSPC', 'pullback', '{ticker}', '{Stamp(20)}', 'the pullback', 1, '{entry}', '{stop}', '{target}', 10, {Invariant(result)}, 0.875, {Invariant(edge)}, 2, '{end}', '{Stamp(22)}', 1, 'history', '{LedgerReadings.Version}');");
        }

        foreach (var (ticker, session, entry, stop, target, result) in new[] { ("AAA", 20, 100, 96, 108, "2.25"), ("BBB", 20, 50, 46, 58, "-1.25"), ("CCC", 20, 20, 16, 28, "NULL"), ("AAA", 29, 109, 105, 117, "NULL"), ("AAA", 10, 100, 100, 108, "NULL") })
        {
            store.Execute(
                "INSERT INTO family_trade (candidate, ticker, session_date, family, place, entry, stop, target, risk_moves, reward_to_risk, cap, ended_on, result, benchmark, members) "
                + $"VALUES ('the rule', '{ticker}', '{Stamp(session)}', 'pullback', 1, '{entry}', '{stop}', '{target}', 2, 2, 10, {(result == "NULL" ? "NULL" : $"'{Stamp(22)}'")}, {result}, {(result == "NULL" ? "NULL" : "0.5")}, {(result == "NULL" ? "NULL" : "2")});");
        }

        // The page list's two scored plans on the same night, as the forward return filler writes them: AAA's still open,
        // whose risk the filler writes only once it resolves, and BBB's stopped out at -10 per cent over the 8 per cent of
        // its close of 50 its stop at 46 put at risk, -1.25 risks at the close.
        store.Execute(
            "INSERT INTO forward_return (ticker, session_date, horizon, outcome, resolved_on, return_pct, base_rate, break_even, planned_risk) VALUES "
            + $"('AAA', '{Stamp(20)}', 'clear', NULL, NULL, NULL, NULL, NULL, NULL), ('BBB', '{Stamp(20)}', 'swing', 'loss', '{Stamp(22)}', -10, NULL, NULL, 8);");

        var filler = new TradeFiller(FixedClock.At(new DateTimeOffset(2026, 2, 16, 15, 0, 0, TimeSpan.Zero), SessionZones.UnitedStates), store.DatabaseFile);
        var outcome = await filler.RunAsync("fill-test");
        var trades = outcome.Sources.Single(source => source.Source == "family_trade");

        // AAA and BBB priced, CCC holding no next session, the newest night's trade waiting and the tenth session's putting
        // up no risk; and both scored plans priced.
        Assert.Equal((2, 1, 0, 1, 1), (trades.Filled, trades.NoNextSession, trades.NoOpen, trades.Waiting, trades.NoRisk));
        Assert.Equal(2, outcome.Sources.Single(source => source.Source == "forward_return").Filled);

        var rows = Query(store, "SELECT ticker || '|' || IFNULL(past, '') || '|' || IFNULL(fill, '') || '|' || IFNULL(result, '') || '|' || IFNULL(benchmark, '') || '|' || IFNULL(edge, '') || '|' || IFNULL(reason, '') FROM trade_fill WHERE source = 'family_trade' ORDER BY ticker, session_date;");

        // Each figure the next-open check works for the same setup on the ledger: AAA 1.75 and BBB nothing at the open, the
        // benchmark 0.5 less the mean of 2 / 4 and -5 / 4, so 0.875, and each edge the result less it.
        Assert.Equal([$"AAA||||||{TradeFills.NoRisk}", "AAA||102|1.75|0.875|0.875|", "BBB|stop|45|0.0|0.875|-0.875|", $"CCC||||||{TradeFills.NoNextSession}"], rows);

        // The scored plans at their stock's own open: AAA's open plan bought at 102 with nothing read against its range and
        // no result yet, and BBB's at 45 under its stop, -1.25 less (45 - 50) / 4, nothing, as the rule's trade.
        Assert.Equal(
            ["AAA||102||||", "BBB|stop|45|0.0|||"],
            Query(store, "SELECT ticker || '|' || IFNULL(past, '') || '|' || IFNULL(fill, '') || '|' || IFNULL(result, '') || '|' || IFNULL(benchmark, '') || '|' || IFNULL(edge, '') || '|' || IFNULL(reason, '') FROM trade_fill WHERE source = 'forward_return' ORDER BY ticker;"));

        using var output = new StringWriter();
        var ledger = new SetupLedger(FixedClock.At(new DateTimeOffset(2026, 2, 16, 15, 0, 0, TimeSpan.Zero), SessionZones.UnitedStates), store.DatabaseFile);

        Assert.Equal(0, await ledger.NextOpenAsync(IndexFamilies.LargeIndex, output));
        SweepRelease(store);
        Assert.Contains("2 of 2 results at the open worked again as stored to a millionth", output.ToString(), StringComparison.Ordinal);

        // Every stored trade holds a fill or a counted reason but the one waiting on its next session, and a run again
        // writes nothing new over figures that stand.
        Assert.Equal(
            Query(store, "SELECT (SELECT COUNT(*) FROM family_trade) + (SELECT COUNT(*) FROM forward_return);").Single(),
            (int.Parse(Query(store, "SELECT COUNT(*) FROM trade_fill;").Single(), CultureInfo.InvariantCulture) + outcome.Waiting).ToString(CultureInfo.InvariantCulture));

        var again = await filler.RunAsync("fill-test-again");

        Assert.Equal((0, 6), (again.Filled + again.NoNextSession + again.NoOpen + again.NoRisk, again.Standing));

        // AAA's plan resolved since at its target, 9 per cent over its risk of 4 per cent: priced again with its result,
        // 2.25 less (102 - 100) / 4, 1.75, as the rule's trade on the same plan.
        store.Execute($"UPDATE forward_return SET outcome = 'win', resolved_on = '{Stamp(22)}', return_pct = 9, planned_risk = 4 WHERE ticker = 'AAA';");

        var resolved = await filler.RunAsync("fill-test-resolved");

        Assert.Equal((1, 5), (resolved.Filled, resolved.Standing));
        Assert.Equal(["1.75"], Query(store, "SELECT ROUND(result, 12) FROM trade_fill WHERE source = 'forward_return' AND ticker = 'AAA';"));

        var whole = await filler.RunAsync("fill-test-whole", again: true);

        Assert.Equal((4, 1, 1, 1), (whole.Filled, whole.NoNextSession, whole.Waiting, whole.NoRisk));
    }

    static string Invariant(double value) => value.ToString("R", CultureInfo.InvariantCulture);

    [Fact]
    public void APaperAccountStartsAtItsFirstListingWhetherItsFirstTradeFilledAndBuysEachTradeAtItsFill()
    {
        var night = new DateOnly(2026, 10, 14);
        var first = new DateOnly(2026, 10, 1);

        // An S&P 400 list's two trades: the first listed on the 1st, its stock holding no next session, and the second
        // listed on the 5th, filled at 101 at the open of the 6th, its plan's close 100 and its stop 95.
        EquityBrief.Api.Reading.PaperRow[] rows =
        [
            new("list pullback", "pullback", "AAA", first, 1, 100, 95, null, null, false, null, null, Unfilled: TradeFills.NoNextSession),
            new("list pullback", "pullback", "BBB", new DateOnly(2026, 10, 5), 1, 100, 95, new DateOnly(2026, 10, 7), 1.6, false, null, null, 101, new DateOnly(2026, 10, 6), 101),
        ];

        var account = EquityBrief.Api.Reading.PaperScreen.Accounts(false, "S&P 400", night, [], null, rows, [], []).Single(one => one.Family == "pullback" && one.Standing == EquityBrief.Web.Marks.PaperAccountView.Provisional);

        // The start is the first night the list kept a trade, as it was before entries moved to the open.
        Assert.Equal(first, account.Since);

        // The one filled trade is bought at its fill on its fill's session, sized from its plan's close, and sold where its
        // result in that close's risk puts it: 101 + 1.6 x 5.
        var trade = Assert.Single(account.Trades).Trade;

        Assert.Equal((new DateOnly(2026, 10, 6), 101m, 100m, 109m, new DateOnly(2026, 10, 5)), (trade.Bought, trade.Buy, trade.SizedAt, trade.Sale!.Value, trade.ListedOn));
    }

    [Fact]
    public void NoSourceARulesVersionPinsNamesTheFillSoNoRulesVersionMovesBecauseOfIt()
    {
        // The fill's code: the core's pricing function, the fill step and the loop's pricing beside the sweep.
        string[] fill = ["TradeFills", "TradeFiller", "OpenFills", "trade_fill"];

        // Every source an evaluator pins, its own and the shared evaluation's, the hooks' and the readings' version, but the
        // quality rules', whose step buys at the fill and which no rule registered before them did.
        // see: A quality rule is registered as the family on its index and kept by the family's own step alone
        var pinned = EquityBrief.Core.Candidates.CandidateEvaluators.All
            .Where(evaluator => evaluator is not EquityBrief.Core.Candidates.QualityCandidate)
            .SelectMany(evaluator => evaluator.OwnSources)
            .Concat(EquityBrief.Core.Candidates.CandidateEvaluator.EvaluationSources)
            .Concat(EquityBrief.Core.Loop.RuleHooks.Sources)
            .Concat(LedgerSources.Select(path => Path.GetRelativePath(Repository.Root, path).Replace(Path.DirectorySeparatorChar, '/')))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        Assert.True(pinned.Length >= 30, $"Read {pinned.Length} pinned source(s), expected at least 30.");

        var naming = pinned
            .Where(path => fill.Any(word => File.ReadAllText(Path.Combine(Repository.Root, path)).Contains(word, StringComparison.Ordinal)))
            .ToArray();

        Assert.Empty(naming);

        // The reader is shown to find a pinned source naming the fill.
        Assert.Contains(fill, word => "var fill = TradeFills.Swing(on, close, stop, target, open, result, null, []);".Contains(word, StringComparison.Ordinal));
    }

    [Fact]
    public void NoReadSurfaceOrRecordDrawsAResultFromTheCloseColumnsOfATradesTable()
    {
        var files = new[]
        {
            Path.Combine(Repository.Root, "src", "EquityBrief.Api", "Reading", "ReadApi.cs"),
            Path.Combine(Repository.Root, "src", "EquityBrief.Api", "Reading", "ReadApi.Paper.cs"),
            Path.Combine(Repository.Root, "src", "EquityBrief.Worker", "Loop", "LiveAlarmReader.cs"),
        };

        var statements = files.SelectMany(file => TradeStatements(File.ReadAllText(file))).ToArray();

        // Every statement reading a trade's table on the read surface, the paper accounts and the live alarm, stated in
        // advance: the Past picks' two halves, the records' and holdings' eight, the paper rows' eight and the alarm's five.
        Assert.True(statements.Length >= 23, $"Read {statements.Length} statement(s) on a trade's table, expected at least 23.");
        Assert.Empty(statements.SelectMany(CloseFigures));

        // The reader is shown to find a close-priced figure read bare and through the trade's own alias, and to pass one
        // read through the fill.
        Assert.NotEmpty(CloseFigures("SELECT result FROM family_trade WHERE ended_on IS NOT NULL;"));
        Assert.NotEmpty(CloseFigures("SELECT t.benchmark FROM family_trade t LEFT JOIN trade_fill f ON f.ticker = t.ticker;"));
        Assert.NotEmpty(CloseFigures("SELECT h.cut_return FROM heavyweight_holding h;"));
        Assert.Empty(CloseFigures("SELECT f.result, f.benchmark FROM family_trade t LEFT JOIN trade_fill f ON f.ticker = t.ticker WHERE t.result IS NOT NULL;"));
    }

    static readonly string[] TradeTables =
    [
        "family_trade", "index_family_trade", "index_rule_trade", "rule_pick",
        "heavyweight_holding", "heavyweight_rule_holding", "index_heavyweight_holding", "index_heavyweight_rule_holding",
    ];

    // Each statement in a source naming a trade's table after FROM or JOIN, read from the strings the queries are written
    // as, each statement ending at its semicolon.
    static IEnumerable<string> TradeStatements(string source) =>
        Regex.Matches(source, "(?:@\"|\\$@\"|\\$\"|\")((?:[^\"]|\"\")*)\"", RegexOptions.Singleline)
            .Select(match => match.Groups[1].Value)
            .SelectMany(text => text.Split(';'))
            .Where(statement => TradeTables.Any(table => Regex.IsMatch(statement, $@"\b(?:FROM|JOIN)\s+{table}\b", RegexOptions.IgnoreCase)));

    // A result, benchmark or size cut's return a statement selects from the trade's own table: read bare or through the
    // alias the statement gives a trade's table, in a select list, its where clause testing whether a trade ended being no
    // figure drawn. One read through any other alias is the fill's joined beside it.
    static IEnumerable<string> CloseFigures(string statement)
    {
        var aliases = TradeTables
            .SelectMany(table => Regex.Matches(statement, $@"\b(?:FROM|JOIN)\s+{table}\s+(\w+)", RegexOptions.IgnoreCase).Select(match => match.Groups[1].Value))
            .Where(alias => !Regex.IsMatch(alias, "^(?:WHERE|ORDER|GROUP|LEFT|JOIN|ON|UNION)$", RegexOptions.IgnoreCase))
            .ToHashSet(StringComparer.Ordinal);

        return Regex.Matches(statement, @"SELECT(.*?)\bFROM\b", RegexOptions.Singleline | RegexOptions.IgnoreCase)
            .SelectMany(select => Regex.Matches(select.Groups[1].Value, @"(?:(\w+)\.)?\b(result|benchmark|cut_return)\b"))
            .Where(match => !match.Groups[1].Success || aliases.Contains(match.Groups[1].Value))
            .Select(match => match.Value);
    }
}
