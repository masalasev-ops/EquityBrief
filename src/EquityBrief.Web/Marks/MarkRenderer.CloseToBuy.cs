using System.Text;

namespace EquityBrief.Web.Marks;

// One stock a single gate short of a buy point on Tonight's one card of them: the row its family's card draws, the
// sessions to its next report as the watch list counts them, and whether the watch list holds it.
public sealed record CloseToBuyRow(CloseToBuyCell Cell, int? SessionsToReport, bool Watched);

// Tonight's card of the stocks one gate short of a buy point across every setup, from 19.8: folded to one line counting
// them as the market line counts them, opened to one table in the page's family order with each family's rows as its own
// card orders them, a Watch press a row on the newest night and a link to Universe with its one gate short chip lit. An
// index whose night stores only the first part each member failed reads no stock as one gate short and says so, and a
// night reading none one gate short draws no card, the counts that would open it saying nought with no link.
// see: A member that missed exactly one gate and no exclusion is drawn close to a buy point nearest first, and recommends nothing
// see: The watch list is the operator's own, up to twenty names of the index, on a page of its own
public sealed partial class MarkRenderer
{
    // The card's anchor and the key and value the page's link keeps it open under.
    public const string CloseToBuyId = "close-to-buy";

    public const string CloseToBuyKey = "close";

    public const string CloseToBuyOpen = "open";

    // The card, from 22.6 One step from qualifying: its summary counting the stocks, a line saying none is a pick, and the
    // rows behind a fold, open where the link keeps it open, each with the step it missed and where it stands in the
    // reader's words; the presses and the Universe link on the newest night alone. A night reading none one step short
    // draws no card, tonight in one minute saying none is close, and a night whose answers say which it cannot read says
    // so in its summary.
    public string CloseToBuy(IReadOnlyList<CloseToBuyRow>? rows, bool open, bool newest, string? universe)
    {
        if (rows is { Count: 0 })
        {
            return string.Empty;
        }

        var html = new StringBuilder();

        html.Append(Invariant, $"<section class=\"card close-to-buy\" id=\"{CloseToBuyId}\" data-card=\"close-to-buy\" data-count=\"{(rows is null ? "none" : rows.Count.ToString(Invariant))}\" data-newest=\"{Flag(newest)}\">");
        html.Append("<div class=\"card-h\"><div><div class=\"lbl\">One step from qualifying</div></div></div><div class=\"card-b\">");

        if (rows is null)
        {
            html.Append("<p class=\"card-summary degraded\" data-summary data-close=\"not read\">Which stocks are one step from qualifying is not read on this index tonight: its night stores only the first step each stock missed.</p></div></section>");

            return html.ToString();
        }

        var line = rows.Count == 1 ? "1 stock missed a pick by a single step tonight." : Formatted($"{rows.Count} stocks missed a pick by a single step tonight.");

        html.Append(Invariant, $"<p class=\"card-summary\" data-summary data-close=\"{rows.Count}\">{Escaped(line)}</p>");
        html.Append(Invariant, $"<p class=\"close-said\">Each passed every step of its setup but one. None is a pick.{(newest ? " Add any to your watch list to see it come back here or become a pick." : string.Empty)}</p>");
        html.Append(Invariant, $"<details class=\"close-to-buy\" data-link-key=\"{CloseToBuyKey}\"{(open ? " open" : string.Empty)}><summary class=\"card-one-line\">See {(rows.Count == 1 ? "the stock" : Formatted($"the {rows.Count} stocks"))} and the step each missed</summary>");
        html.Append("<div class=\"watch-said\" role=\"status\"></div>");
        html.Append(Invariant, $"<div class=\"tbl-wrap\"><table class=\"list-table close-to-buy-table\" data-rows=\"{rows.Count}\"><thead><tr>");
        html.Append(TippedHeading("Setup", "the setup the stock is one step short of"));
        html.Append(TippedHeading("Stock", "the ticker opens the stock's chart, with the company beneath"));
        html.Append(TippedHeading("The step it missed", "the one step of its setup it did not pass tonight"));
        html.Append(TippedHeading("Where it stands", "what it had against what the step needed"));
        html.Append(TippedHeading("Next report", "the trading days to its next report", "r"));

        if (newest)
        {
            html.Append(TippedHeading("Watch", "puts the stock on your watch list, which holds twenty"));
        }

        html.Append("</tr></thead><tbody>");

        foreach (var row in rows)
        {
            var cell = row.Cell;
            var missed = ReaderWords.MissedBy(cell.Family, cell.Gate, cell.Words) ?? ReaderWords.Plain(cell.Gate);

            html.Append(Invariant, $"<tr data-ticker=\"{Escaped(cell.Ticker)}\" data-family=\"{Escaped(cell.Family)}\" data-gate=\"{Escaped(cell.Gate)}\" data-watched=\"{Flag(row.Watched)}\">");
            html.Append(Invariant, $"<td><span class=\"fam-tag\" data-family=\"{Escaped(cell.Family)}\"><span class=\"fam-swatch\" data-family=\"{Escaped(cell.Family)}\"></span>{Escaped(cell.Setup)}</span></td>");
            html.Append(Invariant, $"<td class=\"c-nm\"><a class=\"name-link\" href=\"#/chart/{Uri.EscapeDataString(cell.Ticker)}\">{Escaped(cell.Ticker)}</a>{(cell.Company is { Length: > 0 } company ? Formatted($"<span class=\"co\">{Escaped(company)}</span>") : string.Empty)}</td>");
            html.Append(Invariant, $"<td class=\"missed-gate\"><b>{Escaped(char.ToUpperInvariant(missed[0]) + missed[1..])}</b></td>");
            html.Append(Invariant, $"<td class=\"close-had\">{Escaped(ReaderWords.Plain(StoredWords(cell.Words)))}</td>");
            html.Append(row.SessionsToReport is { } sessions
                ? Formatted($"<td class=\"r num\" data-sessions=\"{sessions}\">in {sessions} trading {(sessions == 1 ? "day" : "days")}</td>")
                : "<td class=\"r\"><span class=\"degraded\">no date on file</span></td>");

            if (newest)
            {
                html.Append(row.Watched
                    ? "<td><span class=\"watching\" data-watching=\"true\">Watching</span></td>"
                    : Formatted($"<td><form class=\"watch-control close-watch\" method=\"post\" action=\"{App.SinglePageApp.WatchPostRoute}\" data-ticker=\"{Escaped(cell.Ticker)}\"><button type=\"submit\" aria-label=\"Add {Escaped(cell.Ticker)} to the watch list\">Watch</button></form></td>"));
            }

            html.Append("</tr>");
        }

        html.Append("</tbody></table></div>");

        if (newest && universe is { Length: > 0 })
        {
            html.Append(Invariant, $"<p class=\"close-universe\"><a href=\"{UniverseHref(null, null, universe, new UniverseState(Short: true).Extra())}\">See them with their readings on Universe</a></p>");
        }

        html.Append("</details></div></section>");

        return html.ToString();
    }

    // The link a line presses to open the card and scroll to it, the night's own route with the index and the card's key.
    public static string CloseToBuyHref(string route, string? universe) =>
        route + (route.Contains('?', StringComparison.Ordinal) ? "&" : "?")
        + (universe is { Length: > 0 } word ? "universe=" + Uri.EscapeDataString(word) + "&" : string.Empty)
        + CloseToBuyKey + "=" + CloseToBuyOpen;
}
