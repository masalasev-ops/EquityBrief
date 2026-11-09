using EquityBrief.Core.Loop;
using EquityBrief.Tests.Harness;
using EquityBrief.Worker.Loop;
using EquityBrief.Worker.Sweep;
using Xunit;

namespace EquityBrief.Tests.Checks;

// fixture-expectations, the 23.8 correction: every result over the long history read against the benchmark as ruled, with
// its index's own fund held over the same sessions beside it. The fund's edge worked by hand, a pick's edge read off its
// benchmark and not the fund, the tally keeping the fund line apart, a walked trade's fund line over its own sessions, and
// a member's benchmark walked by hand under the combination's stop and sale.
// see: Every result over the long history is judged against the benchmark as ruled, with its index's own fund held over the same sessions as a second line
public partial class FixtureExpectations
{
    // A result of 1.5 risks on a buy at 50 with a risk of 2, the fund going from 400 to 410: 2.5 per cent of 50 is 1.25, which
    // is 0.625 risks, so 0.875 over the fund. No close, no risk.
    [Fact]
    public void TheFundsEdgeIsWorkedByHand()
    {
        Assert.Equal(0.875, FundLine.Edge(1.5, 50, 2, 400, 410)!.Value, 12);
        Assert.Equal(2.125, FundLine.Edge(1.5, 50, 2, 400, 390)!.Value, 12);
        Assert.Null(FundLine.Edge(1.5, 50, 0, 400, 410));
        Assert.Null(FundLine.Edge(1.5, 50, 2, double.NaN, 410));
        Assert.Equal(["SPY", "IJH", "IJR"], new[] { "GSPC", "MID", "SML" }.Select(index => FundLine.FundOf(index)));
    }

    // A pick at 1.2 risks after costs, its benchmark -0.3: its edge is 1.5 whatever the fund did, and the fund rising 2 per
    // cent on a buy at 30 with a risk of 1.5 reads 1.2 less 0.4, 0.8. A benchmark no member's walk ended gives no edge, and
    // the fund's line stands.
    [Fact]
    public void APicksEdgeIsReadOffItsBenchmarkAndNotTheFund()
    {
        var (edge, fund) = PlanPointTest.EdgesOf(1.2, 30, 1.5, -0.3, 100, 102);

        Assert.Equal(1.5, edge!.Value, 12);
        Assert.Equal(0.8, fund!.Value, 12);

        var (unsettled, beside) = PlanPointTest.EdgesOf(1.2, 30, 1.5, null, 100, 102);

        Assert.Null(unsettled);
        Assert.Equal(0.8, beside!.Value, 12);
    }

    // Two fills and a pick not taken, the fills' fund edges 0.6 and -0.3: 0.1 a pick offered and 0.15 a fill, apart from the
    // edges over the benchmark, by the tally and the figures over a list alike.
    [Fact]
    public void TheTallyKeepsTheFundLineApart()
    {
        PlanPick[] picks =
        [
            new(2006, "2005 to 2009", PlanPoints.Above, true, 1.0, 0.9, 3, PlanPoints.Reached, 4, null) { FundEdge = 0.6 },
            new(2006, "2005 to 2009", PlanPoints.Above, true, -1.0, -0.6, 2, PlanPoints.Stopped, 6, null) { FundEdge = -0.3 },
            new(2006, "2005 to 2009", PlanPoints.Below, false, null, null, 0, null, 0, null),
        ];
        var tally = new PlanTally(drawdown: false);

        foreach (var pick in picks)
        {
            tally.Add(pick);
        }

        var figures = PlanPoints.FiguresOf(picks);

        Assert.Equal(0.1, figures.FundEdgePerPick!.Value, 12);
        Assert.Equal(0.15, figures.FundEdgePerFill!.Value, 12);
        Assert.Equal(0.1, figures.EdgePerPick!.Value, 12);
        Assert.Equal(0.1, tally.Figures().FundEdgePerPick!.Value, 12);
        Assert.Equal(0.15, tally.Figures().FundEdgePerFill!.Value, 12);
    }

