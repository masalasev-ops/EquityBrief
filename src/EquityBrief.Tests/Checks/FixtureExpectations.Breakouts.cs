using System.Globalization;
using EquityBrief.Core.Families;
using EquityBrief.Core.Filter;
using EquityBrief.Core.Returns;
using EquityBrief.Core.Time;
using EquityBrief.Tests.Harness;
using EquityBrief.Worker.Families;
using EquityBrief.Worker.Returns;

namespace EquityBrief.Tests.Checks;

// The breakout family: each gate worked by hand on both sides of its threshold and at it, the trailing
// stop over constructed closes, the family's order, the evaluator storing every member's answer under the
// swing filter's market check, a stock two families pass listed once, and its trades scored in multiples
// of their risk.
// see: A breakout is a close above the year's high on heavy volume after its ranges narrowed, sold on a trailing stop with no target
public partial class FixtureExpectations
{
    // The claims the breakout's rule makes, which this check reaches: section 17's rows for its settings and
    // section 18's rows for a member holding too few sessions and a trade with no typical move.
    internal static readonly string[] BreakoutClaims =
    [
        CheckReach.Key(Scope.LimitsTable, "Breakout high window"),
        CheckReach.Key(Scope.LimitsTable, "Breakout volume multiple"),
        CheckReach.Key(Scope.LimitsTable, "Breakout range window"),
        CheckReach.Key(Scope.LimitsTable, "Breakout range ceiling"),
        CheckReach.Key(Scope.LimitsTable, "Breakout stop"),
        CheckReach.Key(Scope.LimitsTable, "Breakout session cap"),
        CheckReach.Key(Scope.FailureTable, "A member holding too few sessions for a breakout to be read"),
        CheckReach.Key(Scope.FailureTable, "A breakout with no typical move to place its stop by"),
    ];

    static readonly Gate OpenMarket = new(FamilyRule.Market, true, "breadth 60.0% at or above its floor of 45%", FamilyRule.Values(("breadth", "0.6"), ("floor", "0.45"), ("counted", "6")));

    static readonly Gate ClosedMarket = new(FamilyRule.Market, false, "breadth 40.0% below its floor of 45%", FamilyRule.Values(("breadth", "0.4"), ("floor", "0.45"), ("counted", "6")));

    // A member's year as the breakout reads it, oldest first and ending on the night: 211 quiet sessions
    // between 98 and 100 closing at 99; then the 20 the newer ranges are read against, each between 99 and
    // 101 closing at 100, a range of 2 per cent of the close; then the 20 before tonight, each as given;
    // then tonight, at the close and the volume given. `sessions` shortens the year from its oldest end.
    static IReadOnlyList<FamilyBar> BreakoutYear(decimal close, long volume, (decimal High, decimal Low, decimal Close)? recent = null, int sessions = 252)
    {
        var night = new DateOnly(2026, 10, 2);
        var (high, low, at) = recent ?? (100.5m, 99.5m, 100m);

        var year = Enumerable.Range(0, 252)
            .Select(index => index switch
            {
                < 211 => new FamilyBar(night.AddDays(index - 251), 100m, 98m, 99m, 1000),
                < 231 => new FamilyBar(night.AddDays(index - 251), 101m, 99m, 100m, 1000),
                < 251 => new FamilyBar(night.AddDays(index - 251), high, low, at, 1000),
                _ => new FamilyBar(night, close + 0.5m, close - 1m, close, volume),
            })
            .ToArray();

        return year[(252 - sessions)..];
    }

    static FamilyResult Breakout(IReadOnlyList<FamilyBar> bars, double? typicalMove = 1.5, Gate? market = null, string[]? exclusions = null) =>
        BreakoutRule.Evaluate(new BreakoutInputs("BK", market ?? OpenMarket, bars, 1000, typicalMove, exclusions ?? []));

    static Gate GateOf(FamilyResult result, string name) => result.Gates.Single(gate => gate.Name == name);

