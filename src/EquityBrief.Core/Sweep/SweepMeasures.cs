using EquityBrief.Core.Returns;

namespace EquityBrief.Core.Sweep;

// The figures a candidate carries into a table under one design's plan and exit, laid out by scored year and by
// block: whether it was listed, entered, scored, won, the calibrated bar and the planned break-even it set, what
// it came to in multiples of its planned risk, its edge over the same plan entered on every member that night,
// and whether an open trade of the same stock kept it off the list.
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
    public const int EdgeSum = 9;
    public const int EdgeCount = 10;
    public const int Blocked = 11;

    public const int PerYear = 12;

    // The blocks of 63 sessions from the first scored session, the last of them partial, and the whole ones a
    // record is judged over.
    public const int Blocks = 31;

    public const int WholeBlocks = 30;

    public static int Width => (Years * PerYear) + Blocks;

    // A candidate's figures: listed whatever came of it; entered where it won, lost or ran out of sessions; scored
    // where it won or lost and its paths set a bar, which is every setup the live record reads.
    public static void Fill(Span<float> figures, int year, int block, byte code, float calibrated, float breakEven, float multiple, float edge = float.NaN)
    {
        figures.Clear();
        Tally(figures, year, block, code, calibrated, breakEven, multiple, edge);
    }

    // The same figures added into a running sum rather than written over it.
    public static void Tally(Span<float> figures, int year, int block, byte code, float calibrated, float breakEven, float multiple, float edge)
    {
        if (year < 0 || year >= Years)
        {
            return;
        }

        var at = year * PerYear;
        var entered = code is 1 or 2 or 3;
        var scored = code is 1 or 2 && !float.IsNaN(calibrated);

        figures[at + Listed] += 1;
        figures[at + Entered] += entered ? 1 : 0;

        if (scored)
        {
            figures[at + Scored] += 1;
            figures[at + Wins] += code == 1 ? 1 : 0;
            figures[at + NullSum] += calibrated;

            if (!float.IsNaN(breakEven))
            {
                figures[at + BreakEvenScored] += 1;
                figures[at + BreakEvenSum] += breakEven;
            }

            if (block is >= 0 and < Blocks)
            {
                figures[(Years * PerYear) + block] += 1;
            }
        }

        if (entered && !float.IsNaN(multiple))
        {
            figures[at + MultipleSum] += multiple;
            figures[at + MultipleCount] += 1;

            if (!float.IsNaN(edge))
            {
                figures[at + EdgeSum] += edge;
                figures[at + EdgeCount] += 1;
            }
        }
    }

    // A listing an open trade of the same stock kept off the list, counted in its year and nowhere else.
    public static void TallyBlocked(Span<float> figures, int year)
    {
        if (year >= 0 && year < Years)
        {
            figures[(year * PerYear) + Blocked] += 1;
        }
    }
}

