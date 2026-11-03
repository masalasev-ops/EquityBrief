using System.Globalization;
using EquityBrief.Api.Reading;
using EquityBrief.Core.Ladders;
using EquityBrief.Core.Time;
using EquityBrief.Tests.Harness;
using EquityBrief.Web.Marks;
using Xunit;

namespace EquityBrief.Tests.Checks;

// 22.32: a stock with fewer sessions than its 200-day average needs draws its plan from the levels its sessions give, worked by
// hand over constructed bands, and only a stock with none says no plan can be drawn. Nothing is written and no rule reads it.
// see: A stock with too few sessions for its averages draws its plan from its levels when a page is read and no rule reads it
public partial class FixtureExpectations
{
    static readonly DateOnly ShortNight = new(2026, 11, 2);

    static TemporaryStore ShortStore(int sessions, bool bands = true, bool below = true, bool typical = true)
    {
        var store = new TemporaryStore().Migrated();

        for (var back = 0; back < sessions; back++)
        {
            var day = ShortNight.AddDays(-back).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

            store.Execute($"INSERT INTO bar (ticker, session_date, open, high, low, close, volume, source, observed_at, raw_close) VALUES ('NEWCO', '{day}', '100', '101', '99', '100', 1000, 'test', '{day}T23:00:00Z', '100');");
        }

        var night = ShortNight.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        store.Execute($"INSERT INTO ladder (ticker, as_of, trend_state, plan) VALUES ('NEWCO', '{night}', 'not_classified', '{{\"tranches\":[],\"exits\":[],\"invalidation\":null,\"events\":[],\"arithmetic\":{{}},\"earningsRule\":[{{\"eventDate\":\"2026-10-29\"}}],\"reason\":\"no 200-day average\"}}');");

        if (typical)
        {
            store.Execute($"INSERT INTO indicator (ticker, session_date, name, value, bar_count) VALUES ('NEWCO', '{night}', 'atr14', 2.0, {sessions});");
        }

        void Band(string low, string high, string role) =>
            store.Execute($"INSERT INTO level (ticker, as_of, low_edge, high_edge, role, immediate, strength, has_non_average_anchor, members) VALUES ('NEWCO', '{night}', '{low}', '{high}', '{role}', 0, 3, 1, '[]');");

        if (bands)
        {
            if (below)
            {
                Band("90", "95", "support");
                Band("80", "82", "support");
            }

            Band("110", "112", "resistance");
        }

        return store;
    }

    static ReadApi ShortRead(TemporaryStore store) =>
        new(store.DatabaseFile, FixedClock.At(new DateTimeOffset(2026, 11, 3, 0, 0, 0, TimeSpan.Zero), SessionZones.UnitedStates));

    [Fact]
    public async Task AStockWithTooFewSessionsDrawsItsPlanFromItsLevelsAndSaysWhyTheAverageIsMissing()
    {
        using var store = ShortStore(112);

        var row = await ShortRead(store).PlanLadderAsync("NEWCO", ShortNight);

        // Worked by hand over the bands: it buys in the two supports beneath the 100 close, nearest first; the first's stop is
        // the low edge of the one beneath it, 80, the second has none beneath it, and the whole idea is wrong below 80, the
        // lowest either depends on; it sells in the resistance above.
        var rows = NameScreen.PlanRows(row);
        var tranches = rows.Where(one => one.Kind == PlanKind.Tranche).ToArray();

        Assert.Equal([(90m, 95m, (decimal?)80m), (80m, 82m, null)], tranches.Select(one => (one.LowEdge, one.HighEdge, one.Stop)));
        Assert.Equal(110m, rows.First(one => one.Kind == PlanKind.Exit).LowEdge);
        Assert.Single(rows, one => one.Kind == PlanKind.Invalidation && one.LowEdge == 80m);

        // The first of 112 calendar days back from the night, and the words the operator ruled.
        Assert.Equal(
            "This stock started trading on 14 Jul 2026, so it has 112 of the 200 trading days a 200-day average needs. The plan below uses the levels its 112 days show, without the average.",
            row!.ShortNote);
        Assert.Null(row.ShortWhy);

        var drawn = NameScreen.EntryAndExitPlan(new MarkRenderer(), "NEWCO", row, 100m);

        Assert.Contains("data-short-history=\"true\">This stock started trading on 14 Jul 2026", drawn, StringComparison.Ordinal);
        Assert.DoesNotContain("data-plan=\"none\"", drawn, StringComparison.Ordinal);

        // The night's earnings rule is carried over, the second book stays empty and the plan's shape is one the page reads.
        Assert.Contains("\"earningsRule\":[{\"eventDate\":\"2026-10-29\"}]", row.Plan, StringComparison.Ordinal);
        Assert.NotNull(NameScreen.EventBook(row));

        // Nothing is written: the night's stored plan stands, which is all a rule or a gate reads.
        Assert.Contains("no 200-day average", (await ShortRead(store).LadderAsync("NEWCO", ShortNight))!.Plan, StringComparison.Ordinal);
        Assert.Equal(["1"], FamilyRows(store, "SELECT COUNT(*) FROM ladder;"));
    }

