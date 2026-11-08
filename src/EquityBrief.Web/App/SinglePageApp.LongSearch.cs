using System.Globalization;
using System.Text;

namespace EquityBrief.Web.App;

// One family's newest search over the long history on an index: the history it read through, its first test year, the
// setting it would run now or none, the rule today, its trades, the adjusted p, its years better of those counted, its
// searches and whether it passed.
public sealed record LongSearchRow(string Family, string Through, int FirstTestYear, string? Words, string Current, int Units, double? Adjusted, int Better, int Counted, int Searches, bool Passed);

// One price family's newest recommended buy and sell points on an index beside the traded plan's, over the years no setting
// was chosen on.
public sealed record PlanPointRow(string Family, string Through, int Setups, string? Combination, int? Fills, double? FillRate, double? EdgePerPick, int? TradedFills, double? TradedEdgePerPick, double? LaterEdgePerPick = null, double? TradedLaterEdgePerPick = null);

// From 23.8 the Loop page's row of the long history: each family's starting setting searched again over 2005 onward and each
// price family's buy and sell points, each a proposal that changes nothing until the operator says so.
// see: Every price family's traded plan's buy and sell points are tested over the long history and one combination is recommended a family an index
public sealed partial class SinglePageApp
{
    public const string LongSearchLede =
        "Each family's starting setting searched again over 2005 onward, every third year with the setting carried between, and each price family's buy and sell points tested over the years no setting was chosen on. Each is a proposal and changes nothing until you say so.";

    static string Edge(double? figure) => figure is { } held ? held.ToString("+0.000;-0.000;0.000", CultureInfo.InvariantCulture) : "none";

    string LongSearchCard(IReadOnlyList<LongSearchRow> searches, IReadOnlyList<PlanPointRow> points)
    {
        var passed = searches.Count(row => row.Passed);
        var body = new StringBuilder(Marks.MarkRenderer.CardSummary(searches.Count == 0 && points.Count == 0
            ? "Nothing has been searched over the long history on this index yet."
            : Invariant($"{passed} of {searches.Count} setups' searches over the long history passed, and {points.Count(row => row.Combination is not null)} of {points.Count} setups have buy and sell points recommended; none of it changes anything until you say so.")));

        if (searches.Count == 0 && points.Count == 0)
        {
            body.Append("<p class=\"degraded\" data-long-search=\"none\">No search over the long history and no test of the buy and sell points is stored for this index. Each runs by hand with 'loop-search --long' and 'plan-points'.</p>");
        }

        if (searches.Count > 0)
        {
            body.Append("<p class=\"card-note\">Each family's setting searched again from its first test year, against the rule today.</p>");
            body.Append("<div class=\"tbl-wrap\"><table class=\"loop-table\" data-table=\"long-search\"><thead><tr><th>Family</th><th>From</th><th>Would run, and the rule today</th><th class=\"r\">Trades</th><th class=\"r\">Adjusted p</th><th class=\"r\">Years better</th><th>Passed</th></tr></thead><tbody>");

            foreach (var row in searches)
            {
                var p = row.Adjusted is { } held ? held.ToString("0.0000", CultureInfo.InvariantCulture) : "none";

                body.Append(Invariant($"<tr data-family=\"{Escaped(row.Family)}\" data-passed=\"{(row.Passed ? "yes" : "no")}\"><td>{Escaped(row.Family)}</td><td class=\"n\">{row.FirstTestYear}</td><td>{Escaped(row.Words ?? "no setting meets the floors")}<br><span class=\"muted\">against {Escaped(row.Current)}</span></td><td class=\"n\">{row.Units:N0}</td><td class=\"n\">{p}</td><td class=\"n\">{row.Better} of {row.Counted}</td><td>{(row.Passed ? "yes" : "no")}</td></tr>"));
            }

            body.Append("</tbody></table></div>");
        }

        if (points.Count > 0)
        {
            body.Append("<p class=\"card-note\">Each price family's best way to buy and sell on 2005 to 2018, the years no setting was chosen on, beside the plan traded today. It was picked as the best there, so its edge there reads high; from 2019 is the years it was not picked on.</p>");
            body.Append("<div class=\"tbl-wrap\"><table class=\"loop-table\" data-table=\"plan-points\"><thead><tr><th>Family</th><th>Recommended buy and sell points</th><th class=\"r\">Fills</th><th class=\"r\">Fill rate</th><th class=\"r\">Edge per pick</th><th class=\"r\">Traded plan's edge per pick</th><th class=\"r\">From 2019</th><th class=\"r\">Traded plan's from 2019</th></tr></thead><tbody>");

            foreach (var row in points)
            {
                var rate = row.FillRate is { } held ? held.ToString("0%", CultureInfo.InvariantCulture) : "none";

                body.Append(Invariant($"<tr data-family=\"{Escaped(row.Family)}\"><td>{Escaped(row.Family)}</td><td>{Escaped(row.Combination ?? "none rests on enough fills")}</td><td class=\"n\">{(row.Fills is { } fills ? fills.ToString("N0", CultureInfo.InvariantCulture) : "none")}</td><td class=\"n\">{rate}</td><td class=\"n\">{Edge(row.EdgePerPick)}</td><td class=\"n\">{Edge(row.TradedEdgePerPick)}</td><td class=\"n\">{Edge(row.LaterEdgePerPick)}</td><td class=\"n\">{Edge(row.TradedLaterEdgePerPick)}</td></tr>"));
            }

            body.Append("</tbody></table></div>");
        }

        return Cards.Computed("Loop", body.ToString(), title: "The long history", lede: LongSearchLede, region: "loop-long");
    }
}
