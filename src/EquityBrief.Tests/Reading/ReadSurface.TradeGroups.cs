using EquityBrief.Api.Reading;
using EquityBrief.Core.Cards;
using EquityBrief.Core.Families;
using EquityBrief.Core.Returns;
using EquityBrief.Web.Marks;
using Xunit;

namespace EquityBrief.Tests.Reading;

// 22.25: every count of trades names its group and comes from one function. The night after a constructed restart: a rule
// that first went live on 2 October and was registered again on 3 November, holding trades of both versions and one stock
// waiting on the open, beside a monthly book and a variant, whose trades are nobody's count.
// see: Every count of trades names its group and one function works the three groups
public partial class ReadSurface
{
    static readonly DateOnly RestartNight = new(2026, 11, 4);

    static PaperTrade BoughtTrade(string ticker, DateOnly bought, DateOnly? ended = null, decimal? sale = null, decimal? target = null, decimal? stop = null) =>
        new(ticker, bought, 100m, stop, ended, sale, null, 1, Target: target);

    static CandidateRow LiveRow(long id, string family, string evaluator, DateTimeOffset at, string candidate = "the live drift rule") =>
        new(id, candidate, evaluator, "registered", null, at);

    static IReadOnlyList<PaperAccountSource> RestartSources() =>
    [
        new PaperAccountSource(
            "list-drift",
            DriftRule.Name,
            PaperAccountView.Live,
            "Earnings drift",
            "the live drift rule",
            null,
            false,
            [
                (BoughtTrade("NEW1", new DateOnly(2026, 11, 4)), null),
            ],
            new DateOnly(2026, 11, 4),
            Earlier:
            [
                (BoughtTrade("WIN", new DateOnly(2026, 10, 5), new DateOnly(2026, 10, 20), 120m, 120m, 95m), 2.0, "the version registered 2026-10-02"),
                (BoughtTrade("LOSS", new DateOnly(2026, 10, 6), new DateOnly(2026, 10, 21), 95m, 120m, 95m), -1.0, "the version registered 2026-10-02"),
                (BoughtTrade("HELD", new DateOnly(2026, 10, 28)), null, "the version registered 2026-10-23"),
            ],
            Unfilled: [("WAIT", RestartNight, PaperAccount.Waiting), ("GONE", RestartNight, "no open was stored")]),
        new PaperAccountSource(
            "book-quality",
            QualityRule.Name,
            PaperAccountView.Live,
            "Quality",
            "the live quality rule",
            null,
            true,
            [],
            new DateOnly(2026, 11, 1),
            Unfilled: [("BOOK1", RestartNight, PaperAccount.Waiting), ("BOOK2", RestartNight, PaperAccount.Waiting)]),
        new PaperAccountSource(
            "variant-drift",
            DriftRule.Name,
            PaperAccountView.Variant,
            "A version",
            "the drift rule at another setting",
            null,
            false,
            [(BoughtTrade("NOBODY", new DateOnly(2026, 11, 4)), null)],
            new DateOnly(2026, 11, 4)),
    ];

    static TradeGroupsView RestartGroups() =>
        TradeGroups.Of(
            RestartSources(),
            RestartNight,
            (_, _) => 110m,
            [
                LiveRow(1, DriftRule.Name, "drift", new DateTimeOffset(2026, 10, 2, 11, 0, 0, TimeSpan.Zero)),
                LiveRow(2, DriftRule.Name, "drift", new DateTimeOffset(2026, 11, 3, 5, 0, 0, TimeSpan.Zero)),
            ],
            "GSPC");

    [Fact]
    public void AfterARestartTheSystemsTradesThisVersionsAndTheEarlierVersionsAreCountedByHand()
    {
        var groups = RestartGroups();

        // Worked by hand: four bought under the rule across its versions, the variant's in no group. Open: NEW1 and HELD.
        // Closed: WIN at its target and LOSS at its stop. This version's: NEW1 alone, its restart reset the verdict's count.
        Assert.Equal(new DateOnly(2026, 10, 2), groups.FirstLive);
        Assert.Equal((4, 2, 2), (groups.System.Bought, groups.System.Open, groups.System.Closed));
        Assert.Equal((1, 1, 1), (groups.System.MadeMoney, groups.System.AtTarget, groups.System.AtStop));
        Assert.Equal((1, 1, 0), (groups.ThisVersion.Bought, groups.ThisVersion.Open, groups.ThisVersion.Closed));
        Assert.Equal((3, 1, 2), (groups.EarlierVersions.Bought, groups.EarlierVersions.Open, groups.EarlierVersions.Closed));

        // To buy: WAIT and the two the book's reshuffle chose, none of them open; GONE holds no fill for its own reason.
        Assert.Equal(3, groups.ToBuy);
        Assert.Equal(2, groups.ToBuyFromReshuffles);
        Assert.Equal(1, groups.NewPicks);

        // The mean over all four: +20%, -5%, and two open at a close of 110 on a buy of 100, +10% each; +8.75%.
        Assert.Equal(0.0875, groups.System.MeanReturn!.Value, 6);
    }

