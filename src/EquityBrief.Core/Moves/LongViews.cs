using EquityBrief.Core.Prices;

namespace EquityBrief.Core.Moves;

// One point of the long view: the last session a calendar month holds and its close at the bar store's scale.
public sealed record MonthEnd(DateOnly Session, decimal Close);

// The long view: the month-end closes over the history pulled before the store's year and over the year itself, all at
// the bar store's scale so the plan's levels, which are prices at that scale, sit on the line where they belong. The
// pull's closes are brought to that scale by the ratio of the two closes on the first session both hold, the ratio the
// chart's averages are brought to it by, and given the shape every price this system writes has; a month's point is the
// last close its sessions hold, dated, so a month whose last session a series misses is drawn at the session it holds and
// says which.
// see: The long view draws the month-end closes over the pulled history at the bar store's scale with the plan's levels on the same prices
public static class LongViews
{
    // A pulled close at the bar store's scale.
    public static decimal Scaled(decimal pulled, decimal ratio) => PriceForm.Round(pulled * ratio);

    // The month-end closes of the pulled sessions before the store's first, brought to its scale, then of the store's own
    // sessions, oldest first; a month both hold takes the store's last session in it.
    public static IReadOnlyList<MonthEnd> Of(
        IReadOnlyList<(DateOnly Session, decimal Close)> pulledBefore,
        decimal ratio,
        IReadOnlyList<(DateOnly Session, decimal Close)> stored)
    {
        var first = stored.Count > 0 ? stored.Min(bar => bar.Session) : DateOnly.MaxValue;

        return
        [
            .. pulledBefore
                .Where(bar => bar.Session < first)
                .Select(bar => (bar.Session, Close: Scaled(bar.Close, ratio)))
                .Concat(stored)
                .GroupBy(bar => (bar.Session.Year, bar.Session.Month))
                .Select(month => month.MaxBy(bar => bar.Session))
                .OrderBy(bar => bar.Session)
                .Select(bar => new MonthEnd(bar.Session, bar.Close)),
        ];
    }
}