    // A walked trade listed at 20 with its stop at 18 and a result of 1.0, the fund 100, 101 and 102 over its three sessions:
    // the fund's 2 per cent of 20 is 0.4, a fifth of the risk of 2, so 0.8 over it. An open trade, or a read holding no fund,
    // has no line; a session the fund holds no close for reads none.
    [Fact]
    public void AWalkedTradesFundLineIsReadOverItsOwnSessions()
    {
        var calendar = new[] { new DateOnly(2008, 1, 2), new DateOnly(2008, 1, 3), new DateOnly(2008, 1, 4), new DateOnly(2008, 1, 7) };
        var fund = FundLine.Aligned(calendar, [(calendar[0], 100.0), (calendar[1], 101.0), (calendar[2], 102.0)]);
        var read = new LoopRead("MID", null!, [], [], null!, null!, new Dictionary<string, IReadOnlyList<Core.Readings.FiledIncome>>(), calendar, 0, [], Long: true, Fund: fund);
        var trade = new FamilyTrade(new FamilyListing(0, 0, 0, 0, 0, 20, 18, double.NaN, double.NaN, 10), 0, 1.0, 0.1);

        Assert.True(double.IsNaN(fund[3]));
        Assert.Equal(0.8, read.FundEdge(0, 2, trade)!.Value, 12);
        Assert.Null(read.FundEdge(0, null, trade));
        Assert.Null(read.FundEdge(0, 3, trade));
        Assert.Null((read with { Fund = null }).FundEdge(0, 2, trade));
    }

    // A member closing at 10 with a risk of 1 buys at its next open, 10.2. Its stop at 9 and its target at 12: its close of
    // 12.1 sells at the next open, 12.3, 2.1 risks. The time limit of 5 sessions alone sells at the open after its fifth,
    // 12.6, 2.4 risks. Staged sales at 11 and 12.5 sell a third at 11.2, a third at 12.4 and the last at the limit's 12.6,
    // 1.867 risks. A member closing at 8.9 under its stop of 9 sells at 8.7, -1.5 risks; under a stop of 8.2 its history
    // ends before any sale. A member whose stop cannot be placed, a zone its levels plan does not draw, is not entered. The
    // benchmark is the mean of those that ended.
    [Fact]
    public void AMembersBenchmarkIsWalkedUnderTheCombinationsStopAndSale()
    {
        double[] closes = [10, 10.5, 11, 12.1, 12.5, 12.6, 12.6];
        double[] opens = [10, 10.2, 10.6, 11.2, 12.3, 12.4, 12.6];
        double[] falling = [10, 9.5, 8.9, 9.0];
        double[] fallingOpens = [10, 10.2, 9.4, 8.7];

        double? RuleStop(double fill) => 9;

        double? Wide(double fill) => fill - 2;

        PlanSale Target(double fill) => new(12, null, []);

        PlanSale Limit(double fill) => new(null, null, []);

        PlanSale Staged(double fill) => new(null, null, [11, 12.5]);

        var sold = PlanPoints.MemberResult(closes, opens, 0, 1, RuleStop, Target, 5);
        var timed = PlanPoints.MemberResult(closes, opens, 0, 1, RuleStop, Limit, 5);
        var staged = PlanPoints.MemberResult(closes, opens, 0, 1, RuleStop, Staged, 5);
        var stopped = PlanPoints.MemberResult(falling, fallingOpens, 0, 1, RuleStop, Target, 5);

        Assert.Equal(2.1, sold!.Value, 9);
        Assert.Equal(2.4, timed!.Value, 9);
        Assert.Equal(((11.2 + 12.4 + 12.6) / 3) - 10.2, staged!.Value, 9);
        Assert.Equal(-1.5, stopped!.Value, 9);
        Assert.Null(PlanPoints.MemberResult(falling, fallingOpens, 0, 1, Wide, Target, 5));
        Assert.Null(PlanPoints.MemberResult(closes, opens, 0, 1, _ => null, Target, 5));
        Assert.Null(PlanPoints.MemberResult(closes, opens, 6, 1, RuleStop, Target, 5));
        Assert.Equal(0.3, PlanPoints.BenchmarkOf([sold, stopped, null])!.Value, 9);
        Assert.Null(PlanPoints.BenchmarkOf([null]));

        // A member whose result lies past 20 risks either way is left out, one at 20 kept.
        Assert.Equal(1.0, PlanPoints.BenchmarkOf([1.0, 25.0, -30.0])!.Value, 9);
        Assert.Equal(10.5, PlanPoints.BenchmarkOf([1.0, 20.0])!.Value, 9);
    }
}
