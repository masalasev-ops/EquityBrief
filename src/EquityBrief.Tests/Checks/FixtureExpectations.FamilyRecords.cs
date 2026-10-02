using System.Globalization;
using System.Text.Json;
using EquityBrief.Core.Candidates;
using EquityBrief.Core.Families;
using EquityBrief.Core.Filter;
using EquityBrief.Core.Time;
using EquityBrief.Tests.Harness;
using EquityBrief.Worker.Candidates;
using EquityBrief.Worker.Families;
using EquityBrief.Worker.Filter;

namespace EquityBrief.Tests.Checks;

// fixture-expectations, 13.9: the freezes. The register command writing each new family's rules at one instant
// or none, each family's correction counting its own rules; each variant firing at its own settings on both
// sides of the setting it moves; the pullback's freeze opening its base and registering the swing family again
// at one instant, taken once; the family evaluator storing every registered rule's verdict on every member's
// row, a moved evaluator failing its stage; and the family recorder keeping each rule's own list and its trades,
// each trade's result and benchmark worked by hand.
// see: The new families freeze at their sweeps' proposals, the breakout's provisional setting and the drift's wider stop registered beside them as variants
// see: A registered family rule is evaluated every night at its own settings and keeps its own list, its trades stored with their benchmark when they end
// see: Each setup family's correction for luck counts its own rules alone, at most eight a family
public partial class FixtureExpectations
{
    // The claims the freezes make, which this check reaches: section 17's rows for the pullback's base and a
    // family rule's list, and section 18's three rows.
    internal static readonly string[] FamilyRecordClaims =
    [
        CheckReach.Key(Scope.LimitsTable, "Pullback base"),
        CheckReach.Key(Scope.LimitsTable, "A family rule's list"),
        CheckReach.Key(Scope.FailureTable, "A registered family rule whose evaluator the code no longer carries or has moved"),
        CheckReach.Key(Scope.FailureTable, "A family rule's trade whose stock's closes run out before it ends"),
        CheckReach.Key(Scope.FailureTable, "A family rule's benchmark whose night holds no member to enter"),
    ];

