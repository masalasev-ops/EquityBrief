using System.Text;

namespace EquityBrief.Web.Marks;

// One stock on the Fundamentals card's "Improving fastest" tab: the stock, its company, its score tonight and where its
// newly read filing lifted it from, what changed in one sentence, its status and the line beneath it.
public sealed record ImprovingRowCell(string Ticker, string? Company, int Passed, int Applied, int? BeforePassed, int? BeforeApplied, string Changed, string Status, string Line);

// One stock on its "Strongest businesses" tab: the stock, its company, its score tonight, its business in one sentence, its
// price against its own past, and what the quality book says of it, none where it says nothing.
public sealed record StrongestCell(string Ticker, string? Company, int Passed, int Applied, string Business, string Price, string? Quality);

// The Fundamentals family's card on a night: the index it reads, whether its step read the night and the failure that
// stopped its part where one did; tonight's buys, flags and stocks whose newly read filing raised the score; the trades it
// holds with their change since bought beside the index's stocks over the same nights; the stocks reporting in the next
// five trading days; the two tabs' rows; and its rule in words.
public sealed record ImprovingCardView(
    string Index,
    DateOnly Night,
    bool Read,
    string? Fault,
    int Bought,
    int Flagged,
    int Holding,
    double? HoldingSince,
    double? MembersSince,
    int? Reporting,
    IReadOnlyList<ImprovingRowCell> Improving,
    IReadOnlyList<StrongestCell> Strongest,
    string Rule);

// see: The Fundamentals family buys a company whose newly read filing lifts its scorecard to eight in nine, and flags one whose release reads better
// see: A redesigned page speaks in the reader's words: every card opens on a sentence from its numbers, and no internal term reaches the screen outside the glossary
public sealed partial class MarkRenderer
{
    // The rows the "Improving fastest" tab draws open; the rest sit behind a fold.
    public const int ImprovingShown = 10;

    // The stocks the "Strongest businesses" tab draws.
    public const int StrongestShown = 10;

    // The trading days "Reporting this week" counts after the night.
    public const int ReportingDays = 5;

    // A row's status, in the reader's words.
    public const string BoughtWords = "Bought at tomorrow's open";
    public const string FlaggedWords = "Flagged, waiting for its filing";
    public const string ImprovedWords = "Improved, short of the bar";
    public const string HeldWords = "Bought earlier";

    // What Quality at a fair price says of a strong business, in the reader's words, its book being its holdings.
    public const string InTheQualityBook = "Among Quality at a fair price's holdings";
    public const string AboveItsUsualPrice = "It trades above its usual price, so Quality at a fair price does not hold it";

    public const string WorkedBy = "Worked by code from each company's filings as first filed with the SEC and from four answers the local model reads in each results release. No model judges the stock.";

    // The card's header, as a setup card's: its name, not yet tested, tonight's picks, and what it finds.
    public string ImprovingCardHead(ImprovingCardView card)
    {
        var body = new StringBuilder();

        body.Append("<header class=\"fam-head\" data-family=\"improving\"><span class=\"fam-tag\" data-family=\"improving\"><span class=\"fam-swatch\" data-family=\"improving\"></span>");
        body.Append(Escaped(EquityBrief.Core.Families.ImprovingRule.Label)).Append("</span>");
        body.Append(Invariant, $" <span class=\"status-pill provisional\" title=\"{Escaped(EquityBrief.Core.Families.SetupFamilies.ProvisionalStatus)}\">Not yet tested</span>");
        body.Append(Invariant, $" <span class=\"fam-count\">{Escaped(card.Bought == 1 ? "1 pick" : Formatted($"{card.Bought} picks"))}</span></header>");
        body.Append(Invariant, $"<p class=\"setup-about\">{Escaped(EquityBrief.Core.Families.ImprovingRule.Description)}</p>");

        return body.ToString();
    }

