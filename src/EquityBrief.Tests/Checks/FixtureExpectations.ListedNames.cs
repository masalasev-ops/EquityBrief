using EquityBrief.Api.Reading;
using EquityBrief.Core.Providers;
using EquityBrief.Core.Time;
using EquityBrief.Tests.Harness;
using EquityBrief.Web.Marks;
using EquityBrief.Worker.Members;

namespace EquityBrief.Tests.Checks;

// fixture-expectations, 23.10: on the operator's answer of 2026-11-17, item 6, the names refresh asks the provider's symbol
// list once a week and keeps each member's company name as the list spells it, the common stock's where a code is listed
// twice; inside the week it asks nothing; a refused list keeps every name held; and every page names a member by it, or by
// the name its index's file holds in title case where the list names none, those listed on the Run page.
// see: A member's company is named as the provider's symbol list spells it, read once a week outside the night
public partial class FixtureExpectations
{
    // KTOS listed twice, its common stock and a preferred share; AAON once; ZZZ, a member, not at all; and XYZ, a stock no
    // index holds, which no row is kept for.
    const string ListedSymbols = """
        [
          {"Code": "KTOS", "Name": "Kratos Defense & Security Solutions Inc", "Exchange": "NASDAQ", "Type": "Common Stock"},
          {"Code": "KTOS", "Name": "Kratos Defense Preferred", "Exchange": "NASDAQ", "Type": "Preferred Stock"},
          {"Code": "AAON", "Name": "AAON Inc", "Exchange": "NASDAQ", "Type": "Common Stock"},
          {"Code": "XYZ", "Name": "Xyz Corp", "Exchange": "NYSE", "Type": "Common Stock"}
        ]
        """;

    static TemporaryStore NamesStore()
    {
        var store = new TemporaryStore().Migrated();

        store.Execute(
            "INSERT INTO membership (index_code, ticker, joined, \"left\", observed_at, sector, name) VALUES "
            + "('MID', 'KTOS', NULL, NULL, '2026-11-16T21:10:00Z', NULL, 'KRATOS DEFENSE AND SECURITY SOLUTI'), "
            + "('MID', 'AAON', NULL, NULL, '2026-11-16T21:10:00Z', NULL, 'AAON'), "
            + "('MID', 'ZZZ', NULL, NULL, '2026-11-16T21:10:00Z', NULL, 'ZEBRA ZONE HOLDINGS'), "
            + "('GSPC', 'OLD', NULL, '2026-10-01', '2026-11-16T21:10:00Z', NULL, 'OLD CO');");

        return store;
    }

    static NamesRefresh NamesAt(TemporaryStore store, DateTimeOffset at) => new(FixedClock.At(at, SessionZones.UnitedStates), store.DatabaseFile);

    [Fact]
    public async Task TheNamesRefreshKeepsEachMembersNameAsTheListSpellsItOnceAWeek()
    {
        using var store = NamesStore();
        var first = new DateTimeOffset(2026, 11, 18, 4, 20, 0, TimeSpan.Zero);
        var feed = new RecordedSymbolListFeed(ListedSymbols, "[]");

        // The first run asks once and keeps the two members the list names, KTOS by its common stock's name; ZZZ is counted
        // and named on the row; neither XYZ nor OLD, held by no index today, is kept.
        var outcome = await NamesAt(store, first).RunAsync(feed);

        Assert.True(outcome.Asked);
        Assert.Equal((2, 1), (outcome.Named, outcome.Requests));
        Assert.Equal(["ZZZ"], outcome.Unnamed);
        Assert.Equal(
            ["AAON|AAON Inc|2026-11-17", "KTOS|Kratos Defense & Security Solutions Inc|2026-11-17"],
            Query(store, "SELECT ticker || '|' || name || '|' || read_on FROM listed_name ORDER BY ticker;"));
        Assert.Equal(
            "2 of 3 member(s) named as the symbol list spells them, 1 the list names none of, in 1 request(s): ZZZ",
            Query(store, "SELECT detail FROM run_log WHERE stage = 'names';").Single());

        // Six days later it asks nothing and says when it will.
        var inside = await NamesAt(store, first.AddDays(6)).RunAsync(feed);

        Assert.False(inside.Asked);
        Assert.Equal(1, feed.Requests);
        Assert.Contains("so nothing is asked until 2026-11-24", Query(store, "SELECT detail FROM run_log WHERE stage = 'names' ORDER BY rowid DESC LIMIT 1;").Single(), StringComparison.Ordinal);

        // Seven days later it asks again, each row read again.
        var again = await NamesAt(store, first.AddDays(7)).RunAsync(feed);

        Assert.True(again.Asked);
        Assert.Equal(2, feed.Requests);
        Assert.Equal(["2026-11-24", "2026-11-24"], Query(store, "SELECT read_on FROM listed_name ORDER BY ticker;"));

        // A list that is not a list is refused a week on, every name kept.
        var refused = await NamesAt(store, first.AddDays(14)).RunAsync(new RecordedSymbolListFeed("{}", "[]"));

        Assert.NotNull(refused.Refused);
        Assert.Equal(["AAON|AAON Inc|2026-11-24", "KTOS|Kratos Defense & Security Solutions Inc|2026-11-24"], Query(store, "SELECT ticker || '|' || name || '|' || read_on FROM listed_name ORDER BY ticker;"));
        Assert.Equal("partial", Query(store, "SELECT outcome FROM run_log WHERE stage = 'names' ORDER BY rowid DESC LIMIT 1;").Single());
    }