// What one variation's trades came to over the history, read off a table's cell: counts, the share won against
// the break-even the plans needed and against the calibrated bar, the average result in multiples of risk, the
// average edge over the same plan entered on every member, and the same year by year, with the blocks that held
// a scored trade, the nights that listed a name and the listings an open trade kept off.
public sealed record SweepMeasures(
    int Listed,
    int Entered,
    int Scored,
    int Wins,
    double? Share,
    double? BreakEven,
    double? NoSkill,
    double? AverageMultiple,
    double? Edge,
    int[] YearScored,
    double?[] YearShare,
    double?[] YearBreakEven,
    double?[] YearNoSkill,
    double?[] YearAverageMultiple,
    double?[] YearEdge,
    int BlocksWithTrades,
    int NightsListing,
    int Nights,
    int Blocked)
{
    // Section 17's floors the starting point is read against, proposed and the operator's to rule.
    public const int TradeFloor = 300;
    public const int YearsBeating = 6;
    public const int BlockFloor = 22;
    public const double ListingShare = 0.6;

    // The scored years the proposal may not trail the live rule in: the last three, 2024 to 2026.
    public const int RecentYears = 3;

    public static SweepMeasures Of(ReadOnlySpan<float> figures, int nightsListing, int nights)
    {
        var yearScored = new int[SweepFigures.Years];
        var yearShare = new double?[SweepFigures.Years];
        var yearBreakEven = new double?[SweepFigures.Years];
        var yearNoSkill = new double?[SweepFigures.Years];
        var yearMultiple = new double?[SweepFigures.Years];
        var yearEdge = new double?[SweepFigures.Years];
        double listed = 0, entered = 0, scored = 0, wins = 0, nulls = 0, breakEvenScored = 0, breakEvenSum = 0, multipleSum = 0, multipleCount = 0, edgeSum = 0, edgeCount = 0, blocked = 0;

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
            edgeSum += figures[at + SweepFigures.EdgeSum];
            edgeCount += figures[at + SweepFigures.EdgeCount];
            blocked += figures[at + SweepFigures.Blocked];

            yearScored[year] = (int)Math.Round(yearScoredCount);
            yearShare[year] = yearScoredCount > 0 ? figures[at + SweepFigures.Wins] / yearScoredCount * 100 : null;
            yearNoSkill[year] = yearScoredCount > 0 ? figures[at + SweepFigures.NullSum] / yearScoredCount * 100 : null;
            yearBreakEven[year] = figures[at + SweepFigures.BreakEvenScored] > 0 ? figures[at + SweepFigures.BreakEvenSum] / figures[at + SweepFigures.BreakEvenScored] : null;
            yearMultiple[year] = figures[at + SweepFigures.MultipleCount] > 0 ? figures[at + SweepFigures.MultipleSum] / figures[at + SweepFigures.MultipleCount] : null;
            yearEdge[year] = figures[at + SweepFigures.EdgeCount] > 0 ? figures[at + SweepFigures.EdgeSum] / figures[at + SweepFigures.EdgeCount] : null;
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
            edgeCount > 0 ? edgeSum / edgeCount : null,
            yearScored,
            yearShare,
            yearBreakEven,
            yearNoSkill,
            yearMultiple,
            yearEdge,
            blocks,
            nightsListing,
            nights,
            (int)Math.Round(blocked));
    }

    // The same reading with no arrays behind it, for the settings a search reads by the hundred million.
    public static SweepSummary Summary(ReadOnlySpan<float> figures, int nightsListing, int nights)
    {
        double scored = 0, wins = 0, nulls = 0, breakEvenScored = 0, breakEvenSum = 0, multipleSum = 0, multipleCount = 0, edgeSum = 0, edgeCount = 0, blocked = 0;
        var yearsBeatingBoth = 0;
        var yearsBeatingBreakEven = 0;
        var bestYear = -1;
        var bestMargin = double.NegativeInfinity;
        Span<double> yearWon = stackalloc double[SweepFigures.Years];
        Span<double> yearNeeded = stackalloc double[SweepFigures.Years];
        Span<double> yearCount = stackalloc double[SweepFigures.Years];

        for (var year = 0; year < SweepFigures.Years; year++)
        {
            var at = year * SweepFigures.PerYear;
            var count = figures[at + SweepFigures.Scored];

            scored += count;
            wins += figures[at + SweepFigures.Wins];
            nulls += figures[at + SweepFigures.NullSum];
            breakEvenScored += figures[at + SweepFigures.BreakEvenScored];
            breakEvenSum += figures[at + SweepFigures.BreakEvenSum];
            multipleSum += figures[at + SweepFigures.MultipleSum];
            multipleCount += figures[at + SweepFigures.MultipleCount];
            edgeSum += figures[at + SweepFigures.EdgeSum];
            edgeCount += figures[at + SweepFigures.EdgeCount];
            blocked += figures[at + SweepFigures.Blocked];

            if (count <= 0 || figures[at + SweepFigures.BreakEvenScored] <= 0)
            {
                continue;
            }

            var share = figures[at + SweepFigures.Wins] / count * 100;
            var needed = figures[at + SweepFigures.BreakEvenSum] / figures[at + SweepFigures.BreakEvenScored];
            var chance = figures[at + SweepFigures.NullSum] / count * 100;

            yearCount[year] = count;
            yearWon[year] = share / 100 * count;
            yearNeeded[year] = needed / 100 * count;

            if (share > needed)
            {
                yearsBeatingBreakEven++;

                if (share > chance)
                {
                    yearsBeatingBoth++;
                }
            }

            var margin = (share - needed) * count;

            if (margin > bestMargin)
            {
                bestMargin = margin;
                bestYear = year;
            }
        }

        double restScored = 0, restWon = 0, restNeeded = 0;

        for (var year = 0; year < SweepFigures.Years; year++)
        {
            if (year == bestYear)
            {
                continue;
            }

            restScored += yearCount[year];
            restWon += yearWon[year];
            restNeeded += yearNeeded[year];
        }

        var blocks = 0;

        for (var block = 0; block < SweepFigures.WholeBlocks; block++)
        {
            blocks += figures[(SweepFigures.Years * SweepFigures.PerYear) + block] > 0 ? 1 : 0;
        }

        return new SweepSummary(
            (int)Math.Round(scored),
            (float)(scored > 0 ? wins / scored * 100 : double.NaN),
            (float)(breakEvenScored > 0 ? breakEvenSum / breakEvenScored : double.NaN),
            (float)(scored > 0 ? nulls / scored * 100 : double.NaN),
            (float)(multipleCount > 0 ? multipleSum / multipleCount : double.NaN),
            (float)(edgeCount > 0 ? edgeSum / edgeCount : double.NaN),
            (byte)yearsBeatingBoth,
            (byte)yearsBeatingBreakEven,
            bestYear >= 0 && restScored > 0 && restWon > restNeeded,
            (byte)blocks,
            (float)(nights > 0 ? (double)nightsListing / nights : 0),
            (int)Math.Round(blocked));
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

    // Whether this record's edge trails another's in any of the recent years, the years both hold an edge for;
    // a year one of them has no trade in is no trailing.
    public bool TrailsInARecentYear(SweepMeasures other)
    {
        for (var year = SweepFigures.Years - RecentYears; year < SweepFigures.Years; year++)
        {
            if (YearEdge[year] is { } mine && other.YearEdge[year] is { } theirs && mine < theirs)
            {
                return true;
            }
        }

        return false;
    }
}

// One setting's reading with no arrays behind it: what a search sorts, tests against the floors and measures
// depth over. The floors are the record's four, read the same way.
public readonly record struct SweepSummary(
    int Scored,
    float Share,
    float BreakEven,
    float NoSkill,
    float AverageMultiple,
    float Edge,
    byte YearsBeatingBoth,
    byte YearsBeatingBreakEven,
    bool WithoutBestYear,
    byte Blocks,
    float Listing,
    int Blocked)
{
    public bool BeatsBoth => !float.IsNaN(Share) && Share > BreakEven && Share > NoSkill;

    public bool Viable => Scored >= SweepMeasures.TradeFloor && YearsBeatingBoth >= SweepMeasures.YearsBeating;

    public bool MeetsTheFloors =>
        YearsBeatingBreakEven >= SweepMeasures.YearsBeating
        && WithoutBestYear
        && Scored >= SweepMeasures.TradeFloor
        && Blocks >= SweepMeasures.BlockFloor
        && Listing >= SweepMeasures.ListingShare;

    public bool HasEdge => !float.IsNaN(Edge);
}
