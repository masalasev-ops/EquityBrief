using EquityBrief.Core.Families;
using EquityBrief.Core.Filter;
using EquityBrief.Core.Returns;
using EquityBrief.Core.Time;
using EquityBrief.Tests.Harness;
using EquityBrief.Worker.Families;
using EquityBrief.Worker.Returns;

namespace EquityBrief.Tests.Checks;

// The sector leader family: the sectors' ranking and each member's place worked by hand, a tie and a sector
// a member short of the floor among them, the top quarter at its boundary, the pullback's stored gates
// taken as they stand, and the evaluator and the lister over a constructed night.
// see: A sector leader is a stock in the top quarter of a top three sector, bought at the pullback's buy point
public partial class FixtureExpectations
{
    // The claims the leader's rule makes, which this check reaches: section 17's rows for its settings and
    // section 18's rows for a member with no sector and a sector too small to rank.
    internal static readonly string[] LeaderClaims =
    [
        CheckReach.Key(Scope.LimitsTable, "Leader sectors"),
        CheckReach.Key(Scope.LimitsTable, "Leader share"),
        CheckReach.Key(Scope.LimitsTable, "Sector ranking floor"),
        CheckReach.Key(Scope.FailureTable, "A member the membership names no sector for"),
        CheckReach.Key(Scope.FailureTable, "A sector too small to rank"),
    ];

    // Five sectors. Alpha holds eight members with returns of 0.80 down to 0.10 in steps of a tenth, a median
    // of 0.45. Beta holds five at 0.50, 0.40, 0.30, 0.20 and 0.10, a median of 0.30. Delta holds five at
    // 0.35, 0.30, 0.30, 0.25 and 0.05, a median of 0.30, the same as Beta's. Gamma holds five at 0.10, 0.05,
    // 0.00, -0.05 and -0.10, a median of 0.00. Small holds four, a member short of the floor of five, at
    // 0.90 each. One member has no sector, and one in Alpha has no return.
    static IReadOnlyList<(string Ticker, string? Sector, double? Return)> LeaderMembers() =>
    [
        .. Enumerable.Range(0, 8).Select(at => ($"A{at + 1}", (string?)"Alpha", (double?)Math.Round(0.80 - 0.10 * at, 2))),
        .. new[] { 0.50, 0.40, 0.30, 0.20, 0.10 }.Select((over, at) => ($"B{at + 1}", (string?)"Beta", (double?)over)),
        .. new[] { 0.35, 0.30, 0.30, 0.25, 0.05 }.Select((over, at) => ($"D{at + 1}", (string?)"Delta", (double?)over)),
        .. new[] { 0.10, 0.05, 0.00, -0.05, -0.10 }.Select((over, at) => ($"G{at + 1}", (string?)"Gamma", (double?)over)),
        .. Enumerable.Range(0, 4).Select(at => ($"S{at + 1}", (string?)"Small", (double?)0.90)),
        ("N1", null, 0.95),
        ("A9", "Alpha", null),
    ];

