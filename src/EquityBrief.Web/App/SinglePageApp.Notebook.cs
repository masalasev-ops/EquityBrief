using System.Globalization;
using System.Text;

namespace EquityBrief.Web.App;

// One idea of the notebook as the page draws it: the day it was tested or entered, its source, its index where it names one,
// its plain words and its grammar line where the grammar writes it, its edge per $1 risked over 2005 to 2018 and from 2019,
// its verdict and why.
public sealed record NotebookRow(string Day, string Source, string? Index, string Words, string? Line, double? Early, double? Late, string Verdict, string Why);

// The notebook for one index: its newest ideas and the count of each verdict over all of them.
public sealed record NotebookView(IReadOnlyList<NotebookRow> Rows, IReadOnlyDictionary<string, int> Counts)
{
    public int Total => Counts.Values.Sum();
}

// The notebook on the Improvement loop page: every idea ever tested on the index the page reads, and the operator's own,
// newest first under the count of each verdict, each in plain words above its grammar line, with its figures on the years it
// was chosen on and from 2019, and its verdict and why.
// see: Every idea from any source is tested by the one tester and kept in a notebook nothing is deleted from
public sealed partial class SinglePageApp
{
    public const string NotebookLede = "From every source: the monthly run's engines, the searches over the long history, the tests of the buy and sell points and your own ideas, each tested the one way. Nothing is ever deleted from it.";

    public const string NotebookFigures = "Per $1 risked after costs, against the benchmark. Each idea is chosen on 2005 to 2018 alone and judged from 2019.";

    // The verdicts in the order the count names them, each in its page's word.
    public static IReadOnlyList<(string Verdict, string Words)> NotebookVerdicts { get; } =
    [
        ("passed", "passed"),
        ("near miss", "near misses"),
        ("failed", "failed"),
        ("waiting", "waiting"),
    ];

    public string NotebookRegion(UniverseChoice chosen, NotebookView view)
    {
        if (view.Total == 0)
        {
            return Cards.Computed("The notebook", $"<p class=\"empty\" data-notebook=\"empty\" data-summary>No idea on the {Escaped(chosen.Name)} is in the notebook yet.</p>", title: "Every idea ever tested", lede: Escaped(NotebookLede), region: "loop-notebook");
        }

        var counts = string.Join(", ", NotebookVerdicts.Where(one => view.Counts.GetValueOrDefault(one.Verdict) > 0).Select(one => string.Create(CultureInfo.InvariantCulture, $"{view.Counts[one.Verdict]:N0} {one.Words}")));
        var html = new StringBuilder(string.Create(CultureInfo.InvariantCulture, $"<p data-summary data-notebook=\"count\">{view.Total:N0} idea{(view.Total == 1 ? string.Empty : "s")} on the {Escaped(chosen.Name)} and your own: {counts}. Showing {view.Rows.Count:N0} of {view.Total:N0}, newest first.</p>"));

        html.Append("<div class=\"tbl-wrap\"><table class=\"lm-table notebook-table\"><thead><tr><th>Tested</th><th>From</th><th>The idea</th><th class=\"r\">2005 to 2018</th><th class=\"r\">From 2019</th><th>Verdict and why</th></tr></thead><tbody>");

        foreach (var row in view.Rows)
        {
            var waiting = row.Verdict == "waiting";

            html.Append(CultureInfo.InvariantCulture, $"<tr data-verdict=\"{Escaped(row.Verdict)}\"><td data-h=\"Tested\" class=\"num\">{Escaped(DayWords(row.Day))}</td><td data-h=\"From\">{Escaped(SourceWords(row.Source))}</td>");
            html.Append($"<td data-h=\"The idea\">{Escaped(row.Words)}{(row.Line is { } line ? $"<code class=\"idea-line\">{Escaped(line)}</code>" : string.Empty)}</td>");
            html.Append($"<td data-h=\"2005 to 2018\" class=\"r num\">{Escaped(Edge(row.Early, waiting))}</td><td data-h=\"From 2019\" class=\"r num\">{Escaped(Edge(row.Late, waiting))}</td>");
            html.Append($"<td data-h=\"Verdict and why\"><b>{Escaped(VerdictWords(row.Verdict))}</b> {Escaped(row.Why)}</td></tr>");
        }

        html.Append("</tbody></table></div>");
        html.Append($"<p class=\"lm-small\">{Escaped(NotebookFigures)}</p>");

        return Cards.Computed("The notebook", html.ToString(), title: "Every idea ever tested", lede: Escaped(NotebookLede), region: "loop-notebook");
    }

    public static string SourceWords(string source) => source == "the operator" ? "You" : char.ToUpperInvariant(source[0]) + source[1..];

    public static string VerdictWords(string verdict) => verdict switch
    {
        "near miss" => "Near miss",
        "waiting" => "Waiting",
        _ => char.ToUpperInvariant(verdict[0]) + verdict[1..],
    };

    // An edge per $1 risked, "not yet" on an idea waiting to be tested and a dash where its test reads none.
    public static string Edge(double? edge, bool waiting) =>
        edge is { } held ? (held < 0 ? "-$" : "+$") + Math.Abs(held).ToString("0.00", CultureInfo.InvariantCulture) : waiting ? "not yet" : "-";

    static string DayWords(string day) =>
        DateOnly.TryParseExact(day, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var on) ? on.ToString("d MMM yyyy", CultureInfo.InvariantCulture) : day;
}
