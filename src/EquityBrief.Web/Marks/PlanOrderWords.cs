using System.Globalization;

namespace EquityBrief.Web.Marks;

// From the 23.10 correction of 2026-11-13 the words a live rule's order under a plan buying at a price is drawn in while it
// waits for its price, with that price and the last session it waits, and once its window passed unfilled; one place, so
// every page says the same and the counts read them.
// see: A live rule's trade under a plan is drawn on every page as its plan made it, and its waiting orders are a group of their own
public static class PlanOrderWords
{
    public const string WaitingOpening = "waiting for its price";

    public const string NotFilled = "not filled, no trade";

    public static string Waiting(decimal? price, DateOnly lastDay) =>
        price is { } at
            ? FormattableString.Invariant($"{WaitingOpening}, {at.ToString("0.00", CultureInfo.InvariantCulture)}, until {lastDay.ToString("d MMM yyyy", CultureInfo.InvariantCulture)}")
            : FormattableString.Invariant($"{WaitingOpening}, its levels placing none, until {lastDay.ToString("d MMM yyyy", CultureInfo.InvariantCulture)}");

    public static bool IsWaiting(string? why) => why is not null && why.StartsWith(WaitingOpening, StringComparison.Ordinal);

    public static bool IsNotFilled(string? why) => string.Equals(why, NotFilled, StringComparison.Ordinal);
}
