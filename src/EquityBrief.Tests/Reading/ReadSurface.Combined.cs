using System.Net;
using EquityBrief.Core.Cards;
using EquityBrief.Tests.Checks;
using EquityBrief.Tests.Harness;
using EquityBrief.Web.Marks;

namespace EquityBrief.Tests.Reading;

// read-surface, 21.11 and its correction of 2026-11-07: the live setups' accounts, averaged, each live rule's own account
// given an equal share of one account of the settings' size from the day every one of them counts, worked by hand; a stock
// two families pick held in each family's own account and skipped by neither; and paper trading's tile, line and card for
// it.
// see: The averaged account gives each live rule's own account an equal share, from the day every one of them counts
public partial class ReadSurface
{
    // The rows this check reaches from 21.11: paper trading's, section 17's settings and section 18's trade two families
    // took.
    internal static readonly string[] CombinedPageClaims =
    [
        CheckReach.Key("15.21 Paper trading", Scope.PaperCombined),
        CheckReach.Key(Scope.LimitsTable, Scope.PaperCombined),
        CheckReach.Key(Scope.FailureTable, Scope.PaperTwoFamilies),
    ];

    static readonly DateOnly CombinedDay1 = new(2026, 11, 2);
    static readonly DateOnly CombinedDay2 = new(2026, 11, 3);
    static readonly DateOnly CombinedDay3 = new(2026, 11, 4);

    static PaperTaken Took(string ticker, DateOnly bought, long shares, DateOnly? ended = null, string? skipped = null) =>
        new(new PaperTrade(ticker, bought, 100m, 99m, ended, ended is null ? null : 101m, null, 1), skipped is null ? shares : 0, skipped);

    static PaperWalk Walked(IReadOnlyList<(DateOnly Session, decimal Value)> values, IReadOnlyList<PaperTaken> trades) =>
        new(values, trades, trades.Count(one => one.Skipped is null), trades.Count(one => one.Skipped is not null), 0, null, null, null, null, values.Count > 0 ? values[0].Session : null, [.. values.Select(one => (one.Session, GrowthOnTenThousand(one.Value)))]);

    static double GrowthOnTenThousand(decimal value) => EquityBrief.Core.Prices.Statistic.FromRatio((value / 10_000m) - 1m);

    // The pullback's account counts from 2 Nov and the drift's from 3 Nov, each of $10,000: the averaged account counts from
    // 3 Nov, the later. The pullback stood at $10,200 at the close of 2 Nov, $10,404 on 3 Nov and $10,710 on 4 Nov; the
    // drift at $10,000 on 3 Nov and $9,800 on 4 Nov. The pullback grew by 1.02 and then 1.05 from its close before 3 Nov, not
    // from its size, the drift by 1 and 0.98 from its size: the averaged $10,000 stands at 10,000 times 1.01, $10,100, and
    // then 1.015, $10,150, a return of 1.5%. It holds the pullback's A bought on 2 Nov and open, the drift's B and its A, the stock both families
    // picked, each in its own account and skipped by neither, and the drift's skip of C on 4 Nov; not the pullback's Z,
    // bought and sold on 2 Nov, nor its skip of Y that day.
    [Fact]
    public void TheAveragedAccountIsWorkedByHandOverTwoAccountsThatStartOnDifferentDays()
    {
        var pullback = Walked(
            [(CombinedDay1, 10_200m), (CombinedDay2, 10_404m), (CombinedDay3, 10_710m)],
            [Took("A", CombinedDay1, 10), Took("Z", CombinedDay1, 10, ended: CombinedDay1), Took("Y", CombinedDay1, 0, skipped: PaperAccount.UnderTheCash)]);
        var drift = Walked(
            [(CombinedDay2, 10_000m), (CombinedDay3, 9_800m)],
            [Took("B", CombinedDay2, 20), Took("A", CombinedDay2, 10), Took("C", CombinedDay3, 0, skipped: PaperAccount.UnderTheCash)]);

        var walked = PaperAccount.Averaged(10_000m, [("pullback", pullback), ("drift", drift)], [CombinedDay2, CombinedDay3]);

        Assert.Equal([10_100m, 10_150m], walked.Walk.Values.Select(one => one.Value));
        Assert.Equal(0.015, walked.Walk.Return!.Value, 12);
        Assert.Equal(2, walked.Accounts);
        Assert.Equal(["pullback A", "drift B", "drift A", "drift C"], walked.Trades.Select(one => one.Family + " " + one.Taken.Trade.Ticker));
        Assert.Equal((3, 1), (walked.Walk.Taken, walked.Walk.Skipped));
    }

    // Section 18's row: two families pick one stock, and each family's own account holds it; the averaged account holds an
    // equal share of each, so neither is skipped as already held.
    [Fact]
    public void AStockTwoFamiliesPickIsHeldInEachFamilysAccountAndSkippedByNeither()
    {
        var account = new AccountSettings(10_000m, 1m, 0.2m);
        var trade = new PaperTrade("A", CombinedDay1, 100m, 99m, null, null, null, 1);
        var pullback = PaperAccount.Walk(account with { PositionCap = PaperAccount.AccountCap }, [trade], [CombinedDay1], (_, _) => 100m);
        var breakout = PaperAccount.Walk(account with { PositionCap = PaperAccount.AccountCap }, [trade], [CombinedDay1], (_, _) => 100m);
        var walked = PaperAccount.Averaged(10_000m, [("pullback", pullback), ("breakout", breakout)], [CombinedDay1]);

        Assert.Equal([("pullback", 10L), ("breakout", 10L)], walked.Trades.Select(one => (one.Family, one.Taken.Shares)));
        Assert.Equal(0, walked.Walk.Skipped);
    }

