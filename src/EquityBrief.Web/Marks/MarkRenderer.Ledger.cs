using System.Text;
using EquityBrief.Core.Families;
using EquityBrief.Core.Ledger;

namespace EquityBrief.Web.Marks;

// The Ledger page's marks: a family's setups a year with the share the live rule passes and the share the night's list
// picked, the cut points between the deciles of result and edge, its newest settled setups to choose from, and the
// chosen setup's closes drawn with its plan's lines. Every figure is read off the summary the ledger's writers refresh
// or off the setup's own row; the marks draw and compute none.
// see: A setup is every member-session a family's loose gates pass, and its readings are defined once and read as they stood
// see: A heavyweights' setup is each member of its sector's size cut on a rebalance of the S&P 500's book, held as the rule holds a buy
public sealed partial class MarkRenderer
{
    const int LedgerWidth = 560;

    const int LedgerHeight = 200;

    const int LedgerTop = 22;

    const int LedgerRight = 92;

    // A heavyweights' setup is read as a fraction of the buy and every other family's in risks.
    static string LedgerFigure(string family, double? value) =>
        value is not { } held
            ? "<span class=\"degraded\">none</span>"
            : family == HeavyweightRule.Name
                ? Formatted($"{(held * 100).ToString("+0.0;-0.0;0.0", Invariant)}%")
                : Formatted($"{held.ToString("+0.00;-0.00;0.00", Invariant)} &#215;");

    public string LedgerYears(string family, IReadOnlyList<LedgerYear> years)
    {
        var table = new StringBuilder();

        table.Append(Invariant, $"<div class=\"tbl-wrap\"><table class=\"ledger-years\" data-family=\"{Escaped(family)}\" data-rows=\"{years.Count}\"><thead><tr>");
        table.Append(TippedHeading("Year", "the calendar year of the setups' sessions"));
        table.Append(TippedHeading("Setups", "the member-sessions the family's loose gates passed that year", "r"));
        table.Append(TippedHeading("Live rule", "the share of them the live rule's own setting passes", "r"));
        table.Append(TippedHeading("Picked", "of the setups the night wrote, the share its list picked; the history build writes no pick", "r"));
        table.Append(TippedHeading("Settled", "the setups whose path and benchmark have both ended", "r"));
        table.Append(TippedHeading("Result", family == HeavyweightRule.Name ? "the settled setups' mean return as a share of the buy" : "the settled setups' mean result in multiples of their risk", "r"));
        table.Append(TippedHeading("Edge", family == HeavyweightRule.Name ? "the mean of each return less its size cut's, in points" : "the mean of each result less the same plan on every member that session, in risks", "r"));
        table.Append("</tr></thead><tbody>");

        foreach (var year in years)
        {
            var passed = year.Setups > 0 ? Formatted($"{100.0 * year.LivePasses / year.Setups:0}%") : "<span class=\"degraded\">none</span>";
            var picked = year.NightRows > 0 ? Formatted($"{100.0 * year.Picked / year.NightRows:0}% of {year.NightRows}") : "<span class=\"degraded\">none written by a night</span>";

            table.Append(Invariant, $"<tr data-year=\"{year.Year}\" data-setups=\"{year.Setups}\" data-live-passes=\"{year.LivePasses}\" data-picked=\"{year.Picked}\" data-settled=\"{year.Settled}\">");
            table.Append(Invariant, $"<td class=\"num\">{year.Year}</td><td class=\"r num\">{year.Setups}</td><td class=\"r num\">{passed}</td><td class=\"r num\">{picked}</td><td class=\"r num\">{year.Settled}</td>");
            table.Append(Invariant, $"<td class=\"r num\" data-result-mean=\"{(year.ResultMean is { } r ? r.ToString("R", Invariant) : "none")}\">{LedgerFigure(family, year.ResultMean)}</td>");
            table.Append(Invariant, $"<td class=\"r num\" data-edge-mean=\"{(year.EdgeMean is { } e ? e.ToString("R", Invariant) : "none")}\">{LedgerFigure(family, year.EdgeMean)}</td></tr>");
        }

        table.Append("</tbody></table></div>");

        return table.ToString();
    }

