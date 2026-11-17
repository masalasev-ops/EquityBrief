using System.Globalization;
using System.Text;

namespace EquityBrief.Web.Marks;

// On the operator's answers of 2026-11-16, item 1: the orders earlier nights placed still waiting for their price, drawn on
// Home and Tonight as one row of cards nearest its buy price first, each card carrying every fact the list carried and the
// rows of its plan; a strip of chips above them, one an order, and the arrows and the counter beside the heading; three
// orders or fewer drawn as the ordinary grid with no arrows and no chips. Past picks and a stock's own page keep their lines.
// see: A live rule's trade under a plan is drawn on every page as its plan made it, and its waiting orders are cards of their own nearest their buy price first
public sealed partial class MarkRenderer
{
    // More orders than this draw the row that scrolls, with its arrows and its chips; this many or fewer the plain grid.
    public const int WaitingGridAtMost = 3;

    // A chip whose order stands this share or less above its buy price is filled in the waiting colour.
    public const decimal WaitingNear = 0.03m;

    // The cards a wide screen shows at once, the counter's first reading before the page measures what is in view.
    public const int WaitingInView = 4;

    public const string WaitingLine = "Placed on earlier nights. Each buys only if its price falls to the buy price. If it does not get there by its last day, nothing is bought. Nearest to its buy price first.";

    public const string WaitingState = "Waiting to buy";

    // What a waiting order's take-profit row says before the night places its price.
    public const string TakeProfitNextNight = "added at the next night";

    public static string WaitingHeading(int orders) => FormattableString.Invariant($"Waiting to buy ({orders})");

    // The orders nearest their buy price first, by how far the last close stands above it; an order with no price or no close
    // after every one with both, then by stock and the night that placed it.
    public static IReadOnlyList<WaitingOrderView> NearestFirst(IReadOnlyList<WaitingOrderView> orders) =>
    [
        .. orders
            .OrderBy(order => PlanOrderWords.Gap(order.Price, order.LastClose) is null ? 1 : 0)
            .ThenBy(order => PlanOrderWords.Gap(order.Price, order.LastClose) ?? 0m)
            .ThenBy(order => order.Ticker, StringComparer.Ordinal)
            .ThenBy(order => order.Listed),
    ];

    // The waiting orders' group on Home and Tonight; nothing where none waits.
    public static string WaitingCarousel(IReadOnlyList<WaitingOrderView> orders, string index, DateOnly? night)
    {
        if (orders.Count == 0)
        {
            return string.Empty;
        }

        var sorted = NearestFirst(orders);
        var rolling = sorted.Count > WaitingGridAtMost;
        var widest = sorted.Select(order => PlanOrderWords.Gap(order.Price, order.LastClose) ?? 0m).DefaultIfEmpty(0m).Max();
        var body = new StringBuilder();

        body.Append(Invariant, $"<div class=\"waiting-orders wo\" data-group=\"still-waiting\" data-still-waiting=\"{sorted.Count}\" data-index=\"{Escaped(index)}\" data-carousel=\"{(rolling ? "true" : "false")}\">");
        body.Append(Invariant, $"<div class=\"wo-head\"><p class=\"list-count wo-title\">{Escaped(WaitingHeading(sorted.Count))}</p>");

        if (rolling)
        {
            body.Append(Invariant, $"<div class=\"wo-nav\"><span class=\"wo-count\" aria-live=\"polite\" data-wo-count>1 to {Math.Min(WaitingInView, sorted.Count)} of {sorted.Count}</span><button type=\"button\" class=\"wo-arrow\" data-wo-step=\"-1\" aria-label=\"Show the orders before these\" disabled>&#8249;</button><button type=\"button\" class=\"wo-arrow\" data-wo-step=\"1\" aria-label=\"Show the orders after these\">&#8250;</button></div>");
        }

        body.Append("</div>");
        body.Append(Invariant, $"<p class=\"wo-said\">{Escaped(WaitingLine)}</p>");

        if (rolling)
        {
            body.Append("<div class=\"wo-chips\" role=\"list\">");

            foreach (var (order, at) in sorted.Select((order, at) => (order, at)))
            {
                var gap = PlanOrderWords.Gap(order.Price, order.LastClose);
                var near = gap is { } apart && apart <= WaitingNear;

                body.Append(Invariant, $"<button type=\"button\" role=\"listitem\" class=\"wo-chip{(near ? " near" : string.Empty)}{(at < WaitingInView ? " in-view" : string.Empty)}\" data-at=\"{at}\" data-ticker=\"{Escaped(order.Ticker)}\" data-near=\"{(near ? "true" : "false")}\" aria-label=\"{Escaped(order.Ticker)}, {Escaped(ToGo(gap))} to its buy price\">{Escaped(order.Ticker)}<span>{Escaped(Share(gap))}</span></button>");
            }

            body.Append("</div>");
        }

        body.Append(rolling
            ? "<div class=\"wo-track\" tabindex=\"0\" role=\"group\" aria-label=\"Orders waiting to buy, nearest to its buy price first\" data-wo-track>"
            : "<div class=\"wo-grid\">");

        foreach (var order in sorted)
        {
            body.Append(WaitingCard(order, night, widest));
        }

        return body.Append("</div></div>").ToString();
    }

