namespace EquityBrief.Core.Loop;

// One test year of a proposal against the current rule: the year, whether it is complete, each side's units ended with a
// result, trades or a book's months, and each side's total edge after costs over them.
public sealed record LoopYear(int Year, bool Complete, int CurrentUnits, int ProposedUnits, double CurrentTotal, double ProposedTotal)
{
    public bool Better => ProposedTotal > CurrentTotal;
}

// What the tester reads of one proposal: its adjusted p-value against the bar, the stability screen's counted and
// better years, the paired total with the largest results left out, its units, its blocks and the smallest difference a
// unit the gate detects four times in five.
public sealed record LoopVerdict(
    double? Adjusted,
    int Blocks,
    bool Gate,
    bool Stable,
    int Counted,
    int Better,
    double? Trimmed,
    int Units,
    bool Counts,
    double? Detectable)
{
    public bool Passes => Gate && Stable && Trimmed > 0 && Counts;
}

// The three screens beside the gate: better in at least 60 per cent of the complete test years holding 50 trades a side,
// or a book's twelve months, the latest complete year counted and better; still above nothing with the five largest
// results by size left out of each side; and at least 300 test trades for a swing family, or 36 months of a book.
// see: A proposal passes the tester on a block sign-flip test of its total edge after costs against the current rule, corrected within a run and held to a fixed bar across runs
public static class LoopScreens
{
    // Three years in five.
    public const int StableOf = 3;

    public const int StableIn = 5;

    public const int YearTrades = 50;

    public const int YearMonths = 12;

    public const int LeftOut = 5;

    public const int SwingTrades = 300;

    public const int BookMonths = 36;

    // The years counted and the years better, and whether the screen passes: at least three in five of the counted years
    // better, the latest complete year among them and better. A partial year is never counted.
    public static (bool Passes, int Counted, int Better) Stability(IReadOnlyList<LoopYear> years, int fewest)
    {
        var counted = years.Where(year => year.Complete && year.CurrentUnits >= fewest && year.ProposedUnits >= fewest).ToArray();
        var better = counted.Count(year => year.Better);
        var latest = years.Where(year => year.Complete).OrderByDescending(year => year.Year).FirstOrDefault();
        var passes = latest is not null
            && counted.Contains(latest)
            && latest.Better
            && better * StableIn >= counted.Length * StableOf;

        return (passes, counted.Length, better);
    }

    // The proposal's total less the current rule's, each with its five largest results by size left out; none where a
    // side holds no result.
    public static double? Trimmed(IReadOnlyList<double> current, IReadOnlyList<double> proposed) =>
        current.Count == 0 && proposed.Count == 0
            ? null
            : Kept(proposed) - Kept(current);

    static double Kept(IReadOnlyList<double> results) =>
        results.OrderByDescending(Math.Abs).Skip(LeftOut).Sum();

    // Whether a proposal holds the units its family is judged on.
    public static bool Counts(bool book, int units) => units >= (book ? BookMonths : SwingTrades);

    // The fewest units a year holds on each side to be counted.
    public static int Fewest(bool book) => book ? YearMonths : YearTrades;
}
