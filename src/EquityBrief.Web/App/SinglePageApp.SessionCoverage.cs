using System.Text;

namespace EquityBrief.Web.App;

// One index's year of every member's readings on every session, as counted where they were written.
public sealed record SessionCoverageRow(string Index, int Year, long Rows, int Sessions, int NightSessions, string First, string Last);

// The History page's coverage of every member's readings on every session: on each index by year, the rows, the members a
// session they come to and the sessions a night stored its own rows of, with each index's total.
// see: The readings for every stock on every session are built once before the reviewer, the picker's comparison reading the same plan on every stock until then
public sealed partial class SinglePageApp
{
    public const string SessionCoverageLede =
        "Every member's readings on every session from 2005, the members as they stood that day, for the reviewer and the picker's own rules to match on. A session a night stored is the night's own rows; every other one is worked from the long history the same way.";

    static readonly (string Index, string Name)[] CoverageIndices = [("GSPC", "S&P 500"), ("MID", "S&P 400"), ("SML", "S&P 600")];

    string SessionCoverageCard(IReadOnlyList<SessionCoverageRow> rows)
    {
        if (rows.Count == 0)
        {
            return Cards.Computed("Every session", "<p class=\"empty\">Not built yet. <code>ledger-build --every-session --index &lt;GSPC, MID or SML&gt; --word</code> builds it on each index.</p>", title: "Every member on every session", lede: SessionCoverageLede, region: "session-coverage");
        }

        var body = new StringBuilder();

        body.Append("<div class=\"tbl-wrap\"><table class=\"history-table\" data-table=\"session-coverage\"><thead><tr><th>Year</th>");
        body.Append(string.Concat(CoverageIndices.Select(index => $"<th class=\"r\" colspan=\"2\">{Escaped(index.Name)}</th>")));
        body.Append("</tr><tr><th></th>");
        body.Append(string.Concat(CoverageIndices.Select(_ => "<th class=\"r\">Rows</th><th class=\"r\">Members a session</th>")));
        body.Append("</tr></thead><tbody>");

        foreach (var year in rows.Select(row => row.Year).Distinct().Order())
        {
            body.Append(Invariant($"<tr data-year=\"{year}\"><td>{year}</td>"));

            foreach (var (index, _) in CoverageIndices)
            {
                body.Append(rows.FirstOrDefault(row => row.Index == index && row.Year == year) is { } held
                    ? Invariant($"<td class=\"n\" data-index=\"{index}\">{held.Rows:N0}{(held.NightSessions > 0 ? Invariant($" <span class=\"on\">{held.NightSessions} session(s) from the nights</span>") : string.Empty)}</td><td class=\"n\">{(held.Sessions == 0 ? 0 : 1.0 * held.Rows / held.Sessions):N0}</td>")
                    : Invariant($"<td class=\"n\" data-index=\"{index}\">none</td><td class=\"n\">none</td>"));
            }

            body.Append("</tr>");
        }

        body.Append("<tr class=\"total\"><td>Every year</td>");

        foreach (var (index, _) in CoverageIndices)
        {
            var held = rows.Where(row => row.Index == index).ToArray();

            body.Append(held.Length == 0
                ? Invariant($"<td class=\"n\" data-total=\"{index}\">none</td><td class=\"n\"></td>")
                : Invariant($"<td class=\"n\" data-total=\"{index}\">{held.Sum(row => row.Rows):N0}</td><td class=\"n\"><span class=\"on\">{held.Min(row => row.First)} to {held.Max(row => row.Last)}</span></td>"));
        }

        body.Append("</tr></tbody></table></div>");

        return Cards.Computed("Every session", body.ToString(), title: "Every member on every session", lede: SessionCoverageLede, region: "session-coverage");
    }
}
