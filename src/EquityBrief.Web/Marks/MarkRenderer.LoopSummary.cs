using System.Text;
using EquityBrief.Core.Loop;

namespace EquityBrief.Web.Marks;

// One family's row in the Loop page's summary: its rule today as chips, the proposals the run tested, the strongest of
// them by its adjusted p, the one put to the operator with its presses drawn, and where nothing was tested why and when it
// next can be.
public sealed record LoopSummaryRow(
    string Family,
    IReadOnlyList<string> Chips,
    int Tested,
    LoopProposalRow? Strongest,
    LoopProposalRow? Put,
    string Presses,
    string? Waiting);

// The Loop page's summary and its proposal rows from 19.7: a row a family in the one order, each strongest proposal's
// adjusted p drawn as a meter against the bar the tester reads, one line saying what a change must pass, each proposal a
// row opening to its reasons and its test years as paired bars beside their figures. Every figure is the tester's row.
// see: A proposal passes the tester on a block sign-flip test of its total edge after costs against the current rule, corrected within a run and held to a fixed bar across runs
public sealed partial class MarkRenderer
{
    // The meter's scale: an adjusted p from a ten-thousandth to one on a log scale, the bar marked on it.
    const double MeterFloor = 0.0001;

    const double MeterWide = 160;

    // What a change must pass, each figure from its constant.
    public static string LoopChangeLine() =>
        Formatted($"<p class=\"loop-must\" data-bar=\"{LoopGate.Bar.ToString("R", Invariant)}\">A change is put to you only where it passes all four: an adjusted p at or under the bar of {LoopGate.Bar.ToString("0.0000", Invariant)}; better in {LoopScreens.StableOf} of {LoopScreens.StableIn} complete test years with the latest complete year better; its total less the rule's above nothing with the {LoopScreens.LeftOut} largest results on each side left out; and {LoopScreens.SwingTrades} trades, or {LoopScreens.BookMonths} months for a book, over the test years.</p>");

    // An adjusted p as a meter on a log scale from a ten-thousandth to one, the bar marked, filled in the gain's green at
    // or under the bar and the loss's red over it; a p not read drawn as an empty track saying so.
    public static string PMeter(double? adjusted)
    {
        static double X(double p) => MeterWide * Math.Clamp((Math.Log10(Math.Max(p, MeterFloor)) - Math.Log10(MeterFloor)) / -Math.Log10(MeterFloor), 0, 1);

        var bar = X(LoopGate.Bar);

        if (adjusted is not { } p)
        {
            return Formatted($"<svg class=\"p-meter\" data-adjusted=\"none\" viewBox=\"0 0 {MeterWide} 14\" width=\"{MeterWide}\" height=\"14\" role=\"img\" aria-label=\"no adjusted p read\"><rect class=\"pm-track\" x=\"0\" y=\"3\" width=\"{MeterWide}\" height=\"8\" rx=\"4\"></rect><line class=\"pm-bar\" x1=\"{Number(bar)}\" y1=\"0\" x2=\"{Number(bar)}\" y2=\"14\"></line></svg>");
        }

        var held = p <= LoopGate.Bar;

        return Formatted($"<svg class=\"p-meter\" data-adjusted=\"{p.ToString("R", Invariant)}\" data-held=\"{Flag(held)}\" viewBox=\"0 0 {MeterWide} 14\" width=\"{MeterWide}\" height=\"14\" role=\"img\" aria-label=\"an adjusted p of {p.ToString("0.0000", Invariant)} against the bar of {LoopGate.Bar.ToString("0.0000", Invariant)}\"><rect class=\"pm-track\" x=\"0\" y=\"3\" width=\"{MeterWide}\" height=\"8\" rx=\"4\"></rect><rect class=\"pm-fill {(held ? "gain" : "loss")}\" x=\"{Number(X(p))}\" y=\"3\" width=\"{Number(MeterWide - X(p))}\" height=\"8\" rx=\"4\"></rect><line class=\"pm-bar\" x1=\"{Number(bar)}\" y1=\"0\" x2=\"{Number(bar)}\" y2=\"14\"></line></svg>");
    }

