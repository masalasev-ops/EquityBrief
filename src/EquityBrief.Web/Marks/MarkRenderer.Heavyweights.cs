using System.Text;

namespace EquityBrief.Web.Marks;

// One holding on the sector heavyweights' card: the stock and its company, its sector, the session it was bought on,
// its lead over its sector at the last rebalance that read it, or the words saying why none is drawn, and tonight's close
// beside its 200-session average with whether the book's own rule reads the close as under it.
// see: The sector heavyweights hold the largest companies leading their sectors, rotated on the first session of each month whose stored year and the pulled history before it hold the closes their readings need
public sealed record HeavyweightHoldingCell(
    string Ticker,
    string? Company,
    string Sector,
    DateOnly HeldSince,
    double? Lead,
    DateOnly? LeadOn,
    decimal? Close,
    double? Average200,
    bool Under,
    DecisionCardView? Card = null,
    string? NoLead = null,
    double? SinceBought = null,
    EquityBrief.Core.Families.HeavyweightStanding? Standing = null);

// The four tiles a heavyweights card opens on: its holdings, the sectors holding one of the eleven, its return since its
// last rebalance beside the equal-weighted return of the funds of the sectors it holds over the same sessions, and the
// sessions to its next rebalance, each none where it cannot be read.
public sealed record HeavyweightTilesView(int Holdings, int Sectors, int Of, double? BookSince, double? FundsSince, IReadOnlyList<string> Funds, int? SessionsToNext);

// One sector on a heavyweights card: its name, its fund and the fund's return over the look-back to the night, and for a
// sector holding none, why, read off the book's own rows of its last rebalance. The card's holdings are drawn under the
// sector each was bought in.
public sealed record HeavyweightSectorView(string Sector, string? Fund, double? FundReturn, string? Reason);

// A holding the book ended, as the card names it: the stock, the session it ended on and why.
public sealed record HeavyweightEndedCell(string Ticker, DateOnly EndedOn, string Reason);

// The sector heavyweights' card on a night: the family's words, the sessions its returns are read over, its last
// rebalance on or before the night and the next one, the holdings open at the night's close in sector order, what the
// last rebalance bought, what ended at it or since, why it holds nothing where it holds nothing, the day its live
// rule registered with the variants standing beside it, none where no freeze has registered it, why the month's
// rebalance waits where it has not been read, and what a column's key says, by its heading, where the card's book reads
// that column otherwise than the S&P 500's.
public sealed record HeavyweightCardView(
    string Heading,
    string Eyebrow,
    string Rule,
    int LookBack,
    DateOnly? LastRebalance,
    DateOnly? NextRebalance,
    IReadOnlyList<HeavyweightHoldingCell> Holdings,
    IReadOnlyList<string> Entered,
    IReadOnlyList<HeavyweightEndedCell> Ended,
    string? Empty,
    DateOnly? LiveSince = null,
    int Variants = 0,
    bool SweepFoundNone = false,
    string? Waits = null,
    IReadOnlyDictionary<string, string>? Says = null,
    RuleView? RuleChoice = null,
    HeavyweightTilesView? Tiles = null,
    IReadOnlyList<HeavyweightSectorView>? Sectors = null,
    IReadOnlyList<int>? SweepLost = null,
    FrozenSweepView? Frozen = null);

// One holding on Past picks as of a night: the stock, its sector, its buy and its sale with their closes, why it
// ended, its return, its size cut's over the same sessions and the difference, each in percent and none while open.
public sealed record HeavyweightPickCell(
    string Ticker,
    string Sector,
    DateOnly Bought,
    decimal BuyClose,
    DateOnly? Sold,
    decimal? SaleClose,
    string? Reason,
    double? Result,
    double? CutReturn,
    double? Edge);

public sealed partial class MarkRenderer
{
    // The headings of the columns drawing a holding's sector and its lead, whose keys a card may state in its own words.
    public const string SectorHeading = "Sector";

