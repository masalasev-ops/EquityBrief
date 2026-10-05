using System.Net;
using System.Text;

namespace EquityBrief.Worker.Sweep;

// What a setup family's sweep run read and found, as the page and the run folder hold it.
public sealed record FamilySweepRun(
    string Family,
    string Words,
    DateOnly From,
    DateOnly Through,
    int Names,
    int ScoredNights,
    int OpenNights,
    int Readings,
    DateTimeOffset Started,
    DateTimeOffset Finished,
    string? Note = null);

// A setup family's sweep report: the history read, the provisional setting's record beside the proposal, the
// proposal and its variants, or where no setting meets the floors the strongest settings and what could be tried
// next, the test its checkpoints read from the freeze, and every setting of the grid with its figures.
public static class FamilySweepReport
{
    // The test a family's checkpoints read from its freeze, stated in the report before anything is frozen.
    public const string Test =
        "From the freeze, the family's record is its trades' edge over blocks of 63 sessions, and a checkpoint passes it where the sign-flip test over its whole blocks falls under its level: 0.05 shared among the rules the family registers, its own and its variants, the test the register's candidates are judged by.";

    public static string Build(FamilySweepRun run, FamilyGrid grid, IReadOnlyList<(int[] Setting, FamilyFigures Figures)> read, FamilyProposal proposal)
    {
        var page = new StringBuilder();
        var provisional = read.Single(one => grid.Changes(one.Setting) == 0).Figures;

        page.Append(Invariant($"<!doctype html><html lang=\"en\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width, initial-scale=1\"><title>{Esc(run.Words)} sweep</title><style>"));
        page.Append(SweepReport.Style);
        page.Append("</style></head><body><main>");
        page.Append(Invariant($"<p class=\"eyebrow\">Phase 13, the {Esc(run.Words)} sweep</p><h1>The {Esc(run.Words)} sweep</h1>"));
        page.Append(Invariant($"<p class=\"history\" data-family=\"{Esc(run.Family)}\">Every figure on this page is history: the {Esc(run.Words)} rule replayed over the stored history from {run.From:yyyy-MM-dd} to {run.Through:yyyy-MM-dd}, {run.ScoredNights:N0} sessions scored over {run.Names:N0} names the index held, the market check at the live filter's floor leaving the lists open on {run.OpenNights:N0} of them, five a night in the family's own order with one open trade a stock, and every result read as an edge over the same plan entered at the same close on every member that night. {grid.Settings.Count} settings were read over {run.Readings:N0} member-sessions the loosest one could list. None of it is a live record, and nothing here is registered.</p>"));

        if (run.Note is { } note)
        {
            page.Append(Invariant($"<p class=\"note\">{Esc(note)}</p>"));
        }

        page.Append("<h2>The proposal</h2>");

        if (proposal.Proposed is { } proposed)
        {
            page.Append(Invariant($"<p class=\"proposal\" data-key=\"{Esc(proposed.Key)}\">The proposal is <b>{Esc(proposed.Key)}</b>: the best edge among the settings with at least {FamilySweep.TradeFloor} trades and an edge above nothing in at least {FamilySweep.YearsBeating} of the 8 years.</p>"));
            page.Append(Table([("The proposal", proposed), ("The provisional setting", provisional)]));
            page.Append(Invariant($"<h3>Its variants</h3><p>Each one dial one step from the proposal, the higher edge first, at most {FamilySweep.Variants}.</p>"));
            page.Append(Table([.. proposal.Variants.Select(variant => (variant.Key, variant))]));
        }
        else
        {
            var strongest = SweepNonePassed.StrongestOf(read.Select(one => new Strongest(one.Figures.Key, one.Figures.Trades, one.Figures.YearsBeating, one.Figures.Edge)));
            var next = new List<string>(SweepNonePassed.FloorsMissed(strongest));

            if (strongest.Count > 0)
            {
                var top = read.First(one => one.Figures.Key == strongest[0].Key).Setting;

                next.AddRange(SweepNonePassed.GridEnds([.. grid.Dials.Select(dial => (dial.Dial, (IReadOnlyList<string>)[.. dial.Levels.Select(FamilyGrid.Number)]))], top));
            }

            next.Add(SweepNonePassed.Ideas([.. FamilyIdeas.For(run.Family).Select(idea => idea.Rule)]));
            page.Append(SweepNonePassed.Section(strongest, next, Table([("The provisional setting", provisional)]), Number));
        }

        page.Append("<h2>The test its checkpoints read</h2>");
        page.Append(Invariant($"<p class=\"test\">{Esc(Test)}</p>"));

        page.Append("<h2>Every setting</h2>");
        page.Append(Table([.. read.OrderByDescending(one => one.Figures.Edge ?? double.MinValue).ThenBy(one => one.Figures.Key, StringComparer.Ordinal).Select(one => (one.Figures.Key, one.Figures))]));

        page.Append(Invariant($"<p class=\"run\">Run from {run.Started:yyyy-MM-dd HH:mm:ss}Z to {run.Finished:yyyy-MM-dd HH:mm:ss}Z.</p>"));
        page.Append("</main></body></html>");

        return page.ToString();
    }

    // One row a setting: its trades, the nights a stock was listed, its edge with its standard error, its plain
    // result, the years its edge stood above nothing, the last three years together, the edge without its five
    // largest results by size, and each year's edge with its trades.
    static string Table(IReadOnlyList<(string Label, FamilyFigures Figures)> rows)
    {
        var html = new StringBuilder("<div class=\"table\"><table><thead><tr><th>Setting</th><th>Trades</th><th>Nights listing</th><th>Edge</th><th>Error</th><th>Result</th><th>Years above</th><th>2024 to 2026</th><th>Without the five largest</th><th>Stops under a typical move</th>");

        for (var year = 0; year < 8; year++)
        {
            html.Append(Invariant($"<th>{SweepColumns.FirstScored.Year + year}</th>"));
        }

        html.Append("</tr></thead><tbody>");

        foreach (var (label, figures) in rows)
        {
            html.Append(Invariant($"<tr data-key=\"{Esc(figures.Key)}\" data-trades=\"{figures.Trades}\" data-edge=\"{Number(figures.Edge)}\"><td>{Esc(label)}</td><td class=\"num\">{figures.Trades:N0}</td><td class=\"num\">{figures.NightShare * 100:0}%</td><td class=\"num\">{Number(figures.Edge)}</td><td class=\"num\">{Number(figures.StandardError)}</td><td class=\"num\">{Number(figures.Result)}</td><td class=\"num\">{figures.YearsBeating} of 8</td><td class=\"num\">{Number(figures.RecentEdge)}</td><td class=\"num\">{Number(figures.EdgeWithoutLargest)}</td><td class=\"num\">{(figures.CloseStops is { } share ? Invariant($"{share * 100:0}%") : "none")}</td>"));

            for (var year = 0; year < 8; year++)
            {
                html.Append(Invariant($"<td class=\"num\">{Number(figures.YearEdge[year])} ({figures.YearTrades[year]})</td>"));
            }

            html.Append("</tr>");
        }

        return html.Append("</tbody></table></div>").ToString();
    }

    public static string Number(double? value) => value is { } held ? held.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture) : "none";

    static string Esc(string text) => WebUtility.HtmlEncode(text);

    static string Invariant(FormattableString text) => FormattableString.Invariant(text);
}
