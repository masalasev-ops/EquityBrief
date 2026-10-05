using System.Diagnostics;
using System.Globalization;
using EquityBrief.Core.Sweep;

namespace EquityBrief.Worker.Sweep;

// Step (b)'s trial of one condition setting added alone to one design at its coarse centre: the edge year by
// year with the setting against without, and whether the design keeps it.
public sealed record ConditionTrial(
    int Condition,
    string SettingKey,
    string Setting,
    string DesignKey,
    int YearsUp,
    int RecentYearsUp,
    int ScoredWith,
    int ScoredWithout,
    double? EdgeWith,
    double? EdgeWithout,
    bool Kept);

// Step (b)'s verdict on one condition: each setting's count of the designs that kept it, and whether any
// setting survives, which is what carries the condition into step (c) as a dial with no best setting chosen.
public sealed record ConditionVerdict(int Condition, string Name, IReadOnlyList<ConditionSettingCount> Settings, bool Survives);

public sealed record ConditionSettingCount(string SettingKey, string Setting, int DesignsKept);

// Step (c)'s row: one design under one combination of the surviving conditions, over the coarse settings.
public sealed record CombinationRow(string DesignKey, string Combination, int Viable, double? MedianEdge, double? MedianMultiple);

// How deep a setting sits on the plateau: the fewest single steps it can move on one dial in one direction
// before leaving it, the dial and direction that bound it, and whether that bound is a grid end the dial
// limits depth at.
public readonly record struct SweepDepth(int Depth, int Dial, int Direction, bool AtAGridEnd);

// A setting proposed from one design's search: its point, its summary, its depth and its full record.
public sealed record SweepProposal(int[] Point, SweepSummary Summary, SweepDepth Depth, SweepMeasures Measures);

// One slice of the plateau map: two dials through the starting point, the others held there, each cell's edge
// and whether it is on the plateau.
public sealed record SweepSlice(string RowDial, string ColumnDial, IReadOnlyList<string> RowLabels, IReadOnlyList<string> ColumnLabels, float[][] Edge, bool[][] OnThePlateau, int[][] Scored, int Row, int Column);

// The proposal the same design gives at another plateau margin, stated beside the ruled one.
public sealed record SweepMarginProposal(double Margin, string Setting, float Edge, int Depth);

// What stage 2 found for one design, saved when the design finishes.
public sealed record SweepDesignResult(
    string DesignKey,
    string Combination,
    IReadOnlyList<int> ConditionsOn,
    double GridSize,
    int SampleSize,
    double PerPointSeconds,
    int Evaluations,
    float BestEdge,
    float Line,
    int Leaders,
    int LeadersTrailingTheLiveRule,
    SweepProposal? Proposal,
    IReadOnlyList<string> Refinement,
    IReadOnlyList<string> Extensions,
    IReadOnlyList<string> Limits,
    IReadOnlyList<SweepSlice> Slices,
    IReadOnlyList<SweepMarginProposal> OtherMargins,
    IReadOnlyList<string> Notes);

// Stage 1 ranked on the edge, step (b) trying each condition setting alone, step (c) crossing the survivors,
// and stage 2 searching by sampling and measuring depth one dial at a time, on the operator's rulings of
// 2026-09-30. Each figure is proposed and the operator's to rule.
// see: A starting point is proposed from the deepest setting of a plateau on the edge and never its best variation, and nothing is registered before the operator approves it
public static class SweepSearch
{
    // The plateau's margin under the best edge, in multiples of the risk, and the two other margins the report
    // states the starting point at, for the operator to rule the margin over.
    public const double PlateauMargin = 0.05;

    public static IReadOnlyList<double> OtherMargins { get; } = [0.03, 0.08];

    // The leaders depth is measured around, the most steps depth is counted to, and the sample's budget a design.
    public const int Leaders = 100;
    public const int MostDepth = 6;
    public static readonly TimeSpan SampleBudget = TimeSpan.FromHours(4);
    public const int TimedPoints = 10_000;
    public const int MostSampled = 10_000_000;

    // Step (b): the designs each condition setting is tried on, the designs that must keep a setting for it to
    // survive, and the years the edge must rise in, the recent years among them.
    public const int DesignsTried = 10;
    public const int DesignsKeeping = 6;
    public const int YearsUpToKeep = 6;
    public const int RecentYearsUpToKeep = 2;

