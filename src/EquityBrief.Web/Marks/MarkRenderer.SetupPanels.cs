using System.Text;

namespace EquityBrief.Web.Marks;

// From the operator's order of 2026-11-28 a setup's panel beneath its row on Tonight, in place of its card: no heading,
// revision line or description of its own, which the row says; the Showing selector; the funnel sentence; tonight's picks and
// the stocks its rules met again as one-line rows, each opening its plan; and the setup's folds, every one closed until clicked.
// see: Tonight's setup rows start closed and each opens a panel of its own on a click
// see: A stock's full card is drawn only in its plan view on Tonight, and every other page shows it as one line that opens the plan
public sealed partial class MarkRenderer
{
    public const string MetAgainHeading = "Met the rules again";

    // How the note on a stock qualifying again while its trade is open ends, as the screen writes it.
    public static readonly string OpenTradeNoteEnd = "its card is under " + HeldAgainView.NoteGroup + ".";

    // The Showing selector of a book's panel, as its card's head drew it.
    public static string ShowingOf(string family, RuleView rule) => RuleTabs(family, rule);

    public string SetupPanel(FamilyCardView card, string universeWord)
    {
        var body = new StringBuilder();
        var variant = card.RuleChoice is { Chosen.Live: false } chosen ? chosen : null;

        // The panel's root carries the standing its card's did, which the row draws.
        body.Append(Invariant, $"<section class=\"family-card\" data-family=\"{Escaped(card.Family)}\" data-place=\"{card.Place}\" data-of=\"{card.Of}\" data-picks=\"{card.Picks.Count}\" ");
        body.Append(Invariant, $"data-state=\"{(card.LiveSince is null ? "provisional" : "live")}\" data-live-since=\"{(card.LiveSince is { } since ? DayOf(since) : "none")}\" data-variants=\"{card.Variants}\" ");
        body.Append(Invariant, $"data-rule=\"{Escaped(card.RuleChoice?.Chosen.Slug ?? RuleScreenWords.LiveSlug)}\" data-variant=\"{(variant is null ? "none" : variant.Chosen.Variant!.Value.ToString(Invariant))}\" data-folded=\"{Flag(Folded(card))}\">");

        if (card.RuleChoice is { } rule && !Copying)
        {
            body.Append(RuleTabs(card.Family, rule));
        }

        body.Append(CardSummary(card, variant));

        if (variant is not null)
        {
            body.Append(Invariant, $"<div class=\"variant-band\" role=\"status\" data-variant=\"{variant.Chosen.Variant}\"><p class=\"band-head\">A variant being tested, not tonight's picks</p><p>{Escaped(VersionName(variant.Chosen))}</p><p class=\"band-note\">It runs every night beside the revision running now, on paper only. It becomes the next revision only if it proves better over enough trades and you approve it on the Improvement loop.</p></div>");
            body.Append(ShareLine(variant.Share, "picks"));
            body.Append(VariantPicks(card.Family, variant.Picks ?? [], card.Numbers));
        }
        else if (card.Picks.Count > 0)
        {
            body.Append(PanelPicks(card.Picks, card.Family, universeWord));
        }

        if (variant is null)
        {
            // A stock qualifying again while its trade is open is said by its row under the heading below, and its note goes;
            // one whose night stored no card keeps its note, having no row.
            var again = card.HeldAgain.Where(held => held.HeldAgain is not null).Select(held => held.Ticker).ToHashSet(StringComparer.Ordinal);

            foreach (var note in card.Notes.Where(note => !(note.EndsWith(OpenTradeNoteEnd, StringComparison.Ordinal) && again.Contains(note.Split(' ')[0]))))
            {
                body.Append(Invariant, $"<p class=\"family-note\">{WithSignedFigures(ReaderWords.Plain(note))}</p>");
            }

            body.Append(PanelAgain(card.HeldAgain, universeWord));
        }

        if (Copying)
        {
            body.Append(SetupRecord(card));

            return body.Append("</section>").ToString();
        }

        if (card.RuleChoice is { } drawn)
        {
            body.Append(StretchLine(drawn.Stretch, drawn.Evaluated));
        }

        body.Append(SetupFolds(card, variant));

        return body.Append("</section>").ToString();
    }

