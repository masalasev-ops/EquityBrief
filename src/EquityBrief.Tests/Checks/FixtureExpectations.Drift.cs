using EquityBrief.Core.Families;
using EquityBrief.Core.Returns;
using EquityBrief.Core.Time;
using EquityBrief.Tests.Harness;
using EquityBrief.Worker.Families;
using EquityBrief.Worker.Returns;

namespace EquityBrief.Tests.Checks;

// The earnings drift family: each gate worked by hand on both sides of its threshold and at it, the window
// at its first and last session and one past it, the target where a band is nearer and where the multiple
// of the risk is, the evaluator reading the stored reactions, and its trades scored under their own cap.
// see: The earnings drift buys a beat with a strong reaction within five sessions, stopped under the reaction session's low
public partial class FixtureExpectations
{
    // The claims the drift's rule makes, which this check reaches: section 17's rows for its settings and
    // section 18's rows for a print with no actual and a reaction session the bars do not reach.
    internal static readonly string[] DriftClaims =
    [
        CheckReach.Key(Scope.LimitsTable, "Drift window"),
        CheckReach.Key(Scope.LimitsTable, "Drift reaction"),
        CheckReach.Key(Scope.LimitsTable, "Drift volume multiple"),
        CheckReach.Key(Scope.LimitsTable, "Drift target"),
        CheckReach.Key(Scope.LimitsTable, "Drift session cap"),
        CheckReach.Key(Scope.FailureTable, "A print whose actual the calendar does not carry yet"),
        CheckReach.Key(Scope.FailureTable, "A reaction session the stored bars do not reach"),
    ];

    static readonly DateOnly DriftNight = new(2026, 10, 2);

    // Ten sessions ending on the night, the reaction session the given count of sessions before tonight.
    // Before the reaction every session closes at 100, between 99.5 and 100.5, on 1,000 shares. The
    // reaction closes at 103, 3 up, between 100.5 and 103.5, on the volume given. Each session after it
    // but tonight closes at 103.5, between 103 and 104. Tonight, where it is not the reaction, closes at
    // the close given, between a point beneath it and half a point above.
    static IReadOnlyList<FamilyBar> DriftSessions(int back, decimal close = 104m, long reactionVolume = 2000)
    {
        var reaction = 9 - back;

        return
        [
            .. Enumerable.Range(0, 10).Select(index =>
            {
                var session = DriftNight.AddDays(index - 9);

                return index < reaction ? new FamilyBar(session, 100.5m, 99.5m, 100m, 1000)
                    : index == reaction ? new FamilyBar(session, 103.5m, 100.5m, 103m, reactionVolume)
                    : index < 9 ? new FamilyBar(session, 104m, 103m, 103.5m, 1000)
                    : new FamilyBar(session, close + 0.5m, close - 1m, close, 1000);
            }),
        ];
    }

    static DriftPrint PrintAt(int back, double? surprise = 8.2, bool actual = true) =>
        new(DriftNight.AddDays(-back - 1), DriftNight.AddDays(-back), actual, surprise);

    static FamilyResult Drift(
        int back = 2,
        decimal close = 104m,
        long reactionVolume = 2000,
        DriftPrint? print = null,
        bool noPrint = false,
        double? moveBefore = 3.0,
        double? typicalMove = 2.0,
        decimal[]? bands = null) =>
        DriftRule.Evaluate(new DriftInputs(
            "DR",
            OpenMarket,
            DriftSessions(back, close, reactionVolume),
            noPrint ? null : print ?? PrintAt(back),
            moveBefore,
            1000,
            typicalMove,
            bands ?? [],
            []));

    // The same member read at the settings given: the provisional ones the freeze replaced, or the variant
    // holding the stop no closer than a typical move under the buy.
    static FamilyResult DriftAt(DriftSettings settings, int back = 2, decimal close = 104m, long reactionVolume = 2000, double? moveBefore = 3.0, double? typicalMove = 2.0) =>
        DriftRule.Evaluate(new DriftInputs("DR", OpenMarket, DriftSessions(back, close, reactionVolume), PrintAt(back), moveBefore, 1000, typicalMove, [], []), settings);

