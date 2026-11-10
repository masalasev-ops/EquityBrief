using EquityBrief.Core.Candidates;
using EquityBrief.Core.Loop;
using EquityBrief.Core.Time;
using EquityBrief.Tests.Harness;
using EquityBrief.Worker.Candidates;
using EquityBrief.Worker.Loop;
using Xunit;

namespace EquityBrief.Tests.Checks;

// fixture-expectations, 23.10: the plan a rule's registration states read from its five parameters and stated back, the plan
// stated off read as none, each malformed plan refused; a plan trade walked by hand over constructed bars, filled at its level
// within its window, its stop placed from its fill, sold where the test sells and read in the risk its rule's own plan set; one
// never filled ended at its window's last session with no result, and one still waiting left open; and a live rule given its
// plan by the register, today's plan kept beside it as a variant where its family has room and walked unregistered where not.
// see: At phase 23's restart each price family trades the plan its long test recommends where it beats today's plan from 2019 above the ruled benchmark, today's plan kept beside it as a variant
public partial class FixtureExpectations
{
    static readonly PlanCombination SecondZoneWide = new(PlanPoints.SecondZone, 20, PlanPoints.TypicalStop(4), PlanPoints.FirstBand);

    static readonly PlanCombination DistanceStaged = new(PlanPoints.DistanceEntry(2.11), 20, PlanPoints.TypicalStop(2), PlanPoints.StagedSales);

    static readonly PlanCombination PullbackTimed = new(PlanPoints.FirstPullback, PlanPoints.FirstPullbackSessions, PlanPoints.RuleStop, PlanPoints.TimeLimit);

    [Fact]
    public void APlansFiveParametersReadOneCombinationAndAPlanStatedOffReadsAsNone()
    {
        foreach (var combination in new[] { SecondZoneWide, DistanceStaged, PullbackTimed, new PlanCombination(PlanPoints.Average, 5, PlanPoints.RuleStop, PlanPoints.RuleSale), new PlanCombination(PlanPoints.FirstZone, 10, PlanPoints.ZoneStop, PlanPoints.TimeLimit) })
        {
            Assert.Equal(combination, PlanHook.Of(PlanHook.ParametersOf(combination)));
            Assert.Equal(combination, PlanPrices.Named(combination.Words));
        }

        // By hand: the second zone within 20 sessions, 4 typical days under the buy, the first band above.
        Assert.Equal(
            new Dictionary<string, double> { [PlanHook.EntryParameter] = 2, [PlanHook.WindowParameter] = 20, [PlanHook.StopParameter] = 4, [PlanHook.SaleParameter] = 1 },
            PlanHook.ParametersOf(SecondZoneWide));

        // The plan stated off, and a rule stating none, trade the rule's own plan.
        Assert.Null(PlanHook.Of(new Dictionary<string, double> { [PlanHook.EntryParameter] = 0, [PlanHook.WindowParameter] = 0, [PlanHook.StopParameter] = 0, [PlanHook.SaleParameter] = 0 }));
        Assert.Null(PlanHook.Of(new Dictionary<string, double> { ["stopMoves"] = 1.5 }));

        // Each malformed plan is refused rather than read as the rule's own.
        Assert.Throws<ArgumentException>(() => PlanHook.Of(new Dictionary<string, double> { [PlanHook.EntryParameter] = 7, [PlanHook.WindowParameter] = 5 }));
        Assert.Throws<ArgumentException>(() => PlanHook.Of(new Dictionary<string, double> { [PlanHook.EntryParameter] = 2, [PlanHook.WindowParameter] = 7 }));
        Assert.Throws<ArgumentException>(() => PlanHook.Of(new Dictionary<string, double> { [PlanHook.EntryParameter] = 4, [PlanHook.WindowParameter] = 5, [PlanHook.StopParameter] = -1 }));
        Assert.Throws<ArgumentException>(() => PlanHook.Of(new Dictionary<string, double> { [PlanHook.EntryParameter] = 2, [PlanHook.WindowParameter] = 5, [PlanHook.DistanceParameter] = 1 }));
        Assert.Throws<ArgumentException>(() => PlanHook.Of(new Dictionary<string, double> { [PlanHook.EntryParameter] = 2, [PlanHook.WindowParameter] = 5, [PlanHook.StopParameter] = 3.5 }));

        // The hooks read the plan, say it in their words, and take no exit of the menu beside it.
        var hooks = RuleHooks.Of(PlanHook.ParametersOf(SecondZoneWide));

        Assert.Equal(SecondZoneWide, hooks.Plan);
        Assert.False(hooks.IsOff);
        Assert.Equal("plan: " + SecondZoneWide.Words, hooks.Words());
        Assert.Throws<ArgumentException>(() => RuleHooks.Of(new Dictionary<string, double>(PlanHook.ParametersOf(SecondZoneWide)) { [RuleHooks.ExitParameter] = 1 }));

        // The dip's family word and its sale's level, stated where every family rule pins them, are the dip's own.
        Assert.Equal(Core.Families.DipRule.Name, PlanPoints.DipName);
        Assert.Equal(Core.Families.DipRule.RsiExit, PlanPoints.DipRsiAbove);
    }