    // The cut points between the deciles of the newest year holding settled setups, result beside edge.
    public string LedgerDeciles(LedgerYear? year)
    {
        if (year?.ResultDeciles is not { } results || year.EdgeDeciles is not { } edges)
        {
            return "<p class=\"degraded\" data-deciles=\"none\">No setup of the family has settled yet, so no decile is drawn.</p>";
        }

        var table = new StringBuilder();

        table.Append(Invariant, $"<div class=\"tbl-wrap\"><table class=\"ledger-deciles\" data-family=\"{Escaped(year.Family)}\" data-year=\"{year.Year}\" data-settled=\"{year.Settled}\"><thead><tr>");
        table.Append(TippedHeading("Share below", "the share of the year's settled setups at or under the figure beside it"));
        table.Append(TippedHeading("Result", "the result at that share", "r"));
        table.Append(TippedHeading("Edge", "the edge at that share", "r"));
        table.Append("</tr></thead><tbody>");

        for (var cut = 0; cut < LedgerSummaries.CutPoints; cut++)
        {
            table.Append(Invariant, $"<tr data-cut=\"{cut + 1}\"><td class=\"num\">{(cut + 1) * 10}%</td><td class=\"r num\" data-result=\"{results[cut].ToString("R", Invariant)}\">{LedgerFigure(year.Family, results[cut])}</td><td class=\"r num\" data-edge=\"{edges[cut].ToString("R", Invariant)}\">{LedgerFigure(year.Family, edges[cut])}</td></tr>");
        }

        table.Append("</tbody></table></div>");

        return table.ToString();
    }

    // The family's newest settled setups, each a link drawing its path on the page.
    public string LedgerSettled(IReadOnlyList<LedgerSetupRow> rows, string universe, string route)
    {
        if (rows.Count == 0)
        {
            return "<p class=\"degraded\" data-settled=\"none\">No setup of the family has settled yet.</p>";
        }

        var table = new StringBuilder();

        table.Append(Invariant, $"<div class=\"tbl-wrap\"><table class=\"ledger-settled\" data-rows=\"{rows.Count}\"><thead><tr>");
        table.Append(TippedHeading("Session", "the session the setup was read on and bought at the close of"));
        table.Append(TippedHeading("Stock", "the member, which opens the setup's path below"));
        table.Append(TippedHeading("Live rule", "whether the live rule's own setting passed it"));
        table.Append(TippedHeading("End", "how its path ended"));
        table.Append(TippedHeading("Result", "what its path came to", "r"));
        table.Append(TippedHeading("Edge", "its result less its benchmark", "r"));
        table.Append("</tr></thead><tbody>");

        foreach (var row in rows)
        {
            var link = Formatted($"{route}?universe={Uri.EscapeDataString(universe)}&family={Uri.EscapeDataString(row.Family)}&setup={Uri.EscapeDataString(row.Ticker)}.{DayOf(row.Session)}");

            table.Append(Invariant, $"<tr data-ticker=\"{Escaped(row.Ticker)}\" data-session=\"{DayOf(row.Session)}\" data-end=\"{Escaped(row.End)}\">");
            table.Append(Invariant, $"<td class=\"num\">{DayOf(row.Session)}</td><td><a class=\"ledger-choose\" href=\"{Escaped(link)}\">{Escaped(row.Ticker)}</a></td>");
            table.Append(Invariant, $"<td>{(row.LivePass ? "passed" : "not passed")}</td><td>{Escaped(row.End)}</td>");
            table.Append(Invariant, $"<td class=\"r num\">{LedgerFigure(row.Family, row.Result)}</td><td class=\"r num\">{LedgerFigure(row.Family, row.Edge)}</td></tr>");
        }

        table.Append("</tbody></table></div>");

        return table.ToString();
    }

