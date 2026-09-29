using EquityBrief.Core.Sweep;

namespace EquityBrief.Worker.Sweep;

// A starting point proposed from one design's plateau: the setting, what its neighbours' results came to, and
// its own figures read directly.
public sealed record SweepStart(SweepDesign Design, DialSetting Setting, double NeighbourMedian, int PlateauSize, SweepMeasures Measures);

// A variant proposed beside the starting point: the one thing moved, which part of the filter it moves, and the
// evidence against each of the four tests.
public sealed record SweepVariant(
    string Part,
    string Change,
    SweepDesign Design,
    DialSetting Setting,
    SweepMeasures Measures,
    int YearsItWins,
    int YearsTheStartWins,
    double OutsideShare,
    int FewestTradesInAYear,
    bool OnThePlateau,
    bool Open,
    bool DifferentEnough,
    bool EnoughTrades)
{
    public bool Passes => OnThePlateau && Open && DifferentEnough && EnoughTrades;
}

// The plateau, the starting point and the variants, over the fine grid of the designs stage 2 carried.
// see: A starting point is proposed from the centre of a plateau of the stored history and never its best variation, and nothing is registered before the operator approves it
public static class SweepPlateau
{
    // Section 17's figures for the plateau and the variants, each proposed and the operator's to rule.
    public const double NeighboursOnIt = 0.75;
    public const int YearsEitherMayWin = 5;
    public const double OutsideFloor = 0.25;
    public const int TradesAYear = 30;
    public const int MostVariants = 6;

    // The settings one step away on a single dial, the stop bounds stepping through their options in order.
    public static IEnumerable<DialSetting> Neighbours(SweepGrid grid, DialSetting setting)
    {
        var sizes = new[]
        {
            grid.StrengthBars.Count, grid.DepthLows.Count, grid.DepthHighs.Count, grid.DryUpCeilings.Count, grid.Freshness.Count,
            grid.RewardToRiskFloors.Count, grid.StopBounds.Count, grid.MarketFloors.Count, grid.BandStrengths.Count,
        };
        int[] at = [setting.Strength, setting.DepthLow, setting.DepthHigh, setting.DryUp, setting.Freshness, setting.RewardToRisk, setting.Stop, setting.Market, setting.Band];

        for (var dial = 0; dial < at.Length; dial++)
        {
            foreach (var step in new[] { -1, 1 })
            {
                var moved = at[dial] + step;

                if (moved < 0 || moved >= sizes[dial])
                {
                    continue;
                }

                var next = (int[])at.Clone();

                next[dial] = moved;

                yield return new DialSetting(next[0], next[1], next[2], next[3], next[4], next[5], next[6], next[7], next[8]);
            }
        }
    }

    public static int Index(SweepGrid grid, DialSetting setting) => (setting.Stop * grid.CellsPerStop) + setting.Cell(grid);

    // Whether a setting is on its design's plateau: it and at least three quarters of its neighbours beat both
    // their break-even and no skill.
    public static bool OnThePlateau(SweepGrid grid, SweepVariation[] variations, DialSetting setting)
    {
        if (!variations[Index(grid, setting)].BeatsBoth)
        {
            return false;
        }

        var neighbours = Neighbours(grid, setting).Select(neighbour => variations[Index(grid, neighbour)]).ToArray();

        return neighbours.Length > 0 && neighbours.Count(neighbour => neighbour.BeatsBoth) >= NeighboursOnIt * neighbours.Length;
    }

    // The plateau's centre: of the settings on the plateau meeting the four floors, the one whose neighbours'
    // median result in multiples of risk is highest, and none where no setting qualifies.
    public static (DialSetting Setting, double NeighbourMedian, int PlateauSize)? Centre(SweepGrid grid, SweepVariation[] variations)
    {
        (DialSetting Setting, double Median)? best = null;
        var plateau = 0;

        for (var index = 0; index < variations.Length; index++)
        {
            var setting = DialSetting.Of(grid, index / grid.CellsPerStop, index % grid.CellsPerStop);

            if (!OnThePlateau(grid, variations, setting))
            {
                continue;
            }

            plateau++;

            if (!variations[index].MeetsTheFloors)
            {
                continue;
            }

            var results = Neighbours(grid, setting)
                .Select(double (neighbour) => variations[Index(grid, neighbour)].AverageMultiple)
                .Where(result => !double.IsNaN(result))
                .Order()
                .ToArray();

            if (results.Length == 0)
            {
                continue;
            }

            var median = results.Length % 2 == 1 ? results[results.Length / 2] : (results[(results.Length / 2) - 1] + results[results.Length / 2]) / 2;

            if (best is null || median > best.Value.Median)
            {
                best = (setting, median);
            }
        }

        return best is { } found ? (found.Setting, found.Median, plateau) : null;
    }

