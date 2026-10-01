using System.Globalization;
using EquityBrief.Api.Reading;
using EquityBrief.Core.Families;
using EquityBrief.Core.Returns;
using EquityBrief.Core.Time;
using EquityBrief.Worker.Families;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Tests.Checks;

// The setup families' shared rules: the page's list drawn from what each family passed, in the page's
// order, five a family, a stock listed once and none while a trade for it is still open; the lister that
// stores it for a night and reads every earlier trade by the open trade rule; and a night read as listed
// by the page where the families drew it and by the rule that drew it before. Each worked by hand over
// constructed families and constructed nights.
// see: Tonight's page is drawn from setup families, each a rule of its own listing at most five a night
// see: A stock holds one trade across every family, and one qualifying under two is listed once under the first in the page's order
public partial class FixtureExpectations
{
    static IReadOnlyList<string> FamilyRows(TemporaryStore store, string sql)
    {
        using var connection = new SqliteConnection($"Data Source={store.DatabaseFile}");
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = sql;

        var rows = new List<string>();
        using var reader = command.ExecuteReader();

        while (reader.Read())
        {
            rows.Add(string.Join("|", Enumerable.Range(0, reader.FieldCount).Select(at => reader.IsDBNull(at) ? "null" : Convert.ToString(reader.GetValue(at), CultureInfo.InvariantCulture))));
        }

        return rows;
    }