    // A chosen setup's closes from ten sessions before it to where its path ended, with its buy, its stop where it holds
    // one and its target where it names one drawn across, and the session it was bought on marked.
    public string LedgerPath(LedgerPath? path)
    {
        if (path is null)
        {
            return "<p class=\"degraded\" data-path=\"none\">Choose a settled setup above to draw its path.</p>";
        }

        var setup = path.Setup;

        if (path.Closes.Count < 2)
        {
            return Formatted($"<p class=\"degraded\" data-path=\"no-closes\" data-ticker=\"{Escaped(setup.Ticker)}\">The store holds {path.Closes.Count} close(s) of {Escaped(setup.Ticker)} over this setup's sessions: its bars sit in the pulled history, which this page does not read.</p>");
        }

        var lines = new List<(string Named, string Side, decimal Price)> { ("buy", "buy", setup.Entry) };

        if (setup.Stop > 0m)
        {
            lines.Add(("stop", "sup", setup.Stop));
        }

        if (setup.Target is { } target)
        {
            lines.Add(("target", "res", target));
        }

        var prices = path.Closes.Select(close => PlotValue(close.Close)).Concat(lines.Select(line => PlotValue(line.Price))).ToArray();
        var low = prices.Min();
        var high = prices.Max();
        var span = high - low > 0 ? high - low : 1;
        double plotWidth = LedgerWidth - LedgerRight - 4;
        var step = plotWidth / (path.Closes.Count - 1);

        double Y(double plotted) => LedgerTop + ((LedgerHeight - LedgerTop - 6) * (1 - ((plotted - low) / span)));

        var svg = new StringBuilder();

        svg.Append(Invariant, $"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 {LedgerWidth} {LedgerHeight}\" width=\"{LedgerWidth}\" height=\"{LedgerHeight}\" role=\"img\" class=\"ledger-path\" data-ticker=\"{Escaped(setup.Ticker)}\" data-session=\"{DayOf(setup.Session)}\" data-closes=\"{path.Closes.Count}\">");
        svg.Append(Invariant, $"<title>{Escaped(setup.Ticker)}'s {Escaped(setup.Family)} setup of {DayOf(setup.Session)}, {path.Closes.Count} closes from {DayOf(path.Closes[0].Session)} to {DayOf(path.Closes[^1].Session)}</title>");
        svg.Append(Invariant, $"<text class=\"m-pane-h\" x=\"4\" y=\"14\">{Escaped(setup.Ticker)}, bought {DayOf(setup.Session)}, {Escaped(setup.End)}</text>");
        svg.Append(Invariant, $"<rect class=\"m-plot\" x=\"4\" y=\"{LedgerTop - 4}\" width=\"{plotWidth}\" height=\"{LedgerHeight - LedgerTop}\"/>");

        foreach (var (named, side, price) in lines)
        {
            var at = Y(PlotValue(price));

            svg.Append(Invariant, $"<line class=\"m-edge-{side}\" data-{named}=\"{price.ToString(Invariant)}\" x1=\"4\" y1=\"{Number(at)}\" x2=\"{4 + plotWidth}\" y2=\"{Number(at)}\"/>");
            svg.Append(Invariant, $"<text class=\"m-legend-t\" x=\"{8 + plotWidth}\" y=\"{Number(at + 4)}\">{named} {Price(price)}</text>");
        }

        var bought = path.Closes.Select((close, at) => (close.Session, at)).FirstOrDefault(pair => pair.Session >= setup.Session).at;

        svg.Append(Invariant, $"<line class=\"m-bought\" data-bought=\"{DayOf(setup.Session)}\" x1=\"{Number(4 + (step * bought))}\" y1=\"{LedgerTop - 4}\" x2=\"{Number(4 + (step * bought))}\" y2=\"{LedgerHeight - 2}\"/>");

        var line = new StringBuilder();

        for (var at = 0; at < path.Closes.Count; at++)
        {
            line.Append(at == 0 ? 'M' : 'L').Append(Number(4 + (step * at))).Append(' ').Append(Number(Y(PlotValue(path.Closes[at].Close)))).Append(' ');
        }

        svg.Append(Invariant, $"<path class=\"m-mom\" d=\"{line.ToString().Trim()}\"/>");
        svg.Append("</svg>");

        return svg.ToString();
    }
}
