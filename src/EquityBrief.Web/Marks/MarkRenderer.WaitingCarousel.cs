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

    // The heading of tonight's checklist on the card of a stock met the rules again while its order waits.
    public const string AgainHeading = "Why it qualified again";

    // From the operator's correction of 2026-11-25, item 5, in their words: "Its order of 23 Nov at $320.04 stands; no second order
    // was placed. Tonight's rules would have placed it at $318.13.", the second sentence none where tonight's plan buys at the
    // open and so places no price.
    // see: A stock its setup's rules met again while its order waits stands among tonight's ideas, its waiting card keeping only its mark
    public static string AgainLine(WaitingOrderView order, DecisionCardView again) =>
        FormattableString.Invariant($"Its order of {order.Listed:d MMM}") + (order.Price is { } price ? " at " + Dollars(price) : string.Empty)
        + " stands; no second order was placed."
        + (again.Zone is { NoBuy: false } zone ? " Tonight's rules would have placed it at " + Dollars(zone.Price) + "." : string.Empty);

    // Tonight's checks folded under one line, "Why it qualified again on 24 Nov (5 checks)", the count read off the card's
    // checklist; none where the card holds no checklist.
    public static string AgainFold(DecisionCardView again)
    {
        var checks = AgainChecks(again);
        var heading = AgainHeading + FormattableString.Invariant($" on {again.Night:d MMM}");
        var drawn = WhyItQualified(again, heading).Replace("<h5>" + Escaped(heading) + "</h5>", string.Empty, StringComparison.Ordinal);

        return drawn.Length == 0
            ? string.Empty
            : Formatted($"<details class=\"again-fold\" data-checks=\"{checks}\"><summary>{Escaped(AgainHeading)} on {again.Night:d MMM} ({checks} {(checks == 1 ? "check" : "checks")})</summary>{drawn}</details>");
    }

    // The checks a card's setup passed, read from its first line as its checklist draws them.
    public static int AgainChecks(DecisionCardView card) =>
        card.Lines.FirstOrDefault(line => line.Name == EquityBrief.Core.Cards.CardLines.TrendName) is { } passed ? PassedSteps(card.Family, passed.Words).Count : 0;

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

    // The waiting orders' group on Home and Tonight; nothing where none waits. From the operator's correction of 2026-11-23 an
    // order whose stock its setup's rules met again tonight, given as that night's card, carries the mark on its chip and its
    // card, the line saying its order stands and the price tonight's rules would have placed, and tonight's checklist.
    // From the operator's correction of 2026-11-24 the setups are named as the page given words them, Home's own names on Home,
    // each setup's label where none is given.
    // Drawn plain for the read-only copy, a card's checks row stands empty, since the checks of the night that placed it are
    // the method.
    // see: The read-only copy is published to Cloudflare Pages with no control and no part of the method, and its upload refuses a page that leaks one
    public static string WaitingCarousel(IReadOnlyList<WaitingOrderView> orders, string index, DateOnly? night, IReadOnlyList<LeftOrderView>? left = null, IReadOnlyList<DecisionCardView>? again = null, Func<string, string>? setupName = null, bool plain = false)
    {
        if (orders.Count == 0)
        {
            return LeftNotes(left);
        }

        return Carousel(orders, index, night, again ?? [], setupName ?? SetupLabel, plain) + LeftNotes(left);
    }

    static string SetupLabel(string family) => EquityBrief.Core.Families.SetupFamilies.Named(family)?.Label ?? family;

    // The rows every waiting card draws, in this order, so the cards stand one height with their rows lined up.
    public static readonly string[] WaitingCardRows = ["ticker", "company", "setup", "revision", "buy", "bar", "plan", "placed", "checks", "see-plan"];

    // The card of tonight's whose stock met its setup's rules again while this order waits: the same stock, the setup that
    // placed the order and the night it placed it.
    public static DecisionCardView? AgainOf(WaitingOrderView order, IReadOnlyList<DecisionCardView> again) =>
        again.FirstOrDefault(card => card.Ticker == order.Ticker && card.HeldAgain is { Bought: false } held && held.Family == order.Family && held.Night == order.Listed);

    public const string LeftHeading = "Bought, sold or left the waiting list";

    // From the operator's correction of 2026-11-25, in their words: "In the last 5 trading days: 2 bought, 31 sold, 2
    // cancelled, 1 not placed. See them on Paper trading", each kind counted off the rows Paper trading's tab draws, a kind
    // with none left out, and nothing where none moved.
    // see: Home counts the last five trading days' buys and sales and orders left unbought in one line, and Paper trading lists them in a tab
    public static string RecentLine(IReadOnlyList<LeftOrderView>? left, string href)
    {
        if (left is not { Count: > 0 })
        {
            return string.Empty;
        }

        var counts = LeftOrderView.Kinds.ToDictionary(kind => kind, kind => left.Count(row => row.Kind == kind), StringComparer.Ordinal);
        var said = string.Join(", ", LeftOrderView.Kinds.Where(kind => counts[kind] > 0).Select(kind => FormattableString.Invariant($"{counts[kind]} {kind}")));

        return FormattableString.Invariant($"<p class=\"recent-line\" data-bought=\"{counts[LeftOrderView.BoughtKind]}\" data-sold=\"{counts[LeftOrderView.SoldKind]}\" data-cancelled=\"{counts[LeftOrderView.CancelledKind]}\" data-not-placed=\"{counts[LeftOrderView.NotPlacedKind]}\">In the last {LeftOrderView.Sessions} trading days: {said}. <a href=\"{Escaped(href)}\">See them on Paper trading</a></p>");
    }

    // Under the waiting cards for five trading days, each order that left the waiting list and why, with the revision that
    // placed it; nothing where none left.
    public static string LeftNotes(IReadOnlyList<LeftOrderView>? left)
    {
        if (left is not { Count: > 0 })
        {
            return string.Empty;
        }

        // From the operator's correction of 2026-11-24 every live rule's buy and sale stands here beside the orders that left.
        var html = new StringBuilder(Formatted($"<div class=\"left-notes\" data-left=\"{left.Count}\"><p class=\"list-count\">{LeftHeading} in the last {LeftOrderView.Sessions} trading days ({left.Count})</p><ul class=\"left-list\">"));

        foreach (var row in left)
        {
            html.Append(Formatted($"<li class=\"{(row.Sold ? "sd" : row.Bought ? "bt" : "cx")}\" data-ticker=\"{Escaped(row.Ticker)}\"{(row.Family is { } tagged ? Formatted($" data-family=\"{Escaped(tagged)}\"") : string.Empty)} data-on=\"{DayOf(row.On)}\">{WithSignedFigures(row.Words)}{(row.Family is { } family && row.Listed is { } listed ? RevisionTags.For(family, listed) : string.Empty)}</li>"));
        }

        return html.Append("</ul></div>").ToString();
    }

    static string Carousel(IReadOnlyList<WaitingOrderView> orders, string index, DateOnly? night, IReadOnlyList<DecisionCardView> again, Func<string, string> setupName, bool plain)
    {
        var sorted = NearestFirst(orders);
        var rolling = sorted.Count > WaitingGridAtMost;
        var widest = sorted.Select(order => PlanOrderWords.Gap(order.Price, order.LastClose) ?? 0m).DefaultIfEmpty(0m).Max();
        var universeWord = App.Universes.Offered.FirstOrDefault(choice => choice.Name == index)?.Word ?? App.Universes.Large.Word;
        var body = new StringBuilder();

        body.Append(Invariant, $"<div class=\"waiting-orders wo\" data-group=\"still-waiting\" data-still-waiting=\"{sorted.Count}\" data-index=\"{Escaped(index)}\" data-carousel=\"{(rolling ? "true" : "false")}\">");
        body.Append(Invariant, $"<div class=\"wo-head\"><p class=\"list-count wo-title\" data-wo-title>{Escaped(WaitingHeading(sorted.Count))}</p>");

        if (rolling)
        {
            body.Append(Invariant, $"<div class=\"wo-nav\"><span class=\"wo-count\" aria-live=\"polite\" data-wo-count>1 to {Math.Min(WaitingInView, sorted.Count)} of {sorted.Count}</span><button type=\"button\" class=\"wo-arrow\" data-wo-step=\"-1\" aria-label=\"Show the orders before these\" disabled>&#8249;</button><button type=\"button\" class=\"wo-arrow\" data-wo-step=\"1\" aria-label=\"Show the orders after these\">&#8250;</button></div>");
        }

        body.Append("</div>");
        body.Append(SetupRow(sorted, index, setupName));
        body.Append(Invariant, $"<p class=\"wo-said\">{Escaped(WaitingLine)}</p>");

        if (rolling)
        {
            body.Append("<div class=\"wo-chips\" role=\"list\">");

            foreach (var (order, at) in sorted.Select((order, at) => (order, at)))
            {
                var gap = PlanOrderWords.Gap(order.Price, order.LastClose);
                var near = gap is { } apart && apart <= WaitingNear;
                var metAgain = AgainOf(order, again) is not null;

                body.Append(Invariant, $"<button type=\"button\" role=\"listitem\" class=\"wo-chip{(near ? " near" : string.Empty)}{(at < WaitingInView ? " in-view" : string.Empty)}\" data-at=\"{at}\" data-ticker=\"{Escaped(order.Ticker)}\" data-family=\"{Escaped(order.Family)}\" data-near=\"{(near ? "true" : "false")}\" data-met-again=\"{(metAgain ? "true" : "false")}\" aria-label=\"{Escaped(order.Ticker)}, {Escaped(ToGo(gap))} to its buy price{(metAgain ? ", " + Escaped(HeldAgainView.AgainMark.ToLowerInvariant()) : string.Empty)}\">{Escaped(order.Ticker)}<span>{Escaped(Share(gap))}</span>{(metAgain ? Formatted($"<b class=\"wo-again\" title=\"{Escaped(HeldAgainView.AgainMark)}\">{Escaped(HeldAgainView.AgainMark)}</b>") : string.Empty)}</button>");
            }

            body.Append("</div>");
        }

        body.Append(rolling
            ? "<div class=\"wo-track\" tabindex=\"0\" role=\"group\" aria-label=\"Orders waiting to buy, nearest to its buy price first\" data-wo-track>"
            : "<div class=\"wo-grid\">");

        foreach (var order in sorted)
        {
            body.Append(WaitingCard(order, night, widest, AgainOf(order, again), setupName, universeWord, plain));
        }

        return body.Append("</div></div>").ToString();
    }

    // From the operator's correction of 2026-11-24 a row of setup chips under the heading: "All" with every waiting order's
    // count, chosen when the page opens, then each setup present in the order its first order stands nearest its buy price,
    // with its colour, its name and its count. Choosing one narrows the ticker chips and the cards to that setup.
    // see: Home's waiting orders are chosen by setup from a row of chips, each card one height with its checks folded
    public static string SetupRow(IReadOnlyList<WaitingOrderView> sorted, string index, Func<string, string> setupName)
    {
        var group = "wo-setup-" + System.Text.RegularExpressions.Regex.Replace(index, "[^A-Za-z0-9]", string.Empty).ToLowerInvariant();
        var html = new StringBuilder("<div class=\"wo-setup-bar\" role=\"radiogroup\" aria-label=\"Show the orders of one setup\">");

        void Chip(string key, string? family, string name, int count, bool chosen)
        {
            html.Append(Invariant, $"<input type=\"radio\" class=\"wo-setup-input\" name=\"{group}\" id=\"{group}-{Escaped(key)}\" value=\"{Escaped(key)}\"{(chosen ? " checked" : string.Empty)}>");
            html.Append(Invariant, $"<label class=\"wo-setup-chip\" for=\"{group}-{Escaped(key)}\" data-setup=\"{Escaped(key)}\"{(family is null ? string.Empty : Formatted($" data-family=\"{Escaped(family)}\""))}>{(family is null ? string.Empty : Formatted($"<span class=\"fam-swatch\" data-family=\"{Escaped(family)}\"></span>"))}{Escaped(name)}<span class=\"wo-setup-count\">{count}</span></label>");
        }

        Chip("all", null, "All", sorted.Count, chosen: true);

        foreach (var family in sorted.Select(order => order.Family).Distinct(StringComparer.Ordinal))
        {
            Chip(family, family, setupName(family), sorted.Count(order => order.Family == family), chosen: false);
        }

        return html.Append("</div>").ToString();
    }

    // One waiting order's card, from the operator's correction of 2026-11-24 the same rows in the same places on every card,
    // each a row of the track's grid so the cards stand one height with their rows lined up: the stock; its company on one
    // line; its setup's tag and that it waits; its revision as one short chip; the line saying what it buys at; the bar to its
    // buy price; the rows of its plan, their notes on its plan on Tonight; when and by which setup it was placed and when it is
    // cancelled; the checks of the night that placed it, folded; and the way to its plan. Where its stock met the rules again
    // tonight its mark stands beside its tag, the sentence and tonight's checks standing on its card among tonight's ideas.
    static string WaitingCard(WaitingOrderView order, DateOnly? night, decimal widest, DecisionCardView? again, Func<string, string> setupName, string universeWord, bool plain)
    {
        var card = new StringBuilder();
        var family = Escaped(order.Family);
        var setup = SetupLabel(order.Family);
        var gap = PlanOrderWords.Gap(order.Price, order.LastClose);
        var price = order.Price is { } at ? at.ToString("0.00", CultureInfo.InvariantCulture) : "none";
        var close = order.LastClose is { } last ? last.ToString("0.00", CultureInfo.InvariantCulture) : "none";

        card.Append(Invariant, $"<article class=\"wo-card fam-edge\" data-family=\"{family}\" data-ticker=\"{Escaped(order.Ticker)}\" data-listed=\"{DayOf(order.Listed)}\" data-price=\"{price}\" data-close=\"{close}\" data-last-day=\"{DayOf(order.LastDay)}\" data-gap=\"{(gap is { } apart ? apart.ToString("0.0000", CultureInfo.InvariantCulture) : "none")}\" data-met-again=\"{(again is null ? "false" : "true")}\">");
        card.Append(Invariant, $"<a class=\"home-ticker name-link\" data-row=\"ticker\" href=\"#/chart/{Uri.EscapeDataString(order.Ticker)}\">{Escaped(order.Ticker)}</a>");
        card.Append(order.Company is { Length: > 0 } company
            ? Formatted($"<div class=\"home-co wo-co\" data-row=\"company\" title=\"{Escaped(company)}\">{Escaped(company)}</div>")
            : "<div class=\"home-co wo-co\" data-row=\"company\">&#160;</div>");
        card.Append(Invariant, $"<div class=\"wo-badge\" data-row=\"setup\"><span class=\"fam-tag\" data-family=\"{family}\"><span class=\"fam-swatch\" data-family=\"{family}\"></span>{Escaped(setupName(order.Family))}</span><span class=\"wo-state\">{WaitingState}</span>{(again is null ? string.Empty : Formatted($"<b class=\"wo-again\">{Escaped(HeldAgainView.AgainMark)}</b>"))}</div>");
        card.Append(Invariant, $"<div class=\"wo-rev\" data-row=\"revision\">{RevisionTags.Short(order.Family, order.Listed)}</div>");
        card.Append(Invariant, $"<p class=\"wo-cond\" data-row=\"buy\">{Escaped(WaitingCondition(order))}</p>");

        if (order.Price is { } buy)
        {
            var filled = widest > 0m && gap is { } share ? Math.Clamp(share / widest, 0.02m, 1m) * 100m : 2m;

            card.Append(Invariant, $"<div class=\"wo-bar\" data-row=\"bar\" role=\"img\" aria-label=\"{Escaped(ToGo(gap))} to its buy price of {Escaped(Dollars(buy))}\"><div class=\"wo-bar-track\"><div class=\"wo-fill\" style=\"width:{filled.ToString("0.#", CultureInfo.InvariantCulture)}%\"></div></div><p class=\"wo-bar-labels\"><span>Buy price {Escaped(Dollars(buy))}</span><span>{Escaped(ToGo(gap))}</span></p></div>");
        }
        else
        {
            card.Append("<div class=\"wo-bar\" data-row=\"bar\"></div>");
        }

        card.Append("<div class=\"home-prices wo-plan\" data-row=\"plan\">");
        card.Append(order.Sale is { } parted ? SalePartsMark(parted) : string.Empty);

        foreach (var (key, label, value, _) in WaitingPlanRows(order))
        {
            card.Append(Invariant, $"<div data-plan=\"{key}\"><span>{Escaped(label)}</span><b class=\"{(key == "stop" ? "stop" : key == "buy" ? "buy" : "aim")}\">{Escaped(value)}</b></div>");
        }

        card.Append("</div>");
        card.Append(Invariant, $"<p class=\"wo-placed\" data-row=\"placed\">{Escaped(PlacedLine(order, setup, night))}</p>");
        // From the operator's correction of 2026-11-25, item 7, a stock met again keeps only its mark here: the sentence saying its
        // order stands and tonight's checks stand on its card among tonight's ideas.
        // see: A stock its setup's rules met again while its order waits stands among tonight's ideas, its waiting card keeping only its mark
        card.Append(Invariant, $"<div class=\"wo-why\" data-row=\"checks\">{(plain ? string.Empty : ChecksFold(order))}</div>");
        // From the operator's order of 2026-11-26 its plan on Tonight at its own address, the one its order's night stored.
        // see: See the plan opens the stock's plan on Tonight at its own address
        card.Append(Invariant, $"<a class=\"home-plan\" data-row=\"see-plan\" href=\"{Escaped(PlanHref(order.Ticker, universeWord))}\">See the plan</a>");

        return card.Append("</article>").ToString();
    }

    // The checks of the night that placed the order, folded under one line, "Why it qualified on 20 Nov (5 checks)", the count
    // read off the card that night stored; opened, they stand in a panel over the card's lower half. A night that stored no
    // checks for the order says so.
    public static string ChecksFold(WaitingOrderView order)
    {
        var count = order.Placing is { } placing ? AgainChecks(placing) : 0;
        var placed = order.Placing is { } stored && WhyItQualified(stored, FormattableString.Invariant($"On {order.Listed:d MMM}, when its order was placed")) is { Length: > 0 } drawn
            ? drawn
            : Formatted($"<p class=\"wo-none\">The night of {order.Listed:d MMM} stored no checks for this order.</p>");
        var summary = FormattableString.Invariant($"Why it qualified on {order.Listed:d MMM}") + (count > 0 ? FormattableString.Invariant($" ({count} {(count == 1 ? "check" : "checks")})") : string.Empty);

        return Formatted($"<details class=\"wo-fold\" data-checks=\"{count}\"><summary>{Escaped(summary)}</summary><div class=\"wo-panel\"><button type=\"button\" class=\"wo-close\" aria-label=\"Close the checks\">Close</button>{placed}</div></details>");
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
