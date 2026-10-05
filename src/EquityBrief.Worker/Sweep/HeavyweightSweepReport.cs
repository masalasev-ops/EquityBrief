using System.Net;
using System.Text;
using EquityBrief.Core.Sweep;

namespace EquityBrief.Worker.Sweep;

// The sector heavyweights' sweep report: the history read, the proposal beside the provisional setting with every
// setting one step from it, or where no setting meets the floors the strongest settings and what could be tried next,
// what luck alone would put above nothing, the replay held to the rebalances the night's book stored, and every setting
// with its figures, each in percent of the buy.
public static class HeavyweightSweepReport
{
    public static string Build(
        HeavyweightSweepRun run,
        IReadOnlyList<(HeavyweightSetting Setting, HeavyweightFigures Figures)> read,
        HeavyweightProposal proposal,
        HeavyweightComparison comparison)
    {
        var page = new StringBuilder();
        var provisional = read.Single(one => one.Setting == HeavyweightSweep.Provisional).Figures;

        page.Append("<!doctype html><html lang=\"en\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width, initial-scale=1\"><title>The sector heavyweights' sweep</title><style>");
        page.Append(SweepReport.Style);
        page.Append("</style></head><body><main>");
        page.Append("<p class=\"eyebrow\">Phase 14, the sector heavyweights' sweep</p><h1>The sector heavyweights' sweep</h1>");
        page.Append(Invariant($"<p class=\"history\" data-family=\"heavyweight\">Every figure on this page is history: the sector heavyweights' rule replayed over the pulled history from {run.From:yyyy-MM-dd} to {run.Through:yyyy-MM-dd}, {run.Sessions:N0} sessions over {run.Names:N0} names the index held, {run.WithASector:N0} of them with a sector the companies pull filed and {run.WithCounts:N0} with share counts, rebalanced on the first session of each of {run.Months:N0} months or {run.Weeks:N0} weeks. Each holding is scored by its return in percent of its buy, less the return of the size cut it was chosen from over the same sessions with every company in it weighted equally; every member's return and the index's over the same sessions stand beside it as context and never as its benchmark. {run.Settings} settings were read. None of it is a live record, and nothing here is registered.</p>"));

        page.Append("<h2>The proposal</h2>");

        if (proposal.Proposed is { } proposed)
        {
            page.Append(Invariant($"<p class=\"proposal\" data-key=\"{Esc(proposed.Key)}\">The proposal is <b>{Esc(proposed.Key)}</b>: the best edge among the settings with at least {FamilySweep.TradeFloor} trades and an edge above nothing in at least {FamilySweep.YearsBeating} of the 8 years.</p>"));
            page.Append(Table([("The proposal", proposed), ("The provisional setting", provisional)]));
            page.Append("<h3>Every setting one step from it</h3><p>Each moves one dial one step from the proposal, the higher edge first.</p>");
            page.Append(Table([.. proposal.Neighbours.Select(one => (one.Figures.Key, one.Figures))]));
        }
        else
        {
            var strongest = SweepNonePassed.StrongestOf(read.Select(one => new Strongest(one.Figures.Key, one.Figures.Trades, one.Figures.YearsBeating, one.Figures.Edge)));
            var next = new List<string>(SweepNonePassed.FloorsMissed(strongest));

            // The dials the grid reads in order, the size cut, the look-back and the leaders a sector; the others are
            // choices with no end to read past.
            if (strongest.Count > 0)
            {
                var top = read.First(one => one.Figures.Key == strongest[0].Key).Setting;
                IReadOnlyList<(string Dial, IReadOnlyList<string> Levels)> dials =
                [
                    ("the size cut", [.. HeavyweightSweep.Sizes.Select(size => size == HeavyweightSweep.EveryCompany ? "every company" : Invariant($"{size}"))]),
                    ("the look-back", [.. HeavyweightSweep.LookBacks.Select(lookBack => Invariant($"{lookBack} sessions"))]),
                    ("the leaders a sector", [.. HeavyweightSweep.LeaderCounts.Select(leaders => Invariant($"{leaders}"))]),
                ];

                next.AddRange(SweepNonePassed.GridEnds(dials, [SweepGrid.IndexOf(HeavyweightSweep.Sizes, top.Largest), SweepGrid.IndexOf(HeavyweightSweep.LookBacks, top.LookBack), SweepGrid.IndexOf(HeavyweightSweep.LeaderCounts, top.Leaders)]));
            }

            next.Add(SweepNonePassed.Ideas([]));
            page.Append(SweepNonePassed.Section(strongest, next, Table([("The provisional setting", provisional)]), Percent));
        }

        page.Append("<h2>What luck alone would pass</h2>");
        page.Append(Invariant($"<p class=\"luck\" data-meeting=\"{run.MeetingTheFloors}\" data-luck=\"{HeavyweightSweep.Luck(run.Settings):0}\">{run.MeetingTheFloors} of the {run.Settings} settings meet the floors. An edge with no effect stands above nothing in at least {FamilySweep.YearsBeating} of 8 years in {HeavyweightSweep.LuckPatterns()} of the 256 ways the years can fall, so were the settings independent luck alone would put about {HeavyweightSweep.Luck(run.Settings):0} of them there. They are not independent, since settings one step apart hold mostly the same stocks, so whether the proposal stands on its own is read off its neighbours.</p>"));

        page.Append("<h2>The replay against the night's book</h2>");
        page.Append(comparison.Sessions == 0
            ? "<p class=\"compared\" data-sessions=\"0\">The night's book has stored no rebalance through the history's end, so no session is compared yet.</p>"
            : Invariant($"<p class=\"compared\" data-sessions=\"{comparison.Sessions}\" data-sectors=\"{comparison.Sectors}\" data-matched=\"{comparison.Matched}\">Over the {comparison.Sessions} rebalance(s) the night's book stored through the history's end, the replay at the setting the family froze at, which the book holds at, read {comparison.Matched} of the {comparison.Sectors} sector(s) as stored: the same largest companies in the same places, each lead within a billionth and the same leaders.</p>"));

        if (comparison.Differences.Count > 0)
        {
            page.Append("<ul class=\"differences\">");

            foreach (var difference in comparison.Differences)
            {
                page.Append(Invariant($"<li>{Esc(difference)}</li>"));
            }

            page.Append("</ul>");
        }

        page.Append("<h2>Every setting</h2>");
        page.Append(Table([.. read.OrderByDescending(one => one.Figures.Edge ?? double.MinValue).ThenBy(one => one.Figures.Key, StringComparer.Ordinal).Select(one => (one.Figures.Key, one.Figures))]));

        page.Append(Invariant($"<p class=\"run\">Run from {run.Started:yyyy-MM-dd HH:mm:ss}Z to {run.Finished:yyyy-MM-dd HH:mm:ss}Z.</p>"));
        page.Append("</main></body></html>");

        return page.ToString();
    }

