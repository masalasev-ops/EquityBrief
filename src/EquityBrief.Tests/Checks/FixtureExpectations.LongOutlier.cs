using EquityBrief.Core.History;
using EquityBrief.Core.Loop;
using Xunit;

namespace EquityBrief.Tests.Checks;

// fixture-expectations, the 23.6 correction: a long setup whose edge lies past 20 risks either way is counted as no result,
// apart from the settled setups and read in none of the year's means, worked by hand; one at 20 stays a result; and the buy
// and sell points count such a pick as no result as well.
// see: A long setup whose edge lies past 20 risks either way is counted as no result and listed beside the unexplained jumps
public partial class FixtureExpectations
{
    // Four settled breakouts of 2007 with edges 0.5, -0.5, 20 and 44, and one of -25: the two past 20 are counted apart, the
    // three left settle with a mean edge of 20 / 3, and the one at 20 is kept.
    [Fact]
    public void ALongSetupPast20RisksIsCountedAsNoResult()
    {
        LongTrade[] trades =
        [
            new("breakout", 2007, true, false, 0.5, 0.4, false),
            new("breakout", 2007, true, false, -0.5, -0.6, false),
            new("breakout", 2007, true, false, 20, 19.9, false),
            new("breakout", 2007, true, false, 44, 43.9, false),
            new("breakout", 2007, true, false, -25, -25.1, true),
            new("breakout", 2007, false, false, null, null, false),
        ];

        var year = LongLedger.Years(trades, LongLedger.AsRuled).Single();

        Assert.Equal(6, year.Setups);
        Assert.Equal(3, year.Settled);
        Assert.Equal(2, year.Outlying);
        Assert.Equal(20.0 / 3, year.EdgeMean!.Value, 9);
        Assert.Equal(19.7 / 3, year.EdgeAfterCostMean!.Value, 9);
        Assert.Equal(0, year.Crossing);
        Assert.True(LongLedger.Outlying(20.01));
        Assert.True(LongLedger.Outlying(-20.01));
        Assert.False(LongLedger.Outlying(20));
        Assert.False(LongLedger.Outlying(null));
    }

    // A pick whose result or whose edge over its benchmark lies past 20 risks either way is no result in the buy and sell
    // points: counted as offered and in no figure, as an open pick is.
    [Fact]
    public void APickPast20RisksIsNoResultInThePlanPoints()
    {
        Assert.True(PlanPoints.NoResult(30, 25.5));
        Assert.True(PlanPoints.NoResult(25, 1.0));
        Assert.True(PlanPoints.NoResult(-1.0, -20.5));
        Assert.False(PlanPoints.NoResult(1.2, 1.0));
        Assert.False(PlanPoints.NoResult(20, null));

        var tally = new PlanTally(drawdown: false);

        tally.Add(new PlanPick(2007, "2005 to 2009", PlanPoints.Above, true, null, null, 0, null, 0, null));
        tally.Add(new PlanPick(2007, "2005 to 2009", PlanPoints.Above, true, 1.0, 0.5, 3, PlanPoints.Reached, 4, null));

        Assert.Equal((2, 1), (tally.Figures().Picks, tally.Figures().Fills));
        Assert.Equal(0.25, tally.Figures().EdgePerPick!.Value, 9);
    }

    // The long test reads a unit past 20 risks either way as no result, the one at 20 and an open one as they were.
    [Fact]
    public void TheLongTestReadsAUnitPast20RisksAsNoResult()
    {
        var rule = new Worker.Loop.RuleUnderTest("breakout", "breakout live", "live", "the breakout", Worker.Loop.LoopProcedures.Risks, 10, [(1, 0.5), (2, 25.0), (3, -21.0), (4, 20.0), (5, null)]);

        Assert.Equal([0.5, null, null, 20.0, null], Worker.Loop.RuleTests.Counted(rule).Trades!.Select(unit => unit.Edge));
        Assert.Null(Worker.Loop.RuleTests.Counted(rule with { Trades = null }).Trades);
    }
}
