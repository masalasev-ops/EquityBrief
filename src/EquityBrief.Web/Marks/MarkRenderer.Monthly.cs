using System.Text;

namespace EquityBrief.Web.Marks;

// The newest monthly run as the Run page draws it: when its newest try started, whether every step it ran held and why
// not where one did not, the month it ran for, each family's tests on the S&P 500, the ordinary nights the shape clock has
// counted against those it waits on, and the filter version they are counted under.
public sealed record MonthlyRunView(
    DateTimeOffset? Started,
    string State,
    string? Why,
    string? Month,
    IReadOnlyList<MonthlyFamilyView> Families,
    int Ordinary,
    int Wanted,
    string? Version);

// One family in the newest monthly run: the proposals the tester's newest run tested for it and how many passed, the one
// put to the operator, none where none is, the live or provisional rule's verdict in the per-rule test, none where the
// test read none, and its variants' verdicts counted by their words.
public sealed record MonthlyFamilyView(
    string Family,
    int Tested,
    int Passed,
    string? Put,
    string? Head,
    IReadOnlyList<(string Verdict, int Count)> Variants);

// The Run page's monthly run region, which takes the place of the retired loop's clocks: the run's date and state, a row a
// family of what was tested and put and how the per-rule test read each rule, links to the Loop page and paper trading,
// and the shape clock in one line. It computes none of it.
// see: A registered rule's looks and its futility are monitoring, and a change is adopted only by the operator's approval of a tested proposal
public sealed partial class MarkRenderer
{
    public string MonthlyRunRegion(MonthlyRunView view, string loop, string paper)
    {
        var html = new StringBuilder();

        html.Append(Invariant, $"<div class=\"monthly-run\" data-state=\"{Escaped(view.State)}\" data-month=\"{Escaped(view.Month ?? "none")}\" data-families=\"{view.Families.Count}\">");
        html.Append(view.Started is { } started
            ? Formatted($"<p class=\"mr-state\" data-started=\"{started.UtcDateTime:yyyy-MM-ddTHH:mm:ssZ}\"><span class=\"status-pill {(view.State == WorryItem.Held ? "live" : "failed")}\">{(view.State == WorryItem.Held ? "held" : "stopped")}</span> The monthly run for {Escaped(view.Month ?? "its month")} started at {started.UtcDateTime:HH:mm} UTC on {started.UtcDateTime:yyyy-MM-dd}{(view.Why is { } why ? ": " + Escaped(why) : ", and every step it ran held")}.</p>")
            : "<p class=\"mr-state degraded\" data-started=\"none\">No monthly run has started yet. It runs on the first Saturday of each month.</p>");

        if (view.Families.Count > 0)
        {
            html.Append("<div class=\"tbl-wrap\"><table class=\"monthly-table\"><thead><tr>");
            html.Append(TippedHeading("Setup", "the setup family the tester read"));
            html.Append(TippedHeading("Tested", "the proposals the tester's newest run on the S&P 500 tested against the rule today, and how many passed", "r"));
            html.Append(TippedHeading("Put to you", "the strongest proposal that passed and states a change, which waits for your word on the Loop page"));
            html.Append(TippedHeading("Each rule's verdict", "the per-rule test of each registered rule over the ledger's test years: the live or provisional rule against its benchmark, each variant against it"));
            html.Append("</tr></thead><tbody>");

            foreach (var family in view.Families)
            {
                html.Append(Invariant, $"<tr data-family=\"{Escaped(family.Family)}\" data-tested=\"{family.Tested}\" data-passed=\"{family.Passed}\" data-put=\"{Escaped(family.Put ?? "none")}\">");
                html.Append(Invariant, $"<td><span class=\"fam-tag\" data-family=\"{Escaped(family.Family)}\"><span class=\"fam-swatch\" data-family=\"{Escaped(family.Family)}\"></span>{Escaped(FamilyName(family.Family))}</span></td>");
                html.Append(Invariant, $"<td class=\"r num\">{family.Tested} · {family.Passed} passed</td>");
                html.Append(family.Put is { } put ? $"<td>{Escaped(Capitalised(put))}</td>" : "<td class=\"degraded\">none</td>");
                html.Append("<td>");
                html.Append(family.Head is { } head ? $"<span data-head=\"{Escaped(head)}\">the rule today: {Escaped(head)}</span>" : "<span class=\"degraded\" data-head=\"none\">the rule today: not tested yet</span>");

                if (family.Variants.Count > 0)
                {
                    html.Append("; ");
                    html.Append(Invariant, $"<span data-variants=\"{family.Variants.Sum(one => one.Count)}\">variants: {Escaped(string.Join(", ", family.Variants.Select(one => Formatted($"{one.Count} {one.Verdict}"))))}</span>");
                }

                html.Append("</td></tr>");
            }

            html.Append("</tbody></table></div>");
        }

        html.Append(Invariant, $"<p class=\"mr-links\"><a href=\"{Escaped(loop)}\">The Loop page &#8594;</a> <a href=\"{Escaped(paper)}\">Paper trading &#8594;</a></p>");
        html.Append(Invariant, $"<p class=\"mr-shape\" data-ordinary=\"{view.Ordinary}\" data-wanted=\"{view.Wanted}\">The shape clock: {view.Ordinary} of the {view.Wanted} ordinary nights {(view.Version is { } version ? "under filter version " + Escaped(version) : "under no open filter version")}, after which the page proposes how many stocks each check should pass.</p>");
        html.Append("</div>");

        return html.ToString();
    }
}