    // One row a setting: its trades ended and still held, its edge with its standard error, its result, the size cut's,
    // every member's and the index's returns, the years its edge stood above nothing, the last three years together, the
    // edge without its five largest results by size, how long a holding was held, and each year's edge with its trades.
    static string Table(IReadOnlyList<(string Label, HeavyweightFigures Figures)> rows)
    {
        var html = new StringBuilder("<div class=\"table\"><table><thead><tr><th>Setting</th><th>Trades</th><th>Held at the end</th><th>Edge</th><th>Error</th><th>Result</th><th>Size cut</th><th>Every member</th><th>Index</th><th>Years above</th><th>2024 to 2026</th><th>Without the five largest</th><th>Sessions held, median</th><th>Sessions held, mean</th>");

        for (var year = 0; year < 8; year++)
        {
            html.Append(Invariant($"<th>{SweepColumns.FirstScored.Year + year}</th>"));
        }

        html.Append("</tr></thead><tbody>");

        foreach (var (label, figures) in rows)
        {
            html.Append(Invariant($"<tr data-key=\"{Esc(figures.Key)}\" data-trades=\"{figures.Trades}\" data-edge=\"{Percent(figures.Edge)}\"><td>{Esc(label)}</td><td class=\"num\">{figures.Trades:N0}</td><td class=\"num\">{figures.Open:N0}</td><td class=\"num\">{Percent(figures.Edge)}</td><td class=\"num\">{Percent(figures.StandardError)}</td><td class=\"num\">{Percent(figures.Result)}</td><td class=\"num\">{Percent(figures.CutReturn)}</td><td class=\"num\">{Percent(figures.EveryMember)}</td><td class=\"num\">{Percent(figures.Index)}</td><td class=\"num\">{figures.YearsBeating} of 8</td><td class=\"num\">{Percent(figures.RecentEdge)}</td><td class=\"num\">{Percent(figures.EdgeWithoutLargest)}</td><td class=\"num\">{Sessions(figures.HeldMedian)}</td><td class=\"num\">{Sessions(figures.HeldMean)}</td>"));

            for (var year = 0; year < 8; year++)
            {
                html.Append(Invariant($"<td class=\"num\">{Percent(figures.YearEdge[year])} ({figures.YearTrades[year]})</td>"));
            }

            html.Append("</tr>");
        }

        return html.Append("</tbody></table></div>").ToString();
    }

    // A fraction of the buy as the page draws it, in percent to two places.
    public static string Percent(double? value) => value is { } held ? (held * 100).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + "%" : "none";

    static string Sessions(double? value) => value is { } held ? held.ToString("0", System.Globalization.CultureInfo.InvariantCulture) : "none";

    static string Esc(string text) => WebUtility.HtmlEncode(text);

    static string Invariant(FormattableString text) => FormattableString.Invariant(text);
}