    [Fact]
    public void EachOfTheBreakoutsGatesIsWorkedByHandOnBothSidesOfItsThresholdAndAtIt()
    {
        // The settings the rule is worked at, each provisional until the family's freeze.
        Assert.Equal((251, 1.5, 20, 1.0, 2.0, 63), (BreakoutRule.HighSessions, BreakoutRule.VolumeMultiple, BreakoutRule.RangeSessions, BreakoutRule.RangeCeiling, BreakoutRule.StopMoves, BreakoutRule.CapSessions));
        Assert.Equal(["market", "new high", "volume", "tightened", "trade"], BreakoutRule.Order);

        // The year above with the 20 sessions before tonight between 99.5 and 100.5 closing at 100, a range
        // of 1 per cent against the 2 per cent of the 20 before them, so 0.5 of it. The highest high of the
        // 251 sessions before tonight is 101. Tonight closes at 102 on 1,500 shares against an average of
        // 1,000, 1.5 times, and a typical move of 1.5 puts the stop 3 beneath, at 99.
        var passing = Breakout(BreakoutYear(102m, 1500));

        Assert.True(passing.Passed);
        Assert.Equal(0, passing.Missed);
        Assert.Equal((102m, 99m, null, 1.5), (passing.Entry!.Value, passing.Stop!.Value, passing.Target, passing.OrderBy!.Value));
        Assert.Equal(BreakoutRule.Order, passing.Gates.Select(gate => gate.Name));
        Assert.Equal("the close of 102 is above the highest high of the 251 sessions before it, 101", GateOf(passing, BreakoutRule.NewHigh).Reason);
        Assert.Equal("volume 1.50 times its 50-session average, at or above 1.5", GateOf(passing, BreakoutRule.Volume).Reason);
        Assert.Equal("the mean daily range of the 20 sessions before tonight is 1.00% of the close, no wider than the 2.00% of the 20 before them", GateOf(passing, BreakoutRule.Tightened).Reason);
        Assert.Equal("0.5", GateOf(passing, BreakoutRule.Tightened).Values["ratio"]);
        Assert.Equal(("102", "99", "1.5"), (GateOf(passing, FamilyRule.Trade).Values["close"], GateOf(passing, FamilyRule.Trade).Values["stop"], GateOf(passing, FamilyRule.Trade).Values["typical move"]));

        // The new high: a close a cent above the 101 passes, one at it does not, since a close at the high
        // is not above it, and one a cent beneath does not.
        Assert.True(GateOf(Breakout(BreakoutYear(101.01m, 1500)), BreakoutRule.NewHigh).Passed);
        Assert.False(GateOf(Breakout(BreakoutYear(101m, 1500)), BreakoutRule.NewHigh).Passed);
        Assert.False(GateOf(Breakout(BreakoutYear(100.99m, 1500)), BreakoutRule.NewHigh).Passed);
        Assert.Equal("the close of 101 is not above the highest high of the 251 sessions before it, 101", GateOf(Breakout(BreakoutYear(101m, 1500)), BreakoutRule.NewHigh).Reason);

        // A member one session short of the year: 251 sessions stored, and the gate reads not available
        // with the count, while its other gates are read as they stand.
        var early = Breakout(BreakoutYear(102m, 1500, sessions: 251));

        Assert.False(early.Passed);
        Assert.Equal(1, early.Missed);
        Assert.Equal("not available: 251 sessions are stored, and a close is read against the 251 before it", GateOf(early, BreakoutRule.NewHigh).Reason);
        Assert.Equal("251", GateOf(early, BreakoutRule.NewHigh).Values["sessions"]);

        // The volume: 1,500 against 1,000 is at the multiple and passes; 1,499 is 1.499 times and does not;
        // 1,501 passes.
        Assert.False(GateOf(Breakout(BreakoutYear(102m, 1499)), BreakoutRule.Volume).Passed);
        Assert.True(GateOf(Breakout(BreakoutYear(102m, 1500)), BreakoutRule.Volume).Passed);
        Assert.True(GateOf(Breakout(BreakoutYear(102m, 1501)), BreakoutRule.Volume).Passed);
        Assert.Equal("volume 1.50 times its 50-session average, below 1.5", GateOf(Breakout(BreakoutYear(102m, 1499)), BreakoutRule.Volume).Reason);

        // The ranges: the 20 before tonight between 99 and 101, the same 2 per cent as the 20 before them,
        // are no wider and pass at the ceiling; between 99 and 101.02 they are 2.02 per cent, 1.01 of it,
        // and do not.
        var level = Breakout(BreakoutYear(102m, 1500, (101m, 99m, 100m)));
        var wider = Breakout(BreakoutYear(102m, 1500, (101.02m, 99m, 100m)));

        Assert.True(GateOf(level, BreakoutRule.Tightened).Passed);
        Assert.Equal("1", GateOf(level, BreakoutRule.Tightened).Values["ratio"]);
        Assert.False(GateOf(wider, BreakoutRule.Tightened).Passed);
        Assert.Equal("the mean daily range of the 20 sessions before tonight is 2.02% of the close, wider than the 2.00% of the 20 before them", GateOf(wider, BreakoutRule.Tightened).Reason);
        Assert.Equal((false, 1), (wider.Passed, wider.Missed));

        // A member holding 40 sessions has too few for the ranges, which are read over the 40 before tonight.
        Assert.Equal("not available: 40 sessions are stored, and the ranges are read over the 40 before tonight", GateOf(Breakout(BreakoutYear(102m, 1500, sessions: 40)), BreakoutRule.Tightened).Reason);
        Assert.True(GateOf(Breakout(BreakoutYear(102m, 1500, sessions: 41)), BreakoutRule.Tightened).Passed);

        // The trade: a night storing no typical move places no stop, so the member has no trade.
        var unplaced = Breakout(BreakoutYear(102m, 1500), typicalMove: null);

        Assert.Equal((false, 1, null), (unplaced.Passed, unplaced.Missed, unplaced.Stop));
        Assert.Equal("no typical move is stored for the night to place the stop by", GateOf(unplaced, FamilyRule.Trade).Reason);

        // The market check is the night's one answer: closed, the member passes nothing whatever its chart.
        Assert.Equal((false, 1), (Breakout(BreakoutYear(102m, 1500), market: ClosedMarket).Passed, Breakout(BreakoutYear(102m, 1500), market: ClosedMarket).Missed));

        // A series the filter excluded for a gap passes every gate and is not passed.
        var gapped = Breakout(BreakoutYear(102m, 1500), exclusions: [SwingGates.GapExclusion]);

        Assert.Equal((false, 0), (gapped.Passed, gapped.Missed));

        // The order: the volume against its average, largest first, and the ticker where two tie.
        FamilyResult At(string ticker, long volume) =>
            BreakoutRule.Evaluate(new BreakoutInputs(ticker, OpenMarket, BreakoutYear(102m, volume), 1000, 1.5, []));

        Assert.Equal(["B", "A", "C"], FamilyRule.Ranked([At("C", 1500), At("A", 1500), At("B", 2000), At("D", 1400)]).Select(result => result.Ticker));
    }

