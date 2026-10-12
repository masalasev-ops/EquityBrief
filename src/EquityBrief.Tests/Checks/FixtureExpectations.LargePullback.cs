using EquityBrief.Core.Candidates;
using EquityBrief.Core.Filter;
using EquityBrief.Core.Loop;
using EquityBrief.Core.Sweep;
using EquityBrief.Core.Time;
using EquityBrief.Tests.Harness;
using EquityBrief.Worker.Candidates;
using EquityBrief.Worker.Filter;
using EquityBrief.Worker.Loop;
using EquityBrief.Worker.Sweep;
using Xunit;

namespace EquityBrief.Tests.Checks;

// 17.14: the S&P 500's pullback walked by the tester at its open filter version's settings read by value, keeping on the
// sweep's grid the listings the ideas' run keeps and reading a setting off it; a shape move tested and proposed with its
// test years beside the filter's own; the night opening the next version a shape move an approval names, and storing a
// hook change of the pullback for the swing filter's page; and the shape command accepting no proposal. Each is worked
// by hand over constructed candidates, walks and stores.
// see: The S&P 500's pullback is proposed for and changed through the Loop, the shape clock's moves among its sources
public partial class FixtureExpectations
{
    // The open version of the filter as phase 12 froze it, its reward to risk floor raised to 2 and its trade read from
    // section 10's plan: the base, every value of which sits on the sweep's grid.
    static readonly FilterSettings VersionFive = FilterSettings.Proposed with
    {
        Trade = TradeInput.Clear,
        BreadthFloor = 0.45,
        StrengthFloor = 0.5,
        DepthLow = 1,
        DepthHigh = 5,
        DryUpCeiling = 1.5,
        RewardToRiskFloor = 2,
        StopLow = 0.5,
        StopHigh = 4,
        EarningsWindowSessions = 15,
        ArrivalSessions = 3,
    };

    [Fact]
    public void TheSAndP500sPullbackIsWalkedAtItsOpenVersionsSettingsByValueKeepingTheIdeasRunsListingsOnTheGrid()
    {
        var calendar = Weekdays(new DateOnly(2026, 1, 5), 30);
        var sessionAt = calendar.Select((day, index) => (day, index)).ToDictionary(pair => pair.day, pair => pair.index);
        var series = new[] { SweepColumns.Series(Constructed("A", calendar, [.. Enumerable.Repeat(100m, 30)], 1000), sessionAt) };
        var members = SweepBenchmark.On(series, calendar.Length);
        var allOn = Enumerable.Range(0, 6).Select(_ => Enumerable.Repeat(true, calendar.Length).ToArray()).ToArray();
        var high = SweepAxes.ReferenceHighs.ToList().IndexOf(SweepDesign.Live.ReferenceHigh);

        // Five candidates on five sessions, each passing every gate but the strength, at 0.45, 0.52, 0.55, 0.65 and 0.9.
        SweepCandidate Candidate(int session, double strength)
        {
            var candidate = new SweepCandidate { Name = 0, Session = session, Year = 7, Breadth = 0.6, Uptrend = 1, Earnings = -1 };
            var plan = new SweepPlanOutcomes { RewardToRisk = 2.5, StopMoves = 1.5 };

            candidate.Strength[(int)SweepDesign.Live.Strength] = strength;
            candidate.Depth[high] = 2;
            candidate.DryUp[high] = 0.5;
            candidate.Band[(int)SweepDesign.Live.Support] = 3;
            candidate.Age[SweepCandidate.AgeAt(SweepDesign.Live.Trigger, SweepDesign.Live.Support)] = 0;
            candidate.Plans[SweepCandidate.PlanAt(SweepDesign.Live.Plan, SweepDesign.Live.Support)] = plan;

            return candidate;
        }

        SweepCandidate[] candidates = [Candidate(10, 0.45), Candidate(12, 0.52), Candidate(14, 0.55), Candidate(16, 0.65), Candidate(18, 0.9)];
        var replay = new IdeaReplay(series, candidates, members, allOn, 10);
        var pullbacks = new LargePullback(series, candidates);

        static int[] Sessions(IEnumerable<IdeaListing> listings) => [.. listings.Select(listing => listing.Session)];

        // At the open version's settings, every one on the sweep's grid, the walk keeps the listings the ideas' run keeps
        // at its base: the four of strength 0.5 or more.
        Assert.Equal([12, 14, 16, 18], Sessions(replay.Listings(SweepIdeas.BaseRule)));
        Assert.Equal(Sessions(replay.Listings(SweepIdeas.BaseRule)), Sessions(pullbacks.Listings(VersionFive)));

        // At a strength floor of 0.53, which the grid holds no level of, it keeps the three of 0.55 or more, where the
        // grid's 0.5 keeps four and its 0.6 two.
        Assert.Equal([14, 16, 18], Sessions(pullbacks.Listings(VersionFive with { StrengthFloor = 0.53 })));
        Assert.Equal([16, 18], Sessions(pullbacks.Listings(VersionFive with { StrengthFloor = 0.6 })));
        Assert.Equal(-1, SweepGrid.IndexOf(SweepGrid.Extended.StrengthBars, 0.53));

        // A reward to risk floor of 2.6 over plans of 2.5 keeps none, and one of 2.5 keeps the four again.
        Assert.Empty(pullbacks.Listings(VersionFive with { RewardToRiskFloor = 2.6 }));
        Assert.Equal([12, 14, 16, 18], Sessions(pullbacks.Listings(VersionFive with { RewardToRiskFloor = 2.5 })));
    }

