using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using EquityBrief.Api.Reading;
using EquityBrief.Core.Candidates;
using EquityBrief.Core.Cards;
using EquityBrief.Core.Loop;
using EquityBrief.Core.Paper;
using EquityBrief.Tests.Harness;
using EquityBrief.Web.App;
using EquityBrief.Worker.Indices;
using EquityBrief.Web.Marks;
using Xunit;

namespace EquityBrief.Tests.Reading;

// From the operator's correction of 2026-11-24 at about 11:30 pm: what is picked, ordered, bought and shown agrees across Home,
// Tonight and Paper trading. A stock a rule's live account already holds is met again, never ordered twice; an order of a
// replaced revision is cancelled; every order that leaves unbought and every buy and sale stands in Home's notes; Home's
// account names itself; See the plan opens the chart page; a card labels the rule's checks and the traded plan apart; and the
// reconciliation holds the store to the three pages.
// see: The reconciliation holds every live rule's order and trade to what Home, Tonight and Paper trading draw, and fails on any mismatch
public partial class ReadSurface
{
    static readonly DateOnly AgreeNight = new(2026, 11, 24);

    static readonly DateOnly[] AgreeSessions =
    [
        new(2026, 11, 16), new(2026, 11, 17), new(2026, 11, 18), new(2026, 11, 19), new(2026, 11, 20), new(2026, 11, 23), new(2026, 11, 24),
    ];

    const string AgreeLive = "the live pullback rule";

    static PlanOrder AtLevel(double? level, int window = 20, string entry = "1.13 typical days under the close") =>
        new("words", entry, level, window, PlanPoints.TypicalStop(4), 90, null, 2, null, [], null, null, 63);

    static PlannedTradeRow AgreeOrder(string family, string ticker, DateOnly listed, double? level, DateOnly? filledOn = null, decimal? fill = null, bool notFilled = false, DateOnly? endedOn = null, string owner = AgreeLive) =>
        new(owner, "GSPC", family, ticker, listed, 1, 100m, 90m, null, null, 63, AtLevel(level), filledOn, fill, notFilled, endedOn, null, endedOn is null ? null : 95m);

    // MSCI bought on 20 Nov and open, EBAY waiting from 20 Nov, HUM placed with no price and ended on its night, AIZ sold on 17
    // Nov: on 24 Nov the live rule holds MSCI and EBAY and neither of the others.
    [Fact]
    public void AStockTheLiveRuleHoldsInAnOpenTradeOrAWaitingOrderIsHeldAndNoOther()
    {
        PlannedTradeRow[] planned =
        [
            AgreeOrder("pullback", "MSCI", new(2026, 11, 17), 618.47, new(2026, 11, 20), 618.47m),
            AgreeOrder("pullback", "EBAY", new(2026, 11, 20), 111.33),
            AgreeOrder("dip", "HUM", new(2026, 11, 18), null, notFilled: true),
            AgreeOrder("pullback", "AIZ", new(2026, 11, 4), 300, new(2026, 11, 5), 300m, endedOn: new(2026, 11, 17)),
            AgreeOrder("pullback", "BACK", new(2026, 11, 20), 50, owner: "a background pullback rule"),
        ];

        var held = PlannedTrades.HeldOn(planned, AgreeNight);

        Assert.Equal([("pullback", "EBAY"), ("pullback", "MSCI")], held.Keys.Order());
        Assert.DoesNotContain(("pullback", "MSCI"), PlannedTrades.HeldOn(planned, new DateOnly(2026, 11, 17)).Keys);
    }

