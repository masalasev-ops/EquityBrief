using System.Globalization;
using System.Text;
using EquityBrief.Core.Cards;
using EquityBrief.Core.Loop;
using EquityBrief.Core.Prices;

namespace EquityBrief.Core.Paper;

// One line of a night's order changes: its list, the index, who placed it, the stock and its words, every price in it
// read from the paper account's own rows.
public sealed record OrderChange(string List, string Index, string Who, string Ticker, string Words);

// One account's orders as the night's order changes read them: who it is, by its setup and index; whether a book's; each
// trade the account's walk took, a trade it skipped for want of cash being no order; and each stock it kept that no fill
// bought, with the night that listed it and why.
public sealed record AccountOrders(string Who, bool Book, IReadOnlyList<PaperTrade> Taken, IReadOnlyList<(string Ticker, DateOnly Listed, string Why)> Unfilled);

// One of the AI stock picker's picks as its account trades it: the stock, its night, the range it buys in or none for one
// bought at the open, the trade its account took or none, and the side of its range it opened on with the session that was
// known, where its range did not buy it.
public sealed record PickerOrder(string Ticker, DateOnly Night, BuyRange? Range, PaperTrade? Trade, string? NotBought, DateOnly? NotBoughtOn);

// One index's orders on a night: its code and name, its live rules' orders at a price, each live account's, the picker's,
// and each stock's close on the night. A waiting order's distance is read in the typical daily move its plan was placed
// with, as its fill odds read the ledger's past orders.
public sealed record IndexOrders(
    string Index,
    string Name,
    IReadOnlyList<PlannedTradeRow> Planned,
    IReadOnlyList<AccountOrders> Accounts,
    IReadOnlyList<PickerOrder> Picker,
    IReadOnlyDictionary<string, decimal> Closes);

// One order the 4:30 pm check read as filled: the index, who placed it, the stock, the night it was placed, its buy price and
// the day's low the live quote read.
public sealed record FillCheckRow(string Index, string Who, string Ticker, DateOnly Listed, decimal Price, decimal Low);

// A night's order changes over the three indices, list by list.
public sealed record OrderSummary(DateOnly Night, IReadOnlyList<OrderChange> Changes, bool Checked)
{
    public IReadOnlyList<OrderChange> Of(string list) => [.. Changes.Where(change => change.List == list)];
}

// From the operator's order of 2026-11-28 each night ends with the order changes on the three indices for every live rule,
// book and the AI stock picker: the new orders to place, the fills with the exits to set, the sales, the orders that expired
// or were cancelled, and the waiting orders within one typical daily move of their buy price, each list saying none tonight
// where it holds none; and where the 4:30 pm check ran, each order it read otherwise than the night, by name. Worked from
// the paper account's own rows, one function for the page and the worker, its words holding tickers, prices and dates and
// never an account's size.
// see: Each night ends with the order changes for every live rule, book and the AI stock picker, on Paper trading and in one phone message
public static class OrderChanges
{
    public const string NewOrders = "new";

    public const string Filled = "filled";

    public const string Sold = "sold";

    public const string Ended = "ended";

    public const string CouldFill = "could-fill";

    public const string Differs = "differs";

    // The lists in the order the section and the message draw them, each with its heading.
    public static IReadOnlyList<(string List, string Heading)> Lists { get; } =
    [
        (NewOrders, "New orders: place these"),
        (Filled, "Filled today: set these exits"),
        (Sold, "Sold today"),
        (Ended, "Expired or cancelled: remove these"),
        (CouldFill, "Could fill tomorrow"),
    ];

    // The list of the orders the 4:30 pm check read otherwise than the night, drawn only where it ran.
    public const string DiffersHeading = "Where the 4:30 pm check read it otherwise";

    public const string NoneTonight = "None tonight";

    // How near a waiting order's buy price stands to the night's close to be listed as able to fill tomorrow: one typical
    // daily move.
    public const double CouldFillWithin = 1.0;

