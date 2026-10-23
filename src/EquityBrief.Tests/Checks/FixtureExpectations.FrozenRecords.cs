using System.Globalization;
using System.Text.Json;
using EquityBrief.Core.Candidates;
using EquityBrief.Core.Cards;
using EquityBrief.Core.Families;
using EquityBrief.Core.Ledger;
using EquityBrief.Core.Loop;
using EquityBrief.Core.Releases;
using EquityBrief.Core.Time;
using EquityBrief.Worker.Candidates;
using EquityBrief.Worker.Cards;
using EquityBrief.Worker.Indices;
using EquityBrief.Worker.Sweep;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Tests.Checks;

// fixture-expectations, 20.11: each family frozen on the S&P 400 or 600 replayed at its own registered settings by its
// sweep's own reading of the index's history, the eight the remedy registers each read at the setting its sweep would run
// and a setting no sweep reads refused; a frozen breakout's record its sweep's own figures for its setting at the next
// open after costs, at its market check's own level, a family with no live rule keeping its provisional replay and a live
// rule its sweep cannot read writing no record, its history written under its name; a pick's card of a family its night
// read by its live rule naming that rule's gates and market check, drawing a record only where the record replays the
// rule and setups like it only where the ledger's history stands under the rule; and the forming list's market check
// and the funnel's parts each the rule's own.
// see: A frozen rule on the S&P 400 or 600 is replayed at its own settings by its sweep's own code, and its card draws that record alone
// see: The setup ledger reads each S&P 400 and 600 family's frozen live rule where one stands, and a card draws setups like a pick only once its history is built under that rule
// see: Each frozen rule on the S&P 400 and 600 reads its market check at the level its own sweep chose
public partial class FixtureExpectations
{
    // The eight freezes the 20.11 remedy registers, each family's live rule on its index at the settings the remedy
    // states, in the remedy's order.
    static readonly (string Evaluator, string Parameters)[] RemedyFreezes =
    [
        ("pullback-400", "strength=0.75,depthLow=1,depthHigh=3,dryUp=1.5,freshness=8,rewardToRisk=2,stopLow=0.5,stopHigh=4,market=0,band=6,floors=1,lowestClose=5,quality=2,hold=0,industryFall=0,fundOverSpy=0,creditAverage=0,creditChange=0,largeBreadth=0,breadthFloor=0.35"),
        ("breakout-400", "highSessions=251,volumeMultiple=1.5,rangeCeiling=1,stopMoves=3,floors=1,lowestClose=5,quality=1,hold=0,industryFall=0,fundOverSpy=0,creditAverage=0,creditChange=0,largeBreadth=1,breadthFloor=0.45,nearness=0,recency=0,highVolume=0"),
        ("drift-400", "windowSessions=3,reactionMoves=1.5,volumeMultiple=2,targetMultiple=2,stopFloor=1,floors=1,lowestClose=5,quality=1,hold=0,industryFall=0,fundOverSpy=0,creditAverage=0,creditChange=0,largeBreadth=0,breadthFloor=0.5,wideWindow=0,peers=0,entry=0,revenueGrew=0"),
        ("heavyweight-400", "design=0,largest=10,lookBack=251,leaders=2,highBeta=1,soldOnLeading=0,soldUnderAverage=1,twelveOne=0,window=0,industries=0,members=0,quality=0,floors=1"),
        ("pullback-600", "strength=0.75,depthLow=1,depthHigh=4,dryUp=1,freshness=8,rewardToRisk=2.5,stopLow=0.5,stopHigh=2.5,market=0,band=2,floors=1,lowestClose=5,quality=2,hold=0,industryFall=0,fundOverSpy=0,creditAverage=0,creditChange=0,largeBreadth=2,breadthFloor=0"),
        ("breakout-600", "highSessions=126,volumeMultiple=1.25,rangeCeiling=0.85,stopMoves=3,floors=1,lowestClose=5,quality=1,hold=0,industryFall=0,fundOverSpy=0,creditAverage=0,creditChange=0,largeBreadth=0,breadthFloor=0.4,nearness=0,recency=0,highVolume=0"),
        ("drift-600", "windowSessions=3,reactionMoves=0.5,volumeMultiple=1.25,targetMultiple=2.5,stopFloor=1,floors=1,lowestClose=5,quality=1,hold=63,industryFall=0,fundOverSpy=0,creditAverage=0,creditChange=0,largeBreadth=2,breadthFloor=0,wideWindow=0,peers=0,entry=0,revenueGrew=1"),
        ("heavyweight-600", "design=0,largest=10,lookBack=63,leaders=1,highBeta=1,soldOnLeading=0,soldUnderAverage=1,twelveOne=0,window=0,industries=0,members=0,quality=0,floors=1"),
    ];

    static IReadOnlyDictionary<string, double> FrozenParameters(string typed) =>
        typed.Split(',').Select(pair => pair.Split('=')).ToDictionary(pair => pair[0], pair => double.Parse(pair[1], CultureInfo.InvariantCulture), StringComparer.Ordinal);

    static IReadOnlyDictionary<string, double> FrozenMoved(IReadOnlyDictionary<string, double> from, params (string Name, double Value)[] moves)
    {
        var moved = new Dictionary<string, double>(from, StringComparer.Ordinal);

        foreach (var (name, value) in moves)
        {
            moved[name] = value;
        }

        return moved;
    }

