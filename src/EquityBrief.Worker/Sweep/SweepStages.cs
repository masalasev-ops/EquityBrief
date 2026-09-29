using EquityBrief.Core.Sweep;

namespace EquityBrief.Worker.Sweep;

// One candidate as one design reads it: where it stands against the grid's dials and the plan whose outcomes it
// carries.
public readonly record struct SweepPick(int Index, int Session, SweepCorner Corner, SweepPlanOutcomes Plan, int Year, int Block);

// What stage 1 read of one design: its coarse settings, how many of them were viable, their share, the median
// result of the viable ones in multiples of risk, and the live setting's figures where the design is the live
// rule's own.
public sealed record SweepDesignRank(
    SweepDesign Design,
    int Settings,
    int Viable,
    double ViableShare,
    double? MedianMultiple,
    SweepMeasures? Live);

// One variation of stage 2, the figures its plateau is read over.
public readonly record struct SweepVariation(
    int Stop,
    int Cell,
    float Share,
    float BreakEven,
    float NoSkill,
    float AverageMultiple,
    int Scored,
    byte YearsBeatingBreakEven,
    bool WithoutBestYear,
    byte Blocks,
    float Listing)
{
    public bool BeatsBoth => !float.IsNaN(Share) && Share > BreakEven && Share > NoSkill;

    public bool MeetsTheFloors =>
        YearsBeatingBreakEven >= SweepMeasures.YearsBeating
        && WithoutBestYear
        && Scored >= SweepMeasures.TradeFloor
        && Blocks >= SweepMeasures.BlockFloor
        && Listing >= SweepMeasures.ListingShare;
}

// The two stages' arithmetic over the candidates.
public static class SweepStages
{
    // The candidates one design reads, in session order, each with its corner on the grid and its plan.
    public static List<SweepPick> Picks(IReadOnlyList<SweepCandidate> candidates, SweepDesign design, SweepGrid grid)
    {
        var picks = new List<SweepPick>();
        var high = SweepAxes.ReferenceHighs.ToList().IndexOf(design.ReferenceHigh);
        var bit = (byte)(1 << (int)design.Uptrend);
        var age = SweepCandidate.AgeAt(design.Trigger, design.Support);
        var planAt = SweepCandidate.PlanAt(design.Plan, design.Support);

        for (var at = 0; at < candidates.Count; at++)
        {
            var candidate = candidates[at];

            if ((candidate.Uptrend & bit) == 0
                || candidate.Band[(int)design.Support] < 0
                || candidate.Age[age] < 0
                || candidate.Plans[planAt] is not { } plan
                || (design.EarningsWindow > 0 && candidate.Earnings >= 0 && candidate.Earnings <= design.EarningsWindow))
            {
                continue;
            }

            var corner = SweepCorner.Of(
                grid,
                candidate.Strength[(int)design.Strength],
                candidate.Depth[high],
                candidate.DryUp[high],
                candidate.Age[age],
                plan.RewardToRisk,
                plan.StopMoves,
                candidate.Breadth,
                candidate.Band[(int)design.Support]);

            if (corner is { } placed)
            {
                picks.Add(new SweepPick(at, candidate.Session, placed, plan, candidate.Year, candidate.Block));
            }
        }

        return picks;
    }

    // Stage 1 for one selection design: every exit of it over every coarse setting.
    public static IReadOnlyList<SweepDesignRank> Rank(IReadOnlyList<SweepCandidate> candidates, SweepDesign selection, int nights)
    {
        var grid = SweepGrid.Coarse;
        var picks = Picks(candidates, selection, grid);
        var width = SweepFigures.Width;
        var figures = new float[width];
        var listing = Enumerable.Range(0, grid.StopBounds.Count).Select(stop => Nights(picks, grid, stop)).ToArray();
        var ranks = new List<SweepDesignRank>();

        for (var exit = 0; exit < SweepAxes.Exits; exit++)
        {
            var (hold, breakEven) = SweepAxes.ExitOf(exit);
            var design = selection with { Hold = hold, BreakEven = breakEven };
            var viable = 0;
            var multiples = new List<double>();
            SweepMeasures? live = null;

            for (var stop = 0; stop < grid.StopBounds.Count; stop++)
            {
                var table = new SweepTable(grid, width, stop);

                foreach (var pick in picks)
                {
                    SweepFigures.Fill(figures, pick.Year, pick.Block, pick.Plan.Code[exit], pick.Plan.Null[exit], pick.Plan.BreakEven[exit], pick.Plan.Multiple[exit]);
                    table.Add(pick.Corner, figures);
                }

                table.Accumulate();

                for (var cell = 0; cell < grid.CellsPerStop; cell++)
                {
                    var measures = SweepMeasures.Of(table.At(cell), listing[stop][cell], nights);

                    if (measures.Viable)
                    {
                        viable++;

                        if (measures.AverageMultiple is { } multiple)
                        {
                            multiples.Add(multiple);
                        }
                    }

                    if (design == SweepDesign.Live && stop == DialSetting.LiveOnCoarse.Stop && cell == DialSetting.LiveOnCoarse.Cell(grid))
                    {
                        live = measures;
                    }
                }
            }

            ranks.Add(new SweepDesignRank(
                design,
                grid.Variations,
                viable,
                (double)viable / grid.Variations,
                multiples.Count == 0 ? null : Median(multiples),
                live));
        }

        return ranks;
    }