    // A night read after later fills stored: the trade bought at the session after the night was still to buy on it.
    [Fact]
    public void AnEarlierNightCountsATradeBoughtAfterItAsToBuy()
    {
        var groups = TradeGroups.Of(RestartSources(), new DateOnly(2026, 11, 3), (_, _) => 110m, [], "GSPC");

        Assert.Equal((3, 1), (groups.System.Bought, groups.System.Open));
        Assert.Equal((0, 0), (groups.ThisVersion.Bought, groups.ThisVersion.Open));
        Assert.Equal(4, groups.ToBuy);
    }

    // From the 19.4 correction of 2026-11-07 the line names no start of the accounts', each account's day standing on its tile.
    [Fact]
    public void PaperTradingsLineSaysWhatTheSystemBoughtAndWhatTheAccountsCount()
    {
        Assert.Equal(
            "The system has bought 4 trades since 2 Oct: 2 open, 1 reached their target, 1 hit their stop, +8.8% overall.",
            RestartGroups().SystemLine);
    }

    [Fact]
    public void TheStockAnEarlierVersionHoldsIsNamedWithThatVersionAndNoOtherIs()
    {
        var held = Assert.Single(TradeGroups.EarlierHoldings(RestartSources(), "HELD", RestartNight));

        Assert.Equal("the version registered 2026-10-23", held.Version);
        Assert.Equal(
            "An earlier version of Earnings drift still holds this stock, bought on 28 Oct 2026 under the version registered 2026-10-23. It counts among the system's trades and not toward this version's verdict.",
            held.Words("Earnings drift"));

        // One the earlier version sold, and one the current version holds, draw nothing.
        Assert.Empty(TradeGroups.EarlierHoldings(RestartSources(), "WIN", RestartNight));
        Assert.Empty(TradeGroups.EarlierHoldings(RestartSources(), "NEW1", RestartNight));
    }

    const string HomeAfterTheRestart = "<h2>Tonight's ideas: 1 stock to buy Thursday morning</h2><p>All versions since 2 Oct: 2 ideas bought and sold on paper, 1 made money.</p>";
    const string TonightAfterTheRestart = "<p>1 new pick to buy at tomorrow's open, all from the earnings drift.</p><p>Of the system's trades, 2 are open, 1 of them this version's.</p><p>1 stock to buy at tomorrow's open, at about tonight's close.</p>";
    const string PaperAfterTheRestart = "<p>This version's trades: 1 trade is open and 0 have closed, the newest bought on 4 Nov.</p><p>The system has bought 4 trades since 2 Oct: 2 open, 1 reached their target, 1 hit their stop, +8.8% overall.</p><details><summary>Earlier versions: 3 trades a rule's earlier versions listed</summary></details>";

    [Fact]
    public void TheProofPassesEachPageDrawingItsGroupsCountAfterARestart()
    {
        var verdicts = CountsProof.Check(RestartGroups(), HomeAfterTheRestart, TonightAfterTheRestart, PaperAfterTheRestart);

        Assert.Equal(16, verdicts.Count);
        Assert.All(verdicts, verdict => Assert.Equal("pass", verdict.Verdict));
    }

