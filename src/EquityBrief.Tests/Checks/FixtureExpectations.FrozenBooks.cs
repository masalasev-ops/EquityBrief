using System.Globalization;
using EquityBrief.Core.Candidates;
using EquityBrief.Core.Families;
using EquityBrief.Core.Time;
using EquityBrief.Tests.Harness;
using EquityBrief.Worker.Cards;
using EquityBrief.Worker.Indices;
using EquityBrief.Worker.Research;

namespace EquityBrief.Tests.Checks;

// fixture-expectations, the 15.5 correction of 2026-10-20: an S&P 600 night that read its sector heavyweights by a frozen
// live rule draws that rule's own book on its page, so the night's cards, its report requests and the taken trades'
// follower read that book's buys and sales, worked by hand over one constructed night, and not the index's own book's.
// see: A rule of the S&P 400's or 600's sector heavyweights keeps a book of its own in either design, read by the index families' step
public partial class FixtureExpectations
{
    const string FrozenBookNight = "2026-10-01";

    const string FrozenBook = "the live S&P 600 heavyweights rule of the constructed night";

    // The night: the S&P 600's own book bought S1 and S2 and holds both; the frozen live rule, one leader a sector, bought
    // S2 on its own book and sold it on 2026-10-06; the night's settings name that rule as the one it read the family by.
    static TemporaryStore FrozenBookStore()
    {
        var store = new TemporaryStore().Migrated();
        var evaluator = HeavyweightIndexEvaluator("heavyweight-600");
        var parameters = new Dictionary<string, double>(IndexRules.Provisional(evaluator), StringComparer.Ordinal) { [IndexHeavyweightCandidate.LeadersParameter] = 1 };
        var registered = new DateTimeOffset(2026, 9, 30, 12, 0, 0, TimeSpan.Zero);
        var settings = IndexFamilies.Settings("SML", [new IndexRule(FrozenBook, evaluator, parameters, registered)]).Replace("'", "''", StringComparison.Ordinal);

        store.Execute(
            "INSERT INTO candidate_register (id, candidate, rule, test, evaluator, parameters, evaluator_version, event, retires, registered_at, evidence) " +
            $"VALUES (1, '{FrozenBook}', 'the rule', 'the test', 'heavyweight-600', '{CandidateEvaluator.Write(parameters)}', '{evaluator.Version}', 'registered', NULL, '2026-09-30T12:00:00Z', NULL);");
        store.Execute($"INSERT INTO index_family_night (index_code, session_date, members, breadth, market_open, settings, rebalanced) VALUES ('SML', '{FrozenBookNight}', 2, 0.6, 1, '{settings}', 1);");
        store.Execute(
            "INSERT INTO index_heavyweight_holding (index_code, ticker, entered_on, sector, entry_close, growth, cut, through) VALUES " +
            $"('SML', 'S1', '{FrozenBookNight}', 'Energy', '10', 1.0, '[]', '{FrozenBookNight}'), ('SML', 'S2', '{FrozenBookNight}', 'Utilities', '20', 1.0, '[]', '{FrozenBookNight}');");
        store.Execute(
            "INSERT INTO index_heavyweight_rule_holding (candidate, index_code, ticker, entered_on, sector, entry_close, growth, cut, through, ended_on, exit_close, reason, result, cut_return, cost) VALUES " +
            $"('{FrozenBook}', 'SML', 'S2', '{FrozenBookNight}', 'Utilities', '20', 1.1, '[]', '2026-10-06', '2026-10-06', '22', 'no longer the leader', 0.1, 0.02, 0.001);");

        foreach (var (ticker, close) in new[] { ("S1", "10"), ("S2", "20") })
        {
            store.Execute(
                "INSERT INTO bar (ticker, session_date, open, high, low, close, volume, source, observed_at, raw_close) VALUES " +
                $"('{ticker}', '{FrozenBookNight}', '{close}', '{close}', '{close}', '{close}', 1000, 'test', '{FrozenBookNight}T21:00:00Z', '{close}');");
        }

        return store;
    }