    // On 2026-11-06 the page drew "All setups together", every live rule's trades in one pot skipping 26 picks on the S&P 500,
    // 24 of them for the cash a rule's own account still had. The page draws the live setups' accounts averaged from the day
    // every one counts, under its own name, from the views the rules' own accounts are walked into.
    [Fact]
    public void PaperTradingDrawsTheAveragedAccountsTileLineAndCardFromTheDayEveryLiveAccountCounts()
    {
        var settings = new AccountSettings(10_000m, 1m, 0.2m);
        PaperAccountView View(string family, DateOnly since, PaperWalk walk) =>
            new(family, family, PaperAccountView.Live, "Live", family, "within luck", null, "risks", [], walk, walk.Taken, 0, null, since);

        var pullback = PaperAccount.Walk(settings with { PositionCap = PaperAccount.AccountCap }, [new PaperTrade("A", CombinedDay1, 100m, 99m, null, null, null, 1)], [CombinedDay1, CombinedDay2], (_, session) => session == CombinedDay1 ? 100m : 104m);
        var drift = PaperAccount.Walk(settings with { PositionCap = PaperAccount.AccountCap }, [], [CombinedDay2], (_, _) => null);
        var combined = EquityBrief.Api.Reading.PaperScreen.Averaged([View("pullback", CombinedDay1, pullback), View("drift", CombinedDay2, drift)], settings, [CombinedDay1, CombinedDay2], CombinedDay2, (_, session) => session == CombinedDay1 ? 100m : 104m, _ => null)!;
        var marks = new MarkRenderer();
        var tiles = WebUtility.HtmlDecode(marks.PaperTiles([], null, combined));
        var chart = WebUtility.HtmlDecode(marks.PaperChart([], null, combined));
        var card = WebUtility.HtmlDecode(marks.PaperCombined(combined));

        // It counts from 3 Nov, the drift's start; the pullback's A, 10 shares at 100, stood at $10,000 at the close of 2 Nov
        // and at $10,040 on 3 Nov at a close of 104, so the averaged account is up the mean of 0.4% and 0, 0.2%.
        Assert.Equal(CombinedDay2, combined.Since);
        Assert.Equal(0.002, combined.Walk.Return!.Value, 12);
        Assert.Contains("<div class=\"paper-tile combined\" data-standing=\"combined\"", tiles, StringComparison.Ordinal);
        Assert.Contains(">The live setups' accounts, averaged<", tiles, StringComparison.Ordinal);
        Assert.Contains("Up 0.2% since 3 Nov, with 1 trade open. 0 closed, 0 skipped.", tiles, StringComparison.Ordinal);
        Assert.DoesNotContain("All setups together", tiles + chart + card, StringComparison.Ordinal);

        // Its line in the chart, keyed by its own name.
        Assert.Contains("data-standing=\"combined\"", chart, StringComparison.Ordinal);
        Assert.Contains("</span>The live setups' accounts, averaged</button>", chart, StringComparison.Ordinal);

        // The card: the line saying what it is, and the pullback's A as its own account bought it.
        Assert.Contains("Each of the 2 live setups' own accounts holds an equal share of this one from 2026-11-03, the day every one of them counts", card, StringComparison.Ordinal);
        Assert.Contains("data-family=\"pullback\" data-ticker=\"A\" data-bought=\"2026-11-02\" data-open=\"true\"", card, StringComparison.Ordinal);
        Assert.Contains(">10</td>", card, StringComparison.Ordinal);
        Assert.Contains("+4.0%", card, StringComparison.Ordinal);
        Assert.Equal(
            "The live setups' accounts, averaged: up 0.2% since 3 Nov, the day every one of the 2 counts, with 1 position open across them.",
            MarkRenderer.PaperCombinedSaid(combined, walked: true));

        // The card's words carry no term of 15.23's word list.
        var said = PlainWords.Visible(marks.PaperCombined(combined));
        Assert.All(PlainWords.Terms(), term => Assert.DoesNotMatch(PlainWords.Pattern(term), said));

        // No averaged account before the settings are set or before any live account has a day to count from.
        Assert.Null(EquityBrief.Api.Reading.PaperScreen.Averaged([], settings, [CombinedDay1], CombinedDay1, (_, _) => null, _ => null));
        Assert.Contains("data-combined=\"none\"", marks.PaperCombined(null), StringComparison.Ordinal);
    }

    // Tonight's order of a night's buys, as Tonight draws them and the averaged account's card lists a session's trades.
    [Fact]
    public void TheCombinedAccountFillsASessionInTheOrderTonightDrawsItsBuysIn()
    {
        Assert.Equal(
            ["pullback", "breakout", "drift", "fundamentals", "improving", "dip", "heavyweight", "quality"],
            BuyOrder.Families);
        Assert.Equal(BuyOrder.Families.Count - 1, BuyOrder.Of("quality"));

        BuyPointView Point(string family) => new(family, family, new FamilyPickCell(new ListingCell("T" + family, CombinedDay1, 1, 0, 10m, []), 1, 10m, 9m, 12m, false, 2m, "why", []));

        PlanlessBuyView Planless(string family) => new(family, family, "P" + family, null, "why", "business", 10m, "sells", "#/");

        var drawn = MarkRenderer.BuysInOrder(
            [Point("pullback"), Point("breakout"), Point("drift"), Point("fundamentals"), Point("dip")],
            [Planless("improving"), Planless("heavyweight"), Planless("quality")]);

        Assert.Equal(BuyOrder.Families, drawn.Select(buy => buy.Point?.Family ?? buy.Planless!.Family));
    }
}