    // A page counting the wrong group after a restart, each of the operator's findings of 2 November.
    [Theory]
    [InlineData("2 ideas bought", "0 ideas bought", "home", "Home's ideas bought and sold are the system's closed")]
    [InlineData(", 1 made money", ", 0 made money", "home", "Home's ideas that made money are the system's")]
    [InlineData("ideas: 1 stock", "ideas: 3 stock", "home", "Home's stocks to buy are the new picks")]
    [InlineData("trades, 2 are open", "trades, 1 are open", "tonight", "Tonight's open trades are the system's")]
    [InlineData("1 of them this", "2 of them this", "tonight", "Tonight's open trades of this version are this version's")]
    [InlineData("1 new pick to", "3 new picks to", "tonight", "Tonight's new picks are those to buy less the reshuffle")]
    [InlineData("1 stock to buy at tomorrow's open, at", "3 stocks to buy at tomorrow's open, at", "tonight", "Tomorrow's buys count the same new picks")]
    [InlineData("bought 4 trades", "bought 1 trades", "paper", "Paper trading's bought are the system's")]
    [InlineData(": 2 open,", ": 1 open,", "paper", "Paper trading's open are the system's")]
    [InlineData("1 reached", "0 reached", "paper", "Paper trading's reached target are the system's")]
    [InlineData("1 hit their", "0 hit their", "paper", "Paper trading's hit their stop are the system's")]
    [InlineData("1 trade is open", "2 trades are open", "paper", "Paper trading's open of this version are this version's")]
    [InlineData("versions: 3 trades", "versions: 0 trades", "paper", "Paper trading's fold counts the earlier versions'")]
    public void TheProofFailsAPageCountingAnotherGroupAfterARestart(string right, string wrong, string which, string rule)
    {
        var home = which == "home" ? HomeAfterTheRestart.Replace(right, wrong, StringComparison.Ordinal) : HomeAfterTheRestart;
        var tonight = which == "tonight" ? TonightAfterTheRestart.Replace(right, wrong, StringComparison.Ordinal) : TonightAfterTheRestart;
        var paper = which == "paper" ? PaperAfterTheRestart.Replace(right, wrong, StringComparison.Ordinal) : PaperAfterTheRestart;

        var failed = Assert.Single(CountsProof.Check(RestartGroups(), home, tonight, paper), verdict => verdict.Verdict != "pass");

        Assert.Equal(rule, failed.Rule);
    }

    [Fact]
    public void AnEarlierVersionsSplitThatDoesNotAddUpFailsAndAPageDrawingNoCountIsUnexamined()
    {
        var groups = RestartGroups();
        var broken = groups with { ThisVersion = groups.ThisVersion with { Bought = 2 } };

        Assert.Contains(CountsProof.Check(broken, HomeAfterTheRestart, TonightAfterTheRestart, PaperAfterTheRestart), verdict => verdict.Rule.StartsWith("The system's trades are", StringComparison.Ordinal) && verdict.Verdict == "fail");
        Assert.All(CountsProof.Check(groups, string.Empty, TonightAfterTheRestart, PaperAfterTheRestart).Where(verdict => verdict.Rule.StartsWith("Home", StringComparison.Ordinal)), verdict => Assert.Equal("unexamined", verdict.Verdict));
    }

    // A close at the target sells at the next open, which can fall back under it, and a trailing stop rises past the stop it
    // was bought with: each still counts by the exit that sold it, as its row says.
    [Fact]
    public void ATradesEndingIsTheExitThatSoldItAndNotWhereTheNextOpenLanded()
    {
        var outcome = BoughtTrade("BELOW", new DateOnly(2026, 10, 5), new DateOnly(2026, 10, 20), 118m, 120m, 95m) with { Outcome = PaperAccount.ReachedTarget };
        var trailed = BoughtTrade("TRAIL", new DateOnly(2026, 10, 5), new DateOnly(2026, 10, 20), 110m, null, 95m) with { Outcome = PaperAccount.UnderTrail };
        var timed = BoughtTrade("TIME", new DateOnly(2026, 10, 5), new DateOnly(2026, 10, 20), 101m, 120m, 95m) with { Outcome = PaperAccount.TimeLimit };

        var count = TradeGroups.Count([(outcome, 1.0), (trailed, 0.5), (timed, 0.1)], RestartNight, (_, _) => null);

        Assert.Equal((1, 1, 3), (count.AtTarget, count.AtStop, count.Closed));

        var groups = RestartGroups() with { System = count };

        Assert.Contains("0 open, 1 reached their target, 1 hit their stop, 1 sold by their rules' other exits,", groups.SystemLine, StringComparison.Ordinal);
    }

    [Fact]
    public void AnIndexsTradeIsOpenOnceBoughtStaysHeldUntilSoldAndNamesWhichItIs()
    {
        var day = new DateOnly(2026, 11, 2);
        IndexTradeCell Cell(DateOnly? ended, double? result, string? notBought, string? why = null, decimal? sale = null) => new("X", null, DriftRule.Name, "Earnings drift", day, 100m, 90m, 120m, false, ended, result, null, null, notBought, why, sale);

        var waiting = Cell(null, null, EquityBrief.Core.Ledger.TradeFills.Waiting);
        var held = Cell(null, null, null);
        var selling = Cell(day, null, null, PaperAccount.TargetReached);
        var ended = Cell(day, 1.5, null, null, 121m);
        var refused = Cell(null, null, "no open was stored");

        Assert.Equal((false, true, false), (waiting.Open, waiting.ToBuy, waiting.Selling));
        Assert.Equal((true, false, false), (held.Open, held.ToBuy, held.Selling));
        Assert.Equal((true, false, true), (selling.Open, selling.ToBuy, selling.Selling));
        Assert.Equal((false, false, false), (ended.Open, ended.ToBuy, ended.Selling));
        Assert.Equal((false, false, false), (refused.Open, refused.ToBuy, refused.Selling));

        var drawn = new MarkRenderer().IndexTrades([waiting, selling, refused, ended], "S&P 400");

        Assert.Contains("to buy at tomorrow's open", drawn, StringComparison.Ordinal);
        Assert.Contains("<b>sells at tomorrow's open</b>: target reached", drawn, StringComparison.Ordinal);
        Assert.Contains("not bought: no open was stored", drawn, StringComparison.Ordinal);
        Assert.Contains(", sold at 121.00", drawn, StringComparison.Ordinal);
    }