    // The summary at the page's top: a row a family, its rule today as chips, the proposals tested, the strongest one's
    // adjusted p as a meter with its years better, and the one put to you with its presses, or why nothing is.
    public string LoopSummary(IReadOnlyList<LoopSummaryRow> rows)
    {
        var html = new StringBuilder();

        html.Append(Invariant, $"<div class=\"tbl-wrap\"><table class=\"loop-summary\" data-families=\"{rows.Count}\"><thead><tr>");
        html.Append(TippedHeading("Setup", "the setup family, in the one order"));
        html.Append(TippedHeading("The rule today", "the rule standing on this index, in short words"));
        html.Append(TippedHeading("Tested", "the proposals this run tested against the rule today", "r"));
        html.Append(TippedHeading("Strongest", "the proposal with the lowest adjusted p, drawn against the bar on a scale from a ten-thousandth to one, with the complete test years it was better in"));
        html.Append(TippedHeading("Put to you", "the strongest proposal that passed and states a change, with its presses"));
        html.Append("</tr></thead><tbody>");

        foreach (var row in rows)
        {
            html.Append(Invariant, $"<tr data-family=\"{Escaped(row.Family)}\" data-tested=\"{row.Tested}\" data-strongest=\"{Escaped(row.Strongest?.Proposal ?? "none")}\" data-put=\"{Escaped(row.Put?.Proposal ?? "none")}\">");
            html.Append(Invariant, $"<td><span class=\"fam-tag\" data-family=\"{Escaped(row.Family)}\"><span class=\"fam-swatch\" data-family=\"{Escaped(row.Family)}\"></span>{Escaped(FamilyName(row.Family))}</span></td>");
            html.Append(row.Chips.Count > 0
                ? "<td><span class=\"rule-chips\">" + string.Concat(row.Chips.Select(chip => $"<span class=\"chip\">{Escaped(chip)}</span>")) + "</span></td>"
                : "<td class=\"degraded\">not read</td>");
            html.Append(Invariant, $"<td class=\"r num\">{row.Tested}</td>");

            if (row.Strongest is { } strongest)
            {
                html.Append(Invariant, $"<td>{PMeter(strongest.Adjusted)} <span class=\"loop-better\" data-better=\"{strongest.Better}\" data-counted=\"{strongest.Counted}\">{(strongest.Adjusted is { } p ? Formatted($"p {p.ToString("0.0000", Invariant)}, ") : "p not read, ")}better in {strongest.Better} of {Counted(strongest.Counted, "year", "years")}</span></td>");
            }
            else
            {
                html.Append("<td class=\"degraded\">none</td>");
            }

            html.Append(row.Put is { } put
                ? $"<td><b>{Escaped(Capitalised(put.Proposal))}</b>{row.Presses}</td>"
                : $"<td class=\"degraded\" data-waiting=\"{Escaped(row.Waiting ?? "none")}\">{Escaped(row.Waiting ?? "Nothing is put to you.")}</td>");
            html.Append("</tr>");
        }

        html.Append("</tbody></table></div>");

        return html.ToString();
    }

