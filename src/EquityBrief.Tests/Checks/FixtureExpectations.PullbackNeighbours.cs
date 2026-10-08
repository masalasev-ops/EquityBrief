using EquityBrief.Core.Sweep;
using EquityBrief.Worker.Sweep;

namespace EquityBrief.Tests.Checks;

// fixture-expectations, 17.1: the S&P 400 pullback with profit and cover at half steps about the setting the brief
// names, the grid and its neighbours worked by hand, and its trades under three exits replayed over constructed closes
// at each exit's edge.
// see: The S&P 400 pullback with profit and cover is read at half steps about the setting the brief names and its trades under three exits
public partial class FixtureExpectations
{
    [Fact]
    public void TheNamedPullbackIsTheBriefsSettingAndItsHalfStepsAreWorkedByHand()
    {
        var named = IndexSweepRunner.NamedPullback;
        var extended = SweepGrid.Extended;

        // The setting the brief names, each value held by the extended grid.
        Assert.Equal((0.75, 2.0, 4.0, SweepGrid.Off, 8, 2.0, (0.5, 2.5), 0.50, 6), (
            extended.StrengthBars[named.Strength],
            extended.DepthLows[named.DepthLow],
            extended.DepthHighs[named.DepthHigh],
            extended.DryUpCeilings[named.DryUp],
            extended.Freshness[named.Freshness],
            extended.RewardToRiskFloors[named.RewardToRisk],
            extended.StopBounds[named.Stop],
            extended.MarketFloors[named.Market],
            extended.BandStrengths[named.Band]));

        var (grid, centre, neighbours) = IndexSweepRunner.HalfSteps(named);

        // Each ordered dial holds the value with half a step either side at the extended grid's spacing: the strength
        // 0.75 between 0.67 and 0.85 reads 0.71 and 0.80; the depth's low end 2 between 1.5 and 2.5 reads 1.75 and 2.25;
        // its high end 4 between 3 and 5 reads 3.5 and 4.5; the reward to risk 2 between 1.5 and 2.5 reads 1.75 and 2.25;
        // the market floor 0.50 between 0.45 and 0.55 reads 0.475 and 0.525.
        Assert.Equal([0.71, 0.75, 0.80], grid.StrengthBars);
        Assert.Equal([1.75, 2, 2.25], grid.DepthLows);
        Assert.Equal([3.5, 4, 4.5], grid.DepthHighs);
        Assert.Equal([1.75, 2, 2.25], grid.RewardToRiskFloors);
        Assert.Equal([0.475, 0.50, 0.525], grid.MarketFloors);

        // A whole-number dial reads the integer nearer the setting: the freshness 8 between 5 and 11 reads 7 and 9, the
        // band strength 6 between 4 and 8 reads 5 and 7.
        Assert.Equal([7, 8, 9], grid.Freshness);
        Assert.Equal([5, 6, 7], grid.BandStrengths);

        // The dry-up off has no half step, so its nearest level, 2.0, is read on the one side.
        Assert.Equal([2.0, SweepGrid.Off], grid.DryUpCeilings);

        // The centre sits on the setting's value on every dial.
        Assert.Equal((0.75, 2.0, 4.0, SweepGrid.Off, 8, 2.0, 0.50, 6), (
            grid.StrengthBars[centre.Strength],
            grid.DepthLows[centre.DepthLow],
            grid.DepthHighs[centre.DepthHigh],
            grid.DryUpCeilings[centre.DryUp],
            grid.Freshness[centre.Freshness],
            grid.RewardToRiskFloors[centre.RewardToRisk],
            grid.MarketFloors[centre.Market],
            grid.BandStrengths[centre.Band]));
        Assert.Equal(named.Stop, centre.Stop);

        // Eight ordered dials at two half steps each but the dry-up at one, 15, and the stop's options sharing one end
        // with 0.5 to 2.5: 0.5 to 4 at its high end and 1 to 2.5 at its low, 17 neighbours each moving one dial.
        Assert.Equal(17, neighbours.Count);
        Assert.Equal(["dry-up lower at 2"], neighbours.Where(one => one.Dial == "dry-up").Select(one => one.Dial + " " + one.Direction + " at " + one.Value));
        Assert.Equal(
            ["stop higher at its high end at 0.5 to 4", "stop higher at its low end at 1 to 2.5"],
            neighbours.Where(one => one.Dial == "stop").Select(one => one.Dial + " " + one.Direction + " at " + one.Value).Order(StringComparer.Ordinal));
        Assert.All(neighbours, one => Assert.Equal(1, Moved(centre, one.Setting)));
        Assert.Equal(["freshness lower at 7", "freshness higher at 9"], neighbours.Where(one => one.Dial == "freshness").Select(one => one.Dial + " " + one.Direction + " at " + one.Value));
    }

