using System.Text;

namespace EquityBrief.Web.Marks;

// One holding on Quality at a fair price's card: the stock, its company, the night it was chosen, its score tonight as
// checks passed of the checks that applied, its valuation's place tonight, its change since it was bought, none before
// its buy, and tonight's line in the reader's words.
public sealed record QualityHoldingCell(string Ticker, string? Company, DateOnly ChosenOn, int? Passed, int? Applied, double? Place, double? SinceBought, string Tonight);

// One stock next in line: qualifying tonight and not held, in the family's order.
public sealed record QualityNextCell(string Ticker, string? Company, int Passed, int Applied, double? Place);

// One holding sold since the last reshuffle: the stock, the close it was sold at and why.
public sealed record QualitySoldCell(string Ticker, DateOnly On, string Reason);

// Quality at a fair price's card on a night: the index it reads, whether its step read the night and the failure that
// stopped its part where one did, the holdings it keeps, the last reshuffle and the next with the trading days to it, its
// holdings' change since the last reshuffle beside an equal-weighted hold of the index's members over the same nights,
// each holding, what the last reshuffle chose and what was sold since, the next names in line, and its rule in words.
public sealed record QualityCardView(
    string Index,
    DateOnly Night,
    bool Read,
    string? Fault,
    int Holdings,
    DateOnly? LastReshuffle,
    DateOnly? NextReshuffle,
    int? DaysToNext,
    double? HoldingsSince,
    double? MembersSince,
    IReadOnlyList<QualityHoldingCell> Cells,
    IReadOnlyList<string> Chosen,
    IReadOnlyList<QualitySoldCell> Sold,
    IReadOnlyList<QualityNextCell> Next,
    string Rule,
    DateOnly? LiveSince = null,
    QualityFixedView? Fixed = null);

// From 22.11 a fixed card's line, read off the answer its history test recorded before its freeze: the day its settings
// were fixed, the first year its test read, its holdings, its edge a holding after costs as a fraction of the buy, the
// years it had holdings in and those it came out ahead in, and whether more settings met the floors than luck alone
// passes and whether its year-by-year test passed, none where the answer states neither.
public sealed record QualityFixedView(DateOnly FixedOn, int FromYear, int Holdings, double? Edge, int YearsWithHoldings, int YearsAhead, bool? BeatLuck, bool? TesterPassed);

// see: Quality at a fair price holds the companies the scorecard rates strongest that are not dear against their own history, reshuffled on each month's first session
// see: A redesigned page speaks in the reader's words: every card opens on a sentence from its numbers, and no internal term reaches the screen outside the glossary
public sealed partial class MarkRenderer
{
    // What each column of the card's holdings holds, in the order the columns are drawn.
    public static IReadOnlyList<(string Heading, string Says)> QualityHeadings { get; } =
    [
        ("Stock", "The ticker opens the stock's chart."),
        ("Score", "How many of the fundamentals checks its filings passed tonight, of the checks that apply to it."),
        ("Price against its own past", "How cheap its price over earnings stands against its own last 12 quarters."),
        ("Since bought", "Its change since it was bought at the open after the reshuffle that chose it."),
        ("Tonight", "Whether it still qualifies, and what happens at the next reshuffle if it does not."),
    ];

    // The names the card draws next in line.
    public const int QualityNextShown = 5;

    // The card's header, as a setup card's: its name, not yet tested, its holdings, and what it buys.
    public string QualityCardHead(QualityCardView card)
    {
        var body = new StringBuilder();

        body.Append("<header class=\"fam-head\" data-family=\"quality\"><span class=\"fam-tag\" data-family=\"quality\"><span class=\"fam-swatch\" data-family=\"quality\"></span>");
        body.Append(Escaped(EquityBrief.Core.Families.QualityRule.Label)).Append("</span>");
        body.Append(card.LiveSince is { } since
            ? FormattableString.Invariant($" <span class=\"status-pill live\" data-live-since=\"{DayOf(since)}\">Live since {since:d MMM yyyy}</span>")
            : FormattableString.Invariant($" <span class=\"status-pill provisional\" title=\"{Escaped(EquityBrief.Core.Families.SetupFamilies.ProvisionalStatus)}\">Not yet tested</span>"));
        body.Append(Invariant, $" <span class=\"fam-count\">{Escaped(card.Cells.Count == 1 ? "1 holding" : Formatted($"{card.Cells.Count} holdings"))}</span></header>");
        body.Append(Invariant, $"<p class=\"setup-about\">{Escaped(EquityBrief.Core.Families.QualityRule.Description)}</p>");

        return body.ToString();
    }