    // Six sessions: the night's close at 100 with the rule's stop at 95, a level at 98 traded into on the second session after
    // the night, its stop 2 typical days of 1 under the fill at 96, the target of 103 closed through on the fourth and sold at
    // the fifth's open of 103.20: (103.20 - 98) / (100 - 95) = 1.04 risks.
    static PlanBars SixSessions(int count = 6) => new(
        [.. Enumerable.Range(0, count).Select(day => new DateOnly(2026, 10, 1).AddDays(day))],
        [.. new[] { 100, 99.5, 98.5, 99, 102, 103.2 }.Take(count)],
        [.. new[] { 99.5, 99, 97.5, 98.2, 100.5, 102.8 }.Take(count)],
        [.. new[] { 100, 99.2, 98.4, 101, 103.5, 103 }.Take(count)]);

    static PlanOrder OrderAt(double? level) =>
        new(DistanceStaged.Words, PlanPoints.DistanceEntry(2), level, 5, PlanPoints.TypicalStop(2), 95, null, 1, 103, [], null, null, 10);

    [Fact]
    public void APlanTradeFillsAtItsLevelSellsWhereTheTestSellsAndReadsInItsRulesRisk()
    {
        var bars = SixSessions();
        var night = bars.Sessions[0];

        var end = PlanTrades.Ended(OrderAt(98), 100m, 95m, night, bars)!;

        Assert.Equal((bars.Sessions[2], 98.0, bars.Sessions[4], bars.Sessions[5], 103.2, PlanPoints.Reached), (end.FilledOn!.Value, end.Fill!.Value, end.On, end.SoldOn!.Value, end.Sold!.Value, end.End));
        Assert.Equal(1.04, end.Result!.Value, 9);

        // Bought at its open where a session opens under the level.
        var opensUnder = PlanTrades.Ended(OrderAt(99.8), 100m, 95m, night, bars)!;

        Assert.Equal((bars.Sessions[1], 99.5), (opensUnder.FilledOn!.Value, opensUnder.Fill!.Value));

        // A level no session trades into within its window ends unfilled on the window's last session, with no result.
        var unfilled = PlanTrades.Ended(OrderAt(90), 100m, 95m, night, bars)!;

        Assert.Equal((bars.Sessions[5], (double?)null, PlanEnd.Unfilled), (unfilled.On, unfilled.Result, unfilled.End));

        // A window the stored sessions have not passed yet is still waiting, and a filled trade not yet sold is held.
        Assert.Null(PlanTrades.Where(OrderAt(90), 100m, 95m, night, SixSessions(4)));
        Assert.Equal((bars.Sessions[2], 98.0, (PlanEnd?)null), PlanTrades.Where(OrderAt(98), 100m, 95m, night, SixSessions(4)) is { } held ? (held.FilledOn!.Value, held.Fill!.Value, held.End) : default);

        // An adjustment since the night moves every price by the same factor: the same trade over bars stored at half.
        var halved = new PlanBars(bars.Sessions, [.. bars.Opens.Select(one => one / 2)], [.. bars.Lows.Select(one => one / 2)], [.. bars.Closes.Select(one => one / 2)]);
        var scaled = PlanTrades.Ended(OrderAt(98), 100m, 95m, night, halved)!;

        Assert.Equal((98.0, 103.2), (scaled.Fill!.Value, scaled.Sold!.Value));
        Assert.Equal(1.04, scaled.Result!.Value, 9);
    }

    static readonly Dictionary<string, double> BreakoutLive = new(StringComparer.Ordinal)
    {
        ["highSessions"] = 126, ["indexAverageSessions"] = 0, ["rangeCeiling"] = 0.85, ["stopMoves"] = 1.5, ["vixLookbackSessions"] = 0, ["volumeMultiple"] = 1.5,
    };

    const string PlanLive = "the live breakout rule at a 126-session high";

    static async Task<(TemporaryStore Store, DateTimeOffset At)> PlanRegisterAsync(int variants)
    {
        var store = new TemporaryStore().Migrated();
        var at = new DateTimeOffset(2026, 11, 10, 15, 0, 0, TimeSpan.Zero);
        var registrar = new CandidateRegistrar(FixedClock.At(at, SessionZones.UnitedStates), store.DatabaseFile);

        Assert.Equal(CandidateRegistrar.Registered, (await registrar.RegisterAsync(PlanLive, "the breakout rule", "its record", "breakout", BreakoutLive, "plan-live")).Outcome);

        for (var variant = 1; variant <= variants; variant++)
        {
            var moved = new Dictionary<string, double>(BreakoutLive, StringComparer.Ordinal) { ["volumeMultiple"] = 1.5 + (variant / 10.0) };
            var said = await new CandidateRegistrar(FixedClock.At(at.AddMinutes(variant), SessionZones.UnitedStates), store.DatabaseFile)
                .RegisterAsync(FormattableString.Invariant($"the breakout rule at {moved["volumeMultiple"]} times the volume"), "the breakout rule", "its record", "breakout", moved, FormattableString.Invariant($"plan-variant-{variant}"));

            Assert.Equal(CandidateRegistrar.Registered, said.Outcome);
        }

        return (store, at.AddHours(1));
    }

