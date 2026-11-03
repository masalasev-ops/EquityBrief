using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using EquityBrief.Core.Families;
using EquityBrief.Core.Time;
using EquityBrief.Tests.Checks;
using EquityBrief.Tests.Harness;
using EquityBrief.Web.App;
using EquityBrief.Web.Cloudflare;
using EquityBrief.Web.Marks;

namespace EquityBrief.Tests.Reading;

// read-surface, 22.16: Home, the landing page for a reader new to investing, its headline's branches and its sentences
// worked by hand from the figures, each region read back off the rendered page, the page drawn over a constructed store on
// every index, and the read-only copy holding it as its landing route.
// see: Home is the landing page for a reader new to investing, drawn by code from the newest night's figures in plain words
public partial class ReadSurface
{
    internal static readonly string[] HomeRows =
    [
        .. new[] { "The day", "Tonight's ideas", "How past ideas have done", "How it works", "The foot", "On the read-only copy" }
            .Select(row => CheckReach.Key("15.24 Home", row)),
    ];

    static readonly DateOnly HomeNight = new(2026, 10, 30);

    // The S&P 500 on a Friday it fell 1.8%: 62% of its stocks above their 200-day average against the 45% the rules buy at,
    // the VIX up and above its median, no ideas and no paper account.
    static HomeView Day(double? share = 0.62, double? floor = 0.45, double? moved = -1.8, double? vix = 2.0, bool? above = true) =>
        new(Universes.Large, HomeNight, false, share, floor, true, moved, [("S&P 400", -1.2), ("S&P 600", 0.4)], vix, above, [], 0, "Monday", "No stock met every rule tonight.", null);

    // The S&P 400, where each rule checks its own market level: its own 52% and no floor.
    static HomeView Mid(int open, int rules = 3, double moved = -0.5) =>
        Day(share: 0.52, floor: null, moved: moved) with { Reading = Universes.Mid, Others = [("S&P 500", -1.8), ("S&P 600", 0.4)], RulesOpen = open, Rules = rules };

    [Fact]
    public void HomesHeadlineIsChosenByCodeFromTheShareTheFloorTheDaysMoveAndTheRulesOpen()
    {
        // The S&P 500: 62% against 45% after a fall of 1.8%, a shaky day the rules still buy in; a fall of exactly 1% is
        // shaky and one of 0.99% is not; a share at the floor is a market the rules buy in, and one under it is weak.
        Assert.Equal("A shaky day, but still a market our rules will buy in.", SinglePageApp.HomeHeadline(Day()));
        Assert.Equal("shaky", SinglePageApp.HomeMarket(Day(moved: -1.0)));
        Assert.Equal("A healthy market: our rules are buying.", SinglePageApp.HomeHeadline(Day(moved: -0.99)));
        Assert.Equal("healthy", SinglePageApp.HomeMarket(Day(share: 0.45, moved: 0.3)));
        Assert.Equal("A weak market: our rules are staying out tonight.", SinglePageApp.HomeHeadline(Day(share: 0.4499, moved: 0.3)));
        Assert.Equal("weak", SinglePageApp.HomeMarket(Day(share: 0.40, moved: -2.5)));

        // The S&P 400, from how many of its rules are open: all three healthy, two mixed, none weak, and a fall of a
        // percent or more making either of the first two shaky.
        Assert.Equal("A healthy market: our rules are buying.", SinglePageApp.HomeHeadline(Mid(3)));
        Assert.Equal("A mixed market: some of our rules are buying.", SinglePageApp.HomeHeadline(Mid(2)));
        Assert.Equal("A weak market: our rules are staying out tonight.", SinglePageApp.HomeHeadline(Mid(0)));
        Assert.Equal("shaky", SinglePageApp.HomeMarket(Mid(2, moved: -1.2)));
        Assert.Equal("weak", SinglePageApp.HomeMarket(Mid(0, moved: -1.2)));

        // A later night still running names itself in the headline's place, the newest finished night drawn.
        Assert.Equal("Tonight's run hasn't finished yet. Showing Friday's results.", SinglePageApp.HomeHeadline(Day() with { Running = true }));
    }

