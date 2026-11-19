using EquityBrief.Core.Cards;
using EquityBrief.Core.Families;
using EquityBrief.Core.History;
using EquityBrief.Core.Loop;
using EquityBrief.Web.Marks;

namespace EquityBrief.Tests.Reading;

// read-surface, the 23.10 correction on the operator's review of 2026-11-19: a trade whose close tonight stands under its
// stop, its buy's own session among them, says it closed under it and sells at the next open, as the walk sells it; a trade's
// trading days are counted from its fill as the walk counts its time limit; every order Past picks lists as waiting carries
// its revision and the account that buys it; and a revision's card states the years each test behind it read, and where its
// long test walked no trade before 2019 says so plainly.
// see: A trade under a plan names the one exit its walk sells by on every page, read from its plan and never from a stored target
public partial class ReadSurface
{
    [Fact]
    public void ATradeClosingUnderItsStopOnTheDayItWasBoughtSaysItSellsAtTheNextOpenAsTheWalkSellsIt()
    {
        // Bought at the open of 29 Sep at 100, its stop 90: a close of 89 that same session stands under its stop, so it
        // closed under it on 29 Sep and sells at the next open; a close of 90.01 is still near its stop, one typical move of
        // 4 away; and the stop's own red is the class it is drawn in.
        var bought = PaperAccount.Position(Planned, Filled, Week, (_, session) => session == Filled ? 89m : null, 4);

        Assert.Equal("closed under its stop on 29 Sep; sells at the next open", bought.Standing);
        Assert.True(PaperAccount.IsClosedUnder(bought.Standing));
        Assert.Equal(PaperAccount.NearStop, PaperAccount.Position(Planned, Filled, Week, (_, session) => session == Filled ? 90.01m : null, 4).Standing);

        // The walk reads the close of the buy's own session: filled at 100 on the first session with a stop of 90, a close
        // of 89 there sells at the open after it, 88, as the stop's end; and with no open stored after that close the walk
        // holds it, which is the night the row says it sells at the next open.
        var sale = new PlanSale(120, null, []);
        var sold = PlanPoints.Walk([89.0, 92.0], [100.0, 88.0], 0, 100, 90, sale, 20);

        Assert.Equal((PlanPoints.Stopped, 88.0, 1), (sold!.End, sold.Sold, sold.Sessions));
        Assert.Null(PlanPoints.Walk([89.0], [100.0], 0, 100, 90, sale, 20));
    }

    [Fact]
    public void ATradesTradingDaysAreCountedFromItsFillAsTheWalkCountsItsTimeLimit()
    {
        // Filled on 29 Sep: one trading day held that night, the fill's own, and four by 2 Oct; none before its fill. An order
        // that waited from its 28 Sep listing counts none of the days it waited.
        Assert.Equal(1, PaperAccount.HeldDays(Filled, Filled));
        Assert.Equal(4, PaperAccount.HeldDays(Filled, OnTheNight));
        Assert.Null(PaperAccount.HeldDays(Filled, Listed));

        var waited = PaperAccount.Position(Planned with { Bought = Week[3] }, OnTheNight, Week, (_, session) => session == OnTheNight ? 105m : null, 4);

        Assert.Equal(2, waited.Held);
    }

