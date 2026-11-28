using System.Text;

namespace EquityBrief.Web.Marks;

// One stock the rules hold a plan for on a night, as Tonight's list of every plan draws it: its group, new tonight, waiting
// to buy or already held; its setup and stock; the card its plan is drawn from, tonight's for a new idea or a held stock and
// the card stored on the night its order was placed for a waiting order, none where the store holds none; tonight's pick
// row for a new idea, the order for a waiting one, a book's buy for one its card's plan holds no card of; when it was
// placed or bought; whether its setup's rules met it again tonight; and tonight's close.
public sealed record PlanEntryView(string Group, string Family, string Ticker)
{
    public const string New = "new";

    public const string Waiting = "waiting";

    public const string Held = "held";

    public string? Company { get; init; }

    public DecisionCardView? Card { get; init; }

    public FamilyPickCell? Pick { get; init; }

    public WaitingOrderView? Order { get; init; }

    public PlanlessBuyView? Planless { get; init; }

    public DateOnly? Since { get; init; }

    public bool MetAgain { get; init; }

    // A new idea bought only if its price dips to its buy price, as tonight's counts read it.
    public bool OnDip { get; init; }

    public decimal? Close { get; init; }

    // The id the list's row and the plan it opens share, unique on the page.
    public string Id => ("plan-" + Group + "-" + Family + "-" + Ticker).ToLowerInvariant().Replace('.', '-').Replace(' ', '-');

    // A waiting order's distance above its buy price as a share of it, or a held trade's move since its fill.
    public decimal? Gap => Order is { Price: { } price, LastClose: { } close } && price > 0 ? (close - price) / price : null;
}

// From the operator's order of 2026-11-26 Tonight holds one list of every stock the rules hold a plan for, in three groups,
// and one plan design for every kind, each plan at its own address.
// see: Tonight lists every plan the rules hold on the night in one place, each at its own address in one design
public sealed partial class MarkRenderer
{
    // A setup in a few plain words, as the list and the plan's header name it, its rule's name beside it on the setups' table;
    // from 24.47 kept in the core, which the night's order changes name each setup by too.
    public static IReadOnlyDictionary<string, string> SetupShort => EquityBrief.Core.Paper.SetupNames.Short;

    public static string ShortOf(string family) => SetupShort.GetValueOrDefault(family) ?? Capitalised(SetupOf(family));

    // The address of one plan on Tonight, on the index shown.
    public const string PlanRoute = "#/tonight/plan/";

    public static string PlanHref(string ticker, string universeWord) =>
        PlanRoute + Uri.EscapeDataString(ticker) + (universeWord == App.Universes.Large.Word ? string.Empty : "?" + App.Universes.Query + "=" + Uri.EscapeDataString(universeWord));

    // The groups' headings and filter words, in the list's order.
    static readonly (string Group, string Filter, string Heading)[] PlanGroups =
    [
        (PlanEntryView.New, "New tonight", "New tonight"),
        (PlanEntryView.Waiting, "Waiting to buy", "Waiting to buy, nearest its buy price first"),
        (PlanEntryView.Held, "Already held", "Already held, met the rules again"),
    ];

    // The waiting orders the list shows before the rest are folded behind one line.
    public const int WaitingShown = 5;

    // The entry a plan's address names: the first in the list's order holding the ticker, or the list's first.
    public static PlanEntryView? Chosen(IReadOnlyList<PlanEntryView> entries, string? ticker) =>
        entries.FirstOrDefault(entry => string.Equals(entry.Ticker, ticker, StringComparison.OrdinalIgnoreCase)) ?? entries.FirstOrDefault();