    // Step (c)'s designs carried to stage 2, and how far each slice reaches either side of the starting point.
    public const int Carried = 5;
    public const int SliceReach = 3;

    // The seed the sample and the point-in-time sample are drawn with, stated so a run can be repeated.
    public const int Seed = 20260930;

    // The coarse centre a condition is tried at: of the coarse settings meeting the floors, the one whose median
    // edge over its coarse one-step neighbours is highest; failing that the highest edge meeting the floors, and
    // failing that the highest edge of all.
    public static DialSetting CoarseCentre(IReadOnlyList<SweepPick> picks, SweepDesign design, int nights)
    {
        var coarse = SweepGrid.Coarse;
        var tally = SweepStages.Tally.OneExit(design.ExitIndex, SweepStages.SessionsOf(picks));
        var summaries = new SweepSummary[coarse.Variations];

        for (var index = 0; index < coarse.Variations; index++)
        {
            summaries[index] = SweepStages.Summary(picks, SweepStages.CoarseOnExtended[index], ConditionSetting.Off, nights, tally);
        }

        int? best = null;
        var bestMedian = double.NegativeInfinity;

        for (var index = 0; index < coarse.Variations; index++)
        {
            if (!summaries[index].MeetsTheFloors || !summaries[index].HasEdge)
            {
                continue;
            }

            var setting = DialSetting.Of(coarse, index / coarse.CellsPerStop, index % coarse.CellsPerStop);
            var edges = CoarseNeighbours(setting).Select(neighbour => summaries[(neighbour.Stop * coarse.CellsPerStop) + neighbour.Cell(coarse)]).Where(one => one.HasEdge).Select(one => (double)one.Edge).ToList();

            if (edges.Count == 0)
            {
                continue;
            }

            var median = SweepStages.Median(edges);

            if (best is null || median > bestMedian)
            {
                best = index;
                bestMedian = median;
            }
        }

        best ??= Enumerable.Range(0, coarse.Variations).Where(index => summaries[index].MeetsTheFloors && summaries[index].HasEdge).OrderByDescending(index => summaries[index].Edge).Select(index => (int?)index).FirstOrDefault();
        best ??= Enumerable.Range(0, coarse.Variations).Where(index => summaries[index].HasEdge).OrderByDescending(index => summaries[index].Edge).Select(index => (int?)index).FirstOrDefault();
        best ??= 0;

        return SweepStages.CoarseOnExtended[best.Value];
    }