    // Stage 2 for one design: every setting of the fine grid, or of the grid handed in.
    public static SweepVariation[] Fine(IReadOnlyList<SweepCandidate> candidates, SweepDesign design, int nights, SweepGrid? over = null)
    {
        var grid = over ?? SweepGrid.Fine;
        var picks = Picks(candidates, design, grid);
        var width = SweepFigures.Width;
        var exit = design.ExitIndex;
        var variations = new SweepVariation[grid.Variations];

        for (var stop = 0; stop < grid.StopBounds.Count; stop++)
        {
            var table = new SweepTable(grid, width, stop);
            var figures = new float[width];

            foreach (var pick in picks)
            {
                SweepFigures.Fill(figures, pick.Year, pick.Block, pick.Plan.Code[exit], pick.Plan.Null[exit], pick.Plan.BreakEven[exit], pick.Plan.Multiple[exit]);
                table.Add(pick.Corner, figures);
            }

            table.Accumulate();

            var listing = Nights(picks, grid, stop);

            Parallel.For(0, grid.CellsPerStop, cell =>
            {
                var measures = SweepMeasures.Of(table.At(cell), listing[cell], nights);

                variations[(stop * grid.CellsPerStop) + cell] = new SweepVariation(
                    stop,
                    cell,
                    (float)(measures.Share ?? double.NaN),
                    (float)(measures.BreakEven ?? double.NaN),
                    (float)(measures.NoSkill ?? double.NaN),
                    (float)(measures.AverageMultiple ?? double.NaN),
                    measures.Scored,
                    (byte)measures.YearsBeatingBreakEven,
                    measures.BeatsBreakEvenWithoutItsBestYear,
                    (byte)measures.BlocksWithTrades,
                    (float)measures.ListingShareOfNights);
            });
        }

        return variations;
    }

    // One setting of one design read directly over its candidates, with no table: the figures the tables are held
    // to, and the ones the report states for a starting point, its variants and the live rule.
    public static SweepMeasures Direct(IReadOnlyList<SweepCandidate> candidates, SweepDesign design, SweepGrid grid, DialSetting setting, int nights, HashSet<(int Name, int Session)>? picked = null)
    {
        var width = SweepFigures.Width;
        var sum = new float[width];
        var figures = new float[width];
        var listed = new HashSet<int>();

        foreach (var pick in Picks(candidates, design, grid))
        {
            if (!pick.Corner.Passes(setting))
            {
                continue;
            }

            SweepFigures.Fill(figures, pick.Year, pick.Block, pick.Plan.Code[design.ExitIndex], pick.Plan.Null[design.ExitIndex], pick.Plan.BreakEven[design.ExitIndex], pick.Plan.Multiple[design.ExitIndex]);

            for (var at = 0; at < width; at++)
            {
                sum[at] += figures[at];
            }

            listed.Add(pick.Session);
            picked?.Add((candidates[pick.Index].Name, pick.Session));
        }

        return SweepMeasures.Of(sum, listed.Count, nights);
    }

    static int[] Nights(IReadOnlyList<SweepPick> picks, SweepGrid grid, int stop)
    {
        var counter = new SweepNights(grid, stop);

        foreach (var pick in picks)
        {
            counter.Add(pick.Session, pick.Corner);
        }

        return counter.Counts();
    }

    static double Median(List<double> values)
    {
        values.Sort();

        return values.Count % 2 == 1 ? values[values.Count / 2] : (values[(values.Count / 2) - 1] + values[values.Count / 2]) / 2;
    }
}
