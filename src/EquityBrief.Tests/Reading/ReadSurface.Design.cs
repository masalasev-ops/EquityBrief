using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using EquityBrief.Core.Research;
using EquityBrief.Core.Shortlist;
using EquityBrief.Tests.Checks;
using EquityBrief.Web.App;
using EquityBrief.Web.Marks;

namespace EquityBrief.Tests.Reading;

// read-surface, the screens drawn to the design the operator approved: each mark over a full
// input and over the input it degrades on, asserting what it draws and the words it says in place
// of what it cannot draw; every screen laid out in cards whose keys close on what to take from a
// figure; the name page's opening; and the shell's palette and routing.
// see: Every region is a card that states where its figures came from and how to read them
public partial class ReadSurface
{
    // A constructed stretch of sessions around 108, each with a two-point range and a volume that
    // grows, so a chart has a span and a band drawn inside it is inside the pane.
    static ChartBar[] Sessions(int count) =>
    [
        .. Enumerable.Range(0, count).Select(at =>
        {
            var middle = 108m + decimal.Round((decimal)Math.Sin(at / 6.0) * 4m, 2);

            return new ChartBar(new DateOnly(2026, 6, 1).AddDays(at), middle - 0.5m, middle + 1m, middle - 1m, middle + 0.5m, 1000 + (at * 10));
        }),
    ];

    static double At(Match match, int group = 1) =>
        double.Parse(match.Groups[group].Value, CultureInfo.InvariantCulture);