    [Fact]
    public void HomesSentencesSayWhatEachNumberMeans()
    {
        Assert.Equal(
            "The S&P 500 fell 1.8% today, and investors got more nervous. Most stocks are in long-term uptrends, and the rules buy only when at least 45% are.",
            SinglePageApp.HomeSummary(Day()));
        Assert.Equal(
            "The S&P 500 rose 0.6% today, and investors were calmer. 47% of stocks are in long-term uptrends, and the rules buy only when at least 45% are.",
            SinglePageApp.HomeSummary(Day(share: 0.47, moved: 0.6, vix: -3.1)));
        Assert.Equal(
            "The S&P 500 was flat today. Fewer than 45% of stocks are in long-term uptrends, and the rules buy only when at least 45% are.",
            SinglePageApp.HomeSummary(Day(share: 0.40, moved: 0.04, vix: 0)));
        Assert.Equal(
            "The S&P 400 fell 0.5% today, and investors got more nervous. Most stocks are in long-term uptrends. Each rule here checks its own market level; tonight 2 of 3 are open to buy.",
            SinglePageApp.HomeSummary(Mid(2)));
        Assert.Equal(
            "No close of the S&P 500 is stored for today. No share of stocks in long-term uptrends is stored for today.",
            SinglePageApp.HomeSummary(Day(share: null, moved: null)));
    }

    // The visible text of a fragment, its tags, scripts and styles taken out.
    static string TextOf(string fragment) =>
        WebUtility.HtmlDecode(Regex.Replace(Regex.Replace(fragment, "<(script|style|svg)\\b.*?</\\1>", " ", RegexOptions.Singleline), "<[^>]+>", " "));