    // HUM's dip order placed no price on 18 and 19 Nov and its step ended each on its night; FERG's order of a revision replaced
    // on 16 Nov was cancelled when its fill row said so on 25 Nov; CSCO was bought by the Fundamentals family on 19 Nov at
    // $98.99 and sold on 20 Nov at $90.64, 8.4% under: every one stands in Home's notes with why, worked by hand.
    [Fact]
    public void EveryOrderThatLeftUnboughtAndEveryBuyAndSaleStandsInHomesNotesWithWhy()
    {
        PlannedTradeRow[] planned =
        [
            AgreeOrder("dip", "HUM", new(2026, 11, 18), null, notFilled: true) with { Order = AtLevel(null, 10, "the second zone") },
            AgreeOrder("dip", "HUM", new(2026, 11, 19), null, notFilled: true) with { Order = AtLevel(null, 10, "the second zone") },
            AgreeOrder("drift", "FERG", new(2026, 11, 10), 246.4, notFilled: true) with { Why = PlanOrderWords.Replaced(new DateOnly(2026, 11, 16)), WhyOn = new DateOnly(2026, 11, 25) },
            AgreeOrder("pullback", "EBAY", new(2026, 11, 20), 111.33),
        ];
        var csco = new PaperTrade("CSCO", new DateOnly(2026, 11, 19), 98.99m, 91.55m, new DateOnly(2026, 11, 20), 90.64m, null, 1, Listed: new DateOnly(2026, 11, 18));
        var fundamentals = new PaperAccountSource("list-improving", "improving", PaperAccountView.Live, "Live", "the live Fundamentals rule", null, true, [(csco, -0.084)], new DateOnly(2026, 11, 16));

        var left = RevisionScreen.LeftOf(planned, AgreeNight, AgreeSessions, [fundamentals]);
        string Said(string ticker, DateOnly on) => Assert.Single(left, one => one.Ticker == ticker && one.On == on).Words;

        Assert.Equal("HUM not placed: " + PlanOrderWords.NoPrice, Said("HUM", new DateOnly(2026, 11, 18)));
        Assert.Equal("HUM not placed: " + PlanOrderWords.NoPrice, Said("HUM", new DateOnly(2026, 11, 19)));
        Assert.Equal("FERG cancelled: its revision was replaced on 16 Nov.", Said("FERG", AgreeNight));
        Assert.Equal("CSCO bought 19 Nov at $98.99, at the open.", Assert.Single(left, one => one.Ticker == "CSCO" && !one.Sold).Words);
        Assert.Equal("CSCO sold 20 Nov at $90.64, -8.4% on its buy of $98.99.", Assert.Single(left, one => one.Ticker == "CSCO" && one.Sold).Words);
        Assert.DoesNotContain(left, one => one.Ticker == "EBAY");

        // Home draws them, a sale marked apart, under a heading naming all three.
        var notes = MarkRenderer.LeftNotes(left);

        Assert.Contains($"<p class=\"list-count\">{MarkRenderer.LeftHeading} in the last 5 trading days (5)</p>", notes, StringComparison.Ordinal);
        Assert.Contains("<li class=\"sd\" data-ticker=\"CSCO\" data-family=\"improving\" data-on=\"2026-11-20\">", notes, StringComparison.Ordinal);
        Assert.Contains("<li class=\"cx\" data-ticker=\"FERG\" data-family=\"drift\" data-on=\"2026-11-24\">", notes, StringComparison.Ordinal);

        // Paper trading's own lists take the cancelled order out of the waiting.
        Assert.DoesNotContain(PlanScreen.StillWaiting(planned, AgreeNight), order => order.Ticker == "FERG");
    }

