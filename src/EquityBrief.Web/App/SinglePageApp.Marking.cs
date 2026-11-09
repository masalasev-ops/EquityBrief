using System.Globalization;
using System.Text;
using EquityBrief.Core.History;

namespace EquityBrief.Web.App;

// One session the operator is asked to mark: an unexplained jump or an end no filing settles, its stock and session, the era
// and the data check's words where it is a jump, its evidence, the mark proposed with its words, and the mark already given,
// none where it has none.
public sealed record MarkingRow(string Kind, string Ticker, DateOnly Session, string? Era, string? Says, MarkEvidence Evidence, string Proposed, string ProposedSays, string? Marked)
{
    public const string Jump = "jump";

    public const string End = "end";

    public string Key => Ticker + "|" + Session.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}

// One long ledger setup counted as no result, its edge past 20 risks either way.
public sealed record OutlyingRow(string Index, string Family, string Ticker, DateOnly Session, double Edge);

// What the marking page draws.
public sealed record MarkingView(IReadOnlyList<MarkingRow> Jumps, IReadOnlyList<MarkingRow> Ends, IReadOnlyList<OutlyingRow> Outlying);

// From the 23.4 correction the marking page: every unexplained jump and every end no filing settles with its evidence and a
// proposed mark, which the operator confirms in one press, changes one at a time, or leaves, and the long ledger's setups
// counted as no result beside them. Nothing is marked without the operator's confirm.
// see: The marking page proposes a mark for each unexplained jump and unsettled end from its evidence, and writes none without the operator's confirm
public sealed partial class SinglePageApp
{
    public const string MarkingRoute = "#/marking";

    public const string MarkingPostRoute = "/marks/confirm";

    public const string Unmarked = "leave unmarked";

    public const string MarkingLede =
        "Each move the data check could not explain and each stock whose prices end with no filing saying why, with the days around it, its volume and any filing beside it. Each has a proposed mark: confirm them all at once, change any one, or leave it. Nothing is marked until you confirm.";

    public string MarkingRegion(MarkingView view)
    {
        var region = new StringBuilder();
        var open = view.Jumps.Concat(view.Ends).Count(row => row.Marked is null);

        region.Append("<section class=\"marking\">");
        region.Append(Cards.Masthead("Marking", "<span class=\"m-screen\">Marking</span>", Invariant($"{open:N0} to mark")));

        var body = new StringBuilder(Marks.MarkRenderer.CardSummary(Invariant($"{view.Jumps.Count:N0} unexplained jumps and {view.Ends.Count:N0} unsettled ends, {open:N0} not yet marked; {view.Outlying.Count:N0} long setups past 20 risks are counted as no result.")));

        body.Append(Invariant($"<form class=\"marking-press\" action=\"{MarkingPostRoute}\" method=\"post\">"));
        body.Append("<p class=\"marking-said\" role=\"status\"></p>");
        body.Append("<p><button type=\"submit\" name=\"all\" value=\"1\">Confirm every mark as chosen below</button></p>");
        Table(body, "Unexplained jumps", "jumps", view.Jumps);
        Table(body, "Ends no filing settles", "ends", view.Ends);
        body.Append("<p><button type=\"submit\" name=\"all\" value=\"1\">Confirm every mark as chosen above</button></p>");
        body.Append("</form>");

        body.Append(Invariant($"<p class=\"card-note\">Long setups whose edge lies past 20 risks either way, counted as no result: {view.Outlying.Count:N0}.</p>"));

        if (view.Outlying.Count > 0)
        {
            body.Append("<div class=\"tbl-wrap\"><table class=\"loop-table\" data-table=\"outlying\"><thead><tr><th>Index</th><th>Setup</th><th>Stock</th><th>Day</th><th class=\"r\">Edge</th></tr></thead><tbody>");

            foreach (var row in view.Outlying)
            {
                body.Append(FormattableString.Invariant($"<tr><td>{Escaped(row.Index)}</td><td>{Escaped(row.Family)}</td><td>{Escaped(row.Ticker)}</td><td class=\"n\">{row.Session:yyyy-MM-dd}</td><td class=\"n\">{row.Edge:+0.0;-0.0;0.0}</td></tr>"));
            }

            body.Append("</tbody></table></div>");
        }

        region.Append(Cards.Computed("Marking", body.ToString(), title: "Marks owed", lede: MarkingLede, region: "marking"));
        region.Append("</section>");

        return region.ToString();
    }

    static void Table(StringBuilder body, string title, string list, IReadOnlyList<MarkingRow> rows)
    {
        body.Append(Invariant($"<h3>{Escaped(title)}: {rows.Count:N0}</h3>"));
        body.Append(Invariant($"<div class=\"tbl-wrap\"><table class=\"loop-table\" data-table=\"{list}\"><thead><tr><th>#</th><th>Stock</th><th>Day</th><th>The days around it</th><th>Filings beside it</th><th>Mark</th></tr></thead><tbody>"));

        var number = 0;

        foreach (var row in rows)
        {
            number++;

            var around = string.Join(" ", row.Evidence.Around.Select(one => one.Session == row.Session
                ? Invariant($"<b>{one.Close:0.00} ({Volume(one.Volume)})</b>")
                : Invariant($"{one.Close:0.00} ({Volume(one.Volume)})")));
            var median = row.Evidence.MedianVolume is { } held ? "median " + Volume((long)Math.Round(held)) : "no median";
            var filings = row.Evidence.Filings.Count == 0
                ? "none"
                : string.Join("; ", row.Evidence.Filings.Select(filing => Invariant($"{Escaped(filing.Form)}{(filing.Items is { } items ? " " + Escaped(items) : string.Empty)} {filing.Filed:yyyy-MM-dd}")));

            body.Append(FormattableString.Invariant($"<tr data-mark=\"{Escaped(row.Key)}\"><td class=\"n\">{number}</td><td>{Escaped(row.Ticker)}</td><td class=\"n\">{row.Session:yyyy-MM-dd}</td><td class=\"muted\">{around}<br>{median}{(row.Says is { } says ? "<br>" + Escaped(says) : string.Empty)}</td><td>{filings}</td><td>"));

            if (row.Marked is { } marked)
            {
                body.Append(Invariant($"marked: {Escaped(marked)}"));
            }
            else
            {
                body.Append(FormattableString.Invariant($"<select name=\"m|{Escaped(row.Key)}\" aria-label=\"Mark for {Escaped(row.Ticker)} {row.Session:yyyy-MM-dd}\">"));

                foreach (var kind in new[] { row.Proposed }.Concat(MarkProposals.Kinds.Where(kind => kind != row.Proposed)).Append(Unmarked))
                {
                    body.Append(Invariant($"<option value=\"{Escaped(kind == Unmarked ? string.Empty : kind)}\"{(kind == row.Proposed ? " selected" : string.Empty)}>{Escaped(kind == row.Proposed ? kind + " (proposed)" : kind)}</option>"));
                }

                body.Append("</select>");
                body.Append(Invariant($"<br><span class=\"muted\">{Escaped(row.ProposedSays)}</span>"));
                body.Append(Invariant($"<br><button type=\"submit\" name=\"only\" value=\"{Escaped(row.Key)}\">Confirm this one</button>"));
            }

            body.Append("</td></tr>");
        }

        body.Append("</tbody></table></div>");
    }

    static string Volume(long shares) =>
        shares >= 1_000_000 ? Invariant($"{shares / 1_000_000.0:0.0}M") : shares >= 1_000 ? Invariant($"{shares / 1_000.0:0}K") : Invariant($"{shares}");
}
