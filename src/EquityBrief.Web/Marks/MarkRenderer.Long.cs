using System.Text;
using EquityBrief.Core.History;
using EquityBrief.Core.Loop;

namespace EquityBrief.Web.Marks;

// One rule's verdict over the long history, as the scorecard draws it: passed or not, its adjusted p, the years better of
// those counted and its units, or why it was not walked; the family's first test year on the index; and its units and
// total edge after costs over the years its setting never saw, before 2019, and over those it was chosen on.
public sealed record PaperLongVerdict(
    string Family,
    string Standing,
    bool Passed,
    double? Adjusted,
    int? Better,
    int? Counted,
    int? Units,
    string? Why,
    int FirstTestYear,
    int NeverSeenUnits,
    double NeverSeenTotal,
    int ChosenUnits,
    double ChosenTotal,
    bool Book);

// From 23.7 the scorecard's part over the long history beneath the verdict across the three indices: the rule's own verdict
// from its family's first test year with the years its setting never saw drawn apart from those it was chosen on, and the
// rules summed across the three indices over the same history. Every figure is the long step's row.
// see: The monthly run's last step judges every registered rule over the long history from each family's own first test year, capped at three hours
public sealed partial class MarkRenderer
{
    public const string LongHeading = "Over the long history";

    public static string LongPart(PaperLongVerdict? own, LoopPooledRow? pooled)
    {
        var html = new StringBuilder();
        var book = own?.Book ?? false;

        html.Append(Formatted($"<div class=\"score-long\" data-long=\"{(own is null ? "none" : own.Why is not null ? "not walked" : own.Passed ? "passed" : "did not pass")}\"><h4>{LongHeading}</h4>"));

        if (own is null)
        {
            return html.Append("<p class=\"degraded\" data-long-line=\"none\">No monthly run has tested this rule over the long history yet.</p></div>").ToString();
        }

        if (own.Why is { } why)
        {
            html.Append(Formatted($"<p class=\"degraded\" data-long-line=\"why\">{Escaped(Capitalised(why))}.</p>"));
        }
        else
        {
            var word = own.Passed ? "passed" : "did not pass";

            html.Append(Formatted($"<p data-long-line=\"verdict\"><span class=\"pill\" data-long-verdict=\"{word}\">{word}</span> from {own.FirstTestYear}: {(own.Adjusted is { } p ? Formatted($"p {p.ToString("0.0000", Invariant)}, ") : "p not read, ")}better in {own.Better ?? 0} of {Counted(own.Counted ?? 0, "year", "years")}, {(own.Units ?? 0).ToString("N0", Invariant)} {(book ? "months" : "trades")}.</p>"));
            html.Append(LongYears("never seen", Formatted($"The years its setting never saw, {own.FirstTestYear} to {LongLedger.StandingFrom.Year - 1}"), own.NeverSeenUnits, own.NeverSeenTotal, book));
            html.Append(LongYears("chosen on", Formatted($"The years it was chosen on, {LongLedger.StandingFrom.Year} on"), own.ChosenUnits, own.ChosenTotal, book));
        }

        html.Append(Formatted($"<p data-long-pooled=\"{(pooled is null ? "none" : pooled.Passed ? "passed" : "did not pass")}\">{PooledHeading}: {PooledCell(pooled)}</p>"));

        return html.Append("</div>").ToString();
    }

    // One side of the years, its units and its mean edge after costs, a swing family's in its risk and a book's in points.
    static string LongYears(string side, string said, int units, double total, bool book) =>
        Formatted($"<p data-long-years=\"{side}\">{Escaped(said)}: {units.ToString("N0", Invariant)} {(book ? (units == 1 ? "month" : "months") : (units == 1 ? "trade" : "trades"))}{(units > 0 ? Formatted($", {(total / units).ToString("+0.000;-0.000;0.000", Invariant)} {(book ? "points" : "risks")} each after costs") : string.Empty)}.</p>");
}
