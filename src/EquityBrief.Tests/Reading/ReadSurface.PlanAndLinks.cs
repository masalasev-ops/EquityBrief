using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using EquityBrief.Api.Reading;
using EquityBrief.Tests.Checks;
using EquityBrief.Tests.Harness;
using EquityBrief.Web.App;
using EquityBrief.Web.Marks;

namespace EquityBrief.Tests.Reading;

// read-surface, the 5.8 correction: the plan card read at a glance, and a link to a place on a page
// followed to it rather than read as a screen's address.
public partial class ReadSurface
{
    [Fact]
    public async Task ThePlanIsHeadedEntryAndExitPlanAndNoRuleOfItsColumnRunsUnderTheWordsBesideIt()
    {
        using var store = await FixtureExpectations.WithListings();
        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        var name = FiredNamesOn(store, NightIn(store))[0];
        var page = WebUtility.HtmlDecode(await client.GetStringAsync($"/screens/name/{name}"));

        // The card and its entry in the contents both carry the plain heading.
        Assert.Contains("<a href=\"#plan\">", page, StringComparison.Ordinal);
        Assert.Matches("<a href=\"#plan\"><span class=\"c-n\">\\d+</span>Entry and exit plan</a>", page);
        Assert.DoesNotContain("Where it is bought, sold, and wrong", page, StringComparison.Ordinal);

        // Every rule across the column, the price line among them, ends short of every word set to the
        // right of the column, which is what kept a stop's label and a tranche's lines from being struck
        // through. Read off the drawing rather than off the renderer's constants, on the fixture's page
        // and over a plan worked by hand whose tranche holds the price with two stops and an exit.
        AssertNoRuleRunsUnderTheWordsBesideTheColumn(Regex.Match(page, "<svg class=\"plan-column\".*?</svg>", RegexOptions.Singleline).Value, rules: 1);

        PlanRow[] plan =
        [
            new(107m, 110m, PlanKind.Exit, "sell 1/2", true),
            new(98m, 102m, PlanKind.Tranche, "buy on this price now, stop on a daily close below 96.00", true, BuyOn: "this price now", Stop: 96m),
            new(96m, 96m, PlanKind.Stop, "stop for the 98.00 zone", true),
            new(93m, 94m, PlanKind.Tranche, "buy on the price reaching the zone, stop on a daily close below 92.00", true, BuyOn: "the price reaching the zone", Stop: 92m),
            new(92m, 92m, PlanKind.Invalidation, "stop for the 93.00 zone, and the whole position is wrong below this", true),
        ];

        AssertNoRuleRunsUnderTheWordsBesideTheColumn(new MarkRenderer().PlanColumn("ZZZZ", 100m, plan), rules: 3);

        // The tranches and the exits each under their own heading.
        Assert.Contains("<div class=\"sub plan-sub\">Entries</div><div class=\"tbl-wrap\"><table class=\"tranche-table\"", page, StringComparison.Ordinal);
        Assert.Contains("<div class=\"sub plan-sub\">Exits</div><div class=\"tbl-wrap\"><table class=\"exit-table\"", page, StringComparison.Ordinal);
    }

    static void AssertNoRuleRunsUnderTheWordsBesideTheColumn(string column, int rules)
    {
        var ends = Regex.Matches(column, "<line class=\"(?:stop-rule|invalidation-rule|m-nowline)\" x1=\"[0-9.]+\" y1=\"[0-9.]+\" x2=\"([0-9.]+)\"")
            .Select(rule => double.Parse(rule.Groups[1].Value, CultureInfo.InvariantCulture))
            .ToArray();
        var words = Regex.Matches(column, "<text class=\"[^\"]*\" x=\"([0-9.]+)\" y=\"[0-9.]+\" text-anchor=\"start\">")
            .Select(word => double.Parse(word.Groups[1].Value, CultureInfo.InvariantCulture))
            .ToArray();

        Assert.True(ends.Length >= rules, $"The column draws {ends.Length} rule(s), expected at least {rules}, and this is what reads them.");
        Assert.True(words.Length >= 1, "The column sets no word to its right, and this is what reads them.");
        Assert.True(ends.Max() < words.Min(), $"A rule ends at {ends.Max()}, over words set from {words.Min()}.");
    }

