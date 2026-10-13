using System.Net;
using EquityBrief.Api.Reading;
using EquityBrief.Tests.Checks;
using EquityBrief.Tests.Harness;

namespace EquityBrief.Tests.Reading;

// read-surface, 19.2: every page names a member's sector by the GICS sector its newest company fetch files and never by
// the index snapshot's own scheme, a member filing none drawn as sector not filed and counted in no sector.
// see: Every page names a member's sector by the GICS sector its company fetch files
public partial class ReadSurface
{
    // The fixture's replay files no company fetch for most members, so a test reading sectors writes each current member
    // one filing its GICS sector, the snapshot's word for it mapped to the GICS name where the two differ, so a page reading
    // the snapshot's word in its place fails.
    internal static void FiledSectors(TemporaryStore store) =>
        store.Execute(
            "INSERT INTO company (ticker, fetched_at, sector) " +
            "SELECT ticker, '2026-01-01T00:00:00Z', CASE MAX(sector) WHEN 'Technology' THEN 'Information Technology' WHEN 'Financial Services' THEN 'Financials' " +
            "WHEN 'Healthcare' THEN 'Health Care' WHEN 'Consumer Cyclical' THEN 'Consumer Discretionary' WHEN 'Consumer Defensive' THEN 'Consumer Staples' " +
            "WHEN 'Basic Materials' THEN 'Materials' ELSE MAX(sector) END FROM membership WHERE sector IS NOT NULL GROUP BY ticker;");

    // A member's sector as its newest company fetch files it.
    static string FiledSectorOf(TemporaryStore store, string ticker) =>
        Rows(store, $"SELECT sector FROM company WHERE ticker = '{ticker}' ORDER BY fetched_at DESC LIMIT 1;").Single()[0];

    [Fact]
    public async Task EveryPageNamesAMembersSectorByTheGicsSectorItsCompanyFetchFiles()
    {
        using var store = await FixtureExpectations.WithListings();

        FiledSectors(store);

        // AAPL's snapshot says Technology and its fetch files Information Technology; a newer fetch filing another sector
        // is the one read. MSFT files none in its newest fetch, so it stands in no sector.
        store.Execute(
            "INSERT INTO company (ticker, fetched_at, sector) VALUES ('AAPL', '2026-02-01T00:00:00Z', 'Communication Services'), ('MSFT', '2026-02-01T00:00:00Z', NULL);");

        Assert.Equal("Technology", Rows(store, "SELECT sector FROM membership WHERE ticker = 'AAPL' AND \"left\" IS NULL;").Single()[0]);

        var universe = await Api(store).UniverseAsync(Index);

        Assert.Equal("Communication Services", universe.Single(row => row.Ticker == "AAPL").Sector);
        Assert.Null(universe.Single(row => row.Ticker == "MSFT").Sector);

        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        // On the Universe page MSFT is drawn as sector not filed, in a line of its own and in no sector's.
        var page = WebUtility.HtmlDecode(await client.GetStringAsync("/screens/universe"));

        Assert.Contains("data-ticker=\"MSFT\" data-sector=\"sector not filed\"", page, StringComparison.Ordinal);
        Assert.Contains("data-ticker=\"AAPL\" data-sector=\"Communication Services\"", page, StringComparison.Ordinal);
        Assert.Contains($"<div class=\"sector\" data-sector=\"{UniverseScreen.SectorNotOnFile}\" data-names=\"1\"", page, StringComparison.Ordinal);
        Assert.DoesNotContain("data-sector=\"Technology\"", page, StringComparison.Ordinal);
    }
}