    // The card: its sentence, three tiles, one row a holding, what the last reshuffle chose and what was sold since, then
    // the next names in line and the full rule behind folds.
    public string QualityCard(QualityCardView card)
    {
        var body = new StringBuilder();

        body.Append(Invariant, $"<section class=\"family-card quality-card\" data-family=\"quality\" data-index=\"{Escaped(card.Index)}\" data-holdings=\"{card.Cells.Count}\" ");
        body.Append(Invariant, $"data-last-reshuffle=\"{(card.LastReshuffle is { } last ? DayOf(last) : "none")}\" data-next-reshuffle=\"{(card.NextReshuffle is { } next ? DayOf(next) : "none")}\">");
        body.Append(Invariant, $"<p class=\"card-summary\" data-summary>{Escaped(QualitySummary(card))}</p>");

        if (card.Fixed is { } fixedOn)
        {
            body.Append(Invariant, $"<p class=\"family-frozen quality-fixed\" data-fixed=\"{DayOf(fixedOn.FixedOn)}\" data-holdings=\"{fixedOn.Holdings}\" data-edge=\"{(fixedOn.Edge is { } edge ? edge.ToString("R", Invariant) : "none")}\" ");
            body.Append(Invariant, $"data-years=\"{fixedOn.YearsWithHoldings}\" data-ahead=\"{fixedOn.YearsAhead}\" data-beat-luck=\"{(fixedOn.BeatLuck is { } beat ? Flag(beat) : "none")}\" data-tester-passed=\"{(fixedOn.TesterPassed is { } passed ? Flag(passed) : "none")}\">");
            body.Append(Escaped(QualityFixedLine(fixedOn))).Append("</p>");
        }

        if (!card.Read || card.Fault is not null)
        {
            return body.Append("</section>").ToString();
        }

        body.Append(QualityTiles(card));

        if (card.Cells.Count > 0)
        {
            body.Append(Invariant, $"<div class=\"tbl-wrap\"><table class=\"list-table quality-table\" data-rows=\"{card.Cells.Count}\"><thead><tr>");

            foreach (var (heading, says) in QualityHeadings)
            {
                body.Append(TippedHeading(heading, says, heading is "Score" or "Since bought" ? "r" : null));
            }

            body.Append("</tr></thead><tbody>");

            foreach (var cell in card.Cells)
            {
                body.Append(Invariant, $"<tr data-ticker=\"{Escaped(cell.Ticker)}\" data-chosen-on=\"{DayOf(cell.ChosenOn)}\" data-since-bought=\"{(cell.SinceBought is { } since ? since.ToString("R", Invariant) : "none")}\">");
                body.Append(Invariant, $"<td class=\"c-nm\"><a class=\"name-link\" href=\"#/chart/{Uri.EscapeDataString(cell.Ticker)}\">{Escaped(cell.Ticker)}</a>{(cell.Company is { Length: > 0 } company ? Formatted($"<span class=\"co\">{Escaped(company)}</span>") : string.Empty)}</td>");
                body.Append(cell is { Passed: { } passed, Applied: { } applied }
                    ? Formatted($"<td class=\"r num quality-score\">{passed} of {applied}</td>")
                    : "<td class=\"r num quality-score\"><span class=\"degraded\">not read</span></td>");
                body.Append(Invariant, $"<td class=\"quality-place\">{Escaped(PlaceCell(cell.Place))}</td>");
                body.Append(cell.SinceBought is { } change
                    ? Formatted($"<td class=\"r num quality-since\">{SignedFigure(change * 100, 1, Formatted($"{Unsigned(change * 100, 1):+0.0;-0.0;0.0}%"))}</td>")
                    : "<td class=\"r num quality-since\"><span class=\"degraded\">bought at the next open</span></td>");
                body.Append(Invariant, $"<td class=\"quality-tonight\">{Escaped(cell.Tonight)}</td></tr>");
            }