    [Fact]
    public async Task AFrozenHeavyweightsNightsCardsAreItsLiveRulesOwnBuysSizedOverItsOwnLeaders()
    {
        using var store = FrozenBookStore();

        await new DecisionCards(new SweepClock(new DateTimeOffset(2026, 10, 1, 23, 50, 0, TimeSpan.Zero)), store.DatabaseFile).RunAsync("cards");

        // S2 alone, the live rule's buy, and not S1, which only the index's own book bought; its holding sized over one
        // leader in each of the eleven sectors, where the index's own book reads two.
        Assert.Equal("SML heavyweight S2 11", Text(store, "SELECT group_concat(index_code || ' ' || family || ' ' || ticker || ' ' || book_holdings, '; ') FROM decision_card;"));
        Assert.Equal(2, IndexHeavyweights.Provisional.Leaders);
    }

    [Fact]
    public async Task AFrozenHeavyweightsNightAsksForTheReportsOfItsLiveRulesOwnBuys()
    {
        using var store = FrozenBookStore();

        var ask = await RequestDrain.AskForTheNightAsync(store.DatabaseFile, new DateOnly(2026, 10, 1), FixedClock.At(new DateTimeOffset(2026, 10, 2, 1, 0, 0, TimeSpan.Zero), SessionZones.UnitedStates));

        // The S&P 600's list is the live rule's buy alone: S2 asked for and S1 not.
        Assert.Equal(["S2"], ask.Asked);
        Assert.Contains("S2 is first on the S&P 600's list, and a report on it was asked for", ask.Line, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ATakenFrozenHeavyweightEndsAtItsLiveRulesOwnSale()
    {
        using var store = FrozenBookStore();

        store.Execute("INSERT INTO membership (index_code, ticker, joined, \"left\", observed_at) VALUES ('SML', 'S2', NULL, NULL, '2026-10-01T21:00:00Z');");
        store.Execute(
            "INSERT INTO decision_card (index_code, session_date, family, ticker, place, entry, stop, target, rule, settings, lines, record) VALUES " +
            $"('SML', '{FrozenBookNight}', '{HeavyweightRule.Name}', 'S2', 1, '20', NULL, NULL, 'the rule', '{{}}', '[]', NULL);");
        store.Execute(
            "INSERT INTO taken_trade (ticker, taken_at, index_code, family, night, sector, fill, fill_date, provisional, entered, stop, target, trail, cap, exit_price, exit_date, followed_through) VALUES " +
            $"('S2', '2026-10-02T01:00:00Z', 'SML', '{HeavyweightRule.Name}', '{FrozenBookNight}', 'Utilities', '20', '2026-10-02', 0, 1, NULL, NULL, NULL, NULL, NULL, NULL, NULL);");

        foreach (var (session, close) in new[] { ("2026-10-02", "20.5"), ("2026-10-05", "21"), ("2026-10-06", "22"), ("2026-10-07", "23") })
        {
            store.Execute(
                "INSERT INTO bar (ticker, session_date, open, high, low, close, volume, source, observed_at, raw_close) VALUES " +
                $"('S2', '{session}', '{close}', '{close}', '{close}', '{close}', 1000, 'test', '{session}T21:00:00Z', '{close}');");
        }

        await new TakenFollower(new SweepClock(new DateTimeOffset(2026, 10, 7, 23, 50, 0, TimeSpan.Zero)), store.DatabaseFile).RunAsync("follow");

        // Sold with the live rule's book on 2026-10-06 at that session's close of 22.00, where the index's own book still
        // holds it: (22.00 / 20.00 - 1) in per cent, 10.0000.
        Assert.Equal(
            "2026-10-06 22.00 sold 10.0000",
            Text(store, "SELECT ended_on || ' ' || printf('%.2f', end_price) || ' ' || end_reason || ' ' || printf('%.4f', result) FROM taken_trade;"));
    }

    // The live alarm, on the operator's ruling of the evening of 2026-10-20 (the 17.9 correction): the S&P 600's sector
    // heavyweights read by a frozen live rule have their quarter read off that rule's own book, as the page draws it.
    // Against the newest run's reference of thirty units each a half, so a low of a half, the live rule's book sold five
    // holdings bought at the rebalance of 2026-10-01 in the fourth quarter, each filled at 0.10 against its size cut's
    // 0.10, an edge of nothing a holding, so the quarter counts under the low and starts a run of one; the index's own book
    // sold five over the same sessions at 0.90 against 0.10, an edge of 0.80 a holding, which the alarm reads none of. The
    // live rule's sale of S2 on 2026-10-06 holds no fill, settled with no edge, and counts in neither.
    [Fact]
    public async Task TheLiveAlarmReadsAFrozenHeavyweightsRulesOwnBookOnItsIndex()
    {
        using var store = FrozenBookStore();

        store.Execute("INSERT INTO bar (ticker, session_date, open, high, low, close, volume, source, observed_at, raw_close) VALUES ('S1', '2027-01-04', '10', '10', '10', '10', 1000, 'test', '2027-01-04T21:00:00Z', '10');");
        store.Execute(
            "INSERT INTO trade_fill (source, index_code, owner, ticker, session_date, filled_on, fill, past, result, benchmark, members, edge, reason, filled_at) " +
            $"VALUES ('index_heavyweight_rule_holding', 'SML', '{FrozenBook}', 'S2', '{FrozenBookNight}', NULL, NULL, NULL, NULL, NULL, NULL, NULL, 'no open', '2026-10-02T21:00:00Z');");
        LoopRun(store, "loop-test-SML-20261009T120000Z", "SML", "2026-10");
        store.Execute(
            "INSERT INTO loop_reference (run_id, index_code, family, place, entered, edge) VALUES "
            + string.Join(", ", Enumerable.Range(1, 30).Select(at => FormattableString.Invariant($"('loop-test-SML-20261009T120000Z', 'SML', 'heavyweight', {at}, '{new DateOnly(2023, 1, 2).AddDays(at * 7):yyyy-MM-dd}', 0.5)"))) + ";");

        for (var at = 0; at < 5; at++)
        {
            var ended = FormattableString.Invariant($"2026-11-1{at}");

            store.Execute(
                "INSERT INTO index_heavyweight_rule_holding (candidate, index_code, ticker, entered_on, sector, entry_close, growth, cut, through, ended_on, exit_close, reason, result, cut_return, cost) VALUES " +
                $"('{FrozenBook}', 'SML', 'R{at}', '{FrozenBookNight}', 'Energy', '10', 1.1, '[]', '{ended}', '{ended}', '11', 'no longer the leader', 0.1, 0.1, 0);");
            store.Execute(
                "INSERT INTO index_heavyweight_holding (index_code, ticker, entered_on, sector, entry_close, growth, cut, through, ended_on, exit_close, reason, result, cut_return, cost) VALUES " +
                $"('SML', 'O{at}', '{FrozenBookNight}', 'Energy', '10', 1.9, '[]', '{ended}', '{ended}', '19', 'no longer the leader', 0.9, 0.1, 0);");

            foreach (var (source, owner, ticker, result) in new[] { ("index_heavyweight_rule_holding", FrozenBook, $"R{at}", "0.1"), ("index_heavyweight_holding", "", $"O{at}", "0.9") })
            {
                store.Execute(
                    "INSERT INTO trade_fill (source, index_code, owner, ticker, session_date, filled_on, fill, past, result, benchmark, members, edge, reason, filled_at) " +
                    $"VALUES ('{source}', 'SML', '{owner}', '{ticker}', '{FrozenBookNight}', '2026-10-02', '10', NULL, {result}, 0.1, NULL, NULL, NULL, '2026-10-02T21:00:00Z');");
            }
        }

        await new EquityBrief.Worker.Loop.LiveAlarmReader(new SweepClock(new DateTimeOffset(2027, 1, 4, 23, 50, 0, TimeSpan.Zero)), store.DatabaseFile).RunAsync("night-alarm");

        Assert.Equal(
            ["2026-10-01|5|0|0.5|1|1|1|0"],
            FamilyRows(store, "SELECT period, trades, round(edge, 9), edge_floor, counted, under, streak, flagged FROM loop_alarm WHERE index_code = 'SML' AND family = 'heavyweight' ORDER BY period;"));
    }
}
