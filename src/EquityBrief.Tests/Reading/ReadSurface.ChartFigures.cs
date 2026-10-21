using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using EquityBrief.Core.Report;
using EquityBrief.Core.Time;
using EquityBrief.Tests.Harness;
using EquityBrief.Web.App;

namespace EquityBrief.Tests.Reading;

// read-surface, from the 19.11 correction of 2026-10-21: the chart page's four dividend figures say pays none for a company
// whose fetch files a dividend rate of nought, or no rate beside dividends kept from before the year, with no dividend kept
// in the year to the night, its last whole year drawn where it paid in it, a payer on its year's dividends drawn as one and
// not stored only for one storing neither, and the report's yield tile, on the page and at the quote, says the same, read
// over the universes' store.
// see: A company filing a dividend rate of nought with no dividend kept in the year is drawn as paying none, and one storing neither as not stored
public partial class ReadSurface
{
    [Fact]
    public async Task TheChartPageSaysACompanyPaysNoneWhereItsFetchFilesARateOfNoughtAndNotStoredWhereItStoresNeither()
    {
        var night = new DateOnly(2026, 10, 2);

        // Worked by hand: a rate of nought with nothing kept pays none, and so does one whose dividend is a whole year back,
        // while one a day inside the year is a payer; no rate beside a dividend kept from before the year pays none, while no
        // rate and nothing kept, a dividend after the night alone, or a rate above nought is no company paying none.
        Assert.True(DividendSafety.PaysNone(0m, [], night));
        Assert.True(DividendSafety.PaysNone(0m, [new KeptDividend(new DateOnly(2025, 10, 2), 0.50m)], night));
        Assert.False(DividendSafety.PaysNone(0m, [new KeptDividend(new DateOnly(2025, 10, 3), 0.50m)], night));
        Assert.True(DividendSafety.PaysNone(null, [new KeptDividend(new DateOnly(2025, 10, 2), 0.50m)], night));
        Assert.False(DividendSafety.PaysNone(null, [], night));
        Assert.False(DividendSafety.PaysNone(null, [new KeptDividend(new DateOnly(2026, 10, 3), 0.50m)], night));
        Assert.False(DividendSafety.PaysNone(0.01m, [], night));

        // The rate a yield is read at: the forward rate above nought, the year's two of 0.25 where the fetch files nought,
        // and none where the one dividend kept is a whole year back.
        KeptDividend[] twoInTheYear = [new(new DateOnly(2025, 10, 3), 0.25m), new(new DateOnly(2025, 12, 10), 0.25m)];

        Assert.Equal((1.20m, true), DividendSafety.RateOf(1.20m, twoInTheYear, night));
        Assert.Equal((0.50m, false), DividendSafety.RateOf(0m, twoInTheYear, night));
        Assert.Null(DividendSafety.RateOf(0m, [new KeptDividend(new DateOnly(2025, 10, 2), 0.50m)], night));

        using var store = UniversesStore();

        // S1 files a rate of nought and keeps no dividend; M1 files nought and paid its last a whole year before the night,
        // in 2025, the whole year before the night's; M2 files nought and paid two of 0.25 inside the year; M3 stores
        // neither; S2 files no rate and paid 0.40 in 2025, before the year; AAA files 1.00 and paid two of 0.20 in 2024 and
        // two of 0.25 in 2025, a raise in the one year after the first, so one year raised in a row.
        store.Execute(
            "INSERT INTO dividend_reading (ticker, fetched_at, forward_rate, last_ex_date, by_year) VALUES " +
            "('S1', '2026-10-02T23:00:00Z', '0', NULL, '{}'), ('M1', '2026-10-02T23:00:00Z', '0', NULL, '{}'), ('M2', '2026-10-02T23:00:00Z', '0', NULL, '{}'), " +
            "('AAA', '2026-10-02T23:00:00Z', '1.00', NULL, '{}');");
        store.Execute(
            "INSERT INTO dividend_event (ticker, ex_date, amount, source, run_id) VALUES " +
            "('M1', '2025-10-02', '0.50', 'history', 'constructed'), ('M2', '2025-10-03', '0.25', 'history', 'constructed'), ('M2', '2025-12-10', '0.25', 'history', 'constructed'), " +
            "('S2', '2025-06-02', '0.40', 'history', 'constructed'), " +
            "('AAA', '2024-03-01', '0.20', 'history', 'constructed'), ('AAA', '2024-09-03', '0.20', 'history', 'constructed'), " +
            "('AAA', '2025-03-03', '0.25', 'history', 'constructed'), ('AAA', '2025-09-02', '0.25', 'history', 'constructed');");

        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        async Task<IReadOnlyDictionary<string, string>> Grid(string ticker, string group)
        {
            var page = WebUtility.HtmlDecode(await client.GetStringAsync($"/screens/chart/{ticker}"));
            var held = Regex.Match(page, $"<section class=\"grid-group\" data-group=\"{group}\"><h3>[^<]+</h3><dl>(.*?)</dl></section>", RegexOptions.Singleline);

            Assert.True(held.Success, $"{ticker}'s chart page draws no {group} group.");

            return Regex.Matches(held.Groups[1].Value, "<dt>([^<]+)</dt><dd([^>]*)>(.*?)</dd>")
                .ToDictionary(match => match.Groups[1].Value, match => match.Groups[3].Value + (match.Groups[2].Value.Contains("degraded", StringComparison.Ordinal) ? " (degraded)" : string.Empty));
        }

        string[] dividend = ["Yield", "Paid in its last whole year", "Paid out of earnings", "Raised in a row"];
        var none = await Grid("S1", "dividend");
        var stopped = await Grid("M1", "dividend");
        var payer = await Grid("M2", "dividend");
        var nothing = await Grid("M3", "dividend");
        var unrated = await Grid("S2", "dividend");
        var raising = await Grid("AAA", "dividend");

        Assert.Equal([.. dividend.Select(_ => SinglePageApp.PaysNone)], dividend.Select(label => none[label]));
        Assert.Equal([SinglePageApp.PaysNone, "0.50 in 2025, 1 payment", SinglePageApp.PaysNone, SinglePageApp.PaysNone], dividend.Select(label => stopped[label]));
        Assert.Equal(
            ["1.00% on the last year's payments of 0.50", "0.50 in 2025, 2 payments", "not stored (degraded)", "not stored (degraded)"],
            dividend.Select(label => payer[label]));
        Assert.Equal([.. dividend.Select(_ => "not stored (degraded)")], dividend.Select(label => nothing[label]));
        Assert.Equal([SinglePageApp.PaysNone, "0.40 in 2025, 1 payment", SinglePageApp.PaysNone, SinglePageApp.PaysNone], dividend.Select(label => unrated[label]));
        Assert.Equal(
            ["2.00% on the forward rate of 1.00", "0.50 in 2025, 2 payments", "not stored (degraded)", "1 year"],
            dividend.Select(label => raising[label]));

        // The report's yield tile says a company paying none pays none, on S1's own page and on S2's, one storing neither
        // that none is stored, on M3's, and a payer whose fetch files nought is read on the year's payments, on M2's.
        async Task<string> Tile(string ticker) =>
            Regex.Replace(
                Regex.Match(WebUtility.HtmlDecode(await client.GetStringAsync($"/screens/name/{ticker}")), "<div class=\"lead-tile\" data-tile=\"yield\">(.*?)</div>", RegexOptions.Singleline).Groups[1].Value,
                "<[^>]+>",
                " ");

        const string PaysNoneWords = "It pays none: no payment is kept in the year, and its fetch files no rate above nought.";

        Assert.Contains(PaysNoneWords, await Tile("S1"), StringComparison.Ordinal);
        Assert.Contains(PaysNoneWords, await Tile("S2"), StringComparison.Ordinal);
        Assert.Contains("No dividend is stored for it.", await Tile("M3"), StringComparison.Ordinal);
        Assert.DoesNotContain("It pays none", await Tile("M3"), StringComparison.Ordinal);
        Assert.Matches(@"^\s*Dividend yield\s+1\.00%\s+0\.50\s+paid in the last year at\s+50\.00\s+, the last close; the 10-year not read yet\s*$", await Tile("M2"));

        // At the quote inside the session of 2026-10-07 the live tiles read the same: S1 pays none, and M2's one dividend
        // still inside that day's year, 0.25 of 2025-12-10, is half a per cent of the quote of 50.
        store.Execute(
            "INSERT INTO live_quote (ticker, asked_at, answered_at, quoted_at, price, previous_close, change, change_pct, night, distances) VALUES " +
            "('S1', '2026-10-07T14:55:00Z', '2026-10-07T14:55:04Z', '2026-10-07T14:43:00Z', '50', '50', '0', 0, '2026-10-02', '[]'), " +
            "('M2', '2026-10-07T14:55:00Z', '2026-10-07T14:55:04Z', '2026-10-07T14:43:00Z', '50', '50', '0', 0, '2026-10-02', '[]');");

        using var session = new PassHost(store.Root) { Clock = FixedClock.At(InTheSession, SessionZones.UnitedStates) };
        using var live = session.CreateClient();

        async Task<string> LiveTiles(string ticker)
        {
            using var reply = JsonDocument.Parse(await live.GetStringAsync(SinglePageApp.QuoteRoute + ticker));

            Assert.True(reply.RootElement.GetProperty("live").GetBoolean(), $"{ticker}'s quote is not read inside the session.");

            return Regex.Replace(WebUtility.HtmlDecode(reply.RootElement.GetProperty("tiles").GetString() ?? string.Empty), "<[^>]+>", " ");
        }

        Assert.Contains(PaysNoneWords, await LiveTiles("S1"), StringComparison.Ordinal);
        Assert.Matches(@"Dividend yield\s+0\.50%\s+0\.25\s+paid in the last year at\s+50\.00\s+, the quote", await LiveTiles("M2"));
    }
}
