using EquityBrief.Core.History;
using EquityBrief.Core.Sweep;
using EquityBrief.Data.Migrations;
using EquityBrief.Tests.Harness;
using Microsoft.Data.Sqlite;
using Xunit;

namespace EquityBrief.Tests.Checks;

// fixture-expectations, the 24.19 correction on the operator's answers of 2026-11-23 at about 10:40 pm: LPI, PVAC and WTW_old
// read their later codes' prices by hand where the later close continues the earlier, a pair never priced on one day read as
// it stands; PARA's provider span from 2025-08-07 is dropped by hand while PSKY's stands; the held-none rows of the four codes
// removed and nothing else; and the operator's eight rulings written once, a mark ruled already keeping its ruling.
// see: Three more codes read their later codes' prices by hand where the later close continues the earlier, each listed as made by hand
// see: PARA's provider span from 2025-08-07 is dropped by hand, its code then Banzai International's and PSKY holding Paramount
public partial class FixtureExpectations
{
    static SweepBar HandBar(DateOnly day, decimal close) => new(day, close, close, close, 1000, close, close);

    // WTW_old priced at 19 to 19.5 to 2019-04-18 and WW_old from 2019-04-22 alone, the two never priced on one day. From the
    // link's session the code reads WW_old's closes as they stand, its first of 19.10 continuing WTW_old's last of 19.00; where
    // WW_old opens a fifth lower the link is refused. LPI and VTLE priced alike to 2023-03-10 read VTLE from 2023-03-13 at
    // their ratio there, one.
    [Fact]
    public void ThreeMoreCodesReadTheirLaterCodesByHandAndAPairNeverPricedOnOneDayReadsAsItStands()
    {
        Assert.Equal(
            [("DDR", "SITC", new DateOnly(2018, 10, 12)), ("LPI", "VTLE", new DateOnly(2023, 3, 13)), ("PVAC", "ROCC", new DateOnly(2021, 12, 20)), ("WTW_old", "WW_old", new DateOnly(2019, 4, 22))],
            LongRenames.ByHand);

        var from = new DateOnly(2019, 4, 22);
        DateOnly[] calendar = [.. Enumerable.Range(0, 200).Select(day => new DateOnly(2019, 1, 2).AddDays(day)).Where(day => day.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday) && day != new DateOnly(2019, 4, 19))];
        decimal Own(int at) => 19m + (at % 2 == 0 ? 0m : 0.5m);

        SweepBar[] wtw = [.. calendar.Where(day => day < from).Select((day, at) => HandBar(day, day == new DateOnly(2019, 4, 18) ? 19m : Own(at)))];
        SweepBar[] ww = [.. calendar.Where(day => day >= from).Select(day => HandBar(day, 19.1m))];
        var linked = LongRenames.ByHandJoined(wtw, ww, from, calendar);

        Assert.NotNull(linked);
        Assert.Equal("linked by hand from 2019-04-22, its first close there continuing the last before it, the two never priced on one day and read as they stand", linked.Value.Test);
        Assert.Equal(19.1m, linked.Value.Bars.Single(bar => bar.Session == from).Close);
        Assert.Equal(calendar.Count(day => day >= from), linked.Value.Joined.Count);
        Assert.Null(LongRenames.ByHandJoined(wtw, [.. ww.Select(bar => HandBar(bar.Session, 19.1m * 0.8m))], from, calendar));