    static ReturnBar[] ClosesAfter(params decimal[] closes) =>
        [.. closes.Select((close, at) => new ReturnBar(new DateOnly(2026, 9, 1).AddDays(at + 1), close))];

    [Fact]
    public void TheTrailingStopIsRaisedWithANewHighCloseNeverLoweredAndSellsAtACloseUnderItOrAtTheCap()
    {
        // Bought at 100 with the stop at 96, so the stop trails the highest close by 4.
        //
        // 101: the stop rises to 97. 105: to 101. 102: 4 beneath is 98, under 101, so the stop stays at 101,
        // never lowered. 101: at the stop and not under it, so the trade is still open. 100.99: under the
        // stop, sold at that close, 0.99 per cent above the buy. The plan put 4 per cent at risk, so the
        // result is 0.2475 of its risk.
        var sold = TrailingExit.Over(ClosesAfter(101m, 105m, 102m, 101m, 100.99m, 120m), 100m, 96m, BreakoutRule.Horizon, 63);

        Assert.Equal((BreakoutRule.Horizon, TrailingExit.Trailed, new DateOnly(2026, 9, 6)), (sold.Horizon, sold.Outcome, sold.ResolvedOn!.Value));
        Assert.Equal(0.99, sold.ReturnPct!.Value, 9);
        Assert.Null(sold.BreakEven);
        Assert.Equal(4.0, TrailingExit.RiskPct(100m, 96m)!.Value, 9);
        Assert.Equal(0.2475, sold.ReturnPct!.Value / TrailingExit.RiskPct(100m, 96m)!.Value, 9);

        // Where the stop stands after each of those sessions: 97, 101, 101, 101.
        Assert.Equal(
            [97m, 101m, 101m, 101m],
            Enumerable.Range(1, 4).Select(sessions => TrailingExit.StopAfter(ClosesAfter(101m, 105m, 102m, 101m)[..sessions], 100m, 96m)));

        // A first close under the plan's own stop sells there: 95.99 under 96, a loss of 4.01 per cent.
        var stopped = TrailingExit.Over(ClosesAfter(95.99m), 100m, 96m, BreakoutRule.Horizon, 63);

        Assert.Equal((TrailingExit.Trailed, new DateOnly(2026, 9, 2)), (stopped.Outcome, stopped.ResolvedOn!.Value));
        Assert.Equal(-4.01, stopped.ReturnPct!.Value, 9);

        // The cap: three sessions given, none under the stop, so the trade ends at the third close, 103, as
        // unresolved with what it made, 3 per cent. A fourth close under the stop is past the cap and unread.
        var capped = TrailingExit.Over(ClosesAfter(101m, 102m, 103m, 90m), 100m, 96m, BreakoutRule.Horizon, 3);

        Assert.Equal((ForwardReturnSeries.Unresolved, new DateOnly(2026, 9, 4)), (capped.Outcome, capped.ResolvedOn!.Value));
        Assert.Equal(3.0, capped.ReturnPct!.Value, 9);

        // Two sessions of the three have passed and none sold: not yet matured, with no outcome and no figure.
        var open = TrailingExit.Over(ClosesAfter(101m, 102m), 100m, 96m, BreakoutRule.Horizon, 3);

        Assert.Equal((null, null, null), (open.Outcome, open.ResolvedOn, open.ReturnPct));

        // A stop at the buy is not a plan, and it refuses.
        Assert.Throws<InvalidOperationException>(() => TrailingExit.Over(ClosesAfter(101m), 100m, 100m, BreakoutRule.Horizon, 63));
    }