    // The index sweep's constructed history of a breakout, three members over the weekdays from 2018-01-01 to 2019-05-17,
    // 360 sessions. A rises a hundredth a session and breaks out 5 above the session before on 2019-02-01 on three times its
    // volume, its ranges a third as wide over the ten sessions before, rises a point a session for three and falls 6 on the
    // fourth; B and C fall a hundredth a session, so on the breakout's session A alone of the three closes above its
    // 200-session average and the index's own breadth is a third. Each trades 70,000 shares a session and A 210,000 on its
    // breakout, about $7 million a session, over the S&P 600's $5 million floor and under the S&P 400's $10 million; each
    // files four quarters earning a dollar before 2019; all three are today's members of both indices, read as survivors
    // since no fund's holdings are stored.
    static TemporaryStore FrozenRecordStore()
    {
        var calendar = Weekdays(new DateOnly(2018, 1, 1), 360);
        var breakout = Array.IndexOf(calendar, new DateOnly(2019, 2, 1));
        var a = new decimal[calendar.Length];

        for (var day = 0; day < calendar.Length; day++)
        {
            a[day] = day < breakout ? 100m + (0.01m * day)
                : day == breakout ? a[day - 1] + 5
                : day <= breakout + 3 ? a[day - 1] + 1
                : day == breakout + 4 ? a[day - 1] - 6
                : a[day - 1];
        }

        var store = new TemporaryStore().Migrated();

        using var connection = store.Open();
        using var transaction = connection.BeginTransaction();
        using var insert = connection.CreateCommand();

        insert.Transaction = transaction;
        insert.CommandText = "INSERT INTO bar (ticker, session_date, open, high, low, close, volume, source, observed_at, raw_close) VALUES ($ticker, $day, $close, $high, $low, $close, $volume, 'constructed', '2026-10-06T00:00:00Z', $close);";

        var ticker = insert.Parameters.Add("$ticker", SqliteType.Text);
        var session = insert.Parameters.Add("$day", SqliteType.Text);
        var close = insert.Parameters.Add("$close", SqliteType.Text);
        var high = insert.Parameters.Add("$high", SqliteType.Text);
        var low = insert.Parameters.Add("$low", SqliteType.Text);
        var volume = insert.Parameters.Add("$volume", SqliteType.Integer);

        foreach (var name in new[] { "A", "B", "C" })
        {
            for (var day = 0; day < calendar.Length; day++)
            {
                var price = name == "A" ? a[day] : 100m - (0.01m * day);
                var spread = name == "A" && day >= breakout - 10 && day < breakout ? 0.3m : 1m;

                ticker.Value = name;
                session.Value = calendar[day].ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                close.Value = price.ToString(CultureInfo.InvariantCulture);
                high.Value = (price + spread).ToString(CultureInfo.InvariantCulture);
                low.Value = (price - spread).ToString(CultureInfo.InvariantCulture);
                volume.Value = name == "A" && day == breakout ? 210_000 : 70_000;
                insert.ExecuteNonQuery();
            }
        }

        using var rest = connection.CreateCommand();

        rest.Transaction = transaction;
        rest.CommandText = string.Concat(
            from index in new[] { "MID", "SML" }
            from name in new[] { "A", "B", "C" }
            select $"INSERT INTO pulled_member (index_code, ticker, exchange, name, sector, industry, pull) VALUES ('{index}', '{name}', 'US', NULL, NULL, NULL, 'constructed');")
            + string.Concat(
                from name in new[] { "A", "B", "C" }
                from quarter in new[] { ("2017-12-31", "2018-02-15"), ("2018-03-31", "2018-05-10"), ("2018-06-30", "2018-08-09"), ("2018-09-30", "2018-11-08") }
                select $"INSERT INTO pulled_income (ticker, period_end, filing_date, net_income, operating_income, interest_expense, pull) VALUES ('{name}', '{quarter.Item1}', '{quarter.Item2}', '1.00', '2.00', NULL, 'constructed');");
        rest.ExecuteNonQuery();
        transaction.Commit();

        return store;
    }

