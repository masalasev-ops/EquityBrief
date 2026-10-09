using EquityBrief.Core.Ledger;

namespace EquityBrief.Tests.Checks;

// fixture-expectations, 17.3: a heavyweights' setup's path worked by hand, sold at the first later rebalance that does
// not buy it, at the cap, open where the closes end first and no trade where the buy has no close, and its size cut's
// benchmark over the same sessions, settled only once every member entered holds a close that far on.
// see: A sector heavyweight's trade is scored by its percent return less the equal-weighted return of the size cut it was chosen from
public partial class FixtureExpectations
{
    [Fact]
    public void AHeavyweightsSetupsPathAndItsSizeCutsBenchmarkAreWorkedByHand()
    {
        double[] closes = [100, 101, 102, 103, 104, 105];

        // Sold at the first later rebalance not buying it, three sessions on: 103 over 100.
        var sold = HeavyweightPaths.Replay(closes, 0, [5, 3]);

        Assert.Equal((0.03, 3, SetupEnds.Rebalance), (Math.Round(sold.Result!.Value, 9), sold.Sessions, sold.End));

        // A rebalance on the buy's own session or before it sells nothing; the cap's close ends one that no rebalance does.
        var capped = HeavyweightPaths.Replay(closes, 1, [0, 1], cap: 3);

        Assert.Equal((Math.Round((104.0 / 101) - 1, 9), 3, SetupEnds.Cap), (Math.Round(capped.Result!.Value, 9), capped.Sessions, capped.End));

        // A rebalance at the cap's own session is the cap's close and not a sale.
        Assert.Equal(SetupEnds.Cap, HeavyweightPaths.Replay(closes, 0, [4], cap: 4).End);
        Assert.Equal(SetupEnds.Rebalance, HeavyweightPaths.Replay(closes, 0, [3], cap: 4).End);

        // The closes ending before either: open over the sessions held and no result; and a buy with no close none.
        var open = HeavyweightPaths.Replay(closes, 2, [9]);

        Assert.Equal((null, 3, SetupEnds.Open), (open.Result, open.Sessions, open.End));
        Assert.Equal(SetupEnds.None, HeavyweightPaths.Replay([0, 1, 2], 0, [1]).End);
        Assert.Equal(252, HeavyweightPaths.Cap);

        // The size cut over two sessions: 120 over 100 and 40 over 50, a mean of nothing; a member with no close at the
        // buy not entered; and one whose closes stop short entered and not ended, which leaves the benchmark unsettled.
        var cut = HeavyweightPaths.Cut([([100, 110, 120], 0), ([50, 50, 40], 0), ([0, 1, 2], 0)], 2);

        Assert.Equal((0.0, 2, 2, true), (Math.Round(cut.Mean!.Value, 9), cut.Ended, cut.Entered, cut.Settled));

        var short_ = HeavyweightPaths.Cut([([100, 110, 120], 0), ([50, 50], 0)], 2);

        Assert.Equal((2, 1, false), (short_.Entered, short_.Ended, short_.Settled));
    }
}
