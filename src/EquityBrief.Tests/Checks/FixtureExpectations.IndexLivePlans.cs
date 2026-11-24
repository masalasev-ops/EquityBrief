using EquityBrief.Core.Families;
using EquityBrief.Core.Loop;
using EquityBrief.Worker.Loop;
using Xunit;

namespace EquityBrief.Tests.Checks;

// fixture-expectations, 24.35's second part on the operator's answers of 2026-11-23: the grammar writes the S&P 400's and 600's
// live pullback plans as registered, half at 1.5 times the risk and the rest at the first band above or by the rule's own sale,
// each line read back as the same plan, and refuses what its closed lists do not hold, worked by hand.
// see: Each S&P 400 and 600 pullback setup stores whether its live rule's other gates pass it, as the night read them
public partial class FixtureExpectations
{
    // The S&P 400's live rule's plan as registered: 2.16 typical days under the close within 20 sessions, a stop 3 typical days
    // under the buy, the first band above as its sale and half at 1.5 times the risk; the S&P 600's 0.98 within 10, a stop of
    // 4, the rule's own sale and half at 1.5.
    static Dictionary<string, double> LivePlan(double distance, int window, double stop, int sale) => new(StringComparer.Ordinal)
    {
        [PlanHook.EntryParameter] = PlanHook.Distance,
        [PlanHook.DistanceParameter] = distance,
        [PlanHook.WindowParameter] = window,
        [PlanHook.StopParameter] = stop,
        [PlanHook.SaleParameter] = sale,
        [PlanHook.TakeProfitParameter] = 1.5,
        [PlanHook.TakeProfitShapeParameter] = PlanHook.OnHalf,
    };

    [Fact]
    public void TheGrammarWritesTheSmallerIndicesLivePullbackPlansAndReadsThemBack()
    {
        var sessions = IdeaGrammar.FamilyTimeLimit(SetupFamilies.Pullback);
        var mid = PlanHook.Of(LivePlan(2.16, 20, 3, 1))!;
        var small = PlanHook.Of(LivePlan(0.98, 10, 4, 0))!;

        Assert.Equal((PlanPoints.FirstBand, true, (double?)1.5), (mid.Exit, mid.Half, mid.TakeProfit));
        Assert.Equal((PlanPoints.RuleSale, true, (double?)1.5), (small.Exit, small.Half, small.TakeProfit));

        var midParts = PullbackSearch.PlanParts(mid, sessions);
        var smallParts = PullbackSearch.PlanParts(small, sessions);

        Assert.Equal($"buy 2.16 typical days under the close within 20 sessions · stop 3 typical days · take profit half at 1.5 risks then first band · sell after {sessions} sessions", midParts);
        Assert.Equal($"buy 0.98 typical days under the close within 10 sessions · stop 4 typical days · take profit half at 1.5 risks then rule sale · sell after {sessions} sessions", smallParts);

        foreach (var (index, parts, plan) in new[] { ("400", midParts!, mid), ("600", smallParts!, small) })
        {
            var text = $"event pullback({index}) loose · {parts}";
            var (line, refused) = IdeaGrammar.Parse(text);

            Assert.Null(refused);
            Assert.Equal(text, line!.Canonical);

            var read = Survivors.PlanOf(line);

            Assert.Equal((plan.Entry, plan.Window, plan.Stop, plan.Exit, plan.Half, plan.TakeProfit), (read.Entry, read.Window, read.Stop, read.Exit, read.Half, read.TakeProfit));
        }

        Assert.Contains("half sold at 1.5 times the risk and the rest at the first price band above the buy where it turned down before", IdeaGrammar.Parse($"event pullback(400) loose · {midParts}").Line!.Words, StringComparison.Ordinal);
        Assert.Contains("half sold at 1.5 times the risk and the rest by the rule's own sale", IdeaGrammar.Parse($"event pullback(600) loose · {smallParts}").Line!.Words, StringComparison.Ordinal);

        // A multiple the grammar's list does not hold, and a sale it does not name, are refused.
        Assert.NotNull(IdeaGrammar.Parse($"event pullback(400) loose · buy next open · stop 3 typical days · take profit half at 1.75 risks then first band · sell after {sessions} sessions").Refused);
        Assert.NotNull(IdeaGrammar.Parse($"event pullback(400) loose · buy next open · stop 3 typical days · take profit half at 1.5 risks then second band · sell after {sessions} sessions").Refused);

        // A queued pick of such a line is stored with no result, as one selling at the first band is.
        Assert.Null(Survivors.PickResult(IdeaGrammar.Parse($"event pullback(600) loose · buy next open · stop 3 typical days · take profit half at 1.5 risks then rule sale · sell after {sessions} sessions").Line!, 1.0, [(10, 10), (10, 11), (11, 12)]));
    }
}