    // The variants: each one-step move from the starting point on a dial, and each structural choice stage 2
    // carried both sides of, tested against the four tests and ranked by openness, difference and trades, spread
    // across the filter's parts, at most six.
    public static IReadOnlyList<SweepVariant> Variants(
        IReadOnlyList<SweepCandidate> candidates,
        SweepStart start,
        IReadOnlyDictionary<SweepDesign, SweepVariation[]> fine,
        int nights)
    {
        var grid = SweepGrid.Fine;
        var startPicks = new HashSet<(int, int)>();
        var startMeasures = SweepStages.Direct(candidates, start.Design, grid, start.Setting, nights, startPicks);
        var tested = new List<SweepVariant>();

        SweepVariant Test(string part, string change, SweepDesign design, DialSetting setting, bool onThePlateau)
        {
            var picks = new HashSet<(int, int)>();
            var measures = SweepStages.Direct(candidates, design, grid, setting, nights, picks);

            return Judge(part, change, design, setting, measures, picks, startMeasures, startPicks, onThePlateau);
        }

        var variations = fine[start.Design];

        foreach (var neighbour in Neighbours(grid, start.Setting))
        {
            var (part, change) = Moved(grid, start.Setting, neighbour);

            tested.Add(Test(part, change, start.Design, neighbour, variations[Index(grid, neighbour)].BeatsBoth));
        }

        foreach (var (design, other) in fine)
        {
            if (design == start.Design || StructuralDifference(start.Design, design) is not { } difference)
            {
                continue;
            }

            tested.Add(Test(difference.Part, difference.Change, design, start.Setting, other[Index(grid, start.Setting)].BeatsBoth));
        }

        var ranked = tested
            .Where(variant => variant.Passes)
            .OrderBy(variant => Math.Abs(variant.YearsItWins - variant.YearsTheStartWins))
            .ThenByDescending(variant => variant.OutsideShare)
            .ThenByDescending(variant => variant.Measures.Scored)
            .ToList();

        var chosen = new List<SweepVariant>();

        foreach (var part in ranked.Select(variant => variant.Part).Distinct())
        {
            if (chosen.Count < MostVariants)
            {
                chosen.Add(ranked.First(variant => variant.Part == part));
            }
        }

        foreach (var variant in ranked.Where(variant => !chosen.Contains(variant)))
        {
            if (chosen.Count < MostVariants)
            {
                chosen.Add(variant);
            }
        }

        return [.. chosen.Concat(tested.Where(variant => !variant.Passes)).OrderBy(variant => variant.Passes ? 0 : 1)];
    }

    // One variant against the four tests: its one change reaching a setting on the plateau; open, neither it
    // nor the starting point having the higher average result in more than five of the eight years; at least
    // a quarter of its picks ones the starting point does not pick; and at least thirty trades in every year.
    public static SweepVariant Judge(
        string part,
        string change,
        SweepDesign design,
        DialSetting setting,
        SweepMeasures measures,
        IReadOnlySet<(int Name, int Session)> picks,
        SweepMeasures startMeasures,
        IReadOnlySet<(int Name, int Session)> startPicks,
        bool onThePlateau)
    {
        var (mine, theirs) = (0, 0);

        for (var year = 0; year < SweepFigures.Years; year++)
        {
            if (measures.YearAverageMultiple[year] is not { } own || startMeasures.YearAverageMultiple[year] is not { } other)
            {
                continue;
            }

            mine += own > other ? 1 : 0;
            theirs += other > own ? 1 : 0;
        }

        var outside = picks.Count == 0 ? 0 : (double)picks.Count(pick => !startPicks.Contains(pick)) / picks.Count;
        var fewest = measures.YearScored.Min();

        return new SweepVariant(
            part,
            change,
            design,
            setting,
            measures,
            mine,
            theirs,
            outside,
            fewest,
            onThePlateau,
            mine <= YearsEitherMayWin && theirs <= YearsEitherMayWin,
            outside >= OutsideFloor,
            fewest >= TradesAYear);
    }

