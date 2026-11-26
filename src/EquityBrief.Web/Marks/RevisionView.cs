using System.Globalization;

namespace EquityBrief.Web.Marks;

// A trade or an order a revision holds on a stock, as the top of its own page names it: its setup, the revision that placed
// it, whether it waits, the night it was placed and the session it was bought, its price, stop and one take-profit, the
// trading days left to it, the night's close and where it stands.
public sealed record NameTradeView(string Family, string Revision, bool Waiting, DateOnly Placed, DateOnly? Bought, decimal? Price, decimal? Stop, decimal? TakeProfit, int? DaysLeft, decimal? LastClose, string Standing)
{
    // From the operator's correction of 2026-11-24 a waiting order's notes on how it takes profit, "2 times the risk: ..." or
    // "Take-profit added at the rules restart ...", which its card on Home no longer carries; none where its plan says none.
    public string? TakeProfitNote { get; init; }
}

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
    // Which part of its plan placed its take-profit, in words, none where it names no price.
    public string? TakeProfitFrom { get; init; }

    // How far the night's close stands above the buy price, as a fraction; none where either is not read.
    public double? AboveBuy =>BuyPrice is { } buy && buy > 0m && LastClose is { } close ? EquityBrief.Core.Prices.Statistic.FromRatio((close / buy) - 1m) : null;
}

// An order that left a rule's waiting list in the last five trading days: its stock, the session it left on, whether it was
// bought, the words saying why, and the revision that placed it.
public sealed record LeftOrderView(string Ticker, DateOnly On, bool Bought, string Words, string Revision)
{
    public const int Sessions = 5;

    // The family whose rule placed it and the night it was placed, which Home and Tonight tag it by.
    public string? Family { get; init; }

    public DateOnly? Listed { get; init; }

    // From the operator's correction of 2026-11-25, Paper trading's "Last 5 trading days" tab: what happened, in one of four
    // words; the price it was bought, sold or ordered at, none where its order placed none; and its result in words.
    // see: Home counts the last five trading days' buys and sales and orders left unbought in one line, and Paper trading lists them in a tab
    public const string BoughtKind = "bought";

    public const string SoldKind = "sold";

    public const string CancelledKind = "cancelled";

    public const string NotPlacedKind = "not placed";

    public static IReadOnlyList<string> Kinds { get; } = [BoughtKind, SoldKind, CancelledKind, NotPlacedKind];

    public string Kind => Sold ? SoldKind : Bought ? BoughtKind : Unplaced ? NotPlacedKind : CancelledKind;

    // An order whose plan placed no price, which left unbought on its own night.
    public bool Unplaced { get; init; }

    public decimal? Price { get; init; }

    public string Result { get; init; } = string.Empty;

    public static string BoughtWords(string ticker, DateOnly on, decimal fill, decimal? price) =>
        FormattableString.Invariant($"{ticker} bought {on.ToString("d MMM", CultureInfo.InvariantCulture)} at ${fill:N2}, ")
        + (price is { } level && fill < level
            ? FormattableString.Invariant($"opened under its buy price of ${level:N2}. Now under Open trades.")
            : price is { } at ? FormattableString.Invariant($"the price fell to its buy price of ${at:N2}. Now under Open trades.") : "at the open. Now under Open trades.");

    public static string CancelledWords(string ticker, DateOnly last) =>
        ticker + " cancelled: not bought by its last day, " + last.ToString("d MMM", CultureInfo.InvariantCulture) + ".";

    // From the operator's correction of 2026-11-24, item 5, a trade a rule bought at the open and a trade it sold.
    public bool Sold { get; init; }

    public static string BoughtAtOpenWords(string ticker, DateOnly on, decimal buy, bool soldSince) =>
        FormattableString.Invariant($"{ticker} bought {on.ToString("d MMM", CultureInfo.InvariantCulture)} at ${buy:N2}, at the open.") + (soldSince ? string.Empty : " Now under Open trades.");

    public static string SoldWords(string ticker, DateOnly on, decimal sale, decimal buy) =>
        FormattableString.Invariant($"{ticker} sold {on.ToString("d MMM", CultureInfo.InvariantCulture)} at ${sale:N2}")
        + (buy > 0m ? FormattableString.Invariant($", {(sale / buy - 1m) * 100m:+0.0;-0.0;0.0}% on its buy of ${buy:N2}.") : ".");
}
