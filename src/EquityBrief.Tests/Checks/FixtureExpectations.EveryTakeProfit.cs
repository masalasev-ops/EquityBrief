using EquityBrief.Core.Loop;
using EquityBrief.Worker.Loop;
using Xunit;

namespace EquityBrief.Tests.Checks;

// The operator's decision of 2026-11-18, C: the plan reading walks the take-profit's shapes on the plan a family trades after
// the restart where it holds no take-profit, as before; and with '--every-take-profit' on the family's live and recommended
// plans as well, each without its take-profit, each once and labelled, so a family whose live plan holds a take-profit is
// re-tested. Worked by hand.
public partial class FixtureExpectations
{
    static readonly PlanCombination LivePlanWithTakeProfit = new PlanCombination("1.13 typical days under the close", 20, "4 typical days under the buy", "the first band above").Halving(3);

    static readonly PlanCombination RecommendedPlan = new("the second zone", 10, "4 typical days under the buy", "the time limit alone");

    [Fact]
    public void WithoutTheOptionAFamilyWhoseTradedPlanHoldsATakeProfitIsWalkedOnNothingAndOneThatHoldsNoneOnItsOwnPlan()
    {
        Assert.Empty(PlanPointTest.TakeProfitBases(LivePlanWithTakeProfit, LivePlanWithTakeProfit, RecommendedPlan, every: false));
        Assert.Equal([(string.Empty, RecommendedPlan)], PlanPointTest.TakeProfitBases(RecommendedPlan, LivePlanWithTakeProfit, RecommendedPlan, every: false));
    }

    [Fact]
    public void WithTheOptionTheLiveAndTheRecommendedPlansAreWalkedWithoutTheirTakeProfitEachOnce()
    {
        var bare = new PlanCombination("1.13 typical days under the close", 20, "4 typical days under the buy", "the first band above");

        // The traded plan holds a take-profit, so it is not walked as the run's own; the live plan bare and the recommended one are.
        Assert.Equal([("the live plan", bare), ("the recommended plan", RecommendedPlan)], PlanPointTest.TakeProfitBases(LivePlanWithTakeProfit, LivePlanWithTakeProfit, RecommendedPlan, every: true));

        // Where the run walks the recommended plan as its own, it is not walked twice, and the live plan is walked bare beside it.
        Assert.Equal([(string.Empty, RecommendedPlan), ("the live plan", bare)], PlanPointTest.TakeProfitBases(RecommendedPlan, LivePlanWithTakeProfit, RecommendedPlan, every: true));

        // A family with no recommendation is walked on its live plan alone.
        Assert.Equal([("the live plan", bare)], PlanPointTest.TakeProfitBases(LivePlanWithTakeProfit, LivePlanWithTakeProfit, null, every: true));
        Assert.Equal("1.13 typical days under the close within 20 sessions, 4 typical days under the buy, the first band above", bare.Words);
    }
}