    // The part of the filter a dial belongs to, and the move in plain words.
    static (string Part, string Change) Moved(SweepGrid grid, DialSetting from, DialSetting to)
    {
        static string Number(double value) => value.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);

        if (from.Strength != to.Strength)
        {
            return ("strength", $"the strength bar from {Number(grid.StrengthBars[from.Strength])} to {Number(grid.StrengthBars[to.Strength])}");
        }

        if (from.DepthLow != to.DepthLow)
        {
            return ("pullback", $"the pullback's shallowest depth from {Number(grid.DepthLows[from.DepthLow])} to {Number(grid.DepthLows[to.DepthLow])} typical moves");
        }

        if (from.DepthHigh != to.DepthHigh)
        {
            return ("pullback", $"the pullback's deepest from {Number(grid.DepthHighs[from.DepthHigh])} to {Number(grid.DepthHighs[to.DepthHigh])} typical moves");
        }

        if (from.DryUp != to.DryUp)
        {
            string Ceiling(int at) => double.IsPositiveInfinity(grid.DryUpCeilings[at]) ? "no ceiling" : Number(grid.DryUpCeilings[at]);

            return ("pullback", $"the volume while it came down from {Ceiling(from.DryUp)} to {Ceiling(to.DryUp)} times its average");
        }

        if (from.Freshness != to.Freshness)
        {
            return ("trigger", $"the trigger's window from {grid.Freshness[from.Freshness]} to {grid.Freshness[to.Freshness]} sessions");
        }

        if (from.RewardToRisk != to.RewardToRisk)
        {
            return ("trade", $"the reward to risk floor from {Number(grid.RewardToRiskFloors[from.RewardToRisk])} to {Number(grid.RewardToRiskFloors[to.RewardToRisk])}");
        }

        if (from.Stop != to.Stop)
        {
            return ("trade", $"the stop's bounds from {Number(grid.StopBounds[from.Stop].Low)} to {Number(grid.StopBounds[from.Stop].High)} typical moves to {Number(grid.StopBounds[to.Stop].Low)} to {Number(grid.StopBounds[to.Stop].High)}");
        }

        if (from.Market != to.Market)
        {
            string Floor(int at) => double.IsNegativeInfinity(grid.MarketFloors[at]) ? "off" : Number(grid.MarketFloors[at] * 100) + "%";

            return ("market", $"the market's floor from {Floor(from.Market)} to {Floor(to.Market)}");
        }

        return ("pullback", $"the setup band's least strength from {grid.BandStrengths[from.Band]} to {grid.BandStrengths[to.Band]}");
    }

    // The one structural choice two designs differ in, or none where they differ in none or in more than one.
    static (string Part, string Change)? StructuralDifference(SweepDesign from, SweepDesign to)
    {
        var differences = new List<(string, string)>();

        if (from.Strength != to.Strength) differences.Add(("strength", $"the strength measure from {from.Strength} to {to.Strength}"));
        if (from.Uptrend != to.Uptrend) differences.Add(("strength", $"the uptrend rule from {from.Uptrend} to {to.Uptrend}"));
        if (from.Support != to.Support) differences.Add(("pullback", $"the support from {from.Support} to {to.Support}"));
        if (from.ReferenceHigh != to.ReferenceHigh) differences.Add(("pullback", $"the pullback's reference high from {from.ReferenceHigh} to {to.ReferenceHigh} sessions"));
        if (from.Trigger != to.Trigger) differences.Add(("trigger", $"the trigger from {from.Trigger} to {to.Trigger}"));
        if (from.Plan != to.Plan) differences.Add(("trade", $"the plan from {from.Plan} to {to.Plan}"));
        if (from.Hold != to.Hold || from.BreakEven != to.BreakEven) differences.Add(("trade", $"the exit from a hold of {from.Hold}{(from.BreakEven ? " with the move to break-even" : string.Empty)} to {to.Hold}{(to.BreakEven ? " with the move to break-even" : string.Empty)}"));
        if (from.EarningsWindow != to.EarningsWindow) differences.Add(("trade", $"the earnings window from {from.EarningsWindow} to {to.EarningsWindow} sessions"));

        return differences.Count == 1 ? differences[0] : null;
    }
}