    [Fact]
    public void AShapeMoveIsTestedAndProposedWithItsTestYearsBesideTheFiltersOwn()
    {
        // The stored proposal's levers: the strength floor moved from 0.5 to 0.53, the dry-up ceiling held, the trigger with
        // no threshold, and the reward to risk floor no value in its range reaches; one move.
        var levers = System.Text.Json.JsonSerializer.Serialize(new[]
        {
            new Lever(SwingGates.Trend, ShapeProposals.StrengthFloor, 0.5, 0.53, 40, 30, 20, 35),
            new Lever(SwingGates.Setup, ShapeProposals.DryUpCeiling, 1.5, 1.5, 20, 20, 10, 25),
            new Lever(SwingGates.Trigger, null, null, null, 8, 8, 5, 12),
            new Lever(SwingGates.Trade, ShapeProposals.RewardToRiskFloor, 2, null, 4, null, 5, 10),
        });

        Assert.Equal([(ShapeProposals.StrengthFloor, 0.53)], ShapeProcedures.MovesOf(levers));

        // Test years 2022 and 2023. The filter as it stands enters two trades in 2022 at +1 and -1 risks over their
        // benchmark and one in 2023 at +0.5; the moved filter keeps the 2022 winner and the 2023 trade and adds a 2023 trade
        // at +0.25: 2022 reads 0 against +1, 2023 0.5 against 0.75.
        var calendar = Weekdays(new DateOnly(2021, 1, 4), 780);
        var folds = LoopFolds.Of(calendar, calendar[^1]);
        var read = new LoopRead("GSPC", null!, [], [], null!, null!,new Dictionary<string, IReadOnlyList<EquityBrief.Core.Readings.FiledIncome>>(), calendar, 0, folds);

        int At(DateOnly day) => Array.IndexOf(calendar, day);

        ExitProcedures.Walked Trade(DateOnly entered, double edge) =>
            new(At(entered), At(entered) + 5, new FamilyTrade(new FamilyListing(0, At(entered), At(entered), 2, 0.5, 100, 95, 110, double.NaN, 63), 2, edge, 0), string.Empty);

        IReadOnlyList<ExitProcedures.Walked> standing = [Trade(new DateOnly(2022, 3, 1), 1), Trade(new DateOnly(2022, 6, 1), -1), Trade(new DateOnly(2023, 3, 1), 0.5)];
        IReadOnlyList<ExitProcedures.Walked> moved = [Trade(new DateOnly(2022, 3, 1), 1), Trade(new DateOnly(2023, 3, 1), 0.5), Trade(new DateOnly(2023, 6, 1), 0.25)];

        RuleWalk WalkOf(IReadOnlyList<ExitProcedures.Walked> trades) =>
            new("pullback", "the filter as it stands", [], 63, [], (_, _, _) => trades);

        var walkedAt = new List<FilterSettings>();
        var current = WalkOf(standing);
        var proposals = ShapeProcedures.Run(
            read,
            current,
            settings =>
            {
                walkedAt.Add(settings);

                return WalkOf(moved);
            },
            VersionFive,
            ShapeProcedures.MovesOf(levers),
            7,
            _ => { });

        var proposal = Assert.Single(proposals);

        Assert.Equal([VersionFive with { StrengthFloor = 0.53 }], walkedAt);
        Assert.Equal("the shape clock's move of the strengthFloor", proposal.Proposal);
        Assert.Equal("the swing filter's strength floor at 0.53", proposal.Words);
        Assert.Equal("{\"places\":null,\"key\":null,\"floor\":null,\"hooks\":{},\"shape\":{\"strengthFloor\":0.53}}", proposal.Change!.Json);
        Assert.Equal(
            [(2022, 0.0, 2, 1.0, 1), (2023, 0.5, 1, 0.75, 2)],
            [.. proposal.Evidence.Years.Select(year => (year.Year, year.CurrentTotal, year.CurrentUnits, year.ProposedTotal, year.ProposedUnits))]);
        Assert.Equal(folds.Count, proposal.StableFolds);
        Assert.All(proposal.Folds, fold => Assert.Equal("the swing filter's strength floor at 0.53", fold.Chosen));
    }

