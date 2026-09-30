using EquityBrief.Core.Sweep;

namespace EquityBrief.Worker.Sweep;

// A starting point proposed from one design's plateau: the design, the point in its space, its depth and its
// record.
public sealed record SweepStart(SweepDesign Design, SweepSpace Space, int[] Point, SweepDepth Depth, SweepMeasures Measures, float Line);

// A variant proposed beside the starting point: the one thing moved, which part of the filter it moves, and the
// evidence against each of the four tests, each read on the edge.
public sealed record SweepVariant(
    string Part,
    string Change,
    SweepDesign Design,
    int[] Point,
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

    // The tests it fails, each naming by how much.
    public IReadOnlyList<string> Failing(float line)
    {
        var failing = new List<string>();

        if (!OnThePlateau)
        {
            failing.Add(Measures.Edge is { } edge ? FormattableString.Invariant($"off the plateau, its edge {edge:0.000} against a line of {line:0.000} or a floor missed") : "off the plateau, with no edge");
        }

        if (!Open)
        {
            failing.Add(FormattableString.Invariant($"not open, {(YearsItWins > YearsTheStartWins ? "it" : "the starting point")} winning {Math.Max(YearsItWins, YearsTheStartWins)} of the 8 years against {SweepPlateau.YearsEitherMayWin} allowed"));
        }

        if (!DifferentEnough)
        {
            failing.Add(FormattableString.Invariant($"not different enough, {OutsideShare * 100:0}% of its picks outside the starting point's against {SweepPlateau.OutsideFloor * 100:0}% needed"));
        }

        if (!EnoughTrades)
        {
            failing.Add(FormattableString.Invariant($"too few trades, {FewestTradesInAYear} in its thinnest year against {SweepPlateau.TradesAYear} needed"));
        }

        return failing;
    }
}

// The variants beside the starting point, each one change from it: a dial moved one or two steps either way, a
// condition among them switched on, off or moved, or one structural choice changed, held to the four tests on
// the edge.
// see: A starting point is proposed from the deepest setting of a plateau on the edge and never its best variation, and nothing is registered before the operator approves it
public static class SweepPlateau
{
    // Section 17's figures for the variants, each proposed and the operator's to rule.
    public const int YearsEitherMayWin = 5;
    public const double OutsideFloor = 0.25;
    public const int TradesAYear = 30;
    public const int MostVariants = 6;
    public const int StrongestReported = 3;

