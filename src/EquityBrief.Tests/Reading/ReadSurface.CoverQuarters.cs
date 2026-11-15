using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;

namespace EquityBrief.Tests.Reading;

// read-surface, 23.10's second part: the stock page says, beside its member readings' interest cover, when the cover reads
// older quarters than the profit check, each quarter read as the rules read it from its filing and then its results
// release: the newest quarter read from its release while its filing is not in, or from a filing stating no interest, and
// nothing where the two read the same newest quarter. Worked by hand over a constructed store.
// see: The rules read each quarter from its filing and then its release from the moment the SEC made it public, and the cover from the four newest quarters stating interest
public partial class ReadSurface
{
    const string CoverNight = "2026-10-30";

    // Each member's filer, a number as the SEC's archive addresses one, which every reader of the archive refuses otherwise.
    static string CikOf(string ticker) => ticker switch { "CV" => "0000000101", "CN" => "0000000102", _ => "0000000103" };

    // A member's reading on the night under the S&P 400, its filer and sector, and the five quarters its fetch labels.
    static void CoverMember(TemporaryStore store, string ticker)
    {
        store.Execute(
            "INSERT INTO member_reading (index_code, session_date, ticker, close, profit, coverage) VALUES " +
            $"('MID', '{CoverNight}', '{ticker}', '50', 1, 1);");
        store.Execute(
            "INSERT INTO company (ticker, fetched_at, cik, sector, industry_group, industry, sub_industry) VALUES " +
            $"('{ticker}', '2026-08-01T00:00:00Z', '{CikOf(ticker)}', 'Industrials', NULL, NULL, NULL);");

        foreach (var end in new[] { "2025-09-30", "2025-12-31", "2026-03-31", "2026-06-30", "2026-09-30" })
        {
            store.Execute(
                "INSERT INTO reported_quarter (ticker, fetched_at, session_date, period_end, filing_date) VALUES " +
                $"('{ticker}', '{CoverNight}T23:50:00Z', '{CoverNight}', '{end}', NULL);");
        }
    }

    // A quarter's filing under the member's filer: revenue 100 million, operating income 20 million and net income 10
    // million, and its interest where given.
    static void CoverFiling(TemporaryStore store, string ticker, string end, string filed, decimal? interest)
    {
        var start = DateOnly.ParseExact(end, "yyyy-MM-dd", CultureInfo.InvariantCulture).AddDays(1).AddMonths(-3).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var facts = new List<(string Concept, decimal Value)> { ("Revenues", 100_000_000m), ("OperatingIncomeLoss", 20_000_000m), ("NetIncomeLoss", 10_000_000m) };

        if (interest is { } stated)
        {
            facts.Add(("InterestExpense", stated));
        }

        foreach (var (concept, value) in facts)
        {
            store.Execute(FormattableString.Invariant(
                $"INSERT INTO filed_fact (cik, concept, period_start, period_end, dollars, filed, form, accession, run_id) VALUES ('{CikOf(ticker)}', '{concept}', '{start}', '{end}', '{value}', '{filed}', '10-Q', '{CikOf(ticker)}-{end}', 'constructed');"));
        }
    }

    [Fact]
    public async Task TheStockPageSaysWhenTheCoverReadsOlderQuartersThanTheProfitCheck()
    {
        using var store = new TemporaryStore().Migrated();

        // CV's four quarters to 2026-06-30 filed with 6 million of interest each, and its quarter to 2026-09-30 reported in a
        // release the SEC accepted at noon on 2026-10-29, before that night's late slot, its filing not in: the profit check
        // reads the release's quarter and the cover the four filed, to 2026-06-30. CN's newest quarter is filed stating no
        // interest, so its cover reads the same four. CF's five are all filed with their interest, and the two read the same
        // newest quarter.
        foreach (var ticker in new[] { "CV", "CN", "CF" })
        {
            CoverMember(store, ticker);

            foreach (var (end, filed) in new[] { ("2025-09-30", "2025-11-05"), ("2025-12-31", "2026-02-10"), ("2026-03-31", "2026-05-06"), ("2026-06-30", "2026-08-05") })
            {
                CoverFiling(store, ticker, end, filed, 6_000_000m);
            }
        }

        CoverFiling(store, "CN", "2026-09-30", "2026-10-28", null);
        CoverFiling(store, "CF", "2026-09-30", "2026-10-28", 6_000_000m);

        var release = EquityBrief.Core.Releases.ResultsReleases.Compressed(
            "Condensed consolidated statements of income (in millions) Three months ended September 30, 2026 2025 " +
            "Total revenues 130 100 Operating income 26 20 Net income 13 10");

        using (var connection = store.Open())
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "INSERT INTO release_document (accession, cik, form, filed, accepted_at, exhibit, url, text_gzip, text_sha256, text_length, run_id) " +
                "VALUES ('0000000101-26-000001', '0000000101', '8-K', '2026-10-29', '2026-10-29T12:00:00Z', 'ex99.htm', 'https://www.sec.gov/Archives/edgar/data/1/ex99.htm', $text, $sha, $length, 'constructed');";
            command.Parameters.AddWithValue("$text", release.Gzip);
            command.Parameters.AddWithValue("$sha", release.Sha256);
            command.Parameters.AddWithValue("$length", release.Length);
            command.ExecuteNonQuery();
        }

        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        async Task<string> CoverageOf(string ticker)
        {
            var page = WebUtility.HtmlDecode(await client.GetStringAsync($"/screens/name/{ticker}"));
            var card = Assert.Single(Blocks(page, "<details class=\"rule-part\" id=\"member\" data-card=\"member\">.*?</details>"));

            return Regex.Match(card, "<tr data-reading=\"coverage\"><td>[^<]*</td>(<td[^>]*>.*?</td>)</tr>", RegexOptions.Singleline) is { Success: true } found
                ? found.Groups[1].Value
                : throw new InvalidOperationException($"no coverage row on {ticker}'s card");
        }

        var cv = await CoverageOf("CV");

        Assert.Contains("<small class=\"cover-to\" data-cover-to=\"2026-06-30\">Interest cover read from the quarters to 2026-06-30; the newest quarter's filing is not in yet.</small>", cv, StringComparison.Ordinal);
        Assert.Contains("Interest cover read from the quarters to 2026-06-30; the newest quarter's filing states no interest.", await CoverageOf("CN"), StringComparison.Ordinal);
        Assert.DoesNotContain("data-cover-to", await CoverageOf("CF"), StringComparison.Ordinal);
    }
}
