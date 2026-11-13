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

    // The group the counts name the orders from earlier nights still waiting in.
    public const string StillWaiting = "still waiting for their price";

    // Past picks' count headings for the three groups.
    public const string NewTonightHeading = "New tonight, waiting for their price";

    public const string StillWaitingHeading = "From earlier nights, still waiting for their price";

    public const string NotFilledHeading = "Not filled, no trade";

    public static string Waiting(decimal? price, DateOnly lastDay) =>
        price is { } at
            ? FormattableString.Invariant($"{WaitingOpening}, {at.ToString("0.00", CultureInfo.InvariantCulture)}, until {lastDay.ToString("d MMM yyyy", CultureInfo.InvariantCulture)}")
            : FormattableString.Invariant($"{WaitingOpening}, its levels placing none, until {lastDay.ToString("d MMM yyyy", CultureInfo.InvariantCulture)}");

    public static bool IsWaiting(string? why) => why is not null && why.StartsWith(WaitingOpening, StringComparison.Ordinal);

    public static bool IsNotFilled(string? why) => string.Equals(why, NotFilled, StringComparison.Ordinal);

    // A count's words for the orders waiting, tonight's new and earlier nights' each named as its group, each led by a comma
    // and none where none waits.
    public static string Counts(int newTonight, int stillWaiting) =>
        (newTonight > 0 ? FormattableString.Invariant($", {newTonight} new tonight waiting for {(newTonight == 1 ? "its" : "their")} price") : string.Empty)
        + (stillWaiting > 0 ? FormattableString.Invariant($", {stillWaiting} from earlier nights {StillWaiting}") : string.Empty);
}

// An order from an earlier night still waiting for its price: its family, stock, the night that listed it, its price, none
// where its levels placed none, and the last session it waits.
public sealed record WaitingOrderView(string Family, string Ticker, DateOnly Listed, decimal? Price, DateOnly LastDay)
{
    public string Words => PlanOrderWords.Waiting(Price, LastDay);
}

public sealed partial class MarkRenderer
{
    // The orders from earlier nights still waiting for their price as a group of their own, each numbered with its stock, its
    // setup, the night that listed it, its price and its last session; nothing where none waits.
    // see: A live rule's trade under a plan is drawn on every page as its plan made it, and its waiting orders are a group of their own
    public static string StillWaitingOrders(IReadOnlyList<WaitingOrderView> orders, string index)
    {
        if (orders.Count == 0)
        {
            return string.Empty;
        }

        var body = new System.Text.StringBuilder();

        body.Append(FormattableString.Invariant($"<div class=\"waiting-orders\" data-group=\"still-waiting\" data-still-waiting=\"{orders.Count}\"><p class=\"list-count\">{orders.Count} {(orders.Count == 1 ? "order" : "orders")} from earlier nights {(orders.Count == 1 ? "is still waiting for its price" : "are " + PlanOrderWords.StillWaiting)}, in none of the trades until {(orders.Count == 1 ? "it fills" : "they fill")}:</p><ol>"));

        foreach (var order in orders)
        {
            var setup = EquityBrief.Core.Families.SetupFamilies.Named(order.Family)?.Label ?? order.Family;

            body.Append(FormattableString.Invariant($"<li data-ticker=\"{Escaped(order.Ticker)}\" data-family=\"{Escaped(order.Family)}\" data-listed=\"{DayOf(order.Listed)}\" data-price=\"{(order.Price is { } price ? price.ToString("0.00", CultureInfo.InvariantCulture) : "none")}\" data-last-day=\"{DayOf(order.LastDay)}\"><a class=\"name-link\" href=\"#/chart/{Uri.EscapeDataString(order.Ticker)}\">{Escaped(order.Ticker)}</a> · {Escaped(setup)} · {Escaped(index)}, listed {Escaped(order.Listed.ToString("d MMM", CultureInfo.InvariantCulture))}: {Escaped(order.Words)}</li>"));
        }

        body.Append("</ol></div>");

        return body.ToString();
    }
}
