using EquityBrief.Core.Returns;

namespace EquityBrief.Core.Sweep;

// The figures a candidate carries into a table under one design's plan and exit, laid out by scored year and by
// block: whether it was listed, entered, scored, won, the calibrated bar and the planned break-even it set, and
// what it came to in multiples of its planned risk.
public static class SweepFigures
{
    public const int Years = 8;

    public const int Listed = 0;
    public const int Entered = 1;
    public const int Scored = 2;
    public const int Wins = 3;
    public const int NullSum = 4;
    public const int BreakEvenScored = 5;
    public const int BreakEvenSum = 6;
    public const int MultipleSum = 7;
    public const int MultipleCount = 8;

    public const int PerYear = 9;

    // The blocks of 63 sessions from the first scored session, the last of them partial, and the whole ones a
    // record is judged over.
    public const int Blocks = 31;

    public const int WholeBlocks = 30;

    public static int Width => (Years * PerYear) + Blocks;

    // A candidate's figures: listed whatever came of it; entered where it won, lost or ran out of sessions; scored
    // where it won or lost and its paths set a bar, which is every setup the live record reads.
    public static void Fill(Span<float> figures, int year, int block, byte code, float calibrated, float breakEven, float multiple)
    {
        figures.Clear();

        if (year < 0 || year >= Years)
        {
            return;
        }

        var at = year * PerYear;
        var entered = code is 1 or 2 or 3;
        var scored = code is 1 or 2 && !float.IsNaN(calibrated);

        figures[at + Listed] = 1;
        figures[at + Entered] = entered ? 1 : 0;

        if (scored)
        {
            figures[at + Scored] = 1;
            figures[at + Wins] = code == 1 ? 1 : 0;
            figures[at + NullSum] = calibrated;

            if (!float.IsNaN(breakEven))
            {
                figures[at + BreakEvenScored] = 1;
                figures[at + BreakEvenSum] = breakEven;
            }

            if (block is >= 0 and < Blocks)
            {
                figures[(Years * PerYear) + block] = 1;
            }
        }

        if (entered && !float.IsNaN(multiple))
        {
            figures[at + MultipleSum] = multiple;
            figures[at + MultipleCount] = 1;
        }
    }
}

// What one variation's trades came to over the history, read off a table's cell: counts, the share won against
// the break-even the plans needed and against the calibrated bar, the average result in multiples of risk, and
// the same year by year, with the blocks that held a scored trade and the nights that listed a name.
public sealed record SweepMeasures(
    int Listed,
    int Entered,
    int Scored,
    int Wins,
    double? Share,
    double? BreakEven,
    double? NoSkill,
    double? AverageMultiple,
    int[] YearScored,
    double?[] YearShare,
    double?[] YearBreakEven,
    double?[] YearNoSkill,
    double?[] YearAverageMultiple,
    int BlocksWithTrades,
    int NightsListing,
    int Nights)
{
    // Section 17's floors the starting point is read against, proposed and the operator's to rule.
    public const int TradeFloor = 300;
    public const int YearsBeating = 6;
    public const int BlockFloor = 22;
    public const double ListingShare = 0.6;

    public static SweepMeasures Of(ReadOnlySpan<float> figures, int nightsListing, int nights)
    {
        var yearScored = new int[SweepFigures.Years];
        var yearShare = new double?[SweepFigures.Years];
        var yearBreakEven = new double?[SweepFigures.Years];
        var yearNoSkill = new double?[SweepFigures.Years];
        var yearMultiple = new double?[SweepFigures.Years];
        double listed = 0, entered = 0, scored = 0, wins = 0, nulls = 0, breakEvenScored = 0, breakEvenSum = 0, multipleSum = 0, multipleCount = 0;

        for (var year = 0; year < SweepFigures.Years; year++)
        {
            var at = year * SweepFigures.PerYear;
            var yearScoredCount = figures[at + SweepFigures.Scored];

            listed += figures[at + SweepFigures.Listed];
            entered += figures[at + SweepFigures.Entered];
            scored += yearScoredCount;
            wins += figures[at + SweepFigures.Wins];
            nulls += figures[at + SweepFigures.NullSum];
            breakEvenScored += figures[at + SweepFigures.BreakEvenScored];
            breakEvenSum += figures[at + SweepFigures.BreakEvenSum];
            multipleSum += figures[at + SweepFigures.MultipleSum];
            multipleCount += figures[at + SweepFigures.MultipleCount];

            yearScored[year] = (int)Math.Round(yearScoredCount);
            yearShare[year] = yearScoredCount > 0 ? figures[at + SweepFigures.Wins] / yearScoredCount * 100 : null;
            yearNoSkill[year] = yearScoredCount > 0 ? figures[at + SweepFigures.NullSum] / yearScoredCount * 100 : null;
            yearBreakEven[year] = figures[at + SweepFigures.BreakEvenScored] > 0 ? figures[at + SweepFigures.BreakEvenSum] / figures[at + SweepFigures.BreakEvenScored] : null;
            yearMultiple[year] = figures[at + SweepFigures.MultipleCount] > 0 ? figures[at + SweepFigures.MultipleSum] / figures[at + SweepFigures.MultipleCount] : null;
        }

        var blocks = 0;

        for (var block = 0; block < SweepFigures.WholeBlocks; block++)
        {
            blocks += figures[(SweepFigures.Years * SweepFigures.PerYear) + block] > 0 ? 1 : 0;
        }

        return new SweepMeasures(
            (int)Math.Round(listed),
            (int)Math.Round(entered),
            (int)Math.Round(scored),
            (int)Math.Round(wins),
            scored > 0 ? wins / scored * 100 : null,
            breakEvenScored > 0 ? breakEvenSum / breakEvenScored : null,
            scored > 0 ? nulls / scored * 100 : null,
            multipleCount > 0 ? multipleSum / multipleCount : null,
            yearScored,
            yearShare,
            yearBreakEven,
            yearNoSkill,
            yearMultiple,
            blocks,
            nightsListing,
            nights);
    }

    // Won more often than the break-even its plans needed and than a plan with no edge would have, over the whole
    // history.
    public bool BeatsBoth => Share is { } share && BreakEven is { } needed && NoSkill is { } chance && share > needed && share > chance;

    public bool BeatsBothIn(int year) =>
        YearShare[year] is { } share && YearBreakEven[year] is { } needed && YearNoSkill[year] is { } chance && share > needed && share > chance;

    public int YearsBeatingBoth => Enumerable.Range(0, SweepFigures.Years).Count(BeatsBothIn);

    public int YearsBeatingBreakEven => Enumerable.Range(0, SweepFigures.Years).Count(year => YearShare[year] is { } share && YearBreakEven[year] is { } needed && share > needed);

    // Stage 1's test of one coarse setting: at least 300 scored trades, and beating both its break-even and no
    // skill in at least 6 of the 8 years.
    public bool Viable => Scored >= TradeFloor && YearsBeatingBoth >= YearsBeating;

    // Whether the share still clears the break-even over the years left once the year it cleared by most is
    // removed, which is what "not carried by one year" is read as.
    public bool BeatsBreakEvenWithoutItsBestYear
    {
        get
        {
            var best = Enumerable.Range(0, SweepFigures.Years)
                .Where(year => YearShare[year] is not null && YearBreakEven[year] is not null)
                .OrderByDescending(year => (YearShare[year]!.Value - YearBreakEven[year]!.Value) * YearScored[year])
                .Select(year => (int?)year)
                .FirstOrDefault();

            if (best is not { } removed)
            {
                return false;
            }

            double scored = 0, won = 0, needed = 0;

            for (var year = 0; year < SweepFigures.Years; year++)
            {
                if (year == removed || YearShare[year] is not { } share || YearBreakEven[year] is not { } breakEven)
                {
                    continue;
                }

                scored += YearScored[year];
                won += share / 100 * YearScored[year];
                needed += breakEven / 100 * YearScored[year];
            }

            return scored > 0 && won > needed;
        }
    }

    public double ListingShareOfNights => Nights > 0 ? (double)NightsListing / Nights : 0;

    // The four floors a starting point is held to, each proposed and the operator's to rule.
    public bool MeetsTheFloors =>
        YearsBeatingBreakEven >= YearsBeating
        && BeatsBreakEvenWithoutItsBestYear
        && Scored >= TradeFloor
        && BlocksWithTrades >= BlockFloor
        && ListingShareOfNights >= ListingShare;
}