    [Fact]
    public void TheStopTheInvalidationSitsAtSaysSoAndTheEarningsMovesStandUnderAHeadingOfTheirOwn()
    {
        // Two tranches worked by hand: the lower one's stop is the plan's invalidation, and the upper
        // one's is not.
        PlanRow[] rows =
        [
            new(100m, 102m, PlanKind.Tranche, "buy on this price now, stop on a daily close below 95.00", true, BuyOn: "this price now", Stop: 95m),
            new(95m, 95m, PlanKind.Stop, "stop for the 100.00 zone", true),
            new(90m, 91m, PlanKind.Tranche, "buy on the price reaching the zone, stop on a daily close below 88.00", true, BuyOn: "the price reaching the zone", Stop: 88m),
            new(88m, 88m, PlanKind.Invalidation, "stop for the 90.00 zone, and the whole position is wrong below this", true),
        ];

        var tables = new MarkRenderer().PlanTables("ZZZZ", rows);

        Assert.Contains("<td>this price now</td><td>a daily close below 95.00</td>", tables, StringComparison.Ordinal);
        Assert.Contains("<td>the price reaching the zone</td><td>a daily close below 88.00, where the whole position is wrong</td>", tables, StringComparison.Ordinal);
        Assert.Single(Regex.Matches(tables, "where the whole position is wrong"));

        // A row carrying no condition apart is drawn whole across both columns rather than split.
        var whole = new MarkRenderer().PlanTables("ZZZZ", [new PlanRow(100m, 102m, PlanKind.Tranche, "buy on this price now, no stop beneath", true)]);

        Assert.Contains("<td colspan=\"2\">buy on this price now, no stop beneath</td>", whole, StringComparison.Ordinal);
    }

    [Fact]
    public void TheLastTwoPrintsMovesAgainstTheStopStandUnderAHeadingOfTheirOwn()
    {
        // A plan carrying two prints and no tranche, so the moves are drawn with nothing above them.
        const string Plan =
            """
            {"tranches":[],"exits":[],"invalidation":null,"events":[],
             "arithmetic":{"absent":"no tranche","firstRisk":null},
             "earningsRule":[{"eventDate":"2026-04-29","session":"2026-04-29","move":"13.3685","shareOfStop":"0.5760"},
                             {"eventDate":"2026-07-29","session":"2026-07-29","move":"23.30","shareOfStop":"1.0039"}]}
            """;

        var drawn = NameScreen.Arithmetic(new LadderRow("ZZZZ", new DateOnly(2026, 9, 8), "range", Plan));

        Assert.Contains("<div class=\"sub\">Earnings moves against the first tranche's stop</div><div class=\"tbl-wrap\"><table class=\"earnings-rule\">", drawn, StringComparison.Ordinal);
        Assert.Equal(2, Regex.Matches(drawn, "<tr data-print=").Count);
    }

    [Fact]
    public async Task ALinkToAPlaceOnAPageIsFollowedToItAndNeverReadAsAScreensAddress()
    {
        // Every screen's address opens "#/", which is what lets the page tell a place from a screen.
        Assert.All(
            new[] { SinglePageApp.NameRoute, SinglePageApp.NightRoute, SinglePageApp.RunRoute, SinglePageApp.UniverseRoute, SinglePageApp.ResearchedRoute, SinglePageApp.QueueRoute, SinglePageApp.WatchRoute },
            route => Assert.StartsWith("#/", route, StringComparison.Ordinal));

        using var store = await FixtureExpectations.WithListings();
        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        // The script follows a link whose address is not a screen's to its place, below the masthead
        // measured as it stands, and takes it as no address; a screen's own address is left to the router.
        var shell = await client.GetStringAsync("/");

        Assert.Contains("const jump = event.target.closest('a[href^=\"#\"]:not([href^=\"#/\"])');", shell, StringComparison.Ordinal);

        var handler = shell[shell.IndexOf("const jump", StringComparison.Ordinal)..];

        handler = handler[..handler.IndexOf("return;", StringComparison.Ordinal)];

        Assert.Contains("event.preventDefault();", handler, StringComparison.Ordinal);
        Assert.Contains("document.getElementById(decodeURIComponent(jump.getAttribute('href').slice(1)))", handler, StringComparison.Ordinal);
        Assert.Contains("scrollTo({ top: place.getBoundingClientRect().top + scrollY - (mast ? mast.offsetHeight : 0) - 12 });", handler, StringComparison.Ordinal);

        // And on a name's page every such link names a place the page draws: each entry of the contents
        // and each circle on the twelve-month picture.
        var name = FiredNamesOn(store, NightIn(store))[0];
        var page = await client.GetStringAsync($"/screens/name/{name}");
        var places = Regex.Matches(page, "href=\"#([^/\"][^\"]*)\"").Select(link => link.Groups[1].Value).Distinct().ToArray();

        Assert.True(places.Length >= 5, $"The page links {places.Length} place(s), and this is what reads them.");
        Assert.All(places, place => Assert.Contains($"id=\"{place}\"", page, StringComparison.Ordinal));

        // The folds a reader opens say whether they are open, and the control at the foot of a long one
        // closes it and brings its heading back into view.
        Assert.Contains("const hide = event.target.closest('.fold-hide');", shell, StringComparison.Ordinal);
        Assert.Contains("fold.open = false; fold.querySelector('summary').scrollIntoView({ block: 'center' });", shell, StringComparison.Ordinal);
        Assert.Contains("details.numbers-detail>summary::after{content:\"Show\"", Stylesheet.Css, StringComparison.Ordinal);
        Assert.Contains("details.guidance-fold[open]>summary::after,details.numbers-detail[open]>summary::after{content:\"Hide\"}", Stylesheet.Css, StringComparison.Ordinal);
    }
}
