using System.Net;
using System.Text.RegularExpressions;
using EquityBrief.Core.Report;
using EquityBrief.Web.App;

namespace EquityBrief.Tests.Reading;

// read-surface, from the 19.11 correction of 2026-10-21: the chart page's four dividend figures say pays none for a company
// whose fetch files a dividend rate of nought with no dividend kept in the year to the night, its last whole year drawn
// where it paid in it, and not stored only for one storing neither, and the report's yield tile says the same, read over
// the universes' store.
// see: A company filing a dividend rate of nought with no dividend kept in the year is drawn as paying none, and one storing neither as not stored
public partial class ReadSurface
{
    [Fact]
    public async Task TheChartPageSaysACompanyPaysNoneWhereItsFetchFilesARateOfNoughtAndNotStoredWhereItStoresNeither()
    {
        var night = new DateOnly(2026, 10, 2);

        // Worked by hand: a rate of nought with nothing kept pays none, and so does one whose dividend is a whole year back,
        // while one a day inside the year is a payer; no rate stored, or a rate above nought, is no company paying none.
        Assert.True(DividendSafety.PaysNone(0m, [], night));
        Assert.True(DividendSafety.PaysNone(0m, [new KeptDividend(new DateOnly(2025, 10, 2), 0.50m)], night));
        Assert.False(DividendSafety.PaysNone(0m, [new KeptDividend(new DateOnly(2025, 10, 3), 0.50m)], night));
        Assert.False(DividendSafety.PaysNone(null, [], night));
        Assert.False(DividendSafety.PaysNone(0.01m, [], night));

        using var store = UniversesStore();

        // S1 files a rate of nought and keeps no dividend; M1 files nought and paid its last a whole year before the night,
        // in 2025, the whole year before the night's; M2 files nought and paid two of 0.25 inside the year; M3 stores
        // neither.
        store.Execute(
            "INSERT INTO dividend_reading (ticker, fetched_at, forward_rate, last_ex_date, by_year) VALUES " +
            "('S1', '2026-10-02T23:00:00Z', '0', NULL, '{}'), ('M1', '2026-10-02T23:00:00Z', '0', NULL, '{}'), ('M2', '2026-10-02T23:00:00Z', '0', NULL, '{}');");
        store.Execute(
            "INSERT INTO dividend_event (ticker, ex_date, amount, source, run_id) VALUES " +
            "('M1', '2025-10-02', '0.50', 'history', 'constructed'), ('M2', '2025-10-03', '0.25', 'history', 'constructed'), ('M2', '2025-12-10', '0.25', 'history', 'constructed');");

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

        Assert.Equal([.. dividend.Select(_ => SinglePageApp.PaysNone)], dividend.Select(label => none[label]));
        Assert.Equal([SinglePageApp.PaysNone, "0.50 in 2025, 1 payment", SinglePageApp.PaysNone, SinglePageApp.PaysNone], dividend.Select(label => stopped[label]));
        Assert.Equal(
            ["1.00% on the last year's payments of 0.50", "0.50 in 2025, 2 payments", "not stored (degraded)", "not stored (degraded)"],
            dividend.Select(label => payer[label]));
        Assert.Equal([.. dividend.Select(_ => "not stored (degraded)")], dividend.Select(label => nothing[label]));

        // The report's yield tile says a company paying none pays none, on S1's own page, and one storing no rate that none
        // is stored, on M3's.
        Assert.Contains("It pays none: its fetch files a dividend rate of nought and none is kept in the year.", WebUtility.HtmlDecode(await client.GetStringAsync("/screens/name/S1")), StringComparison.Ordinal);
        Assert.Contains("No dividend is stored for it.", WebUtility.HtmlDecode(await client.GetStringAsync("/screens/name/M3")), StringComparison.Ordinal);
        Assert.DoesNotContain("It pays none", WebUtility.HtmlDecode(await client.GetStringAsync("/screens/name/M3")), StringComparison.Ordinal);
    }
}