    static IEnumerable<DialSetting> CoarseNeighbours(DialSetting setting)
    {
        var grid = SweepGrid.Coarse;
        int[] sizes = [grid.StrengthBars.Count, grid.DepthLows.Count, grid.DepthHighs.Count, grid.DryUpCeilings.Count, grid.Freshness.Count, grid.RewardToRiskFloors.Count, grid.StopBounds.Count, grid.MarketFloors.Count, grid.BandStrengths.Count];
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

    // Step (b): one condition setting added alone to a design at its centre, against the design without it.
    public static ConditionTrial Trial(IReadOnlyList<SweepPick> picks, SweepDesign design, DialSetting centre, int condition, ConditionSetting setting, SweepMeasures without, int nights)
    {
        var with = SweepStages.Measures(picks, design, centre, setting, nights);
        var (years, recent) = YearsUp(without, with);

        return new ConditionTrial(
            condition,
            setting.Key,
            SweepConditions.Describe(setting),
            design.Key,
            years,
            recent,
            with.Scored,
            without.Scored,
            with.Edge,
            without.Edge,
            Kept(with, years, recent));
    }

    // A setting is kept on a design where it raises the edge in at least 6 of the 8 years, at least 2 of them
    // among 2024 to 2026, with at least 300 scored trades left.
    public static bool Kept(SweepMeasures with, int yearsUp, int recentYearsUp) =>
        with.Scored >= SweepMeasures.TradeFloor && yearsUp >= YearsUpToKeep && recentYearsUp >= RecentYearsUpToKeep;

    public static (int Years, int Recent) YearsUp(SweepMeasures without, SweepMeasures with)
    {
        var years = 0;
        var recent = 0;

        for (var year = 0; year < SweepFigures.Years; year++)
        {
            if (with.YearEdge[year] is { } after && without.YearEdge[year] is { } before && after > before)
            {
                years++;
                recent += year >= SweepFigures.Years - SweepMeasures.RecentYears ? 1 : 0;
            }
        }

        return (years, recent);
    }

    // A condition survives where any of its settings was kept on at least 6 of the 10 designs; no best setting
    // is chosen, the plateau choosing among them in stage 2.
    public static IReadOnlyList<ConditionVerdict> Verdicts(IReadOnlyList<ConditionTrial> trials)
    {
        var verdicts = new List<ConditionVerdict>();

        for (var condition = 1; condition <= SweepConditions.Count; condition++)
        {
            var settings = trials
                .Where(trial => trial.Condition == condition)
                .GroupBy(trial => trial.SettingKey)
                .Select(group => new ConditionSettingCount(group.Key, group.First().Setting, group.Count(trial => trial.Kept)))
                .ToArray();

            verdicts.Add(new ConditionVerdict(condition, SweepConditions.Name(condition), settings, settings.Any(setting => setting.DesignsKept >= DesignsKeeping)));
        }

        return verdicts;
    }

    // Step (c): every combination of the survivors on and off, each on at the middle of its tested settings.
    public static IReadOnlyList<ConditionSetting> Combinations(IReadOnlyList<int> survivors)
    {
        var combinations = new List<ConditionSetting>();

        for (var mask = 0; mask < 1 << survivors.Count; mask++)
        {
            var setting = ConditionSetting.Off;

            for (var at = 0; at < survivors.Count; at++)
            {
                if ((mask & (1 << at)) != 0)
                {
                    setting = SweepConditions.Join(setting, SweepConditions.OnAtTheMiddle(survivors[at]));
                }
            }

            combinations.Add(setting);
        }

        return combinations;
    }

    // A selection design's structural neighbours short of the exit: each structural choice moved to each other
    // value alone, the inexpressible left out.
    public static IReadOnlyList<SweepDesign> SelectionNeighbours(SweepDesign selection)
    {
        var neighbours = new List<SweepDesign>();

        foreach (var strength in Enum.GetValues<StrengthMeasure>().Where(value => value != selection.Strength)) neighbours.Add(selection with { Strength = strength });
        foreach (var uptrend in Enum.GetValues<UptrendRule>().Where(value => value != selection.Uptrend)) neighbours.Add(selection with { Uptrend = uptrend });
        foreach (var support in Enum.GetValues<SupportKind>().Where(value => value != selection.Support)) neighbours.Add(selection with { Support = support });
        foreach (var high in SweepAxes.ReferenceHighs.Where(value => value != selection.ReferenceHigh)) neighbours.Add(selection with { ReferenceHigh = high });
        foreach (var trigger in Enum.GetValues<TriggerKind>().Where(value => value != selection.Trigger)) neighbours.Add(selection with { Trigger = trigger });
        foreach (var plan in Enum.GetValues<PlanRule>().Where(value => value != selection.Plan)) neighbours.Add(selection with { Plan = plan });
        foreach (var earnings in SweepAxes.EarningsWindows.Where(value => value != selection.EarningsWindow)) neighbours.Add(selection with { EarningsWindow = earnings });

        return [.. neighbours.Where(SweepAxes.Expressible)];
    }

    // Step (c) for one selection design under one combination: its eight exits ranked over the coarse settings.
    public static IReadOnlyList<CombinationRow> Cross(IReadOnlyList<SweepCandidate> candidates, SweepDesign selection, ConditionSetting combination, int nights)
    {
        var picks = SweepStages.Picks(candidates, selection);

        return [.. SweepStages.Rank(picks, selection, combination, nights).Select(rank => new CombinationRow(rank.Design.Key, combination.Key, rank.Viable, rank.MedianEdge, rank.MedianMultiple))];
    }

    // The strongest distinct rows of step (c) on the edge, one a design and combination, ranked as stage 1 ranks.
    public static IReadOnlyList<CombinationRow> StrongestRows(IEnumerable<CombinationRow> rows, int count)
    {
        var ranked = rows
            .OrderBy(row => row.Viable >= SweepStages.ViableForARank ? 0 : 1)
            .ThenByDescending(row => row.Viable >= SweepStages.ViableForARank ? row.MedianEdge ?? double.MinValue : (double)row.Viable)
            .ThenByDescending(row => row.Viable)
            .ThenBy(row => SweepStages.ChoicesFromTheLiveRule(RankRow.Parse(row.DesignKey)))
            .ThenBy(row => row.DesignKey, StringComparer.Ordinal)
            .ThenBy(row => row.Combination, StringComparer.Ordinal);
        var carried = new List<CombinationRow>();

        foreach (var row in ranked)
        {
            if (carried.Count == count)
            {
                break;
            }

            if (!carried.Any(held => held.DesignKey == row.DesignKey || (held.Viable == row.Viable && held.MedianEdge == row.MedianEdge && held.MedianMultiple == row.MedianMultiple)))
            {
                carried.Add(row);
            }
        }

        return carried;
    }

    // Across the designs, edge first and then depth: the proposals within the plateau margin of the highest edge
    // are kept, and the deepest of them is the starting point, ties to the higher edge, then to the setting
    // nearest the live rule.
    public static int? AcrossDesigns(IReadOnlyList<(SweepProposal? Proposal, int[] Live)> proposals)
    {
        var held = proposals.Select((pair, at) => (pair.Proposal, pair.Live, At: at)).Where(pair => pair.Proposal is not null).ToArray();

        if (held.Length == 0)
        {
            return null;
        }

        var best = held.Max(pair => pair.Proposal!.Summary.Edge);

        return held
            .Where(pair => pair.Proposal!.Summary.Edge >= best - PlateauMargin)
            .OrderByDescending(pair => pair.Proposal!.Depth.Depth)
            .ThenByDescending(pair => pair.Proposal!.Summary.Edge)
            .ThenBy(pair => SweepSpace.Distance(pair.Proposal!.Point, pair.Live))
            .First().At;
    }
}

// Stage 2 for one design: the strong region found by sampling, the plateau's line fixed, depth measured around
// the leaders, the deepest proposed, refined by single-dial moves, and looked beyond a grid end.
public sealed class SweepDesignSearch
{
    readonly int nights;
    readonly int sessions;
    readonly int exit;
    readonly Func<int[], SweepStages.Tally, SweepSummary> compute;
    readonly Func<int[], SweepMeasures> measure;
    readonly Dictionary<string, SweepSummary> evaluated = new(StringComparer.Ordinal);

