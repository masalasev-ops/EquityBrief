using EquityBrief.Api.Reading;
using EquityBrief.Core.Cards;
using EquityBrief.Core.Families;
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

    [Fact]
    public void PaperTradingsLineSaysWhatTheSystemBoughtAndWhatTheAccountsCount()
    {
        Assert.Equal(
            "The system has bought 4 trades since 2 Oct: 2 open, 1 reached their target, 1 hit their stop, +8.8% overall. The accounts above count only the rules as registered now, since 4 Nov.",
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

    const string HomeAfterTheRestart = "<h2>Tonight's ideas: 1 stock to buy Thursday morning</h2><p>Since the rules went live on 2 Oct, 2 ideas have been bought and sold on paper; 1 made money.</p>";
    const string TonightAfterTheRestart = "<p>1 new pick to buy at tomorrow's open, all from the earnings drift.</p><p>Of the system's trades, 2 are open, 1 of them this version's.</p><p>1 stock to buy at tomorrow's open, at about tonight's close.</p>";
    const string PaperAfterTheRestart = "<p>This version's trades: 1 trade is open and 0 have closed, the newest bought on 4 Nov.</p><p>The system has bought 4 trades since 2 Oct: 2 open, 1 reached their target, 1 hit their stop, +8.8% overall.</p><details><summary>Earlier versions: 3 trades a rule's earlier versions listed</summary></details>";

    [Fact]
    public void TheProofPassesEachPageDrawingItsGroupsCountAfterARestart()
    {
        var verdicts = CountsProof.Check(RestartGroups(), HomeAfterTheRestart, TonightAfterTheRestart, PaperAfterTheRestart);

        Assert.Equal(14, verdicts.Count);
        Assert.All(verdicts, verdict => Assert.Equal("pass", verdict.Verdict));
    }

    // A page counting the wrong group after a restart, each of the operator's findings of 2 November.
    [Theory]
    [InlineData("2 ideas have", "0 ideas have", "home", "Home's ideas bought and sold are the system's closed")]
    [InlineData("; 1 made money", "; 0 made money", "home", "Home's ideas that made money are the system's")]
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
    }

    [Fact]
    public void AnIndexsTradeIsOpenOnceBoughtStaysHeldUntilSoldAndNamesWhichItIs()
    {
        var day = new DateOnly(2026, 11, 2);
        IndexTradeCell Cell(DateOnly? ended, double? result, string? notBought) => new("X", null, DriftRule.Name, "Earnings drift", day, 100m, 90m, 120m, false, ended, result, null, null, notBought);

        var waiting = Cell(null, null, EquityBrief.Core.Ledger.TradeFills.Waiting);
        var held = Cell(null, null, null);
        var selling = Cell(day, null, null);
        var ended = Cell(day, 1.5, null);
        var refused = Cell(null, null, "no open was stored");

        Assert.Equal((false, true, false), (waiting.Open, waiting.ToBuy, waiting.Selling));
        Assert.Equal((true, false, false), (held.Open, held.ToBuy, held.Selling));
        Assert.Equal((true, false, true), (selling.Open, selling.ToBuy, selling.Selling));
        Assert.Equal((false, false, false), (ended.Open, ended.ToBuy, ended.Selling));
        Assert.Equal((false, false, false), (refused.Open, refused.ToBuy, refused.Selling));

        var drawn = new MarkRenderer().IndexTrades([waiting, selling, refused], "S&P 400");

        Assert.Contains("to buy at tomorrow's open", drawn, StringComparison.Ordinal);
        Assert.Contains("sells at tomorrow's open", drawn, StringComparison.Ordinal);
        Assert.Contains("not bought: no open was stored", drawn, StringComparison.Ordinal);
    }

    [Fact]
    public void TomorrowsBuysDrawsOneCountAndSaysWhereTheFundamentalsAndTheReshuffleAre()
    {
        var words = System.Net.WebUtility.HtmlDecode(new MarkRenderer().TomorrowsBuys([], null, night: new DateOnly(2026, 11, 2), fundamentals: 2, reshuffled: 3));

        Assert.Contains("2 stocks to buy at", words, StringComparison.Ordinal);
        Assert.Contains("these 0 and the fundamentals' 2 on their own card below.", words, StringComparison.Ordinal);
        Assert.Contains("The monthly books' reshuffle adds 3 more.", words, StringComparison.Ordinal);
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
