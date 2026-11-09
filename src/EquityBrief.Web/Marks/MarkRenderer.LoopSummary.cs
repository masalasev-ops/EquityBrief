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
    string? Waiting,
    LoopPooledRow? Pooled = null,
    LoopDecisionRow? Word = null);

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
        Formatted($"<p class=\"loop-must\" data-bar=\"{LoopGate.Bar.ToString("R", Invariant)}\">A change reaches you only if it passes all four checks: {LoopPlain.Luck(LoopGate.Bar).ToLowerInvariant()} or less that it is luck; better in at least {LoopScreens.StableOf} of every {LoopScreens.StableIn} full test years, including the latest; still ahead of today's rule with the {LoopScreens.LeftOut} biggest results on each side taken out; and at least {LoopScreens.SwingTrades} trades, or {LoopScreens.BookMonths} months for a strategy that holds companies.</p>");

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

    // A strategy's rule today as chips, or that it was not read.
    public static string LoopChips(IReadOnlyList<string> chips) =>
        chips.Count > 0
            ? "<span class=\"rule-chips\">" + string.Concat(chips.Select(chip => $"<span class=\"chip\">{Escaped(chip)}</span>")) + "</span>"
            : "<span class=\"degraded\" data-chips=\"none\">not read</span>";

    // The opening page's list: a line a strategy in the one order, what it does, how many changes were tried and what the
    // month came to, each opening the strategy's own page at the address the route gives.
    public string LoopSummary(IReadOnlyList<LoopSummaryRow> rows, Func<string, string> route)
    {
        var html = new StringBuilder();

        html.Append(Invariant, $"<div class=\"tbl-wrap\"><table class=\"loop-summary\" data-families=\"{rows.Count}\"><thead><tr><th>Strategy</th><th>What it does</th><th class=\"r\">Tried</th><th>This month</th></tr></thead><tbody>");

        foreach (var row in rows)
        {
            var (result, kind) = CheckUpResult(row, row.Word);

            html.Append(Invariant, $"<tr data-family=\"{Escaped(row.Family)}\" data-tested=\"{row.Tested}\" data-strongest=\"{Escaped(row.Strongest?.Proposal ?? "none")}\" data-put=\"{Escaped(row.Put?.Proposal ?? "none")}\">");
            html.Append(Invariant, $"<td><a class=\"cu-open-link\" href=\"{route(row.Family)}\" data-family-page=\"{Escaped(row.Family)}\"><span class=\"fam-tag\" data-family=\"{Escaped(row.Family)}\"><span class=\"fam-swatch\" data-family=\"{Escaped(row.Family)}\"></span>{Escaped(FamilyName(row.Family))}</span></a></td>");
            html.Append($"<td>{Escaped(LoopPlain.Does(row.Family))}</td>");
            html.Append(Invariant, $"<td class=\"r num\">{row.Tested}</td>");
            html.Append($"<td class=\"cu-result\" data-result=\"{kind}\">{Escaped(result)}</td>");
            html.Append("</tr>");
        }

        html.Append("</tbody></table></div>");

        return html.ToString();
    }

    // A proposal as one row opening to its reasons: its change in short words, its p as a meter, its years better, its
    // trimmed difference and its count in the summary, and beneath the part by part verdict, the test years as paired
    // bars beside their figures, and the presses.
    public string LoopProposalRow(LoopProposalRow proposal, IReadOnlyList<LoopTestRow> years, string presses, DateOnly? cutoff = null)
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
        html.Append(LoopProposal(proposal, years, cutoff));
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
