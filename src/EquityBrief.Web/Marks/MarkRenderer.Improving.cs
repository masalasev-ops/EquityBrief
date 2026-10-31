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
    string Rule,
    EquityBrief.Core.Families.ImprovingSweepSetting? Setting = null,
    DateOnly? LiveSince = null,
    QualityFixedView? Fixed = null);

// see: The Fundamentals family buys a company whose newly read filing lifts its scorecard to eight in nine, and flags one whose release reads better
// see: A redesigned page speaks in the reader's words: every card opens on a sentence from its numbers, and no internal term reaches the screen outside the glossary
public sealed partial class MarkRenderer
{
    // The rows the "Improving fastest" tab draws open; the rest sit behind a fold.
    public const int ImprovingShown = 10;

    // The stocks the "Strongest businesses" tab draws.
    public const int StrongestShown = 10;

    // What a row of a member whose scorecard read no check says in its score's place.
    public const string NoChecksRead = "No checks could be read from its filings";

    // The trading days "Reporting this week" counts after the night.
    public const int ReportingDays = 5;

    // A row's status, in the reader's words.
    public const string BoughtWords = "Bought at the next trading day's open";
    public const string FlaggedWords = "Flagged, waiting for its filing";
    public const string ImprovedWords = "Improved, short of the bar";
    public const string HeldWords = "Bought earlier";

    // What Quality at a fair price says of a strong business, in the reader's words, its book being its holdings.
    public const string InTheQualityBook = "Among Quality at a fair price's holdings";
    public const string AboveItsUsualPrice = "It trades above its usual price, so Quality at a fair price does not hold it";

    public const string WorkedBy = "Worked by code from each company's filings as first filed with the SEC and from four answers the local model reads in each results release. No model judges the stock.";

    // Beneath what the card finds, how soon it reads what a company sends.
    public const string CheckedNightly = "Checked every night, so a new filing or results release is read by the next night.";

    // What each column of the "Improving fastest" tab's table holds, in the order the columns are drawn.
    static readonly (string Heading, string Says)[] ImprovingHeadings =
    [
        ("Stock", "The ticker opens the stock's chart page, and the company is named beneath it."),
        ("Business now", "How many of the checks that apply its newest filing passes, and how many the filing before passed."),
        ("What changed on its new report", "What its newly read filing shows that the one before did not, with the figures it filed; for a flagged stock, what its results release reads."),
        ("Tonight", "What this setup does with the stock tonight, and why."),
    ];

    // What each column of the "Strongest businesses" tab's table holds.
    static readonly (string Heading, string Says)[] StrongestHeadings =
    [
        ("Stock", "The ticker opens the stock's chart page, and the company is named beneath it."),
        ("Business now", "How many of the checks that apply its newest filing passes."),
        ("The business", "Its sales against a year before and how that pace moved, and its gross profit against its assets."),
        ("Its price", "Its price against its own last twelve quarters, and what Quality at a fair price says of it."),
    ];

    // The card's header, as a setup card's: its name, not yet tested, tonight's picks, what it finds and how soon it reads.
    public string ImprovingCardHead(ImprovingCardView card)
    {
        var body = new StringBuilder();

        body.Append("<header class=\"fam-head\" data-family=\"improving\"><span class=\"fam-tag\" data-family=\"improving\"><span class=\"fam-swatch\" data-family=\"improving\"></span>");
        body.Append(Escaped(EquityBrief.Core.Families.ImprovingRule.Label)).Append("</span>");
        body.Append(card.LiveSince is { } since
            ? FormattableString.Invariant($" <span class=\"status-pill live\" data-live-since=\"{DayOf(since)}\">Live since {since:d MMM yyyy}</span>")
            : FormattableString.Invariant($" <span class=\"status-pill provisional\" title=\"{Escaped(EquityBrief.Core.Families.SetupFamilies.ProvisionalStatus)}\">Not yet tested</span>"));
        body.Append(Invariant, $" <span class=\"fam-count\">{Escaped(card.Bought == 1 ? "1 pick tonight" : Formatted($"{card.Bought} picks tonight"))}</span></header>");
        body.Append(Invariant, $"<p class=\"setup-about\">{Escaped(EquityBrief.Core.Families.ImprovingRule.Description)}<br>{Escaped(CheckedNightly)}</p>");

        return body.ToString();
    }