    // The card: its sentence, four tiles, the two tabs, and how it picks and how it will be tested behind a fold.
    public string ImprovingCard(ImprovingCardView card)
    {
        var body = new StringBuilder();

        body.Append(Invariant, $"<section class=\"family-card improving-card\" data-family=\"improving\" data-index=\"{Escaped(card.Index)}\" data-bought=\"{card.Bought}\" data-flagged=\"{card.Flagged}\" data-improving=\"{card.Improving.Count}\" data-holding=\"{card.Holding}\">");
        body.Append(Invariant, $"<p class=\"card-summary\" data-summary>{Escaped(ImprovingSummary(card))}</p>");

        if (!card.Read || card.Fault is not null)
        {
            body.Append(ImprovingRuleFold(card));

            return body.Append("</section>").ToString();
        }

        body.Append(ImprovingTiles(card));

        // The tabs: two radio inputs the stylesheet shows one panel under, their labels drawn as chips.
        body.Append("<input type=\"radio\" class=\"improving-tab\" name=\"improving-tab\" id=\"improving-tab-fastest\" value=\"fastest\" checked>");
        body.Append("<input type=\"radio\" class=\"improving-tab\" name=\"improving-tab\" id=\"improving-tab-strongest\" value=\"strongest\">");
        body.Append("<nav class=\"improving-tabs\" aria-label=\"Choose a list\">");
        body.Append(Invariant, $"<label class=\"chip\" for=\"improving-tab-fastest\" data-tab=\"fastest\">Improving fastest<span class=\"n\">{card.Improving.Count}</span></label>");
        body.Append(Invariant, $"<label class=\"chip\" for=\"improving-tab-strongest\" data-tab=\"strongest\">Strongest businesses<span class=\"n\">{card.Strongest.Count}</span></label>");
        body.Append("</nav>");

        body.Append(Invariant, $"<div class=\"improving-panel\" data-tab=\"fastest\" data-rows=\"{card.Improving.Count}\">");

        if (card.Improving.Count == 0)
        {
            body.Append("<p class=\"degraded\" data-rows=\"none\">No company's business improved on a new report tonight, and none waits on its filing after a better results release.</p>");
        }
        else
        {
            body.Append(ImprovingList(card.Improving.Take(ImprovingShown).ToArray(), 1));

            if (card.Improving.Count > ImprovingShown)
            {
                var rest = card.Improving.Count - ImprovingShown;

                body.Append(Invariant, $"<details class=\"fold improving-more\" data-more=\"{rest}\"><summary>{Escaped(rest == 1 ? "1 more" : Formatted($"{rest} more"))}</summary>");
                body.Append(ImprovingList(card.Improving.Skip(ImprovingShown).ToArray(), ImprovingShown + 1));
                body.Append("</details>");
            }
        }

        body.Append("</div>");
        body.Append(Invariant, $"<div class=\"improving-panel\" data-tab=\"strongest\" data-rows=\"{card.Strongest.Count}\">");

        if (card.Strongest.Count == 0)
        {
            body.Append("<p class=\"degraded\" data-rows=\"none\">No stock on the index holds a score tonight.</p>");
        }
        else
        {
            body.Append(Invariant, $"<p class=\"family-note\">The index's {card.Strongest.Count} highest scores tonight, ties broken by gross profit against assets.</p><ol class=\"improving-rows strongest-rows\">");

            foreach (var cell in card.Strongest)
            {
                body.Append(Invariant, $"<li class=\"improving-row\" data-ticker=\"{Escaped(cell.Ticker)}\"><span class=\"improving-name\"><a class=\"name-link\" href=\"#/chart/{Uri.EscapeDataString(cell.Ticker)}\">{Escaped(cell.Ticker)}</a>{(cell.Company is { Length: > 0 } company ? Formatted($" <span class=\"co\">{Escaped(company)}</span>") : string.Empty)}</span>");
                body.Append(Invariant, $"<span class=\"improving-score\">{cell.Passed} of {cell.Applied}</span>");
                body.Append(Invariant, $"<span class=\"improving-changed\">{Escaped(cell.Business)} {Escaped(cell.Price)}</span>");

                if (cell.Quality is { } quality)
                {
                    body.Append(Invariant, $"<span class=\"improving-status\" data-quality=\"{(quality == InTheQualityBook ? "held" : "dear")}\">{Escaped(quality)}.</span>");
                }

                body.Append("</li>");
            }