    [Fact]
    public void EachOfTheDriftsGatesIsWorkedByHandOnBothSidesOfItsThresholdAndAtIt()
    {
        // The settings the rule is worked at, frozen on 2026-10-02 at its sweep's proposal, the provisional
        // ones the freeze replaced, which a variant registers, and the stop floor of the variant the operator
        // added.
        // see: The new families freeze at their sweeps' proposals, the breakout's provisional setting and the drift's wider stop registered beside them as variants
        Assert.Equal((3, 0.5, 2.0, 2.0, 2.5, 60), (DriftRule.WindowSessions, DriftRule.ReactionMoves, DriftRule.VolumeMultiple, DriftRule.TargetBandMoves, DriftRule.TargetRiskMultiple, DriftRule.CapSessions));
        Assert.Equal(new DriftSettings(5, 1.0, 1.5, 2.5), DriftRule.Provisional);
        Assert.Equal(1.0, DriftRule.VariantStopFloorMoves);
        Assert.Equal(["market", "print", "beat", "reaction", "volume", "held", "trade"], DriftRule.Order);

        // A print reported on 2026-09-29 whose reaction session is 2026-09-30, two sessions before tonight,
        // with a surprise of 8.2 per cent. The reaction closed at 103 from 100, 3 up, against a typical move
        // of 3 the session before: 1.0 typical moves, above the floor of 0.5. Its volume of 2,000 is 2 times
        // its average of 1,000, at the multiple. Tonight closes at 104, above the reaction's low of 100.5. The
        // stop is that low, the risk 3.5, and 2.5 times it is 8.75 above the close, 112.75. A band's low
        // edge at 108 is 4 above the close, 2 typical moves of 2, at the floor, and nearer, so the target
        // is 108 and the reward to risk 4 over 3.5, 1.1429.
        var passing = Drift(bands: [105m, 108m, 115m]);

        Assert.True(passing.Passed);
        Assert.Equal((104m, 100.5m, 108m, 8.2), (passing.Entry!.Value, passing.Stop!.Value, passing.Target!.Value, passing.OrderBy!.Value));
        Assert.Equal(DriftRule.Order, passing.Gates.Select(gate => gate.Name));
        Assert.Equal("the print of 2026-09-29 reacted on 2026-09-30, 2 session(s) before tonight, inside the 3-session window", GateOf(passing, DriftRule.Print).Reason);
        Assert.Equal("the print of 2026-09-29 beat its estimate by 8.2%", GateOf(passing, DriftRule.Beat).Reason);
        Assert.Equal("the reaction session closed up 1.00 typical moves, at or above 0.5 up", GateOf(passing, DriftRule.Reaction).Reason);
        Assert.Equal("the reaction session's volume was 2.00 times its 50-session average, at or above 2.0", GateOf(passing, DriftRule.Volume).Reason);
        Assert.Equal("the close of 104 is above the reaction session's low of 100.5", GateOf(passing, DriftRule.Held).Reason);
        Assert.Equal("bought at the close of 104 with the stop at the reaction session's low, 100.5, and the target at 108, the lowest band 2 typical moves or more above the close, a reward to risk of 1.14", GateOf(passing, FamilyRule.Trade).Reason);
        Assert.Equal((DriftRule.FromBand, "1.1429"), (GateOf(passing, FamilyRule.Trade).Values[DriftRule.TargetFromValue], GateOf(passing, FamilyRule.Trade).Values[FamilyRule.RewardToRiskValue]));
        Assert.False(GateOf(passing, FamilyRule.Trade).Values.ContainsKey(DriftRule.StopFromValue));

        // The target: a band a cent short of 2 typical moves above is not one, so with the next at 115,
        // past 112.75, the target is 2.5 times the risk, and the reward to risk 2.5. With no band it is
        // the same. A band nearer than 112.75 and far enough is taken; one at 112.75 is no nearer.
        var byRisk = Drift(bands: [107.99m, 115m]);

        Assert.Equal((112.75m, DriftRule.FromRisk, "2.5"), (byRisk.Target!.Value, GateOf(byRisk, FamilyRule.Trade).Values[DriftRule.TargetFromValue], GateOf(byRisk, FamilyRule.Trade).Values[FamilyRule.RewardToRiskValue]));
        Assert.Equal("bought at the close of 104 with the stop at the reaction session's low, 100.5, and the target at 112.75, 2.5 times the risk above the close, a reward to risk of 2.50", GateOf(byRisk, FamilyRule.Trade).Reason);
        Assert.Equal(112.75m, Drift().Target!.Value);
        Assert.Equal((112.74m, DriftRule.FromBand), (Drift(bands: [112.74m]).Target!.Value, GateOf(Drift(bands: [112.74m]), FamilyRule.Trade).Values[DriftRule.TargetFromValue]));
        Assert.Equal(DriftRule.FromRisk, GateOf(Drift(bands: [112.75m]), FamilyRule.Trade).Values[DriftRule.TargetFromValue]);

        // A night storing no typical move measures no band's distance, so the target is the multiple of the risk.
        Assert.Equal(112.75m, Drift(typicalMove: null, bands: [108m]).Target!.Value);

        // The window, moved by the freeze from five sessions to three: the reaction tonight, its first
        // session, is inside, bought at the reaction's own close of 103 with the stop at 100.5 and 2.5 times
        // the risk of 2.5 above, 109.25; two sessions before tonight, its last, is inside; three before is one
        // past it, and every later gate then reads no print. The provisional setting buys four sessions after
        // the reaction and not five.
        var first = Drift(back: 0);
        var last = Drift(back: 2);
        var past = Drift(back: 3);

        Assert.Equal((true, 103m, 100.5m, 109.25m), (first.Passed, first.Entry!.Value, first.Stop!.Value, first.Target!.Value));
        Assert.True(last.Passed);
        Assert.Equal((false, 6), (past.Passed, past.Missed));
        Assert.Equal("the print of 2026-09-28 reacted on 2026-09-29, 3 session(s) before tonight, outside the 3-session window", GateOf(past, DriftRule.Print).Reason);
        Assert.All(new[] { DriftRule.Beat, DriftRule.Reaction, DriftRule.Volume, DriftRule.Held, FamilyRule.Trade }, name => Assert.Equal("no print inside the window to read", GateOf(past, name).Reason));
        Assert.Equal((null, null, null), (past.Stop, past.Target, past.OrderBy));
        Assert.Equal(
            (true, true, false),
            (DriftAt(DriftRule.Provisional, back: 3).Passed, DriftAt(DriftRule.Provisional, back: 4).Passed, DriftAt(DriftRule.Provisional, back: 5).Passed));

        // The beat: a surprise of a hundredth of a per cent passes; one of exactly zero does not, and a miss
        // does not.
        Assert.True(GateOf(Drift(print: PrintAt(2, 0.01)), DriftRule.Beat).Passed);
        Assert.False(GateOf(Drift(print: PrintAt(2, 0.0)), DriftRule.Beat).Passed);
        Assert.Equal("the print of 2026-09-29 did not beat its estimate: a surprise of -2.5%", GateOf(Drift(print: PrintAt(2, -2.5)), DriftRule.Beat).Reason);

        // A print whose actual the calendar does not carry yet has no surprise, and passes nothing until one
        // is filed: one gate short, with every other gate read as it stands.
        var unfiled = Drift(print: PrintAt(2, null, actual: false));

        Assert.Equal((false, 1), (unfiled.Passed, unfiled.Missed));
        Assert.Equal("the calendar carries no actual for the print of 2026-09-29 yet, so no surprise is read", GateOf(unfiled, DriftRule.Beat).Reason);

        // The reaction, its floor moved by the freeze from 1.0 typical moves to 0.5: against a typical move
        // of 6 the 3 up is 0.5 of one, at the floor, and passes; against 6.01 it is 0.499, below it. Against
        // 3.01 it is 0.997 of one, above the frozen floor and below the provisional one, which 3 reaches.
        // A night storing none for the session before reads not available.
        Assert.True(GateOf(Drift(moveBefore: 6.0), DriftRule.Reaction).Passed);
        Assert.False(GateOf(Drift(moveBefore: 6.01), DriftRule.Reaction).Passed);
        Assert.Equal("the reaction session closed up 0.50 typical moves, below 0.5 up", GateOf(Drift(moveBefore: 6.01), DriftRule.Reaction).Reason);
        Assert.Equal(
            (true, false, true),
            (GateOf(Drift(moveBefore: 3.01), DriftRule.Reaction).Passed, GateOf(DriftAt(DriftRule.Provisional, moveBefore: 3.01), DriftRule.Reaction).Passed, GateOf(DriftAt(DriftRule.Provisional, moveBefore: 3.0), DriftRule.Reaction).Passed));
        Assert.Equal("not available: no typical move is stored for the session before the reaction's", GateOf(Drift(moveBefore: null), DriftRule.Reaction).Reason);

        // The volume, its multiple moved by the freeze from 1.5 to 2: 1,999 shares against 1,000 is under it,
        // 2,000 at it and 2,001 over; the provisional setting passes 1,500 and not 1,499.
        Assert.False(GateOf(Drift(reactionVolume: 1999), DriftRule.Volume).Passed);
        Assert.True(GateOf(Drift(reactionVolume: 2000), DriftRule.Volume).Passed);
        Assert.True(GateOf(Drift(reactionVolume: 2001), DriftRule.Volume).Passed);
        Assert.Equal(
            (false, true, false),
            (GateOf(Drift(reactionVolume: 1500), DriftRule.Volume).Passed, GateOf(DriftAt(DriftRule.Provisional, reactionVolume: 1500), DriftRule.Volume).Passed, GateOf(DriftAt(DriftRule.Provisional, reactionVolume: 1499), DriftRule.Volume).Passed));

        // The stop floor of the variant the operator added, no closer than one typical move under the buy.
        // Tonight at 101 over the reaction's low of 100.5, on a typical move of 2: the floor, 99, sits under
        // the low, so the stop moves down to it, the risk 2 and the target 2.5 times it above, 106, where the
        // live rule stops at the low, 0.5 under the buy, its target 102.25. At 102.5 the floor is the low
        // itself and the stop stays there; at 102.49 it is 100.49, a cent under the low, and the stop moves.
        var floored = DriftRule.Live with { StopFloorMoves = DriftRule.VariantStopFloorMoves };
        var moved = DriftAt(floored, close: 101m);
        var atTheLow = DriftAt(DriftRule.Live, close: 101m);

        Assert.Equal((true, 99m, 106m, DriftRule.FromFloor), (moved.Passed, moved.Stop!.Value, moved.Target!.Value, GateOf(moved, FamilyRule.Trade).Values[DriftRule.StopFromValue]));
        Assert.Equal("bought at the close of 101 with the stop at 99, 1 typical moves under the close and below the reaction session's low of 100.5, and the target at 106, 2.5 times the risk above the close, a reward to risk of 2.50", GateOf(moved, FamilyRule.Trade).Reason);
        Assert.Equal((100.5m, 102.25m), (atTheLow.Stop!.Value, atTheLow.Target!.Value));
        Assert.Equal((100.5m, DriftRule.FromLow), (DriftAt(floored, close: 102.5m).Stop!.Value, GateOf(DriftAt(floored, close: 102.5m), FamilyRule.Trade).Values[DriftRule.StopFromValue]));
        Assert.Equal((100.49m, DriftRule.FromFloor), (DriftAt(floored, close: 102.49m).Stop!.Value, GateOf(DriftAt(floored, close: 102.49m), FamilyRule.Trade).Values[DriftRule.StopFromValue]));

        // A night storing no typical move holds no floor, so the variant places no trade.
        Assert.Equal((false, "no typical move is stored for the night to hold the stop's floor by"), (DriftAt(floored, typicalMove: null).Passed, GateOf(DriftAt(floored, typicalMove: null), FamilyRule.Trade).Reason));

        // The hold: a close a cent above the reaction's low of 100.5 holds; one at it does not, and then no
        // stop sits below the close, so the trade is not placed either.
        Assert.True(GateOf(Drift(close: 100.51m), DriftRule.Held).Passed);

        var given = Drift(close: 100.5m);

        Assert.False(GateOf(given, DriftRule.Held).Passed);
        Assert.Equal("the close of 100.5 is not above the reaction session's low of 100.5", GateOf(given, DriftRule.Held).Reason);
        Assert.Equal((false, 2, null), (given.Passed, given.Missed, given.Stop));

        // A member with no print stored passes nothing, and so does one whose reaction session the stored
        // bars do not reach, or reach with no close before it.
        Assert.Equal("no print's reaction is stored for the name", GateOf(Drift(noPrint: true), DriftRule.Print).Reason);
        Assert.Equal(
            "not available: the stored bars do not reach the reaction session of 2026-09-01 and the close before it",
            GateOf(Drift(print: new DriftPrint(new DateOnly(2026, 8, 31), new DateOnly(2026, 9, 1), true, 8.2)), DriftRule.Print).Reason);
        Assert.StartsWith("not available: the stored bars do not reach", GateOf(Drift(back: 9), DriftRule.Print).Reason, StringComparison.Ordinal);

        // The order: the surprise, largest first, and the ticker where two tie.
        FamilyResult At(string ticker, double surprise) =>
            DriftRule.Evaluate(new DriftInputs(ticker, OpenMarket, DriftSessions(2), PrintAt(2, surprise), 3.0, 1000, 2.0, [], []));

        Assert.Equal(["B", "A", "C"], FamilyRule.Ranked([At("C", 5.0), At("A", 5.0), At("B", 12.5), At("D", -1.0)]).Select(result => result.Ticker));
    }