    // Tonight's picks, a line each: the stock and its company, why it is here, and its buy, stop and target, the line opening
    // its plan.
    string PanelPicks(IReadOnlyList<FamilyPickCell> picks, string family, string universeWord)
    {
        var body = new StringBuilder();

        body.Append(Invariant, $"<ul class=\"sp-rows\" data-rows=\"picks\" data-count=\"{picks.Count}\">");

        foreach (var pick in picks)
        {
            // The plan's price, its stop and its one exit where its plan buys at a price, as the card's own rows draw them.
            var (buy, stop, target, aimed) = pick.Card?.Zone is { NoBuy: false, Sale: { } sale } zone
                ? (zone.Price, pick.Card.Stop, TargetWords(sale), sale.TakeProfit?.Price ?? (sale.OnTimeAlone ? null : sale.Target))
                : (pick.Buy, pick.Stop, pick.Card?.TakeProfit is { } taking ? Dollars(taking.Price) : pick.Target is { } aim ? Dollars(aim) : pick.Trailing ? "rising stop, no target" : pick.Exit is { } exit ? ReaderWords.Plain(exit) : "none", pick.Card?.TakeProfit?.Price ?? pick.Target);

            body.Append(Invariant, $"<li><a class=\"sp-line\" data-plan-line data-ticker=\"{Escaped(pick.Row.Ticker)}\" href=\"{Escaped(PlanHref(pick.Row.Ticker, universeWord))}\">");
            body.Append(Invariant, $"<b class=\"sp-t\">{Escaped(pick.Row.Ticker)}</b>{(pick.Row.Distance?.Name is { Length: > 0 } company ? Formatted($"<span class=\"sp-co\">{Escaped(company)}</span>") : string.Empty)}");
            body.Append(Invariant, $"<span class=\"sp-why\">{Escaped(WhyWords(family, pick.Why))}</span>");
            body.Append(Invariant, $"<span class=\"sp-p\" data-buy=\"{Stored(buy)}\">Buy {Escaped(Dollars(buy))}</span><span class=\"sp-p\" data-stop=\"{Stored(stop)}\">Stop {Escaped(Dollars(stop))}</span><span class=\"sp-p\" data-target=\"{Stored(aimed)}\">Target {Escaped(target)}</span></a></li>");
        }

        return body.Append("</ul>").ToString();
    }

    // The stocks its rules met again tonight, a line each: the stock, whether it is held and since when or its order still
    // waits, and its move since it was bought where held, the line opening its plan.
    string PanelAgain(IReadOnlyList<DecisionCardView> cards, string universeWord)
    {
        var again = cards.Where(card => card.HeldAgain is not null).ToArray();

        if (again.Length == 0)
        {
            return string.Empty;
        }

        var body = new StringBuilder();

        body.Append(Invariant, $"<div class=\"sp-again\" data-held-again=\"{again.Length}\"><p class=\"sp-h\">{MetAgainHeading} ({again.Length})</p><ul class=\"sp-rows\" data-rows=\"again\">");

        foreach (var card in again)
        {
            var held = card.HeldAgain!;
            var state = held.Bought
                ? "already held since " + (held.BoughtOn ?? held.Night)?.ToString("d MMM", Invariant)
                : "order of " + held.Night?.ToString("d MMM", Invariant) + " still waits";
            var since = held.Bought && held.Fill is { } paid && paid > 0 && held.LastClose is { } close
                ? Formatted($"<span class=\"sp-p\">{(close - paid) / paid * 100:+0.0;-0.0;0.0}% since bought</span>")
                : string.Empty;

            body.Append(Invariant, $"<li><a class=\"sp-line\" data-plan-line data-ticker=\"{Escaped(card.Ticker)}\" href=\"{Escaped(PlanHref(card.Ticker, universeWord))}\"><b class=\"sp-t\">{Escaped(card.Ticker)}</b><span class=\"sp-why\">{Escaped(state)}</span>{since}</a></li>");
        }

        return body.Append("</ul></div>").ToString();
    }

    // What a drawn page breaks of the two rules: each full card drawn outside a plan's article, and each setup panel, or fold
    // inside one, drawn open where the page's address named no setup. Each named by its stock or its setup.
    public static IReadOnlyList<string> CardsOutsideThePlan(string page, bool setupNamed = false)
    {
        var found = new List<string>();
        var plans = System.Text.RegularExpressions.Regex.Matches(page, "<article class=\"pl-plan").Select(match => (match.Index, End: Closing(page, match.Index, "article"))).ToArray();

        foreach (System.Text.RegularExpressions.Match card in System.Text.RegularExpressions.Regex.Matches(page, "<div class=\"decision-card[^\"]*\"[^>]*data-ticker=\"([^\"]+)\""))
        {
            if (!plans.Any(plan => card.Index > plan.Index && card.Index < plan.End))
            {
                found.Add("a full card outside the plan view: " + card.Groups[1].Value);
            }
        }

        foreach (System.Text.RegularExpressions.Match row in System.Text.RegularExpressions.Regex.Matches(page, "<tr class=\"st-open\" data-for=\"([^\"]+)\"([^>]*)>"))
        {
            if (!setupNamed && !row.Groups[2].Value.Contains(" hidden", StringComparison.Ordinal))
            {
                found.Add("a setup panel open without a click: " + row.Groups[1].Value);
            }
        }

        foreach (System.Text.RegularExpressions.Match panel in System.Text.RegularExpressions.Regex.Matches(page, "<div class=\"st-panel[^\"]*\" data-family=\"([^\"]+)\""))
        {
            var inside = page[panel.Index..Closing(page, panel.Index, "div")];

            if (System.Text.RegularExpressions.Regex.IsMatch(inside, "<details\\b[^>]*\\sopen(?=[\\s>=])"))
            {
                found.Add("a fold open without a click: " + panel.Groups[1].Value);
            }
        }

        return found;
    }

    // Where the element opening at a place ends, its own closing tag counted past every one of its kind it holds.
    static int Closing(string page, int at, string tag)
    {
        var depth = 0;

        foreach (System.Text.RegularExpressions.Match mark in System.Text.RegularExpressions.Regex.Matches(page[at..], "<(/?)" + tag + "\\b"))
        {
            depth += mark.Groups[1].Value.Length == 0 ? 1 : -1;

            if (depth == 0)
            {
                return at + mark.Index;
            }
        }

        return page.Length;
    }

    static string Stored(decimal? price) => price is { } value ? value.ToString(Invariant) : "none";

    static string Dollars(decimal? price) => price is { } value ? "$" + Figures.Price(value) : "none";

    // A plan's one exit in words, as the card's own target cell states it.
    static string TargetWords(PlanSaleView sale)
    {
        var after = Formatted($"{sale.Cap} trading days");

        if (sale.StagesAndHalves)
        {
            return string.Join(", ", sale.Parts().Select(part => part.Price is { } price ? part.Words + " at $" + Figures.Price(price) : part.LastWords + " after " + after));
        }

        return sale.Kind == EquityBrief.Core.Loop.PlanPoints.StagedSales && sale.Stages.Count > 0
            ? PlanSaleView.SharesOf(sale.Stages.Count).Each + " at each of " + string.Join(", ", sale.Stages.Select(stage => "$" + Figures.Price(stage))) + (sale.TakeProfit is { } rest ? ", the rest at $" + Figures.Price(rest.Price) : string.Empty)
            : sale.TakeProfit is { } taking && (sale.Target is null || sale.OnTimeAlone)
                ? Dollars(taking.Price)
                : sale.Target is { } target && !sale.OnTimeAlone
                    ? Dollars(target)
                    : sale.OnTimeAlone ? "none: sells after " + after : sale.Trail is not null ? "rising stop, no target" : sale.RsiAbove is not null ? "sells on a bounce" : "none above tonight's close: holds for " + after;
    }
}