    [Fact]
    public async Task AnApprovedShapeMoveOpensTheNextFilterVersionFromTheNightAndAHookChangeOfThePullbackIsStoredForItsPage()
    {
        using var store = new TemporaryStore().Migrated();

        // Filter version 1 open at the base, the live filter registered under it, and a session the night reads.
        Assert.Equal(0, (await ShapeVerb(store, new DateTimeOffset(2026, 9, 1, 12, 0, 0, TimeSpan.Zero), "--settings", "strengthFloor=0.5", "--evidence", "the ruling")).Code);
        await RegisterLive(store, new DateTimeOffset(2026, 9, 2, 12, 0, 0, TimeSpan.Zero));
        LargeSession(store, "2026-10-09");

        // The S&P 500's run proposes the shape clock's move of the strength floor and a condition of the pullback, the
        // operator approving the move.
        LoopRun(store, LargeRun, "GSPC", "2026-10");
        LoopProposed(store, LargeRun, "GSPC", "pullback", ShapeProcedures.ProposalName(ShapeProposals.StrengthFloor), true, LoopChange.OfShape(ShapeProposals.StrengthFloor, 0.53).Json);
        LoopDecided(store, LargeRun, "GSPC", "pullback", ShapeProcedures.ProposalName(ShapeProposals.StrengthFloor), LoopDecisions.Approved);

        // By hand the move waits for the night, which alone opens a version.
        Assert.Equal(1, (await new LoopApply(At(2026, 10, 9, 23, 35), store.DatabaseFile, null).RunAsync("by-hand")).Waiting);
        Assert.Equal(["1|null"], FamilyRows(store, "SELECT version, closed_at FROM filter_version ORDER BY version;"));

        // The night opens version 2 with the floor moved, closing version 1, and registers the live filter again under
        // it, in the write that answers the approval.
        var night = await new LoopApply(At(2026, 10, 9, 23, 35), store.DatabaseFile, null).NightAsync("night-applied");

        Assert.Equal(["GSPC|pullback|applied"], [.. night.Applications.Select(one => $"{one.Index}|{one.Family}|{(one.Applied ? "applied" : "refused")}")]);
        Assert.StartsWith("the swing filter opens version 2 at the swing filter's strength floor at 0.53", night.Applications.Single().Words, StringComparison.Ordinal);
        Assert.Equal(["1|2026-10-09T23:35:00Z", "2|null"], FamilyRows(store, "SELECT version, closed_at FROM filter_version ORDER BY version;"));
        Assert.Equal(0.53, FilterSettings.Read(FamilyRows(store, "SELECT settings FROM filter_version WHERE version = '2';").Single()).StrengthFloor);
        Assert.Equal(
            [$"{SwingFamily.LiveCandidate("1")}|retired", $"{SwingFamily.LiveCandidate("2")}|registered"],
            FamilyRows(store, "SELECT candidate, event FROM candidate_register WHERE id > 1 ORDER BY id;"));
        Assert.Equal(["applied|the swing filter opens version 2 at the swing filter's strength floor at 0.53"], FamilyRows(store, "SELECT outcome, words FROM loop_applied;"));
        Assert.NotNull(night.WroteFrom);
        Assert.Equal(["0"], FamilyRows(store, "SELECT COUNT(*) FROM provisional_setting;"));

        // A later run's condition of the pullback approved is stored for the swing filter's page alone: no version opens and
        // nothing is registered.
        const string later = "loop-test-GSPC-20261012T120000Z";

        LargeSession(store, "2026-10-12");
        LoopRun(store, later, "GSPC", "2026-10");
        LoopProposed(store, later, "GSPC", "pullback", "winners against losers, ranked 1", true, LoopChange.OfHooks(new Dictionary<string, double>(StringComparer.Ordinal) { ["also_rsi_above"] = 50 }).Json);
        LoopDecided(store, later, "GSPC", "pullback", "winners against losers, ranked 1", LoopDecisions.Approved);

        var hooked = await new LoopApply(At(2026, 10, 12, 23, 35), store.DatabaseFile, null).NightAsync("night-hooked");

        Assert.Equal(["GSPC|pullback|applied"], [.. hooked.Applications.Select(one => $"{one.Index}|{one.Family}|{(one.Applied ? "applied" : "refused")}")]);
        Assert.Equal(["GSPC|pullback|also requires rsi at or above 50"], FamilyRows(store, "SELECT index_code, family, words FROM provisional_setting;"));
        Assert.Equal(["2|null"], FamilyRows(store, "SELECT version, closed_at FROM filter_version WHERE closed_at IS NULL;"));
        Assert.Equal(["3"], FamilyRows(store, "SELECT COUNT(*) FROM candidate_register;"));
        Assert.Null(hooked.WroteFrom);
    }

