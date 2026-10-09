using System.Text;
using EquityBrief.Core.Loop;
using EquityBrief.Core.Returns;

namespace EquityBrief.Web.Marks;

// The Loop page's marks: a family's rule today in the words its run stored, and each proposal tested against it, its
// change, its verdict part by part and its test years with the setting each fold's learning years chose. Every figure
// is read off the tester's rows; the marks draw and compute none.
// see: A proposal passes the tester on a block sign-flip test of its total edge after costs against the current rule, corrected within a run and held to a fixed bar across runs
// see: A change is adopted only on test years the proposal never saw, and a search is judged as a procedure run year by year
public sealed partial class MarkRenderer
{
    public const string LoopPoints = "points";

    // A total or a difference in its unit: a book's in points of the buy, a swing family's in risks.
    static string LoopFigure(string unit, double? value) =>
        value is not { } held
            ? "<span class=\"degraded\">none</span>"
            : unit == LoopPoints
                ? Formatted($"{(held * 100).ToString("+0.00;-0.00;0.00", Invariant)} points")
                : Formatted($"{held.ToString("+0.00;-0.00;0.00", Invariant)} risks");

    static string LoopUnits(string unit, int count) =>
        unit == LoopPoints ? Formatted($"{count} month(s)") : Formatted($"{count} trade(s)");

    // The rule a family's proposals were tested against, in the words the run stored.
    public string LoopRule(string family, string? current) =>
        current is null
            ? $"<p class=\"degraded\" data-loop-rule=\"none\" data-family=\"{Escaped(family)}\">No procedure was tested for this family in this run, so no rule is read here.</p>"
            : $"<p class=\"loop-rule\" data-family=\"{Escaped(family)}\" data-rule=\"{Escaped(current)}\"><b>The rule today.</b> {Escaped(Capitalised(current))}</p>";

    // What the autopsy read of a family's finished trades in the run, a line a figure, none where it read none.
    public string LoopFindings(string family, IReadOnlyList<LoopFindingRow> findings) =>
        findings.Count == 0
            ? string.Empty
            : $"<div class=\"loop-findings\" data-family=\"{Escaped(family)}\" data-figures=\"{findings.Count}\"><p><b>What the rule's finished trades did.</b></p><ul>"
                + string.Concat(findings.Select(finding => $"<li data-figure=\"{Escaped(finding.Figure)}\" data-value=\"{(finding.Value is { } value ? value.ToString("R", Invariant) : "none")}\" data-trades=\"{finding.Trades}\">{Escaped(Capitalised(finding.Words))}.</li>"))
                + "</ul></div>";

