using EquityBrief.Core.Loop;
using EquityBrief.Core.Swings;
using EquityBrief.Web.Marks;

namespace EquityBrief.Tests.Checks;

// From the operator's order of 2026-11-14 every pick takes profit at a price: its own sale's above the buy, else the nearest
// price above the buy it turned down at before over twenty years, else the buy plus a multiple of its risk. Worked by hand.
// see: Every pick takes profit at its own sale's price, a price it turned down at before or a multiple of its risk
public partial class FixtureExpectations
{
    static readonly DateOnly ProfitNight = new(2026, 11, 13);

    static Swing High(int year, int month, int day, decimal price, int confirmedAfter = 3) =>
        new(new DateOnly(year, month, day), SwingSeries.High, price, new DateOnly(year, month, day).AddDays(confirmedAfter));

    // Six swing highs and a low: 120 in 2010, 110 in 2020 and again in 2024, 130 made in 2005 more than twenty years before the
    // night, 105 made on the night and not yet confirmed, and a low of 90.
    static IReadOnlyList<Swing> ProfitSwings() =>
    [
        High(2010, 6, 1, 120m),
        High(2020, 3, 2, 110m),
        High(2024, 3, 4, 110m),
        High(2005, 1, 3, 130m),
        High(2026, 11, 13, 105m),
        new(new DateOnly(2023, 1, 3), SwingSeries.Low, 90m, new DateOnly(2023, 1, 6)),
    ];

    [Fact]
    public void ThePricesAStockTurnedDownAtAreItsConfirmedSwingHighsOfTwentyYearsTheMostRecentOfEqualPrices()
    {
        var turned = TakeProfit.Before(ProfitSwings(), ProfitNight);

        // 110 once, in March 2024, and 120: 130 is older than twenty years, 105 is not confirmed by the night and the low is no
        // high.
        Assert.Equal([new TurnedDown(110, new DateOnly(2024, 3, 4)), new TurnedDown(120, new DateOnly(2010, 6, 1))], turned);
        Assert.Equal(new TurnedDown(110, new DateOnly(2024, 3, 4)), TakeProfit.NearestAbove(100, turned));
        Assert.Equal(new TurnedDown(120, new DateOnly(2010, 6, 1)), TakeProfit.NearestAbove(110, turned));
        Assert.Null(TakeProfit.NearestAbove(120, turned));
    }

    [Fact]
    public void ThePriceIsTheSetupsOwnAboveTheBuyThenWhereItTurnedDownThenTheMultipleOfTheRisk()
    {
        var turned = TakeProfit.Before(ProfitSwings(), ProfitNight);

        // Bought at 100 with its stop at 95: its own 108 stands; an own price under the buy gives way to 110 of March 2024; above
        // every price it turned down at, 125, it aims for twice the risk, 125 + 2 x 5 = 135; with no stop under the buy, none.
        Assert.Equal(new TakeProfitPrice(108, TakeProfitSource.Own), TakeProfit.Of(100, 95, 108, turned, 2));
        Assert.Equal(new TakeProfitPrice(110, TakeProfitSource.TurnedDown, new DateOnly(2024, 3, 4)), TakeProfit.Of(100, 95, 99, turned, 2));
        Assert.Equal(new TakeProfitPrice(135, TakeProfitSource.Risk, Multiple: 2), TakeProfit.Of(125, 120, null, turned, 2));
        Assert.Null(TakeProfit.Of(125, 126, null, turned, 2));
        Assert.Null(TakeProfit.Of(125, null, null, turned, 2));

        // Staged levels at 104 and 106 keep their shares and what is left takes profit above the last, at 110; above 120 with
        // levels at 121 and 123 at 120 + 1.5 x 3 = 124.5, which stands above 123; with levels to 126 that is under the last, so
        // none.
        var staged = new PlanSale(null, null, [104, 106]);

        Assert.Equal(new TakeProfitPrice(110, TakeProfitSource.TurnedDown, new DateOnly(2024, 3, 4)), TakeProfit.Placed(staged, 100, 95, turned, 2));
        Assert.Equal(new TakeProfitPrice(124.5, TakeProfitSource.Risk, Multiple: 1.5), TakeProfit.Placed(new PlanSale(null, null, [121, 123]), 120, 117, turned, 1.5));
        Assert.Null(TakeProfit.Placed(new PlanSale(null, null, [121, 126]), 120, 117, turned, 1.5));

        // A trail and the dip's bounce stand beside it.
        var trail = TakeProfit.SaleWith(new PlanSale(null, 4, []) { RsiAbove = 65 }, 100, 95, turned, 2);

        Assert.Equal((110, 4, 65), (trail.Target!.Value, trail.Trail!.Value, trail.RsiAbove!.Value));

        // A setup with no stop of its own stops three typical moves of 2 under a buy of 100.
        Assert.Equal(94, TakeProfit.StopUnder(100, 2, 3));
        Assert.Null(TakeProfit.StopUnder(100, 0, 3));
    }

