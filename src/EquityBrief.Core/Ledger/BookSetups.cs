using EquityBrief.Core.Families;

namespace EquityBrief.Core.Ledger;

// The two book families' setups in the ledger, from 21.4. Quality at a fair price: each member on a reshuffle session,
// the first session of a calendar month, whose scorecard clears six in nine, the grid's lowest floor, whatever its
// valuation or its trend, held while it clears that at each later reshuffle. The Fundamentals family: each member a
// filing read anew on the session leaves at six in nine or better, one step past the grid's lowest buy, whether or not
// above its filing before, since the rise, the flag and the faster growth are dials of the grid and not the gate, held
// while every later filing leaves it at five in nine or better, one step past the grid's lowest hold. Each is bought at
// the session's close, held never past a year of sessions and judged against an equal-weighted hold of the index's
// members over the same sessions, as the heavyweights' setups are against their size cut.
// see: A book family's setup is each member a reshuffle or a filing leaves at the ledger's gate, held while it clears the hold and never past a year against an equal-weighted hold of the index
public static class BookSetups
{
    // The scorecard's floor a quality setup clears, the grid's lowest floor.
    public const int QualityFloorInNine = 6;

    // The score a filing read anew leaves a Fundamentals setup at or above, one step past the grid's lowest buy, and the
    // score it is held at or above, one step past the grid's lowest hold.
    public const int ImprovingBuyInNine = 6;

    public const int ImprovingHoldInNine = 5;

    // The sessions a book setup is given, the heavyweights' year.
    public const int Cap = HeavyweightPaths.Cap;

    public static IReadOnlyList<string> Families { get; } = [QualityRule.Name, ImprovingRule.Name];

    // A family whose setups are a book's: no stop, a result a fraction of the buy, and an equal-weighted cut as the
    // benchmark.
    public static bool IsBook(string family) => family == HeavyweightRule.Name || Families.Contains(family, StringComparer.Ordinal);

    public static bool QualityClears(int passed, int applied) => FundamentalScorecard.Clears(passed, applied, QualityFloorInNine);

    public static bool ImprovingBuys(int passed, int applied) => FundamentalScorecard.Clears(passed, applied, ImprovingBuyInNine);

    public static bool ImprovingHolds(int passed, int applied) => FundamentalScorecard.Clears(passed, applied, ImprovingHoldInNine);

    // Whether a session reshuffles the quality book: the calendar's first, or the first in a calendar month, as the
    // family reshuffles on the first night it reads in a month.
    public static bool Reshuffles(IReadOnlyList<DateOnly> calendar, int at) =>
        at == 0 || QualityRule.Reshuffles(calendar[at], calendar[at - 1]);

    // Whether a filing among the days given, in order, is read anew on a session: filed on or after the session before
    // it and before it, as the night reads the filings since its last read; none on the calendar's first session, which
    // has no session before it to read since.
    public static bool FiledAnew(IReadOnlyList<DateOnly> filingDays, IReadOnlyList<DateOnly> calendar, int at)
    {
        if (at <= 0)
        {
            return false;
        }

        var (from, to) = (calendar[at - 1], calendar[at]);
        var low = 0;
        var high = filingDays.Count - 1;

        while (low <= high)
        {
            var middle = (low + high) / 2;

            if (filingDays[middle] < from)
            {
                low = middle + 1;
            }
            else
            {
                high = middle - 1;
            }
        }

        return low < filingDays.Count && filingDays[low] < to;
    }
}