    // Every plan tonight: the filter with each group's count, the list in its three groups, and beside it the plan chosen,
    // each other plan drawn and hidden until its row is chosen; full screen on a phone where the address names one.
    public string EveryPlan(IReadOnlyList<PlanEntryView> entries, DateOnly night, string universeWord, string? named, string? fallback = null, IReadOnlyDictionary<string, string>? levels = null)
    {
        var body = new StringBuilder();
        var chosen = Chosen(entries, named ?? fallback);
        var counts = PlanGroups.ToDictionary(group => group.Group, group => entries.Count(entry => entry.Group == group.Group), StringComparer.Ordinal);

        body.Append(Invariant, $"<section class=\"card tn-plans{(named is not null && chosen is not null ? " sheet" : string.Empty)}\" id=\"plans\" data-card=\"plans\" data-plans=\"{entries.Count}\" data-new=\"{counts[PlanEntryView.New]}\" data-waiting=\"{counts[PlanEntryView.Waiting]}\" data-held=\"{counts[PlanEntryView.Held]}\" data-chosen=\"{Escaped(chosen?.Ticker ?? "none")}\">");
        body.Append("<div class=\"card-h\"><div><h2>Every plan tonight</h2></div></div><div class=\"card-b\">");
        var reshuffled = entries.Count(entry => entry.Planless is not null && entry.Family is EquityBrief.Core.Families.HeavyweightRule.Name or EquityBrief.Core.Families.QualityRule.Name);

        body.Append(Invariant, $"<p class=\"card-summary\" data-summary>{Escaped(PlansSaid(counts, reshuffled))}</p>");
        body.Append("<p class=\"tn-sub\">Every stock the rules have a plan for: tonight's new ideas, orders still waiting from earlier nights, and trades already held that met the rules again. Pick one to see its full plan. Home's \"See the plan\" opens the same view.</p>");

        if (entries.Count == 0)
        {
            return body.Append("</div></section>").ToString();
        }

        body.Append("<div class=\"pl-filter\" role=\"tablist\">");
        body.Append(Invariant, $"<button type=\"button\" class=\"on\" data-plan-filter=\"all\">All <span class=\"n\">{entries.Count}</span></button>");

        foreach (var (group, filter, _) in PlanGroups)
        {
            body.Append(Invariant, $"<button type=\"button\" data-plan-filter=\"{group}\"{(counts[group] == 0 ? " disabled" : string.Empty)}>{Escaped(filter)} <span class=\"n\">{counts[group]}</span></button>");
        }

        body.Append("</div><div class=\"pl-grid\"><div class=\"pl-list\" aria-label=\"Plans\">");

        foreach (var (group, _, heading) in PlanGroups)
        {
            var rows = entries.Where(entry => entry.Group == group).ToArray();

            if (rows.Length == 0)
            {
                continue;
            }

            var onDip = group == PlanEntryView.New && rows.All(entry => entry.OnDip);

            body.Append(Invariant, $"<div class=\"pl-g\" data-g=\"{group}\">{Escaped(heading + (onDip ? ", bought only on a dip" : string.Empty))}</div>");

            foreach (var (entry, at) in rows.Select((entry, at) => (entry, at)))
            {
                var extra = group == PlanEntryView.Waiting && at >= WaitingShown;

                body.Append(PlanRow(entry, universeWord, night, entry == chosen, extra));
            }

            if (group == PlanEntryView.Waiting && rows.Length > WaitingShown)
            {
                body.Append(Invariant, $"<button type=\"button\" class=\"pl-more\" data-g=\"waiting\">and {rows.Length - WaitingShown} more, in the same order</button>");
            }
        }

        body.Append("</div>");

        foreach (var entry in entries)
        {
            body.Append(PlanArticle(entry, night, entry == chosen, levels?.GetValueOrDefault(entry.Ticker)));
        }

        return body.Append("</div></div></section>").ToString();
    }

