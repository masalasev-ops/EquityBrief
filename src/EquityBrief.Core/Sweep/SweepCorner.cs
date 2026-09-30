namespace EquityBrief.Core.Sweep;

// Where one candidate stands against each dial: the levels it passes, read as the first level it passes from
// the end its passing runs to. A dial a larger value passes more of, a strength bar, a depth's low edge, a
// reward to risk floor, a market floor or a band strength floor, is passed at every level up to a last one; a
// dial a smaller value passes more of, a depth's high edge, a dry-up ceiling or a freshness window, from a first
// one on. The stop's bounds are not ordered, so the options it passes are named one by one.
public readonly record struct SweepCorner(
    int StrengthLast,
    int DepthLowLast,
    int DepthHighFirst,
    int DryUpFirst,
    int FreshFirst,
    int RewardLast,
    int MarketLast,
    int BandLast,
    int StopMask)
{
    // The corner of a candidate's readings on a grid, or none where some dial passes it at no level.
    public static SweepCorner? Of(
        SweepGrid grid,
        double strength,
        double depth,
        double dryUp,
        int age,
        double rewardToRisk,
        double stopMoves,
        double breadth,
        int bandStrength)
    {
        var strengthLast = LastAtOrBelow(grid.StrengthBars, strength);
        var depthLowLast = LastAtOrBelow(grid.DepthLows, depth);
        var depthHighFirst = FirstAtOrAbove(grid.DepthHighs, depth);
        var dryUpFirst = FirstPassingCeiling(grid.DryUpCeilings, dryUp);
        var freshFirst = FirstWindowHolding(grid.Freshness, age);
        var rewardLast = LastAtOrBelow(grid.RewardToRiskFloors, rewardToRisk);
        var marketLast = LastMarket(grid.MarketFloors, breadth);
        var bandLast = LastAtOrBelow(grid.BandStrengths, bandStrength);
        var stopMask = 0;

        for (var option = 0; option < grid.StopBounds.Count; option++)
        {
            var (low, high) = grid.StopBounds[option];

            if (stopMoves >= low && stopMoves <= high)
            {
                stopMask |= 1 << option;
            }
        }

        if (strengthLast < 0 || depthLowLast < 0 || depthHighFirst < 0 || dryUpFirst < 0 || freshFirst < 0 || rewardLast < 0 || marketLast < 0 || bandLast < 0 || stopMask == 0)
        {
            return null;
        }

        return new SweepCorner(strengthLast, depthLowLast, depthHighFirst, dryUpFirst, freshFirst, rewardLast, marketLast, bandLast, stopMask);
    }

    // Whether the candidate passes a setting, read directly: what every walk reads.
    public bool Passes(DialSetting setting) =>
        setting.Strength <= StrengthLast
        && setting.DepthLow <= DepthLowLast
        && setting.DepthHigh >= DepthHighFirst
        && setting.DryUp >= DryUpFirst
        && setting.Freshness >= FreshFirst
        && setting.RewardToRisk <= RewardLast
        && setting.Market <= MarketLast
        && setting.Band <= BandLast
        && (StopMask & (1 << setting.Stop)) != 0;

    static int LastAtOrBelow(IReadOnlyList<double> levels, double value)
    {
        if (double.IsNaN(value))
        {
            return -1;
        }

        var last = -1;

        for (var at = 0; at < levels.Count; at++)
        {
            if (levels[at] <= value)
            {
                last = at;
            }
        }

        return last;
    }

    static int LastAtOrBelow(IReadOnlyList<int> levels, int value)
    {
        var last = -1;

        for (var at = 0; at < levels.Count; at++)
        {
            if (levels[at] <= value)
            {
                last = at;
            }
        }

        return last;
    }

    static int FirstAtOrAbove(IReadOnlyList<double> levels, double value)
    {
        if (double.IsNaN(value))
        {
            return -1;
        }

        for (var at = 0; at < levels.Count; at++)
        {
            if (levels[at] >= value)
            {
                return at;
            }
        }

        return -1;
    }

    // The dry-up passes a ceiling strictly above it, and a missing dry-up, the high made on the night, passes
    // only the ceiling that is off.
    static int FirstPassingCeiling(IReadOnlyList<double> ceilings, double dryUp)
    {
        for (var at = 0; at < ceilings.Count; at++)
        {
            if (double.IsPositiveInfinity(ceilings[at]) || (!double.IsNaN(dryUp) && dryUp < ceilings[at]))
            {
                return at;
            }
        }

        return -1;
    }

    // An arrival the given sessions back passes every window longer than that many sessions.
    static int FirstWindowHolding(IReadOnlyList<int> windows, int age)
    {
        if (age < 0)
        {
            return -1;
        }

        for (var at = 0; at < windows.Count; at++)
        {
            if (age < windows[at])
            {
                return at;
            }
        }

        return -1;
    }

    // A breadth passes every floor at or below it, the gate off among them, and a night with no breadth the gate
    // off alone.
    static int LastMarket(IReadOnlyList<double> floors, double breadth)
    {
        var last = -1;

        for (var at = 0; at < floors.Count; at++)
        {
            if (double.IsNegativeInfinity(floors[at]) || (!double.IsNaN(breadth) && breadth >= floors[at]))
            {
                last = at;
            }
        }

        return last;
    }
}
