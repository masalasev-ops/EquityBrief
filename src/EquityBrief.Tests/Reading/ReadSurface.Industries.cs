using System.Net;
using System.Text.RegularExpressions;

namespace EquityBrief.Tests.Reading;

// read-surface, from the 15.1 correction of 2026-10-21: a member's industry on the chart page's grid and its masthead is
// the one its membership row names, and where the row names none the GICS industry its newest company fetch files, read
// over the universes' store.
// see: A member whose membership row names no industry is named and grouped by the GICS industry and sector its company fetch files
public partial class ReadSurface
{
    [Fact]
    public async Task AMemberIsNamedByTheIndustryItsMembershipRowNamesAndWhereItNamesNoneByItsNewestCompanyFetchs()
    {
        using var store = UniversesStore();

        // M1's two company fetches file two industries, the newer read; AAA's membership row names its industry, which
        // stands over its company fetch's; M3 has no fetch and its row names none.
        store.Execute(
            "INSERT INTO company (ticker, fetched_at, sector, industry) VALUES " +
            "('M1', '2026-09-01T23:50:00.000Z', 'Energy', 'Energy Equipment & Services'), ('M1', '2026-10-01T23:50:00.000Z', 'Energy', 'Oil, Gas & Consumable Fuels'), " +
            "('AAA', '2026-10-01T23:50:00.000Z', 'Energy', 'Oil, Gas & Consumable Fuels');");
        store.Execute("UPDATE membership SET industry = 'Oil & Gas Integrated' WHERE ticker = 'AAA';");

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

        Assert.Equal(("Oil, Gas & Consumable Fuels", ", Oil, Gas & Consumable Fuels"), await Named("M1"));
        Assert.Equal(("Oil & Gas Integrated", ", Oil & Gas Integrated"), await Named("AAA"));
        Assert.Equal(("not stored (degraded)", string.Empty), await Named("M3"));
    }
}
