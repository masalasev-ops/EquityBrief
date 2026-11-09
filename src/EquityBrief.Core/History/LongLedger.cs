using EquityBrief.Core.Chart;
using EquityBrief.Core.Sweep;

namespace EquityBrief.Core.History;

// The long history's ledger: the span it is built over, the two readings each setup carries, and the rules its reader
// and its walks read a series by.
// see: The long ledger is read over the long spans and the long prices with the store's own closes after them, each setup under the rule as ruled and with a stock whose prices stop ended at its last price
public static class LongLedger
{
    // The span `ledger-build --long` writes: the first session of 2005 through the last of 2018.
    public static DateOnly From { get; } = new(2005, 1, 3);

    public static DateOnly Through { get; } = new(2018, 12, 31);

    // The long run's own first scored session, which the heavyweights' rebalances count from; the sweeps' pinned
    // constant stays as it stands.
    public static DateOnly FirstScored => From;

    // The two readings of a setup: the benchmark and the trade as ruled, a member whose prices stop before its path ends
    // left out of the benchmark and such a trade left with no result; and each such member and trade ended at its last price.
    public const string AsRuled = "as ruled";

    // The most the long history's tables may grow the store by, in gigabytes of a thousand million bytes, on the operator's
    // answer of 2026-11-07, over the size the store held when it was asked, before the long history's first table; a long
    // ledger chunk that would start with the store past the two together stops the build.
    public const double BudgetGigabytes = 4.5;

    public const double StoreBeforeGigabytes = 5.82;

    public const long CeilingBytes = (long)((StoreBeforeGigabytes + BudgetGigabytes) * 1_000_000_000);

    public const string LastPrice = "last price";

    // The eras the measure reads the years in, as the data check's History page draws them.
    public static IReadOnlyList<(string Era, int First, int Last)> Eras { get; } =
    [
        ("2005 to 2009", 2005, 2009),
        ("2010 to 2014", 2010, 2014),
        ("2015 to 2019", 2015, 2019),
        ("2020 on", 2020, 9999),
    ];

    public static string EraOf(int year) => Eras.First(era => year >= era.First && year <= era.Last).Era;

    // The year the global financial crisis is marked on the page.
    public const int Crisis = 2008;

    // The standing years: the sweeps and the standing ledger scored from the first session of 2019, so the live settings
    // were chosen on the years from it and never saw a year before it.
    public static DateOnly StandingFrom { get; } = new(2019, 1, 1);

    public static bool ChosenOn(int year) => year >= StandingFrom.Year;

    // A stock's series as the long reader reads it under its marks: the sessions before the one its history starts on
    // left out, a bad print skipped, and the sessions after the one its history ends on left out. A move checked and
    // kept reads as it stands. A mark is read wherever it stands, the first read of the series after it was given
    // reading it.
    public static IReadOnlyList<SweepBar> Marked(IReadOnlyList<SweepBar> bars, IReadOnlyList<SessionMark> marks)
    {
        var starts = marks.Where(mark => mark.Kind == SessionMarking.StartsHere).Select(mark => (DateOnly?)mark.Session).Max();
        var ends = marks.Where(mark => mark.Kind == SessionMarking.HistoryEnds).Select(mark => (DateOnly?)mark.Session).Min();
        var bad = marks.Where(mark => mark.Kind == SessionMarking.BadPrint).Select(mark => mark.Session).ToHashSet();

        return
        [
            .. bars.Where(bar => (starts is not { } start || bar.Session >= start)
                && (ends is not { } end || bar.Session <= end)
                && !bad.Contains(bar.Session)),
        ];
    }

    // A series whose prices stop before the replay's end, held at its last close and open for every session after it, so
    // the walks the ledger already runs end a trade and a benchmark member on it at its last price: no stop, target or
    // trail is reached on a price that does not move, and the cap or the hold's end sells it there. A series that runs to
    // the end, or holds no price, is returned as it is.
    // The opens are held at the last close too, the last price a sale after it can read.
    public static double[] HeldFlat(double[] prices, int sessionsAfter, double? at = null)
    {
        if (sessionsAfter <= 0 || prices.Length == 0)
        {
            return prices;
        }

        var held = new double[prices.Length + sessionsAfter];

        Array.Copy(prices, held, prices.Length);
        Array.Fill(held, at ?? prices[^1], prices.Length, sessionsAfter);

        return held;
    }