    static string Day(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    static void StoreYear(TemporaryStore store, string ticker, IReadOnlyList<FamilyBar> bars) =>
        store.Execute(
            "INSERT INTO bar (ticker, session_date, open, high, low, close, volume, source, observed_at, raw_close) VALUES " +
            string.Join(", ", bars.Select(bar => FormattableString.Invariant(
                $"('{ticker}', '{Day(bar.Session)}', '{bar.Close}', '{bar.High}', '{bar.Low}', '{bar.Close}', {bar.Volume}, 'test', '{Day(bar.Session)}T21:00:00Z', '{bar.Close}')"))) + ";");

    // One member's swing filter row on the night: whether the pullback passed it and at what rank, the
    // exclusions the filter stored, and the market gate it stored, which is the one every family reads.
    static void FilterRow(TemporaryStore store, string session, string ticker, bool passed = false, int? rank = null, string exclusions = "[]", bool market = true)
    {
        var gate = market ? OpenMarket : ClosedMarket;
        var gates = FamilyRule.GatesJson([gate]);

        store.Execute(
            "INSERT INTO gate_result (ticker, session_date, version, code, market, trend, setup, family, trigger_pass, trigger_event, trade, " +
            "swing_entry, swing_stop, swing_target, clear_stop, clear_target, exclusions, passed, rank, gates) " +
            $"VALUES ('{ticker}', '{session}', '1', 'code', {(market ? 1 : 0)}, 1, 1, 'pullback', 1, 1, 1, " +
            $"'100', '96', '110', '96', '110', '{exclusions}', {(passed ? 1 : 0)}, {(rank is { } at ? at.ToString(CultureInfo.InvariantCulture) : "NULL")}, '{gates}');");
    }

    const string BreakoutNight = "2026-10-02";

    // The constructed night the evaluator is read over, the market open. Every member holds a typical move
    // of 1.5 and an average volume of 1,000.
    //
    // BA, BB and BC close at 102 above a year's high of 101 after their ranges narrowed, on 2,000, 1,500 and
    // 1,800 shares: all three pass, in the order BA at 2.0 times, BC at 1.8 and BB at 1.5. BC carries the
    // pullback's own exclusion for earnings, which is not the breakout's. NH closes at 100.5, under the high,
    // on 1,500 shares. SH holds 251 sessions, one short. XG passes every gate on 3,000 shares and its series
    // is excluded for a gap.
    static TemporaryStore BreakoutStore(bool market = true)
    {
        var store = new TemporaryStore().Migrated();

        store.Execute("INSERT INTO filter_version (version, settings, opened_at, closed_at, evidence) VALUES ('1', '{\"trade\":\"clear\"}', '2026-09-20T00:00:00Z', NULL, 'test');");
        store.Execute($"INSERT INTO list_rule (session_date, rule) VALUES ('{BreakoutNight}', 'filter');");

        var members = new (string Ticker, IReadOnlyList<FamilyBar> Bars, string Exclusions)[]
        {
            ("BA", BreakoutYear(102m, 2000), "[]"),
            ("BB", BreakoutYear(102m, 1500), "[]"),
            ("BC", BreakoutYear(102m, 1800), "[\"" + SwingGates.EarningsExclusion + "\"]"),
            ("NH", BreakoutYear(100.5m, 1500), "[]"),
            ("SH", BreakoutYear(102m, 1500, sessions: 251), "[]"),
            ("XG", BreakoutYear(102m, 3000), "[\"" + SwingGates.GapExclusion + "\"]"),
        };

        foreach (var (ticker, bars, exclusions) in members)
        {
            StoreYear(store, ticker, bars);
            FilterRow(store, BreakoutNight, ticker, exclusions: exclusions, market: market);
            store.Execute(
                "INSERT INTO indicator (ticker, session_date, name, value, bar_count) VALUES " +
                $"('{ticker}', '{BreakoutNight}', 'atr14', 1.5, 252), ('{ticker}', '{BreakoutNight}', 'vol_avg50', 1000, 252);");
        }

        return store;
    }

    const string BreakoutRows = "SELECT ticker, passed, missed, place, entry, stop, target, order_by, exclusions FROM family_result WHERE family = 'breakout' AND session_date = '" + BreakoutNight + "' ORDER BY place IS NULL, place, ticker;";

    [Fact]
    public async Task TheFamilyEvaluatorStoresEveryMembersAnswerUnderTheFiltersMarketCheckInTheFamilysOrder()
    {
        using var store = BreakoutStore();

        var clock = FixedClock.At(new DateTimeOffset(2026, 10, 2, 23, 40, 0, TimeSpan.Zero), SessionZones.UnitedStates);

        // Two rows from a session older than every stored bar, one that passed and one that did not.
        store.Execute(
            "INSERT INTO family_result (session_date, ticker, family, passed, missed, place, entry, stop, target, order_by, exclusions, gates) VALUES " +
            "('2020-01-02', 'OLD', 'breakout', 1, 0, 1, '50', '47', NULL, 2.0, '[]', '{\"gates\":[]}'), " +
            "('2020-01-02', 'GONE', 'breakout', 0, 2, NULL, '50', '47', NULL, 1.0, '[]', '{\"gates\":[]}');");

        var outcome = await new FamilyEvaluator(clock, store.DatabaseFile).RunAsync("rules-first");

        // Every member's answer, the three passing first in the family's order with their places. Each is
        // bought at its close with the stop 3 beneath, two typical moves of 1.5, and no target. NH and SH
        // are one gate short; XG missed none and its gap keeps it off.
        Assert.Equal(
            [
                "BA|1|0|1|102|99|null|2|[]",
                "BC|1|0|2|102|99|null|1.8|[]",
                "BB|1|0|3|102|99|null|1.5|[]",
                "NH|0|1|null|100.5|97.5|null|1.5|[]",
                "SH|0|1|null|102|99|null|1.5|[]",
                "XG|0|0|null|102|99|null|3|[\"gap\"]",
            ],
            FamilyRows(store, BreakoutRows));
        Assert.Equal((new DateOnly(2026, 10, 2), 6), (outcome.Night!.Value, outcome.RowsWritten));
        Assert.Equal([new FamilyEvaluation("breakout", 6, 3, 2)], outcome.Families);
        Assert.Equal(
            ["family-rules|ok|6|0|0|for 2026-10-02: the breakout family passed 3 of 6 members, 2 one gate short"],
            FamilyRows(store, "SELECT stage, outcome, rows_written, model_calls, network_requests, detail FROM run_log WHERE run_id = 'rules-first';"));

        // The stored gates are the rule's own, in order, with the market gate the filter stored.
        var stored = FamilyRule.GatesOf(FamilyRows(store, "SELECT gates FROM family_result WHERE ticker = 'NH' AND session_date = '" + BreakoutNight + "';").Single());

        Assert.Equal(BreakoutRule.Order, stored.Select(gate => gate.Name));
        Assert.Equal([true, false, true, true, true], stored.Select(gate => gate.Passed));
        Assert.Equal(OpenMarket.Reason, stored[0].Reason);
        Assert.Equal("the close of 100.5 is not above the highest high of the 251 sessions before it, 101", stored[1].Reason);

        // The older rows: the one that passed is a trade and is kept, the one that did not is gone.
        Assert.Equal(["OLD"], FamilyRows(store, "SELECT ticker FROM family_result WHERE session_date = '2020-01-02';"));

        // Run again, the night replaces its own rows: six still.
        await new FamilyEvaluator(clock, store.DatabaseFile).RunAsync("rules-again");

        Assert.Equal(["6"], FamilyRows(store, "SELECT COUNT(*) FROM family_result WHERE session_date = '" + BreakoutNight + "';"));

        // The page's list over the two families. The pullback passed BA, at rank 1, and PB, at rank 2, a
        // member the breakout does not pass. BA is listed once, first under the pullback with the breakout's
        // label, and is under another on the breakout's card, taking none of its five; PB is second; then
        // the breakout's BC and BB take the third and fourth places.
        store.Execute($"UPDATE gate_result SET passed = 1, rank = 1 WHERE ticker = 'BA' AND session_date = '{BreakoutNight}';");
        FilterRow(store, BreakoutNight, "PB", passed: true, rank: 2);
        await new FamilyEvaluator(clock, store.DatabaseFile).RunAsync("rules-with-the-pullback");

        var listed = await new FamilyLister(clock, store.DatabaseFile).RunAsync("families-two");

        Assert.Equal(
            [
                "pullback|BA|listed|1|[\"breakout\"]",
                "pullback|PB|listed|2|[]",
                "breakout|BC|listed|3|[]",
                "breakout|BB|listed|4|[]",
                "breakout|BA|under another|null|[]",
            ],
            FamilyRows(store, "SELECT family, ticker, state, place, also FROM family_pick WHERE session_date = '" + BreakoutNight + "' ORDER BY place IS NULL, place, ticker;"));
        Assert.Equal(["2026-10-02|[\"pullback\",\"breakout\"]"], FamilyRows(store, "SELECT session_date, families FROM family_night;"));
        Assert.Equal([("pullback", 2, 2), ("breakout", 3, 2)], listed.Families);
        Assert.Equal((4, 0, 1, 0), (listed.Listed, listed.OpenTrade, listed.UnderAnother, listed.PastFive));

        // PB holds no bar on the night, so the breakout reads it as not available and stores its answer.
        Assert.Equal(["PB|0|not available: 0 sessions are stored, and a close is read against the 251 before it"], FamilyRows(store, "SELECT ticker, passed, json_extract(gates, '$.gates[1].reason') FROM family_result WHERE ticker = 'PB';"));
    }

    [Fact]
    public async Task AClosedMarketClosesTheBreakoutsListAndANightTheFilterStoredNothingForEvaluatesNoFamily()
    {
        var clock = FixedClock.At(new DateTimeOffset(2026, 10, 2, 23, 40, 0, TimeSpan.Zero), SessionZones.UnitedStates);

        // The same six members on a night the filter's market gate closed: none passes, and the three whose
        // charts pass are one gate short, the market's.
        using (var closed = BreakoutStore(market: false))
        {
            var outcome = await new FamilyEvaluator(clock, closed.DatabaseFile).RunAsync("rules-closed");

            Assert.Equal([new FamilyEvaluation("breakout", 6, 0, 3)], outcome.Families);
            Assert.Equal(["0"], FamilyRows(closed, "SELECT COUNT(*) FROM family_result WHERE passed = 1;"));
            Assert.Equal(
                ["BA|1|" + ClosedMarket.Reason, "BB|1|" + ClosedMarket.Reason, "BC|1|" + ClosedMarket.Reason],
                FamilyRows(closed, "SELECT ticker, missed, json_extract(gates, '$.gates[0].reason') FROM family_result WHERE ticker IN ('BA', 'BB', 'BC') ORDER BY ticker;"));

            var listed = await new FamilyLister(clock, closed.DatabaseFile).RunAsync("families-closed");

            Assert.Equal(0, listed.Listed);
            Assert.Equal([("pullback", 0, 0), ("breakout", 0, 0)], listed.Families);
        }

        // A night the swing filter stored no result for: no family is evaluated, no row is written, and the
        // stage's row says so.
        using (var none = BreakoutStore())
        {
            none.Execute("DELETE FROM gate_result;");

            var outcome = await new FamilyEvaluator(clock, none.DatabaseFile).RunAsync("rules-none");

            Assert.Null(outcome.Night);
            Assert.Equal(["0"], FamilyRows(none, "SELECT COUNT(*) FROM family_result;"));
            Assert.Equal(
                ["the swing filter stored no result for 2026-10-02, so no family was evaluated"],
                FamilyRows(none, "SELECT detail FROM run_log WHERE run_id = 'rules-none';"));
        }
    }

    // A passed breakout on a night, bought at its close with its stop, and the name's closes from that
    // night on, the first of them the night's own with the raw close the provider gave it.
    static void BreakoutTrade(TemporaryStore store, string ticker, string stop, string rawClose, params string[] closes)
    {
        var night = new DateOnly(2026, 9, 1);

        store.Execute(
            "INSERT INTO family_result (session_date, ticker, family, passed, missed, place, entry, stop, target, order_by, exclusions, gates) " +
            $"VALUES ('{Day(night)}', '{ticker}', 'breakout', 1, 0, 1, '{rawClose}', '{stop}', NULL, 2.0, '[]', '{{\"gates\":[]}}');");
        store.Execute(
            "INSERT INTO bar (ticker, session_date, open, high, low, close, volume, source, observed_at, raw_close) VALUES " +
            string.Join(", ", closes.Select((close, at) =>
                $"('{ticker}', '{Day(night.AddDays(at))}', '{close}', '{close}', '{close}', '{close}', 1000, 'test', '{Day(night.AddDays(at))}T21:00:00Z', '{(at == 0 ? rawClose : close)}')")) + ";");
    }

    [Fact]
    public async Task ABreakoutsTradeIsScoredOnItsTrailingStopWithWhatItPutAtRisk()
    {
        using var store = new TemporaryStore().Migrated();

        // T1, bought at 100 with the stop at 96: closes of 101, 105, 102 and 100.99 raise the stop to 101
        // and sell under it on the fourth session after, 0.99 per cent up on 4 per cent at risk.
        BreakoutTrade(store, "T1", "96", "100", "100", "101", "105", "102", "100.99");

        // T2, the same trade on a series a two-for-one split has since restated: the night's close reads 50
        // against a raw close of 100, so the stop of 96 is 48 on today's scale and the trail is 2. Closes of
        // 50.5, 52.5 and 51 raise the stop to 50.5, and 50.49 sells under it, 0.98 per cent up on 4 at risk.
        BreakoutTrade(store, "T2", "96", "100", "50", "50.5", "52.5", "51", "50.49");

        // T3, one session after its buy and above its stop: not yet matured.
        BreakoutTrade(store, "T3", "96", "100", "100", "101");

        // T4, 63 sessions at 103 after its buy at 100: never under its stop, so it ends at its cap,
        // unresolved, 3 per cent up.
        BreakoutTrade(store, "T4", "96", "100", ["100", .. Enumerable.Repeat("103", 63)]);

        // T5, whose night's raw close of 95 sits under its stored stop: no trade the night could buy.
        BreakoutTrade(store, "T5", "96", "95", "95", "97");

        var clock = FixedClock.At(new DateTimeOffset(2026, 12, 1, 23, 40, 0, TimeSpan.Zero), SessionZones.UnitedStates);
        var outcome = await new ForwardReturnFiller(clock, store.DatabaseFile).RunAsync("returns-breakouts");

        Assert.Equal(
            [
                "T1|breakout|trailed|2026-09-05|0.99|4|null|null",
                "T2|breakout|trailed|2026-09-05|0.98|4|null|null",
                "T3|breakout|null|null|null|null|null|null",
                "T4|breakout|unresolved|2026-11-03|3|4|null|null",
            ],
            FamilyRows(store, "SELECT ticker, horizon, outcome, resolved_on, round(return_pct, 6), round(planned_risk, 6), break_even, null_win FROM forward_return ORDER BY ticker;"));
        Assert.Equal((5, 1), (outcome.FamilyTradesExamined, outcome.FamilyTradesNotScorable));
        Assert.EndsWith(
            "; 5 trade(s) of the other setup families read, 1 not scorable from the night's close",
            FamilyRows(store, "SELECT detail FROM run_log WHERE run_id = 'returns-breakouts';").Single(),
            StringComparison.Ordinal);

        // Run again, a decided trade is kept as it was decided and the open one is read again.
        var again = await new ForwardReturnFiller(clock, store.DatabaseFile).RunAsync("returns-again");

        Assert.Equal((3, 4), (again.Kept, FamilyRows(store, "SELECT ticker FROM forward_return;").Count));
    }
}