    [Fact]
    public async Task EachFreezeTheRemedyRegistersIsReplayedAtTheSettingItsOwnSweepWouldRun()
    {
        using var store = FrozenRecordStore();
        var history = new SweepHistory(store.DatabaseFile);
        var through = await history.NewestSessionAsync();
        var large = await IndexSweepRunner.OwnSwingRead.LargeAsync(history, through, _ => { }, default);
        var companies = await history.HeavyweightAsync(through);
        var income = await history.IncomeAsync(through);
        var reads = new Dictionary<string, (IndexSweepRunner.OwnSwingRead Breakout, IndexSweepRunner.OwnSwingRead Drift, IndexSweepRunner.OwnPullbackRead Pullback)>(StringComparer.Ordinal);

        foreach (var index in new[] { "MID", "SML" })
        {
            var inputs = await history.ReadAsync(through, _ => { }, default, index: index, asItStood: true);
            var laid = IndexSweepRunner.OwnSwingRead.Laid(index, inputs, companies, income, through);

            reads[index] = (
                await IndexSweepRunner.OwnSwingRead.ReadAsync(laid, large, BreakoutRule.Name, history, through, 0, default),
                await IndexSweepRunner.OwnSwingRead.ReadAsync(laid, large, DriftRule.Name, history, through, 1, default),
                await IndexSweepRunner.OwnPullbackRead.ReadAsync(laid, large, history, through, _ => { }, default));
        }

        SweepRelease(store);

        // A registration's setting as its own sweep on the index's own history reads it, by the key the sweep names it by,
        // or why it reads none.
        (string? Key, string? Refused) Read(string evaluator, IReadOnlyDictionary<string, double> parameters)
        {
            var carried = IndexEvaluator(evaluator);
            var (breakout, drift, pullback) = reads[carried.Index];

            switch (carried)
            {
                case IndexBreakoutCandidate:
                {
                    var (at, refused) = RuleReplay.OwnSwing(breakout, carried, parameters);

                    return (at?.Key, refused);
                }

                case IndexDriftCandidate:
                {
                    var (at, refused) = RuleReplay.OwnSwing(drift, carried, parameters);

                    return (at?.Key, refused);
                }

                case IndexPullbackCandidate:
                {
                    var (at, refused) = RuleReplay.OwnPullback(pullback, carried, parameters);

                    return (at?.Key, refused);
                }

                default:
                {
                    var (at, refused) = RuleReplay.OwnBook(parameters);

                    return (at?.Key, refused);
                }
            }
        }

        // Each of the eight is a setting its family's registration form admits.
        Assert.All(RemedyFreezes, freeze => Assert.Null(IndexRules.Refusal(IndexEvaluator(freeze.Evaluator), FrozenParameters(freeze.Parameters))));

        // Each read at the setting its 20.11 sweep would run, worked by hand from its registration and the key each sweep's
        // run wrote: the pullbacks on the search's own grid, the S&P 400's own market dial at 35 per cent where its
        // registration states its check on its own breadth at that floor and the S&P 600's off where it states none, each
        // crossed with the profit and cover check its quality of 2 states; the breakouts at the market check's sixth level,
        // the S&P 500's breadth, and its third, the S&P 600's own breadth at 40 per cent; the S&P 400's drift at its own
        // breadth at 50 per cent, its search's strongest once the first pullback's entry was corrected, the S&P 600's
        // reading none, held 63 sessions and its revenue grown; and each book at design (a) with no quality gate and the
        // floor once.
        Assert.Equal(
            [
                "strength 0.75, depth low 1, depth high 3, dry-up 1.5, freshness 8, reward to risk 2, nearest stop 0.5, farthest stop 4, market 35%, band strength 6 with quality, profit and cover",
                "high=251|volume=1.5|ceiling=1|stop=3|market=5",
                "window=3|reaction=1.5|volume=2|target=2|market=4",
                "quality=off|floors=1x|size=10|look-back=251|leaders=2|sector=members|beta=at least 1|rebalance=month|exit=break",
                "strength 0.75, depth low 1, depth high 4, dry-up 1, freshness 8, reward to risk 2.5, nearest stop 0.5, farthest stop 2.5, market off, band strength 2 with quality, profit and cover",
                "high=126|volume=1.25|ceiling=0.85|stop=3|market=2",
                "window=3|reaction=0.5|volume=1.25|target=2.5|market=0 with hold, 63 sessions; revenue, grew on the quarter a year before, both as first filed and stored before the night",
                "quality=off|floors=1x|size=10|look-back=63|leaders=1|sector=members|beta=at least 1|rebalance=month|exit=break",
            ],
            RemedyFreezes.Select(freeze => Read(freeze.Evaluator, FrozenParameters(freeze.Parameters)).Key));

        // The market check's level, in the order the sweep lays its seven: the S&P 400 breakout's on the S&P 500's breadth
        // the sixth, the S&P 400 drift's own breadth at 50 per cent the fifth, the S&P 600 breakout's at 40 the third and
        // the S&P 600 drift's none the first, each the level the sweep names by that key.
        Assert.Equal([5, 4, 2, 0], new[] { 1, 2, 5, 6 }.Select(at => RuleReplay.MarketLevelOf(FrozenParameters(RemedyFreezes[at].Parameters))!.Value));
        Assert.Equal(["S&P 500 0.45", "own 0.50", "own 0.40", "off"], new[] { 5, 4, 2, 0 }.Select(level => IndexSweepRunner.MarketLevels("SML")[level].Key));

        // Three settings no sweep on the index's own history reads, each refused with why and no key: the S&P 400's drift
        // switched on HYG above its average, a market switch the replay reads none of as a level; its pullback held 21
        // sessions, where its search's second stage read holds of 20 and 40; and its book on twelve months' strength.
        Assert.Equal(
            new (string? Key, string? Refused)[]
            {
                (null, "its creditAverage at 1 is a level its replay reads none of"),
                (null, "its hold at 21 is no level its sweep's second stage read"),
                (null, "it reads twelve months' strength skipping the latest month, which its design's sweep at the next open read at no setting"),
            },
            new (string Evaluator, IReadOnlyDictionary<string, double> Parameters)[]
            {
                ("drift-400", FrozenMoved(FrozenParameters(RemedyFreezes[2].Parameters), (IndexRuleCandidate.CreditAverageParameter, 1))),
                ("pullback-400", FrozenMoved(FrozenParameters(RemedyFreezes[0].Parameters), (IndexRuleCandidate.HoldParameter, 21))),
                ("heavyweight-400", FrozenMoved(FrozenParameters(RemedyFreezes[3].Parameters), (IndexHeavyweightCandidate.TwelveOneParameter, 1))),
            }.Select(refused => Read(refused.Evaluator, refused.Parameters)));
    }

