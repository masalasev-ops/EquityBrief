using System.Globalization;
using System.Text;
using EquityBrief.Core.Loop;

namespace EquityBrief.Web.App;

// The pullbacks' search on the Improvement loop page for the index it reads: when it last ran, how many settings it tested
// and how many passed or came near, and its settings best on 2005 to 2018 first, each in words with its figures there and
// from 2019 and its verdict. Before it has run it says when it runs.
// see: The pullbacks' search walks the swing filter's dials as grammar lines, chosen on 2005 to 2018 and judged together from 2019
public sealed partial class SinglePageApp
{
    public const string PullbackSearchLede = "The pullback's swing filter at other settings: its floors on strength, on the share of the index's stocks above their 200-day average, on depth and on reward crossed at three levels each, then its other dials one at a time from the best of those. Each setting buys and sells by the running revision's own plan, is chosen on 2005 to 2018 and judged from 2019, and every setting tried counts toward the one bar for luck. A setting that passes runs in the background like any idea.";

    public string PullbackSearchRegion(UniverseChoice chosen, PullbackSearchView? view)
    {
        if (view is null || view.Index != chosen.Code)
        {
            return Cards.Computed("The pullbacks' search", "<p class=\"empty\" data-search=\"none\" data-summary>The pullbacks' search has not run on this index yet. It runs by hand over the long ledger once the ledger has been rebuilt, its settings, their count and its time put to you first.</p>", title: "The pullback's settings, searched", lede: Escaped(PullbackSearchLede), region: "loop-pullbacks");
        }

        var html = new StringBuilder();

        html.Append(FormattableString.Invariant($"<p data-summary>On {view.Day:d MMM yyyy} it tested {view.Lines:N0} settings: {view.Passed:N0} passed and {view.NearMisses:N0} came near the bar. The best on 2005 to 2018 first.</p>"));
        html.Append("<div class=\"tbl-wrap\"><table class=\"lm-table search-table\"><thead><tr><th>#</th><th>The setting</th><th class=\"r\">Edge to 2018</th><th class=\"r\">Edge from 2019</th><th>Verdict</th></tr></thead><tbody>");

        var number = 0;

        foreach (var row in view.Best)
        {
            number++;
            html.Append(CultureInfo.InvariantCulture, $"<tr data-verdict=\"{Escaped(row.Verdict)}\"><td data-h=\"#\" class=\"num\">{number}</td><td data-h=\"The setting\">{Escaped(row.Words)}</td>");
            html.Append(CultureInfo.InvariantCulture, $"<td data-h=\"Edge to 2018\" class=\"r num\">{Risks(row.EarlyEdge)} over {row.EarlyPicks:N0}</td><td data-h=\"Edge from 2019\" class=\"r num\">{Risks(row.LateEdge)} over {row.LatePicks:N0}</td>");
            html.Append(CultureInfo.InvariantCulture, $"<td data-h=\"Verdict\">{Escaped(row.Verdict)}: {Escaped(row.Why)}</td></tr>");
        }

        html.Append("</tbody></table></div>");

        return Cards.Computed("The pullbacks' search", html.ToString(), title: "The pullback's settings, searched", lede: Escaped(PullbackSearchLede), region: "loop-pullbacks");
    }
}
