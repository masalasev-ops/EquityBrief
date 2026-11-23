using System.Globalization;
using EquityBrief.Core.Time;
using EquityBrief.Tests.Harness;
using EquityBrief.Worker.History;
using Microsoft.Data.Sqlite;
using Xunit;

namespace EquityBrief.Tests.Checks;

// fixture-expectations, the 24.19 correction on the operator's answers of 2026-11-23 at 14:00: a code asked again has its
// prices, splits and dividends asked whether or not the folder keeps them; after the write, its member sessions still without
// a price are marked held none as runs once, never removed, and a code holding one is refused when asked again; and the lists
// name those sessions apart, each worked over a constructed store.
// see: A code's member sessions still without a price after the provider was asked again are marked held none and never asked again
public partial class FixtureExpectations
{
    // The asks read as ASKED or ASKED:FOR; a capture over a folder keeping one code's three answers asks it nothing, and asks
    // its three again when it is named, and nothing else.
    [Fact]
    public async Task ACodeAskedAgainIsAskedWhetherOrNotTheFolderKeepsItsAnswers()
    {
        Assert.Equal([new LongAsk("WOLF_old2", "CREE"), new LongAsk("ADCT_old", "ADCT_old")], LongPricePull.AsksOf("WOLF_old2:CREE, ADCT_old,WOLF_old2"));

        using var store = new TemporaryStore();
        var folder = Path.Combine(store.Root, "capture");
        var feed = new SilentLongFeed();
        LongCode[] codes = [new("AAA", ["Alpha Co"], "1")];
        var through = new DateOnly(2026, 11, 6);

        await LongPricePull.CaptureAsync(folder, codes, feed, through, true, _ => { });

        var first = feed.Requests;

        Assert.Equal(0, await LongPricePull.CaptureAsync(folder, codes, feed, through, true, _ => { }));
        Assert.Equal(3, await LongPricePull.CaptureAsync(folder, codes, feed, through, true, _ => { }, again: new HashSet<string>(StringComparer.Ordinal) { "AAA" }));
        Assert.Equal(first + 3, feed.Requests);
    }

