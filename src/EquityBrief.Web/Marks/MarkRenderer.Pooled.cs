using System.Text;
using EquityBrief.Core.Loop;

namespace EquityBrief.Web.Marks;

// From 23.2 the one verdict across the three indices as the Loop page's summary and paper trading's scorecard draw it,
// under the heading All three indexes together, beside each index's own verdict, and the passes luck alone would bring a
// year across the setups the pooled step read. Every figure is the step's row or worked from the bar.
// see: One verdict across the three indices stands beside each index's own, and an approval still changes one index's rule
public sealed partial class MarkRenderer
{
    public const string PooledHeading = "All three indexes together";

    static readonly string[] SmallNumbers = ["no", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine", "ten", "eleven", "twelve", "thirteen", "fourteen", "fifteen", "sixteen", "seventeen", "eighteen", "nineteen", "twenty"];

    static string IndexName(string index) => index switch
    {
        "GSPC" => "S&P 500",
        "MID" => "S&P 400",
        "SML" => "S&P 600",
        _ => index,
    };

    // The indices a verdict summed, in words.
    static string IndicesSaid(IReadOnlyList<string> indices) =>
        indices.Count switch
        {
            0 => "no index",
            1 => "the " + IndexName(indices[0]) + " alone",
            _ => "the " + string.Join(", ", indices.Take(indices.Count - 1).Select(IndexName)) + " and " + IndexName(indices[^1]),
        };

    static string PooledUnits(LoopPooledRow row) =>
        row.Units.ToString("N0", Invariant) + " " + (row.Unit == "points" ? (row.Units == 1 ? "month" : "months") : (row.Units == 1 ? "trade" : "trades"));

    // The setups the pooled step read: each one holding a verdict of its own rules in the run.
    public static int PooledSetups(IReadOnlyList<LoopPooledRow> rows) =>
        rows.Where(row => row.Own).Select(row => row.Family).Distinct(StringComparer.Ordinal).Count();

    // The passes luck alone would bring a year across the setups the step read, beside the approvals, so a pass is read
    // with it.
    public static string PooledLuckLine(int setups)
    {
        var expected = PooledJudge.FalsePassesAYear(setups);
        var years = PooledJudge.YearsAPass(setups);
        var once = years switch
        {
            null => "no setup was read",
            <= 1 => "about one pass a year may be luck",
            _ => Formatted($"one pass in about {(years < SmallNumbers.Length ? SmallNumbers[years.Value] : years.Value.ToString(Invariant))} years may be luck"),
        };

        return Formatted($"<p class=\"pooled-luck\" data-setups=\"{setups}\" data-false-passes=\"{expected.ToString("R", Invariant)}\" data-years=\"{(years is { } held ? held.ToString(Invariant) : "none")}\">Passes luck alone would bring: about {expected.ToString("0.##", Invariant)} a year across the {setups} {(setups == 1 ? "setup" : "setups")}: {once}.</p>");
    }

    // The summary's cell: the setup's rules summed across the indices, passed or not, its chance of luck and years better,
    // and the index that carried a pass; not yet summed where the step has not run.
    public static string PooledCell(LoopPooledRow? row)
    {
        if (row is null)
        {
            return "<span class=\"degraded\" data-pooled=\"none\">not summed yet</span>";
        }

        var word = row.Passed ? "passed" : "did not pass";

        return Formatted($"<span class=\"pill pooled-pill\" data-pooled=\"{word}\" data-indices=\"{Escaped(string.Join(" ", row.Indices))}\"{(row.Adjusted is { } p ? Formatted($" title=\"adjusted p {p.ToString("0.0000", Invariant)} against the bar {LoopGate.Bar.ToString("0.0000", Invariant)}\"") : string.Empty)}>{word}</span> <span class=\"loop-better\" data-better=\"{row.Better}\" data-counted=\"{row.Counted}\">{(row.Adjusted is { } shown ? Formatted($"p {shown.ToString("0.0000", Invariant)}, ") : "p not read, ")}better in {row.Better} of {Counted(row.Counted, "year", "years")}, {PooledUnits(row)} on {Escaped(IndicesSaid(row.Indices))}{(row.CarriedBy is { } carried ? Formatted($"; the {IndexName(carried)} carried it") : string.Empty)}</span>");
    }

    // The scorecard's part: the head's setup summed across the indices with what it read, the line saying each index's own
    // verdict stands and an approval changes one index's rule, and the passes luck alone would bring.
    public static string PooledPart(LoopPooledRow? own, int setups)
    {
        var html = new StringBuilder();

        html.Append(Formatted($"<div class=\"score-pooled\" data-pooled=\"{(own is null ? "none" : own.Passed ? "passed" : "did not pass")}\"><h4>{PooledHeading}</h4>"));

        if (own is null)
        {
            html.Append("<p class=\"degraded\" data-pooled-line=\"none\">No monthly test has summed this setup across the three indexes yet.</p></div>");

            return html.ToString();
        }

        html.Append(Formatted($"<p class=\"sh-line\" data-pooled-line=\"{(own.Passed ? "passed" : "did not pass")}\">Its setup's rules on {Escaped(IndicesSaid(own.Indices))}, each at its own settings, summed into one test over the same years: it {(own.Passed ? "passed" : "did not pass")}, better in {own.Better} of {Counted(own.Counted, "complete year", "complete years")} over {PooledUnits(own)}{(own.JudgedFrom is { } from ? Formatted($", from {from} alone") : string.Empty)}.{(own.CarriedBy is { } carried ? Formatted($" One index carried it: without the {IndexName(carried)} the others together gained nothing.") : string.Empty)} Each index's own verdict above stands, and an approval still changes one index's rule.</p>"));
        html.Append(PooledLuckLine(setups));
        html.Append("</div>");

        return html.ToString();
    }

    // A changed rule's note in the scorecard: the same change summed across the indices it is registered on, passed or not;
    // none where the step summed no such change.
    public static string PooledChangeNote(string rule, IReadOnlyList<LoopPooledRow> rows) =>
        rows.FirstOrDefault(row => !row.Own && row.Rules.Contains(rule, StringComparer.Ordinal)) is { } kind
            ? Formatted($"<span class=\"pv-note\" data-pooled=\"{(kind.Passed ? "passed" : "did not pass")}\" data-indices=\"{Escaped(string.Join(" ", kind.Indices))}\">{PooledHeading.ToLowerInvariant()}, the same change on {Escaped(IndicesSaid(kind.Indices))}: {(kind.Passed ? "passed" : "did not pass")}</span>")
            : string.Empty;
}