    // The card, as the operator's mockup of 2026-10-26 draws it: its sentence, four tiles, a tab bar over the two tabs' tables
    // each opening on what it lists, and how it picks and how it will be tested behind a fold.
    public string ImprovingCard(ImprovingCardView card)
    {
        var settings = SettingsOf(card);
        var body = new StringBuilder();

        body.Append(Invariant, $"<section class=\"family-card improving-card\" data-family=\"improving\" data-index=\"{Escaped(card.Index)}\" data-bought=\"{card.Bought}\" data-flagged=\"{card.Flagged}\" data-improving=\"{card.Improving.Count}\" data-holding=\"{card.Holding}\">");
        body.Append(Invariant, $"<p class=\"card-summary\" data-summary>{Escaped(ImprovingSummary(card))}</p>");

        // From 22.15 a frozen card's line from its own recorded answer, as every frozen card draws it, and beneath it in
        // the same breath what its history was read with.
        // see: Each index's Fundamentals rule is frozen on the operator's answers of 2026-10-28 with its one-step neighbours as versions
        if (card.Fixed is { } fixedOn)
        {
            body.Append(Invariant, $"<p class=\"family-frozen improving-fixed\" data-fixed=\"{DayOf(fixedOn.FixedOn)}\" data-trades=\"{fixedOn.Holdings}\" data-edge=\"{(fixedOn.Edge is { } edge ? edge.ToString("R", Invariant) : "none")}\" ");
            body.Append(Invariant, $"data-years=\"{fixedOn.YearsWithHoldings}\" data-ahead=\"{fixedOn.YearsAhead}\" data-beat-luck=\"{(fixedOn.BeatLuck is { } beat ? Flag(beat) : "none")}\" data-tester-passed=\"{(fixedOn.TesterPassed is { } passed ? Flag(passed) : "none")}\">");
            body.Append(Escaped(ImprovingFixedLine(fixedOn))).Append(' ').Append(Escaped(EquityBrief.Core.Families.ImprovingRule.ReadingsCaveat)).Append("</p>");
        }

        if (!card.Read || card.Fault is not null)
        {
            body.Append(ImprovingRuleFold(card));

            return body.Append("</section>").ToString();
        }

        body.Append(ImprovingTiles(card));

        // The tabs: two radio inputs the stylesheet shows one panel under, their labels drawn as a tab bar.
        body.Append("<input type=\"radio\" class=\"improving-tab\" name=\"improving-tab\" id=\"improving-tab-fastest\" value=\"fastest\" checked>");
        body.Append("<input type=\"radio\" class=\"improving-tab\" name=\"improving-tab\" id=\"improving-tab-strongest\" value=\"strongest\">");
        body.Append("<nav class=\"improving-tabs\" aria-label=\"Choose a list\">");
        body.Append("<label class=\"improving-tabname\" for=\"improving-tab-fastest\" data-tab=\"fastest\">Improving fastest</label>");
        body.Append("<label class=\"improving-tabname\" for=\"improving-tab-strongest\" data-tab=\"strongest\">Strongest businesses</label>");
        body.Append("</nav>");

        body.Append(Invariant, $"<div class=\"improving-panel\" data-tab=\"fastest\" data-rows=\"{card.Improving.Count}\">");
        var setting = card.Setting ?? EquityBrief.Core.Families.ImprovingGrid.Starting;

        body.Append(Invariant, $"<p class=\"improving-lead\">Companies whose business got better the most on their newest report, whatever their price. A company is bought when its filing lifts it to at least {settings.BuyInNine} of the 9 checks{(setting.Faster ? " with sales growing faster than the quarter before" : string.Empty)}{(setting.FlagFirst ? ", a flag standing the night before" : string.Empty)}.</p>");

        if (card.Improving.Count == 0)
        {
            body.Append("<p class=\"degraded\" data-rows=\"none\">No company's business improved on a new report tonight, and none waits on its filing after a better results release.</p>");
        }
        else
        {
            body.Append(ImprovingTable(card.Improving.Take(ImprovingShown).ToArray()));

            if (card.Improving.Count > ImprovingShown)
            {
                var rest = card.Improving.Count - ImprovingShown;

                body.Append(Invariant, $"<details class=\"fold improving-more\" data-more=\"{rest}\"><summary>{Escaped(rest == 1 ? "1 more" : Formatted($"{rest} more"))}</summary>");
                body.Append(ImprovingTable(card.Improving.Skip(ImprovingShown).ToArray()));
                body.Append("</details>");
            }
        }

        body.Append("</div>");
        body.Append(Invariant, $"<div class=\"improving-panel\" data-tab=\"strongest\" data-rows=\"{card.Strongest.Count}\">");
        body.Append(Invariant, $"<p class=\"improving-lead\">The index's {card.Strongest.Count} highest scores tonight, whatever their price, ties broken by gross profit against assets.</p>");

        if (card.Strongest.Count == 0)
        {
            body.Append("<p class=\"degraded\" data-rows=\"none\">No stock on the index holds a score tonight.</p>");
        }
        else
        {
            body.Append(Invariant, $"<div class=\"tbl-wrap\"><table class=\"list-table improving-table strongest-table\" data-rows=\"{card.Strongest.Count}\"><thead><tr>");

            foreach (var (heading, says) in StrongestHeadings)
            {
                body.Append(TippedHeading(heading, says, null));
            }

            body.Append("</tr></thead><tbody>");

            foreach (var cell in card.Strongest)
            {
                body.Append(Invariant, $"<tr class=\"improving-row\" data-ticker=\"{Escaped(cell.Ticker)}\">");
                body.Append(ImprovingStock(cell.Ticker, cell.Company));
                body.Append(Invariant, $"<td class=\"improving-score\"><b>{cell.Passed} of {cell.Applied}</b></td>");
                body.Append(Invariant, $"<td class=\"improving-changed\">{Escaped(cell.Business)}</td>");
                body.Append(Invariant, $"<td class=\"improving-status\">{Escaped(cell.Price)}");

                if (cell.Quality is { } quality)
                {
                    body.Append(Invariant, $"<span class=\"improving-quality\" data-quality=\"{(quality == InTheQualityBook ? "held" : "dear")}\">{Escaped(quality)}.</span>");
                }

                body.Append("</td></tr>");
            }

            body.Append("</tbody></table></div>");
        }

        body.Append("</div>");
        body.Append(ImprovingRuleFold(card));

        return body.Append("</section>").ToString();
    }