    // The reconciliation over constructed pages: EBAY waiting from 20 Nov drawn on Home, Tonight and Paper trading, MRNA placed
    // tonight Home's idea and Tonight's pick; nothing amiss. Planted: EBAY's card taken off Home, MSCI drawn as tonight's idea
    // and waited on by Paper trading with no order, FERG's order of a replaced revision still waiting.
    [Fact]
    public void TheReconciliationFindsNoMismatchWhereThePagesAgreeAndNamesEachOnePlanted()
    {
        PlannedTradeRow[] planned =
        [
            AgreeOrder("pullback", "EBAY", new(2026, 11, 20), 111.33),
            AgreeOrder("pullback", "MRNA", AgreeNight, 206.71),
        ];
        static string Card(string family, string ticker, string listed) => $"<article class=\"wo-card fam-edge\" data-family=\"{family}\" data-ticker=\"{ticker}\" data-listed=\"{listed}\" data-price=\"1\">";
        var home = Card("pullback", "EBAY", "2026-11-20") + "<article class=\"home-idea fam-edge\" data-family=\"pullback\" data-ticker=\"MRNA\">";
        var tonight = Card("pullback", "EBAY", "2026-11-20") + "<tr data-ticker=\"MRNA\" data-family=\"pullback\" data-place=\"1\">";
        var paper = "<tr data-ticker=\"EBAY\" data-placed=\"2026-11-20\" data-take-profit=\"1\"><tr data-ticker=\"MRNA\" data-placed=\"2026-11-24\" data-take-profit=\"1\">";
        var nights = new Dictionary<DateOnly, string>
        {
            [new DateOnly(2026, 11, 20)] = "<tr data-ticker=\"EBAY\" data-family=\"pullback\" data-place=\"1\">",
            [AgreeNight] = tonight,
        };
        static DateOnly? Never(string index, string owner, DateOnly listed) => null;

        var lines = PicksProof.Check("GSPC", AgreeNight, new DateOnly(2026, 11, 16), planned, AgreeSessions, Never, new PicksProof.DrawnPages(home, tonight, nights, new Dictionary<string, string> { ["pullback"] = paper }), out var extras);

        Assert.Equal(2, lines.Count);
        Assert.All(lines, line => Assert.Empty(line.Mismatches));
        Assert.Empty(extras);
        Assert.Contains($"{PicksProof.Tally} 0\n", PicksProof.Page([("S&P 500", lines, extras)], text: true), StringComparison.Ordinal);

        // EBAY's waiting card taken off Home.
        var missing = PicksProof.Check("GSPC", AgreeNight, new DateOnly(2026, 11, 16), planned, AgreeSessions, Never, new PicksProof.DrawnPages(home.Replace(Card("pullback", "EBAY", "2026-11-20"), string.Empty, StringComparison.Ordinal), tonight, nights, new Dictionary<string, string> { ["pullback"] = paper }), out _);

        Assert.Equal(["Home shows nothing, the store says waiting card"], missing.Single(line => line.Ticker == "EBAY").Mismatches);

        // MSCI drawn as tonight's idea and waited on by Paper trading, no live order standing for it.
        PicksProof.Check("GSPC", AgreeNight, new DateOnly(2026, 11, 16), planned, AgreeSessions, Never, new PicksProof.DrawnPages(home + "<article class=\"home-idea fam-edge\" data-family=\"pullback\" data-ticker=\"MSCI\">", tonight, nights, new Dictionary<string, string> { ["pullback"] = paper + "<tr data-ticker=\"MSCI\" data-placed=\"2026-11-24\" data-take-profit=\"1\">" }), out var msci);

        Assert.Equal(["pullback MSCI: Home's idea tonight with no order of its live rule", "pullback MSCI placed 2026-11-24: Paper trading waits on it and no live order stands for it"], msci);

        // FERG's order, placed on 10 Nov by a revision replaced on 16 Nov, still waiting and drawn so.
        PlannedTradeRow[] ferg = [.. planned, AgreeOrder("drift", "FERG", new(2026, 11, 10), 246.4)];
        var replaced = PicksProof.Check("GSPC", AgreeNight, new DateOnly(2026, 11, 16), ferg, AgreeSessions, (index, owner, listed) => listed < new DateOnly(2026, 11, 16) ? new DateOnly(2026, 11, 16) : null, new PicksProof.DrawnPages(home + Card("drift", "FERG", "2026-11-10"), tonight, nights, new Dictionary<string, string> { ["pullback"] = paper }), out _);

        Assert.Contains("its revision was replaced on 2026-11-16 and its order still waits", replaced.Single(line => line.Ticker == "FERG").Mismatches);
    }

