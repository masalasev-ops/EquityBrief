using System.Globalization;
using System.Text.RegularExpressions;
using EquityBrief.Api.Reading;
using EquityBrief.Tests.Checks;
using EquityBrief.Tests.Harness;
using EquityBrief.Web.App;
using EquityBrief.Web.Marks;

namespace EquityBrief.Tests.Reading;

// read-surface, 5.8: the universe table draws each name's year line beside its ticker, shown while the
// pointer is over the name's cell or it has focus, as the peers table does, read back off the page
// against the store.
public partial class ReadSurface
{
    static IReadOnlyList<string> NameCellsOf(string table) =>
        [.. Regex.Matches(table, "<td class=\"c-nm\">(.*?)</td>", RegexOptions.Singleline).Select(cell => cell.Groups[1].Value)];

    [Fact]
    public void TheUniverseTableDrawsEachNamesYearInItsOwnNameCellAndSaysSoWhereItHoldsTooFew()
    {
        var year = new PeerYear(new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 5), [100m, 104m, 102m, 108m, 110m]);
        UniverseCell[] rows =
        [
            new("ZZAA", "Energy", 110m, "uptrend", 96m, 114m, 9.33, 2.67, 2.67, Name: "Zed Ay", Year: year),
            new("ZZBB", "Energy", 20m, "range", null, null, null, null, null, Name: "Zed Bee"),
        ];

        var cells = NameCellsOf(new MarkRenderer().UniverseTable(rows));

        Assert.Equal(2, cells.Count);

        // The ticker still opens the name's page with the company beneath it, and the year follows in
        // the same cell, over the name's closes with its nearest bands across it and its last close marked.
        Assert.StartsWith("<a class=\"tk\" href=\"#/name/ZZAA\">ZZAA</a><span class=\"co\">Zed Ay</span><span class=\"peer-pop\" role=\"tooltip\"><svg", cells[0], StringComparison.Ordinal);
        Assert.Contains("class=\"year-line\" data-ticker=\"ZZAA\" data-closes=\"5\"", cells[0], StringComparison.Ordinal);
        Assert.Contains("data-support=\"96\"", cells[0], StringComparison.Ordinal);
        Assert.Contains("data-resistance=\"114\"", cells[0], StringComparison.Ordinal);
        Assert.Contains("<circle class=\"m-last\" data-close=\"110\"", cells[0], StringComparison.Ordinal);

        // A name no closes were read for draws no picture and says how many it holds.
        Assert.Contains("<span class=\"peer-pop\" role=\"tooltip\"><span class=\"degraded year-line-none\" data-ticker=\"ZZBB\" data-closes=\"0\">ZZBB holds 0 stored close(s), too few to draw its year.</span></span>", cells[1], StringComparison.Ordinal);
        Assert.DoesNotContain("<svg", cells[1], StringComparison.Ordinal);

        // Shown while the pointer is over the name's cell or it has focus, and placed beside it by the shell.
        Assert.Contains(".universe-table td.c-nm:hover .peer-pop,.universe-table td.c-nm:focus-within .peer-pop{display:block}", Stylesheet.Css, StringComparison.Ordinal);
        Assert.Contains("event.target.closest('.peers-table td.peer, .universe-table td.c-nm, th.tipped')", new SinglePageApp().Shell("EquityBrief"), StringComparison.Ordinal);
    }

    [Fact]
    public void EachRowOfAPageCarriesItsOwnNamesClosesAndNoOther()
    {
        var on = new DateOnly(2026, 9, 25);
        UniverseCell[] page =
        [
            new("ZZBB", "Energy", 20m, "range", null, null, null, null, null),
            new("ZZAA", "Energy", 10m, "uptrend", null, null, null, null, null),
        ];
        CloseRow[] closes = [new("ZZAA", on.AddDays(-1), 9.5m), new("ZZAA", on, 10m), new("ZZCC", on, 40m)];

        var drawn = UniverseScreen.WithYears(page, closes);

        // The page's rows in the order they arrived, the name the closes hold nothing for carrying no year.
        Assert.Equal(["ZZBB", "ZZAA"], drawn.Select(cell => cell.Ticker));
        Assert.Null(drawn[0].Year);
        Assert.Equal(on.AddDays(-1), drawn[1].Year!.From);
        Assert.Equal(on, drawn[1].Year!.To);
        Assert.Equal([9.5m, 10m], drawn[1].Year!.Closes);
    }

    [Fact]
    public async Task EachRowTheUniverseScreenDrawsCarriesItsOwnYearFromTheStore()
    {
        using var store = await FixtureExpectations.WithListings();
        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        var night = DateOnly.ParseExact(NightIn(store), "yyyy-MM-dd", CultureInfo.InvariantCulture);
        var members = (await Api(store).UniverseAsync(Index, night)).ToDictionary(row => row.Ticker, StringComparer.Ordinal);
        var whole = await client.GetStringAsync("/screens/universe");
        var sector = Regex.Match(whole, "<tr data-ticker=\"[^\"]+\" data-sector=\"([^\"]+)\"").Groups[1].Value;
        var drawnRows = new List<int>();
        var read = 0;

        // The whole table, and one sector of it, which draws fewer rows than the index holds.
        foreach (var screen in new[] { whole, await client.GetStringAsync($"/screens/universe?sector={Uri.EscapeDataString(System.Net.WebUtility.HtmlDecode(sector))}") })
        {
            var rows = Regex.Matches(screen, "<tr data-ticker=\"([^\"]+)\" data-sector=.*?</tr>", RegexOptions.Singleline);

            drawnRows.Add(rows.Count);

            // One year for each row the page draws, and none anywhere else on it.
            Assert.Equal(rows.Count, Regex.Matches(screen, "<span class=\"peer-pop\" role=\"tooltip\">").Count);

            foreach (Match row in rows)
            {
                var ticker = row.Groups[1].Value;
                var cell = Assert.Single(NameCellsOf(row.Value));
                var closes = Rows(store, $"SELECT close FROM bar WHERE ticker = '{ticker}' ORDER BY session_date;");

                // Every close the store holds for the name, the newest marked, and the name's own nearest
                // bands across it.
                Assert.True(closes.Count >= MarkRenderer.FewestBars, $"{ticker} holds {closes.Count} stored close(s)");
                Assert.Contains($"class=\"year-line\" data-ticker=\"{ticker}\" data-closes=\"{closes.Count}\"", cell, StringComparison.Ordinal);
                Assert.Contains($"<circle class=\"m-last\" data-close=\"{decimal.Parse(closes[^1][0], CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture)}\"", cell, StringComparison.Ordinal);

                foreach (var (band, named) in new[] { (members[ticker].NearestSupport, "support"), (members[ticker].NearestResistance, "resistance") })
                {
                    if (band is { } price)
                    {
                        Assert.Contains($"data-{named}=\"{price.ToString(CultureInfo.InvariantCulture)}\"", cell, StringComparison.Ordinal);
                    }
                    else
                    {
                        Assert.DoesNotContain($"data-{named}=", cell, StringComparison.Ordinal);
                    }
                }

                read++;
            }
        }

        // Every member the index holds, and fewer on the sector's page.
        Assert.Equal(FixtureExpectation.CurrentMembers.Length, drawnRows[0]);
        Assert.True(drawnRows[1] > 0 && drawnRows[1] < drawnRows[0], $"the sector drew {drawnRows[1]} of the table's {drawnRows[0]} rows");
        Assert.Equal(drawnRows.Sum(), read);
    }
}