// The nights a variation lists a name on, counted without counting a night twice: each night's candidates are
// laid in their corner cells as one bit of a 64-night word, the words are carried along each dial as the table's
// sums are, and each cell counts the nights whose bit reached it.
public sealed class SweepNights
{
    static readonly bool[] UpToALast = [true, true, false, false, false, true, true, true];

    readonly SweepGrid grid;
    readonly int stop;
    readonly ulong[] bits;
    readonly int[] counts;
    int nightsInWord;
    int lastNight = -1;

    public SweepNights(SweepGrid grid, int stop)
    {
        this.grid = grid;
        this.stop = stop;
        bits = new ulong[grid.CellsPerStop];
        counts = new int[grid.CellsPerStop];
    }

    // Candidates are added night by night in order.
    public void Add(int night, SweepCorner corner)
    {
        if (night != lastNight)
        {
            if (nightsInWord == 64)
            {
                Flush();
            }

            lastNight = night;
            nightsInWord++;
        }

        if ((corner.StopMask & (1 << stop)) != 0)
        {
            bits[corner.Cell(grid)] |= 1UL << (nightsInWord - 1);
        }
    }

    public int[] Counts()
    {
        Flush();

        return counts;
    }

    void Flush()
    {
        if (nightsInWord == 0)
        {
            return;
        }

        var sizes = grid.Sizes;
        var strides = new int[sizes.Count];

        strides[^1] = 1;

        for (var dial = sizes.Count - 2; dial >= 0; dial--)
        {
            strides[dial] = strides[dial + 1] * sizes[dial + 1];
        }

        for (var dial = 0; dial < sizes.Count; dial++)
        {
            var stride = strides[dial];
            var size = sizes[dial];

            for (var cell = 0; cell < bits.Length; cell++)
            {
                var level = cell / stride % size;

                if (UpToALast[dial] ? level != size - 1 : level != 0)
                {
                    continue;
                }

                if (UpToALast[dial])
                {
                    for (var at = size - 2; at >= 0; at--)
                    {
                        bits[cell - ((size - 1 - at) * stride)] |= bits[cell - ((size - 2 - at) * stride)];
                    }
                }
                else
                {
                    for (var at = 1; at < size; at++)
                    {
                        bits[cell + (at * stride)] |= bits[cell + ((at - 1) * stride)];
                    }
                }
            }
        }

        for (var cell = 0; cell < bits.Length; cell++)
        {
            counts[cell] += System.Numerics.BitOperations.PopCount(bits[cell]);
            bits[cell] = 0;
        }

        nightsInWord = 0;
    }
}