    static int Moved(DialSetting one, DialSetting other) =>
        (one.Strength != other.Strength ? 1 : 0) + (one.DepthLow != other.DepthLow ? 1 : 0) + (one.DepthHigh != other.DepthHigh ? 1 : 0)
        + (one.DryUp != other.DryUp ? 1 : 0) + (one.Freshness != other.Freshness ? 1 : 0) + (one.RewardToRisk != other.RewardToRisk ? 1 : 0)
        + (one.Stop != other.Stop ? 1 : 0) + (one.Market != other.Market ? 1 : 0) + (one.Band != other.Band ? 1 : 0);

    [Fact]
    public void EachExitIsReplayedOnClosesAtItsEdge()
    {
        // Bought at 100 with the stop at 98, a risk of 2, the target at 106, a typical move of 1 and a cap of 10.
        const int cap = 10;

        double Under(int exit, params double[] after) => SweepExits.Replay([100, .. after, .. Enumerable.Repeat(100.0, cap)], 0, 100, 98, 106, 1, exit, cap) ?? double.NaN;

        // A close at 97.99 loses a session later at -1.005 risks; one at 98 holds; one at 106 wins at 3.
        Assert.Equal(-1.005, Under(SweepExits.Fixed, 97.99), 9);
        Assert.Equal(3.0, Under(SweepExits.Fixed, 106), 9);
        Assert.Equal(0.0, Under(SweepExits.Fixed, 98, 100), 9);

        // Under the fixed exit a close a risk up moves nothing: 102 then 99 then 97.99 loses at -1.005. Under break-even
        // the close at 102 arms the move and the stop stands at the buy from the next session: 99 closes under it, a
        // result of -0.5. A close at 101.99 arms nothing. The stop moves after the session's own reading: 102 then 97.99
        // loses at -1.005 either way.
        Assert.Equal(-1.005, Under(SweepExits.Fixed, 102, 99, 97.99), 9);
        Assert.Equal(-0.5, Under(SweepExits.BreakEven, 102, 99, 97.99), 9);
        Assert.Equal(-1.005, Under(SweepExits.BreakEven, 101.99, 99, 97.99), 9);
        Assert.Equal(-1.005, Under(SweepExits.BreakEven, 102, 97.99), 9);

        // Under the trail the close at 102 sets the stop 2 moves under the highest close, 100; 104 raises it to 102; 101
        // closes under it, a result of 0.5. The stop is never lowered: 104 then 103 holds the stop at 102, and 101.99
        // closes under it at 0.995.
        Assert.Equal(0.5, Under(SweepExits.Trail, 102, 104, 101), 9);
        Assert.Equal(0.995, Under(SweepExits.Trail, 102, 104, 103, 101.99), 9);

        // The trail never lowers below the plan's stop and arms only at a risk up: 101 then 97.99 loses at -1.005.
        Assert.Equal(-1.005, Under(SweepExits.Trail, 101, 97.99), 9);

        // The cap's close ends a trade neither reached, and a series that runs out first has no result.
        Assert.Equal(0.5, SweepExits.Replay([100, 101, 101, 101, 101, 101, 101, 101, 101, 101, 101], 0, 100, 98, 106, 1, SweepExits.Fixed, cap) ?? double.NaN, 9);
        Assert.Null(SweepExits.Replay([100, 101, 101], 0, 100, 98, 106, 1, SweepExits.Fixed, cap));
        Assert.Null(SweepExits.Replay([100, 101], 0, 100, 100, 106, 1, SweepExits.Fixed, cap));
    }
}
