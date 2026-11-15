using EquityBrief.Core.Families;
using EquityBrief.Core.Loop;
using EquityBrief.Web.Marks;
using EquityBrief.Worker.Loop;

namespace EquityBrief.Tests.Checks;

// On the operator's rulings of 2026-11-14 and 2026-11-15 each rule takes profit in the shape its test chose: the multiple of the
// risk alone, the first price it turned down at that far above the buy, or half; today's nearest turned-down price never chosen.
// Worked by hand over the prices the stock turned down at, 110 in March 2024 and 120 in 2010.
// see: Each rule takes profit in the shape its test chose on the early years, never at the nearest price it turned down at
public partial class FixtureExpectations
{
    [Fact]
    public void ATakeProfitIsPlacedByItsShapeTheHalfAtTheMultipleAndTheWholeAsItsPlacingReads()
    {
        var turned = TakeProfit.Before(ProfitSwings(), ProfitNight);
        var limit = new PlanSale(null, null, []);

        // Bought at 100 with its stop at 95, three times the risk is 115: today's whole at the nearest turn, 110; the risk alone at
        // 115; the first turn 115 or more above, 120; and the half at 115 with no target of its own.
        Assert.Equal(110, TakeProfit.Shaped(limit, 100, 95, turned, 3, TakeProfitPlacing.Turned, false).Target);
        Assert.Equal(115, TakeProfit.Shaped(limit, 100, 95, turned, 3, TakeProfitPlacing.Risk, false).Target);
        Assert.Equal(120, TakeProfit.Shaped(limit, 100, 95, turned, 3, TakeProfitPlacing.Beyond, false).Target);
        Assert.Equal(new PlanSale(null, null, []) { HalfAt = 115 }, TakeProfit.Shaped(limit, 100, 95, turned, 3, TakeProfitPlacing.Turned, true));

        // A target of its own above the buy stands for a whole sale, and the half sells beside it.
        var own = new PlanSale(130, null, []);

        Assert.Equal(130, TakeProfit.Shaped(own, 100, 95, turned, 3, TakeProfitPlacing.Risk, false).Target);
        Assert.Equal(own with { HalfAt = 115 }, TakeProfit.Shaped(own, 100, 95, turned, 3, TakeProfitPlacing.Beyond, true));

        // A card draws the price the walk sells at.
        Assert.Equal(new TakeProfitPrice(115, TakeProfitSource.Risk, Multiple: 3), TakeProfit.PlacedShaped(limit, 100, 95, turned, 3, TakeProfitPlacing.Turned, true));
        Assert.Equal(new TakeProfitPrice(120, TakeProfitSource.TurnedDown, new DateOnly(2010, 6, 1)), TakeProfit.PlacedShaped(limit, 100, 95, turned, 3, TakeProfitPlacing.Beyond, false));
    }

    [Fact]
    public void APlansTakeProfitShapeIsItsSeventhParameterReadBackAsItWasStatedAndRefusedWithoutAMultiple()
    {
        var plan = new PlanCombination(PlanPoints.NextOpen, 0, PlanPoints.RuleStop, PlanPoints.RuleSale);

        foreach (var (shaped, stated) in new (PlanCombination, double?)[] { (plan.Taking(2), null), (plan.TakingAtRisk(2), PlanHook.AtTheRisk), (plan.TakingBeyond(3), PlanHook.Beyond), (plan.Halving(1.5), PlanHook.OnHalf) })
        {
            var parameters = PlanHook.ParametersOf(shaped);

            Assert.Equal(stated, parameters.TryGetValue(PlanHook.TakeProfitShapeParameter, out var shape) ? shape : null);
            Assert.Equal(shaped, PlanHook.Of(parameters));
            Assert.Equal(shaped, PlanPrices.Named(shaped.Words));
        }

        var bare = new Dictionary<string, double>(PlanHook.ParametersOf(plan), StringComparer.Ordinal) { [PlanHook.TakeProfitShapeParameter] = PlanHook.OnHalf };

        Assert.Throws<ArgumentException>(() => PlanHook.Of(bare));
        Assert.Throws<ArgumentException>(() => PlanHook.Of(new Dictionary<string, double>(PlanHook.ParametersOf(plan.Taking(2)), StringComparer.Ordinal) { [PlanHook.TakeProfitShapeParameter] = 4 }));

        // An order carries its shape, a card reading the half's price from the fill and the walk selling half there.
        var order = new PlanOrder(plan.Halving(2).Words, PlanPoints.NextOpen, null, 0, PlanPoints.RuleStop, 90, null, 2, null, [], null, null, 10) { TakeProfit = 2, Half = true };

        Assert.Equal(new TakeProfitPrice(120, TakeProfitSource.Risk, Multiple: 2), order.TakeProfitFrom(100, 90));
        Assert.Equal(120, order.SaleFrom(100, 90).HalfAt);
        var stored = PlanOrder.FromJson(order.Json())!;

        Assert.Equal((2.0, true, TakeProfitPlacing.Turned), (stored.TakeProfit!.Value, stored.Half, stored.Placing));
        Assert.Equal((false, TakeProfitPlacing.Risk), (PlanOrder.FromJson((order with { Half = false, Placing = TakeProfitPlacing.Risk }).Json())!.Half, PlanOrder.FromJson((order with { Half = false, Placing = TakeProfitPlacing.Risk }).Json())!.Placing));
    }