    // Home's account names itself as Paper trading names it, and how many accounts it averages.
    [Fact]
    public void HomesAccountFigureIsNamedAsTheRunningRulesAccountsAveraged()
    {
        var past = new HomePast(new DateOnly(2026, 11, 16), 3, 1, 0.0004, -0.029, true, "SPY", new DateOnly(2026, 11, 16)) { Accounts = 7 };

        Assert.Equal("The running rules' accounts averaged, since 16 Nov", SinglePageApp.AveragedLabel(past));
        Assert.Equal($"Since today's rules all took effect on 16 Nov, this account has followed all 7 running rules with an equal share each: Paper trading's \"{SinglePageApp.PaperAveragedName}\", from the same trades. A rule with nothing bought holds its share in cash.", SinglePageApp.AccountLine(past));
    }

    // MRNA's card: its rule's checks read the rule's own stop from tonight's close of $227.38, and the plan it trades buys at
    // $206.71 with its stop at $133.52 and its take-profit at $246.11: $39.40 up for $73.19 down, 0.54 times its risk, worked
    // by hand. The card labels the two apart.
    [Fact]
    public void ACardLabelsTheRulesChecksAndTheTradedPlanApartEachWithItsOwnReward()
    {
        var card = new DecisionCardView("GSPC", AgreeNight, "pullback", "MRNA", "the pullback rule", 206.71m, 133.52m, 246.11m,
            [new CardLineView(1, CardLines.TrendName, "tick", "Passed every step: trade, reward against the risk 2.89 at or above 2; the stop 1.01 typical days' moves below the entry, inside 0.5 to 4.")],
            null)
        {
            Zone = new ZoneBuy(206.71m, 20, 227.38m),
        };

        var (reward, risk) = MarkRenderer.PlanRewardAndRisk(card)!.Value;

        Assert.Equal((39.40m, 73.19m), (reward, risk));

        var drawn = WebUtility.HtmlDecode(new MarkRenderer().DecisionCard(card));

        Assert.Contains($"<h5>{MarkRenderer.RulesChecksHeading}</h5>", drawn, StringComparison.Ordinal);
        Assert.Contains("These checks read the rule's own stop and its reward against the risk from tonight's close of 227.38. The plan it trades, below, buys lower, at 206.71", drawn, StringComparison.Ordinal);
        Assert.Contains("data-plan-reward-to-risk=\"0.54\">The plan it trades: 39.40 up to its take-profit for 73.19 down to its stop from its buy, a reward of 0.54 times its risk.", drawn, StringComparison.Ordinal);

        // A card whose plan buys at the next open keeps its checklist under its own heading, with no line apart.
        var open = new MarkRenderer().DecisionCard(card with { Zone = null });

        Assert.DoesNotContain(MarkRenderer.RulesChecksHeading, open, StringComparison.Ordinal);
        Assert.DoesNotContain("card-plan-rr", open, StringComparison.Ordinal);
    }

    // Every See the plan on Home opens its own stock's chart page, on a night with several ideas, and a card's way to the stock's
    // full page opens the chart page as well.
    [Fact]
    public void EverySeeThePlanOnHomeOpensItsOwnStocksChartPage()
    {
        HomeIdea Idea(string ticker) => new("pullback", ticker, null, "Why.", 100m, 90m, 120m, false, null);

        string[] tickers = ["MRNA", "MA", "SPGI", "APD"];
        var region = new SinglePageApp().HomeRegion(new MarkRenderer(), Day() with { Ideas = [.. tickers.Select(Idea)], IdeasCount = 4 }, string.Empty);
        var links = Regex.Matches(region, "<article class=\"home-idea[^\"]*\" data-family=\"[^\"]+\" data-ticker=\"([^\"]+)\".*?<a class=\"home-plan\" href=\"([^\"]+)\">See the plan</a>", RegexOptions.Singleline);

        Assert.Equal(tickers, links.Select(link => link.Groups[1].Value));
        Assert.All(links, link => Assert.Equal("#/chart/" + link.Groups[1].Value, link.Groups[2].Value));
        Assert.DoesNotContain("href=\"#/tonight?name=", region, StringComparison.Ordinal);

        var card = new DecisionCardView("GSPC", AgreeNight, "pullback", "MA", "the pullback rule", 631.46m, 574.88m, 744.61m, [], null);

        Assert.Contains("<a href=\"#/chart/MA\">The stock's full page</a>", new MarkRenderer().DecisionCard(card, linked: true), StringComparison.Ordinal);
    }

