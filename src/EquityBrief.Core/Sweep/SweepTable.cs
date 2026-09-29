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

    // Whether the candidate passes a setting, read directly rather than off the table: what the table is held to.
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

    // The cell a candidate is laid in on its table: the last level of each dial passed up to one, and the first
    // of each passed from one.
    public int Cell(SweepGrid grid) =>
        new DialSetting(StrengthLast, DepthLowLast, DepthHighFirst, DryUpFirst, FreshFirst, RewardLast, 0, MarketLast, BandLast).Cell(grid);

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

// A table over a grid at one of its stop options: for every setting of the other dials, the sum of a vector of
// figures over the candidates that setting passes. Each candidate the stop option passes is laid once in its
// corner cell, and the cumulative sums along each dial then carry it to every cell whose setting passes it: along
// a dial passed up to a last level, from the top level down; along a dial passed from a first level, from the
// bottom up. One stop option to a table, so the finest grid's table is read an option at a time.
public sealed class SweepTable
{
    // The direction each ordered dial accumulates in, in the order a cell is indexed: true where a candidate
    // passes the levels up to a last one.
    static readonly bool[] UpToALast = [true, true, false, false, false, true, true, true];

    readonly SweepGrid grid;
    readonly int width;
    readonly int stop;
    readonly float[][] cells;

    public SweepTable(SweepGrid grid, int width, int stop)
    {
        this.grid = grid;
        this.width = width;
        this.stop = stop;
        cells = [new float[grid.CellsPerStop * width]];
    }

    public int Width => width;

    public int Stop => stop;

    public void Add(SweepCorner corner, ReadOnlySpan<float> figures)
    {
        if ((corner.StopMask & (1 << stop)) == 0)
        {
            return;
        }

        var into = cells[0].AsSpan(corner.Cell(grid) * width, width);

        for (var at = 0; at < width; at++)
        {
            into[at] += figures[at];
        }
    }

    // Carries every candidate from its corner to every cell passing it. Called once, after the last one is laid.
    public void Accumulate()
    {
        var sizes = grid.Sizes;
        var strides = new int[sizes.Count];

        strides[^1] = 1;

        for (var dial = sizes.Count - 2; dial >= 0; dial--)
        {
            strides[dial] = strides[dial + 1] * sizes[dial + 1];
        }

        foreach (var table in cells)
        {
            for (var dial = 0; dial < sizes.Count; dial++)
            {
                var stride = strides[dial];
                var size = sizes[dial];

                for (var cell = 0; cell < grid.CellsPerStop; cell++)
                {
                    var level = cell / stride % size;

                    // Each line along the dial is walked once, from the end its sums start at.
                    if (UpToALast[dial] ? level != size - 1 : level != 0)
                    {
                        continue;
                    }

                    if (UpToALast[dial])
                    {
                        for (var at = size - 2; at >= 0; at--)
                        {
                            Carry(table, cell - ((size - 1 - at) * stride), cell - ((size - 2 - at) * stride));
                        }
                    }
                    else
                    {
                        for (var at = 1; at < size; at++)
                        {
                            Carry(table, cell + (at * stride), cell + ((at - 1) * stride));
                        }
                    }
                }
            }
        }
    }

    void Carry(float[] table, int into, int from)
    {
        var target = table.AsSpan(into * width, width);
        var source = table.AsSpan(from * width, width);

        for (var at = 0; at < width; at++)
        {
            target[at] += source[at];
        }
    }

    public ReadOnlySpan<float> At(int cell) => cells[0].AsSpan(cell * width, width);
}
