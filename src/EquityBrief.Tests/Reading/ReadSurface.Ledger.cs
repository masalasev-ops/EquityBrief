using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using EquityBrief.Core.Ledger;
using EquityBrief.Tests.Harness;
using EquityBrief.Web.App;

namespace EquityBrief.Tests.Reading;

// read-surface, 17.3: the setup ledger's page under Universe, read off the rendered page over a constructed store. Each
// family's card draws its years and the deciles of its newest year holding settled setups from the summary as stored,
// a heavyweights' figure as a share of the buy and every other family's in risks; the family the link names lists its
// newest settled setups, each a link drawing its path; the chosen setup's closes are drawn with its buy, stop and target
// and its session marked; one whose bars the store holds only in the pulled history says so; and an index whose ledger
// holds nothing says so in one line.
// see: A setup is every member-session a family's loose gates pass, and its readings are defined once and read as they stood
public partial class ReadSurface
{
    // The parts of section 15.19's regions this check reaches.
    internal static readonly string[] LedgerPageClaims =
    [
        CheckReach.Key(Scope.LedgerPage, Scope.LedgerFamilyYears),
        CheckReach.Key(Scope.LedgerPage, Scope.LedgerSettled),
        CheckReach.Key(Scope.LedgerPage, Scope.LedgerPathDrawn),
    ];

    // Every row the Ledger page adds, named after phase 16's report until phase 17's own pair is checked: its three
    // regions and its summary's store.
    internal static string[] LedgerPageRows => [.. LedgerPageClaims, CheckReach.Key(Scope.StoresTable, "Ledger summaries")];

    // The row 22.2 adds to section 15.19, the short-term dip's counts.
    internal static readonly string[] DipLedgerPageRows =
    [
        CheckReach.Key(Scope.LedgerPage, "The short-term dip's setups"),
    ];

    static void LedgerYearRow(TemporaryStore store, string family, int year, int setups, int passes, int nightRows, int picked, int settled, string? result, string? edge, string? deciles) =>
        store.Execute(
            "INSERT INTO ledger_summary (index_code, family, year, setups, live_passes, night_rows, picked, settled, result_mean, edge_mean, result_deciles, edge_deciles, refreshed_at) "
            + $"VALUES ('GSPC', '{family}', {year}, {setups}, {passes}, {nightRows}, {picked}, {settled}, {result ?? "NULL"}, {edge ?? "NULL"}, {(deciles is null ? "NULL" : $"'{deciles}'")}, {(deciles is null ? "NULL" : $"'{deciles}'")}, '2026-10-09T00:00:00.000Z');");

    static void LedgerSettledRow(TemporaryStore store, string ticker, string session, string end, string endedOn, double result, double edge, string source) =>
        store.Execute(
            "INSERT INTO setup (index_code, family, ticker, session_date, rule, live_pass, picked, entry, stop, target, trail, cap, result, edge, sessions, end, ended_on, settled, source, pin) "
            + $"VALUES ('GSPC', 'pullback', '{ticker}', '{session}', 'the live rule', 1, NULL, '100', '95', '110', NULL, 21, {result.ToString(CultureInfo.InvariantCulture)}, {edge.ToString(CultureInfo.InvariantCulture)}, 5, '{end}', '{endedOn}', 1, '{source}', 'x');");