    [Fact]
    public void TheLevelChartNamesItsBandsItsAveragesAndItsLastCloseAndSaysWhatItHasOverTooFewBars()
    {
        var bars = Sessions(60);
        var average = new ChartAverage("sma20", [.. bars.Select((bar, at) => at < 19 ? (double?)null : 107d + (at * 0.01))]);
        ChartBand[] bands = [new(104m, 105.5m, "support", true, 3), new(111m, 112m, "resistance", false, 2)];
        var marks = new MarkRenderer();

        var chart = marks.LevelChart("TEST", bars, [average], bands, new ChartFrame(Scale: 0.5));

        // Drawn at a size of its own, so the profile drawn beside it at the same scale lines up
        // price for price, and never the width of whatever holds it.
        Assert.Contains("width=\"731\" height=\"245\"", chart, StringComparison.Ordinal);
        Assert.DoesNotContain("width=\"100%\"", chart, StringComparison.Ordinal);

        // Each band's two edges drawn as lines, and nothing naming a band inside the plot, where
        // a name is written over the price it is about.
        Assert.Equal(2, Regex.Matches(chart, "class=\"m-edge-sup\"").Count);
        Assert.Equal(2, Regex.Matches(chart, "class=\"m-edge-res\"").Count);
        Assert.DoesNotContain(">support 104.00 to 105.50", chart, StringComparison.Ordinal);
        Assert.DoesNotContain(">resistance 111.00 to 112.00", chart, StringComparison.Ordinal);

        // The legend above the picture instead: the average with a swatch drawn in its own
        // stroke, and the two hues named, which is what the words inside the plot said.
        var legend = Regex.Match(chart, "<g class=\"m-legend\">.*?</g>", RegexOptions.Singleline).Value;

        Assert.Contains(">20-day average</text>", legend, StringComparison.Ordinal);
        Assert.Contains("class=\"m-legend-swatch\"", legend, StringComparison.Ordinal);
        Assert.Contains("m-legend-sup\" x=", legend, StringComparison.Ordinal);
        Assert.Contains(">nearest support</text>", legend, StringComparison.Ordinal);
        Assert.Contains(">nearest resistance</text>", legend, StringComparison.Ordinal);

        // And the nearest band's edges are drawn in that band's hue in the column, which is the
        // other half of what the words carried. The band at 111 to 112 is not the nearest.
        Assert.Contains("class=\"m-tick m-tick-sup\"", chart, StringComparison.Ordinal);
        Assert.DoesNotContain("m-tick-res", chart, StringComparison.Ordinal);

        // The last close as a rule across the pane and a tag carrying the stored close.
        var close = bars[^1].Close.ToString("#,##0.00", CultureInfo.InvariantCulture);

        Assert.Contains("class=\"m-now\"", chart, StringComparison.Ordinal);
        Assert.Contains($">{close}</text>", chart, StringComparison.Ordinal);

        // The tag sits beyond the plot rather than at a number kept here, read off the plot the
        // chart draws, so the column stays outside the picture at whatever width it is drawn.
        var plot = At(Regex.Match(chart, "<rect class=\"m-plot\"[^>]*width=\"([0-9.]+)\""));
        var tag = At(Regex.Match(chart, "<text class=\"m-nowtag-t\" x=\"([0-9.]+)\""));

        Assert.True(tag > plot, $"The close's tag sits at {tag}, inside a plot {plot} wide.");

        // Every price the right-hand column names is a stored one, the close or a band edge, to the
        // two places a picture prints a price at, and it names more than the close alone.
        var column = Regex.Match(chart, "<g class=\"price-column\".*?</g>", RegexOptions.Singleline).Value;
        var named = Regex.Matches(column, ">([0-9.]+)</text>").Select(match => match.Groups[1].Value).ToArray();

        Assert.True(named.Length >= 3, $"The column names {named.Length} price(s), expected at least 3.");
        Assert.All(named, price => Assert.Contains(price, new[] { close, "104.00", "105.50", "111.00", "112.00" }));

        // The dates beneath it are stored sessions, the first and the last among them.
        var axis = Regex.Match(chart, "<g class=\"time-axis\">.*?</g>", RegexOptions.Singleline).Value;
        var dates = Regex.Matches(axis, @">(\d{4}-\d{2}-\d{2})</text>").Select(match => match.Groups[1].Value).ToArray();

        Assert.Equal(5, dates.Length);
        Assert.Equal(bars[0].SessionDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), dates[0]);
        Assert.Equal(bars[^1].SessionDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), dates[^1]);
        Assert.All(dates, date => Assert.Contains(bars, bar => bar.SessionDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) == date));

        // A session the moves table numbers is marked with its number above its candle, and a
        // date the chart does not hold is marked nowhere.
        var marked = marks.LevelChart("TEST", bars, [], [], new ChartFrame(Markers: [bars[30].SessionDate, new DateOnly(2020, 1, 1), bars[10].SessionDate]));

        Assert.Matches("<g class=\"move-mark\" data-session=\"2026-07-01\"><circle class=\"m-mark\"[^>]*/><text class=\"m-mark-t\"[^>]*>1</text></g>", marked);
        Assert.Matches("<g class=\"move-mark\" data-session=\"2026-06-11\"><circle class=\"m-mark\"[^>]*/><text class=\"m-mark-t\"[^>]*>3</text></g>", marked);
        Assert.Equal(2, Regex.Matches(marked, "class=\"move-mark\"").Count);

        // Over too few bars it draws nothing and says how many it has against how many it needs.
        var one = marks.LevelChart("TEST", Sessions(1), [], []);

        Assert.DoesNotContain("<svg", one, StringComparison.Ordinal);
        Assert.Contains("data-sessions=\"1\"", one, StringComparison.Ordinal);
        Assert.Contains("TEST has 1 stored session, and a chart needs at least 2.", one, StringComparison.Ordinal);
    }

    [Fact]
    public void TheVolumeProfileIsDrawnAtASizeOfItsOwnWithTheChartsBandsBehindItAndSaysWhyWhenItHasNone()
    {
        var renderer = new MarkRenderer();
        var axis = renderer.AxisFor(Wide());

        var profile = renderer.VolumeProfile("TEST", Bands(), axis, [new ChartBand(20m, 22m, "support", true, 3)], 0.5);

        // A size of its own at every scale, which is what stops it being stretched across the
        // page, and the same scale the chart beside it is given.
        Assert.Contains("width=\"75\" height=\"193\"", profile, StringComparison.Ordinal);
        Assert.Contains("width=\"150\" height=\"386\"", renderer.VolumeProfile("TEST", Bands(), axis), StringComparison.Ordinal);
        Assert.DoesNotContain("100%", profile, StringComparison.Ordinal);

        // The chart's band carried across, behind the rows.
        Assert.Single(Regex.Matches(profile, "class=\"m-band-sup\""));
        Assert.True(profile.IndexOf("class=\"m-band-sup\"", StringComparison.Ordinal) < profile.IndexOf("class=\"band\"", StringComparison.Ordinal));

        // The even rule where a row would reach if the 1,000 shares were spread evenly over the
        // three bands: 333.33 of the busiest 500, across the 134 the rows are drawn in, from 8.
        var even = Regex.Match(profile, "<line class=\"m-evenrule\" x1=\"([0-9.]+)\"");

        Assert.True(even.Success);
        Assert.Equal(8 + (1000d / 3 / 500 * 134), At(even), 1);
        Assert.Contains(">even</text>", profile, StringComparison.Ordinal);

        // Twice even is past the busiest row, so no rule is drawn off the pane for it.
        Assert.DoesNotContain("m-evenrule2", profile, StringComparison.Ordinal);

        // With no bands it says why, rather than drawing an empty pane.
        var none = renderer.VolumeProfile("TEST", [], axis);

        Assert.DoesNotContain("<svg", none, StringComparison.Ordinal);
        Assert.Contains("TEST has no volume profile, which is what a name with fewer than sixty stored sessions gets.", none, StringComparison.Ordinal);
    }

    [Fact]
    public void ThePlanColumnCentresThePriceKeepsEveryLabelOnItsOwnLineAndSaysSoWhereThereIsNothingToBuy()
    {
        var marks = new MarkRenderer();

        // Rows crowded into a few cents, which put three labels on one line before they were spread.
        PlanRow[] rows =
        [
            new(98m, 99m, PlanKind.Tranche, "buy on the price reaching the zone, stop on a daily close below 97.9", true),
            new(97.9m, 97.9m, PlanKind.Stop, "stop for the 98 zone", true),
            new(97.5m, 98.2m, PlanKind.Tranche, "buy on a failed breakdown back into the zone, stop on a daily close below 97.8", true),
            new(97.8m, 97.8m, PlanKind.Stop, "stop for the 97.5 zone", true),
            new(97.7m, 97.7m, PlanKind.Invalidation, "the whole position is wrong below this", true),
            new(104m, 105m, PlanKind.Exit, "sell 1/3", true),
            new(108m, 109m, PlanKind.Exit, "sell 1/3 and trail the rest", true),
        ];

        var svg = marks.PlanColumn("TEST", 100m, rows);

        // The price now in the middle of the column: 40 above the drawing's middle half of 360.
        Assert.Equal(220, At(Regex.Match(svg, "<line class=\"m-nowline\" x1=\"0\" y1=\"([0-9.]+)\"")), 1);
        Assert.Contains(">Price now 100.00</text>", svg, StringComparison.Ordinal);

        // One label per row, zones named right of the axis and rules left of it, and no two
        // labels on one side within a line of each other.
        var labels = Regex.Matches(svg, "<text class=\"plan-label [^\"]*\" x=\"([0-9.]+)\" y=\"([0-9.]+)\"")
            .Select(match => (X: At(match), Y: At(match, 2)))
            .ToArray();

        Assert.Equal(rows.Length, labels.Length);
        Assert.Equal(2, labels.Select(label => label.X).Distinct().Count());

        foreach (var side in labels.GroupBy(label => label.X))
        {
            var lines = side.Select(label => label.Y).Order().ToArray();

            for (var at = 1; at < lines.Length; at++)
            {
                Assert.True(lines[at] - lines[at - 1] >= 13, $"Two labels at x={side.Key} sit {lines[at] - lines[at - 1]:0.#} apart.");
            }
        }

        // A label moved off its price is joined back to it.
        Assert.Contains("class=\"m-leader\"", svg, StringComparison.Ordinal);

        Assert.Contains(">Buy 98.00 to 99.00</text>", svg, StringComparison.Ordinal);
        Assert.Contains(">Sell at 108.00 to 109.00</text>", svg, StringComparison.Ordinal);
        Assert.Contains(">Stop 97.90</text>", svg, StringComparison.Ordinal);
        Assert.Contains(">Invalidation 97.70</text>", svg, StringComparison.Ordinal);

        // A plan with nothing to buy says so where its purchases would be.
        var sellsOnly = marks.PlanColumn("TEST", 100m, [new(104m, 105m, PlanKind.Exit, "sell 1/3", true)]);

        Assert.Contains("<g class=\"no-purchase\"><rect class=\"m-absent\"", sellsOnly, StringComparison.Ordinal);
        Assert.Contains(">No support band below the price.</text>", sellsOnly, StringComparison.Ordinal);
        Assert.Contains(">No purchase and no invalidation are drawn.</text>", sellsOnly, StringComparison.Ordinal);
        Assert.Contains(">1 exit zone(s), all above the price.</text>", sellsOnly, StringComparison.Ordinal);
        Assert.DoesNotContain("no-purchase", svg, StringComparison.Ordinal);

        // And a name with no plan says so and draws no picture.
        Assert.Contains("has no plan to draw", marks.PlanColumn("TEST", 100m, []), StringComparison.Ordinal);
    }

    [Fact]
    public void TheMomentumPanelDrawsAStretchWithNoReadingAsADashedBoxWithItsCountAndNeverAsALine()
    {
        var values = Enumerable.Range(0, 200).Select(at => at < 60 ? (double?)null : 40 + (at % 20)).ToArray();

        var panel = new MarkRenderer().MomentumPanel("TEST", [new MomentumReading("rsi14", values, 50, 0, 100)]);

        // The usual range drawn behind the reading, which a fixed scale is what makes drawable.
        Assert.Single(Regex.Matches(panel, "class=\"m-neutral\""));

        // The sixty sessions with no reading are one dashed box saying how many.
        var gap = Assert.Single(Regex.Matches(panel, "<g class=\"not-computed\" data-sessions=\"(\\d+)\">(.*?)</g>", RegexOptions.Singleline));

        Assert.Equal("60", gap.Groups[1].Value);
        Assert.Contains("class=\"m-absent\"", gap.Groups[2].Value, StringComparison.Ordinal);
        Assert.Contains(">60 of 200 sessions: not yet computed</text>", gap.Groups[2].Value, StringComparison.Ordinal);

        // And the line begins past the box rather than running across it.
        var box = Regex.Match(gap.Groups[2].Value, "<rect class=\"m-absent\" x=\"([0-9.]+)\" y=\"[0-9.]+\" width=\"([0-9.]+)\"");
        var start = Regex.Match(panel, "<path d=\"M([0-9.]+) ");

        Assert.True(At(start) > At(box) + At(box, 2), $"The line starts at {At(start)}, inside the box ending at {At(box) + At(box, 2)}.");

        // The gap between the two momentum lines drawn as bars about its rule, one per reading.
        var hist = new MarkRenderer().MomentumPanel("TEST", [new MomentumReading("macd_hist", [null, 0.5, -0.25, 1.0], 0, null, null)]);

        Assert.Equal(3, Regex.Matches(hist, "class=\"m-hist\"").Count);
        Assert.DoesNotContain("<path", hist, StringComparison.Ordinal);
        Assert.Contains("data-sessions=\"1\"", hist, StringComparison.Ordinal);
    }

    [Fact]
    public void TheDistanceRowSaysNoneBelowOrNoneAboveInADashedBoxRatherThanDrawingAnEdge()
    {
        var marks = new MarkRenderer();

        var both = marks.DistanceRow(new UniverseCell("TEST", "Tech", 100m, "rising", 98m, 103m, 0.8, 2.5, 0.8));

        Assert.Contains("class=\"support-edge\"", both, StringComparison.Ordinal);
        Assert.Contains("class=\"resistance-edge\"", both, StringComparison.Ordinal);
        Assert.Single(Regex.Matches(both, "class=\"m-dist-sup\""));
        Assert.Single(Regex.Matches(both, "class=\"m-dist-res\""));
        Assert.Contains(">S 0.8</text>", both, StringComparison.Ordinal);
        Assert.Contains(">2.5 R</text>", both, StringComparison.Ordinal);
        Assert.DoesNotContain("m-absent", both, StringComparison.Ordinal);

        // Past the scale's four typical days the block becomes an arrow, so a distant band does
        // not read as one at the edge of the picture.
        var far = marks.DistanceRow(new UniverseCell("TEST", "Tech", 100m, "rising", 90m, 103m, 6.0, 2.5, 2.5));

        Assert.Contains("class=\"m-link-sup\"", far, StringComparison.Ordinal);
        Assert.DoesNotContain("m-dist-sup", far, StringComparison.Ordinal);
        Assert.Contains(">S 6.0</text>", far, StringComparison.Ordinal);

        // No band on a side: the words in a dashed box, and nothing drawn that reads as an edge.
        var none = marks.DistanceRow(new UniverseCell("TEST", "Tech", 100m, "rising", null, null, null, null, null));

        Assert.Equal(2, Regex.Matches(none, "class=\"m-absent\"").Count);
        Assert.Contains(">none below</text>", none, StringComparison.Ordinal);
        Assert.Contains(">none above</text>", none, StringComparison.Ordinal);
        Assert.DoesNotContain("-edge\"", none, StringComparison.Ordinal);
        Assert.DoesNotContain("m-dist-", none, StringComparison.Ordinal);
    }

    // The contents at the head of a name's page, which is how a reader reaches a section
    // without scrolling for it. Read in both directions against the cards the page drew, so
    // neither a card nobody can reach nor an entry pointing at nothing can be written.
    [Fact]
    public async Task TheContentsNamesEveryCardTheNamePageDrewAndNothingElse()
    {
        using var store = await FixtureExpectations.WithListings();
        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        var night = NightIn(store);
        var name = FiredNamesOn(store, night)[0];
        var page = await client.GetStringAsync($"/screens/name/{name}");

        var contents = Regex.Match(page, "<nav class=\"contents\"[^>]*>.*?</nav>", RegexOptions.Singleline);

        Assert.True(contents.Success, "The name page draws no contents.");

        var entries = Regex.Matches(contents.Value, "<li><a href=\"#([^\"]+)\"><span class=\"c-n\">(\\d+)</span>([^<]*)</a></li>")
            .Select(match => (Id: match.Groups[1].Value, At: int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture), Title: match.Groups[3].Value))
            .ToArray();

        // A page drawing the computed sections alone still reaches the count below, so the
        // floor is what a name with no research carries rather than what this one does.
        Assert.True(entries.Length >= 9, $"The contents names {entries.Length} card(s), expected at least 9.");
        Assert.Contains($"data-entries=\"{entries.Length}\"", contents.Value, StringComparison.Ordinal);

        // Numbered from where a reader starts, contiguously, in the order the page draws.
        Assert.Equal([.. Enumerable.Range(0, entries.Length)], [.. entries.Select(entry => entry.At)]);
        Assert.All(entries, entry => Assert.NotEqual(string.Empty, entry.Title));

        // Every card the page drew is named once, and every entry reaches a card. The cards
        // are read off the markup rather than from a list kept here, so a card added without
        // an entry fails this without anything else being edited.
        // Both shapes a region is drawn in: an ordinary card, and the dashed outline a name
        // with no research is drawn in, which is a region a reader reaches like any other.
        var cards = Regex.Matches(page, "<section class=\"(?:card[^\"]*|absent)\" id=\"([^\"]+)\"").Select(match => match.Groups[1].Value).ToArray();

        Assert.Equal(cards.Length, cards.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal([.. cards], [.. entries.Select(entry => entry.Id)]);

        // And the contents stands above the first card it names rather than among them.
        Assert.True(
            page.IndexOf("<nav class=\"contents\"", StringComparison.Ordinal) < page.IndexOf("<section class=\"card\" id=\"how-to-read\"", StringComparison.Ordinal),
            "The contents is drawn below the first card it names.");
    }

    // Section 4 of the architecture is the report region by region, and the name page is what it
    // specifies. Its rows are read against the contents of every fixture name's page, one name given
    // every written region and its industry a cycle: every region a page draws is a row, in the rows'
    // order, every row is a region some page draws, and the count the section states is its count of
    // rows, so a region added, dropped, renamed or moved on either side fails here.
    // see: Section 4 of the architecture defines the report region by region, and nothing outside the corpus does
    [Fact]
    public async Task SectionFourNamesEveryRegionTheNamePageDrawsInTheOrderItDrawsThem()
    {
        var document = Corpus.Read("docs/ARCHITECTURE.html");
        var rows = ReportRegions(document);

        // The reader, shown to read each row's first cell whole with its entities decoded, and to pass
        // over the heading row and every other section's tables.
        Assert.Equal(
            ["Tonight's figures", "How it got here"],
            [.. ReportRegions(
                "<h2>3. Before</h2><table><tr><td>Not this</td></tr></table>" +
                $"<h2>{ReportSection}</h2><p>up to two regions</p><table><tr><th>Region</th><th>What</th></tr>" +
                "<tr><td>Tonight&#39;s figures</td><td>x</td></tr><tr><td>How it got here</td><td>y</td></tr></table>" +
                "<h2>5. After</h2><table><tr><td>Nor this</td></tr></table>")]);

        Assert.True(rows.Count >= 19, $"Section 4 names {rows.Count} region(s), expected at least 19.");

        using var store = await FixtureExpectations.WithListings();

        var night = NightIn(store);

        // The fixture's researched name given every written region a page draws, each dated the night
        // the page draws, and its industry a cycle.
        string[] written =
        [
            .. SinglePageApp.AtTheTop,
            .. SinglePageApp.UnderTheFigures,
            .. SinglePageApp.BeforeTheNumbers,
            .. SinglePageApp.AfterTheNumbers.Where(section => section != ClaimRules.CycleSection),
            .. SinglePageApp.AfterThePlan,
        ];

        foreach (var section in written)
        {
            store.Execute($"INSERT INTO research_section VALUES ('KEYS', '{section}', 90, '{night}', 'a writer', 'accepted', 'A sentence.', '[]', NULL);");
        }

        ThemeDocument(store);
        ThemeCycle(store, 1, night, "accepted", "Orders across the industry are turning up from a low [D1].");

        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        var names = Rows(store, $"SELECT ticker FROM listing WHERE session_date = '{night}' AND ticker IN (SELECT ticker FROM bar) ORDER BY ticker;")
            .Select(row => row[0])
            .ToArray();

        Assert.True(names.Length >= 4, $"Read {names.Length} name page(s), expected at least 4.");

        var drawn = new List<string[]>();

        foreach (var ticker in names)
        {
            var titles = ContentsTitles(await client.GetStringAsync($"/screens/name/{ticker}"));
            var at = titles.Select(title => rows.IndexOf(title)).ToArray();

            Assert.True(
                at.All(index => index >= 0),
                $"{ticker}'s page draws a region section 4 does not name: {string.Join(", ", titles.Where(title => !rows.Contains(title)))}.");
            Assert.True(at.SequenceEqual(at.Order()), $"{ticker}'s page draws its regions out of section 4's order: {string.Join(", ", titles)}.");

            drawn.Add(titles);
        }

        // Both directions: every row is a region some page draws.
        Assert.Equal(
            [.. rows.Order(StringComparer.Ordinal)],
            [.. drawn.SelectMany(titles => titles).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)]);

        // And the count the section states in words is its count of rows.
        var stated = Regex.Match(ReportText(document), @"up to (\w+) regions");

        Assert.True(stated.Success, "Section 4 states no count of its regions.");
        Assert.Equal(rows.Count, Array.IndexOf(CountWords, stated.Groups[1].Value));
    }

    const string ReportSection = "4. The report, section by section, and where each part comes from";

    static readonly string[] CountWords =
    [
        "zero", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine", "ten",
        "eleven", "twelve", "thirteen", "fourteen", "fifteen", "sixteen", "seventeen", "eighteen",
        "nineteen", "twenty", "twenty-one", "twenty-two", "twenty-three", "twenty-four", "twenty-five",
    ];

    // Section 4's own text, from its heading to the next.
    static string ReportText(string document)
    {
        var heading = $"<h2>{ReportSection}</h2>";
        var start = document.IndexOf(heading, StringComparison.Ordinal);

        Assert.True(start >= 0, "The architecture carries no section 4.");

        var end = document.IndexOf("<h2", start + heading.Length, StringComparison.Ordinal);

        return document[start..(end < 0 ? document.Length : end)];
    }

    // The first cell of each row of section 4's table, as a reader reads it.
    static List<string> ReportRegions(string document)
    {
        var table = Regex.Match(ReportText(document), "<table>.*?</table>", RegexOptions.Singleline);

        Assert.True(table.Success, "Section 4 carries no table.");

        return
        [
            .. Regex.Matches(table.Value, "<tr><td>(.*?)</td>", RegexOptions.Singleline)
                .Select(match => WebUtility.HtmlDecode(Regex.Replace(match.Groups[1].Value, "<[^>]+>", string.Empty)).Trim()),
        ];
    }

    // The titles the contents at the head of a name's page names, in its order.
    static string[] ContentsTitles(string page)
    {
        var contents = Regex.Match(page, "<nav class=\"contents\"[^>]*>.*?</nav>", RegexOptions.Singleline);

        Assert.True(contents.Success, "The name page draws no contents.");

        return
        [
            .. Regex.Matches(contents.Value, "<li><a href=\"#[^\"]+\"><span class=\"c-n\">\\d+</span>([^<]*)</a></li>")
                .Select(match => WebUtility.HtmlDecode(match.Groups[1].Value)),
        ];
    }

    [Fact]
    public async Task EveryScreenIsLaidOutInCardsAndEveryKeyClosesOnWhatToTakeFromTheFigure()
    {
        using var store = await FixtureExpectations.WithListings();
        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        var night = NightIn(store);
        var name = FiredNamesOn(store, night)[0];

        var tonight = await client.GetStringAsync("/screens/tonight");
        var universe = await client.GetStringAsync("/screens/universe");
        var run = await client.GetStringAsync("/screens/run");
        var page = await client.GetStringAsync($"/screens/name/{name}");

        // Each screen opens with the line the masthead carries, naming what it is.
        Assert.Contains("<div class=\"screen-mast\" data-title=\"Tonight\">", tonight, StringComparison.Ordinal);
        Assert.Contains("<div class=\"screen-mast\" data-title=\"The universe\">", universe, StringComparison.Ordinal);
        Assert.Contains("<div class=\"screen-mast\" data-title=\"Run evidence\">", run, StringComparison.Ordinal);
        Assert.Contains($"<div class=\"screen-mast\" data-title=\"{name}\"><span class=\"m-tk\">{name}</span>", page, StringComparison.Ordinal);
        Assert.Contains("the last stored price", page, StringComparison.Ordinal);

        var screens = new[]
        {
            (tonight, new[] { "night", "watch", "list", "selected", "totals" }),
            (universe, new[] { "sectors", "index" }),
            (run, new[] { "operational", "records", "shadow", "stale", "queue", "harness" }),
            (page, new[] { "facts", "how-it-got-here", "chart", "plan", "sources" }),
        };

        var keys = 0;

        foreach (var (screen, regions) in screens)
        {
            foreach (var region in regions)
            {
                Assert.Matches($"<section class=\"card\"[^>]* data-card=\"{region}\">", screen);
            }

            // Every key says how to read a figure and closes on what to take from it.
            foreach (Match key in Regex.Matches(screen, "<div class=\"key\">(.*?)</div>", RegexOptions.Singleline))
            {
                keys++;
                Assert.Contains("<p class=\"take\"><b>What to take from it.</b> ", key.Groups[1].Value, StringComparison.Ordinal);
            }
        }

        Assert.True(keys >= 11, $"The four screens carry {keys} keys, expected at least 11.");

        // The name page opens with what it is for, its three refusals and its ten words.
        Assert.Contains("<section class=\"intro\" aria-label=\"About this page\"><p class=\"intro-p\">This page finds the prices ", page, StringComparison.Ordinal);
        Assert.Contains("<li>It does not predict where the price will go.</li>", page, StringComparison.Ordinal);
        Assert.Contains($"<li>It does not rank {name} against any other name.</li>", page, StringComparison.Ordinal);
        Assert.Contains("<li>It does not say how much to buy: the sizing near the end only divides the amount you choose to risk.</li>", page, StringComparison.Ordinal);

        var glossary = Regex.Match(page, "<details class=\"gloss\"><summary>Words used on this page</summary>(.*?)</details>", RegexOptions.Singleline);

        Assert.True(glossary.Success);
        Assert.Equal(10, Regex.Matches(glossary.Groups[1].Value, "<dt>").Count);

        // The refusal about sizing holds because the page states no account and no share count.
        // see: The sizing arithmetic states the risk a share carries and holds no account of the reader's
        Assert.DoesNotContain("your setting", page, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(" shares</dd>", page, StringComparison.Ordinal);

        // Tonight's reasons in six columns in their set order, each head one word with the
        // reason's full name on it.
        var head = Regex.Match(tonight, "<table class=\"list-table\"[^>]*><thead>(.*?)</thead>", RegexOptions.Singleline).Groups[1].Value;
        var columns = Regex.Matches(head, "<abbr title=\"([^\"]+)\">([^<]+)</abbr>").ToArray();

        Assert.Equal(ShortlistSeries.Reasons, columns.Select(column => column.Groups[1].Value).ToArray());
        Assert.Equal(["entry", "crossed", "breakout", "trend", "volume", "earnings"], columns.Select(column => column.Groups[2].Value).ToArray());
    }

    [Fact]
    public void TheShellRemembersThePaletteDefaultsToTheMachinesAndResolvesAnUnknownRouteToTonightWithALine()
    {
        var shell = new SinglePageApp().Shell("EquityBrief");

        // The stylesheet whole, the colour rules at its head, and both palettes designed.
        Assert.StartsWith("/* COLOUR RULES.", Stylesheet.Css, StringComparison.Ordinal);
        Assert.Contains(Stylesheet.Css, shell, StringComparison.Ordinal);
        Assert.Contains("@media (prefers-color-scheme: dark)", Stylesheet.Css, StringComparison.Ordinal);
        Assert.Contains(":root[data-eb-theme='dark']", Stylesheet.Css, StringComparison.Ordinal);

        // The palette is a stamp on the root element, remembered between visits, with the
        // machine's own preference as the default.
        Assert.Contains("localStorage.getItem('eb-theme')", shell, StringComparison.Ordinal);
        Assert.Contains("localStorage.setItem('eb-theme', next)", shell, StringComparison.Ordinal);
        Assert.Contains("document.documentElement.setAttribute('data-eb-theme', next)", shell, StringComparison.Ordinal);
        Assert.Contains("matchMedia('(prefers-color-scheme: dark)')", shell, StringComparison.Ordinal);

        // An unknown route draws tonight with a line naming what was asked for.
        Assert.Contains("const tonight = await fetch('/screens/tonight');", shell, StringComparison.Ordinal);
        Assert.Contains("line.textContent = 'Nothing is drawn at ' + hash + ', so this is Tonight.';", shell, StringComparison.Ordinal);

        // Back and forward return to where the reader was, and a new place starts at its top.
        Assert.Contains("if (!fresh && scrolls[hash] != null) { scrollTo(0, scrolls[hash]); }", shell, StringComparison.Ordinal);

        // The exported report carries the same stylesheet, so it reads as the page it came from.
        Assert.Contains(Stylesheet.Css, new ReportExporter().Document("TEST", new DateOnly(2026, 9, 18), "<section class=\"name\"></section>"), StringComparison.Ordinal);
    }
}
