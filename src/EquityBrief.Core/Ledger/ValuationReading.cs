using EquityBrief.Core.Families;
using EquityBrief.Core.Prices;
using EquityBrief.Core.Sweep;

namespace EquityBrief.Core.Ledger;

// A stock's closes over the sessions a reading reaches, oldest first: each session, its close at the store's scale and
// its close as traded, a close as traded the store does not hold being nought.
public sealed record PriceHistory(DateOnly[] Sessions, double[] Closes, double[] Traded)
{
    public static PriceHistory None { get; } = new([], [], []);

    // A series' bars as a history.
    public static PriceHistory Of(IReadOnlyList<SweepBar> bars) => new(
        [.. bars.Select(bar => bar.Session)],
        [.. bars.Select(bar => Statistic.FromPrice(bar.Close))],
        [.. bars.Select(bar => bar.RawClose > 0m ? Statistic.FromPrice(bar.RawClose) : 0)]);

    // This history followed by one beginning after it.
    public PriceHistory Then(PriceHistory after)
    {
        if (Sessions.Length > 0 && after.Sessions.Length > 0 && after.Sessions[0] <= Sessions[^1])
        {
            throw new ArgumentException($"A history beginning {after.Sessions[0]:yyyy-MM-dd} cannot follow one ending {Sessions[^1]:yyyy-MM-dd}.", nameof(after));
        }

        return Sessions.Length == 0 ? after
            : after.Sessions.Length == 0 ? this
            : new([.. Sessions, .. after.Sessions], [.. Closes, .. after.Closes], [.. Traded, .. after.Traded]);
    }

    // The newest session on or before a day, none where the history starts after it.
    public int? AtOrBefore(DateOnly day)
    {
        var at = Array.BinarySearch(Sessions, day);
        var place = at >= 0 ? at : ~at - 1;

        return place >= 0 ? place : null;
    }
}

// What a valuation reading reads of a filer's facts as first filed before its session: the net income of each four
// quarters running to a quarter of the unbroken run of quarters ending at the newest, by that quarter's end, and each
// count the filings' covers state by the day it stands for, both oldest first.
public sealed record ValuationParts(IReadOnlyList<(DateOnly End, decimal Earnings)> Earnings, IReadOnlyList<(DateOnly Day, decimal Shares)> Counts);

// A stock's earnings over its value placed among the same figure at the ends of its own twelve quarters before the
// session: the share of those quarters it stands above, a tie counting half. The earnings are the four newest quarters'
// net income as first filed before the session, a fiscal fourth quarter its year less its first nine months. The value
// is a count the filing's cover states times the close: the close at the store's scale, and the count brought to it by
// the close as traded against the close at the store's scale on the count's own day, so a split between the count's day
// and the close's moves no value. The current figure reads the session's close and the newest count; each quarter reads
// its own four quarters to its end, the newest close on or before its end and the earliest count standing for a day on
// or after its end, which its own report's cover states. The twelve are the newest twelve quarters, each following the
// one before it, so a quarter missing a part, a broken run of quarters among them, leaves the reading none.
// see: The valuation reading reads the prices before the store's year at the store's scale from the history pulled for those years
public static class ValuationReading
{
    public const int Quarters = 12;

    // The quarters an earnings figure sums.
    public const int YearOfQuarters = 4;

    // The facts a reading reads, only those filed before the session.
    public static ValuationParts PartsOf(IReadOnlyList<FiledFactRow> facts, DateOnly session)
    {
        var known = facts.Where(fact => fact.Filed < session).ToArray();
        var quarters = FirstFiledRevenue.Quarters(
            known.Select(fact => new FiledRevenue(fact.Concept, fact.Start, fact.End, fact.Value, fact.Filed, fact.Form, fact.Accession)),
            FiledFacts.NetIncome);

        // The unbroken run ending at the newest quarter: each quarter beginning the day after the one before it ends,
        // within the days the revenue rule allows a quarter's end to move.
        var from = quarters.Count - 1;

        while (from > 0 && Math.Abs(quarters[from].Start.DayNumber - quarters[from - 1].End.AddDays(1).DayNumber) <= RevenueGrowth.YearBeforeWithin)
        {
            from--;
        }

        var earnings = new List<(DateOnly, decimal)>();

        for (var at = from + YearOfQuarters - 1; at < quarters.Count; at++)
        {
            earnings.Add((quarters[at].End, quarters.Skip(at - YearOfQuarters + 1).Take(YearOfQuarters).Sum(quarter => quarter.Value)));
        }

        var counts = known
            .Where(fact => FiledFacts.SharesOutstanding.Contains(fact.Concept, StringComparer.Ordinal))
            .GroupBy(fact => fact.End)
            .Select(day => (day.Key, day.OrderBy(fact => fact.Filed).ThenBy(fact => fact.Accession, StringComparer.Ordinal).First().Value))
            .OrderBy(count => count.Key)
            .ToArray();

        return new ValuationParts(earnings, counts);
    }

    // The oldest day the reading reads a close for, the end of the oldest of its twelve quarters, none where it has fewer.
    public static DateOnly? OldestDay(ValuationParts parts) => parts.Earnings.Count >= Quarters ? parts.Earnings[^Quarters].End : null;

    // The reading on a session the prices hold, none where any part of it is missing. A count standing for a day the
    // session has not reached is not read, so no close after the session is.
    public static double? Place(ValuationParts parts, PriceHistory prices, DateOnly session)
    {
        var counts = parts.Counts.Where(count => count.Day < session).ToArray();

        if (parts.Earnings.Count < Quarters || counts.Length == 0
            || prices.AtOrBefore(session) is not { } today || prices.Sessions[today] != session)
        {
            return null;
        }

        var (newestDay, newestShares) = counts[^1];

        if (Yield(parts.Earnings[^1].Earnings, newestDay, newestShares, prices, today) is not { } current)
        {
            return null;
        }

        var yields = new List<double>(Quarters);

        foreach (var (end, earnings) in parts.Earnings.Skip(parts.Earnings.Count - Quarters))
        {
            var own = counts.FirstOrDefault(count => count.Day >= end);

            if (own.Day == default || prices.AtOrBefore(end) is not { } close || Yield(earnings, own.Day, own.Shares, prices, close) is not { } yield)
            {
                return null;
            }

            yields.Add(yield);
        }

        return (yields.Count(yield => yield < current) + (0.5 * yields.Count(yield => yield == current))) / Quarters;
    }

    // Earnings over the value at a session: the earnings a share of the count, over the session's close brought to the
    // count's basis by the close as traded on the count's day against the close at the store's scale that day.
    static double? Yield(decimal earnings, DateOnly countDay, decimal shares, PriceHistory prices, int at)
    {
        if (shares <= 0m || prices.AtOrBefore(countDay) is not { } on)
        {
            return null;
        }

        var (closeOn, tradedOn, closeAt) = (prices.Closes[on], prices.Traded[on], prices.Closes[at]);

        return closeOn > 0 && tradedOn > 0 && closeAt > 0
            ? Statistic.FromRatio(earnings / shares) / (tradedOn * closeAt / closeOn)
            : null;
    }
}
