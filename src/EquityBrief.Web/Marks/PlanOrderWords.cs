using System.Globalization;

namespace EquityBrief.Web.Marks;

// From the 23.10 correction of 2026-11-13 the words a live rule's order under a plan buying at a price is drawn in while it
// waits for its price, with that price and the last session it waits, and once its window passed unfilled; one place, so
// every page says the same and the counts read them. From the operator's order of 2026-11-14 in their words: an order buys if
// the price falls to a set level, and is cancelled if not filled by its last day.
// see: A live rule's trade under a plan is drawn on every page as its plan made it, and its waiting orders are a group of their own
public static class PlanOrderWords
{
    public const string WaitingOpening = "buys if it falls to";

    public const string NotFilled = "not filled, no trade";

    // The group the counts name the orders from earlier nights still waiting in.
    public const string StillWaiting = "waiting to buy";

    // Past picks' count headings for the three groups.
    public const string NewTonightHeading = "New tonight, waiting to buy";

    public const string StillWaitingHeading = "From earlier nights, waiting to buy";

    public const string NotFilledHeading = "Not filled, no trade";

    static string Day(DateOnly day) => day.ToString("d MMM yyyy", CultureInfo.InvariantCulture);

    public static string Waiting(decimal? price, DateOnly lastDay) =>
        price is { } at
            ? FormattableString.Invariant($"{WaitingOpening} ${at.ToString("0.00", CultureInfo.InvariantCulture)}; cancelled if not filled by {Day(lastDay)}")
            : FormattableString.Invariant($"{WaitingOpening} a level its plan could not place tonight, so it will not fill; cancelled by {Day(lastDay)}");

    public static bool IsWaiting(string? why) => why is not null && why.StartsWith(WaitingOpening, StringComparison.Ordinal);

    // On the operator's ruling of 2026-11-14 an order its levels placed no price for is not placed, and says so in their words.
    public const string NoPrice = "No price to buy at from its levels tonight, so no order was placed.";

    public static string NotFilledOf(EquityBrief.Core.Loop.PlanOrder order) => order.PlacesNoPrice() ? NoPrice : NotFilled;

    public static bool IsNotFilled(string? why) => string.Equals(why, NotFilled, StringComparison.Ordinal) || string.Equals(why, NoPrice, StringComparison.Ordinal);

    // A count's words for the orders waiting, tonight's new and earlier nights' each named as its group, each led by a comma
    // and none where none waits.
    public static string Counts(int newTonight, int stillWaiting) =>
        (newTonight > 0 ? FormattableString.Invariant($", {newTonight} new tonight {StillWaiting}") : string.Empty)
        + (stillWaiting > 0 ? FormattableString.Invariant($", {stillWaiting} from earlier nights {StillWaiting}") : string.Empty);

    // From the operator's order of 2026-11-14 the group's heading, in their words.
    public static string Heading(int orders) =>
        FormattableString.Invariant($"Waiting to buy: {orders} {(orders == 1 ? "order that buys" : "orders that buy")} only if the price falls to a set level. If it does not get there by the last day, nothing is bought.");

    // How far tonight's close stands above an order's price, over the close, none where either is not read or the close is none.
    public static decimal? Gap(decimal? price, decimal? close) =>
        price is { } at && close is { } last && last > 0 ? (last - at) / last : null;
}

// An order from an earlier night still waiting for its price: its family, stock, the night that listed it, its price, none
// where its levels placed none, and the last session it waits; from the operator's order of 2026-11-14 its company and the
// night's stored close.
public sealed record WaitingOrderView(string Family, string Ticker, DateOnly Listed, decimal? Price, DateOnly LastDay, string? Company = null, decimal? LastClose = null)
{
    public string Words => PlanOrderWords.Waiting(Price, LastDay);

