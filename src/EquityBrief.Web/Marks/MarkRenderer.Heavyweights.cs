using System.Text;

namespace EquityBrief.Web.Marks;

// One holding on the sector heavyweights' card: the stock and its company, its sector, the session it was bought on,
// its lead over its sector at the last rebalance that read it, and tonight's close beside its 200-session average with
// whether the book's own rule reads the close as under it.
// see: The sector heavyweights hold the largest companies leading their sectors, rotated on the first session of each month
public sealed record HeavyweightHoldingCell(
    string Ticker,
    string? Company,
    string Sector,
    DateOnly HeldSince,
    double? Lead,
    DateOnly? LeadOn,
    decimal? Close,
    double? Average200,
    bool Under);

// A holding the book ended, as the card names it: the stock, the session it ended on and why.
public sealed record HeavyweightEndedCell(string Ticker, DateOnly EndedOn, string Reason);

// The sector heavyweights' card on a night: the family's words, the sessions its returns are read over, its last
// rebalance on or before the night and the next one, the holdings open at the night's close in sector order, what the
// last rebalance bought, what ended at it or since, why it holds nothing where it holds nothing, and the day its live
// rule registered with the variants standing beside it, none where no freeze has registered it.
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
    bool SweepFoundNone = false);

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
    // What each column of the sector heavyweights' card holds, in the order the columns are drawn.
    public static IReadOnlyList<(string Heading, string Says)> HeavyweightHeadings { get; } =
    [
        ("#", "The holding's place on the card, the sectors in alphabetical order."),
        ("Stock", "The ticker opens the stock's page, with the company beneath."),
        ("Sector", "The sector the stock was bought as the leader of."),
        ("Held since", "The session the book bought it on, at that session's close."),
        ("Lead over its sector", "Its return over the look-back less its sector fund's over the same sessions, in percentage points, at the last rebalance that read it."),
        ("Close against its 200-day", "Tonight's close beside its 200-day average, which the trend gate read at the rebalance. A close under it sells nothing on its own."),
        ("Plan", "Held while it leads: no stop and no target. It is sold at a month's first close where it no longer leads, or at its last close as a member."),
    ];

    // The sector heavyweights' card: its standing and the night's count in a line, the holdings one to a row, what
    // the last rebalance bought and what ended, and the next rebalance.
    // see: The sector heavyweights hold the largest companies leading their sectors, rotated on the first session of each month
    // see: The market check closes every swing family's list together, and the sector heavyweights read none
    public string HeavyweightCard(HeavyweightCardView card)
    {
        var body = new StringBuilder();

        body.Append(Invariant, $"<section class=\"family-card heavyweight-card\" data-family=\"{EquityBrief.Core.Families.HeavyweightRule.Name}\" data-holdings=\"{card.Holdings.Count}\" data-state=\"{(card.LiveSince is null ? "provisional" : "live")}\" ");
        body.Append(Invariant, $"data-live-since=\"{(card.LiveSince is { } since ? DayOf(since) : "none")}\" data-variants=\"{card.Variants}\" ");
        body.Append(Invariant, $"data-last-rebalance=\"{(card.LastRebalance is { } last ? DayOf(last) : "none")}\" data-next-rebalance=\"{(card.NextRebalance is { } next ? DayOf(next) : "none")}\" data-look-back=\"{card.LookBack}\">");

        // The rule's standing, the night's count and the variants kept in books of their own, in one line.
        body.Append("<p class=\"family-state\">");
        body.Append(card.LiveSince is { } live
            ? Formatted($"Live rule since <b>{DayOf(live)}</b>")
            : $"<b class=\"provisional\">{Escaped(EquityBrief.Core.Families.SetupFamilies.ProvisionalStatus)}</b>");
        body.Append(Invariant, $" · {Count(card.Holdings.Count, "holding")} tonight · held while leading");
        body.Append(card.LiveSince is null ? string.Empty : Formatted($" · {Count(card.Variants, "variant")} kept in books of their own"));
        body.Append(card.LastRebalance is { } read ? Formatted($" · last rebalance {DayOf(read)}") : " · no rebalance read yet");
        body.Append(card.NextRebalance is { } coming ? Formatted($" · next rebalance <b class=\"next-rebalance\">{DayOf(coming)}</b>") : " · next rebalance past the exchange calendar's table");
        body.Append("</p>");
        body.Append(SweepFoundNoneLine(card.SweepFoundNone));

        if (card.Holdings.Count == 0)
        {
            body.Append(Invariant, $"<p class=\"degraded family-empty\" data-holdings=\"0\">{Escaped(card.Empty ?? "The sector heavyweights hold nothing tonight.")}</p>");
        }
        else
        {
            body.Append(Invariant, $"<div class=\"tbl-wrap\"><table class=\"list-table heavyweight-table\" data-rows=\"{card.Holdings.Count}\"><thead><tr>");

            foreach (var (heading, says) in HeavyweightHeadings)
            {
                body.Append(TippedHeading(heading, says, heading switch
                {
                    "#" => "place",
                    "Lead over its sector" => "r",
                    _ => null,
                }));
            }

            body.Append("</tr></thead><tbody>");

            foreach (var (holding, at) in card.Holdings.Select((holding, at) => (holding, at)))
            {
                body.Append(HeavyweightRow(holding, at + 1, card.LookBack));
            }

            body.Append("</tbody></table></div>");
        }

        if (card.LastRebalance is { } rebalance)
        {
            body.Append(Invariant, $"<p class=\"family-note heavyweight-entered\" data-entered=\"{Escaped(string.Join(",", card.Entered))}\">");
            body.Append(card.Entered.Count == 0
                ? Formatted($"The rebalance of {DayOf(rebalance)} bought nothing new.")
                : Formatted($"The rebalance of {DayOf(rebalance)} bought {Escaped(string.Join(", ", card.Entered))}."));
            body.Append("</p>");
        }

        foreach (var ended in card.Ended)
        {
            body.Append(Invariant, $"<p class=\"family-note heavyweight-ended\" data-ticker=\"{Escaped(ended.Ticker)}\" data-ended-on=\"{DayOf(ended.EndedOn)}\" data-reason=\"{Escaped(ended.Reason)}\">");
            body.Append(Invariant, $"{Escaped(ended.Ticker)} was sold at the close of {DayOf(ended.EndedOn)}: {Escaped(ended.Reason)}.</p>");
        }

        body.Append("</section>");

        return body.ToString();
    }

    // One holding as a row of the card.
    string HeavyweightRow(HeavyweightHoldingCell holding, int place, int lookBack)
    {
        var cells = new StringBuilder();

        cells.Append(Invariant, $"<tr data-ticker=\"{Escaped(holding.Ticker)}\" data-sector=\"{Escaped(holding.Sector)}\" data-held-since=\"{DayOf(holding.HeldSince)}\" ");
        cells.Append(Invariant, $"data-lead=\"{(holding.Lead is { } stored ? stored.ToString("R", Invariant) : "none")}\" data-close=\"{(holding.Close is { } close ? close.ToString(Invariant) : "none")}\" data-average=\"{(holding.Average200 is { } average ? average.ToString("R", Invariant) : "none")}\">");
        cells.Append(Invariant, $"<td class=\"place\">{place}</td>");
        cells.Append(Invariant, $"<td class=\"c-nm\"><a class=\"name-link\" href=\"#/name/{Uri.EscapeDataString(holding.Ticker)}\">{Escaped(holding.Ticker)}</a>{(holding.Company is { Length: > 0 } company ? Formatted($"<span class=\"co\">{Escaped(company)}</span>") : string.Empty)}</td>");
        cells.Append(Invariant, $"<td class=\"setup\">{Escaped(holding.Sector)}</td>");
        cells.Append(Invariant, $"<td>{DayOf(holding.HeldSince)}</td>");
        cells.Append(holding.Lead is { } lead
            ? Formatted($"<td class=\"r num heavyweight-lead\">{lead * 100:+0.0;-0.0;0.0} points over {lookBack} sessions{(holding.LeadOn is { } on ? Formatted($", at {DayOf(on)}") : string.Empty)}</td>")
            : "<td class=\"r num heavyweight-lead\"><span class=\"degraded\">not read</span></td>");
        cells.Append(holding.Close is { } shut && holding.Average200 is { } line
            ? Formatted($"<td class=\"heavyweight-average\" data-under=\"{Flag(holding.Under)}\">{Figures.Price(shut)}, {(holding.Under ? "under" : "at or above")} its 200-day average of {line.ToString("#,##0.00", Invariant)}</td>")
            : "<td class=\"heavyweight-average\"><span class=\"degraded\">no close or average stored tonight</span></td>");
        cells.Append("<td class=\"heavyweight-plan\">held while leading</td></tr>");

        return cells.ToString();
    }

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
            body.Append(Invariant, $"<td class=\"c-nm\"><a class=\"name-link\" href=\"#/name/{Uri.EscapeDataString(row.Ticker)}\">{Escaped(row.Ticker)}</a></td>");
            body.Append(Invariant, $"<td class=\"setup\">{Escaped(row.Sector)}</td>");
            body.Append(Invariant, $"<td>{DayOf(row.Bought)} at {Figures.Price(row.BuyClose)}</td>");
            body.Append(row.Sold is { } on && row.SaleClose is { } close
                ? Formatted($"<td>{DayOf(on)} at {Figures.Price(close)}</td>")
                : "<td><b>held</b></td>");
            body.Append(Invariant, $"<td>{(row.Reason is { } why ? Escaped(why) : "still held")}</td>");
            body.Append(Invariant, $"<td class=\"r num\">{InPercent(row.Result)}</td><td class=\"r num\">{InPercent(row.CutReturn)}</td>");
            body.Append(row.Edge is { } edge
                ? Formatted($"<td class=\"r num\">{edge * 100:+0.0;-0.0;0.0} points</td>")
                : "<td class=\"r num\"><span class=\"degraded\">open</span></td>");
            body.Append("</tr>");
        }

        body.Append("</tbody></table></div>");

        return body.ToString();
    }

    static string Stated(double? value) => value is { } held ? held.ToString("R", Invariant) : "none";

    static string InPercent(double? fraction) =>
        fraction is { } held ? Formatted($"{held * 100:+0.0;-0.0;0.0}%") : "<span class=\"degraded\">open</span>";
}