    // A proposal as one row opening to its reasons: its change in short words, its p as a meter, its years better, its
    // trimmed difference and its count in the summary, and beneath the part by part verdict, the test years as paired
    // bars beside their figures, and the presses.
    public string LoopProposalRow(LoopProposalRow proposal, IReadOnlyList<LoopTestRow> years, string presses)
    {
        var book = proposal.Unit == LoopPoints;
        var html = new StringBuilder();

        html.Append(Invariant, $"<details class=\"loop-row\" data-family=\"{Escaped(proposal.Family)}\" data-proposal=\"{Escaped(proposal.Proposal)}\"><summary>");
        html.Append(Invariant, $"<span class=\"lr-name\">{Escaped(Capitalised(proposal.Proposal))}</span>");
        html.Append(Invariant, $"<span class=\"lr-change\">{(proposal.Words is { } words ? Escaped(words) : "proposes no change")}</span>");
        html.Append(PMeter(proposal.Adjusted));
        html.Append(Invariant, $"<span class=\"lr-years\">better in {proposal.Better} of {proposal.Counted}</span>");
        html.Append(Invariant, $"<span class=\"lr-trimmed\">{LoopFigure(proposal.Unit, proposal.Trimmed)} trimmed</span>");
        html.Append(Invariant, $"<span class=\"lr-count\">{LoopUnits(proposal.Unit, proposal.Units)} of {(book ? LoopScreens.BookMonths : LoopScreens.SwingTrades)}</span>");
        html.Append("</summary>");
        html.Append(LoopYearBars(proposal, years));
        html.Append(LoopProposal(proposal, years));
        html.Append(presses);
        html.Append("</details>");

        return html.ToString();
    }

    // Each test year as a pair of bars from one zero line on one scale, the rule today's total above and the proposal's
    // beneath, the year beside them and both figures under the pointer.
    static string LoopYearBars(LoopProposalRow proposal, IReadOnlyList<LoopTestRow> years)
    {
        if (years.Count == 0)
        {
            return string.Empty;
        }

        const int Width = 220;
        const int Label = 54;
        const int Row = 14;
        var top = years.SelectMany(year => new[] { Math.Abs(year.CurrentTotal), Math.Abs(year.ProposedTotal) }).DefaultIfEmpty(0).Max();
        var axis = Label + ((Width - Label) / 2.0);
        var half = ((Width - Label) / 2.0) - 2;
        var height = years.Count * Row;
        var svg = new StringBuilder();

        string Bar(double total, int y, string kind)
        {
            var length = top > 0 ? Math.Abs(total) / top * half : 0;
            var x = total >= 0 ? axis : axis - length;

            return Formatted($"<rect class=\"{kind}\" x=\"{x:0.#}\" y=\"{y}\" width=\"{length:0.#}\" height=\"5\"/>");
        }

        svg.Append(Invariant, $"<svg class=\"loop-yearbars\" width=\"{Width}\" height=\"{height}\" viewBox=\"0 0 {Width} {height}\" role=\"img\" aria-label=\"Each test year's total, the rule today's above and the proposal's beneath\">");
        svg.Append(Invariant, $"<line class=\"yb-axis\" x1=\"{axis:0.#}\" y1=\"0\" x2=\"{axis:0.#}\" y2=\"{height}\"/>");

        foreach (var (year, at) in years.Select((year, at) => (year, at)))
        {
            var y = at * Row;

            svg.Append(Invariant, $"<g data-year=\"{year.Year}\" data-current=\"{year.CurrentTotal.ToString("R", Invariant)}\" data-proposed=\"{year.ProposedTotal.ToString("R", Invariant)}\">");
            svg.Append(Invariant, $"<title>{year.Year}{(year.Complete ? string.Empty : " to date")}: the rule today {Plain(proposal.Unit, year.CurrentTotal)} over {LoopUnits(proposal.Unit, year.CurrentUnits)}, the proposal {Plain(proposal.Unit, year.ProposedTotal)} over {LoopUnits(proposal.Unit, year.ProposedUnits)}</title>");
            svg.Append(Invariant, $"<text class=\"yb-year\" x=\"0\" y=\"{y + 10}\">{year.Year}{(year.Complete ? string.Empty : " to date")}</text>");
            svg.Append(Bar(year.CurrentTotal, y + 2, "yb-this"));
            svg.Append(Bar(year.ProposedTotal, y + 7, "yb-that"));
            svg.Append("</g>");
        }

        svg.Append("</svg>");

        return svg.ToString();

        static string Plain(string unit, double value) =>
            unit == LoopPoints
                ? Formatted($"{value * 100:+0.00;-0.00;0.00} points")
                : Formatted($"{value:+0.00;-0.00;0.00} risks");
    }
}