    // The "Improving fastest" tab's table: each stock, its business now against the filing before, what changed on its
    // new report, and tonight's status in its colour with the line beneath it.
    static string ImprovingTable(IReadOnlyList<ImprovingRowCell> cells)
    {
        var body = new StringBuilder();

        body.Append(Invariant, $"<div class=\"tbl-wrap\"><table class=\"list-table improving-table\" data-rows=\"{cells.Count}\"><thead><tr>");

        foreach (var (heading, says) in ImprovingHeadings)
        {
            body.Append(TippedHeading(heading, says, null));
        }

        body.Append("</tr></thead><tbody>");

        foreach (var cell in cells)
        {
            var state = cell.Status switch
            {
                BoughtWords => "bought",
                FlaggedWords => "flagged",
                HeldWords => "earlier",
                _ => "improved",
            };
            var now = cell.Applied == 0
                ? Formatted($"<b>{NoChecksRead}</b>")
                : state == "flagged"
                ? Formatted($"<b>filing not in yet</b><span class=\"improving-from\">was {cell.Passed} of {cell.Applied}</span>")
                : cell is { BeforePassed: { } before, BeforeApplied: { } applied }
                    ? Formatted($"<b>{cell.Passed} of {cell.Applied}</b><span class=\"improving-from\">up from {before}{(applied != cell.Applied ? Formatted($" of {applied}") : string.Empty)}</span>")
                    : Formatted($"<b>{cell.Passed} of {cell.Applied}</b>");

            body.Append(Invariant, $"<tr class=\"improving-row\" data-ticker=\"{Escaped(cell.Ticker)}\" data-status=\"{Escaped(cell.Status)}\">");
            body.Append(ImprovingStock(cell.Ticker, cell.Company));
            body.Append(Invariant, $"<td class=\"improving-score\">{now}</td>");
            body.Append(Invariant, $"<td class=\"improving-changed\">{Escaped(cell.Changed)}</td>");
            body.Append(Invariant, $"<td class=\"improving-status\" data-state=\"{state}\"><b>{Escaped(cell.Status)}</b><span>{Escaped(cell.Line)}</span></td></tr>");
        }

        return body.Append("</tbody></table></div>").ToString();
    }