    // A standing trade's result at its last price where its stock's prices stop while it is open, in the form its family's
    // results take: a book's the last close over the price it was bought at, less one, and a swing trade's the move from that
    // price in the risk its plan set, its entry less its stop; none where it was bought at no price or its stop stands at or
    // over its entry.
    public static double? AtLastPrice(bool book, double bought, double entry, double stop, double lastClose)
    {
        if (!(bought > 0) || !(lastClose > 0))
        {
            return null;
        }

        if (book)
        {
            return (lastClose / bought) - 1;
        }

        return entry > stop ? (lastClose - bought) / (entry - stop) : null;
    }

    // Whether a code is a member of its index on a day by its long spans: joined on or before it and not yet left.
    public static bool MemberOn(IReadOnlyList<(DateOnly Joined, DateOnly? Left)> spans, DateOnly day) =>
        spans.Any(span => span.Joined <= day && (span.Left is not { } left || left > day));

    // Whether a trade's hold crosses an unexplained jump, its own stock's or a member's its benchmark holds: one standing
    // after the session it was set up on and on or before the session it ended, an open trade crossing none.
    public static bool Crosses(IReadOnlyList<DateOnly> jumps, DateOnly session, DateOnly? endedOn) =>
        endedOn is { } end && jumps.Any(jump => jump > session && jump <= end);

    // A family's years under one reading: its setups, those settled, those reaching their target, the mean edge and the mean
    // edge after costs over the settled, those whose hold crosses an unexplained jump, and the mean edge after costs without
    // them, which every result is shown beside.
    public static IReadOnlyList<LongYear> Years(IEnumerable<LongTrade> trades, string reading) =>
    [
        .. trades
            .GroupBy(trade => (trade.Family, trade.Year))
            .OrderBy(group => group.Key.Family, StringComparer.Ordinal)
            .ThenBy(group => group.Key.Year)
            .Select(group =>
            {
                // From the 23.6 correction a settled setup whose edge lies past 20 risks either way is no result: it is
                // counted apart and read in no figure, as an unexplained jump is never a real result.
                // see: A long setup whose edge lies past 20 risks either way is counted as no result and listed beside the unexplained jumps
                var outlying = group.Count(trade => trade.Settled && Outlying(trade.Edge));
                var settled = group.Where(trade => trade.Settled && !Outlying(trade.Edge)).ToArray();

                return new LongYear(
                    group.Key.Family,
                    group.Key.Year,
                    reading,
                    group.Count(),
                    settled.Length,
                    settled.Count(trade => trade.Target),
                    Mean(settled.Select(trade => trade.Edge)),
                    Mean(settled.Select(trade => trade.EdgeAfterCost)),
                    settled.Count(trade => trade.Crossing),
                    Mean(settled.Where(trade => !trade.Crossing).Select(trade => trade.EdgeAfterCost)))
                {
                    Outlying = outlying,
                };
            }),
    ];

    // Whether an edge lies past 20 risks either way, which the long history counts as no result.
    // see: A long setup whose edge lies past 20 risks either way is counted as no result and listed beside the unexplained jumps
    public static bool Outlying(double? edge) => edge is { } held && Math.Abs(held) > OutlyingRisks;

    static double? Mean(IEnumerable<double?> values)
    {
        var held = values.Where(value => value is not null).Select(value => value!.Value).ToArray();

        return held.Length == 0 ? null : held.Average();
    }

    // Whether the older decade's unexplained jumps a stock-year are more than twice the newer years', which the measure says
    // at its top and does not wait on.
    public static bool OlderJumpsAbove(int olderJumps, int olderStockYears, int newerJumps, int newerStockYears) =>
        olderStockYears > 0 && newerStockYears > 0
        && 1L * olderJumps * newerStockYears > OlderJumpsTimes * newerJumps * olderStockYears;

    public const double OlderJumpsTimes = 2;

    // The edge in risks past which the measure names a swing setup outlying: a member whose prices stand still sets a risk
    // near nought, and the result read in it runs to hundreds.
    public const double OutlyingRisks = 20;
}

// One setup as the long summary reads it: its family and year, whether it settled and reached its target, its edge before
// and after costs, and whether its hold crossed one of its stock's unexplained jumps.
public sealed record LongTrade(string Family, int Year, bool Settled, bool Target, double? Edge, double? EdgeAfterCost, bool Crossing);

// One family's year under one reading, as the long readings keep it.
public sealed record LongYear(string Family, int Year, string Reading, int Setups, int Settled, int Targets, double? EdgeMean, double? EdgeAfterCostMean, int Crossing, double? EdgeAfterCostWithout)
{
    // From the 23.6 correction the settled setups whose edge lies past 20 risks either way, counted as no result and read in
    // none of the figures above.
    public int Outlying { get; init; }
}