    [Fact]
    public async Task EveryPageNamesAMemberAsTheListSpellsItOrInTitleCaseAndTheRunPageListsThoseItNamesNoneOf()
    {
        using var store = NamesStore();
        var read = new ReadApi(store.DatabaseFile, FixedClock.At(new DateTimeOffset(2026, 11, 18, 4, 20, 0, TimeSpan.Zero), SessionZones.UnitedStates));
        var night = new DateOnly(2026, 11, 17);

        // Before the list is read: each member named as its index's file holds it, in title case where that is in capitals,
        // and the Run page says the list has not been read.
        Assert.Equal(
            ["AAON|Aaon", "KTOS|Kratos Defense And Security Soluti", "ZZZ|Zebra Zone Holdings"],
            (await read.PaperStocksAsync(["AAON", "KTOS", "ZZZ"], night)).Values.OrderBy(row => row.Ticker, StringComparer.Ordinal).Select(row => row.Ticker + "|" + row.Company));

        var before = await read.UnlistedNamesAsync("MID", night);

        Assert.Null(before.ReadOn);
        Assert.Contains(MarkRenderer.NamesNotRead, MarkRenderer.UnlistedNames(before.Unlisted, before.ReadOn), StringComparison.Ordinal);

        // Once read: the list's names, ZZZ alone in title case, and the Run page lists ZZZ.
        await NamesAt(store, new DateTimeOffset(2026, 11, 18, 4, 20, 0, TimeSpan.Zero)).RunAsync(new RecordedSymbolListFeed(ListedSymbols, "[]"));

        Assert.Equal(
            ["AAON|AAON Inc", "KTOS|Kratos Defense & Security Solutions Inc", "ZZZ|Zebra Zone Holdings"],
            (await read.PaperStocksAsync(["AAON", "KTOS", "ZZZ"], night)).Values.OrderBy(row => row.Ticker, StringComparer.Ordinal).Select(row => row.Ticker + "|" + row.Company));

        var after = await read.UnlistedNamesAsync("MID", night);
        var listed = MarkRenderer.UnlistedNames(after.Unlisted, after.ReadOn);

        Assert.Equal([("ZZZ", "Zebra Zone Holdings")], after.Unlisted);
        Assert.Contains("<h3>Members the symbol list names none of (1)</h3>", listed, StringComparison.Ordinal);
        Assert.Contains("<li data-ticker=\"ZZZ\">ZZZ: Zebra Zone Holdings</li>", listed, StringComparison.Ordinal);

        // The universe's rows carry the name the page draws and, apart, the name a story is matched against.
        var ktos = (await read.UniverseAsync("MID", night)).Single(row => row.Ticker == "KTOS");

        Assert.Equal(("Kratos Defense & Security Solutions Inc", "KRATOS DEFENSE AND SECURITY SOLUTI"), (ktos.Name, ktos.HeldName));
    }
}