    [Fact]
    public void TheSectorsAreRankedByTheirMembersMedianReturnAndALeaderIsInTheTopQuarterOfATopThreeSector()
    {
        Assert.Equal((3, 4, 5), (LeaderRule.TopSectors, LeaderRule.QuarterOf, LeaderRule.SectorFloor));
        Assert.Equal(["market", "sector", "leader", "setup", "trigger", "trade"], LeaderRule.Order);

        var (sectors, places) = LeaderRule.Standings(LeaderMembers());

        // Alpha first at 0.45. Beta and Delta tie at 0.30 and take the sector's name, Beta second and Delta
        // third. Gamma fourth at 0.00. Small holds four members with a return, one short of the five a
        // sector is ranked on, so it is not ranked whatever its median.
        Assert.Equal(
            ["Alpha 8 0.45 1", "Beta 5 0.30 2", "Delta 5 0.30 3", "Gamma 5 0.00 4", "Small 4 0.90 -"],
            sectors.Select(sector => FormattableString.Invariant($"{sector.Sector} {sector.Counted} {sector.Median:0.00} {(sector.Rank is { } rank ? rank.ToString(System.Globalization.CultureInfo.InvariantCulture) : "-")}")));

        // The top quarter, rounded up: 2 of Alpha's 8, and 2 of a sector of 5. In Delta D2 and D3 tie at
        // 0.30 for the second place, and the ticker puts D2 second and D3 third, so D2 is in and D3 is out.
        Assert.Equal((1, 2), (places["A1"].Place!.Value, places["A1"].Cut));
        Assert.Equal((2, 3), (places["A2"].Place!.Value, places["A3"].Place!.Value));
        Assert.Equal((2, 2, 3, 2), (places["D2"].Place!.Value, places["D2"].Cut, places["D3"].Place!.Value, places["D3"].Cut));
        Assert.Equal((null, null), (places["N1"].Sector, places["N1"].Place));
        Assert.Equal(("Alpha", null), (places["A9"].Sector!.Sector, places["A9"].Place));

        FamilyResult Leader(string ticker, bool setup = true, bool trigger = true, bool trade = true, string[]? exclusions = null, Gate? market = null)
        {
            var member = LeaderMembers().Single(one => one.Ticker == ticker);

            return LeaderRule.Evaluate(new LeaderInputs(
                ticker, market ?? OpenMarket, member.Sector, member.Return, places[ticker], 4,
                setup, trigger, trade, 100m, 96m, 110m, 2.5, exclusions ?? []));
        }

        // A2, second of Alpha's eight in the first sector: a leader, on the pullback's plan as stored.
        var passing = Leader("A2");

        Assert.True(passing.Passed);
        Assert.Equal((100m, 96m, 110m, -1.0, 0.70), (passing.Entry!.Value, passing.Stop!.Value, passing.Target!.Value, passing.OrderBy!.Value, passing.ThenBy!.Value));
        Assert.Equal("its sector, Alpha, ranks 1 of 4 by its members' median return, inside the top 3", GateOf(passing, LeaderRule.Sector).Reason);
        Assert.Equal("its return of 70.0% is 2 of the 8 in its sector, inside the top quarter, the first 2", GateOf(passing, LeaderRule.Leader).Reason);
        Assert.Equal("2.5", GateOf(passing, FamilyRule.Trade).Values[FamilyRule.RewardToRiskValue]);

        // The boundary of the quarter: A3, third of eight, is outside it. The boundary of the sectors: D1,
        // first in the third sector, is a leader; G1, first in the fourth, is not.
        Assert.Equal((false, 1), (Leader("A3").Passed, Leader("A3").Missed));
        Assert.Equal("its return of 60.0% is 3 of the 8 in its sector, outside the top quarter, the first 2", GateOf(Leader("A3"), LeaderRule.Leader).Reason);
        Assert.True(Leader("D1").Passed);
        Assert.True(Leader("D2").Passed);
        Assert.False(Leader("D3").Passed);
        Assert.Equal((false, "its sector, Gamma, ranks 4 of 4 by its members' median return, outside the top 3"), (Leader("G1").Passed, GateOf(Leader("G1"), LeaderRule.Sector).Reason));

        // A member of a sector too small to rank, one the membership names no sector for, and one with no
        // return: none passes, each saying why.
        Assert.Equal("its sector, Small, holds 4 member(s) with a return, fewer than the 5 a sector is ranked on", GateOf(Leader("S1"), LeaderRule.Sector).Reason);
        Assert.Equal("the membership names no sector for the name", GateOf(Leader("N1"), LeaderRule.Sector).Reason);
        Assert.Equal("not available: no return over the long span is stored for the name in a sector", GateOf(Leader("A9"), LeaderRule.Leader).Reason);

        // The pullback's buy point: a leader whose stored setup, trigger or trade gate did not pass is not
        // passed, each one gate short. Nothing here reads the filter's trend and strength gate, so a leader
        // that gate failed is passed all the same.
        Assert.Equal((false, 1), (Leader("A1", setup: false).Passed, Leader("A1", setup: false).Missed));
        Assert.Equal((false, 1), (Leader("A1", trigger: false).Passed, Leader("A1", trigger: false).Missed));
        Assert.Equal((false, 1, null), (Leader("A1", trade: false).Passed, Leader("A1", trade: false).Missed, Leader("A1", trade: false).Stop));
        Assert.DoesNotContain(LeaderRule.Order, name => name == SwingGates.Trend);

        // The market check closes it, and an exclusion the filter stored for the plan keeps it off.
        Assert.False(Leader("A1", market: ClosedMarket).Passed);
        Assert.Equal((false, 0), (Leader("A1", exclusions: [SwingGates.EarningsExclusion]).Passed, Leader("A1", exclusions: [SwingGates.EarningsExclusion]).Missed));

        // The order: the sector's rank, then the stock's return inside it, then the ticker.
        Assert.Equal(["A1", "A2", "B1", "B2", "D1", "D2"], FamilyRule.Ranked(LeaderMembers().Where(member => member.Sector is not null && member.Return is not null).Select(member => Leader(member.Ticker))).Select(result => result.Ticker));
    }