    public const string LeadHeading = "Lead over its sector";

    // What each column of the sector heavyweights' card holds, in the order the columns are drawn.
    public static IReadOnlyList<(string Heading, string Says)> HeavyweightHeadings { get; } =
    [
        ("#", "The holding's place on the card, the sectors in alphabetical order. The day it was bought on is under the pointer."),
        ("Stock", "The ticker opens the stock's chart, with the company beneath."),
        (LeadHeading, "How far its return over the year beat its sector fund's when it was last checked, in percentage points, as a bar."),
        ("Against its 200-day", "Tonight's close above or below its 200-day average, in per cent of the average."),
        ("Since bought", "Its return from its buy to tonight's close, in per cent of the buy."),
        ("Tonight", "Whether it still leads its sector tonight, read off tonight's closes. This sells nothing: a stock is sold only at a reshuffle."),
    ];

    // The sector heavyweights' card's header, as a setup card's: its name, whether it is live, tonight's holdings, the two
    // sentences describing it and the Showing list.
    public string HeavyweightCardHead(HeavyweightCardView card)
    {
        var view = new FamilyCardView(
            EquityBrief.Core.Families.HeavyweightRule.Name,
            card.Heading,
            card.Heading,
            card.Eyebrow,
            card.Rule,
            0,
            0,
            card.LiveSince,
            card.Variants,
            [],
            [],
            null,
            RuleChoice: card.RuleChoice);
        return FamilyCardHead(view, count: card.Holdings.Count == 1 ? "1 holding" : Formatted($"{card.Holdings.Count} holdings"));
    }