    // The sample's points laid flat, one byte a dial, since ten million settings as arrays of their own would
    // cost more in headers than in values, and their summaries beside them.
    readonly List<byte> samplePoints = [];
    readonly List<SweepSummary> sampleSummaries = [];
    readonly SweepStages.Tally tally;
    int evaluations;

    public SweepDesign Design { get; }

    public SweepSpace Space { get; }

    public float Line { get; private set; } = float.NaN;

    public float BestEdge { get; private set; } = float.NaN;

    public int Evaluations => evaluations;

    public SweepDesignSearch(SweepDesign design, IReadOnlyList<SweepPick> picks, int nights, SweepSpace space)
        : this(
            design,
            nights,
            SweepStages.SessionsOf(picks),
            space,
            (point, over) => SweepStages.Summary(picks, space.Setting(point), space.Conditions(point), nights, over),
            point => SweepStages.Measures(picks, design, space.Setting(point), space.Conditions(point), nights))
    {
    }

    // The same search over any reading of a point, which is how a test hands it a landscape worked by hand; the
    // sessions are what a tally's night bits reach, the history's own count.
    public SweepDesignSearch(SweepDesign design, int nights, int sessions, SweepSpace space, Func<int[], SweepStages.Tally, SweepSummary> compute, Func<int[], SweepMeasures> measure)
    {
        Design = design;
        this.nights = nights;
        this.sessions = sessions;
        Space = space;
        this.compute = compute;
        this.measure = measure;
        exit = design.ExitIndex;
        tally = SweepStages.Tally.OneExit(exit, sessions);
    }

    // One point's summary, computed once and kept.
    public SweepSummary Evaluate(ReadOnlySpan<int> point)
    {
        var key = SweepSpace.Key(point);

        if (evaluated.TryGetValue(key, out var held))
        {
            return held;
        }

        var summary = Compute(point.ToArray(), tally);

        evaluated[key] = summary;

        return summary;
    }

    // The line set by hand, for a proposal read at another margin.
    public void SetLine(float line) => Line = line;