    [Fact]
    public void HomesRegionsDrawTheDayTheIdeasThePastTheWayItWorksAndTheFoot()
    {
        var ideas = new HomeIdea[]
        {
            // A plan buying at 100 with its give-up price at 90 and its target at 125: it risks 10 to make 25, $2.50 a dollar.
            new(SetupFamilies.Pullback, "AAA", "Alpha Inc", "A strong stock that dipped.", 100m, 90m, 125m, false, null),
            // From 22.18 a breakout buying at 50 with its give-up price 4 beneath: it can no longer lose once a close reaches 54.
            new(BreakoutRule.Name, "BBB", null, "Closed above its highest price of the last 6 months.", 50m, 46m, null, true, null, 4m, new EquityBrief.Core.Cards.PaperWinners(3, 300, null)),
            new(ImprovingRule.Name, "CCC", "Gamma Co", "Its newest filing shows the business getting stronger: 8 of 9 checks, up from 6.", 20m, null, null, true, null),
        };
        var view = Day() with
        {
            Ideas = ideas,
            IdeasCount = 7,
            Past = new HomePast(new DateOnly(2026, 10, 2), 4, 3, 0.031, 0.012, true),
        };
        var page = new SinglePageApp().HomeRegion(new MarkRenderer(), view, string.Empty);
        var text = TextOf(page);

        // The day: its label, its headline's branch, the bar with the floor marked at 45 and filled to 62, and the tiles.
        Assert.Contains("<section class=\"home\" data-universe=\"500\" data-night=\"2026-10-30\" data-running=\"false\">", page, StringComparison.Ordinal);
        Assert.Contains("<div class=\"screen-mast\" data-title=\"Home: S&amp;P 500 large cap\">", page, StringComparison.Ordinal);
        Assert.Contains("Friday 30 Oct · after the market closed", text, StringComparison.Ordinal);
        Assert.Contains("data-headline=\"shaky\"", page, StringComparison.Ordinal);
        Assert.Contains("<rect class=\"bar-fill open\" x=\"0\" y=\"1\" width=\"62\"", page, StringComparison.Ordinal);
        Assert.Contains("<rect class=\"bar-floor\" x=\"45\"", page, StringComparison.Ordinal);
        Assert.Contains("data-signal=\"trend\" data-tone=\"good\"", page, StringComparison.Ordinal);
        Assert.Contains("62% of these companies trade above their 200-day average. The rules buy only above 45%.", text, StringComparison.Ordinal);
        Assert.Contains("data-signal=\"vix\" data-tone=\"care\"", page, StringComparison.Ordinal);
        Assert.Contains("The market's fear gauge is above its typical level for the past year. Expect bigger swings.", text, StringComparison.Ordinal);
        Assert.Contains("S&P 500 down 1.8%", text, StringComparison.Ordinal);
        Assert.Contains("S&P 400 down 1.2% and S&P 600 up 0.4%.", text, StringComparison.Ordinal);

        // The ideas: the count and the session, each card's prices, the bar sized 10 to 25, a trailing plan's line in the
        // target's place, and a Fundamentals buy giving up below its 200-day average.
        Assert.Contains("Tonight's ideas: 7 stocks to buy Monday morning", text, StringComparison.Ordinal);
        Assert.Equal(["AAA", "BBB", "CCC"], Regex.Matches(page, "<article class=\"home-idea fam-edge\" data-family=\"[a-z]+\" data-ticker=\"([A-Z]+)\"").Select(match => match.Groups[1].Value));
        Assert.Contains("Bounce in an uptrend", text, StringComparison.Ordinal);
        Assert.Contains("<b>$100.00</b>", page, StringComparison.Ordinal);
        Assert.Contains("<b class=\"stop\">$90.00</b>", page, StringComparison.Ordinal);
        Assert.Contains("<b class=\"aim\">$125.00</b>", page, StringComparison.Ordinal);
        Assert.Contains("<div class=\"home-rr\" role=\"img\" aria-label=\"Risks $1 to make about $2.50\"><div class=\"risk\" style=\"flex:10\"></div><div class=\"reward\" style=\"flex:25\"></div></div>", page, StringComparison.Ordinal);
        Assert.Single(Regex.Matches(page, "class=\"home-rr\""));
        Assert.Contains("<b class=\"aim\">No fixed target</b>", page, StringComparison.Ordinal);
        Assert.Single(Regex.Matches(text, Regex.Escape(SinglePageApp.RisesLine)));

        // From 22.18 the breakout's give-up price rising beneath its price, why it has no target, the close past which it
        // can no longer lose, 50 and its distance of 4, and its rule's record below the count it needs.
        Assert.Contains("<b class=\"stop\">$46.00</b></div><p class=\"home-rising\">rising as the stock makes new highs; it never moves down.</p>", page, StringComparison.Ordinal);
        Assert.Contains("Breakouts earn most from the few that keep running, so the rule lets a winner run and sells only when the price closes below a give-up price that rises behind it.", text, StringComparison.Ordinal);
        Assert.Contains("<div class=\"home-odds home-noloss\" data-no-loss=\"54\">The trade can no longer lose once the stock reaches about $54.00.</div>", page, StringComparison.Ordinal);
        Assert.Contains("The breakout rule has 3 of the 300 finished trades its record needs.", text, StringComparison.Ordinal);
        Assert.Contains("<b class=\"stop\">below its 200-day average</b>", page, StringComparison.Ordinal);
        Assert.Contains("<a href=\"#/chart/AAA\">See the plan</a>", page, StringComparison.Ordinal);
        Assert.Contains("<a href=\"#/tonight\">See all 7 with their full plans</a>", page, StringComparison.Ordinal);
        Assert.Contains(SinglePageApp.IdeasNote, text, StringComparison.Ordinal);

        // The past: the account's day, its ideas and those that made money, and the bars at 3.1% and 1.2%, the larger full.
        Assert.Contains("Since the rules went live on 2 Oct, 4 ideas have been bought and sold on paper. 3 made money.", text, StringComparison.Ordinal);
        Assert.Contains("Still early: each rule needs about 300 finished trades for a fair verdict.", text, StringComparison.Ordinal);
        Assert.Contains("+3.1%</b><div class=\"track\"><div class=\"fill ours\" style=\"width:100%\">", Regex.Replace(page, "</b></div>", "</b>"), StringComparison.Ordinal);
        Assert.Contains("+1.2%</b><div class=\"track\"><div class=\"fill index\" style=\"width:38.7%\">", Regex.Replace(page, "</b></div>", "</b>"), StringComparison.Ordinal);
        Assert.Contains("<a href=\"#/picks\">See every past idea</a>", page, StringComparison.Ordinal);

        // How it works: each tile opening Tonight at its family's card; and the foot with the disclaimer beneath it.
        Assert.Equal(
            ["#/tonight?setup=pullback", "#/tonight?setup=breakout", "#/tonight?setup=drift", "#/tonight?setup=improving"],
            Regex.Matches(Regex.Match(page, "<div class=\"card-b home-learn\">.*?</section>", RegexOptions.Singleline).Value, "<a href=\"([^\"]+)\"").Select(match => WebUtility.HtmlDecode(match.Groups[1].Value)));
        Assert.Contains("<div class=\"home-foot\"><span>Want every number, every rule and every version being tested?</span><a class=\"go\" href=\"#/tonight\">Open the full Tonight page</a></div>", page, StringComparison.Ordinal);
        Assert.EndsWith($"<p class=\"home-disclaimer\">{SinglePageApp.Disclaimer}</p></section>", page, StringComparison.Ordinal);

        // No word Home bans reaches the reader.
        Assert.DoesNotMatch(new Regex("\\b(breadth|stops?|setups?|variants?|edges?|R|risk units|typical days' moves)\\b", RegexOptions.IgnoreCase), text);
    }