    [Fact]
    public void AStagedLevelAfterAHalfSellsWhatIsLeftAndNeverMoreThanIsHeld()
    {
        // Filled at 100 with its stop at 90, three staged levels a quarter each, and half at 101. The second session closes at 107:
        // half sells at the next open of 107, the stop then the buy, and the first two levels sell the other half, the third
        // nothing; the third closes at 99, under the stop at the buy, and nothing is left to sell at the open of 98. 107 a share
        // over all, 0.7 of the risk; a third level selling a quarter it does not hold would sell it at 107 and buy it back at 98.
        double[] closes = [100, 107, 99, 99, 99];
        double[] opens = [100, 107, 107, 98, 98, 98];
        var sale = new PlanSale(null, null, [102, 104, 106]) { HalfAt = 101 };
        var walked = PlanPoints.Walk(closes, opens, 0, 100, 90, sale, 4);

        Assert.NotNull(walked);
        Assert.Equal(107, walked!.Sold, 6);
        Assert.Equal(0.7, walked.Result, 6);
    }

    [Fact]
    public void ATakeProfitAddedAtTheRestartCountsFromTheSessionAfterIt()
    {
        // A target at 105 the take-profit set, counted from the fourth bar: the second and third bars close above it and sell
        // nothing, the fourth sells at the open after it; counted from the first it sells at the second.
        double[] closes = [100, 106, 106, 106, 106, 106];
        double[] opens = [100, 106, 106, 106, 106, 106];
        var target = new PlanSale(105, null, []);

        Assert.Equal(2, PlanPoints.Walk(closes, opens, 0, 100, 90, target, 10)!.Sessions);
        Assert.Equal(4, PlanPoints.Walk(closes, opens, 0, 100, 90, target with { TakeProfitFromBar = 3, TargetIsTakeProfit = true }, 10)!.Sessions);

        // A target of the plan's own is not held back.
        Assert.Equal(2, PlanPoints.Walk(closes, opens, 0, 100, 90, target with { TakeProfitFromBar = 3 }, 10)!.Sessions);

        var order = new PlanOrder("words", PlanPoints.NextOpen, null, 0, PlanPoints.RuleStop, 90, null, 2, null, [], null, null, 10) { TakeProfit = 1.5, Placing = TakeProfitPlacing.Risk, TakeProfitAddedOn = new DateOnly(2026, 11, 16) };

        Assert.Equal(new PlanSale(115, null, []) { TakeProfitFromBar = 3, TargetIsTakeProfit = true }, order.SaleWalked(100, 90, 3));
        Assert.Equal(new PlanSale(115, null, []), order.SaleWalked(100, 90, 0));
    }