    // The sector heavyweights' card from 22.6: its summary, a chosen version's band, the three tiles, its holdings grouped
    // by sector under each fund's return, what the last reshuffle bought and what ended, then the sectors holding nothing
    // and the full rule with how its settings were chosen behind folds.
    // see: The sector heavyweights hold the largest companies leading their sectors, rotated on the first session of each month whose stored year and the pulled history before it hold the closes their readings need
    // see: The market check closes every swing family's list together but the short-term dip's, and the sector heavyweights read none
    // see: Paper trading's accounts and a heavyweight's standing tonight are worked by one function in the core when a page is read
    public string HeavyweightCard(HeavyweightCardView card)
    {
        var body = new StringBuilder();
        var variant = card.RuleChoice is { Chosen.Live: false } chosen ? chosen : null;

        body.Append(Invariant, $"<section class=\"family-card heavyweight-card\" data-family=\"{EquityBrief.Core.Families.HeavyweightRule.Name}\" data-holdings=\"{card.Holdings.Count}\" data-state=\"{(card.LiveSince is null ? "provisional" : "live")}\" ");
        body.Append(Invariant, $"data-live-since=\"{(card.LiveSince is { } since ? DayOf(since) : "none")}\" data-variants=\"{card.Variants}\" ");
        body.Append(Invariant, $"data-last-rebalance=\"{(card.LastRebalance is { } last ? DayOf(last) : "none")}\" data-next-rebalance=\"{(card.NextRebalance is { } next ? DayOf(next) : "none")}\" data-look-back=\"{card.LookBack}\">");

        // The summary: what it holds and in how many sectors, and when it next reshuffles.
        var sectorsHeld = card.Holdings.Select(holding => holding.Sector).Distinct(StringComparer.Ordinal).Count();
        var reshuffle = card.Tiles?.SessionsToNext is { } days ? Formatted($"; it reshuffles next in {days} trading {(days == 1 ? "day" : "days")}") : string.Empty;
        var summary = card.Holdings.Count == 0
            ? (variant is null ? "It holds nothing tonight" : "This version would hold nothing tonight") + reshuffle + "."
            : (variant is null ? "It holds " : "This version would hold ") + (card.Holdings.Count == 1 ? "1 stock" : Formatted($"{card.Holdings.Count} stocks")) + Formatted($" in {sectorsHeld} of {card.Tiles?.Of ?? 11} sectors") + reshuffle + ".";

        body.Append(Invariant, $"<p class=\"card-summary\" data-summary>{Escaped(summary)}</p>");

        // A registered version's own holdings drawn under its band, with what it shares with the live holdings, adds and
        // drops.
        // see: A variant's picks are shown on its card when chosen and its results on paper trading beside its verdict, never on Tonight
        if (card.RuleChoice is { } rule)
        {
            body.Append(Invariant, $"<div class=\"heavyweight-rule\" data-rule=\"{Escaped(rule.Chosen.Slug)}\" data-variant=\"{(variant is null ? "none" : variant.Chosen.Variant!.Value.ToString(Invariant))}\">");

            if (variant is not null)
            {
                body.Append(Invariant, $"<div class=\"variant-band\" role=\"status\" data-variant=\"{variant.Chosen.Variant}\"><p class=\"band-head\">A version being tested, not tonight's holdings</p><p>{Escaped(VersionName(variant.Chosen))}</p><p class=\"band-note\">It runs every night beside the live setup with its own paper account. It replaces the live setup only if it proves better and you approve it on the Loop page.</p></div>");

                // A version registered before its setup's settings were fixed says how the tester judged its design.
                // see: An index family's variants may stand registered before its freeze, its provisional rule staying the live one
                if (variant.Untested is { } untested)
                {
                    body.Append(Invariant, $"<p class=\"variant-untested\" data-adjusted=\"{(untested.Adjusted is { } p ? p.ToString("R", Invariant) : "none")}\">{Escaped(ReaderWords.Plain(untested.Line))}</p>");
                }

                body.Append(ShareLine(variant.Share, "holdings"));
            }

            body.Append("</div>");
        }

        if (card.Waits is { } waits)
        {
            body.Append(Invariant, $"<p class=\"family-note heavyweight-waits\">{Escaped(ReaderWords.Plain(waits))}</p>");
        }

        body.Append(HeavyweightTiles(card));

        if (card.Holdings.Count == 0)
        {
            body.Append(Invariant, $"<p class=\"degraded family-empty\" data-holdings=\"0\">{Escaped(ReaderWords.Plain(card.Empty ?? "The sector heavyweights hold nothing tonight."))}</p>");
        }

        // The sectors holding a stock first, a table of their holdings; then, once a rebalance has been read, the sectors
        // holding none behind one expander, a line each with its fund's return and why; before it, the line above alone.
        var sectors = card.Sectors ?? [.. card.Holdings.Select(holding => holding.Sector).Distinct(StringComparer.Ordinal).Select(name => new HeavyweightSectorView(name, null, null, null))];
        HeavyweightSectorView[] empties = card.LastRebalance is null ? [] : [.. sectors.Where(sector => card.Holdings.All(holding => holding.Sector != sector.Sector))];

        if (card.Holdings.Count > 0)
        {
            body.Append(Invariant, $"<div class=\"tbl-wrap opens-rows\"><table class=\"list-table heavyweight-table\" data-rows=\"{card.Holdings.Count}\"><thead><tr>");

            foreach (var (heading, says) in HeavyweightHeadings)
            {
                body.Append(TippedHeading(heading, card.Says?.GetValueOrDefault(heading) ?? says, heading switch
                {
                    "#" => "place",
                    LeadHeading or "Against its 200-day" or "Since bought" => "r",
                    _ => null,
                }));
            }

            body.Append("</tr></thead><tbody>");

            var place = 0;
            var top = card.Holdings.Select(holding => Math.Abs(holding.Lead ?? 0)).DefaultIfEmpty(0).Max();

            foreach (var sector in sectors.Where(sector => card.Holdings.Any(holding => holding.Sector == sector.Sector)))
            {
                HeavyweightHoldingCell[] held = [.. card.Holdings.Where(holding => holding.Sector == sector.Sector)];

                body.Append(Invariant, $"<tr class=\"hw-sector\" data-sector=\"{Escaped(sector.Sector)}\" data-holdings=\"{held.Length}\"><th scope=\"rowgroup\" colspan=\"{HeavyweightHeadings.Count}\">{Escaped(sector.Sector)}");
                body.Append(FundReturnSpan(sector, card.LookBack));
                body.Append("</th></tr>");

                foreach (var holding in held)
                {
                    place++;
                    body.Append(HeavyweightRow(holding, place, card.LookBack, top));

                    // The card of the night the holding was bought, in place beneath its row.
                    if (holding.Card is { } decision)
                    {
                        body.Append(CardRow(decision, HeavyweightHeadings.Count));
                    }
                }
            }

            body.Append("</tbody></table></div>");
        }

        if (card.LastRebalance is { } rebalance)
        {
            body.Append(Invariant, $"<p class=\"family-note heavyweight-entered\" data-entered=\"{Escaped(string.Join(",", card.Entered))}\">");
            body.Append(card.Entered.Count == 0
                ? FormattableString.Invariant($"The last reshuffle, on {rebalance:d MMM yyyy}, bought nothing new.")
                : FormattableString.Invariant($"The last reshuffle, on {rebalance:d MMM yyyy}, bought {Escaped(string.Join(", ", card.Entered))}."));
            body.Append("</p>");
        }

        foreach (var ended in card.Ended)
        {
            body.Append(Invariant, $"<p class=\"family-note heavyweight-ended\" data-ticker=\"{Escaped(ended.Ticker)}\" data-ended-on=\"{DayOf(ended.EndedOn)}\" data-reason=\"{Escaped(ended.Reason)}\">");
            body.Append(Invariant, $"{Escaped(ended.Ticker)} was sold at the close of {ended.EndedOn:d MMM yyyy}: {Escaped(ReaderWords.Plain(ended.Reason))}.</p>");
        }

        // The folds, closed: why each sector holding nothing holds nothing, and the full rule with how its settings were
        // chosen, its versions and its record.
        if (empties.Length > 0)
        {
            body.Append(Invariant, $"<details class=\"hw-empties\" data-empty=\"{empties.Length}\"><summary>Why {(empties.Length == 1 ? "1 sector holds" : Formatted($"{empties.Length} sectors hold"))} nothing</summary><ul class=\"hw-empty-list\">");

            foreach (var sector in empties)
            {
                body.Append(Invariant, $"<li class=\"hw-empty\" data-sector=\"{Escaped(sector.Sector)}\"><b>{Escaped(sector.Sector)}</b>{FundReturnSpan(sector, card.LookBack)} <span class=\"hw-why\">{Escaped(ReaderWords.Plain(sector.Reason ?? "It holds nothing tonight."))}</span></li>");
            }

            body.Append("</ul></details>");
        }

        body.Append("<details class=\"fold fam-rule\"><summary>The full rule, how its settings were chosen, and its record</summary><div class=\"fold-b\">");

        if (card.RuleChoice is { } chips)
        {
            body.Append(RuleChipsLine(chips));
        }

        body.Append(SweepFoundNoneLine(card.SweepFoundNone, card.SweepLost));
        body.Append(variant is null ? FrozenSweepLine(card.Frozen) : string.Empty);
        body.Append(card.Frozen is { } frozen && variant is null ? Formatted($"<p class=\"record-said\">{Escaped(FrozenRecordSaid(frozen))}</p>") : string.Empty);
        body.Append(card.RuleChoice?.NoVariantLine is { } none ? Formatted($"<p class=\"no-variant\" data-no-variant=\"true\">{Escaped(ReaderWords.Plain(none))}</p>") : string.Empty);
        body.Append(Invariant, $"<p class=\"record-said\">{(card.LiveSince is { } live ? FormattableString.Invariant($"It went live on {live:d MMM yyyy}; its holdings since then are on <a href=\"#/paper\">Paper trading</a>.") : "It has not been tested yet, so its holdings count toward nothing; they are on <a href=\"#/paper\">Paper trading</a>.")}</p>");
        body.Append("</div></details>");

        if (card.RuleChoice is { Choices.Count: > 1 } versions)
        {
            body.Append(VersionsSideBySide(versions));
        }

        body.Append("</section>");

        return body.ToString();
    }

    // A sector's fund and its return over the card's look-back, signed in the gain's or the loss's hue, none where the
    // sector names no fund.
    static string FundReturnSpan(HeavyweightSectorView sector, int lookBack) =>
        sector.Fund is { } fund
            ? Formatted($" <span class=\"hw-fund\" data-fund=\"{Escaped(fund)}\" data-return=\"{(sector.FundReturn is { } fundReturn ? fundReturn.ToString("R", Invariant) : "none")}\">{Escaped(fund)} {(sector.FundReturn is { } over ? SignedFigure(over * 100, 1, Formatted($"{Unsigned(over * 100, 1):+0.0;-0.0;0.0}%")) : "<span class=\"degraded\">not read</span>")} over {lookBack} trading days</span>")
            : string.Empty;

    // The four tiles a heavyweights card opens on.
    static string HeavyweightTiles(HeavyweightCardView card)
    {
        if (card.Tiles is not { } tiles)
        {
            return string.Empty;
        }

        static string Percent(double? figure) =>
            figure is { } held ? SignedFigure(held * 100, 1, Formatted($"{Unsigned(held * 100, 1):+0.0;-0.0;0.0}%")) : "<span class=\"degraded\">not read</span>";

        var body = new StringBuilder();

        body.Append(Invariant, $"<div class=\"hw-tiles\" data-holdings=\"{tiles.Holdings}\" data-sectors=\"{tiles.Sectors}\" data-of=\"{tiles.Of}\" data-book-since=\"{(tiles.BookSince is { } book ? book.ToString("R", Invariant) : "none")}\" data-funds-since=\"{(tiles.FundsSince is { } funds ? funds.ToString("R", Invariant) : "none")}\" data-sessions-to-next=\"{(tiles.SessionsToNext is { } next ? next.ToString(Invariant) : "none")}\">");
        body.Append(Invariant, $"<div class=\"hw-tile\"><span class=\"tile-label\">Holding</span><b>{(tiles.Holdings == 1 ? "1 stock" : Formatted($"{tiles.Holdings} stocks"))}</b><span>in {tiles.Sectors} of {tiles.Of} sectors</span></div>");
        body.Append(card.LastRebalance is { } last
            ? FormattableString.Invariant($"<div class=\"hw-tile\" title=\"{Escaped(tiles.Funds.Count == 0 ? "no fund held" : "the funds: " + string.Join(", ", tiles.Funds))}\"><span class=\"tile-label\">Since the last reshuffle, {last:d MMM}</span><b>{Percent(tiles.BookSince)}</b><span>their sector funds {Percent(tiles.FundsSince)}</span></div>")
            : "<div class=\"hw-tile\"><span class=\"tile-label\">Since the last reshuffle</span><b>none</b><span>no reshuffle read yet</span></div>");
        body.Append(Invariant, $"<div class=\"hw-tile\"><span class=\"tile-label\">Next reshuffle</span><b>{(card.NextRebalance is { } coming ? FormattableString.Invariant($"{coming:d MMM}") : "not read")}</b><span>{(tiles.SessionsToNext is { } sessions ? Formatted($"in {sessions} trading {(sessions == 1 ? "day" : "days")}") : "past the exchange calendar's table")}</span></div>");
        body.Append("</div>");

        return body.ToString();
    }

    // One holding as a row of the card: its lead at the last rebalance as a bar against the card's largest, its close
    // against its 200-day average and its return since bought in per cent, and tonight's standing as a pill, the session
    // it was bought on under the pointer.
    string HeavyweightRow(HeavyweightHoldingCell holding, int place, int lookBack, double top)
    {
        var cells = new StringBuilder();
        var standing = holding.Standing;

        cells.Append(Invariant, $"<tr data-ticker=\"{Escaped(holding.Ticker)}\" data-sector=\"{Escaped(holding.Sector)}\" data-held-since=\"{DayOf(holding.HeldSince)}\" title=\"held since {DayOf(holding.HeldSince)}\" ");
        cells.Append(Invariant, $"data-lead=\"{(holding.Lead is { } stored ? stored.ToString("R", Invariant) : "none")}\" data-close=\"{(holding.Close is { } close ? close.ToString(Invariant) : "none")}\" data-average=\"{(holding.Average200 is { } average ? average.ToString("R", Invariant) : "none")}\" ");
        cells.Append(Invariant, $"data-since-bought=\"{(holding.SinceBought is { } since ? since.ToString("R", Invariant) : "none")}\" data-standing=\"{Escaped(standing?.Word ?? EquityBrief.Core.Families.HeavyweightStandings.NotRead)}\">");
        cells.Append(Invariant, $"<td class=\"place\">{place}</td>");
        cells.Append(Invariant, $"<td class=\"c-nm\"><a class=\"name-link\" href=\"#/chart/{Uri.EscapeDataString(holding.Ticker)}\">{Escaped(holding.Ticker)}</a>{(holding.Card is { } decision ? CardToggle(decision) : string.Empty)}{(holding.Company is { Length: > 0 } company ? Formatted($"<span class=\"co\">{Escaped(company)}</span>") : string.Empty)}</td>");
        cells.Append(holding.Lead is { } lead
            ? Formatted($"<td class=\"r num heavyweight-lead\" title=\"over {lookBack} sessions{(holding.LeadOn is { } on ? Formatted($", at the rebalance of {DayOf(on)}") : string.Empty)}\"><span class=\"hw-bar\"><span class=\"hw-fill {(lead >= 0 ? "gain" : "loss")}\" style=\"width:{(top > 0 ? Math.Abs(lead) / top * 100 : 0).ToString("0.#", Invariant)}%\"></span></span> {SignedTenth(lead * 100)} points</td>")
            : Formatted($"<td class=\"r num heavyweight-lead\"><span class=\"degraded\">{Escaped(ReaderWords.Plain(holding.NoLead ?? "not read"))}</span></td>"));
        cells.Append(standing?.OverAverage is { } over
            ? Formatted($"<td class=\"r num heavyweight-average\" data-under=\"{Flag(holding.Under)}\">{SignedFigure(over * 100, 1, Formatted($"{Unsigned(over * 100, 1):+0.0;-0.0;0.0}%"))}</td>")
            : "<td class=\"r num heavyweight-average\"><span class=\"degraded\">no close or average stored tonight</span></td>");
        cells.Append(holding.SinceBought is { } bought
            ? Formatted($"<td class=\"r num heavyweight-since\">{SignedFigure(bought * 100, 1, Formatted($"{Unsigned(bought * 100, 1):+0.0;-0.0;0.0}%"))}</td>")
            : "<td class=\"r num heavyweight-since\"><span class=\"degraded\">not read</span></td>");
        cells.Append(Invariant, $"<td class=\"heavyweight-standing\"><span class=\"hw-pill\" data-standing=\"{Escaped(standing?.Word ?? EquityBrief.Core.Families.HeavyweightStandings.NotRead)}\" title=\"information only: a stock is sold only at a reshuffle\">{Escaped(StandingWords(standing?.Word))}</span></td></tr>");

        return cells.ToString();
    }

    // A holding's standing tonight in a sentence: still leading, or what it fell short of and what that means at the next
    // reshuffle.
    static string StandingWords(string? word) => word switch
    {
        EquityBrief.Core.Families.HeavyweightStandings.StillLeading => "Still leading its sector",
        EquityBrief.Core.Families.HeavyweightStandings.LostItsLead => "Lost its lead; sold at the next reshuffle if it is not leading then",
        EquityBrief.Core.Families.HeavyweightStandings.BelowItsAverage => "Fell below its 200-day average; sold at the next reshuffle if it stops leading",
        EquityBrief.Core.Families.HeavyweightStandings.NotForThisDesign => "Not read for this version",
        _ => "Not read tonight",
    };

    // What each column of Past picks' table of the sector heavyweights holds.
    public static IReadOnlyList<(string Heading, string Says)> HeavyweightPickHeadings { get; } =
    [
        ("Stock", "The ticker opens the stock's page."),
        ("Sector", "The sector the stock was bought as the leader of."),
        ("Bought", "The session it was bought on and that session's close."),
        ("Sold", "The session it was sold on and that session's close, or held while it is still held."),
        ("Why it ended", "No longer the leader at a rebalance, or its stock leaving the index; a close under its 200-day average under the provisional rule, which sold on one."),
        ("Result", "What the holding made from its buy to its sale, in percent of the buy, dividends counted."),
        ("Its sector's largest", "What the sector's largest companies it was chosen from made over the same sessions, each in equal part, in percent."),
        ("Difference", "The result less what the sector's largest made, in percentage points: what leading its sector was worth."),
    ];

    // Past picks' table of the sector heavyweights' holdings, newest first, each result in percent beside its size
    // cut's over the same sessions.
    public string HeavyweightPicks(IReadOnlyList<HeavyweightPickCell> rows)
    {
        var body = new StringBuilder();

        body.Append(Invariant, $"<div class=\"tbl-wrap\"><table class=\"list-table heavyweight-picks\" data-rows=\"{rows.Count}\"><thead><tr>");

        foreach (var (heading, says) in HeavyweightPickHeadings)
        {
            body.Append(TippedHeading(heading, says, heading is "Result" or "Its sector's largest" or "Difference" ? "r" : null));
        }

        body.Append("</tr></thead><tbody>");

        foreach (var row in rows)
        {
            body.Append(Invariant, $"<tr data-ticker=\"{Escaped(row.Ticker)}\" data-bought=\"{DayOf(row.Bought)}\" data-sold=\"{(row.Sold is { } sold ? DayOf(sold) : "open")}\" ");
            body.Append(Invariant, $"data-result=\"{Stated(row.Result)}\" data-cut=\"{Stated(row.CutReturn)}\" data-edge=\"{Stated(row.Edge)}\">");
            body.Append(Invariant, $"<td class=\"c-nm\"><a class=\"name-link\" href=\"#/chart/{Uri.EscapeDataString(row.Ticker)}\">{Escaped(row.Ticker)}</a></td>");
            body.Append(Invariant, $"<td class=\"setup\">{Escaped(row.Sector)}</td>");
            body.Append(Invariant, $"<td>{DayOf(row.Bought)} at {Figures.Price(row.BuyClose)}</td>");
            body.Append(row.Sold is { } on && row.SaleClose is { } close
                ? Formatted($"<td>{DayOf(on)} at {Figures.Price(close)}</td>")
                : "<td><b>held</b></td>");
            body.Append(Invariant, $"<td>{(row.Reason is { } why ? Escaped(why) : "still held")}</td>");
            body.Append(Invariant, $"<td class=\"r num\">{InPercent(row.Result)}</td><td class=\"r num\">{InPercent(row.CutReturn)}</td>");
            body.Append(row.Edge is { } edge
                ? Formatted($"<td class=\"r num\">{SignedTenth(edge * 100)} points</td>")
                : "<td class=\"r num\"><span class=\"degraded\">open</span></td>");
            body.Append("</tr>");
        }

        body.Append("</tbody></table></div>");

        return body.ToString();
    }

    static string Stated(double? value) => value is { } held ? held.ToString("R", Invariant) : "none";

    static string InPercent(double? fraction) =>
        fraction is { } held ? SignedTenth(held * 100) + "%" : "<span class=\"degraded\">open</span>";
}
