using System.Net;
using System.Text.RegularExpressions;
using EquityBrief.Core.Families;
using EquityBrief.Tests.Harness;
using EquityBrief.Web.App;
using EquityBrief.Web.Marks;
using Xunit;

namespace EquityBrief.Tests.Reading;

// The 22.16 correction of 2026-11-09: Home draws a card only for a pick whose setup made money over the long history or has no
// long record yet, its heading counting those cards; the rest are named on one line as watched, and the link to Tonight counts
// every pick.
// see: Home draws a card only for a pick whose setup made money over the long history or has none, and names the rest as watched
public partial class ReadSurface
{
    internal static string[] HomeWatchingRows => [CheckReach.Key("15.24 Home", "Watching, not buying")];

    static HomeIdea Watched(string family, string ticker) =>
        new(family, ticker, null, "Why.", 100m, 90m, 120m, false, null, LongRecord: new SetupLongRecord("GSPC", family, -0.12, 2933, 2019, 2026));

    [Fact]
    public void HomeNamesThePicksOfALosingSetupOnOneLineAndCountsOnlyItsCardsInTheHeading()
    {
        var card = new HomeIdea(ImprovingRule.Name, "CMI", null, "Bought.", 400m, null, null, false, null, LongRecord: SetupLongRecord.Filings("GSPC", ImprovingRule.Name));
        var watching = new[] { Watched(SetupFamilies.Pullback, "BMY"), Watched(SetupFamilies.Pullback, "CTSH"), Watched(DriftRule.Name, "ABNB") };
        var region = new SinglePageApp().HomeRegion(new MarkRenderer(), Day() with { Ideas = [card], IdeasCount = 2, LeftOut = [card with { Ticker = "KMB" }], Watching = watching }, string.Empty);
        var text = Read(region);

        // The heading counts the one card drawn and the one left out for space, two; the line names the three watched in
        // Tonight's order, a family's stocks together; the link counts all five.
        Assert.Contains($"Tonight's ideas: 2 stocks to buy {Day().OpensOn} morning", text, StringComparison.Ordinal);
        Assert.Contains(
            "Watching, not buying: 3 picks tonight. BMY and CTSH (Bounce in an uptrend), ABNB (Strong earnings). These setups have not made money on the S&P 500 since 2019 with their plans as they stand, so they are traded on paper only. See them on Tonight.",
            text,
            StringComparison.Ordinal);
        Assert.Contains("See all 5 picks with their full plans", text, StringComparison.Ordinal);
        Assert.Single(Regex.Matches(region, "<article class=\"home-idea"));
        Assert.True(region.IndexOf("home-more", StringComparison.Ordinal) < region.IndexOf("home-watch", StringComparison.Ordinal));
        Assert.True(region.IndexOf("home-watch", StringComparison.Ordinal) < region.IndexOf("See all 5", StringComparison.Ordinal));

        // One setup, one pick: the singular; and none watched draws no line.
        var one = Read(new SinglePageApp().HomeRegion(new MarkRenderer(), Day() with { Ideas = [card], IdeasCount = 1, Watching = [Watched(DriftRule.Name, "ABNB")] }, string.Empty));

        Assert.Contains(
            "Watching, not buying: 1 pick tonight. ABNB (Strong earnings). This setup has not made money on the S&P 500 since 2019 with its plan as it stands, so it is traded on paper only. See it on Tonight.",
            one,
            StringComparison.Ordinal);
        Assert.DoesNotContain("home-watch", new SinglePageApp().HomeRegion(new MarkRenderer(), Day() with { Ideas = [card], IdeasCount = 1 }, string.Empty), StringComparison.Ordinal);
    }

    [Fact]
    public async Task HomeOnTheSAndP400DrawsTheBreakoutsPickAsWatchedOnceItsSetupLostAndTheFundamentalsPickAsACard()
    {
        using var store = UniversesStore();

        ImprovingNightOn(store);
        SeedPlanPoints(store);

        using var host = new PassHost(store.Root);
        using var client = host.CreateClient();

        // The S&P 400's breakout made money: M1's breakout pick and its Fundamentals buy are both cards, nothing watched.
        var made = WebUtility.HtmlDecode(await client.GetStringAsync("/screens/home?universe=400"));

        Assert.Equal(2, Regex.Matches(made, "<article class=\"home-idea").Count);
        Assert.DoesNotContain("home-watch", made, StringComparison.Ordinal);

        // A newer run finds it lost: its pick is named as watched, the heading counts the Fundamentals card alone and the link both.
        store.Execute("INSERT INTO plan_point (run_id, index_code, family, through, setups, picks, traded_later_edge_per_pick, traded_later_picks) VALUES ('newer', 'MID', 'breakout', '2026-11-06', 10, 5, -0.2, 600);");

        var lost = WebUtility.HtmlDecode(await client.GetStringAsync("/screens/home?universe=400"));
        var text = Read(lost);

        Assert.Equal(["improving"], Regex.Matches(lost, "<article class=\"home-idea fam-edge\" data-family=\"([a-z]+)\"").Select(match => match.Groups[1].Value));
        Assert.Contains("Tonight's ideas: 1 stock to buy", text, StringComparison.Ordinal);
        Assert.Contains("Watching, not buying: 1 pick tonight. M1 (New high). This setup has not made money on the S&P 400 since 2019 with its plan as it stands, so it is traded on paper only. See it on Tonight.", text, StringComparison.Ordinal);
        Assert.Contains("See all 2 picks with their full plans", text, StringComparison.Ordinal);
    }
}