    [Fact]
    public async Task AnSAndP500PullbackPickIsWalkedUnderTheExitItsListCarriedOnThePlanItsVersionRead()
    {
        using var store = new TemporaryStore().Migrated();

        static string Day(DateOnly day) => day.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

        var sessions = SessionsFrom(new DateOnly(2026, 1, 2), 8);
        var night = sessions[0];

        // Filter version 1 open with the trade gate reading section 10's plan.
        Assert.Equal(0, (await ShapeVerb(store, new DateTimeOffset(2026, 1, 1, 22, 0, 0, TimeSpan.Zero), "--settings", "strengthFloor=0.5", "--trade", "clear", "--evidence", "the ruling")).Code);

        // AAA and BBB each bought at the night's close of 100, then closes of 101, 104, 108 and 90. Section 10's plan stops at
        // 95 and aims at 120, a risk of 5; the plan at the nearest bands stops at 92 and aims at 115, a risk of 8.
        foreach (var ticker in new[] { "AAA", "BBB" })
        {
            decimal[] closes = [100m, 101m, 104m, 108m, 90m];

            for (var at = 0; at < closes.Length; at++)
            {
                var close = closes[at].ToString(System.Globalization.CultureInfo.InvariantCulture);

                store.Execute(
                    "INSERT INTO bar (ticker, session_date, open, high, low, close, volume, source, observed_at, raw_close) " +
                    $"VALUES ('{ticker}', '{Day(sessions[at])}', '{close}', '{close}', '{close}', '{close}', 1000, 'test', '2026-01-02T22:00:00Z', '{close}');");
            }

            store.Execute(
                "INSERT INTO gate_result (ticker, session_date, version, code, market, trend, setup, trigger_pass, trade, swing_entry, swing_stop, swing_target, swing_stop_moves, clear_stop, clear_target, clear_stop_moves, exclusions, passed, gates) " +
                $"VALUES ('{ticker}', '{Day(night)}', '1', 'code', 1, 1, 1, 1, 1, '100', '92', '115', 4, '95', '120', 2, '[]', 1, '{{\"gates\":[],\"notes\":[]}}');");
        }

        // The list carried AAA under the menu's target at 1.5 risks and BBB under the plan's own exit.
        store.Execute(
            "INSERT INTO family_pick (session_date, ticker, family, state, place, also, exit) " +
            $"VALUES ('{Day(night)}', 'AAA', 'pullback', 'listed', 1, '[]', 14), ('{Day(night)}', 'BBB', 'pullback', 'listed', 2, '[]', NULL);");

        await new EquityBrief.Worker.Returns.ForwardReturnFiller(FixedClock.At(new DateTimeOffset(2026, 6, 1, 22, 0, 0, TimeSpan.Zero), SessionZones.UnitedStates), store.DatabaseFile).RunAsync("exit-fill");

        // AAA under the exit on section 10's plan, which the version read: its target of 107.50 sold at the close of 108 on
        // the third session, a win of 8 per cent. Section 10's plan under its own target of 120 is stopped by the close of
        // 90 under 95 on the fourth, a loss of 10 per cent, and the swing filter's rows keep that outcome; under the nearest
        // bands' plan the exit's target of 112 is never reached. BBB carries no exit and holds no row under it.
        Assert.Equal(
            [
                $"AAA|clear|loss|{Day(sessions[4])}|-10.0",
                $"AAA|exit|win|{Day(sessions[3])}|8.0",
                $"AAA|swing|loss|{Day(sessions[4])}|-10.0",
                $"BBB|clear|loss|{Day(sessions[4])}|-10.0",
                $"BBB|swing|loss|{Day(sessions[4])}|-10.0",
            ],
            Query(store, "SELECT ticker, horizon, IFNULL(outcome, 'none'), IFNULL(resolved_on, 'none'), printf('%.1f', return_pct) FROM forward_return WHERE horizon IN ('clear', 'swing', 'exit') ORDER BY ticker, horizon;"));
    }

    [Fact]
    public async Task TheShapeCommandAcceptsNoProposalAndSaysWhy()
    {
        using var store = new TemporaryStore().Migrated();

        Assert.Equal(0, (await ShapeVerb(store, RuledAt, "--settings", "strengthFloor=0.6", "--evidence", "the ruling")).Code);
        await RegisterLive(store, LiveRegisteredAt);
        StoreProposal(store, "1", FilterSettings.Proposed with { StrengthFloor = 0.6, DryUpCeiling = 0.85 });

        var before = ShapeTables(store);
        var (code, said) = await ShapeVerb(store, AcceptedAt, "--accept", "1");

        Assert.Equal(1, code);
        Assert.Contains(ShapeCommand.NoAcceptance, said, StringComparison.Ordinal);
        Assert.Equal(before, ShapeTables(store));
    }
}