    SweepSummary Compute(int[] point, SweepStages.Tally over)
    {
        Interlocked.Increment(ref evaluations);

        return compute(point, over);
    }

    public SweepMeasures Measures(ReadOnlySpan<int> point) => measure(point.ToArray());

    public bool OnThePlateau(in SweepSummary summary) => summary.MeetsTheFloors && summary.HasEdge && summary.Edge >= Line;

    // Every coarse setting, with the conditions as the combination step (c) carried.
    public void EvaluateCoarse(ConditionSetting combination, int parallelism)
    {
        var points = SweepStages.CoarseOnExtended.Select(setting => Space.Point(setting, combination)).ToArray();

        EvaluateAll(points, parallelism);
    }

    void EvaluateAll(IReadOnlyList<int[]> points, int parallelism)
    {
        var results = new SweepSummary[points.Count];

        Parallel.For(0, points.Count, new ParallelOptions { MaxDegreeOfParallelism = parallelism }, () => SweepStages.Tally.OneExit(exit, sessions), (at, _, local) =>
        {
            results[at] = Compute(points[at], local);

            return local;
        }, _ => { });

        for (var at = 0; at < points.Count; at++)
        {
            evaluated[SweepSpace.Key(points[at])] = results[at];
        }
    }

    // The sample: balanced so every value of every dial appears equally often, drawn with the seed, its size set
    // from the time the first ten thousand take against the budget, and never more than the grid.
    public (int Size, double GridSize, double PerPointSeconds) EvaluateSample(TimeSpan budget, int seed, int parallelism)
    {
        var gridSize = Space.Size();
        var random = new Random(seed);
        var timed = (int)Math.Min(SweepSearch.TimedPoints, gridSize);
        var watch = Stopwatch.StartNew();

        AddSample(Draw(timed, random), parallelism);

        var perPoint = watch.Elapsed.TotalSeconds / Math.Max(1, timed);
        var size = (int)Math.Min(Math.Min(gridSize, SweepSearch.MostSampled), Math.Floor(budget.TotalSeconds / Math.Max(perPoint, 1e-9)));

        if (size > timed)
        {
            AddSample(Draw(size - timed, random), parallelism);
        }

        return (sampleSummaries.Count, gridSize, perPoint);
    }

    int[] SamplePoint(int at)
    {
        var point = new int[Space.Count];

        for (var dial = 0; dial < Space.Count; dial++)
        {
            point[dial] = samplePoints[(at * Space.Count) + dial];
        }

        return point;
    }

    // Points drawn balanced: each dial's values laid out in turn to the count and shuffled on their own.
    public List<int[]> Draw(int count, Random random)
    {
        var points = new List<int[]>(count);

        for (var at = 0; at < count; at++)
        {
            points.Add(new int[Space.Count]);
        }

        for (var dial = 0; dial < Space.Count; dial++)
        {
            var low = Space.Low(dial);
            var size = Space.High(dial) - low + 1;
            var column = new int[count];

            for (var at = 0; at < count; at++)
            {
                column[at] = low + (at % size);
            }

            for (var at = count - 1; at > 0; at--)
            {
                var other = random.Next(at + 1);

                (column[at], column[other]) = (column[other], column[at]);
            }

            for (var at = 0; at < count; at++)
            {
                points[at][dial] = column[at];
            }
        }

        return points;
    }

    void AddSample(List<int[]> points, int parallelism)
    {
        var results = new SweepSummary[points.Count];

        Parallel.For(0, points.Count, new ParallelOptions { MaxDegreeOfParallelism = parallelism }, () => SweepStages.Tally.OneExit(exit, sessions), (at, _, local) =>
        {
            results[at] = Compute(points[at], local);

            return local;
        }, _ => { });

        for (var at = 0; at < points.Count; at++)
        {
            foreach (var value in points[at])
            {
                samplePoints.Add((byte)value);
            }

            sampleSummaries.Add(results[at]);
        }
    }

    // The plateau's line, fixed before any depth is measured: the best edge among the evaluated settings meeting
    // the floors, less the margin.
    public void FixTheLine(double margin)
    {
        var best = float.NaN;

        foreach (var summary in evaluated.Values.Concat(sampleSummaries))
        {
            if (summary.MeetsTheFloors && summary.HasEdge && (float.IsNaN(best) || summary.Edge > best))
            {
                best = summary.Edge;
            }
        }

        BestEdge = best;
        Line = float.IsNaN(best) ? float.NaN : best - (float)margin;
    }