    [Fact]
    public async Task AFrozenRulesRecordIsItsOwnSweepsReadingOfItsSettingAndAFamilyWithoutOneKeepsItsProvisionalReplay()
    {
        using var store = FrozenRecordStore();
        using var sweeps = new TemporaryDirectory();
        var noon = new DateTimeOffset(2026, 10, 6, 12, 0, 0, TimeSpan.Zero);
        var clock = new SweepClock(noon);

        // The S&P 600's breakout swept on its own history at the next open, at every setting of its grid and every level of
        // its market check.
        Assert.Equal(0, await new IndexSweepRunner(clock, store.DatabaseFile, store.Root, sweeps.Path, TextWriter.Null).RunAsync("SML", IndexSweepRunner.BreakoutOwn, survivorsOnly: true));
        SweepRelease(store);

        using var figures = JsonDocument.Parse(File.ReadAllText(Path.Combine(sweeps.Path, "20261006T120000Z", IndexSweepRunner.FiguresFile)));

        JsonElement AfterCosts(string key) => figures.RootElement.GetProperty("afterCosts").EnumerateArray().Single(one => one.GetProperty("Key").GetString() == key);

        // Its breakout frozen at its provisional dials reading no market check, and its drift frozen switched on HYG above
        // its average, which no sweep on the index's own history reads as a level.
        var breakout = IndexEvaluator("breakout-600");
        var off = FrozenMoved(IndexRules.Provisional(breakout), (IndexRuleCandidate.LargeBreadthParameter, IndexRuleCandidate.NoMarketCheck), (IndexRuleCandidate.BreadthFloorParameter, 0));
        var drift = IndexEvaluator("drift-600");
        var credit = FrozenMoved(IndexRules.Provisional(drift), (IndexRuleCandidate.CreditAverageParameter, 1));

        Assert.Equal(0, (await RegisterVerbAt(store, noon.AddMinutes(1), RegisterVerb.IndexFamily, "breakout", "--index", "SML", "--parameters", Typed(off))).Code);
        Assert.Equal(0, (await RegisterVerbAt(store, noon.AddMinutes(2), RegisterVerb.IndexFamily, "drift", "--index", "SML", "--parameters", Typed(credit))).Code);

        clock.UtcNow = noon.AddMinutes(5);

        using var said = new StringWriter();

        Assert.Equal(0, await new RuleRecorder(clock, store.DatabaseFile, store.Root, said).RunAsync(["SML"]));

        // Three rows: the frozen breakout's, and the pullback's and the heavyweights' provisional replays; none for the
        // drift, whose live rule the sweep's reading cannot read, the run's row saying so.
        Assert.Equal(
            [
                $"breakout|{IndexRules.Words(breakout, off)}|high=126|volume=1.5|ceiling=0.85|stop=1.5|market=0",
                $"heavyweight|{RuleReplay.HeavyweightWords(IndexHeavyweights.Provisional)}|{IndexHeavyweights.Provisional.Key}",
                $"pullback|{RuleReplay.PullbackWords}|{(SweepIdeas.BaseRule with { PerNight = SetupFamilies.ListedANight }).Key}",
            ],
            TextRows(store, "SELECT family || '|' || rule || '|' || settings FROM rule_record WHERE index_code = 'SML' ORDER BY family;"));
        Assert.Contains(
            $"S&P 600 drift: its live rule, the {IndexRules.Words(drift, credit)}, is not replayed, since its creditAverage at 1 is a level its replay reads none of, and no record is written for it",
            Text(store, "SELECT detail FROM run_log WHERE stage = 'rule-record';"),
            StringComparison.Ordinal);

        // The frozen breakout's record is its sweep's own reading of its setting with the market check off: A's breakout
        // alone, bought at the open after it and sold at the fourth session's close on its trail, its result after its round
        // trip the sweep's own to twelve places, a trailing rule's trade winning nothing.
        var (trades, average, median, won, endedBy) = FrozenBreakoutRecord(store);
        var key = "high=126|volume=1.5|ceiling=0.85|stop=1.5|market=0";

        Assert.Equal((1, 1), (trades, AfterCosts(key).GetProperty("Trades").GetInt32()));
        Assert.Equal(AfterCosts(key).GetProperty("Result").GetDouble(), average!.Value, 12);
        Assert.Equal(((int?)4, (double?)null, "[0,0,0,1]"), (median, won, endedBy));

        // The history the cards' stretch line reads, replayed by the same reading and written under the live rule's name:
        // one of the 98 sessions from 2019-01-02 to 2019-05-17 listing A, 2019-02-01, and the drift's not written.
        clock.UtcNow = noon.AddMinutes(10);

        var live = Text(store, "SELECT candidate FROM candidate_register WHERE candidate LIKE 'the live S&P 600 breakout%';");

        Assert.Equal(0, await new RuleRecorder(clock, store.DatabaseFile, store.Root, TextWriter.Null).NightsAsync(["SML"]));
        Assert.Equal(["98|1|2019-02-01"], TextRows(store, $"SELECT COUNT(*) || '|' || SUM(listed) || '|' || MAX(CASE WHEN listed > 0 THEN session_date END) FROM rule_night WHERE index_code = 'SML' AND family = 'breakout' AND rule = '{live.Replace("'", "''", StringComparison.Ordinal)}' AND source = 'history';"));
        Assert.Equal(["breakout", "pullback"], TextRows(store, "SELECT DISTINCT family FROM rule_night WHERE index_code = 'SML' AND source = 'history' ORDER BY family;"));

        // Frozen again at its own breadth at 35 per cent: on the breakout's session a third of the members stand above their
        // average, under the floor, so its sweep's reading at that level lists none and its record holds no trade.
        Assert.Equal(0, (await RegisterVerbAt(store, noon.AddMinutes(11), "--retire", live, "--evidence", "frozen again at its own breadth")).Code);
        Assert.Equal(0, (await RegisterVerbAt(store, noon.AddMinutes(12), RegisterVerb.IndexFamily, "breakout", "--index", "SML", "--parameters", Typed(FrozenMoved(off, (IndexRuleCandidate.LargeBreadthParameter, 0), (IndexRuleCandidate.BreadthFloorParameter, 0.35))))).Code);

        clock.UtcNow = noon.AddMinutes(15);

        Assert.Equal(0, await new RuleRecorder(clock, store.DatabaseFile, store.Root, TextWriter.Null).RunAsync(["SML"]));
        Assert.Equal(["high=126|volume=1.5|ceiling=0.85|stop=1.5|market=1|0"], TextRows(store, "SELECT settings || '|' || trades FROM rule_record WHERE index_code = 'SML' AND family = 'breakout';"));
        Assert.Equal(0, AfterCosts("high=126|volume=1.5|ceiling=0.85|stop=1.5|market=1").GetProperty("Trades").GetInt32());
    }

    // The record command's start guard, on the operator's ruling of the evening of 2026-10-20: a run is refused where it
    // would still be running at a weekday's 23:00 UTC, one whose end falls past midnight among them. The window read by
    // hand at its edges, a Sunday's evening holding no night and a Saturday's run reaching Monday's 23:00 refused; then the
    // nights' form, given two hours, started at 22:00 on Monday 2026-10-19 would end at midnight and is refused before it
    // reads anything.
    [Fact]
    public async Task TheRecordCommandRefusesARunWhoseEndCrossesMidnightIntoTheNightsWindow()
    {
        var twoHours = RuleRecorder.Expected + RuleRecorder.Expected;

        Assert.True(SweepRunner.InTheNightsWindow(SweepUtc(10, 19, 22, 0), twoHours));
        Assert.True(SweepRunner.InTheNightsWindow(SweepUtc(10, 19, 22, 59), twoHours));
        Assert.True(SweepRunner.InTheNightsWindow(SweepUtc(10, 23, 22, 30), twoHours));
        Assert.False(SweepRunner.InTheNightsWindow(SweepUtc(10, 19, 20, 59), twoHours));
        Assert.False(SweepRunner.InTheNightsWindow(SweepUtc(10, 18, 22, 0), twoHours));
        Assert.False(SweepRunner.InTheNightsWindow(SweepUtc(10, 20, 0, 30), twoHours));
        Assert.False(SweepRunner.InTheNightsWindow(SweepUtc(10, 24, 22, 0), TimeSpan.FromHours(26)));
        Assert.True(SweepRunner.InTheNightsWindow(SweepUtc(10, 24, 22, 0), TimeSpan.FromHours(49)));

        var root = Path.Combine(Path.GetTempPath(), "equitybrief-tests", Guid.NewGuid().ToString("n"));
        var clock = new SweepClock(SweepUtc(10, 19, 22, 0));

        using var said = new StringWriter();

        Assert.Equal(2, await new RuleRecorder(clock, Path.Combine(root, "store.db"), root, said).NightsAsync([]));
        Assert.Contains("a run started now would reach the night's window", said.ToString(), StringComparison.Ordinal);
    }

    static (int Trades, double? Average, int? Median, double? Won, string EndedBy) FrozenBreakoutRecord(TemporaryStore store)
    {
        using var connection = store.Open();
        using var command = connection.CreateCommand();

        command.CommandText = "SELECT trades, average, median_sessions, won, ended_by FROM rule_record WHERE index_code = 'SML' AND family = 'breakout';";

        using var reader = command.ExecuteReader();

        Assert.True(reader.Read(), "No record of the S&P 600's breakout is stored.");

        return (
            reader.GetInt32(0),
            reader.IsDBNull(1) ? null : reader.GetDouble(1),
            reader.IsDBNull(2) ? null : reader.GetInt32(2),
            reader.IsDBNull(3) ? null : reader.GetDouble(3),
            reader.GetString(4));
    }

    // The S&P 400 of the index families' store on a night its own breadth was 60 per cent, its breakout frozen on the S&P
    // 500's breadth and its pullback on its own breadth at 35 per cent, its drift read by its provisional rule: IA listed
    // by the breakout, IB by the pullback and ID by the drift, each with its market check stored as its rule read it, the
    // S&P 500's breadth at 55 per cent for IA's; the newest tester run on the index fitting the breakout a score on the close
    // over its 20-session average, which passed nothing; and the ledger's forty finished breakout setups, read under the
    // breakout's provisional setting.
    static (TemporaryStore Store, string Breakout, string Pullback) FrozenCardsStore()
    {
        var store = IndexStore();
        var breakout = IndexEvaluator("breakout-400");
        var pullback = IndexEvaluator("pullback-400");
        var onLarge = FrozenMoved(IndexRules.Provisional(breakout), (IndexRuleCandidate.LargeBreadthParameter, 1), (IndexRuleCandidate.BreadthFloorParameter, IndexRuleCandidate.LargeBreadthFloor));
        var atOwn = FrozenMoved(IndexRules.Provisional(pullback), (IndexPullbackCandidate.MarketParameter, 0), (IndexRuleCandidate.BreadthFloorParameter, 0.35));
        IReadOnlyList<IndexRule> rules =
        [
            new IndexRule("the live S&P 400 " + IndexRules.Words(breakout, onLarge), breakout, onLarge, FreezeAt),
            new IndexRule("the live S&P 400 " + IndexRules.Words(pullback, atOwn), pullback, atOwn, FreezeAt),
        ];

        static string Gates(params EquityBrief.Core.Filter.Gate[] gates) => FamilyRule.GatesJson(gates).Replace("'", "''", StringComparison.Ordinal);

        store.Execute($"INSERT INTO index_family_night (index_code, session_date, members, breadth, market_open, settings, rebalanced) VALUES ('MID', '{IndexNight}', 7, 0.6, 1, '{IndexFamilies.Settings("MID", rules).Replace("'", "''", StringComparison.Ordinal)}', 0);");
        store.Execute(
            "INSERT INTO index_family_result (index_code, session_date, ticker, family, passed, place, entry, stop, target, trail, cap, order_by, reason, gates) VALUES " +
            $"('MID', '{IndexNight}', 'IA', 'breakout', 1, 1, '102', '99', NULL, '3', 63, 2.0, NULL, '{Gates(IndexNightRead.MarketGate(0.55, IndexRuleCandidate.LargeBreadthFloor, IndexNightRead.LargeBreadth), IndexNightRead.SetupGateOf(true))}'), " +
            $"('MID', '{IndexNight}', 'IB', 'pullback', 1, 1, '102', '100', '106', NULL, 63, 2.0, NULL, '{Gates(IndexNightRead.MarketGate(0.6, 0.35), IndexNightRead.SetupGateOf(true))}'), " +
            $"('MID', '{IndexNight}', 'ID', 'drift', 1, 1, '102', '98', '110', NULL, 60, 2.0, NULL, '{Gates(IndexNightRead.MarketGate(0.6, 0.45), IndexNightRead.SetupGateOf(true))}');");
        store.Execute(
            "INSERT INTO index_family_pick (index_code, session_date, ticker, family, state, place, also, held_index, held_family, held_night) VALUES " +
            $"('MID', '{IndexNight}', 'IA', 'breakout', 'listed', 1, '[]', NULL, NULL, NULL), " +
            $"('MID', '{IndexNight}', 'IB', 'pullback', 'listed', 1, '[]', NULL, NULL, NULL), " +
            $"('MID', '{IndexNight}', 'ID', 'drift', 'listed', 1, '[]', NULL, NULL, NULL);");

        // The records an earlier run stored: the breakout's and the drift's provisional replays, and the frozen pullback's.
        foreach (var (family, rule) in new[] { ("breakout", RuleReplay.SwingWords(BreakoutRule.Name, IndexNightRead.BreakoutAsFrozen)), ("pullback", IndexRules.Words(pullback, atOwn)), ("drift", RuleReplay.SwingWords(DriftRule.Name, IndexNightRead.DriftAsFrozen)) })
        {
            store.Execute(
                "INSERT INTO rule_record (index_code, family, rule, settings, recorded_at, first_session, last_session, membership, unit, trades, won, average, median_sessions, ended_by, worst_close) VALUES " +
                $"('MID', '{family}', '{rule.Replace("'", "''", StringComparison.Ordinal)}', 'a setting', '2026-10-01T12:00:00Z', '2019-01-02', '2026-09-30', 'as it stood', 'risks', 20, 0.5, 0.12, 10, '{RuleRecordFigures.EndedByJson([.. Enumerable.Repeat(1, 20)])}', -0.6);");
        }

        var model = new RidgeModel(
            [LedgerReadings.IndexOf("close_over_twenty")],
            [1.0],
            [0.1],
            [0.2],
            [0.0, 0.0],
            0,
            400,
            -1,
            1,
            new Dictionary<string, IReadOnlyList<double>>(StringComparer.Ordinal) { ["MID"] = [.. Enumerable.Range(0, 101).Select(hundredth => 1 + (0.02 * hundredth))] });

        store.Execute($"INSERT INTO loop_run (run_id, month, index_code, through, started_at, ended_at, folds) VALUES ('loop-test-MID-1', '2026-10', 'MID', '{IndexNight}', '2026-10-01T12:00:00Z', '2026-10-01T12:01:00Z', 5);");
        store.Execute(
            "INSERT INTO loop_model (run_id, index_code, family, kind, year, learned_from, learned_before, setups, readings, parameters, hash, pin, words) " +
            $"VALUES ('loop-test-MID-1', 'MID', 'breakout', 'ridge', 2027, '2019-01-02', '2026-10-03', 400, 'close_over_twenty', '{model.Canonical()}', '{model.Hash}', '{RidgeScore.Version}', 'the score weighs most close_over_twenty higher');");

        for (var at = 0; at < 40; at++)
        {
            var day = new DateOnly(2025, 1, 2).AddDays(at).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            var ended = new DateOnly(2025, 1, 2).AddDays(at + 7).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

            store.Execute(
                "INSERT INTO setup (index_code, family, ticker, session_date, rule, live_pass, picked, entry, stop, cap, risk_moves, close_over_twenty, result, benchmark, edge, sessions, end, ended_on, settled, source, pin) " +
                FormattableString.Invariant($"VALUES ('MID', 'breakout', 'S{at:00}', '{day}', '{RuleReplay.SwingWords(BreakoutRule.Name, IndexNightRead.BreakoutAsFrozen)}', 1, NULL, '50', '48', 63, 1.0, {1 + (0.001 * at)}, {(at % 2 == 0 ? 1 : -1)}, 0, {(at % 2 == 0 ? 1 : -1)}, 5, 'target', '{ended}', 1, 'history', '{LedgerReadings.Version}');"));
        }

        return (store, rules[0].Candidate, rules[1].Candidate);
    }

    [Fact]
    public async Task AFrozenFamilysCardNamesItsRulesGatesAndCheckAndDrawsItsRecordAndItsSetupsLikeItOnlyUnderThatRule()
    {
        var (store, _, _) = FrozenCardsStore();

        using var held = store;
        var clock = new SweepClock(new DateTimeOffset(2026, 10, 2, 23, 50, 0, TimeSpan.Zero));
        var breakoutWords = IndexRules.Words(IndexEvaluator("breakout-400"), FrozenMoved(IndexRules.Provisional(IndexEvaluator("breakout-400")), (IndexRuleCandidate.LargeBreadthParameter, 1), (IndexRuleCandidate.BreadthFloorParameter, IndexRuleCandidate.LargeBreadthFloor)));

        CardSimilar SimilarOf(string ticker) => CardSimilar.Read(Text(store, $"SELECT similar FROM decision_card WHERE ticker = '{ticker}';"));

        await new DecisionCards(clock, store.DatabaseFile).RunAsync("cards-1");

        var cards = Cards(store);

        // Three cards, one a pick.
        Assert.Equal(["MID breakout IA", "MID drift ID", "MID pullback IB"], cards.Keys.Order(StringComparer.Ordinal));

        // IA's breakout, read by its live rule on the S&P 500's breadth: its first line names the index's floors and the
        // rule in its registration's words in place of the provisional rule's profit gate, market check and setting; its
        // market line reads the S&P 500's breadth its rule read, 55 per cent against 45, and not the S&P 400's own; the
        // record stored replays the provisional rule, so none is drawn; and the ledger's setups were read under the
        // provisional setting, so the setups like it wait for the history built under the rule.
        var breakout = cards["MID breakout IA"];

        Assert.Equal(
            $"Passed every gate of the {SetupFamilies.Named(BreakoutRule.Name)!.Label.ToLowerInvariant()} on the S&P 400: floors, a close of at least $5 and 10 million dollars a session over 50 sessions; its rule, {breakoutWords}.",
            breakout.Lines[0].Words);
        Assert.StartsWith("The S&P 500's breadth is 55.0% against its floor of 45%", breakout.Lines[2].Words, StringComparison.Ordinal);
        Assert.DoesNotContain("a check this rule does not read", breakout.Lines[2].Words, StringComparison.Ordinal);
        Assert.Null(breakout.Record);
        Assert.Equal(DecisionCards.LedgerWaits, SimilarOf("IA").Unmatched);

        // IB's pullback, read by its live rule on its own breadth at 35 per cent: its market line against that floor, and the
        // record stored replays that rule, so it is drawn, three in four of its twenty trades ended by the fifteenth session.
        var pullback = cards["MID pullback IB"];

        Assert.StartsWith("The S&P 400's breadth is 60.0% against its floor of 35%", pullback.Lines[2].Words, StringComparison.Ordinal);
        Assert.DoesNotContain("a check this rule does not read", pullback.Lines[2].Words, StringComparison.Ordinal);
        Assert.Equal(15, pullback.Record!.Value.GetProperty("heldSessions").GetInt32());

        // ID's drift, read by its provisional rule: its card as before, its provisional setting and check named and the
        // record stored drawn, since it replays the provisional rule.
        var drift = cards["MID drift ID"];

        Assert.Contains("its setting, window 3, reaction 0.5, volume 2, target 2.5", drift.Lines[0].Words, StringComparison.Ordinal);
        Assert.Contains("market, the index's breadth at or above 45%", drift.Lines[0].Words, StringComparison.Ordinal);
        Assert.StartsWith("The S&P 400's breadth is 60.0% against its floor of 45%", drift.Lines[2].Words, StringComparison.Ordinal);
        Assert.DoesNotContain("a check this rule does not read", drift.Lines[2].Words, StringComparison.Ordinal);
        Assert.NotNull(drift.Record);

        // The rule recorder replays the frozen breakout and the ledger's history is built under it, while a night before
        // the freeze wrote S39's row under the provisional rule: the night run again draws the record and matches the
        // setups like IA under the live rule alone, all thirty-nine of thirty-nine stocks, none of them IA and not S39.
        store.Execute($"UPDATE rule_record SET rule = '{breakoutWords.Replace("'", "''", StringComparison.Ordinal)}' WHERE index_code = 'MID' AND family = 'breakout';");
        store.Execute($"UPDATE setup SET rule = '{breakoutWords.Replace("'", "''", StringComparison.Ordinal)}' WHERE index_code = 'MID' AND family = 'breakout' AND ticker <> 'S39';");
        store.Execute("UPDATE setup SET source = 'night' WHERE index_code = 'MID' AND family = 'breakout' AND ticker = 'S39';");

        await new DecisionCards(clock, store.DatabaseFile).RunAsync("cards-2");

        var matched = SimilarOf("IA");

        Assert.Equal(15, Cards(store)["MID breakout IA"].Record!.Value.GetProperty("heldSessions").GetInt32());
        Assert.Null(matched.Unmatched);
        Assert.Equal((39, 39), (matched.Count!.Value, matched.RuleSetups!.Value));
    }

    // A card a night drew by its family's provisional rule, on the operator's ruling of the evening of 2026-10-20: ID's
    // drift, read by its provisional rule, draws the record stored while it replays that rule; replayed again under the
    // S&P 400 drift's frozen rule, as a freeze's remedy writes it, the night run again, still reading the drift by its
    // provisional rule, draws none, the record being the frozen rule's.
    [Fact]
    public async Task AProvisionalCardDrawsNoRecordWhereTheStoredRecordReplaysAFrozenRule()
    {
        var (store, _, _) = FrozenCardsStore();

        using var held = store;
        var clock = new SweepClock(new DateTimeOffset(2026, 10, 2, 23, 50, 0, TimeSpan.Zero));

        await new DecisionCards(clock, store.DatabaseFile).RunAsync("cards-1");

        Assert.Equal(20, Cards(store)["MID drift ID"].Record!.Value.GetProperty("trades").GetInt32());

        var frozen = IndexRules.Words(IndexEvaluator("drift-400"), FrozenParameters(RemedyFreezes[2].Parameters));

        store.Execute($"UPDATE rule_record SET rule = '{frozen.Replace("'", "''", StringComparison.Ordinal)}' WHERE index_code = 'MID' AND family = 'drift';");

        await new DecisionCards(clock, store.DatabaseFile).RunAsync("cards-2");

        Assert.Null(Cards(store)["MID drift ID"].Record);
        Assert.Equal(15, Cards(store)["MID pullback IB"].Record!.Value.GetProperty("heldSessions").GetInt32());
    }

    [Fact]
    public async Task TheFormingListReadsTheMarketCheckOfTheRuleDrawingTheBreakoutsList()
    {
        // The S&P 400 of the closed index store, seven of seventeen members above their average, a breadth of 0.41 under
        // the provisional rule's floor of 45 per cent. NH closes at 100.2, 0.8 under the 126-session high of 101 its year
        // made, under a typical move and with its ranges narrowing: forming a breakout wherever the check is open, and
        // no other member, each of the rest above its high or eleven points under it.
        using var store = ClosedIndexStore();
        var night = new DateTimeOffset(2026, 10, 2, 23, 40, 0, TimeSpan.Zero);
        var clock = FixedClock.At(night, SessionZones.UnitedStates);

        async Task<IReadOnlyList<string>> FormingAsync(IReadOnlyList<RegisterRow> register, string run)
        {
            await new IndexFamilies(clock, store.DatabaseFile).RunAsync(run, default, register, night);
            await new RuleCards(clock, store.DatabaseFile).RunAsync(run + "-cards", register, night);

            return TextRows(store, "SELECT rule || '|' || ticker FROM forming_row WHERE index_code = 'MID' ORDER BY place;");
        }

        // Read by the provisional rule, the index's own breadth under 45 per cent: none forms.
        Assert.Empty(await FormingAsync([], "night-1"));

        // Frozen on its own breadth at 40 per cent, the breadth of 0.41 at or above it: NH forms under the live rule, while
        // the night's own check of the index, at the provisional rule's floor, still reads closed.
        var own = FrozenMoved(IndexRules.Provisional(IndexEvaluator("breakout-400")), (IndexRuleCandidate.BreadthFloorParameter, 0.40));

        Assert.Equal(0, (await RegisterVerbAt(store, FreezeAt, RegisterVerb.IndexFamily, "breakout", "--index", "MID", "--parameters", Typed(own))).Code);

        var register = await new CandidateRegistrar(clock, store.DatabaseFile).RowsAsync();
        var live = register.Single(row => FamilyRecords.IsLive(row.Candidate)).Candidate;

        Assert.Equal([$"{live}|NH"], await FormingAsync(register, "night-2"));
        Assert.Equal(0, Scalar(store, "SELECT market_open FROM index_family_night WHERE index_code = 'MID';"));

        // The market check a family's stored parts hold is the gate the forming list reads, and a night storing no part
        // reads none.
        Assert.True(RuleCards.MarketOf(FamilyRule.GatesJson([IndexNightRead.MarketGate(7.0 / 17.0, 0.40), IndexNightRead.SetupGateOf(false)]))!.Passed);
        Assert.False(RuleCards.MarketOf(FamilyRule.GatesJson([IndexNightRead.MarketGate(7.0 / 17.0, 0.45)]))!.Passed);
        Assert.True(RuleCards.MarketOf(FamilyRule.GatesJson([IndexNightRead.NoMarketGate]))!.Passed);
        Assert.Null(RuleCards.MarketOf(null));
    }

    [Fact]
    public void AnIndexRulesFunnelCountsALevelItsRuleReadsAsAPartOfItsOwnAfterTheCover()
    {
        // Ten members of a night a frozen drift reading no market check and its print's revenue read: five with no setup,
        // two its revenue level kept off and three passing. Every member reached the market check's part and the setup's;
        // five passed the setup and each part to the cover; three the revenue.
        IReadOnlyList<(string Family, bool Passed, string? Reason)> answers =
        [
            .. Enumerable.Repeat<(string, bool, string?)>((DriftRule.Name, false, IndexNightRead.NoSetup), 5),
            .. Enumerable.Repeat<(string, bool, string?)>((DriftRule.Name, false, IndexRules.RevenueNotAbove), 2),
            .. Enumerable.Repeat<(string, bool, string?)>((DriftRule.Name, true, null), 3),
        ];

        Assert.Equal(
            [(RuleRows.Market, 10), (RuleRows.Setup, 5), (RuleRows.Floors, 5), (RuleRows.Profit, 5), (RuleRows.Cover, 5), (RuleRows.Revenue, 3)],
            RuleCards.IndexFunnel(answers, DriftRule.Name));

        // A tone variant names the tone's two steps after the levels though no member failed them, as it did before.
        Assert.Equal(
            [(RuleRows.Market, 10), (RuleRows.Setup, 5), (RuleRows.Floors, 5), (RuleRows.Profit, 5), (RuleRows.Cover, 5), (RuleRows.Revenue, 3), (ReleaseTone.ReadStep, 3), (ReleaseTone.Gate, 3)],
            RuleCards.IndexFunnel(answers, DriftRule.Name, tone: true));

        // A family whose members failed no level reads the five parts it always read.
        Assert.Equal(
            [(RuleRows.Market, 8), (RuleRows.Setup, 3), (RuleRows.Floors, 3), (RuleRows.Profit, 3), (RuleRows.Cover, 3)],
            RuleCards.IndexFunnel([.. answers.Where(answer => answer.Reason != IndexRules.RevenueNotAbove)], DriftRule.Name));
    }
}