            body.Append("</tbody></table></div>");
        }

        if (card.LastReshuffle is { } reshuffle)
        {
            body.Append(Invariant, $"<p class=\"family-note quality-chosen\" data-chosen=\"{Escaped(string.Join(",", card.Chosen))}\">");
            body.Append(card.Chosen.Count == 0
                ? FormattableString.Invariant($"The last reshuffle, on {reshuffle:d MMM yyyy}, chose nothing new.")
                : FormattableString.Invariant($"The last reshuffle, on {reshuffle:d MMM yyyy}, chose {Escaped(string.Join(", ", card.Chosen))}."));
            body.Append("</p>");
        }

        foreach (var sold in card.Sold)
        {
            body.Append(Invariant, $"<p class=\"family-note quality-sold\" data-ticker=\"{Escaped(sold.Ticker)}\" data-sold-on=\"{DayOf(sold.On)}\">");
            body.Append(Invariant, $"{Escaped(sold.Ticker)} was sold at the close of {sold.On:d MMM yyyy}: {Escaped(sold.Reason == "no longer qualifies" && sold.On != card.LastReshuffle ? "a filing dropped its score below its setting's bar" : SoldWords(sold.Reason))}.</p>");
        }

        if (card.Next.Count > 0)
        {
            body.Append(Invariant, $"<details class=\"fold quality-next\" data-next=\"{card.Next.Count}\"><summary>Next in line ({card.Next.Count})</summary><ol class=\"quality-next-list\">");

            foreach (var named in card.Next)
            {
                body.Append(Invariant, $"<li data-ticker=\"{Escaped(named.Ticker)}\"><a class=\"name-link\" href=\"#/chart/{Uri.EscapeDataString(named.Ticker)}\">{Escaped(named.Ticker)}</a>{(named.Company is { Length: > 0 } company ? Formatted($" {Escaped(company)}") : string.Empty)}: a score of {named.Passed} of {named.Applied}, {Escaped(PlaceWords(named.Place))}.</li>");
            }

            body.Append("</ol></details>");
        }

        body.Append("<details class=\"fold fam-rule\"><summary>The full rule, and how it will be tested</summary><div class=\"fold-b\">");
        body.Append(Invariant, $"<p class=\"record-said\">{Escaped(char.ToUpperInvariant(card.Rule[0]) + card.Rule[1..])}.</p>");
        body.Append(card.LiveSince is { } live
            ? FormattableString.Invariant($"<p class=\"record-said\">Its settings were fixed on {live:d MMM yyyy} after its test over the past, and its record counts from then. Each version one setting from it runs beside it unseen, and the monthly test reads whether any does better.</p>")
            : "<p class=\"record-said\">It has not been tested yet, so its holdings count toward nothing. Its test over the past seven years comes next, and its settings are fixed only on that answer.</p>");
        body.Append("</div></details>");

        return body.Append("</section>").ToString();
    }

    // The words a fixed card's line closes on where no setting its test read beat luck or passed the year-by-year test.
    public const string QualityNoneBeatLuck = "No setting beat luck";

    public const string QualityNonePassed = "No setting passed the year-by-year test";

    // A fixed card's line in the reader's words, from its own recorded answer: the day its settings were fixed after its
    // history test, its holdings and its edge a holding behind or ahead of an equal-weight hold of the index's stocks after
    // costs, in how many of the years it had holdings, and where none beat luck or none passed the year-by-year test, that
    // it is a starting point rather than a proven edge.
    // see: A frozen card on the S&P 400 or 600 states its sweep's figures and that no setting beat luck or the tester's bar
    // see: Each index's quality rule is frozen at its starting setting on the operator's word, with its one-step neighbours as versions
    public static string QualityFixedLine(QualityFixedView fixedOn)
    {
        var text = new StringBuilder();

        text.Append(Invariant, $"Its settings were fixed on {fixedOn.FixedOn:d MMM} after its history test from {fixedOn.FromYear}: {fixedOn.Holdings:#,0} holdings");

        if (fixedOn.Edge is { } edge)
        {
            var behind = edge < 0;
            var years = behind
                ? fixedOn.YearsAhead == 0 ? "behind in every year it had holdings" : Formatted($"behind in {fixedOn.YearsWithHoldings - fixedOn.YearsAhead} of the {fixedOn.YearsWithHoldings} years it had holdings")
                : fixedOn.YearsAhead == fixedOn.YearsWithHoldings ? "ahead in every year it had holdings" : Formatted($"ahead in {fixedOn.YearsAhead} of the {fixedOn.YearsWithHoldings} years it had holdings");

            text.Append(Invariant, $", {Math.Abs(edge * 100):0.0} points a holding {(behind ? "behind" : "ahead of")} an equal-weight hold of the index's stocks after costs, {years}");
        }

        text.Append('.');

        var none = fixedOn.BeatLuck == false ? QualityNoneBeatLuck : fixedOn.TesterPassed == false ? QualityNonePassed : null;

        if (none is not null)
        {
            text.Append(' ').Append(none).Append(", ").Append(StartingPoint).Append('.');
        }

        return text.ToString();
    }

    // The card's sentence from tonight's figures.
    static string QualitySummary(QualityCardView card) =>
        card.Fault is not null ? "Its part of the night was not computed, so nothing was bought or sold tonight."
        : !card.Read ? "It has read no night yet: its first holdings are chosen on the first night it runs."
        : card.Cells.Count == 0 ? "It holds nothing tonight: no stock cleared its score and its price." + NextWords(card)
        : Formatted($"It holds {(card.Cells.Count == 1 ? "1 stock" : Formatted($"{card.Cells.Count} stocks"))} of {card.Holdings}, each with strong fundamentals and a price at or below its own usual.") + NextWords(card);

    static string NextWords(QualityCardView card) =>
        card.DaysToNext is { } days ? Formatted($" It reshuffles next in {days} trading {(days == 1 ? "day" : "days")}.") : string.Empty;

    // The three tiles: holdings, the change since the last reshuffle beside the index's members, and the next reshuffle.
    static string QualityTiles(QualityCardView card)
    {
        static string Percent(double? figure) =>
            figure is { } held ? SignedFigure(held * 100, 1, Formatted($"{Unsigned(held * 100, 1):+0.0;-0.0;0.0}%")) : "<span class=\"degraded\">not read</span>";

        var body = new StringBuilder();

        body.Append(Invariant, $"<div class=\"hw-tiles quality-tiles\" data-holdings=\"{card.Cells.Count}\" data-holdings-since=\"{(card.HoldingsSince is { } since ? since.ToString("R", Invariant) : "none")}\" data-members-since=\"{(card.MembersSince is { } members ? members.ToString("R", Invariant) : "none")}\">");
        body.Append(Invariant, $"<div class=\"hw-tile\"><span class=\"tile-label\">Holding</span><b>{card.Cells.Count} of {card.Holdings}</b><span>stocks</span></div>");
        body.Append(card.LastReshuffle is { } last
            ? FormattableString.Invariant($"<div class=\"hw-tile\"><span class=\"tile-label\">Since the last reshuffle, {last:d MMM}</span><b>{Percent(card.HoldingsSince)}</b><span>the index's stocks {Percent(card.MembersSince)}</span></div>")
            : "<div class=\"hw-tile\"><span class=\"tile-label\">Since the last reshuffle</span><b>none</b><span>no reshuffle read yet</span></div>");
        body.Append(Invariant, $"<div class=\"hw-tile\"><span class=\"tile-label\">Next reshuffle</span><b>{(card.NextReshuffle is { } coming ? FormattableString.Invariant($"{coming:d MMM}") : "not read")}</b><span>{(card.DaysToNext is { } days ? Formatted($"in {days} trading {(days == 1 ? "day" : "days")}") : "past the exchange calendar's table")}</span></div>");
        body.Append("</div>");

        return body.ToString();
    }

    // A valuation's place in words: cheaper than in so many of its own last 12 quarters.
    public static string PlaceWords(double? place) =>
        place is { } share
            ? Formatted($"cheaper than in {(int)Math.Round(share * EquityBrief.Core.Ledger.ValuationReading.Quarters, MidpointRounding.AwayFromZero)} of its own last {EquityBrief.Core.Ledger.ValuationReading.Quarters} quarters")
            : "its price over earnings not read";

    // The same in a holding's cell, beneath a heading that says against what.
    public static string PlaceCell(double? place) =>
        place is { } share
            ? Formatted($"cheaper than in {(int)Math.Round(share * EquityBrief.Core.Ledger.ValuationReading.Quarters, MidpointRounding.AwayFromZero)} of {EquityBrief.Core.Ledger.ValuationReading.Quarters} quarters")
            : "not read";

    // Why a holding was sold, in the reader's words.
    static string SoldWords(string reason) => reason switch
    {
        "no longer qualifies" => "it no longer qualified at the reshuffle",
        "left the index" => "it left the index",
        _ => reason,
    };
}