    [Fact]
    public void TheListIsDrawnInThePagesOrderFiveAFamilyAStockListedOnceAndNoneWithATradeStillOpen()
    {
        // Three families in the page's order, each passing its names in its own order, and one stock, h,
        // holding a trade the first family listed on 2026-09-28.
        //
        // first passes a1 to a6, then x, then h: a1 to a5 take its five places, 1 to 5; a6 and x are past
        // five; h is held by its open trade.
        // second passes x, a6, a1, b1, h, b2 to b6: x is on no card yet and second has room, so x is listed
        // at place 6 carrying first's label; a6 the same at 7; a1 is listed under first already, so it is
        // under another and takes none of second's five; b1 at 8; h held; b2 at 9 and b3 at 10, which is
        // second's five, x, a6, b1, b2 and b3; b4, b5 and b6 are past five.
        // third passes b4 and a2: b4 is on no card and third has room, listed at 11 carrying second's label;
        // a2 is under another.
        var open = new Dictionary<string, HeldTrade>(StringComparer.Ordinal) { ["h"] = new("first", new DateOnly(2026, 9, 28)) };

        var picks = FamilyList.Draw(
            [
                new FamilyQualifiers("first", ["a1", "a2", "a3", "a4", "a5", "a6", "x", "h"]),
                new FamilyQualifiers("second", ["x", "a6", "a1", "b1", "h", "b2", "b3", "b4", "b5", "b6"]),
                new FamilyQualifiers("third", ["b4", "a2"]),
            ],
            open);

        string Said(FamilyPick pick) =>
            $"{pick.Family} {pick.Ticker} {pick.State} {(pick.Place is { } place ? place.ToString(CultureInfo.InvariantCulture) : "-")} [{string.Join(",", pick.Also)}]"
            + (pick.HeldBy is { } held ? $" held by {held.Family} {held.Listed.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}" : string.Empty);

        Assert.Equal(
            [
                "first a1 listed 1 [second]",
                "first a2 listed 2 [third]",
                "first a3 listed 3 []",
                "first a4 listed 4 []",
                "first a5 listed 5 []",
                "first a6 past five - []",
                "first x past five - []",
                "first h open trade - [] held by first 2026-09-28",
                "second x listed 6 [first]",
                "second a6 listed 7 [first]",
                "second a1 under another - []",
                "second b1 listed 8 []",
                "second h open trade - [] held by first 2026-09-28",
                "second b2 listed 9 []",
                "second b3 listed 10 []",
                "second b4 past five - []",
                "second b5 past five - []",
                "second b6 past five - []",
                "third b4 listed 11 [second]",
                "third a2 under another - []",
            ],
            picks.Select(Said));

        // A stock is listed once: no ticker holds two listed rows, and the places run 1 to 11 with no gap.
        var listed = picks.Where(pick => pick.State == FamilyList.Listed).ToArray();

        Assert.Equal(listed.Length, listed.Select(pick => pick.Ticker).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(Enumerable.Range(1, 11), listed.Select(pick => pick.Place!.Value));

        // The five is the families' own number, and at exactly five passing a family lists all five.
        Assert.Equal(5, SetupFamilies.ListedANight);
        Assert.All(
            FamilyList.Draw([new FamilyQualifiers("first", ["a1", "a2", "a3", "a4", "a5"])], new Dictionary<string, HeldTrade>()),
            pick => Assert.Equal(FamilyList.Listed, pick.State));

        // With no trade open and one family, the list is that family's first five in its own order.
        Assert.Equal(
            ["a1", "a2", "a3", "a4", "a5"],
            FamilyList.Draw([new FamilyQualifiers("first", ["a1", "a2", "a3", "a4", "a5", "a6"])], new Dictionary<string, HeldTrade>())
                .Where(pick => pick.State == FamilyList.Listed)
                .Select(pick => pick.Ticker));
    }

    // One member's swing filter row on a night, passing or not, at its rank, on the plan clear of the noise.
    static void FamilyGate(TemporaryStore store, string session, string ticker, bool passed, int? rank)
    {
        store.Execute(
            "INSERT INTO gate_result (ticker, session_date, version, code, market, trend, setup, family, trigger_pass, trigger_event, trade, " +
            "swing_entry, swing_stop, swing_target, clear_stop, clear_target, exclusions, passed, rank, gates) " +
            $"VALUES ('{ticker}', '{session}', '1', 'code', 1, 1, 1, 'pullback', {(passed ? 1 : 0)}, 1, 1, " +
            $"'100', '96', '110', '96', '110', '[]', {(passed ? 1 : 0)}, {(rank is { } at ? at.ToString(CultureInfo.InvariantCulture) : "NULL")}, '{{\"gates\":[],\"notes\":[]}}');");
        store.Execute(
            "INSERT OR IGNORE INTO listing (ticker, session_date, reasons, fired_count, plan_at_listing, shadow_reasons, band_strength) " +
            $"VALUES ('{ticker}', '{session}', '[]', 0, '{{}}', '{{\"candidates\":[],\"skipped\":[]}}', 0);");
    }

    static void FamilyBar(TemporaryStore store, string session) =>
        store.Execute(
            "INSERT INTO bar (ticker, session_date, open, high, low, close, volume, source, observed_at, raw_close) " +
            $"VALUES ('PA', '{session}', '100', '100', '100', '100', 1000, 'test', '{session}T21:00:00Z', '100');");

    static void FamilyOutcome(TemporaryStore store, string session, string ticker, string? outcome, string? resolvedOn) =>
        store.Execute(
            "INSERT INTO forward_return (ticker, session_date, horizon, outcome, resolved_on, return_pct, base_rate, break_even) " +
            $"VALUES ('{ticker}', '{session}', '{ForwardReturnSeries.Clear}', {(outcome is null ? "NULL" : $"'{outcome}'")}, {(resolvedOn is null ? "NULL" : $"'{resolvedOn}'")}, NULL, NULL, NULL);");

    static void FamilyState(TemporaryStore store, string session, string ticker, string state) =>
        store.Execute(
            "INSERT INTO fundamental_reading (ticker, session_date, state, read_from, fetched_at, awaited, readings) " +
            $"VALUES ('{ticker}', '{session}', '{state}', NULL, NULL, NULL, '{{}}');");

    // The constructed store the lister is read over. Version 1 reads the plan clear of the noise.
    //
    // 2026-09-28, a night the swing filter listed before the families: PA, PB and PD passed. PA's trade is
    // still open, its outcome row stored and undecided; PB's was stopped out on 2026-09-29; PD's outcome row
    // is missing.
    //
    // 2026-09-30, the night drawn: nine pass, PA, PB, PD and PE at ranks 1 to 4 and PF, PG, PH, PI and PJ
    // at 5 to 9, with PE's and PJ's businesses read as improving and no other member's read.
    static TemporaryStore FamilyStore()
    {
        var store = new TemporaryStore().Migrated();

        store.Execute("INSERT INTO filter_version (version, settings, opened_at, closed_at, evidence) VALUES ('1', '{\"trade\":\"clear\"}', '2026-09-20T00:00:00Z', NULL, 'test');");
        store.Execute("INSERT INTO list_rule (session_date, rule) VALUES ('2026-09-28', 'filter'), ('2026-09-30', 'filter');");

        FamilyGate(store, "2026-09-28", "PA", passed: true, 1);
        FamilyGate(store, "2026-09-28", "PB", passed: true, 2);
        FamilyGate(store, "2026-09-28", "PD", passed: true, 3);
        FamilyGate(store, "2026-09-28", "PE", passed: false, null);
        FamilyOutcome(store, "2026-09-28", "PA", null, null);
        FamilyOutcome(store, "2026-09-28", "PB", ForwardReturnSeries.Loss, "2026-09-29");

        foreach (var (ticker, rank) in new[] { ("PA", 1), ("PB", 2), ("PD", 3), ("PE", 4), ("PF", 5), ("PG", 6), ("PH", 7), ("PI", 8), ("PJ", 9) })
        {
            FamilyGate(store, "2026-09-30", ticker, passed: true, rank);
        }

        FamilyGate(store, "2026-09-30", "PK", passed: false, null);
        FamilyState(store, "2026-09-30", "PE", "improving");
        FamilyState(store, "2026-09-30", "PJ", "improving");
        FamilyBar(store, "2026-09-28");
        FamilyBar(store, "2026-09-30");

        return store;
    }

    [Fact]
    public async Task TheListerStoresTheNightsListHoldsBackAStockWhoseTradeIsStillOpenAndReplacesItsOwnNight()
    {
        using var store = FamilyStore();

        var clock = FixedClock.At(new DateTimeOffset(2026, 9, 30, 23, 40, 0, TimeSpan.Zero), SessionZones.UnitedStates);
        var outcome = await new FamilyLister(clock, store.DatabaseFile).RunAsync("families-first");

        // The pullback's own order: improving businesses first, PE at rank 4 and PJ at 9, then the rest by
        // rank, PA, PB, PD, PF, PG, PH, PI. PA's trade from 2026-09-28 is undecided, so it is open; PD's
        // outcome row is missing two sessions after its listing, inside its 63, so it is read as open; PB's
        // ended on 2026-09-29, the session before, so PB is free. The five listed are PE, PJ, PB, PF and PG,
        // and PH and PI are past five.
        Assert.Equal(
            [
                "PE|listed|1|[]|null|null",
                "PJ|listed|2|[]|null|null",
                "PB|listed|3|[]|null|null",
                "PF|listed|4|[]|null|null",
                "PG|listed|5|[]|null|null",
                "PA|open trade|null|[]|pullback|2026-09-28",
                "PD|open trade|null|[]|pullback|2026-09-28",
                "PH|past five|null|[]|null|null",
                "PI|past five|null|[]|null|null",
            ],
            FamilyRows(store, "SELECT ticker, state, place, also, held_family, held_night FROM family_pick WHERE session_date = '2026-09-30' AND family = 'pullback' ORDER BY state, place, ticker;"));
        // The page's families in its order. The ones after the pullback stored no answer for this night, so
        // each passes none.
        Assert.Equal(["pullback", "breakout", "drift"], SetupFamilies.InPageOrder.Select(family => family.Name));
        Assert.Equal(["2026-09-30|[\"pullback\",\"breakout\",\"drift\"]"], FamilyRows(store, "SELECT session_date, families FROM family_night;"));
        Assert.Equal((new DateOnly(2026, 9, 30), 5, 2, 0, 2), (outcome.Night!.Value, outcome.Listed, outcome.OpenTrade, outcome.UnderAnother, outcome.PastFive));
        Assert.Equal([("pullback", 9, 5), ("breakout", 0, 0), ("drift", 0, 0)], outcome.Families);
        Assert.Equal(
            ["families|ok|9|0|0|5 listed for 2026-09-30: 5 of the 9 the pullback family passed, 0 of the 0 the breakout family passed, 0 of the 0 the drift family passed; 2 held back by a trade still open, 0 listed under another family, 2 past a family's 5"],
            FamilyRows(store, "SELECT stage, outcome, rows_written, model_calls, network_requests, detail FROM run_log WHERE run_id = 'families-first';"));

        // Run again, the night replaces its own rows and no other night's: nine rows still, and one session.
        await new FamilyLister(clock, store.DatabaseFile).RunAsync("families-again");

        Assert.Equal(["9|1"], FamilyRows(store, "SELECT COUNT(*), COUNT(DISTINCT session_date) FROM family_pick;"));
        Assert.Equal(["1"], FamilyRows(store, "SELECT COUNT(*) FROM family_night;"));

        // The next night, 2026-10-01: PE, PB and PH pass. PE's trade from 2026-09-30 is the page's own, with
        // no outcome row yet, so it is open; PB's was stopped out on 2026-10-01 itself, and a stop's night
        // still holds the stock; PH was past five on 2026-09-30, which is no trade, so it is free and listed.
        store.Execute("INSERT INTO list_rule (session_date, rule) VALUES ('2026-10-01', 'filter');");
        FamilyGate(store, "2026-10-01", "PE", passed: true, 1);
        FamilyGate(store, "2026-10-01", "PB", passed: true, 2);
        FamilyGate(store, "2026-10-01", "PH", passed: true, 3);
        FamilyOutcome(store, "2026-09-30", "PB", ForwardReturnSeries.Loss, "2026-10-01");
        FamilyBar(store, "2026-10-01");

        await new FamilyLister(clock, store.DatabaseFile).RunAsync("families-next");

        Assert.Equal(
            [
                "PH|listed|1|null|null",
                "PB|open trade|null|pullback|2026-09-30",
                "PE|open trade|null|pullback|2026-09-30",
            ],
            FamilyRows(store, "SELECT ticker, state, place, held_family, held_night FROM family_pick WHERE session_date = '2026-10-01' ORDER BY state, ticker;"));
        Assert.Equal(["9"], FamilyRows(store, "SELECT COUNT(*) FROM family_pick WHERE session_date = '2026-09-30';"));

        // A night the swing filter stored no result for draws no list and records no session, and says so.
        FamilyBar(store, "2026-10-02");

        var none = await new FamilyLister(clock, store.DatabaseFile).RunAsync("families-none");

        Assert.Null(none.Night);
        Assert.Empty(FamilyRows(store, "SELECT session_date FROM family_night WHERE session_date = '2026-10-02';"));
        Assert.Equal(
            ["the swing filter stored no result for 2026-10-02, so no list was drawn"],
            FamilyRows(store, "SELECT detail FROM run_log WHERE run_id = 'families-none';"));
    }

    [Fact]
    public async Task ANightIsListedByThePageWhereTheFamiliesDrewItAndByTheRuleThatDrewItBefore()
    {
        using var store = FamilyStore();

        // An evening the reasons listed, before any rule: PA fired a reason and PB did not.
        store.Execute(
            "INSERT INTO listing (ticker, session_date, reasons, fired_count, plan_at_listing, shadow_reasons, band_strength) VALUES " +
            "('PA', '2026-09-22', '[]', 2, '{}', '{\"candidates\":[],\"skipped\":[]}', 0), " +
            "('PB', '2026-09-22', '[]', 0, '{}', '{\"candidates\":[],\"skipped\":[]}', 0);");

        var clock = FixedClock.At(new DateTimeOffset(2026, 9, 30, 23, 40, 0, TimeSpan.Zero), SessionZones.UnitedStates);

        await new FamilyLister(clock, store.DatabaseFile).RunAsync("families-read");

        var read = new ReadApi(store.DatabaseFile, clock);

        IReadOnlyList<string> Listed(IReadOnlyList<ListingRow> rows) => [.. rows.Where(row => row.IsListed).Select(row => row.Ticker).Order(StringComparer.Ordinal)];

        // The night the families drew: the five the page lists and no other, whatever the filter passed.
        Assert.Equal(["PB", "PE", "PF", "PG", "PJ"], Listed(await read.ListingsAsync(new DateOnly(2026, 9, 30))));

        // The night the swing filter listed before them: the three it passed.
        Assert.Equal(["PA", "PB", "PD"], Listed(await read.ListingsAsync(new DateOnly(2026, 9, 28))));

        // The evening the reasons listed: the one that fired.
        Assert.Equal(["PA"], Listed(await read.ListingsAsync(new DateOnly(2026, 9, 22))));

        // Past picks' rows follow the same rule: the pullback's trades on the night the families drew are
        // the five listed, in the page's order, each under its family, and the earlier night's are the
        // three the filter passed, in its order.
        var picks = await read.PicksAsync(new DateOnly(2026, 9, 30));

        Assert.Equal(
            [
                "2026-09-30 PE pullback", "2026-09-30 PJ pullback", "2026-09-30 PB pullback", "2026-09-30 PF pullback", "2026-09-30 PG pullback",
                "2026-09-28 PA pullback", "2026-09-28 PB pullback", "2026-09-28 PD pullback",
            ],
            picks.Select(pick => $"{pick.Night.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)} {pick.Ticker} {pick.Family}"));

        // The page's list as the store holds it, the listed first in the page's order.
        var stored = await read.FamilyPicksAsync(new DateOnly(2026, 9, 30));

        Assert.Equal(["pullback", "breakout", "drift"], await read.FamilyNightAsync(new DateOnly(2026, 9, 30)));
        Assert.Null(await read.FamilyNightAsync(new DateOnly(2026, 9, 28)));
        Assert.Equal(["PE", "PJ", "PB", "PF", "PG"], stored.Where(pick => pick.State == FamilyList.Listed).Select(pick => pick.Ticker));
        Assert.Equal(9, stored.Count);
        Assert.Equal(
            (FamilyList.OpenTrade, "pullback", new DateOnly(2026, 9, 28)),
            stored.Where(pick => pick.Ticker == "PA").Select(pick => (pick.State, pick.HeldFamily!, pick.HeldNight!.Value)).Single());
    }
}