    // The variants: each one-step and two-step move on a dial, and each structural choice changed alone, tested
    // against the four tests and ranked by openness, difference and trades, spread across the filter's parts,
    // at most six; where fewer than three pass, the strongest three by edge follow, each naming what it fails.
    public static IReadOnlyList<SweepVariant> Variants(IReadOnlyList<SweepCandidate> candidates, SweepStart start, int nights)
    {
        var space = start.Space;
        var startPicks = new HashSet<(int, int)>();
        var picks = SweepStages.Picks(candidates, start.Design);
        var startMeasures = SweepStages.Measures(picks, start.Design, space.Setting(start.Point), space.Conditions(start.Point), nights, startPicks);
        var tested = new List<SweepVariant>();

        SweepVariant Test(string part, string change, SweepDesign design, IReadOnlyList<SweepPick> over, int[] point)
        {
            var kept = new HashSet<(int, int)>();
            var measures = SweepStages.Measures(over, design, space.Setting(point), space.Conditions(point), nights, kept);

            return Judge(part, change, design, point, measures, kept, startMeasures, startPicks, start.Line);
        }

        for (var dial = 0; dial < space.Count; dial++)
        {
            if (space.IsNoOp(start.Point, dial))
            {
                continue;
            }

            foreach (var steps in new[] { -2, -1, 1, 2 })
            {
                if (space.Step(start.Point, dial, steps) is not { } moved || space.Moved(start.Point, moved) is not { } move)
                {
                    continue;
                }

                tested.Add(Test(move.Part, move.Change, start.Design, picks, moved));
            }
        }

        foreach (var neighbour in StructuralNeighbours(start.Design))
        {
            if (StructuralDifference(start.Design, neighbour) is not { } difference)
            {
                continue;
            }

            tested.Add(Test(difference.Part, difference.Change, neighbour, SweepStages.Picks(candidates, neighbour), start.Point));
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

        var failing = tested.Where(variant => !variant.Passes).OrderByDescending(variant => variant.Measures.Edge ?? double.MinValue).ToList();

        return [.. chosen, .. failing];
    }

    // One variant against the four tests: its one change reaching a setting on the plateau; open, neither it
    // nor the starting point having the higher edge in more than five of the eight years; at least a quarter of
    // its picks ones the starting point does not pick; and at least thirty trades in every year.
    public static SweepVariant Judge(
        string part,
        string change,
        SweepDesign design,
        int[] point,
        SweepMeasures measures,
        IReadOnlySet<(int Name, int Session)> picks,
        SweepMeasures startMeasures,
        IReadOnlySet<(int Name, int Session)> startPicks,
        float line)
    {
        var (mine, theirs) = (0, 0);

        for (var year = 0; year < SweepFigures.Years; year++)
        {
            if (measures.YearEdge[year] is not { } own || startMeasures.YearEdge[year] is not { } other)
            {
                continue;
            }

            mine += own > other ? 1 : 0;
            theirs += other > own ? 1 : 0;
        }

        var outside = picks.Count == 0 ? 0 : (double)picks.Count(pick => !startPicks.Contains(pick)) / picks.Count;
        var fewest = measures.YearScored.Min();
        var onThePlateau = measures.MeetsTheFloors && measures.Edge is { } edge && !float.IsNaN(line) && edge >= line;

        return new SweepVariant(
            part,
            change,
            design,
            point,
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

    // A design's structural neighbours: each structural choice, the exit's hold and its move to break-even among
    // them, changed to each other value alone, the inexpressible left out: 23 for a design every choice of
    // which can be moved.
    public static IReadOnlyList<SweepDesign> StructuralNeighbours(SweepDesign design)
    {
        var neighbours = new List<SweepDesign>(SweepSearch.SelectionNeighbours(design));

        foreach (var hold in SweepAxes.Holds.Where(value => value != design.Hold)) neighbours.Add(design with { Hold = hold });

        neighbours.Add(design with { BreakEven = !design.BreakEven });

        return [.. neighbours.Where(SweepAxes.Expressible)];
    }

    // The one structural choice two designs differ in, or none where they differ in none or in more than one.
    public static (string Part, string Change)? StructuralDifference(SweepDesign from, SweepDesign to)
    {
        var differences = new List<(string, string)>();

        if (from.Strength != to.Strength) differences.Add(("strength", $"the strength measure from {from.Strength} to {to.Strength}"));
        if (from.Uptrend != to.Uptrend) differences.Add(("strength", $"the uptrend rule from {from.Uptrend} to {to.Uptrend}"));
        if (from.Support != to.Support) differences.Add(("pullback", $"the support from {from.Support} to {to.Support}"));
        if (from.ReferenceHigh != to.ReferenceHigh) differences.Add(("pullback", $"the pullback's reference high from {from.ReferenceHigh} to {to.ReferenceHigh} sessions"));
        if (from.Trigger != to.Trigger) differences.Add(("trigger", $"the trigger from {from.Trigger} to {to.Trigger}"));
        if (from.Plan != to.Plan) differences.Add(("trade", $"the plan from {from.Plan} to {to.Plan}"));
        if (from.Hold != to.Hold) differences.Add(("trade", $"the hold from {from.Hold} to {to.Hold} sessions"));
        if (from.BreakEven != to.BreakEven) differences.Add(("trade", to.BreakEven ? "the stop moved to break-even once a close stands the risk above the entry" : "the stop never moved"));
        if (from.EarningsWindow != to.EarningsWindow) differences.Add(("trade", $"the earnings window from {from.EarningsWindow} to {to.EarningsWindow} sessions"));

        return differences.Count == 1 ? differences[0] : null;
    }
}