    [Fact]
    public async Task AnOpenTradeOfALiveRulePlacedBeforeTheRestartTakesItsRulesTakeProfitAndNoOtherDoes()
    {
        var plan = new PlanCombination(PlanPoints.NextOpen, 0, PlanPoints.RuleStop, PlanPoints.RuleSale).Halving(3);
        var live = (plan, new DateOnly(2026, 11, 16));

        // A trade stored with no plan is its rule's own: the next open at its own stop and sale, given the half at three times its
        // risk from the restart.
        var added = await PlanTrades.AddedAtRestartAsync(null!, null, null, live, "ABC", new DateOnly(2026, 11, 12), 50m, 45m, 60m, null, 63, false, CancellationToken.None);

        Assert.NotNull(added);
        Assert.Equal((3.0, true, new DateOnly(2026, 11, 16)), (added!.TakeProfit!.Value, added.Half, added.TakeProfitAddedOn!.Value));
        Assert.Equal((PlanPoints.NextOpen, 45.0, 60.0, 63), (added.Entry, added.RuleStop, added.Target!.Value, added.Cap));

        // One placed on or after the restart, and one holding a take-profit already, take none.
        Assert.Null(await PlanTrades.AddedAtRestartAsync(null!, null, null, live, "ABC", new DateOnly(2026, 11, 16), 50m, 45m, 60m, null, 63, false, CancellationToken.None));
        Assert.Null(await PlanTrades.AddedAtRestartAsync(null!, null, added, live, "ABC", new DateOnly(2026, 11, 12), 50m, 45m, 60m, null, 63, false, CancellationToken.None));
    }

    [Fact]
    public void AnS400Or600RuleStatingNoPlanTakesItsFamilysLiveTakeProfitAsItRegistersAgain()
    {
        var at = new DateTimeOffset(2026, 11, 16, 6, 0, 0, TimeSpan.Zero);
        var plan = PlanPrices.Named("2.23 typical days under the close within 20 sessions, 2 typical days under the buy, the staged sales")!.Halving(3);
        Core.Candidates.RegisterRow Row(long id, string candidate, string evaluator, IReadOnlyDictionary<string, double> parameters) =>
            new(id, candidate, "rule", "test", evaluator, Core.Candidates.CandidateEvaluator.Write(parameters), "v", Core.Candidates.CandidateFamily.Registered, null, at, null);

        var own = new Dictionary<string, double>(StringComparer.Ordinal) { ["withinSessions"] = 10 };
        var live = Row(1, "the live S&P 400 drift rule", "drift-400", new Dictionary<string, double>(own, StringComparer.Ordinal).Concat(PlanHook.ParametersOf(plan)).ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal));
        var background = Row(2, "the S&P 400 drift rule at 5 sessions", "drift-400", own);
        var stated = Row(3, "the S&P 400 drift rule, bought at the next open", "drift-400", new Dictionary<string, double>(own, StringComparer.Ordinal) { [PlanHook.EntryParameter] = 0, [PlanHook.WindowParameter] = 0, [PlanHook.StopParameter] = 0, [PlanHook.SaleParameter] = 0 });
        var large = Row(4, "the drift rule at 5 sessions", "drift", own);
        var exiting = Row(5, "the S&P 400 drift rule, the target standing", "drift-400", new Dictionary<string, double>(own, StringComparer.Ordinal) { [RuleHooks.ExitParameter] = 2 });
        Core.Candidates.RegisterRow[] standing = [live, background, stated, large, exiting];
        var taking = PlanTrades.LiveTakingProfit(standing, at.AddMinutes(1));

        // The background version stating no plan takes the live rule's half at three times its risk, bought at the next open at its
        // own stop and sale; one stating its plan off, one selling by an exit of the menu, the S&P 500's and the live rule keep what
        // they state.
        var given = Worker.Candidates.CandidateRegistrar.WithFamilyTakeProfit(background, Core.Candidates.CandidateEvaluator.Read(background.Parameters), standing, taking);

        Assert.Equal(new PlanCombination(PlanPoints.NextOpen, 0, PlanPoints.RuleStop, PlanPoints.RuleSale).Halving(3), PlanHook.Of(given));
        Assert.Equal(10, given["withinSessions"]);