    [Fact]
    public async Task AStockAtTheAveragesSessionsReadsItsStoredPlanAndOneShortOfThemTheLevels()
    {
        using var enough = ShortStore(ShortHistoryPlan.SessionsTheAverageNeeds);

        var stored = await ShortRead(enough).PlanLadderAsync("NEWCO", ShortNight);

        Assert.Equal((await ShortRead(enough).LadderAsync("NEWCO", ShortNight))!.Plan, stored!.Plan);
        Assert.Null(stored.ShortNote);
        Assert.Null(stored.ShortWhy);
        Assert.Empty(NameScreen.PlanRows(stored));

        using var one = ShortStore(ShortHistoryPlan.SessionsTheAverageNeeds - 1);

        Assert.Contains("199 of the 200 trading days", (await ShortRead(one).PlanLadderAsync("NEWCO", ShortNight))!.ShortNote, StringComparison.Ordinal);

        // A stock with no bars at all reads what the night stored for it, here nothing.
        using var empty = new TemporaryStore().Migrated();

        Assert.Null(await ShortRead(empty).PlanLadderAsync("NEWCO", ShortNight));
    }

    [Fact]
    public async Task OnlyAStockWithNoLevelsSaysNoPlanCanBeDrawnWithThatReason()
    {
        using var none = ShortStore(9, bands: false);

        var row = await ShortRead(none).PlanLadderAsync("NEWCO", ShortNight);
        var drawn = NameScreen.EntryAndExitPlan(new MarkRenderer(), "NEWCO", row, 100m);

        Assert.Null(row!.ShortNote);
        Assert.Equal("This stock started trading on 25 Oct 2026, so it has 9 of the 200 trading days a 200-day average needs, and too few for any support or resistance level, so no plan can be drawn", row.ShortWhy);
        Assert.Contains($"<p class=\"degraded\" data-plan=\"none\">No entry and exit plan is drawn for NEWCO: {System.Net.WebUtility.HtmlEncode(row.ShortWhy)}.</p>", drawn, StringComparison.Ordinal);
        Assert.Empty(NameScreen.PlanRows(row));

        // Bands with no day's move to measure them against cannot place a plan either.
        using var moveless = ShortStore(9, typical: false);

        Assert.Contains("no plan can be drawn", (await ShortRead(moveless).PlanLadderAsync("NEWCO", ShortNight))!.ShortWhy, StringComparison.Ordinal);

        // Levels with no support beneath the price say that, and what its days show, rather than that none could be read.
        using var above = ShortStore(50, below: false);

        var high = await ShortRead(above).PlanLadderAsync("NEWCO", ShortNight);

        Assert.Equal("This stock started trading on 14 Sep 2026, so it has 50 of the 200 trading days a 200-day average needs, and on the levels its 50 days show, no support band sits below the price", high!.ShortWhy);
    }
}
