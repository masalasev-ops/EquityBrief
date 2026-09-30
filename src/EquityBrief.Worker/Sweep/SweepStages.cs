using System.Globalization;
using EquityBrief.Core.Sweep;

namespace EquityBrief.Worker.Sweep;

// One candidate as one design reads it: where it stands against the grid's dials, the plan whose outcomes it
// carries, and the readings the conditions take under the design's reference high and trigger.
public readonly record struct SweepPick(int Index, int Name, int Session, SweepCorner Corner, SweepPlanOutcomes Plan, int Year, int Block, ConditionSetting.Readings Readings);

// What stage 1 read of one design: its coarse settings, how many of them were viable, their share, the median
// edge and the median result of the viable ones, and the live setting's figures where the design is the live
// rule's own.
public sealed record SweepDesignRank(
    SweepDesign Design,
    int Settings,
    int Viable,
    double ViableShare,
    double? MedianEdge,
    double? MedianMultiple,
    SweepMeasures? Live);

// Stage 1's row for one design as the run saves it.
public sealed record RankRow(
    string Key,
    int Settings,
    int Viable,
    double ViableShare,
    double? MedianEdge,
    double? MedianMultiple,
    int? LiveScored,
    double? LiveShare,
    double? LiveBreakEven,
    double? LiveNoSkill,
    double? LiveMultiple,
    double? LiveEdge,
    int? LiveYearsBeatingBoth)
{
    // Read back from the key, which is what the file holds.
    [System.Text.Json.Serialization.JsonIgnore]
    public SweepDesign Design => Parse(Key);

    public static RankRow Of(SweepDesignRank rank) =>
        new(
            rank.Design.Key,
            rank.Settings,
            rank.Viable,
            rank.ViableShare,
            rank.MedianEdge,
            rank.MedianMultiple,
            rank.Live?.Scored,
            rank.Live?.Share,
            rank.Live?.BreakEven,
            rank.Live?.NoSkill,
            rank.Live?.AverageMultiple,
            rank.Live?.Edge,
            rank.Live?.YearsBeatingBoth);

    public static SweepDesign Parse(string key)
    {
        var parts = key.Split('|');

        return new SweepDesign(
            Enum.Parse<StrengthMeasure>(parts[0]),
            Enum.Parse<UptrendRule>(parts[1]),
            Enum.Parse<SupportKind>(parts[2]),
            int.Parse(parts[3], CultureInfo.InvariantCulture),
            Enum.Parse<TriggerKind>(parts[4]),
            Enum.Parse<PlanRule>(parts[5]),
            int.Parse(parts[6], CultureInfo.InvariantCulture),
            parts[7] == "breakeven",
            int.Parse(parts[8], CultureInfo.InvariantCulture));
    }
}

// The stages' arithmetic over the candidates. Every setting is read by one walk over its design's candidates in
// name and session order, keeping a listing unless the stock's kept trade is still open on its session, one
// open-until marker a name for each exit; a table that sums candidates by cell cannot apply that rule, since a
// looser setting can add an earlier listing that blocks a later one.
// see: The sweep ranks on the edge over the same plan entered on every member, with one open trade a stock and seven conditions tested in steps
public static class SweepStages
{
    // Stage 1 needs this many viable settings before a design's median edge ranks it; below it the design ranks
    // among those with too few by its share alone, stated in the report.
    public const int ViableForARank = 100;

    // The coarse settings carried onto the extended grid, whose dials every candidate's corner is read against.
    public static IReadOnlyList<DialSetting> CoarseOnExtended { get; } =
    [
        .. Enumerable.Range(0, SweepGrid.Coarse.Variations)
            .Select(index => DialSetting.Of(SweepGrid.Coarse, index / SweepGrid.Coarse.CellsPerStop, index % SweepGrid.Coarse.CellsPerStop))
            .Select(setting => SweepGrid.Extended.Carry(SweepGrid.Coarse, setting)),
    ];

