using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;

namespace EquityBrief.Tests.Checks;

// fixture-expectations, 19.5: the universe's sector tiles over the fixture's whole night, read off the page it draws. The
// night's company rows file three members under Information Technology and one under Communication Services, so the page
// draws two tiles; the families pass no stock, so no tile counts one on tonight's lists; and no fund's close is stored, so
// each tile reads its fund's return as not read.
// see: Every page names a member's sector by the GICS sector its company fetch files
public partial class FixtureExpectations
{
    [Fact]
    public async Task TheFixturesUniverseDrawsATileAGicsSectorItsCompaniesFileWithNoneOnTonightsLists()
    {
        var expected = Expected("universe-screen");

        Assert.Equal("derived", expected.GetProperty("derivation").GetString());
        Assert.Equal(["company"], expected.GetProperty("tables").EnumerateArray().Select(table => table.GetString()!));

        var night = expected.GetProperty("universeNight").GetString()!;
        var tiles = expected.GetProperty("tiles").EnumerateArray()
            .Select(tile => (Sector: tile.GetProperty("sector").GetString()!, Members: tile.GetProperty("members").GetInt32(), Listed: tile.GetProperty("listed").GetInt32(), Fund: tile.GetProperty("fund").GetString()!))
            .ToArray();

        var (store, code, _, _) = await FixtureReplay.NightAsync();
        using var held = store;

        Assert.Equal(0, code);

        // The derivation's ground: each member's newest company row files the sector the expectation names, no family's
        // list kept a stock on the night, and no close of either fund is stored.
        var filed = Query(store, "SELECT c.ticker || '|' || COALESCE(c.sector, '') FROM company c WHERE c.rowid = (SELECT n.rowid FROM company n WHERE n.ticker = c.ticker ORDER BY n.fetched_at DESC, n.rowid DESC LIMIT 1) ORDER BY c.ticker;")
            .Select(row => row.Split('|'))
            .ToArray();

        Assert.Equal(["AAPL", "KEYS", "MSFT", "NFLX"], filed.Select(row => row[0]));
        Assert.Equal(tiles.Select(tile => (tile.Sector, tile.Members)), filed.GroupBy(row => row[1]).OrderBy(group => group.Key, StringComparer.Ordinal).Select(group => (group.Key, group.Count())));
        Assert.Equal(["0"], Query(store, $"SELECT COUNT(*) FROM family_pick WHERE session_date = '{night}';"));
        Assert.Equal(["0"], Query(store, "SELECT COUNT(*) FROM market_bar WHERE series IN ('XLC', 'XLK');"));

        using var host = new Reading.ReadSurface.Host(store.Root);
        using var client = host.CreateClient();

        var page = WebUtility.HtmlDecode(await client.GetStringAsync("/screens/universe"));
        var drawn = Regex.Matches(page, "<a class=\"sector-tile\" data-sector=\"([^\"]+)\" data-names=\"(\\d+)\" data-uptrend=\"(\\d+)\" data-listed=\"(\\d+)\" data-fund=\"([^\"]+)\" data-fund-return=\"([^\"]+)\"")
            .Select(match => (Sector: match.Groups[1].Value, Members: int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture), Uptrend: match.Groups[3].Value, Listed: int.Parse(match.Groups[4].Value, CultureInfo.InvariantCulture), Fund: match.Groups[5].Value, Return: match.Groups[6].Value))
            .ToArray();

        Assert.Contains($"data-universe=\"500\" data-names=\"4\"", page, StringComparison.Ordinal);
        Assert.Equal(tiles, drawn.Select(tile => (tile.Sector, tile.Members, tile.Listed, tile.Fund)));
        Assert.All(drawn, tile => Assert.Equal(expected.GetProperty("fundReturn").GetString(), tile.Return));
        Assert.Contains($"data-not-filed=\"{expected.GetProperty("notFiled").GetInt32()}\"", page, StringComparison.Ordinal);

        // Each tile's uptrend count off the stored ladder rows, which no expectation states for the night.
        var uptrend = Query(store, $"SELECT l.ticker FROM ladder l WHERE l.as_of = (SELECT MAX(a.as_of) FROM ladder a WHERE a.ticker = l.ticker AND a.as_of <= '{night}') AND l.trend_state = 'uptrend';")
            .ToHashSet(StringComparer.Ordinal);

        foreach (var tile in drawn)
        {
            var members = filed.Where(row => row[1] == tile.Sector).Select(row => row[0]);

            Assert.Equal(members.Count(uptrend.Contains).ToString(CultureInfo.InvariantCulture), tile.Uptrend);
        }
    }
}