    [Fact]
    public void HomeWithNoIdeasAndNoFiguresSaysSoAndTheS400sLinksKeepItsIndex()
    {
        var weak = Day(share: 0.40) with { NoIdeas = "The market was too weak for the rules to buy tonight." };
        var page = new SinglePageApp().HomeRegion(new MarkRenderer(), weak, string.Empty);

        Assert.Contains("<p class=\"card-summary\" data-ideas=\"none\">No new ideas tonight. The market was too weak for the rules to buy tonight.</p>", page, StringComparison.Ordinal);
        Assert.Contains("Tonight's ideas: 0 stocks to buy Monday morning", TextOf(page), StringComparison.Ordinal);
        Assert.Contains("No idea has been bought on paper on this index yet.", TextOf(page), StringComparison.Ordinal);

        // Nothing stored: each tile says so and draws no blank.
        var bare = new SinglePageApp().HomeRegion(new MarkRenderer(), Day(share: null, moved: null, vix: null, above: null), string.Empty);

        Assert.Equal(3, Regex.Matches(bare, "<span class=\"home-meaning\">No figure stored for today</span>").Count);
        Assert.DoesNotContain("class=\"market-bar\"", bare, StringComparison.Ordinal);

        // The S&P 400: its own share with no floor marked, the rules open in the tile, and every link to Tonight keeping it.
        var mid = new SinglePageApp().HomeRegion(new MarkRenderer(), Mid(2), string.Empty);

        Assert.Contains("data-headline=\"mixed\"", mid, StringComparison.Ordinal);
        Assert.DoesNotContain("bar-floor", mid, StringComparison.Ordinal);
        Assert.Contains("52% of these companies trade above their 200-day average. Each rule here checks its own market level; tonight 2 of 3 are open to buy.", TextOf(mid), StringComparison.Ordinal);
        Assert.Contains("data-signal=\"trend\" data-tone=\"care\"", mid, StringComparison.Ordinal);
        Assert.Contains("<a class=\"go\" href=\"#/tonight?universe=400\">", mid, StringComparison.Ordinal);
        Assert.Contains("href=\"#/tonight?universe=400&amp;setup=drift\"", mid, StringComparison.Ordinal);
    }