    static void StoreReaction(TemporaryStore store, string ticker, int back, string timing, double? surprise, bool actual = true)
    {
        var session = DriftNight.AddDays(-back);
        var report = timing == "after" ? session.AddDays(-1) : session;

        store.Execute(
            "INSERT INTO earnings_reaction (ticker, report_date, timing, reaction_session, estimate, actual, surprise_pct, move_pct) " +
            $"VALUES ('{ticker}', '{Day(report)}', '{timing}', '{Day(session)}', '1.00', {(actual ? "'1.10'" : "NULL")}, {(surprise is { } by ? FamilyRule.Figure(by) : "NULL")}, 3.0);");

        // The typical move stored for the session before the reaction's, and the average volume for the reaction's.
        store.Execute(
            "INSERT OR REPLACE INTO indicator (ticker, session_date, name, value, bar_count) VALUES " +
            $"('{ticker}', '{Day(session.AddDays(-1))}', 'atr14', 3.0, 252), ('{ticker}', '{Day(session)}', 'vol_avg50', 1000, 252);");
    }

    // The constructed night the evaluator is read over for the drift, the market open, every member holding
    // the ten sessions above, its reaction on 2,000 shares, and a typical move of 2 tonight.
    //
    // DA reported after the close of 2026-09-29, so its reaction session is the next, 2026-09-30, two
    // sessions before tonight and the window's last: a beat of 12.5 per cent, with a band's low edge at 108.
    // DB reported before the open tonight and reacted tonight: a beat of 5. DC reacted one session back and
    // missed by 2. DD beat by 9 and reacted three sessions back, one past the window. DE reacted one session
    // back and the calendar carries no actual for it yet. DF holds no print.
    static TemporaryStore DriftStore()
    {
        var store = new TemporaryStore().Migrated();

        store.Execute("INSERT INTO filter_version (version, settings, opened_at, closed_at, evidence) VALUES ('1', '{\"trade\":\"clear\"}', '2026-09-20T00:00:00Z', NULL, 'test');");
        store.Execute($"INSERT INTO list_rule (session_date, rule) VALUES ('{BreakoutNight}', 'filter');");

        foreach (var (ticker, back) in new[] { ("DA", 2), ("DB", 0), ("DC", 1), ("DD", 3), ("DE", 1), ("DF", 2) })
        {
            StoreYear(store, ticker, DriftSessions(back));
            FilterRow(store, BreakoutNight, ticker);
            store.Execute($"INSERT OR REPLACE INTO indicator (ticker, session_date, name, value, bar_count) VALUES ('{ticker}', '{BreakoutNight}', 'atr14', 2.0, 252), ('{ticker}', '{BreakoutNight}', 'vol_avg50', 1000, 252);");
        }

        // DA holds an older print too, a miss eight sessions back, and its newest is the one read.
        StoreReaction(store, "DA", 8, "before", -5.0);
        StoreReaction(store, "DA", 2, "after", 12.5);
        StoreReaction(store, "DB", 0, "before", 5.0);
        StoreReaction(store, "DC", 1, "before", -2.0);
        StoreReaction(store, "DD", 3, "before", 9.0);
        StoreReaction(store, "DE", 1, "after", null, actual: false);

        store.Execute(
            "INSERT INTO level (ticker, as_of, low_edge, high_edge, role, immediate, strength, has_non_average_anchor, members) " +
            $"VALUES ('DA', '{BreakoutNight}', '108', '109', 'resistance', 1, 2, 1, '[]');");

        return store;
    }

