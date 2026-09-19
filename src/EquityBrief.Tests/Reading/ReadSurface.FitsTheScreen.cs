using System.Globalization;
using System.Text.RegularExpressions;
using EquityBrief.Tests.Checks;
using EquityBrief.Web.App;

namespace EquityBrief.Tests.Reading;

// read-surface, the 5.8 correction: a screen fits the screen it is read on. The column is as wide
// as the widest picture a screen draws and no wider, and it is the screen's own width below that;
// anything that cannot be narrowed, which is every table and every picture drawn at a size, is
// read in a box of its own that scrolls rather than pushing the page sideways.
// see: A screen is read at the width of the screen it is read on
public partial class ReadSurface
{
    // One declaration of one rule, read off the stylesheet the app and the file both carry.
    // It refuses rather than defaulting, because a number this could not find would be
    // measured as zero and a column of zero holds nothing while a width of zero holds anything.
    static double Declared(string selector, string declaration)
    {
        var rule = Regex.Match(Stylesheet.Css, @"(?m)^\s*" + Regex.Escape(selector) + @"\s*\{([^}]*)\}");

        Assert.True(rule.Success, $"The stylesheet has no rule for `{selector}`.");

        var value = Regex.Match(rule.Groups[1].Value, declaration);

        Assert.True(
            value.Success,
            $"`{selector}` no longer declares what `{declaration}` reads, and a width the column is " +
            "measured against cannot be assumed once the stylesheet stops stating it.");

        return double.Parse(value.Groups[1].Value, CultureInfo.InvariantCulture);
    }

    // Every picture in one figure that is drawn at a size of its own, with the width it is drawn
    // at and the width it is drawn in. A picture drawn at the width of what holds it carries a
    // share rather than a number and takes no room of its own.
    static IReadOnlyList<(double Drawn, double Own)> Pictures(string figure) =>
    [
        .. Regex.Matches(figure, "<svg[^>]*>")
            .Select(svg => (
                Width: Regex.Match(svg.Value, "width=\"(\\d+(?:\\.\\d+)?)\""),
                Box: Regex.Match(svg.Value, "viewBox=\"0 0 (\\d+(?:\\.\\d+)?) ")))
            .Where(picture => picture.Width.Success && picture.Box.Success)
            .Select(picture => (
                double.Parse(picture.Width.Groups[1].Value, CultureInfo.InvariantCulture),
                double.Parse(picture.Box.Groups[1].Value, CultureInfo.InvariantCulture))),
    ];

    [Fact]
    public async Task TheColumnIsAsWideAsTheWidestPictureAScreenDrawsAndIsTheScreensOwnWidthBelowThat()
    {
        using var store = await FixtureExpectations.WithListings();
        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        var name = FiredNamesOn(store, NightIn(store))[0];
        var page = await client.GetStringAsync($"/screens/name/{name}");

        // The widest figure the screens draw: the chart and, on its price scale, the profile
        // beside it, which are one row and are drawn at one size so a price is at one height
        // in both.
        var rows = Regex
            .Matches(page, "<div class=\"fig row-fig\">(.*?)</div>", RegexOptions.Singleline)
            .Select(row => Pictures(row.Groups[1].Value))
            .Where(pictures => pictures.Count > 0)
            .ToArray();

        Assert.Single(rows);
        Assert.Equal(2, rows[0].Count);

        // Both at their own size, neither pre-scaled, which is what the ceiling is sized to
        // hold and what lets the pair be scaled down together below it.
        Assert.All(rows[0], picture => Assert.Equal(picture.Own, picture.Drawn));

        var widest = rows[0].Sum(picture => picture.Drawn);
        var padding = Declared(".card", @"padding\s*:\s*\d+px\s+(\d+)px");
        var gutter = Declared(":root", @"--gutter\s*:\s*(\d+)px");

        // The column is that figure with the card's own padding and the page's gutter around
        // it: no wider, because a line of prose past what an eye follows is read twice, and no
        // narrower, because the figure would then be read through a scroll box on every screen.
        Assert.Equal(widest + (2 * padding) + (2 * gutter), Declared(":root", @"--column\s*:\s*(\d+)px"));

        // And below that width the column is the screen's own, so a narrower screen is filled
        // rather than shown a page laid out for a wider one.
        Assert.Matches(@"(?m)^\.wrap\{width:100%;max-width:var\(--column\)", Stylesheet.Css);

        // What keeps the pair one picture as the column narrows: each keeps its own ratio and
        // the row anchors them at the top. Read as the rule rather than as a drawing, because
        // the suite has no browser to lay one out; held in boxes of a fixed height instead, a
        // price sat 14 points higher in the profile than in the chart at a column of 974.
        Assert.Matches(@"(?m)^\.fig svg\{display:block;max-width:100%;height:auto\}", Stylesheet.Css);
        Assert.Matches(@"(?m)^\.row-fig\{display:flex;align-items:flex-start", Stylesheet.Css);
        Assert.Contains("<meta name=\"viewport\" content=\"width=device-width", new SinglePageApp().Shell("EquityBrief"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task EveryTableAScreenDrawsIsReadInABoxOfItsOwnRatherThanPushingThePageSideways()
    {
        using var store = await FixtureExpectations.WithListings();
        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        var name = FiredNamesOn(store, NightIn(store))[0];

        var screens = new[]
        {
            ("tonight", await client.GetStringAsync("/screens/tonight")),
            ("universe", await client.GetStringAsync("/screens/universe")),
            ("run", await client.GetStringAsync("/screens/run")),
            ("name", await client.GetStringAsync($"/screens/name/{name}")),
            ("the exported report", await client.GetStringAsync(EquityBrief.Web.App.ReportExporter.Route + name)),
        };

        var tables = 0;

        foreach (var (screen, markup) in screens)
        {
            var drawn = Regex.Matches(markup, "<table[ >]").Count;

            Assert.True(drawn > 0, $"The {screen} screen draws no table, and this is what reads them.");

            tables += drawn;

            // Each one opened by the box that scrolls it, named where it is not, because a
            // count says a table is bare and not which one.
            var bare = Regex
                .Matches(markup, "(.{0,24})<table(?: class=\"([a-z-]+)\")?[ >]")
                .Where(table => !table.Groups[1].Value.EndsWith("<div class=\"tbl-wrap\">", StringComparison.Ordinal))
                .Select(table => table.Groups[2].Value)
                .ToArray();

            Assert.True(bare.Length == 0, $"On {screen}, {string.Join(", ", bare)} is drawn outside a box of its own.");
        }

        // The population the loop above ran over, so a screen that stops drawing its tables is
        // not a screen on which every table is boxed: 26 over the five surfaces.
        Assert.Equal(26, tables);

        // What makes the box a box. Without this the wrapper is a div and every table pushes
        // the page as it did before.
        Assert.Matches(@"(?m)^\.tbl-wrap\{overflow-x:auto\}", Stylesheet.Css);
    }
}
