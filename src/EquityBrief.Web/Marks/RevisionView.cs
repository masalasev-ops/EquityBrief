using System.Globalization;

namespace EquityBrief.Web.Marks;

// One revision of a rule as the pages draw it: its number, the first session it ran, the last where a later revision followed
// it, none while it runs, and what it changed from the revision before in the words the rule's settings are drawn in, none
// for the first.
// see: A rule changes only through a new revision, a run of its registrations stating the same settings, and every trade belongs to the revision that listed it
public sealed record RevisionView(int Number, DateOnly From, DateOnly? Until, string? Changed)
{
    // What a revision changed where its settings differ only in one the page draws no words for.
    public const string CodeOnly = "a setting the page draws no words for";

    public bool Running => Until is null;

    // The registration that started it, which its plan is read from.
    public long Registration { get; init; }

    public string Name => Named(Number);

    public static string Named(int number) => "Revision " + number.ToString(CultureInfo.InvariantCulture);

    // "Revision 9, running since 16 Nov 2026", or "Revision 8, ran 30 Oct to 13 Nov 2026" for one no longer running.
    public string Tag => Until is { } until
        ? Name + ", ran " + Ran(From, until)
        : Name + ", running since " + Day(From);

    // The sessions it ran, the year once where both fall in it.
    public static string Ran(DateOnly from, DateOnly until) =>
        from.Year == until.Year
            ? from.ToString("d MMM", CultureInfo.InvariantCulture) + " to " + Day(until)
            : Day(from) + " to " + Day(until);

    public static string Day(DateOnly day) => day.ToString("d MMM yyyy", CultureInfo.InvariantCulture);
}

// A trade or an order a revision holds on a stock, as the top of its own page names it: its setup, the revision that placed
// it, whether it waits, the night it was placed and the session it was bought, its price, stop and one take-profit, the
// trading days left to it, the night's close and where it stands.
public sealed record NameTradeView(string Family, string Revision, bool Waiting, DateOnly Placed, DateOnly? Bought, decimal? Price, decimal? Stop, decimal? TakeProfit, int? DaysLeft, decimal? LastClose, string Standing);

// A revision's plan in five plain rows, read from its registration.
public sealed record PlanRowsView(string LooksFor, string Buys, string Loss, string Profit, string Limit);

// An order a rule placed on an earlier night and has not bought, as its Waiting to buy tab draws it: its stock and company,
// the night it was placed, the price it buys at, none for one buying at the next open, the night's close, the stop and the one
// take-profit its plan works from that price, whether that take-profit sells part of it, the last session it waits and the
// trading days left to it, the words of one with no price to wait for, and the revision that placed it.
public sealed record WaitingRowView(
    string Ticker,
    string? Company,
    DateOnly Placed,
    decimal? BuyPrice,
    decimal? LastClose,
    decimal? Stop,
    decimal? TakeProfit,
    bool TakeProfitIsPart,
    DateOnly? LastDay,
    int? DaysLeft,
    string? Words,
    string Revision)
{
    // How far the night's close stands above the buy price, as a fraction; none where either is not read.
    public double? AboveBuy => BuyPrice is { } buy && buy > 0m && LastClose is { } close ? (double)((close / buy) - 1m) : null;
}

// An order that left a rule's waiting list in the last five trading days: its stock, the session it left on, whether it was
// bought, the words saying why, and the revision that placed it.
public sealed record LeftOrderView(string Ticker, DateOnly On, bool Bought, string Words, string Revision)
{
    public const int Sessions = 5;

    // The family whose rule placed it and the night it was placed, which Home and Tonight tag it by.
    public string? Family { get; init; }

    public DateOnly? Listed { get; init; }

    public static string BoughtWords(string ticker, DateOnly on, decimal fill, decimal? price) =>
        FormattableString.Invariant($"{ticker} bought {on.ToString("d MMM", CultureInfo.InvariantCulture)} at ${fill:N2}, ")
        + (price is { } level && fill < level
            ? FormattableString.Invariant($"opened under its buy price of ${level:N2}. Now under Open trades.")
            : price is { } at ? FormattableString.Invariant($"the price fell to its buy price of ${at:N2}. Now under Open trades.") : "at the open. Now under Open trades.");

    public static string CancelledWords(string ticker, DateOnly last) =>
        ticker + " cancelled: not bought by its last day, " + last.ToString("d MMM", CultureInfo.InvariantCulture) + ".";
}