    public const string Picker = "the AI stock picker";

    public static OrderSummary Of(DateOnly night, DateOnly previous, IReadOnlyList<IndexOrders> indices, IReadOnlyList<FillCheckRow>? checks = null)
    {
        var changes = new List<OrderChange>();

        foreach (var index in indices)
        {
            changes.AddRange(OfIndex(night, previous, index));
        }

        if (checks is not null)
        {
            changes.AddRange(Compared(night, indices, checks));
        }

        return new OrderSummary(night, changes, checks is not null);
    }

    static IEnumerable<OrderChange> OfIndex(DateOnly night, DateOnly previous, IndexOrders index)
    {
        var buying = PlannedTrades.LiveBuying(index.Planned).ToArray();
        var planned = buying.Select(row => (row.Ticker, row.Listed)).ToHashSet();
        string WhoOf(PlannedTradeRow row) => SetupNames.WhoOf(row.Family, index.Name);

        // New orders: each live rule's order at a price placed tonight, each stock a live account kept tonight for the next
        // open, and each of the picker's picks of tonight with its range.
        foreach (var row in buying.Where(row => row.Listed == night && !row.Order.PlacesNoPrice() && PlannedTrades.Price(row) is not null).OrderBy(row => row.Place))
        {
            yield return new OrderChange(NewOrders, index.Index, WhoOf(row), row.Ticker,
                $"{row.Ticker}, {WhoOf(row)}: buy at ${Price(PlannedTrades.Price(row)!.Value)}, last day {Day(PlannedTrades.LastDay(row.Listed, row.Order.Window))}");
        }

        foreach (var account in index.Accounts)
        {
            foreach (var (ticker, _, _) in account.Unfilled.Where(one => one.Listed == night && one.Why == PaperAccount.Waiting && !planned.Contains((one.Ticker, one.Listed))))
            {
                yield return new OrderChange(NewOrders, index.Index, account.Who, ticker, $"{ticker}, {account.Who}: buy at the next open");
            }
        }

        var pickerWho = Picker + " on the " + index.Name;

        foreach (var pick in index.Picker.Where(pick => pick.Night == night))
        {
            var buy = pick.Range is { } range
                ? BuyRanges.PlanLines(range)[0].Replace("Buys ", "buy ", StringComparison.Ordinal) + ", at the open or on a dip back to its top within " + BuyRanges.OrderSessions.ToString(CultureInfo.InvariantCulture) + " days"
                : "buy at the next open";

            yield return new OrderChange(NewOrders, index.Index, pickerWho, pick.Ticker, $"{pick.Ticker}, {pickerWho}: {buy}");
        }

        // Fills: each trade an account's walk took that bought today, with the stop and the take-profit to set from its price.
        foreach (var account in index.Accounts)
        {
            foreach (var trade in account.Taken.Where(trade => trade.Bought == night))
            {
                yield return new OrderChange(Filled, index.Index, account.Who, trade.Ticker, $"{trade.Ticker}, {account.Who}: bought at ${Price(trade.Buy)}; {Exits(trade)}");
            }
        }

        foreach (var pick in index.Picker.Where(pick => pick.NotBought is null && pick.Trade is { } trade && trade.Bought == night))
        {
            yield return new OrderChange(Filled, index.Index, pickerWho, pick.Ticker, $"{pick.Ticker}, {pickerWho}: bought at ${Price(pick.Trade!.Buy)}; {Exits(pick.Trade)}");
        }

        // Sales: each trade sold today, at what price, why and what it came to.
        foreach (var account in index.Accounts)
        {
            foreach (var trade in account.Taken.Where(trade => trade.Ended == night && trade.Sale is not null))
            {
                yield return new OrderChange(Sold, index.Index, account.Who, trade.Ticker, SoldWords(trade, account.Who, account.Book));
            }
        }

        foreach (var pick in index.Picker.Where(pick => pick.NotBought is null && pick.Trade is { Sale: not null } trade && trade.Ended == night))
        {
            yield return new OrderChange(Sold, index.Index, pickerWho, pick.Ticker, SoldWords(pick.Trade!, pickerWho, false));
        }

        // Ended unfilled: each live rule's order at a price whose window passed or that was cancelled tonight, each stock kept for
        // the next open that its fill step bought nothing of today, and each pick its range did not buy.
        foreach (var row in buying.Where(row => row.Fill is null && row.NotFilled && (row.WhyOn ?? PlannedTrades.LastDay(row.Listed, row.Order.Window)) == night))
        {
            yield return new OrderChange(Ended, index.Index, WhoOf(row), row.Ticker, $"{row.Ticker}, {WhoOf(row)}: {row.Why ?? PlanOrderWords.NotFilledOf(row.Order)}");
        }

        foreach (var account in index.Accounts)
        {
            foreach (var (ticker, _, why) in account.Unfilled.Where(one => one.Listed == previous && one.Why != PaperAccount.Waiting && !planned.Contains((one.Ticker, one.Listed))))
            {
                yield return new OrderChange(Ended, index.Index, account.Who, ticker, $"{ticker}, {account.Who}: {why}");
            }
        }

        foreach (var pick in index.Picker.Where(pick => pick.NotBought is not null && pick.NotBoughtOn == night))
        {
            yield return new OrderChange(Ended, index.Index, pickerWho, pick.Ticker, $"{pick.Ticker}, {pickerWho}: {BuyRanges.NotBoughtWords(pick.NotBought!)}");
        }

        // Could fill tomorrow: each order still waiting tomorrow whose buy price stands within one typical daily move under the
        // night's close, nearest first.
        var near = new List<(double Moves, OrderChange Change)>();

        foreach (var row in buying.Where(row => PlannedTrades.IsWaiting(row) && !row.Order.PlacesNoPrice() && row.Listed <= night && PlannedTrades.LastDay(row.Listed, row.Order.Window) > night))
        {
            var typical = row.Order.TypicalDay;

            if (PlannedTrades.Price(row) is not { } price || !index.Closes.TryGetValue(row.Ticker, out var close) || !(typical > 0))
            {
                continue;
            }

            var moves = MovesAbove(close, price, typical);

            if (moves is >= 0 and <= CouldFillWithin)
            {
                var plural = Math.Round(moves, 1) == 1 ? string.Empty : "s";
                var distance = moves.ToString("0.0", CultureInfo.InvariantCulture);

                near.Add((moves, new OrderChange(CouldFill, index.Index, WhoOf(row), row.Ticker,
                    $"{row.Ticker}, {WhoOf(row)}: buy at ${Price(price)}, last close ${Price(close)}, {distance} typical daily move{plural} above")));
            }
        }

        foreach (var (_, change) in near.OrderBy(one => one.Moves))
        {
            yield return change;
        }
    }