    // HOL and ALL are members of the S&P 400 over ten sessions, ALL priced on every one and HOL on seven of them, the fourth to the
    // sixth missing. Asked
    // again and still without them, the three are marked as one run once; marking again writes nothing; the table refuses an
    // update and a delete; an ask for HOL is refused before anything is asked, one for another code is kept with its instant
    // and adds a code the pull does not hold; and the lists name the three as held none and none still without a price.
    [Fact]
    public async Task ACodesSessionsStillWithoutAPriceAreMarkedHeldNoneOnceAndNeverAskedAgain()
    {
        using var store = new TemporaryStore().Migrated();
        string[] days = [.. Enumerable.Range(0, 14).Select(at => new DateOnly(2010, 1, 4).AddDays(at)).Where(day => day.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday)).Select(day => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))];

        store.Execute("INSERT INTO long_market_bar (series, session_date, open, high, low, close, run_id) VALUES " + string.Join(", ", days.Select(day => $"('GSPC', '{day}', '1000', '1000', '1000', '1000', 'r')")) + ";");
        store.Execute($"INSERT INTO long_span (index_code, ticker, joined, \"left\", read_from, run_id) VALUES ('MID', 'HOL', '{days[0]}', NULL, 'fund', 'r'), ('MID', 'ALL', '{days[0]}', NULL, 'fund', 'r');");
        store.Execute("INSERT INTO long_bar (ticker, session_date, open, high, low, close, raw_close, volume, run_id) VALUES "
            + string.Join(", ", days.Select((day, at) => (day, at)).Where(pair => pair.at is < 3 or > 5).Select(pair => LongBarRow("HOL", pair.day, 10m + pair.at))
                .Concat(days.Select((day, at) => LongBarRow("ALL", day, 20m + at)))) + ";");

        var folder = Path.Combine(store.Root, "capture");
        var clock = FixedClock.At(new DateTimeOffset(2026, 11, 24, 4, 30, 0, TimeSpan.Zero), SessionZones.UnitedStates);

        Directory.CreateDirectory(folder);
        await File.WriteAllTextAsync(Path.Combine(folder, LongPricePull.AgainFile), System.Text.Json.JsonSerializer.Serialize(new LongAgain(clock.UtcNow, [new LongAsk("HOL", "HOL")])));

        var marked = await LongPricePull.HeldNoneAsync(store.DatabaseFile, folder, "long-held-none-test", clock);

        Assert.Equal([("HOL", 1, 3)], marked);
        Assert.Equal($"{days[3]} {days[5]} 3", (string)One(store, "SELECT first_session || ' ' || last_session || ' ' || sessions FROM long_held_none;")!);

        await LongPricePull.HeldNoneAsync(store.DatabaseFile, folder, "long-held-none-again", clock);

        Assert.Equal(1L, (long)One(store, "SELECT COUNT(*) FROM long_held_none;")!);
        Assert.Equal(2L, (long)One(store, $"SELECT COUNT(*) FROM run_log WHERE stage = '{LongPricePull.HeldNoneStage}';")!);
        Assert.Throws<SqliteException>(() => store.Execute("UPDATE long_held_none SET sessions = 4;"));
        Assert.Throws<SqliteException>(() => store.Execute("DELETE FROM long_held_none;"));

        await Assert.ThrowsAsync<ArgumentException>(() => LongPricePull.AgainAsync(store.DatabaseFile, folder, [new LongAsk("HOL_new", "HOL")], clock.UtcNow));

        var codes = await LongPricePull.AgainAsync(store.DatabaseFile, folder, [new LongAsk("NEW", "NEW")], clock.UtcNow);

        Assert.Contains(codes, code => code.Ticker == "NEW");
        Assert.Contains("\"NEW\"", await File.ReadAllTextAsync(Path.Combine(folder, LongPricePull.AgainFile)));

        var lists = new StringWriter();

        await new LongHistoryReader(store.DatabaseFile).ListsAsync("MID", DateOnly.ParseExact(days[^1], "yyyy-MM-dd", CultureInfo.InvariantCulture), lists);

        Assert.Contains($"held none HOL 3 member session(s) from {days[3]} to {days[5]} the provider holds no price for", lists.ToString());
        Assert.Contains("3 the provider holds no price for, 0 still with no price", lists.ToString());
    }

    // On the operator's answers of 2026-11-23 at 16:50: ALL is priced by the long prices on all ten sessions, as an answer asked in
    // the day carries a session the night has not stored, and HOL on the first nine, the store's own bars holding HOL's ninth and
    // nothing later. Asked again, HOL's tenth session is past the store's newest stored session and is marked nothing.
    [Fact]
    public async Task TheMarkingStopsAtTheStoresNewestStoredSession()
    {
        using var store = new TemporaryStore().Migrated();
        string[] days = [.. Enumerable.Range(0, 14).Select(at => new DateOnly(2010, 1, 4).AddDays(at)).Where(day => day.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday)).Select(day => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))];

        store.Execute("INSERT INTO long_market_bar (series, session_date, open, high, low, close, run_id) VALUES " + string.Join(", ", days.Select(day => $"('GSPC', '{day}', '1000', '1000', '1000', '1000', 'r')")) + ";");
        store.Execute($"INSERT INTO long_span (index_code, ticker, joined, \"left\", read_from, run_id) VALUES ('MID', 'HOL', '{days[0]}', NULL, 'fund', 'r'), ('MID', 'ALL', '{days[0]}', NULL, 'fund', 'r');");
        store.Execute("INSERT INTO long_bar (ticker, session_date, open, high, low, close, raw_close, volume, run_id) VALUES "
            + string.Join(", ", days[..^1].Select((day, at) => LongBarRow("HOL", day, 10m + at)).Concat(days.Select((day, at) => LongBarRow("ALL", day, 20m + at)))) + ";");
        store.Execute($"INSERT INTO bar (ticker, session_date, open, high, low, close, volume, source, observed_at, raw_close) VALUES ('HOL', '{days[^2]}', '18', '18', '18', '18', 1000, 'bulk', '2010-01-14T21:10:00Z', '18');");

        var folder = Path.Combine(store.Root, "capture");
        var clock = FixedClock.At(new DateTimeOffset(2026, 11, 24, 4, 30, 0, TimeSpan.Zero), SessionZones.UnitedStates);

        Directory.CreateDirectory(folder);
        await File.WriteAllTextAsync(Path.Combine(folder, LongPricePull.AgainFile), System.Text.Json.JsonSerializer.Serialize(new LongAgain(clock.UtcNow, [new LongAsk("HOL", "HOL")])));

        Assert.Equal(DateOnly.ParseExact(days[^2], "yyyy-MM-dd", CultureInfo.InvariantCulture), await new LongHistoryReader(store.DatabaseFile).NewestStoredSessionAsync());
        Assert.Empty(await LongPricePull.HeldNoneAsync(store.DatabaseFile, folder, "long-held-none-test", clock));
        Assert.Equal(0L, (long)One(store, "SELECT COUNT(*) FROM long_held_none;")!);
    }

    // Migration 169 removes the five rows marked for 2026-11-23 and nothing else, and the table refuses a delete after it as
    // before.
    [Fact]
    public void MigrationOneSixtyNineRemovesTheFiveRowsMarkedPastTheStoreAlone()
    {
        using var store = new TemporaryStore();

        new EquityBrief.Data.Migrations.MigrationRunner([.. EquityBrief.Data.Migrations.SchemaMigrations.All.Where(migration => migration.Version <= 168)]).Apply(store.DatabaseFile);
        store.Execute("INSERT INTO long_held_none (ticker, first_session, last_session, sessions, asked_at, run_id) VALUES "
            + string.Join(", ", new[] { "HOS", "LUMN", "PRG", "WEN", "WWW" }.Select(code => $"('{code}', '2026-11-23', '2026-11-23', 1, '2026-11-23T21:18:24Z', 'long-held-none-20261123T213949Z')"))
            + ", ('HOS', '2020-03-02', '2020-03-06', 5, '2026-11-23T21:18:24Z', 'long-held-none-20261123T213949Z'), ('SIX', '2026-11-23', '2026-11-23', 1, '2026-11-23T21:18:24Z', 'long-held-none-other');");

        new EquityBrief.Data.Migrations.MigrationRunner([.. EquityBrief.Data.Migrations.SchemaMigrations.All]).Apply(store.DatabaseFile);

        Assert.Equal("HOS 2020-03-02,SIX 2026-11-23", (string)One(store, "SELECT group_concat(ticker || ' ' || first_session, ',') FROM (SELECT ticker, first_session FROM long_held_none ORDER BY ticker);")!);
        Assert.Throws<SqliteException>(() => store.Execute("DELETE FROM long_held_none;"));
    }
}
