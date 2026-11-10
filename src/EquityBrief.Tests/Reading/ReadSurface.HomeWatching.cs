using System.Net;
using System.Text.RegularExpressions;
using EquityBrief.Core.Families;
using EquityBrief.Tests.Harness;
using EquityBrief.Web.App;
using EquityBrief.Web.Marks;
using Xunit;

namespace EquityBrief.Tests.Reading;

// Home draws every pick of the night as a card: the ideas to buy first and the picks whose setup lost over the long history
// after them as watched, each in Tonight's order. A line above the cards says what splits them, each card opens on whether it
// is an idea to buy or watched and why, the picks watched carry a heading saying why none is offered to buy, and a watched
// card's buy reads as a paper buy. The heading counts the ideas to buy, and the link to Tonight counts every pick.
// see: Home draws every pick as a card in Tonight's order, the ideas to buy first and the watched after them with each card saying why
public partial class ReadSurface
{
    internal static string[] HomeWatchingRows => [CheckReach.Key("15.24 Home", "Watching, not buying")];

    static HomeIdea Watched(string family, string ticker) =>
        new(family, ticker, null, "Why.", 100m, 90m, 120m, false, null, LongRecord: new SetupLongRecord("GSPC", family, -0.12, 2933, 2019, 2026));

    static string[] Tickers(string region, string group) =>
        [.. Regex.Matches(region, "<div class=\"home-ideas\" data-group=\"" + group + "\">(.*?)</article></div>", RegexOptions.Singleline)
            .SelectMany(block => Regex.Matches(block.Groups[1].Value, "<article class=\"home-idea[^\"]*\" data-family=\"[^\"]+\" data-ticker=\"([A-Z0-9]+)\"").Select(match => match.Groups[1].Value))];

