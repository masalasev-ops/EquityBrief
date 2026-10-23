using System.Net;
using System.Text.RegularExpressions;

namespace EquityBrief.Tests.Reading;

// read-surface: a member's industry on the chart page's grid and its masthead is the GICS industry its newest company fetch
// files, on every index and whatever its membership row names, read over the universes' store.
// see: Every member is named and grouped by the GICS industry and sector its company fetch files
public partial class ReadSurface
{
    [Fact]
    public async Task AMemberIsNamedByTheIndustryItsNewestCompanyFetchFilesWhateverItsMembershipRowNames()
    {
        using var store = UniversesStore();

        // The S&P 400's and 600's rows name neither a sector nor an industry, as their funds' holdings files name neither.
        // M1's two company fetches file two industries, the newer read; the S&P 500's AAA and BBB rows name an industry in
        // the index snapshot's words, AAA's fetch filing another and BBB having no fetch; M3 has no fetch.
        store.Execute("UPDATE membership SET sector = NULL WHERE index_code <> 'GSPC';");
        store.Execute(
            "INSERT INTO company (ticker, fetched_at, sector, industry) VALUES " +
            "('M1', '2026-09-01T23:50:00.000Z', 'Energy', 'Energy Equipment & Services'), ('M1', '2026-10-01T23:50:00.000Z', 'Energy', 'Oil, Gas & Consumable Fuels'), " +
            "('AAA', '2026-10-01T23:50:00.000Z', 'Energy', 'Oil, Gas & Consumable Fuels');");
        store.Execute("UPDATE membership SET industry = 'Oil & Gas Integrated' WHERE ticker IN ('AAA', 'BBB');");

        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        async Task<(string Industry, string Mast)> Named(string ticker)
        {
            var page = WebUtility.HtmlDecode(await client.GetStringAsync($"/screens/chart/{ticker}"));
            var industry = Regex.Match(page, "<dt>Industry</dt><dd([^>]*)>([^<]*)</dd>");

            Assert.True(industry.Success, $"{ticker}'s chart page draws no industry.");

            return (
                industry.Groups[2].Value + (industry.Groups[1].Value.Contains("degraded", StringComparison.Ordinal) ? " (degraded)" : string.Empty),
                Regex.Match(page, "<a href=\"#/universe\\?sector=[^\"]+\">[^<]+</a>((?:, )[^<·]*)?").Groups[1].Value.TrimEnd());
        }

        // AAA by its fetch's industry and not its row's; BBB, whose row's words no page reads, not stored.
        Assert.Equal(("Oil, Gas & Consumable Fuels", ", Oil, Gas & Consumable Fuels"), await Named("M1"));
        Assert.Equal(("Oil, Gas & Consumable Fuels", ", Oil, Gas & Consumable Fuels"), await Named("AAA"));
        Assert.Equal(("not stored (degraded)", string.Empty), await Named("BBB"));
        Assert.Equal(("not stored (degraded)", string.Empty), await Named("M3"));
    }
}