    // A table's stock cell: the ticker opening its chart page and the company beneath it.
    static string ImprovingStock(string ticker, string? company) =>
        Formatted($"<td class=\"c-nm\"><a class=\"name-link\" href=\"#/chart/{Uri.EscapeDataString(ticker)}\">{Escaped(ticker)}</a>{(company is { Length: > 0 } named ? Formatted($"<span class=\"co\">{Escaped(named)}</span>") : string.Empty)}</td>");

    // The card's sentence from tonight's figures.
    // The three dials the card's sentences state, read off the rule's own setting where its night states one.
    static EquityBrief.Core.Families.ImprovingSettings SettingsOf(ImprovingCardView card) =>
        card.Setting is { } setting ? new(setting.BuyInNine, setting.HoldInNine, setting.Sessions) : EquityBrief.Core.Families.ImprovingRule.Starting;

    // A frozen Fundamentals card's line, the one every frozen card draws, its record counted in trades.
    public static string ImprovingFixedLine(QualityFixedView fixedOn) => FixedLine(fixedOn, "trade");

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
        var opens = OpenDay(card.Night);
        var bought = card.Bought == 1 ? "1 is bought at " + opens + " open" : Formatted($"{card.Bought} are bought at {opens} open");
        var flagged = card.Flagged == 1
            ? "1 more is flagged from its results release and waits for its filing"
            : Formatted($"{card.Flagged} more are flagged from their results release and wait for their filing");

        return improved == 0 ? opening : opening + " " + bought + "; " + flagged + ".";
    }

    // The four tiles, each a count with a line beneath: bought at tomorrow's open, flagged and waiting for the filing, the
    // trades held with their change since bought beside the index's stocks, and the index's stocks reporting this week.
    static string ImprovingTiles(ImprovingCardView card)
    {
        static string Percent(double? figure) =>
            figure is { } held ? SignedFigure(held * 100, 1, Formatted($"{Unsigned(held * 100, 1):+0.0;-0.0;0.0}%")) : "<span class=\"degraded\">not read</span>";

        var body = new StringBuilder();

        body.Append(Invariant, $"<div class=\"hw-tiles improving-tiles\" data-bought=\"{card.Bought}\" data-flagged=\"{card.Flagged}\" data-holding=\"{card.Holding}\" data-holding-since=\"{(card.HoldingSince is { } since ? since.ToString("R", Invariant) : "none")}\" data-members-since=\"{(card.MembersSince is { } members ? members.ToString("R", Invariant) : "none")}\" data-reporting=\"{(card.Reporting is { } reporting ? reporting.ToString(Invariant) : "none")}\">");
        body.Append(Invariant, $"<div class=\"hw-tile\"><span class=\"tile-label\">Bought at {OpenDay(card.Night)} open</span><b data-state=\"bought\">{card.Bought}</b><span>{(card.Bought switch { 0 => "no new filing lifted one over the bar", 1 => "its filing confirmed the improvement", _ => "their filing confirmed the improvement" })}</span></div>");
        body.Append(Invariant, $"<div class=\"hw-tile\"><span class=\"tile-label\">Flagged, waiting for the filing</span><b data-state=\"flagged\">{card.Flagged}</b><span>results release read; filing usually within weeks</span></div>");
        body.Append(card.Holding == 0
            ? "<div class=\"hw-tile\"><span class=\"tile-label\">Holding</span><b>0</b><span>none bought yet</span></div>"
            : FormattableString.Invariant($"<div class=\"hw-tile\"><span class=\"tile-label\">Holding</span><b>{card.Holding}</b><span>{Percent(card.HoldingSince)} since bought, the index's stocks {Percent(card.MembersSince)}</span></div>"));
        body.Append(Invariant, $"<div class=\"hw-tile\"><span class=\"tile-label\">Reporting this week</span><b>{(card.Reporting is { } count ? count.ToString(Invariant) : "not read")}</b><span>of the index's stocks, in the next {ReportingDays} trading days</span></div>");
        body.Append("</div>");

        return body.ToString();
    }

    // How it picks and how it will be tested, in the reader's words, behind a fold.
    static string ImprovingRuleFold(ImprovingCardView card)
    {
        var settings = SettingsOf(card);
        var setting = card.Setting ?? EquityBrief.Core.Families.ImprovingGrid.Starting;
        var body = new StringBuilder();

        body.Append("<details class=\"fold fam-rule improving-rule\"><summary>How it picks, and how it will be tested</summary><div class=\"fold-b\">");
        body.Append("<p class=\"record-said\">Flagged: a company whose results release, sent since its last filing, reads results better than expected and an outlook raised or kept. A flag buys nothing.</p>");
        body.Append(Invariant, $"<p class=\"record-said\">Bought at the next open: a company whose new filing, read tonight for the first time, lifts its score to at least {settings.BuyInNine} of the 9 checks that apply{(setting.Rise ? " and above the score of the filing before it" : string.Empty)}{(setting.Faster ? ", while its sales over a year grow faster than in the quarter before" : string.Empty)}. {(setting.FlagFirst ? "A flag must have stood the night before." : "A flag is not needed.")}</p>");
        body.Append(Invariant, $"<p class=\"record-said\">Kept while its score stays at {settings.HoldInNine} of 9 or better. Sold at the close of the night a new filing drops it below {settings.HoldInNine}, at the close of its {settings.Sessions}th trading day, or at its last close in the index, whichever comes first. No stop and no target.</p>");
        body.Append("<p class=\"record-said\">Judged by its return against an equal-weight hold of the index's stocks over the same days, in points, after its trading costs. One trade a stock; a stock it buys may also be bought by another setup.</p>");
        body.Append(card.LiveSince is { } live
            ? FormattableString.Invariant($"<p class=\"record-said\">Its settings were fixed on {live:d MMM yyyy} after its test over the past, and its record counts from then. {EquityBrief.Core.Families.ImprovingRule.ReadingsCaveat}</p>")
            : "<p class=\"record-said\">It has not been tested yet, so its picks count toward nothing and ask for no report. Its test over the past comes next, and its settings are fixed only on that answer.</p>");
        body.Append(Invariant, $"<p class=\"record-said\" data-rule=\"{Escaped(card.Rule)}\">{Escaped(WorkedBy)}</p>");
        body.Append("</div></details>");

        return body.ToString();
    }
}