        foreach (var kept in new[] { stated, large, live, exiting })
        {
            var parameters = Core.Candidates.CandidateEvaluator.Read(kept.Parameters);

            Assert.Same(parameters, Worker.Candidates.CandidateRegistrar.WithFamilyTakeProfit(kept, parameters, standing, taking));
        }
    }

    [Fact]
    public void BothTestsChooseAmongTheThreeShapesAndNeverTodaysFull()
    {
        var plan = new PlanCombination(PlanPoints.NextOpen, 0, PlanPoints.RuleStop, PlanPoints.RuleSale);

        Assert.Equal([false, true, true, true, false], new[] { plan.Taking(2), plan.TakingAtRisk(2), plan.TakingBeyond(2), plan.Halving(2), plan }.Select(PlanPointTest.Choosable));
        Assert.Equal(3, BookStops.Choosable.Count);
        Assert.True(BookStops.IsTodaysFull(BookStops.Words((2, 3))));
        Assert.False(BookStops.IsTodaysFull(BookStops.AtRiskWords((2, 3))));
        Assert.Equal(27, BookStops.Beside((_, _, _) => []).Count);

        // Today's full is best on the early years, 2016 to 2018 for Quality on the S&P 400, and is passed over.
        DateOnly[] calendar = [new(2016, 6, 1), new(2017, 6, 1), new(2018, 6, 1), new(2019, 6, 3)];
        (string, IReadOnlyList<(int Opens, double? Edge)>)[] beside =
        [
            (BookStops.Words((2, 2)), [(0, 0.05), (3, 0.01)]),
            (BookStops.AtRiskWords((2, 2)), [(0, 0.02), (3, 0.01)]),
            (BookStops.HalfWords((2, 2)), [(0, 0.01), (3, 0.03)]),
        ];
        var quality = new RuleUnderTest(QualityRule.Name, "the live rule", "live", "words", LoopProcedures.Points, 21, Months: [(0, 0.0)]) { BookBeside = beside };

        Assert.Equal(BookStops.AtRiskWords((2, 2)), RuleTests.BookChosen(quality, "MID", calendar, _ => true));
    }

    [Fact]
    public void EachBookTradesTheExitItsTestChoseTheHalfAtTheMultipleAndNoS400Or600HeavyweightsBookSellsHalf()
    {
        Assert.Equal(new BookExit(2, 1.5, TakeProfitPlacing.Turned, true), BookExits.Of(ImprovingRule.Name, "GSPC"));
        Assert.Equal(new BookExit(4, 3, TakeProfitPlacing.Beyond, false), BookExits.Of(HeavyweightRule.Name, "MID"));
        Assert.Equal(new BookExit(3, 2, TakeProfitPlacing.Risk, false), BookExits.Of(QualityRule.Name, "ABC"));
        Assert.Equal(9, BookExits.Chosen.Count);
        Assert.DoesNotContain(BookExits.Chosen, pair => pair.Key.Family == HeavyweightRule.Name && pair.Key.Index != "GSPC" && pair.Value.Half);

        // Bought at 100 with a typical move of 2 over the turns at 110 and 120: half at 1.5 times a risk of 4 is 106; the first turn
        // 12 or more above is 120; none stands 24 above, so 124; the risk alone at twice 6, 112.
        var turned = TakeProfit.Before(ProfitSwings(), ProfitNight);

        Assert.Equal((96.0, new TakeProfitPrice(106, TakeProfitSource.Risk, Multiple: 1.5)), TakeProfit.ForBook(100, 2, turned, new BookExit(2, 1.5, TakeProfitPlacing.Turned, true)));
        Assert.Equal((94.0, new TakeProfitPrice(120, TakeProfitSource.TurnedDown, new DateOnly(2010, 6, 1))), TakeProfit.ForBook(100, 2, turned, new BookExit(3, 2, TakeProfitPlacing.Beyond, false)));
        Assert.Equal((92.0, new TakeProfitPrice(124, TakeProfitSource.Risk, Multiple: 3)), TakeProfit.ForBook(100, 2, turned, new BookExit(4, 3, TakeProfitPlacing.Beyond, false)));
        Assert.Equal((94.0, new TakeProfitPrice(112, TakeProfitSource.Risk, Multiple: 2)), TakeProfit.ForBook(100, 2, turned, BookExit.Middle));

        // On half the first close at or over 106 sells half and ends nothing; then the stop stands at the buy and no price sells
        // the other half; a whole sale ends at it.
        Assert.Equal(((string?)null, true), TakeProfit.BookStepOf(107, 96, 106, true, false));
        Assert.Equal(((string?)null, false), TakeProfit.BookStepOf(200, 100, 106, true, true));
        Assert.Equal((TakeProfit.UnderItsStop, false), TakeProfit.BookStepOf(99.9, 100, 106, true, true));
        Assert.Equal((TakeProfit.UnderItsStop, false), TakeProfit.BookStepOf(95, 96, 106, true, false));
        Assert.Equal((TakeProfit.TookProfit, false), TakeProfit.BookStepOf(106, 96, 106, false, false));
        Assert.Equal(0.08, TakeProfit.HalfResult(100, 106, 110), 9);
        Assert.Equal("half 1.5", TakeProfit.StoredHalf(1.5));
        Assert.True(TakeProfit.IsHalf("half 1.5"));
        Assert.Equal((TakeProfitSource.Risk, (DateOnly?)null, (double?)1.5), TakeProfit.FromStored("half 1.5"));
    }

    [Fact]
    public void ABooksRecordBoxSaysWhatItMadeInPointsAMonthAndAboutTwelveTimesThatAYear()
    {
        Assert.Equal(
            "From 2019 to 2026, this book made 0.63 percentage points a month more than holding the index's stocks in equal amounts, about 7.6% a year.",
            new SetupLongRecord("MID", QualityRule.Name, null, null, 2019, 2026) { Points = 0.63, Months = 94 }.BookLine);
        Assert.Equal(
            "From 2023 to 2026, this book made 0.13 percentage points a month less than holding each sector's 10 largest stocks in equal amounts, about 1.6% a year.",
            new SetupLongRecord("GSPC", HeavyweightRule.Name, null, null, 2023, 2026) { Points = -0.13, Months = 46 }.BookLine);

        var same = new SetupLongRecord("SML", ImprovingRule.Name, null, null, 2019, 2026) { Points = 0.004, Months = 94 };

        Assert.Equal("From 2019 to 2026, this book made about the same as holding the index's stocks in equal amounts.", same.BookLine);
        Assert.Equal(["made", "lost", "same"], new[] { 0.63, -0.13, 0.004 }.Select(points => new SetupLongRecord("MID", QualityRule.Name, null, null, 2019, 2026) { Points = points }.State));
        Assert.Contains(same.BookLine!, MarkRenderer.SetupRecordBox(same), StringComparison.Ordinal);
        Assert.Null(new SetupLongRecord("MID", "drift", 0.1, 400, 2019, 2026).BookLine);
    }

    [Fact]
    public void ATakeProfitOnHalfIsDrawnWithHowTheOtherHalfSellsInTheOperatorsWords()
    {
        var half = new TakeProfitView(106m, TakeProfitSource.Risk, null, 1.5, 100m, 96m) { Half = true, OtherHalf = TakeProfitView.BookOtherHalf(HeavyweightRule.Name) };

        Assert.Equal("Take profit on half at", half.LabelText);
        Assert.Equal("1.5 times the risk: $6.00 up for $4.00 down.", half.Line);
        Assert.Equal("if it leaves the book at the monthly reshuffle", half.OtherHalf);
        Assert.Equal("Its stop moves up to the buy, $100.00, once the first half sells.", half.OtherHalfLine);
        Assert.Equal("when its filings weaken", TakeProfitView.BookOtherHalf(ImprovingRule.Name));

        Assert.Equal(
            "No earlier high 3 times the risk or more above the buy, so it aims for 3 times the risk: $24.00 up for $8.00 down.",
            new TakeProfitView(124m, TakeProfitSource.Risk, null, 3, 100m, 92m) { Placing = TakeProfitPlacing.Beyond }.Line);
        Assert.Equal(
            "A price where it turned down before, in March 2024. Take-profit added at the rules restart of 16 Nov 2026.",
            new TakeProfitView(110m, TakeProfitSource.TurnedDown, new DateOnly(2024, 3, 4)) { AddedOn = new DateOnly(2026, 11, 16) }.Line);

        // A price family's plan on its time limit draws the half and the other half after its trading days, and no backstop
        // beside it; one with a band above draws the other half at the band and the backstop after.
        var lines = MarkRenderer.PlanSaleLines(new PlanSaleView(PlanPoints.TimeLimit, 100m, 96m, null, [], null, null, 10) { TakeProfit = half });

        Assert.Equal(
            [
                new PlanSaleLine("target", "Take profit on half at", "$106.00", "1.5 times the risk: $6.00 up for $4.00 down."),
                new PlanSaleLine("other-half", "Sell the other half", "after 10 trading days", "Its stop moves up to the buy, $100.00, once the first half sells."),
            ],
            lines);
        Assert.Equal(
            ["Take profit on half at", "Sell the other half", "Otherwise sell after"],
            MarkRenderer.PlanSaleLines(new PlanSaleView(PlanPoints.FirstBand, 100m, 96m, 110m, [], null, null, 10) { TakeProfit = half }).Select(line => line.Label));
    }
}