    [Fact]
    public async Task AFamilysLiveRuleIsGivenItsPlanAndTodaysPlanStandsBesideItWhereTheFamilyHasRoom()
    {
        var (store, at) = await PlanRegisterAsync(0);

        using (store)
        {
            var registrar = new CandidateRegistrar(FixedClock.At(at, SessionZones.UnitedStates), store.DatabaseFile);
            var given = await registrar.RegisterPlannedAsync(PlanLive, SecondZoneWide, true, "23.10's table", "plan-given");

            Assert.Equal(CandidateRegistrar.Registered, given.Outcome);

            var standing = CandidateFamily.Standing(await registrar.RowsAsync(), at);
            var live = standing.Single(row => row.Candidate == PlanLive);
            var today = standing.Single(row => row.Candidate == CandidateRegistrar.VariantOf(PlanLive, CandidateRegistrar.TodaysPlan));

            Assert.Equal(SecondZoneWide, RuleHooks.Of(CandidateEvaluator.Read(live.Parameters)).Plan);
            Assert.Null(RuleHooks.Of(CandidateEvaluator.Read(today.Parameters)).Plan);
            Assert.Equal(1.5, RuleHooks.Without(CandidateEvaluator.Read(today.Parameters))["stopMoves"]);
            Assert.Equal(2, standing.Count);
            Assert.Equal(1, (await registrar.RowsAsync()).Count(row => row.Event == CandidateFamily.Retired && row.Retires == PlanLive));

            // Where today's plan stays traded the plan stands beside the live rule as a variant, which retires nothing.
            var later = new CandidateRegistrar(FixedClock.At(at.AddHours(1), SessionZones.UnitedStates), store.DatabaseFile);
            var beside = await later.RegisterPlannedAsync(PlanLive, PullbackTimed, false, "23.10's table", "plan-beside");

            Assert.Equal(CandidateRegistrar.Registered, beside.Outcome);

            var after = CandidateFamily.Standing(await later.RowsAsync(), at.AddHours(1));

            Assert.Equal(PullbackTimed, RuleHooks.Of(CandidateEvaluator.Read(after.Single(row => row.Candidate == CandidateRegistrar.VariantOf(PlanLive, CandidateRegistrar.PlannedBy(PullbackTimed))).Parameters)).Plan);
            Assert.Equal(1, (await later.RowsAsync()).Count(row => row.Event == CandidateFamily.Retired && row.Retires == PlanLive));
        }
    }

    [Fact]
    public async Task AFamilyHoldingNineGivesItsLiveRuleThePlanAndWalksTodaysPlanUnregistered()
    {
        var (store, at) = await PlanRegisterAsync(CandidateFamily.Maximum - 1);

        using (store)
        {
            var registrar = new CandidateRegistrar(FixedClock.At(at, SessionZones.UnitedStates), store.DatabaseFile);
            var given = await registrar.RegisterPlannedAsync(PlanLive, SecondZoneWide, true, "23.10's table", "plan-full");

            Assert.Equal(CandidateRegistrar.Registered, given.Outcome);
            Assert.Contains($"'{CandidateRegistrar.VariantOf(PlanLive, CandidateRegistrar.TodaysPlan)}' walked unregistered, the family keeping 9", given.Detail, StringComparison.Ordinal);
            Assert.Equal(CandidateFamily.Maximum, CandidateFamily.Standing(await registrar.RowsAsync(), at).Count);

            // A variant with no room and no plan to trade writes nothing, and a rule that is not live is refused.
            var none = await new CandidateRegistrar(FixedClock.At(at.AddHours(1), SessionZones.UnitedStates), store.DatabaseFile).RegisterPlannedAsync(PlanLive, PullbackTimed, false, "23.10's table", "plan-none");

            Assert.Equal(CandidateRegistrar.Refused, none.Outcome);
            Assert.Equal(CandidateRegistrar.Refused, (await registrar.RegisterPlannedAsync("the breakout rule at 1.6 times the volume", SecondZoneWide, true, "23.10's table", "plan-not-live")).Outcome);
            Assert.Equal(CandidateRegistrar.Refused, (await registrar.RegisterPlannedAsync(PlanLive, PlanPoints.Combinations.First(PlanPrices.Traded), true, "23.10's table", "plan-own")).Outcome);
        }
    }
}
