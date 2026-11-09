using System.Globalization;
using System.Text;

namespace EquityBrief.Web.App;

// One family's newest search over the long history on an index: the history it read through, its first test year, the
// setting it would run now or none, the rule today, its trades, the adjusted p, its years better of those counted, its
// searches and whether it passed.
public sealed record LongSearchRow(string Family, string Through, int FirstTestYear, string? Words, string Current, int Units, double? Adjusted, int Better, int Counted, int Searches, bool Passed)
{
    // From the 23.8 correction the second line: the chosen settings' trades and the rule today's against the index's own
    // fund held over the same sessions, a trade on average.
    public double? FundEdge { get; init; }

    public double? CurrentFundEdge { get; init; }
}

// One price family's newest recommended buy and sell points on an index beside the traded plan's, over the years no setting
// was chosen on.
public sealed record PlanPointRow(string Family, string Through, int Setups, string? Combination, int? Fills, double? FillRate, double? EdgePerPick, int? TradedFills, double? TradedEdgePerPick, double? LaterEdgePerPick = null, double? TradedLaterEdgePerPick = null)
{
    // From the 23.8 correction the second line beside each edge over the benchmark: the same picks against the index's own
    // fund held over the same sessions.
    public double? FundEdgePerPick { get; init; }

    public double? TradedFundEdgePerPick { get; init; }

    public double? LaterFundEdgePerPick { get; init; }

    public double? TradedLaterFundEdgePerPick { get; init; }
}

// From 23.8 the Loop page's row of the long history: each family's starting setting searched again over 2005 onward and each
// price family's buy and sell points, each a proposal that changes nothing until the operator says so.
// see: Every price family's traded plan's buy and sell points are tested over the long history and one combination is recommended a family an index
public sealed partial class SinglePageApp
{
    public const string LongSearchLede =
        "Using prices back to 2005, the system looks again for each strategy's best settings and tests each price strategy's ways of buying and selling. None of it changes anything until you say so.";

    static string Edge(double? figure) => figure is { } held ? held.ToString("+0.000;-0.000;0.000", CultureInfo.InvariantCulture) : "none";

    // What the long history came to in a sentence, on its own card and in the opening page's link to it.
    static string LongSearchSaid(IReadOnlyList<LongSearchRow> searches, IReadOnlyList<PlanPointRow> points) =>
        searches.Count == 0 && points.Count == 0
            ? "Nothing has been searched over the long history on this index yet."
            : Invariant($"{searches.Count(row => row.Passed)} of {searches.Count} searches over the long history passed, and {points.Count(row => row.Combination is not null)} of {points.Count} strategies have a recommended way to buy and sell. None of it changes anything until you say so.");

    string LongSearchCard(IReadOnlyList<LongSearchRow> searches, IReadOnlyList<PlanPointRow> points)
    {
        var body = new StringBuilder(Marks.MarkRenderer.CardSummary(LongSearchSaid(searches, points)));

        if (searches.Count == 0 && points.Count == 0)
        {
            body.Append("<p class=\"degraded\" data-long-search=\"none\">No search over the long history and no test of the buy and sell points is stored for this index. Each runs by hand with 'loop-search --long' and 'plan-points'.</p>");
        }

        if (searches.Count > 0)
        {
            body.Append("<p class=\"card-note\">Would a fresh search choose different settings? Each row sets what the search would use against today's rule. The last figures are the gain per trade over simply holding the index's own fund for the same days.</p>");
            body.Append("<div class=\"tbl-wrap\"><table class=\"loop-table\" data-table=\"long-search\"><thead><tr><th>Strategy</th><th>First test year</th><th>What the search would use, and today's rule</th><th class=\"r\">Trades</th><th class=\"r\">Chance it is luck</th><th class=\"r\">Full years better</th><th class=\"r\">Against the fund</th><th>Passed</th></tr></thead><tbody>");

            foreach (var row in searches)
            {
                var would = row.Words is null ? "no setting was good enough" : row.Words.Contains('=', StringComparison.Ordinal) ? "a different combination of this strategy's settings" : row.Words;

                body.Append(Invariant($"<tr data-family=\"{Escaped(row.Family)}\" data-passed=\"{(row.Passed ? "yes" : "no")}\"><td>{Escaped(FamilyLabel(row.Family))}</td><td class=\"n\">{row.FirstTestYear}</td><td{(row.Words is { } stored && stored.Contains('=', StringComparison.Ordinal) ? $" title=\"{Escaped(stored)}\"" : string.Empty)}>{Escaped(would)}<br><span class=\"muted\">against {Escaped(row.Current)}</span></td><td class=\"n\">{row.Units:N0}</td><td class=\"n\">{Escaped(Marks.LoopPlain.Luck(row.Adjusted))}</td><td class=\"n\">{row.Better} of {row.Counted}</td><td class=\"n\">{Edge(row.FundEdge)}<br><span class=\"muted\">today {Edge(row.CurrentFundEdge)}</span></td><td>{(row.Passed ? "yes" : "no")}</td></tr>"));
            }

            body.Append("</tbody></table></div>");
        }

        if (points.Count > 0)
        {
            body.Append("<p class=\"card-note\">The best way found to buy and sell each price strategy on 2005 to 2018, beside the plan traded today. It was picked as the best there, so its gain there looks high; the columns from 2019 cover years it was not picked on. Gains are per $1 risked, over the same plan bought on every stock in the index that day and sold the same way. The last column is the gain over holding the index's own fund for the same days.</p>");
            body.Append("<div class=\"tbl-wrap\"><table class=\"loop-table\" data-table=\"plan-points\"><thead><tr><th>Strategy</th><th>Recommended way to buy and sell</th><th class=\"r\">Times bought</th><th class=\"r\">Share of picks bought</th><th class=\"r\">Gain per pick, 2005 to 2018</th><th class=\"r\">Today's plan, 2005 to 2018</th><th class=\"r\">Gain per pick, from 2019</th><th class=\"r\">Today's plan, from 2019</th><th class=\"r\">Against the fund</th></tr></thead><tbody>");

            foreach (var row in points)
            {
                var rate = row.FillRate is { } held ? held.ToString("0%", CultureInfo.InvariantCulture) : "none";

                body.Append(Invariant($"<tr data-family=\"{Escaped(row.Family)}\"><td>{Escaped(FamilyLabel(row.Family))}</td><td>{Escaped(row.Combination ?? "none was bought often enough to judge")}</td><td class=\"n\">{(row.Fills is { } fills ? fills.ToString("N0", CultureInfo.InvariantCulture) : "none")}</td><td class=\"n\">{rate}</td><td class=\"n\">{Edge(row.EdgePerPick)}</td><td class=\"n\">{Edge(row.TradedEdgePerPick)}</td><td class=\"n\">{Edge(row.LaterEdgePerPick)}</td><td class=\"n\">{Edge(row.TradedLaterEdgePerPick)}</td><td class=\"n\">{Edge(row.FundEdgePerPick)}<br><span class=\"muted\">from 2019 {Edge(row.LaterFundEdgePerPick)}</span></td></tr>"));
            }

            body.Append("</tbody></table></div>");
        }

        return Cards.Computed(Marks.MarkRenderer.CheckUpName, body.ToString(), title: "The long history", lede: LongSearchLede, region: "loop-long");
    }
}