            body.Append("</ol>");
        }

        body.Append("</div>");
        body.Append(ImprovingRuleFold(card));

        return body.Append("</section>").ToString();
    }

    // The rows of the "Improving fastest" tab, numbered from a place.
    static string ImprovingList(IReadOnlyList<ImprovingRowCell> cells, int from)
    {
        var body = new StringBuilder();

        body.Append(Invariant, $"<ol class=\"improving-rows\" start=\"{from}\">");

        foreach (var cell in cells)
        {
            var score = cell is { BeforePassed: { } before, BeforeApplied: { } applied } && cell.Status != FlaggedWords
                ? Formatted($"{cell.Passed} of {cell.Applied}, up from {before}{(applied != cell.Applied ? Formatted($" of {applied}") : string.Empty)}")
                : Formatted($"{cell.Passed} of {cell.Applied}");

            body.Append(Invariant, $"<li class=\"improving-row\" data-ticker=\"{Escaped(cell.Ticker)}\" data-status=\"{Escaped(cell.Status)}\"><span class=\"improving-name\"><a class=\"name-link\" href=\"#/chart/{Uri.EscapeDataString(cell.Ticker)}\">{Escaped(cell.Ticker)}</a>{(cell.Company is { Length: > 0 } company ? Formatted($" <span class=\"co\">{Escaped(company)}</span>") : string.Empty)}</span>");
            body.Append(Invariant, $"<span class=\"improving-score\">{Escaped(score)}</span>");
            body.Append(Invariant, $"<span class=\"improving-changed\">{Escaped(cell.Changed)}</span>");
            body.Append(Invariant, $"<span class=\"improving-status\"><b>{Escaped(cell.Status)}.</b> {Escaped(cell.Line)}</span></li>");
        }

        return body.Append("</ol>").ToString();
    }

    // The card's sentence from tonight's figures.
    static string ImprovingSummary(ImprovingCardView card)
    {
        if (card.Fault is not null)
        {
            return "Its part of the night was not computed, so nothing was bought, sold or flagged tonight.";
        }

        if (!card.Read)
        {
            return "It has read no night yet: it reads its first on the first night after it was built.";
        }

        var improved = card.Improving.Count;
        var opening = improved == 0
            ? "Tonight no company's business improved on a new report."
            : improved == 1
                ? "Tonight 1 company's business improved on a new report."
                : Formatted($"Tonight {improved} companies' businesses improved on a new report.");
        var bought = card.Bought == 1 ? "1 is bought at tomorrow's open" : Formatted($"{card.Bought} are bought at tomorrow's open");
        var flagged = card.Flagged == 1
            ? "1 more is flagged from its results release and waits for its filing"
            : Formatted($"{card.Flagged} more are flagged from their results release and wait for their filing");

        return improved == 0 ? opening : opening + " " + bought + "; " + flagged + ".";
    }

    // The four tiles: bought at tomorrow's open, flagged and waiting for the filing, holding with its change since bought
    // beside the index's stocks, and reporting this week.
    static string ImprovingTiles(ImprovingCardView card)
    {
        static string Percent(double? figure) =>
            figure is { } held ? SignedFigure(held * 100, 1, Formatted($"{Unsigned(held * 100, 1):+0.0;-0.0;0.0}%")) : "<span class=\"degraded\">not read</span>";

        var body = new StringBuilder();

        body.Append(Invariant, $"<div class=\"hw-tiles improving-tiles\" data-bought=\"{card.Bought}\" data-flagged=\"{card.Flagged}\" data-holding=\"{card.Holding}\" data-holding-since=\"{(card.HoldingSince is { } since ? since.ToString("R", Invariant) : "none")}\" data-members-since=\"{(card.MembersSince is { } members ? members.ToString("R", Invariant) : "none")}\" data-reporting=\"{(card.Reporting is { } reporting ? reporting.ToString(Invariant) : "none")}\">");
        body.Append(Invariant, $"<div class=\"hw-tile\"><span class=\"tile-label\">Bought at tomorrow's open</span><b>{card.Bought}</b><span>{(card.Bought == 1 ? "stock" : "stocks")}</span></div>");
        body.Append(Invariant, $"<div class=\"hw-tile\"><span class=\"tile-label\">Flagged, waiting for the filing</span><b>{card.Flagged}</b><span>from their results release</span></div>");
        body.Append(card.Holding == 0
            ? "<div class=\"hw-tile\"><span class=\"tile-label\">Holding</span><b>0</b><span>none bought yet</span></div>"
            : FormattableString.Invariant($"<div class=\"hw-tile\"><span class=\"tile-label\">Holding {card.Holding}</span><b>{Percent(card.HoldingSince)}</b><span>since bought; the index's stocks {Percent(card.MembersSince)}</span></div>"));
        body.Append(Invariant, $"<div class=\"hw-tile\"><span class=\"tile-label\">Reporting this week</span><b>{(card.Reporting is { } count ? count.ToString(Invariant) : "not read")}</b><span>in the next {ReportingDays} trading days</span></div>");
        body.Append("</div>");

        return body.ToString();
    }

    // How it picks and how it will be tested, in the reader's words, behind a fold.
    static string ImprovingRuleFold(ImprovingCardView card)
    {
        var settings = EquityBrief.Core.Families.ImprovingRule.Starting;
        var body = new StringBuilder();

        body.Append("<details class=\"fold fam-rule improving-rule\"><summary>How it picks, and how it will be tested</summary><div class=\"fold-b\">");
        body.Append("<p class=\"record-said\">Flagged: a company whose results release, sent since its last filing, reads results better than expected and an outlook raised or kept. A flag buys nothing.</p>");
        body.Append(Invariant, $"<p class=\"record-said\">Bought at the next open: a company whose new filing, read tonight for the first time, lifts its score to at least {settings.BuyInNine} of the 9 checks that apply and above the score of the filing before it, while its sales over a year grow faster than in the quarter before. A flag is not needed.</p>");
        body.Append(Invariant, $"<p class=\"record-said\">Kept while its score stays at {settings.HoldInNine} of 9 or better. Sold at the close of the night a new filing drops it below {settings.HoldInNine}, at the close of its {settings.Sessions}th trading day, or at its last close in the index, whichever comes first. No stop and no target.</p>");
        body.Append("<p class=\"record-said\">Judged by its return against an equal-weight hold of the index's stocks over the same days, in points, after its trading costs. One trade a stock; a stock it buys may also be bought by another setup.</p>");
        body.Append("<p class=\"record-said\">It has not been tested yet, so its picks count toward nothing and ask for no report. Its test over the past comes next, and its settings are fixed only on that answer.</p>");
        body.Append(Invariant, $"<p class=\"record-said\" data-rule=\"{Escaped(card.Rule)}\">{Escaped(WorkedBy)}</p>");
        body.Append("</div></details>");

        return body.ToString();
    }
}