    // The operator's ruling of 2026-11-03: the S&P 500's list as the others. A stored outcome and a fill with no sale is open, says
    // why it sells and is in no finished count; the night after the sale it has ended, with the sale's price.
    static PickRow FiredRow(string outcome, DateOnly? soldOn = null, decimal? sale = null, double? result = null) =>
        new(
            "FIRE", new DateOnly(2026, 10, 28), "swing", 100m, 95m, 110m, true, outcome, new DateOnly(2026, 11, 2), 10.0, 0.3, "Fire Co", 100m, new DateOnly(2026, 11, 2), 111m,
            Family: DriftRule.Name, FillResult: result, Fill: 101m, FilledOn: new DateOnly(2026, 10, 29), SoldOn: soldOn, Sale: sale);

    [Theory]
    [InlineData(ForwardReturnSeries.Win, PaperAccount.TargetReached, PickStatus.Target)]
    [InlineData(ForwardReturnSeries.Loss, PaperAccount.UnderStop, PickStatus.Stopped)]
    [InlineData(ForwardReturnSeries.Unresolved, PaperAccount.TimeRanOut, PickStatus.Time)]
    public void TheSAndP500sTradeIsOpenUntilTheNextOpenSellsItAndThenEndedWithTheSale(string outcome, string why, string ended)
    {
        var night = new DateOnly(2026, 11, 2);
        var firing = Assert.Single(PicksScreen.Cells([FiredRow(outcome)], night));

        Assert.Equal((PickStatus.Open, why, night, (DateOnly?)null), (firing.Status, firing.FiredWords, firing.FiredOn, firing.EndedOn));

        var summary = PicksScreen.Summary([firing]);

        Assert.Equal((1, 0, 1), (summary.Open, summary.Finished, summary.Selling));
        Assert.Contains("1 of them sells at tomorrow's open", new MarkRenderer().PicksCounts(summary).Replace("&#39;", "'", StringComparison.Ordinal), StringComparison.Ordinal);

        var sale = new DateOnly(2026, 11, 3);
        var sold = Assert.Single(PicksScreen.Cells([FiredRow(outcome, sale, 109.5m, 1.5)], sale));

        Assert.Equal((ended, (string?)null, night, sale, (decimal?)109.5m), (sold.Status, sold.FiredWords, sold.EndedOn, sold.SoldOn, sold.Sale));

        // An earlier night drawn after the sale still reads it as it stood.
        Assert.Equal(PickStatus.Open, Assert.Single(PicksScreen.Cells([FiredRow(outcome, sale, 109.5m, 1.5)], night)).Status);
    }

    [Fact]
    public void WhyAnExitFiredIsReadOffItsClose()
    {
        Assert.Equal(PaperAccount.TargetReached, PaperAccount.WhyFired(121m, 120m, 90m, null, 3, 60));
        Assert.Equal(PaperAccount.UnderStop, PaperAccount.WhyFired(89m, 120m, 90m, null, 3, 60));
        Assert.Equal(PaperAccount.TimeRanOut, PaperAccount.WhyFired(100m, 120m, 90m, null, 60, 60));
        Assert.Equal(PaperAccount.UnderTrail, PaperAccount.WhyFired(100m, null, 90m, 5m, 3, 60));
    }

    [Fact]
    public void TheOneFunctionCountsAFiredTradeOpenAndSellingUntilItsSale()
    {
        var night = new DateOnly(2026, 11, 2);
        var firing = BoughtTrade("FIRE", new DateOnly(2026, 10, 29), null, null, 120m, 90m) with { FiredOn = night, FiredWhy = PaperAccount.TargetReached };
        var sold = firing with { Ended = night, Sale = 121m };
        var later = firing with { FiredOn = night.AddDays(1) };

        var count = TradeGroups.Count([(firing, null), (sold, 2.0), (later, null)], night, (_, _) => 100m);

        Assert.Equal((2, 1, 1), (count.Open, count.Closed, count.Selling));
    }