    [Fact]
    public void HomeDrawsEveryPickAsACardTheIdeasToBuyFirstAndTheWatchedAfterThemEachSayingWhy()
    {
        var filings = new HomeIdea(ImprovingRule.Name, "CMI", null, "Bought.", 400m, null, null, false, null, LongRecord: SetupLongRecord.Filings("GSPC", ImprovingRule.Name));
        var made = new HomeIdea(DipRule.Name, "NUE", null, "Dipped.", 150m, 140m, 160m, false, null, LongRecord: new SetupLongRecord("GSPC", DipRule.Name, 0.06, 512, 2019, 2026));
        var watching = new[] { Watched(SetupFamilies.Pullback, "BMY"), Watched(SetupFamilies.Pullback, "CTSH"), Watched(DriftRule.Name, "ABNB") };
        var region = WebUtility.HtmlDecode(new SinglePageApp().HomeRegion(new MarkRenderer(), Day() with { Ideas = [filings, made], IdeasCount = 2, Watching = watching }, string.Empty));
        var text = Read(region);

        // Every pick a card: the two to buy, then the three watched, each group in Tonight's order; no pick is left to Tonight.
        Assert.Equal(["CMI", "NUE"], Tickers(region, "buy"));
        Assert.Equal(["BMY", "CTSH", "ABNB"], Tickers(region, "watch"));
        Assert.Equal(5, Regex.Matches(region, "<article class=\"home-idea").Count);
        Assert.Equal(3, Regex.Matches(region, "<article class=\"home-idea fam-edge watched\"").Count);
        Assert.DoesNotContain("more on Tonight", text, StringComparison.Ordinal);

        // The heading counts the ideas to buy; the line above the cards says what splits them; the link counts all five.
        Assert.Contains($"Tonight's ideas: 2 stocks to buy {Day().OpensOn} morning", text, StringComparison.Ordinal);
        Assert.Contains("A pick is an idea to buy unless picks like it did worse than the S&P 500's other stocks bought the same way since 2019; then it is watched and traded on paper only. Each card says which, and why.", text, StringComparison.Ordinal);
        Assert.Contains("See all 5 picks with their full plans", text, StringComparison.Ordinal);

        // The picks watched under a heading saying why, between the last idea to buy and the first watched card.
        Assert.Contains(
            "Watching, not buying: 3 picks tonight. Picks like these did worse than the S&P 500's other stocks bought the same way since 2019, with their plans as they stand, so they are traded on paper only, not offered as ideas to buy.",
            text,
            StringComparison.Ordinal);
        Assert.True(region.IndexOf("data-ticker=\"NUE\"", StringComparison.Ordinal) < region.IndexOf("class=\"home-watch\"", StringComparison.Ordinal));
        Assert.True(region.IndexOf("class=\"home-watch\"", StringComparison.Ordinal) < region.IndexOf("data-ticker=\"BMY\"", StringComparison.Ordinal));

        // Each card opens on which it is and why, from its setup's record.
        Assert.Contains("An idea to buy. Picks like this have no long record yet to hold it back.", text, StringComparison.Ordinal);
        Assert.Contains("An idea to buy. Since 2019, picks like this did better than the S&P 500's other stocks bought the same way.", text, StringComparison.Ordinal);
        Assert.Equal(3, Regex.Matches(text, Regex.Escape("Watching, not buying. Since 2019, picks like this did worse than the S&P 500's other stocks bought the same way, so this one is traded on paper only.")).Count);
        Assert.Equal(2, Regex.Matches(region, "<p class=\"home-verdict\" data-verdict=\"buy\">").Count);
        Assert.Equal(3, Regex.Matches(region, "<p class=\"home-verdict\" data-verdict=\"watch\">").Count);

        // A watched card's buy is a paper buy; an idea's is a buy.
        Assert.Equal(3, Regex.Matches(region, "<span>Paper buy at about</span>").Count);
        Assert.Equal(2, Regex.Matches(region, "<span>Buy at about</span>").Count);

        // A watched card says when it would become an idea to buy, beneath its opening line, and names its sentence as why its
        // rule picked it; an idea's card does neither.
        Assert.Equal(3, Regex.Matches(region, "<p class=\"home-verdict\" data-verdict=\"watch\"><b>Watching, not buying\\.</b>[^<]*</p>" + Regex.Escape("<p class=\"home-when\">It becomes an idea to buy when picks like this, traded the way they are now, beat the S&P 500's other stocks bought the same way since 2019.</p><p class=\"home-why\"><b>Why the rule picked it:</b> Why.</p>")).Count);
        Assert.Equal(3, Regex.Matches(region, "<p class=\"home-when\">").Count);
        Assert.Contains("<p class=\"home-why\">Dipped.</p>", region, StringComparison.Ordinal);
        Assert.Contains("<p class=\"home-why\">Bought.</p>", region, StringComparison.Ordinal);

        // Nine picks, two of them from a setup that lost: all seven others are ideas to buy and both watched, none left out,
        // each group in the order given.
        var nine = new[] { "A1", "A2", "W1", "A3", "A4", "A5", "W2", "A6", "A7" }
            .Select(ticker => ticker[0] == 'W' ? Watched(SetupFamilies.Pullback, ticker) : made with { Ticker = ticker })
            .ToArray();
        var (buy, watch) = SinglePageApp.Split(nine);

        Assert.Equal(["A1", "A2", "A3", "A4", "A5", "A6", "A7"], buy.Select(pick => pick.Ticker));
        Assert.Equal(["W1", "W2"], watch.Select(pick => pick.Ticker));
    }