    [Fact]
    public void EveryOrderPastPicksListsAsWaitingCarriesItsRevisionAndTheAccountThatBuysIt()
    {
        // The earnings drift's revision 3 ran 10 Nov to 13 Nov and revision 4 runs from 16 Nov: an order revision 3 placed on
        // 10 Nov says so and that revision 3's own account buys it; one revision 4 placed on 17 Nov carries its tag and its
        // account.
        var revisions = new Dictionary<string, IReadOnlyList<RevisionView>>(StringComparer.Ordinal)
        {
            [DriftRule.Name] = [new RevisionView(3, new DateOnly(2026, 11, 10), new DateOnly(2026, 11, 13), null), new RevisionView(4, new DateOnly(2026, 11, 16), null, null)],
        };
        WaitingOrderView[] orders =
        [
            new(DriftRule.Name, "FERG", new DateOnly(2026, 11, 10), 240.92m, new DateOnly(2026, 12, 9), "Ferguson Enterprises Inc.", 251.23m),
            new(DriftRule.Name, "TGT", new DateOnly(2026, 11, 17), 153.84m, new DateOnly(2026, 12, 17), "Target Corporation", 160m),
        ];

        using var scope = RevisionTags.Use("500", revisions);
        var list = MarkRenderer.StillWaitingOrders(orders, "S&P 500");
        var ferg = list[list.IndexOf("data-ticker=\"FERG\"", StringComparison.Ordinal)..list.IndexOf("</li>", list.IndexOf("data-ticker=\"FERG\"", StringComparison.Ordinal), StringComparison.Ordinal)];
        var tgt = list[list.IndexOf("data-ticker=\"TGT\"", StringComparison.Ordinal)..list.IndexOf("</li>", list.IndexOf("data-ticker=\"TGT\"", StringComparison.Ordinal), StringComparison.Ordinal)];

        Assert.Contains(">Placed by revision 3, ran 10 Nov to 13 Nov 2026</a> Bought in revision 3's own account, not the running revision's.", ferg, StringComparison.Ordinal);
        Assert.Contains(">Revision 4, running since 16 Nov 2026</a> Bought in revision 4's account.", tgt, StringComparison.Ordinal);
    }

    [Fact]
    public void ARevisionsCardStatesTheYearsEachTestBehindItReadAndALongTestWithNoTradeBefore2019SaysSo()
    {
        // The pullback's revision judged over 2022 to 2026 by the monthly test, its long record holding none of its years from
        // 2008 to 2018: its settings chosen on 2019 to 2026, its take-profit on 2005 to 2018, and no long history named.
        var none = new PaperLongVerdict(SetupFamilies.Pullback, "live", false, 0.34, 4, 7, 1896, null, 2008, 0, 0, 1896, 74, false);
        int[] years = [2022, 2023, 2024, 2025, 2026];

        Assert.Equal("Its settings were chosen on 2019 to 2026. Its take-profit was chosen on 2005 to 2018. The monthly test judges it over 2022 to 2026.", MarkRenderer.ChosenOn(SetupFamilies.Pullback, years, none));

        var record = MarkRenderer.LongRecord(none, null);

        Assert.Contains(">The long test walked no trade of it from 2008 to 2018, so its long record reads its years from 2019 alone.</p>", record, StringComparison.Ordinal);
        Assert.Contains(">From 2019 on, it did not pass", record, StringComparison.Ordinal);

        // Once its long record holds trades before 2019 the card names the long history from its first test year, and the
        // plain line is gone.
        var walked = none with { NeverSeenUnits = 1719, NeverSeenTotal = 120 };

        Assert.EndsWith("The monthly test judges it over 2022 to 2026, and it was tested over the long history from 2008.", MarkRenderer.ChosenOn(SetupFamilies.Pullback, years, walked), StringComparison.Ordinal);
        Assert.DoesNotContain("walked no trade", MarkRenderer.LongRecord(walked, null), StringComparison.Ordinal);
        Assert.Contains(">From 2008 on, it did not pass", MarkRenderer.LongRecord(walked, null), StringComparison.Ordinal);

        // A book states its own exit's years: the heavyweights' on 2019 to 2022, Quality's on the years before 2019; and the
        // S&P 500's heavyweights, whose long test years start after 2018, say the fund-holdings data begins in October 2018.
        Assert.Contains("Its stop and take-profit were chosen on 2019 to 2022.", MarkRenderer.ChosenOn(HeavyweightRule.Name, years, null), StringComparison.Ordinal);
        Assert.Contains("Its stop and take-profit were chosen on the years before 2019.", MarkRenderer.ChosenOn(QualityRule.Name, years, null), StringComparison.Ordinal);
        Assert.Contains(MarkRenderer.HeavyweightHoldingsLine, MarkRenderer.LongRecord(none with { Family = HeavyweightRule.Name, FirstTestYear = 2023 }, null), StringComparison.Ordinal);
        Assert.DoesNotContain(MarkRenderer.HeavyweightHoldingsLine, MarkRenderer.LongRecord(walked with { Family = HeavyweightRule.Name, FirstTestYear = 2015 }, null), StringComparison.Ordinal);
        Assert.Equal(2019, LongLedger.StandingFrom.Year);
    }
}
