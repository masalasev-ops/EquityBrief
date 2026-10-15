using System.Text;

namespace EquityBrief.Web.Marks;

// One stock a single gate short of a buy point on Tonight's one card of them: the row its family's card draws, the
// sessions to its next report as the watch list counts them, and whether the watch list holds it.
public sealed record CloseToBuyRow(CloseToBuyCell Cell, int? SessionsToReport, bool Watched);

// Tonight's card of the stocks one gate short of a buy point across every setup, from 19.8: folded to one line counting
// them as the market line counts them, opened to one table in the page's family order with each family's rows as its own
// card orders them, a Watch press a row on the newest night and a link to Universe with its one gate short chip lit. An
// index whose night stores only the first part each member failed reads no stock as one gate short and says so.
// see: A member that missed exactly one gate and no exclusion is drawn close to a buy point nearest first, and recommends nothing
// see: The watch list is the operator's own, up to twenty names of the index, on a page of its own
public sealed partial class MarkRenderer
{
    // The card's anchor and the key and value the page's link keeps it open under.
    public const string CloseToBuyId = "close-to-buy";

    public const string CloseToBuyKey = "close";

    public const string CloseToBuyOpen = "open";

    public const string CloseToBuyGuide = "Each passed every gate of its setup but one tonight with nothing excluding it; none is a pick and none is counted as one.";

    // The card: the rows in the order given, which the read surface puts in the page's family order; open where the link
    // keeps it open; the presses and the Universe link on the newest night alone.
    public string CloseToBuy(IReadOnlyList<CloseToBuyRow>? rows, bool open, bool newest, string? universe)
    {
        var html = new StringBuilder();

        html.Append(Invariant, $"<section class=\"card folded close-to-buy\" id=\"{CloseToBuyId}\" data-card=\"close-to-buy\" data-count=\"{(rows is null ? "none" : rows.Count.ToString(Invariant))}\" data-newest=\"{Flag(newest)}\"><div class=\"card-b\">");

        if (rows is null)
        {
            html.Append("<p class=\"card-one-line degraded\" data-close=\"not read\">One gate short of a buy point: this index's night stores only the first part each member failed, so no stock is read as one gate short.</p></div></section>");

            return html.ToString();
        }

        var line = Formatted($"{Count(rows.Count, "stock")} one gate short of a buy point");

        if (rows.Count == 0)
        {
            html.Append(Invariant, $"<p class=\"card-one-line\" data-close=\"0\">{line}</p></div></section>");

            return html.ToString();
        }

        html.Append(Invariant, $"<details class=\"close-to-buy\" data-link-key=\"{CloseToBuyKey}\"{(open ? " open" : string.Empty)}><summary class=\"card-one-line\">{line}</summary>");
        html.Append("<div class=\"watch-said\" role=\"status\"></div>");
        html.Append(Invariant, $"<div class=\"tbl-wrap\"><table class=\"list-table close-to-buy-table\" data-rows=\"{rows.Count}\"><thead><tr>");
        html.Append(TippedHeading("Setup", "the setup the stock is one gate short of, in the page's family order"));
        html.Append(TippedHeading("Stock", "the ticker opens the stock's page, with the company beneath"));
        html.Append(TippedHeading("The gate it missed", "the one gate of its setup it did not pass tonight"));
        html.Append(TippedHeading("What it had", "what it had against the bar the gate needed"));
        html.Append(TippedHeading("Report in", "the sessions to its next report, as the watch list counts them", "r"));

        if (newest)
        {
            html.Append(TippedHeading("Watch", "puts the stock on your watch list, which holds twenty"));
        }

        html.Append("</tr></thead><tbody>");

        foreach (var row in rows)
        {
            var cell = row.Cell;

            html.Append(Invariant, $"<tr data-ticker=\"{Escaped(cell.Ticker)}\" data-family=\"{Escaped(cell.Family)}\" data-gate=\"{Escaped(cell.Gate)}\" data-watched=\"{Flag(row.Watched)}\">");
            html.Append(Invariant, $"<td><span class=\"fam-tag\" data-family=\"{Escaped(cell.Family)}\"><span class=\"fam-swatch\" data-family=\"{Escaped(cell.Family)}\"></span>{Escaped(cell.Setup)}</span></td>");
            html.Append(Invariant, $"<td class=\"c-nm\"><a class=\"name-link\" href=\"#/name/{Uri.EscapeDataString(cell.Ticker)}\">{Escaped(cell.Ticker)}</a>{(cell.Company is { Length: > 0 } company ? Formatted($"<span class=\"co\">{Escaped(company)}</span>") : string.Empty)}</td>");
            html.Append(Invariant, $"<td class=\"missed-gate\"><b>{Escaped(cell.Gate)}</b></td>");
            html.Append(Invariant, $"<td class=\"close-had\">{Escaped(cell.Words)}</td>");
            html.Append(row.SessionsToReport is { } sessions
                ? Formatted($"<td class=\"r num\" data-sessions=\"{sessions}\">{Count(sessions, "session")}</td>")
                : "<td class=\"r\"><span class=\"degraded\">no date</span></td>");

            if (newest)
            {
                html.Append(row.Watched
                    ? "<td><span class=\"watching\" data-watching=\"true\">Watching</span></td>"
                    : Formatted($"<td><form class=\"watch-control close-watch\" method=\"post\" action=\"{App.SinglePageApp.WatchPostRoute}\" data-ticker=\"{Escaped(cell.Ticker)}\"><button type=\"submit\" aria-label=\"Add {Escaped(cell.Ticker)} to the watch list\">Watch</button></form></td>"));
            }

            html.Append("</tr>");
        }

        html.Append("</tbody></table></div>");
        html.Append(Invariant, $"<p class=\"forming-key\">{Escaped(CloseToBuyGuide)}</p>");

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