    [Fact]
    public void APlanTakingProfitNamesItsMultipleInItsWordsAndItsParametersAndTheWalkSellsAtItsPrice()
    {
        var plan = new PlanCombination(PlanPoints.NextOpen, 0, PlanPoints.RuleStop, PlanPoints.RuleSale).Taking(2);

        Assert.Equal("the next open, the rule's stop, the rule's sale, taking profit at 2 times the risk", plan.Words);
        Assert.Equal(plan, PlanPrices.Named(plan.Words));
        Assert.Equal(plan, PlanHook.Of(PlanHook.ParametersOf(plan)));
        Assert.Null(PlanHook.Of(PlanHook.ParametersOf(plan with { TakeProfit = null })));
        Assert.Throws<ArgumentException>(() => PlanHook.Of(new Dictionary<string, double>(StringComparer.Ordinal) { [PlanHook.TakeProfitParameter] = 2.5 }));

        // Bought at the next open, 100, its rule's stop at 95 and no target of its own: the stock turned down at 110 in March
        // 2024, so a close of 110.5 on the third session sells at the fourth's open, 111.
        var order = new PlanOrder(plan.Words, PlanPoints.NextOpen, null, 0, PlanPoints.RuleStop, 95, null, 2, null, [], null, null, 20)
        {
            TakeProfit = 2,
            TurnedDown = TakeProfit.Before(ProfitSwings(), ProfitNight),
        };
        double[] opens = [99, 100, 102, 104, 111, 112];
        double[] lows = [98, 99, 101, 103, 110, 111];
        double[] closes = [99.5, 101, 103, 110.5, 111.5, 112];

        var state = PlanHook.Walk(order, opens, lows, closes, 0);

        Assert.Equal((1, 100.0, 95.0), (state.FillBar!.Value, state.Fill!.Value, state.Stop!.Value));
        Assert.Equal((111.0, 3, PlanPoints.Reached), (state.Outcome!.Sold, state.Outcome.Sessions, state.Outcome.End));

        // The same order placed before the order holds no multiple and sells as it did, on its time limit here.
        Assert.Null(PlanHook.Walk(order with { TakeProfit = null }, opens, lows, closes, 0).Outcome);
    }

    [Fact]
    public void ACardSaysWhereItsTakeProfitPriceCameFromInTheOperatorsWords()
    {
        Assert.Equal("A price where it turned down before, in March 2024.", new TakeProfitView(110m, TakeProfitSource.TurnedDown, new DateOnly(2024, 3, 4)).Line);
        Assert.Equal("No earlier high above the buy, so it aims for 2 times the risk: $10.00 up for $5.00 down.", new TakeProfitView(135m, TakeProfitSource.Risk, null, 2, 125m, 120m).Line);
        Assert.Null(new TakeProfitView(108m, TakeProfitSource.Own).Line);
        Assert.Equal("3 typical daily moves under the buy.", new TakeProfitView(110m, TakeProfitSource.TurnedDown, new DateOnly(2024, 3, 4), AddedStopMoves: 3).AddedStopLine);
    }
}