    // The leaders: the highest-edge evaluated settings meeting the floors, ties to the lower key, held as a
    // bounded heap over the sample rather than a list of every setting meeting the floors, which over ten
    // million sampled settings would be most of them.
    public IReadOnlyList<int[]> LeadersOf(int count)
    {
        var heap = new PriorityQueue<string, (float Edge, string Key)>(Comparer<(float Edge, string Key)>.Create((one, other) =>
            one.Edge != other.Edge ? one.Edge.CompareTo(other.Edge) : string.CompareOrdinal(other.Key, one.Key)));
        var held = new HashSet<string>(StringComparer.Ordinal);

        void Offer(string key, in SweepSummary summary)
        {
            if (!summary.MeetsTheFloors || !summary.HasEdge || !held.Add(key))
            {
                return;
            }

            heap.Enqueue(key, (summary.Edge, key));

            if (heap.Count > count)
            {
                held.Remove(heap.Dequeue());
            }
        }

        foreach (var (key, summary) in evaluated)
        {
            Offer(key, summary);
        }

        for (var at = 0; at < sampleSummaries.Count; at++)
        {
            var summary = sampleSummaries[at];

            if (summary.MeetsTheFloors && summary.HasEdge)
            {
                Offer(SweepSpace.Key(SamplePoint(at)), summary);
            }
        }

        var leaders = new List<(string Key, float Edge)>();

        while (heap.TryDequeue(out var key, out var priority))
        {
            leaders.Add((key, priority.Edge));
        }

        return [.. leaders.OrderByDescending(pair => pair.Edge).ThenBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => SweepSpace.Parse(pair.Key))];
    }

    // The settings read with the highest edges among those holding the trade floor, whatever other floors they meet,
    // and after them the rest by the most trades, a tie settled by the setting, which a search proposing nothing
    // brings the operator: across a grid of millions the highest edges otherwise fall to settings of a few trades.
    public IReadOnlyList<int[]> Strongest(int count)
    {
        var read = new Dictionary<string, (float Edge, int Scored)>(StringComparer.Ordinal);

        foreach (var (key, summary) in evaluated)
        {
            if (summary.HasEdge)
            {
                read[key] = (summary.Edge, summary.Scored);
            }
        }

        for (var at = 0; at < sampleSummaries.Count; at++)
        {
            if (sampleSummaries[at].HasEdge)
            {
                read[SweepSpace.Key(SamplePoint(at))] = (sampleSummaries[at].Edge, sampleSummaries[at].Scored);
            }
        }

        return
        [
            .. read
                .OrderBy(pair => pair.Value.Scored >= SweepMeasures.TradeFloor ? 0 : 1)
                .ThenByDescending(pair => pair.Value.Scored >= SweepMeasures.TradeFloor ? pair.Value.Edge : pair.Value.Scored)
                .ThenBy(pair => pair.Key, StringComparer.Ordinal)
                .Take(count)
                .Select(pair => SweepSpace.Parse(pair.Key)),
        ];
    }

    // Depth along one dial is the single steps that dial can move in one direction, the others held, before the
    // setting leaves the plateau; a setting's depth is the smallest over every dial and both directions. Beyond
    // off and at either end of a two-value dial the grid does not limit depth, and a move that changes no
    // reading is not a step.
    public SweepDepth Depth(int[] point)
    {
        var best = new SweepDepth(SweepSearch.MostDepth, -1, 0, false);

        for (var dial = 0; dial < Space.Count; dial++)
        {
            if (Space.IsNoOp(point, dial))
            {
                continue;
            }

            foreach (var direction in new[] { -1, 1 })
            {
                var steps = 0;
                var atAGridEnd = false;

                for (var step = 1; step <= SweepSearch.MostDepth; step++)
                {
                    var next = Space.Step(point, dial, direction * step);

                    if (next is null)
                    {
                        atAGridEnd = Space.LimitsAt(dial, direction);

                        if (!atAGridEnd)
                        {
                            steps = SweepSearch.MostDepth;
                        }

                        break;
                    }

                    if (!OnThePlateau(Evaluate(next)))
                    {
                        break;
                    }

                    steps++;
                }

                if (steps < best.Depth || (steps == best.Depth && atAGridEnd && !best.AtAGridEnd))
                {
                    best = new SweepDepth(steps, dial, direction, atAGridEnd && steps < SweepSearch.MostDepth);
                }
            }
        }

        return best;
    }