    // CART held by the breakout's open trade, which the pullback's live rule ordered on the night all the same, its own account
    // holding none: its card says so and the price its order buys at, and carries the mark the reconciliation reads.
    [Fact]
    public void AStockAnotherSetupHoldsThatItsOwnRuleOrderedSaysItsOrderStands()
    {
        var held = HeldAgainView.Of("{\"family\":\"breakout\",\"night\":\"2026-10-23\",\"index\":\"MID\",\"ordered\":\"55.33\"}")! with { Fill = 54.31m, BoughtOn = new DateOnly(2026, 10, 26) };

        Assert.True(held.Ordered);
        Assert.Equal(HeldAgainView.OrderedLabel, held.Labelled);
        Assert.Equal("CART met the pullback rules tonight. Breakout bought it on 26 Oct and that trade is still open; the pullback's own account holds no trade in it, so its rule placed its order tonight: it buys if it falls to $55.33.", held.Line("CART", "pullback"));
        Assert.False(HeldAgainView.Of("{\"family\":\"breakout\",\"night\":\"2026-10-23\",\"index\":\"MID\"}")!.Ordered);
    }

    // The S&P 400 breakout's live rule registered again at 22:00 UTC on 1 Oct, before that night ran, at a change, so its new
    // revision counts from the session of 1 Oct and revision 1 is replaced on it: M2's order of 30 Sep, waiting, is cancelled
    // by the fill step with the operator's words; M3's order of 28 Sep, filled on 29 Sep before the change, keeps its fill; the
    // order placed on 2 Oct by the new revision waits on.
    [Fact]
    public async Task TheFillStepCancelsAnOrderStillUnfilledWhenTheRevisionThatPlacedItIsReplaced()
    {
        using var store = PlanTradesStore();
        var breakout = (IndexRuleCandidate)CandidateEvaluators.Find("breakout-400")!;
        var (registrations, _) = IndexRules.Freeze("breakout", "MID", IndexRules.Provisional(breakout), [], [], new DateTimeOffset(2026, 1, 2, 12, 0, 0, TimeSpan.Zero));
        var live = registrations![0];

        RegisteredRule(store, 400, live with { Parameters = new Dictionary<string, double>(live.Parameters) { [IndexRuleCandidate.QualityParameter] = 2 } }, "2026-10-01T22:00:00Z");

        var replacedOn = PlanOrderWords.ReplacedOn(await new EquityBrief.Data.PaperReads(store.DatabaseFile).RegisteredCandidatesAsync());

        Assert.Equal(new DateOnly(2026, 10, 1), replacedOn("MID", live.Candidate, new DateOnly(2026, 9, 30)));
        Assert.Null(replacedOn("MID", live.Candidate, new DateOnly(2026, 10, 2)));

        await new EquityBrief.Worker.Fills.TradeFiller(EquityBrief.Core.Time.FixedClock.At(new DateTimeOffset(2026, 10, 3, 1, 0, 0, TimeSpan.Zero), EquityBrief.Core.Time.SessionZones.UnitedStates), store.DatabaseFile).RunAsync("fills-agree");

        string? Reason(string ticker, string night) => Strings(store, $"SELECT COALESCE(reason, 'none') FROM trade_fill WHERE source = 'index_rule_trade' AND ticker = '{ticker}' AND session_date = '{night}';").SingleOrDefault();

        Assert.Equal("cancelled: its revision was replaced on 1 Oct", Reason("M2", "2026-09-30"));
        Assert.Equal("none", Reason("M3", "2026-09-28"));
        Assert.DoesNotContain("cancelled", Reason("M1", IndexNight) ?? string.Empty, StringComparison.Ordinal);
    }
}