    [Fact]
    public async Task TheLedgerPageDrawsEachFamilysYearsAndDecilesAndAChosenSetupsPathWithItsPlansLines()
    {
        using var store = new TemporaryStore().Migrated();

        // The summary as the ledger stored it: the pullback's two years, its newest holding settled setups, and the
        // heavyweights' one year read as a share of the buy.
        LedgerYearRow(store, "pullback", 2025, 20, 5, 0, 0, 20, "-0.1", "-0.2", "-1,-0.8,-0.5,-0.2,0,0.2,0.5,1,2");
        LedgerYearRow(store, "pullback", 2026, 40, 10, 8, 4, 30, "0.4", "0.1", "-1,-0.5,0,0.25,0.5,1,1.5,2,3");
        LedgerYearRow(store, "heavyweight", 2026, 30, 6, 30, 6, 12, "0.12", "0.03", "-0.1,-0.05,0,0.02,0.04,0.06,0.1,0.2,0.3");

        // Two settled pullback setups: AAA's, whose closes the bar store holds, and BBB's, whose it holds none of.
        LedgerSettledRow(store, "AAA", "2026-03-09", "target", "2026-03-16", 2.0, 1.5, "night");
        LedgerSettledRow(store, "BBB", "2025-02-03", "stop", "2025-02-10", -1.0, -1.2, "history");

        var day = new DateOnly(2026, 2, 23);

        for (var close = 100; day <= new DateOnly(2026, 3, 20); day = day.AddDays(1))
        {
            if (day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
            {
                continue;
            }

            var stamp = day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

            store.Execute($"INSERT INTO bar (ticker, session_date, open, high, low, close, volume, source, observed_at, raw_close) VALUES ('AAA', '{stamp}', '{close}', '{close + 1}', '{close - 1}', '{close}', 1000, 'constructed', '{stamp}T21:00:00Z', '{close}');");
            close++;
        }

        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        var page = WebUtility.HtmlDecode(await client.GetStringAsync("/screens/ledger?family=pullback&setup=AAA.2026-03-09"));

        // Each family a card, its years from the summary: 2026's ten of forty passed, a quarter, four of the eight a
        // night wrote picked, thirty settled at a mean of 0.40 risks and an edge of 0.10.
        Assert.Contains("data-card=\"ledger-pullback\"", page, StringComparison.Ordinal);
        Assert.Contains("data-card=\"ledger-heavyweight\"", page, StringComparison.Ordinal);
        Assert.Contains("<tr data-year=\"2026\" data-setups=\"40\" data-live-passes=\"10\" data-picked=\"4\" data-settled=\"30\" data-night-rows=\"8\"><td class=\"num\">2026</td><td class=\"r num\">40</td><td class=\"r num\">25%</td><td class=\"r num\">50% of 8</td><td class=\"r num\">30</td>", page, StringComparison.Ordinal);
        Assert.Contains("data-result-mean=\"0.4\"><span class=\"gain\">+0.40</span></td>", page, StringComparison.Ordinal);

        // From 19.7 the unit is named once in each heading, and a year no night wrote reads none with the line beneath.
        Assert.Contains("Result, risks", page, StringComparison.Ordinal);
        Assert.Contains("Result, % of the buy", page, StringComparison.Ordinal);
        Assert.Contains("<td class=\"r num\"><span class=\"degraded\">none</span></td>", page, StringComparison.Ordinal);
        Assert.Contains("None written by a night: the 1 year(s) whose Picked reads none were written by the history build alone, which writes no pick.", page, StringComparison.Ordinal);

        // The heavyweights' year read as a share of the buy, 0.12 as 12.0 per cent.
        Assert.Contains("data-result-mean=\"0.12\"><span class=\"gain\">+12.0</span></td>", page, StringComparison.Ordinal);

        // The deciles of the pullback's newest year holding settled setups, 2026's, nine cut points, the fourth at 0.25,
        // each drawn as a row of the table and a dot of the strip.
        Assert.Contains("<table class=\"ledger-deciles\" data-family=\"pullback\" data-year=\"2026\" data-settled=\"30\">", page, StringComparison.Ordinal);
        Assert.Contains("<tr data-cut=\"4\"><td class=\"num\">40%</td><td class=\"r num\" data-result=\"0.25\"><span class=\"gain\">+0.25</span></td>", page, StringComparison.Ordinal);
        Assert.Equal(2 * LedgerSummaries.CutPoints, page.Split("<tr data-cut=\"", StringSplitOptions.None).Length - 1);
        Assert.Equal(2 * LedgerSummaries.CutPoints, Regex.Matches(page, "<circle class=\"(?:gain|loss)\" data-cut=\"").Count);
        Assert.Contains("<svg class=\"ledger-decile-strip\" data-family=\"pullback\" data-year=\"2026\"", page, StringComparison.Ordinal);
        Assert.Contains("<svg class=\"ledger-edge-bars\" data-family=\"pullback\"", page, StringComparison.Ordinal);

        // The family chosen marked among the tabs, each in its tint, its newest settled setups newest first, each a link.
        Assert.Contains("data-family-link=\"pullback\" href=\"#/ledger?universe=500&family=pullback\" aria-current=\"true\">", page, StringComparison.Ordinal);
        Assert.Contains("data-family-link=\"heavyweight\" href=\"#/ledger?universe=500&family=heavyweight\"><span class=\"fam-swatch\" data-family=\"heavyweight\"></span>Sector heavyweights</a>", page, StringComparison.Ordinal);
        Assert.True(page.IndexOf("data-ticker=\"AAA\" data-session=\"2026-03-09\"", StringComparison.Ordinal) < page.IndexOf("data-ticker=\"BBB\" data-session=\"2025-02-03\"", StringComparison.Ordinal));
        Assert.Contains("href=\"#/ledger?universe=500&family=pullback&setup=AAA.2026-03-09\">AAA</a>", page, StringComparison.Ordinal);

        // AAA's path: its closes from fourteen days before its session to the session it ended on, sixteen weekdays,
        // its buy, its stop and its target drawn across and its session marked.
        Assert.Contains("class=\"ledger-path\" data-ticker=\"AAA\" data-session=\"2026-03-09\" data-closes=\"16\"", page, StringComparison.Ordinal);
        Assert.Contains("data-buy=\"100\"", page, StringComparison.Ordinal);
        Assert.Contains("data-stop=\"95\"", page, StringComparison.Ordinal);
        Assert.Contains("data-target=\"110\"", page, StringComparison.Ordinal);
        Assert.Contains("data-bought=\"2026-03-09\"", page, StringComparison.Ordinal);
        Assert.Contains(SinglePageApp.LedgerGuide, page, StringComparison.Ordinal);

        // BBB's bars sit in the pulled history, which the page does not read, and it says so; and no setup chosen asks
        // for one.
        Assert.Contains(
            "The store holds 0 close(s) of BBB over this setup's sessions: its bars sit in the pulled history, which this page does not read.",
            WebUtility.HtmlDecode(await client.GetStringAsync("/screens/ledger?family=pullback&setup=BBB.2025-02-03")),
            StringComparison.Ordinal);
        Assert.Contains("Choose a settled setup above to draw its path.", WebUtility.HtmlDecode(await client.GetStringAsync("/screens/ledger")), StringComparison.Ordinal);

        // An index the ledger holds nothing for.
        Assert.Contains(
            "The ledger holds no setup on this index yet.",
            WebUtility.HtmlDecode(await client.GetStringAsync("/screens/ledger?universe=400")),
            StringComparison.Ordinal);
    }

    // 22.2: the short-term dip's counts a year on each index, from the summary as each index's ledger stored it, and a
    // settled dip setup's path drawn with its buy and its stop and no target, ended at the next open after its exit.
    [Fact]
    public async Task TheLedgerPageDrawsTheDipsCountsAYearOnEachIndexAndItsSetupsPathWithNoTarget()
    {
        using var store = new TemporaryStore().Migrated();

        foreach (var (index, setups) in new[] { ("GSPC", 120), ("MID", 90), ("SML", 60) })
        {
            store.Execute(
                "INSERT INTO ledger_summary (index_code, family, year, setups, live_passes, night_rows, picked, settled, result_mean, edge_mean, result_deciles, edge_deciles, refreshed_at) "
                + $"VALUES ('{index}', 'dip', 2026, {setups}, 6, 12, 3, 30, 0.2, 0.05, '-1,-0.5,0,0.25,0.5,1,1.5,2,3', '-1,-0.5,0,0.25,0.5,1,1.5,2,3', '2026-10-24T00:00:00.000Z');");
        }

        store.Execute(
            "INSERT INTO setup (index_code, family, ticker, session_date, rule, live_pass, picked, entry, stop, target, trail, cap, result, edge, sessions, end, ended_on, settled, source, pin) "
            + "VALUES ('GSPC', 'dip', 'AAA', '2026-03-09', 'the provisional rule', 0, 0, '100', '96', NULL, NULL, 10, 0.5, 0.4, 3, 'exit', '2026-03-12', 1, 'night', 'x');");

        foreach (var (stamp, close) in new[] { ("2026-03-05", 104), ("2026-03-06", 102), ("2026-03-09", 100), ("2026-03-10", 101), ("2026-03-11", 103), ("2026-03-12", 102) })
        {
            store.Execute($"INSERT INTO bar (ticker, session_date, open, high, low, close, volume, source, observed_at, raw_close) VALUES ('AAA', '{stamp}', '{close}', '{close + 1}', '{close - 1}', '{close}', 1000, 'constructed', '{stamp}T21:00:00Z', '{close}');");
        }

        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        foreach (var (universe, setups) in new[] { ("500", 120), ("400", 90), ("600", 60) })
        {
            var page = WebUtility.HtmlDecode(await client.GetStringAsync($"/screens/ledger?universe={universe}"));

            Assert.Contains("data-card=\"ledger-dip\"", page, StringComparison.Ordinal);
            Assert.Contains("Short-term dip setups", page, StringComparison.Ordinal);
            Assert.Contains($"<tr data-year=\"2026\" data-setups=\"{setups}\" data-live-passes=\"6\" data-picked=\"3\" data-settled=\"30\" data-night-rows=\"12\">", page, StringComparison.Ordinal);
        }

        var path = WebUtility.HtmlDecode(await client.GetStringAsync("/screens/ledger?family=dip&setup=AAA.2026-03-09"));

        Assert.Contains("class=\"ledger-path\" data-ticker=\"AAA\" data-session=\"2026-03-09\"", path, StringComparison.Ordinal);
        Assert.Contains("data-buy=\"100\"", path, StringComparison.Ordinal);
        Assert.Contains("data-stop=\"96\"", path, StringComparison.Ordinal);
        Assert.DoesNotContain("data-target=", path, StringComparison.Ordinal);
        Assert.Contains($"data-end=\"{SetupEnds.Exit}\"", path, StringComparison.Ordinal);
    }

    // 19.2: the Ledger page and the Loop page list the families in Tonight's order, the one list the core holds, over a
    // store whose rows a family stand in the reverse of it, which the store's own order by name would draw as breakout,
    // drift, heavyweight and pullback.
    [Fact]
    public async Task TheLedgerAndTheLoopPagesListTheFamiliesInTonightsOrder()
    {
        using var store = new TemporaryStore().Migrated();
        string[] reversed = ["heavyweight", "fundamentals", "drift", "breakout", "pullback"];

        foreach (var family in reversed)
        {
            LedgerYearRow(store, family, 2026, 10, 2, 0, 0, 5, "0.1", "0.05", "-1,-0.5,0,0.25,0.5,1,1.5,2,3");
        }

        // The Loop page reads the seven families the loop proposes for, in Tonight's order of their cards.
        foreach (var family in EquityBrief.Core.Loop.LoopDecisions.LoopFamilies.Reverse())
        {
            store.Execute(
                "INSERT INTO loop_alarm (index_code, family, period, trades, edge, edge_floor, counted, under, streak, flagged, reference, run_id) VALUES " +
                $"('GSPC', '{family}', '2026-08-01', 5, 0.1, -0.1, 1, 0, 0, 0, '{LargeOctober}', 'night-1');");
        }

        Run(store, LargeOctober, "GSPC", "2026-10");

        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        var ledger = WebUtility.HtmlDecode(await client.GetStringAsync("/screens/ledger"));
        var loop = WebUtility.HtmlDecode(await client.GetStringAsync("/screens/loop?universe=500"));
        string[] order = ["pullback", "breakout", "drift", "fundamentals", "heavyweight"];

        Assert.Equal(order, EquityBrief.Core.Families.SetupFamilies.Order);
        Assert.Equal(order, Regex.Matches(ledger, "data-card=\"ledger-([a-z]+)\"").Select(match => match.Groups[1].Value).Where(card => card != "path"));
        Assert.Equal(order, Regex.Matches(ledger, "data-family-(?:link|chosen)=\"([a-z]+)\"").Select(match => match.Groups[1].Value));
        Assert.Contains("data-family-link=\"pullback\" href=\"#/ledger?universe=500&family=pullback\" aria-current=\"true\">", ledger, StringComparison.Ordinal);
        string[] loopOrder = ["pullback", "breakout", "drift", "improving", "dip", "heavyweight", "quality"];

        Assert.Equal(loopOrder, EquityBrief.Core.Loop.LoopDecisions.LoopFamilies);
        Assert.Equal(loopOrder, Regex.Matches(loop, "<div class=\"loop-alarm\" data-family=\"([a-z]+)\"").Select(match => match.Groups[1].Value));
        Assert.Equal(loopOrder, Regex.Matches(loop, "data-loop-rule=\"none\" data-family=\"([a-z]+)\"").Select(match => match.Groups[1].Value));
    }

    [Fact]
    public void TheDecilesCutPointsAreTheNearestRanksOfTheSortedFiguresAndReadBackAsStored()
    {
        // Ten figures: each cut point the figure at its own rank. Twenty: every second. One: that figure nine times.
        Assert.Equal([1.0, 2, 3, 4, 5, 6, 7, 8, 9], LedgerSummaries.Deciles([.. Enumerable.Range(1, 10).Select(one => (double)one).Reverse()]));
        Assert.Equal([2.0, 4, 6, 8, 10, 12, 14, 16, 18], LedgerSummaries.Deciles([.. Enumerable.Range(1, 20).Select(one => (double)one)]));
        Assert.Equal(Enumerable.Repeat(5.0, 9), LedgerSummaries.Deciles([5.0]));
        Assert.Null(LedgerSummaries.Deciles([]));

        // Three figures: the nearest rank of a tenth is the first, of four tenths the second and of seven tenths the third.
        Assert.Equal([1.0, 1, 1, 2, 2, 2, 3, 3, 3], LedgerSummaries.Deciles([3.0, 1, 2]));

        var stored = LedgerSummaries.Stored([-1.5, 0, 0.123456789]);

        Assert.Equal("-1.5,0,0.123457", stored);
        Assert.Equal([-1.5, 0, 0.123457], LedgerSummaries.Read(stored));
    }

    [Fact]
    public async Task EveryTableTheLedgerPageDrawsIsReadInABoxOfItsOwn()
    {
        using var store = new TemporaryStore().Migrated();

        LedgerYearRow(store, "pullback", 2026, 40, 10, 8, 4, 30, "0.4", "0.1", "-1,-0.5,0,0.25,0.5,1,1.5,2,3");
        LedgerYearRow(store, "heavyweight", 2026, 30, 6, 30, 6, 12, "0.12", "0.03", "-0.1,-0.05,0,0.02,0.04,0.06,0.1,0.2,0.3");
        LedgerSettledRow(store, "AAA", "2026-03-09", "target", "2026-03-16", 2.0, 1.5, "night");

        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        var page = await client.GetStringAsync("/screens/ledger?family=pullback");

        // Each table opened by the box that scrolls it, so a wide one scrolls in its own frame at a phone's width and
        // pushes nothing off the page. Stated in advance: the two families' years and deciles and the pullback's
        // settled setups, five.
        var bare = Regex
            .Matches(page, "(.{0,24})<table(?: class=\"([a-z-]+)\")?[ >]")
            .Where(table => !table.Groups[1].Value.EndsWith("<div class=\"tbl-wrap\">", StringComparison.Ordinal))
            .Select(table => table.Groups[2].Value)
            .ToArray();

        Assert.Equal(5, Regex.Matches(page, "<table[ >]").Count);
        Assert.True(bare.Length == 0, $"On the Ledger page, {string.Join(", ", bare)} is drawn outside a box of its own.");
    }
}