    // The plans counted in a sentence, each group apart.
    static string PlansSaid(IReadOnlyDictionary<string, int> counts, int reshuffled)
    {
        var all = counts.Values.Sum();

        return all == 0
            ? "No stock holds a plan tonight: no setup found a new idea, no order waits for its price and no stock already held met the rules again."
            : Formatted($"{Counted(all, "plan", "plans")} tonight: {counts[PlanEntryView.New]} new{(reshuffled == 0 ? string.Empty : Formatted($", {reshuffled} of them from the monthly books' reshuffle"))}, {counts[PlanEntryView.Waiting]} waiting to buy and {counts[PlanEntryView.Held]} already held that met the rules again.");
    }

    // A row of the list: the ticker, the company, the setup with its swatch, and its figure: the price a new idea buys at, how
    // far a waiting order is from its price and its days left, a held trade's move since it was bought.
    static string PlanRow(PlanEntryView entry, string universeWord, DateOnly night, bool on, bool extra)
    {
        var figure = entry.Group switch
        {
            PlanEntryView.Waiting => WaitingFigure(entry, night),
            PlanEntryView.Held => HeldFigure(entry),
            _ => NewFigure(entry),
        };
        var company = entry.Company is { Length: > 0 } named ? CompanyWords.Short(named) : string.Empty;

        return Formatted($"<a class=\"pl-row{(on ? " on" : string.Empty)}{(extra ? " pl-extra" : string.Empty)}\" href=\"{Escaped(PlanHref(entry.Ticker, universeWord))}\" data-plan-id=\"{Escaped(entry.Id)}\" data-g=\"{entry.Group}\" data-ticker=\"{Escaped(entry.Ticker)}\" data-family=\"{Escaped(entry.Family)}\"><span class=\"r-t\">{Escaped(entry.Ticker)}</span><span class=\"r-co\">{Escaped(company)}</span><span class=\"r-fam\"><i class=\"fam-swatch\" data-family=\"{Escaped(entry.Family)}\"></i>{Escaped(ShortOf(entry.Family))}{(entry.MetAgain ? " <span class=\"r-again\">met again</span>" : string.Empty)}</span><span class=\"r-fig\">{figure}</span></a>");
    }

    static string NewFigure(PlanEntryView entry)
    {
        if (entry.Card?.Zone is { NoBuy: false } zone)
        {
            var below = zone.Close > 0 ? (zone.Close - zone.Price) / zone.Close : 0m;

            return Formatted($"Buys at ${Price(zone.Price)}<br><small>{Math.Abs(below * 100):0.0}% {(below >= 0 ? "below" : "above")} the close</small>");
        }

        if (entry.Card?.Zone is { NoBuy: true })
        {
            return "No buy tonight<br><small>its levels set no price</small>";
        }

        var last = entry.Close ?? entry.Card?.Entry ?? entry.Planless?.Close;

        return Formatted($"Buys at the next open<br><small>{(last is { } close ? "last close $" + Price(close) : "at whatever price it opens")}</small>");
    }

    static string WaitingFigure(PlanEntryView entry, DateOnly night)
    {
        var left = DaysLeft(entry, night);

        return Formatted($"{(entry.Gap is { } gap ? Formatted($"{Math.Max(gap, 0m) * 100:0.0}% to go") : "no price to go to")}<br><small>{(left is { } days ? Counted(days, "trading day left", "trading days left") : "its last day not read")}</small>");
    }

    static string HeldFigure(PlanEntryView entry)
    {
        if (entry.Card?.HeldAgain is { Fill: { } fill } held && (held.LastClose ?? entry.Close) is { } close && fill > 0)
        {
            var moved = (close - fill) / fill;

            return Formatted($"<span class=\"{(moved >= 0 ? "gain" : "loss")}\">{(moved >= 0 ? "+" : "−")}{Math.Abs(moved * 100):0.0}%</span><br><small>since bought</small>");
        }

        return "Bought<br><small>its fill not stored</small>";
    }

    // The trading days a waiting order has left after tonight, its last day among them.
    static int? DaysLeft(PlanEntryView entry, DateOnly night) =>
        entry.Order is { } order ? EquityBrief.Core.Bars.ExchangeClosures.SessionsUntil(night, order.LastDay) : null;

    // One plan, the same design for every kind: its header, its buy condition, the price line, the four plan figures, why it is
    // here, and tabs for sizing and exits, why it qualified, what could go wrong, its setup's record and the operator's trade.
    public string PlanArticle(PlanEntryView entry, DateOnly night, bool shown, string? levels = null)
    {
        var body = new StringBuilder();
        var card = entry.Card;
        var setup = SetupOf(entry.Family);
        var company = entry.Company ?? entry.Pick?.Row.Distance?.Name ?? entry.Planless?.Company;
        var revision = RevisionTags.For(entry.Family, entry.Group == PlanEntryView.Waiting && entry.Order is { } placed ? placed.Listed : DateOnly.MaxValue, order: entry.Group == PlanEntryView.Waiting);

        body.Append(Invariant, $"<article class=\"pl-plan fam-edge{(shown ? " on" : string.Empty)}\" id=\"{Escaped(entry.Id)}\" data-plan=\"{Escaped(entry.Ticker)}\" data-g=\"{entry.Group}\" data-family=\"{Escaped(entry.Family)}\" data-card-night=\"{(card is null ? "none" : DayOf(card.Night))}\"{(shown ? string.Empty : " hidden")}>");
        body.Append("<button type=\"button\" class=\"pl-back\">← All plans</button>");
        body.Append("<header class=\"pp-h\"><div>");
        body.Append(Invariant, $"<h3><span class=\"pp-t\">{Escaped(entry.Ticker)}</span> <span class=\"pp-co\">{Escaped(company ?? string.Empty)}</span></h3>");
        // A stock another setup passed as well carries that setup's label, as its row of tomorrow's buys did.
        var also = string.Concat((entry.Pick?.Also ?? []).Select(label => Formatted($" <span class=\"also-family\" data-also=\"{Escaped(label)}\">also {Escaped(Article(label))}</span>")));

        body.Append(Invariant, $"<p class=\"pp-meta\"><span class=\"pp-fam\"><i class=\"fam-swatch\" data-family=\"{Escaped(entry.Family)}\"></i>{Escaped(ShortOf(entry.Family))}</span>{also}{revision}<span class=\"pp-st\" data-state=\"{entry.Group}\">{Escaped(StateWords(entry))}</span>{(entry.Pick?.Row.Suspect is { } suspect ? SuspectMark(suspect) : string.Empty)}</p>");
        body.Append(Invariant, $"</div><div class=\"pp-links\"><a href=\"{ChartRoute}{Uri.EscapeDataString(entry.Ticker)}\">Chart</a><a href=\"#/name/{Uri.EscapeDataString(entry.Ticker)}\">The stock's full page</a>{(entry.Pick is { } chosenPick && !Copying ? ReportPart(chosenPick.Row) : string.Empty)}</div></header>");

        if (entry.Group == PlanEntryView.Waiting && card is null)
        {
            body.Append(Invariant, $"<p class=\"pp-none\" data-no-placed-plan>The store holds no plan from the night this order was placed, {(entry.Order?.Listed is { } listed ? listed.ToString("d MMM yyyy", Invariant) : "an earlier night")}. What is drawn here is the order as it was stored; nothing is worked out again from tonight's figures.</p>");
        }

        body.Append(Invariant, $"<p class=\"pp-cond\">{Escaped(Condition(entry, night))}</p>");
        body.Append(Ladder(entry));
        body.Append(FourFigures(entry, night));
        body.Append(WhyHere(entry));

        if (Copying)
        {
            body.Append(card is null ? string.Empty : PlainCard(card, entry.Pick, linked: false));

            return body.Append("</article>").ToString();
        }

        if (entry.Planless is { } book)
        {
            body.Append(Invariant, $"<section class=\"pp-book\"><p>{Escaped(book.Sells)}. Its book's holdings and how it is reshuffled are on its setup's row below. <a href=\"{book.Href}\">Open its row</a></p>{SetupRecordBox(book.LongRecord)}</section>");

            return body.Append("</article>").ToString();
        }

        if (card is null)
        {
            body.Append(entry.Group == PlanEntryView.Waiting ? string.Empty : "<p class=\"pp-note\">Its night stored no card for it, so its checks and its record are not drawn.</p>");

            return body.Append("</article>").ToString();
        }

        var thatNight = entry.Group == PlanEntryView.Waiting;
        var checks = ChecksAndPlanApart(card) + OtherChecks(card);

        // A waiting order's checks are the night it was placed, said as that night's.
        if (thatNight)
        {
            checks = AsThatNight(checks.Replace(RulesChecksHeading, "The rule's checks on the night of " + card.Night.ToString("d MMM", Invariant), StringComparison.Ordinal));
        }

        var panels = new (string Key, string Tab, string Html)[]
        {
            ("size", "Sizing and exits", ThePlan(card, setup, entry.Pick, levels is not null) + (levels ?? string.Empty) + (card.HeldAgain is { } held ? Formatted($"<p class=\"pp-note\">{Escaped(thatNight ? AsThatNight(held.Line(entry.Ticker, card.Family)) : held.Line(entry.Ticker, card.Family))}</p>") : string.Empty)),
            ("checks", "Why it qualified", Formatted($"<p class=\"pp-note\" data-checks-when=\"{(thatNight ? DayOf(card.Night) : "tonight")}\">{Escaped(thatNight ? "The rule's checks on the night of " + card.Night.ToString("d MMM yyyy", Invariant) + ", when its order was placed, each against its floor." :"The rule's checks at tonight's close, each against its floor.")}</p>") + (checks.Length > 0 ? checks : "<p class=\"pp-note\">Its card holds no line of checks.</p>")),
            ("risks", "What could go wrong", Hits(card) is { Length: > 0 } hits ? hits : "<p class=\"pp-note\">Nothing the store holds falls inside this trade's hold.</p>"),
            ("record", "Its setup's record", RuleRecord(card) + SetupRecordBox(card.LongRecord) + ScorePart(card, setup) + (card.Behind is { } behind ? Compares(behind) : string.Empty) + Formatted($"<p class=\"pp-note\">{Escaped(RecordNote)}</p>")),
            ("mine", "Your trade", OperatorRecord(card) + Taken(card) + (card.Fill is { } fill ? Formatted($"<p class=\"card-fill\" data-fill>{Escaped(fill)}</p>") : string.Empty) is { Length: > 0 } mine ? mine : "<p class=\"pp-note\">Bought this stock? Record it on the stock's full page, and each night follows your trade by the setup's own exits.</p>"),
        };

        // The tabs hold the card's own parts, under the card its night stored, its sentence first.
        body.Append(Invariant, $"<div class=\"decision-card\" data-index=\"{Escaped(card.Index)}\" data-family=\"{Escaped(card.Family)}\" data-ticker=\"{Escaped(card.Ticker)}\" data-night=\"{DayOf(card.Night)}\">");
        // A plan with no pick row says the card's sentence as why it is here, so the sentence stands once.
        body.Append(entry.Pick is null ? string.Empty : Formatted($"<p class=\"card-summary pp-said\" data-summary>{Escaped(CardSummary(card, setup))}</p>"));
        body.Append("<nav class=\"pp-tabs\" role=\"tablist\">");

        foreach (var (key, tab, _) in panels)
        {
            body.Append(Invariant, $"<button type=\"button\" data-p=\"{key}\"{(key == "size" ? " class=\"on\"" : string.Empty)}>{Escaped(tab)}</button>");
        }

        body.Append("</nav>");

        foreach (var (key, _, html) in panels)
        {
            body.Append(Invariant, $"<section class=\"pp-panel{(key == "size" ? " on" : string.Empty)}\" data-p=\"{key}\">{html}</section>");
        }

        return body.Append("</div></article>").ToString();
    }

    // Words a card stored on an earlier night, said as that night's.
    public static string AsThatNight(string html) =>
        html.Replace("tonight's", "that night's", StringComparison.Ordinal)
            .Replace("Tonight's", "That night's", StringComparison.Ordinal)
            .Replace(" tonight", " that night", StringComparison.Ordinal)
            .Replace("Tonight ", "That night ", StringComparison.Ordinal);

    // The plan's state on its header: new tonight, waiting since the night its order was placed, held since it was bought, and
    // that its setup's rules met it again tonight where they did.
    static string StateWords(PlanEntryView entry)
    {
        var state = entry.Group switch
        {
            PlanEntryView.Waiting => entry.Since is { } placed ? "Waiting since " + placed.ToString("d MMM", Invariant) : "Waiting",
            PlanEntryView.Held => entry.Since is { } bought ? "Held since " + bought.ToString("d MMM", Invariant) : "Bought on an earlier night",
            _ => "New tonight",
        };

        return entry.MetAgain || entry.Group == PlanEntryView.Held ? state + " · met the rules again tonight" : state;
    }

    // The buy condition in one sentence: the price a plan waits for and its last day with tonight's close, a buy at the next
    // open at whatever price it opens, a held trade's fill and where tonight's close stands from it.
    static string Condition(PlanEntryView entry, DateOnly night)
    {
        if (entry.Group == PlanEntryView.Held && entry.Card?.HeldAgain is { } held)
        {
            return held.Fill is { } fill
                ? Formatted($"Bought on {(held.BoughtOn ?? held.Night)?.ToString("d MMM", Invariant) ?? "an earlier night"} at ${Price(fill)}.{((held.LastClose ?? entry.Close) is { } close ? Formatted($" Tonight's close was ${Price(close)}.") : string.Empty)} It is sold by its own exits; nothing buys it again until that trade ends.")
                : "Its order has not been filled, so nothing is held yet and no second order is placed.";
        }

        // From the operator's order of 2026-11-28 an order filled on the night says it was bought, and one still waiting how often
        // its rule's past orders this far away with as long left filled.
        // see: A waiting order's fill odds are how often the ledger's past orders of its rule this far away with as long left filled before their last day
        if (entry.Order is { Bought: { } bought })
        {
            return bought + ". It is sold by its own exits from here.";
        }

        if (entry.Order is { } order)
        {
            var left = DaysLeft(entry, night);

            return (order.Price is { } price
                ? Formatted($"Bought only if the price falls to ${Price(price)} by {order.LastDay.ToString("d MMM", Invariant)}.{(order.LastClose is { } close ? Formatted($" Tonight's close was ${Price(close)}, {Math.Abs((close - price) / price * 100):0.0}% {(close >= price ? "above" : "below")}.") : string.Empty)}{(left is { } days ? " " + Capitalised(Counted(days, "trading day is left.", "trading days are left.")) : string.Empty)} If it never gets there, nothing is bought.")
                : Formatted($"Bought only if the price falls to a set level, which its plan could not place; cancelled if not filled by {order.LastDay.ToString("d MMM", Invariant)}.")) + (order.Odds is { } odds ? " " + odds : string.Empty);
        }

        if (entry.Card?.Zone is { NoBuy: false } zone)
        {
            return ZoneBox(zone, entry.Card.Night) + " If it never gets there, nothing is bought.";
        }

        if (entry.Card?.Zone is { NoBuy: true })
        {
            return NoBuyTonight;
        }

        var last = entry.Close ?? entry.Card?.Entry ?? entry.Pick?.Buy ?? entry.Planless?.Close;

        return "Buys at the next open, at whatever price it opens." + (last is { } at ? Formatted($" Tonight's close was ${Price(at)}.") : string.Empty);
    }

    // The plan's four prices as the price line draws them: its stop, its buy, the last close and its take-profit.
    static (decimal? Stop, decimal? Buy, decimal? Close, decimal? TakeProfit) Prices(PlanEntryView entry)
    {
        var card = entry.Card;

        if (entry.Group == PlanEntryView.Held && card?.HeldAgain is { } held)
        {
            return (held.Stop ?? card.Stop, held.Fill, held.LastClose ?? entry.Close, held.TakeProfit ?? card.TakeProfit?.Price ?? card.Target);
        }

        if (entry.Order is { } order)
        {
            return (order.Stop ?? order.Sale?.Stop ?? card?.Stop, order.Price, order.LastClose, order.Sale?.TakeProfit?.Price ?? order.Sale?.Aim ?? card?.Zone?.Sale?.TakeProfit?.Price ?? card?.TakeProfit?.Price);
        }

        if (entry.Planless is { } book)
        {
            return (book.Stop, null, book.Close, book.TakeProfit?.Price);
        }

        var sale = card?.Zone?.Sale;
        decimal? buy = card?.Zone is { NoBuy: false } zone ? zone.Price : null;

        return (sale?.Stop ?? card?.Stop ?? entry.Pick?.Stop, buy, entry.Close ?? card?.Entry ?? entry.Pick?.Buy, sale?.TakeProfit?.Price ?? sale?.Aim ?? card?.TakeProfit?.Price ?? card?.Target ?? entry.Pick?.Target);
    }

    // The price line: where the stop, the buy, the last close and the take-profit sit against one another, and beneath it the
    // reward against the risk from the buy and the share of the time it must reach its take-profit first to break even.
    static string Ladder(PlanEntryView entry)
    {
        var (stop, buy, close, take) = Prices(entry);
        var marks = new List<(string Kind, string Label, decimal Price)>();

        if (stop is { } s) { marks.Add(("stop", "Stop", s)); }
        if (buy is { } b) { marks.Add(("buy", entry.Group == PlanEntryView.Held ? "Bought" : "Buy", b)); }
        if (close is { } c) { marks.Add(("now", "Last close", c)); }
        if (take is { } t) { marks.Add(("tp", "Take profit", t)); }

        if (marks.Count < 2)
        {
            return string.Empty;
        }

        var low = marks.Min(mark => mark.Price);
        var high = marks.Max(mark => mark.Price);
        var html = new StringBuilder("<figure class=\"pp-ladder\" aria-label=\"Where the plan's prices sit\"><div class=\"lad\"><span class=\"lad-bar\"></span>");

        foreach (var (kind, label, price) in marks)
        {
            var at = high == low ? 50m : 4m + (92m * (price - low) / (high - low));

            html.Append(Invariant, $"<span class=\"lad-m {kind}\" data-{kind}=\"{price.ToString(Invariant)}\" style=\"left:{at.ToString("0.#", Invariant)}%\"><i></i><b>{label}</b><em>${Price(price)}</em></span>");
        }

        html.Append("</div>");

        var from = buy ?? close;

        if (from is { } start && stop is { } floor && take is { } aim && start > floor && aim > start)
        {
            var ratio = (aim - start) / (start - floor);

            html.Append(Invariant, $"<figcaption data-reward-to-risk=\"{ratio.ToString("0.00", Invariant)}\">From the {(buy is null ? "last close" : entry.Group == PlanEntryView.Held ? "fill" : "buy")}, the stop is ${Price(start - floor)} down and the take-profit ${Price(aim - start)} up: {ratio.ToString("0.00", Invariant)} times the risk. {Escaped(EquityBrief.Core.Cards.PlanWords.BreakEvenSaid(ratio, "its take-profit"))}</figcaption>");
        }

        return html.Append("</figure>").ToString();
    }

    // The four plan figures: what it buys at, the close that sells it at a loss, the price it takes profit at, and how long it
    // is held otherwise.
    static string FourFigures(PlanEntryView entry, DateOnly night)
    {
        var card = entry.Card;
        var (stop, buy, _, take) = Prices(entry);
        var html = new StringBuilder("<dl class=\"pp-plan\">");

        void Figure(string kind, string term, string value, string? note) =>
            html.Append(Invariant, $"<div data-figure=\"{kind}\"><dt>{Escaped(term)}</dt><dd class=\"{kind}\">{Escaped(value)}</dd>{(note is null ? string.Empty : Formatted($"<small>{Escaped(note)}</small>"))}</div>");

        if (entry.Group == PlanEntryView.Held)
        {
            Figure("buy", "Bought at", buy is { } fill ? "$" + Price(fill) : "not stored", card?.HeldAgain?.BoughtOn is { } on ? "on " + on.ToString("d MMM", Invariant) : null);
        }
        else if (entry.Order is { } order)
        {
            Figure("buy", "Buy if it dips to", buy is { } price ? "$" + Price(price) : "a set level", "by " + order.LastDay.ToString("d MMM", Invariant));
        }
        else if (card?.Zone is { NoBuy: false } zone)
        {
            Figure("buy", "Buy if it dips to", "$" + Price(zone.Price), Formatted($"within {zone.Within} trading days, by ") + PlanOrderWords.LastDay(card.Night, zone.Within).ToString("d MMM", Invariant));
        }
        else
        {
            Figure("buy", "Buy", "At the next open", "at whatever price it opens");
        }

        var trailing = stop is null && (card?.Trail is not null || entry.Pick?.Trailing == true);

        Figure("stop", "Sell if it closes below", stop is { } floor ? "$" + Price(floor) : trailing ? "A rising stop" : entry.Planless is { } book ? "None" : "None", stop is null && entry.Planless is { } sells ? sells.Sells : "sold at the next day's open");

        var multiple = card?.TakeProfit?.Multiple ?? card?.Zone?.Sale?.TakeProfit?.Multiple ?? entry.Order?.Sale?.TakeProfit?.Multiple;

        // From the operator's correction of 2026-11-24, item 4, a waiting order's notes on how it takes profit leave its card on
        // Home for its plan here, under its take-profit.
        // see: Home's waiting orders are chosen by setup from a row of chips, each card one height with its checks folded
        Figure("target", "Take profit at", take is { } aim ? "$" + Price(aim) : card?.Zone?.Sale is { OnTimeAlone: true } ? "None" : trailing ? "None" : "None", entry.Group == PlanEntryView.Waiting && WaitingTargetNote(entry.Order) is { } said ? said : take is null ? (trailing ? "its stop rises with the price" : card?.Zone?.Sale is { OnTimeAlone: true } ? "it sells on its time limit" : entry.Pick?.Exit is { } exit ? ReaderWords.Plain(exit) : null) : multiple is { } times ? Formatted($"{times:0.##} times the risk") : null);

        var cap = entry.Order?.Sale?.Cap ?? card?.Zone?.Sale?.Cap ?? card?.Cap;

        Figure("limit", "Otherwise sell after", cap is { } days ? Formatted($"{days} trading days") : entry.Planless is not null ? "Its next reshuffle" : "No limit stated", card?.Hits is { } hits && entry.Group != PlanEntryView.Waiting ? "about " + hits.Through.ToString("d MMM", Invariant) : null);

        return html.Append("</dl>").ToString();
    }

    // The note a waiting order's take-profit row carries, as its card drew it before the operator's correction of 2026-11-24;
    // none where its sale holds no take-profit or the row no note.
    public static string? WaitingTargetNote(WaitingOrderView? order) =>
        order?.Sale is { } sale ? PlanSaleLines(sale).Where(line => line.Key == "target").Select(line => line.Note).FirstOrDefault(note => note is { Length: > 0 }) : null;

    // Why it is here, with the business, the news and the entry check where the night stored them.
    string WhyHere(PlanEntryView entry)
    {
        var why = entry.Pick is { } pick ? WhyWords(entry.Family, pick.Why)
            : entry.Planless is { } book ? book.Why
            : entry.Card?.HeldAgain is { } held ? held.Line(entry.Ticker, entry.Family)
            : entry.Card is { } card ? CardSummary(card, SetupOf(entry.Family))
            : entry.Order is { } order ? order.Row(SetupOf(entry.Family))
            : string.Empty;
        var facts = new List<string>();

        if (entry.Pick is { } row)
        {
            facts.Add("<span class=\"pp-business\">The business: " + (row.Row.Business is null ? "<span class=\"degraded\" data-state=\"none\">not read</span>" : BusinessWord(row.Row.Business)) + "</span>");
            facts.Add(row.Row.NewsPositive is { } good && row.Row.NewsNegative is { } bad
                ? Formatted($"<span class=\"pp-news\" data-positive=\"{good}\" data-negative=\"{bad}\">News, 30 days: {good} good, {bad} bad</span>")
                : "<span class=\"pp-news\" data-positive=\"none\" data-negative=\"none\">News, 30 days: no label</span>");
        }
        else if (entry.Planless is { } bought)
        {
            facts.Add("<span class=\"pp-business\">The business: " + Escaped(bought.Business) + "</span>");
        }

        var check = entry.Card?.EntryCheck ?? entry.Planless?.EntryCheck;

        return Formatted($"<div class=\"pp-why\"><p><b>Why it's here.</b> {Escaped(why)}{(facts.Count > 0 ? Formatted($"<span class=\"pp-facts\">{string.Join(" · ", facts)}</span>") : string.Empty)}</p>{EntryCheckLine(check, entry.Ticker)}</div>");
    }
}
