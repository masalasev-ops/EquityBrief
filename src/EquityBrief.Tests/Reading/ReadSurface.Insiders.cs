using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using EquityBrief.Tests.Checks;
using EquityBrief.Tests.Harness;

namespace EquityBrief.Tests.Reading;

// read-surface, 19.13: the report's Insiders region and the chart page's Insiders group, read back off the replayed
// fixture's pages against the store: a store whose history falls short of the year the region reads drawing the line
// saying so on the report and each figure not stored on the chart page; and given the year's spans, Keysight's
// open-market sales of the year newest first with the planned ones marked, the totals over ninety days and the year and
// the line counting what neither counts, each worked from the stored rows by the suite's own query.
// see: Insiders' trades are read from their Form 4 filings, and only open-market purchases and sales are counted
public partial class ReadSurface
{
    // The rows 19.13 adds that this check reaches: section 15.9's region.
    internal static readonly string[] InsiderRows =
    [
        CheckReach.Key("15.9 Name", "Insiders"),
    ];

    const string InsidersCard = "<section class=\"card spined fund\" id=\"insiders\".*?</section>";

    [Fact]
    public async Task ANamesInsidersAreDrawnAfterItsAnalystsAsTheStoreHoldsThemAndSaySoWhereTheHistoryFallsShort()
    {
        using var store = await FixtureReplay.ReplayedAsync();
        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        // The fixture's history holds two quarters and the refresh's days, short of the year to the night: the report draws
        // the region after the analysts with the line saying so, and the chart page's group draws each figure not stored.
        var page = WebUtility.HtmlDecode(await client.GetStringAsync("/screens/name/KEYS"));
        string[] order = ["analysts", "insiders", "valuation"];

        Assert.Equal(order, Regex.Matches(page, "<section class=\"[^\"]*\" id=\"([^\"]+)\"").Select(match => match.Groups[1].Value).Where(order.Contains));
        Assert.Contains("<p class=\"degraded\" data-absent=\"insiders\">The store holds no insider history reaching this night, so no insider's trade is drawn.</p>", Regex.Match(page, InsidersCard, RegexOptions.Singleline).Value, StringComparison.Ordinal);

        var chart = WebUtility.HtmlDecode(await client.GetStringAsync("/screens/chart/KEYS"));
        var group = Regex.Match(chart, "<section class=\"grid-group\" data-group=\"insiders\">.*?</section>", RegexOptions.Singleline).Value;

        Assert.Equal(4, Regex.Matches(group, "<dd class=\"degraded\">not stored</dd>").Count);

        // Given the spans of the year before them, the region draws the year's rows: Keysight's open-market sales filed from
        // the year before the night's next session to the night, read off the store by the suite's own query, newest traded
        // first, each planned where its filing ticks the box.
        store.Execute("INSERT INTO insider_read (first_day, last_day, source, run_id, read_at, filings, stored) VALUES ('2025-07-01', '2026-03-31', 'data-set', 'constructed', '2026-10-16T00:00:00Z', 0, 0);");

        const string Year = "filed >= '2025-09-09' AND filed <= '2026-09-08' AND cik = '0001601046'";
        var sales = Rows(store, $"SELECT traded, filed, shares, price, COALESCE(planned, 0) FROM insider_trade WHERE {Year} AND code = 'S' ORDER BY traded DESC, filed DESC, accession, sequence;");
        var drawn = WebUtility.HtmlDecode(await client.GetStringAsync("/screens/name/KEYS"));
        var card = Regex.Match(drawn, InsidersCard, RegexOptions.Singleline).Value;

        Assert.True(sales.Count >= 6, $"Keysight's year holds {sales.Count} open-market sale(s), expected at least 6.");
        Assert.Equal(
            sales.Select(row => $"sale|{row[4]}|{row[0]}|{row[1]}|{row[2]}|{row[3]}"),
            Regex.Matches(card, "<tr class=\"loss\" data-kind=\"(sale)\" data-planned=\"([01])\" data-traded=\"([^\"]+)\" data-filed=\"([^\"]+)\">.*?data-shares=\"([^\"]+)\".*?data-price=\"([^\"]+)\"", RegexOptions.Singleline)
                .Select(match => string.Join('|', Enumerable.Range(1, 6).Select(at => match.Groups[at].Value))));
        Assert.Equal(sales.Count(row => row[4] == "1"), Regex.Matches(card, "<span class=\"planned\">planned</span>").Count);
        Assert.DoesNotContain("data-kind=\"purchase\"", card, StringComparison.Ordinal);

        // The totals: the ninety days' sales filed from 2026-06-11, ninety days before the next session, and the year's,
        // each the shares times the price summed, and the sellers by their owners, none bought.
        decimal Sold(string where) => Rows(store, $"SELECT shares, price FROM insider_trade WHERE {where} AND code = 'S';")
            .Sum(row => decimal.Parse(row[0], CultureInfo.InvariantCulture) * decimal.Parse(row[1], CultureInfo.InvariantCulture));

        var ninety = Sold(Year + " AND filed >= '2026-06-11'");
        var year = Sold(Year);
        var sellers = Rows(store, $"SELECT DISTINCT owner_ciks FROM insider_trade WHERE {Year} AND code = 'S';").Count;

        Assert.Equal(2451909.24m, ninety);
        Assert.Contains(FormattableString.Invariant($"<p data-span=\"ninety\" data-bought=\"0\" data-sold=\"{ninety}\" data-net=\"{-ninety}\""), card, StringComparison.Ordinal);
        Assert.Contains(FormattableString.Invariant($"<p data-span=\"year\" data-bought=\"0\" data-sold=\"{year}\" data-net=\"{-year}\" data-buyers=\"0\" data-sellers=\"{sellers}\">"), card, StringComparison.Ordinal);

        // The line counting what neither total counts: the year's awards, exercises and conversions, withholding and the rest.
        int Coded(string codes) => Rows(store, $"SELECT accession FROM insider_trade WHERE {Year} AND code IN ({codes});").Count;
        int Rest() => Rows(store, $"SELECT accession FROM insider_trade WHERE {Year} AND code NOT IN ('P', 'S', 'A', 'M', 'C', 'X', 'O', 'F');").Count;

        Assert.Contains(
            FormattableString.Invariant($"<p class=\"insider-left-out\" data-awards=\"{Coded("'A'")}\" data-exercises=\"{Coded("'M', 'C', 'X', 'O'")}\" data-withholding=\"{Coded("'F'")}\" data-others=\"{Rest()}\">"),
            card,
            StringComparison.Ordinal);

        // The chart page's group from the same view: the nets over ninety days and the year in the loss's hue, no buyer and
        // no purchase in the year.
        var figures = Regex.Match(WebUtility.HtmlDecode(await client.GetStringAsync("/screens/chart/KEYS")), "<section class=\"grid-group\" data-group=\"insiders\">.*?</section>", RegexOptions.Singleline).Value;

        Assert.Equal(
            [FormattableString.Invariant($"{-ninety}"), FormattableString.Invariant($"{-year}"), "0"],
            Regex.Matches(figures, "<dd[^>]* data-value=\"([^\"]+)\">").Select(match => match.Groups[1].Value));
        Assert.Equal(2, Regex.Matches(figures, "<dd class=\"loss\" data-value=").Count);
        Assert.Contains("<dt>Newest purchase</dt><dd>none in the year</dd>", figures, StringComparison.Ordinal);
    }
}