    // Whether a point stands as a proposal: on the plateau and not trailing the live rule's edge in any recent
    // year.
    public bool Stands(int[] point, SweepMeasures live, out SweepMeasures measures)
    {
        measures = Measures(point);

        return OnThePlateau(Evaluate(point)) && !measures.TrailsInARecentYear(live);
    }

    // The proposal among the leaders: the deepest that stands, ties to the higher edge, then to the setting
    // nearest the live rule; none where none stands.
    public (SweepProposal? Proposal, int Trailing) Propose(IReadOnlyList<int[]> leaders, SweepMeasures live)
    {
        var livePoint = Space.LivePoint();
        var standing = new List<(int[] Point, SweepDepth Depth, SweepSummary Summary, SweepMeasures Measures)>();
        var trailing = 0;

        foreach (var leader in leaders)
        {
            if (!Stands(leader, live, out var measures))
            {
                trailing++;

                continue;
            }

            standing.Add((leader, Depth(leader), Evaluate(leader), measures));
        }

        var chosen = standing
            .OrderByDescending(one => one.Depth.Depth)
            .ThenByDescending(one => one.Summary.Edge)
            .ThenBy(one => SweepSpace.Distance(one.Point, livePoint))
            .Select(one => (SweepProposal?)new SweepProposal(one.Point, one.Summary, one.Depth, one.Measures))
            .FirstOrDefault();

        return (chosen, trailing);
    }

    // Refined once and again: every one-step and two-step single-dial move from the proposal is measured for
    // depth, and a deeper move that stands is taken, the deepest first with ties to the higher edge, until no
    // move is deeper. Each round is stated.
    public SweepProposal Refine(SweepProposal start, SweepMeasures live, List<string> rounds)
    {
        var livePoint = Space.LivePoint();
        var current = start;

        for (var round = 1; ; round++)
        {
            (SweepProposal Proposal, string Change)? deeper = null;

            for (var dial = 0; dial < Space.Count; dial++)
            {
                if (Space.IsNoOp(current.Point, dial))
                {
                    continue;
                }

                foreach (var steps in new[] { -2, -1, 1, 2 })
                {
                    var next = Space.Step(current.Point, dial, steps);

                    if (next is null || !Stands(next, live, out var measures))
                    {
                        continue;
                    }

                    var depth = Depth(next);

                    if (depth.Depth <= current.Depth.Depth)
                    {
                        continue;
                    }

                    var summary = Evaluate(next);

                    if (deeper is null
                        || depth.Depth > deeper.Value.Proposal.Depth.Depth
                        || (depth.Depth == deeper.Value.Proposal.Depth.Depth && summary.Edge > deeper.Value.Proposal.Summary.Edge)
                        || (depth.Depth == deeper.Value.Proposal.Depth.Depth && summary.Edge == deeper.Value.Proposal.Summary.Edge && SweepSpace.Distance(next, livePoint) < SweepSpace.Distance(deeper.Value.Proposal.Point, livePoint)))
                    {
                        deeper = (new SweepProposal(next, summary, depth, measures), Space.Moved(current.Point, next)?.Change ?? "a move");
                    }
                }
            }

            if (deeper is not { } taken)
            {
                rounds.Add(FormattableString.Invariant($"round {round}: no one-step or two-step move is deeper than {current.Depth.Depth}, so the proposal stands at {Space.Describe(current.Point)}"));

                return current;
            }

            rounds.Add(FormattableString.Invariant($"round {round}: {taken.Change} deepens the plateau from {current.Depth.Depth} to {taken.Proposal.Depth.Depth} at an edge of {taken.Proposal.Summary.Edge:0.000}"));
            current = taken.Proposal;
        }
    }

