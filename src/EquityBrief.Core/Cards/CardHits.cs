using EquityBrief.Core.Bars;
using EquityBrief.Core.Prices;
using EquityBrief.Core.Providers;

namespace EquityBrief.Core.Cards;

// The stock's stored earnings reactions read against the plan: how many, the median move in the stock's typical moves
// and in the plan's risks, and how many moved further than the stop's distance in either direction.
public sealed record ReactionFigures(int Count, double? MedianTypical, double? MedianRisks, int PastTheStop);

// The next ex-dividend date inside the hold: declared where the calendar carries one and estimated where it does not,
// with the payment a share and its size in the plan's risks, or in per cent of the buy for a rule with no stop.
public sealed record DividendAhead(DateOnly Date, bool Declared, decimal? Amount, double? InRisks, double? InPercent);

// What could hit a pick's trade before it ends: the hold's last session, the reactions, the next ex-dividend date inside
// the hold, and the market events inside it with each kind whose table ends before the hold does. Where no date is found
// and the hold runs past the sessions the calendar is asked for with no dividend of the company's kept to estimate from,
// a later date is unread rather than ruled out.
public sealed record CardHits(DateOnly HoldThrough, ReactionFigures Reactions, DividendAhead? Dividend, IReadOnlyList<MarketEvent> Events, IReadOnlyList<string> PastTheTable, bool DividendUnread = false);

// The company's dividend as the quarters fetch kept it: the forward rate a share, the last declared ex-date and each year's
// count. Where the newest fundamentals fetch stands in for it, no year's count is filed and the payments a year are the
// ones the bars' steps show.
public sealed record DividendKept(decimal? ForwardRate, DateOnly? LastExDate, IReadOnlyList<DividendsInYear> ByYear)
{
    public int? PaymentsAYear { get; init; }
}

// What could hit a pick, worked out by the night from stored rows alone.
// see: A pick's next ex-dividend date is the calendar's where it declares one, and otherwise estimated from the dividend the quarters fetch kept or else from the newest fundamentals fetch and the steps its dividends leave in the bars
// see: Market events inside a hold are read from a committed table of the Fed's and the BLS's own dates, and asked of no provider
public static class CardHitsReading
{
    // The sessions a hold is read over where the rule caps none: a month, to the sector heavyweights' next rebalance.
    public const int UncappedSessions = 21;

    // The rise in the ratio of the adjusted close to the raw close from one stored session to the next that is read as a
    // dividend's step: at least a fiftieth of a per cent, which a session with no dividend does not reach, and under a
    // fifth, which a split of six for five or wider reaches.
    public const decimal SmallestStep = 0.0002m;

    public const decimal LargestStep = 0.20m;

    // The payments a year the steps are read as: monthly, quarterly, half-yearly or yearly.
    public static IReadOnlyList<int> UsualPaymentsAYear { get; } = [12, 4, 2, 1];

    // The hold's last session: the cap's count of sessions after the night on the exchange's calendar.
    public static DateOnly HoldThrough(DateOnly night, int? cap)
    {
        var sessions = cap ?? UncappedSessions;
        var day = night;

        for (var counted = 0; counted < sessions;)
        {
            day = day.AddDays(1);

            if (ExchangeClosures.IsSession(day))
            {
                counted++;
            }
        }

        return day;
    }

    // Each stored reaction's move, in per cent of the close before it, read against the night's close and typical move
    // and the plan's distance from its buy to its stop.
    public static ReactionFigures Reactions(IReadOnlyList<double> movesPercent, decimal? close, double? typicalMove, decimal? buy, decimal? stop)
    {
        if (movesPercent.Count == 0)
        {
            return new ReactionFigures(0, null, null, 0);
        }

        var sizes = movesPercent.Select(Math.Abs).ToArray();
        double? medianTypical = close is { } at && typicalMove is { } typical && typical > 0
            ? Median(sizes.Select(size => size / 100 * Statistic.FromPrice(at) / typical))
            : null;
        double[]? risks = buy is { } entry && stop is { } floor && entry > floor
            ? [.. sizes.Select(size => size / 100 * Statistic.FromPrice(entry) / Statistic.FromPrice(entry - floor))]
            : null;

        return new ReactionFigures(sizes.Length, medianTypical, risks is null ? null : Median(risks), risks?.Count(risk => risk > 1) ?? 0);
    }