    // The candidates one design reads, in name and then session order, each with its corner on the grid, its
    // plan and its condition readings.
    public static List<SweepPick> Picks(IReadOnlyList<SweepCandidate> candidates, SweepDesign design, SweepGrid? grid = null)
    {
        var over = grid ?? SweepGrid.Extended;
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
                over,
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
                picks.Add(new SweepPick(at, candidate.Name, candidate.Session, placed, plan, candidate.Year, candidate.Block, candidate.Readings(high, candidate.Age[age])));
            }
        }

        picks.Sort((one, other) => one.Name != other.Name ? one.Name.CompareTo(other.Name) : one.Session.CompareTo(other.Session));

        return picks;
    }

    // The buffers one walk fills for the exits it reads: a figure vector an exit, the nights listed an exit as
    // bits, and the session each name's kept trade blocks it through.
    public sealed class Tally
    {
        public readonly int[] Exits;
        public readonly float[] Sums;
        public readonly ulong[] Nights;
        public readonly int[] OpenUntil;
        public readonly int Words;

        public Tally(int[] exits, int nights)
        {
            Exits = exits;
            Words = (nights + 63) / 64;
            Sums = new float[exits.Length * SweepFigures.Width];
            Nights = new ulong[exits.Length * Words];
            OpenUntil = new int[exits.Length];
        }

        public static Tally AllExits(int nights) => new([.. Enumerable.Range(0, SweepAxes.Exits)], nights);

        public static Tally OneExit(int exit, int nights) => new([exit], nights);

        public void Clear()
        {
            Array.Clear(Sums);
            Array.Clear(Nights);
        }

        public ReadOnlySpan<float> SumsOf(int at) => Sums.AsSpan(at * SweepFigures.Width, SweepFigures.Width);

        public int NightsOf(int at)
        {
            var count = 0;

            for (var word = 0; word < Words; word++)
            {
                count += System.Numerics.BitOperations.PopCount(Nights[(at * Words) + word]);
            }

            return count;
        }
    }

    // One setting walked over a design's picks: every pick the setting and the conditions pass is kept unless
    // the stock's kept trade under that exit is still open on its session, in which case it is counted as
    // blocked; a kept pick's figures, its night and the session its trade blocks the stock through are recorded
    // under each exit the tally reads.
    public static void Walk(IReadOnlyList<SweepPick> picks, DialSetting setting, in ConditionSetting conditions, Tally tally)
    {
        tally.Clear();

        var exits = tally.Exits;
        var width = SweepFigures.Width;
        var words = tally.Words;
        var name = -1;

        for (var at = 0; at < picks.Count; at++)
        {
            var pick = picks[at];

            if (pick.Name != name)
            {
                name = pick.Name;
                Array.Fill(tally.OpenUntil, -1);
            }

            if (!pick.Corner.Passes(setting) || !conditions.Passes(pick.Readings))
            {
                continue;
            }

            var plan = pick.Plan;

            for (var index = 0; index < exits.Length; index++)
            {
                var exit = exits[index];
                var sums = tally.Sums.AsSpan(index * width, width);

                if (pick.Session <= tally.OpenUntil[index])
                {
                    SweepFigures.TallyBlocked(sums, pick.Year);

                    continue;
                }

                SweepFigures.Tally(sums, pick.Year, pick.Block, plan.Code[exit], plan.Null[exit], plan.BreakEven[exit], plan.Multiple[exit], plan.EdgeAt(exit));
                tally.Nights[(index * words) + (pick.Session >> 6)] |= 1UL << (pick.Session & 63);
                tally.OpenUntil[index] = pick.Session + plan.Ends[exit];
            }
        }
    }

    // One setting's summary under one exit, the buffers handed in reused.
    public static SweepSummary Summary(IReadOnlyList<SweepPick> picks, DialSetting setting, in ConditionSetting conditions, int nights, Tally tally)
    {
        Walk(picks, setting, conditions, tally);

        return SweepMeasures.Summary(tally.SumsOf(0), tally.NightsOf(0), nights);
    }

    // One setting's full record under one exit, and the name-sessions it kept where a set is handed in.
    public static SweepMeasures Measures(IReadOnlyList<SweepPick> picks, SweepDesign design, DialSetting setting, in ConditionSetting conditions, int nights, HashSet<(int Name, int Session)>? kept = null)
    {
        var tally = Tally.OneExit(design.ExitIndex, nights);

        Walk(picks, setting, conditions, tally);

        if (kept is not null)
        {
            var exit = design.ExitIndex;
            var name = -1;
            var openUntil = -1;

            foreach (var pick in picks)
            {
                if (pick.Name != name)
                {
                    name = pick.Name;
                    openUntil = -1;
                }

                if (!pick.Corner.Passes(setting) || !conditions.Passes(pick.Readings) || pick.Session <= openUntil)
                {
                    continue;
                }

                kept.Add((pick.Name, pick.Session));
                openUntil = pick.Session + pick.Plan.Ends[exit];
            }
        }

        return SweepMeasures.Of(tally.SumsOf(0), tally.NightsOf(0), nights);
    }

    // One setting of one design read directly over its candidates: the figures the report states for a starting
    // point, its variants and the live rule.
    public static SweepMeasures Direct(IReadOnlyList<SweepCandidate> candidates, SweepDesign design, DialSetting setting, in ConditionSetting conditions, int nights, HashSet<(int Name, int Session)>? kept = null) =>
        Measures(Picks(candidates, design), design, setting, conditions, nights, kept);

    // Stage 1 for one selection design: every exit of it over every coarse setting, with the conditions off.
    public static IReadOnlyList<SweepDesignRank> Rank(IReadOnlyList<SweepCandidate> candidates, SweepDesign selection, int nights) =>
        Rank(Picks(candidates, selection), selection, ConditionSetting.Off, nights);

    public static IReadOnlyList<SweepDesignRank> Rank(IReadOnlyList<SweepPick> picks, SweepDesign selection, ConditionSetting conditions, int nights)
    {
        var tally = Tally.AllExits(nights);
        var viable = new int[SweepAxes.Exits];
        var edges = new List<double>[SweepAxes.Exits];
        var multiples = new List<double>[SweepAxes.Exits];
        var live = new SweepMeasures?[SweepAxes.Exits];
        var liveCoarse = SweepGrid.Extended.Carry(SweepGrid.Coarse, DialSetting.LiveOnCoarse);

        for (var exit = 0; exit < SweepAxes.Exits; exit++)
        {
            edges[exit] = [];
            multiples[exit] = [];
        }

        foreach (var setting in CoarseOnExtended)
        {
            Walk(picks, setting, conditions, tally);

            for (var exit = 0; exit < SweepAxes.Exits; exit++)
            {
                var summary = SweepMeasures.Summary(tally.SumsOf(exit), tally.NightsOf(exit), nights);

                if (summary.Viable)
                {
                    viable[exit]++;

                    if (summary.HasEdge)
                    {
                        edges[exit].Add(summary.Edge);
                    }

                    if (!float.IsNaN(summary.AverageMultiple))
                    {
                        multiples[exit].Add(summary.AverageMultiple);
                    }
                }

                var (hold, breakEven) = SweepAxes.ExitOf(exit);

                if ((selection with { Hold = hold, BreakEven = breakEven }) == SweepDesign.Live && setting == liveCoarse && conditions.IsOff)
                {
                    live[exit] = SweepMeasures.Of(tally.SumsOf(exit), tally.NightsOf(exit), nights);
                }
            }
        }

        var ranks = new List<SweepDesignRank>();

        for (var exit = 0; exit < SweepAxes.Exits; exit++)
        {
            var (hold, breakEven) = SweepAxes.ExitOf(exit);

            ranks.Add(new SweepDesignRank(
                selection with { Hold = hold, BreakEven = breakEven },
                SweepGrid.Coarse.Variations,
                viable[exit],
                (double)viable[exit] / SweepGrid.Coarse.Variations,
                edges[exit].Count == 0 ? null : Median(edges[exit]),
                multiples[exit].Count == 0 ? null : Median(multiples[exit]),
                live[exit]));
        }

        return ranks;
    }

    // Stage 1's ranking: a design with at least 100 viable coarse settings ranks by the median edge of the
    // viable ones, the share viable breaking a tie, then the design nearer the live rule's in structural
    // choices, and its key; a design with fewer ranks below every one of those, by its share.
    public static IReadOnlyList<RankRow> Ranked(IEnumerable<RankRow> rows) =>
        [
            .. rows
                .OrderBy(row => row.Viable >= ViableForARank ? 0 : 1)
                .ThenByDescending(row => row.Viable >= ViableForARank ? row.MedianEdge ?? double.MinValue : row.ViableShare)
                .ThenByDescending(row => row.ViableShare)
                .ThenBy(row => ChoicesFromTheLiveRule(row.Design))
                .ThenBy(row => row.Key, StringComparer.Ordinal),
        ];

    // Two designs whose stage 1 figures are the same to the last digit read the history the same way, the trend
    // rule's versions reading an uptrend alike being the common case, so the strongest distinct designs are
    // taken, each tie read through the design nearest the live rule's.
    public static RankRow[] Strongest(IEnumerable<RankRow> rows, int count)
    {
        var carried = new List<RankRow>();

        foreach (var row in Ranked(rows))
        {
            if (carried.Count == count)
            {
                break;
            }

            if (!carried.Any(held => SameFigures(held, row)))
            {
                carried.Add(row);
            }
        }

        return [.. carried];
    }

    public static bool SameFigures(RankRow one, RankRow other) =>
        one.Viable == other.Viable && one.MedianEdge == other.MedianEdge && one.MedianMultiple == other.MedianMultiple;

    public static int ChoicesFromTheLiveRule(SweepDesign design)
    {
        var live = SweepDesign.Live;

        return (design.Strength != live.Strength ? 1 : 0)
            + (design.Uptrend != live.Uptrend ? 1 : 0)
            + (design.Support != live.Support ? 1 : 0)
            + (design.ReferenceHigh != live.ReferenceHigh ? 1 : 0)
            + (design.Trigger != live.Trigger ? 1 : 0)
            + (design.Plan != live.Plan ? 1 : 0)
            + (design.Hold != live.Hold || design.BreakEven != live.BreakEven ? 1 : 0)
            + (design.EarningsWindow != live.EarningsWindow ? 1 : 0);
    }

    public static double Median(List<double> values)
    {
        values.Sort();

        return values.Count % 2 == 1 ? values[values.Count / 2] : (values[(values.Count / 2) - 1] + values[values.Count / 2]) / 2;
    }
}