    static readonly DateTimeOffset ThreeAt = new(2026, 9, 6, 22, 0, 0, TimeSpan.Zero);
    static readonly DateTimeOffset FamilyAt = new(2026, 9, 7, 22, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task TheFamilyCommandRegistersEachNewFamilysRulesAtOneInstantOrNoneAndCountsOnlyItsOwnFamily()
    {
        // The swing family standing at its maximum of eight, from version 1.
        using var store = await FamilyStore(ThreeAt);

        Assert.Equal(0, (await RegisterVerbAt(store, FamilyAt, RegisterVerb.TheFamily)).Code);

        var breakoutAt = new DateTimeOffset(2026, 10, 2, 20, 0, 0, TimeSpan.Zero);

        // The breakouts' freeze: the live rule at the proposal and its six variants, the five grid neighbours one
        // step along one dial and the provisional setting, at one instant, counted in their own family of eight
        // while the pullback's stands full.
        var (code, said) = await RegisterVerbAt(store, breakoutAt, RegisterVerb.Family, BreakoutRule.Name);

        Assert.Equal(0, code);
        Assert.Contains("registered 7 at one instant, family of 7 of 8", said, StringComparison.Ordinal);
        Assert.Equal(
            [
                "126|1.5|0.85|1.5",
                "251|1.5|0.85|1.5",
                "126|1.25|0.85|1.5",
                "126|2|0.85|1.5",
                "126|1.5|1|1.5",
                "126|1.5|0.85|2",
                "251|1.5|1|2",
            ],
            TheSetupFamilies.Breakouts.Select(one => BreakoutCandidate.SettingsOf(one.Parameters)).Select(settings => FormattableString.Invariant($"{settings.HighSessions}|{settings.VolumeMultiple}|{settings.RangeCeiling}|{settings.StopMoves}")));
        Assert.Equal(
            [.. TheSetupFamilies.Breakouts.Select(one => one.Candidate)],
            TextRows(store, "SELECT candidate FROM candidate_register WHERE evaluator = 'breakout' AND event = 'registered' ORDER BY id;"));
        Assert.Equal(1, Scalar(store, "SELECT COUNT(DISTINCT registered_at) FROM candidate_register WHERE evaluator = 'breakout';"));
        Assert.Equal(
            "the live breakout rule at a 126-session high, 1.5 times the volume, ranges at 0.85 and the stop 1.5 typical moves beneath",
            TheSetupFamilies.Breakouts[0].Candidate);
        Assert.Equal(
            "the breakout rule at a 251-session high, 1.5 times the volume, ranges at 1 and the stop 2 typical moves beneath",
            TheSetupFamilies.Breakouts[^1].Candidate);
        Assert.All(
            TextRows(store, "SELECT evaluator_version || '|' || rule || '|' || test FROM candidate_register WHERE evaluator = 'breakout';"),
            row => Assert.Equal(new BreakoutCandidate().Version + "|" + TheSetupFamilies.BreakoutWords + "|" + Worker.Sweep.FamilySweepReport.Test, row));

        // The drift's: the live rule, its five neighbours and the proposal with its stop no closer than a typical
        // move, in their own family too.
        var (drifted, driftSaid) = await RegisterVerbAt(store, breakoutAt.AddMinutes(1), RegisterVerb.Family, DriftRule.Name);

        Assert.Equal(0, drifted);
        Assert.Contains("registered 7 at one instant, family of 7 of 8", driftSaid, StringComparison.Ordinal);
        Assert.Equal(
            [
                "3|0.5|2|2.5|0",
                "5|0.5|2|2.5|0",
                "3|1|2|2.5|0",
                "3|0.5|1.5|2.5|0",
                "3|0.5|2|2|0",
                "3|0.5|2|3|0",
                "3|0.5|2|2.5|1",
            ],
            TheSetupFamilies.Drifts.Select(one => DriftCandidate.SettingsOf(one.Parameters)).Select(settings => FormattableString.Invariant($"{settings.WindowSessions}|{settings.ReactionMoves}|{settings.VolumeMultiple}|{settings.TargetRiskMultiple}|{settings.StopFloorMoves}")));
        Assert.EndsWith(", the stop at least 1 typical move beneath", TheSetupFamilies.Drifts[^1].Candidate, StringComparison.Ordinal);

        // Each family's rules counted apart: eight pullbacks, seven breakouts and seven drifts standing at once.
        var rows = await new CandidateRegistrar(FixedClock.At(breakoutAt.AddHours(1), SessionZones.UnitedStates), store.DatabaseFile).RowsAsync();
        var standing = CandidateFamily.Standing(rows, breakoutAt.AddHours(1));

        Assert.Equal(
            (8, 7, 7),
            (CandidateFamily.In(standing, SetupFamilies.Pullback).Count, CandidateFamily.In(standing, BreakoutRule.Name).Count, CandidateFamily.In(standing, DriftRule.Name).Count));

        // The breakouts take an eighth and refuse a ninth, whatever the other families hold.
        var registrar = new CandidateRegistrar(FixedClock.At(breakoutAt.AddHours(2), SessionZones.UnitedStates), store.DatabaseFile);
        var eighth = await registrar.RegisterAsync("a breakout at 1.75 times the volume", "a rule", "a test", BreakoutCandidate.EvaluatorName, BreakoutCandidate.ParametersOf(BreakoutRule.Live with { VolumeMultiple = 1.75 }), "register-eighth");
        var ninth = await registrar.RegisterAsync("a breakout at 1.6 times the volume", "a rule", "a test", BreakoutCandidate.EvaluatorName, BreakoutCandidate.ParametersOf(BreakoutRule.Live with { VolumeMultiple = 1.6 }), "register-ninth");

        Assert.Equal((CandidateRegistrar.Registered, CandidateRegistrar.Refused), (eighth.Outcome, ninth.Outcome));
        Assert.Contains("family of 8 of 8", eighth.Detail, StringComparison.Ordinal);
        Assert.Contains("8 candidates of the breakout family already stand registered", ninth.Detail, StringComparison.Ordinal);

        // A second freeze of a family is refused whole, its names standing, and a family no freeze is written for
        // is refused by name, each writing nothing.
        var before = Scalar(store, "SELECT COUNT(*) FROM candidate_register;");
        var (again, againSaid) = await RegisterVerbAt(store, breakoutAt.AddHours(3), RegisterVerb.Family, BreakoutRule.Name);
        var (leader, leaderSaid) = await RegisterVerbAt(store, breakoutAt.AddHours(4), RegisterVerb.Family, LeaderRule.Name);

        Assert.Equal((1, 1), (again, leader));
        Assert.Contains("was refused, so none of the 7 was registered", againSaid, StringComparison.Ordinal);
        Assert.Contains("no freeze is written for a family named 'leader'; the families a freeze is written for are breakout, drift.", leaderSaid, StringComparison.Ordinal);
        Assert.Equal(before, Scalar(store, "SELECT COUNT(*) FROM candidate_register;"));
    }

    // A breakout member over the year above, at the volume and with the high given.
    static FamilyMember BreakoutMember(decimal close, long volume, (decimal High, decimal Low, decimal Close)? recent = null, int? highBack = null)
    {
        var bars = BreakoutYear(close, volume, recent, highBack: highBack);

        return new FamilyMember(
            new BreakoutInputs("BK", OpenMarket, bars, 1000, 1.5, []),
            new DriftInputs("BK", OpenMarket, bars, null, null, null, null, [], []));
    }

    // A drift member over the ten sessions above, with the bands given.
    static FamilyMember DriftMember(int back = 2, decimal close = 104m, long reactionVolume = 2000, double moveBefore = 3.0, decimal[]? bands = null)
    {
        var bars = DriftSessions(back, close, reactionVolume);

        return new FamilyMember(
            new BreakoutInputs("DR", OpenMarket, bars, 1000, 2.0, []),
            new DriftInputs("DR", OpenMarket, bars, PrintAt(back), moveBefore, 1000, 2.0, bands ?? [], []));
    }

    static Registration BreakoutVariant(Func<BreakoutSettings, bool> which) =>
        TheSetupFamilies.Breakouts.Single(one => which(BreakoutCandidate.SettingsOf(one.Parameters)));

    static Registration DriftVariant(Func<DriftSettings, bool> which) =>
        TheSetupFamilies.Drifts.Single(one => which(DriftCandidate.SettingsOf(one.Parameters)));

    [Fact]
    public void EachVariantFiresAtItsOwnSettingsOnBothSidesOfTheSettingItMoves()
    {
        // Through the registered evaluator, at each registration's own parameters, the variant moving each dial
        // the earlier tests do not reach. The breakout at 1.25 times the volume passes 1,250 shares against an
        // average of 1,000 and not 1,249; at 2 times, 2,000 and not 1,999, where the live rule passes both of
        // the lower counts at 1.5 and neither of the counts under it.
        bool Breaks(Registration rule, FamilyMember member) => new BreakoutCandidate().EvaluateMember(member, rule.Parameters).Passed;

        var lower = BreakoutVariant(settings => settings.VolumeMultiple == 1.25);
        var higher = BreakoutVariant(settings => settings.VolumeMultiple == 2.0);
        var live = TheSetupFamilies.Breakouts[0];

        Assert.Equal((true, false), (Breaks(lower, BreakoutMember(102m, 1250)), Breaks(lower, BreakoutMember(102m, 1249))));
        Assert.Equal((true, false), (Breaks(higher, BreakoutMember(102m, 2000)), Breaks(higher, BreakoutMember(102m, 1999))));
        Assert.Equal((false, true, true), (Breaks(live, BreakoutMember(102m, 1250)), Breaks(live, BreakoutMember(102m, 1500)), Breaks(live, BreakoutMember(102m, 1999))));

        // The year's high, the provisional window, at its neighbour: a high of 110 made 127 sessions back keeps the
        // year's window from firing and not the half year's.
        var year = BreakoutVariant(settings => settings.HighSessions == 251 && settings.StopMoves == 1.5);

        Assert.Equal((false, true), (Breaks(year, BreakoutMember(102m, 1500, highBack: 127)), Breaks(live, BreakoutMember(102m, 1500, highBack: 127))));

        // The ranges no wider, at its neighbour: 2 per cent against 2 per cent passes it and not the live ceiling.
        var level = BreakoutVariant(settings => settings.RangeCeiling == 1.0 && settings.HighSessions == 126);

        Assert.Equal((true, false), (Breaks(level, BreakoutMember(102m, 1500, (101m, 99m, 100m))), Breaks(live, BreakoutMember(102m, 1500, (101m, 99m, 100m)))));

        // The wider stop, at its neighbour, places the stop 3 beneath at 99 where the live rule places 99.75.
        var wider = BreakoutVariant(settings => settings.StopMoves == 2 && settings.HighSessions == 126);

        Assert.Equal((99m, 99.75m), (new BreakoutCandidate().EvaluateMember(BreakoutMember(102m, 1500), wider.Parameters).Stop!.Value, new BreakoutCandidate().EvaluateMember(BreakoutMember(102m, 1500), live.Parameters).Stop!.Value));

        // The drift's target at 2.0 times the risk and at 3.0: bought at 104 over the reaction's low of 100.5, a
        // risk of 3.5, the targets 111 and 114.5. A band at 110.99 is nearer than 111 and taken by the 2.0 variant,
        // and one at 111 is not; the 3.0 variant takes a band at 114.49 and not one at 114.5.
        FamilyResult Drifts(Registration rule, FamilyMember member) => new DriftCandidate().EvaluateMember(member, rule.Parameters);

        var nearer = DriftVariant(settings => settings.TargetRiskMultiple == 2.0);
        var farther = DriftVariant(settings => settings.TargetRiskMultiple == 3.0);

        Assert.Equal((111m, 110.99m, 111m), (Drifts(nearer, DriftMember()).Target!.Value, Drifts(nearer, DriftMember(bands: [110.99m])).Target!.Value, Drifts(nearer, DriftMember(bands: [111m])).Target!.Value));
        Assert.Equal((114.5m, 114.49m, 114.5m), (Drifts(farther, DriftMember()).Target!.Value, Drifts(farther, DriftMember(bands: [114.49m])).Target!.Value, Drifts(farther, DriftMember(bands: [114.5m])).Target!.Value));

        // The window of five at its neighbour buys four sessions after the reaction and not five; the reaction of
        // a whole typical move at its neighbour fires on 3 up against a typical move of 3 and not of 3.01; the
        // volume of 1.5 times at its neighbour fires on 1,500 shares and not 1,499.
        var window = DriftVariant(settings => settings.WindowSessions == 5);
        var reaction = DriftVariant(settings => settings.ReactionMoves == 1.0);
        var volume = DriftVariant(settings => settings.VolumeMultiple == 1.5);

        Assert.Equal((true, false), (Drifts(window, DriftMember(back: 4)).Passed, Drifts(window, DriftMember(back: 5)).Passed));
        Assert.Equal((true, false), (Drifts(reaction, DriftMember(moveBefore: 3.0)).Passed, Drifts(reaction, DriftMember(moveBefore: 3.01)).Passed));
        Assert.Equal((true, false), (Drifts(volume, DriftMember(reactionVolume: 1500)).Passed, Drifts(volume, DriftMember(reactionVolume: 1499)).Passed));

        // The operator's variant holds the stop a typical move of 2 under a buy at 101, at 99, under the low.
        var floored = DriftVariant(settings => settings.StopFloorMoves == 1);

        Assert.Equal((99m, 100.5m), (Drifts(floored, DriftMember(close: 101m)).Stop!.Value, Drifts(TheSetupFamilies.Drifts[0], DriftMember(close: 101m)).Stop!.Value));
    }

    [Fact]
    public async Task ThePullbacksFreezeOpensItsBaseAndRegistersTheFamilyAgainAtOneInstantAndIsTakenOnce()
    {
        using var store = await FamilyStore(ThreeAt);

        Assert.Equal(0, (await RegisterVerbAt(store, FamilyAt, RegisterVerb.TheFamily)).Code);

        var frozenAt = new DateTimeOffset(2026, 10, 2, 21, 0, 0, TimeSpan.Zero);
        string[] Freeze(string restarts) => ["--freeze", "--evidence", "the operator's ruling of 2026-10-02", "--restarts", restarts];

        // A count of blocks the live filter has not run is refused with nothing changed.
        var before = ShapeTables(store);

        Assert.Contains("has run 0 non-empty block(s), and --restarts 2 states another count", (await ShapeVerb(store, frozenAt.AddMinutes(-1), Freeze("2"))).Said, StringComparison.Ordinal);
        Assert.Equal(before, ShapeTables(store));

        var (code, said) = await ShapeVerb(store, frozenAt, Freeze("0"));

        Assert.Equal(0, code);
        Assert.StartsWith(
            "shape: filter version 2 opened at the pullback's base, closing 1, its reward-to-risk floor at 2 and every other setting as version 1 held it; retired 8 and registered 8 at one instant, family of 8 of 8",
            said,
            StringComparison.Ordinal);

        // Version 2 is version 1 with its floor raised to 2, opened as version 1 closes.
        const string At = "2026-10-02T21:00:00Z";

        Assert.Equal("[[\"1\",\"2026-10-02T21:00:00Z\"],[\"2\",null]]", Text(store, "SELECT json_group_array(json_array(version, closed_at)) FROM (SELECT * FROM filter_version ORDER BY version);"));
        Assert.Equal(Ruled with { RewardToRiskFloor = ShapeCommand.FreezeRewardToRiskFloor }, FilterSettings.Read(Text(store, "SELECT settings FROM filter_version WHERE version = '2';")));
        Assert.StartsWith("the pullback's freeze opening filter version 2, restarting 0 non-empty block(s): the operator's ruling of 2026-10-02", Text(store, "SELECT evidence FROM filter_version WHERE version = '2';"), StringComparison.Ordinal);

        // The eight standing retired and the eight for version 2 registered, at that instant, the eighth the
        // pullback in the top sectors, and none of it an acceptance.
        Assert.Equal(
            [.. TheSwingFamily.For("1", Ruled).Select(one => one.Candidate).Order(StringComparer.Ordinal)],
            TextRows(store, $"SELECT retires FROM candidate_register WHERE event = 'retired' AND registered_at = '{At}' ORDER BY retires;"));
        Assert.Equal(
            [.. TheSwingFamily.For("2", Ruled with { RewardToRiskFloor = 2 }).Select(one => one.Candidate)],
            TextRows(store, $"SELECT candidate FROM candidate_register WHERE event = 'registered' AND registered_at = '{At}' ORDER BY id;"));
        Assert.Equal(2.0, CandidateEvaluator.Read(Text(store, $"SELECT parameters FROM candidate_register WHERE candidate = '{SwingFamily.LiveCandidate("2")}';"))["rewardToRiskFloor"]);
        Assert.Equal(0, SwingFamily.AcceptedWhileLive(await new CandidateRegistrar(FixedClock.At(frozenAt, SessionZones.UnitedStates), store.DatabaseFile).RowsAsync()));

        // A second freeze is refused with nothing changed.
        var frozen = ShapeTables(store);
        var (again, againSaid) = await ShapeVerb(store, frozenAt.AddMinutes(1), Freeze("0"));

        Assert.Equal(1, again);
        Assert.Contains("filter version 2 was opened by the pullback's freeze, which is taken once. Nothing was changed.", againSaid, StringComparison.Ordinal);
        Assert.Equal(frozen, ShapeTables(store));

        // With no version open there is nothing to freeze.
        using var closed = await FamilyStore(ThreeAt, versionOpen: false);

        Assert.Contains("no filter version is open, so there is no rule to freeze. Nothing was changed.", (await ShapeVerb(closed, frozenAt, Freeze("0"))).Said, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheFamilyEvaluatorStoresEachRegisteredRulesVerdictOnEveryMembersRowAndAMovedOneFailsTheStage()
    {
        using var store = BreakoutStore();

        var registeredAt = new DateTimeOffset(2026, 10, 2, 20, 0, 0, TimeSpan.Zero);
        var clock = FixedClock.At(new DateTimeOffset(2026, 10, 2, 23, 40, 0, TimeSpan.Zero), SessionZones.UnitedStates);

        Assert.Equal(0, (await RegisterVerbAt(store, registeredAt, RegisterVerb.Family, BreakoutRule.Name)).Code);

        var shadow = FamilyRuleShadow.For(await new CandidateRegistrar(clock, store.DatabaseFile).RowsAsync(), registeredAt.AddHours(3));
        var outcome = await new FamilyEvaluator(clock, store.DatabaseFile).RunAsync("rules-shadow", shadow);

        // Worked by hand: five members read, XG's gap withholding it from all seven, so 35 verdicts and no fault.
        Assert.Equal((35, 0), (outcome.Evaluated, outcome.Faults!.Count));

        IReadOnlyList<ShadowOutcome> VerdictsOn(string ticker, string family = BreakoutRule.Name) =>
            FamilyRuleShadow.Read(Text(store, $"SELECT shadow FROM family_result WHERE ticker = '{ticker}' AND family = '{family}' AND session_date = '{BreakoutNight}';"));

        string FiredBy(Registration rule) =>
            string.Join(",", new[] { "BA", "BB", "BC", "NH", "SH" }.Where(ticker => VerdictsOn(ticker).Single(one => one.Candidate == rule.Candidate).Fired));

        // Each rule at its own settings: the volume of 2 times fires on BA alone, at 2,000 shares, and every
        // other rule on BA, BB and BC; NH closes under its high and SH holds too few sessions for any window.
        Assert.All(TheSetupFamilies.Breakouts.Where(one => BreakoutCandidate.SettingsOf(one.Parameters).VolumeMultiple != 2.0), rule => Assert.Equal("BA,BB,BC", FiredBy(rule)));
        Assert.Equal("BA", FiredBy(BreakoutVariant(settings => settings.VolumeMultiple == 2.0)));

        // The verdict carries the trade its own settings place: the live rule stops BA at 99.75, the provisional
        // setting at 99, each with the night's typical move and the figure the family orders by.
        var onBA = VerdictsOn("BA");
        var liveOnBA = onBA.Single(one => one.Candidate == TheSetupFamilies.Breakouts[0].Candidate).Values;
        var provisionalOnBA = onBA.Single(one => one.Candidate == TheSetupFamilies.Breakouts[^1].Candidate).Values;

        Assert.Equal(("102", "99.75", "none", "2", "1.5"), (liveOnBA[FamilyRuleEvaluator.EntryValue], liveOnBA[FamilyRuleEvaluator.StopValue], liveOnBA[FamilyRuleEvaluator.TargetValue], liveOnBA[FamilyRuleEvaluator.OrderValue], liveOnBA[FamilyRuleEvaluator.MoveValue]));
        Assert.Equal("99", provisionalOnBA[FamilyRuleEvaluator.StopValue]);

        // XG is skipped by every rule with the reason, and the drift's rows carry no shadow, no drift rule standing.
        using (var gapped = JsonDocument.Parse(Text(store, $"SELECT shadow FROM family_result WHERE ticker = 'XG' AND family = 'breakout' AND session_date = '{BreakoutNight}';")))
        {
            Assert.Empty(gapped.RootElement.GetProperty("candidates").EnumerateArray());
            Assert.All(gapped.RootElement.GetProperty("skipped").EnumerateArray(), skip => Assert.Equal("the stored series has a gap, so nothing is computed across it", skip.GetProperty("reason").GetString()));
            Assert.Equal(7, gapped.RootElement.GetProperty("skipped").GetArrayLength());
        }

        Assert.Equal(["0"], FamilyRows(store, $"SELECT COUNT(*) FROM family_result WHERE family = 'drift' AND shadow IS NOT NULL AND session_date = '{BreakoutNight}';"));
        Assert.Equal(["ok"], FamilyRows(store, "SELECT outcome FROM run_log WHERE run_id = 'rules-shadow';"));
        Assert.EndsWith("; 7 family candidate(s) registered, 35 shadow evaluation(s) written, 7 skipped on a member without the readings: 0 stale, 7 gapped", FamilyRows(store, "SELECT detail FROM run_log WHERE run_id = 'rules-shadow';").Single(), StringComparison.Ordinal);

        // A rule registered at a version the code no longer carries is evaluated on no member: the reason stands in
        // its place on every row, the other seven are evaluated, and the stage is written as failed naming it.
        const string Moved = "a breakout registered at a version the code no longer carries";

        store.Execute(
            "INSERT INTO candidate_register (id, candidate, rule, test, evaluator, parameters, evaluator_version, event, retires, registered_at, evidence) " +
            $"SELECT MAX(id) + 1, '{Moved}', 'a rule', 'a test', 'breakout', '{CandidateEvaluator.Write(BreakoutCandidate.ParametersOf(BreakoutRule.Live))}', '000000000001', 'registered', NULL, '2026-10-02T20:00:00Z', NULL FROM candidate_register;");

        var withMoved = FamilyRuleShadow.For(await new CandidateRegistrar(clock, store.DatabaseFile).RowsAsync(), registeredAt.AddHours(3));
        var failed = await new FamilyEvaluator(clock, store.DatabaseFile).RunAsync("rules-moved", withMoved);

        Assert.Equal((35, Moved), (failed.Evaluated, Assert.Single(failed.Faults!)));
        Assert.Equal(["failed"], FamilyRows(store, "SELECT outcome FROM run_log WHERE run_id = 'rules-moved';"));
        Assert.Contains($"FAILURE: 1 registered candidate(s) skipped on every member, the code carrying no evaluator by its name or a moved one: '{Moved}'", FamilyRows(store, "SELECT detail FROM run_log WHERE run_id = 'rules-moved';").Single(), StringComparison.Ordinal);

        using (var moved = JsonDocument.Parse(Text(store, $"SELECT shadow FROM family_result WHERE ticker = 'BA' AND family = 'breakout' AND session_date = '{BreakoutNight}';")))
        {
            Assert.Equal(7, moved.RootElement.GetProperty("candidates").GetArrayLength());
            Assert.Equal(Moved, Assert.Single(moved.RootElement.GetProperty("skipped").EnumerateArray()).GetProperty("candidate").GetString());
        }
    }

    // ---- the family recorder ----

    // Weekday sessions from 2026-06-01 the exchange traded, the first of which is the recorder's first night.
    static readonly DateOnly[] RecordSessions = Sessions(new DateOnly(2026, 6, 1), 70);

    static DateOnly[] Sessions(DateOnly from, int count)
    {
        var sessions = new List<DateOnly>();

        for (var day = from; sessions.Count < count; day = day.AddDays(1))
        {
            if (Core.Bars.ExchangeClosures.IsSession(day))
            {
                sessions.Add(day);
            }
        }

        return [.. sessions];
    }

    static string On(int session) => Day(RecordSessions[session]);

    // A member's closes from a session on, one a session.
    static void Closes(TemporaryStore store, string ticker, int from, params decimal[] closes) =>
        store.Execute(
            "INSERT INTO bar (ticker, session_date, open, high, low, close, volume, source, observed_at, raw_close) VALUES " +
            string.Join(", ", closes.Select((close, at) => FormattableString.Invariant(
                $"('{ticker}', '{On(from + at)}', '{close}', '{close}', '{close}', '{close}', 1000, 'test', '{On(from + at)}T21:00:00Z', '{close}')"))) + ";");

    // One verdict a rule's shadow carries for a member: the trade it places and the figure its family orders by.
    sealed record RuleVerdict(string Candidate, bool Fired, string Entry, string Stop, string? Target, double Order, string Move);

    // A member's row of the family on a night, holding the verdicts given in its shadow.
    static void Verdicts(TemporaryStore store, int session, string family, string ticker, params RuleVerdict[] verdicts)
    {
        var shadow = JsonSerializer.Serialize(new
        {
            candidates = verdicts.Select(verdict => new
            {
                candidate = verdict.Candidate,
                fired = verdict.Fired,
                values = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    [FamilyRuleEvaluator.EntryValue] = verdict.Entry,
                    [FamilyRuleEvaluator.StopValue] = verdict.Stop,
                    [FamilyRuleEvaluator.TargetValue] = verdict.Target ?? "none",
                    [FamilyRuleEvaluator.OrderValue] = verdict.Order.ToString("R", CultureInfo.InvariantCulture),
                    [FamilyRuleEvaluator.ThenByValue] = "none",
                    [FamilyRuleEvaluator.MoveValue] = verdict.Move,
                },
            }),
            skipped = Array.Empty<object>(),
        });

        store.Execute(
            "INSERT INTO family_result (session_date, ticker, family, passed, missed, place, entry, stop, target, order_by, exclusions, gates, shadow) " +
            $"VALUES ('{On(session)}', '{ticker}', '{family}', 0, 1, NULL, '100', NULL, NULL, NULL, '[]', '{{\"gates\":[]}}', '{shadow}');");
    }

    static RegisterRow Rule(long id, Registration registration) =>
        new(id, registration.Candidate, registration.Rule, registration.Test, registration.Evaluator, CandidateEvaluator.Write(registration.Parameters),
            CandidateEvaluators.Find(registration.Evaluator)!.Version, CandidateFamily.Registered, null, new DateTimeOffset(2026, 5, 29, 21, 0, 0, TimeSpan.Zero), null);

    // A column as the assertions read it, null where it holds none: printf reads a null as nought.
    static string Shown(string column, string format) => $"CASE WHEN {column} IS NULL THEN 'null' ELSE printf('{format}', {column}) END";

    static Task<FamilyRecordsOutcome> RecordAsync(TemporaryStore store, string run, IReadOnlyList<RegisterRow> standing) =>
        new FamilyRecorder(FixedClock.At(new DateTimeOffset(2026, 12, 31, 23, 0, 0, TimeSpan.Zero), SessionZones.UnitedStates), store.DatabaseFile).RunAsync("GSPC", run, standing);

    const string KeptRows = "SELECT CASE candidate WHEN '{0}' THEN 'R1' ELSE 'R2' END || '|' || ticker || '|' || place || '|' || IFNULL(ended_on, 'open') FROM family_trade WHERE session_date = '{1}' ORDER BY candidate, place;";

    [Fact]
    public async Task ARegisteredRulesListIsFiveANightInItsOrderWithOneOpenTradeAStockOfItsOwnFreedTheNightAfter()
    {
        using var store = new TemporaryStore().Migrated();

        // Two breakout rules, the live one and its variant, each a list of its own.
        var r1 = Rule(1, TheSetupFamilies.Breakouts[0]);
        var r2 = Rule(2, TheSetupFamilies.Breakouts[^1]);
        IReadOnlyList<RegisterRow> standing = [r1, r2];
        string Kept(int session) => string.Format(CultureInfo.InvariantCulture, KeptRows, r1.Candidate, On(session));

        // The first night: R1 fires on seven members, A first and G last in its order, each bought at 100 with the
        // stop at 97 on a typical move of 2; R2 fires on A alone. By hand: R1 keeps A to E, the five a night, and
        // R2 keeps A, held by nothing R1 kept.
        string[] seven = ["A", "B", "C", "D", "E", "F", "G"];

        foreach (var (ticker, at) in seven.Select((ticker, at) => (ticker, at)))
        {
            Closes(store, ticker, 0, 100m);
            Verdicts(store, 0, BreakoutRule.Name, ticker,
                new RuleVerdict(r1.Candidate, true, "100", "97", null, 7 - at, "2"),
                new RuleVerdict(r2.Candidate, ticker == "A", "100", "97", null, 1, "2"));
        }

        var first = await RecordAsync(store, "records-first", standing);

        Assert.Equal((6, 0, 0, 2), (first.Kept, first.Ended, first.Benchmarked, first.Candidates));
        Assert.Equal(["R2|A|1|open", "R1|A|1|open", "R1|B|2|open", "R1|C|3|open", "R1|D|4|open", "R1|E|5|open"], FamilyRows(store, Kept(0)));
        Assert.Equal(["1.5"], FamilyRows(store, $"SELECT DISTINCT risk_moves FROM family_trade WHERE session_date = '{On(0)}';"));
        Assert.Equal(["63"], FamilyRows(store, $"SELECT DISTINCT cap FROM family_trade WHERE session_date = '{On(0)}';"));

        // The second night: A closes at 96.9, through both its stops of 97, so both its trades end on this
        // session, each -3.1 / 3 risks; the rest close at 100. R1 fires on A, F and G, and R2 on B. By hand: A's
        // trade ended tonight still holds it, so R1 keeps F and G; R2 holds no trade on B, R1's own holding it
        // for R1 alone, so R2 keeps B.
        Closes(store, "A", 1, 96.9m);

        foreach (var ticker in seven.Skip(1))
        {
            Closes(store, ticker, 1, 100m);
        }

        foreach (var (ticker, order) in new[] { ("A", 3.0), ("F", 2.0), ("G", 1.0) })
        {
            Verdicts(store, 1, BreakoutRule.Name, ticker, new RuleVerdict(r1.Candidate, true, "100", "97", null, order, "2"));
        }

        Verdicts(store, 1, BreakoutRule.Name, "B", new RuleVerdict(r2.Candidate, true, "100", "97", null, 1, "2"));

        var second = await RecordAsync(store, "records-second", standing);

        Assert.Equal((3, 2), (second.Kept, second.Ended));
        Assert.Equal(["R2|B|1|open", "R1|F|1|open", "R1|G|2|open"], FamilyRows(store, Kept(1)));
        Assert.Equal(
            [$"A|{On(1)}|-1.0333"],
            FamilyRows(store, $"SELECT DISTINCT ticker || '|' || ended_on || '|' || printf('%.4f', result) FROM family_trade WHERE ended_on IS NOT NULL;"));

        // The third night: A is free, its trades having ended the session before, and R1 keeps it again; B is
        // still held by R1's own open trade from the first night.
        foreach (var ticker in seven)
        {
            Closes(store, ticker, 2, 100m);
        }

        Verdicts(store, 2, BreakoutRule.Name, "A", new RuleVerdict(r1.Candidate, true, "100", "97", null, 2, "2"));
        Verdicts(store, 2, BreakoutRule.Name, "B", new RuleVerdict(r1.Candidate, true, "100", "97", null, 1, "2"));

        await RecordAsync(store, "records-third", standing);

        Assert.Equal(["R1|A|1|open"], FamilyRows(store, Kept(2)));

        // Run again, the night replaces its own trades and no other night's.
        await RecordAsync(store, "records-third-again", standing);

        Assert.Equal(["R1|A|1|open"], FamilyRows(store, Kept(2)));
        Assert.Equal(["10"], FamilyRows(store, "SELECT COUNT(*) FROM family_trade;"));
        Assert.Contains(
            "1 trade(s) kept by 2 registered family rule(s), 0 ended and 0 benchmarked",
            FamilyRows(store, "SELECT detail FROM run_log WHERE run_id = 'records-third-again';").Single(),
            StringComparison.Ordinal);

        // A night with no family rule standing keeps nothing and says so.
        await RecordAsync(store, "records-none", []);

        Assert.EndsWith("; no family rule stands registered", FamilyRows(store, "SELECT detail FROM run_log WHERE run_id = 'records-none';").Single(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AKeptTradesResultAndBenchmarkAreWorkedByHandForATrailingAndAFixedExit()
    {
        using var store = new TemporaryStore().Migrated();

        var breakout = Rule(1, TheSetupFamilies.Breakouts[0]);
        var drift = Rule(2, TheSetupFamilies.Drifts[0]);
        IReadOnlyList<RegisterRow> standing = [breakout, drift];

        // The night's members and their typical moves: T and V at 2, M1 at 2, M3 at 1 and U at 1; M2 stores none.
        foreach (var (ticker, move) in new (string, double?)[] { ("T", 2), ("V", 2), ("W", 2), ("M1", 2), ("M2", null), ("M3", 1), ("U", 1) })
        {
            store.Execute(
                "INSERT INTO membership (index_code, ticker, joined, \"left\", observed_at) " +
                $"VALUES ('GSPC', '{ticker}', NULL, NULL, '2026-05-01T21:00:00Z');");

            if (move is { } held)
            {
                store.Execute(
                    "INSERT INTO indicator (ticker, session_date, name, value, bar_count) " +
                    $"VALUES ('{ticker}', '{On(0)}', 'atr14', {held.ToString(CultureInfo.InvariantCulture)}, 252);");
            }
        }

        // The first night's closes and verdicts. The breakout rule keeps T, V and W, each bought at 100 with the
        // stop at 97 and no target: T and V on a typical move of 2, 1.5 moves of risk, and W with no typical
        // move stated. The drift rule keeps U, bought at 100 with the stop at 98 and the target at 104, on a
        // typical move of 1: 2 moves of risk and a reward to risk of 2.
        foreach (var ticker in new[] { "T", "V", "W", "U" })
        {
            Closes(store, ticker, 0, 100m);
        }

        Closes(store, "M1", 0, 50m);
        Closes(store, "M2", 0, 20m);
        Closes(store, "M3", 0, 30m);

        Verdicts(store, 0, BreakoutRule.Name, "T", new RuleVerdict(breakout.Candidate, true, "100", "97", null, 3, "2"));
        Verdicts(store, 0, BreakoutRule.Name, "V", new RuleVerdict(breakout.Candidate, true, "100", "97", null, 2, "2"));
        Verdicts(store, 0, BreakoutRule.Name, "W", new RuleVerdict(breakout.Candidate, true, "100", "97", null, 1, "none"));
        Verdicts(store, 0, DriftRule.Name, "U", new RuleVerdict(drift.Candidate, true, "100", "98", "104", 5, "1"));

        Assert.Equal(4, (await RecordAsync(store, "trades-first", standing)).Kept);
        Assert.Equal(
            ["T|1.5|null|63", "U|2|2|60", "V|1.5|null|63", "W|null|null|63"],
            FamilyRows(store, "SELECT ticker || '|' || " + Shown("risk_moves", "%g") + " || '|' || " + Shown("reward_to_risk", "%g") + " || '|' || cap FROM family_trade ORDER BY ticker;"));

        // The closes after it, through 63 sessions of M3's, which is the calendar the cap is counted on.
        //
        // T: 101, 104, 103, then 100.9. Its stop of 97 rises to 98 under 101 and to 101 under 104, holds at 101
        // under 103, and 100.9 is under it: sold on the fourth session, (100.9 - 100) / 3 = 0.3 risks.
        // U: 101, then 104.5, at or above its target of 104 on the second session: (104.5 - 100) / 2 = 2.25.
        // V and W hold no close after the night: their closes run out before either ends.
        // M1: 53, then 49.4. M3: 30 for 63 sessions.
        Closes(store, "T", 1, 101m, 104m, 103m, 100.9m);
        Closes(store, "U", 1, 101m, 104.5m);
        Closes(store, "M1", 1, 53m, 49.4m);
        Closes(store, "M3", 1, [.. Enumerable.Repeat(30m, 63)]);

        var later = await RecordAsync(store, "trades-later", standing);

        // Every kept trade ended: T and U with their results, V and W ended on the cap's last session, the 63rd
        // after the night, with no result, their closes having run out; each benchmarked once its cap had passed.
        Assert.Equal((0, 4, 4), (later.Kept, later.Ended, later.Benchmarked));

        // The benchmarks, the same plan entered at the night's close on every member holding a bar and a typical
        // move, each one's risk its own typical move times the trade's moves of risk, averaged over those whose
        // closes reach the trade's end.
        //
        // The breakout's plan, 1.5 moves of risk trailed at that distance with no target: T as itself, 0.3; M1 on
        // a move of 2, a risk of 3 from 50, its stop of 47 raised to 50 under 53 and 49.4 under it, -0.2; M3 on a
        // move of 1, a risk of 1.5 from 30, flat to its cap's close, 0; V, W and U read no close past the night,
        // or none reaching their ends, and M2 no typical move. (0.3 - 0.2 + 0) / 3 = 0.0333 over 3 members, for
        // T and V alike, each listed on the same night under the same plan. W's plan states no typical move, so
        // it has no same plan to enter: no benchmark, over no member.
        //
        // The drift's plan, 2 moves of risk and a target at 2 times it: U as itself, 2.25; M3 on a move of 1, a
        // risk of 2 from 30, flat to its cap's close, 0; T on a move of 2, a risk of 4 from 100, its target of 108
        // and its stop of 96 neither reached before its closes run out, and M1 the same, so neither counts.
        // (2.25 + 0) / 2 = 1.125 over 2 members.
        Assert.Equal(
            [
                $"T|{On(4)}|0.3000|0.0333|3",
                $"U|{On(2)}|2.2500|1.1250|2",
                $"V|{On(63)}|null|0.0333|3",
                $"W|{On(63)}|null|null|0",
            ],
            FamilyRows(store, "SELECT ticker || '|' || ended_on || '|' || " + Shown("result", "%.4f") + " || '|' || " + Shown("benchmark", "%.4f") + " || '|' || members FROM family_trade ORDER BY ticker;"));
        Assert.Contains("for " + On(63) + ": 0 trade(s) kept by 2 registered family rule(s), 4 ended and 4 benchmarked", FamilyRows(store, "SELECT detail FROM run_log WHERE run_id = 'trades-later';").Single(), StringComparison.Ordinal);

        // A trade's end and benchmark are written once: run again, nothing moves.
        var again = await RecordAsync(store, "trades-again", standing);

        Assert.Equal((0, 0), (again.Ended, again.Benchmarked));

        // The record reads the trades ended with a result and a benchmark as decided, and the trades whose closes
        // ran out or that hold no benchmark as kept and undecided.
        var trades = FamilyRows(store, "SELECT candidate || '|' || session_date || '|' || IFNULL(ended_on, '') || '|' || IFNULL(result, '') || '|' || IFNULL(benchmark, '') FROM family_trade ORDER BY ticker;")
            .Select(row => row.Split('|'))
            .Select(cells => new FamilyTradeRow(
                cells[0],
                DateOnly.Parse(cells[1], CultureInfo.InvariantCulture),
                cells[2].Length > 0 ? DateOnly.Parse(cells[2], CultureInfo.InvariantCulture) : null,
                cells[3].Length > 0 ? double.Parse(cells[3], CultureInfo.InvariantCulture) : null,
                cells[4].Length > 0 ? double.Parse(cells[4], CultureInfo.InvariantCulture) : null))
            .ToArray();
        var views = FamilyRecords.Family([breakout], trades, RecordSessions[63], BreakoutRule.CapSessions, 1);

        Assert.Equal((3, 1), (Assert.Single(views).Trades, views[0].Decided));
        Assert.Equal(0.3 - (0.1 / 3), views[0].Edge!.Value, 9);
    }
}