    // A figure rounding to nought draws no sign and no hue, either side of nought and at nought, and any other its own.
    [Theory]
    [InlineData(0.05, "+5.0%", "up")]
    [InlineData(-0.05, "-5.0%", "down")]
    [InlineData(0.0004, "0.0%", null)]
    [InlineData(-0.0004, "0.0%", null)]
    [InlineData(0.0, "0.0%", null)]
    public void AHomeBarDrawsNoSignOnANought(double value, string shown, string? hue)
    {
        var bar = SinglePageApp.Bar("ours", "Ours", value, 1);

        Assert.Contains($">{shown}</b>", bar, StringComparison.Ordinal);
        Assert.Equal(hue is null, !bar.Contains("color:var(--", StringComparison.Ordinal));

        if (hue is not null)
        {
            Assert.Contains($"color:var(--{hue})", bar, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task HomeIsDrawnAtItsRouteOnEveryIndexAndTheBarOpensItBeforeTonight()
    {
        using var store = await FixtureExpectations.WithListings();
        using var host = new PassHost(store.Root) { Clock = FixedClock.At(DateTimeOffset.Parse("2026-10-10T16:00:00Z", CultureInfo.InvariantCulture), SessionZones.UnitedStates) };
        using var client = host.CreateClient();

        foreach (var index in Universes.Offered)
        {
            var page = await client.GetStringAsync("/screens/home" + (index == Universes.Large ? string.Empty : "?universe=" + index.Word));

            Assert.Contains(WebUtility.HtmlEncode("Home: " + index.Titled), page, StringComparison.Ordinal);
            Assert.Contains("<p class=\"home-disclaimer\">", page, StringComparison.Ordinal);
        }

        // The shell's bar: Home first, then Tonight at its own route, and the brand opening Home.
        var shell = await client.GetStringAsync("/");

        Assert.Contains("<a class=\"m-brand\" href=\"#/\">", shell, StringComparison.Ordinal);
        Assert.Contains("<a href=\"#/\" data-view=\"home\">Home</a><a href=\"#/tonight\" data-view=\"tonight\">Tonight</a>", shell, StringComparison.Ordinal);
        Assert.Contains("fetch('/screens/home'", shell, StringComparison.Ordinal);
    }

    [Fact]
    public void TheReadOnlyCopyHoldsHomeAsItsLandingRouteAndTonightAtItsOwn()
    {
        Assert.Equal([("#/", "Home"), ("#/tonight", "Tonight")], SiteCopy.Menu.Take(2).Select(entry => (entry.Route, entry.Words)));
        Assert.Equal("/screens/home?universe=400", SiteCopy.ServerRoute(SiteCopy.Canonical("#/?universe=400")));
        Assert.Equal("/screens/tonight", SiteCopy.ServerRoute(SiteCopy.Canonical("#/tonight?setup=drift")));
        Assert.Equal(["#/", "#/tonight"], EquityBrief.Api.Cloudflare.SiteCopyCommand.IndexScreens.Take(2));

        // Home and Tonight are written to files of their own, so neither is drawn over the other.
        Assert.Equal("screens/home.html", SiteCopy.FileOf("#/"));
        Assert.Equal("screens/tonight.html", SiteCopy.FileOf("#/tonight"));
        Assert.Equal("screens/home~universe-400.html", SiteCopy.FileOf(SiteCopy.Canonical("#/?universe=400")));

        // Home's head carries the copy's own mark, the night's close, and no second line.
        var marked = SiteCopy.HomeMarked("<span class=\"m-asof\">Night of 2026-10-30, computed after the close</span>", HomeNight);

        Assert.Equal("<span class=\"m-asof\">As of the close of 30 Oct</span>", marked);
    }
}