    [Fact]
    public async Task TheFamilyEvaluatorPassesALeaderOnThePullbacksStoredGatesAndThePageListsOneBothPassUnderThePullback()
    {
        using var store = new TemporaryStore().Migrated();

        store.Execute("INSERT INTO filter_version (version, settings, opened_at, closed_at, evidence) VALUES ('1', '{\"trade\":\"clear\"}', '2026-09-20T00:00:00Z', NULL, 'test');");
        store.Execute($"INSERT INTO list_rule (session_date, rule) VALUES ('{BreakoutNight}', 'filter');");

        // The members above on the night, each with a bar, its sector and its return. The swing filter
        // stored its setup, trigger and trade gates as passed for A1, A2, B1 and G1, and its own verdict as
        // passed for A1 alone, at rank 1: A2 and B1 failed its trend and strength gate. D1's setup did not pass.
        foreach (var (ticker, sector, over) in LeaderMembers())
        {
            StoreYear(store, ticker, [new FamilyBar(new DateOnly(2026, 10, 2), 101m, 99m, 100m, 1000)]);
            store.Execute(
                "INSERT INTO membership (index_code, ticker, joined, \"left\", observed_at, sector) " +
                $"VALUES ('GSPC', '{ticker}', '2025-01-02', NULL, '2025-01-02T00:00:00Z', {(sector is null ? "NULL" : $"'{sector}'")});");
            store.Execute(
                "INSERT INTO swing_reading (ticker, session_date, bars, return_long) " +
                $"VALUES ('{ticker}', '{BreakoutNight}', 252, {(over is { } held ? FamilyRule.Figure(held) : "NULL")});");

            var buyPoint = ticker is "A1" or "A2" or "B1" or "G1";

            FilterRow(store, BreakoutNight, ticker, passed: ticker == "A1", rank: ticker == "A1" ? 1 : null);
            store.Execute($"UPDATE gate_result SET setup = {(buyPoint ? 1 : 0)}, trigger_pass = {(buyPoint ? 1 : 0)}, trade = {(buyPoint ? 1 : 0)}, trend = {(ticker == "A1" ? 1 : 0)}, clear_reward_to_risk = 2.5 WHERE ticker = '{ticker}';");
        }

        var clock = FixedClock.At(new DateTimeOffset(2026, 10, 2, 23, 40, 0, TimeSpan.Zero), SessionZones.UnitedStates);
        var outcome = await new FamilyEvaluator(clock, store.DatabaseFile).RunAsync("rules-leaders");

        // A1 and A2 are in Alpha's top quarter and B1 in Beta's, each at the pullback's buy point: three
        // leaders, in the order of the sector's rank and then the return, on the plan the filter's row
        // stored, bought at 100 with the stop at 96 and the target at 110. G1 is at a buy point in the
        // fourth sector. D1 leads the third sector and its setup did not pass.
        Assert.Equal(
            ["A1|1|100|96|110|-1", "A2|2|100|96|110|-1", "B1|3|100|96|110|-2"],
            FamilyRows(store, "SELECT ticker, place, entry, stop, target, order_by FROM family_result WHERE family = 'leader' AND passed = 1 ORDER BY place;"));
        Assert.Equal(["G1|0|1", "D1|0|3"], FamilyRows(store, "SELECT ticker, passed, missed FROM family_result WHERE family = 'leader' AND ticker IN ('G1', 'D1') ORDER BY ticker DESC;"));
        Assert.Equal(3, outcome.Families.Single(family => family.Family == LeaderRule.Name).Passed);

        // The page: A1 passed the pullback as well and is listed under it, first, with the leader's label;
        // the leaders' card lists A2 and B1 and holds A1 as under another.
        await new FamilyLister(clock, store.DatabaseFile).RunAsync("families-leaders");

        Assert.Equal(
            ["pullback|A1|listed|1|[\"leader\"]", "leader|A2|listed|2|[]", "leader|B1|listed|3|[]", "leader|A1|under another|null|[]"],
            FamilyRows(store, "SELECT family, ticker, state, place, also FROM family_pick ORDER BY place IS NULL, place;"));

        // A leader's trade is the pullback's plan on the filter's row, so its outcome is that row's: the
        // filler scores no row of its own for it, and the next night reads A2's trade as open from the
        // filter row's outcome under the plan clear of the noise.
        await new ForwardReturnFiller(clock, store.DatabaseFile).RunAsync("returns-leaders");

        Assert.Equal(["0"], FamilyRows(store, "SELECT COUNT(*) FROM forward_return WHERE horizon NOT IN ('clear', 'clear-20', 'swing', 'swing-20', '5', '21', 'setup');"));
        Assert.Equal(["clear"], FamilyRows(store, "SELECT horizon FROM forward_return WHERE ticker = 'A2' AND horizon = '" + ForwardReturnSeries.Clear + "';"));
    }
}