    // How far a close stands above a buy price, in the stock's typical daily moves.
    public static double MovesAbove(decimal close, decimal price, double typical) => Statistic.FromPrice(close - price) / typical;

    // Where the 4:30 pm check read an order otherwise than the night: one it read filled that the night's bars did not fill,
    // and one the night filled that it did not read, each by name.
    static IEnumerable<OrderChange> Compared(DateOnly night, IReadOnlyList<IndexOrders> indices, IReadOnlyList<FillCheckRow> checks)
    {
        foreach (var index in indices)
        {
            var buying = PlannedTrades.LiveBuying(index.Planned).ToArray();
            var read = checks.Where(check => check.Index == index.Index).ToArray();

            foreach (var check in read)
            {
                if (!buying.Any(row => row.Ticker == check.Ticker && row.Listed == check.Listed && row.FilledOn == night))
                {
                    yield return new OrderChange(Differs, index.Index, check.Who, check.Ticker,
                        $"{check.Ticker}, {check.Who}: the 4:30 pm check read its low at ${Price(check.Low)}, at or under its buy at ${Price(check.Price)}, and tonight's bars did not fill it");
                }
            }

            foreach (var row in buying.Where(row => row.FilledOn == night && !read.Any(check => check.Ticker == row.Ticker && check.Listed == row.Listed)))
            {
                var who = SetupNames.WhoOf(row.Family, index.Name);

                yield return new OrderChange(Differs, index.Index, who, row.Ticker, $"{row.Ticker}, {who}: filled tonight at ${Price(row.Fill!.Value)}, which the 4:30 pm check did not read");
            }
        }
    }