    // The row in the operator's words: what it buys at, the last close and how far above, and when it was placed by which
    // setup and when it is cancelled.
    public string Row(string setup)
    {
        var name = Company is { Length: > 0 } company ? Ticker + ", " + CompanyWords.Short(company) : Ticker;
        var buys = Price is { } price
            ? FormattableString.Invariant($"buys if it falls to ${price.ToString("0.00", CultureInfo.InvariantCulture)}.")
            : "buys if it falls to a set level, which its plan could not place tonight.";
        var close = LastClose is { } last
            ? FormattableString.Invariant($" Last close ${last.ToString("0.00", CultureInfo.InvariantCulture)}") + (PlanOrderWords.Gap(Price, last) is { } gap
                ? FormattableString.Invariant($", {Math.Abs(gap * 100).ToString("0.0", CultureInfo.InvariantCulture)}% {(gap >= 0 ? "above" : "below")}.")
                : ".")
            : string.Empty;

        return FormattableString.Invariant($"{name}: {buys}{close} Placed {Listed.ToString("d MMM", CultureInfo.InvariantCulture)} by {setup.ToLowerInvariant()}; cancelled if not filled by {LastDay.ToString("d MMM", CultureInfo.InvariantCulture)}.");
    }
}

// On the operator's ruling of 2026-11-14 the rules the second restart sets are frozen until the monthly run of 6 Feb 2027: beside
// a live version's first session from that restart on, and before the freeze ends, the page says so.
public static class RuleFreeze
{
    public static DateOnly From { get; } = new(2026, 11, 15);

    public static DateOnly Until { get; } = new(2027, 2, 6);

    public static string Beside(DateOnly since) =>
        since >= From && since < Until ? "; rules frozen until " + Until.ToString("d MMM yyyy", CultureInfo.InvariantCulture) : string.Empty;
}

// From the operator's order of 2026-11-14 a company's name as a sentence reads it, its trailing legal words left off.
public static class CompanyWords
{
    static readonly string[] Legal = ["Inc.", "Inc", "Incorporated", "Corp.", "Corp", "Corporation", "Co.", "Co", "Company", "Ltd.", "Ltd", "Limited", "plc", "PLC", "LLC", "L.P.", "LP", "N.V.", "NV", "S.A.", "SA", "AG", "SE", "Holdings", "Holding"];

    public static string Short(string company)
    {
        var words = company.Trim().TrimEnd(',', '.').Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();

        while (words.Count > 1 && Legal.Contains(words[^1].TrimEnd(','), StringComparer.Ordinal))
        {
            words.RemoveAt(words.Count - 1);
        }

        return string.Join(' ', words).TrimEnd(',');
    }
}

public sealed partial class MarkRenderer
{
    // The orders from earlier nights still waiting for their price as a group of their own, under the operator's heading of
    // 2026-11-14, each numbered with its stock and company, its price, the night's close and how far above it, the setup and
    // night that placed it and its last day; nothing where none waits.
    // see: A live rule's trade under a plan is drawn on every page as its plan made it, and its waiting orders are a group of their own
    public static string StillWaitingOrders(IReadOnlyList<WaitingOrderView> orders, string index)
    {
        if (orders.Count == 0)
        {
            return string.Empty;
        }

        var body = new System.Text.StringBuilder();

        body.Append(FormattableString.Invariant($"<div class=\"waiting-orders\" data-group=\"still-waiting\" data-still-waiting=\"{orders.Count}\" data-index=\"{Escaped(index)}\"><p class=\"list-count\">{Escaped(PlanOrderWords.Heading(orders.Count))}</p><ol>"));

        foreach (var order in orders)
        {
            var setup = EquityBrief.Core.Families.SetupFamilies.Named(order.Family)?.Label ?? order.Family;
            var row = order.Row(setup);
            var rest = row[order.Ticker.Length..];

            body.Append(FormattableString.Invariant($"<li data-ticker=\"{Escaped(order.Ticker)}\" data-family=\"{Escaped(order.Family)}\" data-listed=\"{DayOf(order.Listed)}\" data-price=\"{(order.Price is { } price ? price.ToString("0.00", CultureInfo.InvariantCulture) : "none")}\" data-close=\"{(order.LastClose is { } close ? close.ToString("0.00", CultureInfo.InvariantCulture) : "none")}\" data-last-day=\"{DayOf(order.LastDay)}\"><a class=\"name-link\" href=\"#/chart/{Uri.EscapeDataString(order.Ticker)}\">{Escaped(order.Ticker)}</a>{Escaped(rest)}</li>"));
        }

        body.Append("</ol></div>");

        return body.ToString();
    }
}