    // How many dividends a year the stored bars show, oldest session first: each rise in the ratio of the adjusted close to
    // the raw close from one session to the next of at least the smallest step and under the largest is an ex-date the
    // provider's adjustment carries, and the median of the days between two in turn is read as the nearest of a monthly,
    // quarterly, half-yearly or yearly interval by their ratio, so a quarter of 77 days and one of 105 read as quarterly
    // alike. None where the bars show fewer than two steps.
    public static int? PaymentsAYear(IReadOnlyList<(DateOnly Session, decimal Close, decimal RawClose)> bars)
    {
        var steps = new List<DateOnly>();

        for (var at = 1; at < bars.Count; at++)
        {
            var (_, closeBefore, rawBefore) = bars[at - 1];
            var (session, close, raw) = bars[at];

            if (closeBefore <= 0m || rawBefore <= 0m || raw <= 0m)
            {
                continue;
            }

            var rise = close / raw / (closeBefore / rawBefore) - 1m;

            if (rise >= SmallestStep && rise < LargestStep)
            {
                steps.Add(session);
            }
        }

        if (steps.Count < 2)
        {
            return null;
        }

        var gap = Median(steps.Zip(steps.Skip(1), DaysBetween));

        return UsualPaymentsAYear.MinBy(payments => Math.Abs(Math.Log(gap * payments / 365.25)));

        static double DaysBetween(DateOnly earlier, DateOnly later) => later.DayNumber - earlier.DayNumber;
    }

    // The next ex-date inside the hold: the first the calendar declares after the night, and where it declares none the
    // company's last declared ex-date carried forward by its usual interval, a year over the count of its last whole
    // year that paid or over the payments a year its bars' steps show, to the first date after the night. None where
    // neither falls inside the hold or the company pays none.
    public static DividendAhead? Dividend(DateOnly night, DateOnly through, IReadOnlyList<DateOnly> declared, DividendKept? kept, decimal? buy, decimal? stop)
    {
        var count = kept?.ByYear.Where(year => year.Year < night.Year && year.Count > 0).OrderBy(year => year.Year).LastOrDefault()?.Count ?? kept?.PaymentsAYear;
        decimal? amount = kept?.ForwardRate is { } rate && rate > 0m && count is { } paid ? rate / paid : null;

        DividendAhead Ahead(DateOnly on, bool isDeclared) => new(
            on,
            isDeclared,
            amount,
            amount is { } each && buy is { } entry && stop is { } floor && entry > floor ? Statistic.FromRatio(each / (entry - floor)) : null,
            amount is { } share && stop is null && buy is { } bought && bought > 0m ? Statistic.FromRatio(share / bought * 100m) : null);

        if (declared.Where(on => on > night && on <= through).Order().FirstOrDefault() is { } first && first != default)
        {
            return Ahead(first, true);
        }

        if (kept?.LastExDate is not { } last || count is not { } times || kept.ForwardRate is not > 0m)
        {
            return null;
        }

        var interval = 365.0 / times;
        var next = last;

        for (var step = 1; next <= night; step++)
        {
            next = last.AddDays((int)Math.Round(interval * step, MidpointRounding.AwayFromZero));
        }

        return next <= through ? Ahead(next, false) : null;
    }

    // A hold running past the last of the sessions the calendar is asked for, the count the night's ask states, finds no date
    // the calendar does not declare unless the company's dividend is kept to estimate one from; with none kept, a date after
    // those sessions is unread, and the card says so rather than that there is none.
    public static CardHits Read(DateOnly night, int? cap, ReactionFigures reactions, IReadOnlyList<DateOnly> declared, DividendKept? kept, decimal? buy, decimal? stop, IReadOnlyList<MarketEvent>? table = null, int? declaredSessions = null)
    {
        var through = HoldThrough(night, cap);
        var (events, past) = MarketEvents.Within(night.AddDays(1), through, table);
        var dividend = Dividend(night, through, declared, kept, buy, stop);
        var unread = dividend is null && kept is null && declaredSessions is { } asked && through > HoldThrough(night, asked);

        return new CardHits(through, reactions, dividend, events, past, unread);
    }

    static double Median(IEnumerable<double> values)
    {
        var sorted = values.Order().ToArray();
        var middle = sorted.Length / 2;

        return sorted.Length % 2 == 1 ? sorted[middle] : (sorted[middle - 1] + sorted[middle]) / 2;
    }
}