    [Fact]
    public void TheProofHoldsEachPagesSellingCountToTheFunction()
    {
        var groups = RestartGroups() with { System = RestartGroups().System with { Selling = 1 } };
        var tonight = TonightAfterTheRestart.Replace("this version's.", "this version's; 1 of them sells at tomorrow's open.", StringComparison.Ordinal);
        var paper = PaperAfterTheRestart.Replace(": 2 open,", ": 2 open, 1 of them sells at tomorrow's open,", StringComparison.Ordinal);

        Assert.All(CountsProof.Check(groups, HomeAfterTheRestart, tonight, paper), verdict => Assert.Equal("pass", verdict.Verdict));
        Assert.Equal(2, CountsProof.Check(groups, HomeAfterTheRestart, TonightAfterTheRestart, PaperAfterTheRestart).Count(verdict => verdict.Verdict == "fail"));
    }

    // The operator's amendment of 22.17: the copy's fold, each row its stock, family, "an earlier version", the day and its end, no version.
    [Fact]
    public void TheReadOnlyCopysFoldDrawsEveryEarlierTradeAndNoVersion()
    {
        const string Version = "the live quality rule at a score of 7 in 9, code f6b52da6e0c6";
        var row = new PaperBoughtView("k", QualityRule.Name, "AUB", "Atlantic Union", new DateOnly(2026, 10, 23), new DateOnly(2026, 10, 26), 40.06m, null, null, false, null, null, null, null, [], null, Version);
        var page = new PaperPageView([], [], [], [row], new PaperNotBought([]), [], null);

        var copy = new MarkRenderer { Copying = true }.PaperBought(page, new PaperLink("500", null, false, null, null, null));
        var read = new MarkRenderer().PaperBought(page, new PaperLink("500", null, false, null, null, null));

        Assert.Contains("<td>an earlier version</td>", copy, StringComparison.Ordinal);
        Assert.Contains("data-ticker=\"AUB\"", copy, StringComparison.Ordinal);
        Assert.DoesNotContain("f6b52da6e0c6", copy, StringComparison.Ordinal);
        Assert.DoesNotContain("data-version", copy, StringComparison.Ordinal);
        Assert.Contains("f6b52da6e0c6", read, StringComparison.Ordinal);

        // The upload's leak check reads the fold with the rest of the page: this copy passes it and the operator's own is refused.
        var needles = EquityBrief.Web.Cloudflare.SiteCopy.MethodNeedles([Version], [Version]);

        Assert.Empty(EquityBrief.Web.Cloudflare.SiteCopy.Leaks("screens/paper.html", copy, needles));
        Assert.NotEmpty(EquityBrief.Web.Cloudflare.SiteCopy.Leaks("screens/paper.html", read, needles));
    }

    [Fact]
    public void PastPicksSaysHowManyOfTheSystemsOpenTradesItsTableHolds()
    {
        Assert.Equal("Of the system's trades, 2 are open: 1 in this table and 1 under the other setups.", RestartGroups().OpenHere(1, "this table"));
    }

    [Fact]
    public void TheProofHoldsPastPicksAndTheRunPageToTheFunctionsCount()
    {
        var count = new TradeCountView(3, 2, 1, 1, 1, 0, null);
        var page = "<p>Of the system's trades, 2 are open: 2 in this table and 0 under the other setups.</p><dl><div><dt>To buy at tomorrow's open</dt><dd>1</dd></div><div><dt>Reached target</dt><dd>1</dd></div><div><dt>Stopped out</dt><dd>0</dd></div></dl>";

        Assert.All(CountsProof.CheckPicks(count, 1, page), verdict => Assert.Equal("pass", verdict.Verdict));
        Assert.Contains(CountsProof.CheckPicks(count with { Open = 9 }, 1, page), verdict => verdict.Verdict == "fail");
        Assert.Contains(CountsProof.CheckPicks(count, 2, page), verdict => verdict.Verdict == "fail");

        var run = "<p>The pullback's trades under every version, part of the system's trades: 4 bought, 2 still open, 2 decided</p>";

        Assert.All(CountsProof.CheckRun(RestartGroups(), run), verdict => Assert.Equal("pass", verdict.Verdict));
        Assert.Contains(CountsProof.CheckRun(RestartGroups(), run.Replace("4 bought", "5 bought", StringComparison.Ordinal)), verdict => verdict.Verdict == "fail");
    }
}