    // One waiting order's card: its stock and company, its setup's badge and that it waits, the line saying what it buys at,
    // the bar to its buy price, the rows of its plan, when and by which setup it was placed and when it is cancelled, and the
    // way to its plan.
    static string WaitingCard(WaitingOrderView order, DateOnly? night, decimal widest)
    {
        var card = new StringBuilder();
        var family = Escaped(order.Family);
        var setup = EquityBrief.Core.Families.SetupFamilies.Named(order.Family)?.Label ?? order.Family;
        var gap = PlanOrderWords.Gap(order.Price, order.LastClose);
        var price = order.Price is { } at ? at.ToString("0.00", CultureInfo.InvariantCulture) : "none";
        var close = order.LastClose is { } last ? last.ToString("0.00", CultureInfo.InvariantCulture) : "none";

        card.Append(Invariant, $"<article class=\"wo-card fam-edge\" data-family=\"{family}\" data-ticker=\"{Escaped(order.Ticker)}\" data-listed=\"{DayOf(order.Listed)}\" data-price=\"{price}\" data-close=\"{close}\" data-last-day=\"{DayOf(order.LastDay)}\" data-gap=\"{(gap is { } apart ? apart.ToString("0.0000", CultureInfo.InvariantCulture) : "none")}\">");
        card.Append(Invariant, $"<div class=\"wo-card-h\"><div><a class=\"home-ticker name-link\" href=\"#/chart/{Uri.EscapeDataString(order.Ticker)}\">{Escaped(order.Ticker)}</a>{(order.Company is { Length: > 0 } company ? Formatted($"<div class=\"home-co\">{Escaped(company)}</div>") : string.Empty)}</div>");
        card.Append(Invariant, $"<div class=\"wo-badge\"><span class=\"fam-tag\" data-family=\"{family}\"><span class=\"fam-swatch\" data-family=\"{family}\"></span>{Escaped(setup)}</span><span class=\"wo-state\">{WaitingState}</span></div></div>");
        card.Append(Invariant, $"<p class=\"wo-cond\">{Escaped(WaitingCondition(order))}</p>");

        if (order.Price is { } buy)
        {
            var filled = widest > 0m && gap is { } share ? Math.Clamp(share / widest, 0.02m, 1m) * 100m : 2m;

            card.Append(Invariant, $"<div class=\"wo-bar\" role=\"img\" aria-label=\"{Escaped(ToGo(gap))} to its buy price of {Escaped(Dollars(buy))}\"><div class=\"wo-bar-track\"><div class=\"wo-fill\" style=\"width:{filled.ToString("0.#", CultureInfo.InvariantCulture)}%\"></div></div><p class=\"wo-bar-labels\"><span>Buy price {Escaped(Dollars(buy))}</span><span>{Escaped(ToGo(gap))}</span></p></div>");
        }

        card.Append(order.Sale is { } parted ? SalePartsMark(parted) : string.Empty);
        card.Append("<div class=\"home-prices wo-plan\">");

        foreach (var (key, label, value, note) in WaitingPlanRows(order))
        {
            card.Append(Invariant, $"<div data-plan=\"{key}\"><span>{Escaped(label)}</span><b class=\"{(key == "stop" ? "stop" : key == "buy" ? "buy" : "aim")}\">{Escaped(value)}</b>{(note is { } said ? Formatted($"<span class=\"sale-note\">{Escaped(said)}</span>") : string.Empty)}</div>");
        }

        card.Append("</div>");
        card.Append(Invariant, $"<p class=\"wo-placed\">{Escaped(PlacedLine(order, setup, night))}</p>");
        card.Append(Invariant, $"<a class=\"home-plan\" href=\"#/name/{Uri.EscapeDataString(order.Ticker)}\">See the plan</a>");

        return card.Append("</article>").ToString();
    }

    // The highlighted line in the operator's words: "Buys only if it falls to $x. Last close $x, x% above."
    public static string WaitingCondition(WaitingOrderView order)
    {
        var buys = order.Price is { } price
            ? "Buys only if it falls to " + Dollars(price) + "."
            : "Buys only if it falls to a set level, which its plan could not place tonight.";

        return order.LastClose is { } last
            ? buys + " Last close " + Dollars(last) + (PlanOrderWords.Gap(order.Price, last) is { } gap ? FormattableString.Invariant($", {Math.Abs(gap * 100):0.0}% {(gap >= 0 ? "above" : "below")}.") : ".")
            : buys;
    }

    // "Placed <date> by <setup>. Cancelled if not filled by <date>, x trading days left."
    public static string PlacedLine(WaitingOrderView order, string setup, DateOnly? night)
    {
        var left = night is { } on ? PlanOrderWords.SessionsLeft(on, order.LastDay) : (int?)null;

        return FormattableString.Invariant($"Placed {order.Listed:d MMM} by {setup.ToLowerInvariant()}. Cancelled if not filled by {order.LastDay:d MMM}")
            + (left is { } days ? FormattableString.Invariant($", {days} trading {(days == 1 ? "day" : "days")} left.") : ".");
    }

    // The rows of a waiting order's plan as a pick's card draws them: what it buys at, the close that sells it at a loss, and
    // its sale, the take-profit or the half exit's two rows and its time limit; a take-profit row in every card, saying it is
    // added at the next night where the plan holds none yet.
    public static IReadOnlyList<(string Key, string Label, string Value, string? Note)> WaitingPlanRows(WaitingOrderView order)
    {
        var rows = new List<(string Key, string Label, string Value, string? Note)>
        {
            ("buy", "Buy if it dips to", order.Price is { } price ? Dollars(price) : "no price placed tonight", null),
            ("stop", "Sell if it closes below", order.Stop is { } stop ? Dollars(stop) : "placed from its buy price", null),
        };
        var sale = order.Sale is { } sold ? PlanSaleLines(sold) : [];

        foreach (var line in sale)
        {
            rows.Add((line.Key, line.Label, line.Value, line.Note));
        }

        if (!sale.Any(line => line.Key == "target"))
        {
            var limit = rows.FindIndex(row => row.Key == "limit");

            rows.Insert(limit < 0 ? rows.Count : limit, ("target", TakeProfitView.Label, TakeProfitNextNight, null));
        }

        return rows;
    }

    // The cards of a page's waiting orders lacking a buy price, a stop or a take-profit row, by stock.
    public static IReadOnlyList<string> WaitingCardsLackingARow(string page) =>
    [
        .. System.Text.RegularExpressions.Regex.Matches(page, "<article class=\"wo-card[^\"]*\"[^>]*data-ticker=\"([^\"]+)\".*?</article>", System.Text.RegularExpressions.RegexOptions.Singleline)
            .Where(card => !card.Value.Contains("data-plan=\"buy\"", StringComparison.Ordinal)
                || !card.Value.Contains("data-plan=\"stop\"", StringComparison.Ordinal)
                || !card.Value.Contains("data-plan=\"target\"", StringComparison.Ordinal))
            .Select(card => card.Groups[1].Value),
    ];

    static string Dollars(decimal price) => "$" + price.ToString("#,##0.00", CultureInfo.InvariantCulture);

    static string Share(decimal? gap) => gap is { } apart ? FormattableString.Invariant($"{Math.Max(apart, 0m) * 100:0.0}%") : "no price";

    static string ToGo(decimal? gap) => gap is { } apart ? FormattableString.Invariant($"{Math.Max(apart, 0m) * 100:0.0}% to go") : "no price to go to";

    // From the operator's answers of 2026-11-16, item 1b: the box on a new pick that waits for a price, in their words.
    public static string ZoneBox(ZoneBuy zone, DateOnly night)
    {
        var last = PlanOrderWords.LastDay(night, zone.Within);
        var above = zone.Close > 0 ? (zone.Close - zone.Price) / zone.Close : 0m;

        return FormattableString.Invariant($"Not bought unless the price falls to {Dollars(zone.Price)} by {last:d MMM yyyy}. Tonight's close was {Dollars(zone.Close)}, {Math.Abs(above * 100):0.0}% {(above >= 0 ? "above" : "below")}. If it rises from here, there is no trade.");
    }
}