    [Fact]
    public async Task TheFamilyEvaluatorReadsTheStoredReactionsForTheDriftAndStoresEveryMembersAnswer()
    {
        using var store = DriftStore();

        var clock = FixedClock.At(new DateTimeOffset(2026, 10, 2, 23, 40, 0, TimeSpan.Zero), SessionZones.UnitedStates);
        var outcome = await new FamilyEvaluator(clock, store.DatabaseFile).RunAsync("rules-drift");

        // DA and DB pass, DA first on its larger surprise. DA is bought at 104 with the stop at its reaction's
        // low, 100.5, and its band at 108 is nearer than 112.75. DB is bought at its reaction's own close,
        // 103, and with no band its target is 2.5 times its risk of 2.5 above, 109.25. DC and DE are one
        // gate short, the beat; DD and DF read no print inside the window and miss six.
        Assert.Equal(
            [
                "DA|1|0|1|104|100.5|108|12.5",
                "DB|1|0|2|103|100.5|109.25|5",
                "DC|0|1|null|104|100.5|112.75|-2",
                "DD|0|6|null|104|null|null|null",
                "DE|0|1|null|104|100.5|112.75|null",
                "DF|0|6|null|104|null|null|null",
            ],
            FamilyRows(store, "SELECT ticker, passed, missed, place, entry, stop, target, order_by FROM family_result WHERE family = 'drift' ORDER BY ticker;"));
        Assert.Equal(new FamilyEvaluation("drift", 6, 2, 2), outcome.Families.Single(family => family.Family == DriftRule.Name));
        Assert.Contains("the drift family passed 2 of 6 members, 2 one gate short", FamilyRows(store, "SELECT detail FROM run_log WHERE run_id = 'rules-drift';").Single(), StringComparison.Ordinal);

        // DA's print was reported after the close of 2026-09-29 and its reaction is read on the next session.
        Assert.Equal(
            ["the print of 2026-09-29 reacted on 2026-09-30, 2 session(s) before tonight, inside the 3-session window"],
            FamilyRows(store, "SELECT json_extract(gates, '$.gates[1].reason') FROM family_result WHERE family = 'drift' AND ticker = 'DA';"));
        Assert.Equal(
            ["the calendar carries no actual for the print of 2026-09-30 yet, so no surprise is read"],
            FamilyRows(store, "SELECT json_extract(gates, '$.gates[2].reason') FROM family_result WHERE family = 'drift' AND ticker = 'DE';"));

        // The page's list: no pullback and no breakout tonight, so the drift's two take the first places.
        var listed = await new FamilyLister(clock, store.DatabaseFile).RunAsync("families-drift");

        Assert.Equal(
            ["drift|DA|listed|1", "drift|DB|listed|2"],
            FamilyRows(store, "SELECT family, ticker, state, place FROM family_pick ORDER BY place;"));
        Assert.Equal(("drift", 2, 2), listed.Families.Single(family => family.Family == DriftRule.Name));
    }