    // Each reading's spread over a family's finished listings in the run, in a section folded shut, a row a reading;
    // none where the run stored none for the family.
    // see: Winners against losers proposes a condition only where it beats a within-night shuffle of its own search
    public string LoopReadings(string family, IReadOnlyList<LoopReadingRow> spreads)
    {
        if (spreads.Count == 0)
        {
            return string.Empty;
        }

        static string Figure(double? value, string format) => value is { } held ? held.ToString(format, Invariant) : "none";

        var html = new StringBuilder();

        html.Append(Invariant, $"<details class=\"loop-readings\" data-family=\"{Escaped(family)}\" data-readings=\"{spreads.Count}\"><summary>Each reading, winners against losers</summary>");
        html.Append("<div class=\"tbl-wrap\"><table class=\"loop-reading-table\"><thead><tr>");
        html.Append(TippedHeading("Reading", "the reading of the ledger's catalogue, read on each listing's session as the night reads it"));
        html.Append(TippedHeading("Listings", "the finished listings holding the reading, those that beat the same plan on every member that night and those that did not", "r"));
        html.Append(TippedHeading("Winners' median", "the median of the reading among the listings that beat the same plan on every member", "r"));
        html.Append(TippedHeading("Losers' median", "the median of the reading among the others", "r"));
        html.Append(TippedHeading("Edge by tenth", "the mean edge after costs, in risks, of each tenth of the listings in the reading's order, lowest first"));
        html.Append("</tr></thead><tbody>");

        foreach (var spread in spreads)
        {
            var deciles = string.Join(" ", spread.Deciles.Select(decile => Figure(decile, "+0.00;-0.00;0.00")));

            html.Append(Invariant, $"<tr data-reading=\"{Escaped(spread.Reading)}\" data-units=\"{spread.Units}\" data-winners=\"{spread.Winners}\" data-losers=\"{spread.Losers}\">");
            html.Append(Invariant, $"<td>{Escaped(spread.Reading)}</td>");
            html.Append(Invariant, $"<td class=\"r num\">{spread.Units}: {spread.Winners} won, {spread.Losers} did not</td>");
            html.Append(Invariant, $"<td class=\"r num\">{Figure(spread.WinnersMedian, "0.####")}</td><td class=\"r num\">{Figure(spread.LosersMedian, "0.####")}</td>");
            html.Append(Invariant, $"<td class=\"num\" data-deciles=\"{Escaped(deciles)}\">{Escaped(deciles)}</td></tr>");
        }

        html.Append("</tbody></table></div></details>");

        return html.ToString();
    }

    // One proposal: what it changes, its verdict part by part, its test years, and what the gate could detect.
    public string LoopProposal(LoopProposalRow proposal, IReadOnlyList<LoopTestRow> years)
    {
        var book = proposal.Unit == LoopPoints;
        var fewest = LoopScreens.Fewest(book);
        var html = new StringBuilder();

        html.Append(Invariant, $"<div class=\"loop-proposal\" data-family=\"{Escaped(proposal.Family)}\" data-proposal=\"{Escaped(proposal.Proposal)}\" data-passed=\"{(proposal.Passed ? 1 : 0)}\" data-years=\"{years.Count}\">");
        html.Append(Invariant, $"<h4 class=\"loop-name\">{Escaped(Capitalised(proposal.Proposal))}: {(proposal.Passed ? "passed the tester" : "did not pass the tester")}</h4>");
        html.Append(proposal.Words is { } words
            ? $"<p class=\"loop-change\" data-words=\"{Escaped(words)}\"><b>Proposed, run on all finished data.</b> {Escaped(Capitalised(words))}</p>"
            : "<p class=\"loop-change degraded\" data-words=\"none\"><b>No change.</b> The procedure run on all finished data chose no setting that met the floors, so it proposes nothing; its test years below are what it chose year by year.</p>");

        if (proposal.Finding is { } finding)
        {
            html.Append($"<p class=\"loop-finding\" data-finding=\"{Escaped(finding)}\"><b>Why.</b> {Escaped(Capitalised(finding))}.</p>");
        }

        var gate = proposal.Adjusted is { } p
            ? Formatted($"<li data-part=\"gate\" data-adjusted=\"{p.ToString("R", Invariant)}\" data-held=\"{(proposal.Gate ? 1 : 0)}\">The gate: an adjusted p-value of {p.ToString("0.0000", Invariant)} over {proposal.Blocks} blocks of 63 sessions, against the bar of {LoopGate.Bar.ToString("0.0000", Invariant)}; {(proposal.Gate ? "at or under it" : "over it")}.</li>")
            : Formatted($"<li data-part=\"gate\" data-adjusted=\"none\" data-held=\"0\">The gate is not read: {proposal.Blocks} block(s) of 63 sessions, under the floor of {Blocks.Floor}.</li>");
        var stable = Formatted($"<li data-part=\"stable\" data-counted=\"{proposal.Counted}\" data-better=\"{proposal.Better}\" data-held=\"{(proposal.Stable ? 1 : 0)}\">Stability: better in {proposal.Better} of the {proposal.Counted} complete year(s) holding {fewest} {(book ? "months" : "trades")} a side, against three in five with the latest complete year counted and better; {(proposal.Stable ? "held" : "not held")}.</li>");
        var trimmed = Formatted($"<li data-part=\"trimmed\" data-trimmed=\"{(proposal.Trimmed is { } t ? t.ToString("R", Invariant) : "none")}\" data-held=\"{(proposal.Trimmed > 0 ? 1 : 0)}\">With the {LoopScreens.LeftOut} largest results by size left out of each side, the proposal's total less the rule's: {LoopFigure(proposal.Unit, proposal.Trimmed)}; {(proposal.Trimmed > 0 ? "above nothing" : "not above nothing")}.</li>");
        var counts = Formatted($"<li data-part=\"counts\" data-units=\"{proposal.Units}\" data-held=\"{(proposal.Counts ? 1 : 0)}\">The count: {LoopUnits(proposal.Unit, proposal.Units)} over the test years, against the {(book ? LoopScreens.BookMonths : LoopScreens.SwingTrades)} {(book ? "months a book is" : "trades a swing family is")} judged on; {(proposal.Counts ? "enough" : "too few")}.</li>");

        html.Append(Invariant, $"<ul class=\"loop-verdict\">{gate}{stable}{trimmed}{counts}</ul>");
        html.Append(Invariant, $"<div class=\"tbl-wrap\"><table class=\"loop-years\" data-family=\"{Escaped(proposal.Family)}\" data-proposal=\"{Escaped(proposal.Proposal)}\"><thead><tr>");
        html.Append(TippedHeading("Year", "the test year, the newest the months since the latest complete year"));
        html.Append(TippedHeading("Chosen on the years before", "the setting the procedure chose on the trades that ended before the year began"));
        html.Append(TippedHeading("The rule today", "its total edge after costs over the units it entered in the year", "r"));
        html.Append(TippedHeading("Proposed", "the chosen setting's total edge after costs over the units it entered in the year", "r"));
        html.Append(TippedHeading("Better", "whether the proposal's total stood above the rule's, in a year the stability screen counts", "r"));
        html.Append("</tr></thead><tbody>");

        foreach (var year in years)
        {
            var counted = year.Complete && year.CurrentUnits >= fewest && year.ProposedUnits >= fewest;
            var better = counted ? (year.ProposedTotal > year.CurrentTotal ? "yes" : "no") : year.Complete ? "not counted, too few" : "not counted, to date";

            html.Append(Invariant, $"<tr data-year=\"{year.Year}\" data-complete=\"{(year.Complete ? 1 : 0)}\" data-current-units=\"{year.CurrentUnits}\" data-proposed-units=\"{year.ProposedUnits}\" data-current-total=\"{year.CurrentTotal.ToString("R", Invariant)}\" data-proposed-total=\"{year.ProposedTotal.ToString("R", Invariant)}\">");
            html.Append(Invariant, $"<td class=\"num\">{year.Year}{(year.Complete ? string.Empty : " to date")}</td>");
            html.Append(year.Chosen is { } chosen
                ? $"<td data-chosen=\"{Escaped(chosen)}\">{Escaped(Capitalised(chosen))}</td>"
                : "<td data-chosen=\"none\" class=\"degraded\">no setting met the floors on the years before, so the year reads the rule today</td>");
            html.Append(Invariant, $"<td class=\"r num\">{LoopFigure(proposal.Unit, year.CurrentTotal)} over {LoopUnits(proposal.Unit, year.CurrentUnits)}</td>");
            html.Append(Invariant, $"<td class=\"r num\">{LoopFigure(proposal.Unit, year.ProposedTotal)} over {LoopUnits(proposal.Unit, year.ProposedUnits)}</td>");
            html.Append(Invariant, $"<td class=\"r\" data-better=\"{better}\">{better}</td></tr>");
        }

        html.Append("</tbody></table></div>");
        html.Append(proposal.Detectable is { } detectable
            ? Formatted($"<p class=\"loop-power\" data-detectable=\"{detectable.ToString("R", Invariant)}\">At this bar the gate detects a difference of about {LoopFigure(proposal.Unit, detectable)} {(book ? "a month" : "a trade")} four times in five, read from the blocks' own scatter; a smaller true difference passes less often than that.</p>")
            : "<p class=\"loop-power degraded\" data-detectable=\"none\">The blocks are too few for the gate's power to be read.</p>");
        html.Append(Formatted($"<p class=\"loop-folds\" data-stable-folds=\"{proposal.StableFolds}\" data-folds=\"{years.Count}\">{proposal.StableFolds} of the {years.Count} folds chose {(book ? "the same setting as" : "within a grid step of")} the proposal on their own years before.</p>"));
        html.Append("</div>");

        return html.ToString();
    }
}