    [Fact]
    public void HomeSaysWhyForOnePickWatchedForAPickOfTooFewOrUnreadAndForANightEveryPickIsWatched()
    {
        var short12 = new HomeIdea(BreakoutRule.Name, "SHRT", null, "Broke out.", 50m, 45m, null, true, null, LongRecord: new SetupLongRecord("GSPC", BreakoutRule.Name, 0.3, 12, 2019, 2026));
        var unread = new HomeIdea(DriftRule.Name, "UNRD", null, "Beat.", 60m, 55m, 70m, false, null);
        var one = Read(WebUtility.HtmlDecode(new SinglePageApp().HomeRegion(new MarkRenderer(), Day() with { Ideas = [short12, unread], IdeasCount = 2, Watching = [Watched(DriftRule.Name, "ABNB")] }, string.Empty)));

        Assert.Contains(
            "Watching, not buying: 1 pick tonight. Picks like it did worse than the S&P 500's other stocks bought the same way since 2019, with its plan as it stands, so it is traded on paper only, not offered as an idea to buy.",
            one,
            StringComparison.Ordinal);
        Assert.Contains("An idea to buy. Too few picks like this since 2019 for a record to hold it back.", one, StringComparison.Ordinal);
        Assert.Contains("An idea to buy. No long record of picks like this has been read yet to hold it back.", one, StringComparison.Ordinal);

        // None watched: no heading and no watched group.
        var none = new SinglePageApp().HomeRegion(new MarkRenderer(), Day() with { Ideas = [unread], IdeasCount = 1 }, string.Empty);

        Assert.DoesNotContain("home-watch", none, StringComparison.Ordinal);
        Assert.DoesNotContain("data-group=\"watch\"", none, StringComparison.Ordinal);

        // Every pick watched: no idea to buy, said so, and every watched pick still a card.
        var all = WebUtility.HtmlDecode(new SinglePageApp().HomeRegion(new MarkRenderer(), Day() with { Ideas = [], IdeasCount = 0, Watching = [Watched(SetupFamilies.Pullback, "BMY"), Watched(DriftRule.Name, "ABNB")] }, string.Empty));

        Assert.Contains("No new ideas tonight. Every pick tonight is watched, not offered as an idea to buy: each card below says why.", Read(all), StringComparison.Ordinal);
        Assert.Equal(["BMY", "ABNB"], Tickers(all, "watch"));
        Assert.DoesNotContain("data-group=\"buy\"", all, StringComparison.Ordinal);
    }

    [Fact]
    public async Task HomeOnTheSAndP400DrawsTheBreakoutsPickAsWatchedOnceItsSetupLostAndTheFundamentalsPickAsAnIdea()
    {
        using var store = UniversesStore();

        ImprovingNightOn(store);
        SeedPlanPoints(store);

        using var host = new PassHost(store.Root);
        using var client = host.CreateClient();

        // The S&P 400's breakout made money: M1's breakout pick and its Fundamentals buy are both ideas to buy, nothing watched.
        var made = WebUtility.HtmlDecode(await client.GetStringAsync("/screens/home?universe=400"));

        Assert.Equal(2, Regex.Matches(made, "<article class=\"home-idea fam-edge\" data-family").Count);
        Assert.DoesNotContain("home-watch", made, StringComparison.Ordinal);

        // A newer run finds it lost: its pick is a watched card saying why, the heading counts the Fundamentals card alone and
        // the link both.
        store.Execute("INSERT INTO plan_point (run_id, index_code, family, through, setups, picks, traded_later_edge_per_pick, traded_later_picks) VALUES ('newer', 'MID', 'breakout', '2026-11-06', 10, 5, -0.2, 600);");

        var lost = WebUtility.HtmlDecode(await client.GetStringAsync("/screens/home?universe=400"));
        var text = Read(lost);

        Assert.Equal(["improving"], Regex.Matches(lost, "<article class=\"home-idea fam-edge\" data-family=\"([a-z]+)\"").Select(match => match.Groups[1].Value));
        Assert.Equal(["breakout"], Regex.Matches(lost, "<article class=\"home-idea fam-edge watched\" data-family=\"([a-z]+)\"").Select(match => match.Groups[1].Value));
        Assert.Contains("Tonight's ideas: 1 stock to buy", text, StringComparison.Ordinal);
        Assert.Contains("Watching, not buying: 1 pick tonight. Picks like it did worse than the S&P 400's other stocks bought the same way since 2019, with its plan as it stands, so it is traded on paper only, not offered as an idea to buy.", text, StringComparison.Ordinal);
        Assert.Contains("Watching, not buying. Since 2019, picks like this did worse than the S&P 400's other stocks bought the same way, so this one is traded on paper only.", text, StringComparison.Ordinal);
        Assert.Contains("<p class=\"home-when\">It becomes an idea to buy when picks like this, traded the way they are now, beat the S&P 400's other stocks bought the same way since 2019.</p><p class=\"home-why\"><b>Why the rule picked it:</b> ", lost, StringComparison.Ordinal);
        Assert.Contains("See all 2 picks with their full plans", text, StringComparison.Ordinal);
    }
}