        var vital = new DateOnly(2023, 3, 13);
        DateOnly[] days = [.. Enumerable.Range(0, 120).Select(day => new DateOnly(2023, 1, 2).AddDays(day)).Where(day => day.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday))];
        SweepBar[] lpi = [.. days.Where(day => day < vital).Select((day, at) => HandBar(day, 50m + (at % 2 == 0 ? 0m : 0.5m)))];
        SweepBar[] vtle = [.. days.Select((day, at) => HandBar(day, day < vital ? 50m + (at % 2 == 0 ? 0m : 0.5m) : 50.2m))];
        var laredo = LongRenames.ByHandJoined(lpi, vtle, vital, days);

        Assert.Equal("linked by hand from 2023-03-13, its first close there continuing the last before it", laredo!.Value.Test);
        Assert.Equal(50.2m, laredo.Value.Bars.Single(bar => bar.Session == vital).Close);
    }

    // Over a constructed store, as the operator's store holds it: LPI a member of the S&P 600 from 2023-01-03 with no end, priced
    // to 2023-03-10 and carrying a history-ends mark there, and VTLE priced alike to it and on from 2023-03-13 at 50.2. The reader
    // reads LPI's member sessions from 2023-03-13 at VTLE's prices, the link listed as made by hand, though the mark parts LPI's
    // history at its last price.
    [Fact]
    public async Task TheLongReaderReadsAHandLinkPastTheCodesOwnHistoryEndsMark()
    {
        using var store = new TemporaryStore().Migrated();
        var vital = new DateOnly(2023, 3, 13);
        string[] days = [.. Enumerable.Range(0, 120).Select(day => new DateOnly(2023, 1, 3).AddDays(day)).Where(day => day.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday)).Select(day => day.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture))];
        static DateOnly Day(string text) => DateOnly.ParseExact(text, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        decimal Close(int at) => 50m + (at % 2 == 0 ? 0m : 0.5m);

        store.Execute("INSERT INTO long_market_bar (series, session_date, open, high, low, close, run_id) VALUES " + string.Join(", ", days.Select(day => $"('GSPC', '{day}', '1000', '1000', '1000', '1000', 'r')")) + ";");
        store.Execute($"INSERT INTO long_span (index_code, ticker, joined, \"left\", read_from, run_id) VALUES ('SML', 'LPI', '{days[0]}', NULL, 'fund', 'r');");
        store.Execute("INSERT INTO long_bar (ticker, session_date, open, high, low, close, raw_close, volume, run_id) VALUES "
            + string.Join(", ", days.Select((day, at) => (day, at)).Where(one => Day(one.day) < vital).Select(one => LongBarRow("LPI", one.day, Close(one.at)))
                .Concat(days.Select((day, at) => LongBarRow("VTLE", day, Day(day) < vital ? Close(at) : 50.2m)))) + ";");

        var last = days.Last(day => Day(day) < vital);

        store.Execute($"INSERT INTO session_mark (ticker, session_date, kind, says, ruled, run_id) VALUES ('LPI', '{last}', '{EquityBrief.Core.Chart.SessionMarking.HistoryEnds}', 'e', '2026-11-08T00:00:00Z', 'r');");

        var reader = new EquityBrief.Worker.History.LongHistoryReader(store.DatabaseFile);
        var through = Day(days[^1]);
        var lpi = (await reader.ReadAsync("SML", through)).Names.Where(name => name.Ticker == "LPI").ToArray();

        Assert.Contains(lpi, name => name.Bars.Any(bar => bar.Session == vital && bar.Close == 50.2m) && name.MemberOn(vital));
        Assert.Contains(await reader.JoinsAsync("SML", through), join => join.Ticker == "LPI" && join.Other == "VTLE" && join.Test is { } test && test.StartsWith("linked by hand from 2023-03-13", StringComparison.Ordinal) && join.Sessions == days.Count(day => Day(day) >= vital));
    }

    // PARA's rows: Paramount Global's to 2025-08-07 and Banzai International's with no start to 2026-10-14, which continues the
    // first from 2025-08-07; PSKY's from 2025-08-07. Dropped by hand, PARA holds no span from 2025-08-07 and PSKY's stands; a
    // code of the same shape not on the hand list keeps its later span.
    [Fact]
    public void ParasProviderSpanFrom2025_08_07IsDroppedByHandWhilePskysStands()
    {
        var paramount = new DateOnly(2025, 8, 7);

        Assert.Equal([("PARA", paramount)], LongSpans.DroppedByHand);

        ProviderSpan[] rows =
        [
            new("PARA", new DateOnly(2022, 2, 17), paramount, "Paramount Global Class B"),
            new("PARA", null, new DateOnly(2026, 10, 14), "Banzai International, Inc."),
            new("PSKY", paramount, new DateOnly(2026, 10, 6), "Paramount Skydance Corp-B"),
            new("SAME", new DateOnly(2022, 2, 17), paramount, "Same Old Co"),
            new("SAME", null, new DateOnly(2026, 10, 14), "Same New Co"),
        ];
        var spans = LongSpans.Of(new Dictionary<string, IReadOnlyDictionary<DateOnly, IReadOnlySet<string>>>(), rows, providerFrom: new DateOnly(2012, 1, 1), partsThem: (_, _) => LongSpans.DifferentFiler);

        Assert.DoesNotContain(spans, span => span.Ticker == "PARA" && span.Joined >= paramount);
        Assert.Contains(spans, span => span.Ticker == "PARA" && span.Joined == new DateOnly(2022, 2, 17) && span.Left == paramount);
        Assert.Contains(spans, span => span.Ticker == "PSKY" && span.Joined == paramount);
        Assert.Contains(spans, span => span.Ticker == "SAME" && span.Joined == paramount && span.Left == new DateOnly(2026, 10, 14));
    }

    // Migration 171 removes the four codes' held-none rows of the marking of 2026-11-23 and nothing else; migration 172 writes the
    // operator's rulings of the stocks the store holds long prices of, a mark the page ruled before keeping its ruling, and the
    // tables refuse a delete after them.
    [Fact]
    public void MigrationsOneSeventyOneAndTwoRemoveTheFourHeldNoneRowsAndWriteTheEightRulings()
    {
        using var store = new TemporaryStore();

        new MigrationRunner([.. SchemaMigrations.All.Where(migration => migration.Version <= 170)]).Apply(store.DatabaseFile);
        store.Execute("INSERT INTO long_held_none (ticker, first_session, last_session, sessions, asked_at, run_id) VALUES "
            + "('LPI', '2023-03-13', '2025-12-30', 704, 'a', 'long-held-none-20261123T213949Z'), ('PVAC', '2021-12-20', '2023-06-29', 383, 'a', 'long-held-none-20261123T213949Z'), "
            + "('WTW_old', '2019-04-22', '2019-09-27', 112, 'a', 'long-held-none-20261123T213949Z'), ('SIX', '2019-09-30', '2024-09-27', 1258, 'a', 'long-held-none-20261123T213949Z'), "
            + "('HOS', '2020-03-02', '2020-03-06', 5, 'a', 'long-held-none-20261123T213949Z'), ('LPI', '2026-01-02', '2026-01-02', 1, 'a', 'long-held-none-other');");
        store.Execute("INSERT INTO mark_ruling (ticker, session_date, ruling, filer, price, ruled_at) VALUES ('PKY', '2016-10-11', 'read through', 'not on file', 'carries on', '2026-11-24T01:00:00Z');");
        store.Execute("INSERT INTO long_bar (ticker, session_date, open, high, low, close, raw_close, volume, run_id) VALUES "
            + string.Join(", ", new[] { "BLD", "DTV_old", "RDC", "WRK", "CCE_old", "CHMT_old", "PKY" }.Select(code => $"('{code}', '2009-01-02', '10', '10', '10', '10', '10', 1000, 'r')")) + ";");

        new MigrationRunner([.. SchemaMigrations.All]).Apply(store.DatabaseFile);

        // TW2 holds no long price in this store, so it takes no ruling, as a new store takes none.
        Assert.Equal("HOS 2020-03-02,LPI 2026-01-02", (string)One(store, "SELECT group_concat(ticker || ' ' || first_session, ',') FROM (SELECT ticker, first_session FROM long_held_none ORDER BY ticker);")!);
        Assert.Equal(
            "BLD read through,CCE_old kept,CHMT_old kept,DTV_old read through,PKY read through,RDC read through,WRK read through",
            (string)One(store, "SELECT group_concat(ticker || ' ' || ruling, ',') FROM (SELECT ticker, ruling FROM mark_ruling ORDER BY ticker);")!);
        Assert.Equal("files on both sides gaps", (string)One(store, "SELECT filer || ' ' || price FROM mark_ruling WHERE ticker = 'CHMT_old';")!);
        Assert.Throws<SqliteException>(() => store.Execute("DELETE FROM long_held_none;"));
        Assert.Throws<SqliteException>(() => store.Execute("DELETE FROM mark_ruling;"));
    }
}