    // Where the refined proposal's depth runs into a grid end on a dial of three or more values, that dial is
    // extended by up to two values beyond the end, depth is measured again and the refinement runs again with
    // the extension open to it. A dial that cannot be extended, or whose extension still leaves the depth at
    // the new end, is named as a limit of the search.
    public SweepProposal Extend(SweepProposal start, SweepMeasures live, List<string> rounds, List<string> extensions, List<string> limits)
    {
        var current = start;
        var extended = new HashSet<(int Dial, int Direction)>();

        while (current.Depth.AtAGridEnd && current.Depth.Dial >= 0)
        {
            var dial = current.Depth.Dial;
            var direction = current.Depth.Direction;
            var name = Space.Dials[dial].Name;
            var end = direction < 0 ? "low" : "high";

            if (extended.Contains((dial, direction)) || !Space.CanExtend(dial, direction))
            {
                limits.Add(FormattableString.Invariant($"the {name} dial's {end} end bounds the depth at {current.Depth.Depth} and cannot be looked beyond"));

                return current;
            }

            var opened = Space.Extend(dial, direction);

            extended.Add((dial, direction));
            extensions.Add(FormattableString.Invariant($"the {name} dial looked beyond its {end} end at {string.Join(" and ", opened.Select(at => Space.Label(dial, at)))}"));

            var depth = Depth(current.Point);

            current = new SweepProposal(current.Point, current.Summary, depth, current.Measures);
            current = Refine(current, live, rounds);

            if (current.Depth.AtAGridEnd && current.Depth.Dial == dial && current.Depth.Direction == direction)
            {
                limits.Add(FormattableString.Invariant($"the {name} dial's {end} end still bounds the depth at {current.Depth.Depth} after looking beyond it"));

                return current;
            }
        }

        // A proposal sitting at a dial's own end, beyond which the rule allows no value, is named as a limit of
        // the search: the plateau may reach further there and nothing can be read past it.
        for (var dial = 0; dial < Space.Count; dial++)
        {
            foreach (var direction in new[] { -1, 1 })
            {
                if (Space.AtTheRulesEnd(current.Point, dial, direction))
                {
                    limits.Add(FormattableString.Invariant($"the proposal sits at the {Space.Dials[dial].Name} dial's own {(direction < 0 ? "low" : "high")} end, {Space.Label(dial, current.Point[dial])}, beyond which the rule allows no value"));
                }
            }
        }

        return current;
    }

    // Every pair of dials sliced through the starting point, the others held there, each cell's edge and
    // whether it is on the plateau, reaching three steps either side.
    public IReadOnlyList<SweepSlice> Slices(int[] centre)
    {
        var slices = new List<SweepSlice>();

        for (var row = 0; row < Space.Count; row++)
        {
            for (var column = row + 1; column < Space.Count; column++)
            {
                var rowFrom = Math.Max(Space.Low(row), centre[row] - SweepSearch.SliceReach);
                var rowTo = Math.Min(Space.High(row), centre[row] + SweepSearch.SliceReach);
                var columnFrom = Math.Max(Space.Low(column), centre[column] - SweepSearch.SliceReach);
                var columnTo = Math.Min(Space.High(column), centre[column] + SweepSearch.SliceReach);
                var edge = new float[rowTo - rowFrom + 1][];
                var onIt = new bool[rowTo - rowFrom + 1][];
                var scored = new int[rowTo - rowFrom + 1][];

                for (var r = rowFrom; r <= rowTo; r++)
                {
                    edge[r - rowFrom] = new float[columnTo - columnFrom + 1];
                    onIt[r - rowFrom] = new bool[columnTo - columnFrom + 1];
                    scored[r - rowFrom] = new int[columnTo - columnFrom + 1];

                    for (var c = columnFrom; c <= columnTo; c++)
                    {
                        var point = (int[])centre.Clone();

                        point[row] = r;
                        point[column] = c;

                        var summary = Evaluate(point);

                        edge[r - rowFrom][c - columnFrom] = summary.Edge;
                        onIt[r - rowFrom][c - columnFrom] = OnThePlateau(summary);
                        scored[r - rowFrom][c - columnFrom] = summary.Scored;
                    }
                }

                slices.Add(new SweepSlice(
                    Space.Dials[row].Name,
                    Space.Dials[column].Name,
                    [.. Enumerable.Range(rowFrom, rowTo - rowFrom + 1).Select(at => Space.Label(row, at))],
                    [.. Enumerable.Range(columnFrom, columnTo - columnFrom + 1).Select(at => Space.Label(column, at))],
                    edge,
                    onIt,
                    scored,
                    centre[row] - rowFrom,
                    centre[column] - columnFrom));
            }
        }

        return slices;
    }
}
