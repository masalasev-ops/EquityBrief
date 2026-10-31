using EquityBrief.Core.Candidates;
using EquityBrief.Core.Families;
using EquityBrief.Core.Loop;
using EquityBrief.Worker.Indices;
using EquityBrief.Worker.Loop;
using EquityBrief.Worker.Sweep;

namespace EquityBrief.Tests.Checks;

// fixture-expectations, 21.5: an S&P 400's or 600's frozen rule as the engines read it, over the constructed S&P 600 the
// per-rule test walks its frozen rules over. A breakout at a level only its sweep's own reading walks is handed to every
// engine as that reading walks it, its trades the per-rule test's own; one with no such reading laid out, a frozen pullback
// at a level only its search's own reading walks and a frozen heavyweights book each say why no engine reads them.
// see: The engines test a frozen rule at the levels it states, and a frozen rule's setting moves only by a freeze
public partial class FixtureExpectations
{
    [Fact]
    public async Task TheEnginesReadAFrozenRuleAtTheLevelsItStatesAsThePerRuleTestWalksIt()
    {
        using var store = OwnLevelStore();
        var history = new SweepHistory(store.DatabaseFile);
        var through = await history.NewestSessionAsync();
        var large = await IndexSweepRunner.OwnSwingRead.LargeAsync(history, through, _ => { }, default);
        var inputs = await history.ReadAsync(through, _ => { }, default, index: "SML", asItStood: true);
        var laid = IndexSweepRunner.OwnSwingRead.Laid("SML", inputs, await history.HeavyweightAsync(through), await history.IncomeAsync(through), through);
        var breakout = await IndexSweepRunner.OwnSwingRead.ReadAsync(laid, large, BreakoutRule.Name, history, through, 0, default);

        SweepRelease(store);

        var own = new OwnReads(new Dictionary<(string Family, double StopFloor), IndexSweepRunner.OwnSwingRead> { [(BreakoutRule.Name, 0)] = breakout }, null);
        var testInputs = new RuleTestInputs(laid, [], new Dictionary<string, LoopChange>(StringComparer.Ordinal), [], null!, null!, _ => null, null, _ => { }, Own: own);
        var evaluator = IndexEvaluator("breakout-600");

        // The S&P 600's breakout at its own breadth at 40 per cent, read for the engines over its sweep's own reading: its
        // walk the per-rule test's, the one trade A's breakout with the same edge, and its words the live rule's own.
        var atForty = FrozenMoved(IndexRules.Provisional(evaluator), (IndexRuleCandidate.LargeBreadthParameter, 0), (IndexRuleCandidate.BreadthFloorParameter, 0.4));
        var row = StandingRow(FamilyRecords.LivePrefix + "S&P 600 breakout rule at its own breadth", "breakout-600", atForty);
        var walked = RuleTests.ThroughOwnSweep(testInputs, BreakoutRule.Name, row, evaluator, atForty);
        var (engines, why) = RuleTests.LiveSwingWalk(laid, BreakoutRule.Name, row, breakout, null, null);

        Assert.Null(why);
        Assert.Equal(walked.Trades, engines!.Own.Select(one => (one.Entry, one.Edge)));
        Assert.Single(engines.Own);
        Assert.Equal("S&P 600 breakout rule at its own breadth", engines.Current);
        Assert.Equal(engines.Own.Single().Entry, Assert.Single(engines.Listings).Session);

        // With no reading of its sweep laid out, the engines read none of it and say why.
        Assert.Equal((null, RuleTests.NoOwnRead), RuleTests.LiveSwingWalk(laid, BreakoutRule.Name, row, null, null, null));

        // A frozen pullback stating the profit and cover check, which only its search's own reading walks, is read from
        // 21.16 over that reading, and with none laid out the engines read none of it and say why.
        var frozenPullback = FrozenParameters(RemedyFreezes[4].Parameters);

        Assert.Equal((null, RuleTests.NoOwnRead), RuleTests.LivePullbackWalk(laid, [], StandingRow(FamilyRecords.LivePrefix + "S&P 600 pullback rule", "pullback-600", frozenPullback), _ => { }, null, null, null!));

        // A frozen book selling under its 200-day average holds the one change of the menu its rule reads; one that does
        // not is walked by no engine of its own book.
        var bookParameters = FrozenParameters(RemedyFreezes[7].Parameters);
        var sells = FrozenMoved(bookParameters, (IndexHeavyweightCandidate.AverageExitParameter, 1));
        var holds = FrozenMoved(bookParameters, (IndexHeavyweightCandidate.AverageExitParameter, 0));

        Assert.EndsWith("sells under its 200-day average already, the one change of the menu its rule reads", WalkForwardTester.BookMenuWhy(StandingRow(FamilyRecords.LivePrefix + "S&P 600 heavyweights rule", "heavyweight-600", sells)), StringComparison.Ordinal);
        Assert.EndsWith("on no frozen book of their own", WalkForwardTester.BookMenuWhy(StandingRow(FamilyRecords.LivePrefix + "S&P 600 heavyweights rule", "heavyweight-600", holds)), StringComparison.Ordinal);
    }
}
