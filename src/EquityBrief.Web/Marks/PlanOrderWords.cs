using System.Globalization;

namespace EquityBrief.Web.Marks;

// An order from an earlier night still waiting for its price: its family, stock, the night that listed it, its price, none
// where its levels placed none, and the last session it waits; from the operator's order of 2026-11-14 its company and the
// night's stored close.
public sealed record WaitingOrderView(string Family, string Ticker, DateOnly Listed, decimal? Price, DateOnly LastDay, string? Company = null, decimal? LastClose = null)
{
    // From the operator's answers of 2026-11-16 the stop its plan places from its price and the sale it was registered with,
    // which its card draws as a pick's card does.
    public decimal? Stop { get; init; }

    public PlanSaleView? Sale { get; init; }

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
    public static DateOnly From => EquityBrief.Core.Candidates.RuleFreezeDates.From;

    public static DateOnly Until => EquityBrief.Core.Candidates.RuleFreezeDates.Until;

    public static string Beside(DateOnly since) =>
        EquityBrief.Core.Candidates.RuleFreezeDates.Holds(since) ? "; rules frozen until " + Until.ToString("d MMM yyyy", CultureInfo.InvariantCulture) : string.Empty;
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
    // see: A live rule's trade under a plan is drawn on every page as its plan made it, and its waiting orders are cards of their own nearest their buy price first
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

            body.Append(FormattableString.Invariant($"<li data-ticker=\"{Escaped(order.Ticker)}\" data-family=\"{Escaped(order.Family)}\" data-listed=\"{DayOf(order.Listed)}\" data-price=\"{(order.Price is { } price ? price.ToString("0.00", CultureInfo.InvariantCulture) : "none")}\" data-close=\"{(order.LastClose is { } close ? close.ToString("0.00", CultureInfo.InvariantCulture) : "none")}\" data-last-day=\"{DayOf(order.LastDay)}\"><a class=\"name-link\" href=\"#/chart/{Uri.EscapeDataString(order.Ticker)}\">{Escaped(order.Ticker)}</a>{Escaped(rest)}{(RevisionTags.For(order.Family, order.Listed, order: true) is { Length: > 0 } tag ? FormattableString.Invariant($" <span class=\"wo-revision\">{tag} {Escaped(RevisionTags.BuysInto(order.Family, order.Listed))}</span>") : string.Empty)}</li>"));
        }

        body.Append("</ol></div>");

        return body.ToString();
    }
}