    // A passed drift on a night, bought at its close with its stop and its target, and the name's closes
    // from that night on.
    static void DriftTrade(TemporaryStore store, string ticker, params string[] closes)
    {
        var night = new DateOnly(2026, 9, 1);

        store.Execute(
            "INSERT INTO family_result (session_date, ticker, family, passed, missed, place, entry, stop, target, order_by, exclusions, gates) " +
            $"VALUES ('{Day(night)}', '{ticker}', 'drift', 1, 0, 1, '104', '100.5', '108', 8.2, '[]', '{{\"gates\":[]}}');");
        store.Execute(
            "INSERT INTO bar (ticker, session_date, open, high, low, close, volume, source, observed_at, raw_close) VALUES " +
            string.Join(", ", closes.Select((close, at) =>
                $"('{ticker}', '{Day(night.AddDays(at))}', '{close}', '{close}', '{close}', '{close}', 1000, 'test', '{Day(night.AddDays(at))}T21:00:00Z', '{close}')")) + ";");
    }

    [Fact]
    public async Task ADriftsTradeIsScoredToItsTargetItsStopOrItsCapOfSixtySessions()
    {
        using var store = new TemporaryStore().Migrated();

        // Each bought at 104 with the stop at 100.5 and the target at 108: 3.5 at risk for 4, so the plan
        // breaks even at 3.5 of 7.5, 46.666667 per cent, and puts 3.365385 per cent of the buy at risk.
        //
        // W1 closes at 105 and then 108: a win on the second session, 3.846154 per cent up.
        // L1 closes at 100.49, under the stop: a loss on the first, 3.375 per cent down.
        // C1 closes at 105 for sixty sessions and then at 109: its sixtieth session ends it, unresolved,
        // 0.961538 per cent up, and the close at 109 is past its cap and unread.
        // O1 closes at 105 for fifty-nine sessions: not yet matured.
        DriftTrade(store, "W1", "104", "105", "108");
        DriftTrade(store, "L1", "104", "100.49");
        DriftTrade(store, "C1", ["104", .. Enumerable.Repeat("105", 60), "109"]);
        DriftTrade(store, "O1", ["104", .. Enumerable.Repeat("105", 59)]);

        var clock = FixedClock.At(new DateTimeOffset(2026, 12, 1, 23, 40, 0, TimeSpan.Zero), SessionZones.UnitedStates);

        await new ForwardReturnFiller(clock, store.DatabaseFile).RunAsync("returns-drift");

        Assert.Equal(
            [
                "C1|drift|unresolved|2026-10-31|0.961538|46.666667|null",
                "L1|drift|loss|2026-09-02|-3.375|46.666667|3.365385",
                "O1|drift|null|null|null|null|null",
                "W1|drift|win|2026-09-03|3.846154|46.666667|3.365385",
            ],
            FamilyRows(store, "SELECT ticker, horizon, outcome, resolved_on, round(return_pct, 6), round(break_even, 6), round(planned_risk, 6) FROM forward_return ORDER BY ticker;"));
        Assert.Equal((DriftRule.Horizon, 60), (SetupFamilies.EarningsDrift.Horizon, SetupFamilies.EarningsDrift.CapSessions));
        Assert.Equal(ForwardReturnSeries.Unresolved, FamilyRows(store, "SELECT outcome FROM forward_return WHERE ticker = 'C1';").Single());
    }
}
