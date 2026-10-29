using System.Net;

namespace EquityBrief.Tests.Checks;

// fixture-expectations, the 22.14 correction of 2026-10-29: the picks business first's open trades held back on the
// fixture's night, none on any index, since the night is the first its step runs over and no trade of its stands before
// it; recounted from the three pick tables and read off the Run page under each index.
// see: Business first stays in the night unseen until the next change that moves the S&P 400's and 600's pins, and the Run page counts the picks its open trades hold back each night an index
public partial class FixtureExpectations
{
    [Fact]
    public async Task TheFixturesNightHasNoPickHeldBackByBusinessFirstOnAnyIndexAndTheRunPageSaysSo()
    {
        var expected = Expected("business-first-held-back");

        Assert.Equal("derived", expected.GetProperty("derivation").GetString());
        Assert.Equal(["listing"], expected.GetProperty("tables").EnumerateArray().Select(table => table.GetString()!));

        var night = expected.GetProperty("night").GetString()!;
        var counts = expected.GetProperty("heldBack").EnumerateObject()
            .ToDictionary(index => index.Name, index => index.Value.GetInt32(), StringComparer.Ordinal);

        Assert.Equal(["GSPC", "MID", "SML"], counts.Keys.Order(StringComparer.Ordinal));

        var (store, code, _, _) = await FixtureReplay.NightAsync();
        using var held = store;

        Assert.Equal(0, code);

        // The derivation's ground: the night read its members and drew its list, and no trade of business first's stands
        // before it, so no row of any list can name one.
        Assert.Equal(["1"], Query(store, $"SELECT COUNT(*) > 0 FROM listing WHERE session_date = '{night}';"));
        Assert.Equal(["0"], Query(store, $"SELECT COUNT(*) FROM index_family_trade WHERE family = 'fundamentals' AND session_date < '{night}';"));

        foreach (var (index, count) in counts)
        {
            var large = index == "GSPC" ? $"(SELECT COUNT(*) FROM family_pick WHERE session_date = '{night}' AND state = 'open trade' AND held_family = 'fundamentals')" : "0";

            Assert.Equal(
                [count.ToString(System.Globalization.CultureInfo.InvariantCulture)],
                Query(store, $"SELECT {large} + (SELECT COUNT(*) FROM index_family_pick WHERE index_code = '{index}' AND session_date = '{night}' AND state = 'open trade' AND held_family = 'fundamentals' AND family <> 'fundamentals') + (SELECT COUNT(*) FROM dip_pick WHERE index_code = '{index}' AND session_date = '{night}' AND state = 'open trade' AND held_family = 'fundamentals');"));
        }

        using var host = new Reading.ReadSurface.Host(store.Root);
        using var client = host.CreateClient();

        var line = $"<p class=\"held-by-business-first\" data-gspc=\"{counts["GSPC"]}\" data-mid=\"{counts["MID"]}\" data-sml=\"{counts["SML"]}\">Business first's open trades held back {counts["GSPC"]} picks on the S&P 500, {counts["MID"]} on the S&P 400 and {counts["SML"]} on the S&P 600 tonight.</p>";

        foreach (var route in new[] { $"/screens/run/{night}", $"/screens/run/{night}?universe=400", $"/screens/run/{night}?universe=600" })
        {
            Assert.Contains(line, WebUtility.HtmlDecode(await client.GetStringAsync(route)), StringComparison.Ordinal);
        }
    }
}
