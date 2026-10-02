using EquityBrief.Core.Families;
using EquityBrief.Core.Filter;
using EquityBrief.Core.Returns;
using EquityBrief.Core.Time;
using EquityBrief.Tests.Harness;
using EquityBrief.Worker.Families;
using EquityBrief.Worker.Returns;

namespace EquityBrief.Tests.Checks;

// Sector leadership: the sectors' ranking and each member's place worked by hand, a tie and a sector a member
// short of the floor among them, the top quarter at its boundary, the pullback's variant in the top sectors
// read over those standings, and the family evaluator and the lister drawing no leaders' card.
// see: The sector leaders are a variant of the pullback's starting point and not a family of their own
public partial class FixtureExpectations
{
    // The claims sector leadership makes, which this check reaches: section 17's rows for its settings and
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

        // The pullback's variant in the top sectors reads those standings at its own settings, the top 3 and
        // the top quarter. A2, second of Alpha's eight in the first sector, leads; A3, third, is outside the
        // quarter. D1 and D2 lead the third sector, D3 does not; G1, first in the fourth sector, does not.
        bool Leads(string ticker, int topSectors = LeaderRule.TopSectors, int shareOf = LeaderRule.QuarterOf) =>
            Core.Candidates.SwingFilterRule.Leads(places[ticker], topSectors, shareOf);

        Assert.Equal((true, false), (Leads("A2"), Leads("A3")));
        Assert.Equal((true, true, false), (Leads("D1"), Leads("D2"), Leads("D3")));
        Assert.False(Leads("G1"));

        // At other settings: A3 is inside the top third of eight, the first 3, and G1 inside the top 4 sectors.
        Assert.Equal((true, true), (Leads("A3", shareOf: 3), Leads("G1", topSectors: 4)));

        // A member of a sector too small to rank, one the membership names no sector for, and one with no
        // return lead nothing at any setting.
        Assert.Equal((false, false, false), (Leads("S1", 11, 1), Leads("N1", 11, 1), Leads("A9", 11, 1)));
        Assert.Equal(1, LeaderRule.Cut(1, 4));
    }

    [Fact]
    public async Task TheFamilyEvaluatorStoresNoLeaderRowAndThePageListsNoLeadersCardWhileAnEarlierLeaderIsStillNamed()
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

        // A1, A2 and B1 would lead at the pullback's buy point, and the family evaluator evaluates the
        // breakouts and the drift alone: it stores no leader's row and counts no leader family.
        Assert.Equal(["0"], FamilyRows(store, "SELECT COUNT(*) FROM family_result WHERE family = 'leader';"));
        Assert.DoesNotContain(outcome.Families, family => family.Family == LeaderRule.Name);
        Assert.Equal([BreakoutRule.Name, DriftRule.Name], outcome.Families.Select(family => family.Family));

        // The page: A1, which the pullback passed, is listed under it with no other label, and the page draws
        // no leaders' card.
        await new FamilyLister(clock, store.DatabaseFile).RunAsync("families-leaders");

        Assert.Equal(["pullback|A1|listed|1|[]"], FamilyRows(store, "SELECT family, ticker, state, place, also FROM family_pick ORDER BY place IS NULL, place;"));
        Assert.DoesNotContain(SetupFamilies.InPageOrder, family => family.Name == LeaderRule.Name);

        // A leader an earlier night listed is still named as one, its trade read under the pullback's plan.
        Assert.Equal("Sector leader", SetupFamilies.Named(LeaderRule.Name)?.Label);
        Assert.Contains($"WHEN '{LeaderRule.Name}' THEN CASE", SetupFamilies.HorizonIn("f", "s"), StringComparison.Ordinal);
        Assert.Contains($"WHEN '{LeaderRule.Name}' THEN {ForwardReturnSeries.SetupSessionCap}", SetupFamilies.CapIn("f"), StringComparison.Ordinal);
    }
}