    // A trade with no take-profit says how it sells instead: its stop raised as the price climbs, its own exit signal, or its
    // time limit.
    public static string Exits(PaperTrade trade) =>
        (trade.Stop, trade.Target) switch
        {
            ({ } stop, { } target) => $"set the stop at ${Price(stop)} and the take-profit at ${Price(target)}",
            ({ } stop, null) when trade.Trail is not null => $"set the stop at ${Price(stop)}, raised as the price climbs and never lowered, with no take-profit",
            ({ } stop, null) when trade.Exit is not null => $"set the stop at ${Price(stop)}; it sells on its own exit signal, with no take-profit",
            ({ } stop, null) => $"set the stop at ${Price(stop)}; it sells by its time limit, with no take-profit",
            (null, { } target) => $"set the take-profit at ${Price(target)}; it places no stop",
            _ => "it places no stop and no take-profit; its book sells it at a reshuffle",
        };

    // Why a sale came: its stop where it sold at or under it, its take-profit where at or over it, a book's own reason, and
    // its time limit otherwise; with what it came to in per cent of the price paid.
    static string SoldWords(PaperTrade trade, string who, bool book)
    {
        var sale = trade.Sale!.Value;
        var why = trade.Stop is { } stop && sale <= stop ? "its stop"
            : trade.Target is { } target && sale >= target ? "its take-profit"
            : book ? trade.Outcome is { Length: > 0 } reason ? reason : "its book's reshuffle"
            : "its time limit";
        var made = trade.Buy > 0 ? Statistic.FromRatio((sale / trade.Buy) - 1m) * 100 : 0;

        return FormattableString.Invariant($"{trade.Ticker}, {who}: sold at ${Price(sale)} on {why}, {made:+0.0;-0.0;0.0}%");
    }

    // Every list in its order with its lines, "None tonight" under one holding none, and the check's list where it ran.
    public static IReadOnlyList<(string Heading, IReadOnlyList<string> Lines)> Sections(OrderSummary summary)
    {
        var sections = Lists.Select(list => (list.Heading, Lines: (IReadOnlyList<string>)[.. summary.Of(list.List).Select(change => change.Words)])).ToList();

        if (summary.Checked)
        {
            sections.Add((DiffersHeading, [.. summary.Of(Differs).Select(change => change.Words)]));
        }

        return [.. sections.Select(one => (one.Heading, one.Lines.Count == 0 ? (IReadOnlyList<string>)[NoneTonight] : one.Lines))];
    }

    // The one phone message: the night, then each list under its heading.
    public static string Message(OrderSummary summary)
    {
        var text = new StringBuilder();

        text.Append("Order changes, ").Append(Day(summary.Night)).Append('\n');

        foreach (var (heading, lines) in Sections(summary))
        {
            text.Append('\n').Append(heading).Append('\n');

            foreach (var line in lines)
            {
                text.Append(line == NoneTonight ? line : "- " + line).Append('\n');
            }
        }

        return text.ToString();
    }

    public static string Price(decimal price) => price.ToString("#,##0.00", CultureInfo.InvariantCulture);

    public static string Day(DateOnly day) => day.ToString("ddd d MMM", CultureInfo.InvariantCulture);
}
